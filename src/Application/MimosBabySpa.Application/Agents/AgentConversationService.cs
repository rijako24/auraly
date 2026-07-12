using System.Text.Json;

using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using MimosBabySpa.Application.Agents.Composition;

using MimosBabySpa.Application.Agents.Configuration;

using MimosBabySpa.Application.Agents.Facts;

using MimosBabySpa.Application.Agents.Gating;

using MimosBabySpa.Application.Agents.Operations;

using MimosBabySpa.Application.Agents.Runtime;

using MimosBabySpa.Application.Agents.Templates;

using MimosBabySpa.Application.Agents.Operations.Support;

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

///   2. Plan semantico estructurado y ejecucion deterministica de operaciones configuradas.

///   3. Persistencia del turno y propagacion de side-effects al canal.

///

/// NO toma decisiones de negocio; las operaciones y sus servicios son la autoridad.

/// </summary>

public sealed class AgentConversationService : IAgentConversationService

{

    private readonly IAgentConfigProvider _configProvider;

    private readonly AgentOperationRegistry _operationRegistry;

    private readonly IConversationStateManager _stateManager;

    private readonly IMessageService _messageService;

    private readonly IBusinessClock _businessClock;

    private readonly IOperatingHoursTurnPolicy _operatingHoursPolicy;

    private readonly IConversationFactsService _factsService;

    private readonly ICustomerMemoryService _customerMemory;

    private readonly IRequestContextService _requestContext;

    private readonly IReservationLifecycleService _reservationLifecycle;

    private readonly IPaymentLifecycleService _paymentLifecycle;

    private readonly IConversationService _conversationService;

    private readonly IConversationLifecycleService _lifecycleService;

    private readonly IFactHydrator _factHydrator;

    private readonly IUsageBillingService _usageBilling;

    private readonly DeterministicTurnCoordinator _deterministicCoordinator;

    private readonly IDeterministicResponseRenderer _deterministicRenderer;

    private readonly IDeterministicTurnEffectProcessor _deterministicEffects;

    private readonly ILogger<AgentConversationService> _logger;

    public AgentConversationService(

        IAgentConfigProvider configProvider,

        AgentOperationRegistry operationRegistry,

        IConversationStateManager stateManager,

        IMessageService messageService,

        IBusinessClock businessClock,

        IOperatingHoursTurnPolicy operatingHoursPolicy,

        IConversationFactsService factsService,

        ICustomerMemoryService customerMemory,

        IRequestContextService requestContext,

        IReservationLifecycleService reservationLifecycle,

        IPaymentLifecycleService paymentLifecycle,

        IConversationService conversationService,

        IConversationLifecycleService lifecycleService,

        IFactHydrator factHydrator,

        IUsageBillingService usageBilling,

        ILogger<AgentConversationService> logger,

        DeterministicTurnCoordinator deterministicCoordinator,

        IDeterministicResponseRenderer deterministicRenderer,

        IDeterministicTurnEffectProcessor deterministicEffects)

    {

        _configProvider = configProvider;

        _operationRegistry = operationRegistry;

        _stateManager = stateManager;

        _messageService = messageService;

        _businessClock = businessClock;

        _operatingHoursPolicy = operatingHoursPolicy;

        _factsService = factsService;

        _customerMemory = customerMemory;

        _requestContext = requestContext;

        _reservationLifecycle = reservationLifecycle;

        _paymentLifecycle = paymentLifecycle;

        _conversationService = conversationService;

        _lifecycleService = lifecycleService;

        _factHydrator = factHydrator;

        _usageBilling = usageBilling;

        _logger = logger;

        _deterministicCoordinator = deterministicCoordinator;

        _deterministicRenderer = deterministicRenderer;

        _deterministicEffects = deterministicEffects;

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

        // Capa 1 - corto-circuito sin LLM

        if (state.Owner == ConversationOwner.Human)

        {

            _logger.LogInformation("Conv {ConvId}: Owner=Human, skipping bot", conversationId);

            return AgentTurnResult.Ok(string.Empty);

        }

        // Capa 2 - plan estructurado y ejecucion deterministica

        userMessage = SanitizeInput(userMessage);

        session.LatestUserMessage = userMessage;

        var history = (await _messageService.GetRecentConversationHistoryAsync(

            conversationId, config.HistoryWindowSize, cancellationToken)).ToList();
var latestPayment = await _paymentLifecycle.GetLatestByConversationAsync(conversationId, cancellationToken);

        var turnHistory = ProjectHistoryForTurn(

            history,

            session.ConversationState.ActiveRequestStartedAtUtc,

            session.ActivePayment,

            latestPayment);

        return await ProcessDeterministicTurnAsync(

            config,

            session,

            userMessage,

            turnHistory,

            clockSnapshot,

            cancellationToken);

    }

