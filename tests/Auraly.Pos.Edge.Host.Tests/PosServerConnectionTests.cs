using System.Net;
using Auraly.Pos.Edge.Host;
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
}
