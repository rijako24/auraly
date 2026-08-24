using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class BusinessStorageModelTests
{
    [Fact]
    public void Business_model_does_not_query_the_removed_logo_column()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=business-model;Trusted_Connection=True")
            .Options;
        using var context = new ApplicationDbContext(options);

        var entity = context.Model.FindEntityType(typeof(Business));

        entity.Should().NotBeNull();
        entity!.FindProperty("LogoUrl").Should().BeNull();
    }
}
