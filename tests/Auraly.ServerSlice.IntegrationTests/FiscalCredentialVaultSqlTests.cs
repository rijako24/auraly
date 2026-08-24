using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Fiscal")]
public sealed class FiscalCredentialVaultSqlTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Protected_database_round_trips_pin_and_passwordless_certificate()
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auraly:Fiscal:SecretProtectionKey"] = key
            })
            .Build();
        var vault = new SqlProtectedFiscalCredentialVault(
            new SqlServerConnectionFactory(fixture.ConnectionString), configuration);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=9001234567", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        const string password = "integration-password";
        var pfx = certificate.Export(X509ContentType.Pkcs12, password);

        var reference = await vault.StoreAsync(
            fixture.TenantId, fixture.BusinessId, "software-pin", pfx, password,
            certificate.NotBefore, certificate.NotAfter, certificate.Thumbprint,
            CancellationToken.None);

        var secondBusinessId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                INSERT dbo.Businesses(
                    BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
                VALUES(@BusinessId,@TenantId,N'Segunda sede',N'Prueba tenant fiscal',N'Bogotá',
                       N'3000000000',N'fiscal-sede@auraly.test',N'https://auraly.test',1,SYSUTCDATETIME());
                """, connection);
            command.Parameters.AddWithValue("@BusinessId", secondBusinessId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal($"fiscal://tenant/{fixture.TenantId:N}", reference.SoftwarePinReference);
        Assert.Equal("software-pin", await vault.ResolveSoftwarePinAsync(
            secondBusinessId, reference.SoftwarePinReference, CancellationToken.None));
        var restoredPfx = await vault.ResolveCertificatePfxAsync(
            secondBusinessId, reference.CertificateKeyReference, CancellationToken.None);
        using var restored = new X509Certificate2(
            restoredPfx, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
        Assert.Equal(certificate.Thumbprint, restored.Thumbprint);
        Assert.True(restored.HasPrivateKey);

        await using var cleanupConnection = new SqlConnection(fixture.ConnectionString);
        await cleanupConnection.OpenAsync();
        await using var cleanup = new SqlCommand(
            "DELETE dbo.Businesses WHERE BusinessId=@BusinessId;", cleanupConnection);
        cleanup.Parameters.AddWithValue("@BusinessId", secondBusinessId);
        await cleanup.ExecuteNonQueryAsync();
    }
}
