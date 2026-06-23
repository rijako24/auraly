using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class UpdateOrderItemQuantityToolTests
{
    private readonly Mock<ICommerceService> _commerce = new();
    private readonly Mock<IConversationFactsService> _facts = new();

    [Fact]
    public async Task ExecuteAsync_WithOrderItemId_SetsExactFinalQuantity()
    {
        var ctx = CreateContext();
        var productId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        var draft = Snapshot(orderItemId, productId, 2m, 120000m);
        var updated = Snapshot(orderItemId, productId, 3m, 180000m);

        _commerce
            .Setup(c => c.GetDraftAsync(ctx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        _commerce
            .Setup(c => c.UpdateItemQuantityAsync(ctx, orderItemId, 3m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);
        _facts
            .Setup(f => f.ClearFieldsAsync(ctx.ConversationId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new UpdateOrderItemQuantityTool(_commerce.Object, _facts.Object);
        using var args = JsonDocument.Parse($$"""{"order_item_id":"{{orderItemId}}","quantity":3}""");

        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        _commerce.Verify(c => c.UpdateItemQuantityAsync(ctx, orderItemId, 3m, It.IsAny<CancellationToken>()), Times.Once);
        _commerce.Verify(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithProductId_ResolvesExistingItemAndSetsExactFinalQuantity()
    {
        var ctx = CreateContext();
        var productId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        var draft = Snapshot(orderItemId, productId, 2m, 120000m);
        var updated = Snapshot(orderItemId, productId, 3m, 180000m);

        _commerce
            .Setup(c => c.GetDraftAsync(ctx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        _commerce
            .Setup(c => c.UpdateItemQuantityAsync(ctx, orderItemId, 3m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);
        _facts
            .Setup(f => f.ClearFieldsAsync(ctx.ConversationId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new UpdateOrderItemQuantityTool(_commerce.Object, _facts.Object);
        using var args = JsonDocument.Parse($$"""{"product_id":"{{productId}}","quantity":3}""");

        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        _commerce.Verify(c => c.UpdateItemQuantityAsync(ctx, orderItemId, 3m, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static OrderSnapshot Snapshot(Guid orderItemId, Guid productId, decimal quantity, decimal total) =>
        new(
            Guid.NewGuid(),
            OrderStatus.Draft,
            "COP",
            total,
            0m,
            0m,
            total,
            [new OrderItemSnapshot(orderItemId, productId, null, "Vino de Mango 750 ml", "Vino de Mango 750 ml", quantity, 60000m, total)]);

    private static AgentToolContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}