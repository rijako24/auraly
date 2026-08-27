# Rebanada vertical de cuentas por cobrar

Fecha: 2026-08-03
Prevalencia: la distribución vigente de efectos se rige por
`../decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.

## Decisión

La cuenta por cobrar nace únicamente cuando el motor contable procesa la fuente
durable de una venta a crédito verificada. El crédito no se representa como un
medio de pago ficticio: la suma de pagos reales más el valor financiado debe ser
igual al total de la venta.

El recaudo es un documento financiero durable (`ReceivablePayment`). Su aceptación
crea `AccountingSourceDocuments` y `AccountingPostingJobs`; no entra en
`DocumentProcessingJobs`. El único `SqlAccountingPostingProcessor` aplica en una
transacción el pago, asignaciones, saldo de la obligación, movimiento financiero
de la sesión, asiento y evento de salida.

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
4. El motor contable crea en su transacción la obligación, el movimiento inicial y el asiento de cartera.
5. La API permite consultar cartera paginada y registrar un recaudo con llave de idempotencia.
6. El recaudo crea directamente su fuente y trabajo contables durables.
7. El procesador contable canónico aplica abonos sin permitir sobrepago, actualiza estado y contabiliza caja/banco contra cartera.
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

## Límites deliberados de esta rebanada

Esta entrega cubre crédito y recaudo online. El crédito offline no se habilita hasta sincronizar perfil, cupo y una política explícita a POS Edge. Tampoco incluye intereses, cuotas, cheques posfechados, castigos, retenciones, conciliación bancaria ni una pantalla de configuración del perfil dentro del editor de clientes.
