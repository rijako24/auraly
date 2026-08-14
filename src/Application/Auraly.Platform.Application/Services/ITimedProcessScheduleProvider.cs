namespace Auraly.Platform.Application.Services;

public interface ITimedProcessScheduleProvider
{
    Task<TimedProcessScheduleSnapshot> GetScheduleAsync(CancellationToken ct = default);
}
