using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Tools;

namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Orquestador Híbrido Transaccional - Implementación del "Hybrid Transactional Brain"
/// 
/// Este orquestador implementa la arquitectura con separación estricta de responsabilidades:
/// 
/// LLM LAYER:
/// - Comprensión de lenguaje natural
/// - Detección de intención
/// - Extracción de entidades
/// - Llamada a herramientas con (field, value)
/// 
/// FLOW ENGINE (BRAIN):
/// - Determina qué datos faltan
/// - Decide qué herramientas pueden ejecutarse
/// - Valida si se puede avanzar
/// - NO analiza texto del usuario
/// 
/// BACKEND:
/// - Única autoridad para disponibilidad
/// - Única autoridad para crear reservas
/// - Validación de reglas de negocio
/// - Asignación de recursos
/// 
/// PRINCIPIOS:
/// - El LLM NUNCA decide disponibilidad
/// - El LLM NUNCA confirma reservas
/// - El LLM NUNCA aplica reglas de negocio
/// - El LLM NUNCA inventa datos
/// - Todas las decisiones son determinísticas y auditables
/// </summary>
public class HybridTransactionalOrchestrator
{
    private readonly IConversationStateManager _stateManager;
    private readonly IFlowEngine _flowEngine;
    private readonly IBusinessRuleEngine _businessRuleEngine;
    private readonly CachedBusinessContextProvider _cachedContextProvider;
    private readonly IPromptProvider _systemPromptProvider;
    private readonly ILLMAdapter _llmAdapter;
    private readonly GenericToolDispatcher _toolDispatcher;
    private readonly ISmartExtractionService _extractionService;
    private readonly IMessageService _messageService; // ✅ Nuevo: Para historial conversacional
    private readonly ILogger<HybridTransactionalOrchestrator> _logger;

    public HybridTransactionalOrchestrator(
        IConversationStateManager stateManager,
        IFlowEngine flowEngine,
        IBusinessRuleEngine businessRuleEngine,
        CachedBusinessContextProvider cachedContextProvider,
        IPromptProvider systemPromptProvider,
        ILLMAdapter llmAdapter,
        GenericToolDispatcher toolDispatcher,
        ISmartExtractionService extractionService,
        IMessageService messageService, // ✅ Nuevo
        ILogger<HybridTransactionalOrchestrator> logger)
    {
        _stateManager = stateManager;
        _flowEngine = flowEngine;
        _businessRuleEngine = businessRuleEngine;
        _cachedContextProvider = cachedContextProvider;
        _systemPromptProvider = systemPromptProvider;
        _llmAdapter = llmAdapter;
        _toolDispatcher = toolDispatcher;
        _extractionService = extractionService;
        _messageService = messageService; // ✅ Nuevo
        _logger = logger;
    }

    /// <summary>
    /// Procesa un mensaje del usuario siguiendo el flujo Hybrid Transactional Brain.
    /// Refactorizado para ser más limpio y mantenible.
    /// </summary>
    public async Task<string> ProcessMessageAsync(
        Guid conversationId,
        Guid businessId,
        string customerPhone,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "=== INICIO === ConversationId={ConversationId}, BusinessId={BusinessId}",
                conversationId, businessId);

            // FASE 1: Cargar contexto unificado
            var context = await LoadContextAsync(
                conversationId, businessId, customerPhone, userMessage, cancellationToken);

            // FASE 2: Extraer información del mensaje
            var extraction = await ExtractInformationAsync(userMessage, context, cancellationToken);
            if (!extraction.Success)
            {
                _logger.LogWarning("Extracción falló, retornando respuesta de emergencia");
                return extraction.StructuredResponse.ConversationalResponse;
            }

            context.ExtractionResult = extraction;

            // FASE 3: Actualizar estado con datos extraídos
            await UpdateStateFromExtractionAsync(extraction, context, cancellationToken);

            // FASE 4: Ejecutar acciones de flujo (tools)
            await ExecuteFlowActionsAsync(context, cancellationToken);

            // FASE 5: Generar respuesta conversacional
            var response = await GenerateResponseAsync(
                userMessage, context, cancellationToken);

            // FASE 6: Guardar metadatos finales
            await SaveFinalMetadataAsync(
                context, userMessage, response, cancellationToken);

