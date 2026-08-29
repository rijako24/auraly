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
}
