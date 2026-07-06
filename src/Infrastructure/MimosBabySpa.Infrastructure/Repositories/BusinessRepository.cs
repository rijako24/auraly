using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Extensions;

namespace MimosBabySpa.Infrastructure.Repositories;

public class BusinessRepository : IBusinessRepository
{
    private readonly ApplicationDbContext _context;

    public BusinessRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Business?> GetByIdAsync(Guid businessId)
    {
        return await _context.Businesses
            .FirstOrDefaultAsync(b => b.BusinessId == businessId);
    }

    public async Task<Business?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var normalized = name.Trim().ToLower();
        return await _context.Businesses
            .FirstOrDefaultAsync(b => b.Name.ToLower() == normalized, ct);
    }

    public async Task<IReadOnlyList<Business>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Businesses
            .Where(b => b.TenantId == tenantId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Business> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _context.Businesses.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(s) ||
                b.Email.ToLower().Contains(s));
        }

        return await query.OrderBy(b => b.Name).ToPagedListAsync(page, pageSize, ct);
    }
    public async Task<(IReadOnlyList<Business> Items, int TotalCount)> GetPagedByTenantIdAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _context.Businesses
            .Where(b => b.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(s) ||
                b.Email.ToLower().Contains(s));
        }

        return await query.OrderBy(b => b.Name).ToPagedListAsync(page, pageSize, ct);
    }

    public async Task<Business?> GetByIdWithConfigurationAsync(Guid businessId)
    {
        return await _context.Businesses
            .Include(b => b.WhatsAppNumbers)
            .FirstOrDefaultAsync(b => b.BusinessId == businessId);
    }

    public Task<Business> CreateAsync(Business business)
    {
        _context.Businesses.Add(business);
        return Task.FromResult(business);
    }

    public Task<Business> UpdateAsync(Business business)
    {
        _context.Businesses.Update(business);
        return Task.FromResult(business);
    }
}


