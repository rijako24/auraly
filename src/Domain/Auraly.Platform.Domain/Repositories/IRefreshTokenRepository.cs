using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default);
    void Update(RefreshToken refreshToken);
    Task RevokeAllByUserIdAsync(Guid userId, CancellationToken ct = default);
}
