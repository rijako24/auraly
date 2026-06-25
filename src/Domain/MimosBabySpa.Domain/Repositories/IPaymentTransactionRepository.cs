using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

/// <summary>
/// Repositorio para transacciones de pago (auditoría e idempotencia del webhook).
/// Lookup indexado por PaymentReferenceId para correlacionar webhook → conversación.
/// </summary>
public interface IPaymentTransactionRepository
{
    Task<PaymentTransaction?> GetByPaymentReferenceIdAsync(string paymentReferenceId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene una transacción por su PaymentReferenceId con bloqueo pesimista (UPDLOCK).
    /// Debe invocarse dentro de una transacción de base de datos.
    /// </summary>
    Task<PaymentTransaction?> GetByPaymentReferenceIdForUpdateAsync(string paymentReferenceId, CancellationToken ct = default);

    Task<PaymentTransaction?> GetPendingReschedulingByConversationIdAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene una transacción por su ID.
    /// </summary>
    Task<PaymentTransaction?> GetByIdAsync(Guid paymentTransactionId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene transacciones automáticas pendientes (Source=Automated, Status=Created).
    /// Usado por el poller para verificar pagos Wompi que no llegaron vía webhook.
    /// Excluye transacciones manuales (Source=Manual) que no deben pollearse contra Wompi.
    /// </summary>
    Task<List<PaymentTransaction>> GetPendingAutomatedTransactionsAsync(DateTime createdAfter, CancellationToken ct = default);

    /// <summary>
    /// Gets paginated payment transactions for admin dashboard.
    /// </summary>
    Task<(IReadOnlyList<PaymentTransaction> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null,
        PaymentTransactionStatus? status = null, CancellationToken ct = default);

    /// <summary>
    /// Gets total confirmed revenue for a business in the given date range.
    /// </summary>
    Task<decimal> GetTotalRevenueByBusinessIdAsync(
        Guid businessId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    /// <summary>
    /// Gets revenue grouped by date for charts (daily or monthly).
    /// </summary>
    Task<IReadOnlyList<(string Date, decimal Amount)>> GetRevenueChartDataAsync(
        Guid businessId, DateTime from, DateTime to, bool groupByMonth = false, CancellationToken ct = default);

    /// <summary>
    /// Guarda o actualiza una transacción.
    /// </summary>
    Task SaveAsync(PaymentTransaction transaction, CancellationToken ct = default);

    Task DeleteAsync(PaymentTransaction transaction, CancellationToken ct = default);

    Task<PaymentTransaction?> GetActiveByConversationIdAsync(Guid conversationId, CancellationToken ct = default);

    Task<PaymentTransaction?> GetActiveByReservationIdAsync(Guid reservationId, CancellationToken ct = default);

    /// <summary>
    /// Transacción más reciente de la conversación (cualquier estado), para contexto del agente.
    /// </summary>
    Task<PaymentTransaction?> GetLatestByConversationIdAsync(Guid conversationId, CancellationToken ct = default);
}
