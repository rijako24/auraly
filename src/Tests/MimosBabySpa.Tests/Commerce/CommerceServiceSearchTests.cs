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
    public async Task SearchProductsAsync_FiltersAdapterResultsToSellableProductsBeforeReturningToAgent()
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
                    Product("Mango 750ML", isActive: true, isAvailable: true),
                    Product("Dulce 750ML", isActive: false, isAvailable: true),
                    Product("Semidulce 750ML", isActive: true, isAvailable: false)
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
            new AgentToolContext { BusinessId = businessId, ConversationId = Guid.NewGuid() },
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

    private static ProductReference Product(string name, bool isActive, bool isAvailable) =>
        new(
            Guid.NewGuid(),
            null,
            null,
            name,
            null,
            null,
            1000m,
            "COP",
            null,
            isAvailable)
        { IsActive = isActive };
}
