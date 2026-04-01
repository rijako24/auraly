using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow.Handlers;
using MimosBabySpa.Application.GenericFlow.Services;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Models.Flow;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.GenericFlow;

/// <summary>
/// Main entry point for the generic flow engine.
/// Processes one message turn through the agent's configured flow graph.
///
/// Turn pipeline:
///  1. Load agent
///  2. Load flow definition
///  3. Load or create flow execution state
///  4. Skip if flow session is owned by human
///  5. Load knowledge sources (dynamic catalog when applicable)
///  6. Build FlowTurnContext (ConversationId supplied by caller)
///  7–8. Unified extraction + intention handling
///  9. Failed-extraction streak / auto-escalation
///  10. Traverse graph until WaitForUser or End
///  11–12. Append history, save state, return result
/// </summary>
public class FlowOrchestrationService : IFlowOrchestrationService
{
    private readonly IAgentRepository _agentRepo;
    private readonly IFlowDefinitionRepository _flowDefRepo;
    private readonly IKnowledgeSourceRepository _ksRepo;
    private readonly FlowStateManager _stateManager;
    private readonly FlowIntentionDetector _intentionDetector;
    private readonly FlowExtractionService _extractionService;
    private readonly ICatalogContentGenerator _catalogGenerator;
    private readonly IEnumerable<INodeHandler> _handlers;
    private readonly ILogger<FlowOrchestrationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FlowOrchestrationService(
        IAgentRepository agentRepo,
        IFlowDefinitionRepository flowDefRepo,
        IKnowledgeSourceRepository ksRepo,
        FlowStateManager stateManager,
        FlowIntentionDetector intentionDetector,
        FlowExtractionService extractionService,
        ICatalogContentGenerator catalogGenerator,
        IEnumerable<INodeHandler> handlers,
        ILogger<FlowOrchestrationService> logger)
    {
        _agentRepo = agentRepo;
        _flowDefRepo = flowDefRepo;
        _ksRepo = ksRepo;
        _stateManager = stateManager;
        _intentionDetector = intentionDetector;
        _extractionService = extractionService;
        _catalogGenerator = catalogGenerator;
        _handlers = handlers;
        _logger = logger;
    }

