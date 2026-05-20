using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Orquestador del agente. Punto de entrada único para procesar mensajes de usuario.
///
/// Responsabilidades:
///   - Capa 1: comprobar Owner=Human y kill-switch de intenciones críticas.
///   - Capa 2: bucle de Function Calling con límite MaxToolIterations,
///             contador de errores consecutivos y auto-escalación.
///   - Capa 5: sanitización de respuesta y logging de métricas.
///
/// NO toma decisiones de negocio — las tools y los servicios son la autoridad.
/// </summary>
public sealed class AgentConversationService : IAgentConversationService
{
    private readonly IAgentConfigProvider _configProvider;
    private readonly IChatClient _chatClient;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IEscalationConfigProvider _escalationConfig;
    private readonly ILogger<AgentConversationService> _logger;

    // Capa 1 — kill-switch regex para frases de pánico (evita llamada al LLM por seguridad de costos)
    private static readonly string[] CriticalEscalationPhrases =
    [
        "quiero hablar con un humano", "quiero hablar con una persona",
        "agente real", "operador", "hablar con alguien",
        "escalate", "human agent", "hablar con ustedes",
        "estoy muy molest", "queja formal", "voy a demandar"
    ];

    public AgentConversationService(
        IAgentConfigProvider configProvider,
        IChatClient chatClient,
        AgentToolRegistry toolRegistry,
        IConversationStateManager stateManager,
        IMessageService messageService,
        IEscalationConfigProvider escalationConfig,
        ILogger<AgentConversationService> logger)
    {
        _configProvider = configProvider;
        _chatClient = chatClient;
        _toolRegistry = toolRegistry;
        _stateManager = stateManager;
        _messageService = messageService;
        _escalationConfig = escalationConfig;
        _logger = logger;
    }

    public async Task<AgentTurnResult> ProcessMessageAsync(
        Guid agentId,
        Guid conversationId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        // ── Carga inicial ────────────────────────────────────────────────────
        var config = await _configProvider.GetConfigAsync(agentId, cancellationToken);

        var state = await _stateManager.GetOrCreateStateAsync(
            conversationId, config.BusinessId, string.Empty, cancellationToken);

        // ── Capa 1: handover humano — corto-circuito sin LLM ────────────────
        if (state.Owner == ConversationOwner.Human)
        {
            _logger.LogInformation("Conv {ConvId}: Owner=Human, skipping bot processing", conversationId);
            return AgentTurnResult.Ok(string.Empty);
        }

        // ── Capa 1: kill-switch regex para intenciones de pánico ─────────────
        if (IsKillSwitchPhrase(userMessage))
        {
            _logger.LogInformation("Conv {ConvId}: kill-switch triggered — auto-escalating", conversationId);
            return await HandleAutoEscalateAsync(config, state, conversationId, userMessage,
                "kill_switch_phrase", cancellationToken);
        }

        // ── Input sanitization ────────────────────────────────────────────────
        userMessage = SanitizeInput(userMessage);

        // ── Historial de conversación ─────────────────────────────────────────
        var history = await _messageService.GetConversationHistoryAsync(conversationId);
        var messages = BuildMessages(config.SystemPrompt, history, userMessage);

        // ── Tools habilitadas para el agente ──────────────────────────────────
        var enabledTools = _toolRegistry.GetToolsForAgent(config.EnabledToolNames);
        var toolDefinitions = enabledTools.Select(t => new ChatToolDefinition
        {
            Name = t.Name,
            Description = t.Description,
            ParametersJson = t.ParametersSchema
        }).ToList();

        // ── Contexto compartido entre tools del turno ─────────────────────────
        var toolCtx = new AgentToolContext
        {
            AgentId = agentId,
            BusinessId = config.BusinessId,
            ConversationId = conversationId,
            CustomerPhone = state.Phone,
            CustomerName = state.CustomerName,
            EscalationContacts = config.EscalationContacts
        };

        // ── Bucle de Function Calling (Capa 2) ────────────────────────────────
        var callOptions = new ChatCompletionOptions
        {
            Temperature = config.Temperature,
            MaxTokens = 800
        };

        int totalTokens = 0;
        int toolCallCount = 0;
        bool escalated = false;
        bool reservationCreated = false;

        for (int iteration = 0; iteration <= config.MaxToolIterations; iteration++)
        {
            toolCtx.CurrentToolIteration = iteration;

            bool forceText = iteration >= config.MaxToolIterations;
            var iterOptions = forceText
                ? new ChatCompletionOptions { Temperature = config.Temperature, MaxTokens = 800, ForceTextResponse = true }
                : callOptions;

            if (forceText)
                _logger.LogWarning("Conv {ConvId}: MaxToolIterations {Max} reached — forcing text response", conversationId, config.MaxToolIterations);

            var result = await _chatClient.CompleteAsync(messages, forceText ? null : toolDefinitions, iterOptions, cancellationToken);

            totalTokens += result.PromptTokens + result.CompletionTokens;

            if (!result.Success)
            {
                _logger.LogError("Conv {ConvId}: LLM error: {Err}", conversationId, result.ErrorMessage);
                return AgentTurnResult.Fail($"LLM error: {result.ErrorMessage}");
            }

            // FinishReason=Stop → respuesta final
            if (result.FinishReason == ChatCompletionFinishReason.Stop ||
                result.FinishReason == ChatCompletionFinishReason.Length)
            {
                var finalResponse = SanitizeResponse(result.Content ?? string.Empty);
                await PersistTurnAsync(conversationId, userMessage, finalResponse, state,
                    reservationCreated, cancellationToken);

                _logger.LogInformation(
                    "Conv {ConvId}: turn complete. tokens={Tokens}, tools={Tools}, escalated={Esc}",
                    conversationId, totalTokens, toolCallCount, escalated);

                return AgentTurnResult.Ok(finalResponse,
                    escalated, reservationCreated, totalTokens, toolCallCount);
            }

            // FinishReason=ToolCalls → ejecutar tools
            messages.Add(result.AssistantMessage);

            foreach (var toolCall in result.ToolCalls)
            {
                toolCallCount++;
                _logger.LogInformation(
                    "Conv {ConvId}: tool_call [{Id}] {Name}({Args})",
                    conversationId, toolCall.Id, toolCall.FunctionName, toolCall.ArgumentsJson);

                var tool = _toolRegistry.Resolve(toolCall.FunctionName);
                string toolResult;

                if (tool is null)
                {
                    toolResult = ToolError("tool_not_found",
                        $"Tool '{toolCall.FunctionName}' is not registered.",
                        "Check the available tool names.");
                    toolCtx.ConsecutiveToolErrors++;
                }
                else
                {
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
                        toolResult = await tool.ExecuteAsync(argsDoc.RootElement, toolCtx, cancellationToken);

                        // Detectar si la tool retornó error estructurado
                        if (IsToolError(toolResult))
                            toolCtx.ConsecutiveToolErrors++;
                        else
                            toolCtx.ConsecutiveToolErrors = 0;

                        // Detectar reserva creada para propagar al canal
                        if (toolCall.FunctionName == "create_reservation" && !IsToolError(toolResult))
                            reservationCreated = true;

                        // Detectar escalación
                        if (toolCall.FunctionName == "escalate_to_human" && !IsToolError(toolResult))
                            escalated = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Conv {ConvId}: tool {Tool} threw exception", conversationId, toolCall.FunctionName);
                        toolResult = ToolError("tool_exception", ex.Message, "Try again or escalate.");
                        toolCtx.ConsecutiveToolErrors++;
                    }
                }

                _logger.LogInformation(
                    "Conv {ConvId}: tool_result [{Id}] {Name} → {Result}",
                    conversationId, toolCall.Id, toolCall.FunctionName,
                    toolResult.Length > 200 ? toolResult[..200] + "…" : toolResult);

                messages.Add(ChatMessage.Tool(toolCall.Id, toolCall.FunctionName, toolResult));

                // ── Capa 2: auto-escalación por errores consecutivos ──────────
                if (toolCtx.ConsecutiveToolErrors >= config.ConsecutiveErrorEscalationThreshold)
                {
                    _logger.LogWarning(
                        "Conv {ConvId}: {N} consecutive tool errors — auto-escalating",
                        conversationId, toolCtx.ConsecutiveToolErrors);

                    return await HandleAutoEscalateAsync(config, state, conversationId, userMessage,
                        "consecutive_tool_errors", cancellationToken);
                }
            }
        }

