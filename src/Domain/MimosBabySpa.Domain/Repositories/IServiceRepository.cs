using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IServiceRepository
{
    Task<Service?> GetByIdAsync(Guid serviceId);
    Task<Service?> GetByBusinessIdAndNameAsync(Guid businessId, string serviceName);
    Task<IEnumerable<Service>> GetByBusinessIdAsync(Guid businessId);
    Task<IEnumerable<Service>> GetActiveByBusinessIdAsync(Guid businessId);
    Task<(IReadOnlyList<Service> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default);
    Task<Service> CreateAsync(Service service);
    Task<Service> UpdateAsync(Service service);
}
