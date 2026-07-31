# Ejecutar la rebanada de pedidos

## Requisitos

- .NET SDK 8.
- Node y dependencias de `admin` instaladas.
- SQL Server accesible mediante `AURALY_TEST_SQLSERVER` o `localhost\\LOCAL`.
- `SqlPackage.exe` instalado o indicado en `SQLPACKAGE_PATH`.

## Compilación

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

## Pruebas locales

```powershell
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
```

## Pruebas SQL Server

Las pruebas despliegan el DACPAC en una base temporal real y eliminan únicamente esa base al finalizar.

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

Pruebas enfocadas:

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~OrderRecoveryTests|FullyQualifiedName~OrderBatchInvoiceTests|FullyQualifiedName~SourceOrderPosUploadTests"
```

## Frontend

No ejecutar `next build` y `tsc` simultáneamente porque ambos leen o generan `.next`.

```powershell
cd admin
npm run build
npx tsc --noEmit
```

## Flujo manual

1. Iniciar Auraly API con su conexión SQL.
2. Abrir `/dashboard/orders` con un usuario que tenga permisos de pedidos.
3. Filtrar y abrir un pedido creado por el bot.
4. Recuperar uno para llevarlo a `/pos`, o seleccionar varios y facturarlos en lote.
5. En POS Edge, iniciar sesión local, abrir Pedidos y recuperar uno.
6. Emitir la venta, cerrar y abrir la aplicación si se desea comprobar durabilidad.
7. Verificar `OrderInvoiceLinks`, `OrderClaims`, `SalesDocuments` y la outbox local.

## Comportamiento esperado

- El pedido no contiene IVA.
- La venta toma el impuesto vigente del producto al facturar.
- Una recuperación fallida no deja líneas parciales.
- Repetir la misma operación no crea otra factura.
- Un pedido produce como máximo una factura.
- El pedido de otro negocio o un cajero sin permisos no es accesible.
