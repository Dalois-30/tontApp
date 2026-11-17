namespace Application.NotificationManagement.EventHandlers;

using Domain.Gouvernance;
using Domain.NotificationManagement.ValueObjects;
using Domain.PaymentManagement.Events;
using Domain.TontineManagement.Repositories;
using Application.NotificationManagement.Services;
using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>
/// Handles PaiementEnRetardEvent → SMS rappel de retard de paiement via Outbox.
/// JAMAIS d'appel SMS direct depuis ce handler.
/// </summary>
public sealed class PaiementEnRetardEventHandler : INotificationHandler<PaiementEnRetardEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ITontineRepository _tontineRepository;
    private readonly IBureauRepository _bureauRepository;
    private readonly ILogger<PaiementEnRetardEventHandler> _logger;

    public PaiementEnRetardEventHandler(
        INotificationService notificationService,
        ITontineRepository tontineRepository,
        IBureauRepository bureauRepository,
        ILogger<PaiementEnRetardEventHandler> logger)
    {
        _notificationService = notificationService;
        _tontineRepository = tontineRepository;
        _bureauRepository = bureauRepository;
        _logger = logger;
    }

    public async Task Handle(PaiementEnRetardEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling PaiementEnRetard for tontine {TontineId}, payeur {PayeurId}, amount {Montant}",
            notification.TontineId, notification.PayeurId, notification.Montant);

        var tontine = await _tontineRepository.GetByIdReadOnlyAsync(notification.TontineId, cancellationToken);
        var nomTontine = tontine?.Name ?? "votre tontine";

        // Notifier le membre en retard
        var message = SmsTemplate.PaiementEnRetard(nomTontine, notification.Montant, notification.Devise);

        await _notificationService.PlanifierNotificationAsync(
            notification.PayeurId.Value.ToString(),
            NotificationType.RappelPaiement,
            message,
            cancellationToken);

        // Notifier le bureau : responsable et trésorier effectifs (votes échus compris), une fois chacun ;
        // sans bureau, le créateur de la tontine qui détient alors les deux rôles. Le membre en retard,
        // s'il siège au bureau, a déjà reçu son propre rappel.
        if (tontine is null) return;
        var bureau = await _bureauRepository.GetAsync(notification.TontineId.Value, cancellationToken);
        Guid[] roles;
        if (bureau is null)
            roles = [tontine.GestionnaireId.Value];
        else
        {
            var (responsable, tresorier) = bureau.RolesEffectifs(DateTime.UtcNow);
            roles = [responsable, tresorier];
        }

        var membre = tontine.Members.FirstOrDefault(m => m.Id.Value == notification.PayeurId.Value);
        var payeurCompte = membre?.UtilisateurId?.Value;
        var nomMembre = membre?.Name ?? "Un membre";
        var messageBureau = SmsTemplate.RetardPourBureau(nomTontine, nomMembre, notification.Montant, notification.Devise);

        foreach (var destinataire in roles.Distinct().Where(d => d != payeurCompte))
        {
            await _notificationService.PlanifierNotificationAsync(
                destinataire.ToString(),
                NotificationType.RappelPaiement,
                messageBureau,
                cancellationToken);
        }
    }
}
