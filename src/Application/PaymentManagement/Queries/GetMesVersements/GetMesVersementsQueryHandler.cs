namespace Application.PaymentManagement.Queries.GetMesVersements;

using Application.Common;
using Application.PaymentManagement.Queries.GetVersementsByRound;
using Domain.IdentityManagement.ValueObjects;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// A versement's PayeurId is the payer's <b>MemberId</b> within the tontine. A user may hold one
/// membership per tontine, so we resolve all of them first. Versements created before this rule
/// (PayeurId = UtilisateurId) are still returned for history.
/// </summary>
public sealed class GetMesVersementsQueryHandler
    : IQueryHandler<GetMesVersementsQuery, IReadOnlyList<VersementDto>>
{
    private readonly IVersementRepository _versementRepository;
    private readonly ITontineRepository _tontineRepository;

    public GetMesVersementsQueryHandler(
        IVersementRepository versementRepository,
        ITontineRepository tontineRepository)
    {
        _versementRepository = versementRepository;
        _tontineRepository = tontineRepository;
    }

    public async Task<IReadOnlyList<VersementDto>> Handle(
        GetMesVersementsQuery request,
        CancellationToken cancellationToken)
    {
        var utilisateurId = UtilisateurId.From(request.UtilisateurId);
        var tontines = await _tontineRepository.GetByMembreUtilisateurIdReadOnlyAsync(utilisateurId, cancellationToken);

        var payeurIds = tontines
            .SelectMany(t => t.Members)
            .Where(m => m.UtilisateurId is not null && m.UtilisateurId == utilisateurId)
            .Select(m => m.Id.Value)
            .Append(request.UtilisateurId) // legacy versements
            .Distinct();

        var versements = new List<Versement>();
        foreach (var payeurId in payeurIds)
            versements.AddRange(await _versementRepository.GetByPayeurAsync(PayeurId.From(payeurId), cancellationToken));

        IEnumerable<Versement> filtered = versements;
        if (request.TontineId.HasValue)
        {
            var tontineId = TontineId.From(request.TontineId.Value);
            filtered = filtered.Where(v => v.TontineId == tontineId);
        }

        return filtered
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new VersementDto(
                v.Id.Value,
                v.TontineId.Value,
                v.PayeurId.Value,
                v.TourId.Value,
                v.Montant.Valeur,
                v.Montant.Devise,
                v.Statut.ToString(),
                v.ReferenceExterne,
                v.CreatedAt,
                v.ConfirmedAt))
            .ToList();
    }
}
