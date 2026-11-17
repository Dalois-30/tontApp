namespace ApiTests;

using System.Net;
using System.Text.Json.Nodes;
using Application.PaymentManagement.Services;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.NotificationManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Simultaneous writes on the payment chain of a tontine (declarations, validations, rejections) are
/// serialised per tontine on the real PostgreSQL database: no duplicate, no round covered twice, the
/// audit chain stays linear and verifiable; two different tontines never wait for each other.
/// </summary>
[Collection(ApiCollection.Nom)]
public sealed class DeclarationsConcurrentesTests(ApiFactory api)
{
    private const decimal Cotisation = 5000m;

    /// <summary>An active tontine; the first payer joined is the beneficiary of round 1, the others pay it.</summary>
    private sealed record Groupe(Guid Id, Guid TourId, Acteur Gestionnaire, IReadOnlyList<(Acteur Compte, Guid Membre)> Payeurs,
        IReadOnlyDictionary<Guid, Acteur> ComptesParMembre);

    private async Task<Groupe> TontineAsync(int payeurs)
    {
        var g = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var comptes = new List<Acteur>();
        for (var i = 0; i <= payeurs; i++)
            comptes.Add(await api.CompteAsync($"Membre {i:00}"));

        var id = (Guid)(await g.Envoyer("/api/v1/tontines", new
        {
            name = "Tontine concurrente", description = "Tests", contributionAmount = Cotisation, periodicity = "Monthly",
            maxMembers = payeurs + 1,
        }))["id"]!;
        var code = (string)(await g.Lire($"/api/v1/tontines/{id}/invitation/generer?nombreUsagesMax={payeurs + 1}"))["code"]!;
        foreach (var m in comptes)
            await m.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = m.Nom });
        (await g.Post($"/api/v1/tontines/{id}/activate")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var d = await g.Lire($"/api/v1/tontines/{id}/details");
        var tour = d["tours"]!.AsArray().First(t => (string)t!["status"]! == "Ouvert")!;
        var beneficiaire = (Guid)tour["beneficiaire"]!["id"]!;
        var membres = d["membres"]!.AsArray();
        var tous = comptes
            .Select(c => (c, (Guid)membres.First(m => (Guid?)m!["utilisateurId"] == c.Id)!["id"]!))
            .ToList();
        var liste = tous.Where(x => x.Item2 != beneficiaire).ToList();
        liste.Should().HaveCount(payeurs);
        return new Groupe(id, (Guid)tour["id"]!, g, liste, tous.ToDictionary(x => x.Item2, x => x.c));
    }

    private static Task<HttpResponseMessage> Declarer(Acteur qui, Guid tontineId, Guid tourId) =>
        qui.Post("/api/v1/versements/manuel",
            new { tontineId, tourId, montant = Cotisation, moyen = "Espèces", reference = "REF-C" });

    private static async Task<Guid> DeclarerOk(Acteur qui, Guid tontineId, Guid tourId)
    {
        var r = await Declarer(qui, tontineId, tourId);
        var corps = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, corps);
        return (Guid)JsonNode.Parse(corps)!["versementId"]!;
    }

    /// <summary>
    /// <c>audit/verifier</c> says the chain is intact, and both chains (versements, audit entries) are linked:
    /// exactly one genesis, every other previous hash is the current hash of another row, no fork.
    /// </summary>
    private async Task ChainesIntegres(Groupe t, int versementsAttendus)
    {
        var verif = await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/audit/verifier");
        ((bool)verif["estValide"]!).Should().BeTrue(verif.ToJsonString());

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
        var tontineId = TontineId.From(t.Id);
        var versements = await db.Versements.AsNoTracking().Where(v => v.TontineId == tontineId)
            .Select(v => new { v.HashPrecedent, v.HashCourant }).ToListAsync();
        versements.Should().HaveCount(versementsAttendus);
        ChaineLiee(versements.Select(v => (v.HashPrecedent, v.HashCourant)).ToList(), genese: string.Empty);
        var audit = await db.AuditEntries.AsNoTracking().Where(e => e.TontineId == tontineId)
            .Select(e => new { e.HashPrecedent, e.HashCourant }).ToListAsync();
        ChaineLiee(audit.Select(e => (e.HashPrecedent, e.HashCourant)).ToList(),
            genese: Domain.PaymentManagement.Entities.AuditEntry.GenesisHash);
    }

    private static void ChaineLiee(IReadOnlyList<(string Precedent, string Courant)> maillons, string genese)
    {
        if (maillons.Count == 0)
            return;
        maillons.Count(m => m.Precedent == genese).Should().Be(1, "a chain has exactly one genesis");
        maillons.Select(m => m.Precedent).Should().OnlyHaveUniqueItems("a chain never forks");
        var courants = maillons.Select(m => m.Courant).ToHashSet();
        maillons.Where(m => m.Precedent != genese)
            .Should().OnlyContain(m => courants.Contains(m.Precedent), "each link chains on another row of the chain");
    }

    /// <summary>Waits (bounded) until <paramref name="n"/> sessions wait for the tontine's advisory lock in pg_locks.</summary>
    private async Task AttendreAttentes(Guid tontineId, int n)
    {
        var classe = (long)(uint)VerrouTontinePostgres.ClasseVerrou;
        var objet = (long)(uint)VerrouTontinePostgres.Cle(tontineId);
        var echeance = DateTime.UtcNow.AddSeconds(8);
        var vus = -1;
        while (DateTime.UtcNow < echeance)
        {
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
            vus = await db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND objsubid = 2 AND classid::bigint = {classe} AND objid::bigint = {objet}")
                .SingleAsync();
            if (vus == n)
                return;
            await Task.Delay(50);
        }
        vus.Should().Be(n, "sessions waiting for the tontine lock");
    }

    /// <summary>Holds the tontine's lock as a long-running writer would, in its own unit of work.</summary>
    private async Task<(IServiceScope Scope, ITransactionVerrouillee Transaction)> TenirVerrou(Guid tontineId)
    {
        var scope = api.Services.CreateScope();
        var transaction = await scope.ServiceProvider.GetRequiredService<IVerrouTontine>()
            .VerrouillerAsync(TontineId.From(tontineId));
        return (scope, transaction);
    }

    private async Task<int> VersementsDuMembre(Groupe t, Guid membreId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
        var tontineId = TontineId.From(t.Id);
        var payeurId = Domain.PaymentManagement.ValueObjects.PayeurId.From(membreId);
        return await db.Versements.AsNoTracking().CountAsync(v => v.TontineId == tontineId && v.PayeurId == payeurId);
    }

    [Fact]
    public async Task DoublonSimultane_UnAccepte_UnRefuseDejaCouvert()
    {
        var t = await TontineAsync(payeurs: 2);
        var (bruno, _) = t.Payeurs[0];

        var reponses = await Task.WhenAll(Declarer(bruno, t.Id, t.TourId), Declarer(bruno, t.Id, t.TourId));

        reponses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var refus = reponses.Single(r => r.StatusCode != HttpStatusCode.Created);
        refus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refus.Content.ReadAsStringAsync()).Should().Contain("déjà déclaré");
        await ChainesIntegres(t, versementsAttendus: 1);
    }

    [Fact]
    public async Task Rafale_DixMembresDifferents_TousAcceptes_ChaineValide()
    {
        var t = await TontineAsync(payeurs: 10);

        var reponses = await Task.WhenAll(t.Payeurs.Select(p => Declarer(p.Compte, t.Id, t.TourId)));

        reponses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        await ChainesIntegres(t, versementsAttendus: 10);
    }

    [Fact]
    public async Task VingtDeclarationsParalleles_ChaineIntegre_AucunTourCouvertDeuxFois()
    {
        var t = await TontineAsync(payeurs: 10);

        // Each of the 10 payers declares the whole contribution twice, all 20 at the same time.
        var reponses = await Task.WhenAll(t.Payeurs
            .SelectMany(p => new[] { p, p })
            .Select(p => Declarer(p.Compte, t.Id, t.TourId)));

        reponses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(10);
        reponses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(10);
        await ChainesIntegres(t, versementsAttendus: 10);
        foreach (var (_, membre) in t.Payeurs)
            (await VersementsDuMembre(t, membre)).Should().Be(1);
    }

    [Fact]
    public async Task ValidationsSimultanees_UneValidee_LAutreRefusee_ChaineIntacte()
    {
        var t = await TontineAsync(payeurs: 2);
        var v = await DeclarerOk(t.Payeurs[0].Compte, t.Id, t.TourId);

        // The creator is the trésorier by default.
        var reponses = await Task.WhenAll(
            t.Gestionnaire.Post($"/api/v1/versements/{v}/valider"),
            t.Gestionnaire.Post($"/api/v1/versements/{v}/valider"));

        reponses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        reponses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);
        await ChainesIntegres(t, versementsAttendus: 1);
    }

    [Fact]
    public async Task ValidationEtRejetSimultanes_UneSeuleDecision_ChaineIntacte()
    {
        var t = await TontineAsync(payeurs: 2);
        var v = await DeclarerOk(t.Payeurs[0].Compte, t.Id, t.TourId);

        var reponses = await Task.WhenAll(
            t.Gestionnaire.Post($"/api/v1/versements/{v}/valider"),
            t.Gestionnaire.Post($"/api/v1/versements/{v}/rejeter", new { raison = "Argent non reçu" }));

        reponses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        reponses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);
        await ChainesIntegres(t, versementsAttendus: 1);
    }

    [Fact]
    public async Task TontinesDifferentes_AucuneAttenteCroisee()
    {
        var a = await TontineAsync(payeurs: 2);
        var b = await TontineAsync(payeurs: 2);

        var (scope, transaction) = await TenirVerrou(a.Id);
        Task<HttpResponseMessage> declarationA;
        try
        {
            // Tontine B is not blocked by A's lock: it completes while A's lock is held.
            (await Declarer(b.Payeurs[0].Compte, b.Id, b.TourId)).StatusCode.Should().Be(HttpStatusCode.Created);

            // Tontine A waits for its lock.
            declarationA = Declarer(a.Payeurs[0].Compte, a.Id, a.TourId);
            await AttendreAttentes(a.Id, 1);
            declarationA.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await transaction.DisposeAsync();
            scope.Dispose();
        }

        (await declarationA).StatusCode.Should().Be(HttpStatusCode.Created);
        await ChainesIntegres(a, versementsAttendus: 1);
        await ChainesIntegres(b, versementsAttendus: 1);
    }

    [Theory]
    [InlineData("valider")]
    [InlineData("rejeter")]
    public async Task DecisionPendantUnVerrouTenu_Attend_PuisReussit(string decision)
    {
        var t = await TontineAsync(payeurs: 2);
        var v = await DeclarerOk(t.Payeurs[0].Compte, t.Id, t.TourId);

        var (scope, transaction) = await TenirVerrou(t.Id);
        Task<HttpResponseMessage> requete;
        try
        {
            requete = t.Gestionnaire.Post($"/api/v1/versements/{v}/{decision}", new { raison = "Argent non reçu" });
            await AttendreAttentes(t.Id, 1);
            requete.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await transaction.DisposeAsync();
            scope.Dispose();
        }

        var r = await requete;
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        await ChainesIntegres(t, versementsAttendus: 1);
    }

    [Fact]
    public async Task DeuxValidationsSeriees_LaSecondeVoitLaPremiere_Refusee()
    {
        var t = await TontineAsync(payeurs: 2);
        var v = await DeclarerOk(t.Payeurs[0].Compte, t.Id, t.TourId);

        var (scope, transaction) = await TenirVerrou(t.Id);
        Task<HttpResponseMessage> premiere, seconde;
        try
        {
            premiere = t.Gestionnaire.Post($"/api/v1/versements/{v}/valider");
            seconde = t.Gestionnaire.Post($"/api/v1/versements/{v}/valider");
            await AttendreAttentes(t.Id, 2);
        }
        finally
        {
            await transaction.DisposeAsync();
            scope.Dispose();
        }

        var reponses = await Task.WhenAll(premiere, seconde);
        reponses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        reponses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);
        await ChainesIntegres(t, versementsAttendus: 1);
    }

    [Fact]
    public async Task VerrouTenuTropLongtemps_Valider_503Reessayez_SansRienEcrire()
    {
        var t = await TontineAsync(payeurs: 2);
        var v = await DeclarerOk(t.Payeurs[0].Compte, t.Id, t.TourId);

        var (scope, transaction) = await TenirVerrou(t.Id);
        HttpResponseMessage r;
        try
        {
            // Held beyond the API's bounded wait (ApiFactory.DelaiVerrouMs).
            r = await t.Gestionnaire.Post($"/api/v1/versements/{v}/valider");
        }
        finally
        {
            await transaction.DisposeAsync();
            scope.Dispose();
        }

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        r.Headers.RetryAfter.Should().NotBeNull();
        (await r.Content.ReadAsStringAsync()).Should().Contain("Opération en cours sur cette tontine, réessayez");
        ((string)(await t.Gestionnaire.Lire($"/api/v1/versements/{v}/statut"))["statut"]!).Should().Be("en_attente");
        await ChainesIntegres(t, versementsAttendus: 1);

        // Once the lock is free, the same request succeeds.
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");
    }

    // ── Domain events: published once, after the commit ──────────────

    private async Task<List<Domain.NotificationManagement.Notification>> Sms(Acteur qui, NotificationType type)
    {
        using var scope = api.Services.CreateScope();
        var compte = await scope.ServiceProvider.GetRequiredService<IUtilisateurRepository>().GetByIdAsync(UtilisateurId.From(qui.Id));
        var telephone = compte!.Telephone.Value;
        var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
        return await db.Notifications.AsNoTracking().Where(n => n.DestinataireId == telephone && n.Type == type).ToListAsync();
    }

    [Fact]
    public async Task VersementValide_UneSeuleConfirmation_EtScoreCreditMisAJour()
    {
        var t = await TontineAsync(payeurs: 2);
        var (payeur, membre) = t.Payeurs[0];
        var v = await DeclarerOk(payeur, t.Id, t.TourId);
        (await Sms(payeur, NotificationType.ConfirmationPaiement)).Should().BeEmpty("declaring is not confirming");

        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        (await Sms(payeur, NotificationType.ConfirmationPaiement)).Should().ContainSingle()
            .Which.Contenu.Should().Contain("Tontine concurrente");
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
        var profil = await db.ProfilsCredit.AsNoTracking().Include(p => p.Historique).SingleAsync(p => p.MembreId == membre);
        profil.Historique.TotalVersements.Should().Be(1);
    }

    [Fact]
    public async Task OuvertureDeTour_NotifieLeBeneficiaireUneFois()
    {
        var t = await TontineAsync(payeurs: 2);
        var d = await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/details");
        var tours = d["tours"]!.AsArray();
        Guid Beneficiaire(int numero) => (Guid)tours.First(x => (int)x!["numero"]! == numero)!["beneficiaire"]!["id"]!;
        var tous = t.ComptesParMembre;

        // Activation opened round 1: its beneficiary is told once.
        (await Sms(tous[Beneficiaire(1)], NotificationType.OuvertureTour)).Should().ContainSingle();

        // Closing round 1 opens round 2: its beneficiary is told once, round 1's is not told again.
        (await t.Gestionnaire.Post($"/api/v1/tontines/{t.Id}/rounds/{t.TourId}/close")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        d = await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/details");
        tours = d["tours"]!.AsArray();
        (await Sms(tous[Beneficiaire(2)], NotificationType.OuvertureTour)).Should().ContainSingle();
        (await Sms(tous[Beneficiaire(1)], NotificationType.OuvertureTour)).Should().ContainSingle();
    }
}
