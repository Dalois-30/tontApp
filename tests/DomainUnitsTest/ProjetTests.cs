using Domain.IdentityManagement.ValueObjects;
using Domain.ProjectManagement;
using Domain.TontineManagement.ValueObjects;

namespace DomainUnitsTest;

/// <summary>Business rules of the collective project (paliers, instalments, expenses).</summary>
public class ProjetTests
{
    private static readonly DateTime Debut = new(2026, 1, 5);
    private static readonly UtilisateurId Gestionnaire = UtilisateurId.Create();
    private const string G = "gestionnaire";

    /// <summary>Monthly project, 6 instalments from 5 Jan 2026, tiers 10/15/20k.</summary>
    private static (Projet Projet, string Code) Nouveau(ReglesProjet? regles = null)
    {
        var p = Projet.Creer("Moulin à grains", "Achat d'un moulin", 1_000_000m, [10_000m, 15_000m, 20_000m],
            TontinePeriodicity.Monthly, Debut, 6, Gestionnaire, regles);
        var code = p.GenererCodeInvitation(30, G);
        return (p, code);
    }

    private static (Projet, Guid) AvecParticipant(decimal palier = 15_000m, ReglesProjet? regles = null)
    {
        var (p, code) = Nouveau(regles);
        var part = p.Rejoindre(code, "Awa", UtilisateurId.Create(), palier, Debut.AddDays(-1));
        p.Ouvrir(G);
        return (p, part.Id);
    }

    private static void Payer(Projet p, Guid participant, decimal montant)
    {
        var paiement = p.EnregistrerPaiement(participant, montant, "reçu", G, Debut);
        p.DeciderPaiement(paiement.Id, true, G, "CASH-1", Debut);
    }

    [Fact]
    public void Creer_RejectsInvalidSetup()
    {
        Assert.Throws<ArgumentException>(() => Projet.Creer("X", null, 0, [10_000m], TontinePeriodicity.Monthly, Debut, 6, Gestionnaire));
        Assert.Throws<ArgumentException>(() => Projet.Creer("X", null, 100, [], TontinePeriodicity.Monthly, Debut, 6, Gestionnaire));
        Assert.Throws<ArgumentException>(() => Projet.Creer("X", null, 100, [10_000m], TontinePeriodicity.Monthly, Debut, 0, Gestionnaire));
    }

    [Fact]
    public void Echeancier_FollowsThePeriodicity()
    {
        var (p, _) = Nouveau();
        Assert.Equal(new DateTime(2026, 6, 5), p.Echeancier().Last());
        Assert.Equal(6, p.Echeancier().Count);
    }

    [Fact]
    public void Rejoindre_RequiresValidCodeAndOfferedTier()
    {
        var (p, code) = Nouveau();
        Assert.Throws<InvalidOperationException>(() => p.Rejoindre("BAD000", "A", UtilisateurId.Create(), 10_000m, Debut));
        Assert.Throws<InvalidOperationException>(() => p.Rejoindre(code, "A", UtilisateurId.Create(), 12_000m, Debut));
        var u = UtilisateurId.Create();
        p.Rejoindre(code, "A", u, 10_000m, Debut);
        Assert.Throws<InvalidOperationException>(() => p.Rejoindre(code, "A bis", u, 10_000m, Debut));
    }

    [Fact]
    public void PaymentBelowTier_IsPartial_AndTierDoesNotChange()
    {
        var (p, id) = AvecParticipant(15_000m);
        Payer(p, id, 10_000m);

        var s = p.Situation(id, Debut);

        var e1 = s.Echeances[0];
        Assert.Equal(StatutEcheance.Partielle, e1.Statut);
        Assert.Equal(5_000m, e1.Reste);
        Assert.Equal(15_000m, s.PalierActuel);
        Assert.Equal(5_000m, s.ResteDuExigible);
    }

    [Fact]
    public void Overpayment_IsCarriedForwardToNextInstalments()
    {
        var (p, id) = AvecParticipant(15_000m);
        Payer(p, id, 40_000m);

        var s = p.Situation(id, Debut);

        Assert.Equal(StatutEcheance.Soldee, s.Echeances[0].Statut);
        Assert.Equal(StatutEcheance.Soldee, s.Echeances[1].Statut);
        Assert.Equal(StatutEcheance.Partielle, s.Echeances[2].Statut);
        Assert.Equal(10_000m, s.Echeances[2].Paye);
        Assert.Equal(0m, s.ResteDuExigible);
        Assert.Equal(25_000m, s.Avance);
    }

    [Fact]
    public void UnpaidPastInstalment_IsLate()
    {
        var (p, id) = AvecParticipant(10_000m);
        var s = p.Situation(id, new DateTime(2026, 2, 10));
        Assert.Equal(StatutEcheance.EnRetard, s.Echeances[0].Statut);
        Assert.Equal(StatutEcheance.EnRetard, s.Echeances[1].Statut);
        Assert.Equal(StatutEcheance.AVenir, s.Echeances[2].Statut);
        Assert.Equal(20_000m, s.ResteDuExigible);
    }

