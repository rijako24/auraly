using System.Net;
using System.Security.Cryptography;
using Auraly.Api;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Lib.Net.Http.WebPush;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class PosApprovalWebPushServiceTests
{
    [Fact]
    public async Task Visible_supervisor_uses_realtime_without_redundant_web_push()
    {
        var userId = Guid.NewGuid();
        var gateway = new TestPosSynchronizationPushGateway();
        gateway.SetUserConnected(userId, true);
        var delivery = new ConcurrentPushHandler(1);
        var service = CreateService(
            gateway,
            delivery,
            [Recipient(userId, "https://push.test/visible")]);

        await service.NotifyAsync(Approval(), CancellationToken.None);

        Assert.Equal(0, delivery.RequestCount);
    }

    [Fact]
    public async Task Closed_supervisor_receives_all_registered_devices_in_parallel()
    {
        var userId = Guid.NewGuid();
        var gateway = new TestPosSynchronizationPushGateway();
        var delivery = new ConcurrentPushHandler(2);
        var service = CreateService(
            gateway,
            delivery,
            [
                Recipient(userId, "https://push.test/device-one"),
                Recipient(userId, "https://push.test/device-two")
            ]);

        await service.NotifyAsync(Approval(), CancellationToken.None);

        Assert.Equal(2, delivery.RequestCount);
        Assert.Equal(2, delivery.MaximumConcurrency);
    }

    private static PosApprovalWebPushService CreateService(
        TestPosSynchronizationPushGateway gateway,
        ConcurrentPushHandler delivery,
        IReadOnlyList<PosApprovalPushRecipient> recipients)
    {
        var (publicKey, privateKey) = VapidKeys();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:WebPush:PublicKey"] = publicKey,
                ["Notifications:WebPush:PrivateKey"] = privateKey,
                ["Notifications:WebPush:Subject"] = "mailto:test@auralyapp.co",
                ["Notifications:WebPush:PublicAppUrl"] = "https://auralyapp.co"
            })
            .Build();
        return new PosApprovalWebPushService(
            new StubSubscriptionStore(recipients),
            new PushServiceClient(new HttpClient(delivery)),
            gateway,
            configuration,
            NullLogger<PosApprovalWebPushService>.Instance);
    }

    private static PosApprovalPushRecipient Recipient(Guid userId, string endpoint)
    {
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = receiver.ExportParameters(false);
        var publicBytes = new byte[65];
        publicBytes[0] = 4;
        Buffer.BlockCopy(parameters.Q.X!, 0, publicBytes, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y!, 0, publicBytes, 33, 32);
        return new PosApprovalPushRecipient(
            Guid.NewGuid(),
            userId,
            endpoint,
            Base64Url(publicBytes),
            Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private static (string PublicKey, string PrivateKey) VapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var publicBytes = new byte[65];
        publicBytes[0] = 4;
        Buffer.BlockCopy(parameters.Q.X!, 0, publicBytes, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y!, 0, publicBytes, 33, 32);
        return (Base64Url(publicBytes), Base64Url(parameters.D!));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static PosApprovalRequestView Approval() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        CommercePermissionCodes.SalesDiscount,
        Guid.NewGuid(),
        "Cajero prueba",
        "{\"action\":\"Discount\"}",
        PosApprovalStatus.Pending,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(2),
        null,
        null,
        null,
        null);

    private sealed class StubSubscriptionStore(
        IReadOnlyList<PosApprovalPushRecipient> recipients)
        : IPosApprovalPushSubscriptionStore
    {
        public Task UpsertAsync(PosApprovalUserIdentity user, string endpoint, string p256dh, string auth, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(PosApprovalUserIdentity user, string endpoint, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PosApprovalPushRecipient>> RecipientsAsync(PosApprovalRequestView request, CancellationToken cancellationToken) =>
            Task.FromResult(recipients);

        public Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ConcurrentPushHandler(int expectedRequests) : HttpMessageHandler
    {
        private readonly TaskCompletionSource allRequestsStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeRequests;
        private int maximumConcurrency;
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);
        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref activeRequests);
            InterlockedExtensions.Max(ref maximumConcurrency, active);
            if (Interlocked.Increment(ref requestCount) == expectedRequests)
                allRequestsStarted.TrySetResult();
            try
            {
                await allRequestsStarted.Task.WaitAsync(
                    TimeSpan.FromSeconds(2), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            finally
            {
                Interlocked.Decrement(ref activeRequests);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
