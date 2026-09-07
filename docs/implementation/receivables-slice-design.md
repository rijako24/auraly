# Rebanada vertical de cuentas por cobrar

Fecha: 2026-08-03
Prevalencia: la distribución vigente de efectos se rige por
`../decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.

## Decisión

La cuenta por cobrar nace únicamente cuando el motor financiero-contable procesa
la fuente durable de una venta a crédito verificada. El crédito no se representa
como un medio de pago ficticio: la suma de pagos reales más el valor financiado
debe ser igual al total de la venta. Esta cartera comercial funciona aunque el
tenant no active el libro mayor.

El recaudo es un documento financiero durable (`ReceivablePayment`). Su aceptación
crea una única fuente y trabajo financiero-contable. El único
`SqlAccountingPostingProcessor` aplica en una transacción el pago, asignaciones,
saldo de la obligación y movimiento financiero de la sesión. Si el trabajo
congeló contabilidad activa también crea el asiento en esa transacción. El flujo
heredado aún usa `DocumentProcessingJobs` para aceptar el recaudo; su handler no
escribe cartera y debe migrar al ingreso financiero directo sin cambiar el
propietario ni las tablas.

## Modelo

- `CustomerCreditProfiles`: habilitación, plazo y cupo opcional por cliente y negocio.
- `Receivables`: obligación originada por un documento de venta.
- `ReceivableTransactions`: libro inmutable de apertura, pago y futuras compensaciones.
- `CustomerPayments`: encabezado durable e idempotente del recaudo.
- `CustomerPaymentApplications`: distribución del recaudo entre obligaciones.

Las tablas pertenecen al `BusinessId`; el `TenantId` se valida mediante la relación canónica con `Businesses`. No se duplican datos de Party ni del documento fiscal.

## Flujo conectado

1. La venta online recibe pagos reales y, opcionalmente, términos de crédito.
2. La captura valida cliente, vencimiento, perfil y cupo.
3. El motor operacional procesa inventario/costo y publica una señal contable exactamente una vez.
4. El motor financiero-contable crea en su transacción la obligación y el movimiento inicial; agrega el asiento únicamente si contabilidad estaba activa al aceptar la fuente.
5. La API permite consultar cartera paginada y registrar un recaudo con llave de idempotencia.
6. El recaudo crea directamente su fuente y trabajo contables durables.
7. El procesador canónico aplica abonos sin permitir sobrepago y actualiza el estado; contabiliza caja/banco contra cartera únicamente cuando el trabajo requiere asiento.
8. La vista administrativa consulta el libro real y registra abonos; no calcula saldos en el navegador.

## Concurrencia e idempotencia

- La base impide una obligación duplicada por documento origen.
- `PaymentId` e `IdempotencyKey` son únicos por negocio.
- Un replay con el mismo contenido devuelve la aceptación previa; contenido distinto produce conflicto.
- La aceptación usa aislamiento `Serializable`, bloqueos de actualización y reintento acotado de deadlock.
- Dos abonos concurrentes que excedan el saldo no pueden ser aceptados ambos.
- La configuración de cupo se actualiza en una transacción serializable.

## Contabilidad

- Venta a crédito: débito a cuentas por cobrar y créditos a ingresos e impuestos, además del costo/inventario aplicable.
- Recaudo: débito al medio de recaudo configurado y crédito a cuentas por cobrar.
- No se crean pagos de venta ficticios para representar financiación.
- Con contabilidad inactiva, venta, recaudo y devolución conservan la misma
  cartera y transacciones, pero generan cero `AccountingEntries`.
- `AccountingEntryRequired` se congela por trabajo. La activación posterior usa
  saldos iniciales aprobados y no reprocesa el historial como ingresos o recaudos.

## Límites deliberados de esta rebanada

Esta entrega cubre crédito y recaudo online. El crédito offline no se habilita hasta sincronizar perfil, cupo y una política explícita a POS Edge. Tampoco incluye intereses, cuotas, cheques posfechados, castigos, retenciones, conciliación bancaria ni una pantalla de configuración del perfil dentro del editor de clientes.
