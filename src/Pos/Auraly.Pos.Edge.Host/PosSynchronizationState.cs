namespace Auraly.Pos.Edge.Host;

public sealed record PosSynchronizationStatus(
    bool IsSynchronizing,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessfulAt,
    bool LastAttemptFailed,
    IReadOnlyList<string> ActiveStages,
    string? FailedStage,
    string? LastError,
    bool AutomaticRetryScheduled,
    int AutomaticRetryAttempt);

public sealed class PosSynchronizationState
{
    private readonly object gate = new();
    private PosSynchronizationStatus status = new(
        false, null, null, false, [], null, null, false, 0);

    public PosSynchronizationStatus Current
    {
        get { lock (gate) return status; }
    }

    public void Begin(bool preserveFailure = false)
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = true,
                LastAttemptAt = DateTimeOffset.UtcNow,
                ActiveStages = [],
                LastAttemptFailed = preserveFailure && status.LastAttemptFailed,
                FailedStage = preserveFailure ? status.FailedStage : null,
                LastError = preserveFailure ? status.LastError : null,
                AutomaticRetryScheduled = false
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

    public void StageFailed(string stage, string reason)
    {
        lock (gate)
            status = status with
            {
                ActiveStages = status.ActiveStages
                    .Where(value => !string.Equals(value, stage, StringComparison.Ordinal))
                    .ToArray(),
                FailedStage = stage,
                LastError = reason
            };
    }

    public void Succeeded(bool preserveFailure = false)
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = false,
                LastSuccessfulAt = DateTimeOffset.UtcNow,
                ActiveStages = [],
                LastAttemptFailed = preserveFailure && status.LastAttemptFailed,
                FailedStage = preserveFailure ? status.FailedStage : null,
                LastError = preserveFailure ? status.LastError : null,
                AutomaticRetryScheduled = false,
                AutomaticRetryAttempt = preserveFailure ? status.AutomaticRetryAttempt : 0
            };
    }

    public void Failed() { lock (gate) status = status with { IsSynchronizing = false, LastAttemptFailed = true }; }

    public void RetryScheduled(int attempt)
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = false,
                LastAttemptFailed = false,
                FailedStage = null,
                LastError = null,
                AutomaticRetryScheduled = true,
                AutomaticRetryAttempt = attempt
            };
    }

    public void AutomaticRetriesExhausted()
    {
        lock (gate)
            status = status with
            {
                IsSynchronizing = false,
                LastAttemptFailed = true,
                AutomaticRetryScheduled = false,
                AutomaticRetryAttempt = 3,
                LastError = "La preparación no pudo conectarse con Auraly después de tres reintentos. Reintenta ahora o repite el enrolamiento."
            };
    }
}
