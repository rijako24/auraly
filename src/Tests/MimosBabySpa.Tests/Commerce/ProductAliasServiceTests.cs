using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

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
    public async Task ConfirmedExpression_LearnsCustomerAliasAndQueuesBusinessAliasForReview()
    {
        var businessId = Guid.NewGuid();
        var target = Product(businessId, "JAMON CUNIT X 500GR", "CF17");
        var fixture = Fixture(businessId, target);
        var created = new List<ProductAlias>();
        fixture.Aliases.Setup(repository => repository.CreateAsync(
                It.IsAny<ProductAlias>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAlias, CancellationToken>((alias, _) => created.Add(alias))
            .ReturnsAsync((ProductAlias alias, CancellationToken _) => alias);
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
        created.Should().ContainSingle(alias =>
            alias.Scope == ProductAliasScope.Customer
            && alias.Status == ProductAliasStatus.Active
            && alias.ResolutionMode == ProductAliasResolutionMode.AutoResolve
            && alias.CustomerKey == "573001234567");
        created.Should().ContainSingle(alias =>
            alias.Scope == ProductAliasScope.Business
            && alias.Status == ProductAliasStatus.Pending
            && alias.ResolutionMode == ProductAliasResolutionMode.SuggestOnly);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

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
