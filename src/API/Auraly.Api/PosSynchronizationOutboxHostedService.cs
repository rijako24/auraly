using Auraly.Infrastructure.Persistence;

namespace Auraly.Api;

public sealed class PosSynchronizationOutboxHostedService(
    SqlPosSynchronizationOutboxDispatcher dispatcher)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        dispatcher.RunAsync(stoppingToken);
}
