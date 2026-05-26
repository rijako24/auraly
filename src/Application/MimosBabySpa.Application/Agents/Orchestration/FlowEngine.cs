using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Motor de flujo guiado: un turno = un bucle. Cada iteración resuelve el stage activo,
/// ejecuta lookup si aplica, invoca al LLM y procesa tool calls. Puede avanzar de stage
/// dentro del mismo turno sin concatenar respuestas ni llamadas LLM duplicadas.
/// </summary>
public sealed class FlowEngine : IFlowEngine
{
    private const int MaxToolIterationsPerTurn = 6;

    private readonly IFlowLlm _llm;
    private readonly IToolCapabilityGate _gate;
    private readonly IRoleFactResolver _roleResolver;
    private readonly ITemplateRenderer _renderer;
    private readonly IMessageService _messageService;
    private readonly IConversationStateManager _stateManager;
    private readonly IConversationLifecycleService _lifecycleService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngine(
        IFlowLlm llm,
        IToolCapabilityGate gate,
        IRoleFactResolver roleResolver,
        ITemplateRenderer renderer,
        IMessageService messageService,
        IConversationStateManager stateManager,
        IConversationLifecycleService lifecycleService,
        AgentToolRegistry toolRegistry,
        ILogger<FlowEngine> logger)
    {
        _llm = llm;
        _gate = gate;
        _roleResolver = roleResolver;
        _renderer = renderer;
        _messageService = messageService;
        _stateManager = stateManager;
        _lifecycleService = lifecycleService;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task<AgentTurnResult> ProcessTurnAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        CancellationToken ct)
    {
        var turn = new AgentTurnExecution(config.ConsecutiveErrorEscalationThreshold);
        session.Turn = turn;
        session.LastUserMessage = userMessage;

        var history = await _messageService.GetConversationHistoryAsync(session.ConversationId);
        var recentHistory = history.TakeLast(6).ToList();

        var completedStagesAtTurnStart = FlowStageCompletionRules.SnapshotCompletedOneShotStages(session);
        var maxOutboundChars = ResolveOutboundMaxChars(config);
        var maxIterations = Math.Min(config.MaxToolIterations, MaxToolIterationsPerTurn);

        var extraMessages = new List<ChatMessage>();
        string? loggedStageId = null;
        string? stallStageId = null;

        for (var iter = 0; iter < maxIterations; iter++)
        {
            var currentStage = FindApplicableStage(config, session, null);
            session.CurrentStageId = currentStage?.Id;

            if (currentStage is null)
            {
                var fallback = SanitizeResponse(
                    "¿En qué puedo ayudarte?",
                    maxOutboundChars);
                await PersistTurnAsync(session, userMessage, fallback, ct);
                return AgentTurnResult.Ok(fallback, tokens: turn.TotalTokens, toolCalls: turn.ToolCallCount);
            }

            if (!string.Equals(loggedStageId, currentStage.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Conv {C}: stage={Stage} oneShot=[{OneShot}] action=[{Action}] facts=[{Facts}]",
                    session.ConversationId,
                    currentStage.Id,
                    string.Join(',', session.ConversationState.CompletedOneShotStages),
                    string.Join(',', session.ConversationState.CompletedActionStages),
                    string.Join(',', session.Facts.Keys));
                loggedStageId = currentStage.Id;
            }

            if (!string.IsNullOrWhiteSpace(currentStage.Verbatim))
            {
                var reply = currentStage.Verbatim.Trim();
                if (currentStage.CompletedWhen == StageCompletionCriteria.Always)
                    FlowStageCompletionRules.MarkOneShotCompleted(session, currentStage);

                await PersistTurnAsync(session, userMessage, reply, ct);
                return AgentTurnResult.Ok(reply, tokens: turn.TotalTokens, toolCalls: turn.ToolCallCount);
            }

            var lookupResolution = await ResolveStageLookupAsync(
                config, session, currentStage, turn, ct);
            var lookupResult = lookupResolution.Result;
            var lookupOmittedHint = lookupResolution.OmittedHint;

            var renderedTemplate = FlowStageLookupPresentation.IsLlmCurate(currentStage)
                ? null
                : RenderStageTemplate(config, currentStage.Template, lookupResult);

            var toolDefinitions = _toolRegistry
                .GetToolsForStage(config, currentStage)
                .Select(ToChatToolDefinition)
                .ToList();

            var stageCollects = FactSchemaPrompt.ResolveCollectKeys(
                config.FactSchema, currentStage.Collects);

            session.CurrentToolIteration = iter + 1;

            var llmResult = await _llm.RunAsync(new FlowLlmRequest
            {
                Config = config,
                Stage = currentStage,
                UserMessage = extraMessages.Count == 0
                    ? userMessage
                    : "Please continue the conversation after the tool result.",
                StageCollects = stageCollects,
                KnownFacts = session.Facts,
                LookupResult = lookupResult,
                LookupOmittedHint = lookupOmittedHint,
                RenderedTemplate = renderedTemplate,
                History = recentHistory,
                AvailableTools = toolDefinitions,
                ExtraMessages = extraMessages
            }, ct);

            turn.TotalTokens += llmResult.Tokens;

            if (llmResult.ToolCalls.Count > 0)
            {
                stallStageId = null;
                extraMessages.Add(ChatMessage.AssistantWithToolCalls(llmResult.ToolCalls));

                FlowToolResult? lastToolResultInIteration = null;
                foreach (var toolCall in llmResult.ToolCalls)
                {
                    var tool = _toolRegistry.Resolve(toolCall.FunctionName);
                    var toolOutput = await ExecuteToolCallAsync(toolCall, tool, session, turn, ct);
                    extraMessages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, toolOutput));
                    lastToolResultInIteration = FlowToolResult.Parse(toolOutput);

                    if (tool is not null
                        && currentStage.Execute is not null
                        && string.Equals(currentStage.Execute.Tool, tool.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!lastToolResultInIteration.IsError)
                            FlowStageCompletionRules.MarkActionCompleted(session, currentStage);
                    }
                }

                FlowStageCompletionRules.ApplyEndOfTurn(
                    session,
                    currentStage,
                    completedStagesAtTurnStart,
                    llmResult,
                    lastToolResultInIteration ?? lookupResult,
                    _logger);

                if (turn.ShouldAutoEscalate)
                {
                    var escalated = FlowTurnResult.Fallback(
                        "El asistente falló en varias llamadas a herramientas y necesita intervención humana.");
                    return await CompleteTurnAsync(session, userMessage, escalated, maxOutboundChars, turn, ct);
                }

                var nextAfterTools = FindApplicableStage(
                    config, session, lastToolResultInIteration ?? lookupResult);
                if (!ShouldContinueAfterToolCalls(currentStage, nextAfterTools, session))
                {
                    if (string.IsNullOrWhiteSpace(llmResult.Reply)
                        && nextAfterTools is not null
                        && !string.Equals(
                            nextAfterTools.Id, currentStage.Id, StringComparison.OrdinalIgnoreCase)
                        && session.ConversationState.CompletedOneShotStages.Contains(currentStage.Id))
                    {
                        extraMessages.Clear();
                        stallStageId = null;
                        continue;
                    }

                    return await CompleteTurnAsync(session, userMessage, llmResult, maxOutboundChars, turn, ct);
                }

                if (nextAfterTools is not null
                    && !string.Equals(nextAfterTools.Id, currentStage.Id, StringComparison.OrdinalIgnoreCase))
                {
                    extraMessages.Clear();
                    stallStageId = null;
                }

                continue;
            }

