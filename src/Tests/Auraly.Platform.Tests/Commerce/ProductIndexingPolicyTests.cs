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
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
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
