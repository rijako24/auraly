# Evidencia: Entradas de mercanc?a

Fecha de ejecuci?n: 2026-08-02

## Resultados

- `dotnet build Auraly.Commerce.sln --configuration Release`: correcto, 0 errores, 0 advertencias.
- `dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release`: 147/147.
- `dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release`: 87/87 sobre SQL Server real.
- Pruebas nuevas `GoodsReceiptWorkspaceTests`: 2/2.
- `dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release`: 15/15.
- `npx tsc --noEmit`: correcto.
- `npm run build`: correcto; ruta `/dashboard/purchasing/goods-receipts` generada.
- `npm run test:pos`: 25/25.

## Escenarios nuevos demostrados

- Guardado f?sico del borrador en SQL Server.
- Recuperaci?n tras una nueva solicitud.
- Totales recalculados en servidor con `decimal`.
- Listado paginado que combina borradores y confirmadas.
- Actualizaci?n con `rowversion`.
- Rechazo de edici?n obsoleta.
- Rechazo de confirmaci?n obsoleta.
- Eliminaci?n controlada del borrador.
- Opciones de bodega y proveedor limitadas al Business autenticado.
- Productos limitados al proveedor y Business.
- Permiso de creaci?n validado en backend.
- Confirmaci?n con n?mero `EMC`.
- Eliminaci?n transaccional del borrador al crear el documento confirmado.
- Preservaci?n de las 85 pruebas previas de integraci?n.

## Incidencia encontrada y corregida

El primer despliegue del DACPAC detect? una variable de seed repetida en el mismo batch. Se cambi? a `@PurchasingPermissions`; el DACPAC volvi? a compilar y las suites posteriores desplegaron correctamente bases temporales reales.

## Alcance pendiente expl?cito

No se marca como terminada la devoluci?n/reversi?n de compra. Ser? una rebanada documental compensatoria, no una edici?n ni borrado de la entrada confirmada.
