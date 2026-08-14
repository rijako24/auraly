using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class ServiceAddOnRuleRepository : IServiceAddOnRuleRepository
{
    private readonly ApplicationDbContext _context;

    public ServiceAddOnRuleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ServiceAddOnRule>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.ServiceAddOnRules
            .Include(r => r.AddOnService)
            .Include(r => r.CompatibleService!)
                .ThenInclude(s => s.ServiceCategory)
            .Where(r => r.BusinessId == businessId)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.AddOnService.ServiceName)
            .ToListAsync();
    }
}
