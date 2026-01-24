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

            string? availabilityContext = null;
            bool? lastAvailabilityResult = null;

            // PASO 3: Verificar disponibilidad SOLO si el detector lo indica
            if (intentResult.ShouldCheckAvailability && intentResult.HasDate)
            {
                var extractedDate = DateTime.Parse(intentResult.DetectedDateRaw!);
                TimeSpan? extractedTime = null;
                
                if (intentResult.HasTime && !string.IsNullOrWhiteSpace(intentResult.DetectedTimeRaw))
                {
                    extractedTime = TimeSpan.Parse(intentResult.DetectedTimeRaw);
                }

                // Obtener servicio del estado
                var service = conversationState.PrimaryEntity ?? "Servicio";
                int? durationMinutes = conversationState.DurationMinutes ?? 60;

                var availabilityResult = await _availabilityService.CheckAvailabilityAsync(
                    businessId,
                    service,
                    extractedDate,
                    extractedTime,
                    durationMinutes,
                    cancellationToken);

                lastAvailabilityResult = availabilityResult.IsAvailable;

                // Guardar disponibilidad en el estado
                await _contextService.SetAvailabilityAsync(conversation.ConversationId, availabilityResult.IsAvailable);
                
                // Guardar fecha y hora en el estado
                var dateOnly = DateOnly.FromDateTime(extractedDate);
                TimeOnly? timeOnly = extractedTime.HasValue ? TimeOnly.FromTimeSpan(extractedTime.Value) : null;
                await _contextService.SetScheduleAsync(conversation.ConversationId, dateOnly, timeOnly, durationMinutes);

                // Construir contexto de disponibilidad usando template desde SystemConfiguration
                availabilityContext = await BuildAvailabilityContextAsync(
                    extractedDate,
                    extractedTime,
                    availabilityResult);

                _logger.LogInformation(
                    "Disponibilidad verificada automáticamente: IsAvailable={IsAvailable}",
                    availabilityResult.IsAvailable);
            }

            // Obtener estado actualizado después de posibles cambios
            conversationState = await _contextService.GetAsync(conversation.ConversationId);

            // Construir mensajes iniciales con contexto de disponibilidad e intención inyectados
            var chatMessages = await BuildInitialMessagesAsync(
                businessId, 
                conversation, 
                userMessage, 
                availabilityContext,
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

                        // BLOQUEO CRÍTICO: Si el backend dice que NO se permite reserva, bloquear create_reservation
                        if (functionToolCall.Name == "create_reservation" && !intentResult.ShouldAllowReservation)
                        {
                            _logger.LogWarning(
                                "La IA intentó llamar create_reservation pero el backend lo bloqueó. " +
                                "Intent={Intent}, ShouldAllowReservation={ShouldAllow}",
                                intentResult.Intent, intentResult.ShouldAllowReservation);

                            // Crear resultado de error para la tool
                            var blockedResult = new ToolCallResult
                            {
                                ToolCallId = functionToolCall.Id,
                                ToolName = functionToolCall.Name ?? string.Empty,
                                Content = JsonSerializer.Serialize(new
                                {
                                    success = false,
                                    error = "No se puede crear la reserva. Faltan datos necesarios o no hay disponibilidad confirmada."
                                }),
                                IsError = true,
                                ErrorMessage = "Reserva bloqueada por el backend: condiciones no cumplidas"
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
        string? availabilityContext = null,
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

        // 3. Inyectar contexto de disponibilidad si existe
        if (!string.IsNullOrEmpty(availabilityContext))
        {
            messages.Add(new ChatRequestSystemMessage(availabilityContext));
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

        // 6. Mensaje actual del usuario
        messages.Add(new ChatRequestUserMessage(userMessage));

        return messages;
    }

    /// <summary>
    /// Construye el contexto de disponibilidad usando template desde SystemConfiguration.
    /// </summary>
    private async Task<string> BuildAvailabilityContextAsync(
        DateTime extractedDate,
        TimeSpan? extractedTime,
        AvailabilityResult availabilityResult)
    {
        try
        {
            var template = await _businessConfigService.GetSystemConfigurationAsync(
                Domain.Enums.SystemConfigurationKey.AvailabilityContextTemplate);

            // Si no hay template configurado, retornar string vacío
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogWarning("Template de contexto de disponibilidad no encontrado en SystemConfiguration");
                return string.Empty;
            }

            // Reemplazar placeholders con valores reales
            return template
                .Replace("{Date}", extractedDate.ToString("yyyy-MM-dd"))
                .Replace("{Time}", extractedTime.HasValue ? extractedTime.Value.ToString(@"hh\:mm") : "No especificada")
                .Replace("{IsAvailable}", availabilityResult.IsAvailable.ToString())
                .Replace("{CurrentReservations}", availabilityResult.CurrentReservations.ToString())
                .Replace("{Message}", availabilityResult.Message ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al construir contexto de disponibilidad");
            return string.Empty;
        }
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
                .Replace("{IsExplicitConfirmation}", intentResult.IsExplicitConfirmation.ToString())
                .Replace("{IsNarrativeDate}", intentResult.IsNarrativeDate.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al construir contexto de intención detectada");
            return string.Empty;
        }
    }
}
