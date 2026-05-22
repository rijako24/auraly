using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Time;

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
    // Frases que activan escalación inmediata antes de tocar el LLM
    private static readonly string[] KillSwitchPhrases =
    [
        "quiero hablar con un humano", "quiero hablar con una persona",
        "agente real", "operador", "hablar con alguien",
        "escalate", "human agent", "hablar con ustedes",
        "estoy muy molest", "queja formal", "voy a demandar"
    ];

    private readonly IAgentConfigProvider _configProvider;
    private readonly IChatClient _chatClient;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IEscalationConfigProvider _escalationConfig;
    private readonly IBusinessClock _businessClock;
    private readonly ITemporalReferenceBuilder _temporalReferenceBuilder;
    private readonly IBookingPolicyProvider _bookingPolicy;
    private readonly IConversationFactsService _factsService;
    private readonly IReservationLifecycleService _reservationLifecycle;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IConversationService _conversationService;
    private readonly IAgentTurnResponseComposer _turnResponseComposer;
    private readonly IToolCapabilityGate _toolCapabilityGate;
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
        IBookingPolicyProvider bookingPolicy,
        IConversationFactsService factsService,
        IReservationLifecycleService reservationLifecycle,
        IPaymentLifecycleService paymentLifecycle,
        IConversationService conversationService,
        IAgentTurnResponseComposer turnResponseComposer,
        IToolCapabilityGate toolCapabilityGate,
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
        _bookingPolicy = bookingPolicy;
        _factsService = factsService;
        _reservationLifecycle = reservationLifecycle;
        _paymentLifecycle = paymentLifecycle;
        _conversationService = conversationService;
        _turnResponseComposer = turnResponseComposer;
        _toolCapabilityGate = toolCapabilityGate;
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

        if (IsKillSwitchPhrase(userMessage))
        {
            _logger.LogInformation("Conv {ConvId}: kill-switch triggered", conversationId);
            return await EscalateAndPersistAsync(
                config, session, userMessage, "kill_switch_phrase", cancellationToken);
        }

        // Capa 2 — bucle de Function Calling
        userMessage = SanitizeInput(userMessage);

        var history = await _messageService.GetConversationHistoryAsync(conversationId);
        var clockSnapshot = await _businessClock.GetSnapshotAsync(config.BusinessId, cancellationToken);
        var temporal = _temporalReferenceBuilder.Build(clockSnapshot);
        var bookingPolicy = await _bookingPolicy.GetAsync(config.BusinessId, cancellationToken);
        var latestPayment = await _paymentLifecycle.GetLatestByConversationAsync(conversationId, cancellationToken);
        var systemPrompt = AgentTurnPromptContext.AppendTurnContext(
            config.SystemPrompt, config, history, temporal, session, bookingPolicy, latestPayment);
        var messages = BuildMessages(systemPrompt, history, userMessage);
        var turn = new AgentTurnExecution(config.ConsecutiveErrorEscalationThreshold);
        session.Turn = turn;

        var loop = await RunAgentLoopAsync(config, conversationId, messages, turn, session, cancellationToken);

        return loop.Kind switch
        {
            AgentLoopOutcome.OutcomeKind.Completed =>
                await FinalizeTurnAsync(
                    conversationId, userMessage, loop.Response!, session.ConversationState, turn, config, cancellationToken),

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
        CancellationToken ct)
    {
        var toolDefinitions = BuildToolDefinitions(config.EnabledToolNames);

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
                return AgentLoopOutcome.Completed(SanitizeResponse(result.Content ?? string.Empty));

            // FinishReason=ToolCalls → ejecutar y acumular resultados
            messages.Add(result.AssistantMessage);

            foreach (var toolCall in result.ToolCalls)
            {
                toolCtx.CurrentToolIteration = iteration;
                var outcome = await ExecuteToolCallAsync(toolCall, toolCtx, ct);

                turn.RecordToolOutcome(outcome);
                messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, outcome.RawJson));

                if (turn.ShouldAutoEscalate)
                {
                    _logger.LogWarning("Conv {ConvId}: {N} consecutive tool errors — auto-escalating",
                        conversationId, turn.ConsecutiveToolErrors);
                    return AgentLoopOutcome.AutoEscalate("consecutive_tool_errors");
                }
            }
        }

        return AgentLoopOutcome.Failed("Max iterations exceeded without final response.");
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

    private async Task<AgentTurnResult> FinalizeTurnAsync(
        Guid conversationId,
        string userMessage,
        string botResponse,
        ConversationState state,
        AgentTurnExecution turn,
        AgentConfig config,
        CancellationToken ct)
    {
        var finalResponse = _turnResponseComposer.Compose(
            config.SystemPrompt,
            botResponse,
            turn.FragmentEntries);

        await PersistTurnAsync(conversationId, userMessage, finalResponse, state, turn.ReservationCreated, ct);

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
        var escalateTool = _toolRegistry.Resolve("escalate_to_human");
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

        var activeReservation = await _reservationLifecycle.GetActiveAsync(conversationId, ct);
        var activePayment = await _paymentLifecycle.GetActiveByConversationAsync(conversationId, ct);

        var clockSnapshot = await _businessClock.GetSnapshotAsync(config.BusinessId, ct);

        return new AgentToolContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = conversationId,
            BusinessToday = clockSnapshot.Today,
            BusinessNow = clockSnapshot.Now,
            ChannelPhone = channelPhone?.Trim() ?? conversation.UserNumber,
            EscalationContacts = config.EscalationContacts,
            ConversationState = state,
            Conversation = conversation,
            Facts = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase),
            ActiveReservation = activeReservation,
            ActivePayment = activePayment
        };
    }

    private IReadOnlyList<ChatToolDefinition> BuildToolDefinitions(IReadOnlyList<string> enabledNames) =>
        _toolRegistry.GetToolsForAgent(enabledNames)
            .Select(t => new ChatToolDefinition
            {
                Name           = t.Name,
                Description    = t.Description,
                ParametersJson = t.ParametersSchema
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

    private static bool IsKillSwitchPhrase(string message)
    {
        var lower = message.ToLowerInvariant();
        return KillSwitchPhrases.Any(phrase => lower.Contains(phrase));
    }

    private static string SanitizeInput(string message) =>
        message.Length > 4000 ? message[..4000].Trim() : message.Trim();

    private static string SanitizeResponse(string response) =>
        response.Length > 4096 ? response[..4096].Trim() : response.Trim();
}
