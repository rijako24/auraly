# Evidencia de compra en recepción de mercancía

## Decisión

La recepción conserva un único flujo operativo y contable. `PurchaseEvidenceType` determina solamente el respaldo de la compra:

| Tipo | Origen | DIAN | Número de factura del proveedor |
| --- | --- | --- | --- |
| `SupplierElectronicInvoice` | Factura emitida por el proveedor | No se vuelve a emitir | Obligatorio |
| `BuyerElectronicSupportDocument` | Documento soporte emitido por el comprador | Sí | No aplica |
| `InternalReceiptVoucher` | Comprobante interno | No | No aplica |

Los tres respaldos requieren una fecha de emisión. Se conserva el nombre técnico compatible `SupplierInvoiceDate`, pero funcionalmente representa la fecha de emisión del documento de compra y no se confunde con `ReceivedAt`, que es la fecha física registrada por el sistema.

No se crean motores ni colas nuevas. La confirmación crea el `DocumentProcessingJob` de recepción existente. El coordinador contable sigue siendo propietario de inventario/costo, IVA y cuenta por pagar. Cuando corresponde documento soporte, la misma transacción crea el root fiscal, el snapshot inmutable y el proceso fiscal que consumen los workers de generación, firma y transporte DIAN existentes.

## Política del proveedor

`AuralyCatalog.Suppliers.PurchaseEvidencePolicy` es opcional y usa el catálogo `purchase-evidence-type`:

- Factura del proveedor: permite factura electrónica o comprobante interno.
- Documento soporte: permite documento soporte o comprobante interno.
- Comprobante interno: permite solamente comprobante interno.
- Sin configurar: permite los tres tipos.

La interfaz filtra opciones para orientar al usuario, y persistencia vuelve a validar la política dentro de la transacción para evitar que clientes desactualizados la evadan.

La misma entidad de proveedor conserva `DefaultPaymentDueDays` (30 por defecto, entre 0 y 3650). La recepción propone el vencimiento desde la fecha de emisión; el usuario puede ajustarlo para el documento particular y el servidor sólo rechaza fechas anteriores a la emisión.

## Contabilidad

Los tres tipos causan la compra mediante el motor contable actual. La recepción reconoce inventario o costo/gasto, el impuesto descontable cuando existe soporte fiscal válido y la cuenta por pagar cuando aplica. El comprobante interno no admite `DeductibleInputVat`; el impuesto se capitaliza en el costo para no reconocer IVA descontable sin documento fiscal.

## Documento soporte y numeración

Documento soporte usa `FiscalDocuments`, `FiscalDocumentProcesses`, el emisor fiscal activo y una `FiscalSeries` productiva con `DocumentType = SupportDocument`. Su resolución se asigna explícitamente desde el onboarding DIAN y es independiente de la serie de facturas de venta. El código único se calcula como CUDS SHA-384 y el UBL identifica al proveedor como vendedor y al negocio como comprador/emisor.

La consulta oficial `GetNumberingRange` entrega número, prefijo, rango, vigencia
y clave técnica, pero no clasifica el propósito del rango. Por eso Auraly no
adivina “factura” o “documento soporte” por el prefijo: el onboarding presenta
una sección separada y exige confirmar el propósito al reservarlo. La reserva es
atómica y exclusiva, valida tanto `ValidFrom` como `ValidUntil` y crea una serie
y cursor de documento soporte distintos de `SalesInvoice`.

Un gasto sólo genera documento soporte cuando el proveedor está clasificado con
`BuyerElectronicSupportDocument`; un comprobante o nota manual no se envía por
el solo hecho de ser manual. La devolución de una compra respaldada por documento
soporte genera, por el mismo motor fiscal, una nota de ajuste tipo `95`, CUDS y
referencia inmutable al número y CUDS originales. No crea otra cola ni otro
emisor fiscal.

La vista de la recepción reutiliza el visor corporativo de reportes para su
representación imprimible/PDF y exportación tabular. Cuando la compra genera
documento soporte, el reporte presenta el número fiscal y el CUDS persistidos
por el motor; no reconstruye identidad fiscal ni numeración desde maestros.

## Invariantes

- La recepción, la reserva de consecutivo y la creación del proceso fiscal son atómicas e idempotentes.
- Una recepción de factura del proveedor o comprobante interno nunca crea un proceso DIAN.
- Un documento soporte sin emisor, resolución o serie vigente falla explícitamente antes de confirmar.
- Los workers siempre generan desde el snapshot fiscal inmutable, no desde maestros mutables.
- La resolución de documento soporte se reserva para una sola sede y no comparte cursor con ventas.
- Una devolución de documento soporte genera una sola nota de ajuste tipo 95,
  validada con XSD, firmada y referenciada al CUDS original.
