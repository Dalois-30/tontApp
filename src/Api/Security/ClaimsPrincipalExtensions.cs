namespace Api.Security;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Returns the authenticated user's id (JWT "sub"), or null if absent/invalid.</summary>
    public static Guid? GetUtilisateurId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public static bool EstAdmin(this ClaimsPrincipal user) => user.IsInRole("Admin");
}
