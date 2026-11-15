namespace PaymentIntegrationTests;

using Api.Security;
using Application.PaymentManagement.Commands.EnregistrerVersementManuel;
using Application.PaymentManagement.Services;
using Application.TontineManagement.Services;
using Domain.Common;
using Domain.IdentityManagement.ValueObjects;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Entities;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

/// <summary>
/// Phase 0 hardening: tontine ownership/membership checks, manual payments pending validation,
/// real actor recorded in the audit trail.
/// </summary>
public class Phase0SecurityTests
{
    private readonly UtilisateurId _gestionnaire = UtilisateurId.Create();
    private readonly UtilisateurId _membreUser = UtilisateurId.Create();
    private readonly Mock<ITontineRepository> _tontineRepo = new();
    private readonly Mock<IVersementRepository> _versementRepo = new();
    private readonly Mock<IAuditTrailService> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    /// <summary>Active tontine with 3 members, the first one linked to <see cref="_membreUser"/>.</summary>
    private Tontine BuildActiveTontine()
    {
        var tontine = Tontine.Create("Test", null, ContributionAmount.Create(5000, "XOF"),
            TontinePeriodicity.Monthly, 5, _gestionnaire);
        var (_, code) = tontine.GenerateInvitation(nombreUsagesMax: 1);
        tontine.JoinWithInvitation("Alice", code, _membreUser);
        tontine.AddMember("Bob");
        tontine.AddMember("Carol");
        tontine.Activate();

        _tontineRepo.Setup(r => r.GetByIdReadOnlyAsync(tontine.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tontine);
        return tontine;
    }

    private TontineAccessService AccessService() => new(_tontineRepo.Object, _versementRepo.Object);

    // ── Access service ──────────────────────────────────────────────

    [Fact]
    public async Task Acces_Gestionnaire_IsGestionnaire()
    {
        var tontine = BuildActiveTontine();
        var acces = await AccessService().GetAccesAsync(tontine.Id.Value, _gestionnaire.Value);

        acces.Niveau.Should().Be(NiveauAcces.Gestionnaire);
        acces.Permet(NiveauAcces.Membre).Should().BeTrue();
    }

    [Fact]
    public async Task Acces_LinkedMember_IsMembre_WithMemberId()
    {
        var tontine = BuildActiveTontine();
        var acces = await AccessService().GetAccesAsync(tontine.Id.Value, _membreUser.Value);

        acces.Niveau.Should().Be(NiveauAcces.Membre);
        acces.MembreId.Should().Be(tontine.Members.First(m => m.Name == "Alice").Id.Value);
        acces.Permet(NiveauAcces.Gestionnaire).Should().BeFalse();
    }

    [Fact]
    public async Task Acces_Stranger_HasNoAccess()
    {
        var tontine = BuildActiveTontine();
        var acces = await AccessService().GetAccesAsync(tontine.Id.Value, Guid.NewGuid());

        acces.Existe.Should().BeTrue();
        acces.Permet(NiveauAcces.Membre).Should().BeFalse();
    }

    [Fact]
    public async Task Acces_UnknownTontine_DoesNotExist()
    {
        var acces = await AccessService().GetAccesAsync(Guid.NewGuid(), _gestionnaire.Value);
        acces.Existe.Should().BeFalse();
    }

    [Fact]
    public void AccessResults_MapsToHttpStatus()
    {
        var tontineId = Guid.NewGuid();
        var membre = new AccesTontine(true, tontineId, NiveauAcces.Membre, Guid.NewGuid());

        AccessResults.Check(AccesTontine.Introuvable(tontineId), NiveauAcces.Membre, false)
            .Should().BeOfType<NotFoundObjectResult>();
        AccessResults.Check(membre, NiveauAcces.Gestionnaire, false)
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        AccessResults.Check(membre, NiveauAcces.Gestionnaire, estAdmin: true).Should().BeNull();
        AccessResults.Check(membre, NiveauAcces.Membre, false).Should().BeNull();
    }

    // ── Manual payment ──────────────────────────────────────────────

    private EnregistrerVersementManuelCommandHandler ManuelHandler() =>
        new(_versementRepo.Object, _tontineRepo.Object, _audit.Object, _uow.Object);

    private void SetupNoExistingVersements()
    {
        _versementRepo.Setup(r => r.GetByTontineAndTourAsync(It.IsAny<TontineId>(), It.IsAny<TourId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Versement>());
        _versementRepo.Setup(r => r.GetLastByTontineAsync(It.IsAny<TontineId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versement?)null);
        _audit.Setup(a => a.GetDernierHash(It.IsAny<TontineId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuditEntry.GenesisHash);
    }

    [Fact]
    public async Task Manuel_CreatesPendingVersement_AndAuditsRealActor()
    {
        var tontine = BuildActiveTontine();
        SetupNoExistingVersements();
        Versement? added = null;
        _versementRepo.Setup(r => r.AddAsync(It.IsAny<Versement>(), It.IsAny<CancellationToken>()))
            .Callback<Versement, CancellationToken>((v, _) => added = v);

        var membre = tontine.Members.First();
        var tour = tontine.Rounds.First();
        var result = await ManuelHandler().Handle(new EnregistrerVersementManuelCommand(
            tontine.Id.Value, tour.Id.Value, membre.Id.Value, 5000m, "Reçu n°12",
            ActeurId: _gestionnaire.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Statut.Should().Be(VersementStatus.EnAttente);
        result.Value.Should().Be(added.Id.Value);
        added.AuditTrail.Select(e => e.Action).Should()
            .Equal(AuditAction.VersementCree, AuditAction.VersementManuel);
        added.AuditTrail.Last().ActeurId.Should().Be(_gestionnaire.Value.ToString());
        added.VerifierIntegrite().Should().BeTrue();
    }

    [Fact]
    public async Task Manuel_MemberOfAnotherTontine_IsRejected()
    {
        var tontine = BuildActiveTontine();
        SetupNoExistingVersements();

        var result = await ManuelHandler().Handle(new EnregistrerVersementManuelCommand(
            tontine.Id.Value, tontine.Rounds.First().Id.Value, Guid.NewGuid(), 5000m, "x"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _versementRepo.Verify(r => r.AddAsync(It.IsAny<Versement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manuel_RoundOfAnotherTontine_IsRejected()
    {
        var tontine = BuildActiveTontine();
        SetupNoExistingVersements();

        var result = await ManuelHandler().Handle(new EnregistrerVersementManuelCommand(
            tontine.Id.Value, Guid.NewGuid(), tontine.Members.First().Id.Value, 5000m, "x"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Manuel_PendingVersementAlreadyExists_IsRejected()
    {
        var tontine = BuildActiveTontine();
        var membre = tontine.Members.First();
        var tour = tontine.Rounds.First();
        var pending = Versement.Create(tontine.Id, TourId.From(tour.Id.Value), PayeurId.From(membre.Id.Value),
            Montant.Create(5000m));
        _versementRepo.Setup(r => r.GetByTontineAndTourAsync(tontine.Id, It.IsAny<TourId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Versement> { pending });

        var result = await ManuelHandler().Handle(new EnregistrerVersementManuelCommand(
            tontine.Id.Value, tour.Id.Value, membre.Id.Value, 5000m, "x"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ── Domain: actor in audit trail ────────────────────────────────

    [Fact]
    public void Confirmer_RecordsActorInAuditTrail()
    {
        var versement = Versement.Create(TontineId.Create(), TourId.From(Guid.NewGuid()),
            PayeurId.From(Guid.NewGuid()), Montant.Create(5000m));
        var acteur = Guid.NewGuid().ToString();

        versement.Confirmer("CASH-1", acteurId: acteur);

        versement.AuditTrail.Last().ActeurId.Should().Be(acteur);
        versement.AuditTrail.Last().Action.Should().Be(AuditAction.VersementConfirme);
        versement.VerifierIntegrite().Should().BeTrue();
    }

    // ── Audit chain: linear per tontine, survives a DB round-trip ───

    [Fact]
    public void Horloge_TruncatesToMicrosecond()
    {
        var t = Domain.Common.Horloge.MaintenantUtc();
        (t.Ticks % 10).Should().Be(0);
        t.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task VerifierChaine_TwoVersementsChainedThroughService_IsValid()
    {
        // Simulates the handlers: each new audit entry starts from the tontine's last hash.
        var tontineId = TontineId.Create();
        var stored = new List<AuditEntry>();
        var repo = new Mock<IAuditEntryRepository>();
        repo.Setup(r => r.GetLastByTontineAsync(tontineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored.LastOrDefault());
        repo.Setup(r => r.GetByTontineOrderedAsync(tontineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored.ToList());
        var service = new AuditTrailService(repo.Object);

        var v1 = Versement.Create(tontineId, TourId.From(Guid.NewGuid()), PayeurId.From(Guid.NewGuid()),
            Montant.Create(5000m), hashPrecedentAudit: await service.GetDernierHash(tontineId));
        v1.AjouterEntreeAudit(AuditAction.VersementManuel, "gestionnaire", "{}");
        stored.AddRange(v1.AuditTrail);

        var v2 = Versement.Create(tontineId, TourId.From(Guid.NewGuid()), PayeurId.From(Guid.NewGuid()),
            Montant.Create(5000m), hashPrecedentAudit: await service.GetDernierHash(tontineId));
        stored.AddRange(v2.AuditTrail);

        v1.Confirmer("CASH-1", await service.GetDernierHash(tontineId), "gestionnaire");
        stored.Add(v1.AuditTrail.Last());

        var report = await service.VerifierChaine(tontineId);

        report.NombreEntrees.Should().Be(4);
        report.EstIntegre.Should().BeTrue();
        v1.VerifierIntegrite().Should().BeTrue();
    }
}
