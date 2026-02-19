using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
            .Include(r => r.CompatibleService)
            .Where(r => r.BusinessId == businessId)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.AddOnService.ServiceName)
            .ToListAsync();
    }
}
