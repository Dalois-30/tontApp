using Domain.IdentityManagement.ValueObjects;
using Domain.TontineManagement;
using Domain.TontineManagement.Entities;
using Domain.TontineManagement.ValueObjects;

namespace DomainUnitsTest;

/// <summary>
/// The single contribution rule of a tontine: validated money beyond a round's contribution is carried over
/// to the arrears (oldest first), then the open round, then kept as an advance. Contribution: 5 000.
/// </summary>
public class SituationCotisationsTests
{
    private readonly Tontine _tontine;
    private readonly Member _dan;

    public SituationCotisationsTests()
    {
        _tontine = Tontine.Create("Épargne", null, ContributionAmount.Create(5000, "XOF"),
            TontinePeriodicity.Monthly, 5, UtilisateurId.Create());
        foreach (var n in new[] { "Alice", "Bob", "Carol", "Dan" })
            _tontine.AddMember(n);
        _tontine.Activate(); // round 1, beneficiary Alice
        _dan = _tontine.Members.Single(m => m.Name == "Dan"); // Dan, beneficiary of round 4, owes rounds 1–3
    }

    private Round Tour(int numero) => _tontine.Rounds.Single(r => r.RoundNumber == numero);
    private void Cloturer() => _tontine.CloseRound(_tontine.Rounds.Single(r => !r.IsCompleted).Id);
    private static VersementCotisation V(Round tour, decimal montant, bool valide = true) => new(tour.Id.Value, montant, valide);

    private SituationCotisations Situation(params VersementCotisation[] versements) => _tontine.SituationDe(_dan.Id, versements);

    [Fact]
    public void SurplusVersArriere_LesDeuxToursSoldes()
    {
        Cloturer(); // round 2 open, round 1 unpaid
        var s = Situation(V(Tour(2), 10000));

        Assert.All(s.Tours, t => { Assert.True(t.Paye); Assert.Equal(0, t.Reste); });
        Assert.Equal(0, s.Avance);
    }

    [Fact]
    public void SurplusEnAvance_PayeEtAvance_PuisLeTourSuivantEstDejaCouvert()
    {
        var s = Situation(V(Tour(1), 8000));

        Assert.True(s.Tour(Tour(1).Id.Value)!.Paye);
        Assert.Equal(3000, s.Avance);

        Cloturer();
        var apres = Situation(V(Tour(1), 8000));
        var t2 = apres.Tour(Tour(2).Id.Value)!;
        Assert.Equal(3000, t2.Valide);
        Assert.Equal(2000, t2.Reste);
        Assert.Equal(0, apres.Avance);
    }

    [Fact]
    public void TourCouvertParReport_PayeEtCouvert()
    {
        Cloturer();
        var s = Situation(V(Tour(1), 10000));

        var t2 = s.Tour(Tour(2).Id.Value)!;
        Assert.True(t2.Paye);
        Assert.True(t2.Couvert);
        Assert.Equal(5000, t2.Reporte);
    }

    [Fact]
    public void Partiel_ResteDu_SansReport()
    {
        var t1 = Situation(V(Tour(1), 2000)).Tour(Tour(1).Id.Value)!;

        Assert.False(t1.Paye);
        Assert.Equal(2000, t1.Valide);
        Assert.Equal(3000, t1.Reste);
    }

    [Fact]
    public void EnAttente_NestJamaisReporte_EtResteSurSonTour()
    {
        Cloturer();
        var s = Situation(V(Tour(2), 10000, valide: false));

        Assert.Equal(5000, s.Tour(Tour(1).Id.Value)!.Reste);
        var t2 = s.Tour(Tour(2).Id.Value)!;
        Assert.Equal(0, t2.Valide);
        Assert.Equal(10000, t2.EnAttente);
        Assert.Equal(0, t2.Reste);
        Assert.Equal(0, s.Avance);
    }

