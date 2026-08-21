CREATE PROCEDURE dbo.InventoryBalancesSearch
    @BusinessId uniqueidentifier,
    @WarehouseId uniqueidentifier = NULL,
    @ProductId uniqueidentifier = NULL,
    @Search nvarchar(160) = NULL,
    @Pattern nvarchar(324) = NULL,
    @OnlyWithStock bit,
    @IncludeCosts bit,
    @Offset int,
    @PageSize int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*)
    FROM dbo.InventoryBalances b
    INNER JOIN dbo.Products p ON p.ProductId=b.ProductId AND p.BusinessId=b.BusinessId
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.BusinessId=b.BusinessId
    WHERE b.BusinessId=@BusinessId AND w.UseForSales=1
      AND (@WarehouseId IS NULL OR b.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR b.ProductId=@ProductId)
      AND (@OnlyWithStock=0 OR b.QuantityOnHand<>0)
      AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern
        OR p.Name LIKE @Pattern OR w.Code LIKE @Pattern OR w.Name LIKE @Pattern
        OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode
                   WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1));

    SELECT b.WarehouseId,w.Code,w.Name,b.ProductId,COALESCE(p.ProductCode,N''),p.Name,b.QuantityOnHand,
           CASE WHEN @IncludeCosts=1 THEN b.AverageUnitCost END,
           CASE WHEN @IncludeCosts=1 THEN b.InventoryValue END,b.UpdatedAt
    FROM dbo.InventoryBalances b
    INNER JOIN dbo.Products p ON p.ProductId=b.ProductId AND p.BusinessId=b.BusinessId
    INNER JOIN dbo.Warehouses w ON w.WarehouseId=b.WarehouseId AND w.BusinessId=b.BusinessId
    WHERE b.BusinessId=@BusinessId AND w.UseForSales=1
      AND (@WarehouseId IS NULL OR b.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR b.ProductId=@ProductId)
      AND (@OnlyWithStock=0 OR b.QuantityOnHand<>0)
      AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern
        OR p.Name LIKE @Pattern OR w.Code LIKE @Pattern OR w.Name LIKE @Pattern
        OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode
                   WHERE barcode.BusinessId=p.BusinessId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1))
    ORDER BY p.Name,b.WarehouseId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
