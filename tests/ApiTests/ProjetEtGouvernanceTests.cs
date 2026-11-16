namespace ApiTests;

using System.Net;
using Domain.IdentityManagement.ValueObjects;
using FluentAssertions;

/// <summary>
/// Collective project payment circuit and group governance (bureau, votes, séances, history), end to
/// end over HTTP.
/// </summary>
[Collection(ApiCollection.Nom)]
public sealed class ProjetEtGouvernanceTests(ApiFactory api)
{
    // ── Collective project ───────────────────────────────────────────

    private sealed record Projet(Guid Id, Acteur Gestionnaire, Acteur Alice, Acteur Bruno, Acteur Etranger, Guid PartAlice, Guid PartBruno);

    private async Task<Projet> ProjetAsync()
    {
        var g = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var a = await api.CompteAsync("Alice Ngono");
        var b = await api.CompteAsync("Bruno Mbarga");
        var x = await api.CompteAsync("Xavier Etranger");

        var id = (Guid)(await g.Envoyer("/api/v1/projets", new
        {
            nom = "Salle des fêtes", description = "Tests", objectif = 1_000_000m, paliers = new[] { 10_000m, 15_000m },
            periodicite = "Monthly", // Date only, as the clients send it.
            dateDebut = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM-dd"), nombreEcheances = 10,
        }))["id"]!;
        await g.Envoyer($"/api/v1/projets/{id}/ouvrir");
        var code = (string)(await g.Envoyer($"/api/v1/projets/{id}/invitation"))["code"]!;
        await a.Envoyer("/api/v1/projets/rejoindre", new { code, nom = a.Nom, palier = 10_000m });
        await b.Envoyer("/api/v1/projets/rejoindre", new { code, nom = b.Nom, palier = 15_000m });

        var d = await g.Lire($"/api/v1/projets/{id}");
        Guid Part(Acteur u) => (Guid)d["participants"]!.AsArray().First(p => (Guid?)p!["utilisateurId"] == u.Id)!["id"]!;
        return new Projet(id, g, a, b, x, Part(a), Part(b));
    }