    [Fact]
    public void Report_ArrieresDuPlusAncienAuPlusRecent_PuisTourOuvert()
    {
        Cloturer();
        Cloturer(); // round 3 open, rounds 1 and 2 unpaid
        // Round 3 keeps its own 5 000; the 7 000 beyond go to round 1, then round 2.
        var s = Situation(V(Tour(3), 12000));

        Assert.Equal(new decimal[] { 5000, 2000, 5000 }, s.Tours.Select(t => t.Valide));
        Assert.Equal(new decimal[] { 0, 3000, 0 }, s.Tours.Select(t => t.Reste));
    }

    [Fact]
    public void ChaqueTourGardeDabordSonPropreVersement()
    {
        Cloturer(); // round 1 unpaid, 5 000 on round 2: no surplus, round 1 stays due
        var s = Situation(V(Tour(2), 5000));

        Assert.Equal(5000, s.Tour(Tour(1).Id.Value)!.Reste);
        Assert.True(s.Tour(Tour(2).Id.Value)!.Paye);
    }

    [Fact]
    public void SurplusNeRemplitQueLeValide_LAttenteDuTourResteAValider()
    {
        Cloturer(); // round 1: 3 000 pending; round 2: 10 000 validated
        var s = Situation(V(Tour(1), 3000, valide: false), V(Tour(2), 10000));

        var t1 = s.Tour(Tour(1).Id.Value)!;
        Assert.True(t1.Paye);
        Assert.Equal(3000, t1.EnAttente);
        Assert.Equal(0, t1.Reste);
    }

    [Fact]
    public void Report_SurTroisTours_UnAncienEtUnRecentOntChacunLeurExcedent()
    {
        Cloturer();
        Cloturer(); // round 3 open: round 1 overpaid, round 2 unpaid, round 3 overpaid
        var s = Situation(V(Tour(1), 7000), V(Tour(3), 6000));

        Assert.Equal(new decimal[] { 5000, 3000, 5000 }, s.Tours.Select(t => t.Valide));
        Assert.Equal(new decimal[] { 0, 3000, 0 }, s.Tours.Select(t => t.Reporte));
        Assert.Equal(new decimal[] { 0, 2000, 0 }, s.Tours.Select(t => t.Reste));
        Assert.Equal(0, s.Avance);
    }

    [Fact]
    public void TourPartielAvecAttente_LeSurplusCompleteLeValide_LAttenteDevientExcedent()
    {
        Cloturer(); // round 1: 2 000 validated + 2 000 pending; round 2: 8 000 validated
        var s = Situation(V(Tour(1), 2000), V(Tour(1), 2000, valide: false), V(Tour(2), 8000));

        var t1 = s.Tour(Tour(1).Id.Value)!;
        Assert.Equal(5000, t1.Valide);
        Assert.Equal(3000, t1.Reporte);
        Assert.True(t1.Paye);
        Assert.Equal(2000, t1.EnAttenteExcedent);
        Assert.Equal(2000, s.EnAttenteTotal);
        Assert.Equal(2000, s.EnAttenteExcedent);
    }

    [Fact]
    public void AttenteSurUnTourNonDu_ResteVisible()
    {
        var alice = _tontine.Members.Single(m => m.Name == "Alice");
        var s = _tontine.SituationDe(alice.Id, [V(Tour(1), 3000, valide: false)]);

        Assert.Equal(3000, s.EnAttenteHorsTours);
        Assert.Equal(3000, s.EnAttenteTotal);
        Assert.Equal(3000, s.EnAttenteExcedent);
    }

    [Fact]
    public void BeneficiaireNeDoitPasSonTour_SonVersementEstUneAvance()
    {
        var alice = _tontine.Members.Single(m => m.Name == "Alice");
        var s = _tontine.SituationDe(alice.Id, [V(Tour(1), 5000)]);

        Assert.Null(s.Tour(Tour(1).Id.Value));
        Assert.Equal(5000, s.Avance);
    }
}
