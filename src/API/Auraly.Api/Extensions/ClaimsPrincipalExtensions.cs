using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Auraly.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Claim 'sub' no encontrado en el token.");
        return Guid.Parse(sub);
    }

    public static Guid GetTenantId(this ClaimsPrincipal principal)
    {
        var tenantId = principal.FindFirst("tenant_id")?.Value
            ?? throw new UnauthorizedAccessException("Claim 'tenant_id' no encontrado en el token.");
        return Guid.Parse(tenantId);
    }

    public static string GetUsername(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("username")?.Value ?? string.Empty;
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
    {
        return principal.FindAll("permission").Any(c => c.Value == permission)
            || principal.IsInRole("SuperAdmin");
    }
}
