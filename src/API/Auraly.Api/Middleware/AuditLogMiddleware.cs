using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> AuditableMethods = new(StringComparer.OrdinalIgnoreCase)
        { "POST", "PUT", "PATCH", "DELETE" };

    public AuditLogMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IAuditService auditService,
        ILogger<AuditLogMiddleware> logger)
    {
        await _next(context);

        // Draft mutations already persist user, version and idempotency receipts.
        // Avoid a second generic audit write on the latency-critical POS path.
        if (context.Request.Path.StartsWithSegments("/api/commerce/v1/pos/drafts"))
            return;

        if (!AuditableMethods.Contains(context.Request.Method))
            return;

        if (context.User.Identity?.IsAuthenticated != true)
            return;

        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            var pathSegments = context.Request.Path.Value?.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string>();
            var entityType = pathSegments.Length >= 2 ? pathSegments[1] : "Unknown";

            try
            {
                await auditService.LogAsync(
                    action: $"{context.Request.Method} {context.Request.Path}",
                    entityType: entityType,
                    entityId: null,
                    oldValues: null,
                    newValues: null,
                    ct: default);
            }
            catch (Exception exception)
            {
                // The mutation already completed and the response may have started.
                // Do not corrupt a successful response when audit persistence fails.
                logger.LogError(exception,
                    "Audit persistence failed after {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);
            }
        }
    }
}
