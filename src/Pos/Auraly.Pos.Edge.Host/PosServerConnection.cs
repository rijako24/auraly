namespace Auraly.Pos.Edge.Host;

using System.Net;
using Auraly.Pos.Edge.Infrastructure;

public sealed class PosServerConnectionState
{
    private int _connected;

    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    public bool MarkConnected() => Interlocked.Exchange(ref _connected, 1) == 0;

    public bool MarkDisconnected() => Interlocked.Exchange(ref _connected, 0) == 1;
}

public sealed class PosPushConnectionState
{
    private int connected;

    public bool IsConnected => Volatile.Read(ref connected) == 1;

    public void MarkConnected() => Interlocked.Exchange(ref connected, 1);

    public void MarkDisconnected() => Interlocked.Exchange(ref connected, 0);
}

public sealed class PosServerConnectionHandler(
    HttpMessageHandler innerHandler,
    PosServerConnectionState state,
    IPosSynchronizationProgressSink? progress = null) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var changed = response.StatusCode == HttpStatusCode.Unauthorized
                ? state.MarkDisconnected()
                : state.MarkConnected();
            if (changed) progress?.Publish();
            return response;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            if (state.MarkDisconnected()) progress?.Publish();
            throw;
        }
    }
}
