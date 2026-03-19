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
///   - Campos de extracción aplicados vía IConversationStateUpdater; campos de infraestructura
///     (pago, escalación) se actualizan directamente por el orquestador.
///   - FlowEngine es la única fuente de verdad para CanCheck/CanCreate.
///   - UserWantsToCancel reseteado limpiamente.
///   - CurrentStage actualizado en cada evaluación.
///   - Multitenant: businessId en cada operación de estado.
/// </summary>
public class HybridTransactionalOrchestrator
{
    private readonly IConversationStateManager _stateManager;
    private readonly IFlowEngine _flowEngine;
    private readonly CachedBusinessContextProvider _cachedContextProvider;
    private readonly IPromptProvider _systemPromptProvider;
    private readonly ILLMAdapter _llmAdapter;
    private readonly GenericToolDispatcher _toolDispatcher;
    private readonly ISmartExtractionService _extractionService;
    private readonly IMessageService _messageService;
    private readonly IConversationStateUpdater _stateUpdater;
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IEscalationNotifier _escalationNotifier;
    private readonly IEscalationConfigProvider _escalationConfig;
    private readonly IReservationService _reservationService;
    private readonly ILogger<HybridTransactionalOrchestrator> _logger;

    public HybridTransactionalOrchestrator(
        IConversationStateManager stateManager,
        IFlowEngine flowEngine,
        CachedBusinessContextProvider cachedContextProvider,
        IPromptProvider systemPromptProvider,
        ILLMAdapter llmAdapter,
        GenericToolDispatcher toolDispatcher,
        ISmartExtractionService extractionService,
        IMessageService messageService,
        IConversationStateUpdater stateUpdater,
        IPaymentLinkService paymentLinkService,
        IPaymentTransactionRepository paymentTransactionRepository,
        IEscalationNotifier escalationNotifier,
        IEscalationConfigProvider escalationConfig,
        IReservationService reservationService,
        ILogger<HybridTransactionalOrchestrator> logger)
    {
        _stateManager                  = stateManager;
        _flowEngine                    = flowEngine;
        _cachedContextProvider         = cachedContextProvider;
        _systemPromptProvider          = systemPromptProvider;
        _llmAdapter                    = llmAdapter;
        _toolDispatcher                = toolDispatcher;
        _extractionService             = extractionService;
        _messageService                = messageService;
        _stateUpdater                  = stateUpdater;
        _paymentLinkService            = paymentLinkService;
        _paymentTransactionRepository  = paymentTransactionRepository;
        _escalationNotifier             = escalationNotifier;
        _escalationConfig               = escalationConfig;
        _reservationService            = reservationService;
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
        ProcessingContext? ctx = null;

        try
        {
            _logger.LogInformation(
                "=== INICIO === Conv={ConversationId} Biz={BusinessId}",
                conversationId, businessId);

            // ── FASE 1: Contexto ──────────────────────────────────
            ctx = await LoadContextAsync(conversationId, businessId, customerPhone, userMessage, cancellationToken);

            // ── FASE 2: Extracción ────────────────────────────────
            ctx.ExtractionOutput = await ExtractInformationAsync(userMessage, ctx, cancellationToken);

            // Intención de humano: prioridad absoluta (funciona incluso si extracción falló)
            if (ctx.ExtractionOutput.Intentions.UserWantsHumanAssistance)
            {
                return await EscalateAndRespondAsync(ctx, userMessage, LocalizationConstants.EscalationMessages.Redirect, "Solicitud explícita del cliente", cancellationToken);
            }

            // Extracción degradada: pedir repetición o escalar
            if (!ctx.ExtractionOutput.WasSuccessful)
            {
                return await HandleDegradedExtractionAsync(ctx, userMessage, cancellationToken);
            }

            // ── FASE 3: Actualizar estado ─────────────────────────
            ApplyExtractionToState(ctx);

            // ── FASE 4: Acciones de flujo ─────────────────────────
            await ExecuteFlowActionsAsync(ctx, cancellationToken);

            // ── FASE 5: Generar respuesta ─────────────────────────
            var response = await GenerateResponseWithOutcomeAsync(userMessage, ctx, cancellationToken);

            // ── FASE 6: Guardar metadatos y estado (un solo save) ──
            await SaveFinalMetadataAsync(ctx, userMessage, response, cancellationToken);

            _logger.LogInformation(
                "=== FIN === {Chars} chars | Completitud={Pct}% | ReservationCreated={Created}",
                response.Length, ctx.FlowEvaluation.CompletenessPercentage, ctx.State.ReservationCreated);

            return new OrchestratorResult(response, ctx.State.ReservationCreated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en orquestador Conv={ConversationId}", conversationId);
            return await HandleFailedTurnAsync(ctx, businessId, conversationId, customerPhone, userMessage, cancellationToken);
        }
    }

    private async Task<OrchestratorResult> HandleDegradedExtractionAsync(
        ProcessingContext ctx,
        string userMessage,
        CancellationToken ct)
    {
        var escalated = await TryEscalateForDegradedTurnAsync(ctx, userMessage, ct);
        if (escalated != null)
            return escalated;

        var msg = LocalizationConstants.EscalationMessages.PleaseRepeat;
        ctx.UpdateMessageMetadata(userMessage, msg);
        await ctx.SaveStateAsync(ct);
        return new OrchestratorResult(msg, ReservationCreated: false);
    }

    private async Task<OrchestratorResult?> TryEscalateForDegradedTurnAsync(
        ProcessingContext ctx,
        string userMessage,
        CancellationToken ct)
    {
        ctx.State.ConsecutiveDegradedTurns++;
        var threshold = await _escalationConfig.GetConsecutiveDegradedThresholdAsync(ct);

        if (ctx.State.ConsecutiveDegradedTurns >= threshold)
        {
            return await EscalateAndRespondAsync(ctx, userMessage,
                LocalizationConstants.EscalationMessages.TechnicalIssues,
                $"Errores consecutivos ({ctx.State.ConsecutiveDegradedTurns})", ct);
        }

        return null;
    }

    private async Task<OrchestratorResult> EscalateAndRespondAsync(
        ProcessingContext ctx,
        string userMessage,
        string messageToUser,
        string reasonForAdmins,
        CancellationToken ct)
    {
        ctx.State.Owner = Domain.Models.ConversationOwner.Human;
        ctx.State.LastEscalatedAt = DateTime.UtcNow;
        ctx.State.ConsecutiveDegradedTurns = 0;
        ctx.UpdateMessageMetadata(userMessage, messageToUser);
        await ctx.SaveStateAsync(ct);

        // Legacy orchestrator: contacts are passed empty — escalation to Human still applies.
        // Contacts are now configured in the generic flow node (escalate.config.contacts).
        await _escalationNotifier.NotifyAsync(
            ctx.ToolContext.BusinessId,
            [],
            new EscalationNotification(
                ctx.ToolContext.ConversationId,
                ctx.State.Phone ?? "",
                reasonForAdmins,
                userMessage),
            ct);

        return new OrchestratorResult(messageToUser, ReservationCreated: false);
    }

    private async Task<OrchestratorResult> HandleFailedTurnAsync(
        ProcessingContext? ctx,
        Guid businessId,
        Guid conversationId,
        string customerPhone,
        string userMessage,
        CancellationToken ct)
    {
        try
        {
            if (ctx != null)
            {
                var escalated = await TryEscalateForDegradedTurnAsync(ctx, userMessage, ct);
                if (escalated != null)
                    return escalated;

                await ctx.SaveStateAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en handler de turno fallido Conv={ConversationId}", conversationId);
        }

        return new OrchestratorResult(LocalizationConstants.EscalationMessages.ErrorRetry, ReservationCreated: false);
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

        // Detección de nuevo ciclo: sesión completada (reset inmediato tras reserva).
        // PreviousSession captura ReservationId/Service para permitir "cambiar horario" / "avisa después" en cualquier momento.
        var inactivityPeriod = DateTime.UtcNow - state.UpdatedAt;
        var shouldResetForNewCycle = state.ReservationCreated;
        var hasTransactionalData = !string.IsNullOrWhiteSpace(state.Service)
            || state.DesiredDate.HasValue
            || state.DesiredTime.HasValue;
        var shouldResetForAbandonment = !shouldResetForNewCycle
            && inactivityPeriod.TotalHours >= OrchestrationConstants.ResumptionThresholdHours
            && hasTransactionalData;

        if (shouldResetForNewCycle || shouldResetForAbandonment)
        {
            var reason = shouldResetForNewCycle ? "sesión completada" : "inactividad prolongada";
            _logger.LogInformation(
                "Reset de estado: {Reason} (ReservationId={ReservationId}, Inactividad={Hours:F1}h)",
                reason, state.ReservationId, inactivityPeriod.TotalHours);

            state.PreviousSession = CaptureSnapshot(state, businessContext);
            _stateUpdater.ResetForResumption(state);
            ClearTransactionalAttributes(state, businessContext);
        }

        // System prompt del LLM de respuesta (dinámico por negocio y servicio elegido para filtrar add-ons)
        var selectedCategoryId = ResolveSelectedCategoryId(businessContext.Services, state.Service);
        var promptInput = new SystemPromptInput(businessContext, selectedCategoryId);
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

        ctx.ConversationHistory = await LoadConversationHistoryAsync(
            ctx.ToolContext.ConversationId, ctx.State.SessionStartedAt, cancellationToken);

        var output = await _extractionService.ExtractWithValidationAsync(
            userMessage,
            ctx.State,
            ctx.BusinessContext,
            ctx.ConversationHistory,
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
        var intentions = ctx.ExtractionOutput?.Intentions ?? new ExtractionIntentions();

        foreach (var field in ctx.ExtractionOutput?.ExtractedFields!)
        {
            if (field.Confidence < ExtractionConstants.MinConfidence)
            {
                _logger.LogDebug("Ignorado por baja confidence [{Field}={Confidence:F2}]",
                    field.FieldName, field.Confidence);
                continue;
            }

            var fieldName = field.FieldName;
            var isSelectedAddOns = string.Equals(fieldName, "SelectedAddOns", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldName, "Attribute:SelectedAddOns", StringComparison.OrdinalIgnoreCase);

            // Guardia: is_information_query → no aplicar campos de selección
            if (intentions.IsInformationQuery && (isSelectedAddOns || string.Equals(fieldName, "Service", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Ignorado por is_information_query [{Field}]", field.FieldName);
                continue;
            }

            var valueToApply = field.Value;
            if (isSelectedAddOns)
            {
                var validated = ValidateAddOnNamesAgainstCatalog(
                    field.Value, ctx.BusinessContext.AddOnRules, ctx.State.Service);
                if (string.IsNullOrEmpty(validated))
                {
                    _logger.LogDebug("SelectedAddOns ignorado: ningún nombre válido en catálogo para '{Value}'", field.Value);
                    continue;
                }
                if (validated != field.Value)
                    _logger.LogInformation("SelectedAddOns validado: '{Original}' → '{Validated}'", field.Value, validated);
                valueToApply = validated;
            }

            if (ctx.BusinessContext.Attributes.ContainsKey(fieldName))
                fieldName = $"Attribute:{fieldName}";

            var result = _stateUpdater.ApplyField(ctx.State, fieldName, valueToApply);

            if (result.Success)
                _logger.LogInformation("✓ {Field}='{Value}' (conf={Conf:F2})",
                    field.FieldName, field.Value, field.Confidence);
            else
                _logger.LogWarning("✗ {Field} no aplicado: {Msg}", field.FieldName, result.Message);
        }

        ctx.ReEvaluate();
    }

    /// <summary>
    /// Valida nombres de add-ons contra el catálogo. Retorna CSV solo con nombres válidos.
    /// </summary>
    private static string ValidateAddOnNamesAgainstCatalog(
        string value,
        IReadOnlyList<AddOnRuleInfo> addOnRules,
        string? currentService)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var names = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = new List<string>();

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var rule = addOnRules.FirstOrDefault(r =>
                string.Equals(r.AddOnName, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (rule != null)
            {
                if (string.IsNullOrWhiteSpace(rule.CompatibleWithServiceName)
                    || string.Equals(rule.CompatibleWithServiceName, currentService, StringComparison.OrdinalIgnoreCase))
                {
                    valid.Add(rule.AddOnName); // Usar nombre exacto del catálogo
                }
            }
        }

        return valid.Count == 0 ? string.Empty : string.Join(", ", valid);
    }

    // ═════════════════════════════════════════════════════════════════
    // FASE 4 — ACCIONES DE FLUJO
    // ═════════════════════════════════════════════════════════════════

    private async Task ExecuteFlowActionsAsync(ProcessingContext ctx, CancellationToken ct)
    {
        _logger.LogDebug("FASE 4: Ejecutando acciones de flujo...");

        var turnActions = new TurnActions();
        var intentions  = ctx.ExtractionOutput?.Intentions ?? new ExtractionIntentions();

        var activeReservationId = ctx.State.ReservationId ?? ctx.State.PreviousSession?.ReservationId;

        // ── 4a. Cancelación ─────────────────────────────────────
        if (intentions.UserWantsToCancel)
        {
            if (activeReservationId.HasValue && !ctx.State.ReservationCreated)
                ctx.State.ReservationCreated = true;
            _logger.LogInformation("Usuario canceló — reseteando flags transaccionales");
            _stateUpdater.ResetTransactionalFlags(ctx.State);
            ctx.ReEvaluate();
            turnActions.CancellationExecuted = true;
            ctx.TurnActions = turnActions;
            return;
        }

        // ── 4a.1. OnHold ("no puede asistir", "avisa después") ──
        if (intentions.UserWantsToHold && activeReservationId.HasValue)
        {
            var suspended = await _reservationService.SuspendAsync(activeReservationId.Value, ct);
            if (suspended)
            {
                turnActions.SuspendExecuted = true;
                _logger.LogInformation("Reserva {ReservationId} puesta en OnHold", activeReservationId);
                ctx.TurnActions = turnActions;
                return;
            }
        }

        // ── 4a.2. Setup re-agendamiento (data-driven, sin ReschedulingRequested) ──
        if (intentions.UserWantsToReschedule && activeReservationId.HasValue && !ctx.State.ReservationId.HasValue)
        {
            var prev = ctx.State.PreviousSession!;
            ctx.State.ReservationId = activeReservationId;
            ctx.State.ReservationCreated = false;
            ctx.State.Service = prev.Service ?? ctx.State.Service;
            ctx.State.PaymentConfirmed = true;
            // NO setear ReservationConfirmed: el usuario debe confirmar explícitamente el cambio (puerta ConfirmingBooking)
            foreach (var (k, v) in prev.TransactionalAttributes)
                ctx.State.SetAttribute(k, v);
            ctx.ReEvaluate();
            _logger.LogInformation("Setup re-agendamiento: ReservationId={ResId}, Service={Svc}", activeReservationId, ctx.State.Service);
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
                    ctx.State.PaymentReferenceId,
                    ctx.BusinessContext.BusinessId,
                    ct);

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

        // ── 4c. Servicios extras ────
        // Durante re-agendamiento no ofrecer add-ons (ReservationId presente, reserva aún no creada)
        var isRescheduling = ctx.State.ReservationId.HasValue && !ctx.State.ReservationCreated;
        if (!ctx.State.AddOnsOffered && ShouldOfferAddOns(ctx.State, ctx.BusinessContext) && !isRescheduling)
        {
            var alreadySelectedAddOns = HasSelectedAddOnsInExtraction(ctx.ExtractionOutput);
            turnActions.AddOnOfferingRequired = !alreadySelectedAddOns;

            _logger.LogInformation(
                alreadySelectedAddOns
                    ? "Usuario ya proporcionó servicio(s) extra(s) en este turno — marcando AddOnsOffered sin ofrecer."
                    : "Incluyendo servicios extras compatibles para servicio {Service}",
                ctx.State.Service);

            _stateUpdater.ApplyConfirmationFlag(ctx.State, "AddOnsOffered", true);
            ctx.ReEvaluate();
        }


        // ── 4c. Verificación de disponibilidad ───────────────────
        //   - CanCheckAvailability: primera verificación (AvailabilityConfirmed=false)
        //   - ShouldRecheckAvailability + UserRequestedAvailability: re-verificación explícita
        var shouldCheck = ctx.FlowEvaluation.CanCheckAvailability
            || (intentions.UserRequestedAvailability && _flowEngine.ShouldRecheckAvailability(ctx.State));

        if (shouldCheck)
        {
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
        // Invariante: el formulario SIEMPRE se muestra antes de confirmar. Solo aceptar "sí" verbal
        // si el resumen de confirmación ya fue presentado al usuario (evita saltarse el formulario).
        if (intentions.UserConfirmedBooking
            && !ctx.State.ReservationConfirmed
            && ctx.State.ConfirmationSummaryPresented
            && ctx.FlowEvaluation.CurrentStage == TransactionStage.ConfirmingBooking
            && ctx.BusinessContext.PaymentConfig is not { RequiresAnticipo: true })
        {
            _logger.LogInformation("Usuario confirmó reserva (sin anticipo)");
            _stateUpdater.ApplyConfirmationFlag(ctx.State, "ReservationConfirmed", true);
            ctx.ReEvaluate();
        }

        // ── 4e. Generar link de pago o crear transacción manual (si aplica) ─────────────────
        if (ctx.BusinessContext.PaymentConfig is { RequiresAnticipo: true } paymentConfig
            && ctx.FlowEvaluation.CurrentStage == TransactionStage.ConfirmingBooking
            && string.IsNullOrWhiteSpace(ctx.State.PaymentReferenceId)
            && !ctx.State.PaymentConfirmed
            && !turnActions.CheckAvailabilityExecuted)
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
                    Status = PaymentTransactionStatus.Created,
                    Source = PaymentTransactionSource.Automated
                };
                await _paymentTransactionRepository.SaveAsync(paymentTx, ct);

                ctx.ReEvaluate();
            }
            else
            {
                var manualTxId = Guid.NewGuid();
                var manualRefId = manualTxId.ToString("N");
                ctx.State.PaymentReferenceId = manualRefId;
                ctx.State.AnticipoAmountInCents = anticipoCents;
                turnActions.PaymentLinkError = linkResult.ErrorMessage;

                var manualTx = new PaymentTransaction
                {
                    PaymentTransactionId = manualTxId,
                    BusinessId = ctx.ToolContext.BusinessId,
                    ConversationId = ctx.ToolContext.ConversationId,
                    PaymentReferenceId = manualRefId,
                    AmountInCents = anticipoCents,
                    Currency = paymentConfig.Currency,
                    Status = PaymentTransactionStatus.Created,
                    Source = PaymentTransactionSource.Manual
                };
                await _paymentTransactionRepository.SaveAsync(manualTx, ct);

                _logger.LogError("Error generando link de pago: {Error} — transacción manual creada Ref={Ref}",
                    linkResult.ErrorMessage, manualRefId);

                ctx.ReEvaluate();
            }
        }

        // ── 4f. Crear o re-agendar reserva ───────────────────────
        if (ctx.FlowEvaluation.CanCreateReservation)
        {
            if (ctx.State.ReservationId.HasValue)
            {
                _logger.LogInformation("Re-agendando reserva {ReservationId}...", ctx.State.ReservationId);
                var rescheduled = await _reservationService.RescheduleAsync(
                    ctx.State.ReservationId.Value,
                    ctx.State.DesiredDate!.Value,
                    ctx.State.DesiredTime!.Value,
                    ct);

                if (rescheduled)
                {
                    ctx.State.ReservationCreated = true;
                    turnActions.RescheduleExecuted = true;
                    turnActions.ReservationResultMessage = $"✓ Horario actualizado a {ctx.State.DesiredDate.Value:dd/MM/yyyy} {ctx.State.DesiredTime.Value:HH:mm}";
                    ctx.ReEvaluate();
                    _logger.LogInformation("✅ Reserva {ReservationId} re-agendada", ctx.State.ReservationId);
                }
                else
                {
                    turnActions.ReservationResultMessage = "No pude cambiar el horario. Intenta con otra fecha u hora.";
                    _logger.LogWarning("Re-agendamiento falló para reserva {ReservationId}", ctx.State.ReservationId);
                }
            }
            else
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
        CancellationToken ct)
    {
        var result = await _toolDispatcher.ExecuteAsync(toolType, ctx.ToolContext, ct);

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

    private async Task<string> GenerateResponseWithOutcomeAsync(
        string userMessage,
        ProcessingContext ctx,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("FASE 5: Generando respuesta...");

        var usedFallback = false;

        try
        {
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

            var response = await GenerateConversationalResponseAsync(input, ctx.ConversationHistory, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generando respuesta — usando fallback");
            usedFallback = true;
            return BuildFallbackResponse(ctx.TurnActions, ctx.ExtractionOutput ?? new ExtractionOutput());
        }
        finally
        {
            ctx.State.ConsecutiveDegradedTurns = usedFallback
                ? ctx.State.ConsecutiveDegradedTurns + 1
                : 0;
        }
    }

    /// <summary>
    /// Construye respuesta determinística si aplica. Prioridad: post-creación > error pago > pre-confirmación > reenvío link.
    /// </summary>
    private string? TryBuildDeterministicResponse(ProcessingContext ctx)
    {
        // 1. RESERVA CREADA — resumen siempre (prioridad máxima)
        if (ctx.TurnActions.CreateReservationExecuted)
        {
            return "✅ ¡Tu reserva ha sido creada exitosamente!" +
                   ConfirmationSummaryBuilder.BuildPostCreationSummary(ctx.State, ctx.BusinessContext) +
                   "\n\n¡Nos vemos pronto! Si tienes alguna pregunta, aquí estoy.";
        }

        // 1b. RE-AGENDAMIENTO EXITOSO
        if (ctx.TurnActions.RescheduleExecuted)
        {
            return (ctx.TurnActions.ReservationResultMessage ?? "✅ Tu horario ha sido actualizado.") +
                   "\n\nSi necesitas otro cambio, aquí estoy.";
        }

        // 1c. RESERVA EN ESPERA (OnHold)
        if (ctx.TurnActions.SuspendExecuted)
        {
            return "Entendido. He dejado tu reserva en espera. Cuando definas el horario, avísame y la reactivamos.";
        }

        // 2. ERROR DE PAGO — resumen + medios manuales + escalación en FASE 6
        if (ctx.TurnActions.PaymentLinkError != null)
        {
            _stateUpdater.ApplyConfirmationFlag(ctx.State, "ConfirmationSummaryPresented", true);
            return ConfirmationSummaryBuilder.BuildManualPaymentSummary(ctx.State, ctx.BusinessContext);
        }

        // 3. PRE-CONFIRMACIÓN — primera presentación del resumen
        if (ShouldInjectConfirmationSummary(ctx))
        {
            _stateUpdater.ApplyConfirmationFlag(ctx.State, "ConfirmationSummaryPresented", true);

            // Re-agendamiento: resumen conciso pidiendo confirmación explícita del cambio
            if (ctx.State.ReservationId.HasValue && !ctx.State.ReservationCreated)
            {
                return $"Tu reserva será cambiada para el {ctx.State.DesiredDate:dd/MM/yyyy} a las {ctx.State.DesiredTime:HH:mm}. ¿Confirmas este cambio?";
            }

            // Reserva nueva: flujo estándar
            var name = ctx.State.CustomerName;
            var intro = !string.IsNullOrWhiteSpace(name) && name != "-"
                ? $"¡Gracias, {name}! Ya tengo todos los datos necesarios:"
                : "¡Perfecto! Ya tengo todos los datos necesarios:";
            return intro + ConfirmationSummaryBuilder.BuildPreConfirmationSummary(ctx.State, ctx.BusinessContext);
        }

        // 4. Reenvío de link de pago
        if (ctx.State.ConfirmationSummaryPresented
            && ctx.TurnActions.PaymentLinkGenerated
            && !ctx.State.ReservationCreated)
        {
            return "Aquí tienes el link actualizado para realizar el pago del anticipo:" +
                   ConfirmationSummaryBuilder.BuildPaymentLinkBlock(ctx.State, ctx.BusinessContext);
        }

        return null;
    }

    private async Task<string> GenerateConversationalResponseAsync(
        ResponseGenerationInput input,
        IReadOnlyList<Message> history,
        CancellationToken cancellationToken)
    {
        input.IsFirstMessage = !history.Any();
        input.IsReturningCustomer = input.IsFirstMessage && input.State.PreviousSession != null;

        var turnContext   = BuildTurnContext(input.State, input.FlowSnapshot, input.TurnActions, input.ExtractionOutput, input.BusinessContext);
        var instructions  = BuildResponseInstructions(input.State, input.FlowSnapshot, input.TurnActions, input.ExtractionOutput, input.BusinessContext, input.IsFirstMessage, input.IsReturningCustomer);

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

        var guardrails = BuildStateGuardrails(input.State, input.TurnActions, input.FlowSnapshot, input.BusinessContext);
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

        throw new InvalidOperationException("LLM retornó respuesta vacía o fallida");
    }

    // ─────────────────────────────────────────────────────────────────
    // Guardrails — restricciones críticas derivadas del estado, posicionadas
    // después del historial para máximo peso de atención. Evitan alucinaciones
    // sobre reservas y disponibilidad no verificadas.
    // ─────────────────────────────────────────────────────────────────

    private static string? BuildStateGuardrails(
        Domain.Models.ConversationState state,
        TurnActions turnActions,
        FlowEvaluationResult flowSnapshot,
        Configuration.LoadedBusinessContext businessContext)
    {
        var lines = new List<string>();

        if (!state.ReservationCreated && !turnActions.CreateReservationExecuted)
            lines.Add("❌ La reserva NO ha sido creada. PROHIBIDO afirmar que está confirmada, agendada o lista.");

        if (turnActions.IsAwaitingPayment)
            lines.Add("❌ PAGO PENDIENTE. La reserva NO existe. PROHIBIDO confirmar, agendar o mostrar ID de reserva.");

        if (!state.AvailabilityConfirmed && !turnActions.CheckAvailabilityExecuted && string.IsNullOrEmpty(state.AvailableTimeSlots))
            lines.Add("❌ La disponibilidad NO ha sido verificada. PROHIBIDO mostrar o inventar horarios. Los horarios de operación NO son disponibilidad real.");

        // Datos incompletos: PROHIBIDO presentar resumen de confirmación o pedir confirmación.
        // Solo prohibitivo — la prescripción (qué solicitar, en qué orden) viene de las ResponseInstructions por stage.
        // Excepción: si AddOnOfferingRequired, este turno es ofrecer extras — no emitir guardrail de campos.
        if (flowSnapshot.MissingFields.Count > 0 && !turnActions.AddOnOfferingRequired)
        {
            var missingLabels = flowSnapshot.MissingFields
                .Select(f => FieldLabelResolver.Resolve(f, businessContext.Attributes));
            lines.Add(
                $"❌ DATOS INCOMPLETOS — faltan: {string.Join(", ", missingLabels)}. " +
                "PROHIBIDO presentar resumen de confirmación o pedir confirmación al cliente.");
        }

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

        if (state.PreviousSession != null)
        {
            sb.AppendLine();
            sb.AppendLine("**Visita anterior (solo referencia, NO datos actuales):**");
            var status = state.PreviousSession.WasCompleted && state.PreviousSession.Date.HasValue && state.PreviousSession.Time.HasValue
                ? $"Reserva completada ({state.PreviousSession.Date:yyyy-MM-dd} a las {state.PreviousSession.Time:HH:mm})"
                : "No completó la reserva";
            sb.AppendLine($"- Servicio: {state.PreviousSession.Service ?? "—"} | Estado: {status}");
            if (state.PreviousSession.TransactionalAttributes.Any())
            {
                var prefs = string.Join(", ", state.PreviousSession.TransactionalAttributes
                    .Select(a => $"{FieldLabelResolver.Resolve($"Attribute:{a.Key}", businessContext.Attributes)}: {a.Value}"));
                sb.AppendLine($"- Preferencias: {prefs}");
            }
            sb.AppendLine("→ NUEVA SESIÓN: recopilar todo de nuevo. Las preferencias anteriores son contexto interno; solo referenciarlas si el usuario las menciona o pide recomendación.");
        }

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
        bool isFirstMessage,
        bool isReturningCustomer)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ResponseInstructionsTemplate.Header);

        // 0. Primer mensaje: cliente recurrente vs nuevo.
        if (isFirstMessage && isReturningCustomer)
        {
            sb.AppendLine(ResponseInstructionsTemplate.ReturningCustomerInstructions);
            sb.AppendLine();
        }
        else if (isFirstMessage)
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

        // 2. Reserva creada / re-agendada / suspendida: flujo terminado — no agregar instrucciones de stage.
        if (turnActions.CreateReservationExecuted || turnActions.RescheduleExecuted || turnActions.SuspendExecuted)
        {
            if (turnActions.CreateReservationExecuted)
                sb.AppendLine(ResponseInstructionsTemplate.CreateReservationInstructions);
            return sb.ToString();
        }

        // 3. Servicios extras — paso dedicado, reemplaza instrucción de stage.
        if (turnActions.AddOnOfferingRequired)
            sb.AppendLine(ResponseInstructionsTemplate.ServiceSelectedOfferAddOnsInstructions);

        // 4. Instrucciones de turno (complementan al stage).
        if (turnActions.CheckAvailabilityExecuted)
        {
            sb.AppendLine(ResponseInstructionsTemplate.CheckAvailabilityInstructions);
        }

        if (extraction.Intentions.IsInformationQuery)
            sb.AppendLine(ResponseInstructionsTemplate.InformationQueryInstructions);

        // 5. Instrucción principal basada en el stage del FlowEngine.
        // No incluir stage instruction cuando add-on offering es el paso dedicado (evita mezclar con fecha).
        // No incluir en primer mensaje de cliente recurrente (ReturningCustomerInstructions ya pregunta qué necesita).
        // No incluir ConfirmingBooking cuando se verificó disponibilidad este turno (el resumen se difirió).
        if (!extraction.Intentions.IsInformationQuery && !turnActions.AddOnOfferingRequired && !(isFirstMessage && isReturningCustomer)
            && !(turnActions.CheckAvailabilityExecuted && flowSnapshot.CurrentStage == TransactionStage.ConfirmingBooking))
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
    /// Re-agendamiento: ReservationId presente y reserva no creada — instrucciones específicas, NO ofrecer servicios.
    /// </summary>
    private static string BuildStageInstruction(
        Domain.Models.ConversationState state,
        FlowEvaluationResult flowSnapshot,
        Configuration.LoadedBusinessContext businessContext)
    {
        // Re-agendamiento: ReservationId presente pero reserva aún no creada (forma del estado)
        if (state.ReservationId.HasValue && !state.ReservationCreated)
        {
            return flowSnapshot.CurrentStage switch
            {
                TransactionStage.CollectingInformation or TransactionStage.ExploringServices =>
                    "**RE-AGENDAMIENTO: El cliente quiere cambiar el horario de su reserva existente.**\n" +
                    "Pregunta la nueva fecha y hora deseada. NO ofrezcas servicios ni catálogo.",

                TransactionStage.CheckingAvailability =>
                    "**RE-AGENDAMIENTO: Verificando disponibilidad para la nueva fecha/hora.**",

                TransactionStage.CompletingProfile when flowSnapshot.MissingFields.Any() =>
                    "**RE-AGENDAMIENTO: Faltan datos para completar el cambio.**\n" +
                    $"Solicita: {string.Join(", ", flowSnapshot.MissingFields.Select(f => FieldLabelResolver.Resolve(f, businessContext.Attributes)))}",

                _ => string.Empty
            };
        }

        // Flujo estándar: reserva nueva
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

    private static bool HasSelectedAddOnsInExtraction(ExtractionOutput? output) =>
        output?.ExtractedFields?.Any(f =>
            string.Equals(f.FieldName, "SelectedAddOns", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.FieldName, "Attribute:SelectedAddOns", StringComparison.OrdinalIgnoreCase)) ?? false;

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

        return businessContext.AddOnRules.Any(r =>
            string.IsNullOrWhiteSpace(r.CompatibleWithServiceName)
            || string.Equals(r.CompatibleWithServiceName, service.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Guid? ResolveSelectedCategoryId(
        List<ServiceInfo> services,
        string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return null;

        var service = services.FirstOrDefault(s =>
            string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase));

        return service?.CategoryId;
    }

    // ─────────────────────────────────────────────────────────────────
    // Historial conversacional (filtrado por sesión actual)
    // ─────────────────────────────────────────────────────────────────

    private async Task<List<Message>> LoadConversationHistoryAsync(
        Guid conversationId,
        DateTime sessionStartedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var all = await _messageService.GetConversationHistoryAsync(conversationId);
            var recent = all
                .Where(m => m.Timestamp >= sessionStartedAt)
                .OrderBy(m => m.Timestamp)
                .TakeLast(10)
                .ToList();

            _logger.LogDebug("Historial: {Count} mensajes (sesión desde {Since:u})", recent.Count, sessionStartedAt);
            return recent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cargando historial — continuando sin historial");
            return new List<Message>();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Snapshot y limpieza de atributos transaccionales (multi-tenant)
    // ─────────────────────────────────────────────────────────────────

    private static PreviousSessionSnapshot CaptureSnapshot(
        Domain.Models.ConversationState state,
        LoadedBusinessContext businessContext)
    {
        var transactional = businessContext.Attributes
            .Where(kvp => !kvp.Value.PersistAcrossSessions)
            .Where(kvp => state.Attributes.ContainsKey(kvp.Key)
                && !string.IsNullOrWhiteSpace(state.Attributes[kvp.Key]))
            .ToDictionary(kvp => kvp.Key, kvp => state.Attributes[kvp.Key]);

        return new PreviousSessionSnapshot
        {
            Service = state.Service,
            Date = state.DesiredDate,
            Time = state.DesiredTime,
            ReservationId = state.ReservationId,
            WasCompleted = state.ReservationCreated,
            TransactionalAttributes = transactional,
            CapturedAt = DateTime.UtcNow
        };
    }

    private static void ClearTransactionalAttributes(
        Domain.Models.ConversationState state,
        LoadedBusinessContext businessContext)
    {
        var transactionalKeys = businessContext.Attributes
            .Where(kvp => !kvp.Value.PersistAcrossSessions)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in transactionalKeys)
            state.Attributes.Remove(key);
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
        if (turnActions.RescheduleExecuted)
            return "¡El horario de tu reserva fue actualizado exitosamente! ¿Hay algo más en lo que pueda ayudarte?";
        if (turnActions.CreateReservationExecuted)
            return "¡Tu reserva fue creada exitosamente! Te enviaré los detalles. ¿Hay algo más en lo que pueda ayudarte?";
        if (turnActions.CheckAvailabilityExecuted)
            return "He verificado la disponibilidad. ¿Te gustaría reservar alguno de esos horarios?";
        if (extraction.ExtractedFields.Any())
            return "Perfecto, he registrado esa información. ¿Continuamos?";
        return "Entendido. ¿En qué más puedo ayudarte?";
    }

    /// <summary>
    /// Condición para inyectar el resumen pre-confirmación.
    /// El error de pago tiene su propio camino (TryBuildDeterministicResponse caso 2).
    /// </summary>
    private static bool ShouldInjectConfirmationSummary(ProcessingContext ctx) =>
        (ctx.FlowEvaluation.CurrentStage == TransactionStage.ConfirmingBooking
            || ctx.TurnActions.PaymentLinkGenerated)
        && !ctx.State.ReservationCreated
        && !ctx.TurnActions.CreateReservationExecuted
        && !ctx.TurnActions.RescheduleExecuted
        && !ctx.State.ConfirmationSummaryPresented
        && !ctx.TurnActions.CheckAvailabilityExecuted;

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

        if (ctx.TurnActions.PaymentLinkError != null
            && !string.IsNullOrWhiteSpace(ctx.State.PaymentReferenceId))
        {
            ctx.State.Owner = Domain.Models.ConversationOwner.Human;
            ctx.State.LastEscalatedAt = DateTime.UtcNow;
            // Legacy orchestrator: contacts passed empty — see escalate node config for generic engine.
            await _escalationNotifier.NotifyAsync(
                ctx.ToolContext.BusinessId,
                [],
                new EscalationNotification(
                    ctx.ToolContext.ConversationId,
                    ctx.State.Phone ?? "",
                    "Pago manual pendiente — link de pago no disponible. Un asesor debe verificar el pago.",
                    userMessage,
                    ctx.State.PaymentReferenceId),
                cancellationToken);
        }

        ctx.UpdateMessageMetadata(userMessage, botResponse);

        await ctx.SaveStateAsync(cancellationToken);

        _logger.LogDebug("✅ Estado guardado (Version={Version})", ctx.State.Version);
    }
}
