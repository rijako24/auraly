using System.Security.Cryptography;
using Auraly.Application.Authentication;
using Auraly.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class OfflineAuthenticationLeaseSigningConfigurationTests
{
    [Fact]
    public void Complete_private_key_is_accepted_and_exports_trust_material()
    {
        using var rsa = RSA.Create(2048);
        using var signer = Create(rsa.ExportPkcs8PrivateKeyPem());

        signer.ValidateConfiguration();

        Assert.Contains("BEGIN PUBLIC KEY", signer.TrustedPublicKeys["test-key"]);
    }

    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData("not-a-private-key")]
    public void Truncated_or_invalid_private_key_fails_readiness(string configuredValue)
    {
        using var signer = Create(configuredValue);

        var exception = Assert.Throws<OfflineAuthenticationLeaseConfigurationException>(
            signer.ValidateConfiguration);

        Assert.Contains("PEM RSA privado válido", exception.Message);
    }

    private static RsaOfflineAuthenticationLeaseSigner Create(string privateKeyPem) =>
        new(Options.Create(new OfflineAuthenticationLeaseSigningOptions
        {
            KeyId = "test-key",
            PrivateKeyPem = privateKeyPem
        }));
}
