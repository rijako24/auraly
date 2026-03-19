using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Domain.Models.Flow;

/// <summary>
/// In-memory runtime state for a flow execution session.
/// Persisted as JSON in FlowExecutionStateEntity.
/// </summary>
public class FlowExecutionState
{
    public Guid StateId { get; set; } = Guid.NewGuid();
    public string CurrentNodeId { get; set; } = "start";

    /// <summary>
    /// True when the engine stopped at this node and is awaiting user input.
    /// </summary>
    public bool IsWaitingForUser { get; set; }

    public Dictionary<string, string?> Variables { get; set; } = new();
    public Dictionary<string, bool> Flags { get; set; } = new();

    /// <summary>
    /// Results stored by actions. Key = action node id, Value = result data.
    /// </summary>
    public Dictionary<string, Dictionary<string, object?>> ActionResults { get; set; } = new();

    public List<FlowTraceEntry> Trace { get; set; } = new();

    /// <summary>
    /// Recent conversation turns (user + assistant pairs) for extraction context.
    /// Oldest first. Capped by FlowEngineSettings.MaxConversationHistoryMessages * 2.
    /// </summary>
    public List<FlowConversationMessage> ConversationHistory { get; set; } = new();

    /// <summary>
    /// Counts turns where the extraction produced no useful output.
    /// Resets to 0 when at least one variable is successfully extracted.
    /// Used to trigger auto-escalation when DegradedTurnsBeforeEscalation threshold is reached.
    /// </summary>
    public int ConsecutiveDegradedTurns { get; set; }

    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime SessionStartedAt { get; set; } = DateTime.UtcNow;
    public ConversationOwner Owner { get; set; } = ConversationOwner.Bot;
    public PreviousFlowSessionSnapshot? PreviousSession { get; set; }

    public string? GetVariable(string key) =>
        Variables.TryGetValue(key, out var v) ? v : null;

    public bool GetFlag(string key) =>
        Flags.TryGetValue(key, out var f) && f;

    public void SetFlag(string key, bool value) =>
        Flags[key] = value;

    public Dictionary<string, string?> GetVariablesByGroup(string group, FlowDefinitionDocument flow) =>
        flow.Variables
            .Where(v => string.Equals(v.Group, group, StringComparison.OrdinalIgnoreCase) && !v.IsSystemManaged)
            .ToDictionary(v => v.Key, v => GetVariable(v.Key));
}

/// <summary>
/// A single message in the conversation history stored inside FlowExecutionState.
/// Role is "user" or "assistant".
/// </summary>
public class FlowConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class FlowTraceEntry
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string? Port { get; set; }
    public int TurnNumber { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

public class PreviousFlowSessionSnapshot
{
    public Dictionary<string, string?> PersistentVariables { get; set; } = new();
    public DateTime SessionEndedAt { get; set; }
}
