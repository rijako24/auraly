using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryPaymentTransactionRepository : IPaymentTransactionRepository
{
    private readonly List<PaymentTransaction> _store = [];

    public Task<PaymentTransaction?> GetByPaymentReferenceIdAsync(string paymentReferenceId, CancellationToken ct = default)
    {
        return Task.FromResult(_store.FirstOrDefault(t => t.PaymentReferenceId == paymentReferenceId));
    }

    public Task<List<PaymentTransaction>> GetPendingAutomatedTransactionsAsync(DateTime createdAfter, CancellationToken ct = default)
    {
        return Task.FromResult(_store
            .Where(t => t.Source == Domain.Enums.PaymentTransactionSource.Automated
                && t.Status == Domain.Enums.PaymentTransactionStatus.Created
                && t.CreatedAt >= createdAfter)
            .ToList());
    }

    public Task SaveAsync(PaymentTransaction transaction, CancellationToken ct = default)
    {
        var idx = _store.FindIndex(t => t.PaymentTransactionId == transaction.PaymentTransactionId);
        if (idx >= 0)
            _store[idx] = transaction;
        else
            _store.Add(transaction);
        return Task.CompletedTask;
    }

    public Task<PaymentTransaction?> GetActiveByConversationIdAsync(Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult(_store
            .Where(t => t.ConversationId == conversationId
                && t.Status == Domain.Enums.PaymentTransactionStatus.Created)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault());

    public Task<PaymentTransaction?> GetActiveByReservationIdAsync(Guid reservationId, CancellationToken ct = default) =>
        Task.FromResult(_store
            .Where(t => t.ReservationId == reservationId
                && t.Status == Domain.Enums.PaymentTransactionStatus.Created)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault());

    public Task<PaymentTransaction?> GetLatestByConversationIdAsync(Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult(_store
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault());

    public Task<PaymentTransaction?> GetByPaymentReferenceIdForUpdateAsync(string paymentReferenceId, CancellationToken ct = default) =>
        GetByPaymentReferenceIdAsync(paymentReferenceId, ct);

    public Task<PaymentTransaction?> GetPendingReschedulingByConversationIdAsync(Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult(_store
            .Where(t => t.ConversationId == conversationId
                && t.Status == Domain.Enums.PaymentTransactionStatus.Confirmed
                && t.RequiresRescheduling
                && t.ReservationId == null)
            .OrderByDescending(t => t.ConfirmedAt ?? t.CreatedAt)
            .FirstOrDefault());

    public Task<PaymentTransaction?> GetByIdAsync(Guid paymentTransactionId, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(t => t.PaymentTransactionId == paymentTransactionId));

    public Task<(IReadOnlyList<PaymentTransaction> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, Domain.Enums.PaymentTransactionStatus? status, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<decimal> GetTotalRevenueByBusinessIdAsync(
        Guid businessId, DateTime? from, DateTime? to, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<(string Date, decimal Amount)>> GetRevenueChartDataAsync(
        Guid businessId, DateTime from, DateTime to, bool groupByMonth, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
