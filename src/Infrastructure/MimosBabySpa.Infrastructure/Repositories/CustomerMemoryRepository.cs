using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class CustomerMemoryRepository : ICustomerMemoryRepository
{
    private readonly ApplicationDbContext _context;

    public CustomerMemoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CustomerMemory>> GetByBusinessAndUserNumberAsync(
        Guid businessId, string userNumber, CancellationToken ct = default)
    {
        return await _context.CustomerMemory
            .Where(m => m.BusinessId == businessId && m.UserNumber == userNumber)
            .OrderBy(m => m.Field)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(
        Guid businessId, string userNumber, string field, string value, CancellationToken ct = default)
    {
        var existing = await _context.CustomerMemory
            .FirstOrDefaultAsync(
                m => m.BusinessId == businessId
                    && m.UserNumber == userNumber
                    && m.Field == field,
                ct);

        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = now;
            _context.CustomerMemory.Update(existing);
            return;
        }

        _context.CustomerMemory.Add(new CustomerMemory
        {
            CustomerMemoryId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            Field = field,
            Value = value,
            UpdatedAt = now
        });
    }
}
