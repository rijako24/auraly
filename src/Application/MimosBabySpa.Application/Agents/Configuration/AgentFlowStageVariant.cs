namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Variante de una etapa del flujo para un engagement context especifico.
/// Permite adaptar el objetivo y la guia conversacional segun si el cliente es nuevo,
/// recurrente, o viene de una sesion en curso.
/// </summary>
public sealed class AgentFlowStageVariant
{
    /// <summary>Override del goal de la etapa para este engagement. Null = usa el goal base.</summary>
    public string? Goal { get; init; }

    public string? ConversationGuidance { get; init; }
}
