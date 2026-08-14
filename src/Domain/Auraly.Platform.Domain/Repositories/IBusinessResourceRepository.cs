using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessResourceRepository
{
    Task<BusinessResource?> GetByIdAsync(Guid businessResourceId);
    Task<BusinessResource?> GetByBusinessIdAndNameAsync(Guid businessId, string resourceName);
    Task<IEnumerable<BusinessResource>> GetByBusinessIdAsync(Guid businessId);
    Task<BusinessResource> CreateAsync(BusinessResource resource);
    Task<BusinessResource> UpdateAsync(BusinessResource resource);
}
