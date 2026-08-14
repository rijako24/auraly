namespace Auraly.Platform.Domain.Enums;

public enum ScheduledAutomationJobStatus
{
    Pending = 0,
    Locked = 1,
    Sent = 2,
    Skipped = 3,
    Failed = 4,
    Cancelled = 5
}
