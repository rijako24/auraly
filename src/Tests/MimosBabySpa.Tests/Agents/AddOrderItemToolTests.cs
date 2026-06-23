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

public class AddOrderItemToolTests
{
    private readonly Mock<ICommerceService> _commerce = new();
    private readonly Mock<IConversationFactsService> _facts = new();

    [Fact]
    public async Task ExecuteAsync_WhenModelSendsStaleProductId_ResolvesFromLastSearchDynamically()
    {
        var ctx = CreateContext();
        var expectedProductId = Guid.NewGuid();
        AddOrderItemRequest? capturedRequest = null;

        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                Product(Guid.NewGuid(), "Gift Pack 750 ml", 80000m),
                Product(expectedProductId, "Tropical Bottle 207 ml", 26000m),
                Product(Guid.NewGuid(), "Tropical Bottle 750 ml", 60000m)
            ], "local"));

        _commerce
            .Setup(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentToolContext, AddOrderItemRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(new OrderSnapshot(
                Guid.NewGuid(),
                OrderStatus.Draft,
                "COP",
                52000m,
                0m,
                0m,
                52000m,
                []));

        var searchTool = new SearchProductsTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "show me options";
        using (var searchArgs = JsonDocument.Parse("""{"query":"wine","limit":3}"""))
        {
            await searchTool.ExecuteAsync(searchArgs.RootElement, ctx, CancellationToken.None);
        }

        var addTool = new AddOrderItemTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "give me 2 of 207 ml";
        using var addArgs = JsonDocument.Parse($$"""{"product_id":"{{Guid.NewGuid()}}","quantity":2}""");
        var json = await addTool.ExecuteAsync(addArgs.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.ProductId.Should().Be(expectedProductId);
        capturedRequest.Quantity.Should().Be(2m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExistingProductAndAdditiveWording_AddsQuantity()
    {
        var ctx = CreateContext();
        var productId = Guid.NewGuid();
        AddOrderItemRequest? capturedRequest = null;

        _commerce
            .Setup(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentToolContext, AddOrderItemRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(new OrderSnapshot(
                Guid.NewGuid(),
                OrderStatus.Draft,
                "COP",
                300000m,
                0m,
                0m,
                300000m,
                []));
        _facts
            .Setup(f => f.ClearFieldsAsync(ctx.ConversationId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new AddOrderItemTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "quiero agregar 3 mas";
        using var args = JsonDocument.Parse($$"""{"product_id":"{{productId}}","quantity":3}""");

        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.ProductId.Should().Be(productId);
        capturedRequest.Quantity.Should().Be(3m);
        _commerce.Verify(c => c.UpdateItemQuantityAsync(ctx, It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    private static ProductReference Product(Guid productId, string name, decimal unitPrice) =>
        new(
            productId,
            null,
            null,
            name,
            null,
            null,
            unitPrice,
            "COP",
            null,
            true);

    private static AgentToolContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}