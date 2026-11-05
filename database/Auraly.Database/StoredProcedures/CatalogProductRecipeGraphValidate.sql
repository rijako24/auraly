CREATE PROCEDURE [catalog].[ProductRecipeGraphValidate]
    @TenantId UNIQUEIDENTIFIER,
    @ProductId UNIQUEIDENTIFIER,
    @Ingredients NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantExists BIT;
    SELECT @TenantExists = 1 FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK)
    WHERE TenantId = @TenantId;
    IF @TenantExists IS NULL
        THROW 51024, 'La receta pertenece a otra organización.', 1;

    IF EXISTS(SELECT 1 FROM OPENJSON(@Ingredients) input
      WHERE TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(input.value,'$.IngredientProductId')) IS NULL)
      THROW 51024,'La composición contiene un producto inválido.',1;
    DECLARE @Frontier TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY);
    DECLARE @Next TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT @Frontier SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(input.value,'$.IngredientProductId'))
      FROM OPENJSON(@Ingredients) input;
    DECLARE @Depth INT=0;
    WHILE EXISTS(SELECT 1 FROM @Frontier)
    BEGIN
        SET @Depth+=1;
        IF @Depth>=32 OR EXISTS(SELECT 1 FROM @Frontier WHERE ProductId=@ProductId)
          THROW 51024,'La receta formaría un ciclo o supera el máximo de 32 niveles.',1;
        INSERT @Next SELECT DISTINCT component.IngredientProductId
          FROM @Frontier source JOIN catalog.ProductCompositionVersions version
            ON version.TenantId=@TenantId AND version.ProductId=source.ProductId AND version.IsEnabled=1
          JOIN catalog.ProductCompositionComponents component ON component.ProductRecipeVersionId=version.ProductRecipeVersionId
          WHERE version.[Version]=(SELECT MAX(latest.[Version]) FROM catalog.ProductCompositionVersions latest
            WHERE latest.TenantId=@TenantId AND latest.ProductId=version.ProductId)
          UNION
          SELECT link.ParentProductId FROM @Frontier source JOIN dbo.ProductLinks link
            ON link.TenantId=@TenantId AND link.ChildProductId=source.ProductId
              AND link.SharesPrice=1 AND link.IsActive=1;
        DELETE @Frontier;
        INSERT @Frontier SELECT ProductId FROM @Next;
        DELETE @Next;
    END;
END;
