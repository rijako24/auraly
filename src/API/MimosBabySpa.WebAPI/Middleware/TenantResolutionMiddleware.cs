using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
            Guid? tokenTenantId = null;
            if (Guid.TryParse(tenantClaim, out var parsedTenantId))
            {
                tokenTenantId = parsedTenantId;
                tenantContext.SetTenant(parsedTenantId);
            }

            var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(subClaim, out var userId))
                tenantContext.SetUser(userId);

            if (context.Request.Headers.TryGetValue("X-Business-Id", out var bizHeader)
                && Guid.TryParse(bizHeader, out var businessId))
            {
                var business = await unitOfWork.Businesses.GetByIdAsync(businessId);
                var canAccessAllTenants = context.User.HasPermission("tenants.read");

                if (business is not null
                    && (canAccessAllTenants || business.TenantId == tokenTenantId))
                {
                    tenantContext.SetBusiness(business.BusinessId);

                    if (canAccessAllTenants)
                    {
                        tenantContext.SetTenant(business.TenantId);
                        ReplaceTenantClaim(context, business.TenantId);
                    }
                }
            }
        }

        await _next(context);
    }

    private static void ReplaceTenantClaim(HttpContext context, Guid tenantId)
    {
        var identity = context.User.Identities.FirstOrDefault(i => i.IsAuthenticated);
        if (identity is null)
            return;

        var claims = identity.Claims
            .Where(c => c.Type != "tenant_id")
            .Append(new Claim("tenant_id", tenantId.ToString()))
            .ToList();

        var replacementIdentity = new ClaimsIdentity(
            claims,
            identity.AuthenticationType,
            identity.NameClaimType,
            identity.RoleClaimType);

        context.User = new ClaimsPrincipal(replacementIdentity);
    }
}
