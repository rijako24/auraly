# Rebanada: devoluciones de compra

## Decisión

Una devolución de compra es un documento compensatorio `PurchaseReturn` con numeración Auraly `DCP`. Nunca edita ni elimina la entrada `GoodsReceipt` confirmada. Una reversión total de la entrada se expresa devolviendo todas las cantidades aún disponibles; no existe un segundo agregado que duplique esa responsabilidad.

La devolución se captura y confirma en línea. El servidor toma descripción, costo, impuesto, tratamiento tributario y valores monetarios desde las líneas inmutables de la entrada original. El cliente solo indica línea original, cantidad, motivo y observación.

## Flujo conectado

```text
Admin web
  -> POST /api/commerce/v1/purchase-returns/confirm
  -> PurchaseReturns + PurchaseReturnLines
  -> DocumentProcessingJobs (secuencia estricta por Business)
  -> RabbitMQ / publicador configurado
  -> SqlPurchaseReturnDocumentHandler
       -> salida de InventoryMovements al costo original
       -> AccountingPostingJobs
       -> ServerOutboxMessages
  -> PurchaseReturns.Processed
  -> AccountingProcessingCoordinator / SqlAccountingPostingProcessor
       -> crédito sobre Payables
       -> excedente en SupplierCredits
       -> PurchaseReturnFinancialEffects + asiento
```

El inventario, el trabajo contable y el outbox del handler se guardan en la transacción documental. CxP, saldo a favor, efecto financiero y asiento se guardan juntos en la transacción del motor contable; el handler documental no escribe saldos financieros. Un error de existencia o valorización revierte la transacción documental y conserva el documento pendiente/reintentable según la política del motor; no se salta el orden del negocio.

## Cantidades y redondeo

- La suma de devoluciones aceptadas de una línea nunca supera lo recibido.
- La comprobación usa aislamiento serializable y bloqueos de actualización.
- Una devolución parcial prorratea los valores de la línea original.
- La última devolución toma el remanente monetario exacto, evitando residuos por redondeo.
- El costo de inventario reconocido es el costo de adquisición de la entrada: neto más IVA capitalizable cuando aplique.
- Productos sin manejo de inventario no generan movimiento físico; su contrapartida permanece como gasto de compra.

## Cuentas por pagar

1. Se aplica la devolución al saldo pendiente de la CxP original hasta agotarlo.
2. Si la devolución excede ese saldo o la entrada no tenía CxP, se crea un saldo a favor en `SupplierCredits`.
3. `PurchaseReturnFinancialEffects` conserva cuánto se aplicó a cada resultado.
4. No se modifica `OriginalAmount`; la historia se explica mediante una transacción `Credit`.

## Contabilidad

La devolución invierte los componentes reales de la entrada:

- débito a cuentas por pagar por el valor aplicado;
- débito a saldos a favor con proveedores por el excedente;
- crédito a inventario por bienes controlados;
- crédito a compras no inventariables;
- crédito a IVA descontable.

El asiento usa el mismo centro de costo predeterminado y las mismas reglas de periodo y mapeo del motor contable existente. Si falta configuración, queda en `AccountingPendingConfiguration`; el documento comercial e inventario no se pierden.

## Seguridad

Permisos backend:

- `purchasing.purchase-returns.read`
- `purchasing.purchase-returns.create`
- `purchasing.purchase-returns.confirm`

Business y Tenant se obtienen del usuario autenticado y se verifican contra la entrada. La interfaz solo mejora la experiencia; el servidor vuelve a validar todo.

## Deliberadamente pendiente

- Los cruces de `SupplierCredits` implementados y sus límites se rigen por [comprobantes abiertos](accounting-open-vouchers-design.md); no existe otro aplicador en devoluciones.
- Revertir una devolución ya procesada mediante un segundo documento compensatorio.
- Devoluciones offline.
- Integración de una nota del proveedor como artefacto fiscal recibido.

## Preparación para trazabilidad de origen — 2026-10-23

La atribución FIFO aprobada pertenece al ledger y al diseño de cargos posteriores
de [recepciones](goods-receipts-design.md). Este prerrequisito agrupa el camino
existente de devoluciones de compra; todavía no crea asignaciones por origen ni
habilita revisiones posteriores de costo.

