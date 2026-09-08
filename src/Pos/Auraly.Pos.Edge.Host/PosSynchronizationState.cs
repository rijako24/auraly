namespace Auraly.Pos.Edge.Host;

public sealed record PosSynchronizationStatus(
    bool IsSynchronizing,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessfulAt,
    bool LastAttemptFailed,
    IReadOnlyList<string> ActiveStages,
    string? FailedStage,
    string? LastError);

public sealed class PosSynchronizationState
{
    private readonly object gate = new();
    private PosSynchronizationStatus status = new(
        false, null, null, false, [], null, null);

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
                LastAttemptFailed = false,
                ActiveStages = []
            };
    }

    public void StageStarted(string stage)
    {
        lock (gate)
            status = status with
            {
                ActiveStages = status.ActiveStages
                    .Append(stage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
    }

    public void StageSucceeded(string stage)
    {
        lock (gate)
            status = status with
            {
                ActiveStages = status.ActiveStages
                    .Where(value => !string.Equals(value, stage, StringComparison.Ordinal))
                    .ToArray()
            };
    }

    public void StageFailed(string stage)
    {
        lock (gate)
            status = status with
            {
                ActiveStages = status.ActiveStages
                    .Where(value => !string.Equals(value, stage, StringComparison.Ordinal))
                    .ToArray(),
                FailedStage = stage,
                LastError = $"No fue posible sincronizar {stage}. Auraly lo intentará de nuevo automáticamente."
            };
    }

    public void Succeeded()
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = false,
                LastSuccessfulAt = DateTimeOffset.UtcNow,
                LastAttemptFailed = false,
                ActiveStages = [],
                FailedStage = null,
                LastError = null
            };
    }

    public void Failed() { lock (gate) status = status with { IsSynchronizing = false, LastAttemptFailed = true }; }
}
