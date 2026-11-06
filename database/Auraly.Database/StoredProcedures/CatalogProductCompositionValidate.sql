CREATE PROCEDURE [catalog].[ProductCompositionValidate]
    @TenantId UNIQUEIDENTIFIER,
    @ProductIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Products TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT @Products SELECT DISTINCT TRY_CONVERT(UNIQUEIDENTIFIER,value) FROM OPENJSON(@ProductIds);
    -- Accepted sales own their inventory mode until the canonical job completes.
    -- A new recipe version is allowed; its predecessors remain immutable.
    IF EXISTS(
      SELECT 1
      FROM dbo.Businesses businessValue
      JOIN dbo.DocumentProcessingJobs job ON job.BusinessId=businessValue.BusinessId AND job.Status<>N'Completed'
        AND job.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
      JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=job.DocumentId AND payload.DocumentType=job.DocumentType
      CROSS APPLY OPENJSON(payload.InventoryCompositionJson) WITH
        (LineNumber INT,ManageStock BIT,ProductRecipeVersionId UNIQUEIDENTIFIER) frozen
      JOIN catalog.ProductCompositionVersions versionRow
        ON versionRow.TenantId=@TenantId AND versionRow.ProductRecipeVersionId=frozen.ProductRecipeVersionId
      JOIN dbo.Products product ON product.TenantId=@TenantId
      JOIN @Products requested ON requested.ProductId=product.ProductId
      LEFT JOIN catalog.ProductCompositionComponents member
        ON frozen.ManageStock=0 AND member.TenantId=@TenantId
       AND member.ProductRecipeVersionId=frozen.ProductRecipeVersionId AND member.IngredientProductId=product.ProductId
      LEFT JOIN dbo.ProductLinks inventoryLink
        ON inventoryLink.TenantId=@TenantId AND inventoryLink.ChildProductId=product.ProductId
       AND inventoryLink.IsActive=1 AND inventoryLink.SharesInventory=1
      WHERE businessValue.TenantId=@TenantId
        AND ((versionRow.ProductId=product.ProductId AND (product.ManageStock<>frozen.ManageStock
              OR (frozen.ManageStock=1 AND inventoryLink.ProductLinkId IS NOT NULL)))
          OR (member.IngredientProductId IS NOT NULL
              AND (product.ManageStock=0 OR product.BaseUnitCode<>member.BaseUnitCodeSnapshot
                  OR inventoryLink.ProductLinkId IS NOT NULL))))
      THROW 51024,'Una venta aceptada pendiente requiere conservar la modalidad y las unidades de su inventario.',1;
    IF EXISTS(SELECT 1 FROM @Products requested JOIN dbo.Products product
      ON product.ProductId=requested.ProductId AND product.TenantId=@TenantId
      WHERE (product.HasIngredients=1 OR product.IsCombo=1) AND
        (product.IsGenericProduct=1 OR (product.IsIngredient=1 AND product.ManageStock=0) OR EXISTS(SELECT 1 FROM dbo.ProductLinks link
          WHERE link.TenantId=@TenantId AND link.ChildProductId=product.ProductId
            AND link.IsActive=1 AND (link.SharesPrice=1 OR link.SharesInventory=1))))
      THROW 51024,'Una composición requiere costo e inventario propios y no puede ser genérica.',1;
    IF EXISTS(SELECT 1 FROM @Products requested JOIN dbo.Products product
      ON product.ProductId=requested.ProductId AND product.TenantId=@TenantId
      JOIN catalog.ProductCompositionComponents member
        ON member.TenantId=@TenantId AND member.IngredientProductId=product.ProductId
      JOIN catalog.ProductCompositionVersions version
        ON version.TenantId=@TenantId AND version.ProductRecipeVersionId=member.ProductRecipeVersionId
        AND version.IsEnabled=1
      WHERE version.[Version]=(SELECT MAX(latest.[Version]) FROM catalog.ProductCompositionVersions latest
          WHERE latest.TenantId=@TenantId AND latest.ProductId=version.ProductId)
        AND (product.ManageStock=0 OR product.IsGenericProduct=1 OR product.IsActive=0
          OR product.BaseUnitCode<>member.BaseUnitCodeSnapshot
          OR product.IsIngredient<>IIF(version.CompositionKind=N'Ingredients',1,0)
          OR (version.CompositionKind=N'Combo' AND product.IsCombo=1)
          OR EXISTS(SELECT 1 FROM dbo.ProductLinks link WHERE link.TenantId=@TenantId
            AND link.ChildProductId=product.ProductId AND link.SharesInventory=1 AND link.IsActive=1)))
      THROW 51024,'El producto pertenece a una composición vigente y debe conservar su clase e inventario propio.',1;
    IF EXISTS(SELECT 1 FROM OPENJSON(@ProductIds) input
      JOIN dbo.Products product WITH(UPDLOCK,HOLDLOCK)
        ON product.ProductId=TRY_CONVERT(UNIQUEIDENTIFIER,input.value) AND product.TenantId=@TenantId
      LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId AND tax.TenantId=@TenantId
      WHERE product.IsCombo=1 AND (tax.TaxProfileId IS NULL OR tax.IsActive=0 OR tax.Rate<>0))
      THROW 51024,'El combo requiere un perfil de impuesto de venta activo con tarifa cero.',1;
END;
GO
