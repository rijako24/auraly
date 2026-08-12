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
       -> crédito sobre Payables
       -> excedente en SupplierCredits
       -> PurchaseReturnFinancialEffects
       -> AccountingPostingJobs
       -> ServerOutboxMessages
  -> PurchaseReturns.Processed
```

Todo el efecto del handler ocurre en la transacción del motor. Un error de existencia o valorización revierte esa transacción y conserva el documento pendiente/reintentable según la política del motor; no se salta el orden del negocio.

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

- Aplicar `SupplierCredits` a compras o reembolsos futuros.
- Revertir una devolución ya procesada mediante un segundo documento compensatorio.
- Devoluciones offline.
- Integración de una nota del proveedor como artefacto fiscal recibido.