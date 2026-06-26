using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class SearchProductsToolTests
{
    private readonly Mock<ICommerceService> _commerce = new();
    private readonly Mock<IConversationFactsService> _facts = new();

    [Fact]
    public async Task ExecuteAsync_WhenSearchReturnsEmpty_DoesNotInferBroadCatalogSearchFromUserText()
    {
        var ctx = CreateContext();
        ctx.LatestUserMessage = "muestrame las opciones";

        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([], "local"));

        var tool = new SearchProductsTool(_commerce.Object, _facts.Object);
        using var args = JsonDocument.Parse("""{"query":"vino dulce","limit":10}""");

        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"count\":0");
        _commerce.Verify(c => c.SearchProductsAsync(
            ctx,
            It.Is<ProductSearchRequest>(request =>
                request.Query == "vino dulce"
                && request.Category == null
                && request.Limit == 10),
            It.IsAny<CancellationToken>()), Times.Once);
        _commerce.Verify(c => c.SearchProductsAsync(
            ctx,
            It.Is<ProductSearchRequest>(request => request.Query == null && request.Category == null),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AgentToolContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}