    [Fact]
    public void RaisingTier_NonRetroactive_AppliesFromNextInstalment()
    {
        var (p, id) = AvecParticipant(10_000m, new ReglesProjet { HausseRetroactive = false });
        var applique = p.ChangerPalier(id, 20_000m, "awa", new DateTime(2026, 2, 10));

        Assert.True(applique);
        var s = p.Situation(id, new DateTime(2026, 2, 10));
        Assert.Equal(10_000m, s.Echeances[1].Palier); // 5 Feb: already due, unchanged
        Assert.Equal(20_000m, s.Echeances[2].Palier); // 5 Mar: new tier
        Assert.Equal(20_000m, s.PalierActuel);
    }

    [Fact]
    public void LoweringTier_NeedsApproval()
    {
        var (p, id) = AvecParticipant(20_000m);
        var applique = p.ChangerPalier(id, 10_000m, "awa", new DateTime(2026, 2, 10));

        Assert.False(applique);
        var avant = p.Situation(id, new DateTime(2026, 2, 10));
        Assert.Equal(20_000m, avant.PalierActuel);
        Assert.Equal(10_000m, avant.PalierDemande);

        p.DeciderChangementPalier(id, true, G, new DateTime(2026, 2, 12));
        var apres = p.Situation(id, new DateTime(2026, 2, 12));
        Assert.Equal(10_000m, apres.PalierActuel);
        Assert.Equal(20_000m, apres.Echeances[1].Palier);
        Assert.Null(apres.PalierDemande);
    }

    [Fact]
    public void LoweringTier_Refused_KeepsCommitment()
    {
        var (p, id) = AvecParticipant(20_000m);
        p.ChangerPalier(id, 15_000m, "awa", Debut);
        p.DeciderChangementPalier(id, false, G, Debut);

        Assert.Equal(20_000m, p.Situation(id, Debut).PalierActuel);
        Assert.Throws<InvalidOperationException>(() => p.DeciderChangementPalier(id, true, G, Debut));
    }

    [Fact]
    public void RejectedPayment_DoesNotCount()
    {
        var (p, id) = AvecParticipant(10_000m);
        var paiement = p.EnregistrerPaiement(id, 10_000m, "reçu", G, Debut);
        Assert.Throws<InvalidOperationException>(() => p.DeciderPaiement(paiement.Id, false, G, "", Debut));
        p.DeciderPaiement(paiement.Id, false, G, "Reçu illisible", Debut);

        Assert.Equal(0m, p.TotalCollecte);
        Assert.Equal(10_000m, p.Situation(id, Debut).ResteDuExigible);
    }

    [Fact]
    public void Expense_CannotExceedBalance()
    {
        var (p, id) = AvecParticipant(20_000m);
        Payer(p, id, 50_000m);
        var grosse = p.ProposerDepense("Moteur", 60_000m, "devis", G, Debut);
        var petite = p.ProposerDepense("Transport", 30_000m, "facture", G, Debut);

        Assert.Throws<InvalidOperationException>(() => p.DeciderDepense(grosse.Id, true, G, null, Debut));
        p.DeciderDepense(petite.Id, true, G, null, Debut);

        Assert.Equal(20_000m, p.Solde);
    }

    [Fact]
    public void LateParticipant_OwesInstalmentsFromJoiningDay()
    {
        var (p, code) = Nouveau();
        p.Ouvrir(G);
        var tard = p.Rejoindre(code, "Mariam", UtilisateurId.Create(), 10_000m, new DateTime(2026, 3, 1));

        var s = p.Situation(tard.Id, new DateTime(2026, 3, 1));

        Assert.Equal(4, s.Echeances.Count); // March to June
        Assert.Equal(3, s.Echeances[0].Numero);
        Assert.Equal(40_000m, s.EngagementTotal);
    }

    [Fact]
    public void Journal_IsChainedAndVerifiable()
    {
        var (p, id) = AvecParticipant();
        Payer(p, id, 15_000m);

        Assert.True(p.VerifierJournal());
        Assert.Equal(
            ["ProjetCree", "CodeInvitationGenere", "ParticipantAjoute", "ProjetOuvert", "PaiementSaisi", "PaiementValide"],
            p.Journal.OrderBy(e => e.Sequence).Select(e => e.Action).ToArray());
    }

    // ── Un changement de palier ne vaut qu'à partir de sa prise d'effet ──

