using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CatalogRecommendationRegressionTests
{
    [Fact]
    public async Task MultipleCatalogQueries_ProduceOneRecommendationWithoutChangingResultCount()
    {
        var context = CreateContext();
        var pechuga = Product("PO28", "PECHUGA CRIOLLA", "CARNE DE POLLO");
        var cerdo = Product("CE45", "PIERNA DE CERDO TAJADA", "CARNE DE CERDO");
        var tocineta = Product("CF127", "TOCINETA CJ 1K", "CARNES FRIAS");
        var commerce = new Mock<ICommerceService>();
        commerce
            .Setup(service => service.SearchProductsAsync(
                context,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentConversationContext _, ProductSearchRequest request, CancellationToken _) =>
                request.Query == "pechuga"
                    ? new ProductSearchResult([pechuga], "mantis")
                    : new ProductSearchResult([cerdo], "mantis"));
        var recommendations = new Mock<ICatalogRecommendationService>();
        recommendations
            .Setup(service => service.ResolveAsync(
                context,
                It.Is<IReadOnlyList<ProductReference>>(products => products.Count == 2),
                It.IsAny<IReadOnlyList<ProductReference>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductRecommendation(
                tocineta,
                ProductRecommendationType.Complement,
                "Puede complementar la preparacion."));
        var operation = new SearchProductsOperation(
            commerce.Object,
            new Mock<IConversationFactsService>().Object,
            recommendations.Object);
        using var arguments = JsonDocument.Parse("""{"mode":"search","queries":["pechuga","cerdo"],"limit":10}""");

        var outcome = await operation.ExecuteAsync(
            arguments.RootElement,
            new OperationContext { Session = context });
        using var data = JsonDocument.Parse(outcome.Data.GetRawText());

        data.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        data.RootElement.GetProperty("products").GetArrayLength().Should().Be(2);
        data.RootElement.GetProperty("recommendations").GetArrayLength().Should().Be(1);
        recommendations.Verify(service => service.ResolveAsync(
            context,
            It.IsAny<IReadOnlyList<ProductReference>>(),
            It.IsAny<IReadOnlyList<ProductReference>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CjCatalogTemplate_RendersRecommendationOnlyInItsOptionalSection()
    {
        var seed = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "database",
            "Auraly.Database",
            "Scripts",
            "Seeds",
            "SeedCJDistribuciones.sql"));
        var config = JsonSerializer.Deserialize<AgentConfig>(
            ExtractSettingsJson(seed),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!;
        var renderer = new PromptTemplateRenderer();
        var product = new { name = "PECHUGA CRIOLLA", unit_price = 14033.67m, currency = "COP" };
        var recommendation = new
        {
            name = "TOCINETA CJ 1K",
            unit_price = 19099.41m,
            currency = "COP",
            reason = "Combina bien con la pechuga."
        };

        var withRecommendation = renderer.Render(
            config.Templates["catalog_results"],
            new Dictionary<string, object?>
            {
                ["products"] = new[] { product },
                ["recommendations"] = new[] { recommendation }
            });
        var withoutRecommendation = renderer.Render(
            config.Templates["catalog_results"],
            new Dictionary<string, object?>
            {
                ["products"] = new[] { product },
                ["recommendations"] = Array.Empty<object>()
            });

        withRecommendation.Should().Contain("*Productos disponibles*");
        withRecommendation.Should().Contain("PECHUGA CRIOLLA");
        withRecommendation.Should().Contain("*Tambi\u00e9n podr\u00eda servirte*");
        withRecommendation.Should().Contain("TOCINETA CJ 1K");
        withRecommendation.IndexOf("TOCINETA CJ 1K", StringComparison.Ordinal)
            .Should().BeGreaterThan(withRecommendation.IndexOf("PECHUGA CRIOLLA", StringComparison.Ordinal));
        withoutRecommendation.Should().Contain("PECHUGA CRIOLLA");
        withoutRecommendation.Should().NotContain("Tambi\u00e9n podr\u00eda servirte");
        seed.Should().Contain("MERGE dbo.ProductRecommendationRules");
        seed.Should().Contain("N'PO28', N'CF127'");
    }

    private static AgentConversationContext CreateContext() => new()
    {
        BusinessId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    private static ProductReference Product(string code, string name, string category) => new(
        null,
        code,
        code,
        name,
        null,
        category,
        100m,
        "COP",
        10m);

    private static string ExtractSettingsJson(string sql)
    {
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();
        return match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MimosBabySpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MimosBabySpa.sln.");
    }
}
