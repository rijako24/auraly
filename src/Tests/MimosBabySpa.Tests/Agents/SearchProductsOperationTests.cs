using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class SearchProductsOperationTests
{
    private readonly Mock<ICommerceService> _commerce = new();

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

        var operation = new SearchProductsOperation(_commerce.Object);
        using var args = JsonDocument.Parse("""{"query":"vino dulce","limit":10}""");

        var json = (await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx }, CancellationToken.None)).Data.GetRawText();

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

    [Fact]
    public async Task ExecuteAsync_WithProducts_IncludesResponseGuidanceForPricesAndStock()
    {
        var ctx = CreateContext();
        var products = new[]
        {
            new ProductReference(
                Guid.NewGuid(),
                "PO36",
                "PO36",
                "TROZOS DE PECHUGA DE POLLO",
                "Pechuga para preparaciones",
                "Pollo",
                18000m,
                "COP",
                12m),
            new ProductReference(
                Guid.NewGuid(),
                "PO37",
                "PO37",
                "PECHUGA CRIOLLA",
                "Pechuga fresca",
                "Pollo",
                19500m,
                "COP",
                5m)
        };

        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult(products, "mantis"));

        var operation = new SearchProductsOperation(_commerce.Object);
        using var args = JsonDocument.Parse("""{"query":"pechuga","limit":10}""");

        var json = (await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx }, CancellationToken.None)).Data.GetRawText();

        json.Should().Contain("response_guidance");
        json.Should().Contain("unit_price");
        json.Should().Contain("currency");
        json.Should().Contain("Do not show SKU/code");
        json.Should().Contain("Mention available stock quantity only when a requested quantity is greater");
    }
    [Fact]
    public async Task ExecuteAsync_WithDerivedQueries_SearchesEachCatalogQueryAndMergesProducts()
    {
        var ctx = CreateContext();
        var papa = new ProductReference(
            Guid.NewGuid(),
            "PA11",
            "PA11",
            "PAPA FARM FRITES X 2.5K",
            "Papa congelada para freir",
            "Congelados",
            21800m,
            "COP",
            null);
        var tocineta = new ProductReference(
            Guid.NewGuid(),
            "CF12",
            "CF12",
            "TOCINETA AHUMADA 500G",
            "Tocineta ahumada",
            "Carnes frias",
            23500m,
            "COP",
            null);

        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.Is<ProductSearchRequest>(request => request.Query == "papas fritas"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([papa], "mantis"));
        _commerce
            .Setup(c => c.SearchProductsAsync(
                ctx,
                It.Is<ProductSearchRequest>(request => request.Query == "tocineta"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([tocineta], "mantis"));

        var operation = new SearchProductsOperation(_commerce.Object);
        using var args = JsonDocument.Parse("""{"queries":["papas fritas","tocineta"],"limit":10}""");

        var json = (await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx }, CancellationToken.None)).Data.GetRawText();

        json.Should().Contain("PAPA FARM FRITES X 2.5K");
        json.Should().Contain("TOCINETA AHUMADA 500G");
        json.Should().Contain("\"count\":2");
        _commerce.Verify(c => c.SearchProductsAsync(
            ctx,
            It.Is<ProductSearchRequest>(request => request.Query == "quiero preparar un pocho"),
            It.IsAny<CancellationToken>()), Times.Never);
    }
    private static AgentConversationContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
