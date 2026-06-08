using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
