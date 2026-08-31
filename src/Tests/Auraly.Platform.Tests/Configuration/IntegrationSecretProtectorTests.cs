using System.Security.Cryptography;
using Auraly.Platform.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Auraly.Platform.Tests.Configuration;

public sealed class IntegrationSecretProtectorTests
{
    [Fact]
    public void Protect_RoundTrips_WithoutPersistingPlaintext()
    {
        var protector = CreateProtector(RandomNumberGenerator.GetBytes(32));

        var protectedValue = protector.Protect("prv_test_super-secret");

        protectedValue.Should().StartWith("protected:v1:");
        protectedValue.Should().NotContain("prv_test_super-secret");
        protector.Unprotect(protectedValue).Should().Be("prv_test_super-secret");
    }

    [Fact]
    public void Unprotect_AllowsLegacyPlaintext_ForControlledMigration()
    {
        var protector = CreateProtector(RandomNumberGenerator.GetBytes(32));

        protector.Unprotect("pub_test_legacy").Should().Be("pub_test_legacy");
    }

    [Fact]
    public void Unprotect_WithDifferentKey_FailsClosed()
    {
        var protectedValue = CreateProtector(RandomNumberGenerator.GetBytes(32))
            .Protect("test_events_secret");

        var action = () => CreateProtector(RandomNumberGenerator.GetBytes(32))
            .Unprotect(protectedValue);

        action.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Protect_WithoutValidKey_FailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var action = () => new IntegrationSecretProtector(configuration).Protect("secret");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Base64 de 256 bits*");
    }

    private static IntegrationSecretProtector CreateProtector(byte[] key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auraly:Integrations:SecretProtectionKey"] = Convert.ToBase64String(key)
            })
            .Build();
        return new IntegrationSecretProtector(configuration);
    }
}