    [Fact]
    public async Task Projet_MembreDeclare_TresoriereValide_ToutLeMondeVoit()
    {
        var p = await ProjetAsync();
        await p.Gestionnaire.Remplacer($"/api/v1/projets/{p.Id}/gouvernance/tresorier", new { utilisateurId = p.Alice.Id });

        // Bruno declares his own payment; he cannot declare for Alice.
        var apres = await p.Bruno.Envoyer($"/api/v1/projets/{p.Id}/paiements", new { montant = 15_000m, moyen = "Orange Money", preuve = "OM-42" });
        var paiement = (Guid)apres["paiements"]!.AsArray().Single()!["id"]!;
        (await p.Bruno.Post($"/api/v1/projets/{p.Id}/paiements", new { participantId = p.PartAlice, montant = 10_000m, moyen = "Espèces" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Only the treasurer validates: the creator (still responsable) and Bruno are refused.
        (await p.Gestionnaire.Post($"/api/v1/projets/{p.Id}/paiements/{paiement}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Bruno.Post($"/api/v1/projets/{p.Id}/paiements/{paiement}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await p.Alice.Envoyer($"/api/v1/projets/{p.Id}/paiements/{paiement}/valider");

        // Transparency: Alice (another member) reads Bruno's validated payment and who did what.
        var vu = await p.Alice.Lire($"/api/v1/projets/{p.Id}");
        var ligne = vu["paiements"]!.AsArray().Single()!;
        ((string)ligne["statut"]!).Should().Be("valide");
        ((string)ligne["saisiParNom"]!).Should().Be("Bruno Mbarga");
        ((string)ligne["decideParNom"]!).Should().Be("Alice Ngono");
        ((decimal)vu["totalCollecte"]!).Should().Be(15_000m);

        (await p.Etranger.Get($"/api/v1/projets/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Projet_RejetSansMotif_Refuse()
    {
        var p = await ProjetAsync();
        var apres = await p.Bruno.Envoyer($"/api/v1/projets/{p.Id}/paiements", new { montant = 15_000m, moyen = "Espèces" });
        var paiement = (Guid)apres["paiements"]!.AsArray().Single()!["id"]!;

        (await p.Gestionnaire.Post($"/api/v1/projets/{p.Id}/paiements/{paiement}/rejeter", new { motif = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await p.Gestionnaire.Envoyer($"/api/v1/projets/{p.Id}/paiements/{paiement}/rejeter", new { motif = "Pas reçu" });
    }

    // ── Governance of a tontine ──────────────────────────────────────

    /// <summary>Draft tontine created by Gaston with Alice, Bruno and Chloé as members.</summary>
    private async Task<(Guid Id, Acteur G, Acteur A, Acteur B, Acteur C)> TontineBrouillonAsync()
    {
        var g = await api.CompteAsync("Gaston Responsable", RoleUtilisateur.Gestionnaire);
        var a = await api.CompteAsync("Alice Ngono");
        var b = await api.CompteAsync("Bruno Mbarga");
        var c = await api.CompteAsync("Chloé Fotso");
        var id = (Guid)(await g.Envoyer("/api/v1/tontines", new
        {
            name = "Tontine des voisins", description = "Tests", contributionAmount = 5000m, periodicity = "Monthly", maxMembers = 6,
        }))["id"]!;
        var code = (string)(await g.Lire($"/api/v1/tontines/{id}/invitation/generer?nombreUsagesMax=10"))["code"]!;
        foreach (var m in new[] { a, b, c })
            await m.Envoyer("/api/v1/tontines/rejoindre", new { code, memberName = m.Nom });
        return (id, g, a, b, c);
    }

    [Fact]
    public async Task VoteAdopte_LeNouveauResponsableGere_LAncienEstRefuse()
    {
        var (id, g, a, b, c) = await TontineBrouillonAsync();
        var gouv = $"/api/v1/tontines/{id}";

        // Bruno proposes Chloé; Alice, Bruno and Chloé vote yes (3 of 4 electors with Gaston): adopted.
        var etat = await b.Envoyer($"{gouv}/votes/responsable", new { utilisateurId = c.Id });
        var vote = (Guid)etat["votes"]!.AsArray().Single()!["id"]!;
        foreach (var m in new[] { a, b, c })
            etat = await m.Envoyer($"{gouv}/votes/{vote}", new { pour = true });
        ((string)etat["votes"]!.AsArray().Single()!["statut"]!).Should().Be("Adopte");
        ((Guid)etat["bureau"]!["responsableId"]!).Should().Be(c.Id);

        // Activation: the former responsable is refused, the new one (a plain "Membre" account) succeeds.
        (await g.Post($"{gouv}/activate")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.Post($"{gouv}/activate")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Reminders: same rule.
        var tour = (Guid)(await c.Lire($"{gouv}/details"))["tours"]!.AsArray().Single()!["id"]!;
        (await g.Post($"{gouv}/tours/{tour}/relancer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.Post($"{gouv}/tours/{tour}/relancer")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The creator is still the treasurer: he still validates payments.
        var d = await c.Lire($"{gouv}/details");
        var beneficiaire = (string)d["tours"]!.AsArray().Single()!["beneficiaire"]!["nom"]!;
        var payeur = beneficiaire == b.Nom ? a : b;
        var r = await payeur.Post("/api/v1/versements/manuel", new { tontineId = id, tourId = tour, montant = 5000m, moyen = "Espèces" });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var v = (Guid)System.Text.Json.Nodes.JsonNode.Parse(await r.Content.ReadAsStringAsync())!["versementId"]!;
        (await c.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await g.Post($"/api/v1/versements/{v}/valider")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Running the tontine follows the bureau, not the platform role: the new responsable (a plain
        // "Membre" account) succeeds; the former responsable and a plain member are refused.
        foreach (var refuse in new[] { g, a })
        {
            (await refuse.Post($"{gouv}/messages", new { message = "Réunion samedi" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await refuse.Post($"{gouv}/rounds/open")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await refuse.Post($"{gouv}/rounds/{tour}/close")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await refuse.Put($"{gouv}/membres/{MembreDe(d, b)}/suspendre", new { motif = "Absences" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        (await c.Post($"{gouv}/messages", new { message = "Réunion samedi" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // A round is already open: the access gate is passed and the business rule answers.
        var ouverture = await c.Post($"{gouv}/rounds/open");
        ouverture.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ouverture.Content.ReadAsStringAsync()).Should().Contain("already open");
        (await c.Post($"{gouv}/rounds/{tour}/close")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.Post($"{gouv}/rounds/open")).StatusCode.Should().Be(HttpStatusCode.BadRequest); // round 2 opened by the close
        (await c.Put($"{gouv}/membres/{MembreDe(d, a)}/suspendre", new { motif = "Absences" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static Guid MembreDe(System.Text.Json.Nodes.JsonNode details, Acteur u) =>
        (Guid)details["membres"]!.AsArray().First(m => (Guid?)m!["utilisateurId"] == u.Id)!["id"]!;

    [Fact]
    public async Task SeulLeResponsableNommeLeTresorier()
    {
        var (id, g, a, b, _) = await TontineBrouillonAsync();

        (await b.Put($"/api/v1/tontines/{id}/gouvernance/tresorier", new { utilisateurId = b.Id })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var etat = await g.Remplacer($"/api/v1/tontines/{id}/gouvernance/tresorier", new { utilisateurId = a.Id });

        ((Guid)etat["bureau"]!["tresorierId"]!).Should().Be(a.Id);
        (await a.Lire($"/api/v1/tontines/{id}/gouvernance"))["mesRoles"]!.AsArray().Select(x => (string)x!).Should().Contain("tresorier");
    }

    [Fact]
    public async Task Seance_RedigeeParUnMembre_RetrouveeDansLHistorique()
    {
        var (id, _, a, b, _) = await TontineBrouillonAsync();

        await b.Envoyer($"/api/v1/tontines/{id}/seances", new
        {
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"), titre = "Assemblée de rentrée", lieu = "Chez Alice", presents = new[] { "Alice", "Bruno" },
            ordreDuJour = "Calendrier", compteRendu = "Le calendrier des tours est adopté.", decisions = new[] { "Cotisation le 5 du mois" },
        });

        var trouve = (await a.Lire($"/api/v1/tontines/{id}/chronologie?q=rentree")).AsArray();
        trouve.Should().ContainSingle();
        ((string)trouve[0]!["resume"]!).Should().Contain("Assemblée de rentrée").And.Contain("Cotisation le 5 du mois");
        ((string)trouve[0]!["categorie"]!).Should().Be("seances");
        ((string)trouve[0]!["acteurNom"]!).Should().Be("Bruno Mbarga");
    }

    [Fact]
    public async Task Seance_DateEnvoyeeEnUtcAvecHeure_EstAcceptee()
    {
        var (id, _, a, _, _) = await TontineBrouillonAsync();

        // Clients may send an ISO instant ("...Z"): it is a calendar date, stored without time zone.
        var g = await a.Envoyer($"/api/v1/tontines/{id}/seances", new
        {
            date = DateTime.UtcNow.AddDays(-1).ToString("O"), titre = "Réunion de bureau", compteRendu = "Point sur les retards.",
        });

        ((string)g["seances"]![0]!["titre"]!).Should().Be("Réunion de bureau");
    }
}
