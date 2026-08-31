using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Configuration;

public sealed class IntegrationAdminServiceWompiTests
{
    [Fact]
    public async Task Provider_Resolves_The_Historical_Merchant_Version()
    {
        var businessId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            BusinessId = businessId,
            ConnectionType = ConnectionType.Integration,
            Provider = (int)IntegrationProvider.Wompi,
            Capability = (int)IntegrationCapability.Payments,
            IsEnabled = true,
            SettingsJson = "{\"mode\":\"production\",\"configurationVersion\":2}",
            SecretsJson = """
                {
                  "production": {
                    "privateKey": "prv_prod_current",
                    "publicKey": "pub_prod_current",
                    "eventsSecret": "prod_events_current",
                    "integritySecret": "prod_integrity_current"
                  },
                  "versions": {
                    "1": {
                      "mode": "test",
                      "privateKey": "prv_test_original",
                      "publicKey": "pub_test_original",
                      "eventsSecret": "test_events_original",
                      "integritySecret": "test_integrity_original"
                    }
                  }
                }
                """
        };
        var integrations = new Mock<IIntegrationConnectionRepository>();
        integrations.Setup(repository => repository.GetByBusinessProviderCapabilityAsync(
                businessId, IntegrationProvider.Wompi, IntegrationCapability.Payments,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(integrations.Object);
        var protector = new Mock<IIntegrationSecretProtector>();
        protector.Setup(value => value.Unprotect(It.IsAny<string>()))
            .Returns((string value) => value);
        var provider = new IntegrationsConfigProvider(
            unitOfWork.Object, protector.Object, NullLogger<IntegrationsConfigProvider>.Instance);

        var historical = await provider.GetWompiAsync(businessId, 1);

        historical.Should().NotBeNull();
        historical!.ConfigurationVersion.Should().Be(1);
        historical.Mode.Should().Be("test");
        historical.PrivateKey.Should().Be("prv_test_original");
        historical.EventsSecret.Should().Be("test_events_original");
    }

    [Fact]
    public async Task UpdateOperationalMode_PreservesOriginalMerchantVersion()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var business = new Business { BusinessId = businessId, TenantId = tenantId, Name = "Auraly" };
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            ConnectionType = ConnectionType.Integration,
            Provider = (int)IntegrationProvider.Wompi,
            Capability = (int)IntegrationCapability.Payments,
            Name = "Wompi",
            IsEnabled = true,
            SettingsJson = "{\"mode\":\"test\"}",
            SecretsJson = """
                {
                  "test": {
                    "privateKey": "prv_test_current",
                    "publicKey": "pub_test_current",
                    "eventsSecret": "test_events_current",
                    "integritySecret": "test_integrity_current"
                  },
                  "production": {
                    "privateKey": "prv_prod_next",
                    "publicKey": "pub_prod_next",
                    "eventsSecret": "prod_events_next",
                    "integritySecret": "prod_integrity_next"
                  }
                }
                """
        };
        var businesses = new Mock<IBusinessRepository>();
        businesses.Setup(repository => repository.GetByIdAsync(businessId)).ReturnsAsync(business);
        var integrations = new Mock<IIntegrationConnectionRepository>();
        integrations.Setup(repository => repository.GetByBusinessProviderCapabilityAsync(
                businessId, IntegrationProvider.Wompi, IntegrationCapability.Payments, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        integrations.Setup(repository => repository.GetByBusinessIdAsync(
                businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([connection]);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Businesses).Returns(businesses.Object);
        unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(integrations.Object);
        var protector = new Mock<IIntegrationSecretProtector>();
        protector.Setup(value => value.Unprotect(It.IsAny<string>()))
            .Returns((string value) => value);
        protector.Setup(value => value.Protect(It.IsAny<string>()))
            .Returns((string value) => value);
        var service = new IntegrationAdminService(unitOfWork.Object, protector.Object);

        await service.UpdateOperationalModeAsync(
            tenantId, businessId, new UpdateOperationalModeRequest("production"));

        connection.SettingsJson.Should().Contain("\"configurationVersion\":2");
        connection.SecretsJson.Should().Contain("\"1\"")
            .And.Contain("\"2\"")
            .And.Contain("test_events_current")
            .And.Contain("prod_events_next");
        integrations.Verify(repository => repository.UpdateAsync(
            connection, It.IsAny<CancellationToken>()), Times.Once);
    }
}
