CREATE FUNCTION [inventory].[SaleCompositionSnapshot]
(
    @TenantId UNIQUEIDENTIFIER,
    @DocumentId UNIQUEIDENTIFIER,
    @PayloadJson NVARCHAR(MAX)
)
RETURNS TABLE
AS
RETURN
(
    SELECT input.LineNumber,product.ManageStock,
           CASE WHEN restaurant.DocumentId IS NOT NULL AND product.ManageStock=0
                THEN restaurant.ProductRecipeVersionId
                WHEN latest.IsEnabled=1 THEN latest.ProductRecipeVersionId END ProductRecipeVersionId
    FROM OPENJSON(@PayloadJson,'$.lines') WITH
        (LineNumber INT '$.lineNumber',ProductId UNIQUEIDENTIFIER '$.productId') input
    JOIN dbo.Products product ON product.TenantId=@TenantId AND product.ProductId=input.ProductId
    LEFT JOIN sales.RestaurantCheckoutLines restaurant
      ON restaurant.DocumentId=@DocumentId AND restaurant.LineNumber=input.LineNumber
     AND restaurant.TenantId=@TenantId
    OUTER APPLY
    (
        SELECT TOP(1) versionRow.ProductRecipeVersionId,versionRow.IsEnabled
        FROM catalog.ProductCompositionVersions versionRow
        WHERE versionRow.TenantId=@TenantId AND versionRow.ProductId=input.ProductId
        ORDER BY versionRow.[Version] DESC
    ) latest
    WHERE product.HasIngredients=1 OR product.IsCombo=1
       OR restaurant.ProductRecipeVersionId IS NOT NULL
);
