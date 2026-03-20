using System.Text.Json;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Models.Flow;

public class FlowNode
{
    public string Id { get; set; } = string.Empty;
    public FlowNodeType Type { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Raw configuration. Deserialized by each handler into its own typed config.
    /// </summary>
    public JsonElement Config { get; set; }

    /// <summary>
    /// Sub-nodes for cluster nodes (Agent, Router). Null for simple nodes.
    /// Sub-nodes are orchestrated internally by the parent handler — they do NOT
    /// participate in the main graph traversal.
    /// </summary>
    public FlowSubNodeSet? SubNodes { get; set; }

    /// <summary>
    /// The routing intent that brings users TO this agent node.
    /// Used by the agent to detect when the user's intent no longer matches
    /// and a re-route is needed. Null for non-agent nodes.
    /// </summary>
    public string? HandlesIntent { get; set; }
}

public class FlowEdge
{
    public string Id { get; set; } = string.Empty;
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Output port from the source node. Null means the default/unconditional edge.
    /// </summary>
    public string? PortId { get; set; }
}
