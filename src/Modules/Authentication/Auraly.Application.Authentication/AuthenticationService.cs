using System.Security.Cryptography;
using System.Text;
using Auraly.Contracts.Authentication;
using Auraly.BuildingBlocks.Domain.Identity;
using Auraly.Contracts.Authorization;

namespace Auraly.Application.Authentication;

public sealed record AuthenticationUserRecord(
    Guid UserId,
    Guid TenantId,
    string Username,
    string TenantKey,
    string Email,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    string? PasswordHash,
    bool IsActive,
    int AccessFailedCount,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record OpenAuthenticationSessionCommand(
    AuthenticationUserRecord User,
    Guid ClientId,
    string? ClientDescription,
    string? IpAddress,
    byte[] RefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAt,
    PosOfflinePasswordVerifier? OfflinePasswordVerifier,
    DateTimeOffset Now);

public sealed record RotateAuthenticationSessionCommand(
    AuthenticationSessionIdentity Identity,
    byte[] CurrentRefreshTokenHash,
    byte[] NewRefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset Now);

public sealed record AuthenticationSessionRecord(
    AuthenticationSessionIdentity Identity,
    AuthenticationUserRecord User,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public sealed record ParsedAuthenticationToken(
    Guid AuthenticationSessionId,
    Guid UserId,
    Guid TenantId);

public interface IAuthenticationSessionStore
{
    Task<AuthenticationUserRecord?> FindUserAsync(
        Guid tenantId, string normalizedUsername,
        CancellationToken cancellationToken);

    Task<AuthenticationUserRecord?> FindUserAsync(
        string tenantKey, string normalizedUsername,
        CancellationToken cancellationToken);

    Task RecordFailedLoginAsync(
        Guid userId,
        DateTimeOffset now,
        int maxAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken);

    Task<AuthenticationSessionRecord> OpenAsync(
        OpenAuthenticationSessionCommand command,
        CancellationToken cancellationToken);

    Task<AuthenticationSessionRecord> RotateAsync(
        RotateAuthenticationSessionCommand command,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        AuthenticationSessionIdentity identity,
        byte[] refreshTokenHash,
        string reason,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(
        ParsedAuthenticationToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<AuthenticationUserRecord?> GetUserAsync(
        ParsedAuthenticationToken token,
        CancellationToken cancellationToken);
}

public interface IAuthenticationPasswordVerifier
{
    bool Verify(string password, string passwordHash);
}

public interface IAuthenticationTokenIssuer
{
    TimeSpan AccessTokenLifetime { get; }
    TimeSpan RefreshTokenLifetime { get; }

    string IssueAccessToken(
        AuthenticationSessionIdentity identity,
        AuthenticationUserRecord user,
        DateTimeOffset issuedAt);

    ParsedAuthenticationToken ParseExpiredAccessToken(string accessToken);
}

public interface IAuthenticationSessionValidator
{
    Task<bool> IsActiveAsync(
        ParsedAuthenticationToken token,
        CancellationToken cancellationToken = default);
}

public sealed class AuthenticationService(
    IAuthenticationSessionStore store,
    IAuthenticationPasswordVerifier passwordVerifier,
    IAuthenticationTokenIssuer tokenIssuer,
    TimeProvider timeProvider)
    : IAuthenticationSessionValidator
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthenticationResponse> LoginAsync(
        AuthenticationLoginRequest request,
        Guid clientId,
        string? clientDescription,
        string? ipAddress,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.TenantKey) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new AuthenticationValidationException(
                "Username and password are required.");
        EnsureClient(clientId);

        var now = timeProvider.GetUtcNow();
        var user = await store.FindUserAsync(
            TenantKey.Parse(request.TenantKey).Value,
            request.Username.Trim().ToUpperInvariant(), cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash))
            throw new AuthenticationDeniedException("Invalid credentials.");
        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
            throw new AuthenticationDeniedException("The account is temporarily locked.");
        if (!passwordVerifier.Verify(request.Password, user.PasswordHash))
        {
            await store.RecordFailedLoginAsync(
                user.UserId, now, MaxFailedAttempts, LockoutDuration, cancellationToken);
            throw new AuthenticationDeniedException("Invalid credentials.");
        }

        var refreshToken = GenerateRefreshToken();
        var expiresAt = now.Add(tokenIssuer.RefreshTokenLifetime);
        var offlineVerifier = PosOfflinePasswordHasher.Hash(request.Password, now);
        var session = await store.OpenAsync(
            new OpenAuthenticationSessionCommand(
                user,
                clientId,
                Normalize(clientDescription, 500),
                Normalize(ipAddress, 64),
                HashRefreshToken(refreshToken),
                expiresAt,
                offlineVerifier,
                now),
            cancellationToken);
        return Issue(session, refreshToken, correlationId, now);
    }

    public async Task<AuthenticationResponse> RefreshAsync(
        AuthenticationRefreshRequest request,
        Guid clientId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        EnsureClient(clientId);
        if (string.IsNullOrWhiteSpace(request.AccessToken) ||
            string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new AuthenticationValidationException(
                "Access token and refresh token are required.");

        var parsed = tokenIssuer.ParseExpiredAccessToken(request.AccessToken);
        var identity = new AuthenticationSessionIdentity(
            parsed.AuthenticationSessionId,
            parsed.UserId,
            parsed.TenantId,
            clientId);
        var refreshToken = GenerateRefreshToken();
        var now = timeProvider.GetUtcNow();
        var session = await store.RotateAsync(
            new RotateAuthenticationSessionCommand(
                identity,
                HashRefreshToken(request.RefreshToken),
                HashRefreshToken(refreshToken),
                now.Add(tokenIssuer.RefreshTokenLifetime),
                now),
            cancellationToken);
        return Issue(session, refreshToken, correlationId, now);
    }

    public Task RevokeAsync(
        AuthenticationSessionIdentity identity,
        string refreshToken,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new AuthenticationValidationException("Refresh token is required.");
        return store.RevokeAsync(
            identity,
            HashRefreshToken(refreshToken),
            reason,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<bool> IsActiveAsync(
        ParsedAuthenticationToken token,
        CancellationToken cancellationToken = default) =>
        store.IsActiveAsync(token, timeProvider.GetUtcNow(), cancellationToken);

    public async Task<AuthenticationUserView> GetCurrentUserAsync(
        ParsedAuthenticationToken token,
        CancellationToken cancellationToken = default)
    {
        var user = await store.GetUserAsync(token, cancellationToken)
            ?? throw new AuthenticationDeniedException(
                "The authentication session is no longer active.");
        return ToView(user);
    }

    private AuthenticationResponse Issue(
        AuthenticationSessionRecord session,
        string refreshToken,
        string correlationId,
        DateTimeOffset now) =>
        new(
            tokenIssuer.IssueAccessToken(session.Identity, session.User, now),
            refreshToken,
            now.Add(tokenIssuer.AccessTokenLifetime),
            ToView(session.User),
            correlationId);

    private static AuthenticationUserView ToView(AuthenticationUserRecord user) =>
        new(
            user.UserId,
            user.TenantId,
            user.Username,
            user.TenantKey,
            user.Email,
            user.FirstName,
            user.LastName,
            user.AvatarUrl,
            user.Roles,
            user.Permissions);

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public static byte[] HashRefreshToken(string refreshToken) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));

    private static void EnsureClient(Guid clientId)
    {
        if (clientId == Guid.Empty)
            throw new AuthenticationValidationException(
                $"Header '{AuthenticationDefaults.ClientIdHeader}' is required.");
    }

    private static string? Normalize(string? value, int length)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: var count } && count > length
            ? normalized[..length]
            : normalized;
    }
}

public sealed class AuthenticationValidationException(string message) : Exception(message);
public sealed class AuthenticationSessionValidator(
    IAuthenticationSessionStore store,
    TimeProvider timeProvider) : IAuthenticationSessionValidator
{
    public Task<bool> IsActiveAsync(
        ParsedAuthenticationToken token,
        CancellationToken cancellationToken = default) =>
        store.IsActiveAsync(token, timeProvider.GetUtcNow(), cancellationToken);
}

public sealed class AuthenticationDeniedException(string message) : Exception(message);
public sealed class AuthenticationSessionConflictException(string message) : Exception(message);
