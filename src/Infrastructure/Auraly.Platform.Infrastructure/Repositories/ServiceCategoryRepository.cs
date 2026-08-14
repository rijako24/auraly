using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

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

    public async Task<ServiceCategory?> GetByBusinessIdAndNameAsync(Guid businessId, string name) =>
        await _context.ServiceCategories
            .FirstOrDefaultAsync(sc =>
                sc.BusinessId == businessId &&
                sc.Name == name &&
                sc.IsActive);

    public async Task<ServiceCategory> CreateAsync(ServiceCategory category)
    {
        category.ServiceCategoryId = Guid.NewGuid();
        category.CreatedAt = DateTime.UtcNow;
        _context.ServiceCategories.Add(category);
        await _context.SaveChangesAsync();
        return category;
    }
}
