using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Constants;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.Prompts.Templates;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Orquestador Híbrido Transaccional — 6 fases bien definidas.
///
/// FASES:
///   1. CONTEXTO  — Una sola carga de BD por request (negocio + estado + historial).
///   2. EXTRACCIÓN — LLM extrae campos e intenciones del mensaje (JSON Mode, temperatura 0.1).
///   3. ESTADO    — Aplica campos en batch (in-memory); un solo Save al final.
///   4. FLUJO     — FlowEngine decide acciones; tools persisten si corresponde.
///   5. RESPUESTA — Determinística para confirmación y reenvío de link; LLM para el resto (temperatura 0.7).
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
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
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
        IPaymentLinkService paymentLinkService,
        IPaymentTransactionRepository paymentTransactionRepository,
        ILogger<HybridTransactionalOrchestrator> logger)
    {
        _stateManager                  = stateManager;
        _flowEngine                    = flowEngine;
        _businessRuleEngine           = businessRuleEngine;
        _cachedContextProvider         = cachedContextProvider;
        _systemPromptProvider          = systemPromptProvider;
        _llmAdapter                    = llmAdapter;
        _toolDispatcher                = toolDispatcher;
        _extractionService             = extractionService;
        _messageService                = messageService;
        _stateUpdater                  = stateUpdater;
        _paymentLinkService            = paymentLinkService;
        _paymentTransactionRepository  = paymentTransactionRepository;
        _logger                        = logger;
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
            var ctx = await LoadContextAsync(conversationId, businessId, customerPhone, userMessage, cancellationToken);

            // ── FASE 2: Extracción ────────────────────────────────
            ctx.ExtractionOutput = await ExtractInformationAsync(userMessage, ctx, cancellationToken);

            if (!ctx.ExtractionOutput.WasSuccessful)
            {
                _logger.LogWarning("Extracción falló — retornando respuesta de emergencia");
                return new OrchestratorResult(ctx.ExtractionOutput.ConversationalResponseSuggestion, ReservationCreated: false);
            }

            // ── FASE 3: Actualizar estado ─────────────────────────
            ApplyExtractionToState(ctx);

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

        // Detección de retomo: inactividad prolongada con datos transaccionales → reset preservando identidad.
        var inactivityPeriod = DateTime.UtcNow - state.UpdatedAt;
        var hasTransactionalData = !string.IsNullOrWhiteSpace(state.Service)
            || state.DesiredDate.HasValue
            || state.DesiredTime.HasValue;
        if (inactivityPeriod.TotalHours >= OrchestrationConstants.ResumptionThresholdHours && hasTransactionalData)
        {
            _logger.LogInformation(
                "Retomo detectado: {Hours:F1}h de inactividad. Reseteando datos transaccionales.",
                inactivityPeriod.TotalHours);
            _stateUpdater.ResetForResumption(state);
        }

        // System prompt del LLM de respuesta (dinámico por negocio y servicio elegido para filtrar add-ons)
        var selectedCategory = ResolveSelectedCategory(businessContext.Services, state.Service);
        var promptInput = new SystemPromptInput(businessContext, selectedCategory);
        var systemPrompt = await _systemPromptProvider.BuildAsync(promptInput, cancellationToken);

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

        var history = await LoadConversationHistoryAsync(ctx.ToolContext.ConversationId, cancellationToken);

        var output = await _extractionService.ExtractWithValidationAsync(
            userMessage,
            ctx.State,
            ctx.BusinessContext,
            history,
            cancellationToken);

        LogExtraction(output);

        return output;
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 3 — ACTUALIZAR ESTADO (in-memory, sin save ni reload)
    // ═════════════════════════════════════════════════════════════════

    private void ApplyExtractionToState(ProcessingContext ctx)
    {
        _logger.LogDebug("FASE 3: Aplicando {Count} campos...", ctx.ExtractionOutput?.ExtractedFields.Count);

        foreach (var field in ctx.ExtractionOutput?.ExtractedFields!)
        {
            if (field.Confidence < ExtractionConstants.MinConfidence)
            {
                _logger.LogDebug("Ignorado por baja confidence [{Field}={Confidence:F2}]",
                    field.FieldName, field.Confidence);
                continue;
            }

            var fieldName = field.FieldName;
            
            // Si el campo coincide con un atributo configurado, agregar el prefijo requerido por StateUpdater
            if (ctx.BusinessContext.Attributes.ContainsKey(fieldName))
            {
                fieldName = $"Attribute:{fieldName}";
            }

            var result = _stateUpdater.ApplyField(ctx.State, fieldName, field.Value);

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

        // ── 4b. AwaitingPayment — decidir si regenerar link, verificar pago en caliente, o responder vía LLM ────
        if (ctx.FlowEvaluation.CurrentStage == TransactionStage.AwaitingPayment)
        {
            if (intentions.UserRequestsNewPaymentLink)
            {
                _logger.LogInformation("Usuario solicitó nuevo link de pago — limpiando para regenerar");
                _stateUpdater.ResetPaymentFields(ctx.State);
                ctx.ReEvaluate();
                // NO return: fluye a ConfirmingBooking y 4e generará el link
            }
            else if (intentions.UserSaysAlreadyPaid && !string.IsNullOrWhiteSpace(ctx.State.PaymentReferenceId))
            {
                var paymentStatus = await _paymentLinkService.CheckPaymentStatusAsync(
                    ctx.State.PaymentReferenceId, ct);

                if (paymentStatus.IsApproved)
                {
                    var amountValid = !ctx.State.AnticipoAmountInCents.HasValue
                        || !paymentStatus.AmountInCents.HasValue
                        || ctx.State.AnticipoAmountInCents.Value == paymentStatus.AmountInCents.Value;

                    if (amountValid)
                    {
                        _logger.LogInformation(
                            "Pago verificado en caliente: Ref={Ref} TxId={TxId}",
                            ctx.State.PaymentReferenceId, paymentStatus.TransactionId);

                        _stateUpdater.ApplyConfirmationFlag(ctx.State, "PaymentConfirmed", true);
                        _stateUpdater.ApplyConfirmationFlag(ctx.State, "ReservationConfirmed", true);
                        ctx.ReEvaluate();
                        // NO return: fluye a 4f (CanCreateReservation será true)
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Pago verificado pero monto no coincide: esperado={Expected} recibido={Received}",
                            ctx.State.AnticipoAmountInCents, paymentStatus.AmountInCents);
                        turnActions.IsAwaitingPayment = true;
                        ctx.TurnActions = turnActions;
                        return;
                    }
                }
                else
                {
                    _logger.LogDebug("Pago no confirmado aún en Wompi Ref={Ref}", ctx.State.PaymentReferenceId);
                    turnActions.IsAwaitingPayment = true;
                    ctx.TurnActions = turnActions;
                    return;
                }
            }
            else
            {
                turnActions.IsAwaitingPayment = true;
                ctx.TurnActions = turnActions;
                return;
            }
        }

        // ── 4c. Add-ons (INMEDIATO TRAS SERVICIO) ─────────────────
        // Ofrecer add-ons apenas el usuario elige un servicio, ANTES de disponibilidad y datos.
        // Excepción: si el usuario pidió explícitamente disponibilidad y tenemos Service + Date,
        // priorizar CheckAvailability (honrar intención explícita). AddOnsOffered queda false.
        var userAskedAvailability = intentions.UserRequestedAvailability
            && ctx.State.DesiredDate.HasValue
            && !string.IsNullOrWhiteSpace(ctx.State.Service);

        if (!userAskedAvailability
            && !string.IsNullOrWhiteSpace(ctx.State.Service)
            && !ctx.State.AddOnsOffered
            && ShouldOfferAddOns(ctx.State, ctx.BusinessContext))
        {
            var alreadySelectedAddOns = ctx.ExtractionOutput?.ExtractedFields?
                .Any(f => string.Equals(f.FieldName, "SelectedAddOns", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(f.FieldName, "Attribute:SelectedAddOns", StringComparison.OrdinalIgnoreCase)) ?? false;

            if (alreadySelectedAddOns)
            {
                _logger.LogInformation("Usuario ya proporcionó add-on(s) en este turno — marcando AddOnsOffered sin ofrecer.");
                _stateUpdater.ApplyConfirmationFlag(ctx.State, "AddOnsOffered", true);
                ctx.ReEvaluate();
            }
            else
            {
                _logger.LogInformation("Ofreciendo Add-ons compatibles para servicio {Service}", ctx.State.Service);
                turnActions.AddOnOfferingRequired = true;
                _stateUpdater.ApplyConfirmationFlag(ctx.State, "AddOnsOffered", true);
                ctx.ReEvaluate();
                ctx.TurnActions = turnActions;
                return; // No ejecutar disponibilidad, confirmación ni creación
            }
        }

        // ── 4c. Verificación de disponibilidad ───────────────────
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

        // ── 4c.2. Diagnóstico: usuario pidió disponibilidad pero no se ejecutó check ──
        if (intentions.UserRequestedAvailability && !turnActions.CheckAvailabilityExecuted)
        {
            _logger.LogWarning(
                "⚠ Usuario solicitó disponibilidad pero el check no se ejecutó. " +
                "Posible causa: DesiredDate no extraído del mensaje (Service={Service}, Date={Date}).",
                ctx.State.Service ?? "—", ctx.State.DesiredDate?.ToString("yyyy-MM-dd") ?? "—");
        }

        // ── 4d. Confirmación verbal (SOLO si no requiere anticipo) ──
        // Si RequiresAnticipo: la confirmación ES el pago, no el "sí" verbal.
        if (intentions.UserConfirmedBooking
            && !ctx.State.ReservationConfirmed
            && ctx.FlowEvaluation.CurrentStage == TransactionStage.ConfirmingBooking
            && ctx.BusinessContext.PaymentConfig is not { RequiresAnticipo: true })
        {
            _logger.LogInformation("Usuario confirmó reserva (sin anticipo)");
            _stateUpdater.ApplyConfirmationFlag(ctx.State, "ReservationConfirmed", true);
            ctx.ReEvaluate();
        }

        // ── 4e. Generar link de pago (si aplica) ─────────────────
        if (ctx.BusinessContext.PaymentConfig is { RequiresAnticipo: true } paymentConfig
            && ctx.FlowEvaluation.CurrentStage == TransactionStage.ConfirmingBooking
            && string.IsNullOrWhiteSpace(ctx.State.PaymentReferenceId))
        {
            var total = ReservationTotalCalculator.Calculate(
                ctx.State, ctx.BusinessContext.Services, ctx.BusinessContext.AddOnRules);
            var anticipoCents = (long)(total * paymentConfig.AnticipoPorcentaje * 100);

            var linkResult = await _paymentLinkService.GenerateAnticipoLinkAsync(
                new PaymentLinkRequest(
                    ctx.ToolContext.BusinessId,
                    ctx.ToolContext.ConversationId,
                    ctx.State.Phone ?? "",
                    $"Anticipo reserva - {ctx.State.Service}",
                    anticipoCents,
                    paymentConfig.Currency,
                    paymentConfig.LinkExpirationMinutes),
                ct);

            if (linkResult.Success)
            {
                ctx.State.PaymentReferenceId = linkResult.PaymentReferenceId;
                ctx.State.PaymentLinkUrl = linkResult.PaymentLinkUrl;
                ctx.State.AnticipoAmountInCents = anticipoCents;
                ctx.State.PaymentLinkExpiresAt = linkResult.ExpiresAt;
                turnActions.PaymentLinkGenerated = true;

                var paymentTx = new PaymentTransaction
                {
                    PaymentTransactionId = Guid.NewGuid(),
                    BusinessId = ctx.ToolContext.BusinessId,
                    ConversationId = ctx.ToolContext.ConversationId,
                    PaymentReferenceId = linkResult.PaymentReferenceId!,
                    AmountInCents = anticipoCents,
                    Currency = paymentConfig.Currency,
                    Status = PaymentTransactionStatus.Created
                };
                await _paymentTransactionRepository.SaveAsync(paymentTx, ct);

                ctx.ReEvaluate();
            }
            else
            {
                turnActions.PaymentLinkError = linkResult.ErrorMessage;
                _logger.LogError("Error generando link de pago: {Error}", linkResult.ErrorMessage);
            }
        }

        // ── 4f. Crear reserva ────────────────────────────────────
        if (ctx.FlowEvaluation.CanCreateReservation)
        {
            _logger.LogInformation("Creando reserva...");
            var createResult = await ExecuteToolAndReloadAsync(ToolType.CreateReservation, ctx, ct);
            turnActions.CreateReservationExecuted = createResult.Success;
            turnActions.ReservationResultMessage  = createResult.Message;

            if (createResult.Success)
                _logger.LogInformation("✅ Reserva creada exitosamente: {ReservationId}", ctx.State.ReservationId);
            else
                _logger.LogWarning("❌ Creación de reserva falló: {Msg}", createResult.Message);
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

        var deterministic = TryBuildDeterministicResponse(ctx);
        if (deterministic != null)
        {
            _logger.LogInformation("FASE 5: Respuesta determinística (sin LLM)");
            return deterministic;
        }

        var input = new ResponseGenerationInput
        {
            State            = ctx.State,
            FlowSnapshot     = ctx.FlowEvaluation,
            TurnActions      = ctx.TurnActions,
            ExtractionOutput = ctx.ExtractionOutput ?? new ExtractionOutput(),
            BusinessContext  = ctx.BusinessContext,
            UserMessage      = userMessage,
            SystemPrompt     = ctx.SystemPrompt,
            ConversationId   = ctx.ToolContext.ConversationId,
            BusinessId       = ctx.ToolContext.BusinessId
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

    /// <summary>
    /// Construye respuesta determinística si aplica (confirmación o reenvío de link).
    /// Si no aplica ninguna condición, retorna null y el LLM genera la respuesta.
    /// </summary>
    private string? TryBuildDeterministicResponse(ProcessingContext ctx)
    {
        // 1. Primera presentación del resumen de confirmación (intro + resumen + pago si aplica)
        if (ShouldInjectConfirmationSummary(ctx))
        {
            var name = ctx.State.CustomerName;
            var intro = !string.IsNullOrWhiteSpace(name)
                ? $"¡Gracias, {name}! Ya tengo todos los datos necesarios. Aquí está el resumen de tu reserva:"
                : "¡Perfecto! Ya tengo todos los datos necesarios. Aquí está el resumen de tu reserva:";
            var full = intro + ConfirmationSummaryBuilder.BuildInjectableSummary(ctx.State, ctx.BusinessContext);
            _stateUpdater.ApplyConfirmationFlag(ctx.State, "ConfirmationSummaryPresented", true);
            return full;
        }

        // 2. Reenvío de link de pago (resumen ya presentado, usuario solicitó nuevo link)
        if (ctx.State.ConfirmationSummaryPresented
            && ctx.TurnActions.PaymentLinkGenerated
            && !ctx.State.ReservationCreated
            && ctx.TurnActions.PaymentLinkError == null)
        {
            var intro = "Aquí tienes el link actualizado para realizar el pago del anticipo:";
            return intro + ConfirmationSummaryBuilder.BuildPaymentLinkBlock(ctx.State, ctx.BusinessContext);
        }

        return null;
    }

    private async Task<string> GenerateConversationalResponseAsync(
        ResponseGenerationInput input,
        CancellationToken cancellationToken)
    {
        var history = await LoadConversationHistoryAsync(input.ConversationId, cancellationToken);

        input.IsFirstMessage = !history.Any();

        var turnContext   = BuildTurnContext(input.State, input.FlowSnapshot, input.TurnActions, input.ExtractionOutput, input.BusinessContext);
        var instructions  = BuildResponseInstructions(input.State, input.FlowSnapshot, input.TurnActions, input.ExtractionOutput, input.BusinessContext, input.IsFirstMessage);

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

        var guardrails = BuildStateGuardrails(input.State, input.TurnActions);
        if (!string.IsNullOrWhiteSpace(guardrails))
            messages.Add(new() { Role = LLMRole.System, Content = guardrails });

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
    // Guardrails — restricciones críticas derivadas del estado, posicionadas
    // después del historial para máximo peso de atención. Evitan alucinaciones
    // sobre reservas y disponibilidad no verificadas.
    // ─────────────────────────────────────────────────────────────────

    private static string? BuildStateGuardrails(
        Domain.Models.ConversationState state,
        TurnActions turnActions)
    {
        var lines = new List<string>();

        if (turnActions.AddOnOfferingRequired)
            lines.Add("❌ ESTE TURNO: presentar add-ons disponibles. PROHIBIDO preguntar fecha, hora ni datos personales.");

        if (!state.ReservationCreated && !turnActions.CreateReservationExecuted)
            lines.Add("❌ La reserva NO ha sido creada. PROHIBIDO afirmar que está confirmada, agendada o lista.");

        if (turnActions.IsAwaitingPayment)
            lines.Add("❌ PAGO PENDIENTE. La reserva NO existe. PROHIBIDO confirmar, agendar o mostrar ID de reserva.");

        if (turnActions.PaymentLinkError != null)
            lines.Add("❌ Error generando link de pago. Informa al usuario que hubo un inconveniente técnico y que intente nuevamente.");

        if (!state.AvailabilityConfirmed && !turnActions.CheckAvailabilityExecuted && string.IsNullOrEmpty(state.AvailableTimeSlots))
            lines.Add("❌ La disponibilidad NO ha sido verificada. PROHIBIDO mostrar o inventar horarios. Los horarios de operación NO son disponibilidad real.");

        if (lines.Count == 0)
            return null;

        return "## RESTRICCIONES DEL SISTEMA\n" + string.Join("\n", lines);
    }

    // ─────────────────────────────────────────────────────────────────
    // Contexto del turno — bloque dinámico enviado al LLM de respuesta
    // ─────────────────────────────────────────────────────────────────

    private static string BuildTurnContext(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowSnapshot,
        TurnActions turnActions,
        ExtractionOutput extraction,
        Configuration.LoadedBusinessContext businessContext)
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
            sb.AppendLine($"- **Faltan:** {string.Join(", ", flowSnapshot.MissingFields.Select(f => FieldLabelResolver.Resolve(f, businessContext.Attributes)))}");

        sb.AppendLine();

        // Qué pasó en este turno
        if (turnActions.CancellationExecuted)
        {
            sb.AppendLine("**Este turno: el usuario canceló/cambió de intención. Acepta y ofrece ayuda.**");
        }
        else if (turnActions.IsAwaitingPayment
            && state.PaymentLinkExpiresAt.HasValue
            && DateTime.UtcNow > state.PaymentLinkExpiresAt.Value)
        {
            sb.AppendLine("**Link de pago expirado.** Si el usuario pide otro link o dice que expiró, indícale que puede enviar un mensaje (ej. \"envíame otro link\") y se generará uno nuevo.");
        }
        else
        {
            if (turnActions.CheckAvailabilityExecuted)
            {
                sb.AppendLine("**Este turno: se verificó disponibilidad. RESULTADO CONFIRMADO POR EL SISTEMA.**");
                sb.AppendLine("⚠️ Este resultado es DEFINITIVO y tiene prioridad sobre cualquier información previa en el historial.");
                if (state.AvailabilityConfirmed)
                {
                    if (state.DesiredTime.HasValue)
                    {
                        // Hora ya seleccionada — confirmar que ese slot está disponible.
                        // El resumen de confirmación se inyecta programáticamente en TryBuildDeterministicResponse.
                        sb.AppendLine($"→ El horario {state.DesiredTime.Value:HH:mm} está disponible.");
                    }
                    else if (!string.IsNullOrEmpty(state.AvailableTimeSlots))
                    {
                        var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        sb.AppendLine($"**Horarios disponibles confirmados por el sistema:** {string.Join(" | ", slots)}");
                        sb.AppendLine("→ OBLIGATORIO: Muestra estos horarios al cliente. El sistema verificó que SÍ hay disponibilidad.");
                    }
                }
                else
                {
                    // Slot solicitado rechazado: AvailableTimeSlots = alternativas (NO el solicitado)
                    if (!string.IsNullOrEmpty(state.AvailableTimeSlots))
                    {
                        var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        sb.AppendLine("→ El horario solicitado NO está disponible.");
                        sb.AppendLine($"**Alternativas confirmadas por el sistema:** {string.Join(" | ", slots)}");
                        sb.AppendLine("→ OBLIGATORIO: Indica que el horario pedido no está disponible y presenta estas alternativas. Pregunta cuál prefiere el cliente.");
                    }
                    else
                    {
                        sb.AppendLine("→ No hay disponibilidad para esa fecha/hora. Sugiere alternativas (otra fecha).");
                    }
                }
            }
            else if (!string.IsNullOrEmpty(state.AvailableTimeSlots) && !state.AvailabilityConfirmed)
            {
                // Check ejecutado en turno anterior; alternativas almacenadas — usuario insiste o proporciona datos sin re-check
                var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
                sb.AppendLine("**Disponibilidad ya verificada (turno anterior):** El horario solicitado NO está disponible.");
                sb.AppendLine($"**Alternativas confirmadas:** {string.Join(" | ", slots)}");
                sb.AppendLine("→ Presenta estas alternativas y pregunta cuál prefiere el cliente.");
            }

            if (turnActions.CreateReservationExecuted)
                sb.AppendLine("**Este turno: reserva creada exitosamente.** Confirma detalles y celebra.");

            // Guardia anti-alucinación: usuario preguntó disponibilidad pero el sistema
            // NO ejecutó la verificación real. Instrucción explícita según qué falta.
            if (extraction.Intentions.UserRequestedAvailability && !turnActions.CheckAvailabilityExecuted)
            {
                var missingService = string.IsNullOrWhiteSpace(state.Service);
                var missingDate = !state.DesiredDate.HasValue;

                sb.AppendLine("⚠️ **ATENCIÓN — DISPONIBILIDAD NO VERIFICADA EN ESTE TURNO.**");
                sb.AppendLine("→ NO repitas ni inventes horarios de turnos anteriores.");

                if (missingService && missingDate)
                {
                    sb.AppendLine("→ El usuario quiere saber disponibilidad pero aún no eligió servicio ni fecha.");
                    sb.AppendLine("→ INSTRUCCIÓN: Reconoce su interés en la disponibilidad y guíalo a elegir primero el servicio que le interesa. Menciona que una vez elija, podrás verificar horarios disponibles para la fecha que prefiera.");
                }
                else if (missingService)
                {
                    sb.AppendLine("→ Tiene fecha pero falta el servicio.");
                    sb.AppendLine("→ INSTRUCCIÓN: Pregunta qué servicio le interesa para verificar disponibilidad en esa fecha.");
                }
                else if (missingDate)
                {
                    sb.AppendLine($"→ Ya eligió servicio ({state.Service}) pero falta la fecha.");
                    sb.AppendLine("→ INSTRUCCIÓN: Pregunta para qué fecha le gustaría verificar disponibilidad.");
                }
                else
                {
                    sb.AppendLine("→ Servicio y fecha presentes pero el check no se ejecutó por razón técnica.");
                    sb.AppendLine("→ INSTRUCCIÓN: NO prometas verificar. Indica que no pudiste consultar la disponibilidad en este momento.");
                }
            }

            if (extraction.ExtractedFields.Any() && !turnActions.CheckAvailabilityExecuted && !turnActions.CreateReservationExecuted)
                sb.AppendLine("**Este turno: el usuario proporcionó datos nuevos.**");

            if (extraction.Intentions.IsInformationQuery)
            {
                sb.AppendLine("**El usuario pide información sobre servicios/planes.** Muéstralos sin presionar a reservar.");
                sb.AppendLine("→ Opcional: cierra con comentario cálido o invitación suave sin pregunta. El usuario solo explora.");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"**Diagnóstico:** {flowSnapshot.DiagnosticMessage}");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Instrucciones para la respuesta — stage-based + eventos de turno
    //
    // DISEÑO:
    //   - La instrucción principal viene del TransactionStage (fuente de verdad del FlowEngine).
    //   - Los TurnActions añaden contexto adicional al stage, nunca lo reemplazan.
    //   - En ConfirmingBooking (primera vez): FASE 5 retorna intro determinístico,
    //     nunca llega a este método. Las instrucciones solo aplican para turnos
    //     posteriores (resumen ya presentado) o error de pago.
    // ─────────────────────────────────────────────────────────────────

    private static string BuildResponseInstructions(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowSnapshot,
        TurnActions turnActions,
        ExtractionOutput extraction,
        Configuration.LoadedBusinessContext businessContext,
        bool isFirstMessage)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ResponseInstructionsTemplate.Header);

        // 0. Primer mensaje — SIEMPRE presentación (prioridad máxima para este caso).
        if (isFirstMessage)
        {
            sb.AppendLine(ResponseInstructionsTemplate.FirstMessageInstructions);
            sb.AppendLine();
        }

        sb.AppendLine(ResponseInstructionsTemplate.BaseInstructions);

        // 1. Cancelación: máxima prioridad — no agregar más instrucciones.
        if (turnActions.CancellationExecuted)
        {
            sb.AppendLine(ResponseInstructionsTemplate.CancellationInstructions);
            return sb.ToString();
        }

        // 2. Reserva creada: flujo terminado — no agregar instrucciones de stage.
        if (turnActions.CreateReservationExecuted)
        {
            sb.AppendLine(ResponseInstructionsTemplate.CreateReservationInstructions);
            return sb.ToString();
        }

        // 3. Add-ons requeridos: directiva exclusiva — early return en FASE 4, sin conflicto con otros pasos.
        if (turnActions.AddOnOfferingRequired)
        {
            sb.AppendLine(ResponseInstructionsTemplate.ServiceSelectedOfferAddOnsInstructions);
            return sb.ToString();
        }

        // 4. Instrucciones de turno (complementan al stage).
        if (turnActions.CheckAvailabilityExecuted
            && flowSnapshot.CurrentStage != TransactionStage.ConfirmingBooking)
        {
            sb.AppendLine(ResponseInstructionsTemplate.CheckAvailabilityInstructions);
        }

        if (extraction.Intentions.IsInformationQuery)
            sb.AppendLine(ResponseInstructionsTemplate.InformationQueryInstructions);

        if (extraction.Ambiguities.Any())
            sb.AppendLine(ResponseInstructionsTemplate.AmbiguitiesInstructions);

        // 5. Instrucción principal basada en el stage del FlowEngine.
        if (!extraction.Intentions.IsInformationQuery)
        {
            var stageInstruction = BuildStageInstruction(state, flowSnapshot, businessContext);
            if (!string.IsNullOrEmpty(stageInstruction))
                sb.AppendLine(stageInstruction);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Genera la instrucción principal según el stage del FlowEngine.
    /// Flujo: CollectingInformation → ExploringServices → CheckingAvailability → CompletingProfile → ConfirmingBooking.
    /// Cada stage tiene instrucción específica; solo CompletingProfile y fallback usan MissingFields.
    /// </summary>
    private static string BuildStageInstruction(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowSnapshot,
        Configuration.LoadedBusinessContext businessContext)
    {
        return flowSnapshot.CurrentStage switch
        {
            TransactionStage.ConfirmingBooking =>
                ConfirmationSummaryBuilder.BuildInstruction(state, flowSnapshot, businessContext),

            TransactionStage.AwaitingPayment =>
                ResponseInstructionsTemplate.AwaitingPaymentInstructions,

            TransactionStage.CollectingInformation =>
                ResponseInstructionsTemplate.CollectingInformationInstructions,

            TransactionStage.ExploringServices =>
                ResponseInstructionsTemplate.ExploringServicesInstructions,

            TransactionStage.CompletingProfile when flowSnapshot.MissingFields.Any() =>
                ResponseInstructionsTemplate.CompletingProfileInstructions
                    .Replace("{missing_fields}", string.Join(", ", flowSnapshot.MissingFields.Select(f => FieldLabelResolver.Resolve(f, businessContext.Attributes)))),

            _ when flowSnapshot.MissingFields.Any() =>
                ResponseInstructionsTemplate.MissingFieldsInstructions
                    .Replace("{missing_fields}", string.Join(", ", flowSnapshot.MissingFields.Select(f => FieldLabelResolver.Resolve(f, businessContext.Attributes)))),

            _ => string.Empty
        };
    }

    private static bool ShouldOfferAddOns(
        Domain.Models.ConversationState state,
        Configuration.LoadedBusinessContext businessContext)
    {
        if (string.IsNullOrWhiteSpace(state.Service))
            return false;

        var service = businessContext.Services.FirstOrDefault(s =>
            string.Equals(s.Name, state.Service, StringComparison.OrdinalIgnoreCase));

        if (service == null)
            return false;

        var hasCompatibleAddOns = businessContext.AddOnRules.Any(r =>
            (!r.CompatibleServiceCategory.HasValue || r.CompatibleServiceCategory.Value == service.Category) &&
            (string.IsNullOrWhiteSpace(r.CompatibleWithServiceName) || string.Equals(r.CompatibleWithServiceName, service.Name, StringComparison.OrdinalIgnoreCase)));

        return hasCompatibleAddOns;
    }

    private static ServiceCategory? ResolveSelectedCategory(
        List<ServiceInfo> services,
        string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return null;

        var service = services.FirstOrDefault(s =>
            string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase));

        return service?.Category;
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
    // Logging de extracción (siempre visible para debug)
    // ─────────────────────────────────────────────────────────────────

    private void LogExtraction(ExtractionOutput output)
    {
        var i = output.Intentions;
        var intentions = $"UserRequestedAvailability={i.UserRequestedAvailability}, UserConfirmedBooking={i.UserConfirmedBooking}, IsInformationQuery={i.IsInformationQuery}, UserWantsToCancel={i.UserWantsToCancel}, UserRequestsNewPaymentLink={i.UserRequestsNewPaymentLink}, UserSaysAlreadyPaid={i.UserSaysAlreadyPaid}";
        var fields = output.ExtractedFields.Count == 0
            ? "(ninguno)"
            : string.Join(", ", output.ExtractedFields.Select(f => $"{f.FieldName}={f.Value}(conf:{f.Confidence:F2})"));
        var ambiguities = output.Ambiguities.Count > 0 ? $" | Ambiguities: {string.Join(", ", output.Ambiguities.Select(a => $"{a.FieldName}:{a.Type}({a.Text})"))}" : "";

        _logger.LogWarning(
            "Extracción: Method={Method}, Ok={Ok} | Fields: {Fields} | Intentions: {Intentions}{Ambiguities}",
            output.Method, output.WasSuccessful, fields, intentions, ambiguities);
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

    /// <summary>
    /// Condición unificada para inyectar el resumen de confirmación.
    /// Usa PaymentLinkGenerated como señal semántica para cubrir la transición
    /// de stage (ConfirmingBooking → AwaitingPayment) que ocurre al generar el link.
    /// Excluye turnos con error de pago (el LLM comunica el error).
    /// </summary>
    private static bool ShouldInjectConfirmationSummary(ProcessingContext ctx) =>
        (ctx.FlowEvaluation.CurrentStage == TransactionStage.ConfirmingBooking
            || ctx.TurnActions.PaymentLinkGenerated)
        && !ctx.State.ReservationCreated
        && !ctx.TurnActions.CreateReservationExecuted
        && !ctx.State.ConfirmationSummaryPresented
        && ctx.TurnActions.PaymentLinkError == null;

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

        ctx.UpdateMessageMetadata(userMessage, botResponse);

        await ctx.SaveStateAsync(cancellationToken);

        _logger.LogDebug("✅ Estado guardado (Version={Version})", ctx.State.Version);
    }
}
