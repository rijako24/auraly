using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Maneja la confirmación de pago desde el webhook de Wompi.
/// Crea la reserva y envía mensaje proactivo al cliente.
/// </summary>
public interface IPaymentConfirmationHandler
{
    Task<PaymentConfirmationResult> HandleAsync(
        string paymentReferenceId,
        string providerTransactionId,
        long amountInCents,
        string webhookPayload,
        CancellationToken ct = default,
        PaymentTransactionSource? sourceOverride = null);
}

public record PaymentConfirmationResult(bool Success, string? ErrorMessage);
