using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryEnrollmentRepository : IEnrollmentRepository
{
    private readonly List<Enrollment> _store = [];

    public Task<Enrollment?> GetByPaymentTransactionIdAsync(Guid paymentTransactionId, CancellationToken ct = default) =>
        Task.FromResult(_store.FirstOrDefault(e => e.PaymentTransactionId == paymentTransactionId));

    public Task<Enrollment> CreateAsync(Enrollment enrollment, CancellationToken ct = default)
    {
        _store.Add(enrollment);
        return Task.FromResult(enrollment);
    }
}
