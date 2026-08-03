using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed class ExternalCustomerReconciliationCommitState
{
    private bool pending;

    public void MarkPending() => pending = true;

    public bool ConsumeCommitted()
    {
        var value = pending;
        pending = false;
        return value;
    }
}

public sealed class ExternalCustomerReconciliationOutboxSignal
{
    private readonly Channel<byte> channel = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public void Notify() => channel.Writer.TryWrite(0);

    public async ValueTask WaitAsync(CancellationToken cancellationToken) =>
        await channel.Reader.ReadAsync(cancellationToken);

    public async Task WaitOrDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero) return;
        using var notificationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var notification = channel.Reader.ReadAsync(
            notificationCancellation.Token).AsTask();
        var timeout = Task.Delay(delay, cancellationToken);
        var completed = await Task.WhenAny(notification, timeout);
        if (completed == notification)
        {
            await notification;
            return;
        }

        await timeout;
        notificationCancellation.Cancel();
        try
        {
            await notification;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }
}

public sealed class ExternalCustomerReconciliationOutboxHostedService(
    ExternalCustomerReconciliationOutboxSignal signal,
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<ExternalCustomerReconciliationOutboxHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan InfrastructureRetry = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        signal.Notify();
        while (!stoppingToken.IsCancellationRequested)
        {
            await signal.WaitAsync(stoppingToken);
            var continueDispatching = true;
            while (continueDispatching && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<
                        SqlExternalCustomerReconciliationOutboxDispatcher>();
                    var outcome = await dispatcher.DispatchAvailableAsync(stoppingToken);
                    if (outcome.HasImmediateWork)
                        continue;
                    if (outcome.NextAttemptAt is DateTimeOffset next)
                    {
                        var delay = next - timeProvider.GetUtcNow();
                        await signal.WaitOrDelayAsync(delay, stoppingToken);
                        continue;
                    }
                    continueDispatching = false;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "External-customer reconciliation outbox dispatch failed; committed messages remain pending.");
                    await signal.WaitOrDelayAsync(InfrastructureRetry, stoppingToken);
                }
            }
        }
    }
}
