namespace Auraly.Pos.Edge.Host;

public sealed record PosSynchronizationStatus(
    bool IsSynchronizing,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessfulAt,
    bool LastAttemptFailed);

public sealed class PosSynchronizationState
{
    private readonly object gate = new();
    private PosSynchronizationStatus status = new(false, null, null, false);

    public PosSynchronizationStatus Current
    {
        get { lock (gate) return status; }
    }

    public void Begin()
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = true,
                LastAttemptAt = DateTimeOffset.UtcNow,
                LastAttemptFailed = false
            };
    }

    public void Succeeded()
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = false,
                LastSuccessfulAt = DateTimeOffset.UtcNow,
                LastAttemptFailed = false
            };
    }

    public void Failed() { lock (gate) status = status with { IsSynchronizing = false, LastAttemptFailed = true }; }
}
