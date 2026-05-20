namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Contexto compartido entre todas las tools de un turno.
/// Inyecta las identidades de la sesión sin necesidad de pasar parámetros extra en cada tool.
/// </summary>
public sealed class AgentToolContext
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid ConversationId { get; init; }

    /// <summary>Teléfono del cliente. Usado por tools de pago y escalación.</summary>
    public string CustomerPhone { get; init; } = string.Empty;

    /// <summary>Nombre del cliente si ya fue recolectado.</summary>
    public string? CustomerName { get; init; }

    /// <summary>
    /// Contactos de escalación del agente (números WhatsApp de admins).
    /// Leídos de Agent.SettingsJson y propagados desde AgentConversationService.
    /// </summary>
    public IReadOnlyList<string> EscalationContacts { get; init; } = [];

    /// <summary>
    /// Número de iteraciones de tool calling en el turno actual.
    /// Permite que las tools registren si van a contribuir a un loop.
    /// </summary>
    public int CurrentToolIteration { get; set; }

    /// <summary>
    /// Contador de tool calls fallidas consecutivas en el turno.
    /// El orquestador lo incrementa y activa auto-escalación si supera el umbral.
    /// </summary>
    public int ConsecutiveToolErrors { get; set; }
}
