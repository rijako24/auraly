# Entradas de mercanc?a: dise?o implementado

Fecha: 2026-08-02
Rama: `feature/auraly-commerce-goods-receipts`

## Alcance de esta entrega

Esta rebanada conecta la bandeja y captura web de entradas de mercanc?a con el flujo de servidor que ya procesaba documentos confirmados.

```text
Auraly Admin
  -> API Purchasing autenticada
  -> GoodsReceiptDrafts (recuperable, mutable y concurrente)
  -> confirmaci?n inmutable GoodsReceipts
  -> DocumentProcessingJobs (orden estricto por Business)
  -> inventario y costo promedio
  -> costo observado del proveedor
  -> propuesta separada de precio de venta
  -> cuenta por pagar o contrapartida de contado
  -> contabilidad y outbox
```

No se cre? un segundo cat?logo ni una segunda tabla de productos. Bodegas, proveedores, productos, c?digos de barras, perfiles tributarios y costos observados se leen desde sus propietarios can?nicos existentes.

## Separaci?n borrador/documento

`GoodsReceiptDrafts` y `GoodsReceiptDraftLines` almacenan trabajo incompleto. Admiten encabezado parcial, l?neas v?lidas y recuperaci?n.

`GoodsReceipts` y `GoodsReceiptLines` siguen siendo el documento confirmado e inmutable. El n?mero `EMC` solo se asigna al confirmar. El borrador conserva su UUID como identificador del documento y se elimina dentro de la misma transacci?n que persiste el confirmado y su trabajo del motor.

La confirmaci?n de un borrador exige su `DraftConcurrencyToken`. Tanto guardar como confirmar comparan el `rowversion`; una pesta?a obsoleta recibe conflicto 409 y no consume consecutivo.

## Contratos

- `GET /api/commerce/v1/goods-receipts/options`
- `GET /api/commerce/v1/goods-receipts/products`
- `GET /api/commerce/v1/goods-receipts`
- `GET /api/commerce/v1/goods-receipts/drafts/{draftId}`
- `PUT /api/commerce/v1/goods-receipts/drafts/{draftId}`
- `DELETE /api/commerce/v1/goods-receipts/drafts/{draftId}`
- `POST /api/commerce/v1/goods-receipts/confirm`

Todos usan el Business autenticado; no aceptan otro negocio por confiar en el body. Los permisos son lectura, creaci?n y confirmaci?n de entradas.

## Experiencia web

La ruta es `/dashboard/purchasing/goods-receipts`.

La bandeja pagina en servidor y combina b?squeda y estado. El editor permite seleccionar proveedor, bodega, factura, fechas y condici?n de pago; capturar por lector o buscar por c?digo interno, referencia, nombre, c?digo de proveedor y c?digo de barras; repetir una lectura para incrementar cantidad; editar cantidad, costo y descuento con rec?lculo inmediato; eliminar l?neas; guardar, recuperar y eliminar borradores; y confirmar con permiso.

Los costos observados alimentan la propuesta de precios, pero la entrada nunca publica un precio de venta autom?ticamente.

Cada producto de la grilla muestra informativamente último costo recibido y costo promedio vigente antes de capturar el nuevo costo. El concepto de retención y el municipio para reteICA son selectores provenientes de reglas tributarias activas de compra; no aceptan texto libre que el motor no pueda resolver.

Cuando la sede comparte precios, confirmar mantiene la cantidad física exclusivamente en la bodega receptora y recalcula costo/promedio/precio preparado para todas las sedes compartidas. Las sedes independientes conservan el flujo aislado existente.

## Decisiones a?n no implementadas

La reversi?n de una entrada confirmada y la devoluci?n de compra deben construirse como documentos compensatorios procesados por el motor. No se incluyeron aqu? porque necesitan preservar costo original, comprobar cantidades a?n retornables y compensar inventario, cuentas por pagar y contabilidad en una sola operaci?n. No se simulan mediante eliminaci?n ni edici?n del documento confirmado.
