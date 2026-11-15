namespace Application.PaymentManagement.Commands.ConfirmVersement;

using Application.PaymentManagement.Services;
using Application.Common;
using Domain.Common;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;

public sealed class ConfirmVersementCommandHandler : ICommandHandler<ConfirmVersementCommand>
{
    private readonly IVersementRepository _versementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _auditTrailService;

    public ConfirmVersementCommandHandler(
        IVersementRepository versementRepository,
        IUnitOfWork unitOfWork,
        IAuditTrailService auditTrailService)
    {
        _versementRepository = versementRepository;
        _unitOfWork = unitOfWork;
        _auditTrailService = auditTrailService;
    }

    public async Task Handle(ConfirmVersementCommand request, CancellationToken cancellationToken)
    {
        var versement = await _versementRepository.GetByIdAsync(
            VersementId.From(request.VersementId), cancellationToken)
            ?? throw new InvalidOperationException($"Versement {request.VersementId} not found.");

        // Chain on the tontine's last audit entry (tontine-level linear chain).
        var dernierHashAudit = await _auditTrailService.GetDernierHash(versement.TontineId, cancellationToken);
        versement.Confirmer(request.ReferenceExterne, dernierHashAudit, request.ActeurId);

        await _versementRepository.UpdateAsync(versement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
