using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Orquestador del agente. Unico punto de entrada para procesar un turno de conversacion.
///
/// Responsabilidades (por capa):
///   1. Guardrails de corto-circuito (Owner=Human desde UI) - sin llamar al LLM.
///   2. Bucle nativo de Function Calling con limite de iteraciones y auto-escalacion.
///   3. Persistencia del turno y propagacion de side-effects al canal.
///
/// NO toma decisiones de negocio; las tools y sus servicios son la autoridad.
/// </summary>
public sealed class AgentConversationService : IAgentConversationService
{
    private const int MaxEntryActionReconciliationPasses = 6;

    private readonly IAgentConfigProvider _configProvider;
    private readonly IChatClient _chatClient;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IEscalationConfigProvider _escalationConfig;
    private readonly IBusinessClock _businessClock;
    private readonly ITemporalReferenceBuilder _temporalReferenceBuilder;
    private readonly IAgentTurnToolResolver _turnToolResolver;
    private readonly IConversationFactsService _factsService;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly IRequestContextService _requestContext;
    private readonly IReservationLifecycleService _reservationLifecycle;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IConversationService _conversationService;
    private readonly IConversationLifecycleService _lifecycleService;
    private readonly IPromptComposer _promptComposer;
    private readonly IAgentTurnResponseComposer _turnResponseComposer;
    private readonly IToolCapabilityGate _toolCapabilityGate;
    private readonly IFlowRuntimeOrchestrator _flowRuntime;
    private readonly IFlowStageDetector _flowStageDetector;
    private readonly IFactHydrator _factHydrator;
    private readonly IUsageBillingService _usageBilling;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IEventNotificationDispatcher _eventNotificationDispatcher;
    private readonly ILogger<AgentConversationService> _logger;

    public AgentConversationService(
        IAgentConfigProvider configProvider,
        IChatClient chatClient,
        AgentToolRegistry toolRegistry,
        IConversationStateManager stateManager,
        IMessageService messageService,
        IEscalationConfigProvider escalationConfig,
        IBusinessClock businessClock,
        ITemporalReferenceBuilder temporalReferenceBuilder,
        IAgentTurnToolResolver turnToolResolver,
        IConversationFactsService factsService,
        ICustomerMemoryService customerMemory,
        IRequestContextService requestContext,
        IReservationLifecycleService reservationLifecycle,
        IPaymentLifecycleService paymentLifecycle,
        IConversationService conversationService,
        IConversationLifecycleService lifecycleService,
        IPromptComposer promptComposer,
        IAgentTurnResponseComposer turnResponseComposer,
        IToolCapabilityGate toolCapabilityGate,
        IFlowRuntimeOrchestrator flowRuntime,
        IFlowStageDetector flowStageDetector,
        IFactHydrator factHydrator,
        IUsageBillingService usageBilling,
        IMessageSequenceResolver sequenceResolver,
        IEventNotificationDispatcher eventNotificationDispatcher,
        ILogger<AgentConversationService> logger)
    {
        _configProvider = configProvider;
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _stateManager = stateManager;
        _messageService = messageService;
        _escalationConfig = escalationConfig;
        _businessClock = businessClock;
        _temporalReferenceBuilder = temporalReferenceBuilder;
        _turnToolResolver = turnToolResolver;
        _factsService = factsService;
        _customerMemory = customerMemory;
        _requestContext = requestContext;
        _reservationLifecycle = reservationLifecycle;
        _paymentLifecycle = paymentLifecycle;
        _conversationService = conversationService;
        _lifecycleService = lifecycleService;
        _promptComposer = promptComposer;
        _turnResponseComposer = turnResponseComposer;
        _toolCapabilityGate = toolCapabilityGate;
        _flowRuntime = flowRuntime;
        _flowStageDetector = flowStageDetector;
        _factHydrator = factHydrator;
        _usageBilling = usageBilling;
        _sequenceResolver = sequenceResolver;
        _eventNotificationDispatcher = eventNotificationDispatcher;
        _logger = logger;
    }

    public async Task<AgentTurnResult> ProcessMessageAsync(
        Guid agentId,
        Guid conversationId,
        string userMessage,
        string? channelPhone = null,
        CancellationToken cancellationToken = default,
        AgentInboundMetadata? inboundMetadata = null)
    {
        var config = await _configProvider.GetConfigAsync(agentId, cancellationToken);
        var usageGate = await _usageBilling.CanProcessAsync(config.BusinessId, cancellationToken);
        if (!usageGate.IsAllowed)
        {
            _logger.LogWarning(
                "Business {BusinessId}: usage gate blocked agent turn ({Code}) for Conv {ConvId}",
                config.BusinessId, usageGate.Code, conversationId);
            return AgentTurnResult.Ok(string.Empty);
        }

        var state = await _stateManager.GetOrCreateStateAsync(
            conversationId, config.BusinessId, channelPhone ?? string.Empty, cancellationToken);

        var clockSnapshot = await _businessClock.GetSnapshotAsync(config.BusinessId, cancellationToken);
        var session = await LoadTurnSessionAsync(
            config, state, conversationId, channelPhone, inboundMetadata, clockSnapshot, cancellationToken);

        await ApplyInboundFactsAsync(config, session, inboundMetadata?.Facts, cancellationToken);
        await ApplySkipWhenAutoSetsAsync(config, session, cancellationToken);

        // Capa 1 - corto-circuito sin LLM
        if (state.Owner == ConversationOwner.Human)
        {
            _logger.LogInformation("Conv {ConvId}: Owner=Human, skipping bot", conversationId);
            return AgentTurnResult.Ok(string.Empty);
        }


        // Capa 2 - bucle de Function Calling
        userMessage = SanitizeInput(userMessage);
        session.LatestUserMessage = userMessage;

        var history = (await _messageService.GetRecentConversationHistoryAsync(
            conversationId, config.HistoryWindowSize, cancellationToken)).ToList();

        // Resolver engagement e inyectarlo como fact de sesion (efimero, no persiste en BD)
        var engagementKey = await ResolveEngagementKeyAsync(
            config.BusinessId, session.Conversation.UserNumber, history, cancellationToken);
        session.Facts["session.engagement"] = engagementKey;

        await ApplyCollectFactCapturesAsync(config, session, userMessage, cancellationToken);

        var temporal = _temporalReferenceBuilder.Build(clockSnapshot);
        var latestPayment = await _paymentLifecycle.GetLatestByConversationAsync(conversationId, cancellationToken);
        var latestActionablePayment = ResolveActionablePayment(latestPayment);
        session.ActivePayment = ResolveActionablePayment(session.ActivePayment) ?? latestActionablePayment;

        var turnTools = await _turnToolResolver.ResolveAsync(config, clockSnapshot, cancellationToken);
        var effectiveTools = turnTools.EffectiveTools;
        session.OperatingHours = turnTools.OperatingHours;

        session.RuntimeDecision = await _flowRuntime.ApplyAsync(config, session, userMessage, cancellationToken);

        var activeHistory = ProjectHistoryForTurn(history, state.ActiveRequestStartedAtUtc, session.ActivePayment, latestActionablePayment);

        var compositionInput = new PromptCompositionInput
        {
            Config = config,
            History = activeHistory,
            Temporal = temporal,
            Session = session,
            LatestPayment = latestActionablePayment,
            EnabledTools = effectiveTools
        };
        var systemPrompt = _promptComposer.Compose(compositionInput);
        var messages = BuildMessages(systemPrompt, activeHistory, userMessage);
        var turn = new AgentTurnExecution(config.ConsecutiveErrorEscalationThreshold);
        session.Turn = turn;

        var interactiveResult = await TryHandleConfiguredInteractiveActionAsync(
            config,
            conversationId,
            userMessage,
            session,
            turn,
            turnTools.ConfiguredTools,
            cancellationToken);
        if (interactiveResult is not null)
            return interactiveResult;

        var loop = await RunAgentLoopAsync(
            config, conversationId, messages, turn, session, compositionInput, cancellationToken);

        return loop.Kind switch
        {
            AgentLoopOutcome.OutcomeKind.Completed =>
                await FinalizeTurnAsync(
                    conversationId, userMessage, loop.Response!, session, turn, config, effectiveTools, cancellationToken),

            AgentLoopOutcome.OutcomeKind.AutoEscalate =>
                await EscalateAndPersistAsync(
                    config, session, userMessage, loop.Reason!, cancellationToken),

            _ => AgentTurnResult.Fail(loop.Reason ?? "Unknown loop failure")
        };
    }

    private async Task<AgentTurnResult?> TryHandleConfiguredInteractiveActionAsync(
        AgentConfig config,
        Guid conversationId,
        string userMessage,
        AgentToolContext session,
        AgentTurnExecution turn,
        IReadOnlyList<IAgentTool> configuredTools,
        CancellationToken ct)
    {
        var interactive = session.InteractiveAction;
        if (interactive is null)
            return null;

        if (!TryGetConfiguredReservationAutomationAction(config, interactive, out var action)
            || action is null
            || string.IsNullOrWhiteSpace(action.Tool))
        {
            _logger.LogDebug(
                "Conv {ConvId}: interactive payload '{Payload}' has no configured reservation automation action; continuing normal agent flow",
                conversationId,
                interactive.RawPayload);
            return null;
        }

        var toolName = action.Tool.Trim();
        if (!configuredTools.Any(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Conv {ConvId}: configured interactive action {Scope}:{Outcome} references unavailable tool '{Tool}'",
                conversationId,
                interactive.Scope,
                interactive.Outcome,
                toolName);
            return null;
        }

        var toolCall = new ToolCallRequest
        {
            Id = $"interactive_{Guid.NewGuid():N}",
            FunctionName = toolName,
            ArgumentsJson = BuildInteractiveActionArgumentsJson(action.Arguments, interactive)
        };

        session.CurrentToolIteration = 0;
        var factsBeforeTool = SnapshotFacts(session);
        var outcome = await ExecuteToolCallAsync(toolCall, session, ct);
        if (outcome.IsError)
        {
            _logger.LogWarning(
                "Conv {ConvId}: configured interactive action {Scope}:{Outcome} failed with {Code}; continuing normal agent flow",
                conversationId,
                interactive.Scope,
                interactive.Outcome,
                outcome.ErrorCode ?? "tool_error");
            return null;
        }

        var factChanges = GetFactChanges(factsBeforeTool, session.Facts);
        await ApplyReentryInvalidationAsync(session, factChanges.InvalidatingFactKeys, ct);
        await ApplyAfterToolRulesAsync(config, toolCall, outcome, session, ct);
        await DispatchToolEffectNotificationsAsync(config, outcome, session, ct);
        turn.RecordToolOutcome(outcome);

        await EnqueueInteractiveActionSequenceAsync(config, action, interactive, session, ct);

        _logger.LogInformation(
            "Conv {ConvId}: configured interactive action {Scope}:{Outcome} handled by tool {Tool}",
            conversationId,
            interactive.Scope,
            interactive.Outcome,
            toolName);

        return await FinalizeTurnAsync(
            conversationId,
            userMessage,
            string.Empty,
            session,
            turn,
            config,
            configuredTools,
            ct);
    }

    private static bool TryGetConfiguredReservationAutomationAction(
        AgentConfig config,
        InteractivePayloadAction interactive,
        out ReservationAutomationActionConfig? action)
    {
        action = null;

        if (!interactive.Scope.Equals("reservation_attendance", StringComparison.OrdinalIgnoreCase))
            return false;

        return config.ReservationAutomations.Confirmation?.Actions.TryGetValue(interactive.Outcome, out action) == true;
    }

