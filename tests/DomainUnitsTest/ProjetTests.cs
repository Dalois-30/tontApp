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
    private static (Projet Projet, string Code) Nouveau()
    {
        var p = Projet.Creer("Moulin à grains", "Achat d'un moulin", 1_000_000m, [10_000m, 15_000m, 20_000m],
            TontinePeriodicity.Monthly, Debut, 6, Gestionnaire);
        var code = p.GenererCodeInvitation(30, G);
        return (p, code);
    }

    private static (Projet, Guid) AvecParticipant(decimal palier = 15_000m)
    {
        var (p, code) = Nouveau();
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
    public void RaisingTier_AppliesFromNextInstalment()
    {
        var (p, id) = AvecParticipant(10_000m);
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
        Assert.Equal(10_000m, avant.BaisseDemandee);

        p.DeciderBaissePalier(id, true, G, new DateTime(2026, 2, 12));
        var apres = p.Situation(id, new DateTime(2026, 2, 12));
        Assert.Equal(10_000m, apres.PalierActuel);
        Assert.Equal(20_000m, apres.Echeances[1].Palier);
        Assert.Null(apres.BaisseDemandee);
    }

    [Fact]
    public void LoweringTier_Refused_KeepsCommitment()
    {
        var (p, id) = AvecParticipant(20_000m);
        p.ChangerPalier(id, 15_000m, "awa", Debut);
        p.DeciderBaissePalier(id, false, G, Debut);

        Assert.Equal(20_000m, p.Situation(id, Debut).PalierActuel);
        Assert.Throws<InvalidOperationException>(() => p.DeciderBaissePalier(id, true, G, Debut));
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
        p.DeciderBaissePalier(id, true, G, new DateTime(2026, 2, 12));

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
        p.DeciderBaissePalier(id, true, G, new DateTime(2026, 2, 12));

        var paiement = p.EnregistrerPaiement(id, 20_000m, "reçu", G, new DateTime(2026, 3, 1));
        p.DeciderPaiement(paiement.Id, true, G, "CASH-2", new DateTime(2026, 3, 1));

        var s = p.Situation(id, new DateTime(2026, 3, 1));
        Assert.Equal(StatutEcheance.Soldee, s.Echeances[2].Statut);   // gap of instalment 3 settled (20 000)
        Assert.Equal(10_000m, s.Echeances[3].Paye);                   // then 10 000 at the new tier
        Assert.Equal(StatutEcheance.Soldee, s.Echeances[3].Statut);
    }

    [Fact]
    public void Raise_DoesNotIncreaseInstalmentsAlreadyPaid()
    {
        var (p, id) = AvecParticipant(10_000m);
        Payer(p, id, 30_000m); // instalments 1-3 paid at 10 000 on 5 Jan

        p.ChangerPalier(id, 20_000m, "awa", new DateTime(2026, 1, 20)); // from 5 Feb

        var s = p.Situation(id, new DateTime(2026, 1, 20));
        Assert.Equal([10_000m, 10_000m, 10_000m, 20_000m, 20_000m, 20_000m], s.Echeances.Select(e => e.Palier));
        Assert.All(s.Echeances.Take(3), e => Assert.Equal(StatutEcheance.Soldee, e.Statut));
    }
}