`SqlPurchaseReturnStore` obtiene en una lectura el conjunto solicitado de líneas
y devoluciones previas, conservando el bloqueo de la recepción y sus rangos,
orden original, validación de faltantes y `PurchaseReturnCalculator` para cantidades
y residuos. Cabecera y partidas se guardan juntas con tipos y escalas del esquema.
Consecutivo y secuencia delegan al `SqlOperationalDocumentAllocator` existente,
sin aprovisionar una serie ausente. Se conservan los errores de serie ausente o
agotada. La aceptación mantiene una salida de rollback y conserva la causa SQL.

`SqlPurchaseReturnDocumentHandler` envía las partidas ordenadas a
`SqlInventoryLedgerWriter.PostBatchAsync`: una lectura y una escritura del ledger
por documento, con el mismo costo original y valoración `SpecifiedCostIssue`.
Inventario, CxP, contabilidad, reporting y outbox mantienen los motores y tablas
anteriores. No hay otra fórmula de promedio, balance editable o job de devolución.

La captura web conserva el ID y la fecha del primer envío al reintentar, congela
los campos durante la petición y presenta el error del contrato. El acuse dice
«confirmada y enviada al motor». Un replay ya `Processed` devuelve su aceptación
autoritativa sin volver a publicar; uno todavía `Accepted` conserva la señal
necesaria para terminar el trabajo existente.

La regresión reprodujo 48/124 comandos para una/veinte partidas. Con el lote hizo
46/46, manteniendo cantidades, valor de inventario, CxP, límite operacional de dos
segundos y replay sin otra escritura física. Pasaron primero siete casos de
devolución y después 40 de devoluciones, recepción y ledger. La prueba adicional
corrompe la última partida de veinte después de preparar la cabecera: falla el
CHECK original, no queda cabecera, partida ni job, y el mismo comando termina una
sola vez al repetirse sin el fallo. Tres casos de señales prueban aceptación,
replay pendiente y replay terminado; este último falló antes y pasó con el guard.
La comprobación real de navegador pasó dos veces, la última con el aislamiento
de contexto de la vista: una respuesta 503 simulada después de que la API/SQL
aceptara el documento conserva la captura; el segundo envío lleva el mismo ID,
fecha y cuerpo, y recibe el replay de esa devolución. Se verifican diez unidades
recibidas, cuatro devueltas, seis disponibles, CxP original de COP 90.000 con
saldo de COP 54.000, asiento balanceado y gasto asociado intacto en COP 5.000.
La última ejecución duró 1,3 minutos; no es una medición de latencia del motor.

Auditoría posterior de este incremento, 2026-10-24: se revisaron store, handler,
servicio, contratos consumidos, página, hook y pruebas. La aceptación conserva
hash, bloqueo serializable, alcance y replay; numeración y secuencia reutilizan
el allocator existente. El ledger recibe un solo lote ordenado, sin consultas
por partida ni otra fórmula de valoración. Saldos financieros y asiento siguen
en el único processor contable; la revisión corrigió arriba la descripción
antigua que atribuía esos saldos al handler documental. No se creó tabla, motor,
cola, catálogo, estado ni reintento adicional. La captura se reinicia al cambiar
de tenant/sede y sus consultas se identifican con ese mismo contexto; el listado
tiene un propietario y refresco tras aceptación para leer el procesamiento
asíncrono, sin polling. Los campos y el cierre se bloquean durante el envío.
La prueba de rollback conserva el error SQL original y demuestra aceptación
posterior única. El cambio de lote no exige esquema nuevo y su reversión usa
las mismas tablas; no hay backfill ni reproceso histórico. Pasaron las 872 pruebas
Foundation, build de API Release, build web y lint (cero errores, nueve avisos
preexistentes). Evidencia local: `auraly-purchase-return-batch-audit-green`,
`auraly-purchase-return-foundation`, `auraly-purchase-return-real-browser` y
`auraly-purchase-return-scope-build` en TEMP. Esto cierra este incremento;
la trazabilidad FIFO y los cargos posteriores continúan pendientes.
