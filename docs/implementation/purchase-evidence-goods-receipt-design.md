# Evidencia de compra en recepción de mercancía

## Decisión

La recepción conserva un único flujo operativo y contable. `PurchaseEvidenceType` determina solamente el respaldo de la compra:

| Tipo | Origen | DIAN | Datos de factura del proveedor |
| --- | --- | --- | --- |
| `SupplierElectronicInvoice` | Factura emitida por el proveedor | No se vuelve a emitir | Obligatorios |
| `BuyerElectronicSupportDocument` | Documento soporte emitido por el comprador | Sí | No aplican |
| `InternalReceiptVoucher` | Comprobante interno | No | No aplican |

No se crean motores ni colas nuevas. La confirmación crea el `DocumentProcessingJob` de recepción existente. El coordinador contable sigue siendo propietario de inventario/costo, IVA y cuenta por pagar. Cuando corresponde documento soporte, la misma transacción crea el root fiscal, el snapshot inmutable y el proceso fiscal que consumen los workers de generación, firma y transporte DIAN existentes.

## Política del proveedor

`AuralyCatalog.Suppliers.PurchaseEvidencePolicy` es opcional y usa el catálogo `purchase-evidence-type`:

- Factura del proveedor: permite factura electrónica o comprobante interno.
- Documento soporte: permite documento soporte o comprobante interno.
- Comprobante interno: permite solamente comprobante interno.
- Sin configurar: permite los tres tipos.

La interfaz filtra opciones para orientar al usuario, y persistencia vuelve a validar la política dentro de la transacción para evitar que clientes desactualizados la evadan.

## Contabilidad

Los tres tipos causan la compra mediante el motor contable actual. La recepción reconoce inventario o costo/gasto, el impuesto descontable cuando existe soporte fiscal válido y la cuenta por pagar cuando aplica. El comprobante interno no admite `DeductibleInputVat`; el impuesto se capitaliza en el costo para no reconocer IVA descontable sin documento fiscal.

## Documento soporte y numeración

Documento soporte usa `FiscalDocuments`, `FiscalDocumentProcesses`, el emisor fiscal activo y una `FiscalSeries` productiva con `DocumentType = SupportDocument`. Su resolución se asigna explícitamente desde el onboarding DIAN y es independiente de la serie de facturas de venta. El código único se calcula como CUDS SHA-384 y el UBL identifica al proveedor como vendedor y al negocio como comprador/emisor.

## Invariantes

- La recepción, la reserva de consecutivo y la creación del proceso fiscal son atómicas e idempotentes.
- Una recepción de factura del proveedor o comprobante interno nunca crea un proceso DIAN.
- Un documento soporte sin emisor, resolución o serie vigente falla explícitamente antes de confirmar.
- Los workers siempre generan desde el snapshot fiscal inmutable, no desde maestros mutables.
- La resolución de documento soporte se reserva para una sola sede y no comparte cursor con ventas.
