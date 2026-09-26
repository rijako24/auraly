using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosOrdersServerClientTests
{
    [Fact]
    public async Task Order_command_is_forwarded_with_device_user_business_session_and_idempotency()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test/") };
        var client = new PosOrdersServerClient(http,
            new PosDeviceCredentials(deviceId, "device-secret"),
            new PosEdgeRuntimeContext(new TenantId(tenantId), new BusinessId(businessId),
                new WarehouseId(warehouseId), new DeviceId(deviceId), false),
            TestOpenings(http, new PosDeviceCredentials(deviceId, "device-secret")));
        var session = new PosLocalUserSession(Guid.NewGuid(), workSessionId, userId,
            "cashier", "Cashier", ["orders.invoice"],
            DateTimeOffset.UtcNow.AddHours(1), "session-token");
        var body = JsonSerializer.SerializeToElement(new { orderIds = new[] { Guid.NewGuid() } });

        var response = await client.SendAsync(HttpMethod.Post,
            "api/commerce/v1/orders/invoice", body, session, default, "batch:order");

        Assert.Equal(JsonValueKind.Object, response.ValueKind);
        Assert.Equal("/api/commerce/v1/orders/invoice", handler.Path);
        Assert.Equal(deviceId.ToString("D"), handler.Header("X-Auraly-Device-Id"));
        Assert.Equal("device-secret", handler.Header("X-Auraly-Device-Secret"));
        Assert.Equal(userId.ToString("D"), handler.Header("X-Auraly-User-Id"));
        Assert.Equal(businessId.ToString("D"), handler.Header("X-Auraly-Business-Id"));
        Assert.Equal(workSessionId.ToString("D"), handler.Header("X-Auraly-Work-Session-Id"));
        Assert.Equal("batch:order", handler.Header("Idempotency-Key"));
    }

    [Theory]
    [InlineData("OrderInventoryConflict", "OrderInventoryConflict")]
    [InlineData("Internal Server Error", "OrdersUnavailable")]
    public async Task Server_problem_is_preserved_for_the_local_client(string title, string code)
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict,
            new ProblemDetails { Title = title, Detail = "Sin existencia." });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test/") };
        var runtime = new PosEdgeRuntimeContext(new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()), new WarehouseId(Guid.NewGuid()),
            new DeviceId(Guid.NewGuid()), false);
        var client = new PosOrdersServerClient(http,
            new PosDeviceCredentials(runtime.DeviceId.Value, "device-secret"), runtime,
            TestOpenings(http, new PosDeviceCredentials(runtime.DeviceId.Value, "device-secret")));
        var session = new PosLocalUserSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "cashier", "Cashier", ["orders.create"],
            DateTimeOffset.UtcNow.AddHours(1), "session-token");

        var error = await Assert.ThrowsAsync<PosOrdersServerException>(() =>
            client.SendAsync(HttpMethod.Post, "api/commerce/v1/seller-orders",
                JsonSerializer.SerializeToElement(new { }), session, default));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal(code, error.Code);
        Assert.Equal("Sin existencia.", error.Message);
    }

    [Fact]
    public async Task Network_failure_is_reported_as_an_orders_connection_problem()
    {
        using var http = new HttpClient(new ThrowingHandler())
            { BaseAddress = new Uri("https://auraly.test/") };
        var runtime = new PosEdgeRuntimeContext(new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()), new WarehouseId(Guid.NewGuid()),
            new DeviceId(Guid.NewGuid()), false);
        var client = new PosOrdersServerClient(http,
            new PosDeviceCredentials(runtime.DeviceId.Value, "device-secret"), runtime,
            TestOpenings(http, new PosDeviceCredentials(runtime.DeviceId.Value, "device-secret")));
        var session = new PosLocalUserSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "cashier", "Cashier", ["orders.read"],
            DateTimeOffset.UtcNow.AddHours(1), "session-token");

        var error = await Assert.ThrowsAsync<PosOrdersServerException>(() =>
            client.SendAsync(HttpMethod.Get, "api/commerce/v1/orders", null,
                session, default));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal("OrdersUnavailable", error.Code);
        Assert.Contains("conexión con Auraly", error.Message);
    }

    [Fact]
    public async Task Non_json_server_failure_does_not_expose_or_parse_the_upstream_page()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadGateway,
            rawContent: "<html>upstream unavailable</html>");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test/") };
        var runtime = new PosEdgeRuntimeContext(new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()), new WarehouseId(Guid.NewGuid()),
            new DeviceId(Guid.NewGuid()), false);
        var client = new PosOrdersServerClient(http,
            new PosDeviceCredentials(runtime.DeviceId.Value, "device-secret"), runtime,
            TestOpenings(http, new PosDeviceCredentials(runtime.DeviceId.Value, "device-secret")));
        var session = new PosLocalUserSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "cashier", "Cashier", ["orders.read"],
            DateTimeOffset.UtcNow.AddHours(1), "session-token");

        var error = await Assert.ThrowsAsync<PosOrdersServerException>(() =>
            client.SendAsync(HttpMethod.Get, "api/commerce/v1/orders", null,
                session, default));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal("No fue posible procesar los pedidos.", error.Message);
        Assert.DoesNotContain("html", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PosWorkSessionOpenUploader TestOpenings(
        HttpClient http, PosDeviceCredentials credentials) =>
        new("Data Source=:memory:", http, credentials, TimeProvider.System,
            new PosSynchronizationEventLog(TimeProvider.System));

    private sealed class RecordingHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        ProblemDetails? problem = null,
        string? rawContent = null) : HttpMessageHandler
    {
        private HttpRequestMessage? request;
        public string? Path { get; private set; }

        public string? Header(string name) =>
            request?.Headers.TryGetValues(name, out var values) == true ? values.Single() : null;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage value, CancellationToken cancellationToken)
        {
            request = value;
            Path = value.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = statusCode == HttpStatusCode.OK
                    ? JsonContent.Create(new { accepted = true })
                    : rawContent is null
                        ? JsonContent.Create(problem)
                        : new StringContent(rawContent)
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("No route to host.");
    }
}
