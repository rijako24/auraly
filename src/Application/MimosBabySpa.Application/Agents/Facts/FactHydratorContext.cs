namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Contexto de turno disponible para los resolutores de facts.
/// Inmutable — se construye una vez por turno en AgentConversationService.
/// </summary>
public sealed class FactHydratorContext
{
    /// <summary>Teléfono del canal (WhatsApp / simulador). Vacío si no aplica.</summary>
    public string ChannelPhone { get; init; } = string.Empty;

    /// <summary>Email del canal / conversación, si se conoce.</summary>
    public string? ConversationEmail { get; init; }
}
