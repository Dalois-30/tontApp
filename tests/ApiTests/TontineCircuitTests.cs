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

    // ── Partial payments: validated, visible as partial, the rest stays due ──

    private static Task<HttpResponseMessage> DeclarerMontant(Acteur qui, Groupe t, decimal montant, Guid? tourId = null) =>
        qui.Post("/api/v1/versements/manuel", new { tontineId = t.Id, tourId = tourId ?? t.TourId, montant, moyen = "Espèces", reference = "REF-P" });

    private static async Task<Guid> DeclarerMontantOk(Acteur qui, Groupe t, decimal montant, Guid? tourId = null)
    {
        var r = await DeclarerMontant(qui, t, montant, tourId);
        var corps = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, corps);
        return (Guid)JsonNode.Parse(corps)!["versementId"]!;
    }

    private static async Task<JsonNode> Membre(Acteur lecteur, Groupe t, Guid membreId) =>
        (await lecteur.Lire($"/api/v1/tontines/{t.Id}/details"))["membres"]!.AsArray()
            .First(m => (Guid)m!["id"]! == membreId)!;

    [Fact]
    public async Task PartielValide_PayePartiellement_AvecLeReste_SurMobileEtWeb()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("paye_partiel");
        ((decimal)bruno["resteTour"]!).Should().Be(3000m);
        ((decimal)bruno["enAttenteTour"]!).Should().Be(0m);

        var tableau = await t.Gestionnaire.Lire($"/api/v1/dashboard/gestionnaire/tontine?tontineId={t.Id}");
        var web = tableau["membres"]!.AsArray().First(m => (Guid)m!["id"]! == t.MembreBruno)!;
        ((string)web["statut"]!).Should().Be("paye_partiel");
        ((decimal)web["resteTour"]!).Should().Be(3000m);
    }

    [Fact]
    public async Task Complement_AccepteSansDoublon_PuisValide_PasseAPaye_AvecLesDeuxVersements()
    {
        var t = await TontineAsync();
        var v1 = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v1}/valider");

        var v2 = await DeclarerMontantOk(t.Bruno, t, 3000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v2}/valider");

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("paye");
        ((decimal)bruno["resteTour"]!).Should().Be(0m);

        var siens = (await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/versements"))["items"]!.AsArray()
            .Where(x => (Guid)x!["payeurId"]! == t.MembreBruno).ToList();
        siens.Select(x => (Guid)x!["id"]!).Should().BeEquivalentTo([v1, v2]);
        siens.Should().OnlyContain(x => (string)x!["status"]! == "confirme");
    }

    [Fact]
    public async Task PartielPlusAttente_PayePartiel_AValider_EtNouvelleDeclarationRefusee()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");
        await DeclarerMontantOk(t.Bruno, t, 3000m);

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("paye_partiel");
        ((decimal)bruno["enAttenteTour"]!).Should().Be(3000m);
        ((decimal)bruno["resteTour"]!).Should().Be(0m);

        var r = await DeclarerMontant(t.Bruno, t, 500m);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("couverte").And.Contain("Bruno Mbarga");
    }

    [Fact]
    public async Task Complet_Paye_EtToutNouveauVersementRefuse()
    {
        var t = await TontineAsync();
        var v = await DeclarerOk(t.Bruno, t);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("paye");
        ((decimal)bruno["resteTour"]!).Should().Be(0m);

        var r = await DeclarerMontant(t.Bruno, t, 1000m);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("entièrement payée");
    }

    [Fact]
    public async Task ArrierePartiel_ResteDansLesImpayes_EtLeResteSePaie()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");
        (await t.Gestionnaire.Post($"/api/v1/tontines/{t.Id}/rounds/{t.TourId}/close")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        bruno["toursImpayes"]!.AsArray().Select(x => (int)x!).Should().Contain(1);
        ((decimal)bruno["resteDu"]!).Should().Be(3000m);

        // The rest of round 1 can still be declared (catch-up), then the arrears are cleared.
        var v2 = await DeclarerMontantOk(t.Bruno, t, 3000m, t.TourId);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v2}/valider");
        bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((decimal)bruno["resteDu"]!).Should().Be(0m);
        bruno["toursImpayes"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task AttentePartielle_EnAttente_AvecLeReste_EtLeResteSeDeclare()
    {
        var t = await TontineAsync();
        await DeclarerMontantOk(t.Bruno, t, 2000m);

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("en_attente");
        ((decimal)bruno["enAttenteTour"]!).Should().Be(2000m);
        ((decimal)bruno["resteTour"]!).Should().Be(3000m);

        await DeclarerMontantOk(t.Bruno, t, 3000m);
        ((decimal)(await Membre(t.Chloe, t, t.MembreBruno))["resteTour"]!).Should().Be(0m);
    }

    [Fact]
    public async Task PetitReste_50Fcfa_ApresPartielValide_SeDeclareEtSeValide()
    {
        var t = await TontineAsync();
        var v1 = await DeclarerMontantOk(t.Bruno, t, 4950m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v1}/valider");
        ((decimal)(await Membre(t.Chloe, t, t.MembreBruno))["resteTour"]!).Should().Be(50m);

        var v2 = await DeclarerMontantOk(t.Bruno, t, 50m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v2}/valider");

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("paye");
        ((decimal)bruno["resteTour"]!).Should().Be(0m);
    }

    [Fact]
    public async Task ComplementRejete_RevientAPayePartiel_AvecToutLeResteDu()
    {
        var t = await TontineAsync();
        var v1 = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v1}/valider");
        var v2 = await DeclarerMontantOk(t.Bruno, t, 3000m);

        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v2}/rejeter", new { raison = "Argent non reçu" });

        var bruno = await Membre(t.Chloe, t, t.MembreBruno);
        ((string)bruno["statut"]!).Should().Be("paye_partiel");
        ((decimal)bruno["resteTour"]!).Should().Be(3000m);
        ((decimal)bruno["enAttenteTour"]!).Should().Be(0m);
    }

    // ── Surplus carried over: one rule, same situation on every screen ──

    private static async Task<Guid> CloturerTour1(Groupe t)
    {
        (await t.Gestionnaire.Post($"/api/v1/tontines/{t.Id}/rounds/{t.TourId}/close")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var d = await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/details");
        return (Guid)d["tours"]!.AsArray().First(x => (string)x!["status"]! == "Ouvert")!["id"]!;
    }

    private static async Task<string> StatutTourActuel(Acteur lecteur, Groupe t, string nom) =>
        (string)(await lecteur.Lire($"/api/v1/tontines/{t.Id}/tours/actuel"))["payeurs"]!.AsArray()
            .First(p => (string)p!["nom"]! == nom)!["statut"]!;

    private static async Task<IEnumerable<Guid>> Retardataires(Groupe t, Guid tourId) =>
        (await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/tours/{tourId}/retardataires")).AsArray()
            .Select(r => (Guid)r!["membreId"]!);

    [Fact]
    public async Task SurplusVersArriere_EnRetardDUnTour_PaieDouble_AJourPartout()
    {
        var t = await TontineAsync();
        var tour2 = await CloturerTour1(t); // Chloé paid nothing on round 1; round 2 is Bruno's
        var v = await DeclarerMontantOk(t.Chloe, t, 2 * Cotisation, tour2);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        var chloe = await Membre(t.Alice, t, t.MembreChloe);
        ((string)chloe["statut"]!).Should().Be("paye");
        ((decimal)chloe["resteDu"]!).Should().Be(0m);
        ((decimal)chloe["resteTour"]!).Should().Be(0m);
        chloe["toursImpayes"]!.AsArray().Should().BeEmpty();

        var web = (await t.Gestionnaire.Lire($"/api/v1/dashboard/gestionnaire/tontine?tontineId={t.Id}"))["membres"]!.AsArray()
            .First(m => (Guid)m!["id"]! == t.MembreChloe)!;
        ((string)web["statut"]!).Should().Be("paye");
        ((decimal)web["resteDu"]!).Should().Be(0m);

        (await StatutTourActuel(t.Alice, t, "Chloé Fotso")).Should().Be("Payé");
        (await Retardataires(t, t.TourId)).Should().NotContain(t.MembreChloe);
        (await Retardataires(t, tour2)).Should().NotContain(t.MembreChloe);
    }

    [Fact]
    public async Task SurplusEnAvance_PayeAvecAvance_PuisTourSuivantCouvert_EtDeclarationRefusee()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Chloe, t, 2 * Cotisation);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        var chloe = await Membre(t.Bruno, t, t.MembreChloe);
        ((string)chloe["statut"]!).Should().Be("paye");
        ((decimal)chloe["avance"]!).Should().Be(Cotisation);

        var tour2 = await CloturerTour1(t);
        chloe = await Membre(t.Bruno, t, t.MembreChloe);
        ((string)chloe["statut"]!).Should().Be("paye");
        ((decimal)chloe["avance"]!).Should().Be(0m);
        ((decimal)chloe["resteTour"]!).Should().Be(0m);
        (await StatutTourActuel(t.Bruno, t, "Chloé Fotso")).Should().Be("Payé");

        var r = await DeclarerMontant(t.Chloe, t, 1000m, tour2);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("surplus").And.Contain("Chloé Fotso");
    }

    [Fact]
    public async Task DeclarationAuDelaDuReste_Acceptee()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        (await DeclarerMontant(t.Bruno, t, 10000m)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TourActuel_PartielNonCompteCommePaye()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Bruno, t, 2000m);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");

        var actuel = await t.Chloe.Lire($"/api/v1/tontines/{t.Id}/tours/actuel");
        ((int)actuel["nombrePayes"]!).Should().Be(0);
        ((decimal)actuel["montantCollecte"]!).Should().Be(2000m);
        (await StatutTourActuel(t.Chloe, t, "Bruno Mbarga")).Should().Be("Payé partiellement");
    }

    [Fact]
    public async Task SurplusRejete_AucunReport()
    {
        var t = await TontineAsync();
        var tour2 = await CloturerTour1(t);
        var v = await DeclarerMontantOk(t.Chloe, t, 2 * Cotisation, tour2);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/rejeter", new { raison = "Argent non reçu" });

        var chloe = await Membre(t.Alice, t, t.MembreChloe);
        ((decimal)chloe["resteDu"]!).Should().Be(Cotisation);
        ((decimal)chloe["resteTour"]!).Should().Be(Cotisation);
        ((decimal)chloe["avance"]!).Should().Be(0m);
        (await Retardataires(t, t.TourId)).Should().Contain(t.MembreChloe);
    }

    /// <summary>A late member, accepted by the responsable; returns their account and member id.</summary>
    private async Task<(Acteur Compte, Guid Membre)> AdherentTardif(Groupe t, string nom)
    {
        var dan = await api.CompteAsync(nom);
        var code = (string)(await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/invitation/generer?nombreUsagesMax=5"))["code"]!;
        await dan.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = dan.Nom });
        var id = (Guid)(await t.Gestionnaire.Lire($"/api/v1/tontines/{t.Id}/details"))["membres"]!.AsArray()
            .First(m => (Guid?)m!["utilisateurId"] == dan.Id)!["id"]!;
        (await t.Gestionnaire.Post($"/api/v1/tontines/{t.Id}/membres/{id}/accepter")).StatusCode.Should().Be(HttpStatusCode.OK);
        return (dan, id);
    }

    [Fact]
    public async Task TourActuel_Contrat_StatutsEtTotaux_AvecReport()
    {
        var t = await TontineAsync();
        var v = await DeclarerMontantOk(t.Chloe, t, 2 * Cotisation); // 5 000 carried to round 2
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{v}/valider");
        var tour2 = await CloturerTour1(t); // round 2: Bruno is the beneficiary
        var (dan, _) = await AdherentTardif(t, "Dan Tchoua"); // owes rounds 1 and 2, pays nothing
        var a = await DeclarerMontantOk(t.Alice, t, 2000m, tour2);
        await t.Gestionnaire.Envoyer($"/api/v1/versements/{a}/valider");

        var actuel = await t.Alice.Lire($"/api/v1/tontines/{t.Id}/tours/actuel");
        var statuts = actuel["payeurs"]!.AsArray().ToDictionary(p => (string)p!["nom"]!, p => (string)p!["statut"]!);
        statuts["Chloé Fotso"].Should().Be("Payé");
        statuts["Alice Ngono"].Should().Be("Payé partiellement");
        statuts[dan.Nom].Should().Be("En attente");
        statuts["Bruno Mbarga"].Should().Be("Bénéficiaire");
        ((int)actuel["nombreAttendus"]!).Should().Be(3);
        ((int)actuel["nombrePayes"]!).Should().Be(1);
        ((decimal)actuel["montantAttendu"]!).Should().Be(3 * Cotisation);
        ((decimal)actuel["montantCollecte"]!).Should().Be(Cotisation + 2000m);

        // Same totals in the tontine details (web and mobile).
        var tour = (await t.Alice.Lire($"/api/v1/tontines/{t.Id}/details"))["tours"]!.AsArray()
            .First(x => (Guid)x!["id"]! == tour2)!;
        ((decimal)tour["montantCollecte"]!).Should().Be(Cotisation + 2000m);
        ((decimal)tour["montantAttendu"]!).Should().Be(3 * Cotisation);
        ((int)tour["nombreAttendus"]!).Should().Be(3);
        ((int)tour["nombrePayes"]!).Should().Be(1);
        var chloe = await Membre(t.Alice, t, t.MembreChloe);
        ((decimal)chloe["reporteTour"]!).Should().Be(Cotisation);
    }

    [Fact]
    public async Task AdherentTardifSansRattrapage_TourAnterieurRefuse_RetardSurLeTourOuvertSeulement()
    {
        var t = await TontineAsync();
        var tour2 = await CloturerTour1(t);
        (await t.Gestionnaire.Put($"/api/v1/tontines/{t.Id}/regles", new { adhesionEnCours = "AvecValidation", rattrapageAdhesionTardive = false }))
            .IsSuccessStatusCode.Should().BeTrue();
        var (dan, membreDan) = await AdherentTardif(t, "Dan Tchoua");

        var r = await DeclarerMontant(dan, t, Cotisation, t.TourId);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("ne doit pas de cotisation sur le tour 1");

        (await Retardataires(t, t.TourId)).Should().NotContain(membreDan);
        (await Retardataires(t, tour2)).Should().Contain(membreDan);
        var d = await Membre(t.Alice, t, membreDan);
        ((decimal)d["resteDu"]!).Should().Be(0m);
    }

    [Fact]
    public async Task DeclarationSurSonPropreTour_Beneficiaire_RefuseeAvecUnMessageClair()
    {
        var t = await TontineAsync();

        var r = await DeclarerMontant(t.Alice, t, Cotisation);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("bénéficiaire du tour 1");
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
