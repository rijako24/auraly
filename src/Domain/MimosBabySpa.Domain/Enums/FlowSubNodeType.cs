namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Slot types for sub-nodes attached to a cluster node (Agent or Router).
/// Each slot type defines a capability that the parent handler orchestrates internally.
/// </summary>
public enum FlowSubNodeType
{
    /// <summary>
    /// Variable extraction + intention detection via LLM.
    /// Max 1 per agent. Replaces the global Extract node for agent-scoped extraction.
    /// </summary>
    Extract = 0,

    /// <summary>
    /// An action step in the agent's pipeline (IFlowAction).
    /// 0..N per agent, executed in order.
    /// </summary>
    Action = 1,

    /// <summary>
    /// A knowledge source reference providing context to the agent's LLM calls.
    /// 0..N per agent.
    /// </summary>
    Knowledge = 2,

    /// <summary>
    /// Wait-for-event configuration with local intentions.
    /// Max 1 per agent.
    /// </summary>
    Event = 3,

    /// <summary>
    /// Intent classification for the Router node. Lightweight LLM call that only detects intentions.
    /// Max 1 per router.
    /// </summary>
    Classifier = 4
}
