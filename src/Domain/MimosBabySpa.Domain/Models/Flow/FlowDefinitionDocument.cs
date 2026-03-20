namespace MimosBabySpa.Domain.Models.Flow;

public class FlowDefinitionDocument
{
    public List<FlowVariable> Variables { get; set; } = new();

    /// <summary>
    /// Unified intention schema. Detected as boolean flags inside the extraction LLM call
    /// (no separate call needed). Replaces the old Intentions list.
    /// </summary>
    public List<FlowIntentionSchema> IntentionSchema { get; set; } = new();

    /// <summary>
    /// Flow-level routing intents. Inherited by all agent nodes for escape-intent detection
    /// and used by the Router for initial classification. Replaces the routing aspects of
    /// <see cref="IntentionSchema"/> in the new cluster-node architecture.
    /// </summary>
    public List<FlowRoutingIntent> RoutingIntents { get; set; } = new();

    public FlowSessionConfig SessionConfig { get; set; } = new();
    public FlowEngineSettings EngineSettings { get; set; } = new();
    public List<FlowNode> Nodes { get; set; } = new();
    public List<FlowEdge> Edges { get; set; } = new();

    /// <summary>
    /// Optional free-form instructions injected into the extraction LLM prompt.
    /// Supports all template tokens ({{runtime.today}}, {{runtime.tomorrow}}, etc.).
    /// The engine does not interpret this content — it resolves tokens and passes it to the LLM.
    /// Use this to provide date resolution rules, contextual resolution hints, catalog usage
    /// instructions, or any other extraction guidance specific to this flow.
    /// </summary>
    public string? ExtractionInstructions { get; set; }

    public FlowNode? GetNode(string id) =>
        Nodes.FirstOrDefault(n => n.Id == id);

    public List<FlowEdge> GetEdgesFrom(string nodeId, string? portId = null) =>
        portId == null
            ? Edges.Where(e => e.SourceNodeId == nodeId).ToList()
            : Edges.Where(e => e.SourceNodeId == nodeId && e.PortId == portId).ToList();

    public FlowEdge? GetEdge(string sourceNodeId, string? portId) =>
        Edges.FirstOrDefault(e => e.SourceNodeId == sourceNodeId && e.PortId == portId)
        ?? Edges.FirstOrDefault(e => e.SourceNodeId == sourceNodeId && e.PortId == null);

    /// <summary>
    /// Returns a rough topological order index for a node (BFS from start).
    /// Used to determine which gotoNode wins when multiple onChange rules fire.
    /// </summary>
    public int GetTopologicalOrder(string nodeId)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<(string id, int order)>();
        queue.Enqueue(("start", 0));

        while (queue.Count > 0)
        {
            var (current, order) = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == nodeId) return order;

            foreach (var edge in Edges.Where(e => e.SourceNodeId == current))
                queue.Enqueue((edge.TargetNodeId, order + 1));
        }

        return int.MaxValue;
    }
}
