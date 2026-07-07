namespace MimosBabySpa.Application.Services;

public interface ITimedProcessSchedulePolicy
{
    TimedProcessScheduleDecision Evaluate(
        ITimedProcess process,
        TimedProcessScheduleSnapshot schedule,
        DateTime utcNow);
}
