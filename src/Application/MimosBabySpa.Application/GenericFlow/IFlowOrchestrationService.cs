namespace MimosBabySpa.Application.GenericFlow;

public interface IFlowOrchestrationService
{
    /// <summary>
    /// Processes a single user message turn through the configured flow.
    /// </summary>
    /// <param name="conversationId">
    /// Domain conversation row (<c>Conversations</c>). Caller must resolve it first
    /// (same contract as <c>HybridTransactionalOrchestrator.ProcessMessageAsync</c>).
    /// </param>
    /// <param name="agentId">
    /// The agent that owns this channel. Resolved upstream from
    /// <c>BusinessWhatsAppNumbers.AgentId</c> when the WhatsApp message arrives.
    /// </param>
    /// <param name="userIdentifier">
    /// End-user identifier (e.g. phone). Session key for <c>FlowExecutionStates</c>
    /// and for contacting the customer (escalation, payment links).
    /// </param>
    /// <param name="userMessage">Raw message text from the user.</param>
    Task<FlowOrchestratorResult> ProcessTurnAsync(
        Guid conversationId,
        Guid agentId,
        string userIdentifier,
        string userMessage,
        CancellationToken ct = default);
}

public class FlowOrchestratorResult
{
    public bool Success { get; set; }
    public string BotResponse { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public bool IsEscalated { get; set; }
    public bool IsFlowComplete { get; set; }
    public string? CurrentNodeId { get; set; }
    public Dictionary<string, string?> Variables { get; set; } = new();

    public static FlowOrchestratorResult Ok(string response, string? nodeId = null, bool isComplete = false) =>
        new() { Success = true, BotResponse = response, CurrentNodeId = nodeId, IsFlowComplete = isComplete };

    public static FlowOrchestratorResult Escalated(string response) =>
        new() { Success = true, BotResponse = response, IsEscalated = true };

    public static FlowOrchestratorResult Err(string error, string fallback) =>
        new() { Success = false, ErrorMessage = error, BotResponse = fallback };
}
