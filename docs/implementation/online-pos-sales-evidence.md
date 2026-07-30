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

El BFF admite dos topologías sin duplicar autenticación:

- con gateway único, `NEXT_PUBLIC_API_URL` continúa resolviendo login, administración y Commerce;
- con hosts separados, `NEXT_PUBLIC_API_URL` conserva el API principal y `AURALY_COMMERCE_API_URL` apunta a la raíz de `Auraly.Api`;
- solamente `commerce/*` y `health` se envían al host Commerce dedicado;
- el token `HttpOnly` de la sesión web se reenvía como Bearer y nunca queda expuesto al código cliente.

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
- Pruebas POS/BFF: 23/23.
- TypeScript: correcto.
- Next.js 14.2.21: build correcto; ruta `/pos` generada.
- DACPAC: 0 errores y 0 advertencias.

Las pruebas cubren emisión online, dos cajeros concurrentes en la misma caja, idempotencia, inventario, pago, movimiento de caja, impuestos, historial, recibo exacto, QR, autorización por caja, reimpresión y rechazo SQL de rangos fiscales solapados.

## Validación conectada adicional

Se desplegó el DACPAC real en una base SQL Server aislada y se inició `Auraly.Api` junto con el build de producción del frontend. El recorrido a través del BFF autenticado confirmó:

- negocio `Tienda Auraly Visual`;
- sede `Sede Principal`;
- caja `Caja 01`;
- bodega `Bodega Principal`;
- producto capturado por código con precio de venta `$ 6.800`;
- cantidad `2` y total `$ 16.184`;
- documento Auraly `VTA01-00000002`;
- número fiscal `FEV2`;
- CUFE de 96 caracteres;
- siguiente borrador vacío.

La fila persistida conserva `RegisterId`, `DocumentSeriesId`, `FiscalSeriesId` y `FiscalAuthorizationId`. `DeviceId` es `NULL` y `SourceMode` es `Online`, que es la separación intencional entre caja comercial y dispositivo POS Edge.

## Pendiente de una rebanada posterior

- Aceptación visual interactiva final del flujo por parte del usuario.
- Selección/administración completa de resoluciones y rangos desde el panel.
- Envío real al ambiente de habilitación DIAN, que continúa separado de esta rebanada.
