using System.Security.Claims;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api;

internal sealed record PosPortfolioDeviceContext(Guid UserId, Guid TenantId, Guid BusinessId,
    Guid WorkSessionId, IReadOnlySet<string> Permissions)
{
    public static async Task<PosPortfolioDeviceContext> RequireAsync(
        HttpContext context, WorkSessionService sessions, IUserService users,
        CancellationToken token)
    {
        static Guid Header(HttpContext context, string name) =>
            Guid.TryParse(context.Request.Headers[name], out var value) && value != Guid.Empty
                ? value
                : throw new WorkSessionForbiddenException($"El dispositivo no envió '{name}'.");

        var device = context.User.ToPosDeviceIdentity();
        var userId = Header(context, "X-Auraly-User-Id");
        var businessId = Header(context, "X-Auraly-Business-Id");
        var sessionId = Header(context, "X-Auraly-Work-Session-Id");
        await sessions.RequireActiveDeviceSessionAsync(
            userId, device.TenantId, businessId, device.DeviceId, sessionId, token);
        var permissions = await users.GetUserPermissionsAsync(userId, businessId, token);
        return new(userId, device.TenantId, businessId, sessionId,
            new HashSet<string>(permissions, StringComparer.Ordinal));
    }

    public void RequirePermission(string permission)
    {
        if (!Permissions.Contains(permission))
            throw new WorkSessionForbiddenException("El usuario no tiene permiso para gestionar esta cartera.");
    }

    public void RequirePayment(Guid businessId, Guid? workSessionId)
    {
        if (BusinessId != businessId || WorkSessionId != workSessionId)
            throw new WorkSessionForbiddenException(
                "El pago no corresponde al negocio y la sesión de trabajo autenticados.");
    }
}
