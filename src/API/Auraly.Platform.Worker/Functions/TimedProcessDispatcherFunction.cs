using Microsoft.Azure.Functions.Worker;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Worker.Functions;

public sealed class TimedProcessDispatcherFunction
{
    private readonly ITimedProcessScheduler _scheduler;

    public TimedProcessDispatcherFunction(ITimedProcessScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    [Function("TimedProcessDispatcher")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timerInfo, CancellationToken ct)
    {
        await _scheduler.RunDueAsync(DateTime.UtcNow, ct);
    }
}
