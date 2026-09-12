CREATE PROCEDURE [dbo].[SellerOrderProductResolve]
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER = NULL,
    @Quantity DECIMAL(19,6) = NULL,
    @LinesJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Preserve the former result shape while old application instances drain.
    IF @LinesJson IS NULL
    BEGIN
        SELECT COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),p.Name,
               COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
               COALESCE(balance.QuantityOnHand,0),
               p.ManageStock,COALESCE(tax.Rate,0)
        FROM dbo.Products p
        LEFT JOIN dbo.InventoryBalances balance
          ON balance.BusinessId=@BusinessId
         AND balance.WarehouseId=@WarehouseId
         AND balance.ProductId=p.ProductId
        LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.IsActive=1
        WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
          AND p.ProductId=@ProductId AND p.IsActive=1
          AND EXISTS(
            SELECT 1 FROM dbo.ProductPrices price
            WHERE price.BusinessId=@BusinessId AND price.ProductId=p.ProductId
              AND price.IsActive=1 AND price.ValidFrom<=SYSDATETIMEOFFSET()
              AND (price.ValidUntil IS NULL OR price.ValidUntil>SYSDATETIMEOFFSET()));
        RETURN;
    END;

    ;WITH requested AS
    (
        SELECT ProductId,Quantity
        FROM OPENJSON(@LinesJson)
        WITH(ProductId UNIQUEIDENTIFIER '$.productId',Quantity DECIMAL(19,6) '$.quantity')
    )
    SELECT requested.ProductId,
           COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),p.Name,
           COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
           COALESCE(balance.QuantityOnHand,0),
           CAST(CASE WHEN p.ManageStock=1 OR inventoryLink.ProductLinkId IS NOT NULL THEN 1 ELSE 0 END AS BIT),
           COALESCE(tax.Rate,0),
           COALESCE(inventoryLink.ParentProductId,p.ProductId),
           COALESCE(NULLIF(inventoryLink.InventoryFactor,0),1)
    FROM requested
    JOIN dbo.Products p ON p.ProductId=requested.ProductId
    LEFT JOIN dbo.ProductLinks inventoryLink
      ON inventoryLink.BusinessId=@BusinessId
     AND inventoryLink.ChildProductId=p.ProductId
     AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
    LEFT JOIN dbo.InventoryBalances balance
      ON balance.BusinessId=@BusinessId
     AND balance.WarehouseId=@WarehouseId
     AND balance.ProductId=COALESCE(inventoryLink.ParentProductId,p.ProductId)
    LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.IsActive=1
    WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
      AND p.IsActive=1
      AND EXISTS(
        SELECT 1 FROM dbo.ProductPrices price
        WHERE price.BusinessId=@BusinessId AND price.ProductId=p.ProductId
          AND price.IsActive=1 AND price.ValidFrom<=SYSDATETIMEOFFSET()
          AND (price.ValidUntil IS NULL OR price.ValidUntil>SYSDATETIMEOFFSET()));
END
