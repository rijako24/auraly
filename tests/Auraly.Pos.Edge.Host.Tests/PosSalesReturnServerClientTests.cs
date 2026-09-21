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
        var client = new PosSalesReturnServerClient(http,
            new PosDeviceCredentials(deviceId, "device-secret"));
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
}
