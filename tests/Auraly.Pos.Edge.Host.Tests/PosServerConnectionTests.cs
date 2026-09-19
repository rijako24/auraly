using System.Net;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosServerConnectionTests
{
    [Fact]
    public async Task Accepted_response_marks_the_authenticated_server_connection_as_available()
    {
        var state = new PosServerConnectionState();
        using var client = new HttpClient(new PosServerConnectionHandler(
            new ResponseHandler(HttpStatusCode.OK), state));

        using var response = await client.GetAsync("https://server.test/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(state.IsConnected);
    }

    [Fact]
    public async Task Unauthorized_response_does_not_report_a_usable_server_connection()
    {
        var state = new PosServerConnectionState();
        state.MarkConnected();
        using var client = new HttpClient(new PosServerConnectionHandler(
            new ResponseHandler(HttpStatusCode.Unauthorized), state));

        using var response = await client.GetAsync("https://server.test/sync");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(state.IsConnected);
    }

    [Fact]
    public async Task Transport_failure_marks_the_server_connection_as_unavailable()
    {
        var state = new PosServerConnectionState();
        state.MarkConnected();
        using var observedClient = new HttpClient(
            new PosServerConnectionHandler(new ThrowingHandler(), state));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => observedClient.GetAsync("https://server.test/sync"));

        Assert.False(state.IsConnected);
    }

    [Fact]
    public async Task Connection_change_is_published_immediately_to_the_local_ui()
    {
        var state = new PosServerConnectionState();
        var progress = new RecordingProgressSink();
        using var client = new HttpClient(new PosServerConnectionHandler(
            new ResponseHandler(HttpStatusCode.OK), state, progress));

        using var first = await client.GetAsync("https://server.test/catalog/page-1");
        using var second = await client.GetAsync("https://server.test/catalog/page-2");

        Assert.True(state.IsConnected);
        Assert.Equal(1, progress.PublishCount);
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Server unavailable");
    }

    private sealed class RecordingProgressSink : IPosSynchronizationProgressSink
    {
        public int PublishCount { get; private set; }

        public void Publish() => PublishCount++;
    }
}
