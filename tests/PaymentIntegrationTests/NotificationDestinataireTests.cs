namespace PaymentIntegrationTests;

using Application.NotificationManagement.Services;
using Domain.Common;
using Domain.IdentityManagement;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.NotificationManagement;
using Domain.NotificationManagement.Repositories;
using Domain.NotificationManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

/// <summary>Notifications are addressed to phone numbers; ids must be resolved, never thrown on.</summary>
public class NotificationDestinataireTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUtilisateurRepository> _users = new();
    private readonly Mock<ITontineRepository> _tontines = new();
    private readonly List<Notification> _added = [];

    public NotificationDestinataireTests()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => _added.Add(n)).Returns(Task.CompletedTask);
    }

    private NotificationService Service() => new(_repo.Object, new Mock<IUnitOfWork>().Object,
        NullLogger<NotificationService>.Instance, _users.Object, _tontines.Object);

    private Utilisateur User(string tel, bool optOut = false)
    {
        var u = Utilisateur.Create(tel, "Fatou", "hash");
        if (optOut) u.DesactiverSms();
        _users.Setup(r => r.GetByIdAsync(u.Id, It.IsAny<CancellationToken>())).ReturnsAsync(u);
        return u;
    }

    [Fact]
    public async Task MemberId_IsResolvedToTheLinkedAccountPhone()
    {
        var u = User("+22670000010");
        var membreId = MemberId.Create();
        _tontines.Setup(t => t.GetUtilisateurIdOfMembreAsync(membreId, It.IsAny<CancellationToken>())).ReturnsAsync(u.Id);

        var ok = await Service().PlanifierNotificationAsync(membreId.Value.ToString(), NotificationType.RappelPaiement, "Rappel");

        ok.Should().BeTrue();
        _added.Single().DestinataireId.Should().Be("+22670000010");
    }

    [Fact]
    public async Task UtilisateurId_IsResolvedToPhone()
    {
        var u = User("+22670000011");
        (await Service().PlanifierNotificationAsync(u.Id.Value.ToString(), NotificationType.OuvertureTour, "Tour"))
            .Should().BeTrue();
        _added.Single().DestinataireId.Should().Be("+22670000011");
    }

    [Fact]
    public async Task MemberWithoutAccount_IsSkippedWithoutThrowing()
    {
        (await Service().PlanifierNotificationAsync(Guid.NewGuid().ToString(), NotificationType.RappelPaiement, "Rappel"))
            .Should().BeFalse();
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task OptOut_SkipsNonCriticalButKeepsPaymentConfirmation()
    {
        var u = User("+22670000012", optOut: true);

        (await Service().PlanifierNotificationAsync(u.Id.Value.ToString(), NotificationType.RappelPaiement, "Rappel"))
            .Should().BeFalse();
        (await Service().PlanifierNotificationAsync(u.Id.Value.ToString(), NotificationType.ConfirmationPaiement, "Merci"))
            .Should().BeTrue();
        _added.Single().Type.Should().Be(NotificationType.ConfirmationPaiement);
    }
}
