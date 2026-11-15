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
/// Member as seen in the current round. Statut: paye | en_attente | a_payer | en_retard | beneficiaire |
/// suspendu | adhesion_en_attente. ResteDu: contributions owed for earlier rounds (arrears, including the
/// rounds opened before a late member joined); ToursImpayes lists those round numbers.
/// </summary>
public sealed record DashMembreDto(Guid Id, string Nom, string? Telephone, string Statut, Guid? UtilisateurId, int Rang,
    decimal ResteDu, IReadOnlyList<int> ToursImpayes);

public sealed record DashTourDto(Guid Id, int Numero, DashMembreDto? Beneficiaire, string Status, DateTime DateOuverture, DateTime? DateCloture, DateTime DateLimite);

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
    string? GestionnaireNom);

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
    DateTime? DateConfirmation);

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
