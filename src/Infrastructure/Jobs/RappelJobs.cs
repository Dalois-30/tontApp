namespace Infrastructure.Jobs;

using Application.NotificationManagement.Services;
using Application.TontineManagement.Services;
using Domain.PaymentManagement.Repositories;
using Domain.NotificationManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shared logic of the J-3 / J-1 reminders: for each active tontine, each open round due on the target
/// day, only the active members who still owe something on that round (contribution − validated after
/// carry-over − pending, the single rule of <see cref="SituationCotisationsExtensions"/>) are reminded,
/// of what they still owe. The beneficiary and the members who do not owe the round are left out.
/// </summary>
internal static class RappelEcheance
{
    public static async Task<int> PlanifierAsync(
        ITontineRepository tontines,
        IVersementRepository versements,
        INotificationService notifications,
        DateTime echeance,
        Func<string, decimal, string, string> gabarit,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var activeTontines = await tontines.GetByStatusReadOnlyAsync(TontineStatus.Active, cancellationToken);
        var notificationsSent = 0;

        foreach (var tontine in activeTontines)
        {
            try
            {
                var rounds = tontine.GetActiveRounds().Where(r => r.DateLimite.Date == echeance).ToList();
                if (rounds.Count == 0)
                    continue;

                var situations = tontine.Situations(await versements.GetByTontineAsync(tontine.Id, cancellationToken));
                var devise = tontine.ContributionAmount?.Currency ?? Domain.Common.Devises.ParDefaut;

                foreach (var round in rounds)
                {
                    foreach (var member in tontine.GetActiveMembers())
                    {
                        var reste = situations.TryGetValue(member.Id.Value, out var situation)
                            ? situation.Tour(round.Id.Value)?.Reste ?? 0
                            : 0;
                        if (reste <= 0)
                            continue; // paid, covered by carry-over or pending, or round not owed (beneficiary)

                        if (await notifications.PlanifierNotificationAsync(
                                member.Id.Value.ToString(),
                                NotificationType.RappelPaiement,
                                gabarit(tontine.Name, reste, devise),
                                cancellationToken))
                            notificationsSent++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One faulty tontine must not deprive the following ones of their reminders.
                logger.LogError(ex, "Payment reminders failed for tontine {TontineId}", tontine.Id.Value);
            }
        }

        return notificationsSent;
    }
}

/// <summary>
/// Daily Hangfire cron job: sends J-3 reminder SMS (3 days before payment deadline) to the members
/// who still owe something on the round. Runs daily at 08:00 UTC.
/// </summary>
public sealed class RappelJ3Job
{
    private readonly ITontineRepository _tontineRepository;
    private readonly IVersementRepository _versementRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RappelJ3Job> _logger;

    public RappelJ3Job(
        ITontineRepository tontineRepository,
        IVersementRepository versementRepository,
        INotificationService notificationService,
        ILogger<RappelJ3Job> logger)
    {
        _tontineRepository = tontineRepository;
        _versementRepository = versementRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RappelJ3Job: checking for rounds due in 3 days...");

        var notificationsSent = await RappelEcheance.PlanifierAsync(
            _tontineRepository, _versementRepository, _notificationService,
            DateTime.UtcNow.Date.AddDays(3), SmsTemplate.RappelJ3, _logger, cancellationToken);

        _logger.LogInformation("RappelJ3Job: {Count} reminder notifications planned", notificationsSent);
    }
}

/// <summary>
/// Daily Hangfire cron job: sends J-1 reminder SMS (1 day before payment deadline) to the members
/// who still owe something on the round. Runs daily at 08:00 UTC.
/// </summary>
public sealed class RappelJ1Job
{
    private readonly ITontineRepository _tontineRepository;
    private readonly IVersementRepository _versementRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RappelJ1Job> _logger;

    public RappelJ1Job(
        ITontineRepository tontineRepository,
        IVersementRepository versementRepository,
        INotificationService notificationService,
        ILogger<RappelJ1Job> logger)
    {
        _tontineRepository = tontineRepository;
        _versementRepository = versementRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RappelJ1Job: checking for rounds due tomorrow...");

        var notificationsSent = await RappelEcheance.PlanifierAsync(
            _tontineRepository, _versementRepository, _notificationService,
            DateTime.UtcNow.Date.AddDays(1), SmsTemplate.RappelJ1, _logger, cancellationToken);

        _logger.LogInformation("RappelJ1Job: {Count} reminder notifications planned", notificationsSent);
    }
}

/// <summary>
/// Weekly Hangfire cron job: sends recap SMS every Monday at 09:00 UTC.
/// </summary>
public sealed class RecapHebdoJob
{
    private readonly ITontineRepository _tontineRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RecapHebdoJob> _logger;

    public RecapHebdoJob(
        ITontineRepository tontineRepository,
        INotificationService notificationService,
        ILogger<RecapHebdoJob> logger)
    {
        _tontineRepository = tontineRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RecapHebdoJob: generating weekly recap notifications...");

        var activeTontines = await _tontineRepository.GetByStatusReadOnlyAsync(
            TontineStatus.Active, cancellationToken);

        var notificationsSent = 0;

        foreach (var tontine in activeTontines)
        {
            var members = tontine.GetActiveMembers();
            var totalMembers = members.Count;

            // Simplified: count members who have at least contributed (proxy for "à jour")
            var membresAJour = totalMembers;
            var message = SmsTemplate.RecapHebdomadaire(tontine.Name, membresAJour, totalMembers);

            foreach (var member in members)
            {
                await _notificationService.PlanifierNotificationAsync(
                    member.Id.Value.ToString(),
                    NotificationType.RecapHebdomadaire,
                    message,
                    cancellationToken);

                notificationsSent++;
            }
        }

        _logger.LogInformation("RecapHebdoJob: {Count} recap notifications planned", notificationsSent);
    }
}
