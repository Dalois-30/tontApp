namespace Application.PaymentManagement.Commands.InitierVersement;

using Application.PaymentManagement.Services;
using Application.Common;
using Domain.Common;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Ports;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;

public sealed class InitierVersementCommandHandler : ICommandHandler<InitierVersementCommand, Guid>
{
    private readonly IVersementRepository _versementRepository;
    private readonly IMobileMoneyGateway _mobileMoneyGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IVerrouTontine _verrou;

    public InitierVersementCommandHandler(
        IVersementRepository versementRepository,
        IMobileMoneyGateway mobileMoneyGateway,
        IUnitOfWork unitOfWork,
        IAuditTrailService auditTrailService,
        IVerrouTontine verrou)
    {
        _versementRepository = versementRepository;
        _mobileMoneyGateway = mobileMoneyGateway;
        _unitOfWork = unitOfWork;
        _auditTrailService = auditTrailService;
        _verrou = verrou;
    }

    public async Task<Guid> Handle(InitierVersementCommand request, CancellationToken cancellationToken)
    {
        // One versement write at a time per tontine: last hashes read and insert under the tontine's lock.
        await using var verrou = await _verrou.VerrouillerAsync(TontineId.From(request.TontineId), cancellationToken);

        // Build the hash chain: get the last versement for this tontine
        var lastVersement = await _versementRepository.GetLastByTontineAsync(
            TontineId.From(request.TontineId), cancellationToken);
        var hashPrecedent = lastVersement?.HashCourant ?? string.Empty;

        var montant = Montant.Create(request.Montant, request.Devise);

        var versement = Versement.Create(
            TontineId.From(request.TontineId),
            TourId.From(request.TourId),
            PayeurId.From(request.PayeurId),
            montant,
            hashPrecedent,
            await _auditTrailService.GetDernierHash(TontineId.From(request.TontineId), cancellationToken));

        await _versementRepository.AddAsync(versement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await verrou.ValiderAsync(cancellationToken);

        // Initiate mobile money payment (after persisting the versement)
        var mobileMoneyRequest = new MobileMoneyRequest(
            request.NumeroTelephone,
            montant.Valeur,
            montant.Devise,
            versement.Id.Value.ToString());

        await _mobileMoneyGateway.InitierPaiementAsync(mobileMoneyRequest, cancellationToken);

        return versement.Id.Value;
    }
}
