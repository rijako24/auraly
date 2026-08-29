CREATE PROCEDURE [dbo].[SellerOrderProductResolve]
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @Quantity DECIMAL(19,6)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),p.Name,
           COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),resolved.Amount,
           resolved.PriceSource,
           COALESCE(balance.QuantityOnHand,0),
           p.ManageStock,COALESCE(tax.Rate,0)
    FROM dbo.Products p
    LEFT JOIN dbo.InventoryBalances balance
      ON balance.BusinessId=@BusinessId
     AND balance.WarehouseId=@WarehouseId
     AND balance.ProductId=p.ProductId
    LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.IsActive=1
    CROSS APPLY dbo.CustomerProductPriceResolve(
      @BusinessId,@WarehouseId,@CustomerId,p.ProductId,@Quantity,SYSDATETIMEOFFSET()) resolved
    WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
      AND p.ProductId=@ProductId AND p.IsActive=1;
END