    public async Task<FlowOrchestratorResult> ProcessTurnAsync(
        Guid conversationId,
        Guid agentId,
        string userIdentifier,
        string userMessage,
        CancellationToken ct = default)
    {
        // ── 1. Load agent (already resolved upstream from BusinessWhatsAppNumbers.AgentId) ──
        var agent = await _agentRepo.GetByIdAsync(agentId, ct);
        if (agent == null)
        {
            _logger.LogWarning("Agent {AgentId} not found for user {Identifier}", agentId, userIdentifier);
            return FlowOrchestratorResult.Err("Agent not found", "No pudimos procesar tu mensaje. Intenta más tarde.");
        }

        var businessId = agent.BusinessId;

        // ── 2. Load flow definition ────────────────────────────────────────────────
        var flowDefEntity = await _flowDefRepo.GetActiveByAgentAsync(agent.AgentId, ct);
        if (flowDefEntity == null)
        {
            _logger.LogError("No active flow definition for agent {AgentId}", agent.AgentId);
            return FlowOrchestratorResult.Err("Flow not found", "El servicio no está disponible en este momento.");
        }

        FlowDefinitionDocument flow;
        try
        {
            flow = JsonSerializer.Deserialize<FlowDefinitionDocument>(
                flowDefEntity.DefinitionJson, JsonOpts)
                ?? throw new InvalidOperationException("Null flow deserialization");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize flow definition {FlowId}", flowDefEntity.FlowDefinitionId);
            return FlowOrchestratorResult.Err("Invalid flow", "El servicio no está disponible.");
        }

        // ── 3. Load state ──────────────────────────────────────────────────────────
        var (state, flowDefinitionId) = await _stateManager.LoadOrCreateAsync(
            userIdentifier, agent, flow, ct);

        // ── 4. Skip if human-owned ─────────────────────────────────────────────────
        if (state.Owner == ConversationOwner.Human)
        {
            _logger.LogDebug("Conversation {Identifier} owned by human — skipping", userIdentifier);
            return FlowOrchestratorResult.Ok(string.Empty);
        }

        // ── 5. Load knowledge sources ──────────────────────────────────────────────
        var allSources = agent.KnowledgeSources
            .Select(aks => aks.KnowledgeSource)
            .Where(ks => ks.IsActive)
            .ToList();

        // Override ServiceCatalog KS with dynamically generated content from DB tables.
        // This ensures the LLM always sees up-to-date service names, prices and add-ons
        // without requiring manual SQL updates to the KnowledgeSource text.
        var catalogSources = allSources.Where(ks => ks.Type == KnowledgeSourceType.ServiceCatalog).ToList();
        if (catalogSources.Count > 0)
        {
            var dynamicCatalog = await _catalogGenerator.GenerateAsync(businessId, ct);
            if (!string.IsNullOrWhiteSpace(dynamicCatalog))
                foreach (var ks in catalogSources)
                    ks.Content = dynamicCatalog;
        }

        // ── 6. Build context (ConversationId from caller — same as legacy orchestrator) ─
        var ctx = new FlowTurnContext
        {
            State = state,
            FlowDefinition = flow,
            Agent = agent,
            BusinessId = businessId,
            ConversationId = conversationId,
            UserIdentifier = userIdentifier,
            UserMessage = userMessage,
            TurnNumber = state.Trace.Select(t => t.TurnNumber).DefaultIfEmpty(0).Max() + 1,
            KnowledgeSources = allSources,
            ConversationHistory = state.ConversationHistory
                .Select(m => (m.Role, m.Content))
                .ToList()
        };

        // ── 7+8. Unified extraction: extracts variables AND detects all intention flags ──
        // IsWaitingForUser is NOT cleared here. TraverseGraphAsync reads it to determine
        // whether this turn is a re-entry to a waiting node. Any redirect that changes
        // CurrentNodeId (goto_node, onChange.gotoNode) already clears IsWaitingForUser
        // itself, so no pre-clearing is needed here.

        var extractionResult = await _extractionService.ExtractAsync(
            userMessage,
            flow,
            state,
            state.ConversationHistory,
            allSources,
            flow.EngineSettings,
            ct);

        if (extractionResult.WasSuccessful)
        {
            ctx.DetectedIntentions = extractionResult.Intentions;
        }
        else
        {
            // Extraction LLM failed → degraded regex fallback for critical intentions only
            _logger.LogWarning("Extraction failed — running degraded regex fallback for critical intentions");
            ctx.DetectedIntentions = _intentionDetector.DegradedDetect(userMessage, flow);
        }

        // Log extraction + intentions for easier error routing and debugging
        var activeIntentions = ctx.DetectedIntentions
            .Where(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
        var extractedSummary = extractionResult.ExtractedFields
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();
        _logger.LogInformation(
            "[Flow] Extracted=[{Fields}] | Intentions=[{Intentions}]",
            string.Join(", ", extractedSummary),
            string.Join(", ", activeIntentions.Count > 0 ? activeIntentions : new[] { "(none)" }));

        // Process intention behaviors (escalate, goto_node) before graph traversal
        var intentionRedirect = ProcessGlobalIntentionRedirects(ctx, flow);
        if (intentionRedirect != null)
        {
            await _stateManager.SaveAsync(userIdentifier, agent, flowDefinitionId, state, ct);
            return intentionRedirect;
        }

        _extractionService.ApplyExtraction(extractionResult.ExtractedFields, flow, state, ctx, allSources);

        // ── 9. Track failed extraction turns and auto-escalate if threshold reached ─
        UpdateDegradedTurns(state, extractionResult.WasSuccessful);

        var degradedEscalation = TryEscalateForDegradedTurns(state, flow, agent, ctx);
        if (degradedEscalation != null)
        {
            AppendToConversationHistory(state, userMessage, degradedEscalation.BotResponse, flow.EngineSettings);
            await _stateManager.SaveAsync(userIdentifier, agent, flowDefinitionId, state, ct);
            return degradedEscalation;
        }

        // ── 10. Traverse graph ─────────────────────────────────────────────────────
        var result = await TraverseGraphAsync(ctx, flow.EngineSettings, ct);

        // ── 11. Append turn to conversation history ────────────────────────────────
        AppendToConversationHistory(state, userMessage, result.BotResponse, flow.EngineSettings);

        // ── 12. Save state ─────────────────────────────────────────────────────────
        await _stateManager.SaveAsync(userIdentifier, agent, flowDefinitionId, state, ct);

        return result;
    }

    private FlowOrchestratorResult? ProcessGlobalIntentionRedirects(
        FlowTurnContext ctx, FlowDefinitionDocument flow)
    {
        // Process IntentionSchema behaviors ordered by priority (lower = higher priority)
        var triggered = flow.IntentionSchema
            .Where(i => ctx.IsIntentionDetected(i.Key) && i.Behavior.Action != "none")
            .OrderBy(i => i.Priority)
            .FirstOrDefault();

        if (triggered == null) return null;

        var behavior = triggered.Behavior;
        _logger.LogInformation("Intention '{Key}' triggered action '{Action}'", triggered.Key, behavior.Action);

        switch (behavior.Action)
        {
            case "escalate":
                ctx.State.Owner = ConversationOwner.Human;
                var escalationMsg = behavior.ResponseTemplate ?? "Te estoy conectando con un asesor.";
                return FlowOrchestratorResult.Escalated(escalationMsg);

            case "goto_node":
                if (!string.IsNullOrEmpty(behavior.TargetNodeId))
                {
                    ctx.State.CurrentNodeId = behavior.TargetNodeId;
                    ctx.State.IsWaitingForUser = false;
                }
                return null; // continue traversal from new node

            default:
                return null;
        }
    }

    private async Task<FlowOrchestratorResult> TraverseGraphAsync(
        FlowTurnContext ctx,
        FlowEngineSettings settings,
        CancellationToken ct)
    {
        var state = ctx.State;
        var flow = ctx.FlowDefinition;
        var nodesVisited = 0;

        while (nodesVisited < settings.MaxNodesPerTurn)
        {
            var currentNode = flow.GetNode(state.CurrentNodeId);
            if (currentNode == null)
            {
                _logger.LogError("Node '{NodeId}' not found in flow definition", state.CurrentNodeId);
                return FlowOrchestratorResult.Err("Node not found", "Ocurrió un error interno.");
            }

            nodesVisited++;
            _logger.LogDebug("Processing node {NodeId} ({Type})", currentNode.Id, currentNode.Type);

            var handler = GetHandler(currentNode.Type);
            if (handler == null)
            {
                _logger.LogError("No handler registered for node type {Type}", currentNode.Type);
                return FlowOrchestratorResult.Err("No handler", "Error interno.");
            }

            NodeExecutionResult nodeResult;

            // ── Re-entry: user replied to a node that was waiting ─────────────────
            // executeWhen is skipped — it was already evaluated when the node first ran.
            // ReEntryBehavior drives whether the node re-executes or advances past.
            if (state.IsWaitingForUser && state.CurrentNodeId == currentNode.Id)
            {
                state.IsWaitingForUser = false;

                if (handler.ReEntryBehavior == ReEntryBehavior.AdvancePast)
                {
                    _logger.LogDebug("Node {NodeId}: AdvancePast re-entry", currentNode.Id);
                    nodeResult = NodeExecutionResult.Advance();
                }
                else
                {
                    _logger.LogDebug("Node {NodeId}: ReExecute re-entry", currentNode.Id);
                    nodeResult = await handler.ExecuteAsync(currentNode, ctx, ct);
                }
            }
            else
            {
                // ── First visit: evaluate executeWhen before running ───────────────
                state.IsWaitingForUser = false;

                if (HasExecuteWhenCondition(currentNode.Config, out var condition)
                    && !EvaluateCondition(condition!, state))
                {
                    _logger.LogDebug("Node {NodeId}: executeWhen=false — skipping", currentNode.Id);
                    RecordTrace(state, currentNode, "skipped", ctx.TurnNumber);
                    state.CurrentNodeId = GetNextNodeId(flow, currentNode.Id, "skipped") ?? "end";
                    continue;
                }

                nodeResult = await handler.ExecuteAsync(currentNode, ctx, ct);
            }

            // ── Apply node-level setFlags and setVariables ─────────────────────────
            if (nodeResult.Status != NodeExecutionStatus.Error)
            {
                ApplyNodeStateModifiers(currentNode.Config, state);
            }

            // ── Record trace ───────────────────────────────────────────────────────
            RecordTrace(state, currentNode, nodeResult.NextPort, ctx.TurnNumber);

            // ── Handle result ──────────────────────────────────────────────────────
            switch (nodeResult.Status)
            {
                case NodeExecutionStatus.WaitForUser:
                    state.CurrentNodeId = currentNode.Id;
                    state.IsWaitingForUser = true;
                    return FlowOrchestratorResult.Ok(
                        nodeResult.BotResponse ?? string.Empty,
                        currentNode.Id,
                        isComplete: false);

                case NodeExecutionStatus.Error:
                    _logger.LogError("Node {NodeId} error: {Error}", currentNode.Id, nodeResult.ErrorMessage);
                    return FlowOrchestratorResult.Err(nodeResult.ErrorMessage ?? "Error", "Ocurrió un error. ¿Puedes repetirlo?");

                case NodeExecutionStatus.Advance:
                case NodeExecutionStatus.Skipped:
                    if (currentNode.Type == FlowNodeType.End)
                    {
                        _logger.LogInformation("Flow completed at node {NodeId}", currentNode.Id);
                        return FlowOrchestratorResult.Ok(
                            ctx.PendingBotResponse ?? string.Empty,
                            currentNode.Id,
                            isComplete: true);
                    }

                    var nextNodeId = GetNextNodeId(flow, currentNode.Id, nodeResult.NextPort);
                    if (nextNodeId == null)
                    {
                        _logger.LogWarning("No edge from {NodeId} port={Port} — ending flow",
                            currentNode.Id, nodeResult.NextPort);
                        return FlowOrchestratorResult.Ok(
                            ctx.PendingBotResponse ?? string.Empty,
                            currentNode.Id,
                            isComplete: true);
                    }

                    state.CurrentNodeId = nextNodeId;
                    break;
            }
        }

        _logger.LogWarning("MaxNodesPerTurn ({Max}) reached — potential infinite loop", settings.MaxNodesPerTurn);
        return FlowOrchestratorResult.Err("Max nodes reached", "Ocurrió un error procesando tu solicitud.");
    }

    private static bool HasExecuteWhenCondition(JsonElement config, out FlowCondition? condition)
    {
        condition = null;
        if (!config.TryGetProperty("executeWhen", out var ewProp) ||
            ewProp.ValueKind == JsonValueKind.Null) return false;

        try
        {
            condition = JsonSerializer.Deserialize<FlowCondition>(ewProp.GetRawText(), JsonOpts);
            return condition != null;
        }
        catch { return false; }
    }

    private static bool EvaluateCondition(FlowCondition condition, FlowExecutionState state)
    {
        var p = condition.Parameters;

        return condition.Type switch
        {
            "FlagIsTrue" => p.TryGetValue("flag", out var f) && state.GetFlag(f),
            "FlagIsFalse" => p.TryGetValue("flag", out var f) && !state.GetFlag(f),
            "VariableIsNull" => p.TryGetValue("variable", out var v) && state.GetVariable(v) == null,
            "VariableIsNotNull" => p.TryGetValue("variable", out var v) && state.GetVariable(v) != null,
            "VariableEquals" => p.TryGetValue("variable", out var v) &&
                                p.TryGetValue("value", out var expected) &&
                                string.Equals(state.GetVariable(v), expected, StringComparison.OrdinalIgnoreCase),
            "And" => condition.Conditions?.All(c => EvaluateCondition(c, state)) ?? true,
            "Or" => condition.Conditions?.Any(c => EvaluateCondition(c, state)) ?? false,
            _ => true
        };
    }

    private static void ApplyNodeStateModifiers(JsonElement config, FlowExecutionState state)
    {
        if (config.TryGetProperty("setFlags", out var flagsProp))
        {
            foreach (var prop in flagsProp.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.True ||
                    prop.Value.ValueKind == JsonValueKind.False)
                    state.SetFlag(prop.Name, prop.Value.GetBoolean());
        }

        if (config.TryGetProperty("setVariables", out var varsProp))
        {
            foreach (var prop in varsProp.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Null)
                    state.Variables.Remove(prop.Name);
                else
                    state.Variables[prop.Name] = prop.Value.GetString();
            }
        }
    }

