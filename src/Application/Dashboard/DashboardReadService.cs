namespace Application.Dashboard;

using Application.NotificationManagement.Services;
using Application.PaymentManagement.Services;
using Application.TontineManagement.Services;
using Domain.Gouvernance;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.NotificationManagement.Repositories;
using Domain.NotificationManagement.ValueObjects;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement;
using Domain.TontineManagement.Entities;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Read models for the web dashboard and the gestionnaire screens of the mobile app.
/// Access control is done by the API layer (TontineAccess filter / AccessResults) before calling this.
/// </summary>
public interface IDashboardReadService
{
    /// <summary>
    /// Tontine shown on the bureau's dashboard, with the caller's roles (<see cref="DashTontineDto.MesRoles"/>):
    /// the requested one (access is checked by the caller), else among the tontines where the caller is
    /// responsable or trésorier, the most recent active one, else the most recent. Null when there is none.
    /// </summary>
    Task<DashTontineDto?> GetTontineGestionnaireAsync(Guid utilisateurId, Guid? tontineId, bool admin, CancellationToken ct = default);
    Task<DashTontineDto?> GetTontineAsync(Guid tontineId, CancellationToken ct = default);
    Task<PageResult<DashVersementDto>> GetVersementsAsync(Guid tontineId, int page, int pageSize, CancellationToken ct = default);
    Task<DashAuditResult> GetAuditAsync(Guid tontineId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<RetardataireDto>?> GetRetardatairesAsync(Guid tontineId, Guid tourId, CancellationToken ct = default);
    /// <summary>Plans an SMS reminder for each given member of the tontine. Returns the number of reminders planned.</summary>
    Task<int> RelancerAsync(Guid tontineId, IReadOnlyCollection<Guid> membreIds, CancellationToken ct = default);
    Task<VersementStatutDto?> GetVersementStatutAsync(Guid versementId, CancellationToken ct = default);
    Task<DashAdminMetrics> GetAdminMetricsAsync(CancellationToken ct = default);
    Task<PageResult<DashTontineListItem>> GetAdminTontinesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<PageResult<DashAlerteDto>> GetAdminAlertesAsync(int page, int pageSize, CancellationToken ct = default);
}

public sealed class DashboardReadService : IDashboardReadService
{
    private readonly ITontineRepository _tontines;
    private readonly IVersementRepository _versements;
    private readonly IAuditEntryRepository _audit;
    private readonly IAuditTrailService _auditTrail;
    private readonly IUtilisateurRepository _utilisateurs;
    private readonly INotificationRepository _notifications;
    private readonly INotificationService _notificationService;
    private readonly ITontineAccessService _access;
    private readonly IBureauRepository _bureaux;

    public DashboardReadService(
        ITontineRepository tontines,
        IVersementRepository versements,
        IAuditEntryRepository audit,
        IAuditTrailService auditTrail,
        IUtilisateurRepository utilisateurs,
        INotificationRepository notifications,
        INotificationService notificationService,
        ITontineAccessService access,
        IBureauRepository bureaux)
    {
        _tontines = tontines;
        _versements = versements;
        _audit = audit;
        _auditTrail = auditTrail;
        _utilisateurs = utilisateurs;
        _notifications = notifications;
        _notificationService = notificationService;
        _access = access;
        _bureaux = bureaux;
    }

    // ── Gestionnaire ────────────────────────────────────────────────

    public async Task<DashTontineDto?> GetTontineGestionnaireAsync(Guid utilisateurId, Guid? tontineId, bool admin, CancellationToken ct = default)
    {
        if (tontineId is { } id)
        {
            var demandee = await _tontines.GetByIdReadOnlyAsync(TontineId.From(id), ct);
            if (demandee is null) return null;
            var acces = await _access.GetAccesAsync(id, utilisateurId, ct);
            return await BuildTontineAsync(demandee, ct) with { MesRoles = RolesBureau(acces, admin) };
        }

        // Candidates: the tontines the caller created (no bureau yet ⇒ creator holds both roles) and those
        // where the bureau seats them. The access service has the last word (same rights as the mobile API):
        // a creator replaced as responsable and trésorier is dropped.
        // One instant for the bureau lookup and every access check: a vote reaching its deadline
        // in between cannot make the two disagree.
        var maintenant = DateTime.UtcNow;
        var creees = await _tontines.GetByGestionnaireIdReadOnlyAsync(UtilisateurId.From(utilisateurId), ct);
        var parId = creees.ToDictionary(t => t.Id.Value);
        var ids = parId.Keys
            .Concat(await _bureaux.GetGroupesOuSiegeAsync(utilisateurId, TypeGroupe.Tontine, maintenant, ct))
            .Distinct()
            .ToList();

        var siennes = new List<(Tontine Tontine, AccesTontine Acces)>();
        foreach (var candidat in ids)
        {
            var acces = await _access.GetAccesAsync(candidat, utilisateurId, maintenant, ct);
            if (!acces.EstBureau) continue;
            var t = parId.GetValueOrDefault(candidat) ?? await _tontines.GetByIdReadOnlyAsync(TontineId.From(candidat), ct);
            if (t is not null) siennes.Add((t, acces));
        }

        // Deterministic default: active first, then the most recent, then by id.
        var choisie = siennes
            .OrderByDescending(x => x.Tontine.Status == TontineStatus.Active)
            .ThenByDescending(x => x.Tontine.CreatedAt)
            .ThenBy(x => x.Tontine.Id.Value)
            .FirstOrDefault();
        return choisie.Tontine is null
            ? null
            : await BuildTontineAsync(choisie.Tontine, ct) with { MesRoles = RolesBureau(choisie.Acces, admin) };
    }

