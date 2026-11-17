namespace ApiTests;

using System.Net;
using System.Text.Json.Nodes;
using Domain.Gouvernance;
using Domain.IdentityManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The web dashboard's "gestionnaire" space is the bureau's: open to the responsable and the trésorier
/// of a tontine whatever their platform role, closed to plain members and to a creator who no longer
/// sits on the bureau. Same rights as the mobile API.
/// </summary>
[Collection(ApiCollection.Nom)]
public sealed class TableauDeBordBureauTests(ApiFactory api)
{
    private const decimal Cotisation = 5000m;
    private const string Tableau = "/api/v1/dashboard/gestionnaire/tontine";

    /// <summary>
    /// Active tontine created by Gaston (platform "Gestionnaire", responsable + trésorier, not a member)
    /// with Alice, Bruno and Chloé ("Membre" accounts). Round 1 is open.
    /// </summary>
    private sealed record Groupe(Guid Id, Guid TourId, Acteur Gaston, Acteur Alice, Acteur Bruno, Acteur Chloe, JsonNode Details)
    {
        public Guid MembreDe(Acteur u) => (Guid)Details["membres"]!.AsArray().First(m => (Guid?)m!["utilisateurId"] == u.Id)!["id"]!;

        /// <summary>A member who owes round 1 (not its beneficiary), other than <paramref name="sauf"/>.</summary>
        public Acteur Payeur(params Acteur[] sauf)
        {
            var beneficiaire = (string?)Details["tours"]!.AsArray().First(t => (Guid)t!["id"]! == TourId)!["beneficiaire"]?["nom"];
            return new[] { Alice, Bruno, Chloe }.First(m => m.Nom != beneficiaire && !sauf.Contains(m));
        }
    }

