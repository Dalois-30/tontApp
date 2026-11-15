namespace Application.PaymentManagement.Queries.GetMesVersements;

using Application.Common;
using Application.PaymentManagement.Queries.GetVersementsByRound;

/// <summary>
/// Versements of the authenticated user across all the tontines they belong to.
/// </summary>
public sealed record GetMesVersementsQuery(
    Guid UtilisateurId,
    Guid? TontineId = null) : IQuery<IReadOnlyList<VersementDto>>;
