namespace Auraly.Platform.Application.Services;

public sealed class TimedProcessSchedule
{
    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 1;
}
