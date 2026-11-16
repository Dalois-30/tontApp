namespace Api.Controllers;

using Api.Security;
using Application.Dashboard;
using Application.PaymentManagement.Commands.ConfirmVersement;
using Application.PaymentManagement.Commands.EnregistrerVersementManuel;
using Application.PaymentManagement.Commands.InitierVersement;
using Application.PaymentManagement.Commands.RejeterVersement;
using Application.TontineManagement.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>
/// Endpoints for managing versements (payments).
/// </summary>
[ApiController]
[Route("api/v1/versements")]
[Produces("application/json")]
[Authorize]
public class VersementController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITontineAccessService _accessService;
    private readonly FeatureFlags _features;
    private readonly IDashboardReadService _dashboard;

    public VersementController(
        IMediator mediator,
        ITontineAccessService accessService,
        IOptions<FeatureFlags> features,
        IDashboardReadService dashboard)
    {
        _dashboard = dashboard;
        _mediator = mediator;
        _accessService = accessService;
        _features = features.Value;
    }

    // ── POST /api/v1/versements/manuel ─────────────────────────────
    /// <summary>
    /// Declare a payment made outside the app (cash, mobile money...). Any member declares their own
    /// (<c>membreId</c> omitted or their own); the responsable or the trésorier can declare for any member
    /// (member without a smartphone). The round must be the open one or a past round the member still owes.
    /// The versement is created EN_ATTENTE and must then be validated by the trésorier via
    /// POST /api/v1/versements/{id}/valider.
    /// </summary>
    /// <response code="201">Payment recorded, pending validation. Body contains the versement id.</response>
    /// <response code="400">Validation error or business rule violation (already declared, round not due...).</response>
    /// <response code="403">Caller is not a member, or declares for someone else without being in the bureau.</response>
    /// <response code="404">Tontine not found.</response>
    [HttpPost("manuel")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnregistrerVersementManuel(
        [FromBody] EnregistrerVersementManuelRequest request,
        CancellationToken cancellationToken)
    {
        if (User.GetUtilisateurId() is not { } userId)
            return Unauthorized(new { error = "User identity could not be determined." });

        var acces = await _accessService.GetAccesAsync(request.TontineId, userId, cancellationToken);
        var admin = User.EstAdmin();
        if (AccessResults.Check(acces, NiveauAcces.Membre, admin) is { } denied)
            return denied;

        var membreId = request.MembreId ?? acces.MembreId;
        if (membreId is null)
            return BadRequest(new { error = "Choose the member who paid." });
        if (membreId != acces.MembreId && !acces.EstBureau && !admin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "You can only declare your own payments." });

        var preuve = string.Join(" · ", new[] { request.Moyen, request.Reference, request.DescriptionPreuve }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        if (preuve.Length == 0)
            return BadRequest(new { error = "Give the payment method or a receipt reference." });

        var command = new EnregistrerVersementManuelCommand(
            request.TontineId,
            request.TourId,
            membreId.Value,
            request.Montant,
            preuve,
            ActeurId: userId);

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return StatusCode(StatusCodes.Status201Created,
            new { versementId = result.Value, status = "en_attente_validation" });
    }

    // ── POST /api/v1/versements/{id}/valider ───────────────────────
    /// <summary>
    /// Validate (confirm) a pending versement after checking the money was received.
    /// Only the tontine's trésorier (or an admin) may validate.
    /// </summary>
    [HttpPost("{id:guid}/valider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Valider(
        Guid id,
        [FromBody] ValiderVersementRequest? request,
        CancellationToken cancellationToken)
    {
        if (User.GetUtilisateurId() is not { } userId)
            return Unauthorized(new { error = "User identity could not be determined." });

        var acces = await _accessService.GetAccesParVersementAsync(id, userId, cancellationToken);
        if (acces is null)
            return NotFound(new { error = "Versement not found." });
        if (AccessResults.CheckTresorier(acces, User.EstAdmin()) is { } denied)
            return denied;

        var reference = string.IsNullOrWhiteSpace(request?.Reference)
            ? $"CASH-{DateTime.UtcNow:yyyyMMddHHmmss}-{id.ToString()[..8]}"
            : request!.Reference!;

        try
        {
            await _mediator.Send(new ConfirmVersementCommand(id, reference, userId.ToString()), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new { status = "versement_valide", reference });
    }

    // ── POST /api/v1/versements/{id}/rejeter ───────────────────────
    /// <summary>
    /// Reject a pending versement (money not received, wrong amount...). Only the tontine's trésorier
    /// (or an admin), with a reason that every member can read.
    /// </summary>
    [HttpPost("{id:guid}/rejeter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rejeter(
        Guid id,
        [FromBody] RejeterVersementRequest? request,
        CancellationToken cancellationToken)
    {
        if (User.GetUtilisateurId() is not { } userId)
            return Unauthorized(new { error = "User identity could not be determined." });

        var acces = await _accessService.GetAccesParVersementAsync(id, userId, cancellationToken);
        if (acces is null)
            return NotFound(new { error = "Versement not found." });
        if (AccessResults.CheckTresorier(acces, User.EstAdmin()) is { } denied)
            return denied;

        if (string.IsNullOrWhiteSpace(request?.Raison))
            return BadRequest(new { error = "A reason is required." });

        try
        {
            await _mediator.Send(new RejeterVersementCommand(id, request.Raison.Trim(), userId.ToString()), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new { status = "versement_rejete" });
    }

    // ── GET /api/v1/versements/{id}/statut ─────────────────────────
    /// <summary>Status of a versement, visible to the members and the gestionnaire of its tontine.</summary>
    [HttpGet("{id:guid}/statut")]
    public async Task<IActionResult> GetStatut(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUtilisateurId() is not { } userId)
            return Unauthorized(new { error = "User identity could not be determined." });

        var acces = await _accessService.GetAccesParVersementAsync(id, userId, cancellationToken);
        if (acces is null)
            return NotFound(new { error = "Versement not found." });
        if (AccessResults.Check(acces, NiveauAcces.Membre, User.EstAdmin()) is { } denied)
            return denied;

        return Ok(await _dashboard.GetVersementStatutAsync(id, cancellationToken));
    }

    // ── POST /api/v1/versements/initier ────────────────────────────
    /// <summary>
    /// Initiate a mobile money payment for a round contribution (feature flag Features:MobileMoney).
    /// The payer is the caller's membership in the tontine; confirmation arrives through the
    /// signed webhook /api/v1/webhooks/orange-money.
    /// </summary>
    [HttpPost("initier")]
    public async Task<IActionResult> Initier(
        [FromBody] InitierVersementRequest request,
        CancellationToken cancellationToken)
    {
        if (!_features.MobileMoney)
            return NotFound(new { error = "Mobile money payments are disabled." });

        if (User.GetUtilisateurId() is not { } userId)
            return Unauthorized(new { error = "User identity could not be determined." });

        var acces = await _accessService.GetAccesAsync(request.TontineId, userId, cancellationToken);
        if (!acces.Existe)
            return NotFound(new { error = "Tontine not found." });
        if (acces.MembreId is not { } membreId)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "You are not a member of this tontine." });

        var command = new InitierVersementCommand(
            request.TontineId,
            request.TourId,
            membreId,
            request.NumeroTelephone,
            request.Montant,
            request.Devise ?? Domain.Common.Devises.ParDefaut);

        var versementId = await _mediator.Send(command, cancellationToken);
        return Ok(new { versementId });
    }
}

/// <summary>
/// <c>MembreId</c> omitted: the caller declares their own payment. The proof is built from
/// <c>Moyen</c>, <c>Reference</c> and <c>DescriptionPreuve</c> (at least one is required).
/// </summary>
public sealed record EnregistrerVersementManuelRequest(
    Guid TontineId,
    Guid TourId,
    Guid? MembreId,
    decimal Montant,
    string? DescriptionPreuve = null,
    string? Moyen = null,
    string? Reference = null);

public sealed record InitierVersementRequest(
    Guid TontineId,
    Guid TourId,
    string NumeroTelephone,
    decimal Montant,
    string? Devise);

public sealed record ValiderVersementRequest(string? Reference);

public sealed record RejeterVersementRequest(string Raison);
