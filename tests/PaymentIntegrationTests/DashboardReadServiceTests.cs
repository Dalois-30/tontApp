namespace PaymentIntegrationTests;

using Application.Dashboard;
using Application.NotificationManagement.Services;
using Application.PaymentManagement.Services;
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
        _users.Object, new Mock<INotificationRepository>().Object, _notif.Object);

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
