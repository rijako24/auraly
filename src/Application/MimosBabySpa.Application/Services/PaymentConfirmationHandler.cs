using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Manejador de confirmación de pago (webhook Wompi, poller o confirmación manual).
/// Verifica disponibilidad antes de crear la reserva. Si el slot fue tomado,
/// notifica al cliente con alternativas para que elija otra hora.
/// </summary>
public class PaymentConfirmationHandler : IPaymentConfirmationHandler
{
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IConversationStateManager _stateManager;
    private readonly IConversationStateUpdater _stateUpdater;
    private readonly IReservationService _reservationService;
    private readonly CachedBusinessContextProvider _contextProvider;
    private readonly PaymentConfirmationNotifier _confirmationNotifier;
    private readonly IAvailabilityService _availabilityService;
    private readonly ILogger<PaymentConfirmationHandler> _logger;

    public PaymentConfirmationHandler(
        IPaymentTransactionRepository paymentTransactionRepository,
        IConversationStateManager stateManager,
        IConversationStateUpdater stateUpdater,
        IReservationService reservationService,
        CachedBusinessContextProvider contextProvider,
        PaymentConfirmationNotifier confirmationNotifier,
        IAvailabilityService availabilityService,
        ILogger<PaymentConfirmationHandler> logger)
    {
        _paymentTransactionRepository = paymentTransactionRepository;
        _stateManager = stateManager;
        _stateUpdater = stateUpdater;
        _reservationService = reservationService;
        _contextProvider = contextProvider;
        _confirmationNotifier = confirmationNotifier;
        _availabilityService = availabilityService;
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
        await _stateManager.SaveStateAsync(conversationId, state, ct);

        var businessContext = await _contextProvider.GetOrLoadAsync(state.BusinessId, ct);
        var originalTime = state.DesiredTime?.ToString("HH:mm") ?? "??";

        var availability = await _availabilityService.CheckAvailabilityAsync(
            state.BusinessId,
            state.Service!,
            state.DesiredDate!.Value.ToDateTime(TimeOnly.MinValue),
            state.DesiredTime!.Value.ToTimeSpan(),
            policy: null,
            ct);

        if (!availability.IsAvailable)
        {
            _logger.LogWarning(
                "Webhook: slot tomado tras pago. Ref={Ref} Service={Service} Date={Date} Time={Time}",
                paymentReferenceId, state.Service, state.DesiredDate, originalTime);

            _stateUpdater.ApplyConfirmationFlag(state, "AvailabilityConfirmed", false);
            state.DesiredTime = null;

            if (availability.AvailableTimeSlots.Count == 0)
                state.DesiredDate = null;

            state.Owner = ConversationOwner.Bot;
            state.ConsecutiveDegradedTurns = 0;
            await _stateManager.SaveStateAsync(conversationId, state, ct);

            await _confirmationNotifier.SendSlotTakenAsync(state, originalTime, availability.AvailableTimeSlots, ct);
            return new PaymentConfirmationResult(true, null);
        }

        try
        {
            var addOnsCsv = state.GetAttribute("SelectedAddOns");
            var attributes = string.IsNullOrWhiteSpace(addOnsCsv)
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["SelectedAddOns"] = addOnsCsv };

            var reservation = await _reservationService.CreateReservationAsync(
                new CreateReservationRequest(
                    state.BusinessId, conversationId,
                    state.Service!, state.DesiredDate!.Value, state.DesiredTime!.Value,
                    state.CustomerName, state.Email, state.Phone, attributes),
                ct);

            state.ReservationCreated = true;
            state.ReservationId = reservation.ReservationId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Webhook: CreateReservation falló tras disponibilidad OK (race condition). Ref={Ref}",
                paymentReferenceId);

            var fallbackAvailability = await _availabilityService.CheckAvailabilityAsync(
                state.BusinessId,
                state.Service!,
                state.DesiredDate!.Value.ToDateTime(TimeOnly.MinValue),
                null,
                policy: null,
                ct);

            _stateUpdater.ApplyConfirmationFlag(state, "AvailabilityConfirmed", false);
            state.DesiredTime = null;

            if (fallbackAvailability.AvailableTimeSlots.Count == 0)
                state.DesiredDate = null;

            state.Owner = ConversationOwner.Bot;
            state.ConsecutiveDegradedTurns = 0;
            await _stateManager.SaveStateAsync(conversationId, state, ct);

            await _confirmationNotifier.SendSlotTakenAsync(state, originalTime, fallbackAvailability.AvailableTimeSlots, ct);
            return new PaymentConfirmationResult(true, null);
        }

        _logger.LogInformation("Webhook: reserva creada exitosamente Ref={Ref} ReservationId={ResId}",
            paymentReferenceId, state.ReservationId);

        state.Owner = ConversationOwner.Bot;
        state.ConsecutiveDegradedTurns = 0;
        await _stateManager.SaveStateAsync(conversationId, state, ct);

        await _confirmationNotifier.SendAsync(state, businessContext, ct);
        return new PaymentConfirmationResult(true, null);
    }
}
