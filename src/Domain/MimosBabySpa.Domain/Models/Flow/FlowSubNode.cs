using System.Text.Json;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Models.Flow;

/// <summary>
/// A sub-node attached to a cluster node (Agent or Router).
/// Sub-nodes are NOT traversed by the main graph engine — they are orchestrated
/// internally by their parent node's handler, similar to n8n's cluster node pattern.
/// </summary>
public class FlowSubNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public FlowSubNodeType Slot { get; set; }

    /// <summary>
    /// Sub-node configuration. Interpreted by the parent handler based on <see cref="Slot"/>.
    /// </summary>
    public JsonElement Config { get; set; }
}

/// <summary>
/// Collection of typed sub-node slots for a cluster node.
/// Each slot has cardinality constraints enforced by the catalog.
/// </summary>
public class FlowSubNodeSet
{
    /// <summary>
    /// Variable extraction + routing intent detection. Max 1.
    /// Replaces the global Extract node — each agent extracts only its own fields.
    /// </summary>
    public FlowSubNode? Extract { get; set; }

    /// <summary>
    /// Ordered pipeline of actions (IFlowAction). 0..N.
    /// Replaces <c>config.actionPipeline</c> with real, individually configurable sub-nodes.
    /// </summary>
    public List<FlowSubNode>? Actions { get; set; }

    /// <summary>
    /// Knowledge sources providing context to the agent's LLM calls. 0..N.
    /// </summary>
    public List<FlowSubNode>? Knowledge { get; set; }

    /// <summary>
    /// Wait-for-event with local intentions. Max 1.
    /// </summary>
    public FlowSubNode? Event { get; set; }

    /// <summary>
    /// Intent classifier for Router nodes. Max 1.
    /// Runs a lightweight LLM call to classify user intent for routing.
    /// </summary>
    public FlowSubNode? Classifier { get; set; }

    /// <summary>
    /// Returns all sub-nodes across all slots as a flat list.
    /// </summary>
    public IEnumerable<FlowSubNode> All()
    {
        if (Extract != null) yield return Extract;
        if (Classifier != null) yield return Classifier;
        if (Actions != null)
            foreach (var a in Actions) yield return a;
        if (Knowledge != null)
            foreach (var k in Knowledge) yield return k;
        if (Event != null) yield return Event;
    }
}
