CREATE PROCEDURE [catalog].[ProductCompositionSave]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @ExpectedVersion INT,
    @Ingredients NVARCHAR(MAX),
    @VersionId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @Now DATETIMEOFFSET(7),
    @CompositionKind NVARCHAR(16) = N'Ingredients',
    @EmitCatalogChanges BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT=0 THROW 51024,'La composición requiere la transacción del producto.',1;
    IF @CompositionKind NOT IN(N'Ingredients',N'Combo') OR @CompositionKind IS NULL
        THROW 51024,'La modalidad de composición no es válida.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE TenantId=@TenantId AND BusinessId=@BusinessId)
        THROW 51021,'La sede no pertenece a este negocio.',1;
    EXEC catalog.ProductRecipeGraphValidate @TenantId,@ProductId,@Ingredients;
    IF NOT EXISTS(SELECT 1 FROM dbo.Products WITH(UPDLOCK,HOLDLOCK)
        WHERE TenantId=@TenantId AND ProductId=@ProductId AND IsActive=1 AND IsGenericProduct=0
          AND (IsIngredient=0 OR (@CompositionKind=N'Ingredients' AND ManageStock=1)))
        THROW 51024,'El producto no admite esa composición.',1;
    DECLARE @CurrentVersion INT=ISNULL((SELECT MAX([Version]) FROM catalog.ProductCompositionVersions WITH(UPDLOCK,HOLDLOCK)
        WHERE TenantId=@TenantId AND ProductId=@ProductId),0);
    IF @CurrentVersion<>@ExpectedVersion THROW 51024,'La composición cambió. Actualiza antes de guardar.',1;
    DECLARE @Items TABLE(LineNumber INT PRIMARY KEY,ProductId UNIQUEIDENTIFIER,Quantity DECIMAL(19,6));
    INSERT @Items
      SELECT TRY_CONVERT(INT,j.[key])+1,TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(j.value,'$.IngredientProductId')),
        TRY_CONVERT(DECIMAL(19,6),JSON_VALUE(j.value,'$.QuantityPerUnit')) FROM OPENJSON(@Ingredients) j;
    DECLARE @Count INT=(SELECT COUNT(*) FROM @Items);
    IF @Count>100 OR EXISTS(SELECT ProductId FROM @Items GROUP BY ProductId HAVING COUNT(*)>1)
        THROW 51024,'La composición supera 100 componentes o contiene duplicados.',1;
    IF EXISTS(SELECT 1 FROM @Items i LEFT JOIN dbo.Products p WITH(UPDLOCK,HOLDLOCK)
        ON p.TenantId=@TenantId AND p.ProductId=i.ProductId
        WHERE p.ProductId IS NULL OR p.ProductId=@ProductId OR p.IsActive=0 OR p.IsGenericProduct=1
          OR p.IsIngredient<>CASE WHEN @CompositionKind=N'Ingredients' THEN 1 ELSE 0 END
          OR (@CompositionKind=N'Combo' AND p.IsCombo=1)
          OR p.ManageStock=0 OR p.BaseUnitCode IS NULL OR i.Quantity IS NULL OR i.Quantity<=0
          OR EXISTS(SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=@TenantId
            AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1))
        THROW 51024,'Selecciona componentes activos, de la clase elegida y con inventario propio.',1;
    IF @CurrentVersion>0 OR @Count>0
    BEGIN
        INSERT catalog.ProductCompositionVersions(ProductRecipeVersionId,TenantId,ProductId,[Version],IsEnabled,
            CompositionKind,PublishedAt,PublishedByUserId)
          VALUES(@VersionId,@TenantId,@ProductId,@CurrentVersion+1,IIF(@Count>0,1,0),@CompositionKind,@Now,@UserId);
        INSERT catalog.ProductCompositionComponents(ProductRecipeVersionId,LineNumber,TenantId,IngredientProductId,
            QuantityPerUnit,BaseUnitCodeSnapshot,ProductCodeSnapshot,ProductNameSnapshot)
          SELECT @VersionId,i.LineNumber,@TenantId,i.ProductId,i.Quantity,p.BaseUnitCode,p.ProductCode,p.Name
          FROM @Items i JOIN dbo.Products p ON p.TenantId=@TenantId AND p.ProductId=i.ProductId;
    END;
    UPDATE dbo.Products SET HasIngredients=IIF(@Count>0 AND @CompositionKind=N'Ingredients',1,0),
        IsCombo=IIF(@Count>0 AND @CompositionKind=N'Combo',1,0),
        UpdatedAt=@Now,UpdatedByUserId=@UserId
      WHERE TenantId=@TenantId AND ProductId=@ProductId;
    DECLARE @Products NVARCHAR(MAX)=CONCAT(N'["',CONVERT(NVARCHAR(36),@ProductId),N'"]');
    EXEC catalog.ProductCompositionValidate @TenantId,@Products;
    IF @EmitCatalogChanges=1
    BEGIN
        EXEC dbo.ProductCompositionCostsPrepare @BusinessId,@Products,@UserId,@Now,@AllBusinesses=1;
        DECLARE @Changes TABLE(BusinessId UNIQUEIDENTIFIER,CursorValue BIGINT);
        INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
          OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @Changes
          SELECT BusinessId,@ProductId,N'Upsert',@Now FROM dbo.Businesses WHERE TenantId=@TenantId AND IsActive=1;
        INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
          SELECT NEWID(),BusinessId,N'Catalog',CursorValue,@Now FROM @Changes;
    END;
END;
GO
