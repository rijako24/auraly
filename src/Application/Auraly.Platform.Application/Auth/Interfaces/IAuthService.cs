using Auraly.Platform.Application.Auth.DTOs;

namespace Auraly.Platform.Application.Auth.Interfaces;

public interface IAuthService
{
    Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
