using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosSalesHistoryServerClientTests
{
    [Fact]
    public async Task History_is_forwarded_with_device_and_local_session_scope()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var context = new OnlineSalesDraftContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            workSessionId);
        var handler = new HistoryHandler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://auraly.test/")
        };
        var client = new PosSalesHistoryServerClient(
            http,
            new PosDeviceCredentials(deviceId, "device-secret"));
        var session = new PosLocalUserSession(
            Guid.NewGuid(),
            workSessionId,
            userId,
            "cashier",
            "Cashier",
            ["sales.create"],
            DateTimeOffset.UtcNow.AddHours(1),
            "session-token");

        var page = await client.SearchSalesAsync(
            new SearchOnlineSalesIssuedSalesRequest(context, Take: 20),
            session,
            default);

        Assert.Empty(page.Items);
        Assert.Equal("/api/pos/v1/history/sales/search", handler.Path);
        Assert.Equal(deviceId.ToString("D"), handler.DeviceId);
        Assert.Equal("device-secret", handler.DeviceSecret);
        Assert.Equal(userId.ToString("D"), handler.UserId);
        Assert.Equal(workSessionId.ToString("D"), handler.WorkSessionId);
        Assert.Equal(workSessionId, handler.Request?.Context.WorkSessionId);
    }

    private sealed class HistoryHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? DeviceId { get; private set; }
        public string? DeviceSecret { get; private set; }
        public string? UserId { get; private set; }
        public string? WorkSessionId { get; private set; }
        public SearchOnlineSalesIssuedSalesRequest? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            DeviceId = Header(request, "X-Auraly-Device-Id");
            DeviceSecret = Header(request, "X-Auraly-Device-Secret");
            UserId = Header(request, "X-Auraly-User-Id");
            WorkSessionId = Header(request, "X-Auraly-Work-Session-Id");
            Request = await request.Content!.ReadFromJsonAsync<SearchOnlineSalesIssuedSalesRequest>(
                cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new OnlineSalesIssuedSalePage([], false, null))
            };
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
