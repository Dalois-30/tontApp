namespace Domain.NotificationManagement.ValueObjects;

public enum NotificationType
{
    RappelPaiement,
    ConfirmationPaiement,
    OuvertureTour,
    ClotureTour,
    Bienvenue,
    Suspension,
    RecapHebdomadaire,
    MessagePersonnalise,
    ConfirmationAbonnement,
    RappelRenouvellement,
    RenouvellementReussi,
    RenouvellementEchoue,
    /// <summary>Late-payment recap sent to the bureau the day after a round's deadline.</summary>
    RecapRetards
}
