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
    FROM dbo.Products p
    CROSS JOIN dbo.Warehouses w
    LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=@BusinessId AND b.ProductId=p.ProductId AND b.WarehouseId=w.WarehouseId
    WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND p.IsActive=1 AND w.BusinessId=@BusinessId AND w.IsActive=1 AND w.IsInventoryVisible=1
      AND (@WarehouseId IS NULL OR w.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR p.ProductId=@ProductId)
      AND (@OnlyWithStock=0 OR COALESCE(b.QuantityOnHand,0)<>0)
      AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern
        OR p.Name LIKE @Pattern OR w.Code LIKE @Pattern OR w.Name LIKE @Pattern
        OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode
                   WHERE barcode.BusinessId=@BusinessId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1));

    SELECT w.WarehouseId,w.Code,w.Name,p.ProductId,COALESCE(p.ProductCode,N''),p.Name,p.ManageStock,COALESCE(b.QuantityOnHand,0),
           CASE WHEN @IncludeCosts=1 THEN price.CostBasisAmount END,
           CASE WHEN @IncludeCosts=1 THEN b.AverageUnitCost END,
           CASE WHEN @IncludeCosts=1 THEN b.InventoryValue END,b.UpdatedAt
    FROM dbo.Products p
    CROSS JOIN dbo.Warehouses w
    LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=@BusinessId AND b.ProductId=p.ProductId AND b.WarehouseId=w.WarehouseId
    LEFT JOIN dbo.ProductPrices price ON price.BusinessId=@BusinessId AND price.ProductId=p.ProductId AND price.IsActive=1
    WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND p.IsActive=1 AND w.BusinessId=@BusinessId AND w.IsActive=1 AND w.IsInventoryVisible=1
      AND (@WarehouseId IS NULL OR w.WarehouseId=@WarehouseId)
      AND (@ProductId IS NULL OR p.ProductId=@ProductId)
      AND (@OnlyWithStock=0 OR COALESCE(b.QuantityOnHand,0)<>0)
      AND (@Search IS NULL OR p.ProductCode LIKE @Pattern OR p.Reference LIKE @Pattern
        OR p.Name LIKE @Pattern OR w.Code LIKE @Pattern OR w.Name LIKE @Pattern
        OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes barcode
                   WHERE barcode.BusinessId=@BusinessId AND barcode.ProductId=p.ProductId
                     AND barcode.Barcode LIKE @Pattern AND barcode.IsActive=1))
    ORDER BY p.Name,w.WarehouseId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
