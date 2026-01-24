using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ConversationAgent : IConversationAgent
{
    private readonly OpenAIClient _openAIClient;
    private readonly string _textDeploymentName;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly IBusinessConfigurationService _businessConfigService;
    private readonly IDateTimeExtractorService _dateTimeExtractor;
    private readonly IAvailabilityService _availabilityService;
    private readonly IReservationIntentDetector _reservationIntentDetector;
    private readonly IIntentDetectorService _intentDetector;
    private readonly IConversationContextService _contextService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationAgent> _logger;
    private const int MaxIterations = 10; // Límite de seguridad para evitar loops infinitos

    public ConversationAgent(
        OpenAIClient openAIClient,
        string textDeploymentName,
        IToolDispatcher toolDispatcher,
        IBusinessConfigurationService businessConfigService,
        IDateTimeExtractorService dateTimeExtractor,
        IAvailabilityService availabilityService,
        IReservationIntentDetector reservationIntentDetector,
        IIntentDetectorService intentDetector,
        IConversationContextService contextService,
        IUnitOfWork unitOfWork,
        ILogger<ConversationAgent> logger)
    {
        _openAIClient = openAIClient;
        _textDeploymentName = textDeploymentName;
        _toolDispatcher = toolDispatcher;
        _businessConfigService = businessConfigService;
        _dateTimeExtractor = dateTimeExtractor;
        _availabilityService = availabilityService;
        _reservationIntentDetector = reservationIntentDetector;
        _intentDetector = intentDetector;
        _contextService = contextService;
        _unitOfWork = unitOfWork;
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
            // PASO 1: Obtener estado de conversación
            var conversationState = await _contextService.GetAsync(conversation.ConversationId);

            // PASO 2: Detectar intención usando el detector híbrido (reglas + heurísticas + IA controlada)
            var intentResult = _intentDetector.Detect(userMessage, conversationState);
            
            // Actualizar intención en el estado
            if (intentResult.Intent != IntentType.Unknown)
            {
                await _contextService.SetIntentAsync(conversation.ConversationId, intentResult.Intent);
            }

            _logger.LogInformation(
                "Intención detectada: {Intent}, ShouldCheckAvailability={ShouldCheck}, ShouldAllowReservation={ShouldAllow}",
                intentResult.Intent, intentResult.ShouldCheckAvailability, intentResult.ShouldAllowReservation);

            // Construir mensajes iniciales con contexto de intención inyectado
            var chatMessages = await BuildInitialMessagesAsync(
                businessId, 
                conversation, 
                userMessage, 
                intentResult,
                conversationState);

            // Obtener todas las herramientas disponibles sin limitaciones
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

                // Configurar opciones de chat
                // El modelo puede usar todas las herramientas disponibles cuando detecte intenciones
                var options = new ChatCompletionsOptions(_textDeploymentName, chatMessages)
                {
                    Temperature = 0.3f,
                    MaxTokens = 1000,
                    ToolChoice = functionDefinitions.Any() 
                        ? ChatCompletionsToolChoice.Auto // Permitir que el modelo use herramientas cuando sea apropiado
                        : ChatCompletionsToolChoice.None
                };

                // Agregar todas las herramientas disponibles
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

                        // BLOQUEO CRÍTICO: Validar si el backend permite ejecutar la tool
                        // Recalcular intentResult con el estado actualizado y el mensaje original
                        var (shouldBlock, updatedIntentResult) = await ShouldBlockToolExecutionAsync(
                            functionToolCall.Name,
                            conversation?.ConversationId,
                            userMessage,
                            intentResult);

                        if (shouldBlock.HasValue && shouldBlock.Value)
                        {
                            intentResult = updatedIntentResult;
                            var (errorMessage, logMessage, logPropertyName, logPropertyValue) = GetBlockingDetails(functionToolCall.Name, intentResult);
                            
                            if (functionToolCall.Name == "check_availability")
                            {
                                _logger.LogWarning(
                                    "La IA intentó llamar {ToolName} pero el backend lo bloqueó. Intent={Intent}, ShouldCheckAvailability={ShouldCheck}",
                                    functionToolCall.Name, intentResult?.Intent, intentResult?.ShouldCheckAvailability ?? false);
                            }
                            else if (functionToolCall.Name == "create_reservation")
                            {
                                _logger.LogWarning(
                                    "La IA intentó llamar {ToolName} pero el backend lo bloqueó. Intent={Intent}, ShouldAllowReservation={ShouldAllow}",
                                    functionToolCall.Name, intentResult?.Intent, intentResult?.ShouldAllowReservation ?? false);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "La IA intentó llamar {ToolName} pero el backend lo bloqueó. Intent={Intent}",
                                    functionToolCall.Name, intentResult?.Intent);
                            }

                            var blockedResult = new ToolCallResult
                            {
                                ToolCallId = functionToolCall.Id,
                                ToolName = functionToolCall.Name ?? string.Empty,
                                Content = JsonSerializer.Serialize(new
                                {
                                    success = false,
                                    error = errorMessage
                                }),
                                IsError = true,
                                ErrorMessage = $"Tool {functionToolCall.Name} bloqueada por el backend: condiciones no cumplidas"
                            };

                            var blockedToolResponseMessage = new ChatRequestToolMessage(blockedResult.Content, functionToolCall.Id);
                            chatMessages.Add(blockedToolResponseMessage);
                            continue;
                        }

                        // Pasar conversationId a las herramientas para que ellas completen sus propios parámetros
                        // Cada herramienta es responsable de validar y completar sus parámetros desde el contexto
                        var toolResult = await _toolDispatcher.ExecuteToolAsync(
                            businessId,
                            toolCallRequest,
                            conversation?.ConversationId,
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
        string userMessage,
        IntentDetectionResult? intentResult = null,
        ConversationStateModel? conversationState = null)
    {
        var messages = new List<ChatRequestMessage>();

        // 1. System Prompt principal (desde BD) + reglas estrictas
        var systemPrompt = await _businessConfigService.BuildSystemPromptAsync(businessId);
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatRequestSystemMessage(systemPrompt));
        }

        // 2. Inyectar información de intención detectada por el backend usando template desde SystemConfiguration
        if (intentResult != null)
        {
            var intentContext = await BuildIntentDetectionContextAsync(intentResult);
            if (!string.IsNullOrEmpty(intentContext))
            {
                messages.Add(new ChatRequestSystemMessage(intentContext));
            }
        }

        // 3. Inyectar estado de conversación (memoria del contexto)
        if (conversationState != null)
        {
            var memoryBlock = BuildMemoryBlock(conversationState);
            if (!string.IsNullOrEmpty(memoryBlock))
            {
                messages.Add(new ChatRequestSystemMessage(memoryBlock));
            }
        }

        // 5. Historial de mensajes recientes (últimos 10 para mantener contexto)
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

        // 6. Mensaje actual del usuario
        messages.Add(new ChatRequestUserMessage(userMessage));

        return messages;
    }

    /// <summary>
    /// Construye un bloque de memoria legible desde el estado de conversación.
    /// Formatea el ConversationState en un texto estructurado para el LLM.
    /// </summary>
    private string BuildMemoryBlock(ConversationStateModel state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ESTADO ACTUAL DE LA CONVERSACIÓN ===");
        sb.AppendLine();

        // Identidad del cliente
        if (!string.IsNullOrWhiteSpace(state.CustomerName) || 
            !string.IsNullOrWhiteSpace(state.Phone) || 
            !string.IsNullOrWhiteSpace(state.Email))
        {
            sb.AppendLine("IDENTIDAD DEL CLIENTE:");
            if (!string.IsNullOrWhiteSpace(state.CustomerName))
                sb.AppendLine($"- Nombre: {state.CustomerName}");
            if (!string.IsNullOrWhiteSpace(state.Phone))
                sb.AppendLine($"- Teléfono: {state.Phone}");
            if (!string.IsNullOrWhiteSpace(state.Email))
                sb.AppendLine($"- Email: {state.Email}");
            sb.AppendLine();
        }

        // Intenciones
        if (state.CurrentIntent != IntentType.Unknown || state.LastIntent != IntentType.Unknown)
        {
            sb.AppendLine("INTENCIONES:");
            if (state.CurrentIntent != IntentType.Unknown)
                sb.AppendLine($"- Intención actual: {state.CurrentIntent}");
            if (state.LastIntent != IntentType.Unknown && state.LastIntent != state.CurrentIntent)
                sb.AppendLine($"- Intención anterior: {state.LastIntent}");
            sb.AppendLine();
        }


        // Programación deseada
        if (state.DesiredDate.HasValue || state.DesiredTime.HasValue || state.DurationMinutes.HasValue || !string.IsNullOrWhiteSpace(state.Service))
        {
            sb.AppendLine("PROGRAMACIÓN DESEADA:");
            if (!string.IsNullOrWhiteSpace(state.Service))
                sb.AppendLine($"- Servicio: {state.Service}");
            if (state.DesiredDate.HasValue)
                sb.AppendLine($"- Fecha: {state.DesiredDate.Value:yyyy-MM-dd}");
            if (state.DesiredTime.HasValue)
                sb.AppendLine($"- Hora: {state.DesiredTime.Value:HH\\:mm}");
            if (state.DurationMinutes.HasValue)
                sb.AppendLine($"- Duración: {state.DurationMinutes} minutos");
            sb.AppendLine();
        }

        // Atributos dinámicos (campos personalizados del negocio)
        if (state.Attributes != null && state.Attributes.Any())
        {
            sb.AppendLine("INFORMACIÓN ADICIONAL:");
            foreach (var attr in state.Attributes)
            {
                sb.AppendLine($"- {attr.Key}: {attr.Value}");
            }
            sb.AppendLine();
        }

        // Estado de disponibilidad
        if (state.AvailabilityChecked)
        {
            sb.AppendLine("DISPONIBILIDAD:");
            sb.AppendLine($"- Verificada: Sí");
            if (state.LastAvailabilityResult.HasValue)
                sb.AppendLine($"- Resultado: {(state.LastAvailabilityResult.Value ? "Disponible" : "No disponible")}");
            if (state.LastAvailabilityCheckAt.HasValue)
                sb.AppendLine($"- Última verificación: {state.LastAvailabilityCheckAt.Value:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
        }

        // Estado de reserva
        if (state.ReservationConfirmed)
        {
            sb.AppendLine("RESERVA:");
            sb.AppendLine("- Estado: Confirmada");
            if (!string.IsNullOrWhiteSpace(state.ReservationId))
                sb.AppendLine($"- ID de reserva: {state.ReservationId}");
            sb.AppendLine();
        }

        sb.AppendLine("=== FIN DEL ESTADO ===");
        return sb.ToString();
    }


    /// <summary>
    /// Determina si una tool debe ser bloqueada basándose en la validación del backend.
    /// </summary>
    /// <param name="toolName">Nombre de la tool a validar</param>
    /// <param name="conversationId">ID de la conversación (opcional)</param>
    /// <param name="userMessage">Mensaje original del usuario</param>
    /// <param name="currentIntentResult">Resultado de intención actual (puede ser null)</param>
    /// <returns>Tupla con (bool? shouldBlock, IntentDetectionResult? updatedIntentResult)</returns>
    private async Task<(bool? shouldBlock, IntentDetectionResult? updatedIntentResult)> ShouldBlockToolExecutionAsync(
        string? toolName,
        Guid? conversationId,
        string userMessage,
        IntentDetectionResult? currentIntentResult)
    {
        var updatedIntentResult = currentIntentResult;

        // Solo validar check_availability y create_reservation
        if (toolName != "check_availability" && toolName != "create_reservation")
        {
            return (null, updatedIntentResult);
        }

        // Recalcular intentResult con el estado actualizado y el mensaje original
        if (conversationId.HasValue)
        {
            var currentState = await _contextService.GetAsync(conversationId.Value);
            updatedIntentResult = _intentDetector.Detect(userMessage, currentState);
        }

        if (updatedIntentResult == null)
        {
            return (null, updatedIntentResult);
        }

        // Validar según el tipo de tool
        if (toolName == "check_availability")
        {
            return (!updatedIntentResult.ShouldCheckAvailability, updatedIntentResult);
        }
        else if (toolName == "create_reservation")
        {
            return (!updatedIntentResult.ShouldAllowReservation, updatedIntentResult);
        }

        return (null, updatedIntentResult);
    }

    /// <summary>
    /// Obtiene los detalles del bloqueo (mensaje de error, log, etc.) para una tool específica.
    /// </summary>
    private (string errorMessage, string logMessage, string logPropertyName, object logPropertyValue) GetBlockingDetails(
        string? toolName,
        IntentDetectionResult? intentResult)
    {
        if (toolName == "check_availability")
        {
            return (
                "No se puede verificar disponibilidad. Faltan datos necesarios o la solicitud no es válida.",
                "Intent={Intent}, ShouldCheckAvailability={ShouldCheck}",
                "ShouldCheck",
                intentResult?.ShouldCheckAvailability ?? false
            );
        }
        else if (toolName == "create_reservation")
        {
            return (
                "No se puede crear la reserva. Faltan datos necesarios o no hay disponibilidad confirmada.",
                "Intent={Intent}, ShouldAllowReservation={ShouldAllow}",
                "ShouldAllow",
                intentResult?.ShouldAllowReservation ?? false
            );
        }

        return (
            "La tool no puede ser ejecutada. Condiciones no cumplidas.",
            "Intent={Intent}",
            "Intent",
            intentResult?.Intent.ToString() ?? "Unknown"
        );
    }

    /// <summary>
    /// Construye el contexto de intención detectada usando template desde SystemConfiguration.
    /// </summary>
    private async Task<string> BuildIntentDetectionContextAsync(IntentDetectionResult intentResult)
    {
        try
        {
            var template = await _businessConfigService.GetSystemConfigurationAsync(
                Domain.Enums.SystemConfigurationKey.IntentDetectionContextTemplate);

            // Si no hay template configurado, retornar string vacío
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogWarning("Template de contexto de intención detectada no encontrado en SystemConfiguration");
                return string.Empty;
            }

            // Reemplazar placeholders con valores reales
            return template
                .Replace("{Intent}", intentResult.Intent.ToString())
                .Replace("{ShouldAllowReservation}", intentResult.ShouldAllowReservation.ToString())
                .Replace("{HasDate}", intentResult.HasDate.ToString())
                .Replace("{IsExplicitConfirmation}", intentResult.IsExplicitConfirmation.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al construir contexto de intención detectada");
            return string.Empty;
        }
    }
}
