# Rebanada de venta POS online

Fecha de validación: 2026-07-30.

## Decisión cerrada

Una venta online siempre pertenece a una caja real.

- `RegisterId` es obligatorio.
- La caja determina la sede, la bodega, la serie operativa Auraly y la serie fiscal DIAN.
- `DeviceId` es `NULL` únicamente porque el navegador online no representa un equipo POS Edge enrolado.
- `SourceMode` distingue `Online` de `PosEdge`.
- Varios usuarios online pueden usar la misma caja. Los consecutivos se asignan dentro de la transacción de emisión, no al abrir el borrador.
- Una caja enrolada para emisión offline no se ofrece como caja online, porque su rango fiscal está reservado al dispositivo.
- Los rangos fiscales activos del mismo negocio, autorización, tipo documental y prefijo no se pueden solapar. SQL Server lo garantiza mediante `TR_FiscalSeries_PreventOverlappingActiveRanges`.

## Flujo conectado

1. Sin token local de POS Edge, `/pos` usa la sesión web autenticada.
2. En el primer acceso solicita negocio, sede y caja; la bodega se deriva de la caja.
3. La selección recordada contiene solamente `RegisterId` y siempre se revalida en el servidor.
4. `OnlinePosClient` consume los mismos casos de uso visuales que `PosEdgeClient`.
5. El borrador activo, sus líneas, cliente, descuentos y ventas en espera se persisten en SQL Server.
6. Al cobrar, el servidor toma ambos consecutivos de forma atómica, congela el snapshot fiscal, calcula CUFE/QR y procesa venta, inventario, pago, caja, impuestos y outbox una sola vez.
7. La clave idempotente del checkout es estable por borrador y el BFF la conserva.
8. La respuesta contiene el siguiente borrador vacío; la pantalla queda lista para otra venta.
9. La representación de 80 mm se construye desde el snapshot recibido del servidor. La reimpresión busca el documento por numeración y usa el mismo snapshot.
10. El QR se entrega como SVG autenticado y validado contra negocio, sede y caja.

## Conectividad

El modo online no sondea el servidor periódicamente. Usa las operaciones reales y los eventos `online`/`offline` del navegador. El intervalo de salud de tres segundos existe solo para comprobar el proceso local de una instalación POS Edge.

El service worker conserva únicamente el shell instalable. Las rutas `/api/` nunca se almacenan en caché, por lo que una venta online no puede aparentar una respuesta vigente usando datos antiguos.

## Evidencia ejecutada

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --no-restore
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-restore
$env:AURALY_TEST_SQLSERVER='.\TEST'
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-restore
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
cd admin
npm run test:pos
npx --no-install tsc --noEmit
npm run build
```

Resultados:

- Solución .NET: 0 errores y 0 advertencias.
- Fundación: 109/109.
- Integración con SQL Server real y DACPAC desplegado: 42/42.
- Pruebas POS/BFF: 19/19.
- TypeScript: correcto.
- Next.js 14.2.21: build correcto; ruta `/pos` generada.
- DACPAC: 0 errores y 0 advertencias.

Las pruebas cubren emisión online, dos cajeros concurrentes en la misma caja, idempotencia, inventario, pago, movimiento de caja, impuestos, historial, recibo exacto, QR, autorización por caja, reimpresión y rechazo SQL de rangos fiscales solapados.

## Pendiente de una rebanada posterior

- Prueba visual automatizada de navegador con credenciales de un entorno desplegado.
- Selección/administración completa de resoluciones y rangos desde el panel.
- Envío real al ambiente de habilitación DIAN, que continúa separado de esta rebanada.
