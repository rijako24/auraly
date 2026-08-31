using System.Security.Cryptography;
using System.Text;
using Auraly.Platform.Application.Configuration;
using Microsoft.Extensions.Configuration;

namespace Auraly.Platform.Infrastructure.Configuration;

public sealed class IntegrationSecretProtector(IConfiguration configuration)
    : IIntegrationSecretProtector
{
    private const string Prefix = "protected:v1:";

    public string Protect(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return string.Empty;
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext;
        var value = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[value.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key(), tag.Length);
        aes.Encrypt(nonce, value, ciphertext, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    public string Unprotect(string protectedOrLegacyValue)
    {
        if (string.IsNullOrWhiteSpace(protectedOrLegacyValue)) return string.Empty;
        if (!protectedOrLegacyValue.StartsWith(Prefix, StringComparison.Ordinal))
            return protectedOrLegacyValue;
        byte[] payload;
        try { payload = Convert.FromBase64String(protectedOrLegacyValue[Prefix.Length..]); }
        catch (FormatException exception) { throw new CryptographicException("El secreto protegido de integración es inválido.", exception); }
        if (payload.Length < 29) throw new CryptographicException("El secreto protegido de integración es inválido.");
        var plaintext = new byte[payload.Length - 28];
        using var aes = new AesGcm(Key(), 16);
        aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] Key()
    {
        var encoded = configuration["Auraly:Integrations:SecretProtectionKey"]
            ?? configuration["Auraly:Fiscal:SecretProtectionKey"];
        try
        {
            var key = Convert.FromBase64String(encoded ?? string.Empty);
            if (key.Length != 32) throw new FormatException();
            return key;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Auraly:Integrations:SecretProtectionKey debe ser una llave Base64 de 256 bits.");
        }
    }
}