    private async Task<AgentTurnResult> ProcessDeterministicTurnAsync(

        AgentConfig config,

        AgentConversationContext session,

        string userMessage,

        IReadOnlyList<Domain.Entities.Message> history,

        BusinessClockSnapshot clockSnapshot,

        CancellationToken ct)

    {

        var chatHistory = history

            .Where(message => !string.IsNullOrWhiteSpace(message.MessageText))

            .Select(message => IsBotSender(message.Sender)

                ? ChatMessage.Assistant(message.MessageText)

                : ChatMessage.User(message.MessageText))

            .ToList();

        var operatingHours = await _operatingHoursPolicy.EvaluateAsync(config, clockSnapshot, ct);

        session.OperatingHours = operatingHours;

        DeterministicConversationPosition.ExpireSecondaryFlowIfNeeded(
            config,
            session.ConversationState,
            session.BusinessNow.UtcDateTime);

        var activeFlow = DeterministicConversationPosition.ResolveFlow(config, session.ConversationState);

        var currentStage = DeterministicConversationPosition.ResolveStage(

            activeFlow, session.ConversationState, session.Facts, config.FactSchema);

        if (currentStage is null)

            return AgentTurnResult.Fail($"Flow '{activeFlow.Id}' has no stages.");

        var interactiveResult = await TryHandleConfiguredInteractiveActionAsync(

            config, userMessage, session, chatHistory, currentStage, ct);

        if (interactiveResult is not null)

            return interactiveResult;

        if (operatingHours.IsEnforced && operatingHours.IsOutsideOperatingHours)

        {

            var outside = config.OperatingHours.OutsideHours;

            var guidance = (outside.Guidance ?? string.Empty).Replace(

                "{{next_operating_window}}",

                operatingHours.NextOperatingWindowText ?? string.Empty,

                StringComparison.OrdinalIgnoreCase);

            var presentations = string.IsNullOrWhiteSpace(outside.Template)

                ? Array.Empty<OperationPresentation>()

                : new[]

                {

                    new OperationPresentation(

                        outside.Template,

                        session.Facts.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase),

                        FragmentRenderMode.Exclusive,

                        FragmentPriority.Required)

                };

            var closedTurn = new DeterministicTurnResult

            {

                Success = true,

                CurrentStageId = currentStage.Id,

                Facts = new Dictionary<string, string>(session.Facts, StringComparer.OrdinalIgnoreCase),

                FactVersions = new Dictionary<string, long>(

                    session.ConversationState.FactVersions, StringComparer.OrdinalIgnoreCase),

                Presentations = presentations,

                Sequences = string.IsNullOrWhiteSpace(outside.SendMessageSequence)

                    ? []

                    : [outside.SendMessageSequence],

                Response = new StageResponseDefinition { Guidance = guidance }

            };

            return await FinalizeDeterministicTurnAsync(

                config, session, userMessage, chatHistory, currentStage, closedTurn, ct);

        }

        var versions = new Dictionary<string, long>(

            session.ConversationState.FactVersions, StringComparer.OrdinalIgnoreCase);

        var executedOperationKeys = session.ConversationState.ExecutedOperationKeys
            .Where(entry => entry.Value >= DateTime.UtcNow.AddDays(-30))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = await _deterministicCoordinator.ExecuteAsync(
            new DeterministicTurnRequest

            {

                Config = config,

                OperationContext = new OperationContext

                {

                    AgentId = config.AgentId,

                    BusinessId = config.BusinessId,

                    ConversationId = session.ConversationId,

                    BusinessToday = session.BusinessToday,

                    BusinessNow = session.BusinessNow,

                    Config = config,

                    ConversationState = session.ConversationState,

                    Facts = session.Facts,

                    Session = session

                },

                CurrentFacts = session.Facts,

                FactVersions = versions,

                CurrentFlowId = activeFlow.Id,

                CurrentStageId = currentStage.Id,

                ActiveFlowId = activeFlow.Id,

                HasOpenPrimaryRequest = session.ConversationState.ActiveRequestStartedAtUtc.HasValue,

                LatestUserMessage = userMessage,

                RecentConversation = chatHistory,
                ExecutedActionKeys = executedOperationKeys
            },

            ct);
        LogDeterministicTurnTrace(session.ConversationId, result);


