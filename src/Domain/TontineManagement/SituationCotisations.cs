namespace Domain.TontineManagement;

using Domain.TontineManagement.Entities;

/// <summary>A member's payment as the contribution rule sees it: its round, its amount, validated or pending.</summary>
/// <remarks>Rejected payments are left out by the caller: they count for nothing.</remarks>
public sealed record VersementCotisation(Guid TourId, decimal Montant, bool Valide);

/// <summary>
/// Situation of a member on one round they owe, after the surplus carry-over.
/// <see cref="Valide"/> includes the surplus carried from other rounds; <see cref="EnAttente"/> is what was
/// declared on this round and waits for validation; <see cref="Reste"/> = Du − Valide − EnAttente (≥ 0);
/// <see cref="Reporte"/>: the part of <see cref="Valide"/> carried over from the surplus of other rounds.
/// </summary>
public sealed record SituationTourCotisation(Round Tour, decimal Du, decimal Valide, decimal EnAttente, decimal Reste,
    decimal Reporte = 0)
{
    /// <summary>Validated (directly or by carry-over) up to the contribution.</summary>
    public bool Paye => Valide >= Du;
    /// <summary>Nothing more can be declared: validated + pending covers the contribution.</summary>
    public bool Couvert => Reste <= 0;
    /// <summary>Pending money beyond what the round still owes (it will become surplus once validated).</summary>
    public decimal EnAttenteExcedent => Math.Max(0, EnAttente - Math.Max(0, Du - Valide));
}

/// <summary>
/// Situation of a member over the rounds they owe (<see cref="Tontine.ToursDusPar"/>), oldest first.
/// <see cref="Avance"/>: validated money beyond every round owed so far; it will cover the next rounds.
/// <see cref="EnAttenteHorsTours"/>: pending money declared on rounds the member does not (or no longer) owe;
/// kept visible, never dropped.
/// </summary>
public sealed record SituationCotisations(IReadOnlyList<SituationTourCotisation> Tours, decimal Avance,
    decimal EnAttenteHorsTours = 0)
{
    public SituationTourCotisation? Tour(Guid tourId) => Tours.FirstOrDefault(t => t.Tour.Id.Value == tourId);

    /// <summary>All the member's money waiting for validation, on any round.</summary>
    public decimal EnAttenteTotal => Tours.Sum(t => t.EnAttente) + EnAttenteHorsTours;

    /// <summary>Pending money beyond what the rounds still owe (per-round excess + pending on rounds not owed).</summary>
    public decimal EnAttenteExcedent => Tours.Sum(t => t.EnAttenteExcedent) + EnAttenteHorsTours;

    /// <summary>
    /// The single contribution rule of a tontine. Each round owed first keeps the validated money paid on it,
    /// up to the contribution. What is validated beyond (on any round) is carried over: to the arrears, oldest
    /// first, then to the open round, then kept as an advance for the rounds to come. Pending money stays on
    /// its round, "to validate", and is never carried. Pure computation: payments are never moved.
    /// </summary>
    public static SituationCotisations Calculer(
        IReadOnlyList<Round> toursDus, decimal cotisation, IEnumerable<VersementCotisation> versements)
    {
        var liste = versements.Where(v => v.Montant > 0).ToList();
        var tours = toursDus.OrderBy(r => r.RoundNumber).ToList();
        var n = tours.Count;
        var valide = new decimal[n];
        var enAttente = new decimal[n];
        var reporte = new decimal[n];
        var indexes = new Dictionary<Guid, int>();
        for (var i = 0; i < n; i++) indexes[tours[i].Id.Value] = i;

        decimal surplus = 0;
        foreach (var groupe in liste.Where(v => v.Valide).GroupBy(v => v.TourId))
        {
            var total = groupe.Sum(v => v.Montant);
            if (indexes.TryGetValue(groupe.Key, out var i))
            {
                valide[i] = Math.Min(total, cotisation);
                surplus += total - valide[i];
            }
            else
            {
                surplus += total; // paid on a round not owed: all of it is surplus
            }
        }
        decimal horsTours = 0;
        foreach (var v in liste.Where(v => !v.Valide))
            if (indexes.TryGetValue(v.TourId, out var i))
                enAttente[i] += v.Montant;
            else
                horsTours += v.Montant;

        // Carry-over: arrears oldest first, then the open round (the most recent one owed).
        for (var i = 0; i < n && surplus > 0; i++)
        {
            var part = Math.Min(cotisation - valide[i], surplus);
            if (part <= 0) continue;
            valide[i] += part;
            reporte[i] = part;
            surplus -= part;
        }

        var situation = Enumerable.Range(0, n)
            .Select(i => new SituationTourCotisation(tours[i], cotisation, valide[i], enAttente[i],
                Math.Max(0, cotisation - valide[i] - enAttente[i]), reporte[i]))
            .ToList();
        return new SituationCotisations(situation, surplus, horsTours);
    }
}
