namespace Auraly.Platform.Application.Agents;

/// <summary>
/// Punto de entrada único para procesar un mensaje de usuario con el agente.
/// </summary>
public interface IAgentConversationService
{
    /// <summary>
    /// Procesa un mensaje del usuario en el contexto de una conversación.
    /// <paramref name="channelPhone"/> es el identificador del cliente en el canal (ej. número WhatsApp).
    /// Se siembra en el estado cuando aún no hay teléfono de contacto persistido.
    /// </summary>
    Task<AgentTurnResult> ProcessMessageAsync(
        Guid agentId,
        Guid conversationId,
        string userMessage,
        string? channelPhone = null,
        CancellationToken cancellationToken = default,
        AgentInboundMetadata? inboundMetadata = null);
}

public sealed record AgentInboundMetadata(
    string? ProviderMessageId,
    string? ReplyToProviderMessageId,
    string? InteractivePayload,
    IReadOnlyDictionary<string, string>? Facts = null,
    string? RecipientPhoneNumberId = null);
