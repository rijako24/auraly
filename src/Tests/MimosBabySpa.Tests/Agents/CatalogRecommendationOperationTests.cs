using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CatalogRecommendationOperationTests
{
    [Fact]
    public async Task Recommendation_IsSeparateFromResults_AndRemainsSelectable()
    {
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            ConversationState = new ConversationStateModel(),
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        var result = new ProductReference(
            Guid.NewGuid(), "PO28", "PO28", "PECHUGA CRIOLLA", null,
            "CARNE DE POLLO", 14033.67m, "COP", 20m);
        var recommended = new ProductReference(
            Guid.NewGuid(), "CF127", "CF127", "TOCINETA CJ 1K", null,
            "CARNES FRIAS", 19099.41m, "COP", 50m);
        var commerce = new Mock<ICommerceService>();
        commerce
            .Setup(service => service.SearchProductsAsync(
                context,
                It.IsAny<ProductSearchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductSearchResult([result], "mantis"));
        var recommendations = new Mock<ICatalogRecommendationService>();
        recommendations
            .Setup(service => service.ResolveAsync(
                context,
                It.IsAny<IReadOnlyList<ProductReference>>(),
                It.IsAny<IReadOnlyList<ProductReference>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductRecommendation(
                recommended,
                ProductRecommendationType.Complement,
                "Combina bien con la pechuga."));
        var facts = new Mock<IConversationFactsService>();
        var operation = new SearchProductsOperation(commerce.Object, facts.Object, recommendations.Object);
        using var arguments = JsonDocument.Parse("""{"mode":"search","query":"pechuga","limit":10}""");

        var outcome = await operation.ExecuteAsync(
            arguments.RootElement,
            new OperationContext { Session = context },
            CancellationToken.None);
        using var data = JsonDocument.Parse(outcome.Data.GetRawText());

        var products = data.RootElement.GetProperty("products");
        var recommendationItems = data.RootElement.GetProperty("recommendations");
        products.GetArrayLength().Should().Be(1);
        products[0].GetProperty("name").GetString().Should().Be("PECHUGA CRIOLLA");
        recommendationItems.GetArrayLength().Should().Be(1);
        recommendationItems[0].GetProperty("name").GetString().Should().Be("TOCINETA CJ 1K");
        context.Facts["system.catalog_products"].Should().Contain("PECHUGA CRIOLLA").And.NotContain("TOCINETA CJ 1K");
        context.Facts["system.catalog_recommendations"].Should().Contain("TOCINETA CJ 1K");

        commerce.Invocations.Clear();
        var resolver = new CommerceCartProductResolver(commerce.Object);
        var selected = await resolver.FindAsync(context, "tocineta cj");

        selected.Should().ContainSingle();
        selected[0].ExternalProductId.Should().Be("CF127");

        context.Facts["system.catalog_recommendations"] =
            """{"schemaVersion":1,"products":[{"productId":null,"externalProductId":"PO99","sku":"PO99","name":"PECHUGA RECOMENDADA","unitPrice":15000,"currency":"COP","stockQuantity":10}]}""";
        var originalSelection = await resolver.FindAsync(context, "pechuga");

        originalSelection.Should().ContainSingle();
        originalSelection[0].ExternalProductId.Should().Be("PO28");

        commerce.Verify(service => service.SearchProductsAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<ProductSearchRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

        commerce.Verify(service => service.AddItemAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<AddOrderItemRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

    }
}
