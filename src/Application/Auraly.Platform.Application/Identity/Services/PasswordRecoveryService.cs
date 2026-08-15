using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Auraly.Contracts.Authorization;
using Auraly.Platform.Application.Auth.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class PasswordRecoveryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PasswordRecoveryService(
    IPasswordRecoveryStore store,
    IPasswordHasher passwordHasher,
    TimeProvider clock)
{
    public async Task<RequestPasswordRecoveryResult> RequestAsync(
        RequestPasswordRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantKey = request.TenantKey?.Trim() ?? string.Empty;
        var username = request.Username?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        if (!tenantKey.StartsWith('@') || tenantKey.Length < 3)
            throw new PasswordRecoveryException("InvalidTenantKey", "Escribe una clave de empresa válida.");
        if (string.IsNullOrWhiteSpace(username))
            throw new PasswordRecoveryException("UsernameRequired", "Escribe tu usuario.");
        try { _ = new MailAddress(email); }
        catch { throw new PasswordRecoveryException("EmailRequired", "Escribe el correo asociado a tu usuario."); }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var now = clock.GetUtcNow();
        await store.CreateAsync(
            new RequestPasswordRecoveryRequest(tenantKey, username, email),
            Guid.NewGuid(), rawToken, tokenHash, now, now.AddMinutes(30), cancellationToken);
        return new RequestPasswordRecoveryResult(MaskEmail(email), "Requested");
    }

    public async Task ConfirmAsync(ConfirmPasswordRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.Token?.Trim() ?? string.Empty;
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw Unavailable();
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
            throw new PasswordRecoveryException("WeakPassword", "La contraseña debe tener al menos 10 caracteres.");
        if (!string.Equals(request.Password, request.PasswordConfirmation, StringComparison.Ordinal))
            throw new PasswordRecoveryException("PasswordMismatch", "Las contraseñas no coinciden.");

        var now = clock.GetUtcNow();
        var offline = PosOfflinePasswordHasher.Hash(request.Password, now);
        var material = new PasswordRecoveryMaterial(
            passwordHasher.Hash(request.Password), offline.Salt, offline.Hash,
            offline.Iterations, offline.ChangedAt);
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (!await store.ConsumeAsync(tokenHash, material, now, cancellationToken)) throw Unavailable();
    }

    private static PasswordRecoveryException Unavailable() =>
        new("PasswordRecoveryUnavailable", "El enlace no es válido, ya fue usado o venció.");

    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@');
        if (separator <= 0) return "***";
        var local = email[..separator];
        var visible = local[..Math.Min(2, local.Length)];
        return $"{visible}***{email[separator..]}";
    }
}