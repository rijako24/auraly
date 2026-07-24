using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class CommerceServiceSearchTests
{
    [Fact]
    public async Task SearchProductsAsync_WhenLocalCatalogIsEmpty_ReturnsNotReadyWithoutCallingProvider()
    {
        var fixture = Fixture(CommerceProvider.Xion, []);

        var result = await fixture.Service.SearchProductsAsync(
            fixture.Context,
            new ProductSearchRequest("zucaritas", null, 10));

        result.CatalogReady.Should().BeFalse();
        result.Products.Should().BeEmpty();
        fixture.Adapter.Verify(adapter => adapter.SearchProductsAsync(
            It.IsAny<ProductSearchRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Adapter.Verify(adapter => adapter.GetProductAsync(
            It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchProductsAsync_QuotesOnlyLocalIdentities_AndFillsPageAfterAvailabilityFiltering()
    {
        var products = Enumerable.Range(1, 6)
            .Select(index => Identity($"TRULULU {index}", $"T{index}"))
            .ToArray();
        var fixture = Fixture(CommerceProvider.Xion, products);
        fixture.Adapter.Setup(adapter => adapter.GetProductAsync(
                It.IsAny<AddOrderItemRequest>(),
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AddOrderItemRequest request, CommerceAdapterContext _, CancellationToken _) =>
                request.Sku switch
                {
                    "T1" => Quote(request, active: false, stock: 10),
                    "T2" => Quote(request, active: true, stock: 0),
                    _ => Quote(request, active: true, stock: 10)
                });

        var result = await fixture.Service.SearchProductsAsync(
            fixture.Context,
            new ProductSearchRequest("trululu", null, 3));

        result.Products.Select(product => product.Sku).Should().Equal("T3", "T4", "T5");
        result.HasMore.Should().BeTrue();
        result.Source.Should().Be("local-identity+xion-quote");
        fixture.Adapter.Verify(adapter => adapter.GetProductAsync(
            It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6));
        fixture.Adapter.Verify(adapter => adapter.SearchProductsAsync(
            It.IsAny<ProductSearchRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchProductsAsync_NeverUsesProviderSearchAsAQueryFallback()
    {
        var fixture = Fixture(CommerceProvider.Xion, [Identity("PECHUGA CRIOLLA", "P1")]);

        var result = await fixture.Service.SearchProductsAsync(
            fixture.Context,
            new ProductSearchRequest("producto inexistente", null, 10));

        result.CatalogReady.Should().BeTrue();
        result.Products.Should().BeEmpty();
        fixture.Adapter.Verify(adapter => adapter.SearchProductsAsync(
            It.IsAny<ProductSearchRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Adapter.Verify(adapter => adapter.GetProductAsync(
            It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetProductAsync_WithSearchTextButNoExactIdentity_DoesNotSearchProvider()
    {
        var fixture = Fixture(CommerceProvider.Xion, [Identity("TOCINETA", "CF127")]);

        var result = await fixture.Service.GetProductAsync(
            fixture.Context,
            new ProductLookupRequest(null, null, null, null, "tocineta"));

        result.Should().BeNull();
        fixture.Adapter.Verify(adapter => adapter.SearchProductsAsync(
            It.IsAny<ProductSearchRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Adapter.Verify(adapter => adapter.GetProductAsync(
            It.IsAny<AddOrderItemRequest>(),
            It.IsAny<CommerceAdapterContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SearchFixture Fixture(CommerceProvider provider, IReadOnlyList<Product> catalog)
    {
        var businessId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        foreach (var product in catalog)
        {
            product.BusinessId = businessId;
            product.IntegrationConnectionId = provider == CommerceProvider.Local ? null : connectionId;
        }

        var unitOfWork = new Mock<IUnitOfWork>();
        var connections = new Mock<IIntegrationConnectionRepository>();
        var products = new Mock<IProductRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();
        var connection = provider == CommerceProvider.Local
            ? null
            : new IntegrationConnection
            {
                IntegrationConnectionId = connectionId,
                BusinessId = businessId,
                Provider = (int)provider,
                Capability = (int)CommerceCapability.CatalogAndOrders,
                IsEnabled = true,
                LastSyncAt = DateTime.UtcNow
            };

        unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(connections.Object);
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        connections.Setup(repository => repository.GetCommerceConnectionAsync(
                businessId, provider, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        products.Setup(repository => repository.GetIdentityCatalogAsync(
                businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        adapterFactory.Setup(factory => factory.Resolve(provider)).Returns(adapter.Object);
        adapter.Setup(value => value.GetProductAsync(
                It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductReference?)null);
        promotions.Setup(value => value.EvaluateAsync(
                businessId,
                It.IsAny<IReadOnlyList<PromotionPricingItem>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyList<PromotionPricingItem>, DateTime?, CancellationToken>((_, items, _, _) =>
                Task.FromResult(PromotionPricingResult.Empty(items)));

        var service = new CommerceService(
            unitOfWork.Object,
            adapterFactory.Object,
            promotions.Object,
            new ProductCatalogAvailabilityService(unitOfWork.Object));
        var context = new AgentConversationContext
        {
            BusinessId = businessId,
            ConversationId = Guid.NewGuid(),
            Config = new AgentConfig
            {
                Commerce = new CommerceConfig { Enabled = true, Provider = provider }
            }
        };
        return new(service, context, adapter);
    }

    private static Product Identity(string name, string sku) => new()
    {
        ProductId = Guid.NewGuid(),
        Name = name,
        Sku = sku,
        ExternalProductId = sku,
        IsActive = true,
        Currency = "COP"
    };

    private static ProductReference Quote(AddOrderItemRequest request, bool active, decimal stock) => new(
        request.ProductId,
        request.ExternalProductId,
        request.Sku,
        request.Name ?? request.Sku ?? "PRODUCT",
        null,
        null,
        1000m,
        "COP",
        stock)
    { IsActive = active };

    private sealed record SearchFixture(
        CommerceService Service,
        AgentConversationContext Context,
        Mock<ICommerceAdapter> Adapter);
}
