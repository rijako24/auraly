namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Mensajes visibles al usuario final. Obligatorios por tenant — sin defaults en español en código.
/// </summary>
public sealed class AgentHumanMessages
{
    public string EscalationUserMessage { get; init; } = string.Empty;

    public string SemanticTriggerLineFormat { get; init; } = string.Empty;

    public string PaidSlotRescheduleAction { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> SemanticTriggers { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
