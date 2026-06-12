using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class PermissionRepository : IPermissionRepository
{
    private readonly ApplicationDbContext _context;

    public PermissionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Permissions.OrderBy(p => p.Module).ThenBy(p => p.Action).ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> GetByModuleAsync(string module, CancellationToken ct = default) =>
        await _context.Permissions.Where(p => p.Module == module).OrderBy(p => p.Action).ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> GetByRoleIdAsync(Guid roleId, CancellationToken ct = default) =>
        await _context.Permissions
            .Where(p => p.RolePermissions.Any(rp => rp.RoleId == roleId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetResourcesByUserIdAsync(Guid userId, Guid? businessId = null, CancellationToken ct = default)
    {
        var userRoles = await _context.UserRoles
            .Include(ur => ur.Role)
                .ThenInclude(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct);

        var permissions = new HashSet<string>();
        foreach (var ur in userRoles)
        {
            if (businessId.HasValue)
            {
                if (ur.BusinessId.HasValue && ur.BusinessId != businessId)
                    continue;
            }

            foreach (var rp in ur.Role.RolePermissions)
            {
                permissions.Add(rp.Permission.Resource);
            }
        }

        return permissions.ToList();
    }

    public async Task<Permission?> GetByResourceAsync(string resource, CancellationToken ct = default) =>
        await _context.Permissions.FirstOrDefaultAsync(p => p.Resource == resource, ct);

    public async Task<IReadOnlyList<Permission>> GetByIdsAsync(IEnumerable<Guid> permissionIds, CancellationToken ct = default)
    {
        var ids = permissionIds.ToList();
        return await _context.Permissions.Where(p => ids.Contains(p.PermissionId)).ToListAsync(ct);
    }

    public async Task AddAsync(Permission permission, CancellationToken ct = default)
    {
        await _context.Permissions.AddAsync(permission, ct);
    }

    public async Task AddRangeAsync(IEnumerable<Permission> permissions, CancellationToken ct = default)
    {
        await _context.Permissions.AddRangeAsync(permissions, ct);
    }

    public async Task<bool> ExistsByResourceAsync(string resource, CancellationToken ct = default) =>
        await _context.Permissions.AnyAsync(p => p.Resource == resource, ct);
}
