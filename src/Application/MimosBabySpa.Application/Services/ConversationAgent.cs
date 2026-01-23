using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
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
    private readonly IConversationStateService _stateService;
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
        IConversationStateService stateService,
        IUnitOfWork unitOfWork,
        ILogger<ConversationAgent> logger)
    {
        _openAIClient = openAIClient;
        _textDeploymentName = textDeploymentName;
        _toolDispatcher = toolDispatcher;
        _businessConfigService = businessConfigService;
        _dateTimeExtractor = dateTimeExtractor;
        _availabilityService = availabilityService;
        _stateService = stateService;
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
            // REFACTORIZADO: Detectar fecha/hora manualmente desde backend
            var extractedDate = _dateTimeExtractor.ExtractDate(userMessage);
            var extractedTime = _dateTimeExtractor.ExtractTime(userMessage);
            string? availabilityContext = null;

            // Si se detecta fecha/hora, verificar disponibilidad AUTOMÁTICAMENTE desde backend
            if (extractedDate.HasValue)
            {
                _logger.LogInformation(
                    "Fecha detectada en mensaje: {Date}. Verificando disponibilidad automáticamente.",
                    extractedDate.Value.ToString("yyyy-MM-dd"));

                // Obtener servicio del contexto si está disponible
                var serviceContext = await GetContextValueAsync(conversation.ConversationId, "service");
                var service = serviceContext ?? "Servicio"; // Valor por defecto si no hay contexto

                // Obtener duración del servicio (podría venir del contexto o ser un valor por defecto)
                // Por ahora usamos un valor por defecto, pero esto debería venir de la configuración del negocio
                int? durationMinutes = 60; // Valor por defecto

                var availabilityResult = await _availabilityService.CheckAvailabilityAsync(
                    businessId,
                    service,
                    extractedDate.Value,
                    extractedTime,
                    durationMinutes,
                    cancellationToken);

                // Construir contexto de disponibilidad para inyectar en el prompt
                availabilityContext = $@"
INFORMACIÓN DE DISPONIBILIDAD (CALCULADA POR EL SISTEMA - NO INFERIR):
- Fecha consultada: {extractedDate.Value:yyyy-MM-dd}
- Hora consultada: {(extractedTime.HasValue ? extractedTime.Value.ToString(@"hh\:mm") : "No especificada")}
- ¿Está disponible? {availabilityResult.IsAvailable}
- Capacidad máxima: {availabilityResult.MaxCapacity}
- Reservas actuales: {availabilityResult.CurrentReservations}
- Mensaje del sistema: {availabilityResult.Message}

IMPORTANTE: Usa estos valores EXACTOS. NO infieras disponibilidad. NO apliques reglas propias.
Si '¿Está disponible?' es 'False', el horario NO está disponible. Si es 'True', está disponible.";

                _logger.LogInformation(
                    "Disponibilidad verificada automáticamente: IsAvailable={IsAvailable}",
                    availabilityResult.IsAvailable);
            }

            // Construir mensajes iniciales con contexto de disponibilidad inyectado
            var chatMessages = await BuildInitialMessagesAsync(
                businessId, 
                conversation, 
                userMessage, 
                availabilityContext);

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

    private async Task<string?> GetContextValueAsync(Guid conversationId, string field)
    {
        try
        {
            var context = await _unitOfWork.ConversationContexts.GetByConversationIdAndFieldAsync(conversationId, field);
            return context?.Value;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<ChatRequestMessage>> BuildInitialMessagesAsync(
        Guid businessId,
        Conversation conversation,
        string userMessage,
        string? availabilityContext = null)
    {
        var messages = new List<ChatRequestMessage>();

        // 1. System Prompt principal (desde BD) + reglas estrictas
        var systemPrompt = await _businessConfigService.BuildSystemPromptAsync(businessId);
        
        // Agregar reglas estrictas sobre disponibilidad y reservas
        var strictRules = @"

=== REGLAS CRÍTICAS DE DISPONIBILIDAD Y RESERVAS ===

1. DISPONIBILIDAD (CALCULADA POR EL BACKEND):
   - NUNCA infieras disponibilidad. Solo usa el valor 'is_available' proporcionado por el sistema.
   - Si el sistema dice 'is_available=false', el horario NO está disponible. Punto.
   - Si el sistema dice 'is_available=true', el horario está disponible. Punto.
   - El backend ya calculó conflictos de recursos y reglas de coexistencia.
   - NO cuentes reservas manualmente. NO compares cupos. NO apliques reglas propias.
   - NO expliques coexistencia ni conflictos de recursos al cliente.

2. DETECCIÓN DE INTENCIÓN DE RESERVA:
   - Cuando el cliente exprese claramente que quiere reservar, confirmar, agendar o hacer una cita, DEBES llamar inmediatamente la herramienta 'create_reservation'.
   - Señales de intención de reserva incluyen:
     * ""Quiero reservar"", ""Quiero agendar"", ""Quiero hacer una cita""
     * ""Confirmo"", ""Sí, confirma"", ""Perfecto, reserva""
     * ""Sí, quiero ese horario"", ""Me parece bien"", ""Perfecto""
     * ""Reserva para [fecha/hora]"", ""Agenda para [fecha/hora]""
     * Cualquier confirmación explícita después de mostrar disponibilidad
   - ANTES de llamar 'create_reservation', asegúrate de tener:
     * Servicio (del contexto o del mensaje)
     * Fecha (del contexto o del mensaje)
     * Hora (del contexto o del mensaje)
     * Duración (de BusinessInformation en el prompt)
   - Si falta información, pregunta amablemente antes de crear la reserva.

3. CREACIÓN DE RESERVAS:
   - Cuando llames 'create_reservation', incluye TODA la información adicional del cliente en el campo 'notes' como JSON string.
   - Ejemplo de notes: '{\""customerName\"":\""Juan Pérez\"",\""phone\"":\""+1234567890\"",\""age\"":\""6 meses\""}'
   - NUNCA confirmes una reserva sin haber recibido 'success=true' del backend.
   - Si recibes 'success=false', informa al usuario que el horario no está disponible.
   - NO asumas que una reserva fue creada. Solo confirma si el backend lo confirma.
   - Si recibes 'success=true', confirma la reserva al cliente con todos los detalles.

4. HERRAMIENTAS DISPONIBLES:
   - 'update_conversation_state': Úsala para guardar información del cliente (nombre, teléfono, edad del bebé, servicio, fecha, hora). Cuando uses estos campos en las notes de create_reservation, traduce los nombres al español: customerName→nombreCliente, phone→telefono, babyAgeMonths→edadBebeMeses.
   - 'create_reservation': Úsala cuando el cliente exprese claramente que quiere reservar o confirmar una cita. Si incluyes información del contexto en notes, traduce los nombres de campos al español.
   - 'check_availability': El sistema la llama automáticamente cuando detecta fecha/hora. NO la llames manualmente.

5. TU ROL:
   - Eres un asistente conversacional amigable y natural.
   - Presentas información de disponibilidad que el sistema calcula.
   - Ayudas a recolectar información del cliente.
   - Detectas cuando el cliente quiere reservar y llamas 'create_reservation' inmediatamente.
   - NO tomas decisiones de negocio. El backend lo hace.
   - NO uses frases negativas como ""lamentablemente"" o ""no puedo"".
   - Ofrece continuar con la reserva de manera natural cuando está disponible.

6. RESPUESTA AL CLIENTE:
   - Solo indica si el horario está disponible o no.
   - Nunca expliques conflictos ni coexistencia.
   - Nunca uses frases negativas.
   - Cuando el cliente confirme, llama inmediatamente 'create_reservation'.
   - Después de crear la reserva exitosamente, confirma con todos los detalles.

";

        var fullSystemPrompt = (systemPrompt ?? "") + strictRules;
        if (!string.IsNullOrEmpty(fullSystemPrompt))
        {
            messages.Add(new ChatRequestSystemMessage(fullSystemPrompt));
        }

        // 2. Inyectar contexto de disponibilidad si existe
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

        // 4. Mensaje actual del usuario
        messages.Add(new ChatRequestUserMessage(userMessage));

        return messages;
    }
}
