-- Existing recipe history is preserved. Only its latest enabled version classifies a product.
DECLARE @CompositionChanges TABLE(ProductId UNIQUEIDENTIFIER PRIMARY KEY,TenantId UNIQUEIDENTIFIER);
UPDATE product
SET HasIngredients=CASE WHEN currentVersion.IsEnabled=1 AND currentVersion.CompositionKind=N'Ingredients' THEN 1 ELSE 0 END,
    IsCombo=CASE WHEN currentVersion.IsEnabled=1 AND currentVersion.CompositionKind=N'Combo' THEN 1 ELSE 0 END
OUTPUT inserted.ProductId,inserted.TenantId INTO @CompositionChanges
FROM dbo.Products product
OUTER APPLY(SELECT TOP(1) CONVERT(BIT,CASE WHEN version.IsEnabled=1 AND EXISTS(
    SELECT 1 FROM catalog.ProductCompositionComponents member WHERE member.ProductRecipeVersionId=version.ProductRecipeVersionId)
    THEN 1 ELSE 0 END) IsEnabled,version.CompositionKind
  FROM catalog.ProductCompositionVersions version
  WHERE version.TenantId=product.TenantId AND version.ProductId=product.ProductId
  ORDER BY version.[Version] DESC) currentVersion
WHERE product.HasIngredients<>CASE WHEN currentVersion.IsEnabled=1 AND currentVersion.CompositionKind=N'Ingredients' THEN 1 ELSE 0 END
   OR product.IsCombo<>CASE WHEN currentVersion.IsEnabled=1 AND currentVersion.CompositionKind=N'Combo' THEN 1 ELSE 0 END;
DECLARE @CompositionCursors TABLE(BusinessId UNIQUEIDENTIFIER,CursorValue BIGINT);
INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
  OUTPUT inserted.BusinessId,inserted.CatalogChangeId INTO @CompositionCursors
  SELECT business.BusinessId,changed.ProductId,N'Upsert',SYSDATETIMEOFFSET()
  FROM @CompositionChanges changed JOIN dbo.Businesses business
    ON business.TenantId=changed.TenantId AND business.IsActive=1;
INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
  SELECT NEWID(),BusinessId,N'Catalog',MAX(CursorValue),SYSDATETIMEOFFSET()
  FROM @CompositionCursors GROUP BY BusinessId;