    /// <summary>Roles of the caller on the tontine, as the gouvernance screen names them.</summary>
    private static IReadOnlyList<string> RolesBureau(AccesTontine acces, bool admin)
    {
        var roles = new List<string>();
        if (acces.Existe && acces.Niveau >= NiveauAcces.Gestionnaire) roles.Add("responsable");
        if (acces.Existe && acces.EstTresorier) roles.Add("tresorier");
        if (admin) roles.Add("admin");
        return roles;
    }

    public async Task<DashTontineDto?> GetTontineAsync(Guid tontineId, CancellationToken ct = default)
    {
        var tontine = await _tontines.GetByIdReadOnlyAsync(TontineId.From(tontineId), ct);
        return tontine is null ? null : await BuildTontineAsync(tontine, ct);
    }

    public async Task<PageResult<DashVersementDto>> GetVersementsAsync(Guid tontineId, int page, int pageSize, CancellationToken ct = default)
    {
        var tontine = await _tontines.GetByIdReadOnlyAsync(TontineId.From(tontineId), ct);
        if (tontine is null)
            return PageResult<DashVersementDto>.From([], page, pageSize);

        var versements = await _versements.GetByTontineAsync(tontine.Id, ct);
        var noms = await NomsPayeursAsync(tontine, versements, ct);
        var numeros = tontine.Rounds.ToDictionary(r => r.Id.Value, r => r.RoundNumber);

        var items = versements
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new DashVersementDto(
                v.Id.Value,
                v.Montant.Valeur,
                v.PayeurId.Value,
                noms.GetValueOrDefault(v.PayeurId.Value, "Inconnu"),
                v.TourId.Value,
                numeros.TryGetValue(v.TourId.Value, out var n) ? n : null,
                StatutVersement(v.Statut),
                v.ReferenceExterne,
                v.CreatedAt,
                v.ConfirmedAt,
                ChampAudit(v, AuditAction.VersementManuel, "DescriptionPreuve"),
                ChampAudit(v, AuditAction.VersementRejete, "Raison")));

        return PageResult<DashVersementDto>.From(items, page, pageSize);
    }

