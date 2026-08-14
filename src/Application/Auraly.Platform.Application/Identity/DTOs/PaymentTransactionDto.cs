namespace Auraly.Platform.Application.Identity.DTOs;

public record PaymentTransactionDto(
    Guid PaymentTransactionId,
    Guid BusinessId,
    Guid ConversationId,
    Guid? ReservationId,
    string PaymentReferenceId,
    string? ProviderTransactionId,
    long AmountInCents,
    string Currency,
    string Status,
    string Source,
    DateTime CreatedAt,
    DateTime? ConfirmedAt);
