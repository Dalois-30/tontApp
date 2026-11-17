namespace Application.TontineManagement.Commands.DeciderAdhesion;

using Application.Common;
using Application.PaymentManagement.Services;
using Domain.Common;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>Gestionnaire's decision on a membership request made while the tontine was active.</summary>
public sealed record DeciderAdhesionCommand(Guid TontineId, Guid MembreId, bool Accepter) : ICommand<Result>;

public sealed class DeciderAdhesionCommandHandler : ICommandHandler<DeciderAdhesionCommand, Result>
{
    private readonly ITontineRepository _tontines;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IVerrouTontine _verrou;

    public DeciderAdhesionCommandHandler(ITontineRepository tontines, IUnitOfWork unitOfWork, IVerrouTontine verrou)
    {
        _tontines = tontines;
        _unitOfWork = unitOfWork;
        _verrou = verrou;
    }

    public async Task<Result> Handle(DeciderAdhesionCommand request, CancellationToken cancellationToken)
    {
        // Same per-tontine lock as the payment writers: a round closed / opened or a member suspended /
        // admitted never interleaves with a declaration or validation checking who owes what.
        await using var verrou = await _verrou.VerrouillerAsync(TontineId.From(request.TontineId), cancellationToken);
        var tontine = await _tontines.GetByIdAsync(TontineId.From(request.TontineId), cancellationToken);
        if (tontine is null)
            return Result.Failure("Tontine not found.");

        try
        {
            var membreId = MemberId.From(request.MembreId);
            if (request.Accepter) tontine.AccepterAdhesion(membreId);
            else tontine.RefuserAdhesion(membreId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }

        await _tontines.UpdateAsync(tontine, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await verrou.ValiderAsync(cancellationToken);
        return Result.Success();
    }
}
