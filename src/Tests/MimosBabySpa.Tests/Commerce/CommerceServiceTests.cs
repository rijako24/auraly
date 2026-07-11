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
            .Returns<ProductReference>(p => p.IsActive && (!p.StockQuantity.HasValue || p.StockQuantity.Value > 0));

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
                null));
        promotions
            .Setup(p => p.EvaluateAsync(
                businessId,
                It.IsAny<IReadOnlyList<PromotionPricingItem>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyList<PromotionPricingItem>, DateTime?, CancellationToken>((_, items, _, _) =>
                Task.FromResult(PromotionPricingResult.Empty(items)));

        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object, availability.Object);
        var ctx = new AgentConversationContext
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
    public async Task RemoveItemAsync_DeletesBeforeRepricing_AndDoesNotResurrectTheLine()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var removed = new OrderDraftItem
        {
            OrderDraftItemId = Guid.NewGuid(), OrderDraftId = draftId, BusinessId = businessId,
            Sku = "CRIOLLA", ProductNameSnapshot = "PECHUGA CRIOLLA", Quantity = 2, UnitPrice = 10, LineTotal = 20
        };
        var remaining = new OrderDraftItem
        {
            OrderDraftItemId = Guid.NewGuid(), OrderDraftId = draftId, BusinessId = businessId,
            Sku = "MAC", ProductNameSnapshot = "PECHUGA MAC POLLO", Quantity = 1, UnitPrice = 10, LineTotal = 10
        };
        var activeItems = new List<OrderDraftItem> { removed, remaining };
        var draft = new OrderDraft
        {
            OrderDraftId = draftId, BusinessId = businessId, ConversationId = conversationId, Currency = "COP"
        };
        var unitOfWork = new Mock<IUnitOfWork>();
        var drafts = new Mock<IOrderDraftRepository>();
        var items = new Mock<IOrderDraftItemRepository>();
        var promotions = new Mock<IPromotionPricingService>();
        unitOfWork.SetupGet(value => value.OrderDrafts).Returns(drafts.Object);
        unitOfWork.SetupGet(value => value.OrderDraftItems).Returns(items.Object);
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        drafts.Setup(value => value.GetActiveDraftsByConversationAsync(businessId, conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([draft]);
        drafts.Setup(value => value.UpdateAsync(draft, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        items.Setup(value => value.GetByIdAsync(businessId, removed.OrderDraftItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(removed);
        items.Setup(value => value.GetByDraftIdAsync(businessId, draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => activeItems.ToList());
        items.Setup(value => value.DeleteAsync(removed, It.IsAny<CancellationToken>()))
            .Callback(() => activeItems.Remove(removed))
            .Returns(Task.CompletedTask);
        items.Setup(value => value.UpdateAsync(It.IsAny<OrderDraftItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderDraftItem value, CancellationToken _) => value);
        promotions.Setup(value => value.EvaluateAsync(
                businessId, It.IsAny<IReadOnlyList<PromotionPricingItem>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyList<PromotionPricingItem>, DateTime?, CancellationToken>((_, pricingItems, _, _) =>
                Task.FromResult(PromotionPricingResult.Empty(pricingItems)));
        var service = new CommerceService(
            unitOfWork.Object,
            Mock.Of<ICommerceAdapterFactory>(),
            promotions.Object,
            Mock.Of<IProductCatalogAvailabilityService>());
        var context = new AgentConversationContext { BusinessId = businessId, ConversationId = conversationId };

        var snapshot = await service.RemoveItemAsync(context, removed.OrderDraftItemId);

        snapshot.Items.Should().ContainSingle().Which.ProductName.Should().Be("PECHUGA MAC POLLO");
        items.Verify(value => value.UpdateAsync(removed, It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
    [Fact]
    public async Task UpdateItemQuantityAsync_WhenDesiredQuantityExceedsStock_RejectsBeforeMutation()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var draft = new OrderDraft
        {
            OrderDraftId = draftId,
            BusinessId = businessId,
            ConversationId = conversationId,
            Currency = "COP"
        };
        var item = new OrderDraftItem
        {
            OrderDraftItemId = itemId,
            OrderDraftId = draftId,
            BusinessId = businessId,
            ProductId = productId,
            Sku = "PECHUGA-CAMPOLLO",
            ProductNameSnapshot = "PECHUGA CAMPOLLO",
            Quantity = 2,
            UnitPrice = 10000,
            LineTotal = 20000
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var drafts = new Mock<IOrderDraftRepository>();
        var items = new Mock<IOrderDraftItemRepository>();
        var connections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();
        var availability = new Mock<IProductCatalogAvailabilityService>();
        unitOfWork.SetupGet(value => value.OrderDrafts).Returns(drafts.Object);
        unitOfWork.SetupGet(value => value.OrderDraftItems).Returns(items.Object);
        unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(connections.Object);
        drafts.Setup(value => value.GetActiveDraftsByConversationAsync(businessId, conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([draft]);
        items.Setup(value => value.GetByIdAsync(businessId, itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        connections.Setup(value => value.GetCommerceConnectionAsync(
                businessId, CommerceProvider.Local, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntegrationConnection?)null);
        adapterFactory.Setup(value => value.Resolve(CommerceProvider.Local)).Returns(adapter.Object);
        adapter.Setup(value => value.GetProductAsync(
                It.IsAny<AddOrderItemRequest>(), It.IsAny<CommerceAdapterContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductReference(
                productId, null, item.Sku, item.ProductNameSnapshot, null, null, item.UnitPrice, "COP", 4));
        availability.Setup(value => value.IsSellable(It.IsAny<ProductReference>())).Returns(true);
        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object, availability.Object);
        var context = new AgentConversationContext { BusinessId = businessId, ConversationId = conversationId };

        var act = () => service.UpdateItemQuantityAsync(context, itemId, 5);

        var exception = await act.Should().ThrowAsync<InsufficientProductStockException>();
        exception.Which.AvailableQuantity.Should().Be(4);
        exception.Which.RequestedQuantity.Should().Be(5);
        item.Quantity.Should().Be(2);
        items.Verify(value => value.UpdateAsync(It.IsAny<OrderDraftItem>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
            .Returns<ProductReference>(p => p.IsActive && (!p.StockQuantity.HasValue || p.StockQuantity.Value > 0));

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
                null)
            { IsActive = false });

        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object, availability.Object);
        var ctx = new AgentConversationContext
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
