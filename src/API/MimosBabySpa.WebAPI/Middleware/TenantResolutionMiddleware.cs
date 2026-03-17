using System.IdentityModel.Tokens.Jwt;
using MimosBabySpa.Application.Common.Interfaces;

namespace MimosBabySpa.WebAPI.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(tenantClaim, out var tenantId))
                tenantContext.SetTenant(tenantId);

            var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (Guid.TryParse(subClaim, out var userId))
                tenantContext.SetUser(userId);

            if (context.Request.Headers.TryGetValue("X-Business-Id", out var bizHeader)
                && Guid.TryParse(bizHeader, out var businessId))
                tenantContext.SetBusiness(businessId);
        }

        await _next(context);
    }
}
