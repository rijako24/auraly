namespace MimosBabySpa.Application.Services;

public sealed class TimedProcessScheduleSnapshot
{
    public static TimedProcessScheduleSnapshot Empty { get; } = new(new Dictionary<string, TimedProcessSchedule>());

    private readonly IReadOnlyDictionary<string, TimedProcessSchedule> _schedules;

    public TimedProcessScheduleSnapshot(IReadOnlyDictionary<string, TimedProcessSchedule> schedules)
    {
        _schedules = new Dictionary<string, TimedProcessSchedule>(
            schedules,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string processName, out TimedProcessSchedule schedule) =>
        _schedules.TryGetValue(processName, out schedule!);
}
