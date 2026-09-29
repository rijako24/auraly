using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ProductIndexingPolicyTests
{
    [Fact]
    public async Task CreateAsync_DoesNotGenerateSearchTermsImplicitly()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        var businessId = Guid.NewGuid();
        context.Businesses.Add(new Business
        {
            BusinessId = businessId,
            TenantId = businessId,
            Name = "Test business",
            IsActive = true
        });
        await context.SaveChangesAsync();
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            TenantId = businessId,
            BusinessId = businessId,
            Name = "JAMON CUNIT X 500GR",
            Sku = "CF17",
            UnitPrice = 10m,
            IsActive = true
        };

        await new ProductRepository(context).CreateAsync(product);
        await context.SaveChangesAsync();

        context.ProductSearchTerms.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_Seeds_separate_initial_prices_for_all_active_tenant_businesses()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        var tenantId = Guid.NewGuid();
        var sharedSource = Guid.NewGuid();
        var sharedPeer = Guid.NewGuid();
        var independent = Guid.NewGuid();
        foreach (var (businessId, sharesPrices) in new[]
            {
                (sharedSource, true), (sharedPeer, true), (independent, false)
            })
            context.Businesses.Add(new Business
            {
                BusinessId = businessId,
                TenantId = tenantId,
                Name = businessId.ToString("N"),
                IsActive = true,
                SharesProductPrices = sharesPrices
            });
        await context.SaveChangesAsync();

        var sharedProduct = new Product
        {
            ProductId = Guid.NewGuid(), BusinessId = sharedSource,
            Name = "Compartido", UnitPrice = 10m, IsActive = true
        };
        var independentProduct = new Product
        {
            ProductId = Guid.NewGuid(), BusinessId = independent,
            Name = "Independiente", UnitPrice = 20m, IsActive = true
        };
        var repository = new ProductRepository(context);
        await repository.CreateManyAsync([sharedProduct]);
        await repository.CreateManyAsync([independentProduct]);
        await context.SaveChangesAsync();

        sharedProduct.TenantId.Should().Be(tenantId);
        independentProduct.TenantId.Should().Be(tenantId);
        context.PublishedProductPrices.Where(price => price.ProductId == sharedProduct.ProductId)
            .Select(price => price.BusinessId).Should().BeEquivalentTo([sharedSource, sharedPeer, independent]);
        context.PublishedProductPrices.Where(price => price.ProductId == independentProduct.ProductId)
            .Select(price => price.BusinessId).Should().BeEquivalentTo([sharedSource, sharedPeer, independent]);
    }
}
