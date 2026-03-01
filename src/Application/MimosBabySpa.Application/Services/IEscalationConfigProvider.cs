namespace MimosBabySpa.Application.Services;

/// <summary>
/// Proporciona el threshold de turnos degradados consecutivos para escalar a humano.
/// Lee de SystemConfiguration (HumanEscalationErrorThreshold).
/// </summary>
public interface IEscalationConfigProvider
{
    Task<int> GetConsecutiveDegradedThresholdAsync(CancellationToken ct = default);
}