    public async Task<DashAuditResult> GetAuditAsync(Guid tontineId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var id = TontineId.From(tontineId);

        var total = await _audit.CountByTontineAsync(id, ct);
        var entries = await _audit.GetByTontinePagedAsync(id, page, pageSize, ct);
        var chaine = await _auditTrail.VerifierChaine(id, ct);

        var noms = new Dictionary<string, string?>();
        foreach (var acteur in entries.Select(e => e.ActeurId).Distinct())
            noms[acteur] = await NomUtilisateurAsync(acteur, ct);

        var dtos = entries.Select(e => new DashAuditEntryDto(
            e.Id.Value, e.Action.ToString(), e.ActeurId, noms[e.ActeurId], e.Timestamp, e.Payload, e.HashCourant)).ToList();

        return new DashAuditResult(dtos, total, page, pageSize, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)), chaine.EstIntegre);
    }

    public async Task<IReadOnlyList<RetardataireDto>?> GetRetardatairesAsync(Guid tontineId, Guid tourId, CancellationToken ct = default)
    {
        var tontine = await _tontines.GetByIdReadOnlyAsync(TontineId.From(tontineId), ct);
        var tour = tontine?.Rounds.FirstOrDefault(r => r.Id.Value == tourId);
        if (tontine is null || tour is null)
            return null;

        var versements = await _versements.GetByTontineAndTourAsync(tontine.Id, TourId.From(tourId), ct);
        // Up to date once validated + pending covers the contribution (a partial payment still owes the rest).
        var aJour = versements.Where(v => v.Statut != VersementStatus.Echoue)
            .GroupBy(v => v.PayeurId.Value)
            .Where(g => g.Sum(v => v.Montant.Valeur) >= tontine.ContributionAmount.Amount)
            .Select(g => g.Key).ToHashSet();
        var joursRetard = Math.Max(0, (int)(DateTime.UtcNow - tour.DateLimite).TotalDays);

        var result = new List<RetardataireDto>();
        foreach (var m in tontine.Members.Where(m => m.Statut == StatutMembre.Actif
                                                     && m.Id != tour.BeneficiaryId
                                                     && !aJour.Contains(m.Id.Value)))
        {
            result.Add(new RetardataireDto(m.Id.Value, m.Name, await TelephoneAsync(m, ct), joursRetard));
        }
        return result;
    }

    public async Task<int> RelancerAsync(Guid tontineId, IReadOnlyCollection<Guid> membreIds, CancellationToken ct = default)
    {
        var tontine = await _tontines.GetByIdReadOnlyAsync(TontineId.From(tontineId), ct);
        if (tontine is null)
            return 0;

        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var cotisation = tontine.ContributionAmount.Amount;
        var devise = tontine.ContributionAmount.Currency;
        var messageComplet = $"Rappel {tontine.Name} : votre cotisation de {cotisation.ToString("N0", fr)} {devise} est attendue. Merci de la remettre au gestionnaire.";

        // A member who already paid part of the open round is reminded of the rest only
        // (same rule as the details: contribution − validated − pending).
        var tourOuvert = tontine.Rounds.FirstOrDefault(r => !r.IsCompleted);
        var dejaVerse = tourOuvert is null
            ? new Dictionary<Guid, decimal>()
            : (await _versements.GetByTontineAsync(tontine.Id, ct))
                .Where(v => v.TourId.Value == tourOuvert.Id.Value && v.Statut != VersementStatus.Echoue)
                .GroupBy(v => v.PayeurId.Value)
                .ToDictionary(g => g.Key, g => g.Sum(v => v.Montant.Valeur));

        var count = 0;
        foreach (var membre in tontine.Members.Where(m => membreIds.Contains(m.Id.Value)))
        {
            var reste = cotisation - dejaVerse.GetValueOrDefault(membre.Id.Value);
            var message = reste > 0 && reste < cotisation
                ? $"Rappel {tontine.Name} : il reste {reste.ToString("N0", fr)} {devise} à payer sur votre cotisation de {cotisation.ToString("N0", fr)} {devise}. Merci de le remettre au gestionnaire."
                : messageComplet;
            if (await _notificationService.PlanifierNotificationAsync(membre.Id.Value.ToString(), NotificationType.RappelPaiement, message, ct))
                count++;
        }
        return count;
    }

    public async Task<VersementStatutDto?> GetVersementStatutAsync(Guid versementId, CancellationToken ct = default)
    {
        var v = await _versements.GetByIdAsync(VersementId.From(versementId), ct);
        return v is null ? null : new VersementStatutDto(v.Id.Value, StatutVersement(v.Statut), v.Montant.Valeur, v.CreatedAt, v.ConfirmedAt);
    }

    // ── Admin ───────────────────────────────────────────────────────

    public async Task<DashAdminMetrics> GetAdminMetricsAsync(CancellationToken ct = default)
    {
        var tontines = await _tontines.GetAllReadOnlyAsync(ct);
        var depuis = DateTime.UtcNow.AddDays(-7);

        decimal volume = 0;
        foreach (var t in tontines.Where(t => t.Status == TontineStatus.Active))
        {
            var versements = await _versements.GetByTontineAsync(t.Id, ct);
            volume += versements.Where(v => v.Statut == VersementStatus.Confirme && v.ConfirmedAt >= depuis).Sum(v => v.Montant.Valeur);
        }

        var notifications = (await _notifications.GetAllAsync(ct)).Where(n => n.CreatedAt >= depuis).ToList();
        var alertes = await BuildAlertesAsync(ct);

        return new DashAdminMetrics(
            tontines.Count(t => t.Status == TontineStatus.Active),
            tontines.Sum(t => t.Members.Count),
            volume,
            notifications.Count(n => n.Statut == NotificationStatus.Envoyee),
            notifications.Count(n => n.Statut == NotificationStatus.Echouee),
            0, // API error rate is exposed by Prometheus (/metrics), not computed here
            alertes.Take(5).ToList());
    }

    public async Task<PageResult<DashTontineListItem>> GetAdminTontinesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var tontines = await _tontines.GetAllReadOnlyAsync(ct);
        var items = tontines
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new DashTontineListItem(t.Id.Value, t.Name, t.Status.ToString(), t.Members.Count,
                t.ContributionAmount.Amount, t.Periodicity.ToString(), t.CreatedAt));
        return PageResult<DashTontineListItem>.From(items, page, pageSize);
    }

    public async Task<PageResult<DashAlerteDto>> GetAdminAlertesAsync(int page, int pageSize, CancellationToken ct = default)
        => PageResult<DashAlerteDto>.From(await BuildAlertesAsync(ct), page, pageSize);

    /// <summary>
    /// Alerts derived from live data: broken audit chain (critical), overdue rounds (warning),
    /// failed SMS (info).
    /// </summary>
    private async Task<List<DashAlerteDto>> BuildAlertesAsync(CancellationToken ct)
    {
        var alertes = new List<DashAlerteDto>();
        var now = DateTime.UtcNow;

        foreach (var t in await _tontines.GetAllReadOnlyAsync(ct))
        {
            if (t.Status is TontineStatus.Active or TontineStatus.Suspended)
            {
                var chaine = await _auditTrail.VerifierChaine(t.Id, ct);
                if (!chaine.EstIntegre)
                    alertes.Add(new DashAlerteDto($"audit-{t.Id.Value}", "Audit",
                        $"Chaîne d'audit invalide pour « {t.Name} » ({chaine.NombreEntreesInvalides} entrée(s)).",
                        "critical", now, false));
            }

            var tour = t.Rounds.FirstOrDefault(r => !r.IsCompleted);
            if (t.Status == TontineStatus.Active && tour is not null && tour.DateLimite < now)
                alertes.Add(new DashAlerteDto($"retard-{tour.Id.Value}", "Retard",
                    $"Tour {tour.RoundNumber} de « {t.Name} » dépassé depuis le {tour.DateLimite:dd/MM/yyyy}.",
                    "warning", tour.DateLimite, false));
        }

        foreach (var n in (await _notifications.GetAllAsync(ct)).Where(n => n.Statut == NotificationStatus.Echouee))
            alertes.Add(new DashAlerteDto($"sms-{n.Id.Value}", "SMS", $"Échec d'envoi ({n.Type}) au destinataire {n.DestinataireId}.",
                "info", n.CreatedAt, false));

        return alertes.OrderByDescending(a => a.Severity == "critical").ThenByDescending(a => a.Timestamp).ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task<DashTontineDto> BuildTontineAsync(Tontine tontine, CancellationToken ct)
    {
        var tourOuvert = tontine.Rounds.FirstOrDefault(r => !r.IsCompleted);
        var versements = await _versements.GetByTontineAsync(tontine.Id, ct);

        // Amounts per (member, round): only confirmed payments count as paid, pending ones are "to validate".
        // A round is settled for a member once validated + pending covers the contribution.
        var montants = versements
            .Where(v => v.Statut != VersementStatus.Echoue)
            .GroupBy(v => (v.PayeurId.Value, v.TourId.Value))
            .ToDictionary(g => g.Key, g => new MontantsTour(
                g.Where(v => v.Statut == VersementStatus.Confirme).Sum(v => v.Montant.Valeur),
                g.Where(v => v.Statut == VersementStatus.EnAttente).Sum(v => v.Montant.Valeur)));
        var cotisation = tontine.ContributionAmount.Amount;
        decimal Reste(MontantsTour m) => Math.Max(0, cotisation - m.Valide - m.EnAttente);

        var membres = new List<DashMembreDto>();
        foreach (var m in tontine.Members.OrderBy(m => m.Statut == StatutMembre.EnAttente).ThenBy(m => m.Rang))
        {
            var arrieres = m.Statut == StatutMembre.Actif
                ? tontine.ToursDusPar(m.Id)
                    .Where(r => r.Id != tourOuvert?.Id)
                    .Select(r => (r.RoundNumber, Reste: Reste(montants.GetValueOrDefault((m.Id.Value, r.Id.Value), MontantsTour.Zero))))
                    .Where(x => x.Reste > 0)
                    .ToList()
                : [];

            var statut = StatutMembreTour(m, tourOuvert, montants, cotisation);
            var duTourOuvert = tourOuvert is not null && m.Statut == StatutMembre.Actif && m.Id != tourOuvert.BeneficiaryId;
            var duTour = duTourOuvert
                ? montants.GetValueOrDefault((m.Id.Value, tourOuvert!.Id.Value), MontantsTour.Zero)
                : MontantsTour.Zero;

            membres.Add(new DashMembreDto(m.Id.Value, m.Name, await TelephoneAsync(m, ct),
                statut, m.UtilisateurId?.Value, m.Rang,
                arrieres.Sum(x => x.Reste), arrieres.Select(x => x.RoundNumber).ToList(),
                duTourOuvert ? Reste(duTour) : 0, duTour.EnAttente,
                arrieres.Select(x => x.Reste).ToList()));
        }

        var parId = membres.ToDictionary(m => m.Id);
        var tours = tontine.Rounds.OrderBy(r => r.RoundNumber).Select(r => new DashTourDto(
            r.Id.Value, r.RoundNumber, parId.GetValueOrDefault(r.BeneficiaryId.Value),
            r.IsCompleted ? "Cloture" : "Ouvert", r.ScheduledDate, r.IsCompleted ? r.DateLimite : null, r.DateLimite)).ToList();

        return new DashTontineDto(tontine.Id.Value, tontine.Name, tontine.Description ?? string.Empty,
            tontine.ContributionAmount.Amount, tontine.ContributionAmount.Currency, tontine.Periodicity.ToString(),
            tontine.MaxMembers, tontine.Status.ToString(), membres, tours, tontine.CreatedAt,
            tontine.GestionnaireId.Value, await NomUtilisateurAsync(tontine.GestionnaireId.Value.ToString(), ct),
            tontine.AdhesionEnCours.ToString(), tontine.RattrapageAdhesionTardive);
    }

    private sealed record MontantsTour(decimal Valide, decimal EnAttente)
    {
        public static readonly MontantsTour Zero = new(0, 0);
    }

    /// <summary>
    /// Status of a member in the open round, computed on amounts: validated ≥ contribution → paye;
    /// some validated but less → paye_partiel (not a conventional payment); only pending → en_attente.
    /// </summary>
    private static string StatutMembreTour(Member m, Round? tour,
        IReadOnlyDictionary<(Guid, Guid), MontantsTour> montants, decimal cotisation)
    {
        if (m.Statut == StatutMembre.Suspendu) return "suspendu";
        if (m.Statut == StatutMembre.EnAttente) return "adhesion_en_attente";
        if (tour is null) return "a_payer";
        if (m.Id == tour.BeneficiaryId) return "beneficiaire";

        var siens = montants.GetValueOrDefault((m.Id.Value, tour.Id.Value), MontantsTour.Zero);
        if (siens.Valide >= cotisation && siens.Valide > 0) return "paye";
        if (siens.Valide > 0) return "paye_partiel";
        if (siens.EnAttente > 0) return "en_attente";
        return DateTime.UtcNow > tour.DateLimite ? "en_retard" : "a_payer";
    }

    /// <summary>A text field of the payload of the versement's last audit entry of the given action.</summary>
    private static string? ChampAudit(Versement v, AuditAction action, string champ)
    {
        var e = v.AuditTrail.Where(x => x.Action == action).OrderBy(x => x.Timestamp).LastOrDefault();
        if (e is null || string.IsNullOrWhiteSpace(e.Payload)) return null;
        try
        {
            using var d = System.Text.Json.JsonDocument.Parse(e.Payload);
            return d.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && d.RootElement.TryGetProperty(champ, out var x) && x.ValueKind == System.Text.Json.JsonValueKind.String
                && !string.IsNullOrWhiteSpace(x.GetString()) ? x.GetString() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static string StatutVersement(VersementStatus s) => s switch
    {
        VersementStatus.Confirme => "confirme",
        VersementStatus.Echoue => "rejete",
        _ => "en_attente"
    };

    private async Task<Dictionary<Guid, string>> NomsPayeursAsync(Tontine tontine, IEnumerable<Versement> versements, CancellationToken ct)
    {
        var noms = tontine.Members.ToDictionary(m => m.Id.Value, m => m.Name);
        // Legacy versements may carry a UtilisateurId as PayeurId.
        foreach (var id in versements.Select(v => v.PayeurId.Value).Distinct().Where(id => !noms.ContainsKey(id)))
            noms[id] = await NomUtilisateurAsync(id.ToString(), ct) ?? "Inconnu";
        return noms;
    }

    private async Task<string?> NomUtilisateurAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return id == "system" ? "Système" : null;
        var u = await _utilisateurs.GetByIdAsync(UtilisateurId.From(guid), ct);
        return u?.Nom;
    }

    private async Task<string?> TelephoneAsync(Member m, CancellationToken ct)
    {
        if (m.UtilisateurId is null) return null;
        var u = await _utilisateurs.GetByIdAsync(m.UtilisateurId, ct);
        return u?.Telephone.Value;
    }
}
