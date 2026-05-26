namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Tool destructiva ejecutada por el motor cuando se cumplen las condiciones.
/// Solo corre una vez (el stage queda en CompletedActionStages tras el éxito).
/// </summary>
public sealed class AgentFlowStageExecute
{
    /// <summary>
    /// Condición adicional para activar el execute dentro del stage.
    /// Útil para ramificar entre flujo con/sin anticipo usando @result.flow.
    /// Si null, el execute aplica siempre que las condiciones del stage se cumplan.
    /// </summary>
    public AgentFlowStageCondition? AppliesWhen { get; init; }

    /// <summary>Nombre registrado de la tool (p. ej. "create_reservation").</summary>
    public string Tool { get; init; } = string.Empty;

    /// <summary>Argumentos resolvibles: @fact.X, @const.X, @result.X.</summary>
    public IReadOnlyDictionary<string, string> Args { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
