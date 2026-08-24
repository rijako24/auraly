using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlProtectedFiscalCredentialVault(
    SqlServerConnectionFactory connections,
    IConfiguration configuration) : IFiscalCredentialVault
{
    public async Task<FiscalCredentialReference> StoreAsync(
        Guid tenantId,
        Guid businessId,
        string softwarePin,
        byte[] certificatePfx,
        string certificatePassword,
        DateTimeOffset validFrom,
        DateTimeOffset validTo,
        string thumbprint,
        CancellationToken cancellationToken)
    {
        var certificates = new X509Certificate2Collection();
        certificates.Import(
            certificatePfx,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        byte[] passwordlessPfx;
        try
        {
            passwordlessPfx = certificates.Export(X509ContentType.Pkcs12)
                ?? throw new CryptographicException("The fiscal certificate chain could not be exported.");
        }
        finally
        {
            foreach (var certificate in certificates)
                certificate.Dispose();
        }
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1)
                THROW 51021,'Business is outside the authenticated tenant.',1;
            MERGE fiscal.FiscalCredentialSecrets AS target
            USING (SELECT @TenantId TenantId) AS source
            ON target.TenantId=source.TenantId
            WHEN MATCHED THEN UPDATE SET
                ProtectedSoftwarePin=@Pin,ProtectedCertificatePfx=@Pfx,
                CertificateThumbprint=@Thumbprint,CertificateValidFrom=@ValidFrom,
                CertificateValidTo=@ValidTo,UpdatedAt=@Now
            WHEN NOT MATCHED THEN INSERT(
                TenantId,ProtectedSoftwarePin,ProtectedCertificatePfx,
                CertificateThumbprint,CertificateValidFrom,CertificateValidTo,CreatedAt,UpdatedAt)
              VALUES(@TenantId,@Pin,@Pfx,@Thumbprint,@ValidFrom,@ValidTo,@Now,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@Pin", Protect(Encoding.UTF8.GetBytes(softwarePin)));
        Add(command, "@Pfx", Protect(passwordlessPfx));
        Add(command, "@Thumbprint", thumbprint);
        Add(command, "@ValidFrom", validFrom);
        Add(command, "@ValidTo", validTo);
        Add(command, "@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var reference = $"fiscal://tenant/{tenantId:N}";
        return new FiscalCredentialReference(
            "ProtectedDatabase", reference, reference, thumbprint, validFrom, validTo);
    }

    public async Task<string> ResolveSoftwarePinAsync(
        Guid businessId, string secretReference, CancellationToken cancellationToken)
    {
        var tenantId = ParseTenantReference(secretReference);
        var payload = await ReadAsync(
            tenantId, businessId, "ProtectedSoftwarePin", cancellationToken);
        return Encoding.UTF8.GetString(Unprotect(payload));
    }

    public async Task<byte[]> ResolveCertificatePfxAsync(
        Guid businessId, string certificateKeyReference, CancellationToken cancellationToken)
    {
        var tenantId = ParseTenantReference(certificateKeyReference);
        var payload = await ReadAsync(
            tenantId, businessId, "ProtectedCertificatePfx", cancellationToken);
        return Unprotect(payload);
    }

    private async Task<byte[]> ReadAsync(
        Guid tenantId, Guid businessId, string column, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT credentials.{column}
            FROM fiscal.FiscalCredentialSecrets credentials
            WHERE credentials.TenantId=@TenantId
              AND EXISTS(SELECT 1 FROM dbo.Businesses
                         WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND IsActive=1);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        return await command.ExecuteScalarAsync(cancellationToken) as byte[]
            ?? throw new InvalidOperationException("The configured fiscal credential is unavailable.");
    }

    private static Guid ParseTenantReference(string reference)
    {
        const string prefix = "fiscal://tenant/";
        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(reference[prefix.Length..], "N", out var tenantId))
            throw new InvalidOperationException("The fiscal credential reference is invalid for this tenant.");
        return tenantId;
    }

    private byte[] Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(ProtectionKey(), tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    private byte[] Unprotect(byte[] payload)
    {
        if (payload.Length < 29)
            throw new CryptographicException("Protected fiscal credential is invalid.");
        var plaintext = new byte[payload.Length - 28];
        using var aes = new AesGcm(ProtectionKey(), 16);
        aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plaintext);
        return plaintext;
    }

    private byte[] ProtectionKey()
    {
        var encoded = configuration["Auraly:Fiscal:SecretProtectionKey"];
        try
        {
            var key = Convert.FromBase64String(encoded ?? string.Empty);
            if (key.Length != 32) throw new FormatException();
            return key;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Auraly:Fiscal:SecretProtectionKey must be a Base64-encoded 256-bit key.");
        }
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
