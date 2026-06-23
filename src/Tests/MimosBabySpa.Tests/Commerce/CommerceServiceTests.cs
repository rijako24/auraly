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
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        var order = new Order
        {
            OrderId = orderId,
            BusinessId = businessId,
            ConversationId = conversationId,
            Status = OrderStatus.Draft,
            Currency = "COP"
        };
        var existingItem = new OrderItem
        {
            OrderItemId = orderItemId,
            OrderId = orderId,
            BusinessId = businessId,
            ProductId = productId,
            Sku = "Vino de Mango 750 ml",
            ProductNameSnapshot = "Vino de Mango 750 ml",
            Quantity = 2m,
            UnitPrice = 60000m,
            LineTotal = 120000m
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var orders = new Mock<IOrderRepository>();
        var orderItems = new Mock<IOrderItemRepository>();
        var integrationConnections = new Mock<IIntegrationConnectionRepository>();
        var adapterFactory = new Mock<ICommerceAdapterFactory>();
        var adapter = new Mock<ICommerceAdapter>();
        var promotions = new Mock<IPromotionPricingService>();

        unitOfWork.SetupGet(u => u.Orders).Returns(orders.Object);
        unitOfWork.SetupGet(u => u.OrderItems).Returns(orderItems.Object);
        unitOfWork.SetupGet(u => u.IntegrationConnections).Returns(integrationConnections.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        integrationConnections
            .Setup(r => r.GetCommerceConnectionAsync(businessId, CommerceProvider.Local, CommerceCapability.CatalogAndOrders, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntegrationConnection?)null);
        orders
            .Setup(r => r.GetActiveDraftByConversationAsync(businessId, conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orderItems
            .Setup(r => r.GetByOrderIdAsync(businessId, orderId, It.IsAny<CancellationToken>()))
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

        var service = new CommerceService(unitOfWork.Object, adapterFactory.Object, promotions.Object);
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
        orderItems.Verify(r => r.CreateAsync(It.IsAny<OrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        orderItems.Verify(r => r.UpdateAsync(existingItem, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}