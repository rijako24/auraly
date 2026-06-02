using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IServiceCategoryRepository
{
    Task<ServiceCategory?> GetByIdAsync(Guid serviceCategoryId);
    Task<IEnumerable<ServiceCategory>> GetByBusinessIdAsync(Guid businessId);
    Task<ServiceCategory?> GetByBusinessIdAndNameAsync(Guid businessId, string name);
    Task<ServiceCategory> CreateAsync(ServiceCategory category);
}
