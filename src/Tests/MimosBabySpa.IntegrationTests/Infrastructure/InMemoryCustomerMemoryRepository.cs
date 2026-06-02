using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public class InMemoryCustomerMemoryRepository : ICustomerMemoryRepository
{
    private readonly List<CustomerMemory> _store = [];

    public Task<IReadOnlyList<CustomerMemory>> GetByBusinessAndUserNumberAsync(
        Guid businessId, string userNumber, CancellationToken ct = default)
    {
        IReadOnlyList<CustomerMemory> rows = _store
            .Where(m => m.BusinessId == businessId && m.UserNumber == userNumber)
            .OrderBy(m => m.Field)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task UpsertAsync(
        Guid businessId, string userNumber, string field, string value, CancellationToken ct = default)
    {
        var existing = _store.FirstOrDefault(m =>
            m.BusinessId == businessId
            && m.UserNumber == userNumber
            && m.Field.Equals(field, StringComparison.OrdinalIgnoreCase));

        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = now;
            return Task.CompletedTask;
        }

        _store.Add(new CustomerMemory
        {
            CustomerMemoryId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            Field = field,
            Value = value,
            UpdatedAt = now
        });

        return Task.CompletedTask;
    }
}
