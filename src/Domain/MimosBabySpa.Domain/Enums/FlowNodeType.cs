namespace MimosBabySpa.Domain.Enums;

public enum FlowNodeType
{
    Start = 0,
    CollectFields = 1,
    Action = 2,
    LLMClassify = 3,
    GenerateResponse = 4,
    WaitForEvent = 5,
    Escalate = 6,
    End = 7,

    /// <summary>
    /// Reserved for deserialization compatibility. No handler registered.
    /// Extraction is now handled inside Agent sub-nodes.
    /// </summary>
    Extract = 8,

    /// <summary>
    /// Three-phase routing: flags → pre-detected intents → LLM classification.
    /// Config: { "routes": [...], "defaultPort": "string", "classification": { "instructions": "..." } }
    /// </summary>
    IntentionRouter = 9,

    /// <summary>
    /// Cluster node with sub-nodes (Extract, Actions, Knowledge, Event).
    /// Agents are sticky — conversation stays on the active agent between turns.
    /// </summary>
    Agent = 10
}
