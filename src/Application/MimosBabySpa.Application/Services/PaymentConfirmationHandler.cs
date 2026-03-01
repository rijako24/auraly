using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación del manejador de confirmación de pago.
/// Invocado por el webhook de Wompi o el poller cuando el pago es aprobado.
/// Usa PaymentTransaction (lookup indexado) para correlacionar pago → conversación.
/// </summary>
public class PaymentConfirmationHandler : IPaymentConfirmationHandler
{
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IConversationStateManager _stateManager;
    private readonly IConversationStateUpdater _stateUpdater;
    private readonly GenericToolDispatcher _toolDispatcher;
    private readonly CachedBusinessContextProvider _contextProvider;
    private readonly ILogger<PaymentConfirmationHandler> _logger;

    public PaymentConfirmationHandler(
        IPaymentTransactionRepository paymentTransactionRepository,
        IConversationStateManager stateManager,
        IConversationStateUpdater stateUpdater,
        GenericToolDispatcher toolDispatcher,
        CachedBusinessContextProvider contextProvider,
        ILogger<PaymentConfirmationHandler> logger)
    {
        _paymentTransactionRepository = paymentTransactionRepository;
        _stateManager = stateManager;
        _stateUpdater = stateUpdater;
        _toolDispatcher = toolDispatcher;
        _contextProvider = contextProvider;
        _logger = logger;
    }

    public async Task<PaymentConfirmationResult> HandleAsync(
        string paymentReferenceId,
        string providerTransactionId,
        long amountInCents,
        string webhookPayload,
        CancellationToken ct = default)
    {
        var paymentTx = await _paymentTransactionRepository.GetByPaymentReferenceIdAsync(paymentReferenceId, ct);
        if (paymentTx == null)
        {
            _logger.LogWarning("Webhook: PaymentReferenceId={Ref} no encontrado en PaymentTransactions", paymentReferenceId);
            return new PaymentConfirmationResult(false, "Conversación no encontrada");
        }

        var state = await _stateManager.GetStateByConversationIdAsync(paymentTx.ConversationId, ct);
        if (state == null)
        {
            _logger.LogWarning("Webhook: estado no encontrado para ConversationId={ConvId}", paymentTx.ConversationId);
            return new PaymentConfirmationResult(false, "Estado de conversación no encontrado");
        }

        var conversationId = paymentTx.ConversationId;

        if (paymentTx.Status == PaymentTransactionStatus.Confirmed || state.PaymentConfirmed)
        {
            _logger.LogInformation("Webhook: pago ya procesado (idempotencia) Ref={Ref}", paymentReferenceId);
            return new PaymentConfirmationResult(true, null);
        }

        if (state.PaymentReferenceId == null)
        {
            _logger.LogWarning("Webhook: usuario canceló antes del pago Ref={Ref}", paymentReferenceId);
            return new PaymentConfirmationResult(false, "Reserva cancelada por el usuario");
        }

        if (state.AnticipoAmountInCents.HasValue && state.AnticipoAmountInCents.Value != amountInCents)
        {
            _logger.LogWarning("Webhook: monto no coincide esperado={Expected} recibido={Received}",
                state.AnticipoAmountInCents, amountInCents);
            return new PaymentConfirmationResult(false, "Monto no coincide");
        }

        paymentTx.ProviderTransactionId = providerTransactionId;
        paymentTx.Status = PaymentTransactionStatus.Confirmed;
        paymentTx.ConfirmedAt = DateTime.UtcNow;
        paymentTx.WebhookPayloadJson = webhookPayload;
        await _paymentTransactionRepository.SaveAsync(paymentTx, ct);

        _stateUpdater.ApplyConfirmationFlag(state, "PaymentConfirmed", true);
        _stateUpdater.ApplyConfirmationFlag(state, "ReservationConfirmed", true);
        await _stateManager.SaveStateAsync(conversationId, state, ct);

        var businessContext = await _contextProvider.GetOrLoadAsync(state.BusinessId, ct);
        var context = new ToolExecutionContext
        {
            ConversationId = conversationId,
            BusinessId = state.BusinessId,
            State = state,
            RequiredFields = businessContext.RequiredFields,
            UserMessage = "[Webhook pago confirmado]"
        };

        var result = await _toolDispatcher.ExecuteAsync(
            ToolType.CreateReservation,
            context,
            ct);

        if (!result.Success)
        {
            _logger.LogError("Webhook: CreateReservation falló Ref={Ref} Error={Error}",
                paymentReferenceId, result.Message);
            return new PaymentConfirmationResult(false, result.Message);
        }

        _logger.LogInformation("Webhook: reserva creada exitosamente Ref={Ref} ReservationId={ResId}",
            paymentReferenceId, state.ReservationId);
        return new PaymentConfirmationResult(true, null);
    }
}
