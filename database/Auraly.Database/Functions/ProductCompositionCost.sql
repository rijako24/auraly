CREATE FUNCTION [catalog].[ProductCompositionCost](
    @TenantId UNIQUEIDENTIFIER,@BusinessId UNIQUEIDENTIFIER,@Components NVARCHAR(MAX),@AsOf DATETIMEOFFSET(7))
RETURNS TABLE
AS RETURN
(
    SELECT TRY_CONVERT(DECIMAL(19,6),SUM(component.Quantity*cost.UnitCostWithTax)) Cost,
      COUNT_BIG(*) ComponentCount,
      SUM(CASE WHEN p.ProductId IS NULL OR cost.CostBasisAmount IS NULL OR cost.CostBasisAmount<=0
        OR (cost.PurchaseTaxRate IS NULL AND cost.IsGross=0)
        OR component.Quantity IS NULL OR component.Quantity<=0 THEN 1 ELSE 0 END) Missing
    FROM OPENJSON(@Components) WITH(ProductId UNIQUEIDENTIFIER '$.IngredientProductId',
      Quantity DECIMAL(19,6) '$.QuantityPerUnit') component
    LEFT JOIN dbo.Products p ON p.TenantId=@TenantId AND p.ProductId=component.ProductId AND p.IsActive=1
    OUTER APPLY dbo.ProductPriceCostBasis(@TenantId,@BusinessId,p.ProductId,@AsOf) cost
);
GO
