using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Data;
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
        await context.SaveChangesAsync();

        var repository = new ProductRepository(context);
        await repository.ReplaceSearchTermsAsync(product);
        await context.SaveChangesAsync();

        var terms = await repository.GetSearchTermsAsync(businessId, product.ProductId);
        terms.Should().Contain(["alkaparra", "vinagre", "500", "gr", "48"]);
        terms.Should().NotContain(["producto", "general", "unidad", "vario", "pv"]);
        terms.Should().NotContain(term => term.Contains("produkto", StringComparison.Ordinal));
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
}
