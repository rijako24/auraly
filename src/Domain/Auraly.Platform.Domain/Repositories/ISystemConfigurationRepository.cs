using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Repositories;

public interface ISystemConfigurationRepository
{
    Task<SystemConfiguration?> GetByKeyAsync(SystemConfigurationKey key);
    Task<IEnumerable<SystemConfiguration>> GetAllActiveAsync();
    Task<SystemConfiguration> CreateAsync(SystemConfiguration configuration);
    Task<SystemConfiguration> UpdateAsync(SystemConfiguration configuration);
}
