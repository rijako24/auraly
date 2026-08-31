using System.Net;
using System.Net.Http.Json;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosIdentitySynchronizationJourneyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_or_uninitialized_local_identity_is_synchronized_once_and_then_logged_in(
        bool hasExistingSnapshot)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), $"auraly-identity-login-refresh-{Guid.NewGuid():N}.db");
        var keyDirectory = Path.Combine(
            Path.GetTempPath(), $"auraly-identity-login-refresh-keys-{Guid.NewGuid():N}");
        try
        {
            var now = DateTimeOffset.UtcNow;
            const string password = "New-Cashier-Password-1";
            var userId = Guid.NewGuid();
            var verifier = PosOfflinePasswordHasher.Hash(password, now);
            var handler = new MutableIdentityServerHandler(Snapshot(
                "revision-new-user", now, userId, "new.cashier", "Cajera nueva",
                [CommercePermissionCodes.SalesCreate], verifier));
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://auraly.test")
            };
            var identities = new PosLocalIdentityStore(
                $"Data Source={databasePath}", keyDirectory,
                new Uuid7AuralyIdGenerator(TimeProvider.System), TimeProvider.System);
            await identities.InitializeAsync();
            if (hasExistingSnapshot)
                await identities.ApplySnapshotAsync(new PosOfflineIdentitySnapshot(
                    "revision-before-user",
                    now.AddDays(-30),
                    now.AddDays(-29),
                    []));
            var synchronizer = new PosIdentitySynchronizer(
                http,
                new PosDeviceCredentials(Guid.NewGuid(), "device-secret"),
                new PosOperationalScope(Guid.NewGuid(), Guid.NewGuid()),
                identities,
                new PosSynchronizationEventLog(TimeProvider.System));
            var authentication = new PosEdgeAuthenticationService(
                identities, synchronizer);

            var session = await authentication.LoginAsync(
                new PosLocalLoginRequest("new.cashier", password));

            Assert.Equal(userId, session.UserId);
            Assert.Equal("Cajera nueva", session.DisplayName);
            Assert.Equal(1, handler.RequestCount);

            await Assert.ThrowsAsync<PosLocalLoginException>(() =>
                authentication.LoginAsync(
                    new PosLocalLoginRequest("new.cashier", "wrong-password")));
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Change_made_while_offline_is_downloaded_on_reconnect_and_used_by_local_login()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), $"auraly-identity-reconnect-{Guid.NewGuid():N}.db");
        var keyDirectory = Path.Combine(
            Path.GetTempPath(), $"auraly-identity-reconnect-keys-{Guid.NewGuid():N}");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.NewGuid();
            const string password = "Cashier-Password-1";
            var verifier = PosOfflinePasswordHasher.Hash(password, now);
            var handler = new MutableIdentityServerHandler(Snapshot(
                "revision-1", now, userId, "cashier", "Cajera inicial",
                [CommercePermissionCodes.SalesCreate], verifier));
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://auraly.test")
            };
            var clock = TimeProvider.System;
            var identities = new PosLocalIdentityStore(
                $"Data Source={databasePath}", keyDirectory,
                new Uuid7AuralyIdGenerator(clock), clock);
            await identities.InitializeAsync();
            var events = new PosSynchronizationEventLog(clock);
            var synchronizer = new PosIdentitySynchronizer(
                http,
                new PosDeviceCredentials(Guid.NewGuid(), "device-secret"),
                new PosOperationalScope(Guid.NewGuid(), Guid.NewGuid()),
                identities,
                events);

            await synchronizer.SynchronizeAsync();
            handler.IsOnline = false;
            var initialLogin = await identities.LoginAsync(
                new PosLocalLoginRequest("cashier", password));
            Assert.Equal("Cajera inicial", initialLogin.DisplayName);

            handler.Snapshot = Snapshot(
                "revision-2", now.AddMinutes(1), userId, "cashier-updated",
                "Cajera sincronizada",
                [CommercePermissionCodes.SalesCreate, CommercePermissionCodes.SalesDiscount],
                verifier);
            await Assert.ThrowsAsync<HttpRequestException>(
                () => synchronizer.SynchronizeAsync());
            Assert.Equal(
                "Cajera inicial",
                Assert.Single(await identities.ReadIdentitySummariesAsync()).DisplayName);

            handler.IsOnline = true;
            await synchronizer.SynchronizeAsync();
            handler.IsOnline = false;

            var synchronized = Assert.Single(
                await identities.ReadIdentitySummariesAsync());
            Assert.Equal("cashier-updated", synchronized.Username);
            Assert.Equal("Cajera sincronizada", synchronized.DisplayName);
            Assert.Contains(CommercePermissionCodes.SalesDiscount, synchronized.Permissions);
            var reconnectedLogin = await identities.LoginAsync(
                new PosLocalLoginRequest("cashier-updated", password));
            Assert.Equal("Cajera sincronizada", reconnectedLogin.DisplayName);
            Assert.Contains(CommercePermissionCodes.SalesDiscount, reconnectedLogin.Permissions);
            Assert.Contains(events.Read(), item =>
                item.Category == "Usuario" &&
                item.Title.Contains("Cajera sincronizada", StringComparison.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task User_without_local_pos_access_is_delegated_to_cloud_after_one_refresh()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), $"auraly-cloud-login-required-{Guid.NewGuid():N}.db");
        var keyDirectory = Path.Combine(
            Path.GetTempPath(), $"auraly-cloud-login-required-keys-{Guid.NewGuid():N}");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var handler = new MutableIdentityServerHandler(new PosOfflineIdentitySnapshot(
                "revision-without-admin", now, now.AddDays(1), []));
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test") };
            var identities = new PosLocalIdentityStore(
                $"Data Source={databasePath}", keyDirectory,
                new Uuid7AuralyIdGenerator(TimeProvider.System), TimeProvider.System);
            await identities.InitializeAsync();
            await identities.ApplySnapshotAsync(new PosOfflineIdentitySnapshot(
                "revision-before-admin", now.AddMinutes(-1), now, []));
            var synchronizer = new PosIdentitySynchronizer(
                http,
                new PosDeviceCredentials(Guid.NewGuid(), "device-secret"),
                new PosOperationalScope(Guid.NewGuid(), Guid.NewGuid()),
                identities,
                new PosSynchronizationEventLog(TimeProvider.System));
            var authentication = new PosEdgeAuthenticationService(identities, synchronizer);

            var error = await Assert.ThrowsAsync<PosLocalLoginException>(() =>
                authentication.LoginAsync(new PosLocalLoginRequest("admin", "password")));

            Assert.Equal("CloudLoginRequired", error.Code);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
                if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, recursive: true);
        }
    }

    private static PosOfflineIdentitySnapshot Snapshot(
        string revision,
        DateTimeOffset issuedAt,
        Guid userId,
        string username,
        string displayName,
        IReadOnlyList<string> permissions,
        PosOfflinePasswordVerifier verifier) =>
        new(revision, issuedAt, issuedAt.AddDays(1),
        [new PosOfflineUserProjection(
            userId, username, displayName, permissions, verifier)]);

    private sealed class MutableIdentityServerHandler(
        PosOfflineIdentitySnapshot snapshot) : HttpMessageHandler
    {
        public bool IsOnline { get; set; } = true;
        public PosOfflineIdentitySnapshot Snapshot { get; set; } = snapshot;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!IsOnline)
                throw new HttpRequestException("Auraly Server is unavailable.");
            RequestCount++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.StartsWith(
                "/api/pos/v1/identity/snapshot?businessId=",
                request.RequestUri!.PathAndQuery,
                StringComparison.Ordinal);
            Assert.True(request.Headers.Contains("X-Auraly-Device-Id"));
            Assert.True(request.Headers.Contains("X-Auraly-Device-Secret"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Snapshot)
            });
        }
    }
}
