namespace Auraly.Pos.Edge.Host;

public sealed class PosServerConnectionState
{
    private int _connected;

    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    public void MarkConnected() => Interlocked.Exchange(ref _connected, 1);

    public void MarkDisconnected() => Interlocked.Exchange(ref _connected, 0);
}

public sealed class PosServerConnectionHandler(
    HttpMessageHandler innerHandler,
    PosServerConnectionState state) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            state.MarkConnected();
            return response;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            state.MarkDisconnected();
            throw;
        }
    }
}
