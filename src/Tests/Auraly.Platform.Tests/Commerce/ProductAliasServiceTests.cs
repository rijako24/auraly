using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ProductAliasServiceTests
{
    [Fact]
    public async Task Import_AutoResolveAliasMatchingAnotherNativeProduct_IsRejected()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var nativeOwner = Product(businessId, "JAMONADA CUNICHEF", "OTHER");
        var fixture = Fixture(businessId, target, nativeCandidates: [nativeOwner]);

        var result = await fixture.Service.ImportAsync(businessId,
            new ProductAliasImportRequest([new("jamonada cunichef", ProductId: target.ProductId)]));

        result.Created.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Code == "native_identity_conflict");
        fixture.Aliases.Verify(repository => repository.CreateAsync(
            It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Import_DryRun_ValidatesAndCountsWithoutWriting()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var fixture = Fixture(businessId, target);

        var result = await fixture.Service.ImportAsync(businessId,
            new ProductAliasImportRequest(
                [new("jamonada cunichef", ProductId: target.ProductId)], DryRun: true));

        result.Created.Should().Be(1);
        result.Errors.Should().BeEmpty();
        fixture.Aliases.Verify(repository => repository.CreateAsync(
            It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Import_ValidatesDuplicateAndConflictingRowsWithinSameBatch()
    {
        var businessId = Guid.NewGuid();
        var first = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var second = Product(businessId, "MORTADELA X 500GR", "CF18");
        var fixture = Fixture(businessId, first, [second]);
        var row = new ProductAliasImportItem("producto de siempre", ProductId: first.ProductId);

        var result = await fixture.Service.ImportAsync(businessId,
            new ProductAliasImportRequest(
                [row, row, new("producto de siempre", ProductId: second.ProductId)],
                DryRun: true));

        result.Created.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Errors.Should().ContainSingle(error => error.Code == "alias_conflict");
        fixture.Aliases.Verify(repository => repository.CreateAsync(
            It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByProduct_ExplainsMappingsSharedAcrossProductsAndCustomers()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "MOTOR OSSEO 100", "OS100");
        var other = Product(businessId, "MOTOR OSSEO 200", "OS200");
        var fixture = Fixture(businessId, target, [other]);
        var globalTarget = LearnedAlias(
            businessId, target.ProductId, ProductAliasScope.Business);
        globalTarget.NormalizedAlias = "motor implante";
        var customerTarget = LearnedAlias(
            businessId, target.ProductId, ProductAliasScope.Customer);
        customerTarget.NormalizedAlias = "motor implante";
        customerTarget.CustomerKey = "customer-a";
        var globalOther = LearnedAlias(
            businessId, other.ProductId, ProductAliasScope.Business);
        globalOther.NormalizedAlias = "motor implante";

        fixture.Aliases.Setup(repository => repository.GetByProductAsync(
                businessId, target.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([globalTarget, customerTarget]);
        fixture.Aliases.Setup(repository => repository.GetByBusinessAsync(
                businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([globalTarget, customerTarget, globalOther]);

        var result = await fixture.Service.GetByProductAsync(
            businessId, target.ProductId);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(alias =>
            alias.NormalizedAlias == "motor implante"
            && alias.SharedMappingCount == 3
            && alias.DistinctProductCount == 2
            && alias.BusinessMappingCount == 2
            && alias.DistinctCustomerCount == 1);
    }

    [Fact]
    public async Task Import_LargeBatch_UsesBulkReadsInsteadOfPerRowQueries()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var fixture = Fixture(businessId, target);
        var items = Enumerable.Range(0, 1_000)
            .Select(index => new ProductAliasImportItem($"alias conocido {index}", ProductId: target.ProductId))
            .ToList();

        var result = await fixture.Service.ImportAsync(businessId,
            new ProductAliasImportRequest(items, DryRun: true));

        result.Created.Should().Be(1_000);
        result.Errors.Should().BeEmpty();
        fixture.UnitOfWork.VerifyGet(unit => unit.Products, Times.AtLeastOnce);
        fixture.Aliases.Verify(repository => repository.FindConflictsAsync(
            It.IsAny<Guid>(), It.IsAny<ProductAliasScope>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmedExpression_RequiresTwoCustomerConfirmationsAndQueuesBusinessAliasForReview()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var fixture = Fixture(businessId, target);
        var created = new List<ProductAlias>();
        fixture.Aliases.Setup(repository => repository.CreateAsync(
                It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAlias, CancellationToken>((alias, _) => created.Add(alias))
            .ReturnsAsync((ProductAlias alias, CancellationToken _) => alias);
        fixture.Aliases.Setup(repository => repository.GetMappingAsync(
                businessId, target.ProductId, It.IsAny<ProductAliasScope>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, ProductAliasScope scope, string customerKey,
                string normalizedAlias, CancellationToken _) => created.FirstOrDefault(alias =>
                    alias.Scope == scope
                    && alias.CustomerKey == customerKey
                    && alias.NormalizedAlias == normalizedAlias));
        var context = new AgentConversationContext
        {
            BusinessId = businessId,
            ConversationId = Guid.NewGuid(),
            ChannelPhone = "+57 300 123 4567"
        };

        await fixture.Service.LearnConfirmedAsync(context, "jamonada cunichef",
            new ProductReference(target.ProductId, target.ExternalProductId, target.Sku,
                target.Name, null, null, target.UnitPrice, target.Currency, target.StockQuantity));

        created.Should().HaveCount(2);
        var customerAlias = created.Should().ContainSingle(alias =>
            alias.Scope == ProductAliasScope.Customer
            && alias.Status == ProductAliasStatus.Pending
            && alias.ResolutionMode == ProductAliasResolutionMode.SuggestOnly
            && alias.CustomerKey == "573001234567").Subject;
        created.Should().ContainSingle(alias =>
            alias.Scope == ProductAliasScope.Business
            && alias.Status == ProductAliasStatus.Pending
            && alias.ResolutionMode == ProductAliasResolutionMode.SuggestOnly);
        await fixture.Service.LearnConfirmedAsync(context, "jamonada cunichef",
            new ProductReference(target.ProductId, target.ExternalProductId, target.Sku,
                target.Name, null, null, target.UnitPrice, target.Currency, target.StockQuantity));

        customerAlias.UsageCount.Should().Be(2);
        customerAlias.Status.Should().Be(ProductAliasStatus.Active);
        customerAlias.ResolutionMode.Should().Be(ProductAliasResolutionMode.AutoResolve);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Review_ApproveSuggestOnly_ActivatesLearnedAliasWithoutConflictLookup()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var fixture = Fixture(businessId, target);
        var learned = LearnedAlias(businessId, target.ProductId, ProductAliasScope.Business);
        fixture.Aliases.Setup(repository => repository.GetByIdAsync(
                businessId, target.ProductId, learned.ProductAliasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(learned);

        var result = await fixture.Service.ReviewAsync(
            businessId,
            target.ProductId,
            learned.ProductAliasId,
            new ReviewProductAliasRequest(ProductAliasReviewAction.Approve));

        result.Status.Should().Be(ProductAliasStatus.Active);
        result.ResolutionMode.Should().Be(ProductAliasResolutionMode.SuggestOnly);
        fixture.Aliases.Verify(repository => repository.FindConflictsAsync(
            It.IsAny<Guid>(), It.IsAny<ProductAliasScope>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Review_AutoResolveWithActiveConflict_IsRejectedWithoutWriting()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var other = Product(businessId, "MORTADELA X 500GR", "CF18");
        var fixture = Fixture(businessId, target);
        var learned = LearnedAlias(businessId, target.ProductId, ProductAliasScope.Business);
        fixture.Aliases.Setup(repository => repository.GetByIdAsync(
                businessId, target.ProductId, learned.ProductAliasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(learned);
        fixture.Aliases.Setup(repository => repository.FindConflictsAsync(
                businessId, ProductAliasScope.Business, string.Empty,
                learned.NormalizedAlias, target.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ProductAlias
            {
                ProductAliasId = Guid.NewGuid(), BusinessId = businessId, ProductId = other.ProductId,
                Scope = ProductAliasScope.Business, Alias = learned.Alias,
                NormalizedAlias = learned.NormalizedAlias, Status = ProductAliasStatus.Active
            }]);

        var action = () => fixture.Service.ReviewAsync(
            businessId,
            target.ProductId,
            learned.ProductAliasId,
            new ReviewProductAliasRequest(
                ProductAliasReviewAction.Approve,
                ProductAliasResolutionMode.AutoResolve));

        await action.Should().ThrowAsync<DomainValidationException>();
        fixture.Aliases.Verify(repository => repository.UpdateAsync(
            It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Promote_CustomerLearning_CreatesActiveGlobalLearnedAlias()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var fixture = Fixture(businessId, target);
        var learned = LearnedAlias(businessId, target.ProductId, ProductAliasScope.Customer);
        learned.CustomerKey = "573001234567";
        learned.UsageCount = 3;
        fixture.Aliases.Setup(repository => repository.GetByIdAsync(
                businessId, target.ProductId, learned.ProductAliasId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(learned);
        ProductAlias? created = null;
        fixture.Aliases.Setup(repository => repository.CreateAsync(
                It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAlias, CancellationToken>((alias, _) => created = alias)
            .ReturnsAsync((ProductAlias alias, CancellationToken _) => alias);

        var result = await fixture.Service.PromoteAsync(
            businessId,
            target.ProductId,
            learned.ProductAliasId,
            new PromoteProductAliasRequest());

        result.Scope.Should().Be(ProductAliasScope.Business);
        result.Source.Should().Be(ProductAliasSource.Learned);
        result.Status.Should().Be(ProductAliasStatus.Active);
        created.Should().NotBeNull();
        created!.CustomerKey.Should().BeEmpty();
        created.UsageCount.Should().Be(3);
    }

    private static ProductAlias LearnedAlias(
        Guid businessId,
        Guid productId,
        ProductAliasScope scope) => new()
    {
        ProductAliasId = Guid.NewGuid(),
        BusinessId = businessId,
        ProductId = productId,
        Scope = scope,
        CustomerKey = string.Empty,
        Alias = "jamonada cunichef",
        NormalizedAlias = "jamonada cunichef",
        Kind = ProductAliasKind.Alias,
        ResolutionMode = ProductAliasResolutionMode.SuggestOnly,
        Source = ProductAliasSource.Learned,
        Status = ProductAliasStatus.Pending,
        UsageCount = 1,
        CreatedAt = DateTime.UtcNow
    };

    private static AliasFixture Fixture(
        Guid businessId, Product target, IReadOnlyList<Product>? nativeCandidates = null)
    {
        var products = new Mock<IProductRepository>();
        var aliases = new Mock<IProductAliasRepository>();
        var unit = new Mock<IUnitOfWork>();
        unit.SetupGet(value => value.Products).Returns(products.Object);
        unit.SetupGet(value => value.ProductAliases).Returns(aliases.Object);
        unit.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var catalog = new[] { target }.Concat(nativeCandidates ?? []).ToList();
        products.Setup(repository => repository.GetIdentityCatalogAsync(
                businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        aliases.Setup(repository => repository.GetByBusinessAsync(
                businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        products.Setup(repository => repository.GetByIdAsync(
                businessId, target.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        products.Setup(repository => repository.GetBySkuAsync(
                businessId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        products.Setup(repository => repository.GetByAnyExternalIdAsync(
                businessId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);
        products.Setup(repository => repository.SearchAsync(
                businessId, It.IsAny<string?>(), null, 50,
                It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(nativeCandidates ?? []);
        aliases.Setup(repository => repository.FindConflictsAsync(
                businessId, It.IsAny<ProductAliasScope>(), It.IsAny<string>(),
                It.IsAny<string>(), target.ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        aliases.Setup(repository => repository.GetMappingAsync(
                businessId, target.ProductId, It.IsAny<ProductAliasScope>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductAlias?)null);
        aliases.Setup(repository => repository.CreateAsync(
                It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductAlias alias, CancellationToken _) => alias);
        aliases.Setup(repository => repository.UpdateAsync(
                It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductAlias alias, CancellationToken _) => alias);
        return new(new ProductAliasService(unit.Object), unit, aliases);
    }

    private static Product Product(Guid businessId, string name, string sku) => new()
    {
        ProductId = Guid.NewGuid(), BusinessId = businessId, Name = name, Sku = sku,
        ExternalProductId = sku, UnitPrice = 10, Currency = "COP", StockQuantity = 100
    };

    private sealed record AliasFixture(
        ProductAliasService Service, Mock<IUnitOfWork> UnitOfWork,
        Mock<IProductAliasRepository> Aliases);
}
