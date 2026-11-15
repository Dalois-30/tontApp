namespace Application.PaymentManagement.Commands.CreateVersement;

using Application.PaymentManagement.Services;
using Application.Common;
using Domain.Common;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;

public sealed class CreateVersementCommandHandler : ICommandHandler<CreateVersementCommand, Guid>
{
    private readonly IVersementRepository _versementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _auditTrailService;

    public CreateVersementCommandHandler(
        IVersementRepository versementRepository,
        IUnitOfWork unitOfWork,
        IAuditTrailService auditTrailService)
    {
        _versementRepository = versementRepository;
        _unitOfWork = unitOfWork;
        _auditTrailService = auditTrailService;
    }

    public async Task<Guid> Handle(CreateVersementCommand request, CancellationToken cancellationToken)
    {
        var lastVersement = await _versementRepository.GetLastByTontineAsync(
            TontineId.From(request.TontineId), cancellationToken);
        var hashPrecedent = lastVersement?.HashCourant ?? string.Empty;

        var versement = Versement.Create(
            TontineId.From(request.TontineId),
            TourId.From(request.TourId),
            PayeurId.From(request.PayeurId),
            Montant.Create(request.Montant, request.Devise),
            hashPrecedent,
            await _auditTrailService.GetDernierHash(TontineId.From(request.TontineId), cancellationToken));

        await _versementRepository.AddAsync(versement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return versement.Id.Value;
    }
}
