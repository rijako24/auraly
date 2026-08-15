using Xunit;
using System.Text.Json;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Tests.Services;

public sealed class CentralAuditPolicyTests
{
    [Fact]
    public async Task SaveChanges_audits_allowed_aggregate_and_redacts_sensitive_properties()
    {
        await using var context = CreateContext();
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Name = "Crema",
            RawPayloadJson = """{"accessToken":"never-store-this"}"""
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var audit = await context.AuditLogs.SingleAsync();
        Assert.Equal("Added", audit.Action);
        Assert.Equal(nameof(Product), audit.EntityType);
        Assert.Equal(product.ProductId.ToString(), audit.EntityId);
        Assert.Null(audit.OldValues);
        Assert.NotNull(audit.NewValues);

        using var values = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal("Crema", values.RootElement.GetProperty(nameof(Product.Name)).GetString());
        Assert.False(values.RootElement.TryGetProperty(nameof(Product.RawPayloadJson), out _));
    }

    [Fact]
    public async Task SaveChanges_only_captures_modified_properties()
    {
        await using var context = CreateContext();
        var product = new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Name = "Crema",
            Description = "Original"
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.AuditLogs.RemoveRange(context.AuditLogs);
        await context.SaveChangesAsync();

        product.Description = "Actualizada";
        await context.SaveChangesAsync();

        var audit = await context.AuditLogs.SingleAsync();
        using var before = JsonDocument.Parse(audit.OldValues!);
        using var after = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal("Original", before.RootElement.GetProperty(nameof(Product.Description)).GetString());
        Assert.Equal("Actualizada", after.RootElement.GetProperty(nameof(Product.Description)).GetString());
        Assert.Single(before.RootElement.EnumerateObject());
        Assert.Single(after.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task SaveChanges_does_not_audit_high_volume_excluded_entities()
    {
        await using var context = CreateContext();
        context.Messages.Add(new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Sender = "User",
            MessageText = "No debe duplicarse en auditoría",
            Timestamp = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task SaveChanges_without_changes_does_not_write_audit_rows()
    {
        await using var context = CreateContext();
        context.Products.Add(new Product
        {
            ProductId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            Name = "Crema"
        });
        await context.SaveChangesAsync();
        context.AuditLogs.RemoveRange(context.AuditLogs);
        await context.SaveChangesAsync();

        await context.SaveChangesAsync();

        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"central-audit-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}