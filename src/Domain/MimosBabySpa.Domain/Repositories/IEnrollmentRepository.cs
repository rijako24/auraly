using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IEnrollmentRepository
{
    Task<Enrollment?> GetByPaymentTransactionIdAsync(Guid paymentTransactionId, CancellationToken ct = default);
    Task<Enrollment> CreateAsync(Enrollment enrollment, CancellationToken ct = default);
}
