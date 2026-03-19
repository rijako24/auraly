using System.Text.Json;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Routes the flow based on boolean intention flags detected during extraction.
/// Zero LLM calls — reads ctx.DetectedIntentions, evaluates routes in order, and returns
/// the port of the first matching route. Falls back to defaultPort if none match.
///
/// Replaces LLMClassify for routing nodes (detect_intent, detect_confirmation).
///
/// Config:
///   routes: [{ "when": "intention_key", "port": "portId" }]  — evaluated top to bottom
///   defaultPort: string — port when no route matches (required)
/// </summary>
public class IntentionRouterNodeHandler : INodeHandler
{
    public FlowNodeType NodeType => FlowNodeType.IntentionRouter;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;
        var defaultPort = config.TryGetProperty("defaultPort", out var dp) ? dp.GetString() : null;

        if (config.TryGetProperty("routes", out var routesProp)
            && routesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var route in routesProp.EnumerateArray())
            {
                var when = route.TryGetProperty("when", out var w) ? w.GetString() : null;
                var port = route.TryGetProperty("port", out var p) ? p.GetString() : null;

                if (when != null && port != null && ctx.IsIntentionDetected(when))
                    return Task.FromResult(NodeExecutionResult.Advance(port));
            }
        }

        return Task.FromResult(NodeExecutionResult.Advance(defaultPort));
    }
}
