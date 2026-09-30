using System.Text.Json;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlProductLinksWriter
{
    public static async Task SaveChildrenAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid productId,
        IReadOnlyCollection<LinkedProductInput> children, DateTimeOffset now,
        bool setUnsharedManageStock, int validationError, CancellationToken ct)
    {
        var rows = children.Select(child => new
        {
            LinkId = Guid.NewGuid(),
            child.ChildProductId,
            child.SharesInventory,
            InventoryFactor = child.SharesInventory ? child.InventoryFactor : null,
            child.SharesPrice,
            PriceFactor = child.SharesPrice ? child.PriceFactor : null,
            child.AllowsConversion,
            ConversionFactor = child.AllowsConversion ? child.ConversionFactor : null
        });
        await using var command = new SqlCommand("""
            DECLARE @Links TABLE (
              LinkId UNIQUEIDENTIFIER NOT NULL,
              ChildProductId UNIQUEIDENTIFIER NOT NULL,
              SharesInventory BIT NOT NULL,
              InventoryFactor DECIMAL(19,6) NULL,
              SharesPrice BIT NOT NULL,
              PriceFactor DECIMAL(19,6) NULL,
              AllowsConversion BIT NOT NULL,
              ConversionFactor DECIMAL(19,6) NULL);
            INSERT @Links
            SELECT LinkId,ChildProductId,SharesInventory,InventoryFactor,
                   SharesPrice,PriceFactor,AllowsConversion,ConversionFactor
            FROM OPENJSON(@LinksJson) WITH (
              LinkId UNIQUEIDENTIFIER '$.LinkId',
              ChildProductId UNIQUEIDENTIFIER '$.ChildProductId',
              SharesInventory BIT '$.SharesInventory',
              InventoryFactor DECIMAL(19,6) '$.InventoryFactor',
              SharesPrice BIT '$.SharesPrice',
              PriceFactor DECIMAL(19,6) '$.PriceFactor',
              AllowsConversion BIT '$.AllowsConversion',
              ConversionFactor DECIMAL(19,6) '$.ConversionFactor');
            IF EXISTS (SELECT 1 FROM @Links WHERE ChildProductId=@ProductId)
              THROW @ValidationError,'A product cannot be linked to itself.',1;
            IF EXISTS (SELECT ChildProductId FROM @Links
                       GROUP BY ChildProductId HAVING COUNT(*)>1)
              THROW @ValidationError,'The linked product list contains duplicates.',1;
            IF EXISTS (
              SELECT 1 FROM @Links requested
              LEFT JOIN dbo.Products product
                ON product.ProductId=requested.ChildProductId
               AND product.TenantId=@TenantId AND product.IsActive=1
              WHERE product.ProductId IS NULL)
              THROW @ValidationError,'The linked product is outside the tenant or inactive.',1;
            IF EXISTS (
              SELECT 1 FROM @Links requested
              JOIN dbo.InventoryBalances balance
                ON balance.ProductId=requested.ChildProductId AND balance.QuantityOnHand<>0
              JOIN dbo.Businesses businessValue
                ON businessValue.BusinessId=balance.BusinessId AND businessValue.TenantId=@TenantId
              WHERE requested.SharesInventory=1)
              THROW @ValidationError,'El producto tiene existencias. Deja su inventario en cero antes de vincularlo.',1;
            IF EXISTS (SELECT 1 FROM @Links WHERE AllowsConversion=1)
               AND NOT EXISTS (
                 SELECT 1 FROM dbo.Products
                 WHERE ProductId=@ProductId AND TenantId=@TenantId
                   AND ManageStock=1 AND ConversionMaximumLossPercent IS NOT NULL)
              THROW @ValidationError,'A convertible family must manage inventory and define a maximum conversion loss.',1;
            IF EXISTS (
              SELECT 1 FROM @Links requested
              JOIN dbo.ProductLinks existing
                ON existing.TenantId=@TenantId AND existing.ParentProductId=requested.ChildProductId
               AND existing.IsActive=1)
              THROW @ValidationError,'Linked products cannot contain other linked products.',1;
            IF EXISTS (
              SELECT 1 FROM @Links requested
              JOIN dbo.ProductLinks existing
                ON existing.TenantId=@TenantId AND existing.ChildProductId=requested.ChildProductId
               AND existing.ParentProductId<>@ProductId AND existing.IsActive=1)
              THROW @ValidationError,'The product is already linked to another root product.',1;

            UPDATE existing
            SET ParentProductId=@ProductId,SharesInventory=requested.SharesInventory,
                InventoryFactor=requested.InventoryFactor,SharesPrice=requested.SharesPrice,
                PriceFactor=requested.PriceFactor,AllowsConversion=requested.AllowsConversion,
                ConversionFactor=requested.ConversionFactor,IsActive=1,UpdatedAt=@Now
            FROM dbo.ProductLinks existing
            JOIN @Links requested ON requested.ChildProductId=existing.ChildProductId
            WHERE existing.TenantId=@TenantId;
            INSERT dbo.ProductLinks
              (ProductLinkId,TenantId,ChildProductId,ParentProductId,InventoryFactor,
               PriceFactor,ConversionFactor,SharesInventory,SharesPrice,AllowsConversion,IsActive,CreatedAt)
            SELECT requested.LinkId,@TenantId,requested.ChildProductId,@ProductId,
                   requested.InventoryFactor,requested.PriceFactor,requested.ConversionFactor,
                   requested.SharesInventory,requested.SharesPrice,requested.AllowsConversion,1,@Now
            FROM @Links requested
            WHERE NOT EXISTS (
              SELECT 1 FROM dbo.ProductLinks existing WITH(UPDLOCK,HOLDLOCK)
              WHERE existing.TenantId=@TenantId
                AND existing.ChildProductId=requested.ChildProductId);
            UPDATE product
            SET ManageStock=CASE
                  WHEN requested.SharesInventory=1 THEN 0
                  WHEN requested.AllowsConversion=1 OR @SetUnsharedManageStock=1 THEN 1
                  ELSE product.ManageStock END,
                UpdatedAt=@Now
            FROM dbo.Products product
            JOIN @Links requested ON requested.ChildProductId=product.ProductId
            WHERE product.TenantId=@TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@LinksJson", JsonSerializer.Serialize(rows));
        command.Parameters.AddWithValue("@SetUnsharedManageStock", setUnsharedManageStock);
        command.Parameters.AddWithValue("@ValidationError", validationError);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(ct);
    }
}