        // No debería llegar aquí, pero como seguro
        return AgentTurnResult.Fail("Max iterations exceeded without final response.");
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

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

    private async Task<AgentTurnResult> HandleAutoEscalateAsync(
        AgentConfig config,
        ConversationState state,
        Guid conversationId,
        string userMessage,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var escalateTool = _toolRegistry.Resolve("escalate_to_human");
            if (escalateTool is not null)
            {
                var toolCtx = new AgentToolContext
                {
                    AgentId = config.AgentId,
                    BusinessId = config.BusinessId,
                    ConversationId = conversationId,
                    CustomerPhone = state.Phone,
                    CustomerName = state.CustomerName
                };

                var argsJson = JsonSerializer.Serialize(new { reason, last_user_message = userMessage });
                using var doc = JsonDocument.Parse(argsJson);
                await escalateTool.ExecuteAsync(doc.RootElement, toolCtx, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conv {ConvId}: auto-escalate tool failed", conversationId);
        }

        const string escalateMsg = "Te estamos comunicando con un agente humano. En breve te atenderán.";
        await PersistTurnAsync(conversationId, userMessage, escalateMsg, state, false, ct);
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
        if (reservationCreated) state.ReservationCreated = true;

        await _stateManager.SaveStateAsync(conversationId, state, ct);
    }

    private static bool IsKillSwitchPhrase(string message)
    {
        var lower = message.ToLowerInvariant();
        return CriticalEscalationPhrases.Any(phrase => lower.Contains(phrase));
    }

    private static string SanitizeInput(string message)
    {
        if (message.Length > 4000)
            message = message[..4000];
        return message.Trim();
    }

    private static string SanitizeResponse(string response)
    {
        if (response.Length > 4096)
            response = response[..4096];
        return response.Trim();
    }

    private static string ToolError(string code, string message, string hint) =>
        JsonSerializer.Serialize(new { ok = false, error = new { code, message, hint } });

    private static bool IsToolError(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() == false;
        }
        catch { return false; }
    }
}
