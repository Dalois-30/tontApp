namespace Api.Controllers;

using Api.Security;
using Application.Gouvernance;
using Application.ProjectManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Governance of a group (<c>projets</c> or <c>tontines</c>): bureau (responsable, trésorier), votes,
/// séance reports and the readable history of the group. Readable by every member.
/// </summary>
[ApiController]
[Route("api/v1/{type:regex(^(projets|tontines)$)}/{id:guid}")]
[Produces("application/json")]
[Authorize]
public class GouvernanceController : ControllerBase
{
    private readonly GouvernanceService _service;

    public GouvernanceController(GouvernanceService service) => _service = service;

    private Guid UserId => User.GetUtilisateurId() ?? throw new AccesProjetRefuseException("User identity could not be determined.");
    private bool Admin => User.EstAdmin();

    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (ProjetIntrouvableException ex) { return NotFound(new { error = ex.Message }); }
        catch (AccesProjetRefuseException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("gouvernance")]
    public Task<IActionResult> Consulter(string type, Guid id, CancellationToken ct) =>
        Run(() => _service.ConsulterAsync(GouvernanceService.TypeDepuis(type), id, UserId, Admin, ct));

    /// <summary>The responsable names the treasurer.</summary>
    [HttpPut("gouvernance/tresorier")]
    public Task<IActionResult> NommerTresorier(string type, Guid id, [FromBody] UtilisateurRequest r, CancellationToken ct) =>
        Run(() => _service.NommerTresorierAsync(GouvernanceService.TypeDepuis(type), id, r.UtilisateurId, UserId, Admin, ct));

    /// <summary>Platform administrator: reassign a role directly.</summary>
    [HttpPut("gouvernance/roles")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> ReattribuerRole(string type, Guid id, [FromBody] RoleRequest r, CancellationToken ct) =>
        Run(() => _service.ReattribuerRoleAsync(GouvernanceService.TypeDepuis(type), id, r, UserId, Admin, ct));

    [HttpPut("gouvernance/regles")]
    public Task<IActionResult> ModifierRegles(string type, Guid id, [FromBody] ReglesGouvernanceDto r, CancellationToken ct) =>
        Run(() => _service.ModifierReglesAsync(GouvernanceService.TypeDepuis(type), id, r, UserId, Admin, ct));

    /// <summary>Any member proposes a new responsable: opens a vote of the members.</summary>
    [HttpPost("votes/responsable")]
    public Task<IActionResult> ProposerResponsable(string type, Guid id, [FromBody] UtilisateurRequest r, CancellationToken ct) =>
        Run(() => _service.ProposerResponsableAsync(GouvernanceService.TypeDepuis(type), id, r.UtilisateurId, UserId, Admin, ct));

    [HttpPost("votes/{voteId:guid}")]
    public Task<IActionResult> Voter(string type, Guid id, Guid voteId, [FromBody] VoteRequest r, CancellationToken ct) =>
        Run(() => _service.VoterAsync(GouvernanceService.TypeDepuis(type), id, voteId, r.Pour, UserId, Admin, ct));

    /// <summary>Any member writes a séance report.</summary>
    [HttpPost("seances")]
    public Task<IActionResult> RedigerSeance(string type, Guid id, [FromBody] SeanceRequest r, CancellationToken ct) =>
        Run(() => _service.RedigerSeanceAsync(GouvernanceService.TypeDepuis(type), id, r, UserId, Admin, ct));

    /// <summary>History of the group, newest first. Filters: categorie, participantId, q (free text).</summary>
    [HttpGet("chronologie")]
    public Task<IActionResult> Chronologie(string type, Guid id, [FromQuery] string? categorie, [FromQuery] Guid? participantId,
        [FromQuery] string? q, CancellationToken ct) =>
        Run(() => _service.ChronologieAsync(GouvernanceService.TypeDepuis(type), id, categorie, participantId, q, UserId, Admin, ct));
}
