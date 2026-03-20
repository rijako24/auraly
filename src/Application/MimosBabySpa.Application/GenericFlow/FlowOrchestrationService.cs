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
/// Generic flow engine with cluster-node architecture.
/// Agents are sticky (no reset to start each turn). Extraction happens inside each agent's sub-node.
/// ReRoute allows agents to transfer control back to the Router when an escape intent is detected.
/// </summary>
public class FlowOrchestrationService : IFlowOrchestrationService
{
    private readonly IAgentRepository _agentRepo;
    private readonly IFlowDefinitionRepository _flowDefRepo;
    private readonly FlowStateManager _stateManager;
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
        FlowStateManager stateManager,
        ICatalogContentGenerator catalogGenerator,
        IEnumerable<INodeHandler> handlers,
        ILogger<FlowOrchestrationService> logger)
    {
        _agentRepo = agentRepo;
        _flowDefRepo = flowDefRepo;
        _stateManager = stateManager;
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
        var agent = await _agentRepo.GetByIdAsync(agentId, ct);
        if (agent == null)
        {
            _logger.LogWarning("Agent {AgentId} not found for user {Identifier}", agentId, userIdentifier);
            return FlowOrchestratorResult.Err("Agent not found", "No pudimos procesar tu mensaje. Intenta más tarde.");
        }

        var businessId = agent.BusinessId;

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

        var (state, flowDefinitionId) = await _stateManager.LoadOrCreateAsync(
            userIdentifier, agent, flow, ct);

        if (state.Owner == ConversationOwner.Human)
        {
            _logger.LogDebug("Conversation {Identifier} owned by human — skipping", userIdentifier);
            return FlowOrchestratorResult.Ok(string.Empty);
        }

        var allSources = agent.KnowledgeSources
            .Select(aks => aks.KnowledgeSource)
            .Where(ks => ks.IsActive)
            .ToList();

        var catalogSources = allSources.Where(ks => ks.Type == KnowledgeSourceType.ServiceCatalog).ToList();
        if (catalogSources.Count > 0)
        {
            var dynamicCatalog = await _catalogGenerator.GenerateAsync(businessId, ct);
            if (!string.IsNullOrWhiteSpace(dynamicCatalog))
                foreach (var ks in catalogSources)
                    ks.Content = dynamicCatalog;
        }

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

        _logger.LogDebug("Traversal from CurrentNodeId={NodeId}", state.CurrentNodeId);

        ctx.TerminateTurn = null;

        var result = await TraverseGraphAsync(ctx, flow.EngineSettings, ct);

        AppendToConversationHistory(state, userMessage, result.BotResponse, flow.EngineSettings);

        await _stateManager.SaveAsync(userIdentifier, agent, flowDefinitionId, state, ct);

        return result;
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

            if (ctx.TerminateTurn != null)
                return MapTerminationToOrchestratorResult(ctx.TerminateTurn, state);

            if (nodeResult.Status != NodeExecutionStatus.Error)
                ApplyNodeStateModifiers(currentNode.Config, state);

            RecordTrace(state, currentNode, nodeResult.NextPort, ctx.TurnNumber);

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

                case NodeExecutionStatus.ReRoute:
                    var routerNodeId = FindRouterNodeId(flow);
                    if (routerNodeId == null)
                    {
                        _logger.LogWarning("ReRoute requested but no Router node found — staying on {NodeId}", currentNode.Id);
                        break;
                    }
                    if (!string.IsNullOrEmpty(nodeResult.DetectedIntent))
                        ctx.DetectedIntentions[nodeResult.DetectedIntent] = true;
                    _logger.LogInformation("Agent {NodeId} triggered ReRoute for intent '{Intent}' — jumping to Router {RouterId}",
                        currentNode.Id, nodeResult.DetectedIntent, routerNodeId);
                    state.CurrentNodeId = routerNodeId;
                    state.IsWaitingForUser = false;
                    continue;

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

                    var nextNodeId = state.PendingJumpNodeId;
                    state.PendingJumpNodeId = null;
                    if (string.IsNullOrEmpty(nextNodeId))
                        nextNodeId = GetNextNodeId(flow, currentNode.Id, nodeResult.NextPort);

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

    private static FlowOrchestratorResult MapTerminationToOrchestratorResult(
        FlowTurnTerminationRequest t,
        FlowExecutionState state)
    {
        var r = new FlowOrchestratorResult
        {
            Success = t.Success,
            BotResponse = t.BotResponse,
            ErrorMessage = t.ErrorMessage,
            IsEscalated = t.IsEscalated,
            IsFlowComplete = t.IsFlowComplete,
            CurrentNodeId = t.CurrentNodeId
        };
        foreach (var kv in state.Variables)
            r.Variables[kv.Key] = kv.Value;
        return r;
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

    private static string? FindRouterNodeId(FlowDefinitionDocument flow) =>
        flow.Nodes.FirstOrDefault(n => n.Type == FlowNodeType.IntentionRouter)?.Id;

    private static string? GetNextNodeId(FlowDefinitionDocument flow, string nodeId, string? port)
    {
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

        if (state.Trace.Count > 200)
            state.Trace.RemoveAt(0);
    }

    private INodeHandler? GetHandler(FlowNodeType type) =>
        _handlers.FirstOrDefault(h => h.NodeType == type);

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

        var maxMessages = settings.MaxConversationHistoryMessages * 2;
        while (state.ConversationHistory.Count > maxMessages)
            state.ConversationHistory.RemoveAt(0);
    }
}
