using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ServiceCoexistenceRuleRepository : IServiceCoexistenceRuleRepository
{
    private readonly ApplicationDbContext _context;

    public ServiceCoexistenceRuleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceCoexistenceRule?> GetByIdAsync(Guid ruleId)
    {
        return await _context.ServiceCoexistenceRules
            .Include(r => r.Service1)
            .Include(r => r.Service2)
            .FirstOrDefaultAsync(r => r.ServiceCoexistenceRuleId == ruleId);
    }

    public async Task<IEnumerable<ServiceCoexistenceRule>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.ServiceCoexistenceRules
            .Include(r => r.Service1)
            .Include(r => r.Service2)
            .Where(r => r.BusinessId == businessId)
            .ToListAsync();
    }

    public async Task<ServiceCoexistenceRule?> GetByServicesAsync(Guid businessId, Guid serviceId1, Guid serviceId2)
    {
        // Buscar en ambas direcciones (serviceId1-serviceId2 o serviceId2-serviceId1)
        return await _context.ServiceCoexistenceRules
            .Include(r => r.Service1)
            .Include(r => r.Service2)
            .FirstOrDefaultAsync(r => r.BusinessId == businessId &&
                                     ((r.ServiceId1 == serviceId1 && r.ServiceId2 == serviceId2) ||
                                      (r.ServiceId1 == serviceId2 && r.ServiceId2 == serviceId1)));
    }

    public Task<ServiceCoexistenceRule> CreateAsync(ServiceCoexistenceRule rule)
    {
        _context.ServiceCoexistenceRules.Add(rule);
        return Task.FromResult(rule);
    }

    public async Task DeleteAsync(Guid ruleId)
    {
        var rule = await _context.ServiceCoexistenceRules.FindAsync(ruleId);
        if (rule != null)
        {
            _context.ServiceCoexistenceRules.Remove(rule);
        }
    }
}
