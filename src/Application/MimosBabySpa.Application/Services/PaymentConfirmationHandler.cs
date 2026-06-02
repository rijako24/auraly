using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.StateManagement;
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
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly IAvailabilityService _availabilityService;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IConversationLifecycleService _lifecycle;
    private readonly ILogger<PaymentConfirmationHandler> _logger;

    public PaymentConfirmationHandler(
        IUnitOfWork unitOfWork,
        IConversationStateManager stateManager,
        IPaymentLifecycleService paymentLifecycle,
        IReservationService reservationService,
        IActiveAgentConfigResolver activeAgentConfig,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        IAvailabilityService availabilityService,
        ISchedulingPolicyProvider schedulingPolicy,
        IConversationLifecycleService lifecycle,
        ILogger<PaymentConfirmationHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _stateManager = stateManager;
        _paymentLifecycle = paymentLifecycle;
        _reservationService = reservationService;
        _activeAgentConfig = activeAgentConfig;
        _sequenceResolver = sequenceResolver;
        _outboundDispatcher = outboundDispatcher;
        _availabilityService = availabilityService;
        _schedulingPolicy = schedulingPolicy;
        _lifecycle = lifecycle;
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
                if (paymentTx.ReservationId.HasValue || paymentTx.RequiresRescheduling)
                {
                    _logger.LogInformation("Webhook: pago ya procesado Ref={Ref}", paymentReferenceId);
                    outcome = PaymentConfirmationOutcome.Ok();
                    return;
                }
            }

            if (paymentTx.Status == PaymentTransactionStatus.Superseded)
            {
                _logger.LogWarning("Webhook: pago superseded Ref={Ref}", paymentReferenceId);
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
                await _stateManager.SaveStateAsync(paymentTx.ConversationId, state, ct);

                await SendWebhookSequenceAsync(
                    state.BusinessId,
                    reservation,
                    WompiWebhookOutcomes.ReservationCreated,
                    payment: null,
                    ct);

                await _lifecycle.CloseAsync(
                    paymentTx.ConversationId, ConversationCloseReasons.ReservationConfirmed, ct);
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
            Payment = payment
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

        await _outboundDispatcher.SendAllAsync(businessId, phone, messages, ct);
    }

    private sealed record PaymentConfirmationOutcome(bool Success, string? Error)
    {
        public static PaymentConfirmationOutcome Ok() => new(true, null);
        public static PaymentConfirmationOutcome Failed(string error) => new(false, error);
        public static PaymentConfirmationOutcome NotFound(string error) => new(false, error);
        public PaymentConfirmationResult ToResult() => new(Success, Error);
    }
}
