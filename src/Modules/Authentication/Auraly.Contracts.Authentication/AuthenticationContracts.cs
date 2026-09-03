namespace Auraly.Contracts.Authentication;

public static class AuthenticationDefaults
{
    public const string SessionIdClaim = "sid";
    public const string TenantIdClaim = "tenant_id";
    public const string IdentityTenantIdClaim = "identity_tenant_id";
    public const string PermissionClaim = "permission";
    public const string ClientIdHeader = "X-Auraly-Client-Id";
}

public sealed record AuthenticationLoginRequest(
    string Username,
    string TenantKey,
    string Password);

public sealed record AuthenticationRefreshRequest(
    string AccessToken,
    string RefreshToken);

public sealed record AuthenticationRevokeRequest(
    string RefreshToken);

public sealed record AuthenticationUserView(
    Guid UserId,
    Guid TenantId,
    string Username,
    string TenantKey,
    string Email,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record AuthenticationResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    AuthenticationUserView User,
    string CorrelationId);

public sealed record AuthenticationSessionIdentity(
    Guid AuthenticationSessionId,
    Guid UserId,
    Guid TenantId,
    Guid ClientId);