            if (string.Equals(stallStageId, currentStage.Id, StringComparison.OrdinalIgnoreCase))
            {
                var exhausted = FlowTurnResult.Fallback(
                    "El asistente no pudo avanzar en este paso. Por favor intenta nuevamente.");
                return await CompleteTurnAsync(session, userMessage, exhausted, maxOutboundChars, turn, ct);
            }

            stallStageId = currentStage.Id;

            var stageJustCompleted = FlowStageCompletionRules.ApplyEndOfTurn(
                session,
                currentStage,
                completedStagesAtTurnStart,
                llmResult,
                lookupResult,
                _logger);

            if (stageJustCompleted
                && ShouldAdvanceSameTurn(session, currentStage, FindApplicableStage(config, session, lookupResult)))
            {
                _logger.LogInformation(
                    "Conv {C}: stage {Completed} completed, continuing to next stage in same turn",
                    session.ConversationId, currentStage.Id);
                stallStageId = null;
                continue;
            }

            return await CompleteTurnAsync(session, userMessage, llmResult, maxOutboundChars, turn, ct);
        }

        var tooMany = FlowTurnResult.Fallback(
            "El asistente hizo demasiadas llamadas a herramientas y no pudo finalizar. Por favor intenta nuevamente.");
        return await CompleteTurnAsync(session, userMessage, tooMany, maxOutboundChars, turn, ct);
    }

    private async Task<AgentTurnResult> CompleteTurnAsync(
        AgentToolContext session,
        string userMessage,
        FlowTurnResult llmResult,
        int maxOutboundChars,
        AgentTurnExecution turn,
        CancellationToken ct)
    {
        var reply = SanitizeResponse(llmResult.Reply, maxOutboundChars);
        await PersistTurnAsync(session, userMessage, reply, ct);

        _logger.LogInformation(
            "Conv {C}: turn complete — tokens={T} tools={TC} escalated={E} reservation={R}",
            session.ConversationId, turn.TotalTokens, turn.ToolCallCount,
            turn.EscalatedToHuman, turn.ReservationCreated);

        return AgentTurnResult.Ok(reply,
            escalated: turn.EscalatedToHuman,
            reservationCreated: turn.ReservationCreated,
            tokens: turn.TotalTokens,
            toolCalls: turn.ToolCallCount);
    }

    private sealed record StageLookupResolution(FlowToolResult? Result, string? OmittedHint);

    private async Task<StageLookupResolution> ResolveStageLookupAsync(
        AgentConfig config,
        AgentToolContext session,
        AgentFlowStage stage,
        AgentTurnExecution turn,
        CancellationToken ct)
    {
        if (stage.Lookup is null)
            return new StageLookupResolution(null, null);

        var lookupTool = _toolRegistry.Resolve(stage.Lookup.Tool);
        var (resolvedArgs, unresolved) = FlowRefResolver.ResolveArgsDetailed(
            stage.Lookup.Args, session, null);

        if (!FlowLookupGate.CanExecute(stage, session, lookupTool))
        {
            var hint = FlowLookupGate.FormatOmittedHint(unresolved);
            _logger.LogInformation(
                "Conv {C}: lookup {T} omitted — unresolved args: {Args}",
                session.ConversationId, stage.Lookup.Tool, string.Join(',', unresolved));
            return new StageLookupResolution(null, hint);
        }

        var lookupResult = await RunLookupToolAsync(
            stage.Lookup.Tool,
            resolvedArgs,
            session, turn, ct);

        if (lookupResult.IsError)
        {
            _logger.LogWarning("Conv {C}: lookup {T} failed: {E}",
                session.ConversationId, stage.Lookup.Tool, lookupResult.ErrorMessage);
            return new StageLookupResolution(null, null);
        }

        return new StageLookupResolution(lookupResult, null);
    }

    private static bool ShouldContinueAfterToolCalls(
        AgentFlowStage currentStage,
        AgentFlowStage? nextStage,
        AgentToolContext session)
    {
        if (nextStage is null)
            return false;

        if (string.Equals(nextStage.Id, currentStage.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (nextStage.Lookup is not null || !string.IsNullOrWhiteSpace(nextStage.Verbatim))
            return true;

        return session.ConversationState.CompletedOneShotStages.Contains(currentStage.Id)
            && !string.Equals(nextStage.Id, currentStage.Id, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldAdvanceSameTurn(
        AgentToolContext session,
        AgentFlowStage completedStage,
        AgentFlowStage? nextStage)
    {
        if (nextStage is null)
            return false;

        if (string.Equals(nextStage.Id, completedStage.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        if (nextStage.Lookup is not null)
        {
            var lookupTool = _toolRegistry.Resolve(nextStage.Lookup.Tool);
            return FlowLookupGate.CanExecute(nextStage, session, lookupTool);
        }

        return session.ConversationState.CompletedOneShotStages.Contains(completedStage.Id);
    }

    private async Task<string> ExecuteToolCallAsync(
        ToolCallRequest toolCall,
        IAgentTool? tool,
        AgentToolContext session,
        AgentTurnExecution turn,
        CancellationToken ct)
    {
        if (tool is null)
        {
            var error = ToolResultHelper.Error(
                "tool_not_found",
                $"Tool '{toolCall.FunctionName}' is not available.",
                "Use only the tools exposed in the current stage.");
            turn.RecordToolOutcome(ToolExecutionOutcome.Parse(error), toolCall.FunctionName);
            return error;
        }

        try
        {
            using var argsDoc = JsonDocument.Parse(toolCall.ArgumentsJson);

            var gate = await _gate.EvaluateAsync(tool, argsDoc.RootElement, session, ct);
            if (!gate.IsAllowed)
            {
                var blocked = ToolResultHelper.Error(
                    "gate_blocked",
                    gate.Reason ?? "Preconditions not met.",
                    gate.Remediation);
                turn.RecordToolOutcome(ToolExecutionOutcome.Parse(blocked), tool.Name);
                return blocked;
            }

            var invocation = new ToolInvocation
            {
                Arguments = argsDoc.RootElement,
                ResolvedFacts = _roleResolver.Resolve(tool, session),
                Context = session
            };

            var rawJson = await tool.ExecuteAsync(invocation, ct);
            var outcome = ToolExecutionOutcome.Parse(rawJson);
            turn.RecordToolOutcome(outcome, tool.Name);
            return rawJson;
        }
        catch (JsonException ex)
        {
            var error = ToolResultHelper.Error(
                "invalid_json",
                $"Tool arguments are not valid JSON: {ex.Message}",
                "Verify the JSON object in the tool call.");
            turn.RecordToolOutcome(ToolExecutionOutcome.Parse(error), tool.Name);
            return error;
        }
        catch (Exception ex)
        {
            var error = ToolResultHelper.Error(
                "tool_exception",
                ex.Message,
                "An unexpected error occurred executing the tool.");
            turn.RecordToolOutcome(ToolExecutionOutcome.Parse(error), tool.Name);
            return error;
        }
    }

    private static ChatToolDefinition ToChatToolDefinition(IAgentTool tool) =>
        new()
        {
            Name = tool.Name,
            Description = tool.Description,
            ParametersJson = tool.ParametersSchema
        };

    private AgentFlowStage? FindApplicableStage(
        AgentConfig config,
        AgentToolContext session,
        FlowToolResult? lastToolResult)
    {
        foreach (var stage in config.Flow.Stages)
        {
            if (FlowStageCompletionRules.IsStageCompleted(stage, session, lastToolResult))
                continue;

            if (stage.AppliesWhen is not null
                && !FlowRefResolver.EvaluateCondition(stage.AppliesWhen, session, lastToolResult))
                continue;

            return stage;
        }

        return null;
    }

    private async Task<FlowToolResult> RunLookupToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string> args,
        AgentToolContext session,
        AgentTurnExecution turn,
        CancellationToken ct) =>
        await ExecuteRegisteredToolAsync(toolName, args, session, turn, ct);

    private async Task<FlowToolResult> ExecuteRegisteredToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string> args,
        AgentToolContext session,
        AgentTurnExecution turn,
        CancellationToken ct)
    {
        var tool = _toolRegistry.Resolve(toolName);
        if (tool is null)
        {
            _logger.LogWarning("Conv {C}: tool '{T}' not registered", session.ConversationId, toolName);
            return FlowToolResult.FromError("tool_not_found", $"Tool '{toolName}' is not registered.");
        }

        var argsJson = JsonSerializer.Serialize(args);
        using var argsDoc = JsonDocument.Parse(argsJson);

        var invocation = new ToolInvocation
        {
            Arguments = argsDoc.RootElement,
            ResolvedFacts = _roleResolver.Resolve(tool, session),
            Context = session
        };

        _logger.LogInformation("Conv {C}: engine tool [{T}]({A})",
            session.ConversationId, toolName, argsJson);

        var rawJson = await tool.ExecuteAsync(invocation, ct);
        var result = FlowToolResult.Parse(rawJson);

        var outcome = ToolExecutionOutcome.Parse(rawJson);
        turn.RecordToolOutcome(outcome, toolName);

        return result;
    }

    private string? RenderStageTemplate(AgentConfig config, string? stageTemplate, FlowToolResult? lookupResult)
    {
        if (stageTemplate is null) return null;

        var templateId = ResolveTemplateId(stageTemplate, lookupResult);
        if (templateId is null || !config.Templates.TryGetValue(templateId, out var tmplText))
            return null;

        var data = lookupResult?.TemplateData ?? new Dictionary<string, object?>();
        return _renderer.Render(tmplText, data);
    }

    private static string? ResolveTemplateId(string? stageTemplate, FlowToolResult? toolResult)
    {
        if (string.IsNullOrWhiteSpace(stageTemplate)) return null;

        if (stageTemplate.Equals("@result.template_id", StringComparison.OrdinalIgnoreCase))
            return toolResult?.TemplateId;

        return stageTemplate;
    }

    private static int ResolveOutboundMaxChars(AgentConfig config) =>
        Math.Min(config.OperationalLimits.OutputMaxChars, Messaging.WhatsAppMessageLimits.MaxTextBodyChars);

    private static string SanitizeResponse(string response, int maxChars) =>
        response.Length > maxChars ? response[..maxChars].Trim() : response.Trim();

    private async Task PersistTurnAsync(
        AgentToolContext session,
        string userMessage,
        string botResponse,
        CancellationToken ct)
    {
        await _messageService.SaveMessageAsync(session.ConversationId, "user", userMessage);
        if (!string.IsNullOrWhiteSpace(botResponse))
            await _messageService.SaveMessageAsync(session.ConversationId, "bot", botResponse);

        var state = session.ConversationState;
        state.LastUserMessage = userMessage;
        state.LastBotMessage = botResponse;
        state.ConsecutiveDegradedTurns = 0;

        await _stateManager.SaveStateAsync(session.ConversationId, state, ct);
        await _lifecycleService.TouchActivityAsync(session.ConversationId, userMessage, ct);
    }
}
