using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IUserExternalLoginRepository
{
    Task<UserExternalLogin?> GetAsync(string provider, string providerKey, CancellationToken ct = default);
    Task<IReadOnlyList<UserExternalLogin>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(UserExternalLogin login, CancellationToken ct = default);
}
