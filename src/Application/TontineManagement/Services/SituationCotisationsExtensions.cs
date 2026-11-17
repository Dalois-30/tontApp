namespace Application.TontineManagement.Services;

using Domain.PaymentManagement;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement;
using Domain.TontineManagement.Entities;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Totals of one round over the active members who owe it (beneficiary and members not owing it excluded):
/// Attendu = contributions owed; Collecte = validated after carry-over; NombreAttendus = members owing it;
/// NombrePayes = members whose validated money (after carry-over) covers the contribution.
/// </summary>
public sealed record TotauxTour(decimal Attendu, decimal Collecte, int NombreAttendus, int NombrePayes);

/// <summary>
/// Bridges stored versements to the tontine's single contribution rule (<see cref="SituationCotisations"/>).
/// Details, dashboard, late payers, SMS reminders, current round and the declaration check all go through here.
/// </summary>
public static class SituationCotisationsExtensions
{
    /// <summary>Situation of one member, from all the versements of the tontine (other payers are ignored).</summary>
    public static SituationCotisations SituationDe(this Tontine tontine, MemberId membreId, IEnumerable<Versement> versements)
        => tontine.SituationDe(membreId, versements
            .Where(v => v.PayeurId.Value == membreId.Value && v.Statut != VersementStatus.Echoue)
            .Select(v => new VersementCotisation(v.TourId.Value, v.Montant.Valeur, v.Statut == VersementStatus.Confirme)));

    /// <summary>Totals of a round, from the members' situations (same figures on every screen).</summary>
    public static TotauxTour TotauxDe(this Tontine tontine, Round tour, IReadOnlyDictionary<Guid, SituationCotisations> situations)
    {
        var dus = tontine.Members.Where(m => m.Statut == StatutMembre.Actif)
            .Select(m => situations[m.Id.Value].Tour(tour.Id.Value))
            .OfType<SituationTourCotisation>()
            .ToList();
        return new TotauxTour(dus.Sum(t => t.Du), dus.Sum(t => t.Valide), dus.Count, dus.Count(t => t.Paye));
    }

    /// <summary>Situation of every member of the tontine, by member id.</summary>
    public static IReadOnlyDictionary<Guid, SituationCotisations> Situations(this Tontine tontine, IEnumerable<Versement> versements)
    {
        var parPayeur = versements.Where(v => v.Statut != VersementStatus.Echoue).ToLookup(v => v.PayeurId.Value);
        return tontine.Members.ToDictionary(m => m.Id.Value, m => tontine.SituationDe(m.Id, parPayeur[m.Id.Value]));
    }
}
