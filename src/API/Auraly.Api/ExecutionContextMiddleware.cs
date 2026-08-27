using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Api;

internal sealed class ExecutionContextMiddleware(
    RequestDelegate next,
    ILogger<ExecutionContextMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        IAuralyExecutionContextAccessor executionContext,
        IExecutionAccessResolver directory)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (context.User.HasClaim(claim =>
                claim.Type == PosAuthenticationDefaults.DeviceIdClaim))
        {
            await next(context);
            return;
        }

        if (!TryGuid(context.User, ClaimTypes.NameIdentifier, out var userId))
        {
            await Problem(context, 401, "La sesión no contiene un usuario válido.");
            return;
        }
        executionContext.SetUser(userId);

        var hasTenantHeader = context.Request.Headers.TryGetValue(
            "X-Tenant-Id", out var tenantHeader);
        var hasBusinessHeader = context.Request.Headers.ContainsKey("X-Business-Id");

        Guid tenantId;
        if (hasTenantHeader)
        {
            if (!Guid.TryParse(tenantHeader.ToString(), out tenantId))
            {
                await Problem(context, 400, "El identificador del tenant no es válido.");
                return;
            }
        }
        else if (!TryGuid(context.User, "tenant_id", out tenantId))
        {
            await Problem(context, 401, "La sesión no contiene un tenant de identidad válido.");
            return;
        }

        Guid? businessId = null;
        if (context.Request.Headers.TryGetValue("X-Business-Id", out var businessHeader))
        {
            if (!Guid.TryParse(businessHeader.ToString(), out var parsedBusinessId))
            {
                await Problem(context, 400, "El identificador del negocio no es válido.");
                return;
            }
            businessId = parsedBusinessId;
        }

        var access = await directory.ResolveAccessAsync(
            userId, tenantId, businessId, context.RequestAborted);
        if (!access.IsAllowed)
        {
            await Problem(context, 403,
                "El usuario no puede trabajar en el tenant o negocio seleccionado.");
            return;
        }

        var identity = context.User.Identities.First(candidate => candidate.IsAuthenticated);
        Replace(identity, "tenant_id", new[] { tenantId.ToString("D") });
        Replace(identity, ClaimTypes.Role, access.Roles);
        Replace(identity, "permission", access.Permissions);
        Replace(identity, "business_id", businessId is { } selectedBusiness
            ? new[] { selectedBusiness.ToString("D") }
            : Array.Empty<string>());

        executionContext.SetTenant(tenantId);
        if (businessId is { } selected)
            executionContext.SetBusiness(selected);

        Activity.Current?.SetTag("auraly.tenant.id", tenantId);
        Activity.Current?.SetTag("auraly.business.id", businessId);
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId,
            ["BusinessId"] = businessId
        });
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            logger.LogInformation(
                new EventId(1100, "TenantRequestCompleted"),
                "Tenant request completed {Method} {Route} with {StatusCode} in {ElapsedMilliseconds} ms for tenant {TenantId} and business {BusinessId}",
                context.Request.Method,
                context.GetEndpoint()?.DisplayName ?? "unmatched",
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                tenantId,
                businessId);
        }
    }

    private static void Replace(
        ClaimsIdentity identity,
        string claimType,
        IEnumerable<string> values)
    {
        foreach (var claim in identity.FindAll(claimType).ToArray())
            identity.RemoveClaim(claim);
        foreach (var value in values.Distinct(StringComparer.Ordinal))
            identity.AddClaim(new Claim(claimType, value));
    }

    private static bool TryGuid(
        ClaimsPrincipal principal,
        string claimType,
        out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value);

    private static Task Problem(HttpContext context, int status, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = status == 403 ? "Acceso denegado" : "Contexto inválido",
            Detail = detail
        });
    }
}
