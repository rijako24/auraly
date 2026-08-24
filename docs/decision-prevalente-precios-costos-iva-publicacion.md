# Decisión prevalente: costos, IVA, precio preparado y precio público

**Fecha:** 8 de agosto de 2026  
**Estado:** vigente y obligatoria  
**Prevalencia:** reemplaza cualquier regla anterior que permita publicar un precio desde la ficha del producto, que recalcule el costo al editar el precio de venta o que trate costo, costo promedio, precio de venta y precio público como el mismo dato.

## 1. Referencia funcional verificada

Se revisaron en Xion los formularios y servicios de:

- mantenimiento de productos;
- productos, proveedores y costos;
- entrada de mercancía y su resumen;
- actualización de precios de venta;
- editor común abierto desde productos, proveedores y entradas;
- cálculo de utilidad y selección de costo promedio.

Xion confirma estas reglas útiles:

- el costo del proveedor, el costo aplicado al producto, el costo promedio, el precio preparado y el precio público son conceptos diferentes;
- cambiar margen recalcula precio de venta;
- cambiar precio de venta recalcula margen;
- cambiar precio de venta no recalcula costo;
- `Guardar` conserva el precio preparado sin cambiar el público;
- `Actualizar` conserva el precio preparado y lo copia al público;
- la entrada de mercancía y productos/proveedores abren el mismo actualizador;
- el parámetro legacy `PrecioConCostoPromedio` elige la base de costo usada al formar precio y calcular utilidad, no evita calcular el costo promedio de inventario.
El contraste tributario de Xion también mostró que:

- `Producto` tiene `IvaCompraId` e `IvaVentaId` separados;
- la entrada conserva `IvaCompraId` en cada línea y totaliza los IVA de compra del documento;
- `ProductoService.CalcularPrecioCosto` suma el IVA de compra al costo neto junto con otros componentes;
- `ProductoService.CalcularPrecioVenta` recibe costo y margen, pero no recibe IVA de venta;
- otros modelos de Xion sí distinguen `PrecioVentaConIva` y calculan `PrecioVentaSinIva` dividiendo por `1 + tarifa`.

Xion sirve como referencia para separar IVA de compra y venta y para la experiencia de edición, pero su fórmula no determina si el IVA de compra es descontable y no integra consistentemente el IVA de venta al formar el precio. Auraly no copiará esa omisión: aplicará el tratamiento tributario confirmado de la compra y calculará explícitamente precio neto, IVA de venta y precio bruto.

Auraly conserva ese comportamiento, pero no las cuatro escalas rígidas, los formularios WinForms ni la arquitectura legacy.

## 2. Los cuatro conceptos canónicos

| Concepto | Propietario | Uso |
|---|---|---|
| Precio costo | Pricing/Purchasing | Costo vigente con el que el negocio prepara el precio. Puede originarse en edición autorizada o en una entrada procesada. |
| Precio costo promedio | Inventory | Promedio móvil ponderado autoritativo por `BusinessId + WarehouseId + ProductId`. No es editable. |
| Precio de venta | Pricing | Precio final preparado, incluido el IVA de venta, todavía no visible para facturación. |
| Precio público | Pricing | Precio final publicado, incluido el IVA de venta, que consumen catálogo, pedidos y facturación. |

El costo promedio no se duplica dentro de `ProductPrices`: una sede puede tener varias bodegas con saldos y promedios diferentes. La API de producto proyecta los cuatro valores juntos, pero obtiene el promedio desde `InventoryBalances`.

## 3. IVA de compra e IVA de venta

La creación y edición del producto exige seleccionar por separado:

```text
PurchaseTaxProfileId   // IVA de compra esperado
SalesTaxProfileId      // IVA de venta
```

Ambos perfiles pertenecen al mismo `BusinessId`, deben estar activos y pueden representar una operación gravada, exenta, excluida o no aplicable según el catálogo tributario vigente.

No se infiere un impuesto a partir del otro. El proveedor puede facturar un tratamiento diferente del aplicado por el negocio al vender.

La línea confirmada de entrada conserva un snapshot del impuesto realmente recibido. Una diferencia frente al perfil esperado del producto genera una advertencia revisable; no se corrige silenciosamente la factura del proveedor.

### 3.1 Tratamiento del IVA de compra

El costo de adquisición utilizado por inventario se determina así:

```text
AcquisitionUnitCost = NetUnitCost + CapitalizablePurchaseTaxPerUnit
```

