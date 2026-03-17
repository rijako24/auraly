using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class PaymentTransactionRepository : IPaymentTransactionRepository
{
    private readonly ApplicationDbContext _context;

    public PaymentTransactionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentTransaction?> GetByPaymentReferenceIdAsync(string paymentReferenceId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.PaymentReferenceId == paymentReferenceId, ct);
    }

    public async Task<List<PaymentTransaction>> GetPendingAutomatedTransactionsAsync(DateTime createdAfter, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .Where(t => t.Source == PaymentTransactionSource.Automated
                && t.Status == PaymentTransactionStatus.Created
                && t.CreatedAt >= createdAfter)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(PaymentTransaction transaction, CancellationToken ct = default)
    {
        var existing = await _context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.PaymentTransactionId == transaction.PaymentTransactionId, ct);

        if (existing != null)
        {
            existing.ProviderTransactionId = transaction.ProviderTransactionId;
            existing.Status = transaction.Status;
            existing.ConfirmedAt = transaction.ConfirmedAt;
            existing.WebhookPayloadJson = transaction.WebhookPayloadJson;
            _context.PaymentTransactions.Update(existing);
        }
        else
        {
            _context.PaymentTransactions.Add(transaction);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<PaymentTransaction?> GetByIdAsync(Guid paymentTransactionId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .Include(t => t.Conversation)
            .FirstOrDefaultAsync(t => t.PaymentTransactionId == paymentTransactionId, ct);
    }

    public async Task<(IReadOnlyList<PaymentTransaction> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, PaymentTransactionStatus? status, CancellationToken ct = default)
    {
        var query = _context.PaymentTransactions
            .Include(t => t.Conversation)
            .Where(t => t.BusinessId == businessId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t =>
                t.PaymentReferenceId.Contains(term) ||
                (t.ProviderTransactionId != null && t.ProviderTransactionId.Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<decimal> GetTotalRevenueByBusinessIdAsync(
        Guid businessId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var query = _context.PaymentTransactions
            .Where(t => t.BusinessId == businessId && t.Status == PaymentTransactionStatus.Confirmed);

        if (from.HasValue)
            query = query.Where(t => t.ConfirmedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.ConfirmedAt <= to.Value);

        var sum = await query.SumAsync(t => t.AmountInCents, ct);
        return sum / 100m;
    }

    public async Task<IReadOnlyList<(string Date, decimal Amount)>> GetRevenueChartDataAsync(
        Guid businessId, DateTime from, DateTime to, bool groupByMonth, CancellationToken ct = default)
    {
        var query = _context.PaymentTransactions
            .Where(t => t.BusinessId == businessId
                && t.Status == PaymentTransactionStatus.Confirmed
                && t.ConfirmedAt != null
                && t.ConfirmedAt >= from
                && t.ConfirmedAt <= to);

        var data = groupByMonth
            ? await query
                .GroupBy(t => new { Year = t.ConfirmedAt!.Value.Year, Month = t.ConfirmedAt.Value.Month })
                .Select(g => new
                {
                    Date = g.Key.Year + "-" + g.Key.Month.ToString("D2") + "-01",
                    Amount = g.Sum(t => t.AmountInCents) / 100m
                })
                .OrderBy(x => x.Date)
                .ToListAsync(ct)
            : await query
                .GroupBy(t => t.ConfirmedAt!.Value.Date)
                .Select(g => new
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Amount = g.Sum(t => t.AmountInCents) / 100m
                })
                .OrderBy(x => x.Date)
                .ToListAsync(ct);

        return data.Select(x => (x.Date, x.Amount)).ToList();
    }
}
