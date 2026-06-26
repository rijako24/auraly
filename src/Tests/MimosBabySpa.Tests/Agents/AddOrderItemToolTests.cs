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
    public async Task ExecuteAsync_WhenExplicitProductIdProvided_DoesNotReplaceItWithSelectedProduct()
    {
        var ctx = CreateContext();
        var selectedProductId = Guid.NewGuid();
        var suppliedProductId = Guid.NewGuid();
        AddOrderItemRequest? capturedRequest = null;

        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                Product(selectedProductId, "Mango 750ML", 59900m)
            ], "local"));

        _commerce
            .Setup(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentToolContext, AddOrderItemRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(new OrderSnapshot(
                Guid.NewGuid(),
                OrderStatus.Draft,
                "COP",
                119800m,
                0m,
                0m,
                119800m,
                []));

        var searchTool = new SearchProductsTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "quiero mango";
        using (var searchArgs = JsonDocument.Parse("""{"query":"mango","limit":5}"""))
        {
            await searchTool.ExecuteAsync(searchArgs.RootElement, ctx, CancellationToken.None);
        }

        var addTool = new AddOrderItemTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "agrega 2";
        using var addArgs = JsonDocument.Parse($$"""{"product_id":"{{suppliedProductId}}","quantity":2}""");
        var json = await addTool.ExecuteAsync(addArgs.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.ProductId.Should().Be(suppliedProductId);
        capturedRequest.ProductId.Should().NotBe(selectedProductId);
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

    [Fact]
    public async Task ExecuteAsync_WhenProductIsInactive_ReturnsRecoverableError()
    {
        var ctx = CreateContext();
        var productId = Guid.NewGuid();

        _commerce
            .Setup(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Product inactive."));

        var tool = new AddOrderItemTool(_commerce.Object, _facts.Object);
        using var args = JsonDocument.Parse($$"""{"product_id":"{{productId}}","quantity":1}""");

        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":false");
        json.Should().Contain("product_inactive");
    }

    [Fact]
    public async Task ExecuteAsync_WhenQuantityOnlyAfterSingleSelectedProduct_AddsSelectedProduct()
    {
        var ctx = CreateContext();
        var productId = Guid.NewGuid();
        AddOrderItemRequest? capturedRequest = null;

        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                Product(productId, "Mango 750ML", 59900m)
            ], "local"));

        _commerce
            .Setup(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentToolContext, AddOrderItemRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(new OrderSnapshot(
                Guid.NewGuid(),
                OrderStatus.Draft,
                "COP",
                119800m,
                0m,
                0m,
                119800m,
                []));

        var searchTool = new SearchProductsTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "quiero mango";
        using (var searchArgs = JsonDocument.Parse("""{"query":"mango","limit":5}"""))
        {
            await searchTool.ExecuteAsync(searchArgs.RootElement, ctx, CancellationToken.None);
        }

        var addTool = new AddOrderItemTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "2";
        using var addArgs = JsonDocument.Parse("""{"quantity":2}""");

        var json = await addTool.ExecuteAsync(addArgs.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.ProductId.Should().Be(productId);
        capturedRequest.Quantity.Should().Be(2m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenQuantityOnlyAndPreviousSearchIsAmbiguous_DoesNotListStaleCandidates()
    {
        var ctx = CreateContext();
        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([
                Product(Guid.NewGuid(), "Dulce 750ML", 49900m),
                Product(Guid.NewGuid(), "Premium 750ML", 69900m)
            ], "local"));

        var searchTool = new SearchProductsTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "que vinos tienes?";
        using (var searchArgs = JsonDocument.Parse("""{"query":"vino","limit":10}"""))
        {
            await searchTool.ExecuteAsync(searchArgs.RootElement, ctx, CancellationToken.None);
        }

        var addTool = new AddOrderItemTool(_commerce.Object, _facts.Object);
        ctx.LatestUserMessage = "1";
        using var addArgs = JsonDocument.Parse("""{"quantity":1}""");

        var json = await addTool.ExecuteAsync(addArgs.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("missing_prerequisites");
        json.Should().Contain("search_products");
        json.Should().NotContain("Dulce 750ML");
        json.Should().NotContain("Premium 750ML");
        _commerce.Verify(c => c.AddItemAsync(ctx, It.IsAny<AddOrderItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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