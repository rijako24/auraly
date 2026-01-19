using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
