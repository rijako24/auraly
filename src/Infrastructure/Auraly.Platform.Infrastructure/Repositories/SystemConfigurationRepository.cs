using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class SystemConfigurationRepository : ISystemConfigurationRepository
{
    private readonly ApplicationDbContext _context;

    public SystemConfigurationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SystemConfiguration?> GetByKeyAsync(SystemConfigurationKey key)
    {
        return await _context.SystemConfigurations
            .FirstOrDefaultAsync(c => c.SystemConfigurationId == key && c.IsActive);
    }

    public async Task<IEnumerable<SystemConfiguration>> GetAllActiveAsync()
    {
        return await _context.SystemConfigurations
            .Where(c => c.IsActive)
            .ToListAsync();
    }

    public Task<SystemConfiguration> CreateAsync(SystemConfiguration configuration)
    {
        _context.SystemConfigurations.Add(configuration);
        return Task.FromResult(configuration);
    }

    public Task<SystemConfiguration> UpdateAsync(SystemConfiguration configuration)
    {
        _context.SystemConfigurations.Update(configuration);
        return Task.FromResult(configuration);
    }
}
