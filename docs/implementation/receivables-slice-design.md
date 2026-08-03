# Rebanada vertical de cuentas por cobrar

Fecha: 2026-08-03

## Decisión

La cuenta por cobrar nace únicamente cuando el motor procesa una venta a crédito verificada. El crédito no se representa como un medio de pago ficticio: la suma de pagos reales más el valor financiado debe ser igual al total de la venta.

El recaudo es otro documento durable (`ReceivablePayment`). Se acepta con idempotencia, se ordena en `DocumentProcessingJobs` y el motor aplica, en una sola transacción, el pago, sus asignaciones, el saldo de la obligación, el movimiento de la sesión de trabajo cuando exista, el asiento contable y el evento de salida.

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
3. El motor procesa la venta exactamente una vez.
4. En la misma transacción crea la obligación, el movimiento inicial y el asiento de cartera.
5. La API permite consultar cartera paginada y registrar un recaudo con llave de idempotencia.
6. El recaudo se serializa como documento y entra al mismo motor ordenado por negocio.
7. El procesador aplica abonos sin permitir sobrepago, actualiza estado y contabiliza caja/banco contra cartera.
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
