using System.Security.Cryptography;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Host;
using Microsoft.Extensions.Options;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosOfflineAuthenticationLeaseTests : IAsyncLifetime
{
    private const string KeyId = "pos-edge-test-key";
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"auraly-offline-lease-{Guid.NewGuid():N}.db");
    private readonly string _keyDirectory =
        Path.Combine(Path.GetTempPath(), $"auraly-offline-lease-keys-{Guid.NewGuid():N}");
    private readonly RSA _signingKey = RSA.Create(2048);
    private readonly MutableTimeProvider _clock = new(
        new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(-5)));
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task Valid_lease_survives_SQLite_reopen_and_rejects_clock_rollback()
    {
        var verifier = CreateVerifier();
        var connectionString = $"Data Source={_databasePath}";
        var identities = new PosLocalIdentityStore(
            connectionString,
            _keyDirectory,
            new Uuid7AuralyIdGenerator(_clock),
            _clock);
        var store = new PosOfflineLeaseStore(
            connectionString, _tenantId, _deviceId, verifier, _clock);
        await identities.InitializeAsync();
        await store.InitializeAsync();
        var response = CreateResponse(_clock.GetUtcNow().AddHours(8));
        await identities.ApplyLeaseUserAsync(response.User);
        var saved = await store.SaveAsync(response);

        _clock.Advance(TimeSpan.FromMinutes(10));
        var reopened = new PosOfflineLeaseStore(
            connectionString, _tenantId, _deviceId, verifier, _clock);
        await reopened.InitializeAsync();
        var restored = await reopened.RequireForUserAsync("cashier");
        Assert.Equal(saved.Payload.LeaseId, restored.Payload.LeaseId);

        _clock.Advance(TimeSpan.FromMinutes(-5));
        var error = await Assert.ThrowsAsync<PosLocalLoginException>(
            () => reopened.RequireForUserAsync("cashier"));
        Assert.Equal("ClockRollbackDetected", error.Code);
    }

    [Fact]
    public async Task Pending_release_survives_reopen_and_is_completed_once()
    {
        var connectionString = $"Data Source={_databasePath}";
        var verifier = CreateVerifier();
        var store = new PosOfflineLeaseStore(
            connectionString, _tenantId, _deviceId, verifier, _clock);
        await store.InitializeAsync();
        var saved = await store.SaveAsync(CreateResponse(_clock.GetUtcNow().AddHours(8)));
        await store.QueueReleaseAsync(_userId);

        var reopened = new PosOfflineLeaseStore(
            connectionString, _tenantId, _deviceId, verifier, _clock);
        await reopened.InitializeAsync();
        Assert.Equal(saved.Payload.LeaseId, await reopened.PendingReleaseAsync());

        await reopened.MarkReleasedAsync(saved.Payload.LeaseId);
        await reopened.MarkReleasedAsync(saved.Payload.LeaseId);
        Assert.Null(await reopened.PendingReleaseAsync());
    }

    [Fact]
    public void Tampered_signature_is_rejected()
    {
        var response = CreateResponse(_clock.GetUtcNow().AddHours(8));
        var signature = OfflineAuthenticationLeaseTokenCodec.Decode(
            response.Lease.Signature);
        signature[0] ^= 0x01;
        var tampered = response.Lease with
        {
            Signature = OfflineAuthenticationLeaseTokenCodec.Encode(signature)
        };

        var error = Assert.Throws<PosLocalLoginException>(() =>
            CreateVerifier().Verify(tampered, _tenantId, _deviceId, _clock.GetUtcNow()));
        Assert.Equal("OfflineLeaseInvalid", error.Code);
    }

    [Fact]
    public void Lease_for_another_device_is_rejected()
    {
        var response = CreateResponse(_clock.GetUtcNow().AddHours(8));
        var error = Assert.Throws<PosLocalLoginException>(() =>
            CreateVerifier().Verify(
                response.Lease, _tenantId, Guid.NewGuid(), _clock.GetUtcNow()));
        Assert.Equal("OfflineLeaseInvalid", error.Code);
    }

    [Fact]
    public void Expired_lease_is_rejected()
    {
        var response = CreateResponse(_clock.GetUtcNow().AddMinutes(-1));
        var error = Assert.Throws<PosLocalLoginException>(() =>
            CreateVerifier().Verify(
                response.Lease, _tenantId, _deviceId, _clock.GetUtcNow()));
        Assert.Equal("OfflineLeaseExpired", error.Code);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _signingKey.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            if (File.Exists(path)) File.Delete(path);
        if (Directory.Exists(_keyDirectory)) Directory.Delete(_keyDirectory, recursive: true);
        return Task.CompletedTask;
    }

    private PosOfflineLeaseVerifier CreateVerifier() =>
        new(Options.Create(new PosOfflineLeaseTrustOptions
        {
            TrustedPublicKeys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KeyId] = _signingKey.ExportSubjectPublicKeyInfoPem()
            }
        }));

    private OfflineAuthenticationLeaseAcquireResponse CreateResponse(
        DateTimeOffset expiresAt)
    {
        var issuedAt = _clock.GetUtcNow().AddMinutes(-1);
        var payload = new OfflineAuthenticationLeasePayload(
            1,
            Guid.NewGuid(),
            _tenantId,
            _userId,
            _deviceId,
            issuedAt,
            issuedAt,
            expiresAt,
            Guid.NewGuid());
        var payloadBytes = OfflineAuthenticationLeaseTokenCodec.Serialize(payload);
        var signature = _signingKey.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var verifier = PosOfflinePasswordHasher.Hash("Cashier-Password-1", issuedAt);
        return new OfflineAuthenticationLeaseAcquireResponse(
            new SignedOfflineAuthenticationLease(
                KeyId,
                OfflineAuthenticationLeaseAlgorithms.RsaPssSha256,
                OfflineAuthenticationLeaseTokenCodec.Encode(payloadBytes),
                OfflineAuthenticationLeaseTokenCodec.Encode(signature)),
            new OfflineAuthenticationLeaseUser(
                _userId,
                "cashier",
                "Cajera de prueba",
                ["sales.create"],
                verifier.Salt,
                verifier.Hash,
                verifier.Iterations,
                verifier.ChangedAt));
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan value) => _value = _value.Add(value);
    }
}
