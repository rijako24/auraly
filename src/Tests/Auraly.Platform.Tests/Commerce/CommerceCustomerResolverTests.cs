using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class CommerceCustomerResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesConfiguredCommerceConnectionAndChannelPhone()
    {
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true
        };
        var expected = new CommerceCustomerReference(
            CommerceProvider.Mantis,
            "10013",
            "6826",
            "Claudia",
            "3001234567");
        CommerceAdapterContext? receivedContext = null;
        var adapter = new Mock<ICommerceAdapter>();
        var lookup = adapter.As<ICommerceCustomerLookup>();
        lookup.Setup(value => value.FindCustomerAsync(
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<CommerceAdapterContext, CancellationToken>((context, _) => receivedContext = context)
            .ReturnsAsync(expected);
        var connections = new Mock<IIntegrationConnectionRepository>();
        connections.Setup(repository => repository.GetCommerceConnectionAsync(
                businessId,
                CommerceProvider.Mantis,
                CommerceCapability.CatalogAndOrders,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(connections.Object);
        var adapters = new Mock<ICommerceAdapterFactory>();
        adapters.Setup(factory => factory.Resolve(CommerceProvider.Mantis)).Returns(adapter.Object);
        var resolver = new CommerceCustomerResolver(unitOfWork.Object, adapters.Object);
        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = businessId,
            Commerce = new CommerceConfig { Enabled = true, Provider = CommerceProvider.Mantis }
        };

        var result = await resolver.ResolveAsync(
            businessId,
            agentId,
            conversationId,
            "+57 300 123 4567",
            config,
            CancellationToken.None);

        result.Should().Be(expected);
        receivedContext.Should().NotBeNull();
        receivedContext!.CustomerPhone.Should().Be("+57 300 123 4567");
        receivedContext.Connection.Should().BeSameAs(connection);
    }

    [Fact]
    public async Task ResolveAsync_WhenCommerceIsLocal_DoesNotQueryAnyConnection()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var adapters = new Mock<ICommerceAdapterFactory>();
        var resolver = new CommerceCustomerResolver(unitOfWork.Object, adapters.Object);
        var config = new AgentConfig
        {
            Commerce = new CommerceConfig { Enabled = true, Provider = CommerceProvider.Local }
        };

        var result = await resolver.ResolveAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "3001234567",
            config,
            CancellationToken.None);

        result.Should().BeNull();
        unitOfWork.VerifyGet(value => value.IntegrationConnections, Times.Never);
        adapters.Verify(factory => factory.Resolve(It.IsAny<CommerceProvider>()), Times.Never);
    }
}
