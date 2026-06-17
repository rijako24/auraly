using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class PaymentConfirmationHandler : IPaymentConfirmationHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationStateManager _stateManager;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IReservationService _reservationService;
    private readonly IActiveAgentConfigResolver _activeAgentConfig;
    private readonly IRequestContextService _requestContext;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly IReservationCreatedNotificationDispatcher _reservationNotificationDispatcher;
    private readonly IAvailabilityService _availabilityService;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly ILogger<PaymentConfirmationHandler> _logger;

    public PaymentConfirmationHandler(
        IUnitOfWork unitOfWork,
        IConversationStateManager stateManager,
        IPaymentLifecycleService paymentLifecycle,
        IReservationService reservationService,
        IActiveAgentConfigResolver activeAgentConfig,
        IRequestContextService requestContext,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        IReservationCreatedNotificationDispatcher reservationNotificationDispatcher,
        IAvailabilityService availabilityService,
        ISchedulingPolicyProvider schedulingPolicy,
        ILogger<PaymentConfirmationHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _stateManager = stateManager;
        _paymentLifecycle = paymentLifecycle;
        _reservationService = reservationService;
        _activeAgentConfig = activeAgentConfig;
        _requestContext = requestContext;
        _sequenceResolver = sequenceResolver;
        _outboundDispatcher = outboundDispatcher;
        _reservationNotificationDispatcher = reservationNotificationDispatcher;
        _availabilityService = availabilityService;
        _schedulingPolicy = schedulingPolicy;
        _logger = logger;
    }

    public async Task<PaymentConfirmationResult> HandleAsync(
        string paymentReferenceId,
        string providerTransactionId,
        long amountInCents,
        string webhookPayload,
        CancellationToken ct = default)
    {
        PaymentConfirmationOutcome? outcome = null;

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var paymentTx = await _unitOfWork.PaymentTransactions
                .GetByPaymentReferenceIdForUpdateAsync(paymentReferenceId, ct);

            if (paymentTx is null)
            {
                _logger.LogWarning("Webhook: PaymentReferenceId={Ref} not found", paymentReferenceId);
                outcome = PaymentConfirmationOutcome.NotFound("Conversación no encontrada");
                return;
            }

            if (paymentTx.Status == PaymentTransactionStatus.Confirmed)
            {
                if (paymentTx.CheckoutKind == CheckoutKind.Enrollment)
                {
                    var existingEnrollment = await _unitOfWork.Enrollments
                        .GetByPaymentTransactionIdAsync(paymentTx.PaymentTransactionId, ct);
                    if (existingEnrollment is not null)
                    {
                        _logger.LogInformation("Webhook: inscripción ya procesada Ref={Ref}", paymentReferenceId);
                        outcome = PaymentConfirmationOutcome.Ok();
                        return;
                    }
                }

                if (paymentTx.ReservationId.HasValue || paymentTx.RequiresRescheduling)
                {
                    _logger.LogInformation("Webhook: pago ya procesado Ref={Ref}", paymentReferenceId);
                    outcome = PaymentConfirmationOutcome.Ok();
                    return;
                }
            }

            if (paymentTx.Status is PaymentTransactionStatus.Superseded
                or PaymentTransactionStatus.Abandoned
                or PaymentTransactionStatus.Expired)
            {
                _logger.LogWarning(
                    "Webhook: pago para checkout inactivo Ref={Ref} Status={Status}",
                    paymentReferenceId,
                    paymentTx.Status);
                paymentTx.RequiresRefund = true;
                await _unitOfWork.PaymentTransactions.SaveAsync(paymentTx, ct);
                outcome = PaymentConfirmationOutcome.Ok();
                return;
            }

            if (paymentTx.AmountInCents != amountInCents)
            {
                _logger.LogWarning(
                    "Webhook: monto no coincide esperado={Expected} recibido={Received}",
                    paymentTx.AmountInCents, amountInCents);
                outcome = PaymentConfirmationOutcome.Failed("Monto no coincide");
                return;
            }

            if (paymentTx.CheckoutKind == CheckoutKind.Enrollment)
            {
                await HandleEnrollmentPaymentAsync(paymentTx, providerTransactionId, webhookPayload, ct);
                outcome = PaymentConfirmationOutcome.Ok();
                return;
            }

            var snapshot = PaymentTransactionSnapshotMapper.ToIntentSnapshot(paymentTx);
            if (snapshot is null || !paymentTx.Snapshot_ServiceId.HasValue)
            {
                _logger.LogWarning("Webhook: snapshot incompleto Ref={Ref}", paymentReferenceId);
                outcome = PaymentConfirmationOutcome.Failed("Intent de reserva incompleto");
                return;
            }

            var service = await _unitOfWork.Services.GetByIdAsync(paymentTx.Snapshot_ServiceId.Value);
            if (service is null)
            {
                outcome = PaymentConfirmationOutcome.Failed("Servicio del snapshot no encontrado");
                return;
            }

            snapshot = snapshot with { ServiceName = service.ServiceName };

            await _paymentLifecycle.MarkConfirmedAsync(paymentTx, providerTransactionId, webhookPayload, ct);

            var state = await _stateManager.GetStateByConversationIdAsync(paymentTx.ConversationId, ct);
            if (state is null)
            {
                _logger.LogWarning("Webhook: agent state not found ConvId={ConvId}", paymentTx.ConversationId);
                outcome = PaymentConfirmationOutcome.Failed("Estado de conversación no encontrado");
                return;
            }

            var originalTime = TimeOnly.FromDateTime(snapshot.ReservationDateTime).ToString("HH:mm");
            var policy = await _schedulingPolicy.GetAsync(state.BusinessId, ct);

            var availability = await _availabilityService.CheckAvailabilityAsync(
                state.BusinessId,
                service.ServiceName,
                snapshot.ReservationDateTime.Date,
                snapshot.ReservationDateTime.TimeOfDay,
                policy,
                ct);

            if (!availability.IsAvailable)
            {
                _logger.LogWarning("Webhook: slot taken after payment Ref={Ref}", paymentReferenceId);
                await _paymentLifecycle.MarkRequiresReschedulingAsync(paymentTx, ct);

                state.Owner = ConversationOwner.Bot;
                state.ConsecutiveDegradedTurns = 0;
                await _stateManager.SaveStateAsync(paymentTx.ConversationId, state, ct);

                var stubReservation = PaymentTransactionSnapshotMapper.ToNotificationReservation(
                    paymentTx, service.ServiceName);
                await SendWebhookSequenceAsync(
                    state.BusinessId,
                    stubReservation,
                    WompiWebhookOutcomes.SlotUnavailableAfterPayment,
                    new PaymentSequenceContext
                    {
                        Amount = paymentTx.AmountInCents / 100m,
                        Currency = paymentTx.Currency,
                        OriginalTime = originalTime,
                        AvailableSlots = availability.AvailableTimeSlots
                    },
                    custom: null,
                    ct);

                outcome = PaymentConfirmationOutcome.Ok();
                return;
            }

            try
            {
                var created = await _reservationService.CreateFromIntentSnapshotAsync(
                    state.BusinessId,
                    paymentTx.ConversationId,
                    snapshot,
                    snapshot.ReservationDateTime,
                    ct);

                await _paymentLifecycle.LinkReservationAsync(paymentTx, created.ReservationId, ct);

                var reservation = await _unitOfWork.Reservations.GetByIdAsync(created.ReservationId);
                if (reservation is null)
                {
                    outcome = PaymentConfirmationOutcome.Failed("Reserva creada pero no encontrada");
                    return;
                }

                state.Owner = ConversationOwner.Bot;
                state.ConsecutiveDegradedTurns = 0;
                await CompleteRequestContextAsync(
                    state.BusinessId,
                    paymentTx.ConversationId,
                    state,
                    "payment_reservation_confirmed",
                    ct);
                await _stateManager.SaveStateAsync(paymentTx.ConversationId, state, ct);

                await _reservationNotificationDispatcher.SendForActiveAgentAsync(
                    state.BusinessId,
                    reservation,
                    custom: null,
                    ct);

                await SendWebhookSequenceAsync(
                    state.BusinessId,
                    reservation,
                    ResolveOutcome(paymentTx, WompiWebhookOutcomes.ReservationCreated),
                    payment: null,
                    custom: null,
                    ct);

                outcome = PaymentConfirmationOutcome.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook: CreateFromIntentSnapshot failed Ref={Ref}", paymentReferenceId);
                await _paymentLifecycle.MarkRequiresReschedulingAsync(paymentTx, ct);
                state.Owner = ConversationOwner.Bot;
                await _stateManager.SaveStateAsync(paymentTx.ConversationId, state, ct);
                outcome = PaymentConfirmationOutcome.Ok();
            }
        }, ct);

        return outcome?.ToResult() ?? new PaymentConfirmationResult(false, "Error interno");
    }

    private async Task SendWebhookSequenceAsync(
        Guid businessId,
        Domain.Entities.Reservation reservation,
        string outcomeKey,
        PaymentSequenceContext? payment,
        IReadOnlyDictionary<string, string>? custom,
        CancellationToken ct)
    {
        var phone = reservation.CustomerPhoneSnapshot?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("Webhook outbound: teléfono vacío, no se envía secuencia");
            return;
        }

        var agentConfig = await _activeAgentConfig.GetActiveConfigAsync(businessId, ct);
        if (agentConfig is null)
        {
            _logger.LogWarning(
                "Webhook outbound: no hay agente activo para BusinessId={BusinessId}",
                businessId);
            return;
        }

        var sequenceName = agentConfig.Webhooks.Wompi?.TryGetValue(outcomeKey, out var outcome) == true
            ? outcome.SendMessageSequence
            : null;

        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            _logger.LogWarning(
                "Webhook outbound: outcome '{Outcome}' sin sendMessageSequence para BusinessId={BusinessId}",
                outcomeKey,
                businessId);
            return;
        }

        var context = new MessageSequenceContext
        {
            Reservation = reservation,
            Payment = payment,
            Custom = custom ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var messages = await _sequenceResolver.ResolveAsync(
            businessId,
            sequenceName,
            agentConfig.MessageSequences,
            context,
            ct);

        if (messages.Count == 0)
        {
            _logger.LogWarning(
                "Webhook outbound: secuencia '{Sequence}' vacía para BusinessId={BusinessId}",
                sequenceName,
                businessId);
            return;
        }

        await _outboundDispatcher.SendAllAsync(
            businessId,
            phone,
            messages,
            reservation.ConversationId,
            ct);
    }

    private async Task HandleEnrollmentPaymentAsync(
        PaymentTransaction paymentTx,
        string providerTransactionId,
        string webhookPayload,
        CancellationToken ct)
    {
        var snapshot = ParseCheckoutSnapshot(paymentTx.CheckoutSnapshotJson)
            ?? throw new InvalidOperationException("Enrollment checkout snapshot is missing or invalid.");

        await _paymentLifecycle.MarkConfirmedAsync(paymentTx, providerTransactionId, webhookPayload, ct);

        var existing = await _unitOfWork.Enrollments.GetByPaymentTransactionIdAsync(paymentTx.PaymentTransactionId, ct);
        if (existing is null)
        {
            await _unitOfWork.Enrollments.CreateAsync(new Enrollment
            {
                EnrollmentId = Guid.NewGuid(),
                BusinessId = paymentTx.BusinessId,
                ConversationId = paymentTx.ConversationId,
                ServiceId = snapshot.ServiceId,
                PaymentTransactionId = paymentTx.PaymentTransactionId,
                CustomerName = snapshot.PayerName ?? string.Empty,
                CustomerPhone = snapshot.PaymentPhone ?? string.Empty,
                CustomerEmail = snapshot.PayerEmail,
                FixedScheduleLabel = snapshot.FixedSchedule,
                Status = EnrollmentStatus.Paid,
                CustomAttributesJson = paymentTx.CheckoutSnapshotJson,
                CreatedAt = DateTime.UtcNow
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var state = await _stateManager.GetStateByConversationIdAsync(paymentTx.ConversationId, ct);
        if (state is not null)
        {
            state.Owner = ConversationOwner.Bot;
            state.ConsecutiveDegradedTurns = 0;
            await CompleteRequestContextAsync(
                paymentTx.BusinessId,
                paymentTx.ConversationId,
                state,
                "payment_enrollment_confirmed",
                ct);
            await _stateManager.SaveStateAsync(paymentTx.ConversationId, state, ct);
        }

        var notification = new Domain.Entities.Reservation
        {
            ReservationId = Guid.Empty,
            BusinessId = paymentTx.BusinessId,
            ConversationId = paymentTx.ConversationId,
            ServiceId = snapshot.ServiceId,
            CustomerNameSnapshot = snapshot.PayerName,
            CustomerPhoneSnapshot = snapshot.PaymentPhone,
            CustomerEmailSnapshot = snapshot.PayerEmail,
            CustomAttributesJson = paymentTx.CheckoutSnapshotJson,
            Service = new Service { ServiceId = snapshot.ServiceId, ServiceName = snapshot.ServiceName ?? string.Empty }
        };

        var custom = new Dictionary<string, string>(snapshot.Facts ?? [], StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(snapshot.FixedSchedule))
            custom["fixed_schedule"] = snapshot.FixedSchedule;
        if (!string.IsNullOrWhiteSpace(snapshot.ServiceCategory))
            custom["service_category"] = snapshot.ServiceCategory;

        await SendWebhookSequenceAsync(
            paymentTx.BusinessId,
            notification,
            ResolveOutcome(paymentTx, string.Empty),
            new PaymentSequenceContext
            {
                Amount = paymentTx.AmountInCents / 100m,
                Currency = paymentTx.Currency
            },
            custom,
            ct);

    }

    private async Task CompleteRequestContextAsync(
        Guid businessId,
        Guid conversationId,
        ConversationState? state,
        string reason,
        CancellationToken ct)
    {
        if (state is null)
            return;

        var config = await _activeAgentConfig.GetActiveConfigAsync(businessId, ct);
        if (config is null)
        {
            _logger.LogWarning(
                "Webhook cleanup: no active agent config for BusinessId={BusinessId}",
                businessId);
            return;
        }

        await _requestContext.CompleteAsync(
            conversationId,
            config,
            state,
            inMemoryFacts: null,
            reason,
            ct);
    }

    private static string ResolveOutcome(PaymentTransaction payment, string legacyFallback) =>
        !string.IsNullOrWhiteSpace(payment.ConfirmationOutcome)
            ? payment.ConfirmationOutcome
            : legacyFallback;

    private static GenericCheckoutSnapshot? ParseCheckoutSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GenericCheckoutSnapshot>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class GenericCheckoutSnapshot
    {
        [JsonPropertyName("service_id")]
        public Guid ServiceId { get; set; }

        [JsonPropertyName("service_name")]
        public string? ServiceName { get; set; }

        [JsonPropertyName("service_category")]
        public string? ServiceCategory { get; set; }

        [JsonPropertyName("payer_name")]
        public string? PayerName { get; set; }

        [JsonPropertyName("payment_phone")]
        public string? PaymentPhone { get; set; }

        [JsonPropertyName("payer_email")]
        public string? PayerEmail { get; set; }

        [JsonPropertyName("fixed_schedule")]
        public string? FixedSchedule { get; set; }

        [JsonPropertyName("facts")]
        public Dictionary<string, string>? Facts { get; set; }
    }

    private sealed record PaymentConfirmationOutcome(bool Success, string? Error)
    {
        public static PaymentConfirmationOutcome Ok() => new(true, null);
        public static PaymentConfirmationOutcome Failed(string error) => new(false, error);
        public static PaymentConfirmationOutcome NotFound(string error) => new(false, error);
        public PaymentConfirmationResult ToResult() => new(Success, Error);
    }
}
