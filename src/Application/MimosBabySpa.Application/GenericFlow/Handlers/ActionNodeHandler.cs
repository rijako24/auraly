using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow.Actions;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Executes a registered IFlowAction and maps inputs/outputs using template resolution.
/// Config:
///   action_type: string              — key to resolve the IFlowAction
///   input_mapping: { key: template } — inputs resolved from state
///   output_mapping: { stateKey: resultKey | "flag:KEY": resultKey } — map action data to state.
///   Null action values are skipped (existing state is not cleared). Use setVariables on a node to clear explicitly.
///   onSuccessTemplate: string?       — optional direct response on success
///   onFailureTemplate: string?       — optional direct response on failure
/// </summary>
public class ActionNodeHandler : INodeHandler
{
    private readonly IEnumerable<IFlowAction> _actions;
    private readonly TemplateResolver _templateResolver;
    private readonly ILogger<ActionNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.Action;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public ActionNodeHandler(
        IEnumerable<IFlowAction> actions,
        TemplateResolver templateResolver,
        ILogger<ActionNodeHandler> logger)
    {
        _actions = actions;
        _templateResolver = templateResolver;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;

        if (!config.TryGetProperty("action_type", out var atProp) || atProp.GetString() is not { } actionType)
        {
            _logger.LogError("ActionNodeHandler: node {NodeId} missing action_type", node.Id);
            return NodeExecutionResult.Error("Missing action_type in node config");
        }

        var action = _actions.FirstOrDefault(a =>
            string.Equals(a.ActionType, actionType, StringComparison.OrdinalIgnoreCase));

        if (action == null)
        {
            _logger.LogError("ActionNodeHandler: action '{ActionType}' not registered", actionType);
            return NodeExecutionResult.Error($"Unknown action type: {actionType}");
        }

        var inputs = BuildInputs(config, ctx);
        _logger.LogDebug("Executing action '{ActionType}' on node {NodeId}", actionType, node.Id);

        FlowActionResult result;
        try
        {
            result = await action.ExecuteAsync(inputs, ctx, ct);
        }
        catch (Exception ex)
        {
            // Follow the graph failure port (typically → escalate) instead of stopping the turn with Error,
            // which left the user stuck without human handoff.
            _logger.LogError(ex, "Action '{ActionType}' threw exception — advancing failure port", actionType);
            return NodeExecutionResult.Advance("failure");
        }

        // Store action results in state for template access
        ctx.State.ActionResults[node.Id] = result.Data
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Apply output_mapping
        if (result.Success)
            ApplyOutputMapping(config, result, ctx.State);

        // Apply setFlags/setVariables from node config (done in orchestrator, but actions can also set via mapping)
        // Determine output port
        var port = result.OutputPort ?? (result.Success ? "success" : "failure");

        _logger.LogDebug("Action '{ActionType}' completed: port={Port}", actionType, port);

        // Optional response template on success/failure
        if (GetResponseTemplate(config, result.Success) is { } template && !string.IsNullOrEmpty(template))
        {
            var resolved = _templateResolver.Resolve(template, ctx);
            return NodeExecutionResult.WaitForUser(resolved);
        }

        return NodeExecutionResult.Advance(port);
    }

    // Properties consumed by ActionNodeHandler itself — not forwarded to the action.
    private static readonly HashSet<string> _reservedConfigKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "action_type", "input_mapping", "output_mapping",
        "onSuccessTemplate", "onFailureTemplate"
    };

    private Dictionary<string, object?> BuildInputs(JsonElement config, FlowTurnContext ctx)
    {
        var inputs = new Dictionary<string, object?>();

        // 1. Resolved template values from input_mapping
        if (config.TryGetProperty("input_mapping", out var mappingProp))
        {
            foreach (var prop in mappingProp.EnumerateObject())
            {
                var rawValue = prop.Value.GetString() ?? string.Empty;
                var resolved = _templateResolver.Resolve(rawValue, ctx);
                inputs[prop.Name] = resolved;
            }
        }

        // 2. Extra top-level config blocks (e.g. "scheduling", "payment", "contacts") are
        //    forwarded as raw JSON strings so actions can deserialize what they need.
        //    This allows node-level configuration without changing the IFlowAction interface.
        foreach (var prop in config.EnumerateObject())
        {
            if (_reservedConfigKeys.Contains(prop.Name) || inputs.ContainsKey(prop.Name))
                continue;

            inputs[prop.Name] = prop.Value.GetRawText();
        }

        return inputs;
    }

    private static void ApplyOutputMapping(
        JsonElement config, FlowActionResult result, FlowExecutionState state)
    {
        if (!config.TryGetProperty("output_mapping", out var mappingProp)) return;

        foreach (var prop in mappingProp.EnumerateObject())
        {
            var stateKey = prop.Name;
            var dataKey = prop.Value.GetString() ?? string.Empty;

            if (!result.Data.TryGetValue(dataKey, out var value)) continue;
            // Do not write nulls: e.g. confirmed_time=null must not erase desired_time extracted earlier.
            if (value is null) continue;

            var strValue = value.ToString();

            if (stateKey.StartsWith("flag:", StringComparison.OrdinalIgnoreCase))
            {
                var flagKey = stateKey["flag:".Length..];
                if (bool.TryParse(strValue, out var boolVal))
                    state.SetFlag(flagKey, boolVal);
            }
            else
            {
                state.Variables[stateKey] = strValue;
            }
        }
    }

    private static string? GetResponseTemplate(JsonElement config, bool success)
    {
        var key = success ? "onSuccessTemplate" : "onFailureTemplate";
        return config.TryGetProperty(key, out var p) ? p.GetString() : null;
    }
}
