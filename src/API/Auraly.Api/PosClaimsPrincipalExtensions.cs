using System.Security.Claims;
using Auraly.Application.Sales;

namespace Auraly.Api;

public static class PosClaimsPrincipalExtensions
{
    public static PosDeviceIdentity ToPosDeviceIdentity(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new PosDeviceIdentity(
            RequiredGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.TenantIdClaim));
    }

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The authenticated device lacks claim '{claimType}'.");
    }
}

