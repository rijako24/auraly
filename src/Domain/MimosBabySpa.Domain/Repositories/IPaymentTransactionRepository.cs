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
    /// Obtiene transacciones automáticas pendientes (Source=Automated, Status=Created).
    /// Usado por el poller para verificar pagos Wompi que no llegaron vía webhook.
    /// Excluye transacciones manuales (Source=Manual) que no deben pollearse contra Wompi.
    /// </summary>
    Task<List<PaymentTransaction>> GetPendingAutomatedTransactionsAsync(DateTime createdAfter, CancellationToken ct = default);

    /// <summary>
    /// Guarda o actualiza una transacción.
    /// </summary>
    Task SaveAsync(PaymentTransaction transaction, CancellationToken ct = default);
}
