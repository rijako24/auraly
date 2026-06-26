using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public class CommerceServiceTests
{
    [Fact]
    public async Task AddItemAsync_WhenProductAlreadyExists_AddsQuantityToExistingLine()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var orderDraftId = Guid.NewGuid();
        var orderDraftItemId = Guid.NewGuid();
        var draft = new OrderDraft
        {
            OrderDraftId = orderDraftId,
            BusinessId = businessId,
            ConversationId = conversationId,
            Currency = "COP"
        };
        var existingItem = new OrderDraftItem
        {
            OrderDraftItemId = orderDraftItemId,
            OrderDraftId = orderDraftId,
            BusinessId = businessId,
            ProductId = productId,
            Sku = "Vino de Mango 750 ml",
            ProductNameSnapshot = "Vino de Mango 750 ml",
            Quantity = 2m,
            UnitPrice = 60000m,
            LineTotal = 120000m
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var orderDrafts = new Mock<IOrderDraftRepository>();
        var orderDraftItems = new Mock<IOrderDraftItemRepository>();
        var integrationConnections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();
        var availability = new Mock<IProductCatalogAvailabilityService>();
        availability.Setup(a => a.IsSellable(It.IsAny<ProductReference>()))
            .Returns<ProductReference>(p => p.IsActive && p.IsAvailable);

        unitOfWork.SetupGet(u => u.OrderDrafts).Returns(orderDrafts.Object);
        unitOfWork.SetupGet(u => u.OrderDraftItems).Returns(orderDraftItems.Object);
        unitOfWork.SetupGet(u => u.IntegrationConnections).Returns(integrationConnections.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        integrationConnections
            .Setup(r => r.GetCommerceConnectionAsync(businessId, CommerceProvider.Local, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntegrationConnection?)null);
        orderDrafts
            .Setup(r => r.GetActiveDraftsByConversationAsync(businessId, conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([draft]);
        orderDraftItems
            .Setup(r => r.GetByDraftIdAsync(businessId, orderDraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [existingItem]);
        adapterFactory
            .Setup(f => f.Resolve(CommerceProvider.Local))
            .Returns(adapter.Object);
        adapter
            .SetupGet(a => a.Provider)
            .Returns(CommerceProvider.Local);
        adapter
            .Setup(a => a.GetProductAsync(It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductReference(
                productId,
                null,
                "Vino de Mango 750 ml",
                "Vino de Mango 750 ml",
                null,
                null,
                60000m,
                "COP",
                null,
                true));
        promotions
            .Setup(p => p.EvaluateAsync(
                businessId,
                It.IsAny<IReadOnlyList<PromotionPricingItem>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyList<PromotionPricingItem>, DateTime?, CancellationToken>((_, items, _, _) =>
                Task.FromResult(PromotionPricingResult.Empty(items)));

        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object, availability.Object);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationId = conversationId
        };

        var snapshot = await service.AddItemAsync(
            ctx,
            new AddOrderItemRequest(productId, null, null, null, 3m, null),
            CancellationToken.None);

        existingItem.Quantity.Should().Be(5m);
        snapshot.Items.Should().ContainSingle();
        snapshot.Items[0].Quantity.Should().Be(5m);
        snapshot.Total.Should().Be(300000m);
        orderDraftItems.Verify(r => r.CreateAsync(It.IsAny<OrderDraftItem>(), It.IsAny<CancellationToken>()), Times.Never);
        orderDraftItems.Verify(r => r.UpdateAsync(existingItem, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
    [Fact]
    public async Task AddItemAsync_WhenProductIsInactive_ThrowsProductInactive()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var unitOfWork = new Mock<IUnitOfWork>();
        var integrationConnections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();
        var availability = new Mock<IProductCatalogAvailabilityService>();
        availability.Setup(a => a.IsSellable(It.IsAny<ProductReference>()))
            .Returns<ProductReference>(p => p.IsActive && p.IsAvailable);

        unitOfWork.SetupGet(u => u.IntegrationConnections).Returns(integrationConnections.Object);
        integrationConnections
            .Setup(r => r.GetCommerceConnectionAsync(businessId, CommerceProvider.Local, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntegrationConnection?)null);
        adapterFactory
            .Setup(f => f.Resolve(CommerceProvider.Local))
            .Returns(adapter.Object);
        adapter
            .Setup(a => a.GetProductAsync(It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductReference(
                productId,
                null,
                "SOL-MANGO-750",
                "Mango 750ML",
                null,
                "Vinos artesanales",
                59900m,
                "COP",
                null,
                false)
            { IsActive = false });

        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object, availability.Object);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationId = conversationId
        };

        var act = () => service.AddItemAsync(
            ctx,
            new AddOrderItemRequest(productId, null, null, null, 1m, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Product inactive.");
    }
}
