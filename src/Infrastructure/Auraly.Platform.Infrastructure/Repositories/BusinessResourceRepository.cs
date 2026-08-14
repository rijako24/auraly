using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class BusinessResourceRepository : IBusinessResourceRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessResourceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BusinessResource?> GetByIdAsync(Guid businessResourceId)
    {
        return await _context.BusinessResources
            .FirstOrDefaultAsync(r => r.BusinessResourceId == businessResourceId);
    }

    public async Task<BusinessResource?> GetByBusinessIdAndNameAsync(Guid businessId, string resourceName)
    {
        return await _context.BusinessResources
            .FirstOrDefaultAsync(r => r.BusinessId == businessId && r.ResourceName == resourceName);
    }

    public async Task<IEnumerable<BusinessResource>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.BusinessResources
            .Where(r => r.BusinessId == businessId)
            .OrderBy(r => r.ResourceName)
            .ToListAsync();
    }

    public Task<BusinessResource> CreateAsync(BusinessResource resource)
    {
        _context.BusinessResources.Add(resource);
        return Task.FromResult(resource);
    }

    public Task<BusinessResource> UpdateAsync(BusinessResource resource)
    {
        _context.BusinessResources.Update(resource);
        return Task.FromResult(resource);
    }
}
