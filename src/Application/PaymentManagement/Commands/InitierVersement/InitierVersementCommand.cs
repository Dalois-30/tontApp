namespace Application.PaymentManagement.Commands.InitierVersement;

using Application.Common;

public sealed record InitierVersementCommand(
    Guid TontineId,
    Guid TourId,
    Guid PayeurId,
    string NumeroTelephone,
    decimal Montant,
    string Devise = Domain.Common.Devises.ParDefaut) : ICommand<Guid>;
