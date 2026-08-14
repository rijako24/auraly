using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ProductCandidateRetrieverCustomerKeyTests
{
    [Fact]
    public async Task RetrieveAsync_PrefersStableExternalKeyOverLegacyPhone()
    {
        var businessId = Guid.NewGuid();
        var product = Product(businessId);
        var stableAlias = Alias(
            businessId,
            product,
            "mantis:10013:6826");
        var fixture = Fixture(
            businessId,
            stableResults: [stableAlias],
            legacyResults: []);
        var context = Context(businessId);

        var results = await fixture.Retriever.RetrieveAsync(
            context,
            "papa",
            CancellationToken.None);

        results.Should().ContainSingle(candidate =>
            candidate.Product.ProductId == product.ProductId
            && candidate.Source == ProductMatchSource.CustomerAlias
            && candidate.CanAutoResolve);
        fixture.Aliases.Verify(repository => repository.FindActiveAsync(
            businessId,
            "papa",
            "mantis:10013:6826",
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Aliases.Verify(repository => repository.FindActiveAsync(
            businessId,
            "papa",
            "573001234567",
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetrieveAsync_FallsBackToLegacyPhoneAlias()
    {
        var businessId = Guid.NewGuid();
        var product = Product(businessId);
        var legacyAlias = Alias(
            businessId,
            product,
            "573001234567");
        var fixture = Fixture(
            businessId,
            stableResults: [],
            legacyResults: [legacyAlias]);

        var results = await fixture.Retriever.RetrieveAsync(
            Context(businessId),
            "papa",
            CancellationToken.None);

        results.Should().ContainSingle(candidate =>
            candidate.Product.ProductId == product.ProductId
            && candidate.Source == ProductMatchSource.CustomerAlias);
        fixture.Aliases.Verify(repository => repository.FindActiveAsync(
            businessId,
            "papa",
            "573001234567",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RetrieverFixture Fixture(
        Guid businessId,
        IReadOnlyList<ProductAlias> stableResults,
        IReadOnlyList<ProductAlias> legacyResults)
    {
        var aliases = new Mock<IProductAliasRepository>();
        aliases.Setup(repository => repository.FindActiveAsync(
                businessId,
                "papa",
                "mantis:10013:6826",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stableResults);
        aliases.Setup(repository => repository.FindActiveAsync(
                businessId,
                "papa",
                "573001234567",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(legacyResults);
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.SearchByIndexTermsAsync(
                businessId,
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var unit = new Mock<IUnitOfWork>();
        unit.SetupGet(value => value.ProductAliases).Returns(aliases.Object);
        unit.SetupGet(value => value.Products).Returns(products.Object);
        return new(
            new LocalProductCandidateRetriever(unit.Object),
            aliases);
    }

    private static AgentConversationContext Context(Guid businessId) => new()
    {
        BusinessId = businessId,
        ChannelPhone = "+57 300 123 4567",
        CommerceCustomer = new CommerceCustomerReference(
            CommerceProvider.Mantis,
            "10013",
            "6826",
            "Cliente",
            "3001234567")
    };

    private static Product Product(Guid businessId) => new()
    {
        ProductId = Guid.NewGuid(),
        BusinessId = businessId,
        ExternalProductId = "CG16",
        Sku = "CG16",
        Name = "PAPA MARQUISE 2.5",
        Currency = "COP",
        IsActive = true
    };

    private static ProductAlias Alias(
        Guid businessId,
        Product product,
        string customerKey) => new()
    {
        ProductAliasId = Guid.NewGuid(),
        BusinessId = businessId,
        ProductId = product.ProductId,
        Product = product,
        Scope = ProductAliasScope.Customer,
        CustomerKey = customerKey,
        Alias = "papa",
        NormalizedAlias = "papa",
        ResolutionMode = ProductAliasResolutionMode.AutoResolve,
        Status = ProductAliasStatus.Active
    };

    private sealed record RetrieverFixture(
        LocalProductCandidateRetriever Retriever,
        Mock<IProductAliasRepository> Aliases);
}
