using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Models.Flow;

public class FlowTurnContext
{
    public FlowExecutionState State { get; set; } = new();
    public FlowDefinitionDocument FlowDefinition { get; set; } = new();
    public Agent Agent { get; set; } = null!;
    public Guid BusinessId { get; set; }

    /// <summary>
    /// Persistent conversation row (<c>Conversations</c>). Set by the orchestrator entry point
    /// from the caller-resolved conversation id (same as legacy <c>ProcessMessageAsync</c>).
    /// </summary>
    public Guid ConversationId { get; set; }

    public string UserIdentifier { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public int TurnNumber { get; set; }

    /// <summary>
    /// Recent conversation history (last N messages, oldest first).
    /// </summary>
    public List<(string Role, string Content)> ConversationHistory { get; set; } = new();

    /// <summary>
    /// Knowledge sources available to this agent for this turn.
    /// </summary>
    public List<KnowledgeSource> KnowledgeSources { get; set; } = new();

    /// <summary>
    /// Validation errors from extraction (e.g. invalid email format).
    /// Injected into prompts via {{validation_errors}}.
    /// </summary>
    public List<FlowValidationError> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Intentions detected in the current turn. Key = intention key, Value = true if detected.
    /// </summary>
    public Dictionary<string, bool> DetectedIntentions { get; set; } = new();

    /// <summary>
    /// Signals that the engine should re-evaluate flow logic in the same turn.
    /// </summary>
    public bool ShouldReEvaluate { get; set; }

    /// <summary>
    /// The bot response to send back to the user. Set by node handlers.
    /// </summary>
    public string? PendingBotResponse { get; set; }

    /// <summary>
    /// When set after a node handler runs, the engine stops traversal and returns this outcome immediately
    /// (e.g. global intention escalation from the Extract node).
    /// </summary>
    public FlowTurnTerminationRequest? TerminateTurn { get; set; }

    public void ReEvaluate() => ShouldReEvaluate = true;

    public bool IsIntentionDetected(string key) =>
        DetectedIntentions.TryGetValue(key, out var v) && v;
}

public record FlowValidationError(string VariableKey, string ProvidedValue, string ErrorMessage);
