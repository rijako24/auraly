using Auraly.Contracts.Authorization;
using System.Security.Cryptography;
using System.Text;
using MimosBabySpa.Application.Auth.Interfaces;
using Auraly.Contracts.Tenants;

namespace Auraly.Application.Tenants;

public sealed class TenantInvitationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class TenantInvitationService(
    ITenantProvisioningStore store,
    IPasswordHasher passwordHasher,
    TimeProvider clock)
{
    public async Task<AcceptTenantInvitationResult> AcceptAsync(
        AcceptTenantInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.Token?.Trim() ?? string.Empty;
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new TenantInvitationException("InvalidInvitation", "La invitación no es válida.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
            throw new TenantInvitationException("WeakPassword", "La contraseña debe tener al menos 10 caracteres.");
        if (!string.Equals(request.Password, request.PasswordConfirmation, StringComparison.Ordinal))
            throw new TenantInvitationException("PasswordMismatch", "Las contraseñas no coinciden.");

        var now = clock.GetUtcNow();
        var offline = PosOfflinePasswordHasher.Hash(request.Password, now);
        var material = new TenantInvitationPasswordMaterial(
            passwordHasher.Hash(request.Password),
            offline.Salt,
            offline.Hash,
            offline.Iterations,
            offline.ChangedAt);
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return await store.AcceptInvitationAsync(tokenHash, material, now, cancellationToken);
    }
}
