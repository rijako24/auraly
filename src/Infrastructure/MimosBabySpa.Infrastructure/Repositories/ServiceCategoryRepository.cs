using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ServiceCategoryRepository : IServiceCategoryRepository
{
    private readonly ApplicationDbContext _context;

    public ServiceCategoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceCategory?> GetByIdAsync(Guid serviceCategoryId)
    {
        return await _context.ServiceCategories
            .FirstOrDefaultAsync(sc => sc.ServiceCategoryId == serviceCategoryId);
    }

    public async Task<IEnumerable<ServiceCategory>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.ServiceCategories
            .Where(sc => sc.BusinessId == businessId && sc.IsActive)
            .OrderBy(sc => sc.DisplayOrder)
            .ThenBy(sc => sc.Name)
            .ToListAsync();
    }
}
