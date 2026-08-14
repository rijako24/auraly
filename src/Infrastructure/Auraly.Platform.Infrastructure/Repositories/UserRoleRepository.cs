using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class UserRoleRepository : IUserRoleRepository
{
    private readonly ApplicationDbContext _context;

    public UserRoleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _context.UserRoles
            .Include(ur => ur.Role)
            .Include(ur => ur.Business)
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct);

    public async Task<bool> ExistsAsync(Guid userId, Guid roleId, Guid? businessId, CancellationToken ct = default)
    {
        var query = _context.UserRoles.Where(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (businessId.HasValue)
            query = query.Where(ur => ur.BusinessId == businessId);
        else
            query = query.Where(ur => ur.BusinessId == null);
        return await query.AnyAsync(ct);
    }

    public async Task AddAsync(UserRole userRole, CancellationToken ct = default)
    {
        await _context.UserRoles.AddAsync(userRole, ct);
    }

    public void Delete(UserRole userRole)
    {
        _context.UserRoles.Remove(userRole);
    }

    public async Task<UserRole?> GetAsync(Guid userId, Guid roleId, Guid? businessId, CancellationToken ct = default)
    {
        var query = _context.UserRoles
            .Include(ur => ur.Role)
            .Include(ur => ur.Business)
            .Where(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (businessId.HasValue)
            query = query.Where(ur => ur.BusinessId == businessId);
        else
            query = query.Where(ur => ur.BusinessId == null);

        return await query.FirstOrDefaultAsync(ct);
    }
}
