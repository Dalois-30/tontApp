using Application.NotificationManagement.EventHandlers;
using Application.NotificationManagement.Services;
using Domain.Gouvernance;
using Domain.IdentityManagement.ValueObjects;
using Domain.NotificationManagement.ValueObjects;
using Domain.PaymentManagement.Events;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;

namespace NotificationTests;

public class PaiementEnRetardEventHandlerTests
{
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ITontineRepository> _tontineRepositoryMock;
    private readonly Mock<IBureauRepository> _bureauRepositoryMock = new();
    private readonly Mock<ILogger<PaiementEnRetardEventHandler>> _loggerMock;
    private readonly PaiementEnRetardEventHandler _handler;

    public PaiementEnRetardEventHandlerTests()
    {
        _notificationServiceMock = new Mock<INotificationService>();
        _tontineRepositoryMock = new Mock<ITontineRepository>();
        _loggerMock = new Mock<ILogger<PaiementEnRetardEventHandler>>();

        _handler = new PaiementEnRetardEventHandler(
            _notificationServiceMock.Object,
            _tontineRepositoryMock.Object,
            _bureauRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidEvent_PlanifiesRappelSms()
    {
        // Arrange
        var tontineId = TontineId.Create();
        var payeurId = PayeurId.From(Guid.NewGuid());

        var evt = new PaiementEnRetardEvent(
            tontineId,
            TourId.From(Guid.NewGuid()),
            payeurId,
            5000m,
            "XOF");

        var tontine = CreateTontine(tontineId, "Tontine Entraide");
        _tontineRepositoryMock
            .Setup(r => r.GetByIdReadOnlyAsync(tontineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tontine);

        _notificationServiceMock
            .Setup(s => s.PlanifierNotificationAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(evt, CancellationToken.None);

        // Assert
        _notificationServiceMock.Verify(s => s.PlanifierNotificationAsync(
            payeurId.Value.ToString(),
            NotificationType.RappelPaiement,
            It.Is<string>(msg => msg.Contains("retard") && msg.Contains("Tontine Entraide")),
            It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_IncludesMontantAndDevise_InMessage()
    {
        // Arrange
        var tontineId = TontineId.Create();
        var payeurId = PayeurId.From(Guid.NewGuid());

        var evt = new PaiementEnRetardEvent(
            tontineId,
            TourId.From(Guid.NewGuid()),
            payeurId,
            7500m,
            "XOF");

        _tontineRepositoryMock
            .Setup(r => r.GetByIdReadOnlyAsync(tontineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tontine?)null);

        _notificationServiceMock
            .Setup(s => s.PlanifierNotificationAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(evt, CancellationToken.None);

        // Assert
        _notificationServiceMock.Verify(s => s.PlanifierNotificationAsync(
            It.IsAny<string>(),
            NotificationType.RappelPaiement,
            It.Is<string>(msg => msg.Contains("7500") && msg.Contains("XOF")),
            It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_WhenTontineNotFound_UsesFallbackName()
    {
        // Arrange
        var tontineId = TontineId.Create();
        var payeurId = PayeurId.From(Guid.NewGuid());

        var evt = new PaiementEnRetardEvent(
            tontineId,
            TourId.From(Guid.NewGuid()),
            payeurId,
            2000m,
            "XOF");

        _tontineRepositoryMock
            .Setup(r => r.GetByIdReadOnlyAsync(tontineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tontine?)null);

        _notificationServiceMock
            .Setup(s => s.PlanifierNotificationAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _handler.Handle(evt, CancellationToken.None);

        // Assert
        _notificationServiceMock.Verify(s => s.PlanifierNotificationAsync(
            It.IsAny<string>(),
            NotificationType.RappelPaiement,
            It.Is<string>(msg => msg.Contains("votre tontine")),
            It.IsAny<CancellationToken>()),
            Times.Once());
    }

    private static Tontine CreateTontine(TontineId id, string name)
    {
        var tontine = Tontine.Create(
            name,
            "Test description",
            ContributionAmount.Create(5000, "XOF"),
            TontinePeriodicity.Monthly,
            10,
            UtilisateurId.Create(),
            ModeAttribution.Sequentiel);

        var idProperty = typeof(Domain.Common.Entity<TontineId>).GetProperty("Id");
        idProperty?.SetValue(tontine, id);

        return tontine;
    }

    // ── Alerte au bureau ────────────────────────────────────────────

    private (PaiementEnRetardEvent Evt, Tontine Tontine) Retard()
    {
        var tontineId = TontineId.Create();
        var tontine = CreateTontine(tontineId, "Tontine Entraide");
        _tontineRepositoryMock.Setup(r => r.GetByIdReadOnlyAsync(tontineId, It.IsAny<CancellationToken>())).ReturnsAsync(tontine);
        _notificationServiceMock.Setup(s => s.PlanifierNotificationAsync(
            It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return (new PaiementEnRetardEvent(tontineId, TourId.From(Guid.NewGuid()), PayeurId.From(Guid.NewGuid()), 5000m, "XAF"), tontine);
    }

    private static Domain.TontineManagement.Entities.Member AvecCompte(Domain.TontineManagement.Entities.Member m, UtilisateurId compte)
    {
        typeof(Domain.TontineManagement.Entities.Member).GetProperty("UtilisateurId")!.SetValue(m, compte);
        return m;
    }

    private void VerifierAlerteBureau(Guid destinataire, Times fois) =>
        _notificationServiceMock.Verify(s => s.PlanifierNotificationAsync(
            destinataire.ToString(), NotificationType.RappelPaiement, It.IsAny<string>(), It.IsAny<CancellationToken>()), fois);

    [Fact]
    public async Task SansBureau_AlerteLeCreateur()
    {
        var (evt, tontine) = Retard();

        await _handler.Handle(evt, CancellationToken.None);

        VerifierAlerteBureau(tontine.GestionnaireId.Value, Times.Once());
    }

    [Fact]
    public async Task TresorierNomme_ResponsableEtTresorierAlertes_PasLAncien()
    {
        var (evt, tontine) = Retard();
        var createur = tontine.GestionnaireId.Value;
        var tresorier = Guid.NewGuid();
        var bureau = Bureau.Creer(evt.TontineId.Value, TypeGroupe.Tontine, createur);
        bureau.NommerTresorier(tresorier, createur.ToString());
        _bureauRepositoryMock.Setup(r => r.GetAsync(evt.TontineId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(bureau);

        await _handler.Handle(evt, CancellationToken.None);

        VerifierAlerteBureau(createur, Times.Once());   // toujours responsable
        VerifierAlerteBureau(tresorier, Times.Once());
    }

    [Fact]
    public async Task CreateurEvince_NEstPlusAlerte()
    {
        var (evt, tontine) = Retard();
        var createur = tontine.GestionnaireId.Value;
        var responsable = Guid.NewGuid();
        var tresorier = Guid.NewGuid();
        var bureau = Bureau.Creer(evt.TontineId.Value, TypeGroupe.Tontine, createur);
        bureau.ReattribuerRole(RoleBureau.Responsable, responsable, "admin");
        bureau.ReattribuerRole(RoleBureau.Tresorier, tresorier, "admin");
        _bureauRepositoryMock.Setup(r => r.GetAsync(evt.TontineId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(bureau);

        await _handler.Handle(evt, CancellationToken.None);

        VerifierAlerteBureau(createur, Times.Never());
        VerifierAlerteBureau(responsable, Times.Once());
        VerifierAlerteBureau(tresorier, Times.Once());
    }

    [Fact]
    public async Task VoteEchu_LeNouveauResponsableEstAlerte_SansModifierLeBureau()
    {
        var (evt, tontine) = Retard();
        var createur = tontine.GestionnaireId.Value;
        var candidat = Guid.NewGuid();
        var autre = Guid.NewGuid();
        var bureau = Bureau.Creer(evt.TontineId.Value, TypeGroupe.Tontine, createur);
        var debut = DateTime.UtcNow.AddDays(-10);
        var vote = bureau.ProposerChangementResponsable(candidat, candidat, [createur, candidat, autre, Guid.NewGuid()], debut);
        bureau.Voter(vote.Id, candidat, true, debut);
        bureau.Voter(vote.Id, autre, true, debut); // 2 oui sur 4 : quorum atteint, adopté à l'échéance (passée)
        var journal = bureau.Journal.Count;
        _bureauRepositoryMock.Setup(r => r.GetAsync(evt.TontineId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(bureau);

        await _handler.Handle(evt, CancellationToken.None);

        VerifierAlerteBureau(candidat, Times.Once());
        VerifierAlerteBureau(createur, Times.Once()); // toujours trésorier
        Assert.Equal(journal, bureau.Journal.Count);
        Assert.Equal(StatutVote.Ouvert, vote.Statut);
    }

    [Fact]
    public async Task CreateurTientLesDeuxRoles_UneSeuleAlerte_QuiNommeLeMembreEnRetard()
    {
        var (evt0, tontine) = Retard();
        var bruno = AvecCompte(tontine.AddMember("Bruno Mbarga"), UtilisateurId.Create());
        var evt = new PaiementEnRetardEvent(evt0.TontineId, evt0.TourId, PayeurId.From(bruno.Id.Value), 5000m, "XAF");
        var createur = tontine.GestionnaireId.Value;
        _bureauRepositoryMock.Setup(r => r.GetAsync(evt.TontineId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bureau.Creer(evt.TontineId.Value, TypeGroupe.Tontine, createur));

        await _handler.Handle(evt, CancellationToken.None);

        _notificationServiceMock.Verify(s => s.PlanifierNotificationAsync(
            createur.ToString(), NotificationType.RappelPaiement,
            It.Is<string>(m => m.Contains("Bruno Mbarga") && m.Contains("5000") && m.Contains("XAF") && m.Contains("Tontine Entraide")),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task PayeurMembreDuBureau_PasDAlerteSurSonPropreRetard()
    {
        var (evt0, tontine) = Retard();
        var createur = tontine.GestionnaireId.Value;
        var tresorierCompte = UtilisateurId.Create();
        var alice = AvecCompte(tontine.AddMember("Alice Tresoriere"), tresorierCompte);
        var bureau = Bureau.Creer(evt0.TontineId.Value, TypeGroupe.Tontine, createur);
        bureau.NommerTresorier(tresorierCompte.Value, createur.ToString());
        _bureauRepositoryMock.Setup(r => r.GetAsync(evt0.TontineId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(bureau);
        var evt = new PaiementEnRetardEvent(evt0.TontineId, evt0.TourId, PayeurId.From(alice.Id.Value), 5000m, "XAF");

        await _handler.Handle(evt, CancellationToken.None);

        VerifierAlerteBureau(tresorierCompte.Value, Times.Never()); // son rappel personnel est adressé au membre, pas au compte
        VerifierAlerteBureau(createur, Times.Once());
    }

    [Fact]
    public async Task TontineIntrouvable_NiBureauNiAlerteBureau()
    {
        var evt = new PaiementEnRetardEvent(TontineId.Create(), TourId.From(Guid.NewGuid()), PayeurId.From(Guid.NewGuid()), 5000m, "XAF");
        _notificationServiceMock.Setup(s => s.PlanifierNotificationAsync(
            It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _handler.Handle(evt, CancellationToken.None);

        _bureauRepositoryMock.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _notificationServiceMock.Verify(s => s.PlanifierNotificationAsync(
            It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}
