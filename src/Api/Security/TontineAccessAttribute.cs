namespace Api.Security;

using Application.TontineManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// Restricts an action to users having at least <see cref="Niveau"/> on the tontine whose id is
/// in the route value <see cref="RouteKey"/> (default "id"). Admins always pass.
/// Returns 404 when the tontine does not exist, 403 when access is insufficient.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TontineAccessAttribute : Attribute, IAsyncActionFilter
{
    public NiveauAcces Niveau { get; }
    public string RouteKey { get; init; } = "id";

    public TontineAccessAttribute(NiveauAcces niveau) => Niveau = niveau;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var userId = user.GetUtilisateurId();
        if (userId is null)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "User identity could not be determined." });
            return;
        }

        if (!Guid.TryParse(context.RouteData.Values[RouteKey]?.ToString(), out var tontineId))
        {
            context.Result = new BadRequestObjectResult(new { error = "Invalid tontine id." });
            return;
        }

        var service = context.HttpContext.RequestServices.GetRequiredService<ITontineAccessService>();
        var acces = await service.GetAccesAsync(tontineId, userId.Value, context.HttpContext.RequestAborted);

        var denied = AccessResults.Check(acces, Niveau, user.EstAdmin());
        if (denied is not null)
        {
            context.Result = denied;
            return;
        }

        await next();
    }
}

public static class AccessResults
{
    /// <summary>Returns an error result when access is denied, or null when allowed.</summary>
    public static IActionResult? Check(AccesTontine? acces, NiveauAcces requis, bool estAdmin)
    {
        if (acces is null || !acces.Existe)
            return new NotFoundObjectResult(new { error = "Tontine not found." });

        if (estAdmin || acces.Permet(requis))
            return null;

        return Interdit("You do not have access to this tontine.");
    }

    /// <summary>Validating or rejecting a payment: the trésorier of the tontine (or an admin) only.</summary>
    public static IActionResult? CheckTresorier(AccesTontine? acces, bool estAdmin)
    {
        if (acces is null || !acces.Existe)
            return new NotFoundObjectResult(new { error = "Tontine not found." });

        if (estAdmin || acces.EstTresorier)
            return null;

        return Interdit("Only the treasurer of this tontine can validate or reject a payment.");
    }

    private static ObjectResult Interdit(string message) =>
        new(new { error = message }) { StatusCode = StatusCodes.Status403Forbidden };
}
