CREATE PROCEDURE [dbo].[SellerOrderCatalogGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @Search NVARCHAR(250),
    @Contains NVARCHAR(252),
    @Prefix NVARCHAR(251),
    @Skip INT,
    @Take INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Businesses b
        INNER JOIN dbo.Warehouses w ON w.BusinessId = b.BusinessId
        INNER JOIN dbo.Customers c ON c.BusinessId = b.BusinessId
        WHERE b.BusinessId = @BusinessId AND b.TenantId = @TenantId
          AND w.WarehouseId = @WarehouseId AND w.IsActive = 1 AND w.UseForSales = 1
          AND c.CustomerId = @CustomerId AND c.IsActive = 1)
        THROW 51300, 'Selecciona una bodega de venta válida.', 1;

    SELECT p.ProductId,
           COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
           p.Name,
           COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
           resolved.Amount,
           resolved.PriceSource,
           COALESCE(balance.QuantityOnHand,0),
           p.ManageStock
    FROM dbo.Products p
    CROSS APPLY dbo.CustomerProductPriceResolve(
      @BusinessId,@WarehouseId,@CustomerId,p.ProductId,1,SYSDATETIMEOFFSET()) resolved
    LEFT JOIN dbo.InventoryBalances balance
      ON balance.BusinessId=p.BusinessId
     AND balance.WarehouseId=@WarehouseId
     AND balance.ProductId=p.ProductId
    WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND(@Search=N''
      OR p.Name COLLATE Latin1_General_100_CI_AI LIKE @Contains COLLATE Latin1_General_100_CI_AI
      OR p.ProductCode COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI
      OR p.Sku COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI
      OR p.Reference COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI)
    ORDER BY CASE WHEN p.ProductCode=@Search OR p.Sku=@Search THEN 0 ELSE 1 END,p.Name,p.ProductId
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
