# Decisión: motor durable, orden de documentos, inventario y contabilidad

**Estado:** vigente y obligatoria  
**Fecha:** 31 de julio de 2026  
**Alcance:** Auraly Commerce Cloud y On-Premise

## 1. Prevalencia

Esta decisión complementa y, ante contradicción, reemplaza las reglas de ejecución descritas en:

- `decision-motor-documentos-ids-y-flujo-pedidos.md`;
- `decision-despachos-verificacion-entradas-salidas-inventario.md`;
- `auditoria-riesgos-y-puerta-implementacion-auraly-commerce.md`.

No cambia las decisiones de identificadores, numeración Auraly, numeración DIAN, snapshots fiscales ni separación modular.

## 2. Decisión ejecutiva

Auraly procesará cada documento definitivo de forma atómica y en orden estricto dentro de su `BusinessId`. Negocios diferentes podrán procesarse en paralelo.

No habrá una cola global que serialice todos los tenants. Tampoco se permitirá ejecutar en paralelo documentos definitivos del mismo negocio que puedan alterar inventario, costo, caja o cartera.

```text
Business A: 000001 -> 000002 -> 000003
Business B: 000001 -> 000002
Business C: 000001 -> 000002 -> 000003 -> 000004
```

Los tres negocios pueden avanzar simultáneamente. Dentro de cada negocio no se salta una secuencia crítica.

## 3. Motivo

El orden cambia resultados aunque la cantidad final coincida. Una entrada procesada antes de una venta puede modificar el costo promedio usado como costo de venta; procesarla después produce otro costo, otra utilidad, otra valoración y otro asiento.

El orden también es esencial para:

- bloquear negativos;
- aplicar conteos sin borrar movimientos posteriores;
- trasladar cantidad y valor atómicamente;
- conservar valor en conversiones;
- limitar devoluciones al saldo no devuelto;
- vincular pagos a obligaciones existentes;
- contabilizar documentos y reversiones en secuencia trazable.

## 4. Qué define el orden

El orden autoritativo es el de confirmación o aceptación en el servidor, no el de creación del borrador ni una fecha editable por el usuario.

Al aceptar un documento definitivo, el servidor asigna un `BusinessProcessingSequence` monotónico y sin ambigüedad. Este número es técnico y distinto de:

- `DocumentId`;
- número operativo Auraly;
- número fiscal DIAN;
- fecha del documento;
- secuencia local de POS Edge.

Documento, líneas, hash, secuencia y trabajo durable se guardan en una sola transacción. Nunca puede existir un documento definitivo sin trabajo ni un trabajo sin su payload inmutable.

## 5. Unidad de trabajo

La unidad de procesamiento es el documento completo, no cada línea.

Una factura de cincuenta productos genera un trabajo. Dentro de una sola transacción, su procesador registra todas las líneas, movimientos de inventario, impuestos, pagos, caja o cartera, vínculos, auditoría, solicitud contable y outbox.

No puede confirmarse parcialmente.

## 6. Estados

Estados comunes mínimos:

- `Draft`: todavía editable y sin posición en la cola;
- `Submitted`: validado y sometido a confirmación;
- `PendingProcessing`: durable y con secuencia asignada;
- `Processing`: posee un lease vigente;
- `Posted`: efectos internos obligatorios confirmados;
- `Rejected`: rechazado antes de producir efectos definitivos;
- `RetryScheduled`: error técnico recuperable;
- `NeedsIntervention`: requiere decisión autorizada;
- `Reversed`: compensado mediante otro documento.

`Posted` o `Confirmed` significan que los efectos internos críticos quedaron en commit. No significan que la DIAN ya aceptó el documento.

## 7. Modelo durable

### `BusinessProcessingCursors`

- `BusinessId`;
- `LastAssignedSequence`;
- `LastCompletedSequence`;
- `LeaseOwner`;
- `LeaseExpiresAt`;
- `RowVersion`.

### `DocumentProcessingJobs`

- `JobId`;
- `BusinessId`;
- `ProcessingSequence`;
- `DocumentId`;
- `DocumentType`;
- `PayloadHash`;
- `Status`;
- `AttemptCount`;
- `AvailableAt`;
- `LeaseOwner`;
- `LeaseExpiresAt`;
- `LastErrorCode`;
- `LastErrorMessage` sanitizado;
- `CreatedAt`;
- `StartedAt`;
- `CompletedAt`;
- `RowVersion`.

Restricciones mínimas:

```text
UNIQUE (BusinessId, ProcessingSequence)
UNIQUE (DocumentId, DocumentType)
```

El registro actual `DocumentProcessingReceipts` evoluciona mediante expansión y contracción. No se pierde su evidencia ni se crean dos motores permanentes.

## 8. Adquisición y recuperación

Un worker adquiere un lease de un negocio y procesa exclusivamente su siguiente trabajo elegible. Otro worker puede adquirir otro negocio.

