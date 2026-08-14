using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class UserExternalLoginRepository : IUserExternalLoginRepository
{
    private readonly ApplicationDbContext _context;

    public UserExternalLoginRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserExternalLogin?> GetAsync(string provider, string providerKey, CancellationToken ct = default) =>
        await _context.UserExternalLogins
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ProviderKey == providerKey, ct);

    public async Task<IReadOnlyList<UserExternalLogin>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _context.UserExternalLogins
            .Where(e => e.UserId == userId)
            .ToListAsync(ct);

    public async Task AddAsync(UserExternalLogin login, CancellationToken ct = default)
    {
        await _context.UserExternalLogins.AddAsync(login, ct);
    }
}
