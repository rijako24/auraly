using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class RolePermissionRepository : IRolePermissionRepository
{
    private readonly ApplicationDbContext _context;

    public RolePermissionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(Guid roleId, CancellationToken ct = default) =>
        await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);

    public async Task AddRangeAsync(IEnumerable<RolePermission> rolePermissions, CancellationToken ct = default)
    {
        await _context.RolePermissions.AddRangeAsync(rolePermissions, ct);
    }

    public void DeleteRange(IEnumerable<RolePermission> rolePermissions)
    {
        _context.RolePermissions.RemoveRange(rolePermissions);
    }
}
