using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;

namespace Auraly.Application.Authentication;

public sealed record OfflineAuthenticationLeaseDevice(
    Guid TenantId,
    Guid DeviceId);

public sealed record OfflineAuthenticationLeaseCandidate(
    OfflineAuthenticationLeasePayload Payload,
    SignedOfflineAuthenticationLease SignedLease,
    PosOfflinePasswordVerifier PasswordVerifier);

public sealed record OfflineAuthenticationLeasePolicy(TimeSpan Duration)
{
    public static readonly TimeSpan SystemMaximum = TimeSpan.FromHours(24);

    public void EnsureValid()
    {
        if (Duration <= TimeSpan.Zero || Duration > SystemMaximum)
            throw new OfflineAuthenticationLeaseConfigurationException(
                $"Offline lease duration must be greater than zero and no longer than {SystemMaximum.TotalHours:0} hours.");
    }
}

public interface IOfflineAuthenticationLeaseSigner
{
    SignedOfflineAuthenticationLease Sign(OfflineAuthenticationLeasePayload payload);
}

public interface IOfflineAuthenticationLeaseTrustProvider
{
    IReadOnlyDictionary<string, string> TrustedPublicKeys { get; }
}

public interface IOfflineAuthenticationLeaseStore
{
    Task<SignedOfflineAuthenticationLease> AcquireAsync(
        OfflineAuthenticationLeaseCandidate candidate,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid tenantId,
        Guid deviceId,
        Guid leaseId,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken);
}

public sealed class OfflineAuthenticationLeaseService(
    IAuthenticationSessionStore authenticationSessions,
    IAuthenticationPasswordVerifier passwordVerifier,
    IOfflineAuthenticationLeaseStore leases,
    IOfflineAuthenticationLeaseSigner signer,
    IAuralyIdGenerator ids,
    OfflineAuthenticationLeasePolicy policy,
    TimeProvider timeProvider)
{
    private const int MaximumFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<OfflineAuthenticationLeaseAcquireResponse> AcquireAsync(
        OfflineAuthenticationLeaseDevice device,
        OfflineAuthenticationLeaseAcquireRequest request,
        CancellationToken cancellationToken = default)
    {
        policy.EnsureValid();
        EnsureDevice(device);
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new AuthenticationValidationException(
                "Username and password are required.");

        var now = timeProvider.GetUtcNow();
        var user = await authenticationSessions.FindUserAsync(
            request.Username.Trim().ToUpperInvariant(), cancellationToken);
        if (user is null || user.TenantId != device.TenantId || !user.IsActive ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
            throw new AuthenticationDeniedException("Invalid credentials.");
        if (user.LockoutEnd is { } lockedUntil && lockedUntil > now)
            throw new AuthenticationDeniedException("The account is temporarily locked.");
        if (!passwordVerifier.Verify(request.Password, user.PasswordHash))
        {
            await authenticationSessions.RecordFailedLoginAsync(
                user.UserId, now, MaximumFailures, LockoutDuration, cancellationToken);
            throw new AuthenticationDeniedException("Invalid credentials.");
        }

        var verifier = PosOfflinePasswordHasher.Hash(request.Password, now);
        var payload = new OfflineAuthenticationLeasePayload(
            1,
            ids.NewId(),
            user.TenantId,
            user.UserId,
            device.DeviceId,
            now,
            now,
            now.Add(policy.Duration),
            ids.NewId());
        var signed = signer.Sign(payload);
        var persisted = await leases.AcquireAsync(
            new OfflineAuthenticationLeaseCandidate(payload, signed, verifier),
            cancellationToken);

        return new OfflineAuthenticationLeaseAcquireResponse(
            persisted,
            new OfflineAuthenticationLeaseUser(
                user.UserId,
                user.Username,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Permissions,
                verifier.Salt,
                verifier.Hash,
                verifier.Iterations,
                verifier.ChangedAt));
    }

    public Task ReleaseAsync(
        OfflineAuthenticationLeaseDevice device,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        EnsureDevice(device);
        if (leaseId == Guid.Empty)
            throw new AuthenticationValidationException("LeaseId is required.");
        return leases.ReleaseAsync(
            device.TenantId,
            device.DeviceId,
            leaseId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static void EnsureDevice(OfflineAuthenticationLeaseDevice device)
    {
        if (device.TenantId == Guid.Empty || device.DeviceId == Guid.Empty)
            throw new AuthenticationValidationException(
                "The enrolled device identity is incomplete.");
    }
}

public sealed class OfflineAuthenticationLeaseConflictException(string message)
    : Exception(message);

public sealed class OfflineAuthenticationLeaseConfigurationException(string message)
    : Exception(message);
