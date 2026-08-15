using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class AppUserRepository : IAppUserRepository
{
    private readonly ApplicationDbContext _context;

    public AppUserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct = default) =>
        await _context.AppUsers
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task<AppUser?> GetByUsernameAsync(string normalizedUsername, CancellationToken ct = default) =>
        await _context.AppUsers
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, ct);

    public async Task<AppUser?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        await _context.AppUsers
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task<AppUser?> GetByExternalLoginAsync(string provider, string providerKey, CancellationToken ct = default) =>
        await _context.AppUsers
            .Include(u => u.Tenant)
            .Where(u => u.ExternalLogins.Any(e => e.Provider == provider && e.ProviderKey == providerKey))
            .FirstOrDefaultAsync(ct);

    public async Task<AppUser?> GetWithRolesAndPermissionsAsync(Guid userId, CancellationToken ct = default) =>
        await _context.AppUsers
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Business)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task<(IReadOnlyList<AppUser> Items, int TotalCount)> GetPagedByTenantAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _context.AppUsers
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Business)
            .Where(u => u.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s) ||
                (u.FirstName + " " + u.LastName).ToLower().Contains(s));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
    public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _context.AppUsers.CountAsync(user => user.TenantId == tenantId && user.IsActive, ct);


    public async Task<bool> ExistsWithUsernameAsync(Guid tenantId, string normalizedUsername, Guid? excludeUserId = null, CancellationToken ct = default)
    {
        var query = _context.AppUsers.Where(u => u.TenantId == tenantId && u.NormalizedUsername == normalizedUsername);
        if (excludeUserId.HasValue)
            query = query.Where(u => u.UserId != excludeUserId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task<bool> ExistsWithEmailAsync(Guid tenantId, string normalizedEmail, Guid? excludeUserId = null, CancellationToken ct = default)
    {
        var query = _context.AppUsers.Where(u => u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail);
        if (excludeUserId.HasValue)
            query = query.Where(u => u.UserId != excludeUserId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task AddAsync(AppUser user, CancellationToken ct = default)
    {
        await _context.AppUsers.AddAsync(user, ct);
    }

    public void Update(AppUser user)
    {
        _context.AppUsers.Update(user);
    }
}