        session.ConversationState.ExecutedOperationKeys = executedOperationKeys
            .TakeLast(500)
            .ToDictionary(key => key, _ => DateTime.UtcNow, StringComparer.OrdinalIgnoreCase);
        if (!result.Success)

        {

            _logger.LogError(

                "Conv {ConvId}: deterministic turn failed: {Errors}",

                session.ConversationId,

                string.Join("; ", result.Errors));

            return AgentTurnResult.Fail(string.Join("; ", result.Errors));

        }

        var selectedFlow = AgentFlowCatalog.Find(config, result.Route?.ActiveFlowId)

            ?? activeFlow;

        var selectedStage = selectedFlow.Stages.FirstOrDefault(stage =>

                stage.Id.Equals(result.CurrentStageId, StringComparison.OrdinalIgnoreCase))

            ?? currentStage;

        return await FinalizeDeterministicTurnAsync(

            config, session, userMessage, chatHistory, selectedStage, result, ct);

    }

    private void LogDeterministicTurnTrace(Guid conversationId, DeterministicTurnResult result)
    {
        if (result.PlanningWarnings.Count > 0)
        {
            _logger.LogWarning(
                "Conv {ConvId} [PLAN_FAIL_SOFT] {Warnings}",
                conversationId,
                string.Join(" | ", result.PlanningWarnings));
        }

        var capturedFacts = result.Plan?.Facts
            .Select(fact => $"{fact.Key}:{fact.Operation}={fact.Value.GetRawText()}")
            .ToList() ?? [];
        _logger.LogInformation(
            "Conv {ConvId} [FACTS_CAPTURED] {Facts}",
            conversationId,
            capturedFacts.Count == 0 ? "none" : string.Join(" | ", capturedFacts));

        var executedActions = result.Trace.Where(trace => !trace.Skipped).ToList();
        if (executedActions.Count == 0)
        {
            _logger.LogInformation("Conv {ConvId} [ACTIONS_EXECUTED] none", conversationId);
        }
        else
        {
            foreach (var action in executedActions)
            {
                _logger.LogInformation(
                    "Conv {ConvId} [ACTION_EXECUTED] Action={ActionId}, Operation={OperationId}, Success={Success}, Outcome={OutcomeCode}, ErrorCode={ErrorCode}, Error={ErrorMessage}, Arguments={ArgumentsJson}",
                    conversationId,
                    action.ActionId,
                    action.OperationId,
                    action.Success,
                    action.OutcomeCode,
                    action.Outcome?.Error?.Code ?? "none",
                    SanitizeOperationError(action.Outcome?.Error?.Message),
                    action.ArgumentsJson);
            }
        }

        var skippedActions = result.Trace.Where(trace => trace.Skipped).ToList();
        if (skippedActions.Count > 0)
        {
            _logger.LogDebug(
                "Conv {ConvId} [ACTIONS_SKIPPED] {Actions}",
                conversationId,
                string.Join(" | ", skippedActions.Select(action =>
                    $"{action.ActionId}:{action.OperationId} ({action.SkipReason})")));
        }
    }

    private static string SanitizeOperationError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "none";

        var singleLine = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return singleLine.Length <= 500 ? singleLine : singleLine[..500];
    }
    private async Task<AgentTurnResult> FinalizeDeterministicTurnAsync(

        AgentConfig config,

        AgentConversationContext session,

        string userMessage,

        IReadOnlyList<ChatMessage> history,

        AgentFlowStage stage,

        DeterministicTurnResult turn,

        CancellationToken ct)

    {

        session.Facts.Clear();

        foreach (var (key, value) in turn.Facts)

            session.Facts[key] = value;

        session.ConversationState.FactVersions = new Dictionary<string, long>(

            turn.FactVersions,

            StringComparer.OrdinalIgnoreCase);

        session.ConversationState.ActiveFlowId = turn.Route?.ActiveFlowId

            ?? session.ConversationState.ActiveFlowId;

        session.ConversationState.ActiveStageId = turn.CurrentStageId;

        DeterministicConversationPosition.RefreshFlowLease(
            config,
            session.ConversationState,
            session.BusinessNow.UtcDateTime);

        UpdateStageSnapshots(config, session);

        var rendered = await _deterministicRenderer.RenderAsync(

            new DeterministicResponseRequest(config, stage, turn, userMessage, history),

            ct);

        var effects = await _deterministicEffects.ProcessAsync(

            new DeterministicTurnEffectRequest(

                config.BusinessId,

                session.ConversationId,

                config,

                session.ConversationState,

                session.Facts,

                turn),

            ct);

        await PersistCurrentStageNameAsync(config, session, ct);

        await PersistTurnAsync(

            session.ConversationId,

            userMessage,

            rendered.Text,

            session.ConversationState,

            ct);

        var promptTokens = turn.PromptTokens + rendered.PromptTokens;

        var completionTokens = turn.CompletionTokens + rendered.CompletionTokens;

        var operationCount = turn.Trace.Count(trace => !trace.Skipped);

        await _usageBilling.ChargeAsync(new UsageChargeRequest(

            config.BusinessId,

            config.AgentId,

            session.ConversationId,

            MessageId: null,

            UsageOperationType.AgentTurn,

            promptTokens,

            completionTokens,

            operationCount,

            effects.OutboundMessages.Count,

            config.Model,

            MetadataJson: JsonSerializer.Serialize(new

            {

                engine = "deterministic",

                flow = turn.Route?.ActiveFlowId,

                stage = turn.CurrentStageId,

                request_completed = turn.RequestCompleted

            })), ct);

        var trace = new List<AgentTurnTraceEntry>();
        if (turn.Plan is not null)
        {
            trace.Add(new AgentTurnTraceEntry
            {
                Kind = "turn_plan",
                Iteration = 0,
                StageId = stage.Id,
                Content = JsonSerializer.Serialize(new
                {
                    plan = turn.Plan,
                    route = turn.Route,
                    visitedStages = turn.VisitedStages,
                    resultingStage = turn.CurrentStageId
                })
            });
        }

        trace.AddRange(turn.Trace.Select((entry, index) => new AgentTurnTraceEntry
        {
            Kind = entry.Skipped ? "operation_skipped" : "operation",
            Iteration = index + 1,
            StageId = stage.Id,
            ActionId = entry.ActionId,
            OperationId = entry.OperationId,
            OperationArgumentsJson = entry.ArgumentsJson,
            OperationOutcomeJson = entry.Outcome is null ? null : JsonSerializer.Serialize(entry.Outcome)
        }));

        return AgentTurnResult.Ok(

            rendered.Text,

            requestCompleted: turn.RequestCompleted,

            tokens: promptTokens + completionTokens,

            operationCount: operationCount,

            outboundMessages: effects.OutboundMessages,

            trace: trace);

    }

    private async Task<AgentTurnResult?> TryHandleConfiguredInteractiveActionAsync(

        AgentConfig config,

        string userMessage,

        AgentConversationContext session,

        IReadOnlyList<ChatMessage> history,

        AgentFlowStage currentStage,

        CancellationToken ct)

    {

        var interactive = session.InteractiveAction;

        if (interactive is null)

            return null;

        if (!TryGetConfiguredReservationAutomationAction(config, interactive, out var action)

            || action is null

            || string.IsNullOrWhiteSpace(action.Operation))

        {

            _logger.LogDebug(

                "Conv {ConvId}: interactive payload '{Payload}' has no configured reservation automation operation; continuing normal flow",

                session.ConversationId,

                interactive.RawPayload);

            return null;

        }

        var operationId = action.Operation.Trim();

        if (!_operationRegistry.TryGet(operationId, out var operation))

            throw new InvalidOperationException($"Compiled interactive operation '{operationId}' is not registered.");

        var argumentsJson = BuildInteractiveActionArgumentsJson(action.Arguments, interactive);

        using var argumentsDocument = JsonDocument.Parse(argumentsJson);

        var outcome = await operation.ExecuteAsync(

            argumentsDocument.RootElement,

            new OperationContext

            {

                AgentId = config.AgentId,

                BusinessId = config.BusinessId,

                ConversationId = session.ConversationId,

                BusinessToday = session.BusinessToday,

                BusinessNow = session.BusinessNow,

                Config = config,

                ConversationState = session.ConversationState,

                Facts = session.Facts,

                Session = session

            },

            ct);

        var sequences = string.IsNullOrWhiteSpace(action.SendMessageSequence)

            ? Array.Empty<string>()

            : new[] { action.SendMessageSequence.Trim() };

        var response = new StageResponseDefinition

        {

            Mode = outcome.Success ? "continue" : "ask_clarification",

            Guidance = outcome.Success

                ? "Comunica Ãºnicamente el resultado vigente de la acciÃ³n solicitada desde el botÃ³n."

                : outcome.Error?.Message ?? "No fue posible procesar la acciÃ³n del botÃ³n."

        };

        var turn = new DeterministicTurnResult

        {

            Success = true,

            CurrentStageId = currentStage.Id,

            Facts = new Dictionary<string, string>(session.Facts, StringComparer.OrdinalIgnoreCase),

            Trace =

            [

                new StageOperationTrace(

                    $"interactive:{interactive.Scope}:{interactive.Outcome}",

                    operationId,

                    argumentsJson,

                    outcome.Code,

                    outcome.Success,

                    Outcome: outcome)

            ],

            Presentations = outcome.Presentations,

            OperationEffects = outcome.Effects,

            Sequences = sequences,

            Events = outcome.Events,

            DomainEvents = outcome.DomainEvents,

            EscalateToHuman = outcome.Effects.OfType<EscalateHumanOperationEffect>().Any(),

            RequestCompleted = outcome.Effects.OfType<CompleteRequestOperationEffect>().Any(),

            Response = response

        };

        _logger.LogInformation(

            "Conv {ConvId}: configured interactive action {Scope}:{Outcome} handled by operation {Operation} with outcome {OutcomeCode}",

            session.ConversationId,

            interactive.Scope,

            interactive.Outcome,

            operationId,

            outcome.Code);

        return await FinalizeDeterministicTurnAsync(

            config,

            session,

            userMessage,

            history,

            currentStage,

            turn,

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

    private async Task ApplyReentryInvalidationAsync(

        AgentConversationContext ctx,

        IReadOnlyCollection<string> changedFactKeys,

        CancellationToken ct)

    {

        if (changedFactKeys.Count == 0)

            return;

        var invalidation = FlowCheckpointInvalidation.GetInvalidations(ctx, changedFactKeys);

        foreach (var stageId in invalidation.StageSnapshotsToReset)

        {

            ctx.ConversationState.StageFactSnapshots.Remove(stageId);

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

        AgentConversationContext session,

        CancellationToken ct)

    {

        var activeFlow = DeterministicConversationPosition.ResolveFlow(config, session.ConversationState);

        var currentStage = activeFlow.Stages.FirstOrDefault(stage =>

            stage.Id.Equals(session.ConversationState.ActiveStageId, StringComparison.OrdinalIgnoreCase))

            ?? DeterministicConversationPosition.ResolveStage(activeFlow, session.ConversationState, session.Facts, config.FactSchema);

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

    private async Task<AgentConversationContext> LoadTurnSessionAsync(

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

        _factHydrator.Hydrate(config.FactSchema, mutableFacts, new FactHydratorContext

        {

            ChannelPhone = resolvedPhone

        });

        var parsedInteractiveAction = InteractivePayloadParser.TryParse(inboundMetadata?.InteractivePayload, out var action)

            ? action

            : null;

        var session = new AgentConversationContext

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

        AgentConversationContext session,

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

    private static bool ApplyConversationIdentityFact(

        AgentConversationContext session,

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

            || MatchesFactToken(entry.Label, lookup));

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

    private static bool HasStaleVerification(AgentConversationContext ctx, string verificationType) =>

        ctx.ConversationState.Verifications.TryGetValue(verificationType, out var entry)

        && !VerificationSnapshot.Matches(entry.PayloadJson, ctx.Facts);

    private static bool FactValuesEqual(string? left, string? right) =>

        string.Equals(NormalizeFactValue(left), NormalizeFactValue(right), StringComparison.OrdinalIgnoreCase);

    private static bool HasFactValue(string? value) =>

        !string.IsNullOrWhiteSpace(NormalizeFactValue(value));

    private static string? NormalizeFactValue(string? value) =>

        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SanitizeInput(string message) =>

        message.Length > 4000 ? message[..4000].Trim() : message.Trim();

    /// <summary>

    /// Al finalizar cada turno, guarda snapshots de los facts para las etapas que acaban de completarse

    /// y que tienen ReentryOnFactChanged configurado.

    /// Solo se graba la primera vez que una etapa se completa (snapshot inmutable).

    /// </summary>

    private void UpdateStageSnapshots(AgentConfig config, AgentConversationContext session)

    {

        var activeFlow = DeterministicConversationPosition.ResolveFlow(config, session.ConversationState);

        if (activeFlow.Stages.Count == 0) return;

        var currentStage = DeterministicConversationPosition.ResolveStage(activeFlow, session.ConversationState, session.Facts, config.FactSchema);

        var currentIdx = activeFlow.Stages.ToList().FindIndex(s => s.Id == currentStage.Id);

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
