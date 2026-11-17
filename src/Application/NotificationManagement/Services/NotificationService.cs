namespace Application.NotificationManagement.Services;

using Domain.Common;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using Domain.NotificationManagement;
using Domain.NotificationManagement.Repositories;
using Domain.NotificationManagement.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service for creating notification outbox messages.
/// NEVER sends SMS directly — always goes through the Outbox pattern.
/// Enforces rate limiting (max 10 SMS/member/day) and opt-out rules.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Creates a notification and adds it to the outbox for async processing.
    /// <paramref name="destinataireId"/> may be a phone number (E.164), a UtilisateurId or a MemberId:
    /// ids are resolved to the account's phone. Returns false (without throwing) when no phone can be
    /// found (member without account), the user opted out of non-critical SMS, or the rate limit is hit.
    /// </summary>
    Task<bool> PlanifierNotificationAsync(
        string destinataireId,
        NotificationType type,
        string contenu,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="PlanifierNotificationAsync"/>, but at most once per (recipient, <paramref name="reference"/>),
    /// ever: running again, or concurrently, plans nothing more (<see cref="ResultatPlanification.DejaPlanifiee"/>).
    /// </summary>
    Task<ResultatPlanification> PlanifierNotificationUniqueAsync(
        string destinataireId,
        NotificationType type,
        string contenu,
        string reference,
        CancellationToken cancellationToken = default);
}

public enum ResultatPlanification
{
    Planifiee,
    /// <summary>A notification with the same reference was already planned for this recipient.</summary>
    DejaPlanifiee,
    /// <summary>Not planned: no phone (no account), SMS opt-out, or daily quota reached.</summary>
    NonPlanifiee,
}

public sealed class NotificationService : INotificationService
{
    private const int MaxSmsParJourParMembre = 10;

    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<NotificationService> _logger;
    private readonly IUtilisateurRepository _utilisateurs;
    private readonly ITontineRepository _tontines;

    public NotificationService(
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        ILogger<NotificationService> logger,
        IUtilisateurRepository utilisateurs,
        ITontineRepository tontines)
    {
        _utilisateurs = utilisateurs;
        _tontines = tontines;
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<bool> PlanifierNotificationAsync(
        string destinataireId,
        NotificationType type,
        string contenu,
        CancellationToken cancellationToken = default)
        => await PlanifierAsync(destinataireId, type, contenu, null, cancellationToken) == ResultatPlanification.Planifiee;

    public Task<ResultatPlanification> PlanifierNotificationUniqueAsync(
        string destinataireId,
        NotificationType type,
        string contenu,
        string reference,
        CancellationToken cancellationToken = default)
        => PlanifierAsync(destinataireId, type, contenu, reference, cancellationToken);

    private async Task<ResultatPlanification> PlanifierAsync(
        string destinataireId,
        NotificationType type,
        string contenu,
        string? reference,
        CancellationToken cancellationToken)
    {
        var (telephone, optOut) = await ResoudreDestinataireAsync(destinataireId, cancellationToken);
        if (telephone is null)
        {
            _logger.LogInformation("No phone number for recipient {DestinataireId}: notification {Type} skipped.", destinataireId, type);
            return ResultatPlanification.NonPlanifiee;
        }
        destinataireId = telephone;

        // Critical notifications (payment confirmations) bypass opt-out and rate limits
        var notification = Notification.CreateFull(
            destinataireId,
            Canal.SMS,
            type,
            contenu);

        if (!notification.EstCritique() && optOut)
        {
            _logger.LogInformation("Recipient {DestinataireId} opted out of SMS: notification {Type} skipped.", destinataireId, type);
            return ResultatPlanification.NonPlanifiee;
        }

        if (notification.SoumiseAuQuota())
        {
            // Rate limit: max 10 SMS/member/day (non-critical only)
            var countToday = await _notificationRepository.CountTodayByDestinataireAsync(
                destinataireId, cancellationToken);

            if (countToday >= MaxSmsParJourParMembre)
            {
                _logger.LogWarning(
                    "Rate limit reached for member {DestinataireId}: {Count} SMS sent today (max {Max}). Notification dropped.",
                    destinataireId, countToday, MaxSmsParJourParMembre);
                return ResultatPlanification.NonPlanifiee;
            }
        }

        if (reference is not null)
        {
            notification.DefinirReference(reference);
            if (!await _notificationRepository.AjouterSiReferenceAbsenteAsync(notification, cancellationToken))
                return ResultatPlanification.DejaPlanifiee;
        }
        else
        {
            await _notificationRepository.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Notification {NotificationId} planned for {DestinataireId} (type: {Type})",
            notification.Id, destinataireId, type);

        return ResultatPlanification.Planifiee;
    }

    /// <summary>Phone number (E.164) and SMS opt-out flag of the recipient.</summary>
    private async Task<(string? Telephone, bool OptOut)> ResoudreDestinataireAsync(string destinataireId, CancellationToken ct)
    {
        if (!Guid.TryParse(destinataireId, out var id))
            return (destinataireId, false); // already a phone number

        var utilisateur = await _utilisateurs.GetByIdAsync(UtilisateurId.From(id), ct);
        if (utilisateur is null)
        {
            var compte = await _tontines.GetUtilisateurIdOfMembreAsync(MemberId.From(id), ct);
            if (compte is not null)
                utilisateur = await _utilisateurs.GetByIdAsync(compte, ct);
        }

        return utilisateur is null ? (null, false) : (utilisateur.Telephone.Value, utilisateur.SmsOptOut);
    }
}
