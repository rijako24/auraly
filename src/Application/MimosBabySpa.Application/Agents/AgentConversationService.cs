using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Orquestador del agente. Único punto de entrada para procesar un turno de conversación.
///
/// Responsabilidades (por capa):
///   1. Guardrails de corto-circuito (Owner=Human, kill-switch) — sin llamar al LLM.
///   2. Bucle nativo de Function Calling con límite de iteraciones y auto-escalación.
///   3. Persistencia del turno y propagación de side-effects al canal.
///
/// NO toma decisiones de negocio — las tools y sus servicios son la autoridad.
/// </summary>
public sealed class AgentConversationService : IAgentConversationService
{

    private readonly IAgentConfigProvider _configProvider;
    private readonly IChatClient _chatClient;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IEscalationConfigProvider _escalationConfig;
    private readonly IBusinessClock _businessClock;
    private readonly ITemporalReferenceBuilder _temporalReferenceBuilder;
    private readonly IConversationFactsService _factsService;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly IReservationLifecycleService _reservationLifecycle;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IConversationService _conversationService;
    private readonly IConversationLifecycleService _lifecycleService;
    private readonly IPromptComposer _promptComposer;
    private readonly IAgentTurnResponseComposer _turnResponseComposer;
    private readonly IToolCapabilityGate _toolCapabilityGate;
    private readonly IFlowStageDetector _flowStageDetector;
    private readonly IFactHydrator _factHydrator;
    private readonly IUsageBillingService _usageBilling;
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
        IConversationFactsService factsService,
        ICustomerMemoryService customerMemory,
        IReservationLifecycleService reservationLifecycle,
        IPaymentLifecycleService paymentLifecycle,
        IConversationService conversationService,
        IConversationLifecycleService lifecycleService,
        IPromptComposer promptComposer,
        IAgentTurnResponseComposer turnResponseComposer,
        IToolCapabilityGate toolCapabilityGate,
        IFlowStageDetector flowStageDetector,
        IFactHydrator factHydrator,
        IUsageBillingService usageBilling,
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
        _factsService = factsService;
        _customerMemory = customerMemory;
        _reservationLifecycle = reservationLifecycle;
        _paymentLifecycle = paymentLifecycle;
        _conversationService = conversationService;
        _lifecycleService = lifecycleService;
        _promptComposer = promptComposer;
        _turnResponseComposer = turnResponseComposer;
        _toolCapabilityGate = toolCapabilityGate;
        _flowStageDetector = flowStageDetector;
        _factHydrator = factHydrator;
        _usageBilling = usageBilling;
        _logger = logger;
    }

    public async Task<AgentTurnResult> ProcessMessageAsync(
        Guid agentId,
        Guid conversationId,
        string userMessage,
        string? channelPhone = null,
        CancellationToken cancellationToken = default)
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

        var session = await LoadTurnSessionAsync(
            config, state, conversationId, channelPhone, cancellationToken);

        // Capa 1 — corto-circuito sin LLM
        if (state.Owner == ConversationOwner.Human)
        {
            _logger.LogInformation("Conv {ConvId}: Owner=Human, skipping bot", conversationId);
            return AgentTurnResult.Ok(string.Empty);
        }

        if (IsKillSwitchPhrase(userMessage, config.KillSwitchPhrases))
        {
            _logger.LogInformation("Conv {ConvId}: kill-switch triggered", conversationId);
            return await EscalateAndPersistAsync(
                config, session, userMessage, "kill_switch_phrase", cancellationToken);
        }

        // Capa 2 — bucle de Function Calling
        userMessage = SanitizeInput(userMessage);

        var history = (await _messageService.GetRecentConversationHistoryAsync(
            conversationId, config.HistoryWindowSize, cancellationToken)).ToList();

        // Resolver engagement e inyectarlo como fact de sesión (efímero, no persiste en BD)
        var engagementKey = await ResolveEngagementKeyAsync(
            config.BusinessId, session.Conversation.UserNumber, history, cancellationToken);
        session.Facts["session.engagement"] = engagementKey;

        var clockSnapshot = await _businessClock.GetSnapshotAsync(config.BusinessId, cancellationToken);
        var temporal = _temporalReferenceBuilder.Build(clockSnapshot);
        var latestPayment = await _paymentLifecycle.GetLatestByConversationAsync(conversationId, cancellationToken);
        var enabledTools = _toolRegistry.GetToolsForAgent(config.EnabledToolNames).ToList();
        var compositionInput = new PromptCompositionInput
        {
            Config = config,
            History = history,
            Temporal = temporal,
            Session = session,
            LatestPayment = latestPayment,
            EnabledTools = enabledTools
        };
        var systemPrompt = _promptComposer.Compose(compositionInput);
        var messages = BuildMessages(systemPrompt, history, userMessage);
        var turn = new AgentTurnExecution(config.ConsecutiveErrorEscalationThreshold);
        session.Turn = turn;

        var loop = await RunAgentLoopAsync(
            config, conversationId, messages, turn, session, compositionInput, cancellationToken);

        return loop.Kind switch
        {
            AgentLoopOutcome.OutcomeKind.Completed =>
                await FinalizeTurnAsync(
                    conversationId, userMessage, loop.Response!, session, turn, config, cancellationToken),

            AgentLoopOutcome.OutcomeKind.AutoEscalate =>
                await EscalateAndPersistAsync(
                    config, session, userMessage, loop.Reason!, cancellationToken),

            _ => AgentTurnResult.Fail(loop.Reason ?? "Unknown loop failure")
        };
    }

    // ── Bucle de Function Calling ────────────────────────────────────────────

    private async Task<AgentLoopOutcome> RunAgentLoopAsync(
        AgentConfig config,
        Guid conversationId,
        List<ChatMessage> messages,
        AgentTurnExecution turn,
        AgentToolContext toolCtx,
        PromptCompositionInput compositionInput,
        CancellationToken ct)
    {
        var toolDefinitions = BuildToolDefinitions(config);
        var lastStageId = _flowStageDetector.DetectCurrentStage(config.Flow, toolCtx)?.Id;
        var recoveredEmptyToolTurn = false;

        for (int iteration = 0; iteration <= config.MaxToolIterations; iteration++)
        {
            var forceText = iteration >= config.MaxToolIterations;
            if (forceText)
                _logger.LogWarning("Conv {ConvId}: MaxToolIterations reached — forcing text response", conversationId);

            var options = BuildOptions(config, forceText);
            var result  = await _chatClient.CompleteAsync(
                messages, forceText ? null : toolDefinitions, options, ct);

            turn.AddTokens(result.PromptTokens, result.CompletionTokens);

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

                return AgentLoopOutcome.Completed(response);
            }

            // FinishReason=ToolCalls → ejecutar y acumular resultados
            messages.Add(result.AssistantMessage);

            foreach (var toolCall in result.ToolCalls)
            {
                toolCtx.CurrentToolIteration = iteration;
                var outcome = await ExecuteToolCallAsync(toolCall, toolCtx, ct);
                await ApplyAfterToolRulesAsync(config, toolCall, outcome, toolCtx, ct);

                turn.RecordToolOutcome(outcome);
                messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, outcome.RawJson));

                if (turn.ShouldAutoEscalate)
                {
                    _logger.LogWarning("Conv {ConvId}: {N} consecutive tool errors — auto-escalating",
                        conversationId, turn.ConsecutiveToolErrors);
                    return AgentLoopOutcome.AutoEscalate("consecutive_tool_errors");
                }
            }

            if (turn.OutboundMessages.Count > 0)
            {
                _logger.LogInformation(
                    "Conv {ConvId}: salida directa al usuario encolada ({Count} mensajes) — turno completo sin texto del LLM",
                    conversationId, turn.OutboundMessages.Count);
                return AgentLoopOutcome.Completed(string.Empty);
            }

            RefreshSystemPromptIfStageChanged(config, messages, compositionInput, ref lastStageId);
        }

        return AgentLoopOutcome.Failed("Max iterations exceeded without final response.");
    }

    /// <summary>
    /// Tras ejecutar tools, la etapa activa puede haber avanzado. Recompone el system prompt
    /// para que el LLM reciba goal, hint y acciones_permitidas actualizados en la siguiente iteración.
    /// </summary>
    private void RefreshSystemPromptIfStageChanged(
        AgentConfig config,
        List<ChatMessage> messages,
        PromptCompositionInput compositionInput,
        ref string? lastStageId)
    {
        var currentStageId = _flowStageDetector.DetectCurrentStage(config.Flow, compositionInput.Session)?.Id;
        if (string.Equals(currentStageId, lastStageId, StringComparison.OrdinalIgnoreCase))
            return;

        lastStageId = currentStageId;
        var systemPrompt = _promptComposer.Compose(compositionInput);
        messages[0] = ChatMessage.System(systemPrompt);

        _logger.LogDebug(
            "Conv: stage changed to '{Stage}' — system prompt refreshed",
            currentStageId ?? "(none)");
    }

    private async Task<ToolExecutionOutcome> ExecuteToolCallAsync(
        ToolCallRequest toolCall, AgentToolContext ctx, CancellationToken ct)
    {
        _logger.LogInformation("Conv {ConvId}: tool_call [{Id}] {Name}({Args})",
            ctx.ConversationId, toolCall.Id, toolCall.FunctionName, toolCall.ArgumentsJson);

        var tool = _toolRegistry.Resolve(toolCall.FunctionName);

        if (tool is null)
        {
            var notFound = ToolResultHelper.Error("tool_not_found",
                $"Tool '{toolCall.FunctionName}' is not registered.",
                "Check the available tool names.");
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

                return ToolExecutionOutcome.Parse(
                    ToolResultHelper.Error(gate.Code!, gate.Reason!, gate.Remediation));
            }

            var rawJson = await tool.ExecuteAsync(argsDoc.RootElement, ctx, ct);
            var outcome = ToolExecutionOutcome.Parse(rawJson);

            _logger.LogInformation("Conv {ConvId}: tool_result [{Id}] {Name} → {Result}",
                ctx.ConversationId, toolCall.Id, toolCall.FunctionName,
                rawJson.Length > 200 ? rawJson[..200] + "…" : rawJson);

            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conv {ConvId}: tool {Name} threw exception",
                ctx.ConversationId, toolCall.FunctionName);

            return ToolExecutionOutcome.Parse(
                ToolResultHelper.Error("tool_exception", ex.Message, "Try again or escalate."));
        }
    }

    // ── Persistencia y escalación ────────────────────────────────────────────

    private async Task ApplyAfterToolRulesAsync(
        AgentConfig config,
        ToolCallRequest toolCall,
        ToolExecutionOutcome outcome,
        AgentToolContext ctx,
        CancellationToken ct)
    {
        if (outcome.IsError)
            return;

        var currentStage = _flowStageDetector.DetectCurrentStage(config.Flow, ctx);
        if (currentStage is null || currentStage.AfterTool.Count == 0)
            return;

        foreach (var rule in currentStage.AfterTool)
        {
            if (!rule.Tool.Equals(toolCall.FunctionName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsAfterToolRuleMatch(outcome.RawJson, rule.When))
                continue;

            foreach (var (key, valueTemplate) in EnumerateAfterToolFactActions(rule))
            {
                if (ctx.Facts.TryGetValue(key, out var existing)
                    && !string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                var value = ResolveAfterToolValue(outcome.RawJson, valueTemplate);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var schemaEntry = config.FactSchema.FirstOrDefault(e =>
                    e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

                await _factsService.SetAsync(
                    ctx.ConversationId,
                    ctx.BusinessId,
                    key,
                    value,
                    schemaEntry?.PersistsAcrossConversations ?? false,
                    ct);

                ctx.Facts[key] = value;

                _logger.LogInformation(
                    "Conv {ConvId}: afterTool rule on stage '{Stage}' set {Key}={Value}",
                    ctx.ConversationId,
                    currentStage.Id,
                    key,
                    value);
            }
        }
    }

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

            return JsonElementEquals(value, condition.Expected);
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

    private async Task<AgentTurnResult> FinalizeTurnAsync(
        Guid conversationId,
        string userMessage,
        string botResponse,
        AgentToolContext session,
        AgentTurnExecution turn,
        AgentConfig config,
        CancellationToken ct)
    {
        var finalResponse = _turnResponseComposer.Compose(
            config,
            _toolRegistry.GetToolsForAgent(config.EnabledToolNames).ToList(),
            botResponse,
            turn.FragmentEntries);

        UpdateStageSnapshots(config, session);
        await PersistCurrentStageNameAsync(config, session, ct);
        await PersistTurnAsync(conversationId, userMessage, finalResponse, session.ConversationState, turn.ReservationCreated, ct);
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
                reservation_created = turn.ReservationCreated
            })), ct);

        _logger.LogInformation(
            "Conv {ConvId}: turn complete — tokens={Tokens}, tools={Tools}, escalated={Esc}, fragments={Fragments}",
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

        const string escalateMsg = "Te estamos comunicando con un agente humano. En breve te atenderán.";
        await PersistTurnAsync(session.ConversationId, userMessage, escalateMsg, session.ConversationState, reservationCreated: false, ct);

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
        bool reservationCreated,
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
        var currentStage = _flowStageDetector.DetectCurrentStage(config.Flow, session);
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
        CancellationToken ct)
    {
        var facts = await _factsService.GetAllAsync(conversationId, ct);
        var conversation = await _conversationService.GetConversationByIdAsync(conversationId)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        var clockSnapshot = await _businessClock.GetSnapshotAsync(config.BusinessId, ct);
        var resolvedPhone = channelPhone?.Trim() ?? conversation.UserNumber;

        var reservationSession = await _reservationLifecycle.ResolveForSessionAsync(
            conversationId,
            config.BusinessId,
            resolvedPhone,
            clockSnapshot.Today,
            ct);
        var activePayment = await _paymentLifecycle.GetActiveByConversationAsync(conversationId, ct);

        var mutableFacts = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);

        var durable = await _customerMemory.GetAllAsync(config.BusinessId, conversation.UserNumber, ct);
        foreach (var entry in config.FactSchema.Where(e => e.PersistsAcrossConversations))
        {
            if (!durable.TryGetValue(entry.Key, out var durableValue)
                || string.IsNullOrWhiteSpace(durableValue))
            {
                continue;
            }

            if (!mutableFacts.TryGetValue(entry.Key, out var current) || string.IsNullOrWhiteSpace(current))
                mutableFacts[entry.Key] = durableValue;
        }

        // Hidratar facts de fuente=channel/session antes de construir el contexto
        // Nota: el engagement se agrega por separado en ProcessMessageAsync (requiere historia)
        _factHydrator.Hydrate(config.FactSchema, mutableFacts, new FactHydratorContext
        {
            ChannelPhone = resolvedPhone
        });

        var session = new AgentToolContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = conversationId,
            BusinessToday = clockSnapshot.Today,
            BusinessNow = clockSnapshot.Now,
            ChannelPhone = resolvedPhone,
            EscalationContacts = config.EscalationContacts,
            Config = config,
            ConversationState = state,
            Conversation = conversation,
            Facts = mutableFacts,
            CustomerMemorySummary = durable.TryGetValue(CustomerMemoryKeys.Summary, out var summary)
                && !string.IsNullOrWhiteSpace(summary)
                    ? summary.Trim()
                    : null,
            ManageableReservations = reservationSession.ManageableReservations,
            ActivePayment = activePayment
        };

        // Aplicar AutoSetOnSkip para stages que se saltan declarativamente
        await ApplySkipWhenAutoSetsAsync(config, session, ct);

        return session;
    }

    /// <summary>
    /// Para cada etapa que tenga SkipWhen satisfecho, aplica AutoSetOnSkip
    /// solo si el fact aún no tiene valor (el dato de usuario tiene precedencia).
    /// </summary>
    private async Task ApplySkipWhenAutoSetsAsync(
        AgentConfig config,
        AgentToolContext session,
        CancellationToken ct)
    {
        foreach (var stage in config.Flow.Stages)
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
                    schemaEntry?.PersistsAcrossConversations ?? false,
                    ct);
                session.Facts[factKey] = factValue;

                _logger.LogDebug(
                    "Conv {ConvId}: stage '{Stage}' skip-auto-set {Key}={Value}",
                    session.ConversationId, stage.Id, factKey, factValue);
            }
        }
    }

    /// <summary>
    /// Evalúa la condición SkipWhen (keys separados por &amp;&amp;, todos deben estar presentes).
    /// </summary>
    private static bool EvaluateSkipWhen(string skipWhen, IReadOnlyDictionary<string, string> facts)
    {
        var conditions = skipWhen.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return conditions.All(cond =>
            facts.TryGetValue(cond, out var val) && !string.IsNullOrWhiteSpace(val));
    }

    private IReadOnlyList<ChatToolDefinition> BuildToolDefinitions(AgentConfig config) =>
        _toolRegistry.GetToolsForAgent(config.EnabledToolNames)
            .Select(t => new ChatToolDefinition
            {
                Name           = t.Name,
                Description    = t.Description,
                ParametersJson = t.BuildParametersSchema(config)
            })
            .ToList();

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

    private static bool IsKillSwitchPhrase(string message, IReadOnlyList<string> configuredPhrases)
    {
        if (configuredPhrases.Count == 0)
            return false;

        var lower = message.ToLowerInvariant();
        return configuredPhrases.Any(phrase => lower.Contains(phrase.ToLowerInvariant()));
    }

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
        if (config.Flow.Stages.Count == 0) return;

        var currentStage = _flowStageDetector.DetectCurrentStage(config.Flow, session);
        var currentIdx = currentStage is null
            ? config.Flow.Stages.Count
            : config.Flow.Stages.ToList().FindIndex(s => s.Id == currentStage.Id);

        var snapshots = session.ConversationState.StageFactSnapshots;

        for (var i = 0; i < currentIdx; i++)
        {
            var stage = config.Flow.Stages[i];
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
