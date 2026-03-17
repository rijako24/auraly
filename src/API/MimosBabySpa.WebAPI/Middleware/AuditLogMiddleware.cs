using MimosBabySpa.Application.Identity.Interfaces;

namespace MimosBabySpa.WebAPI.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> AuditableMethods = new(StringComparer.OrdinalIgnoreCase)
        { "POST", "PUT", "PATCH", "DELETE" };

    public AuditLogMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IAuditService auditService)
    {
        await _next(context);

        if (!AuditableMethods.Contains(context.Request.Method))
            return;

        if (context.User.Identity?.IsAuthenticated != true)
            return;

        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            var pathSegments = context.Request.Path.Value?.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string>();
            var entityType = pathSegments.Length >= 2 ? pathSegments[1] : "Unknown";

            await auditService.LogAsync(
                action: $"{context.Request.Method} {context.Request.Path}",
                entityType: entityType,
                entityId: null,
                oldValues: null,
                newValues: null,
                ct: default);
        }
    }
}
