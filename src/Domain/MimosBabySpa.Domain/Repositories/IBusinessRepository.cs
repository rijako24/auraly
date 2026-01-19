using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessRepository
{
    Task<Business?> GetByIdAsync(Guid businessId);
    Task<Business?> GetByIdWithConfigurationAsync(Guid businessId);
    Task<Business> CreateAsync(Business business);
    Task<Business> UpdateAsync(Business business);
}
