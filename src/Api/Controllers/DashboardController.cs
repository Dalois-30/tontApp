namespace Api.Controllers;

using Api.Security;
using Application.Dashboard;
using Application.TontineManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Read endpoints backing the Next.js dashboard (gestionnaire and admin spaces).
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardReadService _dashboard;
    private readonly ITontineAccessService _access;

    public DashboardController(IDashboardReadService dashboard, ITontineAccessService access)
    {
        _dashboard = dashboard;
        _access = access;
    }

    // ── Bureau (responsable, trésorier) ─────────────────────────────
    // The "gestionnaire" space is the bureau's: open to whoever is responsable or trésorier of a tontine
    // (whatever their platform role), or an administrator. Same rights as the mobile API (TontineAccessService).

    /// <summary>
    /// Tontine of the dashboard: <paramref name="tontineId"/> if given (the caller must sit on its bureau),
    /// otherwise the caller's most recent active tontine among those where they sit on the bureau.
    /// The response carries the caller's roles (<c>mesRoles</c>).
    /// </summary>
    [HttpGet("gestionnaire/tontine")]
    public async Task<IActionResult> GetTontine([FromQuery] Guid? tontineId, CancellationToken ct)
    {
        var (tontine, error) = await ResolveTontineAsync(tontineId, ct);
        return error ?? Ok(tontine);
    }

    /// <summary>
    /// Tontines where the caller sits on the bureau (responsable or trésorier), with their roles on each,
    /// the default tontine first. An administrator gets the tontines where they sit (any other one is
    /// opened with an explicit <c>tontineId</c>). Empty list when there is none.
    /// </summary>
    [HttpGet("gestionnaire/tontines")]
    public async Task<IActionResult> GetTontinesBureau(CancellationToken ct)
    {
        if (User.GetUtilisateurId() is not { } userId)
            return Unauthorized(new { error = "User identity could not be determined." });
        return Ok(await _dashboard.GetTontinesBureauAsync(userId, User.EstAdmin(), ct));
    }

    [HttpGet("gestionnaire/versements")]
    public async Task<IActionResult> GetVersements(
        [FromQuery] Guid? tontineId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (tontine, error) = await ResolveTontineAsync(tontineId, ct);
        if (error is not null) return error;
        return Ok(await _dashboard.GetVersementsAsync(tontine!.Id, page, pageSize, ct));
    }

    [HttpGet("gestionnaire/audit")]
    public async Task<IActionResult> GetAudit(
        [FromQuery] Guid? tontineId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (tontine, error) = await ResolveTontineAsync(tontineId, ct);
        if (error is not null) return error;
        return Ok(await _dashboard.GetAuditAsync(tontine!.Id, page, pageSize, ct));
    }

    // ── Admin ───────────────────────────────────────────────────────

    [HttpGet("admin/metrics")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetMetrics(CancellationToken ct) =>
        Ok(await _dashboard.GetAdminMetricsAsync(ct));

    [HttpGet("admin/tontines")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdminTontines([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await _dashboard.GetAdminTontinesAsync(page, pageSize, ct));

    [HttpGet("admin/alertes")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdminAlertes([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await _dashboard.GetAdminAlertesAsync(page, pageSize, ct));

    private async Task<(DashTontineDto? Tontine, IActionResult? Error)> ResolveTontineAsync(Guid? tontineId, CancellationToken ct)
    {
        if (User.GetUtilisateurId() is not { } userId)
            return (null, Unauthorized(new { error = "User identity could not be determined." }));

        var admin = User.EstAdmin();
        if (tontineId is { } id)
        {
            var acces = await _access.GetAccesAsync(id, userId, ct);
            if (!acces.Existe)
                return (null, NotFound(new { error = "Tontine not found." }));
            if (!admin && !acces.EstBureau)
                return (null, Interdit("Only the responsable or the treasurer of this tontine can open its dashboard."));
        }

        var tontine = await _dashboard.GetTontineGestionnaireAsync(userId, tontineId, admin, ct);
        if (tontine is not null)
            return (tontine, null);

        // No tontine on the caller's bureau: a platform gestionnaire (or admin) may create one; anyone else
        // has no business in the bureau's space.
        return admin || User.IsInRole("Gestionnaire")
            ? (null, NotFound(new { error = "Aucune tontine trouvée." }))
            : (null, Interdit("Vous n'êtes responsable ni trésorier d'aucune tontine."));
    }

    private ObjectResult Interdit(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = message });
}
