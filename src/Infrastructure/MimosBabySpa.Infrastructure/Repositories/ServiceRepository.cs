using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Extensions;

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
            .Include(s => s.ServiceCategory)
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .FirstOrDefaultAsync(s => s.ServiceId == serviceId);
    }

    public async Task<Service?> GetByBusinessIdAndNameAsync(Guid businessId, string serviceName)
    {
        return await _context.Services
            .Include(s => s.ServiceCategory)
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
            .Include(s => s.ServiceCategory)
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
            .Include(s => s.ServiceCategory)
            .Include(s => s.ResourceUsages)
                .ThenInclude(ru => ru.BusinessResource)
            .Include(s => s.BundleItems.OrderBy(b => b.DisplayOrder))
                .ThenInclude(b => b.IncludedService)
            .Where(s => s.BusinessId == businessId && s.IsActive)
            .OrderBy(s => s.ServiceName)
            .ToListAsync();
    }

    public async Task<(IReadOnlyList<Service> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _context.Services
            .Include(s => s.ServiceCategory)
            .Where(s => s.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(svc =>
                svc.ServiceName.ToLower().Contains(s) ||
                svc.Description.ToLower().Contains(s));
        }

        return await query.OrderBy(svc => svc.ServiceName).ToPagedListAsync(page, pageSize, ct);
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
