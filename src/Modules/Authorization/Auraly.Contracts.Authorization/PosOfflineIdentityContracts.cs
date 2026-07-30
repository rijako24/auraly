namespace Auraly.Contracts.Authorization;

public sealed record PosOfflinePasswordVerifier(
    byte[] Salt,
    byte[] Hash,
    int Iterations,
    DateTimeOffset ChangedAt);

public sealed record PosOfflineUserProjection(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    PosOfflinePasswordVerifier PasswordVerifier);

public sealed record PosOfflineIdentitySnapshot(
    string Revision,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    IReadOnlyList<PosOfflineUserProjection> Users);
