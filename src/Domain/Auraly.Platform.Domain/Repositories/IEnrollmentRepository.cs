using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IEnrollmentRepository
{
    Task<Enrollment?> GetByPaymentTransactionIdAsync(Guid paymentTransactionId, CancellationToken ct = default);
    Task<Enrollment> CreateAsync(Enrollment enrollment, CancellationToken ct = default);
}
