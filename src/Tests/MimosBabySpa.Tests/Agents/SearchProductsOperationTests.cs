using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
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
    public async Task ExecuteAsync_WhenExactRancheraMatchesExist_FiltersFuzzyNoiseAndKeepsRecommendation()
    {
        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            Commerce = new CommerceConfig
            {
                Enabled = true,
                Matching = new ProductMatchingPolicy { ExactNameDominanceMinimumMatches = 2 }
            }
        };
        var ranchera = new ProductReference(Guid.NewGuid(), "CF1", "CF1", "SALCH RANCHERA X 5 UND", null, "Carnes frias", 12000m, "COP", 5m);
        var rancheraSuper = new ProductReference(Guid.NewGuid(), "CF2", "CF2", "SALCHICHA RANCHERA SUPER", null, "Carnes frias", 18000m, "COP", 5m);
        var manguera = new ProductReference(Guid.NewGuid(), "CF3", "CF3", "SALCHI MANGUERA LARGA", null, "Carnes frias", 10000m, "COP", 5m);
        var panelera = new ProductReference(Guid.NewGuid(), "SA1", "SA1", "SALSA PANELERA", null, "Salsas", 8000m, "COP", 5m);
        var brioche = new ProductReference(Guid.NewGuid(), "PA1", "PA1", "PAN BURGER BRIOCHE", null, "Pan", 9000m, "COP", 5m);
        _commerce
            .Setup(service => service.SearchProductsAsync(ctx, It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([ranchera, rancheraSuper, manguera, panelera], "mantis"));
        var recommendations = new Mock<ICatalogRecommendationService>();
        recommendations
            .Setup(service => service.ResolveAsync(
                ctx,
                It.Is<IReadOnlyList<ProductReference>>(products => products.Count == 2),
                It.IsAny<IReadOnlyList<ProductReference>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductRecommendation(
                brioche,
                ProductRecommendationType.Complement,
                "Complementa las rancheras."));
        var operation = new SearchProductsOperation(
            _commerce.Object,
            new Mock<IConversationFactsService>().Object,
            recommendations.Object);
        using var args = JsonDocument.Parse("""{"query":"rancheras","limit":10}""");

        var outcome = await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx });
        using var data = JsonDocument.Parse(outcome.Data.GetRawText());

        data.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        data.RootElement.GetProperty("products").EnumerateArray()
            .Select(product => product.GetProperty("name").GetString())
            .Should().Equal("SALCH RANCHERA X 5 UND", "SALCHICHA RANCHERA SUPER");
        data.RootElement.GetProperty("recommendations").GetArrayLength().Should().Be(1);
        data.RootElement.GetProperty("recommendations")[0].GetProperty("name").GetString()
            .Should().Be("PAN BURGER BRIOCHE");
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
    [Fact]
    public async Task SequentialSearches_KeepProductsFromEveryOfferInTheActiveRequest()
    {
        var ctx = CreateContext();
        var pechuga = new ProductReference(
            Guid.NewGuid(), "PO63", "PO63", "PECHUGA CRIOLLA", null, "Pollo", 14033.67m, "COP", 20m);
        var cerdo = new ProductReference(
            Guid.NewGuid(), "CE10", "CE10", "PIERNA DE CERDO CON PIEL Y HUESO", null, "Cerdo", 10319.16m, "COP", 15m);
        _commerce
            .Setup(c => c.SearchProductsAsync(ctx, It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentConversationContext _, ProductSearchRequest request, CancellationToken _) =>
                request.Query == "pechuga"
                    ? new ProductSearchResult([pechuga], "mantis")
                    : new ProductSearchResult([cerdo], "mantis"));
        var facts = new Mock<IConversationFactsService>();
        var operation = new SearchProductsOperation(_commerce.Object, facts.Object);

        using (var pechugaArgs = JsonDocument.Parse("""{"query":"pechuga","limit":10}"""))
            await operation.ExecuteAsync(pechugaArgs.RootElement, new OperationContext { Session = ctx });
        using (var cerdoArgs = JsonDocument.Parse("""{"query":"cerdo","limit":10}"""))
            await operation.ExecuteAsync(cerdoArgs.RootElement, new OperationContext { Session = ctx });

        _commerce.Invocations.Clear();
        var resolver = new CommerceCartProductResolver(_commerce.Object);
        var rememberedPechuga = await resolver.FindAsync(ctx, "pechuga criolla");
        var rememberedCerdo = await resolver.FindAsync(ctx, "pierna con piel");

        rememberedPechuga.Should().ContainSingle().Which.Name.Should().Be("PECHUGA CRIOLLA");
        rememberedCerdo.Should().ContainSingle().Which.Name.Should().Be("PIERNA DE CERDO CON PIEL Y HUESO");
        ctx.Facts["system.catalog_products"].Should().Contain("schemaVersion").And.Contain(":2");
        ctx.Facts["system.catalog_products"].Should().Contain("searchTerms").And.Contain("pechuga");
        ctx.Facts["system.catalog_products"].Should().Contain("searchTerms").And.Contain("cerdo");
        _commerce.Verify(c => c.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(), It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task OfferMemory_UsesConfiguredBoundsAndKeepsTheMostRecentSnapshots()
    {
        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            Commerce = new CommerceConfig
            {
                Enabled = true,
                OfferMemoryMaxSnapshots = 2,
                OfferMemoryMaxProducts = 2
            }
        };
        _commerce
            .Setup(c => c.SearchProductsAsync(ctx, It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentConversationContext _, ProductSearchRequest request, CancellationToken _) =>
                new ProductSearchResult(
                    [new ProductReference(null, request.Query, request.Query, request.Query!.ToUpperInvariant(), null, null, 1m, "COP", 1m)],
                    "mantis"));
        var operation = new SearchProductsOperation(_commerce.Object, new Mock<IConversationFactsService>().Object);

        foreach (var query in new[] { "pechuga", "cerdo", "salchicha" })
        {
            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { query, limit = 10 }));
            await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx });
        }

        var memory = ctx.Facts["system.catalog_products"];
        memory.Should().NotContain("PECHUGA");
        memory.Should().Contain("CERDO");
        memory.Should().Contain("SALCHICHA");
        memory.Should().Contain("sequence").And.Contain(":3");
    }
    [Fact]
    public async Task SemanticReplacementReference_IsRememberedWithoutMatchingAnyPhraseRule()
    {
        var ctx = CreateContext();
        ctx.LatestUserMessage = "Ese maíz ya no me convence; muéstrame otros";
        ctx.Config = new AgentConfig
        {
            Commerce = new CommerceConfig
            {
                Enabled = true,
                Conversation = new CommerceConversationPolicy { ProductReplacementRules = [] }
            }
        };
        var options = new[]
        {
            new ProductReference(null, "M1", "M1", "MAIZ SUPER DULCE X 500 GR", null, null, 8_000m, "COP", 20m),
            new ProductReference(null, "M2", "M2", "MAIZ TIERNO X 1 KG", null, null, 9_000m, "COP", 20m)
        };
        _commerce
            .Setup(service => service.SearchProductsAsync(
                ctx, It.IsAny<ProductSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult(options, "mantis"));
        var operation = new SearchProductsOperation(
            _commerce.Object, new Mock<IConversationFactsService>().Object);
        using var args = JsonDocument.Parse(
            """{"query":"maiz","replacement_reference":"maíz","limit":10}""");

        await operation.ExecuteAsync(args.RootElement, new OperationContext { Session = ctx });

        var offer = CatalogOfferMemory.Read(ctx.Facts)!.Snapshots.Single();
        offer.ReplacementReference.Should().Be("maíz");
        offer.Products.Select(product => product.Name).Should().Equal(
            "MAIZ SUPER DULCE X 500 GR", "MAIZ TIERNO X 1 KG");
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
