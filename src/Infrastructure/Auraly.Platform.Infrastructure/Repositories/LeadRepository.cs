using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Extensions;

namespace Auraly.Platform.Infrastructure.Repositories;

public class LeadRepository : ILeadRepository
{
    private readonly ApplicationDbContext _context;

    public LeadRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Lead?> GetByUserNumberAsync(string userNumber)
    {
        return await _context.Leads
            .FirstOrDefaultAsync(l => l.UserNumber == userNumber);
    }

    public async Task<Lead?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber)
    {
        return await _context.Leads
            .FirstOrDefaultAsync(l => l.BusinessId == businessId && l.UserNumber == userNumber);
    }

    public async Task<IEnumerable<Lead>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.Leads
            .Where(l => l.BusinessId == businessId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    public async Task<(IReadOnlyList<Lead> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _context.Leads
            .Where(l => l.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(l =>
                l.UserNumber.ToLower().Contains(s) ||
                (l.CustomerName != null && l.CustomerName.ToLower().Contains(s)));
        }

        return await query.OrderByDescending(l => l.Timestamp).ToPagedListAsync(page, pageSize, ct);
    }

    public async Task<IReadOnlyList<Lead>> GetInactiveByBusinessIdAsync(
        Guid businessId, DateTime inactiveBeforeUtc, int limit, CancellationToken ct = default)
    {
        return await _context.Leads
            .Where(l => l.BusinessId == businessId && l.Timestamp <= inactiveBeforeUtc)
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<Lead> CreateAsync(Lead lead)
    {
        _context.Leads.Add(lead);
        return Task.FromResult(lead);
    }

    public Task<Lead> UpdateAsync(Lead lead)
    {
        _context.Leads.Update(lead);
        return Task.FromResult(lead);
    }

    public async Task<Lead?> GetByIdAsync(Guid leadId)
    {
        return await _context.Leads.FindAsync(leadId);
    }
}
