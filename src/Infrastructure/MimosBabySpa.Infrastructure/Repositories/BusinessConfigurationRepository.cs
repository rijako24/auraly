using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class BusinessConfigurationRepository : IBusinessConfigurationRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessConfigurationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<BusinessConfiguration>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.BusinessConfigurations
            .Where(c => c.BusinessId == businessId)
            .ToListAsync();
    }

    public async Task<BusinessConfiguration?> GetByBusinessIdAndKeyAsync(Guid businessId, BusinessConfigurationKey key)
    {
        return await _context.BusinessConfigurations
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Key == key);
    }

    public async Task<IEnumerable<BusinessConfiguration>> GetActiveByBusinessIdAsync(Guid businessId)
    {
        return await _context.BusinessConfigurations
            .Where(c => c.BusinessId == businessId && c.IsActive)
            .ToListAsync();
    }

    public Task<BusinessConfiguration> CreateAsync(BusinessConfiguration configuration)
    {
        _context.BusinessConfigurations.Add(configuration);
        return Task.FromResult(configuration);
    }

    public Task<BusinessConfiguration> UpdateAsync(BusinessConfiguration configuration)
    {
        _context.BusinessConfigurations.Update(configuration);
        return Task.FromResult(configuration);
    }
}
