using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

/// <summary>
/// Repositorio para transacciones de pago (auditoría e idempotencia del webhook).
/// Lookup indexado por PaymentReferenceId para correlacionar webhook → conversación.
/// </summary>
public interface IPaymentTransactionRepository
{
    /// <summary>
    /// Obtiene una transacción por su PaymentReferenceId (ID del payment link de Wompi).
    /// </summary>
    Task<PaymentTransaction?> GetByPaymentReferenceIdAsync(string paymentReferenceId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene transacciones pendientes de confirmación (Status=Created) creadas recientemente.
    /// Usado por el poller para verificar pagos que no llegaron vía webhook.
    /// </summary>
    Task<List<PaymentTransaction>> GetPendingTransactionsAsync(DateTime createdAfter, CancellationToken ct = default);

    /// <summary>
    /// Guarda o actualiza una transacción.
    /// </summary>
    Task SaveAsync(PaymentTransaction transaction, CancellationToken ct = default);
}
