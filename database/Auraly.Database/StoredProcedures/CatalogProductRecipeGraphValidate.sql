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

    DECLARE @CurrentRecipes TABLE (ProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ProductRecipeVersionId UNIQUEIDENTIFIER NOT NULL);
    INSERT @CurrentRecipes(ProductId, ProductRecipeVersionId)
    SELECT version.ProductId, version.ProductRecipeVersionId
    FROM catalog.ProductRecipeVersions version
    WHERE version.TenantId = @TenantId AND version.IsEnabled = 1
      AND version.[Version] = (SELECT MAX(latest.[Version])
          FROM catalog.ProductRecipeVersions latest
          WHERE latest.TenantId = @TenantId AND latest.ProductId = version.ProductId);

    DECLARE @Invalid BIT = 0;
    ;WITH IngredientTree AS
    (
        SELECT TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(item.[value], '$.IngredientProductId')) ProductId,
               1 Depth
        FROM OPENJSON(@Ingredients) item
        UNION ALL
        SELECT component.IngredientProductId, tree.Depth + 1
        FROM IngredientTree tree
        JOIN @CurrentRecipes currentRecipe ON currentRecipe.ProductId = tree.ProductId
        JOIN catalog.ProductRecipeIngredients component
          ON component.ProductRecipeVersionId = currentRecipe.ProductRecipeVersionId
        WHERE tree.Depth < 32
    )
    SELECT @Invalid = 1
    FROM IngredientTree
    WHERE ProductId = @ProductId OR ProductId IS NULL OR Depth >= 32
    OPTION (MAXRECURSION 32);

    IF @Invalid = 1
        THROW 51024, 'La receta formaría un ciclo o supera el máximo de 32 niveles.', 1;
END;
