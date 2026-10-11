-- Las incorporaciones sin ruta nunca debieron exigir una comanda.
-- Conserva la modalidad de inventario y la receta vigentes al migrarlas.
UPDATE addition
SET Status=N'Direct',
    ManageStockAtSend=product.ManageStock,
    ProductRecipeVersionId=CASE WHEN product.ManageStock=0 THEN recipe.ProductRecipeVersionId ELSE NULL END
FROM sales.RestaurantAccountAdditions addition
JOIN dbo.Products product
  ON product.TenantId=addition.TenantId AND product.ProductId=addition.ProductId
OUTER APPLY (
    SELECT TOP(1) versionRow.ProductRecipeVersionId
    FROM catalog.ProductRecipeVersions versionRow
    WHERE versionRow.TenantId=addition.TenantId
      AND versionRow.ProductId=addition.ProductId
      AND versionRow.IsEnabled=1
    ORDER BY versionRow.[Version] DESC
) recipe
WHERE addition.Status=N'Pending'
  AND NOT EXISTS (
      SELECT 1 FROM sales.RestaurantProductRoutes route
      WHERE route.BusinessId=addition.BusinessId
        AND route.ProductId=addition.ProductId
  );
