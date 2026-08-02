using System.Security.Claims;
using System.Text.Encodings.Web;
using Auraly.Application.Sales;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Auraly.Api;

public static class PosAuthenticationDefaults
{
    public const string Scheme = "AuralyPosDevice";
    public const string PermissionClaim = "auraly:permission";
    public const string DeviceIdClaim = "auraly:device_id";
    public const string TenantIdClaim = "auraly:tenant_id";
}

public sealed class PosDeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPosDeviceAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Auraly-Device-Id", out var deviceHeader) ||
            !Guid.TryParse(deviceHeader.ToString(), out var deviceId) ||
            !Request.Headers.TryGetValue("X-Auraly-Device-Secret", out var secretHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await authenticator.AuthenticateAsync(
            deviceId,
            secretHeader.ToString(),
            Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Invalid POS device credentials.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.DeviceId.ToString("D")),
            new(PosAuthenticationDefaults.DeviceIdClaim, identity.DeviceId.ToString("D")),
            new(PosAuthenticationDefaults.TenantIdClaim, identity.TenantId.ToString("D"))
        };
        claims.AddRange(identity.Permissions.Select(
            permission => new Claim(PosAuthenticationDefaults.PermissionClaim, permission)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

