using System.Security.Cryptography;
using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlProtectedFiscalTechnicalKeyStore(
    SqlServerConnectionFactory connections,
    IConfiguration configuration,
    IAuralyIdGenerator ids) : IFiscalTechnicalKeyProvider, IFiscalTechnicalKeySecretWriter
{
    public async Task<FiscalVerificationMaterial?> ResolveAsync(
        FiscalKeyReference reference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1) s.ProtectedValue,a.SupplierTaxId,a.QrValidationUrl
            FROM dbo.FiscalTechnicalKeySecrets s
            JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
            JOIN dbo.Businesses b ON b.BusinessId=s.BusinessId
            WHERE b.TenantId=@TenantId AND s.BusinessId=@BusinessId
              AND a.AuthorizationNumber=@AuthorizationNumber
              AND s.TechnicalKeyVersion=@Version AND s.Environment=@Environment
              AND a.IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", reference.TenantId);
        Add(command, "@BusinessId", reference.BusinessId);
        Add(command, "@AuthorizationNumber", reference.AuthorizationNumber);
        Add(command, "@Version", reference.TechnicalKeyVersion);
        Add(command, "@Environment", (int)reference.Environment);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var value = Unprotect((byte[])reader[0]);
        return new FiscalVerificationMaterial(
            new FiscalTechnicalKey(value, reference.TechnicalKeyVersion),
            reader.GetString(1), reference.Environment, reader.GetString(2));
    }

    public async Task SaveAsync(
        Guid tenantId,
        Guid businessId,
        Guid fiscalAuthorizationId,
        string authorizationNumber,
        string version,
        int environment,
        string supplierTaxId,
        string qrValidationUrl,
        string technicalKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(technicalKey)) return;
        const string sql = """
            IF NOT EXISTS(
                SELECT 1 FROM dbo.Businesses b
                JOIN dbo.FiscalAuthorizations a ON a.BusinessId=b.BusinessId
                WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
                  AND a.FiscalAuthorizationId=@AuthorizationId
                  AND a.AuthorizationNumber=@AuthorizationNumber
                  AND a.SupplierTaxId=@SupplierTaxId AND a.QrValidationUrl=@QrUrl)
                THROW 51020,'Fiscal authorization scope is invalid.',1;
            MERGE dbo.FiscalTechnicalKeySecrets AS target
            USING (SELECT @BusinessId BusinessId,@AuthorizationId FiscalAuthorizationId,
                          @Version TechnicalKeyVersion,@Environment Environment) AS source
            ON target.BusinessId=source.BusinessId
              AND target.FiscalAuthorizationId=source.FiscalAuthorizationId
              AND target.TechnicalKeyVersion=source.TechnicalKeyVersion
              AND target.Environment=source.Environment
            WHEN MATCHED THEN UPDATE SET ProtectedValue=@ProtectedValue,UpdatedAt=@Now
            WHEN NOT MATCHED THEN INSERT(
                FiscalTechnicalKeySecretId,BusinessId,FiscalAuthorizationId,
                TechnicalKeyVersion,Environment,ProtectedValue,CreatedAt,UpdatedAt)
              VALUES(@Id,@BusinessId,@AuthorizationId,@Version,@Environment,
                     @ProtectedValue,@Now,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@TenantId", tenantId);
        Add(command, "@BusinessId", businessId);
        Add(command, "@AuthorizationId", fiscalAuthorizationId);
        Add(command, "@AuthorizationNumber", authorizationNumber.Trim());
        Add(command, "@Version", version.Trim());
        Add(command, "@Environment", environment);
        Add(command, "@SupplierTaxId", supplierTaxId.Trim());
        Add(command, "@QrUrl", qrValidationUrl.Trim());
        Add(command, "@ProtectedValue", Protect(technicalKey.Trim()));
        Add(command, "@Id", ids.NewId());
        Add(command, "@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private byte[] ProtectionKey()
    {
        var encoded = configuration["Auraly:Fiscal:SecretProtectionKey"];
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException(
                "Auraly:Fiscal:SecretProtectionKey must be supplied by secure configuration.");
        try
        {
            var key = Convert.FromBase64String(encoded);
            if (key.Length != 32) throw new FormatException();
            return key;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Auraly:Fiscal:SecretProtectionKey must be a Base64-encoded 256-bit key.");
        }
    }

    private byte[] Protect(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(ProtectionKey(), tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    private string Unprotect(byte[] payload)
    {
        if (payload.Length < 29) throw new CryptographicException("Protected fiscal key is invalid.");
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var ciphertext = payload.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(ProtectionKey(), tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static void Add(SqlCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}

public sealed class CompositeFiscalTechnicalKeyProvider(
    SqlProtectedFiscalTechnicalKeyStore protectedStore,
    ConfigurationFiscalTechnicalKeyProvider configuration) : IFiscalTechnicalKeyProvider
{
    public async Task<FiscalVerificationMaterial?> ResolveAsync(
        FiscalKeyReference reference, CancellationToken cancellationToken)
    {
        var stored = await protectedStore.ResolveAsync(reference, cancellationToken);
        return stored ?? await configuration.ResolveAsync(reference, cancellationToken);
    }
}


