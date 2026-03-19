using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Waits for an external event (e.g., payment confirmed) or user confirmation.
/// On re-entry, checks if the event has been received via flags.
/// Also evaluates local intentions for node-specific user responses.
///
/// Config:
///   event_type: string          — flag name to check (e.g., "payment_confirmed")
///   waitingMessage: string      — message to show while waiting (supports templates)
///   localIntentions: array      — node-scoped intentions with advance_port behavior
/// </summary>
public class WaitForEventNodeHandler : INodeHandler
{
    private readonly TemplateResolver _templateResolver;
    private readonly ILogger<WaitForEventNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.WaitForEvent;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public WaitForEventNodeHandler(
        TemplateResolver templateResolver,
        ILogger<WaitForEventNodeHandler> logger)
    {
        _templateResolver = templateResolver;
        _logger = logger;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;
        var eventType = config.TryGetProperty("event_type", out var et) ? et.GetString() : null;

        // Check if external event was already received
        if (!string.IsNullOrEmpty(eventType) && ctx.State.GetFlag(eventType))
        {
            _logger.LogDebug("WaitForEvent {NodeId}: event '{Event}' received — advancing", node.Id, eventType);
            return Task.FromResult(NodeExecutionResult.Advance("received"));
        }

        // Evaluate local intentions (node-scoped, higher priority than waiting)
        if (config.TryGetProperty("localIntentions", out var liProp))
        {
            foreach (var li in liProp.EnumerateArray())
            {
                var key = li.TryGetProperty("key", out var kp) ? kp.GetString() : null;
                if (key == null || !ctx.IsIntentionDetected(key)) continue;

                var behavior = li.TryGetProperty("behavior", out var bp) ? bp : default;
                if (behavior.ValueKind == JsonValueKind.Undefined) continue;

                var action = behavior.TryGetProperty("action", out var ap) ? ap.GetString() : null;
                var targetPort = behavior.TryGetProperty("targetPort", out var tp) ? tp.GetString() : null;

                if (action == "advance_port" && !string.IsNullOrEmpty(targetPort))
                {
                    _logger.LogDebug("WaitForEvent {NodeId}: local intention '{Key}' → port '{Port}'",
                        node.Id, key, targetPort);
                    return Task.FromResult(NodeExecutionResult.Advance(targetPort));
                }
            }
        }

        // Still waiting — send waiting message
        var waitingMsg = config.TryGetProperty("waitingMessage", out var wm)
            ? wm.GetString() ?? string.Empty
            : string.Empty;

        var resolved = _templateResolver.Resolve(waitingMsg, ctx);
        _logger.LogDebug("WaitForEvent {NodeId}: still waiting", node.Id);
        return Task.FromResult(NodeExecutionResult.WaitForUser(resolved));
    }
}
