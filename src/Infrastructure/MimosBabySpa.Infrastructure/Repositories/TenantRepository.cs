using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _context;

    public TenantRepository(ApplicationDbContext context) => _context = context;

    public async Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Businesses)
            .Include(t => t.AppUsers)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is not null) await LoadDeviceCountAsync(tenant, ct);
        return tenant;
    }

    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _context.Tenants
            .Include(t => t.Businesses)
            .Include(t => t.AppUsers)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(value) || t.Email.ToLower().Contains(value));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        foreach (var tenant in items) await LoadDeviceCountAsync(tenant, ct);
        return (items, totalCount);
    }

    private async Task LoadDeviceCountAsync(Tenant tenant, CancellationToken ct)
    {
        tenant.ActiveEnrolledDeviceCount = await _context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.EnrolledDevices WHERE TenantId={tenant.TenantId} AND IsActive=1")
            .SingleAsync(ct);
    }

    public Task RevokeActiveAuthenticationSessionsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default) =>
        _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.AuthenticationSessions
            SET Status=N'Revoked', RevokedAt={now}, RevocationReason=N'TenantDeactivated', UpdatedAt={now}
            WHERE TenantId={tenantId} AND Status=N'Active';", ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default) => await _context.Tenants.AddAsync(tenant, ct);
    public void Update(Tenant tenant) => _context.Tenants.Update(tenant);
}