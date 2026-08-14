using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IServiceCategoryRepository
{
    Task<ServiceCategory?> GetByIdAsync(Guid serviceCategoryId);
    Task<IEnumerable<ServiceCategory>> GetByBusinessIdAsync(Guid businessId);
    Task<ServiceCategory?> GetByBusinessIdAndNameAsync(Guid businessId, string name);
    Task<ServiceCategory> CreateAsync(ServiceCategory category);
}
