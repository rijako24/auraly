using Auraly.Contracts.Authorization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Platform.Application.Auth.Interfaces;
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

        if (request.IdentificationType is not ("CC" or "CE" or "PAS"))
            throw new TenantInvitationException("InvalidIdentificationType", "Selecciona un tipo de identificación válido.");
        if (string.IsNullOrWhiteSpace(request.Identification))
            throw new TenantInvitationException("IdentificationRequired", "La identificación es obligatoria.");
        if (string.IsNullOrWhiteSpace(request.FirstName))
            throw new TenantInvitationException("FirstNameRequired", "Los nombres son obligatorios.");
        if (string.IsNullOrWhiteSpace(request.LastName))
            throw new TenantInvitationException("LastNameRequired", "Los apellidos son obligatorios.");
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            throw new TenantInvitationException("EmailRequired", "Escribe un correo válido.");
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new TenantInvitationException("PhoneRequired", "El teléfono es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.Address))
            throw new TenantInvitationException("AddressRequired", "La dirección es obligatoria.");

        var now = clock.GetUtcNow();
        var offline = PosOfflinePasswordHasher.Hash(request.Password, now);
        var material = new TenantInvitationPasswordMaterial(
            passwordHasher.Hash(request.Password),
            offline.Salt,
            offline.Hash,
            offline.Iterations,
            offline.ChangedAt);
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var profile = new TenantInvitationAdministratorProfile(
            request.IdentificationType.Trim(),
            request.Identification.Trim(),
            request.FirstName.Trim(),
            request.LastName.Trim(),
            request.Email.Trim(),
            request.Phone.Trim(),
            request.Address.Trim());
        return await store.AcceptInvitationAsync(tokenHash, profile, material, now, cancellationToken);
    }
}
