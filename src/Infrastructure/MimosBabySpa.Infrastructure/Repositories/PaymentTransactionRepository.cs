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

    public async Task<PaymentTransaction?> GetByPaymentReferenceIdForUpdateAsync(string paymentReferenceId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .FromSqlInterpolated($"SELECT * FROM dbo.PaymentTransactions WITH (UPDLOCK, ROWLOCK) WHERE PaymentReferenceId = {paymentReferenceId}")
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PaymentTransaction?> GetPendingReschedulingByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .Where(t => t.ConversationId == conversationId
                && t.Status == PaymentTransactionStatus.Confirmed
                && t.RequiresRescheduling
                && t.ReservationId == null)
            .OrderByDescending(t => t.ConfirmedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync(ct);
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
            existing.Source = transaction.Source;
            existing.ConfirmedAt = transaction.ConfirmedAt;
            existing.WebhookPayloadJson = transaction.WebhookPayloadJson;
            existing.CheckoutKind = transaction.CheckoutKind;
            existing.CheckoutSnapshotJson = transaction.CheckoutSnapshotJson;
            existing.QuoteHash = transaction.QuoteHash;
            existing.ConfirmationOutcome = transaction.ConfirmationOutcome;
            existing.LinkUrl = transaction.LinkUrl;
            existing.ExpiresAt = transaction.ExpiresAt;
            existing.ReservationId = transaction.ReservationId;
            existing.AmountInCents = transaction.AmountInCents;
            existing.Currency = transaction.Currency;
            existing.RequiresRescheduling = transaction.RequiresRescheduling;
            existing.RequiresRefund = transaction.RequiresRefund;
            existing.SupersededAt = transaction.SupersededAt;
            existing.SupersededByPaymentTransactionId = transaction.SupersededByPaymentTransactionId;
            _context.PaymentTransactions.Update(existing);
        }
        else
        {
            _context.PaymentTransactions.Add(transaction);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(PaymentTransaction transaction, CancellationToken ct = default)
    {
        var existing = await _context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.PaymentTransactionId == transaction.PaymentTransactionId, ct);

        if (existing is null)
            return;

        _context.PaymentTransactions.Remove(existing);
        await _context.SaveChangesAsync(ct);
    }
    public async Task<PaymentTransaction?> GetByIdAsync(Guid paymentTransactionId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .Include(t => t.Conversation)
            .FirstOrDefaultAsync(t => t.PaymentTransactionId == paymentTransactionId, ct);
    }

    public async Task<PaymentTransaction?> GetActiveByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.PaymentTransactions
            .Where(t => t.ConversationId == conversationId
                && t.Status == PaymentTransactionStatus.Created
                && (t.ExpiresAt == null || t.ExpiresAt > now))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PaymentTransaction?> GetActiveByReservationIdAsync(Guid reservationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.PaymentTransactions
            .Where(t => t.ReservationId == reservationId
                && t.Status == PaymentTransactionStatus.Created
                && (t.ExpiresAt == null || t.ExpiresAt > now))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PaymentTransaction?> GetLatestByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _context.PaymentTransactions
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(IReadOnlyList<PaymentTransaction> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, PaymentTransactionStatus? status, CancellationToken ct = default)
    {
        var query = _context.PaymentTransactions
            .Include(t => t.Conversation)
            .Where(t => t.BusinessId == businessId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        else
            query = query.Where(t => t.Status != PaymentTransactionStatus.Abandoned
                && t.Status != PaymentTransactionStatus.Superseded);

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

        if (groupByMonth)
        {
            var monthly = await query
                .GroupBy(t => new { Year = t.ConfirmedAt!.Value.Year, Month = t.ConfirmedAt.Value.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    AmountInCents = g.Sum(t => t.AmountInCents)
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync(ct);

            return monthly
                .Select(x => ($"{x.Year:D4}-{x.Month:D2}-01", x.AmountInCents / 100m))
                .ToList();
        }

        var daily = await query
            .GroupBy(t => t.ConfirmedAt!.Value.Date)
            .Select(g => new
            {
                Date = g.Key,
                AmountInCents = g.Sum(t => t.AmountInCents)
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        return daily
            .Select(x => (x.Date.ToString("yyyy-MM-dd"), x.AmountInCents / 100m))
            .ToList();
    }
}
