using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class EnrollmentRepository : IEnrollmentRepository
{
    private readonly ApplicationDbContext _context;

    public EnrollmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Enrollment?> GetByPaymentTransactionIdAsync(Guid paymentTransactionId, CancellationToken ct = default)
    {
        return _context.Enrollments
            .Include(e => e.Service)
            .FirstOrDefaultAsync(e => e.PaymentTransactionId == paymentTransactionId, ct);
    }

    public Task<Enrollment> CreateAsync(Enrollment enrollment, CancellationToken ct = default)
    {
        _context.Enrollments.Add(enrollment);
        return Task.FromResult(enrollment);
    }
}
