using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Infrastructure.Data;

internal static class ProductOfferModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductLink>(entity =>
        {
            entity.ToTable("ProductLinks");
            entity.HasKey(value => value.ProductLinkId);
            entity.Property(value => value.InventoryFactor).HasPrecision(19, 6);
            entity.Property(value => value.PriceFactor).HasPrecision(19, 6);
            entity.Property(value => value.ConversionFactor).HasPrecision(19, 6);
            entity.HasIndex(value => new { value.BusinessId, value.ChildProductId }).IsUnique();
            entity.HasIndex(value => new
            {
                value.BusinessId, value.ParentProductId, value.IsActive
            });
        });

        modelBuilder.Entity<ProductOffer>(entity =>
        {
            entity.HasKey(value => value.ProductOfferId);
            entity.Property(value => value.Condition).IsRequired().HasMaxLength(30);
            entity.Property(value => value.Color).HasMaxLength(100);
            entity.Property(value => value.VariantLabel).HasMaxLength(250);
            entity.Property(value => value.UnitPrice).HasPrecision(18, 2);
            entity.Property(value => value.Currency).IsRequired().HasMaxLength(10);
            entity.Property(value => value.PriceSourceUrl).HasMaxLength(1000);
            entity.HasOne(value => value.Product)
                .WithMany(value => value.Offers)
                .HasForeignKey(value => value.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Business)
                .WithMany()
                .HasForeignKey(value => value.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.BusinessId, value.Condition, value.IsActive, value.IsAvailable });
            entity.HasIndex(value => new
            {
                value.ProductId, value.Condition, value.StorageGb, value.Color, value.VariantLabel
            }).IsUnique();
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasKey(value => value.ProductImageId);
            entity.Property(value => value.MediaUrl).IsRequired().HasMaxLength(1500);
            entity.Property(value => value.AltText).HasMaxLength(300);
            entity.HasOne(value => value.Product)
                .WithMany(value => value.Images)
                .HasForeignKey(value => value.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Business)
                .WithMany()
                .HasForeignKey(value => value.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(value => value.ProductOffer)
                .WithMany(value => value.Images)
                .HasForeignKey(value => value.ProductOfferId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasIndex(value => new
            {
                value.ProductId,
                value.ProductOfferId,
                value.IsActive,
                value.DisplayOrder
            });
        });
    }
}
