using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface ICustomerMemoryRepository
{
    Task<IReadOnlyList<CustomerMemory>> GetByBusinessAndUserNumberAsync(
        Guid businessId, string userNumber, CancellationToken ct = default);

    Task UpsertAsync(Guid businessId, string userNumber, string field, string value, CancellationToken ct = default);
}
