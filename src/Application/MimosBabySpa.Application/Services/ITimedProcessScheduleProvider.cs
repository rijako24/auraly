namespace MimosBabySpa.Application.Services;

public interface ITimedProcessScheduleProvider
{
    Task<TimedProcessScheduleSnapshot> GetScheduleAsync(CancellationToken ct = default);
}
