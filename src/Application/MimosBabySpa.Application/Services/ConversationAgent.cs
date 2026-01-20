using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

public class ConversationAgent : IConversationAgent
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _textDeploymentName;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly IBusinessConfigurationService _businessConfigService;
    private readonly ILogger<ConversationAgent> _logger;
    private const int MaxIterations = 10; // Límite de seguridad para evitar loops infinitos

    public ConversationAgent(
        OpenAIClient openAIClient,
        string textDeploymentName,
        IToolDispatcher toolDispatcher,
        IBusinessConfigurationService businessConfigService,
        ILogger<ConversationAgent> logger)
    {
        _openAIClient = openAIClient;
        _textDeploymentName = textDeploymentName;
        _toolDispatcher = toolDispatcher;
        _businessConfigService = businessConfigService;
        _logger = logger;
    }

    public async Task<string> ProcessMessageAsync(
        Guid businessId,
        string userMessage,
        Conversation conversation,
        Lead? lead,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Construir mensajes iniciales
            var chatMessages = await BuildInitialMessagesAsync(businessId, conversation, userMessage);

            // Obtener herramientas disponibles
            var tools = _toolDispatcher.GetAvailableTools();
            var functionDefinitions = tools.Select(t => new ChatCompletionsFunctionToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = BinaryData.FromString(JsonSerializer.Serialize(t.ParametersSchema.RootElement))
            }).ToList();

            var iteration = 0;
            while (iteration < MaxIterations)
            {
                iteration++;

                // Configurar opciones de chat con tools
                var options = new ChatCompletionsOptions(_textDeploymentName, chatMessages)
                {
                    Temperature = 0.3f, // Reducida para mayor precisión en el uso de tools
                    MaxTokens = 1000,
                    ToolChoice = ChatCompletionsToolChoice.Auto // Asegurar que las tools estén disponibles
                };

                // Agregar function definitions
                foreach (var functionDef in functionDefinitions)
                {
                    options.Tools.Add(functionDef);
                }

                // Llamar a OpenAI
                var response = await _openAIClient.GetChatCompletionsAsync(options, cancellationToken);
                var choice = response.Value.Choices[0];
                var message = choice.Message;

                // Agregar mensaje del asistente al historial
                var assistantMessage = new ChatRequestAssistantMessage(message.Content ?? string.Empty);
                if (message.ToolCalls != null && message.ToolCalls.Any())
                {
                    foreach (var toolCall in message.ToolCalls)
                    {
                        if (toolCall is ChatCompletionsFunctionToolCall functionToolCall)
                        {
                            assistantMessage.ToolCalls.Add(functionToolCall);
                        }
                    }
                }
                chatMessages.Add(assistantMessage);

                // Si no hay tool calls, retornar la respuesta final
                if (message.ToolCalls == null || !message.ToolCalls.Any())
                {
                    var finalResponse = message.Content ?? "Disculpa, no pude generar una respuesta.";
                    _logger.LogInformation("Agente completó procesamiento en {Iteration} iteraciones", iteration);
                    return finalResponse;
                }

                // Ejecutar tools solicitadas
                foreach (var toolCall in message.ToolCalls)
                {
                    if (toolCall is ChatCompletionsFunctionToolCall functionToolCall)
                    {
                        var toolCallRequest = new ToolCallRequest
                        {
                            Id = functionToolCall.Id,
                            Name = functionToolCall.Name ?? string.Empty,
                            Arguments = functionToolCall.Arguments != null 
                                ? JsonSerializer.Deserialize<JsonElement>(functionToolCall.Arguments.ToString() ?? "{}")
                                : null
                        };

                        // Inyectar conversationId automáticamente para update_conversation_state
                        if (functionToolCall.Name == "update_conversation_state" && conversation != null)
                        {
                            var argsJson = toolCallRequest.Arguments.HasValue 
                                ? toolCallRequest.Arguments.Value.GetRawText() 
                                : "{}";
                            
                            var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson) 
                                ?? new Dictionary<string, object>();
                            
                            argsDict["conversationId"] = conversation.ConversationId.ToString();
                            toolCallRequest.Arguments = JsonSerializer.Deserialize<JsonElement>(
                                JsonSerializer.Serialize(argsDict));
                        }

                        var toolResult = await _toolDispatcher.ExecuteToolAsync(
                            businessId,
                            toolCallRequest,
                            cancellationToken);

                        // Agregar resultado de la tool al historial
                        var toolResponseMessage = new ChatRequestToolMessage(toolResult.Content, functionToolCall.Id);
                        chatMessages.Add(toolResponseMessage);

                        _logger.LogInformation(
                            "Tool {ToolName} ejecutada. Resultado: {IsError}",
                            functionToolCall.Name,
                            toolResult.IsError ? $"Error: {toolResult.ErrorMessage}" : "Éxito");
                    }
                }
            }

            // Si llegamos aquí, se alcanzó el límite de iteraciones
            _logger.LogWarning("Se alcanzó el límite de iteraciones ({MaxIterations})", MaxIterations);
            return "Disculpa, estoy teniendo dificultades procesando tu solicitud. ¿Podrías reformular tu pregunta?";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en ConversationAgent.ProcessMessageAsync");
            return "Disculpa, estoy teniendo dificultades técnicas. ¿Podrías repetir tu pregunta?";
        }
    }

    private async Task<List<ChatRequestMessage>> BuildInitialMessagesAsync(
        Guid businessId,
        Conversation conversation,
        string userMessage)
    {
        var messages = new List<ChatRequestMessage>();

        // 1. System Prompt principal (desde BD)
        var systemPrompt = await _businessConfigService.BuildSystemPromptAsync(businessId);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatRequestSystemMessage(systemPrompt));
        }

        // 2. Historial de mensajes recientes (últimos 10 para mantener contexto)
        if (conversation?.Messages != null && conversation.Messages.Any())
        {
            var recentMessages = conversation.Messages
                .OrderByDescending(m => m.Timestamp)
                .Take(10)
                .OrderBy(m => m.Timestamp)
                .ToList();

            foreach (var msg in recentMessages)
            {
                if (msg.Sender == "User")
                {
                    messages.Add(new ChatRequestUserMessage(msg.MessageText));
                }
                else if (msg.Sender == "Bot" || msg.Sender == "Assistant")
                {
                    messages.Add(new ChatRequestAssistantMessage(msg.MessageText));
                }
            }
        }

        // 4. Mensaje actual del usuario
        messages.Add(new ChatRequestUserMessage(userMessage));

        return messages;
    }
}
