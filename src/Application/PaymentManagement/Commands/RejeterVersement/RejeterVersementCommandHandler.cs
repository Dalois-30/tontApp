namespace Application.PaymentManagement.Commands.RejeterVersement;

using Application.PaymentManagement.Services;
using Application.Common;
using Domain.Common;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;

public sealed class RejeterVersementCommandHandler : ICommandHandler<RejeterVersementCommand>
{
    private readonly IVersementRepository _versementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IVerrouTontine _verrou;

    public RejeterVersementCommandHandler(
        IVersementRepository versementRepository,
        IUnitOfWork unitOfWork,
        IAuditTrailService auditTrailService,
        IVerrouTontine verrou)
    {
        _versementRepository = versementRepository;
        _unitOfWork = unitOfWork;
        _auditTrailService = auditTrailService;
        _verrou = verrou;
    }

    public async Task Handle(RejeterVersementCommand request, CancellationToken cancellationToken)
    {
        var versementId = VersementId.From(request.VersementId);
        var tontineId = await _versementRepository.GetTontineIdAsync(versementId, cancellationToken)
            ?? throw new InvalidOperationException($"Versement {request.VersementId} not found.");

        // One versement write at a time per tontine. Under the lock, the committed state is re-read without
        // tracking (independent of anything the unit of work loaded before): a decision committed meanwhile
        // is seen and a second one refused, instead of chaining two audit entries on the same previous hash.
        await using var verrou = await _verrou.VerrouillerAsync(tontineId, cancellationToken);
        var actuel = await _versementRepository.GetByIdSansSuiviAsync(versementId, cancellationToken)
            ?? throw new InvalidOperationException($"Versement {request.VersementId} not found.");
        if (actuel.Statut != VersementStatus.EnAttente)
            throw new InvalidOperationException("Only a pending versement can be rejected.");

        var versement = await _versementRepository.GetByIdAsync(versementId, cancellationToken)
            ?? throw new InvalidOperationException($"Versement {request.VersementId} not found.");

        var dernierHashAudit = await _auditTrailService.GetDernierHash(versement.TontineId, cancellationToken);
        versement.Rejeter(request.Raison, dernierHashAudit, request.ActeurId);

        await _versementRepository.UpdateAsync(versement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await verrou.ValiderAsync(cancellationToken);
    }
}
