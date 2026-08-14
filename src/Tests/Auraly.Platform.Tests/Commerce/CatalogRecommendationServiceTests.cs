using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using ConversationStateModel = Auraly.Platform.Domain.Models.ConversationState;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class CatalogRecommendationServiceTests
{
    [Fact]
    public async Task ResolveAsync_ProductRuleWinsOverHigherPriorityCategoryRule()
    {
        var fixture = CreateFixture();
        fixture.Rules.Add(Rule(
            ProductRecommendationMatchType.Category,
            "CARNE DE POLLO",
            "SA30",
            priority: 999));
        fixture.Rules.Add(Rule(
            ProductRecommendationMatchType.Product,
            "PO28",
            "CF127",
            priority: 1));
        fixture.Commerce
            .Setup(service => service.GetProductAsync(
                fixture.Context,
                It.IsAny<ProductLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentConversationContext _, ProductLookupRequest request, CancellationToken _) =>
                Product(request.ExternalProductId!));

        var recommendation = await fixture.Service.ResolveAsync(
            fixture.Context,
            [Product("PO28", category: "CARNE DE POLLO")],
            []);

        recommendation.Should().NotBeNull();
        recommendation!.Product.ExternalProductId.Should().Be("CF127");
    }

    [Fact]
    public async Task ResolveAsync_PreviouslyRecommendedTargetUsesNextValidRule()
    {
        var fixture = CreateFixture();
        fixture.Rules.Add(Rule(ProductRecommendationMatchType.Product, "PO28", "CF127", 100));
        fixture.Rules.Add(Rule(ProductRecommendationMatchType.Category, "CARNE DE POLLO", "SA30", 50));
        fixture.Commerce
            .Setup(service => service.GetProductAsync(
                fixture.Context,
                It.IsAny<ProductLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentConversationContext _, ProductLookupRequest request, CancellationToken _) =>
                Product(request.ExternalProductId!));

        var recommendation = await fixture.Service.ResolveAsync(
            fixture.Context,
            [Product("PO28", category: "CARNE DE POLLO")],
            [Product("CF127")]);

        recommendation.Should().NotBeNull();
        recommendation!.Product.ExternalProductId.Should().Be("SA30");
        fixture.Commerce.Verify(service => service.GetProductAsync(
            fixture.Context,
            It.Is<ProductLookupRequest>(request => request.ExternalProductId == "CF127"),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_TargetAlreadyInCartIsNotOffered()
    {
        var fixture = CreateFixture();
        var draftId = Guid.NewGuid();
        fixture.Drafts
            .Setup(repository => repository.GetActiveDraftsByConversationAsync(
                fixture.Context.BusinessId,
                fixture.Context.ConversationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OrderDraft { OrderDraftId = draftId }]);
        fixture.DraftItems
            .Setup(repository => repository.GetByDraftIdAsync(
                fixture.Context.BusinessId,
                draftId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OrderDraftItem
                {
                    ExternalProductId = "CF127",
                    Sku = "CF127",
                    ProductNameSnapshot = "TOCINETA CJ 1K"
                }
            ]);
        fixture.Rules.Add(Rule(ProductRecommendationMatchType.Product, "PO28", "CF127", 100));

        var recommendation = await fixture.Service.ResolveAsync(
            fixture.Context,
            [Product("PO28", category: "CARNE DE POLLO")],
            []);

        recommendation.Should().BeNull();
        fixture.Commerce.Verify(service => service.GetProductAsync(
            It.IsAny<AgentConversationContext>(),
            It.IsAny<ProductLookupRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_UnavailableTargetFallsThroughDeterministically()
    {
        var fixture = CreateFixture();
        fixture.Rules.Add(Rule(ProductRecommendationMatchType.Product, "PO28", "CF127", 100));
        fixture.Rules.Add(Rule(ProductRecommendationMatchType.Product, "PO28", "SA30", 90));
        fixture.Commerce
            .Setup(service => service.GetProductAsync(
                fixture.Context,
                It.Is<ProductLookupRequest>(request => request.ExternalProductId == "CF127"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductReference?)null);
        fixture.Commerce
            .Setup(service => service.GetProductAsync(
                fixture.Context,
                It.Is<ProductLookupRequest>(request => request.ExternalProductId == "SA30"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product("SA30"));

        var recommendation = await fixture.Service.ResolveAsync(
            fixture.Context,
            [Product("PO28", category: "CARNE DE POLLO")],
            []);

        recommendation.Should().NotBeNull();
        recommendation!.Product.ExternalProductId.Should().Be("SA30");
        fixture.Commerce.Invocations
            .Where(invocation => invocation.Method.Name == nameof(IProductLookupService.GetProductAsync))
            .Select(invocation => ((ProductLookupRequest)invocation.Arguments[1]).ExternalProductId)
            .Should().Equal("CF127", "SA30");
    }

    [Fact]
    public async Task ResolveAsync_ProductLinksUseExternalIdentitiesForLiveLookup()
    {
        var fixture = CreateFixture();
        var sourceProductId = Guid.NewGuid();
        var recommendedProductId = Guid.NewGuid();
        fixture.Rules.Add(new ProductRecommendationRule
        {
            ProductRecommendationRuleId = Guid.NewGuid(),
            MatchType = ProductRecommendationMatchType.Product,
            SourceProductId = sourceProductId,
            SourceProduct = new Product
            {
                ProductId = sourceProductId,
                ExternalProductId = "SOURCE-LIVE",
                Sku = "SOURCE-SKU"
            },
            RecommendedProductId = recommendedProductId,
            RecommendedProduct = new Product
            {
                ProductId = recommendedProductId,
                ExternalProductId = "TARGET-LIVE",
                Sku = "TARGET-SKU",
                Name = "Cached target name"
            },
            RecommendationType = ProductRecommendationType.Complement,
            Priority = 100,
            IsActive = true
        });
        fixture.Commerce
            .Setup(service => service.GetProductAsync(
                fixture.Context,
                It.Is<ProductLookupRequest>(request =>
                    request.ProductId == recommendedProductId
                    && request.ExternalProductId == "TARGET-LIVE"
                    && request.Sku == "TARGET-SKU"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product("TARGET-LIVE"));

        var recommendation = await fixture.Service.ResolveAsync(
            fixture.Context,
            [Product("SOURCE-LIVE")],
            []);

        recommendation.Should().NotBeNull();
        recommendation!.Product.ExternalProductId.Should().Be("TARGET-LIVE");
        fixture.Commerce.VerifyAll();
    }


    [Fact]
    public async Task ResolveAsync_RecommendationFailureDoesNotInvalidatePrimaryCatalogResults()
    {
        var fixture = CreateFixture();
        fixture.RuleRepository
            .Setup(repository => repository.GetActiveAsync(
                fixture.Context.BusinessId,
                It.IsAny<Guid?>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Recommendation storage unavailable."));

        var action = async () => await fixture.Service.ResolveAsync(
            fixture.Context,
            [Product("PO28", category: "CARNE DE POLLO")],
            []);

        var recommendation = await action.Should().NotThrowAsync();
        recommendation.Which.Should().BeNull();
    }


    private static Fixture CreateFixture()
    {
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Conversation = new Conversation(),
            ConversationState = new ConversationStateModel(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Config = new AgentConfig
            {
                Commerce = new CommerceConfig
                {
                    Enabled = true,
                    Provider = CommerceProvider.Mantis
                }
            }
        };
        var connection = new IntegrationConnection
        {
            IntegrationConnectionId = Guid.NewGuid(),
            BusinessId = context.BusinessId,
            Provider = (int)CommerceProvider.Mantis,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            IsEnabled = true
        };
        var unitOfWork = new Mock<IUnitOfWork>();
        var integrations = new Mock<IIntegrationConnectionRepository>();
        var ruleRepository = new Mock<IProductRecommendationRuleRepository>();
        var drafts = new Mock<IOrderDraftRepository>();
        var draftItems = new Mock<IOrderDraftItemRepository>();
        var commerce = new Mock<IProductLookupService>();
        var rules = new List<ProductRecommendationRule>();

        unitOfWork.SetupGet(value => value.IntegrationConnections).Returns(integrations.Object);
        unitOfWork.SetupGet(value => value.ProductRecommendationRules).Returns(ruleRepository.Object);
        unitOfWork.SetupGet(value => value.OrderDrafts).Returns(drafts.Object);
        unitOfWork.SetupGet(value => value.OrderDraftItems).Returns(draftItems.Object);
        integrations
            .Setup(repository => repository.GetCommerceConnectionAsync(
                context.BusinessId,
                CommerceProvider.Mantis,
                CommerceCapability.CatalogAndOrders,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        ruleRepository
            .Setup(repository => repository.GetActiveAsync(
                context.BusinessId,
                connection.IntegrationConnectionId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);
        drafts
            .Setup(repository => repository.GetActiveDraftsByConversationAsync(
                context.BusinessId,
                context.ConversationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = new CatalogRecommendationService(
            unitOfWork.Object,
            commerce.Object,
            NullLogger<CatalogRecommendationService>.Instance);
        return new Fixture(context, service, commerce, drafts, draftItems, ruleRepository, rules);
    }

    private static ProductRecommendationRule Rule(
        ProductRecommendationMatchType matchType,
        string source,
        string target,
        int priority) => new()
        {
            ProductRecommendationRuleId = Guid.NewGuid(),
            MatchType = matchType,
            SourceValue = source,
            RecommendedExternalProductId = target,
            RecommendedSku = target,
            RecommendationType = ProductRecommendationType.Complement,
            Priority = priority,
            IsActive = true
        };

    private static ProductReference Product(
        string code,
        string? category = null) => new(
            null,
            code,
            code,
            $"PRODUCT {code}",
            null,
            category,
            100m,
            "COP",
            10m);

    private sealed record Fixture(
        AgentConversationContext Context,
        CatalogRecommendationService Service,
        Mock<IProductLookupService> Commerce,
        Mock<IOrderDraftRepository> Drafts,
        Mock<IOrderDraftItemRepository> DraftItems,
        Mock<IProductRecommendationRuleRepository> RuleRepository,
        List<ProductRecommendationRule> Rules);
}
