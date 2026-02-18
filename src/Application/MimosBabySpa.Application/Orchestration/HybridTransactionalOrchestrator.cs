using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.Prompts.Templates;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Tools;

namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Orquestador Híbrido Transaccional — 6 fases bien definidas.
///
/// FASES:
///   1. CONTEXTO  — Una sola carga de BD por request (negocio + estado + historial).
///   2. EXTRACCIÓN — LLM extrae campos e intenciones del mensaje (JSON Mode, temperatura 0.1).
///   3. ESTADO    — Aplica campos en batch (in-memory); un solo Save al final.
///   4. FLUJO     — FlowEngine decide acciones; tools persisten si corresponde.
///   5. RESPUESTA — LLM genera respuesta conversacional (temperatura 0.7).
///   6. METADATOS — Actualiza LastUserMessage/LastBotMessage y Save final.
///
/// GARANTÍAS:
///   - Mínimos round-trips a BD: 1 read inicial + 1-2 reads post-tool + 1 write final.
///   - Sin mutaciones directas al estado: todo pasa por IConversationStateUpdater.
///   - FlowEngine es la única fuente de verdad para CanCheck/CanCreate.
///   - UserWantsToCancel reseteado limpiamente.
///   - CurrentStage actualizado en cada evaluación.
///   - Multitenant: businessId en cada operación de estado.
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
    private readonly IMessageService _messageService;
    private readonly IConversationStateUpdater _stateUpdater;
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
        IMessageService messageService,
        IConversationStateUpdater stateUpdater,
        ILogger<HybridTransactionalOrchestrator> logger)
    {
        _stateManager         = stateManager;
        _flowEngine           = flowEngine;
        _businessRuleEngine   = businessRuleEngine;
        _cachedContextProvider = cachedContextProvider;
        _systemPromptProvider = systemPromptProvider;
        _llmAdapter           = llmAdapter;
        _toolDispatcher       = toolDispatcher;
        _extractionService    = extractionService;
        _messageService       = messageService;
        _stateUpdater         = stateUpdater;
        _logger               = logger;
    }

    // ═════════════════════════════════════════════════════════════════
    // PUNTO DE ENTRADA
    // ═════════════════════════════════════════════════════════════════

    public async Task<OrchestratorResult> ProcessMessageAsync(
        Guid conversationId,
        Guid businessId,
        string customerPhone,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "=== INICIO === Conv={ConversationId} Biz={BusinessId}",
                conversationId, businessId);

            // ── FASE 1: Contexto ──────────────────────────────────
            var ctx = await LoadContextAsync(
                conversationId, businessId, customerPhone, userMessage, cancellationToken);

            // ── FASE 2: Extracción ────────────────────────────────
            var extraction = await ExtractInformationAsync(userMessage, ctx, cancellationToken);
            ctx.ExtractionOutput = extraction;

            if (!extraction.WasSuccessful)
            {
                _logger.LogWarning("Extracción falló — retornando respuesta de emergencia");
                return new OrchestratorResult(extraction.ConversationalResponseSuggestion, ReservationCreated: false);
            }

            // ── FASE 3: Actualizar estado ─────────────────────────
            ApplyExtractionToState(extraction, ctx);

            // ── FASE 4: Acciones de flujo ─────────────────────────
            await ExecuteFlowActionsAsync(ctx, cancellationToken);

            // ── FASE 5: Generar respuesta ─────────────────────────
            var response = await GenerateResponseAsync(userMessage, ctx, cancellationToken);

            // ── FASE 6: Guardar metadatos finales (LastUserMessage, LastBotMessage en ConversationState)
            await SaveFinalMetadataAsync(ctx, userMessage, response, cancellationToken);

            _logger.LogInformation(
                "=== FIN === {Chars} chars | Completitud={Pct}% | ReservationCreated={Created}",
                response.Length, ctx.FlowEvaluation.CompletenessPercentage, ctx.State.ReservationCreated);

            return new OrchestratorResult(response, ctx.State.ReservationCreated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en orquestador Conv={ConversationId}", conversationId);
            return new OrchestratorResult(
                "Disculpa, ha ocurrido un error. Por favor intenta nuevamente.",
                ReservationCreated: false);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 1 — CONTEXTO
    // ═════════════════════════════════════════════════════════════════

    private async Task<ProcessingContext> LoadContextAsync(
        Guid conversationId,
        Guid businessId,
        string customerPhone,
        string userMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 1: Cargando contexto...");

        // Una sola carga de configuración de negocio (con caché por businessId)
        var businessContext = await _cachedContextProvider.GetOrLoadAsync(businessId, cancellationToken);

        // Estado de conversación (por conversationId + businessId)
        var state = await _stateManager.GetOrCreateStateAsync(
            conversationId, businessId, customerPhone, cancellationToken);

        // System prompt del LLM de respuesta (dinámico por negocio)
        var systemPrompt = await _systemPromptProvider.BuildAsync(businessContext, cancellationToken);

        var context = new ProcessingContext(
            state,
            businessContext.RequiredFields,
            systemPrompt,
            businessContext,
            _flowEngine,
            _stateManager,
            conversationId,
            businessId,
            customerPhone,
            userMessage);

        _logger.LogInformation(
            "✅ Contexto: Version={Version}, Completitud={Pct}%",
            state.Version, context.FlowEvaluation.CompletenessPercentage);

        return context;
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 2 — EXTRACCIÓN
    // ═════════════════════════════════════════════════════════════════

    private async Task<ExtractionOutput> ExtractInformationAsync(
        string userMessage,
        ProcessingContext ctx,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 2: Extrayendo información...");

        var output = await _extractionService.ExtractWithValidationAsync(
            userMessage, ctx.State, ctx.BusinessContext, cancellationToken);

        _logger.LogInformation(
            "Extracción: {Count} campos, método={Method}, exitoso={Ok}",
            output.ExtractedFields.Count, output.Method, output.WasSuccessful);

        return output;
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 3 — ACTUALIZAR ESTADO (in-memory, sin save ni reload)
    // ═════════════════════════════════════════════════════════════════

    private void ApplyExtractionToState(ExtractionOutput extraction, ProcessingContext ctx)
    {
        _logger.LogDebug("FASE 3: Aplicando {Count} campos...", extraction.ExtractedFields.Count);

        foreach (var field in extraction.ExtractedFields)
        {
            if (field.Confidence < ExtractionConstants.MinConfidence)
            {
                _logger.LogDebug("Ignorado por baja confidence [{Field}={Confidence:F2}]",
                    field.FieldName, field.Confidence);
                continue;
            }

            var result = _stateUpdater.ApplyField(ctx.State, field.FieldName, field.Value);

            if (result.Success)
                _logger.LogInformation("✓ {Field}='{Value}' (conf={Conf:F2})",
                    field.FieldName, field.Value, field.Confidence);
            else
                _logger.LogWarning("✗ {Field} no aplicado: {Msg}", field.FieldName, result.Message);
        }

        // Re-evaluar flujo in-memory (sin BD) para que FASE 4 tenga datos frescos
        ctx.ReEvaluate();
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 4 — ACCIONES DE FLUJO
    // ═════════════════════════════════════════════════════════════════

    private async Task ExecuteFlowActionsAsync(ProcessingContext ctx, CancellationToken ct)
    {
        _logger.LogDebug("FASE 4: Ejecutando acciones de flujo...");

        var turnActions = new TurnActions();
        var intentions  = ctx.ExtractionOutput?.Intentions ?? new ExtractionIntentions();

        // ── 4a. Cancelación (prioridad más alta) ─────────────────
        if (intentions.UserWantsToCancel)
        {
            _logger.LogInformation("Usuario canceló — reseteando flags transaccionales");
            _stateUpdater.ResetTransactionalFlags(ctx.State);
            ctx.ReEvaluate();
            turnActions.CancellationExecuted = true;
            ctx.TurnActions = turnActions;
            return; // No continuar con otras acciones
        }

        // ── 4b. Verificación de disponibilidad ───────────────────
        //   - CanCheckAvailability: primera verificación (AvailabilityConfirmed=false)
        //   - ShouldRecheckAvailability + UserRequestedAvailability: re-verificación explícita
        var shouldCheck = ctx.FlowEvaluation.CanCheckAvailability
            || (intentions.UserRequestedAvailability && _flowEngine.ShouldRecheckAvailability(ctx.State));

        if (shouldCheck)
        {
            // Si es re-verificación, primero resetear el flag para que el tool pueda ejecutar
            if (!ctx.FlowEvaluation.CanCheckAvailability && intentions.UserRequestedAvailability)
            {
                _stateUpdater.ApplyConfirmationFlag(ctx.State, "AvailabilityConfirmed", false);
                ctx.ReEvaluate();
            }

            _logger.LogInformation("Verificando disponibilidad...");
            var checkResult = await ExecuteToolAndReloadAsync(ToolType.CheckAvailability, ctx, ct);
            turnActions.CheckAvailabilityExecuted = true;
            turnActions.AvailabilityResultMessage = checkResult.Message;

            _logger.LogInformation(
                "Disponibilidad: Confirmada={Confirmed}, Slots={Slots}",
                ctx.State.AvailabilityConfirmed, ctx.State.AvailableTimeSlots);
        }

        // ── 4c. Confirmación de reserva por el usuario ───────────
        if (intentions.UserConfirmedBooking && !ctx.State.ReservationConfirmed)
        {
            _logger.LogInformation("Usuario confirmó reserva");
            _stateUpdater.ApplyConfirmationFlag(ctx.State, "ReservationConfirmed", true);
            ctx.ReEvaluate(); // in-memory, no BD
        }

        // ── 4d. Crear reserva ─────────────────────────────────────
        if (ctx.FlowEvaluation.CanCreateReservation)
        {
            _logger.LogInformation("Creando reserva...");
            var createResult = await ExecuteToolAndReloadAsync(ToolType.CreateReservation, ctx, ct);
            turnActions.CreateReservationExecuted = createResult.Success;
            turnActions.ReservationResultMessage  = createResult.Message;

            if (createResult.Success)
                _logger.LogInformation("✅ Reserva creada exitosamente");
            else
                _logger.LogWarning("❌ Creación de reserva falló: {Msg}", createResult.Message);
        }
        else if (ctx.State.ReservationConfirmed && !ctx.FlowEvaluation.CanCreateReservation)
        {
            _logger.LogWarning(
                "Usuario confirmó pero faltan requisitos. Missing: [{Missing}]",
                string.Join(", ", ctx.FlowEvaluation.MissingFields));
        }

        ctx.TurnActions = turnActions;
    }

    /// <summary>
    /// Ejecuta un tool y recarga el estado desde BD (necesario porque el tool puede persistir).
    /// Solo se hace reload cuando el tool realmente modifica estado en BD.
    /// </summary>
    private async Task<ToolExecutionResult> ExecuteToolAndReloadAsync(
        ToolType toolType,
        ProcessingContext ctx,
        CancellationToken ct,
        Dictionary<string, object>? parameters = null)
    {
        var result = await _toolDispatcher.ExecuteAsync(toolType, ctx.ToolContext, parameters, ct);

        // El tool puede haber persistido cambios en BD → recargar estado fresco
        if (result.StateModified)
            await ctx.ReloadAndEvaluateAsync(ct);
        else
            ctx.ReEvaluate(); // Solo re-evalúa in-memory si no hubo cambios en BD

        return result;
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 5 — GENERAR RESPUESTA
    // ═════════════════════════════════════════════════════════════════

    private async Task<string> GenerateResponseAsync(
        string userMessage,
        ProcessingContext ctx,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 5: Generando respuesta...");

        var input = new ResponseGenerationInput
        {
            State          = ctx.State,
            FlowSnapshot   = ctx.FlowEvaluation,
            TurnActions    = ctx.TurnActions,
            ExtractionOutput = ctx.ExtractionOutput ?? new ExtractionOutput(),
            UserMessage    = userMessage,
            SystemPrompt   = ctx.SystemPrompt,
            ConversationId = ctx.ToolContext.ConversationId,
            BusinessId     = ctx.ToolContext.BusinessId
        };

        try
        {
            return await GenerateConversationalResponseAsync(input, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generando respuesta — usando fallback");
            return BuildFallbackResponse(input.TurnActions, input.ExtractionOutput);
        }
    }

    private async Task<string> GenerateConversationalResponseAsync(
        ResponseGenerationInput input,
        CancellationToken cancellationToken)
    {
        var turnContext  = BuildTurnContext(input.State, input.FlowSnapshot, input.TurnActions, input.ExtractionOutput);
        var instructions = BuildResponseInstructions(input.FlowSnapshot, input.TurnActions, input.ExtractionOutput);
        var history      = await LoadConversationHistoryAsync(input.ConversationId, cancellationToken);

        var messages = new List<LLMMessage>
        {
            new() { Role = LLMRole.System, Content = input.SystemPrompt },
            new() { Role = LLMRole.System, Content = turnContext },
            new() { Role = LLMRole.System, Content = instructions }
        };

        foreach (var msg in history)
        {
            var role = msg.Sender.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? LLMRole.User : LLMRole.Assistant;
            messages.Add(new() { Role = role, Content = msg.MessageText });
        }
        messages.Add(new() { Role = LLMRole.User, Content = input.UserMessage });

        var request = new LLMRequest
        {
            Messages    = messages,
            Temperature = 0.7f,
            MaxTokens   = 400
        };

        var response = await _llmAdapter.SendMessageAsync(request, cancellationToken);
        if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            return response.Content.Trim();

        return BuildFallbackResponse(input.TurnActions, input.ExtractionOutput);
    }

    // ─────────────────────────────────────────────────────────────────
    // Contexto del turno — bloque dinámico enviado al LLM de respuesta
    // ─────────────────────────────────────────────────────────────────

    private static string BuildTurnContext(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowSnapshot,
        TurnActions turnActions,
        ExtractionOutput extraction)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# CONTEXTO DEL TURNO");
        sb.AppendLine($"Etapa: {state.CurrentStage} | Completitud: {flowSnapshot.CompletenessPercentage}%");
        sb.AppendLine();

        // Estado compacto
        sb.AppendLine("**Datos actuales:**");
        sb.AppendLine($"- Cliente: {state.CustomerName ?? "—"} | Tel: {state.Phone ?? "—"} | Email: {state.Email ?? "—"}");
        sb.AppendLine($"- Servicio: {state.Service ?? "—"} | Fecha: {state.DesiredDate?.ToString("yyyy-MM-dd") ?? "—"} | Hora: {state.DesiredTime?.ToString("HH:mm") ?? "—"}");
        sb.AppendLine($"- Disponibilidad: {(state.AvailabilityConfirmed ? "✓ Confirmada" : "Pendiente")} | Reserva: {(state.ReservationConfirmed ? "✓ Confirmada" : "Pendiente")}");

        if (state.Attributes.Any())
        {
            var attrs = string.Join(" | ", state.Attributes.Select(a => $"{a.Key}={a.Value}"));
            sb.AppendLine($"- Atributos: {attrs}");
        }

        if (flowSnapshot.MissingFields.Any())
            sb.AppendLine($"- **Faltan:** {string.Join(", ", flowSnapshot.MissingFields)}");

        sb.AppendLine();

        // Qué pasó en este turno
        if (turnActions.CancellationExecuted)
        {
            sb.AppendLine("**Este turno: el usuario canceló/cambió de intención. Acepta y ofrece ayuda.**");
        }
        else
        {
            if (turnActions.CheckAvailabilityExecuted)
            {
                sb.AppendLine("**Este turno: se verificó disponibilidad.**");
                if (state.AvailabilityConfirmed && !string.IsNullOrEmpty(state.AvailableTimeSlots))
                {
                    var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    sb.AppendLine($"**Horarios disponibles:** {string.Join(" | ", slots)}");
                    sb.AppendLine("→ Muestra TODOS estos horarios al cliente.");
                }
                else if (!state.AvailabilityConfirmed)
                {
                    sb.AppendLine("→ No hay disponibilidad. Sugiere alternativas.");
                }
            }

            if (turnActions.CreateReservationExecuted)
                sb.AppendLine("**Este turno: reserva creada exitosamente.** Confirma detalles y celebra.");

            if (extraction.ExtractedFields.Any() && !turnActions.CheckAvailabilityExecuted && !turnActions.CreateReservationExecuted)
                sb.AppendLine("**Este turno: el usuario proporcionó datos nuevos.**");

            if (extraction.Intentions.IsInformationQuery)
                sb.AppendLine("**El usuario pide información sobre servicios/planes.** Muéstralos sin presionar a reservar.");
        }

        sb.AppendLine();
        sb.AppendLine($"**Diagnóstico:** {flowSnapshot.DiagnosticMessage}");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Instrucciones para la respuesta — condicionales y lean
    // ─────────────────────────────────────────────────────────────────

    private static string BuildResponseInstructions(
        FlowEvaluationResult flowSnapshot,
        TurnActions turnActions,
        ExtractionOutput extraction)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ResponseInstructionsTemplate.Header);
        sb.AppendLine(ResponseInstructionsTemplate.BaseInstructions);

        if (turnActions.CancellationExecuted)
        {
            sb.AppendLine(ResponseInstructionsTemplate.CancellationInstructions);
            return sb.ToString(); // No agregar otras instrucciones si hubo cancelación
        }

        if (turnActions.CheckAvailabilityExecuted)
            sb.AppendLine(ResponseInstructionsTemplate.CheckAvailabilityInstructions);

        if (turnActions.CreateReservationExecuted)
            sb.AppendLine(ResponseInstructionsTemplate.CreateReservationInstructions);

        // Tiempo seleccionado pero sin confirmación aún
        var timeSelected = extraction.ExtractedFields
            .Any(f => f.FieldName == "DesiredTime" && f.Confidence >= ExtractionConstants.MinConfidence);
        if (timeSelected && !extraction.Intentions.UserConfirmedBooking && !turnActions.CreateReservationExecuted)
            sb.AppendLine(ResponseInstructionsTemplate.TimeSelectedInstructions);

        if (extraction.Intentions.IsInformationQuery)
            sb.AppendLine(ResponseInstructionsTemplate.InformationQueryInstructions);

        if (flowSnapshot.MissingFields.Any() && !extraction.Intentions.IsInformationQuery)
            sb.AppendLine(ResponseInstructionsTemplate.MissingFieldsInstructions
                .Replace("{missing_fields}", string.Join(", ", flowSnapshot.MissingFields)));

        if (extraction.Ambiguities.Any())
            sb.AppendLine(ResponseInstructionsTemplate.AmbiguitiesInstructions);

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Historial conversacional
    // ─────────────────────────────────────────────────────────────────

    private async Task<List<Domain.Entities.Message>> LoadConversationHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var all = await _messageService.GetConversationHistoryAsync(conversationId);
            var recent = all.OrderBy(m => m.Timestamp).TakeLast(10).ToList();

            _logger.LogDebug("Historial: {Count} mensajes", recent.Count);
            return recent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cargando historial — continuando sin historial");
            return new List<Domain.Entities.Message>();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Fallback de respuesta (cuando el LLM de respuesta falla)
    // ─────────────────────────────────────────────────────────────────

    private static string BuildFallbackResponse(TurnActions turnActions, ExtractionOutput extraction)
    {
        if (turnActions.CancellationExecuted)
            return "Entendido. Si en algún momento quieres retomar, aquí estaré. ¿Hay algo más en lo que pueda ayudarte?";
        if (turnActions.CreateReservationExecuted)
            return "¡Tu reserva fue creada exitosamente! Te enviaré los detalles. ¿Hay algo más en lo que pueda ayudarte?";
        if (turnActions.CheckAvailabilityExecuted)
            return "He verificado la disponibilidad. ¿Te gustaría reservar alguno de esos horarios?";
        if (extraction.ExtractedFields.Any())
            return "Perfecto, he registrado esa información. ¿Continuamos?";
        return "Entendido. ¿En qué más puedo ayudarte?";
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 6 — GUARDAR METADATOS FINALES
    // ═════════════════════════════════════════════════════════════════

    private async Task SaveFinalMetadataAsync(
        ProcessingContext ctx,
        string userMessage,
        string botResponse,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 6: Guardando metadatos finales...");

        // Actualizar LastUserMessage y LastBotMessage (para contexto de inferencia en el próximo turno)
        ctx.UpdateMessageMetadata(userMessage, botResponse);

        // Un solo Save al final de todo el flujo
        await ctx.SaveStateAsync(cancellationToken);

        _logger.LogDebug("✅ Estado guardado (Version={Version})", ctx.State.Version);
    }
}