- Si el IVA de compra es descontable, se registra como impuesto descontable y no aumenta el costo del inventario.
- Si no es descontable, corresponde a una operación excluida/no responsable o la política tributaria confirmada exige capitalizarlo, aumenta el costo.
- El tratamiento queda congelado en la línea de entrada (`DeductibleInputVat`, `CapitalizedCost` o `NotApplicable`).
- No basta con conocer la tarifa: el derecho a descuento depende de la situación fiscal y del destino de la adquisición.

Esta separación se sustenta en los artículos 485 y 488 del Estatuto Tributario y en doctrina DIAN: el IVA descontable no puede simultáneamente tratarse como costo; cuando no es descontable puede constituir mayor valor del costo según corresponda.

### 3.2 Tratamiento del IVA de venta

`SalePrice` y `PublicPrice` son precios finales para el comprador e incluyen el IVA de venta. Para no inflar artificialmente la utilidad, Pricing calcula el margen sobre el precio neto de venta:

```text
NetSalePrice = GrossSalePrice / (1 + SalesTaxRate / 100)
MarginPercent = (NetSalePrice - CostBasis) / NetSalePrice * 100
NetSalePrice = CostBasis / (1 - MarginPercent / 100)
GrossSalePrice = NetSalePrice * (1 + SalesTaxRate / 100)
```

Después se aplica la regla de redondeo al precio bruto y se recalcula el margen efectivo usando el precio bruto redondeado convertido nuevamente a neto.

Para tarifa cero, exento o excluido:

```text
GrossSalePrice = NetSalePrice
```

Cambiar el IVA de venta no modifica automáticamente el precio público existente. Genera una propuesta pendiente porque altera base, impuesto y margen efectivo. Solo el publicador puede aplicar el nuevo precio.

## 4. Modelo persistente

`ProductPrices` conserva una fila activa por `BusinessId + ProductId` con, como mínimo:

```text
ProductPriceId
BusinessId
ProductId
CostPrice
TargetMarginPercent
SalePrice
PublicPrice
CurrencyCode
CostSource
CostSourceDocumentId?
CostSourceWarehouseId?
CostUpdatedAt
SalePriceUpdatedAt
PublicPriceUpdatedAt?
PublicPriceUpdatedByUserId?
RowVersion
```

`InventoryBalances` conserva `AverageUnitCost` por bodega. `SupplierCostObservations` y `SupplierProductLatestCosts` conservan costo e historial por proveedor. Ninguno reemplaza a los demás.

## 5. Ficha de producto

La ficha contiene una sección integrada `Precio y rentabilidad`; no abre un segundo diálogo para consultar o editar precios.

Muestra:

- IVA de compra;
- IVA de venta;
- precio costo editable;
- costo promedio de solo lectura, con su bodega;
- margen editable;
- precio de venta preparado editable;
- precio público de solo lectura;
- origen y fecha del costo;
- diferencia entre precio preparado y público.

Reglas reactivas:

| Campo que cambia | Se conserva | Se recalcula |
|---|---|---|
| Precio costo | Margen e IVA de venta | Precio de venta bruto |
| Margen | Costo e IVA de venta | Precio de venta bruto |
| Precio de venta | Costo e IVA de venta | Margen |
| IVA de venta | Costo y margen | Nueva propuesta de precio de venta; nunca el público |
| Precio público | No editable | Nada |

`Guardar precio preparado` actualiza costo, margen y `SalePrice`; jamás `PublicPrice`, catálogo u outbox.

## 6. Entrada de mercancía

Cada línea captura cantidad, costo neto unitario, descuento, IVA de compra realmente facturado y tratamiento tributario. Al procesarla, el motor secuencial:

1. conserva el snapshot de la línea;
2. calcula el costo unitario de adquisición;
3. registra IVA descontable o capitalizable;
4. actualiza el último costo del proveedor;
5. actualiza `CostPrice` del producto;
6. actualiza existencias, valor y costo promedio de la bodega;
7. selecciona la base configurada para formar precio;
8. conserva el margen del producto y calcula el nuevo `SalePrice` bruto usando el IVA de venta;
9. crea una propuesta idempotente;
10. genera cuenta por pagar y contabilización;
11. no modifica `PublicPrice`.

La bodega configura `PriceFormationCostBasis`:

```text
LatestReceiptCost
WeightedAverageCost
```

El promedio se calcula siempre. Esta configuración solo elige la base de la propuesta y del análisis comercial.

## 7. Precios y rentabilidad

Es el único módulo que publica. Puede abrirse desde el menú o filtrado por entrada, proveedor o producto y utiliza el mismo caso de uso.

Muestra último costo, costo promedio, tratamiento del IVA de compra, IVA de venta, precio público actual, margen, precio preparado, diferencia, bodega y documento fuente.

