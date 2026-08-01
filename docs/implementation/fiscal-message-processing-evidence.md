# Procesamiento fiscal dirigido por mensajes

Fecha de evidencia: 2026-08-01  
Rama: `feature/auraly-commerce-accounting-engine`

## Resultado

La generación y el envío fiscal ya no son workers que consultan periódicamente SQL Server. Cada factura comercial procesada publica un trabajo fiscal concreto y cada mensaje identifica exactamente:

- `SignalId` único;
- `BusinessId` usado como sesión del broker;
- `DocumentId` que debe procesarse;
- etapa `Generation` o `Submission`.

No se agregó otra tabla de movimientos. `DocumentProcessingJobs` continúa siendo la fuente durable del motor operativo y `FiscalDocumentProcesses` conserva el estado durable de la rama fiscal derivada.

## Flujo conectado

1. El mensaje operativo de una factura se recibe por la sesión de su `BusinessId`.
2. `DocumentProcessingWorker` procesa el `DocumentProcessingJob` exacto e idempotente.
3. Antes de completar ese mensaje se publica `Generation` para el mismo `DocumentId`.
4. El consumidor fiscal adquiere solamente ese documento, genera y firma sus artefactos.
5. Solo una generación terminada correctamente publica `Submission`.
6. El envío o la consulta de estado DIAN persiste primero su intento y después llama el transporte.
7. Cuando DIAN exige una consulta posterior, se agenda un mensaje único para `NextAttemptAt`.
8. El mensaje se completa únicamente después de persistir el resultado y publicar el paso siguiente requerido.

Si la publicación fiscal falla después de que el movimiento comercial fue procesado, el mensaje operativo no se completa. Su nueva entrega vuelve a ejecutar el motor idempotente y reintenta la publicación fiscal sin duplicar inventario, pagos ni eventos.

## Orden y concurrencia

Las dos colas requieren sesiones. `SessionId` es `BusinessId` y el procesador usa `MaxConcurrentCallsPerSession = 1`:

- dentro de un negocio se conserva ejecución serial;
- negocios diferentes pueden avanzar en paralelo;
- no existe drenaje global de la cola;
- cada mensaje procesa un solo movimiento o documento;
- no existe `PeriodicTimer`, `TOP (1)` ni búsqueda del siguiente documento fiscal.

El motor operativo mantiene la regla más estricta definida en `decision-motor-documental-ordenado-y-efectos-intrinsecos.md`. La rama fiscal no vuelve a aplicar efectos comerciales y no altera el orden ya confirmado de inventario, pagos, contabilidad operativa o acumulados.

## Caídas y concesiones

Una entrega puede repetirse mientras otro intento aún conserva la concesión SQL. En ese caso el mensaje no se descarta:

- el almacén consulta únicamente el mismo `BusinessId` y `DocumentId`;
- calcula el instante durable más tardío entre `NextAttemptAt` y el vencimiento de `LockedAt`;
- agenda un único mensaje para ese instante;
- un estado terminal o no elegible no produce otro mensaje.

Esto es recuperación dirigida por mensajes, no sondeo. La consulta SQL ocurre solo como consecuencia de una entrega del broker.

## Reintento administrativo

`POST /api/commerce/v1/fiscal/documents/{documentId}/retry`:

- vuelve a colocar el estado durable en la etapa permitida;
- publica `Generation` o `Submission` según el estado retornado;
- permite republicar idempotentemente un estado ya pendiente cuando la respuesta anterior pudo perderse;
- nunca cambia número fiscal, CUFE ni snapshot.

## Configuración

Los nombres no secretos están en `src/API/Auraly.Api/appsettings.json`:

```text
Auraly:DocumentProcessing:ServiceBus:QueueName=auraly-document-processing
Auraly:Fiscal:ServiceBus:QueueName=auraly-fiscal-processing
```

El secreto se entrega únicamente por configuración segura:

```text
Auraly:DocumentProcessing:ServiceBus:ConnectionString
```

Ambas colas deben tener sesiones habilitadas. En pruebas no existe bypass productivo: el host `Testing` reemplaza únicamente el publicador por un recolector determinístico del ensamblado de pruebas.

## Evidencia ejecutada

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

Resultado al cerrar esta evidencia:

- solución y DACPAC: `0` errores, `0` advertencias;
- fundación: `129/129`;
- POS Edge Host: `15/15`;
- integración con SQL Server y DACPAC real: `74/74`.

Las pruebas cubren contratos por documento, publicación en reintento, ausencia estructural de timers y scans, exclusión por `BusinessId`, concurrencia de generación, idempotencia de envío, estados DIAN y recuperación después de una concesión activa.

## Límite de la evidencia

La integración productiva usa el SDK real de Azure Service Bus y está conectada a los consumidores reales. En este equipo no se suministró una conexión a un namespace de Azure Service Bus; por eso no se declara ejecutada una prueba contra el broker remoto. Tampoco se declara conectividad real con DIAN sin certificado, software y `TestSetId` válidos. Las pruebas automáticas usan SQL Server real y transportes determinísticos únicamente en proyectos de prueba.
