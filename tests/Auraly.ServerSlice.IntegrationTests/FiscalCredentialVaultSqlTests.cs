using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auraly.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
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

        Assert.Equal("software-pin", await vault.ResolveSoftwarePinAsync(
            fixture.BusinessId, reference.SoftwarePinReference, CancellationToken.None));
        var restoredPfx = await vault.ResolveCertificatePfxAsync(
            fixture.BusinessId, reference.CertificateKeyReference, CancellationToken.None);
        using var restored = new X509Certificate2(
            restoredPfx, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
        Assert.Equal(certificate.Thumbprint, restored.Thumbprint);
        Assert.True(restored.HasPrivateKey);
    }
}
