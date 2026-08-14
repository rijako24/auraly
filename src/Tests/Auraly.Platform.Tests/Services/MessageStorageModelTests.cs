using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Data;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class MessageStorageModelTests
{
    [Fact]
    public void MessageText_AllowsCompleteMultiProductResponses()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=message-model;Trusted_Connection=True")
            .Options;
        using var context = new ApplicationDbContext(options);

        var property = context.Model.FindEntityType(typeof(Message))!
            .FindProperty(nameof(Message.MessageText))!;

        property.GetMaxLength().Should().BeNull();
        property.GetColumnType().Should().Be("nvarchar(max)");
    }
}
