namespace ApiTests;

using System.Net;
using System.Text.Json.Nodes;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.NotificationManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Infrastructure.Jobs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily jobs on a real database: the late-payment recap sent to the bureau the day after a round's
/// deadline, and the J-3 / J-1 reminders sent only to the members who still owe something.
/// Deadlines are moved in the database; the jobs run as Hangfire would run them (resolved from the API's services).
/// </summary>
[Collection(ApiCollection.Nom)]
public sealed class RappelsEtRecapRetardsTests(ApiFactory api)
{
    private const decimal Cotisation = 5000m;

    /// <summary>
    /// Active tontine: Gaston (creator, responsable + trésorier, not a member); members in joining order
    /// Alice (beneficiary of round 1), Bruno (beneficiary of round 2), Chloé, David, Emma.
    /// </summary>
    private sealed record Groupe(Guid Id, Guid Tour1, Acteur Gaston, Acteur Alice, Acteur Bruno, Acteur Chloe,
        Acteur David, Acteur Emma, Dictionary<Guid, Guid> Membres);

    private async Task<Groupe> TontineAsync()
    {
        var g = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var membres = new List<Acteur>();
        foreach (var nom in new[] { "Alice Ngono", "Bruno Mbarga", "Chloé Fotso", "David Kamga", "Emma Tchana" })
            membres.Add(await api.CompteAsync(nom));

        var id = (Guid)(await g.Envoyer("/api/v1/tontines", new
        {
            name = "Tontine " + Guid.NewGuid().ToString("N")[..6], description = "Tests", contributionAmount = Cotisation,
            periodicity = "Monthly", maxMembers = 8,
        }))["id"]!;
        var code = (string)(await g.Lire($"/api/v1/tontines/{id}/invitation/generer?nombreUsagesMax=10"))["code"]!;
        foreach (var m in membres)
            await m.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = m.Nom });
        (await g.Post($"/api/v1/tontines/{id}/activate")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var d = await g.Lire($"/api/v1/tontines/{id}/details");
        var ids = membres.ToDictionary(u => u.Id,
            u => (Guid)d["membres"]!.AsArray().First(m => (Guid?)m!["utilisateurId"] == u.Id)!["id"]!);
        var tour = (Guid)d["tours"]!.AsArray().First(t => (string)t!["status"]! == "Ouvert")!["id"]!;
        return new Groupe(id, tour, g, membres[0], membres[1], membres[2], membres[3], membres[4], ids);
    }

    /// <summary>Declared by the member, validated by the trésorier (Gaston as long as nobody else is named).</summary>
    private static async Task PayerAsync(Groupe t, Acteur qui, decimal montant, Guid tourId, Acteur? tresorier = null)
    {
        var r = await qui.Post("/api/v1/versements/manuel",
            new { tontineId = t.Id, tourId, montant, moyen = "Espèces", reference = "REF-R" });
        var corps = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, corps);
        var v = (Guid)JsonNode.Parse(corps)!["versementId"]!;
        await (tresorier ?? t.Gaston).Envoyer($"/api/v1/versements/{v}/valider");
    }

    private static async Task<Guid> CloturerTour1(Groupe t)
    {
        (await t.Gaston.Post($"/api/v1/tontines/{t.Id}/rounds/{t.Tour1}/close")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var d = await t.Gaston.Lire($"/api/v1/tontines/{t.Id}/details");
        return (Guid)d["tours"]!.AsArray().First(x => (string)x!["status"]! == "Ouvert")!["id"]!;
    }

    private static Task NommerTresorier(Groupe t, Acteur tresorier) =>
        t.Gaston.Remplacer($"/api/v1/tontines/{t.Id}/gouvernance/tresorier", new { utilisateurId = tresorier.Id });

    private async Task EcheanceDansAsync(Guid tourId, int jours)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
        var date = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(jours).AddHours(12), DateTimeKind.Utc);
        (await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tours_de_role SET date_limite = {date} WHERE id = {tourId}"))
            .Should().Be(1);
    }

    private async Task ExecuterAsync<TJob>(Func<TJob, Task> execution) where TJob : notnull
    {
        using var scope = api.Services.CreateScope();
        await execution(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    private static string Statut(TontineStatus statut) => statut.ToString().ToUpperInvariant(); // as stored

    private async Task StatutAsync(Groupe t, TontineStatus statut)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TontineDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE tontines SET statut = {Statut(statut)} WHERE id = {t.Id}");
    }

    /// <summary>Logger that keeps what the job writes.</summary>
    private sealed class JournalCapture : ILogger<RecapRetardsBureauJob>
    {
        public List<(LogLevel Niveau, string Message)> Entrees { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entrees) Entrees.Add((logLevel, formatter(state, exception)));
        }
    }

    private Task RecapAsync() => ExecuterAsync<RecapRetardsBureauJob>(j => j.ExecuteAsync());

    /// <summary>Contents of the SMS of <paramref name="type"/> planned for this account.</summary>
    private async Task<List<string>> SmsAsync(Acteur qui, NotificationType type)
    {
        using var scope = api.Services.CreateScope();
        var compte = await scope.ServiceProvider.GetRequiredService<IUtilisateurRepository>().GetByIdAsync(UtilisateurId.From(qui.Id));
        var telephone = compte!.Telephone.Value;
        var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.DestinataireId == telephone && n.Type == type)
            .Select(n => n.Contenu).ToListAsync();
    }

    // ── Récapitulatif des retards au bureau ──────────────────────────

    [Fact]
    public async Task Retards_UnRecapAuResponsableEtAuTresorier_NomsEtTotal_UneSeuleFoisMemeSiLeJobRepasse()
    {
        var t = await TontineAsync();
        await PayerAsync(t, t.David, 2000m, t.Tour1);      // partial
        await PayerAsync(t, t.Emma, Cotisation, t.Tour1);  // paid
        await NommerTresorier(t, t.Emma);
        await EcheanceDansAsync(t.Tour1, -1);

        await RecapAsync();
        await RecapAsync();

        foreach (var bureau in new[] { t.Gaston, t.Emma })
        {
            var sms = await SmsAsync(bureau, NotificationType.RecapRetards);
            sms.Should().ContainSingle();
            sms[0].Should().Contain("tour 1").And.Contain("3 en retard").And.Contain("13000 XAF")
                .And.Contain("Bruno Mbarga").And.Contain("Chloé Fotso").And.Contain("David Kamga")
                .And.NotContain("Emma").And.NotContain("Alice");
        }
        foreach (var membre in new[] { t.Alice, t.Bruno, t.Chloe, t.David })
            (await SmsAsync(membre, NotificationType.RecapRetards)).Should().BeEmpty();

        // The late list changes during the day: still no second recap.
        await PayerAsync(t, t.David, 3000m, t.Tour1, tresorier: t.Emma);
        await RecapAsync();
        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().ContainSingle();
        (await SmsAsync(t.Emma, NotificationType.RecapRetards)).Should().ContainSingle();
    }

    [Fact]
    public async Task ResponsableEstAussiTresorier_UnSeulSms()
    {
        var t = await TontineAsync();
        await EcheanceDansAsync(t.Tour1, -1);

        await RecapAsync();

        var sms = await SmsAsync(t.Gaston, NotificationType.RecapRetards);
        sms.Should().ContainSingle().Which.Should().Contain("4 en retard").And.Contain("20000 XAF");
    }

    [Fact]
    public async Task PersonneEnRetard_ReportCompris_AucunSms()
    {
        var t = await TontineAsync();
        await PayerAsync(t, t.Chloe, 2 * Cotisation, t.Tour1); // round 2 covered by the carry-over
        foreach (var m in new[] { t.Bruno, t.David, t.Emma })
            await PayerAsync(t, m, Cotisation, t.Tour1);
        var tour2 = await CloturerTour1(t);                     // round 2: Bruno is the beneficiary
        foreach (var m in new[] { t.Alice, t.David, t.Emma })
            await PayerAsync(t, m, Cotisation, tour2);
        await EcheanceDansAsync(t.Tour1, -30);
        await EcheanceDansAsync(tour2, -1);

        await RecapAsync();

        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().BeEmpty();
    }

    [Fact]
    public async Task MembreDuBureauSeulEnRetard_FigureDansLeRecapDuResponsable_SansAlerteSurLuiMeme()
    {
        var t = await TontineAsync();
        foreach (var m in new[] { t.Bruno, t.Chloe, t.David })
            await PayerAsync(t, m, Cotisation, t.Tour1);
        await NommerTresorier(t, t.Emma); // Emma, the trésorier, is the only one late
        await EcheanceDansAsync(t.Tour1, -1);

        await RecapAsync();

        (await SmsAsync(t.Emma, NotificationType.RecapRetards)).Should().BeEmpty();
        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().ContainSingle()
            .Which.Should().Contain("1 en retard").And.Contain("Emma Tchana").And.Contain("5000 XAF");
    }

    [Fact]
    public async Task EcheanceAujourdhuiOuHorsFenetre_Rien_JourManque_RecapRattrape()
    {
        var t = await TontineAsync();

        await EcheanceDansAsync(t.Tour1, 0);                                  // not passed yet
        await RecapAsync();
        await EcheanceDansAsync(t.Tour1, -(RecapRetardsBureauJob.FenetreJours + 1)); // too old
        await RecapAsync();
        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().BeEmpty();

        await EcheanceDansAsync(t.Tour1, -2);                                 // job missed yesterday
        await RecapAsync();
        await RecapAsync();
        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().ContainSingle();
    }

    [Fact]
    public async Task TontineNonActive_AucunRecap()
    {
        var t = await TontineAsync();
        await EcheanceDansAsync(t.Tour1, -1);
        await StatutAsync(t, TontineStatus.Suspended);

        await RecapAsync();

        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().BeEmpty();
    }

    [Fact]
    public async Task TontineTermineeParLaClotureDuDernierTour_RecapEnvoye()
    {
        var t = await TontineAsync();
        await EcheanceDansAsync(t.Tour1, -1);
        await StatutAsync(t, TontineStatus.Completed);

        await RecapAsync();

        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().ContainSingle();
    }

    [Fact]
    public async Task ResponsableEtTresorierReattribues_LesNouveauxRecoiventLeRecap_PasLeCreateur()
    {
        var t = await TontineAsync();
        var admin = await api.CompteAsync("Admin Plateforme", RoleUtilisateur.Admin);
        await admin.Remplacer($"/api/v1/tontines/{t.Id}/gouvernance/roles", new { utilisateurId = t.Alice.Id, role = "Responsable" });
        await admin.Remplacer($"/api/v1/tontines/{t.Id}/gouvernance/roles", new { utilisateurId = t.Emma.Id, role = "Tresorier" });
        await EcheanceDansAsync(t.Tour1, -1);

        await RecapAsync();

        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().BeEmpty();
        (await SmsAsync(t.Alice, NotificationType.RecapRetards)).Should().ContainSingle();
        (await SmsAsync(t.Emma, NotificationType.RecapRetards)).Should().ContainSingle()
            .Which.Should().Contain("4 en retard").And.Contain("Emma Tchana"); // late herself, with others
    }

    [Fact]
    public async Task TourClosAvantEcheance_AvecImpayes_Liste()
    {
        var t = await TontineAsync();
        await PayerAsync(t, t.Bruno, Cotisation, t.Tour1);
        await CloturerTour1(t);
        await EcheanceDansAsync(t.Tour1, -1);

        await RecapAsync();

        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().ContainSingle()
            .Which.Should().Contain("tour 1: 3 en retard, 15000 XAF du.").And.NotContain("Bruno");
    }

    [Fact]
    public async Task DeuxToursEchusLeMemeJour_DeuxRecaps()
    {
        var t = await TontineAsync();
        var tour2 = await CloturerTour1(t);
        await EcheanceDansAsync(t.Tour1, -1);
        await EcheanceDansAsync(tour2, -1);

        await RecapAsync();
        await RecapAsync();

        var sms = await SmsAsync(t.Gaston, NotificationType.RecapRetards);
        sms.Should().HaveCount(2);
        sms.Should().ContainSingle(m => m.Contains(" tour 1: "));
        sms.Should().ContainSingle(m => m.Contains(" tour 2: "));
    }

    [Fact]
    public async Task DeuxExecutionsSimultanees_UnSeulRecapParPersonne()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Emma);
        await EcheanceDansAsync(t.Tour1, -1);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RecapAsync()));

        (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Should().ContainSingle();
        (await SmsAsync(t.Emma, NotificationType.RecapRetards)).Should().ContainSingle();
    }

    [Fact]
    public async Task SansBureauEtCreateurSansCompte_Avertissement_SansPlantage_LesAutresTontinesServies()
    {
        var orpheline = await TontineAsync();
        var autre = await TontineAsync();
        await EcheanceDansAsync(orpheline.Tour1, -1);
        await EcheanceDansAsync(autre.Tour1, -1);
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TontineDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE tontines SET gestionnaire_id = {Guid.NewGuid()} WHERE id = {orpheline.Id}");
        var journal = new JournalCapture();

        using (var scope = api.Services.CreateScope())
            await ActivatorUtilities.CreateInstance<RecapRetardsBureauJob>(scope.ServiceProvider, journal).ExecuteAsync();

        journal.Entrees.Should().Contain(e => e.Niveau == LogLevel.Warning && e.Message.Contains(orpheline.Id.ToString())
                                              && e.Message.Contains("round 1"));
        journal.Entrees.Should().NotContain(e => e.Niveau == LogLevel.Error);
        (await SmsAsync(orpheline.Gaston, NotificationType.RecapRetards)).Should().BeEmpty();
        (await SmsAsync(autre.Gaston, NotificationType.RecapRetards)).Should().ContainSingle();
    }

    [Fact]
    public async Task LeRecapNommeExactementLesRetardatairesDeLEcran()
    {
        var t = await TontineAsync();
        await PayerAsync(t, t.Bruno, Cotisation, t.Tour1);   // paid
        await PayerAsync(t, t.David, 2000m, t.Tour1);        // partial
        var r = await t.Emma.Post("/api/v1/versements/manuel", // pending: covered, as on the screen
            new { tontineId = t.Id, tourId = t.Tour1, montant = Cotisation, moyen = "Espèces", reference = "REF-A" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        await EcheanceDansAsync(t.Tour1, -1);

        await RecapAsync();

        var ecran = (await t.Gaston.Lire($"/api/v1/tontines/{t.Id}/tours/{t.Tour1}/retardataires")).AsArray()
            .Select(x => (string)x!["nom"]!).ToList();
        var sms = (await SmsAsync(t.Gaston, NotificationType.RecapRetards)).Single();
        var noms = sms[(sms.IndexOf(" du. ", StringComparison.Ordinal) + 5)..].Split(", ");
        noms.Should().BeEquivalentTo(ecran);
        ecran.Should().BeEquivalentTo("Chloé Fotso", "David Kamga");
    }

    // ── Rappels J-3 / J-1 : seulement ceux qui doivent encore ────────

    [Fact]
    public async Task RappelJ1_PasAuMembreQuiAPaye_NiAuBeneficiaire_LePartielEstRappeleDuReste()
    {
        var t = await TontineAsync();
        await PayerAsync(t, t.Bruno, Cotisation, t.Tour1);
        await PayerAsync(t, t.Chloe, 2000m, t.Tour1);
        await EcheanceDansAsync(t.Tour1, 1);

        await ExecuterAsync<RappelJ1Job>(j => j.ExecuteAsync());

        (await SmsAsync(t.Bruno, NotificationType.RappelPaiement)).Should().BeEmpty();
        (await SmsAsync(t.Alice, NotificationType.RappelPaiement)).Should().BeEmpty();
        (await SmsAsync(t.Chloe, NotificationType.RappelPaiement)).Should().ContainSingle()
            .Which.Should().Contain("3000").And.Contain("demain");
        (await SmsAsync(t.David, NotificationType.RappelPaiement)).Should().ContainSingle()
            .Which.Should().Contain("5000");
    }

    [Fact]
    public async Task RappelJ3_PasAuMembreCouvertParLeReport()
    {
        var t = await TontineAsync();
        await PayerAsync(t, t.Chloe, 2 * Cotisation, t.Tour1);
        var tour2 = await CloturerTour1(t); // Chloé's round 2 is covered by her surplus
        await EcheanceDansAsync(tour2, 3);

        await ExecuterAsync<RappelJ3Job>(j => j.ExecuteAsync());

        (await SmsAsync(t.Chloe, NotificationType.RappelPaiement)).Should().BeEmpty();
        (await SmsAsync(t.Bruno, NotificationType.RappelPaiement)).Should().BeEmpty(); // beneficiary of round 2
        (await SmsAsync(t.Alice, NotificationType.RappelPaiement)).Should().ContainSingle()
            .Which.Should().Contain("3 jours");
    }
}
