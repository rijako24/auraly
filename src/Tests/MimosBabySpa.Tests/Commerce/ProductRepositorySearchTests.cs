using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Data.ReadModels;
using MimosBabySpa.Infrastructure.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Commerce;

public sealed class ProductRepositorySearchTests
{
    [Fact]
    public async Task SearchByIndexTerms_IncludesActiveProductWithoutIndexRows()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var jamon = Product(businessId, "JAMON CUNIT X 500GR", "CF17", active: true);
        context.Products.Add(jamon);
        Publish(context, jamon, 10m);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchByIndexTermsAsync(
            businessId, ["jamon", "cuni"], 20);

        result.Should().ContainSingle(product => product.ProductId == jamon.ProductId);
    }

    [Fact]
    public async Task SearchByIndexTerms_ReturnsInactiveIdentityWithoutHidingActiveProduct()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var inactiveExternal = Product(businessId, "TOCINETA IMPORTADA", "EXT-1", active: false);
        var activeLocal = Product(businessId, "Tocineta ahumada 500 g", "LOCAL-1", active: true);
        context.Products.AddRange(inactiveExternal, activeLocal);
        Publish(context, inactiveExternal, 10m);
        Publish(context, activeLocal, 10m);
        context.ProductSearchTerms.Add(new ProductSearchTerm
        {
            BusinessId = businessId,
            ProductId = inactiveExternal.ProductId,
            Product = inactiveExternal,
            Term = "tocineta"
        });
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchByIndexTermsAsync(
            businessId, ["tocineta"], 20);

        result.Select(product => product.ProductId).Should().Contain([
            activeLocal.ProductId, inactiveExternal.ProductId]);
        result[0].ProductId.Should().Be(activeLocal.ProductId);
    }

    [Fact]
    public async Task SearchByIndexTerms_UnionsConfiguredTermsAndNativeIdentity()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var keywordProduct = Product(businessId, "PRESENTACION ESPECIAL", "KEY-1", active: true);
        var nativeProduct = Product(businessId, "Papa a la francesa 2.5 kg", "PAPA-1", active: true);
        context.Products.AddRange(keywordProduct, nativeProduct);
        Publish(context, keywordProduct, 10m);
        Publish(context, nativeProduct, 10m);
        context.ProductSearchTerms.Add(new ProductSearchTerm
        {
            BusinessId = businessId,
            ProductId = keywordProduct.ProductId,
            Product = keywordProduct,
            Term = "papa"
        });
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchByIndexTermsAsync(
            businessId, ["papa"], 20);

        result.Select(product => product.ProductId)
            .Should().BeEquivalentTo([keywordProduct.ProductId, nativeProduct.ProductId]);
    }

    [Fact]
    public async Task ReplaceSearchTerms_IndexesStableIdentityWithoutProviderDescriptionNoise()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var product = Product(businessId, "ALKAPARRAS VINAGRE x500gr", "PV48", active: true);
        product.ExternalProductId = "PV48";
        product.CategoryName = "VINAGRES";
        product.Description = "UNIDAD PRODUCTO GENERAL VARIOS";
        context.Products.Add(product);
        Publish(context, product, 10m);
        await context.SaveChangesAsync();

        var repository = new ProductRepository(context);
        await repository.ReplaceSearchTermsAsync(product);
        await context.SaveChangesAsync();

        var terms = await repository.GetSearchTermsAsync(businessId, product.ProductId);
        terms.Should().Contain(["alkaparra", "vinagre", "500", "gr", "48"]);
        terms.Should().NotContain(["producto", "general", "unidad", "vario", "pv"]);
        terms.Should().NotContain(term => term.Contains("produkto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetById_ReturnsNullWhenProductDoesNotExist()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();

        var result = await new ProductRepository(context).GetByIdAsync(
            businessId, Guid.NewGuid());

        result.Should().BeNull();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Product Product(Guid businessId, string name, string sku, bool active) => new()
    {
        ProductId = Guid.NewGuid(),
        BusinessId = businessId,
        Name = name,
        Sku = sku,
        UnitPrice = 10m,
        Currency = "COP",
        IsActive = active
    };

    [Fact]
    public async Task SearchAsync_DoesNotExposeLegacyUnitPriceWithoutPublishedPrice()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var product = Product(businessId, "PRODUCTO SIN PUBLICAR", "NO-PUBLICADO", active: true);
        product.UnitPrice = 98765m;
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchAsync(
            businessId, null, null, 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_UsesOnlyTheCanonicalPublishedPrice()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var product = Product(businessId, "PRODUCTO PUBLICADO", "PUBLICADO", active: true);
        product.UnitPrice = 111m;
        context.Products.Add(product);
        Publish(context, product, 25900m);
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).SearchAsync(
            businessId, null, null, 10);

        result.Should().ContainSingle();
        result[0].UnitPrice.Should().Be(25900m);
        result[0].HasPublishedPrice.Should().BeTrue();
    }

    [Fact]
    public async Task GetLinkedFamily_ReturnsEveryOptionWithItsIndependentPriceAndStock()
    {
        await using var context = CreateContext();
        var businessId = Guid.NewGuid();
        var model = Product(businessId, "Tenis Runner", "RUNNER", active: true);
        var black40 = Product(businessId, "Tenis Runner negro talla 40", "RUN-N40", active: true);
        var white41 = Product(businessId, "Tenis Runner blanco talla 41", "RUN-B41", active: true);
        model.StockQuantity = 0;
        black40.StockQuantity = 4;
        white41.StockQuantity = 7;
        context.Products.AddRange(model, black40, white41);
        Publish(context, model, 100_000m);
        Publish(context, black40, 110_000m);
        Publish(context, white41, 120_000m);
        context.ProductLinks.AddRange(
            FamilyLink(businessId, model.ProductId, black40.ProductId),
            FamilyLink(businessId, model.ProductId, white41.ProductId));
        await context.SaveChangesAsync();

        var result = await new ProductRepository(context).GetLinkedFamilyAsync(
            businessId, [black40.ProductId]);

        result.Select(product => product.ProductId).Should().BeEquivalentTo([
            model.ProductId, black40.ProductId, white41.ProductId]);
        result.Single(product => product.ProductId == black40.ProductId).StockQuantity.Should().Be(4);
        result.Single(product => product.ProductId == white41.ProductId).UnitPrice.Should().Be(120_000m);
    }

    private static ProductLink FamilyLink(Guid businessId, Guid parentId, Guid childId) => new()
    {
        ProductLinkId = Guid.NewGuid(),
        BusinessId = businessId,
        ParentProductId = parentId,
        ChildProductId = childId,
        SharesInventory = false,
        SharesPrice = false,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static void Publish(ApplicationDbContext context, Product product, decimal amount)
    {
        var now = DateTimeOffset.UtcNow;
        context.PublishedProductPrices.Add(new PublishedProductPriceRow
        {
            ProductPriceId = Guid.NewGuid(),
            BusinessId = product.BusinessId,
            ProductId = product.ProductId,
            Amount = amount,
            CurrencyCode = "COP",
            ValidFrom = now.AddMinutes(-1),
            IsActive = true,
            CreatedAt = now
        });
    }
}
