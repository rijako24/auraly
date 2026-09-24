using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosSalesReturnServerClientTests
{
    [Fact]
    public async Task Return_query_is_forwarded_without_requiring_a_work_session_header()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var handler = new ReturnHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test/") };
        var store = new PosOfflineWorkSessionClosureStore(
            "Data Source=:memory:", TimeProvider.System);
        var client = new PosSalesReturnServerClient(http,
            new PosDeviceCredentials(deviceId, "device-secret"), store);
        var session = new PosLocalUserSession(Guid.NewGuid(), workSessionId, userId,
            "cashier", "Cashier", ["sales.returns.create"],
            DateTimeOffset.UtcNow.AddHours(1), "session-token");
        var body = JsonSerializer.SerializeToElement(new
        {
            context = new { businessId = Guid.NewGuid(), workSessionId },
            query = new { page = 1, pageSize = 25 }
        });

        var result = await client.SearchAsync(body, session, default);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("/api/pos/v1/sales-returns/search", handler.Path);
        Assert.Equal(deviceId.ToString("D"), handler.DeviceId);
        Assert.Equal(userId.ToString("D"), handler.UserId);
        Assert.Null(handler.WorkSessionId);
    }

    [Fact]
    public async Task Confirmed_refund_is_projected_once_into_its_local_work_session()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-return-{Guid.NewGuid():N}.db");
        try
        {
            var returnId = Guid.NewGuid();
            var workSessionId = Guid.NewGuid();
            var handler = new ConfirmReturnHandler(returnId, workSessionId);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test/") };
            var store = new PosOfflineWorkSessionClosureStore(
                $"Data Source={path}", TimeProvider.System);
            await store.InitializeAsync();
            var client = new PosSalesReturnServerClient(http,
                new PosDeviceCredentials(Guid.NewGuid(), "device-secret"), store);
            var session = new PosLocalUserSession(Guid.NewGuid(), workSessionId, Guid.NewGuid(),
                "cashier", "Cashier", ["sales.returns.create"],
                DateTimeOffset.UtcNow.AddHours(1), "session-token");
            var body = JsonSerializer.SerializeToElement(new
            {
                returnId,
                economicResolution = "Refund",
                refundMethodCode = "CreditCard",
                workSessionId
            });

            await client.ConfirmAsync(body, session, default);
            await client.ConfirmAsync(body, session, default);

            var refund = Assert.Single(await store.ReadRefundsAsync(workSessionId));
            Assert.Equal(returnId, refund.ReturnId);
            Assert.Equal("CreditCard", refund.PaymentMethodCode);
            Assert.Equal(12_500m, refund.Amount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Confirmed_customer_credit_is_counted_locally_without_a_refund_method()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-credit-return-{Guid.NewGuid():N}.db");
        try
        {
            var returnId = Guid.NewGuid();
            var workSessionId = Guid.NewGuid();
            using var http = new HttpClient(new ConfirmReturnHandler(returnId, workSessionId, null))
                { BaseAddress = new Uri("https://auraly.test/") };
            var store = new PosOfflineWorkSessionClosureStore($"Data Source={path}", TimeProvider.System);
            await store.InitializeAsync();
            var client = new PosSalesReturnServerClient(http,
                new PosDeviceCredentials(Guid.NewGuid(), "device-secret"), store);
            var session = new PosLocalUserSession(Guid.NewGuid(), workSessionId, Guid.NewGuid(),
                "cashier", "Cashier", ["sales.returns.create"],
                DateTimeOffset.UtcNow.AddHours(1), "session-token");
            var body = JsonSerializer.SerializeToElement(new
            {
                returnId, economicResolution = "CustomerCredit", workSessionId
            });

            await client.ConfirmAsync(body, session, default);
            await client.ConfirmAsync(body, session, default);

            var credit = Assert.Single(await store.ReadRefundsAsync(workSessionId));
            Assert.Equal(returnId, credit.ReturnId);
            Assert.Equal("CustomerCredit", credit.PaymentMethodCode);
            Assert.Equal(12_500m, credit.Amount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejected_return_without_detail_reports_server_status_and_code()
    {
        using var http = new HttpClient(new RejectedReturnHandler())
        { BaseAddress = new Uri("https://auraly.test/") };
        var client = new PosSalesReturnServerClient(http,
            new PosDeviceCredentials(Guid.NewGuid(), "device-secret"),
            new PosOfflineWorkSessionClosureStore("Data Source=:memory:", TimeProvider.System));
        var session = new PosLocalUserSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "cashier", "Cashier", ["sales.returns.create"],
            DateTimeOffset.UtcNow.AddHours(1), "session-token");

        var error = await Assert.ThrowsAsync<PosSalesReturnServerException>(() =>
            client.ConfirmAsync(JsonSerializer.SerializeToElement(new { returnId = Guid.NewGuid() }),
                session, CancellationToken.None));

        Assert.Equal(500, error.StatusCode);
        Assert.Contains("HTTP 500", error.Message);
        Assert.Contains("Internal Server Error", error.Message);
    }

    private sealed class ReturnHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? DeviceId { get; private set; }
        public string? UserId { get; private set; }
        public string? WorkSessionId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            DeviceId = Header(request, "X-Auraly-Device-Id");
            UserId = Header(request, "X-Auraly-User-Id");
            WorkSessionId = Header(request, "X-Auraly-Work-Session-Id");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = Array.Empty<object>() })
            });
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }

    private sealed class ConfirmReturnHandler(Guid returnId, Guid workSessionId,
        string? refundMethodCode = "CreditCard")
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new
                {
                    returnId,
                    movementId = Guid.NewGuid(),
                    documentNumber = "DVT-1",
                    status = "Accepted",
                    processingSequence = 1,
                    idempotentReplay = false,
                    totalAmount = 12_500m,
                    refundMethodCode,
                    workSessionId
                })
            });
    }

    private sealed class RejectedReturnHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = JsonContent.Create(new { title = "Internal Server Error", status = 500 })
            });
    }
}
