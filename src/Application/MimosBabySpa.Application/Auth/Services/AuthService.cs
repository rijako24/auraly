using MimosBabySpa.Application.Auth.DTOs;
using MimosBabySpa.Application.Auth.Interfaces;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Auth.Services;

public sealed class AuthService(
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher) : IAuthService
{
    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await unitOfWork.AppUsers.GetByIdAsync(
            userId, cancellationToken)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new InvalidOperationException(
                "La cuenta no tiene contraseña local configurada.");
        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException(
                "La contraseña actual es incorrecta.");

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PosOfflinePasswordSalt = null;
        user.PosOfflinePasswordHash = null;
        user.PosOfflinePasswordIterations = null;
        user.PosOfflinePasswordChangedAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        unitOfWork.AppUsers.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
