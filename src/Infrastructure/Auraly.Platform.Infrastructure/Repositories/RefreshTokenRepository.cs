using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;

    public RefreshTokenRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == token, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        await _context.RefreshTokens.AddAsync(refreshToken, ct);
    }

    public void Update(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Update(refreshToken);
    }

    public async Task RevokeAllByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        if (tokens.Count > 0)
            _context.RefreshTokens.UpdateRange(tokens);
    }
}
