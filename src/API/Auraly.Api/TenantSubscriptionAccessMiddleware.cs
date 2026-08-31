using System.Security.Claims;
using Auraly.Infrastructure.Persistence;

namespace Auraly.Api;

public sealed class TenantSubscriptionAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        SqlTenantSubscriptionAccessStore subscriptions)
    {
        var cancellationToken = context.RequestAborted;
        if (!ShouldCheck(context.Request) || context.User.Identity?.IsAuthenticated != true
            || !TryTenantId(context.User, out var tenantId))
        {
            await next(context);
            return;
        }

        if (!await subscriptions.IsSuspendedAsync(tenantId, cancellationToken))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["X-Auraly-Subscription-Status"] = "Suspended";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://auralyapp.co/problems/subscription-suspended",
            title = "La suscripción está suspendida",
            status = StatusCodes.Status402PaymentRequired,
            detail = "El periodo de gracia terminó. Puedes consultar Auraly y entrar a Suscripción para pagar y reactivar la operación.",
            actionUrl = "/dashboard/subscription"
        }, cancellationToken);
    }

    public static bool ShouldCheck(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)) return false;
        var path = request.Path.Value ?? string.Empty;
        return !path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/v1/tenant-commercial", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/v1/support", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);
}