    private async Task EnqueueInteractiveActionSequenceAsync(
        AgentConfig config,
        ReservationAutomationActionConfig action,
        InteractivePayloadAction interactive,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        var sequenceName = action.SendMessageSequence?.Trim();
        if (string.IsNullOrWhiteSpace(sequenceName))
            return;

        if (ctx.Turn is null)
            return;

        if (!config.MessageSequences.ContainsKey(sequenceName))
        {
            _logger.LogWarning(
                "Conv {ConvId}: interactive action sequence '{Sequence}' is not configured",
                ctx.ConversationId,
                sequenceName);
            return;
        }

        if (!ctx.Turn.TryMarkSequenceEnqueued(sequenceName))
            return;

        var custom = new Dictionary<string, string>(ctx.Facts, StringComparer.OrdinalIgnoreCase)
        {
            ["interactive.scope"] = interactive.Scope,
            ["interactive.outcome"] = interactive.Outcome,
            ["interactive.source_id"] = interactive.SourceId,
            ["interactive.raw_payload"] = interactive.RawPayload
        };

        var messages = await _sequenceResolver.ResolveAsync(
            ctx.BusinessId,
            sequenceName,
            config.MessageSequences,
            new MessageSequenceContext
            {
                Reservation = ctx.SingleManageableReservation,
                Custom = custom
            },
            ct);

        if (messages.Count == 0)
            return;

        ctx.Turn.EnqueueOutbound(messages);
        ctx.Turn.MarkDirectOutboundRequested();
    }

    private static string BuildInteractiveActionArgumentsJson(
        IReadOnlyDictionary<string, JsonElement> configuredArguments,
        InteractivePayloadAction interactive)
    {
        if (configuredArguments.Count == 0)
            return "{}";

        var resolved = configuredArguments.ToDictionary(
            pair => pair.Key,
            pair => ResolveInteractiveArgument(pair.Value, interactive),
            StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(resolved);
    }

    private static object? ResolveInteractiveArgument(JsonElement value, InteractivePayloadAction interactive)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => ResolveInteractivePlaceholders(value.GetString() ?? string.Empty, interactive),
            JsonValueKind.Number => value.Clone(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.Clone()
        };
    }

    private static string ResolveInteractivePlaceholders(string value, InteractivePayloadAction interactive)
    {
        return Regex.Replace(value, "\\{(?<key>[^{}]+)\\}", match =>
        {
            var key = match.Groups["key"].Value.Trim();
            return key switch
            {
                "scope" => interactive.Scope,
                "outcome" => interactive.Outcome,
                "source_id" => interactive.SourceId,
                "raw_payload" => interactive.RawPayload,
                _ => match.Value
            };
        });
    }

    private static string BuildEntryActionArgumentsJson(
        IReadOnlyDictionary<string, JsonElement> configuredArguments,
        AgentToolContext ctx)
    {
        if (configuredArguments.Count == 0)
            return "{}";

        var resolved = configuredArguments.ToDictionary(
            pair => pair.Key,
            pair => ResolveEntryActionArgument(pair.Value, ctx),
            StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(resolved);
    }

    private static object? ResolveEntryActionArgument(JsonElement value, AgentToolContext ctx)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => ResolveEntryActionPlaceholders(value.GetString() ?? string.Empty, ctx),
            JsonValueKind.Number => value.Clone(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.Clone()
        };
    }