    [Fact]
    public void Decrease_DoesNotRedistributeMoneyAlreadyReceived()
    {
        // Palier 20 000, 50 000 payés le 5 janvier : échéances 1 et 2 soldées, 3 entamée (10 000 / 20 000).
        var (p, id) = AvecParticipant(20_000m);
        Payer(p, id, 50_000m);

        // Baisse à 10 000 demandée et approuvée en février.
        p.ChangerPalier(id, 10_000m, "mariam", new DateTime(2026, 2, 10));
        p.DeciderChangementPalier(id, true, G, new DateTime(2026, 2, 12));

        var s = p.Situation(id, new DateTime(2026, 2, 12));

        Assert.Equal([20_000m, 20_000m, 20_000m, 10_000m, 10_000m, 10_000m], s.Echeances.Select(e => e.Palier));
        Assert.Equal(StatutEcheance.Partielle, s.Echeances[2].Statut); // still owes 10 000 on instalment 3
        Assert.Equal(10_000m, s.Echeances[2].Reste);
        Assert.Equal(0m, s.Echeances[3].Paye);                         // nothing spread to instalment 4
        Assert.Equal(10_000m, s.PalierActuel);
    }

    [Fact]
    public void PaymentAfterDecrease_SettlesTheOldGapFirst_ThenUsesTheNewTier()
    {
        var (p, id) = AvecParticipant(20_000m);
        Payer(p, id, 50_000m);
        p.ChangerPalier(id, 10_000m, "mariam", new DateTime(2026, 2, 10));
        p.DeciderChangementPalier(id, true, G, new DateTime(2026, 2, 12));

        var paiement = p.EnregistrerPaiement(id, 20_000m, "reçu", G, new DateTime(2026, 3, 1));
        p.DeciderPaiement(paiement.Id, true, G, "CASH-2", new DateTime(2026, 3, 1));

        var s = p.Situation(id, new DateTime(2026, 3, 1));
        Assert.Equal(StatutEcheance.Soldee, s.Echeances[2].Statut);   // gap of instalment 3 settled (20 000)
        Assert.Equal(10_000m, s.Echeances[3].Paye);                   // then 10 000 at the new tier
        Assert.Equal(StatutEcheance.Soldee, s.Echeances[3].Statut);
    }

    [Fact]
    public void Raise_NonRetroactive_DoesNotIncreaseInstalmentsAlreadyPaid()
    {
        var (p, id) = AvecParticipant(10_000m, new ReglesProjet { HausseRetroactive = false });
        Payer(p, id, 30_000m); // instalments 1-3 paid at 10 000 on 5 Jan

        p.ChangerPalier(id, 20_000m, "awa", new DateTime(2026, 1, 20)); // from 5 Feb

        var s = p.Situation(id, new DateTime(2026, 1, 20));
        Assert.Equal([10_000m, 10_000m, 10_000m, 20_000m, 20_000m, 20_000m], s.Echeances.Select(e => e.Palier));
        Assert.All(s.Echeances.Take(3), e => Assert.Equal(StatutEcheance.Soldee, e.Statut));
    }

    // ── Règles configurables ─────────────────────────────────────────

    [Fact]
    public void Raise_Retroactive_ByDefault_OpensGapsOnPastInstalments_FilledFirst()
    {
        var (p, id) = AvecParticipant(10_000m);
        Payer(p, id, 30_000m); // 1-3 paid at 10 000
        p.ChangerPalier(id, 15_000m, "awa", new DateTime(2026, 1, 20));

        var s = p.Situation(id, new DateTime(2026, 1, 20));
        Assert.All(s.Echeances, e => Assert.Equal(15_000m, e.Palier));
        Assert.Equal(StatutEcheance.Partielle, s.Echeances[0].Statut);
        Assert.Equal(5_000m, s.ResteDuExigible); // instalment 1 (5 Jan) now owes 5 000

        var pay = p.EnregistrerPaiement(id, 15_000m, "reçu", G, new DateTime(2026, 1, 21));
        p.DeciderPaiement(pay.Id, true, G, "CASH", new DateTime(2026, 1, 21));
        var apres = p.Situation(id, new DateTime(2026, 1, 21));
        Assert.All(apres.Echeances.Take(3), e => Assert.Equal(StatutEcheance.Soldee, e.Statut)); // gaps filled first
    }

    [Fact]
    public void Raise_CanRequireApproval()
    {
        var (p, id) = AvecParticipant(10_000m, new ReglesProjet { HausseSoumiseAValidation = true });
        Assert.False(p.ChangerPalier(id, 20_000m, "awa", Debut));
        Assert.Equal(10_000m, p.Situation(id, Debut).PalierActuel);
        p.DeciderChangementPalier(id, true, G, Debut);
        Assert.Equal(20_000m, p.Situation(id, Debut).PalierActuel);
    }

