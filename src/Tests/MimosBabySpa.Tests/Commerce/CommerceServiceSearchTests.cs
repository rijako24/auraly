using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class CommerceServiceSearchTests
{
    [Fact]
    public async Task SearchProductsAsync_ReturnsOnlySellableProducts()
    {
        var businessId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var integrationConnections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();

        unitOfWork.SetupGet(u => u.IntegrationConnections).Returns(integrationConnections.Object);
        integrationConnections
            .Setup(r => r.GetCommerceConnectionAsync(businessId, CommerceProvider.Local, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.IntegrationConnection?)null);
        adapterFactory.Setup(f => f.Resolve(CommerceProvider.Local)).Returns(adapter.Object);
        adapter
            .Setup(a => a.SearchProductsAsync(It.IsAny<ProductSearchRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult(
                [
                    Product("Mango 750ML", isActive: true),
                    Product("Dulce 750ML", isActive: false),
                    Product("Semidulce 750ML", isActive: true, stockQuantity: 0)
                ],
                "adapter"));
        promotions
            .Setup(p => p.EvaluateAsync(
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

        var result = await service.SearchProductsAsync(
            new AgentConversationContext { BusinessId = businessId, ConversationId = Guid.NewGuid() },
            new ProductSearchRequest("vino", null, 10),
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        result.Products[0].Name.Should().Be("Mango 750ML");
        promotions.Verify(p => p.EvaluateAsync(
            businessId,
            It.Is<IReadOnlyList<PromotionPricingItem>>(items => items.Count == 1),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SearchProductsAsync_RetriesGenericFallbackAndPreservesOriginalFilters()
    {
        var businessId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var integrationConnections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();
        var queries = new List<string?>();

        unitOfWork.SetupGet(u => u.IntegrationConnections).Returns(integrationConnections.Object);
        integrationConnections
            .Setup(r => r.GetCommerceConnectionAsync(
                businessId,
                CommerceProvider.Local,
                CommerceCapability.CatalogAndOrders,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.IntegrationConnection?)null);
        adapterFactory.Setup(f => f.Resolve(CommerceProvider.Local)).Returns(adapter.Object);
        adapter
            .Setup(a => a.SearchProductsAsync(
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CommerceAdapterContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductSearchRequest request, CommerceAdapterContext _, CancellationToken _) =>
            {
                queries.Add(request.Query);
                return request.Query == "pechuga"
                    ? new ProductSearchResult([Product("PECHUGA CRIOLLA", true, 10)], "adapter")
                    : new ProductSearchResult([], "adapter");
            });
        promotions
            .Setup(p => p.EvaluateAsync(
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

        var result = await service.SearchProductsAsync(
            new AgentConversationContext { BusinessId = businessId, ConversationId = Guid.NewGuid() },
            new ProductSearchRequest("pechugas", null, 10),
            CancellationToken.None);

        result.Products.Should().ContainSingle();
        result.Products[0].Name.Should().Be("PECHUGA CRIOLLA");
        queries.Should().Equal("pechugas", "pechuga");
        result.AppliedFilters.Should().NotBeNull();
        result.AppliedFilters!.Query.Should().Be("pechugas");
    }

    [Fact]
    public async Task GetProductAsync_UsesConfiguredSearchText_ButRequiresExactTargetIdentity()
    {
        var businessId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var connections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();
        unitOfWork.SetupGet(unit => unit.IntegrationConnections).Returns(connections.Object);
        connections.Setup(repository => repository.GetCommerceConnectionAsync(
                businessId, CommerceProvider.Local, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.IntegrationConnection?)null);
        adapterFactory.Setup(factory => factory.Resolve(CommerceProvider.Local)).Returns(adapter.Object);
        adapter.Setup(value => value.GetProductAsync(
                It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductReference?)null);
        adapter.Setup(value => value.SearchProductsAsync(
                It.Is<ProductSearchRequest>(request => request.Query == "tocineta"),
                It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                new ProductReference(null, "CF31", "CF31", "TOCINETA NOJOS", null, null, 100m, "COP", 20),
                new ProductReference(null, "CF127", "CF127", "TOCINETA CJ 1K", null, null, 200m, "COP", 30)
            ], "adapter"));
        promotions.Setup(value => value.EvaluateAsync(
                businessId, It.IsAny<IReadOnlyList<PromotionPricingItem>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyList<PromotionPricingItem>, DateTime?, CancellationToken>((_, items, _, _) =>
                Task.FromResult(PromotionPricingResult.Empty(items)));
        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object,
            new ProductCatalogAvailabilityService(unitOfWork.Object));

        var result = await service.GetProductAsync(
            new AgentConversationContext { BusinessId = businessId, ConversationId = Guid.NewGuid() },
            new ProductLookupRequest(null, "CF127", "CF127", null, "tocineta"));

        result.Should().NotBeNull();
        result!.ExternalProductId.Should().Be("CF127");
    }
    private static ProductReference Product(string name, bool isActive, decimal? stockQuantity = null) =>
        new(
            Guid.NewGuid(),
            null,
            null,
            name,
            null,
            null,
            1000m,
            "COP",
            stockQuantity)
        { IsActive = isActive };
}
