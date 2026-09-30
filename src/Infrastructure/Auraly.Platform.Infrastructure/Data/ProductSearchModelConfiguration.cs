using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Infrastructure.Data;

internal static class ProductSearchModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductAlias>(entity =>
        {
            entity.HasKey(alias => alias.ProductAliasId);
            entity.Property(alias => alias.Scope).HasConversion<int>();
            entity.Property(alias => alias.CustomerKey).IsRequired().HasMaxLength(100);
            entity.Property(alias => alias.Alias).IsRequired().HasMaxLength(250);
            entity.Property(alias => alias.NormalizedAlias).IsRequired().HasMaxLength(250);
            entity.Property(alias => alias.Kind).HasConversion<int>();
            entity.Property(alias => alias.ResolutionMode).HasConversion<int>();
            entity.Property(alias => alias.Source).HasConversion<int>();
            entity.Property(alias => alias.Status).HasConversion<int>();
            entity.Property(alias => alias.RowVersion).IsRowVersion();
            entity.HasOne(alias => alias.Product).WithMany(product => product.Aliases).HasForeignKey(alias => alias.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(alias => new { alias.TenantId, alias.ProductId, alias.Scope, alias.CustomerKey, alias.NormalizedAlias }).IsUnique();
            entity.HasIndex(alias => new { alias.TenantId, alias.NormalizedAlias, alias.Scope, alias.CustomerKey, alias.Status });
            entity.HasIndex(alias => new { alias.TenantId, alias.Scope, alias.CustomerKey, alias.NormalizedAlias })
                .IsUnique().HasFilter("[Status] = 1 AND [ResolutionMode] = 1");
        });

        modelBuilder.Entity<ProductSearchTerm>(entity =>
        {
            entity.HasKey(term => new { term.TenantId, term.ProductId, term.Term });
            entity.Property(term => term.Term).IsRequired().HasMaxLength(100);
            entity.HasOne(term => term.Product).WithMany(product => product.SearchTerms).HasForeignKey(term => term.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(term => new { term.TenantId, term.Term, term.ProductId });
        });
    }
}
