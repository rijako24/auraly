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
}
