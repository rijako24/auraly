namespace MimosBabySpa.Application.Services;

public sealed class TimedProcessSchedulePolicy : ITimedProcessSchedulePolicy
{
    private static readonly DateTime UnixEpochUtc = DateTime.UnixEpoch;

    public TimedProcessScheduleDecision Evaluate(
        ITimedProcess process,
        TimedProcessScheduleSnapshot schedule,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(process.Name))
            return TimedProcessScheduleDecision.Skip("process name is empty");

        if (!schedule.TryGet(process.Name, out var configured))
            return TimedProcessScheduleDecision.Run("process has no configured schedule");

        if (!configured.Enabled)
            return TimedProcessScheduleDecision.Skip("process is disabled");

        if (configured.IntervalMinutes <= 0)
            return TimedProcessScheduleDecision.Skip("process interval is invalid");

        if (configured.IntervalMinutes == 1)
            return TimedProcessScheduleDecision.Run("process interval is every minute");

        var tickUtc = utcNow.Kind == DateTimeKind.Utc
            ? utcNow
            : utcNow.ToUniversalTime();
        var elapsedMinutes = (long)Math.Floor((tickUtc - UnixEpochUtc).TotalMinutes);

        return elapsedMinutes % configured.IntervalMinutes == 0
            ? TimedProcessScheduleDecision.Run("process interval is due")
            : TimedProcessScheduleDecision.Skip("process interval is not due");
    }
}
