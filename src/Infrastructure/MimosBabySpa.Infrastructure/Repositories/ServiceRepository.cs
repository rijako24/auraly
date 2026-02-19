using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ServiceRepository : IServiceRepository
{
    private readonly ApplicationDbContext _context;

    public ServiceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Service?> GetByIdAsync(Guid serviceId)
    {
        return await _context.Services
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .FirstOrDefaultAsync(s => s.ServiceId == serviceId);
    }

    public async Task<Service?> GetByBusinessIdAndNameAsync(Guid businessId, string serviceName)
    {
        return await _context.Services
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .FirstOrDefaultAsync(s => s.BusinessId == businessId &&
                                     s.ServiceName == serviceName &&
                                     s.IsActive);
    }

    public async Task<IEnumerable<Service>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.Services
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .Where(s => s.BusinessId == businessId)
            .OrderBy(s => s.ServiceName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Service>> GetActiveByBusinessIdAsync(Guid businessId)
    {
        return await _context.Services
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .Where(s => s.BusinessId == businessId && s.IsActive)
            .OrderBy(s => s.ServiceName)
            .ToListAsync();
    }

    public Task<Service> CreateAsync(Service service)
    {
        _context.Services.Add(service);
        return Task.FromResult(service);
    }

    public Task<Service> UpdateAsync(Service service)
    {
        _context.Services.Update(service);
        return Task.FromResult(service);
    }
}