    private async Task<Groupe> TontineAsync(Acteur? createur = null, bool activer = true)
    {
        var g = createur ?? await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var a = await api.CompteAsync("Alice Ngono");
        var b = await api.CompteAsync("Bruno Mbarga");
        var c = await api.CompteAsync("Chloé Fotso");

        var id = (Guid)(await g.Envoyer("/api/v1/tontines", new
        {
            name = "Tontine du bureau", description = "Tests", contributionAmount = Cotisation, periodicity = "Monthly", maxMembers = 8,
        }))["id"]!;
        var code = (string)(await g.Lire($"/api/v1/tontines/{id}/invitation/generer?nombreUsagesMax=10"))["code"]!;
        foreach (var m in new[] { a, b, c })
            await m.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = m.Nom });
        if (activer)
            (await g.Post($"/api/v1/tontines/{id}/activate")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var d = await g.Lire($"/api/v1/tontines/{id}/details");
        var tour = (Guid?)d["tours"]!.AsArray().FirstOrDefault(t => (string)t!["status"]! == "Ouvert")?["id"] ?? Guid.Empty;
        return new Groupe(id, tour, g, a, b, c, d);
    }

    private static Task NommerTresorier(Groupe t, Acteur par, Acteur tresorier) =>
        par.Remplacer($"/api/v1/tontines/{t.Id}/gouvernance/tresorier", new { utilisateurId = tresorier.Id });

    /// <summary>Alice, Bruno and Chloé elect <paramref name="candidat"/> as responsable (3 of 4 electors).</summary>
    private static async Task ElireResponsable(Groupe t, Acteur candidat)
    {
        var etat = await t.Alice.Envoyer($"/api/v1/tontines/{t.Id}/votes/responsable", new { utilisateurId = candidat.Id });
        var vote = (Guid)etat["votes"]!.AsArray().Single()!["id"]!;
        foreach (var m in new[] { t.Alice, t.Bruno, t.Chloe })
            etat = await m.Envoyer($"/api/v1/tontines/{t.Id}/votes/{vote}", new { pour = true });
        ((Guid)etat["bureau"]!["responsableId"]!).Should().Be(candidat.Id);
    }

    private static async Task<Guid> Declarer(Acteur payeur, Groupe t)
    {
        var r = await payeur.Post("/api/v1/versements/manuel", new { tontineId = t.Id, tourId = t.TourId, montant = Cotisation, moyen = "Espèces" });
        var corps = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, corps);
        return (Guid)JsonNode.Parse(corps)!["versementId"]!;
    }

    private static string[] Roles(JsonNode tableau) =>
        tableau["mesRoles"]!.AsArray().Select(x => (string)x!).ToArray();

    // ── Trésorier « Membre » ─────────────────────────────────────────

    [Fact]
    public async Task TresorierMembre_OuvreLeTableauDeBord_EtValide_SansRoleDeResponsable()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Gaston, t.Bruno);

        // Default tontine (no tontineId): the one where Bruno is trésorier, with his role only.
        var tableau = await t.Bruno.Lire(Tableau);
        ((Guid)tableau["id"]!).Should().Be(t.Id);
        Roles(tableau).Should().BeEquivalentTo("tresorier");
        (await t.Bruno.Get($"/api/v1/dashboard/gestionnaire/versements?tontineId={t.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await t.Bruno.Get("/api/v1/dashboard/gestionnaire/audit")).StatusCode.Should().Be(HttpStatusCode.OK);

        // He validates a payment from the web (same endpoint as the dashboard's proxy).
        var payeur = t.Payeur(t.Bruno);
        var v = await Declarer(payeur, t);
        (await t.Bruno.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.OK);
        tableau = await t.Bruno.Lire(Tableau);
        ((string)tableau["membres"]!.AsArray().First(m => (Guid)m!["id"]! == t.MembreDe(payeur))!["statut"]!).Should().Be("paye");

        // Running the tontine stays the responsable's.
        (await t.Bruno.Put($"/api/v1/tontines/{t.Id}/regles", new { adhesionEnCours = "Libre", rattrapageAdhesionTardive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Bruno.Post($"/api/v1/tontines/{t.Id}/members/{t.MembreDe(payeur)}/relancer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Gaston is still responsable: he keeps the tontine, without the trésorier role.
        Roles(await t.Gaston.Lire(Tableau)).Should().BeEquivalentTo("responsable");
    }

    // ── Responsable élu « Membre » ───────────────────────────────────

    [Fact]
    public async Task ResponsableElu_VoitSaTontineParDefaut_AccepteLesAdhesions_EtRelance()
    {
        var t = await TontineAsync();
        await ElireResponsable(t, t.Chloe);

        var tableau = await t.Chloe.Lire(Tableau);
        ((Guid)tableau["id"]!).Should().Be(t.Id);
        Roles(tableau).Should().BeEquivalentTo("responsable");

        // A late membership request: the elected responsable accepts it.
        var dan = await api.CompteAsync("Dan Tchoua");
        var code = (string)(await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/invitation/generer?nombreUsagesMax=5"))["code"]!;
        await dan.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = dan.Nom });
        var demande = (Guid)(await t.Chloe.Lire(Tableau))["membres"]!.AsArray()
            .First(m => (string)m!["statut"]! == "adhesion_en_attente")!["id"]!;
        (await t.Chloe.Post($"/api/v1/tontines/{t.Id}/membres/{demande}/accepter")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Reminder of a member who owes the round.
        var payeur = t.Payeur(t.Chloe);
        (await t.Chloe.Post($"/api/v1/tontines/{t.Id}/members/{t.MembreDe(payeur)}/relancer")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Créateur évincé ──────────────────────────────────────────────

    [Fact]
    public async Task CreateurEvince_NeVoitPlusLaTontine_SaufSIlResteTresorier()
    {
        var t = await TontineAsync();
        await ElireResponsable(t, t.Chloe);

        // Still the trésorier: he keeps the tontine, as trésorier only.
        var tableau = await t.Gaston.Lire(Tableau);
        ((Guid)tableau["id"]!).Should().Be(t.Id);
        Roles(tableau).Should().BeEquivalentTo("tresorier");

        // The new responsable names someone else trésorier: Gaston no longer sits on the bureau.
        await NommerTresorier(t, t.Chloe, t.Alice);
        (await t.Gaston.Get($"{Tableau}?tontineId={t.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Gaston.Get($"/api/v1/dashboard/gestionnaire/versements?tontineId={t.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Without tontineId: none left ("Gestionnaire" platform role ⇒ « aucune tontine », he may create one).
        (await t.Gaston.Get(Tableau)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        Roles(await t.Alice.Lire(Tableau)).Should().BeEquivalentTo("tresorier");
    }

    // ── Simple membre ────────────────────────────────────────────────

    [Fact]
    public async Task SimpleMembre_EstRefuseSurLEspaceDuBureau()
    {
        var t = await TontineAsync();

        foreach (var url in new[]
                 {
                     Tableau, $"{Tableau}?tontineId={t.Id}",
                     $"/api/v1/dashboard/gestionnaire/versements?tontineId={t.Id}", "/api/v1/dashboard/gestionnaire/versements",
                     $"/api/v1/dashboard/gestionnaire/audit?tontineId={t.Id}", "/api/v1/dashboard/gestionnaire/audit",
                 })
            (await t.Alice.Get(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);

        // An unknown tontine is still a 404.
        (await t.Alice.Get($"{Tableau}?tontineId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Plusieurs rôles ──────────────────────────────────────────────

    [Fact]
    public async Task PlusieursRoles_TontineParDefautDeterministe_EtChacuneAccessibleParSonId()
    {
        // Henri creates T1 (responsable + trésorier of it) and is named trésorier of T2.
        var henri = await api.CompteAsync("Henri Kamga", RoleUtilisateur.Gestionnaire);
        var t1 = await TontineAsync(henri);
        var t2 = await TontineAsync();
        // The trésorier must be a member of the group: Henri joins T2.
        var code = (string)(await t2.Gaston.Lire($"/api/v1/tontines/{t2.Id}/invitation/generer?nombreUsagesMax=5"))["code"]!;
        await henri.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = henri.Nom });
        var demande = (Guid)(await t2.Gaston.Lire(Tableau))["membres"]!.AsArray()
            .First(m => (string)m!["statut"]! == "adhesion_en_attente")!["id"]!;
        await t2.Gaston.Envoyer($"/api/v1/tontines/{t2.Id}/membres/{demande}/accepter");
        await NommerTresorier(t2, t2.Gaston, henri);

        // Default: both active, the most recent (T2) first; the same on every call.
        var premier = (Guid)(await henri.Lire(Tableau))["id"]!;
        premier.Should().Be(t2.Id);
        ((Guid)(await henri.Lire(Tableau))["id"]!).Should().Be(premier);

        Roles(await henri.Lire($"{Tableau}?tontineId={t1.Id}")).Should().BeEquivalentTo("responsable", "tresorier");
        Roles(await henri.Lire($"{Tableau}?tontineId={t2.Id}")).Should().BeEquivalentTo("tresorier");
        (await henri.Get($"/api/v1/dashboard/gestionnaire/audit?tontineId={t2.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TontineParDefaut_LActiveAvantUnBrouillonPlusRecent()
    {
        var gaston = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var active = await TontineAsync(gaston);
        var brouillon = await TontineAsync(gaston, activer: false);
        brouillon.Id.Should().NotBe(active.Id);

        ((Guid)(await gaston.Lire(Tableau))["id"]!).Should().Be(active.Id);
    }

    // ── Administrateur ───────────────────────────────────────────────

    [Fact]
    public async Task Admin_OuvreToutesLesTontinesParLeurId_SansSiegeAuBureau()
    {
        var t = await TontineAsync();
        var admin = await api.CompteAsync("Ada Admin", RoleUtilisateur.Admin);

        Roles(await admin.Lire($"{Tableau}?tontineId={t.Id}")).Should().Equal("admin");
        (await admin.Get($"/api/v1/dashboard/gestionnaire/versements?tontineId={t.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.Get($"/api/v1/dashboard/gestionnaire/audit?tontineId={t.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // No bureau seat and no tontineId: nothing to show by default.
        (await admin.Get(Tableau)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Sélecteur de tontine : les tontines du bureau de l'appelant ──

    private const string MesTontines = "/api/v1/dashboard/gestionnaire/tontines";

    private static (Guid Id, string[] Roles)[] Liste(JsonNode liste) =>
        liste.AsArray().Select(x => ((Guid)x!["id"]!, x["mesRoles"]!.AsArray().Select(r => (string)r!).ToArray())).ToArray();

    [Fact]
    public async Task MesTontines_ResponsableDeT1_TresorierDeT2_ListeLesDeuxAvecSesRoles_EtChacuneSOuvre()
    {
        // Henri is responsable of T1 (elected; Gaston created it and stays trésorier) and trésorier of T2.
        var henri = await api.CompteAsync("Henri Kamga");
        var t1 = await TontineAsync();
        await RejoindreEtAccepter(t1, henri);
        await ElireResponsableParTous(t1, henri);
        var t2 = await TontineAsync();
        await RejoindreEtAccepter(t2, henri);
        await NommerTresorier(t2, t2.Gaston, henri);

        var liste = Liste(await henri.Lire(MesTontines));
        liste.Select(x => x.Id).Should().BeEquivalentTo([t1.Id, t2.Id]);
        liste.Single(x => x.Id == t1.Id).Roles.Should().BeEquivalentTo("responsable");
        liste.Single(x => x.Id == t2.Id).Roles.Should().BeEquivalentTo("tresorier");
        // The first one is the default tontine of the dashboard.
        ((Guid)(await henri.Lire(Tableau))["id"]!).Should().Be(liste[0].Id);

        // Choosing T1: its dashboard, payments and audit, with the responsable's rights.
        Roles(await henri.Lire($"{Tableau}?tontineId={t1.Id}")).Should().BeEquivalentTo("responsable");
        (await henri.Get($"/api/v1/dashboard/gestionnaire/versements?tontineId={t1.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await henri.Get($"/api/v1/dashboard/gestionnaire/audit?tontineId={t1.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await henri.Put($"/api/v1/tontines/{t1.Id}/regles", new { adhesionEnCours = "Libre", rattrapageAdhesionTardive = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Choosing T2, where he is only trésorier: running it stays refused.
        Roles(await henri.Lire($"{Tableau}?tontineId={t2.Id}")).Should().BeEquivalentTo("tresorier");
        (await henri.Put($"/api/v1/tontines/{t2.Id}/regles", new { adhesionEnCours = "Libre", rattrapageAdhesionTardive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MesTontines_ActivesAvantClotureesMemePlusRecentes_AvecLeurStatut()
    {
        var gaston = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var ancienne = await TontineAsync(gaston);
        var recente = await TontineAsync(gaston);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TontineDbContext>();
            var tontineId = TontineId.From(recente.Id);
            (await db.Tontines.SingleAsync(x => x.Id == tontineId)).Clore();
            await db.SaveChangesAsync();
        }

        var liste = (await gaston.Lire(MesTontines)).AsArray();
        liste.Select(x => (Guid)x!["id"]!).Should().Equal(ancienne.Id, recente.Id);
        liste.Select(x => (string)x!["status"]!).Should().Equal("Active", "Completed");
        liste.Select(x => (string)x!["nom"]!).Should().AllSatisfy(n => n.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task MesTontines_CreateurRemplaceDansLesDeuxRoles_NEstPlusListe()
    {
        var t = await TontineAsync();
        Liste(await t.Gaston.Lire(MesTontines)).Select(x => x.Id).Should().Contain(t.Id);

        await ElireResponsable(t, t.Chloe);
        await NommerTresorier(t, t.Chloe, t.Alice);

        Liste(await t.Gaston.Lire(MesTontines)).Select(x => x.Id).Should().NotContain(t.Id);
        Liste(await t.Chloe.Lire(MesTontines)).Single(x => x.Id == t.Id).Roles.Should().BeEquivalentTo("responsable");
    }

    [Fact]
    public async Task MesTontines_UneSeule_PourUnTresorier()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Gaston, t.Bruno);

        var liste = Liste(await t.Bruno.Lire(MesTontines));
        liste.Should().ContainSingle();
        liste[0].Id.Should().Be(t.Id);
        liste[0].Roles.Should().BeEquivalentTo("tresorier");
    }

    [Fact]
    public async Task MesTontines_ChoixHorsBureau_Refuse()
    {
        var t = await TontineAsync();
        var autre = await TontineAsync();

        // Alice is a plain member: no tontine of hers; Gaston of T cannot open the other one.
        (await t.Alice.Lire(MesTontines)).AsArray().Should().BeEmpty();
        Liste(await t.Gaston.Lire(MesTontines)).Select(x => x.Id).Should().NotContain(autre.Id);
        (await t.Gaston.Get($"{Tableau}?tontineId={autre.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Gaston.Get($"/api/v1/dashboard/gestionnaire/versements?tontineId={autre.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Gaston.Get($"/api/v1/dashboard/gestionnaire/audit?tontineId={autre.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MesTontines_Admin_SeulementOuIlSiege_MaisToutTontineIdExpliciteAccepte()
    {
        var etrangere = await TontineAsync();
        var admin = await api.CompteAsync("Ada Admin", RoleUtilisateur.Admin);
        (await admin.Lire(MesTontines)).AsArray().Should().BeEmpty();

        var sienne = await TontineAsync(admin);
        Liste(await admin.Lire(MesTontines)).Select(x => x.Id).Should().Equal(sienne.Id);

        Roles(await admin.Lire($"{Tableau}?tontineId={etrangere.Id}")).Should().Equal("admin");
    }

    [Fact]
    public async Task MesTontines_AdminAussiResponsable_AAdminEnPlusDeResponsable()
    {
        var t = await TontineAsync();
        var admin = await api.CompteAsync("Ada Admin", RoleUtilisateur.Admin);
        await RejoindreEtAccepter(t, admin);
        await ElireResponsableParTous(t, admin);

        Liste(await admin.Lire(MesTontines)).Single(x => x.Id == t.Id).Roles
            .Should().BeEquivalentTo("responsable", "admin");
    }

    [Fact]
    public async Task MesTontines_SansJeton_401()
    {
        using var client = api.CreateClient();
        (await client.GetAsync(MesTontines)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task RejoindreEtAccepter(Groupe t, Acteur nouveau)
    {
        var code = (string)(await t.Gaston.Lire($"/api/v1/tontines/{t.Id}/invitation/generer?nombreUsagesMax=5"))["code"]!;
        await nouveau.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = nouveau.Nom });
        var demande = (Guid)(await t.Gaston.Lire($"{Tableau}?tontineId={t.Id}"))["membres"]!.AsArray()
            .First(m => (string)m!["statut"]! == "adhesion_en_attente")!["id"]!;
        await t.Gaston.Envoyer($"/api/v1/tontines/{t.Id}/membres/{demande}/accepter");
    }

    /// <summary>Alice, Bruno and Chloé elect <paramref name="candidat"/> (a fourth member) as responsable.</summary>
    private static async Task ElireResponsableParTous(Groupe t, Acteur candidat)
    {
        var etat = await t.Alice.Envoyer($"/api/v1/tontines/{t.Id}/votes/responsable", new { utilisateurId = candidat.Id });
        var vote = (Guid)etat["votes"]!.AsArray().Single()!["id"]!;
        foreach (var m in new[] { t.Alice, t.Bruno, t.Chloe, candidat })
        {
            etat = await m.Envoyer($"/api/v1/tontines/{t.Id}/votes/{vote}", new { pour = true });
            if ((Guid?)etat["bureau"]!["responsableId"] == candidat.Id) break;
        }
        ((Guid)etat["bureau"]!["responsableId"]!).Should().Be(candidat.Id);
    }

    // ── Repository on a real database: the candidate of an open vote ──

    [Fact]
    public async Task GroupesOuSiege_CandidatDUnVoteOuvert_CompteApresLEcheanceSeulement()
    {
        var t = await TontineAsync();
        // Alice proposes Chloé; Alice and Bruno vote yes: 2 of 4 electors, not decided yet.
        var etat = await t.Alice.Envoyer($"/api/v1/tontines/{t.Id}/votes/responsable", new { utilisateurId = t.Chloe.Id });
        var vote = (Guid)etat["votes"]!.AsArray().Single()!["id"]!;
        foreach (var m in new[] { t.Alice, t.Bruno })
            etat = await m.Envoyer($"/api/v1/tontines/{t.Id}/votes/{vote}", new { pour = true });
        ((string)etat["votes"]!.AsArray().Single()!["statut"]!).Should().Be("Ouvert");

        using var scope = api.Services.CreateScope();
        var bureaux = scope.ServiceProvider.GetRequiredService<IBureauRepository>();
        var maintenant = DateTime.UtcNow;

        // Past the deadline (7 days), the vote is adopted (quorum reached, 2 yes, 0 no): Chloé is responsable.
        (await bureaux.GetGroupesOuSiegeAsync(t.Chloe.Id, TypeGroupe.Tontine, maintenant.AddDays(8))).Should().Contain(t.Id);
        (await bureaux.GetGroupesOuSiegeAsync(t.Chloe.Id, TypeGroupe.Tontine, maintenant)).Should().NotContain(t.Id);
        (await bureaux.GetGroupesOuSiegeAsync(t.Chloe.Id, TypeGroupe.Projet, maintenant.AddDays(8))).Should().NotContain(t.Id);
    }

    [Fact]
    public async Task IdentifiantVide_TontineIntrouvable_JamaisErreurServeur()
    {
        // "00000000-…" is a valid GUID that names no tontine (TontineId refuses it): 404, never a 500.
        var t = await TontineAsync();
        var vide = Guid.Empty;

        foreach (var url in new[]
        {
            $"{Tableau}?tontineId={vide}",
            $"/api/v1/dashboard/gestionnaire/versements?tontineId={vide}",
            $"/api/v1/dashboard/gestionnaire/audit?tontineId={vide}",
            $"/api/v1/tontines/{vide}/details",
            $"/api/v1/tontines/{vide}/versements",
            $"/api/v1/tontines/{vide}/audit",
        })
            (await t.Gaston.Get(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
    }
}