Reglas:

- el lease tiene propietario y vencimiento explícitos;
- un reinicio recupera leases vencidos;
- los reintentos usan espera creciente acotada;
- un duplicado devuelve el resultado existente;
- el worker nunca reaplica efectos ya confirmados;
- un error del negocio A no detiene B ni C;
- un error crítico de la secuencia A-25 impide ejecutar A-26 hasta resolver A-25.

Los errores de datos y configuración se detectan antes de asignar la secuencia siempre que el documento aún no haya sido emitido. Una factura offline ya emitida se conserva y recibe tratamiento explícito; nunca se descarta ni renumera.

## 9. Inventario y bloqueo

Todo saldo se identifica por:

```text
BusinessId + WarehouseId + ProductId
```

Ningún módulo escribe directamente en el saldo. El procesador carga y bloquea las filas involucradas dentro de la transacción. Para documentos multilínea, traslados y conversiones, adquiere los bloqueos en orden canónico:

```text
WarehouseId ascendente, ProductId ascendente
```

El modelo definitivo incluye:

- `InventoryTransactions` como encabezado de kardex;
- `InventoryTransactionLines` con cantidades y fotografías de costo;
- `InventoryBalances` como proyección autoritativa rápida y conciliable.

Cada línea conserva cantidad anterior y posterior, costo promedio anterior y posterior, costo reconocido y referencia al documento.

## 10. Costo promedio

El método inicial es promedio ponderado permanente por producto y negocio. El costo de venta se congela por línea y nunca se recalcula silenciosamente por cambios posteriores.

Las ventas offline conservan `OccurredAt`, secuencia local y caja. Al llegar reciben `PostedAt` y secuencia del servidor. No se insertan retroactivamente entre movimientos ya contabilizados. El servidor usa la historia de valoración aplicable a la emisión; cualquier diferencia posterior se registra mediante un ajuste explícito.

## 11. Conteos

Un conteo registra `BaseInventorySequence` y la existencia del sistema al iniciar. Al confirmar genera una diferencia, no reemplaza ciegamente el saldo actual:

```text
ajuste = cantidad contada - cantidad del sistema en la base del conteo
```

Entradas, ventas o traslados posteriores al inicio permanecen intactos.

## 12. Traslados, conversiones y reversiones

- Un traslado descuenta origen y aumenta destino en la misma transacción.
- Una conversión consume y produce cantidad y valor atómicamente; la merma es explícita.
- Una anulación crea un documento y movimiento inverso; no elimina kardex.
- Una nota crédito restaura inventario solamente para las líneas cuya disposición física lo determine.

## 13. Carriles derivados

La secuencia crítica del negocio cubre documento, inventario, costo, caja y cartera. En el mismo commit crea trabajos derivados durables:

- contabilidad;
- fiscal;
- outbox y notificaciones;
- reportes/proyecciones.

La contabilidad se ordena por entidad legal. DIAN se procesa por documento y dependencias. Un error fiscal o contable no reaplica inventario, pagos ni cartera.

Una configuración contable faltante genera `AccountingPendingConfiguration`, impide cerrar el periodo y exige corrección, pero no elimina una factura offline ya emitida.

## 14. Despertar y transporte

SQL Server es la fuente de verdad durable para Cloud y On-Premise. Una notificación puede despertar al worker, pero perderla no pierde el trabajo: el worker revisa la cola SQL indexada.

Azure Service Bus podrá incorporarse después como transporte y señal de escala. No reemplazará la idempotencia ni el estado durable en SQL. No se incorporan RabbitMQ, Redis o una cola en memoria como autoridad del MVP.

## 15. Pruebas obligatorias

La implementación debe probar con SQL Server real:

- inventario inicial, entrada, venta y devolución;
- entrada y venta en órdenes opuestos, demostrando su efecto de costo;
- dos cajas vendiendo el mismo producto;
- múltiples negocios en paralelo;
- fallo y recuperación de lease;
- duplicado y respuesta HTTP perdida;
- documento multilínea sin efectos parciales;
- traslado y conversión atómicos;
- conteo con movimientos posteriores;
- venta offline tardía y negativo permitido;
- entrada posterior a negativo;
- reversión;
- nota crédito parcial concurrente;
- trabajo contable pendiente sin reaplicar inventario;
- caída DIAN sin bloquear el negocio;
- ausencia de deadlocks;
- conciliación de cantidad y valor.

## 16. Consecuencia para el código actual

`InventoryMovements` y `DocumentProcessingReceipts` son una base inicial, pero no representan todavía esta decisión completa. Faltan secuencia por negocio, saldo y valoración, fotografías anterior/posterior, lease explícito y trabajo genérico para documentos distintos de ventas.

La siguiente rebanada debe evolucionarlos antes de construir entradas, conteos, traslados y contabilidad sobre supuestos incompletos.
