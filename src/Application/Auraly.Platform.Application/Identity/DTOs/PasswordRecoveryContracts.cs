namespace Auraly.Platform.Application.Identity.DTOs;

public sealed record RequestPasswordRecoveryRequest(string TenantKey, string Username, string Email);
public sealed record RequestPasswordRecoveryResult(string MaskedEmail, string Status);
public sealed record ConfirmPasswordRecoveryRequest(string Token, string Password, string PasswordConfirmation);
public sealed record PasswordRecoveryMaterial(
    string PasswordHash,
    byte[] OfflineSalt,
    byte[] OfflineHash,
    int OfflineIterations,
    DateTimeOffset ChangedAt);

public interface IPasswordRecoveryStore
{
    Task CreateAsync(
        RequestPasswordRecoveryRequest request,
        Guid requestId,
        string rawToken,
        byte[] tokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
    Task<bool> ConsumeAsync(
        byte[] tokenHash,
        PasswordRecoveryMaterial material,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}