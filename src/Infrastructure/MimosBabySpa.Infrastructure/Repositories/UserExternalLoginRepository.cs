using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
