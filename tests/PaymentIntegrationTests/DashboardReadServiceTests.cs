namespace PaymentIntegrationTests;

using Application.Dashboard;
using Application.NotificationManagement.Services;
using Application.PaymentManagement.Services;
using Application.TontineManagement.Queries.GetTourActuel;
using Domain.IdentityManagement;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.NotificationManagement.Repositories;
using Domain.NotificationManagement.ValueObjects;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Moq;

public class DashboardReadServiceTests
{
    private readonly UtilisateurId _gestionnaire = UtilisateurId.Create();
    private readonly Mock<ITontineRepository> _tontines = new();
    private readonly Mock<IVersementRepository> _versements = new();
    private readonly Mock<IUtilisateurRepository> _users = new();
    private readonly Mock<INotificationService> _notif = new();
    private readonly Tontine _tontine;
    private readonly List<Versement> _stored = [];

    public DashboardReadServiceTests()
    {
        _tontine = Tontine.Create("Épargne", null, ContributionAmount.Create(5000, "XOF"),
            TontinePeriodicity.Monthly, 5, _gestionnaire);
        foreach (var n in new[] { "Alice", "Bob", "Carol", "Dan" })
            _tontine.AddMember(n);
        _tontine.Activate(); // round 1, beneficiary = Alice (sequential)

        _tontines.Setup(r => r.GetByIdReadOnlyAsync(_tontine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_tontine);
        _versements.Setup(r => r.GetByTontineAndTourAsync(_tontine.Id, It.IsAny<TourId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored.ToList());
        _versements.Setup(r => r.GetByTontineAsync(_tontine.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored.ToList());
        _users.Setup(r => r.GetByIdAsync(It.IsAny<UtilisateurId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Utilisateur?)null);
        _notif.Setup(n => n.PlanifierNotificationAsync(It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private DashboardReadService Service() => new(_tontines.Object, _versements.Object,
        new Mock<IAuditEntryRepository>().Object, new Mock<IAuditTrailService>().Object,
        _users.Object, new Mock<INotificationRepository>().Object, _notif.Object,
        new Mock<Application.TontineManagement.Services.ITontineAccessService>().Object,
        new Mock<Domain.Gouvernance.IBureauRepository>().Object);

    private Guid Membre(string nom) => _tontine.Members.First(m => m.Name == nom).Id.Value;
    private Guid Tour => _tontine.Rounds.First().Id.Value;

    private void Versement(string nom, bool confirmer, decimal montant = 5000, Guid? tour = null)
    {
        var v = Domain.PaymentManagement.Versement.Create(_tontine.Id, TourId.From(tour ?? Tour), PayeurId.From(Membre(nom)), Montant.Create(montant));
        if (confirmer) v.Confirmer("CASH");
        _stored.Add(v);
    }

    // ── Partial payments (contribution 5 000) ───────────────────────

    [Fact]
    public async Task PartielValide_PayePartiel_AvecResteDuTour()
    {
        Versement("Bob", confirmer: true, montant: 2000);

        var bob = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == "Bob");

        bob.Statut.Should().Be("paye_partiel");
        bob.ResteTour.Should().Be(3000);
        bob.EnAttenteTour.Should().Be(0);
        bob.ResteDu.Should().Be(0);
    }

    [Fact]
    public async Task Complement_Valide_PasseAPaye()
    {
        Versement("Bob", confirmer: true, montant: 2000);
        Versement("Bob", confirmer: true, montant: 3000);

        var bob = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == "Bob");

        bob.Statut.Should().Be("paye");
        bob.ResteTour.Should().Be(0);
    }

    [Fact]
    public async Task PartielPlusAttente_PayePartiel_AvecMontantAValider_EtRienAPayer()
    {
        Versement("Bob", confirmer: true, montant: 2000);
        Versement("Bob", confirmer: false, montant: 3000);

        var bob = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == "Bob");

        bob.Statut.Should().Be("paye_partiel");
        bob.EnAttenteTour.Should().Be(3000);
        bob.ResteTour.Should().Be(0);
    }

    [Fact]
    public async Task Complet_Paye_SansReste()
    {
        Versement("Bob", confirmer: true);

        var membres = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres;

        membres.Single(m => m.Nom == "Bob").Statut.Should().Be("paye");
        membres.Single(m => m.Nom == "Bob").ResteTour.Should().Be(0);
        membres.Single(m => m.Nom == "Dan").ResteTour.Should().Be(5000);
        membres.Single(m => m.Nom == "Alice").ResteTour.Should().Be(0); // beneficiary
    }

    [Fact]
    public async Task ArrierePartiel_ResteDansLesImpayes_AvecLeReste()
    {
        Versement("Bob", confirmer: true, montant: 2000);   // round 1, partial
        Versement("Carol", confirmer: true, montant: 2000);
        Versement("Carol", confirmer: false, montant: 3000); // round 1 covered (validated + pending)
        Versement("Dan", confirmer: true);                   // round 1 paid
        _tontine.CloseRound(_tontine.Rounds.First().Id);

        var membres = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres;

        var bob = membres.Single(m => m.Nom == "Bob");
        bob.ToursImpayes.Should().Equal(1);
        bob.ResteDu.Should().Be(3000);
        bob.RestesImpayes.Should().Equal(3000m);
        membres.Single(m => m.Nom == "Carol").ToursImpayes.Should().BeEmpty();
        membres.Single(m => m.Nom == "Dan").ResteDu.Should().Be(0);
    }

    [Fact]
    public async Task Retardataires_UnPaiementPartielResteRetardataire()
    {
        Versement("Bob", confirmer: true, montant: 2000);
        Versement("Carol", confirmer: true);

        var result = await Service().GetRetardatairesAsync(_tontine.Id.Value, Tour);

        result!.Select(r => r.Nom).Should().BeEquivalentTo("Bob", "Dan");
    }

    // ── Surplus carried over (single rule, same result everywhere) ──

    private Guid TourNumero(int n) => _tontine.Rounds.Single(r => r.RoundNumber == n).Id.Value;
    private void Cloturer() => _tontine.CloseRound(_tontine.Rounds.Single(r => !r.IsCompleted).Id);

    private async Task<(DashMembreDto Dto, IReadOnlyList<RetardataireDto> Retard, TourActuelDto Actuel)> Partout(string nom)
    {
        var dto = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == nom);
        var ouvert = _tontine.Rounds.Single(r => !r.IsCompleted).Id.Value;
        var retard = (await Service().GetRetardatairesAsync(_tontine.Id.Value, ouvert))!;
        var actuel = (await new GetTourActuelQueryHandler(_tontines.Object, _versements.Object)
            .Handle(new GetTourActuelQuery(_tontine.Id.Value), CancellationToken.None))!;
        return (dto, retard, actuel);
    }

    [Fact]
    public async Task SurplusVersArriere_AJour_SansResteDu_NiRetardataire_PartoutPareil()
    {
        Cloturer(); // round 2 open (Bob beneficiary); Dan paid nothing on round 1
        Versement("Dan", confirmer: true, montant: 10000, tour: TourNumero(2));

        var (dan, retard, actuel) = await Partout("Dan");

        dan.Statut.Should().Be("paye");
        dan.ResteDu.Should().Be(0);
        dan.ToursImpayes.Should().BeEmpty();
        dan.ResteTour.Should().Be(0);
        dan.Avance.Should().Be(0);
        retard.Select(r => r.Nom).Should().NotContain("Dan");
        actuel.Payeurs.Single(p => p.Nom == "Dan").Statut.Should().Be("Payé");
        (await Service().GetRetardatairesAsync(_tontine.Id.Value, TourNumero(1)))!.Select(r => r.Nom).Should().NotContain("Dan");
    }

    [Fact]
    public async Task SurplusEnAvance_PayeAvecAvance_PuisTourSuivantDejaCouvert()
    {
        Versement("Bob", confirmer: true, montant: 8000);

        var (bob, _, _) = await Partout("Bob");
        bob.Statut.Should().Be("paye");
        bob.Avance.Should().Be(3000);

        Cloturer(); // round 2: Bob is the beneficiary; round 3 is Carol's: Bob owes it
        Cloturer();
        var (apres, retard, actuel) = await Partout("Bob");
        apres.Statut.Should().Be("paye_partiel");
        apres.ResteTour.Should().Be(2000);
        apres.Avance.Should().Be(0);
        retard.Select(r => r.Nom).Should().Contain("Bob");
        actuel.Payeurs.Single(p => p.Nom == "Bob").Statut.Should().Be("Payé partiellement");
    }

    [Fact]
    public async Task TourActuel_PartielNonCompteCommePaye()
    {
        Versement("Bob", confirmer: true, montant: 2000);
        Versement("Carol", confirmer: true);

        var (_, _, actuel) = await Partout("Bob");

        actuel.Payeurs.Single(p => p.Nom == "Bob").Statut.Should().Be("Payé partiellement");
        actuel.Payeurs.Single(p => p.Nom == "Carol").Statut.Should().Be("Payé");
        actuel.Payeurs.Single(p => p.Nom == "Alice").Statut.Should().Be("Bénéficiaire");
        actuel.NombrePayes.Should().Be(1);
        actuel.NombreAttendus.Should().Be(3);
        actuel.MontantCollecte.Should().Be(7000);
        actuel.MontantAttendu.Should().Be(15000);
    }

    [Fact]
    public async Task Rejet_AucunReportDuMontantRejete()
    {
        Cloturer();
        Versement("Dan", confirmer: true, tour: TourNumero(2));
        var rejete = Domain.PaymentManagement.Versement.Create(_tontine.Id, TourId.From(TourNumero(2)), PayeurId.From(Membre("Dan")), Montant.Create(5000));
        rejete.Rejeter("Argent non reçu");
        _stored.Add(rejete);

        var (dan, _, _) = await Partout("Dan");

        dan.ToursImpayes.Should().Equal(1);
        dan.ResteDu.Should().Be(5000);
        dan.Avance.Should().Be(0);
    }

    [Fact]
    public async Task Relance_TourCouvertParReport_PasDeResteReclame()
    {
        Cloturer();
        Versement("Dan", confirmer: true, montant: 7000, tour: TourNumero(1)); // 2 000 carried to round 2
        var messages = new Dictionary<string, string>();
        _notif.Setup(n => n.PlanifierNotificationAsync(It.IsAny<string>(), NotificationType.RappelPaiement, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, NotificationType, string, CancellationToken>((dest, _, msg, _) => messages[dest] = msg)
            .ReturnsAsync(true);

        await Service().RelancerAsync(_tontine.Id.Value, [Membre("Dan")]);

        static string N(decimal x) => x.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        messages[Membre("Dan").ToString()].Should().Contain($"il reste {N(3000)} XOF");
    }

    [Fact]
    public async Task Relance_TourCouvertParReport_OuNonDu_AucunSms()
    {
        Cloturer(); // round 2 open, Bob is its beneficiary (does not owe it)
        Versement("Dan", confirmer: true, montant: 10000, tour: TourNumero(1)); // round 2 covered by carry-over

        var count = await Service().RelancerAsync(_tontine.Id.Value, [Membre("Dan"), Membre("Bob")]);

        count.Should().Be(0);
        _notif.Verify(n => n.PlanifierNotificationAsync(It.IsAny<string>(), It.IsAny<NotificationType>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relance_TourCouvertParAttente_AucunSms()
    {
        Versement("Dan", confirmer: false);

        (await Service().RelancerAsync(_tontine.Id.Value, [Membre("Dan")])).Should().Be(0);
    }

    /// <summary>Dan's first owed round is the next one (as for a member whose terms start later).</summary>
    private void PremierTourDuDan(int numero) =>
        typeof(Domain.TontineManagement.Entities.Member).GetProperty("PremierTourDu")!
            .SetValue(_tontine.Members.Single(m => m.Name == "Dan"), numero);

    [Fact]
    public async Task TourNonDu_StatutNonDu_PartoutPareil_NiRetard_NiRelance()
    {
        PremierTourDuDan(2);

        var (dan, retard, actuel) = await Partout("Dan");

        dan.Statut.Should().Be("non_du");
        dan.ResteTour.Should().Be(0);
        retard.Select(r => r.Nom).Should().NotContain("Dan");
        actuel.Payeurs.Single(p => p.Nom == "Dan").Statut.Should().Be("Non dû");
        actuel.NombreAttendus.Should().Be(2); // Bob, Carol (Alice is the beneficiary)
        (await Service().RelancerAsync(_tontine.Id.Value, [Membre("Dan")])).Should().Be(0);

        var tour = (await Service().GetTontineAsync(_tontine.Id.Value))!.Tours.Single(t => t.Numero == 1);
        tour.NombreAttendus.Should().Be(2);
        tour.MontantAttendu.Should().Be(10000);
    }

    [Fact]
    public async Task AdherentTardifSansRattrapage_NeDoitPasLesToursAnterieurs()
    {
        Cloturer(); // round 2 open
        _tontine.ModifierReglesAdhesion(ModeAdhesionEnCours.AvecValidation, rattrapage: false);
        var (_, code) = _tontine.GenerateInvitation();
        var eve = _tontine.JoinWithInvitation("Eve", code, UtilisateurId.Create());
        _tontine.AccepterAdhesion(eve.Id);

        var (dto, retard, actuel) = await Partout("Eve");

        dto.Statut.Should().Be("a_payer");
        dto.ResteDu.Should().Be(0);
        dto.ToursImpayes.Should().BeEmpty();
        retard.Select(r => r.Nom).Should().Contain("Eve");
        actuel.Payeurs.Single(p => p.Nom == "Eve").Statut.Should().Be("En attente");
        (await Service().GetRetardatairesAsync(_tontine.Id.Value, TourNumero(1)))!.Select(r => r.Nom).Should().NotContain("Eve");
    }

    [Fact]
    public async Task TotauxDesTours_RegleCommune_IdentiquesAuTourActuel()
    {
        Versement("Bob", confirmer: true, montant: 8000); // 3 000 of advance
        Versement("Carol", confirmer: true, montant: 2000);
        Cloturer(); // round 2: Bob is the beneficiary; Bob's advance stays
        Versement("Dan", confirmer: true, montant: 2000, tour: TourNumero(2));

        var dto = (await Service().GetTontineAsync(_tontine.Id.Value))!;
        var (_, _, actuel) = await Partout("Dan");

        var t1 = dto.Tours.Single(t => t.Numero == 1);
        t1.MontantAttendu.Should().Be(15000);
        t1.MontantCollecte.Should().Be(7000);   // Bob 5 000 (capped) + Carol 2 000; Dan's 2 000 stays on round 2
        t1.NombrePayes.Should().Be(1);
        var t2 = dto.Tours.Single(t => t.Numero == 2);
        t2.MontantCollecte.Should().Be(actuel.MontantCollecte);
        t2.MontantAttendu.Should().Be(actuel.MontantAttendu);
        t2.NombreAttendus.Should().Be(actuel.NombreAttendus);
        t2.NombrePayes.Should().Be(actuel.NombrePayes);
    }

    [Fact]
    public async Task AttenteAuDelaDuDu_ExposeeAuTresorier_EtReporteDuTour()
    {
        Versement("Dan", confirmer: true, montant: 2000);
        Versement("Dan", confirmer: false, montant: 4000); // 1 000 beyond what round 1 still owes

        var dan = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == "Dan");

        dan.EnAttenteTour.Should().Be(4000);
        dan.EnAttenteTotal.Should().Be(4000);
        dan.EnAttenteExcedentTour.Should().Be(1000);
        dan.EnAttenteExcedent.Should().Be(1000);

        Cloturer();
        Versement("Carol", confirmer: true, montant: 10000, tour: TourNumero(1));
        var carol = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == "Carol");
        carol.Statut.Should().Be("paye");
        carol.ReporteTour.Should().Be(5000);
    }

    [Fact]
    public async Task MembreSuspendu_GardeSonAvanceVisible()
    {
        Versement("Bob", confirmer: true, montant: 8000);
        _tontine.SuspendMember(_tontine.Members.Single(m => m.Name == "Bob").Id);

        var bob = (await Service().GetTontineAsync(_tontine.Id.Value))!.Membres.Single(m => m.Nom == "Bob");

        bob.Statut.Should().Be("suspendu");
        bob.Avance.Should().Be(3000);
    }

    [Fact]
    public async Task Tontine_MemberStatuses_ReflectCurrentRound()
    {
        Versement("Bob", confirmer: true);
        Versement("Carol", confirmer: false);

        var dto = await Service().GetTontineAsync(_tontine.Id.Value);

        dto!.Membres.ToDictionary(m => m.Nom, m => m.Statut).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Alice"] = "beneficiaire", ["Bob"] = "paye", ["Carol"] = "en_attente", ["Dan"] = "a_payer"
        });
        dto.Tours.Single().Status.Should().Be("Ouvert");
        dto.Tours.Single().Beneficiaire!.Nom.Should().Be("Alice");
        dto.GestionnaireId.Should().Be(_gestionnaire.Value);
    }

    [Fact]
    public async Task Retardataires_ExcludeBeneficiaryAndMembersWithPayment()
    {
        Versement("Bob", confirmer: true);
        Versement("Carol", confirmer: false);

        var result = await Service().GetRetardatairesAsync(_tontine.Id.Value, Tour);

        result!.Select(r => r.Nom).Should().Equal("Dan");
    }

    [Fact]
    public async Task Retardataires_UnknownRound_ReturnsNull()
    {
        (await Service().GetRetardatairesAsync(_tontine.Id.Value, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Versements_ArePagedWithPayerNameAndRoundNumber()
    {
        Versement("Bob", confirmer: true);
        Versement("Dan", confirmer: false);

        var page = await Service().GetVersementsAsync(_tontine.Id.Value, 1, 1);

        page.TotalCount.Should().Be(2);
        page.TotalPages.Should().Be(2);
        page.Items.Single().PayeurNom.Should().BeOneOf("Bob", "Dan");
        page.Items.Single().TourNumero.Should().Be(1);
    }

    [Fact]
    public async Task Relancer_OnlyTargetsMembersOfTheTontine()
    {
        var count = await Service().RelancerAsync(_tontine.Id.Value, [Membre("Dan"), Guid.NewGuid()]);

        count.Should().Be(1);
        _notif.Verify(n => n.PlanifierNotificationAsync(Membre("Dan").ToString(), NotificationType.RappelPaiement,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Relancer_PayeurPartiel_RecoitLeResteDansSonMessage()
    {
        Versement("Bob", confirmer: true, montant: 2000);
        var messages = new Dictionary<string, string>();
        _notif.Setup(n => n.PlanifierNotificationAsync(It.IsAny<string>(), NotificationType.RappelPaiement, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, NotificationType, string, CancellationToken>((dest, _, msg, _) => messages[dest] = msg)
            .ReturnsAsync(true);

        var count = await Service().RelancerAsync(_tontine.Id.Value, [Membre("Bob"), Membre("Dan")]);

        static string N(decimal x) => x.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        count.Should().Be(2);
        messages[Membre("Bob").ToString()].Should().Contain($"il reste {N(3000)} XOF").And.Contain($"cotisation de {N(5000)} XOF");
        messages[Membre("Dan").ToString()].Should().Contain($"votre cotisation de {N(5000)} XOF est attendue");
    }

    [Fact]
    public async Task LateMember_PendingThenOwesEarlierRounds()
    {
        // Round 1 (Alice beneficiary) is closed, round 2 is open (Bob beneficiary).
        Versement("Bob", confirmer: true); // Bob paid round 1
        _tontine.CloseRound(_tontine.Rounds.First().Id);
        var (_, code) = _tontine.GenerateInvitation();
        var eve = _tontine.JoinWithInvitation("Eve", code, UtilisateurId.Create());

        var pending = await Service().GetTontineAsync(_tontine.Id.Value);
        pending!.Membres.Single(m => m.Nom == "Eve").Statut.Should().Be("adhesion_en_attente");

        _tontine.AccepterAdhesion(eve.Id);
        var dto = await Service().GetTontineAsync(_tontine.Id.Value);

        var eveDto = dto!.Membres.Single(m => m.Nom == "Eve");
        eveDto.Rang.Should().Be(5);
        eveDto.ToursImpayes.Should().Equal(1);         // arrears: round 1, opened before she joined
        eveDto.ResteDu.Should().Be(5000);
        dto.Membres.Single(m => m.Nom == "Bob").ResteDu.Should().Be(0);      // paid round 1
        dto.Membres.Single(m => m.Nom == "Carol").ToursImpayes.Should().Equal(1);
        dto.Membres.Single(m => m.Nom == "Alice").ResteDu.Should().Be(0);    // round 1 beneficiary
    }
}
