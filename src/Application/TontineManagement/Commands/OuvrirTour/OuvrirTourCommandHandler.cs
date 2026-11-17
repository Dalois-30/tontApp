namespace Application.TontineManagement.Commands.OuvrirTour;

using Application.Common;
using Application.PaymentManagement.Services;
using Domain.Common;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

public sealed class OuvrirTourCommandHandler : ICommandHandler<OuvrirTourCommand, Guid>
{
    private readonly ITontineRepository _tontineRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IVerrouTontine _verrou;

    public OuvrirTourCommandHandler(ITontineRepository tontineRepository, IUnitOfWork unitOfWork, IVerrouTontine verrou)
    {
        _tontineRepository = tontineRepository;
        _unitOfWork = unitOfWork;
        _verrou = verrou;
    }

    public async Task<Guid> Handle(OuvrirTourCommand request, CancellationToken cancellationToken)
    {
        // Same per-tontine lock as the payment writers: a round closed / opened or a member suspended /
        // admitted never interleaves with a declaration or validation checking who owes what.
        await using var verrou = await _verrou.VerrouillerAsync(TontineId.From(request.TontineId), cancellationToken);
        var tontine = await _tontineRepository.GetByIdAsync(
            TontineId.From(request.TontineId), cancellationToken)
            ?? throw new InvalidOperationException($"Tontine {request.TontineId} not found.");

        var round = tontine.OuvrirTour();

        await _tontineRepository.UpdateAsync(tontine, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await verrou.ValiderAsync(cancellationToken);

        return round.Id.Value;
    }
}
