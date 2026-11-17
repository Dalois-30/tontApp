namespace Infrastructure.Jobs;

using Application.NotificationManagement.Services;
using Application.TontineManagement.Services;
using Domain.Gouvernance;
using Domain.NotificationManagement.ValueObjects;
using Domain.PaymentManagement.Repositories;
using Domain.TontineManagement;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire cron job: once a round's deadline has passed, one SMS to the effective responsable and
/// trésorier of the tontine (once if the same person; the creator when there is no bureau yet) listing
/// the active members late on that round (unpaid or partial after the surplus carry-over, i.e. not covered
/// by validated + pending money: exactly the late-payers screen) and the total they still owe.
/// Nothing is sent when nobody is late. A bureau member who is the only late member gets no recap about themself.
///
/// Rounds whose deadline fell in the last <see cref="FenetreJours"/> days are considered, closed or not, in
/// active tontines and in tontines completed by the closing of their last round: a day without the job
/// only delays the recap. Each recipient gets each round's recap exactly once, ever (notification reference
/// per (recipient, round), unique in the database), even if the job runs again or twice at the same time.
/// SMS go through the outbox.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed class RecapRetardsBureauJob
{
    /// <summary>How many days back a missed recap is still sent (yesterday included).</summary>
    public const int FenetreJours = 7;

    private readonly ITontineRepository _tontineRepository;
    private readonly IVersementRepository _versementRepository;
    private readonly IBureauRepository _bureauRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RecapRetardsBureauJob> _logger;

    public RecapRetardsBureauJob(
        ITontineRepository tontineRepository,
        IVersementRepository versementRepository,
        IBureauRepository bureauRepository,
        INotificationService notificationService,
        ILogger<RecapRetardsBureauJob> logger)
    {
        _tontineRepository = tontineRepository;
        _versementRepository = versementRepository;
        _bureauRepository = bureauRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Reference of the recap of a round for one recipient: the key that makes it unique.</summary>
    public static string Reference(Guid tourId, Guid destinataire) => $"recap-retards:{tourId:N}:{destinataire:N}";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("RecapRetardsBureauJob: checking rounds whose deadline has passed...");

        var maintenant = DateTime.UtcNow;
        var aujourdhui = maintenant.Date;
        var debut = aujourdhui.AddDays(-FenetreJours);
        var tontines = (await _tontineRepository.GetByStatusReadOnlyAsync(TontineStatus.Active, cancellationToken))
            .Concat(await _tontineRepository.GetByStatusReadOnlyAsync(TontineStatus.Completed, cancellationToken));
        var notificationsSent = 0;

        foreach (var tontine in tontines)
        {
            try
            {
                // Closed or not: a round whose deadline has passed still has its late members.
                var tours = tontine.Rounds
                    .Where(r => r.DateLimite.Date >= debut && r.DateLimite.Date < aujourdhui)
                    .OrderBy(r => r.RoundNumber).ToList();
                if (tours.Count > 0)
                    notificationsSent += await RecapitulerAsync(tontine, tours, maintenant, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One faulty tontine must not deprive the following ones of their recap.
                _logger.LogError(ex, "RecapRetardsBureauJob: recap failed for tontine {TontineId}", tontine.Id.Value);
            }
        }

        _logger.LogInformation("RecapRetardsBureauJob: {Count} recap notifications planned", notificationsSent);
    }

    private async Task<int> RecapitulerAsync(Tontine tontine, IReadOnlyList<Domain.TontineManagement.Entities.Round> tours,
        DateTime maintenant, CancellationToken cancellationToken)
    {
        var situations = tontine.Situations(await _versementRepository.GetByTontineAsync(tontine.Id, cancellationToken));
        var devise = tontine.ContributionAmount?.Currency ?? Domain.Common.Devises.ParDefaut;

        var bureau = await _bureauRepository.GetAsync(tontine.Id.Value, cancellationToken);
        Guid[] destinataires = bureau is null
            ? [tontine.GestionnaireId.Value]
            : RolesDistincts(bureau.RolesEffectifs(maintenant));

        var envoyes = 0;
        foreach (var tour in tours)
        {
            var retards = tontine.GetActiveMembers()
                .Select(m => (Membre: m, Situation: situations.TryGetValue(m.Id.Value, out var s) ? s.Tour(tour.Id.Value) : null))
                .Where(x => x.Situation is { Reste: > 0 })
                .ToList();
            if (retards.Count == 0)
                continue;

            var message = SmsTemplate.RecapRetardsBureau(tontine.Name, tour.RoundNumber,
                retards.Select(x => x.Membre.Name).ToList(), retards.Sum(x => x.Situation!.Reste), devise);

            foreach (var destinataire in destinataires)
            {
                // A bureau member late alone gets no alert about themself.
                if (retards.All(x => x.Membre.UtilisateurId?.Value == destinataire))
                    continue;

                var resultat = await _notificationService.PlanifierNotificationUniqueAsync(
                    destinataire.ToString(), NotificationType.RecapRetards, message,
                    Reference(tour.Id.Value, destinataire), cancellationToken);

                if (resultat == ResultatPlanification.Planifiee)
                    envoyes++;
                else if (resultat == ResultatPlanification.NonPlanifiee)
                    _logger.LogWarning(
                        "RecapRetardsBureauJob: recap of tontine {TontineId} round {NumeroTour} not sent to {Destinataire} (no account or phone, or SMS opt-out)",
                        tontine.Id.Value, tour.RoundNumber, destinataire);
            }
        }
        return envoyes;
    }

    private static Guid[] RolesDistincts((Guid ResponsableId, Guid TresorierId) roles)
        => roles.ResponsableId == roles.TresorierId ? [roles.ResponsableId] : [roles.ResponsableId, roles.TresorierId];
}