            _logger.LogInformation(
                "=== FIN === Respuesta: {Length} caracteres, Completitud: {Completeness}%",
                response.Length, context.FlowEvaluation.CompletenessPercentage);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en orquestador");
            return "Disculpa, ha ocurrido un error. Por favor intenta nuevamente.";
        }
    }

    // ========================================
    // MÉTODOS PRIVADOS - FASES DEL PROCESAMIENTO
    // ========================================

    /// <summary>
    /// FASE 1: Carga el contexto unificado para el procesamiento.
    /// ✅ REFACTORIZADO: Una sola carga de configuración usando BusinessContext.
    /// </summary>
    private async Task<ProcessingContext> LoadContextAsync(
        Guid conversationId,
        Guid businessId,
        string customerPhone,
        string userMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 1: Cargando contexto...");

        var startTime = DateTime.UtcNow;

        // ✅ UNA SOLA CARGA de toda la configuración del negocio (con caché)
        var businessContext = await _cachedContextProvider.GetOrLoadAsync(
            businessId, cancellationToken);

        // Cargar estado de conversación
        var state = await _stateManager.GetOrCreateStateAsync(
            conversationId, businessId, customerPhone, cancellationToken);

        // Construir prompt del sistema usando el provider
        var systemPrompt = await _systemPromptProvider.BuildAsync(
            businessContext, cancellationToken);

        var context = new ProcessingContext(
            state,
            businessContext.RequiredFields, // ✅ Ya calculado en BusinessContext
            systemPrompt,
            businessContext, // ✅ Pasar contexto completo
            _flowEngine,
            _stateManager,
            conversationId,
            businessId,
            customerPhone,
            userMessage);

        var elapsed = DateTime.UtcNow - startTime;
        
        _logger.LogInformation(
            "✅ Contexto cargado en {Elapsed}ms: Version={Version}, Completitud={Completeness}%",
            elapsed.TotalMilliseconds, state.Version, context.FlowEvaluation.CompletenessPercentage);

        return context;
    }

    /// <summary>
    /// FASE 2: Extrae información del mensaje del usuario.
    /// ✅ REFACTORIZADO: Pasa BusinessContext precargado al servicio de extracción.
    /// </summary>
    private async Task<ExtractionResult> ExtractInformationAsync(
        string userMessage,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 2: Extrayendo información...");

        // ✅ Pasar BusinessContext precargado (sin cargas adicionales)
        var extraction = await _extractionService.ExtractWithValidationAsync(
            userMessage, context.State, context.BusinessContext, cancellationToken);

        if (extraction.Success)
        {
            _logger.LogInformation(
                "Extracción exitosa: Campos={FieldCount}, Confidence={Confidence:F2}",
                extraction.StructuredResponse.ExtractedFields.Count,
                extraction.ValidationResult.Confidence);
        }

        return extraction;
    }

    /// <summary>
    /// FASE 3: Actualiza el estado con los datos extraídos.
    /// </summary>
    private async Task UpdateStateFromExtractionAsync(
        ExtractionResult extraction,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 3: Actualizando estado con {Count} campos extraídos...",
            extraction.StructuredResponse.ExtractedFields.Count);

        foreach (var field in extraction.StructuredResponse.ExtractedFields)
        {
            if (field.Confidence < 0.5)
            {
                _logger.LogDebug("Campo '{Field}' ignorado por baja confidence: {Confidence:F2}",
                    field.FieldName, field.Confidence);
                continue;
            }

            var result = await _toolDispatcher.ExecuteAsync(
                ToolType.UpdateConversationState,
                context.ToolContext,
                new Dictionary<string, object>
                {
                    { "field", field.FieldName },
                    { "value", field.Value }
                },
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "✓ {Field} = {Value} (confidence: {Confidence:F2})",
                    field.FieldName, field.Value, field.Confidence);
            }
        }

        // Recargar estado después de actualizaciones
        await context.ReloadAndEvaluateAsync(cancellationToken);
    }

    /// <summary>
    /// FASE 4: Ejecuta acciones de flujo (tools) basándose en la evaluación.
    /// ✅ REFACTORIZADO: Usa helper para eliminar duplicación del patrón Execute → Reload.
    /// </summary>
    private async Task ExecuteFlowActionsAsync(
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 4: Ejecutando acciones de flujo...");

        // Verificar disponibilidad (decisión simple basada en FlowEngine)
        if (context.FlowEvaluation.CanCheckAvailability)
        {
            _logger.LogInformation("Verificando disponibilidad...");
            
            await ExecuteToolAndReloadAsync(
                ToolType.CheckAvailability,
                context,
                cancellationToken);
            
            _logger.LogInformation(
                "Disponibilidad verificada: {Confirmed}",
                context.State.AvailabilityConfirmed);
        }

        // Confirmar reserva si el usuario lo indicó
        if (context.ExtractionResult?.StructuredResponse.FlowAnalysis.UserConfirmedBooking == true)
        {
            _logger.LogInformation("Usuario confirmó reserva");
            
            // Marcar confirmación en el estado
            context.State.ReservationConfirmed = true;
            context.State.UpdatedAt = DateTime.UtcNow;
            context.State.Version++;
            
            // Re-evaluar con confirmación
            context.ReEvaluate();
        }

        // Crear reserva (decisión simple basada en FlowEngine)
        if (context.FlowEvaluation.CanCreateReservation)
        {
            _logger.LogInformation("Creando reserva...");
            
            var result = await ExecuteToolAndReloadAsync(
                ToolType.CreateReservation,
                context,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Reserva creada exitosamente");
            }
        }
        else if (context.State.ReservationConfirmed)
        {
            _logger.LogWarning(
                "Usuario confirmó pero faltan requisitos. Missing: {Fields}",
                string.Join(", ", context.FlowEvaluation.MissingFields));
        }
    }

    /// <summary>
    /// Helper para ejecutar un tool y recargar el estado automáticamente.
    /// Elimina la duplicación del patrón "Execute → ReloadAndEvaluate".
    /// </summary>
    private async Task<ToolExecutionResult> ExecuteToolAndReloadAsync(
        ToolType toolType,
        ProcessingContext context,
        CancellationToken cancellationToken,
        Dictionary<string, object>? parameters = null)
    {
        var result = await _toolDispatcher.ExecuteAsync(
            toolType,
            context.ToolContext,
            parameters,
            cancellationToken);

        // Siempre recargar y re-evaluar después de ejecutar un tool
        await context.ReloadAndEvaluateAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// FASE 5: Genera la respuesta conversacional final.
    /// </summary>
    private async Task<string> GenerateResponseAsync(
        string userMessage,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 5: Generando respuesta...");

        try
        {
            return await GenerateConversationalResponseAsync(
                context.SystemPrompt,
                context.State,
                context.FlowEvaluation,
                userMessage,
                context.ExtractionResult!,
                new List<(string, ToolExecutionResult)>(), // Simplificado: no se usa realmente
                context.ToolContext.ConversationId, // ✅ Nuevo: Para cargar historial
                context.ToolContext.BusinessId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generando respuesta, usando fallback");
            return context.ExtractionResult?.StructuredResponse.ConversationalResponse 
                ?? "Disculpa, no pude procesar tu mensaje correctamente.";
        }
    }

    /// <summary>
    /// FASE 6: Guarda los metadatos finales (una sola vez).
    /// ✅ NOTA: Los mensajes se guardan por el llamador (webhook, tests, console).
    /// El orquestador solo lee el historial, no lo persiste (separación de responsabilidades).
    /// </summary>
    private async Task SaveFinalMetadataAsync(
        ProcessingContext context,
        string userMessage,
        string botResponse,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 6: Guardando metadatos finales...");

        // Recargar estado fresco por si tools lo modificaron
        await context.ReloadAndEvaluateAsync(cancellationToken);
        
        // Actualizar metadatos (LastUserMessage, LastBotMessage en el estado)
        context.UpdateMessageMetadata(userMessage, botResponse);
        
        // Guardar estado
        await context.SaveStateAsync(cancellationToken);
        
        _logger.LogDebug("Metadatos guardados");
    }

    // ========================================
    // MÉTODOS PRIVADOS - GENERACIÓN DE RESPUESTAS
    // ========================================

    /// <summary>
    /// Genera una respuesta conversacional usando el system prompt.
    /// Este método SIEMPRE se usa para generar respuestas finales al usuario.
    /// ✅ MEJORADO: Incluye historial conversacional para mantener coherencia natural.
    /// </summary>
    private async Task<string> GenerateConversationalResponseAsync(
        string systemPrompt,
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowEvaluation,
        string userMessage,
        ExtractionResult extractionResult,
        List<(string FunctionName, ToolExecutionResult Result)> toolResults,
        Guid conversationId, // ✅ Nuevo: Para cargar historial
        Guid businessId,
        CancellationToken cancellationToken)
    {
        var stateContext = BuildStateContext(state, flowEvaluation);
        var extractionContext = BuildExtractionContext(extractionResult);
        var toolResultsContext = BuildToolResultsContext(toolResults);

        // ✅ CARGAR HISTORIAL CONVERSACIONAL (últimos 5 mensajes)
        var conversationHistory = await LoadConversationHistoryAsync(conversationId, cancellationToken);

        var request = new LLMRequest
        {
            Messages = new List<LLMMessage>
            {
                // System prompt con personalidad y reglas de negocio
                new LLMMessage { Role = LLMRole.System, Content = systemPrompt },
                
                // Contexto del estado actual de la conversación
                new LLMMessage { Role = LLMRole.System, Content = stateContext },
                
                // Información extraída del mensaje actual (si aplica)
                new LLMMessage { Role = LLMRole.System, Content = extractionContext },
                
                // Resultados de herramientas ejecutadas (si aplica)
                new LLMMessage { Role = LLMRole.System, Content = toolResultsContext },
                
                // Instrucciones para generar respuesta (simplificadas, sin reglas hardcodeadas)
                new LLMMessage
                {
                    Role = LLMRole.System,
                    Content = await BuildResponseInstructionsAsync(state, flowEvaluation, extractionResult, toolResults, businessId, cancellationToken)
                }
            },
            Temperature = 0.7f,
            MaxTokens = 400
        };

        // ✅ AGREGAR HISTORIAL CONVERSACIONAL antes del mensaje actual
        // Esto permite que el LLM mantenga coherencia naturalmente
        foreach (var historyMessage in conversationHistory)
        {
            var role = historyMessage.Sender.Equals("User", StringComparison.OrdinalIgnoreCase) 
                ? LLMRole.User 
                : LLMRole.Assistant;
            
            request.Messages.Add(new LLMMessage 
            { 
                Role = role, 
                Content = historyMessage.MessageText 
            });
        }

        // Mensaje actual del usuario (al final)
        request.Messages.Add(new LLMMessage { Role = LLMRole.User, Content = userMessage });

        var response = await _llmAdapter.SendMessageAsync(request, cancellationToken);

        if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
        {
            return response.Content.Trim();
        }

        // Fallback si el LLM falla
        return BuildFallbackResponse(flowEvaluation, extractionResult, toolResults);
    }

    /// <summary>
    /// Carga el historial conversacional reciente (últimos 10 mensajes).
    /// Esto permite que el LLM mantenga coherencia conversacional naturalmente.
    /// </summary>
    private async Task<List<Domain.Entities.Message>> LoadConversationHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var allMessages = await _messageService.GetConversationHistoryAsync(conversationId);
            
            // Obtener últimos 10 mensajes ordenados por timestamp
            var recentMessages = allMessages
                .OrderBy(m => m.Timestamp)
                .TakeLast(10)
                .ToList();

            _logger.LogDebug(
                "Cargados {Count} mensajes del historial para conversación {ConversationId}",
                recentMessages.Count, conversationId);

            return recentMessages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cargando historial conversacional, continuando sin historial");
            return new List<Domain.Entities.Message>();
        }
    }

    /// <summary>
    /// Construye el contexto del estado actual de la conversación
    /// </summary>
    /// <summary>
    /// Construye el contexto de estado cargando templates y poblando datos dinámicos.
    /// El backend SOLO carga y popula, NO construye contenido.
    /// </summary>
    private string BuildStateContext(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowEvaluation)
    {
        var context = new System.Text.StringBuilder();
        
        // ═══════════════════════════════════════════════════════
        // HEADER
        // ═══════════════════════════════════════════════════════
        context.AppendLine(Prompts.Templates.StateContextTemplate.Header);
        
        // ═══════════════════════════════════════════════════════
        // CONTEXTO CONVERSACIONAL
        // ═══════════════════════════════════════════════════════
        // ✅ SIMPLIFICADO: El historial conversacional se pasa directamente al LLM
        // No necesitamos reglas hardcodeadas sobre "primera interacción"
        context.AppendLine($"**Etapa actual**: {state.CurrentStage}");
        context.AppendLine();
        
        // ═══════════════════════════════════════════════════════
        // COMPLETENESS
        // ═══════════════════════════════════════════════════════
        context.AppendLine(
            Prompts.Templates.StateContextTemplate.CompletenessSection
                .Replace("{completeness_percentage}", flowEvaluation.CompletenessPercentage.ToString()));
        
        // ═══════════════════════════════════════════════════════
        // INFORMATION COLLECTED
        // ═══════════════════════════════════════════════════════
        context.AppendLine(
            Prompts.Templates.StateContextTemplate.InformationSection
                .Replace("{customer_name}", state.CustomerName ?? "NO RECOLECTADO")
                .Replace("{phone}", state.Phone ?? "NO RECOLECTADO")
                .Replace("{email}", state.Email ?? "NO RECOLECTADO")
                .Replace("{service}", state.Service ?? "NO SELECCIONADO")
                .Replace("{desired_date}", state.DesiredDate?.ToString("yyyy-MM-dd") ?? "NO ESTABLECIDA")
                .Replace("{desired_time}", state.DesiredTime?.ToString("HH:mm") ?? "NO ESTABLECIDA")
                .Replace("{availability_confirmed}", state.AvailabilityConfirmed ? "SÍ" : "NO")
                .Replace("{reservation_confirmed}", state.ReservationConfirmed ? "SÍ" : "NO")
                .Replace("{reservation_created}", state.ReservationCreated ? "SÍ" : "NO"));

        // ═══════════════════════════════════════════════════════
        // AVAILABLE TIME SLOTS (si aplica) - REFORZADO
        // ═══════════════════════════════════════════════════════
        if (state.AvailabilityConfirmed && !string.IsNullOrEmpty(state.AvailableTimeSlots))
        {
            var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            // ✅ REFORZAR: Hacer MÁS visible
            context.AppendLine();
            context.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            context.AppendLine("🚨 HORARIOS DISPONIBLES CONFIRMADOS 🚨");
            context.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            context.AppendLine();
            context.AppendLine(
                Prompts.Templates.AvailableTimeSlotsTemplate.Build(
                    state.CustomerName ?? string.Empty,
                    slots));
            context.AppendLine();
            context.AppendLine("⚠️ DEBES MOSTRAR ESTOS HORARIOS AL CLIENTE EN TU RESPUESTA");
            context.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            context.AppendLine();
        }

        // ═══════════════════════════════════════════════════════
        // BUSINESS-SPECIFIC ATTRIBUTES
        // ═══════════════════════════════════════════════════════
        if (state.Attributes.Any())
        {
            var attributesList = new System.Text.StringBuilder();
            foreach (var attr in state.Attributes)
            {
                // Remover el prefijo "Attribute:" si existe para mostrar el nombre limpio
                var displayName = attr.Key.StartsWith("Attribute:") 
                    ? attr.Key.Substring("Attribute:".Length) 
                    : attr.Key;
                attributesList.AppendLine($"- {displayName}: {attr.Value}");
            }
            
            context.AppendLine(
                Prompts.Templates.StateContextTemplate.AttributesSection
                    .Replace("{attributes_list}", attributesList.ToString().TrimEnd()));
        }

        // ═══════════════════════════════════════════════════════
        // MISSING FIELDS
        // ═══════════════════════════════════════════════════════
        if (flowEvaluation.MissingFields.Any())
        {
            var missingFieldsList = string.Join("\n", 
                flowEvaluation.MissingFields.Select(f => $"- {f}"));
            
            context.AppendLine(
                Prompts.Templates.StateContextTemplate.MissingFieldsSection
                    .Replace("{missing_fields_list}", missingFieldsList));
        }

        // ═══════════════════════════════════════════════════════
        // FLOW STATE / DIAGNOSTIC
        // ═══════════════════════════════════════════════════════
        context.AppendLine(
            Prompts.Templates.StateContextTemplate.FlowStateSection
                .Replace("{diagnostic_message}", flowEvaluation.DiagnosticMessage));

        return context.ToString();
    }

    /// <summary>
    /// Construye el contexto de información extraída del mensaje actual
    /// </summary>
    private string BuildExtractionContext(ExtractionResult extractionResult)
    {
        if (extractionResult == null || !extractionResult.StructuredResponse.ExtractedFields.Any())
        {
            return "# INFORMACIÓN EXTRAÍDA DEL MENSAJE ACTUAL\n\n*(No se extrajo información nueva en este mensaje)*";
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("# INFORMACIÓN EXTRAÍDA DEL MENSAJE ACTUAL");
        context.AppendLine();
        context.AppendLine("## Campos extraídos:");
        
        foreach (var field in extractionResult.StructuredResponse.ExtractedFields)
        {
            context.AppendLine($"- **{field.FieldName}**: {field.Value} (confianza: {field.Confidence:F2})");
        }

        // Mostrar ambigüedades si las hay
        if (extractionResult.StructuredResponse.Ambiguities.Any())
        {
            context.AppendLine();
            context.AppendLine("## Ambigüedades detectadas:");
            foreach (var ambiguity in extractionResult.StructuredResponse.Ambiguities)
            {
                context.AppendLine($"- {ambiguity.FieldName}: {ambiguity.AmbiguousText} (tipo: {ambiguity.Type})");
            }
        }

        return context.ToString();
    }

    /// <summary>
    /// Construye el contexto de resultados de herramientas ejecutadas
    /// </summary>
    private string BuildToolResultsContext(List<(string FunctionName, ToolExecutionResult Result)> toolResults)
    {
        if (!toolResults.Any())
        {
            return "# RESULTADOS DE HERRAMIENTAS\n\n*(No se ejecutaron herramientas en este mensaje)*";
        }

        var context = new System.Text.StringBuilder();
        context.AppendLine("# RESULTADOS DE HERRAMIENTAS EJECUTADAS");
        context.AppendLine();

        foreach (var (functionName, result) in toolResults.Where(r => r.Result.Success))
        {
            context.AppendLine($"## {functionName}:");
            context.AppendLine($"- Resultado: {result.Message}");
            
            if (result.Data != null && result.Data.Any())
            {
                context.AppendLine("- Datos:");
                foreach (var kvp in result.Data)
                {
                    context.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                }
            }
            context.AppendLine();
        }

        return context.ToString();
    }

    /// <summary>
    /// Construye las instrucciones para generar la respuesta basadas en el contexto
    /// </summary>
    /// <summary>
    /// Construye las instrucciones para generar respuesta CARGANDO templates.
    /// El backend SOLO carga y popula, NO construye contenido.
    /// </summary>
    private async Task<string> BuildResponseInstructionsAsync(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowEvaluation,
        ExtractionResult extractionResult,
        List<(string FunctionName, ToolExecutionResult Result)> toolResults,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        var instructions = new System.Text.StringBuilder();
        
        // ═══════════════════════════════════════════════════════
        // HEADER + BASE INSTRUCTIONS
        // ═══════════════════════════════════════════════════════
        instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.Header);
        instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.BaseInstructions);
        instructions.AppendLine();

        // ✅ SIMPLIFICADO: El historial conversacional se pasa directamente al LLM
        // El LLM puede ver si ya saludó o no, manteniendo coherencia naturalmente
        // No necesitamos reglas hardcodeadas sobre "primera interacción" o "cuándo saludar"

        // ═══════════════════════════════════════════════════════
        // TIME SELECTED (si usuario seleccionó horario)
        // ═══════════════════════════════════════════════════════
        bool timeSelected = extractionResult.StructuredResponse.ExtractedFields
            .Any(f => f.FieldName == "DesiredTime" && f.Confidence >= 0.8);
        if (timeSelected && !state.ReservationConfirmed)
        {
            instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.TimeSelectedInstructions);
            instructions.AppendLine();
        }

        // ═══════════════════════════════════════════════════════
        // INFORMATION QUERY (si aplica)
        // ═══════════════════════════════════════════════════════
        if (extractionResult.StructuredResponse.FlowAnalysis.IsInformationQuery)
        {
            instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.InformationQueryInstructions);
            instructions.AppendLine();
            // ✅ El historial conversacional también ayuda: si el usuario pregunta por servicios
            // después de que ya le mostramos opciones, el LLM puede inferir que está explorando
        }

        // ═══════════════════════════════════════════════════════
        // CHECK AVAILABILITY (si se ejecutó)
        // ═══════════════════════════════════════════════════════
        if (toolResults.Any(r => r.FunctionName == "check_availability"))
        {
            instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.CheckAvailabilityInstructions);
            instructions.AppendLine();
        }

        // ═══════════════════════════════════════════════════════
        // CREATE RESERVATION (si se ejecutó)
        // ═══════════════════════════════════════════════════════
        if (toolResults.Any(r => r.FunctionName == "create_reservation"))
        {
            instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.CreateReservationInstructions);
            instructions.AppendLine();
        }

        // ═══════════════════════════════════════════════════════
        // MISSING FIELDS (si hay) - SOLO si NO es consulta informativa
        // ═══════════════════════════════════════════════════════
        // ✅ Si el usuario está explorando (IsInformationQuery), NO pedir datos de reserva
        if (flowEvaluation.MissingFields.Any() && 
            !extractionResult.StructuredResponse.FlowAnalysis.IsInformationQuery)
        {
            instructions.AppendLine(
                Prompts.Templates.ResponseInstructionsTemplate.MissingFieldsInstructions
                    .Replace("{missing_fields}", string.Join(", ", flowEvaluation.MissingFields)));
            instructions.AppendLine();
        }
        else if (flowEvaluation.MissingFields.Any() && 
                 extractionResult.StructuredResponse.FlowAnalysis.IsInformationQuery)
        {
            // Si es consulta informativa pero hay campos faltantes, solo mencionar suavemente
            instructions.AppendLine("**Nota**: Cuando estés listo para reservar, necesitaré algunos datos. Por ahora, solo estoy compartiendo información. 😊");
            instructions.AppendLine();
        }

        // ═══════════════════════════════════════════════════════
        // AMBIGUITIES (si hay)
        // ═══════════════════════════════════════════════════════
        if (extractionResult.StructuredResponse.Ambiguities.Any())
        {
            instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.AmbiguitiesInstructions);
            instructions.AppendLine();
        }

        // ═══════════════════════════════════════════════════════
        // FINAL REMINDER
        // ═══════════════════════════════════════════════════════
        instructions.AppendLine(Prompts.Templates.ResponseInstructionsTemplate.FinalReminder);

        return instructions.ToString();
    }

    /// <summary>
    /// Construye una respuesta de fallback si el LLM falla
    /// </summary>
    private string BuildFallbackResponse(
        FlowEvaluationResult flowEvaluation,
        ExtractionResult extractionResult,
        List<(string FunctionName, ToolExecutionResult Result)> toolResults)
    {
        // Si se ejecutaron herramientas exitosamente, confirmar
        if (toolResults.Any(r => r.FunctionName == "create_reservation" && r.Result.Success))
        {
            return "¡Perfecto! He creado tu reserva. Te enviaré los detalles en breve. ¿Hay algo más en lo que pueda ayudarte? 😊";
        }

        if (toolResults.Any(r => r.FunctionName == "check_availability" && r.Result.Success))
        {
            return "He verificado la disponibilidad. ¿Te gustaría que reserve alguno de estos horarios? 😊";
        }

        // Si se extrajo información nueva, confirmar brevemente
        if (extractionResult.StructuredResponse.ExtractedFields.Any())
        {
            return "Perfecto, he guardado esa información. ¿En qué más puedo ayudarte? 😊";
        }

        // Respuesta genérica
        return "Entendido. ¿En qué más puedo ayudarte? 😊";
    }

    private int CalculateCompleteness(
        Domain.Models.ConversationState state,
        RequiredFieldsConfiguration requiredFields)
    {
        var evaluation = _flowEngine.Evaluate(state, requiredFields);
        return evaluation.CompletenessPercentage;
    }
}
