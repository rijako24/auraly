using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.API.Functions;

public sealed class TimedProcessDispatcherFunction
{
    private readonly IEnumerable<ITimedProcess> _processes;
    private readonly ILogger<TimedProcessDispatcherFunction> _logger;

    public TimedProcessDispatcherFunction(
        IEnumerable<ITimedProcess> processes,
        ILogger<TimedProcessDispatcherFunction> logger)
    {
        _processes = processes;
        _logger = logger;
    }

    [Function("TimedProcessDispatcher")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timerInfo, CancellationToken ct)
    {
        foreach (var process in _processes)
        {
            try
            {
                _logger.LogInformation("TimedProcessDispatcher: starting {Process}", process.Name);
                await process.RunAsync(ct);
                _logger.LogInformation("TimedProcessDispatcher: finished {Process}", process.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimedProcessDispatcher: process {Process} failed", process.Name);
            }
        }
    }
}
