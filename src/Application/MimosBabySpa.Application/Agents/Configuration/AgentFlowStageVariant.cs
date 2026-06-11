namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Variante de una etapa del flujo para un engagement context especifico.
/// Permite adaptar el objetivo y el hint segun si el cliente es nuevo,
/// recurrente, o viene de una sesion en curso.
/// </summary>
public sealed class AgentFlowStageVariant
{
    /// <summary>Override del goal de la etapa para este engagement. Null = usa el goal base.</summary>
    public string? Goal { get; init; }

    /// <summary>
    /// Plantilla sugerida para el LLM en este engagement.
    /// El LLM adapta el hint al mensaje concreto del cliente.
    /// </summary>
    public string? Hint { get; init; }
}
