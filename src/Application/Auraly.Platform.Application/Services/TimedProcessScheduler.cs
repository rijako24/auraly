using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Services;

public sealed class TimedProcessScheduler : ITimedProcessScheduler
{
    private readonly IEnumerable<ITimedProcess> _processes;
    private readonly ITimedProcessScheduleProvider _scheduleProvider;
    private readonly ITimedProcessSchedulePolicy _schedulePolicy;
    private readonly ILogger<TimedProcessScheduler> _logger;

    public TimedProcessScheduler(
        IEnumerable<ITimedProcess> processes,
        ITimedProcessScheduleProvider scheduleProvider,
        ITimedProcessSchedulePolicy schedulePolicy,
        ILogger<TimedProcessScheduler> logger)
    {
        _processes = processes;
        _scheduleProvider = scheduleProvider;
        _schedulePolicy = schedulePolicy;
        _logger = logger;
    }

    public async Task RunDueAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var schedule = await _scheduleProvider.GetScheduleAsync(ct);

        foreach (var process in _processes)
        {
            var decision = _schedulePolicy.Evaluate(process, schedule, utcNow);
            if (!decision.ShouldRun)
            {
                _logger.LogDebug(
                    "TimedProcessScheduler: skipped {Process}. Reason={Reason}",
                    process.Name,
                    decision.Reason);
                continue;
            }

            try
            {
                _logger.LogInformation("TimedProcessScheduler: starting {Process}", process.Name);
                await process.RunAsync(ct);
                _logger.LogInformation("TimedProcessScheduler: finished {Process}", process.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimedProcessScheduler: process {Process} failed", process.Name);
            }
        }
    }
}
