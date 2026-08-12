using MimosBabySpa.Application.Auth.DTOs;

namespace MimosBabySpa.Application.Auth.Interfaces;

public interface IAuthService
{
    Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
