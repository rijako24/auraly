namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Variante de una etapa del flujo para un engagement context específico.
/// Permite adaptar el objetivo, el hint y las restricciones según si el
/// cliente es nuevo, recurrente, o viene de una sesión en curso.
/// </summary>
public sealed class AgentFlowStageVariant
{
    /// <summary>Override del goal de la etapa para este engagement. Null = usa el goal base.</summary>
    public string? Goal { get; init; }

    /// <summary>
    /// Plantilla sugerida para el LLM en este engagement.
    /// Ej.: "¡Hola! Soy Mimi de Mimo's Baby Spa..." para firstEver.
    /// El LLM adapta el hint al mensaje concreto del cliente.
    /// </summary>
    public string? Hint { get; init; }

    /// <summary>Override de constraints para este engagement. Null = usa constraints base.</summary>
    public StageConstraints? Constraints { get; init; }
}
