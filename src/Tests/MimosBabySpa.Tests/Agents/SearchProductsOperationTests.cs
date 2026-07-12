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

        var outcome = await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx }, CancellationToken.None);
        var json = outcome.Data.GetRawText();

        outcome.Code.Should().Be("products.not_found");
        json.Should().Contain("\"count\":0");
        json.Should().Contain("\"search_text\":\"vino dulce\"");
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
    public async Task DisplayedCatalogProduct_IsResolvedFromTheSameAuthoritativeSnapshot()
    {
        var ctx = CreateContext();
        var displayed = new ProductReference(
            Guid.NewGuid(), "PO36", "PO36", "PECHUGA CAMPOLLO", null, "Pollo", 13541.35m, "COP", 7m);
        _commerce
            .Setup(c => c.SearchProductsAsync(ctx, It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([displayed], "mantis"));
        var facts = new Mock<IConversationFactsService>();
        var operation = new SearchProductsOperation(_commerce.Object, facts.Object);
        using var args = JsonDocument.Parse("""{"query":"pollo","limit":10}""");

        await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx }, CancellationToken.None);
        _commerce.Invocations.Clear();
        var resolver = new CommerceCartProductResolver(_commerce.Object);

        var matches = await resolver.FindAsync(ctx, "PECHUGA CAMPOLLO");

        matches.Should().ContainSingle();
        matches[0].Name.Should().Be("PECHUGA CAMPOLLO");
        matches[0].UnitPrice.Should().Be(13541.35m);
        matches[0].StockQuantity.Should().Be(7m);
        _commerce.Verify(c => c.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(), It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task PartialPluralReference_IsResolvedOnlyAgainstTheDisplayedCatalogSnapshot()
    {
        var ctx = CreateContext();
        var products = new[]
        {
            new ProductReference(Guid.NewGuid(), "PO26", "PO26", "PERNIL MERCAPOLLO", null, "Pollo", 5647.05m, "COP", null),
            new ProductReference(Guid.NewGuid(), "PO20", "PO20", "PERNIL CAMPOLLO", null, "Pollo", 6499.82m, "COP", null),
            new ProductReference(Guid.NewGuid(), "PO60", "PO60", "ALA JUMBO MERCAPOLLO", null, "Pollo", 7145.01m, "COP", null),
            new ProductReference(Guid.NewGuid(), "PO61", "PO61", "BANDEJA FILETE DE PECHUGA CON HUESO", null, "Pollo", 14634.14m, "COP", null),
            new ProductReference(Guid.NewGuid(), "PO62", "PO62", "PECHUGA MAC POLLO", null, "Pollo", 13001.08m, "COP", null),
            new ProductReference(Guid.NewGuid(), "PO63", "PO63", "PECHUGA CRIOLLA", null, "Pollo", 14033.67m, "COP", null)
        };
        _commerce
            .Setup(c => c.SearchProductsAsync(ctx, It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult(products, "mantis"));
        var facts = new Mock<IConversationFactsService>();
        var operation = new SearchProductsOperation(_commerce.Object, facts.Object);
        using var args = JsonDocument.Parse("""{"query":"pollo","limit":10}""");
        await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx }, CancellationToken.None);
        _commerce.Invocations.Clear();
        var resolver = new CommerceCartProductResolver(_commerce.Object);

        var perniles = await resolver.FindAsync(ctx, "perniles");
        var alas = await resolver.FindAsync(ctx, "alas");
        var bandejas = await resolver.FindAsync(ctx, "2 bandejas");
        var macPollo = await resolver.FindAsync(ctx, "1 de mac pollo");
        var criollas = await resolver.FindAsync(ctx, "2 criollas");

        perniles.Select(product => product.Name).Should().Equal("PERNIL MERCAPOLLO", "PERNIL CAMPOLLO");
        alas.Should().ContainSingle().Which.Name.Should().Be("ALA JUMBO MERCAPOLLO");
        bandejas.Should().ContainSingle().Which.Name.Should().Be("BANDEJA FILETE DE PECHUGA CON HUESO");
        macPollo.Should().ContainSingle().Which.Name.Should().Be("PECHUGA MAC POLLO");
        criollas.Should().ContainSingle().Which.Name.Should().Be("PECHUGA CRIOLLA");
        _commerce.Verify(c => c.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(), It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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
