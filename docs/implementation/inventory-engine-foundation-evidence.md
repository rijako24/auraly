# Evidencia: sobre genérico y saldo del motor de documentos

**Fecha:** 31 de julio de 2026  
**Rama:** `feature/auraly-commerce-accounting-engine`

## Responsabilidades

Los workers se implementan en código .NET. SQL Server no ejecuta ciclos infinitos ni contiene el motor de negocio; conserva el estado durable necesario para recuperar el trabajo:

- secuencia y cursor por negocio;
- trabajo, estado, intentos y lease;
- payload canónico, versión y hash;
- saldos, kardex y efectos procesados;
- errores y resultados.

`DocumentProcessingHostedService` aloja el worker en la API. El mismo núcleo puede alojarse posteriormente en Azure Functions, WebJob, servicio Windows o contenedor on-premise.

## Sobre documental genérico

`DocumentProcessingPayloads` desacopla el lector de la cola de `SalesDocuments` y `FiscalSnapshots`. Una recepción válida guarda dentro de la misma transacción:

1. documento y snapshot del módulo productor;
2. secuencia de `BusinessId`;
3. `DocumentProcessingJobs`;
4. payload JSON inmutable y su SHA-256.

El script `BackfillDocumentProcessingPayloads.sql` incorpora de forma idempotente los trabajos de ventas existentes. Los siguientes tipos documentales podrán registrar su payload sin adoptar tablas fiscales de ventas.

## Inventario conectado

Para un producto con `ManageStock = 1`, el manejador de venta:

- valida producto, bodega y negocio;
- bloquea o crea `InventoryBalances`;
- descuenta la cantidad dentro de la transacción del documento;
- conserva cantidad anterior y posterior;
- conserva costo promedio anterior y posterior;
- congela el costo reconocido y el cambio de valor;
- registra la secuencia del motor y las fechas de ocurrencia/publicación.

Una venta offline ya emitida puede producir saldo negativo al publicarse. No se elimina ni se renumera.

Para un producto con `ManageStock = 0`, se procesa la factura, línea, impuesto y pago, pero no se crea kardex ni se altera saldo.

## Concurrencia

La recepción reintenta un deadlock SQL como una transacción completa. No interpreta que otro documento concurrente necesariamente haya persistido el documento de la víctima. La prueba de dos cajeros sobre la misma caja demuestra que ambos documentos sobreviven sin colisión de número ni duplicación.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --maxcpucount:1 --nodeReuse:false
# 0 errores, 0 advertencias

dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --maxcpucount:1 --nodeReuse:false
# 0 errores, 0 advertencias

dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
# 57 aprobadas; SQL Server real y DACPAC

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
# 114 aprobadas

dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
# 9 aprobadas
```

La prueba nueva demuestra:

- dos ventas aplicadas en secuencia producen fotografías encadenadas;
- el saldo materializado coincide con el último movimiento;
- repetir un documento no vuelve a descontar;
- un producto no inventariable no crea kardex ni cambia el saldo;
- las líneas comerciales permanecen procesadas en ambos casos.

## Pendiente deliberado

El costo promedio inicial permanece en cero hasta que la rebanada de entradas de mercancía publique el primer valor autoritativo. La siguiente rebanada debe implementar entrada, costo de proveedor y cuenta por pagar utilizando el mismo turno transaccional y demostrar que una venta posterior congela el nuevo costo.
