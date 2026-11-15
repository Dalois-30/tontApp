namespace Application.TontineManagement.Commands.ModifierReglesAdhesion;

using Application.Common;
using Domain.Common;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>Late-joining rules of a tontine: mode (Interdite | AvecValidation | Libre) and catch-up.</summary>
public sealed record ModifierReglesAdhesionCommand(Guid TontineId, string AdhesionEnCours, bool RattrapageAdhesionTardive) : ICommand<Result>;

public sealed class ModifierReglesAdhesionCommandHandler : ICommandHandler<ModifierReglesAdhesionCommand, Result>
{
    private readonly ITontineRepository _tontines;
    private readonly IUnitOfWork _uow;

    public ModifierReglesAdhesionCommandHandler(ITontineRepository tontines, IUnitOfWork uow)
    {
        _tontines = tontines;
        _uow = uow;
    }

    public async Task<Result> Handle(ModifierReglesAdhesionCommand r, CancellationToken ct)
    {
        if (!Enum.TryParse<ModeAdhesionEnCours>(r.AdhesionEnCours, true, out var mode))
            return Result.Failure("AdhesionEnCours must be Interdite, AvecValidation or Libre.");
        var tontine = await _tontines.GetByIdAsync(TontineId.From(r.TontineId), ct);
        if (tontine is null) return Result.Failure("Tontine not found.");
        try { tontine.ModifierReglesAdhesion(mode, r.RattrapageAdhesionTardive); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
        await _tontines.UpdateAsync(tontine, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
