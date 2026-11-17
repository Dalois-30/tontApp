namespace Application.Dashboard;

// Read models consumed by the Next.js dashboard (dashboard/lib/types.ts) and the mobile app.
// Property names are serialized in camelCase and must stay aligned with the TypeScript types.

public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize, int TotalPages)
{
    public static PageResult<T> From(IEnumerable<T> all, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var list = all.ToList();
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PageResult<T>(items, list.Count, page, pageSize, Math.Max(1, (int)Math.Ceiling(list.Count / (double)pageSize)));
    }
}

/// <summary>
/// Member as seen in the current round. Statut: paye | paye_partiel | en_attente | a_payer | en_retard | non_du | beneficiaire |
/// suspendu | adhesion_en_attente. ResteDu: contributions owed for earlier rounds (arrears, including the
/// rounds opened before a late member joined); ToursImpayes lists those round numbers.
/// Statut is computed on amounts: paye_partiel = some payment validated, but less than the contribution.
/// A round is settled once validated + pending covers the contribution; a partly paid past round stays in
/// ToursImpayes with its remainder (RestesImpayes, aligned with ToursImpayes).
/// ResteTour: what is still to declare on the open round (contribution − validated − pending);
/// EnAttenteTour: amount declared on the open round and waiting for validation.
/// All of these are computed after the surplus carry-over (SituationCotisations): validated money beyond a
/// round's contribution covers the arrears (oldest first), then the open round; what is left is Avance,
/// which will cover the next rounds once opened (sent for every member, suspended ones included).
/// EnAttenteTotal: all the member's money waiting for validation (any round, rounds no longer owed included);
/// EnAttenteExcedent: the part of it beyond what the rounds still owe (it will become surplus once validated).
/// ReporteTour: validated money carried into the open round from other rounds' surplus;
/// EnAttenteExcedentTour: pending money on the open round beyond what it still owes.
/// Statut non_du: the member does not owe the open round (late member without catch-up).
/// </summary>
public sealed record DashMembreDto(Guid Id, string Nom, string? Telephone, string Statut, Guid? UtilisateurId, int Rang,
    decimal ResteDu, IReadOnlyList<int> ToursImpayes,
    decimal ResteTour = 0, decimal EnAttenteTour = 0, IReadOnlyList<decimal>? RestesImpayes = null,
    decimal Avance = 0, decimal EnAttenteTotal = 0, decimal EnAttenteExcedent = 0,
    decimal ReporteTour = 0, decimal EnAttenteExcedentTour = 0);

/// <summary>
/// A round. Totals from the shared contribution rule, over the active members who owe the round (same figures
/// as GET /tours/actuel): MontantCollecte = validated after carry-over; MontantAttendu = contributions owed;
/// NombreAttendus = members owing it; NombrePayes = members whose validated money covers the contribution.
/// </summary>
public sealed record DashTourDto(Guid Id, int Numero, DashMembreDto? Beneficiaire, string Status, DateTime DateOuverture, DateTime? DateCloture, DateTime DateLimite,
    decimal MontantCollecte = 0, decimal MontantAttendu = 0, int NombreAttendus = 0, int NombrePayes = 0);

public sealed record DashTontineDto(
    Guid Id,
    string Nom,
    string Description,
    decimal MontantCotisation,
    string Devise,
    string Periodicite,
    int MaxMembres,
    string Status,
    IReadOnlyList<DashMembreDto> Membres,
    IReadOnlyList<DashTourDto> Tours,
    DateTime CreatedAt,
    Guid GestionnaireId,
    string? GestionnaireNom,
    string AdhesionEnCours,
    bool RattrapageAdhesionTardive,
    // Roles of the caller on this tontine ("responsable", "tresorier", "admin"); set by the bureau's dashboard only.
    IReadOnlyList<string>? MesRoles = null);

public sealed record DashVersementDto(
    Guid Id,
    decimal Montant,
    Guid PayeurId,
    string PayeurNom,
    Guid TourId,
    int? TourNumero,
    string Status,
    string? Reference,
    DateTime DateCreation,
    DateTime? DateConfirmation,
    /// <summary>What the declarant gave as proof (method, reference), from the VersementManuel audit entry.</summary>
    string? Preuve = null,
    /// <summary>Reason of a rejection, from the VersementRejete audit entry.</summary>
    string? Motif = null);

public sealed record DashAuditEntryDto(Guid Id, string Action, string ActeurId, string? ActeurNom, DateTime Timestamp, string Payload, string Hash);

public sealed record DashAuditResult(IReadOnlyList<DashAuditEntryDto> Entries, int TotalCount, int Page, int PageSize, int TotalPages, bool ChaineValide);

public sealed record DashAlerteDto(string Id, string Type, string Message, string Severity, DateTime Timestamp, bool Resolved);

public sealed record DashAdminMetrics(
    int TontinesActives,
    int TotalMembres,
    decimal VolumeFcfaSemaine,
    int SmsEnvoyesSemaine,
    int SmsEchecsSemaine,
    double TauxErreurApi,
    IReadOnlyList<DashAlerteDto> Alertes);

public sealed record DashTontineListItem(Guid Id, string Nom, string Status, int MembresCount, decimal MontantCotisation, string Periodicite, DateTime CreatedAt);

public sealed record RetardataireDto(Guid MembreId, string Nom, string? Telephone, int JoursRetard);

public sealed record VersementStatutDto(Guid VersementId, string Statut, decimal Montant, DateTime DateCreation, DateTime? DateConfirmation);
