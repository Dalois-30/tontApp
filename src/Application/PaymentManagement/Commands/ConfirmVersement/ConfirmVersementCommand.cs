namespace Application.PaymentManagement.Commands.ConfirmVersement;

using Application.Common;

public sealed record ConfirmVersementCommand(
    Guid VersementId,
    string ReferenceExterne,
    string ActeurId = "system") : ICommand;
