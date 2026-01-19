using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessConfigurationRepository
{
    Task<IEnumerable<BusinessConfiguration>> GetByBusinessIdAsync(Guid businessId);
    Task<BusinessConfiguration?> GetByBusinessIdAndKeyAsync(Guid businessId, BusinessConfigurationKey key);
    Task<IEnumerable<BusinessConfiguration>> GetActiveByBusinessIdAsync(Guid businessId);
    Task<BusinessConfiguration> CreateAsync(BusinessConfiguration configuration);
    Task<BusinessConfiguration> UpdateAsync(BusinessConfiguration configuration);
}
