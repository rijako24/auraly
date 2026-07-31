# Evidencia — fundación del motor ordenado de documentos

Fecha: 2026-07-31  
Rama: `feature/auraly-commerce-accounting-engine`

## Alcance conectado

- La recepción verificada de una venta crea `SalesDocuments`, snapshot fiscal, cursor de negocio y trabajo durable en una sola transacción SQL.
- `BusinessProcessingCursors` conserva la última secuencia asignada y completada por `BusinessId`.
- `DocumentProcessingJobs` conserva secuencia, estado, intentos, disponibilidad, lease, error y trazabilidad.
- El procesador solo adquiere `LastCompletedSequence + 1` del negocio.
- Negocios distintos pueden avanzar en paralelo.
- Dos solicitudes válidas concurrentes esperan de forma corta y acotada su turno; no invierten el orden.
- Un trabajo crítico no resuelto bloquea los documentos posteriores del mismo negocio.
- Después de cinco fallos, el trabajo pasa a `NeedsIntervention`; continúa bloqueando y deja de reintentarse automáticamente.
- El worker alojado en `Auraly.Api` recupera trabajos pendientes o leases vencidos. Espera cinco segundos antes de tomar un trabajo nuevo para dar prioridad al procesamiento inmediato de la solicitud.
- La configuración `Auraly:DocumentProcessing:Worker:Enabled` permite ejecutar el mismo host sin el worker en pruebas controladas.
- `DocumentProcessingReceipts` se conserva como recibo de idempotencia durante la transición; no reemplaza la cola ordenada.

## Garantía explícita

No existe una operación automática para saltar un documento crítico. `RetryScheduled` y `NeedsIntervention` no avanzan `LastCompletedSequence`. El siguiente documento solo se procesa cuando el anterior queda completado o una resolución administrativa futura, explícita y auditada, registra una salida válida.

## Pruebas añadidas

- Dos ventas aceptadas reciben secuencias consecutivas y un duplicado no consume una nueva.
- El cursor termina con `LastAssignedSequence == LastCompletedSequence` después del procesamiento correcto.
- Un trabajo crítico simulado como no resuelto deja el documento siguiente recibido pero sin líneas ni movimiento de inventario.
- Al resolver el trabajo anterior, el mismo documento pendiente se procesa una sola vez.
- Un fallo en un negocio no impide que el worker intente otro negocio en el mismo ciclo.

## Evidencia ejecutada

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --maxcpucount:1 --nodeReuse:false
# 0 errores, 0 advertencias

dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release --maxcpucount:1 --nodeReuse:false
# DACPAC generado, 0 errores, 0 advertencias

dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release --maxcpucount:1
# 114 aprobadas

dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --maxcpucount:1 --blame-hang-timeout 10m
# 55 aprobadas; SQL Server real y despliegue del DACPAC
```

La línea base de POS Edge también permaneció aprobada antes del cambio: 9 pruebas.

## Pendiente de la siguiente parte

- Saldo y valoración autoritativa mediante `InventoryBalances` e historial con fotografías anterior/posterior.
- Promedio ponderado permanente y costo de venta congelado.
- Tipos documentales de entrada, conteo, traslado, conversión, devolución y notas.
- Kernel contable, periodos, centros de costo y asientos derivados.
- Operación administrativa segura para resolver `NeedsIntervention`; no se implementará como “saltar documento”.
