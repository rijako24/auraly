using System.Security.Cryptography;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Host;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosLocalApprovalTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"auraly-local-approval-{Guid.NewGuid():N}.db");
    private readonly string _keyDirectory = Path.Combine(
        Path.GetTempPath(), $"auraly-local-approval-keys-{Guid.NewGuid():N}");
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly Guid _cashierId = Guid.NewGuid();
    private readonly Guid _supervisorId = Guid.NewGuid();
    private PosLocalIdentityStore? _store;

    [Fact]
    public async Task Secondary_supervisor_credential_authorizes_once_and_is_audited_durably()
    {
        var store = Assert.IsType<PosLocalIdentityStore>(_store);
        var session = await store.LoginAsync(
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"),
            _cashierId,
            DateTimeOffset.UtcNow.AddHours(8));
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var authorization = await store.AuthorizeSensitiveAsync(
            session,
            CommercePermissionCodes.SalesRemoveLine,
            draftId,
            lineId,
            "Supervisor-Secret-1");
        Assert.Equal(_cashierId, authorization.RequestedByUserId);
        Assert.Equal(_supervisorId, authorization.AuthorizedByUserId);
        Assert.Equal("OfflineSupervisorCredential", authorization.Method);
        await store.CompleteSensitiveAsync(authorization);

        await using var database = new SqliteConnection($"Data Source={_databasePath}");
        await database.OpenAsync();
        await using var audit = database.CreateCommand();
        audit.CommandText = """
            SELECT Status,PermissionResource,DraftId,LineId,AuthorizationMethod
            FROM PosLocalApprovalAudits WHERE AuthorizationId=$id;
            """;
        audit.Parameters.AddWithValue("$id", authorization.AuthorizationId.ToString("D"));
        await using var reader = await audit.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Completed", reader.GetString(0));
        Assert.Equal(CommercePermissionCodes.SalesRemoveLine, reader.GetString(1));
        Assert.Equal(draftId.ToString("D"), reader.GetString(2));
        Assert.Equal(lineId.ToString("D"), reader.GetString(3));
        Assert.Equal("OfflineSupervisorCredential", reader.GetString(4));

        await using var verifier = database.CreateCommand();
        verifier.CommandText =
            "SELECT ProtectedSupervisorCredential FROM PosOfflineUsers WHERE UserId=$id;";
        verifier.Parameters.AddWithValue("$id", _supervisorId.ToString("D"));
        var protectedValue = Assert.IsType<string>(await verifier.ExecuteScalarAsync());
        Assert.DoesNotContain("Supervisor-Secret-1", protectedValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_credential_never_creates_an_authorization_audit()
    {
        var store = Assert.IsType<PosLocalIdentityStore>(_store);
        var session = await store.LoginAsync(
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"),
            _cashierId,
            DateTimeOffset.UtcNow.AddHours(8));

        var error = await Assert.ThrowsAsync<PosLocalApprovalException>(() =>
            store.AuthorizeSensitiveAsync(
                session,
                CommercePermissionCodes.SalesRestartDraft,
                Guid.NewGuid(),
                null,
                "wrong-secret"));
        Assert.Equal("InvalidSupervisorCredential", error.Code);

        await using var database = new SqliteConnection($"Data Source={_databasePath}");
        await database.OpenAsync();
        await using var count = database.CreateCommand();
        count.CommandText = "SELECT COUNT(1) FROM PosLocalApprovalAudits;";
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task One_time_supervisor_credential_cannot_be_reused_or_restored_by_the_same_snapshot()
    {
        var store = Assert.IsType<PosLocalIdentityStore>(_store);
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var salt = RandomNumberGenerator.GetBytes(32);
        const int iterations = 210_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            "One-Time-Supervisor-1", salt, iterations,
            HashAlgorithmName.SHA256, 32);
        var cashierPassword = PosOfflinePasswordHasher.Hash("Cashier-Password-1", now);
        var supervisorPassword = PosOfflinePasswordHasher.Hash("Supervisor-Password-1", now);
        var snapshot = new PosOfflineIdentitySnapshot(
            "one-time-approval-test",
            now,
            now.AddDays(1),
            [
                new PosOfflineUserProjection(
                    _cashierId, "cashier", "Cajero",
                    [CommercePermissionCodes.SalesCreate], cashierPassword),
                new PosOfflineUserProjection(
                    _supervisorId, "supervisor", "Supervisora",
                    [
                        CommercePermissionCodes.SalesCreate,
                        CommercePermissionCodes.SalesRemoveLine,
                        CommercePermissionCodes.PosApprovalsAuthorize
                    ],
                    supervisorPassword,
                    new PosOfflineSupervisorCredentialVerifier(
                        salt, hash, iterations, now, true))
            ]);
        await store.ApplySnapshotAsync(snapshot);
        var session = await store.LoginAsync(
            new PosLocalLoginRequest("cashier", "Cashier-Password-1"),
            _cashierId,
            now.AddHours(8));

        await store.AuthorizeSensitiveAsync(
            session,
            CommercePermissionCodes.SalesRemoveLine,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "One-Time-Supervisor-1");

        var second = await Assert.ThrowsAsync<PosLocalApprovalException>(() =>
            store.AuthorizeSensitiveAsync(
                session,
                CommercePermissionCodes.SalesRemoveLine,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "One-Time-Supervisor-1"));
        Assert.Equal("InvalidSupervisorCredential", second.Code);

        await store.ApplySnapshotAsync(snapshot);
        var afterRefresh = await Assert.ThrowsAsync<PosLocalApprovalException>(() =>
            store.AuthorizeSensitiveAsync(
                session,
                CommercePermissionCodes.SalesRemoveLine,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "One-Time-Supervisor-1"));
        Assert.Equal("InvalidSupervisorCredential", afterRefresh.Code);
    }

    public async Task InitializeAsync()
    {
        _store = new PosLocalIdentityStore(
            $"Data Source={_databasePath}",
            _keyDirectory,
            new Uuid7AuralyIdGenerator(_clock),
            _clock);
        await _store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var cashierPassword = PosOfflinePasswordHasher.Hash("Cashier-Password-1", now);
        var supervisorPassword = PosOfflinePasswordHasher.Hash("Supervisor-Password-1", now);
        var salt = RandomNumberGenerator.GetBytes(32);
        const int iterations = 210_000;
        var supervisorHash = Rfc2898DeriveBytes.Pbkdf2(
            "Supervisor-Secret-1", salt, iterations,
            HashAlgorithmName.SHA256, 32);
        await _store.ApplySnapshotAsync(new PosOfflineIdentitySnapshot(
            "approval-test",
            now,
            now.AddDays(1),
            [
                new PosOfflineUserProjection(
                    _cashierId,
                    "cashier",
                    "Cajero",
                    [CommercePermissionCodes.SalesCreate],
                    cashierPassword),
                new PosOfflineUserProjection(
                    _supervisorId,
                    "supervisor",
                    "Supervisora",
                    [
                        CommercePermissionCodes.SalesCreate,
                        CommercePermissionCodes.SalesRemoveLine,
                        CommercePermissionCodes.SalesRestartDraft,
                        CommercePermissionCodes.PosApprovalsAuthorize
                    ],
                    supervisorPassword,
                    new PosOfflineSupervisorCredentialVerifier(
                        salt, supervisorHash, iterations, now))
            ]));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        if (Directory.Exists(_keyDirectory)) Directory.Delete(_keyDirectory, true);
        return Task.CompletedTask;
    }
}
