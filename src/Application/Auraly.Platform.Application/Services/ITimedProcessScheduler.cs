namespace Auraly.Platform.Application.Services;

public interface ITimedProcessScheduler
{
    Task RunDueAsync(DateTime utcNow, CancellationToken ct = default);
}
