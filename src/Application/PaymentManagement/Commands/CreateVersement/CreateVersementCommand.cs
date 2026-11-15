namespace Application.PaymentManagement.Commands.CreateVersement;

using Application.Common;

public sealed record CreateVersementCommand(
    Guid TontineId,
    Guid PayeurId,
    Guid TourId,
    decimal Montant,
    string Devise = Domain.Common.Devises.ParDefaut) : ICommand<Guid>;
