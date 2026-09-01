# Segunda rebanada: POS Edge a servidor

Fecha: 2026-07-27

Esta rebanada conecta una factura ya emitida y durable en Auraly POS Edge con
`Auraly.Api`, SQL Server, la verificación fiscal y el motor idempotente de
documentos. No incluye firma XML ni transmisión a la DIAN.

## Flujo ejecutable

```text
SQLite POS Edge
  -> PosEdgeOutboxUploader
  -> POST /api/pos/v1/sales
  -> autenticación del dispositivo enrolado y activo
  -> ReceivePosSaleService
  -> persistencia exacta del payload y snapshot
  -> FiscalSnapshotVerifier + Auraly.Fiscal.Core
  -> DocumentProcessingEngine
  -> líneas + pago + salida de inventario + outbox servidor
  -> recibo durable
  -> estado durable de la outbox SQLite
```

El host real es `src/API/Auraly.Api`. La composición registra implementaciones
SQL Server de autenticación, recepción, recibos y procesamiento. La conexión
`ConnectionStrings:Auraly` es obligatoria; el host no crea el esquema.

## Contrato y autenticación

El endpoint canónico es `POST /api/pos/v1/sales`. Recibe la identidad
organizacional, dispositivo, documento, serie, autorización, prefijo,
consecutivo, fecha de emisión, cliente, líneas, impuestos, totales, pagos, CUFE
y snapshot exacto.

La caja se autentica con:

- `X-Auraly-Device-Id`
- `X-Auraly-Device-Secret`
- `Idempotency-Key`

El secreto se almacena como PBKDF2 con salt en `PosDevices`. Los IDs del cuerpo
deben coincidir con el contexto autenticado: tenant, empresa, sede, bodega y
caja. El dispositivo debe estar enrolado y activo. La acción fue autorizada al
usuario en POS Edge antes de quedar durable en el outbox; el transporte no
vuelve a exigir un permiso técnico por caja. No existe un bypass de
autenticación en producción.

## Persistencia SQL Server

`database/Auraly.Database/Auraly.Database.sqlproj` continúa siendo el único
dueño del esquema. La rebanada usa `dbo` y una sola base:


- `Warehouses`
- `CashRegisters`
- `PosDevices`
- `PosDevicePermissions`
- `FiscalAuthorizations`
- `FiscalSeries`
- `SalesDocuments`
- `SalesDocumentLines`
- `SalesPayments`
- `FiscalSnapshots`
- `DocumentProcessingJobs`
- `DocumentProcessingPayloads`
- `InventoryMovements`
- `ServerOutboxMessages`

Los IDs internos son `uniqueidentifier`; cantidades y dinero son `decimal`; los
instantes son `datetimeoffset`. Hay restricciones para documento, clave de
idempotencia, número fiscal, efectos de inventario, pagos y evento servidor.
El snapshot serializado y su hash SHA-256 se conservan sin corregirlo.

La recepción inicial y un conflicto fiscal son durables. Para una venta
verificada, la adquisición de su `DocumentProcessingJob` y los efectos se
ejecutan en una transacción serializable: líneas, pagos, movimiento, evento,
finalización del mismo trabajo y avance del cursor se confirman juntos. No
existe `DocumentProcessingReceipts`.

## Verificación fiscal

`FiscalSnapshotVerifier` reconstruye `CufeInput` desde el snapshot recibido,
resuelve la clave técnica por empresa, autorización, versión y ambiente, y usa
la misma librería `Auraly.Fiscal.Core` que POS Edge. Compara el CUFE en tiempo
constante.
El CUFE autoritativo es el generado una sola vez por POS Edge. El servidor no
crea un segundo CUFE ni sustituye el recibido: ejecuta la misma función pura
únicamente para comparar integridad antes de firmar. El valor persistido como
`CufeCalculated` es evidencia técnica de esa comparación y no otra numeración
fiscal. Cuando el servidor es el emisor original de una venta completamente
online, el CUFE se calcula una sola vez en el servidor.

Si el CUFE o cualquier dato estructural no coincide:

- el documento queda `FiscalIntegrityConflict`;
- se conservan CUFE recibido, CUFE calculado, payload, hash y detalle;
- no se crean líneas procesadas, movimiento, pago ni evento;
- la factura local no se modifica, elimina ni renumera.

Las claves técnicas no forman parte del request, respuesta, snapshot o logs.
La implementación actual las obtiene de configuración protegida; SaaS puede
reemplazar el proveedor por Key Vault sin cambiar el caso de uso.

## Idempotencia y concurrencia

La identidad principal es `(TenantId, DocumentId, DocumentType)`. También se
valida `IdempotencyKey` y el hash del payload. Una repetición exacta devuelve el
mismo `JobId`. Reutilizar un ID o una clave con otro payload devuelve
conflicto y no altera el documento original.

`DocumentProcessingJobs` registra identidad, secuencia, estado, intentos,
lease, finalización, error y `rowversion`. Dos cargas simultáneas se serializan
en SQL Server. El perdedor de una colisión relee el trabajo durable del ganador.
Las restricciones únicas constituyen una segunda barrera contra
duplicar venta, pago, movimiento o evento.

Una venta fiscal emitida offline siempre produce la salida de inventario al
sincronizarse, aunque el saldo resulte negativo. La rebanada registra el hecho;
no rechaza ni borra retrospectivamente la factura.

## Outbox POS Edge

SQLite conserva el payload completo y soporta:

- `Pending`
- `Uploading`
- `Uploaded`
- `RetryScheduled`
- `FiscalIntegrityConflict`
- `FailedPermanent`

El uploader reclama mensajes con lease, envía con clave idempotente, programa
backoff para errores transitorios y exige un recibo durable antes de completar
el pendiente. Un proceso reiniciado recupera mensajes pendientes o leases
vencidos. La inicialización también actualiza bases creadas por la versión
anterior, agregando sin pérdida el identificador de autorización fiscal y las
columnas de entrega de la outbox.

## Fuera de alcance

- XML UBL, firma y transmisión DIAN.
- Certificado de producción.
- UI POS completa e impresión física.
- Sincronización completa del catálogo.
- Compras, cartera, reportería y módulos adicionales.

