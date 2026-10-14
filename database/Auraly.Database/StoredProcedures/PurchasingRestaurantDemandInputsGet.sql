CREATE PROCEDURE [purchasing].[RestaurantDemandInputsGet]
    @BusinessId UNIQUEIDENTIFIER,
    @WarehouseId UNIQUEIDENTIFIER,
    @ProductIdsJson NVARCHAR(MAX),
    @StartDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId UNIQUEIDENTIFIER=(SELECT TenantId FROM dbo.Businesses
        WHERE BusinessId=@BusinessId AND IsActive=1 AND PointOfSaleMode=N'RestaurantBar');
    IF @TenantId IS NULL OR @StartDate IS NULL OR NOT EXISTS(
        SELECT 1 FROM dbo.Warehouses WHERE BusinessId=@BusinessId
          AND WarehouseId=@WarehouseId AND IsActive=1 AND IsSystem=0)
        THROW 51220,'El sugerido requiere una sede de restaurante, bodega activa y fecha.',1;

    DECLARE @ProductIds TABLE(ProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
    INSERT @ProductIds(ProductId)
    SELECT DISTINCT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@ProductIdsJson)
    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM @ProductIds) OR (SELECT COUNT(*) FROM @ProductIds)>100
        THROW 51220,'Selecciona entre 1 y 100 productos.',1;

    CREATE TABLE #RelatedProducts(ProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
    INSERT #RelatedProducts(ProductId) SELECT ProductId FROM @ProductIds;
    DECLARE @Depth INT=0,@Added INT=1;
    WHILE @Added>0 AND @Depth<32
    BEGIN
        INSERT #RelatedProducts(ProductId)
        SELECT DISTINCT version.ProductId
        FROM #RelatedProducts related
        JOIN catalog.ProductRecipeIngredients component
          ON component.TenantId=@TenantId AND component.IngredientProductId=related.ProductId
        JOIN catalog.ProductRecipeVersions version
          ON version.ProductRecipeVersionId=component.ProductRecipeVersionId
             AND version.TenantId=@TenantId AND version.IsEnabled=1
        WHERE NOT EXISTS(SELECT 1 FROM catalog.ProductRecipeVersions newer
            WHERE newer.TenantId=@TenantId AND newer.ProductId=version.ProductId
              AND newer.[Version]>version.[Version])
          AND NOT EXISTS(SELECT 1 FROM #RelatedProducts existing
              WHERE existing.ProductId=version.ProductId);
        SET @Added=@@ROWCOUNT;
        SET @Depth=@Depth+1;
        IF (SELECT COUNT(*) FROM #RelatedProducts)>500
            THROW 51220,'Acota la selección de ingredientes para calcular el sugerido.',1;
    END;
    IF @Added>0
        THROW 51220,'La cadena de recetas supera 32 niveles.',1;

    SET @Depth=0;
    SET @Added=1;
    WHILE @Added>0 AND @Depth<32
    BEGIN
        INSERT #RelatedProducts(ProductId)
        SELECT DISTINCT component.IngredientProductId
        FROM #RelatedProducts related
        JOIN catalog.ProductRecipeVersions version
          ON version.TenantId=@TenantId AND version.ProductId=related.ProductId
             AND version.IsEnabled=1
        JOIN catalog.ProductRecipeIngredients component
          ON component.ProductRecipeVersionId=version.ProductRecipeVersionId
        WHERE NOT EXISTS(SELECT 1 FROM catalog.ProductRecipeVersions newer
            WHERE newer.TenantId=@TenantId AND newer.ProductId=version.ProductId
              AND newer.[Version]>version.[Version])
          AND NOT EXISTS(SELECT 1 FROM #RelatedProducts existing
              WHERE existing.ProductId=component.IngredientProductId);
        SET @Added=@@ROWCOUNT;
        SET @Depth=@Depth+1;
        IF (SELECT COUNT(*) FROM #RelatedProducts)>500
            THROW 51220,'Acota la selección de ingredientes para calcular el sugerido.',1;
    END;
    IF @Added>0
        THROW 51220,'La cadena de recetas supera 32 niveles.',1;

    SELECT related.ProductId,component.IngredientProductId,component.QuantityPerUnit
    INTO #CurrentRecipes
    FROM #RelatedProducts related
    CROSS APPLY (SELECT TOP(1) version.ProductRecipeVersionId,version.IsEnabled
        FROM catalog.ProductRecipeVersions version
        WHERE version.TenantId=@TenantId AND version.ProductId=related.ProductId
        ORDER BY version.[Version] DESC) latest
    JOIN catalog.ProductRecipeIngredients component
      ON component.ProductRecipeVersionId=latest.ProductRecipeVersionId
    WHERE latest.IsEnabled=1;

    SELECT recipe.ProductId,recipe.IngredientProductId,recipe.QuantityPerUnit
    FROM #CurrentRecipes recipe
    JOIN #RelatedProducts related ON related.ProductId=recipe.ProductId;

    DECLARE @HistoryEnd DATE=CASE WHEN @StartDate>CONVERT(date,SYSUTCDATETIME())
        THEN CONVERT(date,SYSUTCDATETIME()) ELSE @StartDate END;
    DECLARE @FromDate DATE=DATEADD(DAY,-56,@HistoryEnd);
    SELECT fact.ProductId,fact.BusinessLocalDate,SUM(fact.Quantity)
    FROM reporting.SalesReportLineFacts fact
    JOIN #RelatedProducts related ON related.ProductId=fact.ProductId
    WHERE fact.BusinessId=@BusinessId AND fact.WarehouseId=@WarehouseId
      AND fact.BusinessLocalDate>=@FromDate AND fact.BusinessLocalDate<@HistoryEnd
      AND fact.MovementType IN(N'Sale',N'Return')
    GROUP BY fact.ProductId,fact.BusinessLocalDate;

    SELECT DISTINCT fact.BusinessLocalDate
    FROM reporting.SalesReportLineFacts fact
    WHERE fact.BusinessId=@BusinessId AND fact.WarehouseId=@WarehouseId
      AND fact.BusinessLocalDate>=@FromDate AND fact.BusinessLocalDate<@HistoryEnd;

    SELECT product.ProductId,product.ManageStock,COALESCE(balance.QuantityOnHand,0),
           COALESCE(incoming.Quantity,0)
    FROM dbo.Products product
    JOIN (SELECT ProductId FROM #RelatedProducts
          UNION SELECT IngredientProductId FROM #CurrentRecipes recipe
              JOIN #RelatedProducts related ON related.ProductId=recipe.ProductId) nodes
      ON nodes.ProductId=product.ProductId
    LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
      AND balance.WarehouseId=@WarehouseId AND balance.ProductId=product.ProductId
    OUTER APPLY
    (
        SELECT SUM(CASE WHEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity>0
          THEN line.OrderedQuantity-line.ReceivedQuantity-line.CancelledQuantity ELSE 0 END) Quantity
        FROM purchasing.PurchaseOrderLines line
        JOIN purchasing.PurchaseOrders purchaseOrder ON purchaseOrder.PurchaseOrderId=line.PurchaseOrderId
        WHERE purchaseOrder.BusinessId=@BusinessId AND purchaseOrder.WarehouseId=@WarehouseId
          AND purchaseOrder.Status IN(N'Open',N'PartiallyReceived') AND line.ProductId=product.ProductId
    ) incoming
    WHERE product.TenantId=@TenantId;
END;
