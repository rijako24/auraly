# Evidencia — motor documental, entrada de mercancía y WorkSession

Fecha: 2026-07-31  
Rama: `feature/auraly-commerce-accounting-engine`

## Alcance conectado

- `DocumentProcessingJobs` es la única autoridad durable del movimiento, su orden y su resultado idempotente.
- Cada documento publica un mensaje con `MessageId = JobId` y `SessionId = BusinessId`.
- El consumidor procesa exactamente el movimiento señalado; no escanea, drena ni sondea SQL.
- Un error crítico conserva el turno y bloquea solamente los movimientos posteriores del mismo negocio.
- La entrada de mercancía procesa en una sola transacción el movimiento de inventario, saldo, costo promedio, costo de proveedor, propuesta de revisión de precio, cuenta por pagar, transacción de cartera y evento de outbox.
- Una venta procesada conserva `SoldByUserId`, obtiene una `WorkSession` abierta y registra cada pago una sola vez en `WorkSessionMovements`.
- El CUFE autoritativo continúa siendo el generado una vez en el origen. El servidor ejecuta la misma función pura únicamente como comprobación de integridad y nunca reemplaza, corrige o renumera la factura.

## Pruebas ejecutadas

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
# 59 aprobadas; SQL Server real y despliegue del DACPAC

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
# 124 aprobadas

dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
# 9 aprobadas

dotnet build Auraly.Commerce.sln --configuration Release
# 0 errores, 0 advertencias; incluye Auraly.Database.dacpac

cd admin
npx tsc --noEmit
# correcto

npm run build
# correcto; 47 páginas estáticas, incluida /pos
```

Escenarios SQL comprobados incluyen orden estricto, bloqueo por error crítico, duplicado y concurrencia, una sola salida de inventario, un solo pago, un solo evento de outbox, costo promedio, entrada de mercancía y asociación durable de venta/pago con `WorkSession`.

## Frontera pendiente

Esta evidencia no declara eliminada globalmente la entidad de caja. Todavía existen flujos anteriores basados en `CashRegisters`, `CashSessions`, `CashierShifts` y `RegisterId` en ventas online, enrolamiento, catálogo, pedidos, numeración y APIs de efectivo.

La siguiente migración debe implementar primero el ciclo explícito de `WorkSession`, cierre por usuario y sesión única de autenticación. Después debe retirar las dependencias anteriores de forma vertical, actualizando esquema, API, POS Edge, pedidos, series y pruebas en el mismo cambio. No se hará una eliminación mecánica ni se mantendrán dos modelos permanentes.
