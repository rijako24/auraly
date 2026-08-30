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
                new PosLocalLoginRequest("cashier", password),
                userId,
                now.AddHours(8));
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
                new PosLocalLoginRequest("cashier-updated", password),
                userId,
                now.AddHours(8));
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!IsOnline)
                throw new HttpRequestException("Auraly Server is unavailable.");
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
