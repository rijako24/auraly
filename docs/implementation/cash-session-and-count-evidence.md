# Evidencia de sesiones y arqueos de caja

Fecha: 2026-07-29.

## Rebanada conectada

La implementación conecta:

1. autenticación y permisos del usuario;
2. sesión de caja y turno por cajero;
3. autorización puntual de supervisor;
4. venta y medios de pago;
5. movimiento de caja;
6. arqueo, entrega y cierre;
7. persistencia SQL Server e idempotencia.

Una caja online admite varios cajeros simultáneos. Comparten la sesión de la
caja, pero cada venta conserva `SoldByUserId` y `CashierShiftId`. La apertura
se serializa por `RegisterId` para evitar sesiones duplicadas sin bloquear
cajas distintas.

## POS

El diálogo de cobro usa el selector visual canónico de Auraly y conserva la
operación con teclado. El valor recibido se agrupa en formato colombiano
mientras se escribe y se convierte al valor decimal usado por la liquidación.

La grilla de venta usa cuatro columnas comerciales:

- Producto, con código, unidad, origen del precio, descuento e IVA;
- Cantidad;
- Precio unitario;
- Total.

El cambio visual no modifica el snapshot fiscal ni los cálculos.

## Pruebas ejecutadas

```text
dotnet build Auraly.Commerce.sln --configuration Release --no-restore
Resultado: 0 errores, 0 advertencias.

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj \
  --configuration Release --no-build
Resultado: 107 aprobadas.

dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj \
  --configuration Release --no-build
Resultado: 4 aprobadas.

dotnet test tests/Auraly.ServerSlice.IntegrationTests/\
Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
Resultado: 32 aprobadas con SQL Server real y despliegue del DACPAC.

cd admin
npx tsc --noEmit
Resultado: aprobado.

npm run test:pos
Resultado: 10 aprobadas.

npm run build
Resultado: aprobado; /pos generado.
```

La prueba de integración reproduce dos aperturas concurrentes sobre la misma
caja, comprueba una sola `CashSession`, turnos distintos y ausencia de
duplicados. La suite completa también cubre reinicio de SQLite, carga durable,
inventario, pagos, conflicto fiscal y autorización.

`npm run lint` no se registra como aprobado: el repositorio todavía no tiene
una configuración ESLint y el comando abre el asistente interactivo de Next.

## Pendiente

La sesión unificada local/online, la búsqueda de clientes, los pedidos y los
diálogos visuales de entrega/cierre siguen siendo rebanadas posteriores. No se
presentan como capacidades terminadas.