    private static string? GetNextNodeId(FlowDefinitionDocument flow, string nodeId, string? port)
    {
        // Try exact port match first, then fall back to null-port default edge
        var edge = flow.Edges.FirstOrDefault(e => e.SourceNodeId == nodeId && e.PortId == port)
                   ?? flow.Edges.FirstOrDefault(e => e.SourceNodeId == nodeId && e.PortId == null);
        return edge?.TargetNodeId;
    }

    private static void RecordTrace(
        FlowExecutionState state, FlowNode node, string? port, int turnNumber)
    {
        state.Trace.Add(new FlowTraceEntry
        {
            NodeId = node.Id,
            NodeType = node.Type.ToString(),
            Port = port,
            TurnNumber = turnNumber,
            ExecutedAt = DateTime.UtcNow
        });

        // Keep trace bounded to avoid unbounded growth
        if (state.Trace.Count > 200)
            state.Trace.RemoveAt(0);
    }

    private INodeHandler? GetHandler(FlowNodeType type) =>
        _handlers.FirstOrDefault(h => h.NodeType == type);

    /// <summary>
    /// Increments the counter when the extraction LLM call failed (WasSuccessful false);
    /// resets it when extraction succeeded (including when the model returned no fields).
    /// </summary>
    private static void UpdateDegradedTurns(FlowExecutionState state, bool extractionWasSuccessful)
    {
        if (extractionWasSuccessful)
            state.ConsecutiveDegradedTurns = 0;
        else
            state.ConsecutiveDegradedTurns++;
    }

