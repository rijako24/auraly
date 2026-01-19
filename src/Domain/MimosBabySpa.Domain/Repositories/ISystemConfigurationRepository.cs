using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface ISystemConfigurationRepository
{
    Task<SystemConfiguration?> GetByKeyAsync(SystemConfigurationKey key);
    Task<IEnumerable<SystemConfiguration>> GetAllActiveAsync();
    Task<SystemConfiguration> CreateAsync(SystemConfiguration configuration);
    Task<SystemConfiguration> UpdateAsync(SystemConfiguration configuration);
}
