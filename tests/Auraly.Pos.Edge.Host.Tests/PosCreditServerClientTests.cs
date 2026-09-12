using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosCreditServerClientTests
{
    [Fact]
    public async Task Enrolled_credit_uses_device_authenticated_server_validation()
    {
        var businessId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var dueDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var handler = new CreditValidationHandler(customerId, 50_000m, dueDate);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test") };
        var client = new PosCreditServerClient(
            http,
            new PosDeviceCredentials(deviceId, "device-secret"),
            new PosOperationalScope(businessId, warehouseId));

        var result = await client.ValidateAsync(
            customerId, 50_000m, fiscalEnvironment: null,
            cancellationToken: default);

        Assert.True(result.IsAllowed);
        Assert.Equal(businessId, handler.Request!.BusinessId);
        Assert.Equal(customerId, handler.Request.CustomerId);
        Assert.Equal(50_000m, handler.Request.Amount);
        Assert.Equal(dueDate, result.DueDate);
        Assert.Equal(deviceId.ToString("D"), handler.DeviceId);
        Assert.Equal("device-secret", handler.Secret);
    }

    [Fact]
    public async Task Enrolled_credit_is_rejected_before_issue_when_server_is_unavailable()
    {
        using var http = new HttpClient(new DisconnectedHandler())
        {
            BaseAddress = new Uri("https://auraly.test")
        };
        var client = new PosCreditServerClient(
            http,
            new PosDeviceCredentials(Guid.NewGuid(), "device-secret"),
            new PosOperationalScope(Guid.NewGuid(), Guid.NewGuid()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ValidateAsync(
                Guid.NewGuid(),
                50_000m,
                fiscalEnvironment: null,
                cancellationToken: default));

        Assert.Contains("requiere conexión", error.Message, StringComparison.Ordinal);
    }

    private sealed class CreditValidationHandler(
        Guid expectedCustomerId,
        decimal expectedAmount,
        DateTimeOffset dueDate) : HttpMessageHandler
    {
        public PosCreditValidationRequest? Request { get; private set; }
        public string? DeviceId { get; private set; }
        public string? Secret { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("/api/pos/v1/sales/credit-validation", request.RequestUri!.AbsolutePath);
            DeviceId = request.Headers.GetValues("X-Auraly-Device-Id").Single();
            Secret = request.Headers.GetValues("X-Auraly-Device-Secret").Single();
            Request = await request.Content!.ReadFromJsonAsync<PosCreditValidationRequest>(
                cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PosCreditValidationResult(
                    expectedCustomerId,
                    expectedAmount,
                    75_000m,
                    IsAllowed: true,
                    RejectionReason: null,
                    DueDate: dueDate))
            };
        }
    }

    private sealed class DisconnectedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
