namespace Auraly.Platform.Infrastructure.Services;

/// <summary>
/// Renueva una presencia temporal mientras una operacion larga sigue en curso.
/// La liberacion cancela y espera el loop para impedir renovaciones despues de responder.
/// </summary>
public sealed class TypingIndicatorHeartbeat : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cancellation;
    private readonly Task _loop;
    private int _disposed;

    public TypingIndicatorHeartbeat(
        Func<CancellationToken, Task> refresh,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        _refresh = refresh;
        _interval = interval;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_cancellation.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _refresh(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Best-effort: una renovacion fallida no puede tumbar la respuesta del agente.
            }

            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _cancellation.CancelAsync();
        await _loop;
        _cancellation.Dispose();
    }
}
