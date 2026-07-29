using Auraly.Application.Fiscal;

namespace Auraly.Api;

public sealed class FiscalGenerationHostedService(
    IServiceScopeFactory scopes,
    ILogger<FiscalGenerationHostedService> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                for (var processed = 0; processed < 20 && !stoppingToken.IsCancellationRequested; processed++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
                    if (!await worker.ProcessNextAsync(workerId, stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The durable fiscal generation loop failed and will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}