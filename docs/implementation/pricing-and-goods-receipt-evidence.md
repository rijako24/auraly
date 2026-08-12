# Evidencia: recepción de mercancía y precios/rentabilidad

**Fecha:** 9 de agosto de 2026

## Flujo conectado

- `ProductPrices.Amount` is the exact final public price. POS web and POS Edge never add VAT to it again.
- VAT included in the public price is derived only for the tax breakdown and immutable fiscal snapshot; the derivation never changes the published value.
- Goods receipt keeps the supplier presentation and converts one capture such as `3 boxes x 24` into 72 base units before queuing the document.
- Supplier, purchase presentation and units per presentation use the same contract from Product and Goods Receipt.
- These rules are covered in automated tests and are mandatory even when another user interface consumes the services.
- La recepción busca primero productos asociados al proveedor y permite abrir explícitamente el catálogo general.
- Un producto ajeno al proveedor se asocia únicamente tras confirmación y permiso `catalog.costs.manage`.
- Las líneas calculan cantidad, costo neto, descuento, IVA de compra y total; el impuesto y su tratamiento quedan congelados en el documento.
- Confirmar la entrada recorre API, SQL Server, motor documental, inventario, costo promedio, cuenta por pagar y propuesta de precio.
- La relación producto-proveedor permanece estable. Los cambios de costo negociado crean versiones y mantienen una sola versión activa.
- Producto y recepción preparan precio; solo `Precios y rentabilidad` publica el precio consumido por facturación y POS.
- En rentabilidad, cambiar margen recalcula precio de venta bruto con IVA; cambiar precio bruto recalcula margen sin modificar costo.
- Las flechas arriba/abajo recorren filas; en recepción el foco se mantiene en cantidad y la navegación no altera el valor.

## Pruebas ejecutadas

```text
npm run test:pos
52 aprobadas, 0 fallidas

npx tsc --noEmit
aprobado
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
164 aprobadas, 0 fallidas

dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~GoodsReceiptWorkspaceTests
3 aprobadas, 0 fallidas; SQL Server real y DACPAC desplegado en base aislada


dotnet test ... --filter CatalogVerticalSliceTests|GoodsReceiptWorkspaceTests|GoodsReceiptProcessingTests|PricingVerticalSliceTests
13 aprobadas, 0 fallidas; SQL Server real

npm run build
aprobado; incluye /dashboard/purchasing/goods-receipts y /dashboard/products/pricing

dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
0 errores, 0 advertencias; DACPAC generado

npm run check:encoding
UTF-8 verificado
```

Escenarios cubiertos expresamente:

- selección por proveedor y asociación explícita desde catálogo general;
- múltiples líneas y tarifas de IVA de compra;
- cambio reactivo de cantidad, descuento y costo;
- navegación de cantidades con flechas;
- guardar, recuperar, confirmar y repetir una entrada sin duplicar efectos;
- movimiento de inventario, promedio ponderado, costo proveedor, cuenta por pagar y propuesta;
- versión de costo negociado sin borrar la relación producto-proveedor;
- margen a precio y precio a margen con IVA de venta incluido;
- precio preparado no visible en POS hasta publicación;
- publicación idempotente y sincronización del precio público.

## Validación visual

La automatización del navegador no pudo iniciarse por el ACL de lectura del worktree aislado. No se declaró esa validación como aprobada. La API y el admin se dejan levantados para la prueba manual del flujo.