namespace Auraly.Platform.Application.Agents.Configuration;

/// <summary>
/// Configures the single contextual follow-up that may be sent while a conversation
/// is waiting for the customer. The stage response decides whether a wait exists.
/// </summary>
public sealed class ConversationFollowUpDefinitions
{
    public bool Enabled { get; init; }
    public int DelayMinutes { get; init; } = 120;

    /// <summary>Renderer-only guidance for the follow-up message.</summary>
    public string Guidance { get; init; } =
        "Retoma con empatía y sin presionar. Reconoce que la persona puede estar ocupada, "
        + "ofrece seguir disponible y vuelve brevemente a la pregunta pendiente sin repetir toda la respuesta.";

    /// <summary>Optional deterministic fallback when contextual rendering is unavailable.</summary>
    public string? FallbackSequence { get; init; }

    /// <summary>Defers follow-ups to the next configured operating window.</summary>
    public bool RespectOperatingHours { get; init; } = true;
}
