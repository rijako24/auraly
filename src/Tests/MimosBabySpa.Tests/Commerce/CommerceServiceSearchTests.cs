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
    public async Task SearchProductsAsync_ReturnsInactiveProductsAsUnavailable()
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

        result.Products.Should().HaveCount(3);
        result.Products[0].Name.Should().Be("Mango 750ML");
        result.Products[1].Name.Should().Be("Dulce 750ML");
        result.Products[1].IsActive.Should().BeFalse();
        result.Products[2].Name.Should().Be("Semidulce 750ML");
        result.Products[2].IsActive.Should().BeTrue();
        promotions.Verify(p => p.EvaluateAsync(
            businessId,
            It.Is<IReadOnlyList<PromotionPricingItem>>(items => items.Count == 3),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()));
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
