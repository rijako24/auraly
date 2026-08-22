using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Extensions;

namespace Auraly.Platform.Infrastructure.Repositories;

public class AppRoleRepository : IAppRoleRepository
{
    private readonly ApplicationDbContext _context;

    public AppRoleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AppRole?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
        await _context.AppRoles
            .Include(r => r.UserRoles)
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId, ct);

    public async Task<AppRole?> GetWithPermissionsAsync(Guid roleId, CancellationToken ct = default) =>
        await _context.AppRoles
            .Include(r => r.UserRoles)
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.RoleId == roleId, ct);

    public async Task<IReadOnlyList<AppRole>> GetByTenantAsync(Guid? tenantId, bool includeSystemRoles = true, CancellationToken ct = default)
    {
        var query = _context.AppRoles
            .Include(r => r.UserRoles)
            .Include(r => r.RolePermissions)
            .AsQueryable();

        if (tenantId.HasValue)
        {
            query = query.Where(r => r.TenantId == tenantId || (includeSystemRoles && r.IsSystemRole && r.TenantId == null));
        }
        else if (includeSystemRoles)
        {
            query = query.Where(r => r.TenantId == null);
        }

        return await query.OrderBy(r => r.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AppRole>> GetActiveSystemRolesAsync(CancellationToken ct = default) =>
        await _context.AppRoles
            .Include(r => r.Tenant)
            .Include(r => r.RolePermissions)
            .Where(r => r.IsSystemRole && r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<AppRole> Items, int TotalCount)> GetPagedByTenantAsync(
        Guid? tenantId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _context.AppRoles
            .Include(r => r.UserRoles)
            .Include(r => r.RolePermissions)
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(r => r.TenantId == tenantId || (r.IsSystemRole && r.TenantId == null));
        else
            query = query.Where(r => r.TenantId == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(s));
        }

        return await query.OrderBy(r => r.Name).ToPagedListAsync(page, pageSize, ct);
    }

    public async Task<bool> ExistsWithNameAsync(Guid? tenantId, string normalizedName, Guid? excludeRoleId = null, CancellationToken ct = default)
    {
        var query = _context.AppRoles.Where(r => r.NormalizedName == normalizedName && r.TenantId == tenantId);
        if (excludeRoleId.HasValue)
            query = query.Where(r => r.RoleId != excludeRoleId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task AddAsync(AppRole role, CancellationToken ct = default)
    {
        await _context.AppRoles.AddAsync(role, ct);
    }

    public void Update(AppRole role)
    {
        _context.AppRoles.Update(role);
    }

    public void Delete(AppRole role)
    {
        _context.AppRoles.Remove(role);
    }
}
