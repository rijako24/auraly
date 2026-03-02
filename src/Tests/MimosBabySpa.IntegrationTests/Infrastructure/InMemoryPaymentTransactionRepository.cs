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
}
