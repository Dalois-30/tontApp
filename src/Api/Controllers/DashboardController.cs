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

    // ── Gestionnaire ────────────────────────────────────────────────

    /// <summary>
    /// Tontine of the dashboard: <paramref name="tontineId"/> if given (must be managed by the caller),
    /// otherwise the caller's most recent active tontine.
    /// </summary>
    [HttpGet("gestionnaire/tontine")]
    [Authorize(Roles = "Gestionnaire,Admin")]
    public async Task<IActionResult> GetTontine([FromQuery] Guid? tontineId, CancellationToken ct)
    {
        var (tontine, error) = await ResolveTontineAsync(tontineId, ct);
        return error ?? Ok(tontine);
    }

    [HttpGet("gestionnaire/versements")]
    [Authorize(Roles = "Gestionnaire,Admin")]
    public async Task<IActionResult> GetVersements(
        [FromQuery] Guid? tontineId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (tontine, error) = await ResolveTontineAsync(tontineId, ct);
        if (error is not null) return error;
        return Ok(await _dashboard.GetVersementsAsync(tontine!.Id, page, pageSize, ct));
    }

    [HttpGet("gestionnaire/audit")]
    [Authorize(Roles = "Gestionnaire,Admin")]
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

        if (tontineId is { } id)
        {
            var acces = await _access.GetAccesAsync(id, userId, ct);
            if (AccessResults.Check(acces, NiveauAcces.Gestionnaire, User.EstAdmin()) is { } denied)
                return (null, denied);
        }

        var tontine = await _dashboard.GetTontineGestionnaireAsync(userId, tontineId, ct);
        return tontine is null
            ? (null, NotFound(new { error = "Aucune tontine trouvée." }))
            : (tontine, null);
    }
}
