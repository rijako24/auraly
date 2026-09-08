namespace Auraly.Contracts.Authorization;

public sealed record PosOfflinePasswordVerifier(
    byte[] Salt,
    byte[] Hash,
    int Iterations,
    DateTimeOffset ChangedAt);

public sealed record PosOfflineSupervisorCredentialVerifier(
    byte[] Salt,
    byte[] Hash,
    int Iterations,
    DateTimeOffset ChangedAt,
    bool IsOneTime = false);

public sealed record PosOfflineUserProjection(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    PosOfflinePasswordVerifier PasswordVerifier,
    PosOfflineSupervisorCredentialVerifier? SupervisorCredential = null);

public sealed record PosOfflineIdentitySnapshot(
    string Revision,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    IReadOnlyList<PosOfflineUserProjection> Users,
    long? Cursor = null);

public sealed record PosOfflineIdentityDelta(
    long Version,
    string Kind,
    Guid UserId,
    PosOfflineUserProjection? User);

public sealed record PosOfflineIdentityDeltaPage(
    long FromCursor,
    long ToCursor,
    bool HasMore,
    IReadOnlyList<PosOfflineIdentityDelta> Changes);
