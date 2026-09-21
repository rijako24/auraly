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
    public async Task History_is_forwarded_with_device_and_business_scope_only()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        var context = new OnlineSalesHistoryContext(Guid.NewGuid());
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
            ["sales.reprint"],
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
        Assert.Null(handler.WorkSessionId);
        Assert.Equal(context.BusinessId, handler.Request?.Context.BusinessId);
    }

    [Fact]
    public async Task History_option_search_does_not_forward_a_work_session()
    {
        var context = new OnlineSalesHistoryContext(Guid.NewGuid());
        var handler = new HistoryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test/") };
        var client = new PosSalesHistoryServerClient(
            http, new PosDeviceCredentials(Guid.NewGuid(), "device-secret"));
        var session = new PosLocalUserSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cashier", "Cashier",
            ["sales.reprint"], DateTimeOffset.UtcNow.AddHours(1), "session-token");

        var page = await client.SearchCustomersAsync(
            new SearchOnlineSalesHistoryOptionsRequest(context, "cliente", Take: 10),
            session,
            default);

        Assert.Empty(page.Items);
        Assert.Equal("/api/pos/v1/history/customers/search", handler.Path);
        Assert.Null(handler.WorkSessionId);
        Assert.Equal(context.BusinessId, handler.OptionsRequest?.Context.BusinessId);
    }

    private sealed class HistoryHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? DeviceId { get; private set; }
        public string? DeviceSecret { get; private set; }
        public string? UserId { get; private set; }
        public string? WorkSessionId { get; private set; }
        public SearchOnlineSalesIssuedSalesRequest? Request { get; private set; }
        public SearchOnlineSalesHistoryOptionsRequest? OptionsRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            DeviceId = Header(request, "X-Auraly-Device-Id");
            DeviceSecret = Header(request, "X-Auraly-Device-Secret");
            UserId = Header(request, "X-Auraly-User-Id");
            WorkSessionId = Header(request, "X-Auraly-Work-Session-Id");
            if (Path!.EndsWith("/customers/search", StringComparison.Ordinal))
            {
                OptionsRequest = await request.Content!
                    .ReadFromJsonAsync<SearchOnlineSalesHistoryOptionsRequest>(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new OnlineSalesCustomerPage([], false, null))
                };
            }
            Request = await request.Content!
                .ReadFromJsonAsync<SearchOnlineSalesIssuedSalesRequest>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new OnlineSalesIssuedSalePage([], false, null))
            };
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
