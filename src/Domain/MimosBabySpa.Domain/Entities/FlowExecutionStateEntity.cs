namespace MimosBabySpa.Domain.Entities;

public class FlowExecutionStateEntity
{
    public Guid FlowExecutionStateId { get; set; }
    public Guid BusinessId { get; set; }

    /// <summary>
    /// User's phone number or channel identifier — used as session key alongside BusinessId.
    /// </summary>
    public string UserIdentifier { get; set; } = string.Empty;

    public Guid AgentId { get; set; }
    public Guid FlowDefinitionId { get; set; }

    /// <summary>
    /// ID of the current node in the flow graph.
    /// </summary>
    public string CurrentNodeId { get; set; } = "start";

    /// <summary>
    /// True when the current node emitted WaitForUser on the last turn.
    /// </summary>
    public bool IsWaitingForUser { get; set; }

    public string VariablesJson { get; set; } = "{}";
    public string FlagsJson { get; set; } = "{}";
    public string ActionResultsJson { get; set; } = "{}";
    public string? TraceJson { get; set; }
    public string? PreviousSessionJson { get; set; }

    /// <summary>
    /// Serialized list of recent conversation messages (user + assistant pairs).
    /// Null when history is empty.
    /// </summary>
    public string? ConversationHistoryJson { get; set; }

    /// <summary>
    /// Counter of consecutive turns where no variables were extracted.
    /// Used for auto-escalation logic.
    /// </summary>
    public int ConsecutiveDegradedTurns { get; set; }

    /// <summary>
    /// Current conversation owner (Bot or Human).
    /// </summary>
    public string Owner { get; set; } = "Bot";

    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SessionStartedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;
    public virtual FlowDefinitionEntity FlowDefinition { get; set; } = null!;
}
