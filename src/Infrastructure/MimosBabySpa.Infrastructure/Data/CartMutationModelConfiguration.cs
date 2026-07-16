using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Infrastructure.Data;

internal static class CartMutationModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CartMutationReceipt>(entity =>
        {
            entity.HasKey(receipt => receipt.CartMutationReceiptId);
            entity.Property(receipt => receipt.IdempotencyKey).IsRequired().HasMaxLength(200);
            entity.Property(receipt => receipt.SnapshotJson).IsRequired().HasColumnType("NVARCHAR(MAX)");
            entity.HasOne(receipt => receipt.Business).WithMany().HasForeignKey(receipt => receipt.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(receipt => receipt.Conversation).WithMany().HasForeignKey(receipt => receipt.ConversationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(receipt => new { receipt.BusinessId, receipt.ConversationId, receipt.IdempotencyKey }).IsUnique();
            entity.HasIndex(receipt => receipt.CreatedAt);
        });
}
