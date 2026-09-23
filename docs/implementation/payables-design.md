# Cuentas por pagar: diseño de la rebanada vertical

Fecha: 2 de agosto de 2026

## Alcance conectado

La rebanada extiende la obligación que ya crea una entrada de mercancía. No
crea una segunda cartera ni copia la tesorería de Xion.

El recorrido productivo es:

```text
Entrada de mercancía procesada
  -> Payables + movimiento Opening
  -> consulta paginada del admin
  -> confirmación de pago autenticada
  -> reserva transaccional de aplicaciones
  -> AccountingSourceDocuments + AccountingPostingJobs durables
  -> SqlAccountingPostingProcessor
  -> aplicaciones + movimientos de cartera
  -> saldo y estado de la obligación
  -> asiento contable
  -> evento de outbox del servidor
```

No existe sondeo periódico desde el frontend. La aceptación durable informa el
comprobante, pero el saldo sólo cambia cuando se aplica el trabajo financiero.
El admin muestra ese estado y ofrece actualización explícita de la vista activa;
no consulta automáticamente otra vez la cartera al recibir la aceptación.

## Fronteras

- `Auraly.Domain.Payables` valida asignaciones, importes y moneda.
- `Auraly.Contracts.Payables` contiene contratos API, permisos y el payload
  inmutable del documento `PayablePayment`.
- `Auraly.Application.Payables` aplica autorización y coordina el caso de uso.
- `Auraly.Infrastructure.Persistence` conserva la aceptación SQL y las fuentes
  financieras durables.
- `Auraly.Api` expone los endpoints autenticados.
- `Auraly.Commerce.Accounting.Infrastructure` contabiliza el documento
  completado sin duplicar el procesamiento comercial.

Los dos proyectos nuevos están incluidos en `Auraly.Commerce.sln` y tienen
consumidores reales. No se agregó una infraestructura modular vacía.

## API

```text
GET  /api/commerce/v1/payables
GET  /api/commerce/v1/payables/{payableId}
POST /api/commerce/v1/payable-payments/confirm
```

La lista aplica paginación de servidor y combina búsqueda, proveedor, estado y
vencimiento. El servidor obtiene `TenantId`, `BusinessId` y `UserId` de la
identidad autenticada; no confía en el alcance del body.

Permisos:

- `payables.read`
- `payables.payments.create`

## Persistencia e idempotencia

`SupplierPayments` conserva la cabecera comercial del pago.
`SupplierPaymentApplications` conserva cada asignación a una obligación. Las
aplicaciones se insertan al aceptar el comando para reservar el saldo antes de
que el worker lo procese. Una transacción serializable bloquea la obligación y
suma pagos aceptados aún no aplicados, evitando que dos solicitudes concurrentes
sobrepasen el saldo.

La identidad idempotente combina el alcance del Business, `PaymentId`, clave de
idempotencia y hash del comando. Un replay idéntico devuelve la aceptación
existente. Reutilizar el ID o la clave con otro contenido produce conflicto.

El handler usa el mismo `ProcessingSequence`, sesión SQL y recibo idempotente del
motor. En una sola transacción:

1. aplica todas las asignaciones;
2. agrega movimientos `Payment` a la cartera;
3. recalcula `OutstandingAmount` y `Status`;
4. marca el pago procesado;
5. registra el trabajo contable;
6. publica el evento en outbox.

Una republicación del mensaje no repite ninguno de esos efectos.

## Contabilidad

En COP, el pago contabiliza:

- débito a cuentas por pagar;
- crédito a caja para `Cash`;
- crédito a la cuenta bancaria activa seleccionada para `BankTransfer`; la cuenta
  principal se propone por defecto, pero puede cambiarse antes de confirmar.

La categoría `Bank` es explícita: no se reutiliza indebidamente la cuenta puente
de transferencias. El procesador reconstruye la fuente inmutable desde
`SupplierPayments`, conserva el hash del payload y genera como máximo un asiento
por documento.

## Interfaz administrativa

La ruta `/dashboard/payables` está integrada al menú y protegida por permiso.
Incluye:

- resumen de saldo total, vencido y obligaciones;
- búsqueda y filtros combinables;
- tabla paginada y adaptable;
- detalle con trazabilidad de movimientos;
- modal de abono con efectivo o transferencia y selector de cuenta bancaria;
- importes COP formateados y estados legibles;
- actualización explícita de saldos e historial después de aceptar un pago.

En la caja preparada Edge guarda primero el pago y sus medios en SQLite; solo
después lo confirma con identidad del dispositivo en el servicio canónico.
El comprobante autoritativo actualiza la misma proyección por `PaymentId`. El
cierre de la caja se calcula localmente, incluso sin conexión.

El backend admite un pago aplicado a varias obligaciones del mismo proveedor.
La primera pantalla registra un abono desde una obligación para mantener el flujo
simple; la selección masiva se agregará cuando exista su caso de uso visual
completo.

## Límites conscientes

- Moneda: COP.
- Medios: efectivo y transferencia bancaria.
- No se implementaron anticipos, retenciones, reversos ni conciliación bancaria.
- El proveedor actual aún no converge completamente al modelo `Party`; por eso
  el asiento no asigna todavía tercero contable. Esa convergencia debe ocurrir
  en la rebanada de Parties, sin duplicar proveedores.
- La regresión reveló que la política de costo promedio cuando una recepción
  cubre inventario previamente negativo necesita una decisión contable explícita.
  No se cambió silenciosamente el algoritmo dentro de esta rebanada.
