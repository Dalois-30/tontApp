namespace ApiTests;

using System.Net;
using System.Text.Json.Nodes;
using Domain.IdentityManagement.ValueObjects;
using FluentAssertions;

/// <summary>
/// Payment circuit of a tontine, end to end over HTTP: each member declares their own payment, the
/// trésorier validates or rejects (reason required), every member reads all payments and the journal.
/// </summary>
[Collection(ApiCollection.Nom)]
public sealed class TontineCircuitTests(ApiFactory api)
{
    private const decimal Cotisation = 5000m;

    /// <summary>
    /// An active tontine: Gaston (creator, responsable + trésorier, not a member), Alice (beneficiary of
    /// round 1), Bruno and Chloé (payers of round 1), and Xavier who is not in the group.
    /// </summary>
    private sealed record Groupe(Guid Id, Guid TourId, Acteur Gestionnaire, Acteur Alice, Acteur Bruno, Acteur Chloe,
        Acteur Etranger, Guid MembreAlice, Guid MembreBruno, Guid MembreChloe);

    private async Task<Groupe> TontineAsync(bool activer = true)
    {
        var g = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var a = await api.CompteAsync("Alice Ngono");
        var b = await api.CompteAsync("Bruno Mbarga");
        var c = await api.CompteAsync("Chloé Fotso");
        var x = await api.CompteAsync("Xavier Etranger");

        var id = (Guid)(await g.Envoyer("/api/v1/tontines", new
        {
            name = "Tontine du quartier", description = "Tests", contributionAmount = Cotisation, periodicity = "Monthly", maxMembers = 6,
        }))["id"]!;
        var code = (string)(await g.Lire($"/api/v1/tontines/{id}/invitation/generer?nombreUsagesMax=10"))["code"]!;
        foreach (var m in new[] { a, b, c })
            await m.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = m.Nom });

        if (activer)
            (await g.Post($"/api/v1/tontines/{id}/activate")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var d = await g.Lire($"/api/v1/tontines/{id}/details");
        Guid Membre(Acteur u) => (Guid)d["membres"]!.AsArray().First(m => (Guid?)m!["utilisateurId"] == u.Id)!["id"]!;
        var tour = d["tours"]!.AsArray().FirstOrDefault(t => (string)t!["status"]! == "Ouvert");
        return new Groupe(id, tour is null ? Guid.Empty : (Guid)tour["id"]!, g, a, b, c, x, Membre(a), Membre(b), Membre(c));
    }

    private static Task<HttpResponseMessage> Declarer(Acteur qui, Groupe t, Guid? membreId = null, string? moyen = "Espèces") =>
        qui.Post("/api/v1/versements/manuel", new { tontineId = t.Id, tourId = t.TourId, membreId, montant = Cotisation, moyen, reference = "REF-1" });

    private static async Task<Guid> DeclarerOk(Acteur qui, Groupe t, Guid? membreId = null)
    {
        var r = await Declarer(qui, t, membreId);
        var corps = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, corps);
        return (Guid)JsonNode.Parse(corps)!["versementId"]!;
    }

    private static async Task<string> StatutMembre(Acteur lecteur, Groupe t, Guid membreId) =>
        (string)(await lecteur.Lire($"/api/v1/tontines/{t.Id}/details"))["membres"]!.AsArray()
            .First(m => (Guid)m!["id"]! == membreId)!["statut"]!;

    private static Task NommerTresorier(Groupe t, Acteur tresorier) =>
        t.Gestionnaire.Remplacer($"/api/v1/tontines/{t.Id}/gouvernance/tresorier", new { utilisateurId = tresorier.Id });

    // ── Declaration ──────────────────────────────────────────────────

    [Fact]
    public async Task UnMembre_DeclareSonPaiement_EnAttenteDeValidation()
    {
        var t = await TontineAsync();

        await DeclarerOk(t.Bruno, t);

        (await StatutMembre(t.Chloe, t, t.MembreBruno)).Should().Be("en_attente");
    }

    [Fact]
    public async Task UnSimpleMembre_NePeutPasDeclarerPourUnAutre()
    {
        var t = await TontineAsync();

        (await Declarer(t.Bruno, t, t.MembreChloe)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LeBureau_PeutDeclarerPourUnMembre()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Alice);

        await DeclarerOk(t.Alice, t, t.MembreBruno);   // trésorier
        await DeclarerOk(t.Gestionnaire, t, t.MembreChloe); // responsable
    }

    [Fact]
    public async Task DoubleDeclaration_EstRefuseeAvecUnMessageClair()
    {
        var t = await TontineAsync();
        await DeclarerOk(t.Bruno, t);

        var r = await Declarer(t.Bruno, t);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("déjà déclaré").And.Contain("Bruno Mbarga");
    }

    [Fact]
    public async Task LeBeneficiaireDuTour_NeDoitRien()
    {
        var t = await TontineAsync();

        (await Declarer(t.Alice, t)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SansMoyenNiReference_DeclarationRefusee()
    {
        var t = await TontineAsync();

        var r = await t.Bruno.Post("/api/v1/versements/manuel", new { tontineId = t.Id, tourId = t.TourId, montant = Cotisation });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Validation ───────────────────────────────────────────────────

    [Fact]
    public async Task SeulLeTresorierValide_MemeLeCreateurResponsableEstRefuse()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Alice);
        var v = await DeclarerOk(t.Bruno, t);

        (await t.Gestionnaire.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Chloe.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Bruno.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await t.Alice.Envoyer($"/api/v1/versements/{v}/valider");

        (await StatutMembre(t.Chloe, t, t.MembreBruno)).Should().Be("paye");
        (await t.Bruno.Lire($"/api/v1/versements/{v}/statut")).ToJsonString().Should().Contain("onfirm");
        var verif = await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/audit/verifier");
        ((bool)verif["estValide"]!).Should().BeTrue();
        ((int)verif["nombreEntrees"]!).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Rejet_ExigeUnMotif_EtSeulementParLeTresorier()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Alice);
        var v = await DeclarerOk(t.Bruno, t);

        (await t.Alice.Post($"/api/v1/versements/{v}/rejeter", new { raison = "  " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await t.Gestionnaire.Post($"/api/v1/versements/{v}/rejeter", new { raison = "pas reçu" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await t.Alice.Envoyer($"/api/v1/versements/{v}/rejeter", new { raison = "Argent non reçu" });

        var rejet = (await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/chronologie?categorie=paiements")).AsArray()
            .Single(e => (string)e!["action"]! == "VersementRejete")!;
        ((string)rejet["resume"]!).Should().Contain("Bruno Mbarga").And.Contain("rejeté (Argent non reçu)");
        ((string)rejet["acteurNom"]!).Should().Be("Alice Ngono");
        var liste = (await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/versements"))["items"]!.AsArray()
            .Single(x => (Guid)x!["id"]! == v)!;
        ((string)liste["motif"]!).Should().Be("Argent non reçu");
        ((string)liste["preuve"]!).Should().Contain("Espèces").And.Contain("REF-1");

        (await StatutMembre(t.Chloe, t, t.MembreBruno)).Should().NotBe("paye").And.NotBe("en_attente");
        // After a rejection the member can declare again.
        await DeclarerOk(t.Bruno, t);
    }

    // ── Transparency ─────────────────────────────────────────────────

    [Fact]
    public async Task ToutMembre_LitTousLesVersementsEtLeJournal()
    {
        var t = await TontineAsync();
        var v = await DeclarerOk(t.Bruno, t);

        var versements = await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/versements");
        versements.ToJsonString().Should().Contain(v.ToString()).And.Contain("Bruno Mbarga");

        var audit = await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/audit");
        ((int)audit["totalCount"]!).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UnNonMembre_NAccedeARien()
    {
        var t = await TontineAsync();
        var v = await DeclarerOk(t.Bruno, t);

        (await t.Etranger.Get($"/api/v1/tontines/{t.Id}/versements")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Etranger.Get($"/api/v1/tontines/{t.Id}/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Etranger.Get($"/api/v1/tontines/{t.Id}/details")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Declarer(t.Etranger, t)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await t.Etranger.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Historique_MontreDeclarationEtValidation_AvecNomsEtMontant()
    {
        var t = await TontineAsync();
        await NommerTresorier(t, t.Alice);
        var v = await DeclarerOk(t.Bruno, t);
        await t.Alice.Envoyer($"/api/v1/versements/{v}/valider");

        var evenements = (await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/chronologie?categorie=paiements")).AsArray();

        var declaration = evenements.Single(e => (string)e!["action"]! == "VersementManuel")!;
        ((string)declaration["resume"]!).Should().Contain("Bruno Mbarga").And.Contain("5 000 FCFA").And.Contain("déclaré");
        ((string)declaration["acteurNom"]!).Should().Be("Bruno Mbarga");
        ((Guid)declaration["participantId"]!).Should().Be(t.MembreBruno);

        var validation = evenements.Single(e => (string)e!["action"]! == "VersementConfirme")!;
        ((string)validation["resume"]!).Should().Contain("Bruno Mbarga").And.Contain("5 000 FCFA").And.Contain("validé");
        ((string)validation["acteurNom"]!).Should().Be("Alice Ngono");

        // A declared payment is described by its declaration only, never as a "created" technical event.
        evenements.Should().NotContain(e => (string)e!["action"]! == "VersementCree");

        // Free-text search reaches the payments too.
        (await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/chronologie?q=bruno")).AsArray().Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // ── Web dashboard: the creator (responsable + trésorier) keeps the existing flow ──

    [Fact]
    public async Task TableauDeBord_LeCreateurEncaissePuisValide_CommeAvant()
    {
        var t = await TontineAsync();

        // Same body as the dashboard's proxy: membreId + descriptionPreuve.
        var r = await t.Gestionnaire.Post("/api/v1/versements/manuel", new
        {
            tontineId = t.Id, tourId = t.TourId, membreId = t.MembreChloe, montant = Cotisation, descriptionPreuve = "Reçu n°7",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var v = (Guid)JsonNode.Parse(await r.Content.ReadAsStringAsync())!["versementId"]!;

        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider", new { reference = (string?)null });

        var tableau = await t.Gestionnaire.Lire($"/api/v1/dashboard/gestionnaire/tontine?tontineId={t.Id}");
        tableau["membres"]!.AsArray().First(m => (Guid)m!["id"]! == t.MembreChloe)!["statut"]!.GetValue<string>().Should().Be("paye");
        (await t.Gestionnaire.Lire($"/api/v1/dashboard/gestionnaire/versements?tontineId={t.Id}")).ToJsonString().Should().Contain("confirme");
        (await t.Gestionnaire.Lire($"/api/v1/dashboard/gestionnaire/audit?tontineId={t.Id}")).ToJsonString().Should().Contain("VersementConfirme");
    }
}
