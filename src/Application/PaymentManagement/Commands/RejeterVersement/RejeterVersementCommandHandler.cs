namespace Application.PaymentManagement.Commands.RejeterVersement;

using Application.PaymentManagement.Services;
using Application.Common;
using Domain.Common;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;

public sealed class RejeterVersementCommandHandler : ICommandHandler<RejeterVersementCommand>
{
    private readonly IVersementRepository _versementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _auditTrailService;

    public RejeterVersementCommandHandler(
        IVersementRepository versementRepository,
        IUnitOfWork unitOfWork,
        IAuditTrailService auditTrailService)
    {
        _versementRepository = versementRepository;
        _unitOfWork = unitOfWork;
        _auditTrailService = auditTrailService;
    }

    public async Task Handle(RejeterVersementCommand request, CancellationToken cancellationToken)
    {
        var versement = await _versementRepository.GetByIdAsync(
            VersementId.From(request.VersementId), cancellationToken)
            ?? throw new InvalidOperationException($"Versement {request.VersementId} not found.");

        var dernierHashAudit = await _auditTrailService.GetDernierHash(versement.TontineId, cancellationToken);
        versement.Rejeter(request.Raison, dernierHashAudit, request.ActeurId);

        await _versementRepository.UpdateAsync(versement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
