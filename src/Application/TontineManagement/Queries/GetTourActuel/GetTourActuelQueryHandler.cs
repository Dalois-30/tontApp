namespace Application.TontineManagement.Queries.GetTourActuel;

using Application.Common;
using Application.TontineManagement.Services;
using Domain.PaymentManagement.Repositories;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

public sealed class GetTourActuelQueryHandler : IQueryHandler<GetTourActuelQuery, TourActuelDto?>
{
    private readonly ITontineRepository _tontineRepository;
    private readonly IVersementRepository _versementRepository;

    public GetTourActuelQueryHandler(
        ITontineRepository tontineRepository,
        IVersementRepository versementRepository)
    {
        _tontineRepository = tontineRepository;
        _versementRepository = versementRepository;
    }

    public async Task<TourActuelDto?> Handle(GetTourActuelQuery request, CancellationToken cancellationToken)
    {
        var tontine = await _tontineRepository.GetByIdReadOnlyAsync(
            TontineId.From(request.TontineId), cancellationToken);

        if (tontine is null)
            return null;

        var currentRound = tontine.Rounds.FirstOrDefault(r => !r.IsCompleted);
        if (currentRound is null)
            return null;

        var beneficiary = tontine.Members.FirstOrDefault(m => m.Id == currentRound.BeneficiaryId);

        // Single contribution rule (surplus carried over): a partial payment is not counted as paid.
        var situations = tontine.Situations(await _versementRepository.GetByTontineAsync(tontine.Id, cancellationToken));

        var activeMembers = tontine.Members
            .Where(m => m.Statut == StatutMembre.Actif)
            .ToList();

        // Owed by the active members who owe this round (not the beneficiary, nor a late member without catch-up).
        var dus = activeMembers
            .Select(m => (Membre: m, Tour: situations[m.Id.Value].Tour(currentRound.Id.Value)))
            .ToList();
        var totaux = tontine.TotauxDe(currentRound, situations);
        var (montantAttendu, montantCollecte, nombreAttendus, nombrePayes) =
            (totaux.Attendu, totaux.Collecte, totaux.NombreAttendus, totaux.NombrePayes);

        var payeurs = dus.Select(x => new PayeurDto(
            x.Membre.Id.Value,
            x.Membre.Name,
            x.Tour switch
            {
                null => x.Membre.Id == currentRound.BeneficiaryId ? "Bénéficiaire" : "Non dû",
                { Paye: true } => "Payé",
                { Valide: > 0 } => "Payé partiellement",
                _ => "En attente"
            })).ToList();

        var pourcentage = montantAttendu > 0
            ? (double)(montantCollecte / montantAttendu) * 100
            : 0;

        return new TourActuelDto(
            currentRound.Id.Value,
            currentRound.RoundNumber,
            beneficiary?.Name ?? "Inconnu",
            beneficiary?.Id.Value ?? Guid.Empty,
            currentRound.ScheduledDate,
            currentRound.DateLimite,
            montantCollecte,
            montantAttendu,
            nombrePayes,
            nombreAttendus,
            Math.Round(pourcentage, 1),
            payeurs);
    }
}