- `Guardar propuesta`: actualiza `CostPrice`, margen y `SalePrice`; no publica.
- `Publicar precio`: confirma `SalePrice` y lo copia a `PublicPrice` en una transacción con auditoría, `CatalogChanges`, outbox y notificación a POS.

Ningún endpoint de producto puede escribir `PublicPrice`. POS, pedidos y facturación leen exclusivamente `PublicPrice`; POS Edge no descarga costos, promedio ni margen.

## 8. Snapshot tributario de cada venta

El perfil de IVA de venta del producto es una configuración para preparar nuevas líneas; no es la fuente histórica de una factura ya emitida.

Al agregar el producto, POS online o POS Edge congelan por línea:

```text
ProductId
DescriptionSnapshot
Quantity
GrossUnitPrice
DiscountAmount
SalesTaxProfileId
TaxCode
TaxRate
TaxableAmount
TaxAmount
LineTotal
```

La factura conserva `TaxCode`, `TaxRate`, base e impuesto en cada línea inmutable. Los totales por tarifa se obtienen agrupando esas líneas y reporting los materializa en `reporting.SalesReportTaxFacts`; no se mantiene una segunda tabla operacional con los mismos importes.

En modo desconectado, `TaxCode`, `TaxRate`, base e impuesto vienen del catálogo SQLite vigente al capturar la línea y quedan dentro del snapshot fiscal y de la outbox local. Al subir la factura:

- el servidor persiste exactamente los valores recibidos;
- no reemplaza el IVA con el perfil actual del producto;
- valida aritmética, totales y coincidencia con el snapshot fiscal;
- una modificación posterior del IVA del producto solo afecta ventas nuevas;
- devoluciones y notas crédito toman el impuesto de la línea original.

El servidor puede detectar y registrar una diferencia frente a la configuración vigente, pero nunca reescribe una factura local ya emitida. `SalesDocumentLines`, el snapshot fiscal y el resumen tributario son la evidencia histórica.

Pruebas obligatorias adicionales:

- factura offline con varias tarifas conserva IVA por línea;
- la totalización por código y tarifa coincide con las líneas;
- cambiar el IVA del producto después de emitir no altera la factura;
- subir y reintentar conserva exactamente tarifa, base e impuesto;
- una alteración del IVA durante la carga produce conflicto de integridad;
- devolución y nota crédito reutilizan el IVA histórico del documento original.

## 9. Productos nuevos

Un producto puede crearse con IVA de compra, IVA de venta, costo, margen y precio preparado. Mientras no exista `PublicPrice`, queda `PendingPricePublication` y no se incluye en el catálogo vendible. Nunca se envía al POS con precio cero ni se publica implícitamente desde la creación.

## 10. Pruebas obligatorias

Además de las pruebas anteriores de Pricing:

- crear producto exige perfiles de compra y venta válidos;
- perfiles de otro negocio o inactivos son rechazados;
- IVA de compra descontable no aumenta costo;
- IVA de compra capitalizable sí aumenta costo;
- la entrada usa el impuesto real de su snapshot y detecta diferencia frente al esperado;
- cambiar costo conserva margen y recalcula precio bruto con IVA;
- cambiar margen conserva costo y recalcula precio bruto con IVA;
- cambiar precio bruto conserva costo y recalcula margen sobre venta neta;
- ningún cambio recalcula el costo hacia atrás;
- cambiar IVA de venta crea propuesta y no cambia el público;
- guardar desde producto o entrada no cambia `PublicPrice`;
- publicar es el único camino que cambia `PublicPrice` y notifica cajas;
- el POS recibe precio público bruto, pero ningún costo o margen;
- costo promedio ponderado por bodega, incluida entrada después de inventario negativo;
- procesamiento y propuestas idempotentes;
- concurrencia de publicaciones;
- SQL Server real y SQLite POS.

## 11. Fuentes oficiales consultadas

Consulta realizada el 8 de agosto de 2026:

- DIAN, Concepto 014527 de 2025: artículos 485, 488 y reglas sobre IVA descontable y costo. https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_14527_2025.htm
- DIAN, Concepto 000483 de 2026: requisitos para tratar el IVA de adquisiciones como descontable. https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_0483_2026.htm
- DIAN, Concepto 008208 de 2026: operaciones excluidas e IVA de compras como mayor valor del costo cuando no es descontable. https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_8208_2026.htm
- DIAN, Oficio 900227 de 2020: artículos 485, 488 y soporte del impuesto descontable. https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_900227_2020.htm

La configuración tributaria del producto facilita la operación, pero no sustituye la validación fiscal de cada documento ni constituye por sí sola asesoría tributaria.
