namespace Application.PaymentManagement.Commands.EnregistrerVersementManuel;

using System.Text.Json;
using Application.Common;
using Application.PaymentManagement.Services;
using Domain.Common;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Records a payment declared by a member (or by the bureau on behalf of a member).
/// The versement stays EN_ATTENTE until it is explicitly validated
/// (POST /api/v1/versements/{id}/valider): entering a payment and validating it are two distinct,
/// audited steps.
/// </summary>
public sealed class EnregistrerVersementManuelCommandHandler
    : ICommandHandler<EnregistrerVersementManuelCommand, Result<Guid>>
{
    private readonly IVersementRepository _versementRepository;
    private readonly ITontineRepository _tontineRepository;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IUnitOfWork _unitOfWork;

    public EnregistrerVersementManuelCommandHandler(
        IVersementRepository versementRepository,
        ITontineRepository tontineRepository,
        IAuditTrailService auditTrailService,
        IUnitOfWork unitOfWork)
    {
        _versementRepository = versementRepository;
        _tontineRepository = tontineRepository;
        _auditTrailService = auditTrailService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        EnregistrerVersementManuelCommand request,
        CancellationToken cancellationToken)
    {
        var tontineId = TontineId.From(request.TontineId);

        // The member and the round must belong to this tontine.
        var tontine = await _tontineRepository.GetByIdReadOnlyAsync(tontineId, cancellationToken);
        if (tontine is null)
            return Result<Guid>.Failure("Tontine not found.");

        var membre = tontine.Members.FirstOrDefault(m => m.Id.Value == request.MembreId);
        if (membre is null)
            return Result<Guid>.Failure("Member not found in this tontine.");

        if (tontine.Rounds.All(r => r.Id.Value != request.TourId))
            return Result<Guid>.Failure("Round not found in this tontine.");

        if (membre.Statut != StatutMembre.Actif)
            return Result<Guid>.Failure("Only an active member can pay a contribution.");

        // The open round, or a past round the member owes (catch-up rule of the tontine).
        if (tontine.ToursDusPar(membre.Id).All(r => r.Id.Value != request.TourId))
            return Result<Guid>.Failure("This member does not owe a contribution for this round.");

        var tourId = TourId.From(request.TourId);
        var payeurId = PayeurId.From(request.MembreId);

        // Idempotence: reject if a pending or confirmed versement already exists for this membre + tour
        var existingVersements = await _versementRepository.GetByTontineAndTourAsync(
            tontineId, tourId, cancellationToken);

        if (existingVersements.Any(v => v.PayeurId == payeurId && v.Statut != VersementStatus.Echoue))
        {
            return Result<Guid>.Failure(
                $"Un versement est déjà déclaré (en attente de validation) ou validé pour {membre.Name} sur ce tour.");
        }

        // Build hash chain from last versement for this tontine
        var lastVersement = await _versementRepository.GetLastByTontineAsync(tontineId, cancellationToken);
        var hashPrecedent = lastVersement?.HashCourant ?? string.Empty;

        var montant = Montant.Create(request.Montant, request.Devise);

        // Audit entries are chained per tontine: start from the tontine's last audit hash.
        var dernierHashAudit = await _auditTrailService.GetDernierHash(tontineId, cancellationToken);
        var versement = Versement.Create(tontineId, tourId, payeurId, montant, hashPrecedent, dernierHashAudit);

        var acteurId = request.ActeurId?.ToString() ?? "system";
        var payload = JsonSerializer.Serialize(new
        {
            request.TontineId,
            request.TourId,
            request.MembreId,
            request.Montant,
            request.DescriptionPreuve,
            SaisiPar = acteurId
        });

        // Chained on the VersementCree entry above, keeping the tontine chain linear.
        versement.AjouterEntreeAudit(AuditAction.VersementManuel, acteurId, payload);

        await _versementRepository.AddAsync(versement, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(versement.Id.Value);
    }
}
