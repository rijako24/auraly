CREATE PROCEDURE [dbo].[ProductCompositionCostsPrepare]
    @BusinessId UNIQUEIDENTIFIER,
    @ProductIds NVARCHAR(MAX),
    @UserId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7),
    @AllBusinesses BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0 THROW 51020,'La preparación compuesta requiere una transacción.',1;
    DECLARE @TenantId UNIQUEIDENTIFIER=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
    IF @TenantId IS NULL THROW 51020,'La sede no existe.',1;
    DECLARE @Scopes TABLE(BusinessId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT @Scopes SELECT target.BusinessId FROM dbo.Businesses source
      JOIN dbo.Businesses target ON target.TenantId=source.TenantId AND target.IsActive=1
      WHERE source.BusinessId=@BusinessId AND (@AllBusinesses=1 OR target.BusinessId=@BusinessId
        OR (source.SharesProductPrices=1 AND target.SharesProductPrices=1));
    -- Reverse edges are indexed by tenant/component. Work is limited to affected ancestors.
    DECLARE @Affected TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY,Depth INT);
    INSERT @Affected SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER,value),0 FROM OPENJSON(@ProductIds);
    DECLARE @Frontier TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY);
    DECLARE @Next TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT @Frontier SELECT ProductId FROM @Affected;
    DECLARE @WalkDepth INT=0;
    WHILE EXISTS(SELECT 1 FROM @Frontier)
    BEGIN
        SET @WalkDepth+=1;
        IF @WalkDepth>32 THROW 51020,'La composición contiene un ciclo o supera 32 niveles.',1;
        INSERT @Next
          SELECT DISTINCT version.ProductId
          FROM @Frontier source JOIN catalog.ProductCompositionComponents component
            ON component.TenantId=@TenantId AND component.IngredientProductId=source.ProductId
          JOIN catalog.ProductCompositionVersions version ON version.ProductRecipeVersionId=component.ProductRecipeVersionId
            AND version.TenantId=@TenantId AND version.IsEnabled=1
          WHERE version.[Version]=(SELECT MAX(latest.[Version]) FROM catalog.ProductCompositionVersions latest
            WHERE latest.TenantId=@TenantId AND latest.ProductId=version.ProductId)
          UNION
          SELECT link.ChildProductId FROM @Frontier source JOIN dbo.ProductLinks link
            ON link.TenantId=@TenantId AND link.ParentProductId=source.ProductId
              AND link.SharesPrice=1 AND link.IsActive=1;
        UPDATE affected SET Depth=@WalkDepth FROM @Affected affected JOIN @Next nextValue ON nextValue.ProductId=affected.ProductId;
        INSERT @Affected SELECT nextValue.ProductId,@WalkDepth FROM @Next nextValue
          WHERE NOT EXISTS(SELECT 1 FROM @Affected affected WHERE affected.ProductId=nextValue.ProductId);
        DELETE @Frontier;
        INSERT @Frontier SELECT ProductId FROM @Next;
        DELETE @Next;
    END;
    DECLARE @Depth INT=0,@MaxDepth INT=ISNULL((SELECT MAX(Depth) FROM @Affected),0),@Families NVARCHAR(MAX);
    WHILE @Depth<=@MaxDepth
    BEGIN
        DECLARE @Costs TABLE(BusinessId UNIQUEIDENTIFIER,ProductId UNIQUEIDENTIFIER,Cost DECIMAL(19,6) NULL,Missing INT,PRIMARY KEY(BusinessId,ProductId));
        INSERT @Costs
        SELECT scope.BusinessId,v.ProductId,cost.Cost,COALESCE(cost.Missing,0)
        FROM @Affected a JOIN catalog.ProductCompositionVersions v ON v.ProductId=a.ProductId AND v.TenantId=@TenantId
          AND v.IsEnabled=1 AND v.[Version]=(SELECT MAX(latest.[Version]) FROM catalog.ProductCompositionVersions latest
            WHERE latest.TenantId=@TenantId AND latest.ProductId=v.ProductId)
        CROSS JOIN @Scopes scope
        CROSS APPLY(SELECT IngredientProductId,QuantityPerUnit FROM catalog.ProductCompositionComponents i
          WHERE i.ProductRecipeVersionId=v.ProductRecipeVersionId FOR JSON PATH) components(JsonValue)
        CROSS APPLY catalog.ProductCompositionCost(@TenantId,scope.BusinessId,components.JsonValue) cost
        WHERE a.Depth=@Depth AND
          ((@AllBusinesses=1 AND scope.BusinessId=@BusinessId AND a.Depth=0)
           OR EXISTS(SELECT 1 FROM dbo.ProductPrices offered WHERE offered.BusinessId=scope.BusinessId
             AND offered.ProductId=v.ProductId AND offered.IsActive=1)
           OR EXISTS(SELECT 1 FROM dbo.ProductPricePreparations pending WHERE pending.BusinessId=scope.BusinessId
             AND pending.ProductId=v.ProductId AND pending.Status=N'Pending'));
        IF EXISTS(SELECT 1 FROM @Costs WHERE Missing>0 OR Cost IS NULL OR Cost<=0)
          THROW 51020,'Falta costo o impuesto de compra para preparar la composición.',1;
        -- A retry or an unchanged cost does not supersede an authoritative preparation.
        SELECT @Families=(SELECT c.ProductId ParentProductId,c.Cost ParentCost,c.BusinessId TargetBusinessId FROM @Costs c
          WHERE NOT EXISTS(SELECT 1 FROM dbo.ProductPricePreparations currentValue
            WHERE currentValue.BusinessId=c.BusinessId AND currentValue.ProductId=c.ProductId
              AND currentValue.Status=N'Pending'
              AND currentValue.CostBasisAmount=c.Cost)
            AND NOT EXISTS(SELECT 1 FROM dbo.ProductPrices price
              WHERE price.BusinessId=c.BusinessId AND price.ProductId=c.ProductId AND price.IsActive=1
                AND price.CostBasisType=N'Composition' AND price.CostBasisAmount=c.Cost
                AND NOT EXISTS(SELECT 1 FROM dbo.ProductPricePreparations pending
                  WHERE pending.BusinessId=c.BusinessId AND pending.ProductId=c.ProductId AND pending.Status=N'Pending'))
          ORDER BY c.BusinessId,c.ProductId FOR JSON PATH);
        IF @Families<>N'[]'
          EXEC dbo.ProductLinkedCostsPrepare @BusinessId=@BusinessId,@UserId=@UserId,@Now=@Now,
            @FamiliesJson=@Families,@PrepareParents=1,@CompositionPass=1;
        DELETE @Costs;
        SET @Depth+=1;
    END;
END;
GO
