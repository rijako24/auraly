namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Accion transversal declarada por tenant. Sus tools pueden usarse aunque
/// la etapa activa tenga una whitelist distinta.
/// </summary>
public sealed class AgentGlobalAction
{
    public string Id { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string? ConversationGuidance { get; init; }

    /// <summary>
    /// Optional runtime conditions for this global action. Empty means always enabled.
    /// Supported tokens include runtime_state:&lt;name&gt;, fact:&lt;key&gt;, manageable_reservation.exists,
    /// payment.pending_checkout, payment.confirmed_without_reservation, and conversation.owner_human.
    /// </summary>
    public IReadOnlyList<string> RuntimeWhenAny { get; init; } = [];

    public IReadOnlyList<string> AllowedActions { get; init; } = [];

}
