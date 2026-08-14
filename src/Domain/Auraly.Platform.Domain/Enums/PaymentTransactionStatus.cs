namespace Auraly.Platform.Domain.Enums;

/// <summary>
/// Estado de una transacción de pago.
/// </summary>
public enum PaymentTransactionStatus
{
    Created = 0,
    Confirmed = 1,
    Failed = 2,
    Refunded = 3,
    Expired = 4,
    Abandoned = 5,
    Superseded = 50
}