    /// <summary>
    /// Returns an escalation result if the failed-extraction streak reaches the configured threshold.
    /// Sets Owner=Human and resets the counter so a fresh session can start later.
    /// Returns null when escalation is disabled or threshold not yet reached.
    /// </summary>
    private FlowOrchestratorResult? TryEscalateForDegradedTurns(
        FlowExecutionState state,
        FlowDefinitionDocument flow,
        Agent agent,
        FlowTurnContext ctx)
    {
        var threshold = flow.EngineSettings.DegradedTurnsBeforeEscalation;
        if (threshold <= 0 || state.ConsecutiveDegradedTurns < threshold)
            return null;

        _logger.LogWarning(
            "Auto-escalating after {N} consecutive failed extraction calls for {Identifier}",
            state.ConsecutiveDegradedTurns, ctx.UserIdentifier);

        state.Owner = ConversationOwner.Human;
        state.ConsecutiveDegradedTurns = 0;

        var msg = GetEscalationMessageFromSettings(agent.SettingsJson)
                  ?? "No he podido entenderte. Te conecto con un asesor para ayudarte mejor.";

        return FlowOrchestratorResult.Escalated(msg);
    }

    /// <summary>
    /// Reads the escalationMessage from agent SettingsJson.
    /// Structure expected: { "messages": { "escalationMessage": "..." } }
    /// </summary>
    private static string? GetEscalationMessageFromSettings(string? settingsJson)
    {
        if (string.IsNullOrEmpty(settingsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty("messages", out var msgs) &&
                msgs.TryGetProperty("escalationMessage", out var em))
                return em.GetString();
        }
        catch { /* fall through */ }
        return null;
    }

    /// <summary>
    /// Appends the current user message and bot response to the persistent conversation history
    /// stored inside the state. The list is capped at MaxConversationHistoryMessages * 2 entries
    /// (each turn = 1 user + 1 assistant message).
    /// </summary>
    private static void AppendToConversationHistory(
        FlowExecutionState state,
        string userMessage,
        string? botResponse,
        FlowEngineSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(userMessage))
            state.ConversationHistory.Add(new FlowConversationMessage
            {
                Role = "user",
                Content = userMessage
            });

        if (!string.IsNullOrWhiteSpace(botResponse))
            state.ConversationHistory.Add(new FlowConversationMessage
            {
                Role = "assistant",
                Content = botResponse
            });

        // MaxConversationHistoryMessages is in turns; each turn has 2 messages (user + assistant)
        var maxMessages = settings.MaxConversationHistoryMessages * 2;
        while (state.ConversationHistory.Count > maxMessages)
            state.ConversationHistory.RemoveAt(0);
    }
}
