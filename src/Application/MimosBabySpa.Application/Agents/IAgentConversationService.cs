namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Punto de entrada único para procesar un mensaje de usuario con el agente.
/// Reemplaza a IFlowOrchestrationService y HybridTransactionalOrchestrator.
/// El orquestador no toma decisiones de negocio — delega al LLM via Function Calling.
/// </summary>
public interface IAgentConversationService
{
    /// <summary>
    /// Procesa un mensaje del usuario en el contexto de una conversación.
    /// El historial se carga desde BD (MessageService) y el estado desde ConversationStateManager.
    /// </summary>
    Task<AgentTurnResult> ProcessMessageAsync(
        Guid agentId,
        Guid conversationId,
        string userMessage,
        CancellationToken cancellationToken = default);
}
