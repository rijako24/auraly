using System.Net;
using System.Net.Http.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Orders;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosOrderServerClientTests
{
    [Fact]
    public async Task Saving_an_enrolled_pos_order_uses_one_device_authenticated_server_request()
    {
        var handler = new SaveHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test") };
        var deviceId = Guid.NewGuid();
        var client = Client(http, deviceId);
        var session = Session();
        var draft = new PosDraft(
            new DraftId(Guid.NewGuid()),
            new PosDraftScope(
                new BusinessId(Guid.NewGuid()),
                new WarehouseId(Guid.NewGuid()),
                new DeviceId(deviceId),
                new WorkSessionId(session.WorkSessionId),
                new UserId(session.UserId)),
            Guid.NewGuid(), null, PosDraftStatus.Active, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new PosDraftLine(
                Guid.NewGuid(), new ProductId(Guid.NewGuid()), "P-1", "Producto", "EA",
                "01", 19m, 2m, 100m, 90m, "COP", "Captured", null, 10m, null,
                false, 50m, false, 1)]);

        var result = await client.SaveAsync(session, draft, "pos-order-draft", default);

        Assert.Equal("Confirmed", result.Status);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("/api/pos/v1/orders/save", handler.Path);
        Assert.Equal(deviceId.ToString("D"), handler.DeviceId);
        Assert.Equal("pos-order-draft", handler.IdempotencyKey);
        Assert.Equal(draft.CustomerId, handler.Payload!.CustomerId);
        Assert.Equal(90m, Assert.Single(handler.Payload.Lines).UnitPrice);
    }

    [Fact]
    public async Task Order_print_uses_the_device_authenticated_post_endpoint()
    {
        var orderId = Guid.NewGuid();
        var handler = new PrintBatchHandler(orderId);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test") };
        var deviceId = Guid.NewGuid();
        var client = Client(http, deviceId);

        var result = await client.PrintBatchAsync(Session(), [orderId], default);

        Assert.Empty(result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/pos/v1/orders/print-batch", handler.Path);
        Assert.Equal(deviceId.ToString("D"), handler.DeviceId);
        Assert.NotNull(handler.OrderIds);
        Assert.Equal(orderId, Assert.Single(handler.OrderIds));
    }

    [Fact]
    public async Task Recovered_order_cancel_uses_the_device_endpoint_and_stable_idempotency_key()
    {
        var orderId = Guid.NewGuid();
        var handler = new CancelOrderHandler(orderId);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test") };
        var client = Client(http, Guid.NewGuid());
        var session = Session();
        var draftId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var authorization = new PosSensitiveActionAuthorization(
            null,
            approvalId,
            operationId,
            session,
            supervisorId,
            "sales.drafts.restart");

        var result = await client.CancelAsync(
            session, orderId, draftId, authorization,
            "Venta reiniciada.", "cancel-operation", default);

        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/pos/v1/orders/{orderId:D}/cancel", handler.Path);
        Assert.Equal("cancel-operation", handler.IdempotencyKey);
        Assert.Equal("Venta reiniciada.", handler.Reason);
        Assert.Equal(draftId, handler.RestartAuthorization?.DraftId);
        Assert.Equal(supervisorId, handler.RestartAuthorization?.AuthorizedByUserId);
        Assert.Equal(approvalId, handler.RestartAuthorization?.ApprovalRequestId);
        Assert.Equal(operationId, handler.RestartAuthorization?.OperationId);
        Assert.Equal("sales.drafts.restart", handler.RestartAuthorization?.PermissionResource);
    }

    [Fact]
    public async Task Order_server_problem_detail_is_preserved_without_exposing_unstructured_content()
    {
        using var detailedHttp = new HttpClient(new RejectionHandler(
            HttpStatusCode.Forbidden,
            JsonContent.Create(new { detail = "Permission 'orders.read' is required." })))
        { BaseAddress = new Uri("https://auraly.test") };
        var detailed = await Assert.ThrowsAsync<PosOrderServerException>(() =>
            Client(detailedHttp, Guid.NewGuid()).PrintBatchAsync(
                Session(), [Guid.NewGuid()], default));
        Assert.Equal("Permission 'orders.read' is required.", detailed.Message);

        using var legacyHttp = new HttpClient(new RejectionHandler(
            HttpStatusCode.MethodNotAllowed,
            new StringContent("<html>server internals</html>")))
        { BaseAddress = new Uri("https://auraly.test") };
        var legacy = await Assert.ThrowsAsync<PosOrderServerException>(() =>
            Client(legacyHttp, Guid.NewGuid()).PrintBatchAsync(
                Session(), [Guid.NewGuid()], default));
        Assert.Contains("versión de Auraly Server", legacy.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("html", legacy.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PosOrderServerClient Client(HttpClient http, Guid deviceId) => new(
        http,
        new PosDeviceCredentials(deviceId, "device-secret"),
        new PosEdgeRuntimeContext(
            new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()),
            new WarehouseId(Guid.NewGuid()),
            new DeviceId(deviceId),
            warehouseAllowsNegativeStock: false));

    private static PosLocalUserSession Session() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "admin",
        "Administrador",
        ["orders.read"],
        DateTimeOffset.UtcNow.AddHours(1),
        "session-token");

    private sealed class PrintBatchHandler(Guid expectedOrderId) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? DeviceId { get; private set; }
        public Guid[]? OrderIds { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            DeviceId = request.Headers.GetValues("X-Auraly-Device-Id").Single();
            var payload = await request.Content!.ReadFromJsonAsync<PrintPayload>(
                cancellationToken);
            OrderIds = payload!.OrderIds;
            Assert.Equal(expectedOrderId, Assert.Single(OrderIds));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<OrderPrintDocument>())
            };
        }
    }

    private sealed class SaveHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? DeviceId { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public PosSaveOrderRequest? Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;
            DeviceId = request.Headers.GetValues("X-Auraly-Device-Id").Single();
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            Payload = await request.Content!.ReadFromJsonAsync<PosSaveOrderRequest>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PosSaveOrderResponse(
                    Guid.NewGuid(), "PED-1", "Confirmed", 170m, false, []))
            };
        }
    }

    private sealed class RejectionHandler(
        HttpStatusCode statusCode,
        HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = content
            });
    }

    private sealed class CancelOrderHandler(Guid orderId) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Reason { get; private set; }
        public RestartAuthorizationPayload? RestartAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            var payload = await request.Content!.ReadFromJsonAsync<CancelPayload>(cancellationToken);
            Reason = payload!.Reason;
            RestartAuthorization = payload.RestartAuthorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CancelOrderResponse(
                    orderId, "PED-1", "Cancelled", false))
            };
        }
    }

    private sealed record PrintPayload(Guid[] OrderIds);
    private sealed record CancelPayload(
        string Reason,
        RestartAuthorizationPayload RestartAuthorization);
    private sealed record RestartAuthorizationPayload(
        Guid DraftId,
        string PermissionResource,
        Guid AuthorizedByUserId,
        Guid? ApprovalRequestId,
        Guid OperationId);
}
