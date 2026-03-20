using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryPaymentTransactionRepository : IPaymentTransactionRepository
{
    private readonly List<PaymentTransaction> _store = [];

    public Task<PaymentTransaction?> GetByPaymentReferenceIdAsync(string paymentReferenceId, CancellationToken ct = default)
    {
        return Task.FromResult(_store.FirstOrDefault(t => t.PaymentReferenceId == paymentReferenceId));
    }

    public Task<PaymentTransaction?> GetByIdAsync(Guid paymentTransactionId, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(t => t.PaymentTransactionId == paymentTransactionId));

    public Task<(IReadOnlyList<PaymentTransaction> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null,
        PaymentTransactionStatus? status = null, CancellationToken ct = default)
    {
        IEnumerable<PaymentTransaction> q = _store.Where(t => t.BusinessId == businessId);
        if (status.HasValue)
            q = q.Where(t => t.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(t =>
                t.PaymentReferenceId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.ProviderTransactionId != null &&
                 t.ProviderTransactionId.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        var list = q.OrderByDescending(t => t.CreatedAt).ToList();
        var total = list.Count;
        var items = list
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<(IReadOnlyList<PaymentTransaction> Items, int TotalCount)>((items, total));
    }

    public Task<decimal> GetTotalRevenueByBusinessIdAsync(
        Guid businessId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = _store.Where(t =>
            t.BusinessId == businessId &&
            t.Status == PaymentTransactionStatus.Confirmed);
        if (from.HasValue)
            q = q.Where(t => (t.ConfirmedAt ?? t.CreatedAt) >= from.Value);
        if (to.HasValue)
            q = q.Where(t => (t.ConfirmedAt ?? t.CreatedAt) <= to.Value);
        var cents = q.Sum(t => t.AmountInCents);
        return Task.FromResult(cents / 100m);
    }

    public Task<IReadOnlyList<(string Date, decimal Amount)>> GetRevenueChartDataAsync(
        Guid businessId, DateTime from, DateTime to, bool groupByMonth = false, CancellationToken ct = default)
    {
        var q = _store.Where(t =>
            t.BusinessId == businessId &&
            t.Status == PaymentTransactionStatus.Confirmed);
        var at = q
            .Select(t => (At: t.ConfirmedAt ?? t.CreatedAt, Cents: t.AmountInCents))
            .Where(x => x.At >= from && x.At <= to)
            .ToList();

        var groups = groupByMonth
            ? at.GroupBy(x => x.At.ToString("yyyy-MM"))
            : at.GroupBy(x => x.At.ToString("yyyy-MM-dd"));

        var result = groups
            .Select(g => (g.Key, g.Sum(c => c.Cents) / 100m))
            .OrderBy(x => x.Key)
            .ToList();
        return Task.FromResult<IReadOnlyList<(string Date, decimal Amount)>>(result);
    }

    public Task<List<PaymentTransaction>> GetPendingAutomatedTransactionsAsync(DateTime createdAfter, CancellationToken ct = default)
    {
        return Task.FromResult(_store
            .Where(t => t.Source == PaymentTransactionSource.Automated
                && t.Status == PaymentTransactionStatus.Created
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
}
