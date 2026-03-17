using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IServiceCategoryRepository
{
    Task<ServiceCategory?> GetByIdAsync(Guid serviceCategoryId);
    Task<IEnumerable<ServiceCategory>> GetByBusinessIdAsync(Guid businessId);
}
