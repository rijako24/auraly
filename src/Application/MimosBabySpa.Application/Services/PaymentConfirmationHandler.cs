using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
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
    private readonly IActiveAgentConfigResolver _activeAgentConfig;
    private readonly IRequestContextService _requestContext;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly IEventNotificationDispatcher _notificationDispatcher;
    private readonly IExternalEscalationService _externalEscalations;
    private readonly IPaidCheckoutFulfillmentRegistry _fulfillmentRegistry;
    private readonly ILogger<PaymentConfirmationHandler> _logger;

    public PaymentConfirmationHandler(
        IUnitOfWork unitOfWork,
        IConversationStateManager stateManager,
        IPaymentLifecycleService paymentLifecycle,
        IActiveAgentConfigResolver activeAgentConfig,
        IRequestContextService requestContext,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        IEventNotificationDispatcher notificationDispatcher,
        IExternalEscalationService externalEscalations,
        IPaidCheckoutFulfillmentRegistry fulfillmentRegistry,
        ILogger<PaymentConfirmationHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _stateManager = stateManager;
        _paymentLifecycle = paymentLifecycle;
        _activeAgentConfig = activeAgentConfig;
        _requestContext = requestContext;
        _sequenceResolver = sequenceResolver;
        _outboundDispatcher = outboundDispatcher;
        _notificationDispatcher = notificationDispatcher;
        _externalEscalations = externalEscalations;
        _fulfillmentRegistry = fulfillmentRegistry;
        _logger = logger;
    }

    public async Task<PaymentConfirmationResult> HandleAsync(
        string paymentReferenceId,
        string providerTransactionId,
        long amountInCents,
        string webhookPayload,
        CancellationToken ct = default,
        PaymentTransactionSource? sourceOverride = null)
    {
        PaymentConfirmationOutcome? outcome = null;
        PaymentTransaction? fulfilledPayment = null;
        AgentConfig? fulfilledConfig = null;
        PaidCheckoutFulfillmentResult? fulfilledResult = null;

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var payment = await _unitOfWork.PaymentTransactions
                .GetByPaymentReferenceIdForUpdateAsync(paymentReferenceId, ct);

            if (payment is null)
            {
                _logger.LogWarning("Webhook: PaymentReferenceId={Ref} not found", paymentReferenceId);
                outcome = PaymentConfirmationOutcome.NotFound("Conversacion no encontrada");
                return;
            }

            var fulfillment = _fulfillmentRegistry.Resolve(payment.CheckoutKind);
            if (payment.Status == PaymentTransactionStatus.Confirmed
                && await fulfillment.IsFulfilledAsync(payment, ct))
            {
                _logger.LogInformation("Webhook: pago ya procesado Ref={Ref}", paymentReferenceId);
                outcome = PaymentConfirmationOutcome.Ok();
                return;
            }

            if (payment.Status is PaymentTransactionStatus.Superseded
                or PaymentTransactionStatus.Abandoned
                or PaymentTransactionStatus.Expired)
            {
                _logger.LogWarning(
                    "Webhook: pago para checkout inactivo Ref={Ref} Status={Status}",
                    paymentReferenceId,
                    payment.Status);
                payment.RequiresRefund = true;
                await _unitOfWork.PaymentTransactions.SaveAsync(payment, ct);
                outcome = PaymentConfirmationOutcome.Ok();
                return;
            }

            if (payment.AmountInCents != amountInCents)
            {
                _logger.LogWarning(
                    "Webhook: monto no coincide esperado={Expected} recibido={Received}",
                    payment.AmountInCents,
                    amountInCents);
                outcome = PaymentConfirmationOutcome.Failed("Monto no coincide");
                return;
            }

            var config = await _activeAgentConfig.GetActiveConfigAsync(payment.BusinessId, ct);
            if (config is null)
            {
                _logger.LogWarning(
                    "Webhook: no active agent config for BusinessId={BusinessId}",
                    payment.BusinessId);
                outcome = PaymentConfirmationOutcome.Failed("Agente activo no encontrado");
                return;
            }

            var state = await _stateManager.GetStateByConversationIdAsync(payment.ConversationId, ct);
            if (state is null)
            {
                _logger.LogWarning("Webhook: agent state not found ConvId={ConvId}", payment.ConversationId);
                outcome = PaymentConfirmationOutcome.Failed("Estado de conversacion no encontrado");
                return;
            }

            if (payment.Status != PaymentTransactionStatus.Confirmed)
                await _paymentLifecycle.MarkConfirmedAsync(payment, providerTransactionId, webhookPayload, ct, sourceOverride);

            var result = await fulfillment.FulfillAsync(payment, state, config, ct);

            state.ConsecutiveDegradedTurns = 0;
            await CompleteRequestContextAsync(payment.BusinessId, payment.ConversationId, config, state, result.CompletionReason, ct);
            await _stateManager.SaveStateAsync(payment.ConversationId, state, ct);

            fulfilledPayment = payment;
            fulfilledConfig = config;
            fulfilledResult = result;

            outcome = PaymentConfirmationOutcome.Ok();
        }, ct);

        if (outcome?.Success == true && fulfilledPayment is not null && fulfilledConfig is not null && fulfilledResult is not null)
        {
            await DispatchFulfillmentSideEffectsAsync(fulfilledPayment, fulfilledConfig, fulfilledResult, ct);
        }

        return outcome?.ToResult() ?? new PaymentConfirmationResult(false, "Error interno");
    }

    private async Task DispatchFulfillmentSideEffectsAsync(
        PaymentTransaction payment,
        AgentConfig config,
        PaidCheckoutFulfillmentResult result,
        CancellationToken ct)
    {
        if (result.ReservationNotification is not null)
        {
            await _notificationDispatcher.SendEventAsync(
                payment.BusinessId,
                config,
                WompiWebhookOutcomes.ReservationCreated,
                new MessageSequenceContext
                {
                    Reservation = result.ReservationNotification,
                    Custom = result.CustomPayload
                },
                ct);
        }

        if (result.NotifyCustomer)
            await SendCustomerSequenceAsync(payment, config, result, ct);

        if (result.NotifyAdmin)
            await _notificationDispatcher.SendEventAsync(payment.BusinessId, config, result.EventName, result.CustomPayload, ct);

        if (result.TriggerExternalEscalation)
        {
            await _externalEscalations.EscalateEventAsync(
                config.AgentId,
                result.EventName,
                result.TargetId,
                result.CustomPayload,
                ct);
        }
    }
    private async Task SendCustomerSequenceAsync(
        PaymentTransaction payment,
        AgentConfig config,
        PaidCheckoutFulfillmentResult result,
        CancellationToken ct)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(payment.ConversationId);
        var phone = !string.IsNullOrWhiteSpace(conversation?.UserNumber)
            ? conversation.UserNumber.Trim()
            : result.CustomerPhone?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("Webhook outbound: telefono vacio, no se envia secuencia");
            return;
        }

        var sequenceName = config.Webhooks.Wompi?.TryGetValue(result.OutcomeKey, out var outcome) == true
            ? outcome.SendMessageSequence
            : null;

        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            _logger.LogWarning(
                "Webhook outbound: outcome '{Outcome}' sin sendMessageSequence para BusinessId={BusinessId}",
                result.OutcomeKey,
                payment.BusinessId);
            return;
        }

        var messages = await _sequenceResolver.ResolveAsync(
            payment.BusinessId,
            sequenceName,
            config.MessageSequences,
            new MessageSequenceContext
            {
                Reservation = result.SequenceReservation,
                Payment = result.Payment,
                Custom = result.CustomPayload
            },
            ct);

        if (messages.Count == 0)
        {
            _logger.LogWarning(
                "Webhook outbound: secuencia '{Sequence}' vacia para BusinessId={BusinessId}",
                sequenceName,
                payment.BusinessId);
            return;
        }

        await _outboundDispatcher.SendAllAsync(
            payment.BusinessId,
            phone,
            messages,
            payment.ConversationId,
            ct);
    }

    private async Task CompleteRequestContextAsync(
        Guid businessId,
        Guid conversationId,
        AgentConfig config,
        ConversationState state,
        string reason,
        CancellationToken ct)
    {
        await _requestContext.CompleteAsync(
            conversationId,
            config,
            state,
            inMemoryFacts: null,
            reason,
            ct);
    }

    private sealed record PaymentConfirmationOutcome(bool Success, string? Error)
    {
        public static PaymentConfirmationOutcome Ok() => new(true, null);
        public static PaymentConfirmationOutcome Failed(string error) => new(false, error);
        public static PaymentConfirmationOutcome NotFound(string error) => new(false, error);
        public PaymentConfirmationResult ToResult() => new(Success, Error);
    }
}
