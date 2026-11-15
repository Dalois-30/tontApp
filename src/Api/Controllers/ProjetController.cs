namespace Api.Controllers;

using Api.Security;
using Application.ProjectManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Collective projects: tiers (paliers), instalments, payments, expenses and balance.
/// Roles are per project: its gestionnaire manages it; participants read it and manage their own tier.
/// </summary>
[ApiController]
[Route("api/v1/projets")]
[Produces("application/json")]
[Authorize]
public class ProjetController : ControllerBase
{
    private readonly ProjetService _service;

    public ProjetController(ProjetService service) => _service = service;

    private Guid UserId => User.GetUtilisateurId() ?? throw new AccesProjetRefuseException("User identity could not be determined.");
    private bool Admin => User.EstAdmin();

    /// <summary>Runs a use case and maps domain errors to HTTP statuses.</summary>
    private async Task<IActionResult> Run<T>(Func<Task<T>> action, Func<T, IActionResult>? ok = null)
    {
        try
        {
            var result = await action();
            return ok is null ? Ok(result) : ok(result);
        }
        catch (ProjetIntrouvableException ex) { return NotFound(new { error = ex.Message }); }
        catch (AccesProjetRefuseException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet]
    public Task<IActionResult> MesProjets(CancellationToken ct) => Run(() => _service.MesProjetsAsync(UserId, ct));

    [HttpPost]
    [Authorize(Roles = "Gestionnaire,Admin")]
    public Task<IActionResult> Creer([FromBody] CreerProjetRequest request, CancellationToken ct) =>
        Run(() => _service.CreerAsync(request, UserId, ct), p => StatusCode(StatusCodes.Status201Created, p));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Details(Guid id, CancellationToken ct) => Run(() => _service.DetailsAsync(id, UserId, Admin, ct));

    [HttpPost("{id:guid}/ouvrir")]
    public Task<IActionResult> Ouvrir(Guid id, CancellationToken ct) => Run(() => _service.OuvrirAsync(id, UserId, Admin, ct));

    [HttpPost("{id:guid}/clore")]
    public Task<IActionResult> Clore(Guid id, CancellationToken ct) => Run(() => _service.CloreAsync(id, UserId, Admin, ct));

    [HttpPost("{id:guid}/invitation")]
    public Task<IActionResult> GenererCode(Guid id, [FromQuery] int expirationJours = 30, CancellationToken ct = default) =>
        Run(() => _service.GenererCodeAsync(id, UserId, Admin, expirationJours, ct),
            r => Ok(new { code = r.Code, expiration = r.ExpireLe }));

    /// <summary>Preview of the project behind an invitation code (name, tiers, schedule).</summary>
    [HttpGet("apercu")]
    public Task<IActionResult> Apercu([FromQuery] string code, CancellationToken ct) => Run(() => _service.ApercuAsync(code ?? "", ct));

    [HttpPost("rejoindre")]
    public Task<IActionResult> Rejoindre([FromBody] RejoindreProjetRequest r, CancellationToken ct) =>
        Run(() => _service.RejoindreAsync(r.Code, r.Nom, r.Palier, UserId, ct));

    /// <summary>The caller changes their own tier: raise applied from the next instalment, decrease pending approval.</summary>
    [HttpPut("{id:guid}/mon-palier")]
    public Task<IActionResult> ChangerMonPalier(Guid id, [FromBody] ChangerPalierRequest r, CancellationToken ct) =>
        Run(() => _service.ChangerMonPalierAsync(id, r.Palier, UserId, ct),
            x => Ok(new { statut = x.Applique ? "applique" : "en_attente_validation", projet = x.Projet }));

    [HttpPost("{id:guid}/participants/{participantId:guid}/baisse-palier/{decision:regex(^(approuver|refuser)$)}")]
    public Task<IActionResult> DeciderBaisse(Guid id, Guid participantId, string decision, CancellationToken ct) =>
        Run(() => _service.DeciderBaisseAsync(id, participantId, decision == "approuver", UserId, Admin, ct));

    [HttpPost("{id:guid}/paiements")]
    public Task<IActionResult> EnregistrerPaiement(Guid id, [FromBody] PaiementProjetRequest r, CancellationToken ct) =>
        Run(() => _service.EnregistrerPaiementAsync(id, r.ParticipantId, r.Montant, r.Preuve ?? "", UserId, Admin, ct));

    [HttpPost("{id:guid}/paiements/{paiementId:guid}/{decision:regex(^(valider|rejeter)$)}")]
    public Task<IActionResult> DeciderPaiement(Guid id, Guid paiementId, string decision, [FromBody] MotifRequest? r, CancellationToken ct) =>
        Run(() => _service.DeciderPaiementAsync(id, paiementId, decision == "valider", r?.Motif, UserId, Admin, ct));

    [HttpPost("{id:guid}/depenses")]
    public Task<IActionResult> ProposerDepense(Guid id, [FromBody] DepenseRequest r, CancellationToken ct) =>
        Run(() => _service.ProposerDepenseAsync(id, r.Libelle, r.Montant, r.Justificatif ?? "", UserId, Admin, ct));

    [HttpPost("{id:guid}/depenses/{depenseId:guid}/{decision:regex(^(valider|rejeter)$)}")]
    public Task<IActionResult> DeciderDepense(Guid id, Guid depenseId, string decision, [FromBody] MotifRequest? r, CancellationToken ct) =>
        Run(() => _service.DeciderDepenseAsync(id, depenseId, decision == "valider", r?.Motif, UserId, Admin, ct));
}

public sealed record RejoindreProjetRequest(string Code, string Nom, decimal Palier);
public sealed record ChangerPalierRequest(decimal Palier);
public sealed record PaiementProjetRequest(Guid ParticipantId, decimal Montant, string? Preuve);
public sealed record DepenseRequest(string Libelle, decimal Montant, string? Justificatif);
public sealed record MotifRequest(string? Motif);
