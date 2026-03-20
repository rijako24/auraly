using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow.Actions;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Cluster node: each Agent contains sub-nodes (Extract, Actions, Knowledge, Event)
/// defined via <see cref="FlowNode.SubNodes"/>. Runs scoped extraction, then executes
/// the action pipeline one eligible step per turn.
/// </summary>
public class AgentNodeHandler : INodeHandler
{
    private static readonly HashSet<string> ReservedStepKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "action_type", "input_mapping", "output_mapping",
        "onSuccessTemplate", "onFailureTemplate",
        "requiredVariables", "requiredFlags", "skipIfFlag", "onSuccessSetFlags"
    };

    private readonly IEnumerable<IFlowAction> _actions;
    private readonly TemplateResolver _templateResolver;
    private readonly ILLMAdapter _llm;
    private readonly FlowPromptBuilder _promptBuilder;
    private readonly IKnowledgeSourceRepository _ksRepo;
    private readonly FlowExtractionService _extractionService;
    private readonly ILogger<AgentNodeHandler> _logger;

    public FlowNodeType NodeType => FlowNodeType.Agent;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.ReExecute;

    public AgentNodeHandler(
        IEnumerable<IFlowAction> actions,
        TemplateResolver templateResolver,
        ILLMAdapter llm,
        FlowPromptBuilder promptBuilder,
        IKnowledgeSourceRepository ksRepo,
        FlowExtractionService extractionService,
        ILogger<AgentNodeHandler> logger)
    {
        _actions = actions;
        _templateResolver = templateResolver;
        _llm = llm;
        _promptBuilder = promptBuilder;
        _ksRepo = ksRepo;
        _extractionService = extractionService;
        _logger = logger;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node, FlowTurnContext ctx, CancellationToken ct)
    {
        var config = node.Config;
        var state = ctx.State;

        var activeVar = config.TryGetProperty("activeAgentVariable", out var aav) ? aav.GetString() ?? "_active_agent" : "_active_agent";
        var activeVal = config.TryGetProperty("activeAgentValue", out var aax) ? aax.GetString() ?? node.Id : node.Id;
        state.Variables[activeVar] = activeVal;

        var subNodes = node.SubNodes;

        // ── 1. EXTRACT: run agent-scoped extraction + escape intent detection ────
        if (subNodes?.Extract != null)
        {
            var escapeResult = await RunAgentExtractionAsync(node, subNodes.Extract, ctx, ct);
            if (escapeResult != null)
                return escapeResult;
        }

        // ── 2. KNOWLEDGE: load knowledge sources from sub-nodes ──────────────────
        if (subNodes?.Knowledge?.Count > 0)
        {
            var ksIds = new List<Guid>();
            foreach (var ksSub in subNodes.Knowledge)
            {
                if (ksSub.Config.TryGetProperty("knowledgeSourceId", out var kid)
                    && Guid.TryParse(kid.GetString(), out var id))
                    ksIds.Add(id);
            }
            if (ksIds.Count > 0)
            {
                var loadedSources = await _ksRepo.GetByIdsAsync(ksIds, ct);
                foreach (var ks in loadedSources)
                {
                    if (!ctx.KnowledgeSources.Any(existing => existing.KnowledgeSourceId == ks.KnowledgeSourceId))
                        ctx.KnowledgeSources.Add(ks);
                }
            }
        }

        // ── 3. EVENT: evaluate wait-for-event sub-node ───────────────────────────
        if (subNodes?.Event != null)
        {
            var waitResult = EvaluateWaitForEvent(subNodes.Event.Config, node, ctx);
            if (waitResult != null)
                return waitResult;
        }

        // ── 4. ACTIONS: execute pipeline from sub-nodes ──────────────────────────
        if (subNodes?.Actions?.Count > 0)
        {
            var steps = subNodes.Actions.Select(a => a.Config).ToList();
            return await RunPipelineStepsAsync(node, config, steps, ctx, ct);
        }

        // ── 5. No pipeline — conversational agent ───────────────────────────────
        _logger.LogDebug("Agent {NodeId}: no action sub-nodes — responding conversationally", node.Id);
        return await RespondAfterActionAsync(node, config, ctx, ct);
    }

    /// <summary>
    /// Runs agent-scoped extraction using the Extract sub-node config.
    /// Detects routing intents inherited from the flow and triggers ReRoute if needed.
    /// Returns null to continue processing, or a NodeExecutionResult to exit.
    /// </summary>
    private async Task<NodeExecutionResult?> RunAgentExtractionAsync(
        FlowNode node, FlowSubNode extractSubNode, FlowTurnContext ctx, CancellationToken ct)
    {
        var flow = ctx.FlowDefinition;
        var state = ctx.State;

        var extractConfig = extractSubNode.Config;
        var fields = new List<string>();
        if (extractConfig.TryGetProperty("fields", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fieldsProp.EnumerateArray())
            {
                if (f.GetString() is { } key) fields.Add(key);
            }
        }

        var routingIntents = flow.RoutingIntents;

        if (fields.Count == 0 && routingIntents.Count == 0)
            return null;

        var extractableVars = flow.Variables
            .Where(v => !v.IsSystemManaged && fields.Contains(v.Key))
            .ToList();

        var intentSchemas = routingIntents.Select(ri => new FlowIntentionSchema
        {
            Key = ri.Key,
            Description = ri.Description,
            Examples = ri.Examples,
            Priority = 50
        }).ToList();

        var localIntentions = GetLocalIntentionsFromEventSubNode(node);
        var allIntentions = intentSchemas.Concat(localIntentions).ToList();

        var extractionResult = await _extractionService.ExtractAsync(
            ctx.UserMessage,
            flow,
            state,
            state.ConversationHistory,
            ctx.KnowledgeSources,
            flow.EngineSettings,
            ct,
            extractableVars,
            allIntentions);

        if (extractionResult.WasSuccessful)
        {
            foreach (var kv in extractionResult.Intentions)
                ctx.DetectedIntentions[kv.Key] = kv.Value;

            _extractionService.ApplyExtraction(
                extractionResult.ExtractedFields, flow, state, ctx, ctx.KnowledgeSources);
        }

        if (!string.IsNullOrEmpty(node.HandlesIntent))
        {
            foreach (var ri in routingIntents)
            {
                if (ri.Key == node.HandlesIntent) continue;
                if (ctx.IsIntentionDetected(ri.Key))
                {
                    _logger.LogInformation("Agent {NodeId}: escape intent '{Intent}' detected — requesting ReRoute",
                        node.Id, ri.Key);
                    return NodeExecutionResult.ReRoute(ri.Key);
                }
            }
        }

        return null;
    }

    private static List<FlowIntentionSchema> GetLocalIntentionsFromEventSubNode(FlowNode node)
    {
        var eventSub = node.SubNodes?.Event;
        if (eventSub == null) return [];

        if (!eventSub.Config.TryGetProperty("localIntentions", out var liProp)
            || liProp.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<FlowIntentionSchema>();
        foreach (var li in liProp.EnumerateArray())
        {
            var key = li.TryGetProperty("key", out var kp) ? kp.GetString() : null;
            if (string.IsNullOrEmpty(key)) continue;
            var description = li.TryGetProperty("description", out var dp) ? dp.GetString() ?? "" : "";
            result.Add(new FlowIntentionSchema { Key = key, Description = description, Priority = 5 });
        }
        return result;
    }

    private NodeExecutionResult? EvaluateWaitForEvent(JsonElement wfe, FlowNode node, FlowTurnContext ctx)
    {
        var eventType = wfe.TryGetProperty("event_type", out var et) ? et.GetString() : null;
        if (string.IsNullOrEmpty(eventType)) return null;

        if (ctx.State.GetFlag(eventType))
            return NodeExecutionResult.Advance("received");

        if (wfe.TryGetProperty("localIntentions", out var liProp))
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
                    return NodeExecutionResult.Advance(targetPort);
            }
        }

        var waitingMsg = wfe.TryGetProperty("waitingMessage", out var wm)
            ? wm.GetString() ?? string.Empty
            : string.Empty;
        var resolved = _templateResolver.Resolve(waitingMsg, ctx);
        return NodeExecutionResult.WaitForUser(resolved);
    }

    private async Task<NodeExecutionResult> RunPipelineStepsAsync(
        FlowNode node, JsonElement config, List<JsonElement> steps,
        FlowTurnContext ctx, CancellationToken ct)
    {
        var state = ctx.State;
        var flow = ctx.FlowDefinition;
        FlowActionResult? actionResult = null;
        int? executedIndex = null;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.ValueKind != JsonValueKind.Object) continue;

            var stepDoneFlag = $"__agentStep:{node.Id}:{i}";
            if (state.GetFlag(stepDoneFlag))
                continue;

            if (step.TryGetProperty("skipIfFlag", out var sif) && sif.GetString() is { } sf && state.GetFlag(sf))
                continue;

            if (!StepRequirementsMet(step, state, out _))
            {
                _logger.LogDebug("Agent {NodeId}: step {Index} blocked by missing data — collect", node.Id, i);
                return await CollectMissingForPipelineAsync(node, config, steps, flow, ctx, ct);
            }

            if (!step.TryGetProperty("action_type", out var atEl) || atEl.GetString() is not { } actionType)
            {
                _logger.LogError("Agent {NodeId} step {Index}: missing action_type", node.Id, i);
                return NodeExecutionResult.Error("Agent step missing action_type");
            }

            var action = _actions.FirstOrDefault(a =>
                string.Equals(a.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
            if (action == null)
            {
                _logger.LogError("Agent {NodeId}: unknown action '{ActionType}'", node.Id, actionType);
                return NodeExecutionResult.Error($"Unknown action: {actionType}");
            }

            var inputs = BuildStepInputs(step, ctx);
            _logger.LogDebug("Agent {NodeId}: executing step {Index} action {Action}", node.Id, i, actionType);

            try
            {
                actionResult = await action.ExecuteAsync(inputs, ctx, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent step action threw — failure port");
                return NodeExecutionResult.Advance("failure");
            }

            var resultKey = $"{node.Id}::step{i}";
            state.ActionResults[resultKey] = actionResult.Data.ToDictionary(kv => kv.Key, kv => kv.Value);

            if (actionResult.Success)
                ApplyStepOutputMapping(step, actionResult, state);

            ApplyOnSuccessSetFlags(step, state);

            state.SetFlag(stepDoneFlag, true);
            executedIndex = i;

            var actionPort = actionResult.OutputPort ?? (actionResult.Success ? "success" : "failure");

            if (GetStepResponseTemplate(step, actionResult.Success) is { } tmpl && !string.IsNullOrEmpty(tmpl))
            {
                var resolved = _templateResolver.Resolve(tmpl, ctx);
                return NodeExecutionResult.WaitForUser(resolved);
            }

            if (!actionResult.Success)
                return NodeExecutionResult.Advance(actionPort);

            break;
        }

        if (executedIndex.HasValue)
            return await RespondAfterActionAsync(node, config, ctx, ct);

        if (AllPipelineStepsComplete(node, steps, state))
        {
            var completionBehavior = config.TryGetProperty("completionBehavior", out var cb)
                ? cb.GetString() ?? "advance"
                : "advance";

            if (string.Equals(completionBehavior, "respond", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Agent {NodeId}: pipeline complete — staying as conversational agent", node.Id);
                return await RespondAfterActionAsync(node, config, ctx, ct);
            }

            ClearActiveAgent(state, config.TryGetProperty("activeAgentVariable", out var aav2) ? aav2.GetString() ?? "_active_agent" : "_active_agent");
            var completionPort = config.TryGetProperty("completionPort", out var cpo) ? cpo.GetString() ?? "completed" : "completed";
            _logger.LogInformation("Agent {NodeId}: pipeline complete — port {Port}", node.Id, completionPort);
            return NodeExecutionResult.Advance(completionPort);
        }

        return await CollectMissingForPipelineAsync(node, config, steps, flow, ctx, ct);
    }

    private static void ClearActiveAgent(FlowExecutionState state, string activeVar) =>
        state.Variables.Remove(activeVar);

    private static bool StepRequirementsMet(JsonElement step, FlowExecutionState state, out string? missing)
    {
        missing = null;
        if (step.TryGetProperty("requiredVariables", out var rv) && rv.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rv.EnumerateArray())
            {
                var key = item.GetString();
                if (string.IsNullOrEmpty(key)) continue;
                if (state.GetVariable(key) == null)
                {
                    missing = key;
                    return false;
                }
            }
        }

        if (step.TryGetProperty("requiredFlags", out var rf) && rf.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rf.EnumerateArray())
            {
                var key = item.GetString();
                if (string.IsNullOrEmpty(key)) continue;
                if (!state.GetFlag(key))
                {
                    missing = key;
                    return false;
                }
            }
        }

        return true;
    }

    private static bool AllPipelineStepsComplete(FlowNode node, List<JsonElement> steps, FlowExecutionState state)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.ValueKind != JsonValueKind.Object) continue;

            if (step.TryGetProperty("skipIfFlag", out var sif) && sif.GetString() is { } sf && state.GetFlag(sf))
                continue;

            var stepDoneFlag = $"__agentStep:{node.Id}:{i}";
            if (!state.GetFlag(stepDoneFlag))
                return false;
        }

        return true;
    }

    private Dictionary<string, object?> BuildStepInputs(JsonElement step, FlowTurnContext ctx)
    {
        var inputs = new Dictionary<string, object?>();

        if (step.TryGetProperty("input_mapping", out var mappingProp))
        {
            foreach (var prop in mappingProp.EnumerateObject())
            {
                var rawValue = prop.Value.GetString() ?? string.Empty;
                inputs[prop.Name] = _templateResolver.Resolve(rawValue, ctx);
            }
        }

        foreach (var prop in step.EnumerateObject())
        {
            if (ReservedStepKeys.Contains(prop.Name) || inputs.ContainsKey(prop.Name))
                continue;
            inputs[prop.Name] = prop.Value.GetRawText();
        }

        return inputs;
    }

    private static void ApplyStepOutputMapping(JsonElement step, FlowActionResult result, FlowExecutionState state)
    {
        if (!step.TryGetProperty("output_mapping", out var mappingProp)) return;

        foreach (var prop in mappingProp.EnumerateObject())
        {
            var stateKey = prop.Name;
            var dataKey = prop.Value.GetString() ?? string.Empty;
            if (!result.Data.TryGetValue(dataKey, out var value) || value is null) continue;

            var strValue = value.ToString();
            if (stateKey.StartsWith("flag:", StringComparison.OrdinalIgnoreCase))
            {
                var flagKey = stateKey["flag:".Length..];
                if (bool.TryParse(strValue, out var boolVal))
                    state.SetFlag(flagKey, boolVal);
            }
            else
                state.Variables[stateKey] = strValue;
        }
    }

    private static void ApplyOnSuccessSetFlags(JsonElement step, FlowExecutionState state)
    {
        if (!step.TryGetProperty("onSuccessSetFlags", out var f) || f.ValueKind != JsonValueKind.Object)
            return;
        foreach (var prop in f.EnumerateObject())
        {
            if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                state.SetFlag(prop.Name, prop.Value.GetBoolean());
        }
    }

    private static string? GetStepResponseTemplate(JsonElement step, bool success)
    {
        var key = success ? "onSuccessTemplate" : "onFailureTemplate";
        return step.TryGetProperty(key, out var p) ? p.GetString() : null;
    }

    private async Task<NodeExecutionResult> RespondAfterActionAsync(
        FlowNode node, JsonElement config, FlowTurnContext ctx, CancellationToken ct)
    {
        var mode = config.TryGetProperty("responseMode", out var m) ? m.GetString() : "llm";
        var waitForUser = !config.TryGetProperty("waitForUser", out var wf) || wf.GetBoolean();

        if (mode == "template")
        {
            var template = config.TryGetProperty("instructions", out var inst)
                ? inst.GetString() ?? string.Empty
                : string.Empty;
            var templateReply = _templateResolver.Resolve(template, ctx);
            if (!waitForUser)
                ctx.PendingBotResponse = templateReply;
            return waitForUser
                ? NodeExecutionResult.WaitForUser(templateReply)
                : NodeExecutionResult.Advance();
        }

        var nodeSources = await LoadKnowledgeSourcesFromConfig(config, ct);
        var systemPrompt = _promptBuilder.Build(ctx.Agent, node, ctx, nodeSources);
        var messages = new List<LLMMessage> { new() { Role = LLMRole.System, Content = systemPrompt } };
        foreach (var (role, content) in ctx.ConversationHistory)
        {
            if (Enum.TryParse<LLMRole>(role, ignoreCase: true, out var llmRole))
                messages.Add(new() { Role = llmRole, Content = content });
        }

        messages.Add(new() { Role = LLMRole.User, Content = ctx.UserMessage });

        var request = new LLMRequest
        {
            Temperature = ctx.FlowDefinition.EngineSettings.ResponseTemperature,
            MaxTokens = ctx.FlowDefinition.EngineSettings.ResponseMaxTokens,
            Messages = messages
        };

        var llmResponse = await _llm.SendMessageAsync(request, ct);
        var llmReply = !llmResponse.Success
            ? "Disculpa, ocurrió un error. ¿Puedes repetirlo?"
            : llmResponse.Content;

        if (!waitForUser)
            ctx.PendingBotResponse = llmReply;

        return waitForUser
            ? NodeExecutionResult.WaitForUser(llmReply)
            : NodeExecutionResult.Advance();
    }

    private async Task<NodeExecutionResult> CollectMissingForPipelineAsync(
        FlowNode node,
        JsonElement config,
        List<JsonElement> steps,
        FlowDefinitionDocument flow,
        FlowTurnContext ctx,
        CancellationToken ct)
    {
        var neededKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.ValueKind != JsonValueKind.Object) continue;
            var stepDoneFlag = $"__agentStep:{node.Id}:{i}";
            if (ctx.State.GetFlag(stepDoneFlag)) continue;
            if (step.TryGetProperty("skipIfFlag", out var sif) && sif.GetString() is { } sf && ctx.State.GetFlag(sf))
                continue;

            if (!step.TryGetProperty("requiredVariables", out var rv) || rv.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in rv.EnumerateArray())
            {
                var k = item.GetString();
                if (!string.IsNullOrEmpty(k) && ctx.State.GetVariable(k) == null)
                    neededKeys.Add(k);
            }
        }

        if (neededKeys.Count == 0)
        {
            var fallback = config.TryGetProperty("instructions", out var ins)
                ? _templateResolver.Resolve(ins.GetString() ?? string.Empty, ctx)
                : "¿En qué puedo ayudarte?";
            return NodeExecutionResult.WaitForUser(string.IsNullOrWhiteSpace(fallback) ? "¿En qué puedo ayudarte?" : fallback);
        }

        var missingFields = neededKeys
            .Select(k => flow.Variables.FirstOrDefault(v => string.Equals(v.Key, k, StringComparison.OrdinalIgnoreCase)))
            .Where(v => v != null)
            .Cast<FlowVariable>()
            .OrderBy(v => v.DisplayOrder)
            .ToList();

        if (missingFields.Count == 0)
        {
            return NodeExecutionResult.WaitForUser(
                "Necesito un poco más de información para continuar. ¿Puedes detallar tu solicitud?");
        }

        JsonElement collectConfig = config;
        if (config.TryGetProperty("collect", out var coll) && coll.ValueKind == JsonValueKind.Object)
            collectConfig = coll;

        var nodeSources = await LoadKnowledgeSourcesFromCollectConfig(collectConfig, ct);
        var systemPrompt = _promptBuilder.Build(ctx.Agent, node, ctx, nodeSources);
        var userPrompt = BuildCollectUserPrompt(missingFields, ctx);

        var request = new LLMRequest
        {
            Temperature = ctx.FlowDefinition.EngineSettings.ResponseTemperature,
            MaxTokens = ctx.FlowDefinition.EngineSettings.ResponseMaxTokens,
            Messages = BuildCollectMessages(ctx, systemPrompt, userPrompt)
        };

        var response = await _llm.SendMessageAsync(request, ct);
        if (!response.Success)
            return NodeExecutionResult.WaitForUser("Disculpa, ocurrió un error. ¿Puedes repetirlo?");

        return NodeExecutionResult.WaitForUser(response.Content);
    }

    private static string BuildCollectUserPrompt(List<FlowVariable> missingFields, FlowTurnContext ctx)
    {
        var sb = new StringBuilder();
        if (ctx.ValidationErrors.Count > 0)
        {
            sb.AppendLine("Errores de validación del turno anterior:");
            foreach (var err in ctx.ValidationErrors)
                sb.AppendLine($"- {err.VariableKey}: \"{err.ProvidedValue}\" — {err.ErrorMessage}");
            sb.AppendLine();
        }

        sb.AppendLine($"Faltan los siguientes campos: {string.Join(", ", missingFields.Select(f => f.Label))}.");
        sb.Append("Pide la información de forma conversacional.");
        return sb.ToString();
    }

    private static List<LLMMessage> BuildCollectMessages(
        FlowTurnContext ctx, string systemPrompt, string userPrompt)
    {
        var messages = new List<LLMMessage> { new() { Role = LLMRole.System, Content = systemPrompt } };
        foreach (var (role, content) in ctx.ConversationHistory)
        {
            if (Enum.TryParse<LLMRole>(role, ignoreCase: true, out var llmRole))
                messages.Add(new() { Role = llmRole, Content = content });
        }

        messages.Add(new() { Role = LLMRole.User, Content = ctx.UserMessage });
        messages.Add(new() { Role = LLMRole.System, Content = userPrompt });
        return messages;
    }

    private async Task<IEnumerable<KnowledgeSource>> LoadKnowledgeSourcesFromConfig(
        JsonElement config, CancellationToken ct)
    {
        if (!config.TryGetProperty("knowledgeSourceIds", out var idsProp)) return [];
        return await LoadKsByIds(idsProp, ct);
    }

    private async Task<IEnumerable<KnowledgeSource>> LoadKnowledgeSourcesFromCollectConfig(
        JsonElement collectConfig, CancellationToken ct)
    {
        if (!collectConfig.TryGetProperty("knowledgeSourceIds", out var idsProp)) return [];
        return await LoadKsByIds(idsProp, ct);
    }

    private async Task<IEnumerable<KnowledgeSource>> LoadKsByIds(JsonElement idsProp, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var item in idsProp.EnumerateArray())
        {
            if (Guid.TryParse(item.GetString(), out var id))
                ids.Add(id);
        }

        return ids.Count == 0 ? [] : await _ksRepo.GetByIdsAsync(ids, ct);
    }
}