    private static string ResolveEntryActionPlaceholders(string value, AgentToolContext ctx)
    {
        var resolved = value
            .Replace("{{user.message}}", ctx.LatestUserMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{{message}}", ctx.LatestUserMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{{latest_user_message}}", ctx.LatestUserMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        return Regex.Replace(
            resolved,
            @"\{\{(?:fact|facts)\.(?<key>[^{}]+)\}\}",
            match =>
            {
                var key = match.Groups["key"].Value.Trim();
                return ctx.Facts.TryGetValue(key, out var factValue) ? factValue ?? string.Empty : string.Empty;
            },
            RegexOptions.IgnoreCase);
    }
    // Bucle de Function Calling

    private async Task<AgentLoopOutcome> RunAgentLoopAsync(
        AgentConfig config,
        Guid conversationId,
        List<ChatMessage> messages,
        AgentTurnExecution turn,
        AgentToolContext toolCtx,
        PromptCompositionInput compositionInput,
        CancellationToken ct)
    {
        var currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
        var scopedTools = AgentTurnToolScope.Resolve(config, toolCtx, compositionInput.EnabledTools, currentStage);
        var toolDefinitions = BuildToolDefinitions(config, scopedTools);
        var lastStageId = currentStage?.Id;
        turn.RecordPromptTrace(0, lastStageId, messages[0].Content ?? string.Empty, scopedTools.Select(t => t.Name).ToList());

        var recoveredEmptyToolTurn = false;
        var recoveredToollessMutationTurn = false;
        var stateMutationRanThisTurn = false;
        ToolRepairDirective? pendingToolRepair = null;
        var stageChangeToolBudget = 0;

        var initialEntryActionFragmentRevision = turn.FragmentRevision;
        var initialEntryActions = await TryRunStageEntryActionsUntilStableAsync(
            config,
            conversationId,
            messages,
            turn,
            toolCtx,
            compositionInput,
            lastStageId,
            ct,
            onlyImmediateActions: true);
        if (initialEntryActions.RanAny)
        {
            if (turn.HasTurnCompletingFragmentSince(initialEntryActionFragmentRevision))
                return AgentLoopOutcome.Completed(string.Empty);

            if (RefreshSystemPromptIfStageChanged(config, messages, compositionInput, turn, 0, ref lastStageId))
                stageChangeToolBudget = Math.Max(stageChangeToolBudget, 1);

            currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
            scopedTools = AgentTurnToolScope.Resolve(config, toolCtx, compositionInput.EnabledTools, currentStage);
            toolDefinitions = BuildToolDefinitions(config, scopedTools);
            lastStageId = currentStage?.Id;
        }

        for (int iteration = 0; iteration <= config.MaxToolIterations || stageChangeToolBudget > 0; iteration++)
        {
            var beyondConfiguredLimit = iteration >= config.MaxToolIterations;
            var forceText = beyondConfiguredLimit && stageChangeToolBudget <= 0;
            if (beyondConfiguredLimit && !forceText)
            {
                stageChangeToolBudget--;
                _logger.LogInformation(
                    "Conv {ConvId}: allowing one more tool iteration after valid stage transition",
                    conversationId);
            }
            else if (forceText)
            {
                _logger.LogWarning("Conv {ConvId}: MaxToolIterations reached - forcing text response", conversationId);
            }

            var options = BuildOptions(config, forceText);
            var result  = await _chatClient.CompleteAsync(
                messages, forceText ? null : toolDefinitions, options, ct);

            turn.AddTokens(result.PromptTokens, result.CompletionTokens);
            turn.RecordLlmTrace(iteration, lastStageId, result);

            if (!result.Success)
                return AgentLoopOutcome.Failed($"LLM error: {result.ErrorMessage}");

            if (IsFinalAnswer(result.FinishReason))
            {
                var response = SanitizeResponse(result.Content ?? string.Empty);

                if (ShouldRecoverEmptyToolTurn(response, turn, forceText, recoveredEmptyToolTurn))
                {
                    recoveredEmptyToolTurn = true;
                    _logger.LogWarning(
                        "Conv {ConvId}: empty final response after tool calls - requesting textual continuation",
                        conversationId);

                    messages.Add(ChatMessage.System(
                        "La respuesta final al cliente no puede estar vacia. Continua el flujo con una accion concreta: " +
                        "si falta informacion, pide solo el siguiente dato; si una herramienta entrego datos listos, " +
                        "usa la siguiente herramienta permitida o resume el resultado al cliente."));

                    continue;
                }

                if (ShouldRecoverToollessMutationTurn(
                        response,
                        turn,
                        forceText,
                        recoveredToollessMutationTurn,
                        scopedTools,
                        toolCtx,
                        currentStage,
                        config))
                {
                    recoveredToollessMutationTurn = true;
                    _logger.LogWarning(
                        "Conv {ConvId}: final response claimed state changes without tool calls - forcing tool use",
                        conversationId);

                    messages.Add(ChatMessage.System(BuildToollessRecoveryInstruction(currentStage, scopedTools)));

                    continue;
                }

                if (!forceText)
                {
                    var pendingEntryActionFragmentRevision = turn.FragmentRevision;
                    var pendingEntryActions = await TryRunStageEntryActionsUntilStableAsync(
                        config,
                        conversationId,
                        messages,
                        turn,
                        toolCtx,
                        compositionInput,
                        lastStageId,
                        ct,
                        includeGlobalActions: false,
                        onlyImmediateActions: currentStage is not null && MentionsCollectableFactValue(config, currentStage, toolCtx.LatestUserMessage));
                    if (pendingEntryActions.RanAny)
                    {
                        if (turn.HasTurnCompletingFragmentSince(pendingEntryActionFragmentRevision))
                            return AgentLoopOutcome.Completed(response);
                        stageChangeToolBudget = Math.Max(stageChangeToolBudget, 1);
                        if (RefreshSystemPromptIfStageChanged(config, messages, compositionInput, turn, iteration + 1, ref lastStageId))
                            stageChangeToolBudget = Math.Max(stageChangeToolBudget, 1);

                        currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
                        scopedTools = AgentTurnToolScope.Resolve(config, toolCtx, compositionInput.EnabledTools, currentStage);
                        toolDefinitions = BuildToolDefinitions(config, scopedTools);
                        lastStageId = currentStage?.Id;
                        continue;
                    }
                }

                return AgentLoopOutcome.Completed(response);
            }

            // FinishReason=ToolCalls: ejecutar y acumular resultados
            messages.Add(result.AssistantMessage);
            var userOutput = UserOutputCompletionState.None;

            var originalToolCalls = result.ToolCalls.ToList();
            var toolCalls = OrderStateMutationToolCallsFirst(originalToolCalls, scopedTools);
            if (!ToolCallOrderMatches(originalToolCalls, toolCalls))
            {
                _logger.LogInformation(
                    "Conv {ConvId}: executing state mutation tools before dependent tools in the same LLM batch",
                    conversationId);
            }

            var stateMutationRanInBatch = false;
            var stateMutationChangedStageInBatch = false;
            for (var toolCallIndex = 0; toolCallIndex < toolCalls.Count; toolCallIndex++)
            {
                var toolCall = toolCalls[toolCallIndex];
                toolCtx.CurrentToolIteration = iteration;
                if (pendingToolRepair is { } repairDirective && ShouldBlockUntilToolRepair(repairDirective, toolCall))
                {
                    var repairResult = BuildToolRepairRequiredResult(repairDirective);
                    messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, repairResult));
                    turn.RecordToolTrace(iteration, lastStageId, toolCall, repairResult);
                    _logger.LogInformation(
                        "Conv {ConvId}: blocked tool {Tool} until recoverable tool repair is completed",
                        conversationId,
                        toolCall.FunctionName);
                    continue;
                }

                var unsupportedUserFactResult = ToolArgumentFactGuard.BuildUnsupportedUserFactResult(
                    config,
                    currentStage,
                    toolCall.FunctionName,
                    toolCall.ArgumentsJson,
                    toolCtx,
                    scopedTools);
                if (unsupportedUserFactResult is not null)
                {
                    messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, unsupportedUserFactResult));
                    turn.RecordToolTrace(iteration, lastStageId, toolCall, unsupportedUserFactResult);
                    _logger.LogInformation(
                        "Conv {ConvId}: blocked tool {Tool} because an argument contained an unsupported user fact",
                        conversationId,
                        toolCall.FunctionName);
                    continue;
                }
                if (ShouldDeferCommitToolUntilFactCapture(config, currentStage, scopedTools, toolCall, toolCtx, stateMutationRanInBatch || stateMutationRanThisTurn))
                {
                    var deferredResult = BuildDeferredFactCaptureToolResult();
                    messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, deferredResult));
                    turn.RecordToolTrace(iteration, lastStageId, toolCall, deferredResult);
                    _logger.LogInformation(
                        "Conv {ConvId}: deferred tool {Tool} until latest user fact changes are captured",
                        conversationId,
                        toolCall.FunctionName);
                    continue;
                }

                var outboundCountBeforeTool = turn.OutboundMessages.Count;

                var fragmentCountBeforeTool = turn.FragmentEntries.Count;
                var fragmentRevisionBeforeTool = turn.FragmentRevision;
                var factsBeforeTool = SnapshotFacts(toolCtx);
                var outcome = await ExecuteToolCallAsync(toolCall, toolCtx, ct);
                IReadOnlyList<string> changedFactKeys = [];
                IReadOnlyList<string> invalidatingFactKeys = [];
                if (!outcome.IsError)
                {
                    var factChanges = GetFactChanges(factsBeforeTool, toolCtx.Facts);
                    changedFactKeys = factChanges.ChangedFactKeys;
                    invalidatingFactKeys = factChanges.InvalidatingFactKeys;
                    await ApplyReentryInvalidationAsync(toolCtx, invalidatingFactKeys, ct);
                }
                await ApplyAfterToolRulesAsync(config, toolCall, outcome, toolCtx, ct, lastStageId);
                await DispatchToolEffectNotificationsAsync(config, outcome, toolCtx, ct);

                turn.RecordToolOutcome(outcome);
                var completedPendingRepair = ToolMatchesRepair(toolCall, pendingToolRepair);
                if (TryCreateToolRepairDirective(outcome, out var repair))
                    pendingToolRepair = repair;
                else if (completedPendingRepair)
                    pendingToolRepair = null;

                if (!outcome.IsError && IsStateMutationToolCall(toolCall, scopedTools))
                {
                    stateMutationRanInBatch = true;
                    stateMutationRanThisTurn = true;
                }

                var llmVisibleToolResult = BuildLlmVisibleToolResult(outcome.RawJson);
                turn.RecordToolTrace(iteration, lastStageId, toolCall, llmVisibleToolResult);
                messages.Add(ChatMessage.Tool(
                    toolCall.Id,
                    toolCall.FunctionName,
                    llmVisibleToolResult));

                if (turn.OutboundMessages.Count > outboundCountBeforeTool)
                    userOutput = userOutput with { OutboundMessagesQueued = true };

                if (turn.HasTurnCompletingFragmentSince(fragmentRevisionBeforeTool))
                {
                    userOutput = userOutput with { FragmentQueued = true };
                    _logger.LogInformation(
                        "Conv {ConvId}: turn-completing fragment output queued; waiting for next customer turn",
                        conversationId);
                    break;
                }

                if (HasStageChanged(config, toolCtx, lastStageId) && turn.FragmentEntries.Count > fragmentCountBeforeTool)
                {
                    userOutput = userOutput with { FragmentQueued = true };
                    lastStageId = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx)?.Id;
                    _logger.LogInformation(
                        "Conv {ConvId}: stage advanced to '{Stage}' after fragment output; waiting for next customer turn",
                        conversationId,
                        lastStageId ?? "(none)");
                    break;
                }

                if (HasStageChanged(config, toolCtx, lastStageId))
                {
                    var nextStageId = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx)?.Id;
                    if (stateMutationRanInBatch)
                        stateMutationChangedStageInBatch = true;
                    var staleCount = 0;
                    var replayedFactCount = 0;
                    var factsInvalidatedByCurrentTool = FlowCheckpointInvalidation
                        .GetInvalidations(toolCtx, invalidatingFactKeys)
                        .FactsToClear
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    for (var skippedIndex = toolCallIndex + 1; skippedIndex < toolCalls.Count; skippedIndex++)
                    {
                        var skipped = toolCalls[skippedIndex];
                        if (IsStateMutationToolCall(skipped, scopedTools))
                        {
                            if (TryGetSetFactKey(skipped, out var skippedFactKey)
                                && (factsInvalidatedByCurrentTool.Contains(skippedFactKey)
                                    || changedFactKeys.Contains(skippedFactKey, StringComparer.OrdinalIgnoreCase)))
                            {
                                var staleFactResult = """{"ok":false,"error":"stale_fact_invalidated_by_previous_tool","message":"A previous tool call changed a dependency for this fact. Retry with refreshed context."}""";
                                messages.Add(ChatMessage.Tool(skipped.Id, skipped.FunctionName, staleFactResult));
                                turn.RecordToolTrace(iteration, lastStageId, skipped, staleFactResult);
                                staleCount++;
                                continue;
                            }


                            var skippedFactsBefore = SnapshotFacts(toolCtx);
                            var skippedOutcome = await ExecuteToolCallAsync(skipped, toolCtx, ct);
                            if (!skippedOutcome.IsError)
                            {
                                var skippedFactChanges = GetFactChanges(skippedFactsBefore, toolCtx.Facts);
                                await ApplyReentryInvalidationAsync(toolCtx, skippedFactChanges.InvalidatingFactKeys, ct);
                            }

                            await ApplyAfterToolRulesAsync(config, skipped, skippedOutcome, toolCtx, ct, nextStageId);
                            await DispatchToolEffectNotificationsAsync(config, skippedOutcome, toolCtx, ct);

                            turn.RecordToolOutcome(skippedOutcome);
                            var completedSkippedRepair = ToolMatchesRepair(skipped, pendingToolRepair);
                            if (TryCreateToolRepairDirective(skippedOutcome, out var skippedRepair))
                                pendingToolRepair = skippedRepair;
                            else if (completedSkippedRepair)
                                pendingToolRepair = null;

                            var skippedVisibleResult = BuildLlmVisibleToolResult(skippedOutcome.RawJson);
                            turn.RecordToolTrace(iteration, nextStageId, skipped, skippedVisibleResult);
                            messages.Add(ChatMessage.Tool(
                                skipped.Id,
                                skipped.FunctionName,
                                skippedVisibleResult));
                            replayedFactCount++;
                            continue;
                        }

                        staleCount++;
                        var staleResult = """{"ok":false,"error":"stale_tool_batch_stage_changed","message":"Conversation state changed after a previous tool call. Retry with the refreshed stage context."}""";
                        messages.Add(ChatMessage.Tool(skipped.Id, skipped.FunctionName, staleResult));
                        turn.RecordToolTrace(iteration, lastStageId, skipped, staleResult);
                    }

                    _logger.LogInformation(
                        "Conv {ConvId}: stage changed from '{PreviousStage}' to '{NextStage}' after tool {Tool}; replayed {ReplayedFacts} fact tool calls and skipped {Skipped} stale tool calls for refreshed context",
                        conversationId,
                        lastStageId ?? "(none)",
                        nextStageId ?? "(none)",
                        toolCall.FunctionName,
                        replayedFactCount,
                        staleCount);
                    break;
                }
                if (turn.ShouldAutoEscalate)
                {
                    _logger.LogWarning("Conv {ConvId}: {N} consecutive tool errors - auto-escalating",
                        conversationId, turn.ConsecutiveToolErrors);
                    return AgentLoopOutcome.AutoEscalate("consecutive_tool_errors");
                }
            }
            if (ShouldCompleteTurnForUserOutput(userOutput, turn, conversationId))
                return AgentLoopOutcome.Completed(string.Empty);

            var reconciledEntryActionFragmentRevision = turn.FragmentRevision;
            var reconciledEntryActions = await TryRunStageEntryActionsUntilStableAsync(
                config,
                conversationId,
                messages,
                turn,
                toolCtx,
                compositionInput,
                lastStageId,
                ct,
                includeGlobalActions: false,
                onlyImmediateActions: stateMutationChangedStageInBatch);
            if (reconciledEntryActions.RanAny)
            {
                if (turn.HasTurnCompletingFragmentSince(reconciledEntryActionFragmentRevision))
                    return AgentLoopOutcome.Completed(string.Empty);
                stageChangeToolBudget = Math.Max(stageChangeToolBudget, 1);
                if (turn.ShouldAutoEscalate)
                {
                    _logger.LogWarning("Conv {ConvId}: {N} consecutive tool errors - auto-escalating",
                        conversationId, turn.ConsecutiveToolErrors);
                    return AgentLoopOutcome.AutoEscalate("consecutive_tool_errors");
                }
            }

            if (RefreshSystemPromptIfStageChanged(config, messages, compositionInput, turn, iteration + 1, ref lastStageId))
            {
                stageChangeToolBudget = Math.Max(stageChangeToolBudget, 1);
                currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
                scopedTools = AgentTurnToolScope.Resolve(config, toolCtx, compositionInput.EnabledTools, currentStage);
                toolDefinitions = BuildToolDefinitions(config, scopedTools);
            }
            else if (reconciledEntryActions.RanAny)
            {
                currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
                scopedTools = AgentTurnToolScope.Resolve(config, toolCtx, compositionInput.EnabledTools, currentStage);
                toolDefinitions = BuildToolDefinitions(config, scopedTools);
            }
        }

        return AgentLoopOutcome.Failed("Max iterations exceeded without final response.");
    }

    private async Task<EntryActionReconciliationResult> TryRunStageEntryActionsUntilStableAsync(
        AgentConfig config,
        Guid conversationId,
        List<ChatMessage> messages,
        AgentTurnExecution turn,
        AgentToolContext toolCtx,
        PromptCompositionInput compositionInput,
        string? stageId,
        CancellationToken ct,
        bool includeGlobalActions = true,
        bool onlyImmediateActions = false)
    {
        var ranAny = false;
        var currentStageId = stageId;

        for (var pass = 0; pass < MaxEntryActionReconciliationPasses; pass++)
        {
            var currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
            currentStageId = currentStage?.Id;
            var scopedTools = AgentTurnToolScope.Resolve(config, toolCtx, compositionInput.EnabledTools, currentStage);

            var executed = await TryRunStageEntryActionAsync(
                config,
                conversationId,
                messages,
                turn,
                toolCtx,
                scopedTools,
                currentStageId,
                ct,
                includeGlobalActions,
                onlyImmediateActions);

            if (!executed)
                return new EntryActionReconciliationResult(ranAny, currentStageId);

            ranAny = true;
            if (turn.EscalatedToHuman || turn.RequestCompleted)
                return new EntryActionReconciliationResult(true, currentStageId);
        }

        currentStageId = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx)?.Id;
        _logger.LogWarning(
            "Conv {ConvId}: entry action reconciliation reached {MaxPasses} passes; continuing with current state",
            conversationId,
            MaxEntryActionReconciliationPasses);
        return new EntryActionReconciliationResult(true, currentStageId);
    }

    private readonly record struct EntryActionReconciliationResult(bool RanAny, string? StageId);
    private async Task<bool> TryRunStageEntryActionAsync(
        AgentConfig config,
        Guid conversationId,
        List<ChatMessage> messages,
        AgentTurnExecution turn,
        AgentToolContext toolCtx,
        IReadOnlyList<IAgentTool> scopedTools,
        string? stageId,
        CancellationToken ct,
        bool includeGlobalActions = true,
        bool onlyImmediateActions = false)
    {
        if (includeGlobalActions)
        {
            foreach (var globalAction in AgentTurnToolScope.OrderedGlobalActions(config))
            {
                var executed = await TryRunGlobalEntryActionAsync(
                    config,
                    conversationId,
                    messages,
                    turn,
                    toolCtx,
                    scopedTools,
                    stageId,
                    globalAction,
                    ct,
                    onlyImmediateActions);
                if (executed)
                    return true;
            }
        }

        var stage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx);
        if (stage is null || stage.EntryActions.Count == 0)
            return false;

        foreach (var entryAction in stage.EntryActions)
        {
            if (onlyImmediateActions && !IsImmediateEntryAction(entryAction) && entryAction.When.MessageMatches.Count == 0)
                continue;

            if (HasStageEntryActionRun(toolCtx.ConversationState, stage.Id, entryAction, toolCtx))
                continue;

            var executed = await TryRunEntryActionAsync(
                config,
                conversationId,
                messages,
                turn,
                toolCtx,
                scopedTools,
                stageId,
                entryAction,
                stage.AllowedActions,
                $"stage {stage.Id}",
                ct);
            if (executed)
            {
                MarkStageEntryActionRun(toolCtx.ConversationState, stage.Id, entryAction, toolCtx);
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryRunGlobalEntryActionAsync(
        AgentConfig config,
        Guid conversationId,
        List<ChatMessage> messages,
        AgentTurnExecution turn,
        AgentToolContext toolCtx,
        IReadOnlyList<IAgentTool> scopedTools,
        string? stageId,
        AgentGlobalAction globalAction,
        CancellationToken ct,
        bool onlyImmediateActions)
    {
        if (globalAction.EntryActions.Count == 0)
            return false;

        var scopeId = BuildGlobalEntryActionScopeId(globalAction);
        foreach (var entryAction in globalAction.EntryActions)
        {
            if (onlyImmediateActions && !IsImmediateEntryAction(entryAction) && entryAction.When.MessageMatches.Count == 0)
                continue;

            if (HasStageEntryActionRun(toolCtx.ConversationState, scopeId, entryAction, toolCtx))
                continue;

            var executed = await TryRunEntryActionAsync(
                config,
                conversationId,
                messages,
                turn,
                toolCtx,
                scopedTools,
                stageId,
                entryAction,
                globalAction.AllowedActions,
                $"globalAction {globalAction.Id}",
                ct);
            if (executed)
            {
                MarkStageEntryActionRun(toolCtx.ConversationState, scopeId, entryAction, toolCtx);
                return true;
            }
        }

        return false;
    }

    private static string BuildGlobalEntryActionScopeId(AgentGlobalAction action) =>
        $"global:{(string.IsNullOrWhiteSpace(action.Id) ? "unnamed" : action.Id.Trim())}";

    private static bool IsImmediateEntryAction(StageEntryAction action) =>
        action.When.RequiredFacts.Count == 0
        && action.When.MissingFacts.Count == 0
        && action.When.MissingVerifications.Count == 0
        && !EntryActionArgumentsReferenceFacts(action.Arguments.Values);

    private static bool EntryActionArgumentsReferenceFacts(IEnumerable<JsonElement> arguments) =>
        arguments.Any(EntryActionArgumentReferencesFact);

    private static bool EntryActionArgumentReferencesFact(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => Regex.IsMatch(
                value.GetString() ?? string.Empty,
                @"\{\{(?:fact|facts)\.",
                RegexOptions.IgnoreCase),
            JsonValueKind.Object => value.EnumerateObject().Any(property =>
                EntryActionArgumentReferencesFact(property.Value)),
            JsonValueKind.Array => value.EnumerateArray().Any(EntryActionArgumentReferencesFact),
            _ => false
        };
    }

    private async Task<bool> TryRunEntryActionAsync(
        AgentConfig config,
        Guid conversationId,
        List<ChatMessage> messages,
        AgentTurnExecution turn,
        AgentToolContext toolCtx,
        IReadOnlyList<IAgentTool> scopedTools,
        string? stageId,
        StageEntryAction action,
        IReadOnlyList<string> allowedActions,
        string scopeLabel,
        CancellationToken ct)
    {
        var toolName = action.Tool.Trim();
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        if (!MatchesStageEntryAction(action, toolCtx))
            return false;

        if (allowedActions.Count > 0
            && !allowedActions.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Conv {ConvId}: {Scope} entry action {Tool} is not listed in allowedActions",
                conversationId,
                scopeLabel,
                toolName);
            return false;
        }

        if (!scopedTools.Any(tool => tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
            return false;

        var toolCall = new ToolCallRequest
        {
            Id = $"entry_{Guid.NewGuid():N}",
            FunctionName = toolName,
            ArgumentsJson = BuildEntryActionArgumentsJson(action.Arguments, toolCtx)
        };

        _logger.LogInformation(
            "Conv {ConvId}: executing configured entry action {Tool} for {Scope}",
            conversationId,
            toolName,
            scopeLabel);

        var factsBeforeTool = SnapshotFacts(toolCtx);
        var outcome = await ExecuteToolCallAsync(toolCall, toolCtx, ct);
        if (!outcome.IsError)
        {
            var factChanges = GetFactChanges(factsBeforeTool, toolCtx.Facts);
            await ApplyReentryInvalidationAsync(toolCtx, factChanges.InvalidatingFactKeys, ct);
        }

        await ApplyAfterToolRulesAsync(config, toolCall, outcome, toolCtx, ct, stageId);
        await DispatchToolEffectNotificationsAsync(config, outcome, toolCtx, ct);
        turn.RecordToolOutcome(outcome);
        var llmVisibleToolResult = BuildLlmVisibleToolResult(outcome.RawJson);
        turn.RecordToolTrace(0, stageId, toolCall, llmVisibleToolResult);
        messages.Add(ChatMessage.AssistantWithToolCalls([toolCall]));
        messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, llmVisibleToolResult));

        return !outcome.IsError;
    }

    private static bool MatchesStageEntryAction(StageEntryAction action, AgentToolContext ctx) =>
        StageEntryActionMatcher.Matches(action, ctx);

    private static bool MatchesEntryActionMessage(StageEntryMessageMatch match, string normalizedMessage)
    {
        if (string.IsNullOrWhiteSpace(normalizedMessage))
            return false;

        return match.AnyOf.Any(candidate =>
        {
            var normalizedCandidate = NormalizeIntentText(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
                return false;

            return ContainsNormalizedPhrase(normalizedMessage, normalizedCandidate);
        });
    }

    private static bool ContainsNormalizedPhrase(string normalizedMessage, string normalizedCandidate) =>
        $" {normalizedMessage} ".Contains($" {normalizedCandidate} ", StringComparison.Ordinal);

    private static bool IsMissingFact(AgentToolContext ctx, string factKey) =>
        !ctx.Facts.TryGetValue(factKey, out var value) || string.IsNullOrWhiteSpace(value);

    private static string NormalizeIntentText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray();

        return Regex.Replace(new string(chars).Normalize(System.Text.NormalizationForm.FormC), "\\s+", " ").Trim();
    }
    private bool ShouldCompleteTurnForUserOutput(
        UserOutputCompletionState userOutput,
        AgentTurnExecution turn,
        Guid conversationId)
    {
        if (userOutput.FragmentQueued)
            return true;

        if (!userOutput.OutboundMessagesQueued || turn.OutboundMessages.Count == 0)
            return false;

        if (!turn.DirectOutboundRequested)
        {
            _logger.LogDebug(
                "Conv {ConvId}: salida outbound diferida encolada ({Count} mensajes); continua el LLM para respuesta principal",
                conversationId, turn.OutboundMessages.Count);
            return false;
        }

        _logger.LogInformation(
            "Conv {ConvId}: salida directa al usuario encolada ({Count} mensajes) - turno completo sin texto del LLM",
            conversationId, turn.OutboundMessages.Count);
        return true;
    }

    private readonly record struct UserOutputCompletionState(
        bool OutboundMessagesQueued,
        bool FragmentQueued)
    {
        public static UserOutputCompletionState None { get; } = new(false, false);
    }


    private static List<ToolCallRequest> OrderStateMutationToolCallsFirst(
        IReadOnlyList<ToolCallRequest> toolCalls,
        IReadOnlyList<IAgentTool> scopedTools)
    {
        if (toolCalls.Count <= 1)
            return toolCalls.ToList();

        return toolCalls
            .Select((toolCall, index) => new OrderedToolCall(
                toolCall,
                index,
                IsStateMutationToolCall(toolCall, scopedTools) ? 0 : 1))
            .OrderBy(item => item.Phase)
            .ThenBy(item => item.Index)
            .Select(item => item.ToolCall)
            .ToList();
    }

    private static bool ToolCallOrderMatches(
        IReadOnlyList<ToolCallRequest> original,
        IReadOnlyList<ToolCallRequest> ordered)
    {
        if (original.Count != ordered.Count)
            return false;

        for (var i = 0; i < original.Count; i++)
        {
            if (!ReferenceEquals(original[i], ordered[i]))
                return false;
        }

        return true;
    }

    private static bool IsStateMutationToolCall(
        ToolCallRequest toolCall,
        IReadOnlyList<IAgentTool> scopedTools)
    {
        var tool = scopedTools.FirstOrDefault(candidate =>
            candidate.Name.Equals(toolCall.FunctionName, StringComparison.OrdinalIgnoreCase));

        return tool?.Capabilities.Contains(ToolCapabilities.FactWrite, StringComparer.OrdinalIgnoreCase) == true;
    }


    private static bool ShouldDeferCommitToolUntilFactCapture(
        AgentConfig config,
        AgentFlowStage? currentStage,
        IReadOnlyList<IAgentTool> scopedTools,
        ToolCallRequest toolCall,
        AgentToolContext ctx,
        bool stateMutationRanInBatch)
    {
        if (stateMutationRanInBatch || currentStage is null)
            return false;

        var tool = scopedTools.FirstOrDefault(candidate =>
            candidate.Name.Equals(toolCall.FunctionName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
            return false;

        var latestMessageMentionsStageFact = MentionsCollectableFactValue(config, currentStage, ctx.LatestUserMessage);
        if (!latestMessageMentionsStageFact)
            return false;

        var isCommitTool = tool.Capabilities.Contains(ToolCapabilities.CheckoutPrepare, StringComparer.OrdinalIgnoreCase)
            || tool.Capabilities.Contains(ToolCapabilities.ReservationCreate, StringComparer.OrdinalIgnoreCase);
        if (isCommitTool)
            return true;

        return currentStage.EntryActions.Any(action =>
            action.Tool.Equals(toolCall.FunctionName, StringComparison.OrdinalIgnoreCase)
            && !IsImmediateEntryAction(action));
    }

    private static string BuildDeferredFactCaptureToolResult() =>
        ToolResultHelper.ErrorWithLlm(
            "state_change_requires_capture",
            "The latest user message appears to change state required by this action. Capture changed facts with an allowed state-writing tool, refresh required checks, then retry this action.",
            new { next_action = "capture_changed_facts_then_retry" },
            recoverable: true);
    private static bool ShouldBlockUntilToolRepair(ToolRepairDirective repair, ToolCallRequest toolCall) =>
        !ToolMatchesRepair(toolCall, repair);

    private static bool ToolMatchesRepair(ToolCallRequest toolCall, ToolRepairDirective? repair) =>
        repair.HasValue && ToolMatchesRepair(toolCall, repair.Value);

    private static bool ToolMatchesRepair(ToolCallRequest toolCall, ToolRepairDirective repair) =>
        toolCall.FunctionName.Equals(repair.RequiredToolName, StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateToolRepairDirective(ToolExecutionOutcome outcome, out ToolRepairDirective repair)
    {
        repair = default;
        if (!outcome.IsRecoverableError)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(outcome.RawJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("llm", out var llm)
                || !llm.TryGetProperty("tool", out var tool)
                || tool.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var requiredToolName = tool.GetString();
            if (string.IsNullOrWhiteSpace(requiredToolName))
                return false;

            repair = new ToolRepairDirective(requiredToolName.Trim(), llm.Clone());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildToolRepairRequiredResult(ToolRepairDirective repair) =>
        JsonSerializer.Serialize(new
        {
            ok = false,
            error = new
            {
                code = "tool_repair_required",
                message = "A previous recoverable tool result requires a specific tool before continuing this batch.",
                recoverable = true
            },
            llm = repair.Llm
        });

    private readonly record struct ToolRepairDirective(string RequiredToolName, JsonElement Llm);
    private readonly record struct OrderedToolCall(ToolCallRequest ToolCall, int Index, int Phase);
    private bool HasStageChanged(AgentConfig config, AgentToolContext toolCtx, string? lastStageId)
    {
        var currentStageId = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, toolCtx), toolCtx)?.Id;
        return !string.Equals(currentStageId, lastStageId, StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>
    /// Tras ejecutar tools, la etapa activa puede haber avanzado. Recompone el system prompt
    /// para que el LLM reciba goal, guia conversacional y herramientas del turno actualizadas en la siguiente iteracion.
    /// </summary>
    private bool RefreshSystemPromptIfStageChanged(
        AgentConfig config,
        List<ChatMessage> messages,
        PromptCompositionInput compositionInput,
        AgentTurnExecution turn,
        int nextIteration,
        ref string? lastStageId)
    {
        var session = compositionInput.Session;
        var flow = session is null ? config.Flow : ActiveFlowResolver.Resolve(config, session);
        var currentStageId = _flowStageDetector.DetectCurrentStage(flow, session)?.Id;
        if (string.Equals(currentStageId, lastStageId, StringComparison.OrdinalIgnoreCase))
            return false;

        lastStageId = currentStageId;
        var systemPrompt = _promptComposer.Compose(compositionInput);
        messages[0] = ChatMessage.System(systemPrompt);
        if (compositionInput.Session is not null)
        {
            var currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, compositionInput.Session), compositionInput.Session);
            var scopedTools = AgentTurnToolScope.Resolve(config, compositionInput.Session, compositionInput.EnabledTools, currentStage);
            turn.RecordPromptTrace(nextIteration, currentStageId, systemPrompt, scopedTools.Select(t => t.Name).ToList());
        }
        else
        {
            turn.RecordPromptTrace(nextIteration, currentStageId, systemPrompt, []);
        }

        _logger.LogDebug(
            "Conv: stage changed to '{Stage}' - system prompt refreshed",
            currentStageId ?? "(none)");

        return true;
    }

    private async Task<ToolExecutionOutcome> ExecuteToolCallAsync(
        ToolCallRequest toolCall, AgentToolContext ctx, CancellationToken ct)
    {
        _logger.LogInformation("Conv {ConvId}: tool_call [{Id}] {Name}({Args})",
            ctx.ConversationId, toolCall.Id, toolCall.FunctionName, toolCall.ArgumentsJson);

        var tool = _toolRegistry.Resolve(toolCall.FunctionName);

        if (tool is null)
        {
            var notFound = ToolResultHelper.ErrorWithNextAction(
                "tool_not_found",
                $"Tool '{toolCall.FunctionName}' is not registered.",
                "select_available_tool",
                new { tool = toolCall.FunctionName });
            return ToolExecutionOutcome.Parse(notFound);
        }

        try
        {
            using var argsDoc = JsonDocument.Parse(toolCall.ArgumentsJson);

            var gate = await _toolCapabilityGate.EvaluateAsync(tool, argsDoc.RootElement, ctx, ct);
            if (!gate.IsAllowed)
            {
                _logger.LogInformation(
                    "Conv {ConvId}: tool {Name} blocked by gate ({Code}): {Reason}",
                    ctx.ConversationId, toolCall.FunctionName, gate.Code, gate.Reason);

                var gateResult = gate.Llm is null
                    ? ToolResultHelper.Error(gate.Code!, gate.Reason!)
                    : ToolResultHelper.ErrorWithLlm(gate.Code!, gate.Reason!, gate.Llm, recoverable: true);

                return ToolExecutionOutcome.Parse(gateResult);
            }
            var rawJson = await tool.ExecuteAsync(argsDoc.RootElement, ctx, ct);
            var outcome = ToolExecutionOutcome.Parse(rawJson);

            _logger.LogInformation("Conv {ConvId}: tool_result [{Id}] {Name} -> {Result}",
                ctx.ConversationId, toolCall.Id, toolCall.FunctionName,
                rawJson.Length > 200 ? rawJson[..200] + "..." : rawJson);

            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conv {ConvId}: tool {Name} threw exception",
                ctx.ConversationId, toolCall.FunctionName);

            return ToolExecutionOutcome.Parse(
                ToolResultHelper.ErrorWithNextAction(
                    "tool_exception",
                    ex.Message,
                    "human_handoff",
                    new { tool = toolCall.FunctionName }));
        }
    }

    // Persistencia y escalacion

    private static string BuildLlmVisibleToolResult(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                return rawJson;

            if (!root.TryGetProperty("llm", out var llm))
                return rawJson;

            return llm.ValueKind switch
            {
                JsonValueKind.Object or JsonValueKind.Array => $"{{\"ok\":true,\"data\":{llm.GetRawText()}}}",
                JsonValueKind.String => $"{{\"ok\":true,\"message\":{llm.GetRawText()}}}",
                _ => rawJson
            };
        }
        catch
        {
            return rawJson;
        }
    }

    private async Task ApplyAfterToolRulesAsync(
        AgentConfig config,
        ToolCallRequest toolCall,
        ToolExecutionOutcome outcome,
        AgentToolContext ctx,
        CancellationToken ct,
        string? stageId = null)
    {
        if (outcome.IsError)
            return;

        var currentStage = ResolveAfterToolStage(config, ctx, stageId);
        if (currentStage is null || currentStage.AfterTool.Count == 0)
            return;

        foreach (var rule in currentStage.AfterTool)
        {
            if (!rule.Tool.Equals(toolCall.FunctionName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsAfterToolRuleMatch(outcome.RawJson, rule.When))
                continue;

            await EnqueueAfterToolSequenceAsync(config, currentStage, rule, ctx, ct);

            var factActions = ResolveAfterToolFactActions(config, rule, outcome.RawJson, ctx).ToList();
            await ApplyReentryInvalidationAsync(
                ctx,
                GetInvalidatingFactKeys(ctx.Facts, factActions.Select(action =>
                    new KeyValuePair<string, string>(action.Key, action.Value))),
                ct);

            foreach (var action in factActions)
            {
                await _factsService.SetAsync(
                    ctx.ConversationId,
                    ctx.BusinessId,
                    action.Key,
                    action.Value,
                    action.RememberAcrossRequests,
                    ct);

                ctx.Facts[action.Key] = action.Value;

                _logger.LogInformation(
                    "Conv {ConvId}: afterTool rule on stage '{Stage}' set {Key}={Value}",
                    ctx.ConversationId,
                    currentStage.Id,
                    action.Key,
                    action.Value);
            }
        }
    }

    private AgentFlowStage? ResolveAfterToolStage(AgentConfig config, AgentToolContext ctx, string? stageId)
    {
        if (!string.IsNullOrWhiteSpace(stageId))
        {
            var activeFlow = ActiveFlowResolver.Resolve(config, ctx);
            var configuredStage = activeFlow.Stages.FirstOrDefault(stage =>
                stage.Id.Equals(stageId, StringComparison.OrdinalIgnoreCase));
            if (configuredStage is not null)
                return configuredStage;
        }

        return _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, ctx), ctx);
    }

    private async Task ApplyReentryInvalidationAsync(
        AgentToolContext ctx,
        IReadOnlyCollection<string> changedFactKeys,
        CancellationToken ct)
    {
        if (changedFactKeys.Count == 0)
            return;

        var invalidation = FlowCheckpointInvalidation.GetInvalidations(ctx, changedFactKeys);
        foreach (var stageId in invalidation.StageSnapshotsToReset)
        {
            ctx.ConversationState.StageFactSnapshots.Remove(stageId);
            ClearStageEntryActionRuns(ctx.ConversationState, stageId);
        }

        IReadOnlyList<string> cleared = [];
        if (invalidation.FactsToClear.Count > 0)
        {
            cleared = await _factsService.ClearFieldsAsync(ctx.ConversationId, invalidation.FactsToClear, ct);
            foreach (var factKey in cleared)
                ctx.Facts.Remove(factKey);
        }

        if (HasStaleVerification(ctx, VerificationFactTypes.CheckoutPrepared))
            ctx.ActivePayment = null;

        if (cleared.Count > 0 || invalidation.StageSnapshotsToReset.Count > 0)
        {
            _logger.LogInformation(
                "Conv {ConvId}: reentry invalidation cleared derived checkpoints [{Facts}] and stage snapshots [{Stages}] after changes [{ChangedFacts}]",
                ctx.ConversationId,
                string.Join(", ", cleared),
                string.Join(", ", invalidation.StageSnapshotsToReset),
                string.Join(", ", changedFactKeys));
        }
    }

    private static bool TryGetSetFactKey(ToolCallRequest toolCall, out string key)
    {
        key = string.Empty;
        if (!toolCall.FunctionName.Equals("set_fact", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
            if (!doc.RootElement.TryGetProperty("key", out var keyElement)
                || keyElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            key = keyElement.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(key);
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private static Dictionary<string, string> SnapshotFacts(AgentToolContext ctx) =>
        new(ctx.Facts, StringComparer.OrdinalIgnoreCase);

    private const string StageEntryActionRunsKey = "__entry_action_runs";

    private static bool HasStageEntryActionRun(ConversationState state, string stageId, StageEntryAction action, AgentToolContext ctx) =>
        state.StageFactSnapshots.TryGetValue(StageEntryActionRunsKey, out var runs)
        && runs.ContainsKey(BuildStageEntryActionRunKey(stageId, action, ctx));

    private static void MarkStageEntryActionRun(ConversationState state, string stageId, StageEntryAction action, AgentToolContext ctx)
    {
        if (!state.StageFactSnapshots.TryGetValue(StageEntryActionRunsKey, out var runs))
        {
            runs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            state.StageFactSnapshots[StageEntryActionRunsKey] = runs;
        }

        runs[BuildStageEntryActionRunKey(stageId, action, ctx)] = DateTime.UtcNow.ToString("O");
    }

    private static void ClearStageEntryActionRuns(ConversationState state, string stageId)
    {
        if (!state.StageFactSnapshots.TryGetValue(StageEntryActionRunsKey, out var runs))
            return;

        var prefix = NormalizeStageEntryActionKeyPart(stageId) + "::";
        var keys = runs.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keys)
            runs.Remove(key);

        if (runs.Count == 0)
            state.StageFactSnapshots.Remove(StageEntryActionRunsKey);
    }

    private static string BuildStageEntryActionRunKey(string stageId, StageEntryAction action, AgentToolContext ctx)
    {
        var argsSignature = string.Join("&", action.Arguments
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
                $"{NormalizeStageEntryActionKeyPart(pair.Key)}={JsonSerializer.Serialize(pair.Value)}"));
        var conditionSignature = BuildStageEntryActionConditionSignature(action.When, ctx);

        return string.Join("::",
            NormalizeStageEntryActionKeyPart(stageId),
            NormalizeStageEntryActionKeyPart(action.Tool),
            argsSignature,
            conditionSignature);
    }

    private static string BuildStageEntryActionConditionSignature(StageEntryActionCondition condition, AgentToolContext ctx)
    {
        var parts = new List<string>();
        parts.AddRange(condition.RequiredFacts
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => $"rf:{NormalizeStageEntryActionKeyPart(key)}={NormalizeStageEntryActionKeyPart(ReadFact(ctx.Facts, key))}"));
        parts.AddRange(condition.MissingFacts
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => $"mf:{NormalizeStageEntryActionKeyPart(key)}={(IsMissingFact(ctx.Facts, key) ? "missing" : "present")}"));
        parts.AddRange(condition.MissingVerifications
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => $"mv:{NormalizeStageEntryActionKeyPart(key)}={(IsMissingVerification(ctx, key) ? "missing" : "active")}"));
        if (condition.MessageMatches.Count > 0)
            parts.Add($"msg:{NormalizeStageEntryActionKeyPart(ctx.LatestUserMessage)}");

        return string.Join("&", parts);
    }

    private static string ReadFact(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var value) ? value : string.Empty;

    private static bool IsMissingFact(IReadOnlyDictionary<string, string> facts, string key) =>
        !facts.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value);

    private static bool IsMissingVerification(AgentToolContext ctx, string verificationType)
    {
        if (ctx.ConversationState is null || string.IsNullOrWhiteSpace(verificationType))
            return true;

        if (!ctx.ConversationState.Verifications.TryGetValue(verificationType, out var entry))
            return true;

        if (entry.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return true;

        return !VerificationSnapshot.Matches(entry.PayloadJson, ctx.Facts);
    }

    private static string NormalizeStageEntryActionKeyPart(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static FactChangeSet GetFactChanges(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var keys = before.Keys
            .Concat(after.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var changed = new List<string>();
        var invalidating = new List<string>();
        foreach (var key in keys)
        {
            before.TryGetValue(key, out var previous);
            after.TryGetValue(key, out var current);
            if (FactValuesEqual(previous, current))
                continue;

            changed.Add(key);
            if (HasFactValue(previous))
                invalidating.Add(key);
        }

        return new FactChangeSet(changed, invalidating);
    }

    private static List<string> GetInvalidatingFactKeys(
        IReadOnlyDictionary<string, string> currentFacts,
        IEnumerable<KeyValuePair<string, string>> pendingFacts)
    {
        var invalidating = new List<string>();
        foreach (var pending in pendingFacts)
        {
            currentFacts.TryGetValue(pending.Key, out var previous);
            if (HasFactValue(previous) && !FactValuesEqual(previous, pending.Value))
                invalidating.Add(pending.Key);
        }

        return invalidating;
    }

    private static bool FactValuesEqual(string? left, string? right) =>
        string.Equals(NormalizeFactValue(left), NormalizeFactValue(right), StringComparison.OrdinalIgnoreCase);

    private static bool HasFactValue(string? value) =>
        !string.IsNullOrWhiteSpace(NormalizeFactValue(value));

    private static string? NormalizeFactValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FactChangeSet(
        IReadOnlyList<string> ChangedFactKeys,
        IReadOnlyList<string> InvalidatingFactKeys);

    private readonly record struct AfterToolFactAction(
        string Key,
        string Value,
        bool RememberAcrossRequests);

    private static IEnumerable<AfterToolFactAction> ResolveAfterToolFactActions(
        AgentConfig config,
        MimosBabySpa.Application.Agents.Configuration.StageAfterToolRule rule,
        string outcomeJson,
        AgentToolContext ctx)
    {
        foreach (var (key, valueTemplate) in EnumerateAfterToolFactActions(rule))
        {
            var value = ResolveAfterToolValue(outcomeJson, valueTemplate);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (ctx.Facts.TryGetValue(key, out var existing)
                && string.Equals(existing, value, StringComparison.Ordinal))
            {
                continue;
            }

            var schemaEntry = config.FactSchema.FirstOrDefault(e =>
                e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

            yield return new AfterToolFactAction(
                key,
                value,
                schemaEntry?.ShouldRememberAcrossRequests() ?? false);
        }
    }

    private async Task EnqueueAfterToolSequenceAsync(
        AgentConfig config,
        AgentFlowStage currentStage,
        MimosBabySpa.Application.Agents.Configuration.StageAfterToolRule rule,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        var sequenceName = rule.SendMessageSequence?.Trim();
        if (string.IsNullOrWhiteSpace(sequenceName))
            return;

        if (ctx.Turn is null)
        {
            _logger.LogWarning(
                "Conv {ConvId}: afterTool sequence '{Sequence}' skipped because turn context is unavailable",
                ctx.ConversationId,
                sequenceName);
            return;
        }

        if (!config.MessageSequences.ContainsKey(sequenceName))
        {
            _logger.LogWarning(
                "Conv {ConvId}: afterTool sequence '{Sequence}' on stage '{Stage}' is not configured",
                ctx.ConversationId,
                sequenceName,
                currentStage.Id);
            return;
        }

        var sentFactKey = BuildSequenceSentFactKey(sequenceName);
        if (rule.SendOncePerConversation
            && ctx.Facts.TryGetValue(sentFactKey, out var sent)
            && string.Equals(sent, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Conv {ConvId}: afterTool sequence '{Sequence}' on stage '{Stage}' skipped because it was already sent",
                ctx.ConversationId,
                sequenceName,
                currentStage.Id);
            return;
        }

        if (!ctx.Turn.TryMarkSequenceEnqueued(sequenceName))
            return;

        var messages = await _sequenceResolver.ResolveAsync(
            ctx.BusinessId,
            sequenceName,
            config.MessageSequences,
            new MessageSequenceContext { Custom = ctx.Facts },
            ct);

        if (messages.Count == 0)
        {
            _logger.LogWarning(
                "Conv {ConvId}: afterTool sequence '{Sequence}' on stage '{Stage}' resolved to zero messages",
                ctx.ConversationId,
                sequenceName,
                currentStage.Id);
            return;
        }

        ctx.Turn.EnqueueOutbound(messages);

        if (rule.SendOncePerConversation)
        {
            await _factsService.SetAsync(
                ctx.ConversationId,
                ctx.BusinessId,
                sentFactKey,
                "true",
                rememberAcrossRequests: false,
                ct);

            ctx.Facts[sentFactKey] = "true";
        }

        _logger.LogInformation(
            "Conv {ConvId}: afterTool sequence '{Sequence}' on stage '{Stage}' queued ({Count} messages)",
            ctx.ConversationId,
            sequenceName,
            currentStage.Id,
            messages.Count);
    }

    private static string BuildSequenceSentFactKey(string sequenceName) =>
        $"system.sequence.{sequenceName}.sent";

    private static IEnumerable<KeyValuePair<string, string>> EnumerateAfterToolFactActions(
        MimosBabySpa.Application.Agents.Configuration.StageAfterToolRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.SetFact.Key))
            yield return new KeyValuePair<string, string>(rule.SetFact.Key, rule.SetFact.Value);

        foreach (var pair in rule.SetFacts)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
                yield return pair;
        }
    }

    private static string? ResolveAfterToolValue(string rawJson, string valueTemplate)
    {
        if (string.IsNullOrWhiteSpace(valueTemplate))
            return null;

        var trimmed = valueTemplate.Trim();
        if (!trimmed.StartsWith("{{", StringComparison.Ordinal) || !trimmed.EndsWith("}}", StringComparison.Ordinal))
            return trimmed;

        var path = trimmed[2..^2].Trim();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return TryGetJsonPath(doc.RootElement, path, out var value)
                ? JsonElementToFactValue(value)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAfterToolRuleMatch(
        string rawJson,
        MimosBabySpa.Application.Agents.Configuration.ToolResultCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Path))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!TryGetJsonPath(doc.RootElement, condition.Path, out var value))
                return false;

            if (!string.IsNullOrWhiteSpace(condition.NotExpected)
                && JsonElementEquals(value, condition.NotExpected))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(condition.Expected)
                || JsonElementEquals(value, condition.Expected);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static bool JsonElementEquals(JsonElement value, string? expected)
    {
        if (expected is null)
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.String => string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => string.Equals(value.GetRawText(), expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.True => string.Equals("true", expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.False => string.Equals("false", expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Null => string.Equals("null", expected, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(value.GetRawText(), expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? JsonElementToFactValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };

    private async Task DispatchToolEffectNotificationsAsync(
        AgentConfig config,
        ToolExecutionOutcome outcome,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        if (outcome.IsError || outcome.Events.Count == 0)
            return;

        var notificationEvents = outcome.Events
            .Where(effect => !string.IsNullOrWhiteSpace(effect))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(effect => config.Notifications.TryGetValue(effect, out var notification) && notification.Enabled)
            .ToList();

        var custom = SnapshotFacts(ctx);
        foreach (var eventName in notificationEvents)
        {
            var context = ResolveToolEffectNotificationContext(ctx, eventName, custom);
            await _eventNotificationDispatcher.SendEventAsync(
                ctx.BusinessId,
                config,
                eventName,
                context,
                ct);
        }
    }

    private static MessageSequenceContext ResolveToolEffectNotificationContext(
        AgentToolContext ctx,
        string eventName,
        IReadOnlyDictionary<string, string> custom)
    {
        if (!ctx.NotificationContexts.TryGetValue(eventName, out var context))
            return new MessageSequenceContext { Custom = custom };

        var mergedCustom = new Dictionary<string, string>(custom, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context.Custom)
            mergedCustom[key] = value;

        return context with { Custom = mergedCustom };
    }
    private async Task<AgentTurnResult> FinalizeTurnAsync(
        Guid conversationId,
        string userMessage,
        string botResponse,
        AgentToolContext session,
        AgentTurnExecution turn,
        AgentConfig config,
        IReadOnlyList<IAgentTool> effectiveTools,
        CancellationToken ct)
    {
        var finalResponse = _turnResponseComposer.Compose(
            config,
            effectiveTools,
            botResponse,
            turn.FragmentEntries);

        UpdateStageSnapshots(config, session);
        if (turn.RequestCompleted)
        {
            await _requestContext.CompleteAsync(
                conversationId,
                config,
                session.ConversationState,
                session.Facts,
                ToolSideEffectNames.RequestCompleted,
                ct);
        }
        await PersistCurrentStageNameAsync(config, session, ct);
        await PersistTurnAsync(conversationId, userMessage, finalResponse, session.ConversationState, ct);
        await _usageBilling.ChargeAsync(new UsageChargeRequest(
            config.BusinessId,
            config.AgentId,
            conversationId,
            MessageId: null,
            UsageOperationType.AgentTurn,
            turn.PromptTokens,
            turn.CompletionTokens,
            turn.ToolCallCount,
            turn.OutboundMessages.Count,
            config.Model,
            MetadataJson: JsonSerializer.Serialize(new
            {
                final_response = !string.IsNullOrWhiteSpace(finalResponse),
                escalated = turn.EscalatedToHuman,
                request_completed = turn.RequestCompleted
            })), ct);

        _logger.LogInformation(
            "Conv {ConvId}: turn complete - tokens={Tokens}, tools={Tools}, escalated={Esc}, fragments={Fragments}",
            conversationId, turn.TotalTokens, turn.ToolCallCount, turn.EscalatedToHuman, turn.FragmentEntries.Count);

        return turn.ToSuccessResult(finalResponse);
    }

    private async Task<AgentTurnResult> EscalateAndPersistAsync(
        AgentConfig config,
        AgentToolContext session,
        string userMessage,
        string reason,
        CancellationToken ct)
    {
        var escalateTool = _toolRegistry.ResolveByCapability(ToolCapabilities.HumanEscalate);
        if (escalateTool is not null)
        {
            try
            {
                var argsJson = JsonSerializer.Serialize(new { reason, last_user_message = userMessage });
                using var doc = JsonDocument.Parse(argsJson);
                await escalateTool.ExecuteAsync(doc.RootElement, session, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conv {ConvId}: auto-escalate tool failed", session.ConversationId);
            }
        }

        const string escalateMsg = "Te estamos comunicando con un agente humano. En breve te atenderan.";
        await PersistTurnAsync(session.ConversationId, userMessage, escalateMsg, session.ConversationState, ct);

        await _usageBilling.ChargeAsync(new UsageChargeRequest(
            config.BusinessId,
            config.AgentId,
            session.ConversationId,
            MessageId: null,
            UsageOperationType.AgentTurn,
            ToolCalls: 1,
            OutboundMessages: 1,
            Model: config.Model,
            MetadataJson: JsonSerializer.Serialize(new { escalated = true, reason })), ct);

        return AgentTurnResult.Ok(escalateMsg, escalated: true);
    }

    private async Task PersistTurnAsync(
        Guid conversationId,
        string userMessage,
        string botResponse,
        ConversationState state,
        CancellationToken ct)
    {
        await _messageService.SaveMessageAsync(conversationId, "user", userMessage);
        if (!string.IsNullOrWhiteSpace(botResponse))
            await _messageService.SaveMessageAsync(conversationId, "bot", botResponse);

        state.LastUserMessage = userMessage;
        state.LastBotMessage = botResponse;
        state.ConsecutiveDegradedTurns = 0;

        await _stateManager.SaveStateAsync(conversationId, state, ct);
        await _lifecycleService.TouchActivityAsync(conversationId, userMessage, ct);
    }

    private async Task PersistCurrentStageNameAsync(
        AgentConfig config,
        AgentToolContext session,
        CancellationToken ct)
    {
        var currentStage = _flowStageDetector.DetectCurrentStage(ActiveFlowResolver.Resolve(config, session), session);
        var stageName = ResolveStageName(currentStage);

        if (string.Equals(session.Conversation.CurrentStageName, stageName, StringComparison.Ordinal))
            return;

        session.Conversation.CurrentStageName = stageName;
        await _conversationService.UpdateConversationAsync(session.Conversation, ct);
    }

    private static string? ResolveStageName(AgentFlowStage? stage)
    {
        if (stage is null)
            return null;

        return !string.IsNullOrWhiteSpace(stage.Name)
            ? stage.Name.Trim()
            : stage.Id;
    }

    /// <summary>
    /// Determina el engagement y lo devuelve como key de string para almacenar en facts.
    /// Valores: "firstEver" | "returningCustomer" | "continuingSession"
    /// </summary>
    private async Task<string> ResolveEngagementKeyAsync(
        Guid businessId,
        string userNumber,
        IEnumerable<Domain.Entities.Message> history,
        CancellationToken ct)
    {
        if (history.Any(m =>
                m.Sender.Equals("bot", StringComparison.OrdinalIgnoreCase)
                || m.Sender.Equals("assistant", StringComparison.OrdinalIgnoreCase)))
        {
            return "continuingSession";
        }

        var hasClosed = await _conversationService.HasClosedConversationsAsync(businessId, userNumber, ct);
        return hasClosed ? "returningCustomer" : "firstEver";
    }

    private async Task<AgentToolContext> LoadTurnSessionAsync(
        AgentConfig config,
        ConversationState state,
        Guid conversationId,
        string? channelPhone,
        AgentInboundMetadata? inboundMetadata,
        BusinessClockSnapshot clockSnapshot,
        CancellationToken ct)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(conversationId)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        var resolvedPhone = channelPhone?.Trim() ?? conversation.UserNumber;
        var factRecords = await _factsService.GetAllRecordsAsync(conversationId, ct);
        var mutableFacts = factRecords.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var rollover = await _requestContext.ApplyRetentionAsync(
            conversation,
            config,
            state,
            mutableFacts,
            clockSnapshot,
            ct);

        var reservationSession = await _reservationLifecycle.ResolveForSessionAsync(
            conversationId,
            config.BusinessId,
            resolvedPhone,
            clockSnapshot.Today,
            ct);
        var activePayment = await _paymentLifecycle.GetActiveByConversationAsync(conversationId, ct);

        var durable = await _customerMemory.GetAllRecordsAsync(config.BusinessId, conversation.UserNumber, ct);
        var durableByKey = durable
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in config.FactSchema.Where(e => e.ShouldRememberAcrossRequests()))
        {
            if (!durableByKey.TryGetValue(entry.Key, out var durableValue)
                || string.IsNullOrWhiteSpace(durableValue.Value))
            {
                continue;
            }

            var retention = entry.Retention();
            if (retention.HasValue
                && durableValue.UpdatedAt.Add(retention.Value) < clockSnapshot.Now.UtcDateTime)
            {
                continue;
            }

            if (!mutableFacts.TryGetValue(entry.Key, out var current) || string.IsNullOrWhiteSpace(current))
                mutableFacts[entry.Key] = durableValue.Value;
        }

        // Hidratar facts de fuente=channel/session antes de construir el contexto
        // Nota: el engagement se agrega por separado en ProcessMessageAsync (requiere historia)
        _factHydrator.Hydrate(config.FactSchema, mutableFacts, new FactHydratorContext
        {
            ChannelPhone = resolvedPhone
        });

        var parsedInteractiveAction = InteractivePayloadParser.TryParse(inboundMetadata?.InteractivePayload, out var action)
            ? action
            : null;

        var session = new AgentToolContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = conversationId,
            BusinessToday = clockSnapshot.Today,
            BusinessNow = clockSnapshot.Now,
            BusinessDayRollover = rollover.BusinessDayChanged,
            PreviousBusinessDay = rollover.PreviousBusinessDay,
            RolloverClearedFacts = rollover.ClearedFacts,
            ChannelPhone = resolvedPhone,
            ProviderMessageId = inboundMetadata?.ProviderMessageId,
            ReplyToProviderMessageId = inboundMetadata?.ReplyToProviderMessageId,
            InteractivePayload = inboundMetadata?.InteractivePayload,
            InteractiveAction = parsedInteractiveAction,
            EscalationContacts = config.Escalations.Human.Contacts,
            Config = config,
            ConversationState = state,
            Conversation = conversation,
            Facts = mutableFacts,
            ManageableReservations = reservationSession.ManageableReservations,
            ActivePayment = activePayment
        };

        return session;
    }

    private async Task ApplyInboundFactsAsync(
        AgentConfig config,
        AgentToolContext session,
        IReadOnlyDictionary<string, string>? inboundFacts,
        CancellationToken ct)
    {
        var normalizedFacts = NormalizeInboundFacts(inboundFacts);
        if (normalizedFacts.Count == 0 || config.FactSchema.Count == 0)
            return;

        var roleIndex = new FactRoleIndex(config.FactSchema);
        var changedKeys = new List<string>();
        var conversationIdentityChanged = false;

        foreach (var (rawKey, value) in normalizedFacts)
        {
            var schemaEntry = ResolveInboundFactEntry(config, roleIndex, rawKey);
            if (schemaEntry is null)
            {
                _logger.LogDebug(
                    "Conv {ConvId}: inbound fact '{FactKey}' skipped because it is not defined in the agent fact schema",
                    session.ConversationId,
                    rawKey);
                continue;
            }

            var changed = session.Facts.TryGetValue(schemaEntry.Key, out var existing)
                && HasFactValue(existing)
                && !FactValuesEqual(existing, value);

            await _factsService.SetAsync(
                session.ConversationId,
                session.BusinessId,
                schemaEntry.Key,
                value,
                schemaEntry.ShouldRememberAcrossRequests(),
                ct);

            session.Facts[schemaEntry.Key] = value;
            if (changed)
                changedKeys.Add(schemaEntry.Key);

            conversationIdentityChanged |= ApplyConversationIdentityFact(session, schemaEntry, value);

            _logger.LogInformation(
                "Conv {ConvId}: inbound fact '{RawFactKey}' mapped to '{FactKey}'",
                session.ConversationId,
                rawKey,
                schemaEntry.Key);
        }

        if (conversationIdentityChanged)
            await _conversationService.UpdateConversationAsync(session.Conversation, ct);

        await ApplyReentryInvalidationAsync(session, changedKeys, ct);
    }

    private async Task ApplyCollectFactCapturesAsync(
        AgentConfig config,
        AgentToolContext session,
        string message,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message) || config.FactSchema.Count == 0)
            return;

        var activeFlow = ActiveFlowResolver.Resolve(config, session);
        var currentStage = _flowStageDetector.DetectCurrentStage(activeFlow, session);
        if (currentStage is null)
            return;

        var candidateKeys = currentStage.Collect
            .Concat(currentStage.AdvanceWhenFacts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidateKeys.Count == 0)
            return;

        var changedKeys = new List<string>();
        var conversationIdentityChanged = false;

        foreach (var entry in config.FactSchema.Where(entry => candidateKeys.Contains(entry.Key)))
        {
            if (!CanCaptureDeterministically(entry))
                continue;

            if (!TryExtractDeterministicFactValue(entry, message, session.BusinessToday, out var value))
                continue;

            var changed = session.Facts.TryGetValue(entry.Key, out var existing)
                && HasFactValue(existing)
                && !FactValuesEqual(existing, value);

            if (session.Facts.TryGetValue(entry.Key, out existing)
                && FactValuesEqual(existing, value))
            {
                continue;
            }

            await _factsService.SetAsync(
                session.ConversationId,
                session.BusinessId,
                entry.Key,
                value,
                entry.ShouldRememberAcrossRequests(),
                ct);

            session.Facts[entry.Key] = value;
            if (changed)
                changedKeys.Add(entry.Key);

            conversationIdentityChanged |= ApplyConversationIdentityFact(session, entry, value);

            _logger.LogInformation(
                "Conv {ConvId}: collect fact '{FactKey}' captured deterministically from latest user message",
                session.ConversationId,
                entry.Key);
        }

        if (conversationIdentityChanged)
            await _conversationService.UpdateConversationAsync(session.Conversation, ct);

        await ApplyReentryInvalidationAsync(session, changedKeys, ct);
    }

    private static bool CanCaptureDeterministically(FactSchemaEntry entry)
    {
        if (!entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            return false;

        if (entry.ValueSource is not null
            && (entry.ValueSource.Equals("catalog", StringComparison.OrdinalIgnoreCase)
                || entry.ValueSource.Equals("tool", StringComparison.OrdinalIgnoreCase)
                || entry.ValueSource.Equals("external", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var type = entry.Type.Trim().ToLowerInvariant();
        return type is "phone" or "email"
            || (type == "boolean" && entry.Aliases.Count > 0);
    }

    internal static bool TryExtractDeterministicFactValue(
        FactSchemaEntry entry,
        string message,
        DateOnly businessToday,
        out string value)
    {
        value = string.Empty;
        return entry.Type.Trim().ToLowerInvariant() switch
        {
            "email" => TryExtractEmail(message, out value),
            "phone" => TryExtractPhone(message, out value),
            "boolean" => TryExtractBooleanAlias(entry, message, out value),
            _ => false
        };
    }

    private static bool TryExtractBooleanAlias(FactSchemaEntry entry, string message, out string value)
    {
        value = string.Empty;
        if (entry.Aliases.Count == 0)
            return false;

        var normalizedMessage = NormalizeIntentText(message);
        if (string.IsNullOrWhiteSpace(normalizedMessage))
            return false;

        foreach (var alias in entry.Aliases)
        {
            var normalizedAlias = NormalizeIntentText(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
                continue;

            if (!ContainsNormalizedPhrase(normalizedMessage, normalizedAlias))
                continue;

            value = bool.TrueString.ToLowerInvariant();
            return true;
        }

        return false;
    }

    private static bool TryExtractEmail(string message, out string value)
    {
        value = string.Empty;
        var match = Regex.Match(
            message,
            @"(?<!\S)[^\s@]+@[^\s@]+\.[^\s@.,;:!?]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        value = match.Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        return true;
    }

    private static bool TryExtractPhone(string message, out string value)
    {
        value = string.Empty;
        var match = Regex.Match(
            message,
            @"(?<!\d)(?:\+?\d[\d\s().-]{6,}\d)(?!\d)",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        if (digits.Length < 7)
            return false;

        value = match.Value.Trim();
        return true;
    }

    private static bool ApplyConversationIdentityFact(
        AgentToolContext session,
        FactSchemaEntry entry,
        string value)
    {
        if (IsFactRoleOrKey(entry, "customer.name", ConversationFactKeys.CustomerName))
        {
            session.Conversation.CustomerName = value;
            return true;
        }

        if (IsFactRoleOrKey(entry, "customer.email", ConversationFactKeys.CustomerEmail))
        {
            session.Conversation.CustomerEmail = value;
            return true;
        }

        return false;
    }

    private static bool IsFactRoleOrKey(FactSchemaEntry entry, string role, string key) =>
        entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Role, role, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> NormalizeInboundFacts(
        IReadOnlyDictionary<string, string>? facts)
    {
        if (facts is null || facts.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return facts
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static FactSchemaEntry? ResolveInboundFactEntry(
        AgentConfig config,
        FactRoleIndex roleIndex,
        string rawKey)
    {
        var direct = roleIndex.EntryFor(rawKey.Trim());
        if (direct is not null)
            return direct;

        var lookup = NormalizeFactLookupToken(rawKey);
        if (string.IsNullOrWhiteSpace(lookup))
            return null;

        return config.FactSchema.FirstOrDefault(entry =>
            MatchesFactToken(entry.Key, lookup)
            || MatchesFactToken(entry.Role, lookup)
            || MatchesFactToken(entry.Label, lookup)
            || entry.Aliases.Any(alias => MatchesFactToken(alias, lookup)));
    }

    private static bool MatchesFactToken(string? candidate, string lookup) =>
        !string.IsNullOrWhiteSpace(candidate)
        && NormalizeFactLookupToken(candidate).Equals(lookup, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFactLookupToken(string value)
    {
        var normalized = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
    /// <summary>
    /// Para cada etapa que tenga SkipWhen satisfecho, aplica AutoSetOnSkip
    /// solo si el fact aun no tiene valor (el dato de usuario tiene precedencia).
    /// </summary>
    private async Task ApplySkipWhenAutoSetsAsync(
        AgentConfig config,
        AgentToolContext session,
        CancellationToken ct)
    {
        var activeFlow = ActiveFlowResolver.Resolve(config, session);

        foreach (var stage in activeFlow.Stages)
        {
            if (stage.AutoSetOnSkip.Count == 0 || string.IsNullOrWhiteSpace(stage.SkipWhen))
                continue;

            if (!EvaluateSkipWhen(stage.SkipWhen, session.Facts))
                continue;

            foreach (var (factKey, factValue) in stage.AutoSetOnSkip)
            {
                if (session.Facts.TryGetValue(factKey, out var existing)
                    && !string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                var schemaEntry = config.FactSchema.FirstOrDefault(e =>
                    e.Key.Equals(factKey, StringComparison.OrdinalIgnoreCase));

                await _factsService.SetAsync(
                    session.ConversationId,
                    session.BusinessId,
                    factKey,
                    factValue,
                    schemaEntry?.ShouldRememberAcrossRequests() ?? false,
                    ct);
                session.Facts[factKey] = factValue;

                _logger.LogDebug(
                    "Conv {ConvId}: stage '{Stage}' skip-auto-set {Key}={Value}",
                    session.ConversationId, stage.Id, factKey, factValue);
            }
        }
    }

    /// <summary>
    /// Evalua la condicion SkipWhen (keys separados por &amp;&amp;, todos deben estar presentes).
    /// </summary>
    private static bool EvaluateSkipWhen(string skipWhen, IReadOnlyDictionary<string, string> facts)
    {
        var conditions = skipWhen.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return conditions.All(cond =>
            facts.TryGetValue(cond, out var val) && !string.IsNullOrWhiteSpace(val));
    }

    private static IReadOnlyList<ChatToolDefinition> BuildToolDefinitions(AgentConfig config, IReadOnlyList<IAgentTool> effectiveTools) =>
        effectiveTools
            .Select(t => new ChatToolDefinition
            {
                Name           = t.Name,
                Description    = t.Description,
                ParametersJson = t.BuildParametersSchema(config)
            })
            .ToList();

    private static PaymentTransaction? ResolveActionablePayment(PaymentTransaction? payment)
    {
        if (payment is null)
            return null;

        if (payment.Status == PaymentTransactionStatus.Created)
            return payment;

        if (payment.Status == PaymentTransactionStatus.Confirmed
            && (!payment.ReservationId.HasValue || payment.RequiresRescheduling))
        {
            return payment;
        }

        return null;
    }
    private static IReadOnlyList<Domain.Entities.Message> ProjectHistoryForTurn(
        IReadOnlyList<Domain.Entities.Message> history,
        DateTime? activeRequestStartedAtUtc,
        PaymentTransaction? activePayment,
        PaymentTransaction? latestPayment)
    {
        var filtered = activeRequestStartedAtUtc.HasValue
            ? history.Where(message => message.Timestamp >= activeRequestStartedAtUtc.Value)
            : history;

        return filtered
            .Select(message => RedactInactivePaymentArtifact(message, activePayment, latestPayment))
            .ToList();
    }

    private static Domain.Entities.Message RedactInactivePaymentArtifact(
        Domain.Entities.Message message,
        PaymentTransaction? activePayment,
        PaymentTransaction? latestPayment)
    {
        var inactiveLink = latestPayment?.LinkUrl;
        if (!IsBotSender(message.Sender)
            || string.IsNullOrWhiteSpace(inactiveLink)
            || activePayment?.PaymentTransactionId == latestPayment!.PaymentTransactionId)
        {
            return message;
        }

        var redacted = message.MessageText.Replace(
            inactiveLink,
            "[link de pago no vigente]",
            StringComparison.OrdinalIgnoreCase);

        return redacted == message.MessageText
            ? message
            : new Domain.Entities.Message
            {
                MessageId = message.MessageId,
                ConversationId = message.ConversationId,
                Sender = message.Sender,
                MessageText = redacted,
                Timestamp = message.Timestamp
            };
    }
    private static bool IsBotSender(string sender) =>
        sender.Equals("bot", StringComparison.OrdinalIgnoreCase) ||
        sender.Equals("assistant", StringComparison.OrdinalIgnoreCase);
    private static List<ChatMessage> BuildMessages(
        string systemPrompt,
        IEnumerable<Domain.Entities.Message> history,
        string userMessage)
    {
        var messages = new List<ChatMessage> { ChatMessage.System(systemPrompt) };

        foreach (var msg in history)
        {
            if (msg.Sender == "user")
                messages.Add(ChatMessage.User(msg.MessageText));
            else if (msg.Sender == "bot" || msg.Sender == "assistant")
                messages.Add(ChatMessage.Assistant(msg.MessageText));
        }

        messages.Add(ChatMessage.User(userMessage));
        return messages;
    }
    private static ChatCompletionOptions BuildOptions(AgentConfig config, bool forceText) =>
        new() { Temperature = config.Temperature, MaxTokens = 800, ForceTextResponse = forceText };

    private static bool IsFinalAnswer(ChatCompletionFinishReason reason) =>
        reason is ChatCompletionFinishReason.Stop or ChatCompletionFinishReason.Length;
    private static bool ShouldRecoverEmptyToolTurn(
        string response,
        AgentTurnExecution turn,
        bool forceText,
        bool alreadyRecovered) =>
        !forceText &&
        !alreadyRecovered &&
        string.IsNullOrWhiteSpace(response) &&
        turn.ToolCallCount > 0 &&
        turn.OutboundMessages.Count == 0 &&
        turn.FragmentEntries.Count == 0;

    private static string BuildToollessRecoveryInstruction(
        AgentFlowStage? currentStage,
        IReadOnlyList<IAgentTool> scopedTools)
    {
        var stageId = currentStage?.Id ?? "actual";
        var missingFacts = currentStage?.AdvanceWhenFacts
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var allowedTools = scopedTools
            .Select(tool => tool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return "No confirmes cambios ni presentes artefactos generados desde historial sin herramientas. " +
            $"La etapa '{stageId}' todavia tiene datos o verificaciones pendientes" +
            (missingFacts.Count > 0 ? $": {string.Join(", ", missingFacts)}. " : ". ") +
            (allowedTools.Count > 0 ? $"Usa una de las tools permitidas de este turno ({string.Join(", ", allowedTools)}) para registrar, verificar o regenerar el estado vigente. " : string.Empty) +
            "Despues responde solo con el resultado vigente de las tools ejecutadas.";
    }
    private static bool ShouldRecoverToollessMutationTurn(
        string response,
        AgentTurnExecution turn,
        bool forceText,
        bool alreadyRecovered,
        IReadOnlyList<IAgentTool> scopedTools,
        AgentToolContext ctx,
        AgentFlowStage? currentStage,
        AgentConfig config)
    {
        if (forceText || alreadyRecovered || string.IsNullOrWhiteSpace(response))
            return false;

        if (currentStage is null || !IsStatefulToolStage(currentStage))
            return false;

        if (scopedTools.Count == 0)
            return false;

        var containsGeneratedArtifact = ContainsGeneratedArtifact(response)
            && scopedTools.Any(tool => tool.Capabilities.Contains(
                ToolCapabilities.CheckoutPrepare,
                StringComparer.OrdinalIgnoreCase));
        if (turn.ToolCallCount > 0)
            return containsGeneratedArtifact;

        if (MentionsCollectableFactValue(config, currentStage, ctx.LatestUserMessage))
            return true;

        return containsGeneratedArtifact;
    }

    private static bool IsStatefulToolStage(AgentFlowStage stage) =>
        stage.AllowedActions.Count > 0
        && (stage.Collect.Count > 0 || stage.AdvanceWhenFacts.Count > 0);

    private static bool MentionsCollectableFactValue(
        AgentConfig config,
        AgentFlowStage stage,
        string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var normalized = NormalizeIntentText(message);
        var collectableFactTokens = stage.Collect
            .Concat(stage.AdvanceWhenFacts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(factKey => BuildFactSearchTokens(config, factKey))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(NormalizeIntentText)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (collectableFactTokens.Any(token => ContainsNormalizedPhrase(normalized, token)))
            return true;

        return stage.Collect
            .Concat(stage.AdvanceWhenFacts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(factKey => FactValueShapeMatcher.MessageMatchesFactShape(config.FactSchema, factKey, message));
    }

    private static bool HasStaleVerification(AgentToolContext ctx, string verificationType) =>
        ctx.ConversationState.Verifications.TryGetValue(verificationType, out var entry)
        && !VerificationSnapshot.Matches(entry.PayloadJson, ctx.Facts);

    private static IEnumerable<string> BuildFactSearchTokens(AgentConfig config, string factKey)
    {
        var entry = config.FactSchema.FirstOrDefault(e => e.Key.Equals(factKey, StringComparison.OrdinalIgnoreCase));
        yield return factKey.Replace('_', ' ');

        if (entry is null)
            yield break;

        if (!string.IsNullOrWhiteSpace(entry.Label))
            yield return entry.Label;

        if (!string.IsNullOrWhiteSpace(entry.Role))
            yield return entry.Role.Split('.').Last();

        foreach (var alias in entry.Aliases)
            yield return alias;
    }

    private static bool ContainsGeneratedArtifact(string response) =>
        response.Contains("http://", StringComparison.OrdinalIgnoreCase)
        || response.Contains("https://", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(response, @"\{\{[A-Z][A-Z0-9_]*:[^{}]+\}\}", RegexOptions.CultureInvariant);

    private static string SanitizeInput(string message) =>
        message.Length > 4000 ? message[..4000].Trim() : message.Trim();

    private static string SanitizeResponse(string response) =>
        response.Length > 4096 ? response[..4096].Trim() : response.Trim();

    /// <summary>
    /// Al finalizar cada turno, guarda snapshots de los facts para las etapas que acaban de completarse
    /// y que tienen ReentryOnFactChanged configurado.
    /// Solo se graba la primera vez que una etapa se completa (snapshot inmutable).
    /// </summary>
    private void UpdateStageSnapshots(AgentConfig config, AgentToolContext session)
    {
        var activeFlow = ActiveFlowResolver.Resolve(config, session);
        if (activeFlow.Stages.Count == 0) return;

        var currentStage = _flowStageDetector.DetectCurrentStage(activeFlow, session);
        var currentIdx = currentStage is null
            ? activeFlow.Stages.Count
            : activeFlow.Stages.ToList().FindIndex(s => s.Id == currentStage.Id);

        var snapshots = session.ConversationState.StageFactSnapshots;

        for (var i = 0; i < currentIdx; i++)
        {
            var stage = activeFlow.Stages[i];
            if (stage.ReentryOnFactChanged.Count == 0) continue;
            if (snapshots.ContainsKey(stage.Id)) continue;

            var snap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in stage.ReentryOnFactChanged)
            {
                if (session.Facts.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                    snap[key] = raw;
            }

            if (snap.Count > 0)
            {
                snapshots[stage.Id] = snap;
                _logger.LogDebug("Conv {ConvId}: stage '{Stage}' snapshot saved ({Count} facts)",
                    session.ConversationId, stage.Id, snap.Count);
            }
        }
    }
}
