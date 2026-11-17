namespace Application.TontineManagement.Queries.GetTourActuel;

/// <summary>
/// The open round with its payment progress, computed with the tontine's single contribution rule
/// (validated surplus carried over from other rounds; pending money is never counted as collected).
/// Only the active members who owe the round are expected (the beneficiary and a late member without
/// catch-up are not): <see cref="MontantAttendu"/> = contribution × <see cref="NombreAttendus"/>;
/// <see cref="MontantCollecte"/> = validated money after carry-over, capped at the contribution per member;
/// <see cref="NombrePayes"/> = members whose validated money (after carry-over) covers the contribution —
/// a partial payment is not counted. Same figures as the round totals of the tontine details (DashTourDto).
/// </summary>
public sealed record TourActuelDto(
    Guid TourId,
    int Numero,
    string BeneficiaireNom,
    Guid BeneficiaireId,
    DateTime DateOuverture,
    DateTime? DateCloture,
    decimal MontantCollecte,
    decimal MontantAttendu,
    int NombrePayes,
    int NombreAttendus,
    double PourcentageComplete,
    IReadOnlyList<PayeurDto> Payeurs);

/// <summary>
/// An active member in the open round. Statut: "Payé" (validated after carry-over ≥ contribution),
/// "Payé partiellement" (some validated, less than the contribution), "En attente" (nothing validated yet,
/// possibly a declaration waiting for validation), "Bénéficiaire" (receives this round, owes nothing),
/// "Non dû" (does not owe this round: late member without catch-up).
/// </summary>
public sealed record PayeurDto(
    Guid MembreId,
    string Nom,
    string Statut);
