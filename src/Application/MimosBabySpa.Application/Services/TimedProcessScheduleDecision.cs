namespace MimosBabySpa.Application.Services;

public sealed record TimedProcessScheduleDecision(bool ShouldRun, string Reason)
{
    public static TimedProcessScheduleDecision Run(string reason) => new(true, reason);

    public static TimedProcessScheduleDecision Skip(string reason) => new(false, reason);
}