    [Fact]
    public void Decrease_CanBeForbidden_OrImmediate()
    {
        var (interdit, a) = AvecParticipant(20_000m, new ReglesProjet { BaisseAutorisee = false });
        Assert.Throws<InvalidOperationException>(() => interdit.ChangerPalier(a, 10_000m, "x", Debut));

        var (libre, b) = AvecParticipant(20_000m, new ReglesProjet { BaisseSoumiseAValidation = false });
        Assert.True(libre.ChangerPalier(b, 10_000m, "x", Debut));
    }

    [Fact]
    public void Decrease_Retroactive_RedistributesMoneyAlreadyReceived()
    {
        var (p, id) = AvecParticipant(20_000m, new ReglesProjet { BaisseRetroactive = true, BaisseSoumiseAValidation = false });
        Payer(p, id, 50_000m);
        p.ChangerPalier(id, 10_000m, "m", new DateTime(2026, 2, 10));

        var s = p.Situation(id, new DateTime(2026, 2, 10));
        Assert.All(s.Echeances, e => Assert.Equal(10_000m, e.Palier));
        Assert.Equal(5, s.Echeances.Count(e => e.Statut == StatutEcheance.Soldee));
    }

    [Fact]
    public void PartialPayment_CanBeForbidden()
    {
        var (p, id) = AvecParticipant(15_000m, new ReglesProjet { PaiementPartielAutorise = false });
        Assert.Throws<InvalidOperationException>(() => p.EnregistrerPaiement(id, 10_000m, "r", G, Debut));
        p.EnregistrerPaiement(id, 15_000m, "r", G, Debut);
    }

    [Fact]
    public void Overpayment_CanBeRefused()
    {
        var (p, id) = AvecParticipant(15_000m, new ReglesProjet { TropPercu = TraitementTropPercu.Refuser });
        // On 5 Jan: instalment 1 due (15 000); the next one (5 Feb) may be paid in advance.
        Assert.Throws<InvalidOperationException>(() => p.EnregistrerPaiement(id, 40_000m, "r", G, Debut));
        p.EnregistrerPaiement(id, 30_000m, "r", G, Debut);
    }

    [Fact]
    public void LateParticipant_CanOweEarlierInstalments()
    {
        var (p, code) = Nouveau(new ReglesProjet { RattrapageAdhesionTardive = true });
        p.Ouvrir(G);
        var tard = p.Rejoindre(code, "Mariam", UtilisateurId.Create(), 10_000m, new DateTime(2026, 3, 1));

        var s = p.Situation(tard.Id, new DateTime(2026, 3, 1));
        Assert.Equal(6, s.Echeances.Count);
        Assert.Equal(20_000m, s.ResteDuExigible); // January and February to catch up
    }

    [Fact]
    public void ModifierRegles_IsJournaled_AndForbiddenOnceClosed()
    {
        var (p, _) = AvecParticipant();
        p.ModifierRegles(new ReglesProjet { BaisseAutorisee = false }, G);
        Assert.False(p.Regles.BaisseAutorisee);
        Assert.Equal("ReglesModifiees", p.Journal.OrderBy(j => j.Sequence).Last().Action);
        p.Clore(G);
        Assert.Throws<InvalidOperationException>(() => p.ModifierRegles(ReglesProjet.ParDefaut, G));
    }

    // ── Participants sans compte et import ──────────────────────────

    [Fact]
    public void Gestionnaire_CanRegisterParticipantWithoutAccount()
    {
        var (p, _) = Nouveau();
        var x = p.AjouterParticipant("BEMO Grace", 15_000m, Debut, G);
        Assert.Null(x.UtilisateurId);
        Assert.Throws<InvalidOperationException>(() => p.AjouterParticipant("bemo grace", 15_000m, Debut, G));
        Assert.Equal("ParticipantInscrit", p.Journal.OrderBy(j => j.Sequence).Last().Action);
    }

    [Fact]
    public void ImportedPayments_AreValidatedAtTheirDate_AndAllocated()
    {
        var (p, _) = Nouveau();
        var x = p.AjouterParticipant("FOTSO Mathias", 15_000m, Debut, G);
        p.ImporterPaiement(x.Id, 15_000m, Debut.AddDays(6), "Cash", G);
        p.ImporterPaiement(x.Id, 45_000m, Debut.AddDays(70), "Cash", G); // catches up instalments 2 and 3, + 1 in advance

        var s = p.Situation(x.Id, Debut.AddDays(70));
        Assert.Equal(60_000m, s.TotalPaye);
        Assert.Equal(0m, s.ResteDuExigible);
        Assert.Equal(15_000m, s.Avance);
        Assert.All(p.Paiements, y => Assert.Equal(StatutOperation.Validee, y.Statut));
        Assert.Throws<InvalidOperationException>(() => p.ImporterPaiement(x.Id, 5_000m, Debut.AddDays(-30), "Cash", G));
        Assert.True(p.VerifierJournal());
    }
}
