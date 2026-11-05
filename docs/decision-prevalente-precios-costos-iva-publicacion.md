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
| Precio costo | Pricing/Purchasing | Costo vigente con el que el negocio prepara el precio. Puede originarse en edición autorizada, en una entrada procesada o, para un producto con costo vinculado, del costo de su principal multiplicado por el factor configurado. |
| Precio costo promedio | Inventory | Promedio móvil ponderado autoritativo del grupo de sedes que comparte precios; para una sede independiente usa solo sus bodegas. No es editable. |
| Precio de venta | Pricing | Precio final preparado, incluido el IVA de venta, todavía no visible para facturación. |
| Precio público | Pricing | Precio final publicado, incluido el IVA de venta, que consumen catálogo, pedidos y facturación. |

El producto pertenece al tenant: existe un solo `ProductId` y un solo código para todas sus sedes. `ProductPrices` materializa una fila activa por `BusinessId + ProductId`. El costo promedio no se duplica dentro de `ProductPrices`; Inventory lo calcula con las existencias reales de todas las bodegas de las sedes activas cuyo `SharesProductPrices=1`, o con las bodegas de la sede actual cuando esta no comparte. Cada `InventoryBalance` conserva su cantidad física —incluidos saldos negativos— y recibe el promedio común del grupo.

## 3. IVA de compra e IVA de venta

### Ampliación: costo comercial de ingredientes y combos (2026-11-04)

El usuario requiere que los productos con lista Ingredientes y los productos con
lista Combo preparen su costo comercial mediante la suma de
`QuantityPerUnit × precio costo con IVA del componente`. La fuente fue
confirmada expresamente por el usuario: no es el precio de venta. Las listas son excluyentes
según el diseño gastronómico. Esta es una nueva fuente de costo para Pricing,
no un costo de adquisición ni una modificación de `InventoryBalances`.

Un cambio de precio costo de un componente, del impuesto que interviene en su
costo con IVA o de la composición debe recalcular el costo comercial
de los productos dependientes, manteniendo su margen objetivo y su impuesto de
venta. El precio se calcula con `PriceMargin` y el flujo canónico de preparación
y publicación, sin otra fórmula en UI, worker de combos, polling ni regla copiada
en la receta. Los cambios de cantidades/composición también afectan esa base.
No se obtiene el nuevo costo editando el margen del padre. Cambiar solo el precio
de venta, el margen o una promoción del componente no modifica el costo del padre.
La composición usa cantidades convertidas a la unidad de costo del componente,
con su precisión canónica, antes de sumar; no suma precios unitarios sin cantidad.

El costo del padre queda bloqueado automáticamente mientras tenga una composición
vigente no vacía, independientemente de si controla stock propio. No es solo un
campo deshabilitado: el servidor rechaza cambios manuales a su costo derivados
de formularios, importaciones o llamadas directas y usa el mismo propietario de
composición/Pricing para recalcularlo. Al quitar la última línea deja de ser
derivado; no se borra el historial ni se pone a cero el último costo válido por
defecto. La nueva captura manual exige la operación de preparación habitual.

Decisión final: no existe checkbox de sincronización. Recalcular el costo y
preparar el precio son automáticos; publicar sigue siendo una acción manual.

El guardado extiende `catalog.ProductCompositionSave`, con una sola versión y
una sola lista de componentes. La suma pertenece a
`catalog.ProductCompositionCost`; `dbo.ProductCompositionCostsPrepare` resuelve
las dependencias afectadas y delega la escritura de preparaciones a
`dbo.ProductLinkedCostsPrepare`. No introduce otro publicador ni toca el kardex.
El usuario pidió renombrar las dos tablas: `catalog.ProductCompositionVersions`
y `catalog.ProductCompositionComponents`. El refactor de esquema conserva datos,
versiones y relaciones mediante rename registrado en el dacpac; no copia las tablas.
El corte debe coordinar esquema y runtime porque el nombre SQL cambia. Un rollback
requiere un refactor inverso que preserve datos e historial de refactors y un
runtime compatible; publicar sin más el dacpac anterior no es un rollback del
rename. La prueba local cubre migración de dos tablas pobladas y segunda
publicación idempotente, preservando versión, componentes y relación.
Los IDs y contratos públicos de receta conservan compatibilidad. El selector de
captura es segmentado Ingredientes/Combo, con indicador deslizante, en creación y
edición; ambas posiciones conservan las validaciones canónicas de componentes.
El selector ocupa todo el ancho de la sección y la creación deja separación
explícita antes de agregar componentes. Cada fila muestra el costo unitario
vigente con IVA en su unidad, tanto al crear como al consultar o editar.
`ProductPriceCostBasis.UnitCostWithTax` es la misma fuente usada por la suma
compuesta: respeta costo ya bruto, composición y tratamiento capitalizado sin
sumar IVA dos veces. Las opciones paginadas y la receta entregan ese importe en
su respuesta existente; no hay consultas por fila ni cálculos tributarios en UI.
Solo se devuelve con `catalog.costs.read`; un costo faltante permanece nulo.
Las opciones se paginan y limitan antes de resolver costos (hasta 100 por página);
la receta está acotada a 100 componentes. Presupuesto de opción paginada local:
menos de un segundo, un comando de datos además de autorización. Los importes
visibles son costos actuales del contexto de sede, no snapshots históricos de
la versión de composición.

Verificación de esta ampliación: backend y dacpac sin advertencias ni errores;
build web de 94 rutas y lint de los archivos afectados aprobados. Los 18 casos
de composición y restaurante pasaron. La regresión nueva cruza costos unitarios
con IVA, cantidades y total preparado; prueba IVA capitalizado, costo faltante,
ocultamiento por permiso y una consulta de datos para la página de opciones,
dentro del presupuesto de un segundo. El navegador confirmó un selector del
ancho exacto del contenido y separación de 20 px al crear, costos visibles en
Ingredientes/Combo y cantidades fraccionadas que concilian con el preview. Se
inspeccionaron creación y edición en escritorio y móvil. La auditoría posterior
conserva el resolvedor, la suma y el publicador canónicos: el frontend solo
presenta importes, no consulta por fila ni agrega efectos de inventario. El campo
de respuesta es aditivo y nullable; los clientes anteriores siguen funcionando.

Los cambios de composición e impuestos recorren las sedes activas del tenant
en un conjunto; un cambio de costo conserva el alcance de precios de la sede
o grupo y no modifica las sedes independientes.
La preparación se limita a padres ofrecidos o con preparación pendiente en cada
sede; crear una composición no exige costos en una sede que no ofrece ese padre.
`dbo.ProductPriceCostBasis` es el único resolvedor de la base y de su procedencia
bruta para el contexto de Pricing y la suma compuesta. Al quitar la composición,
el último costo retenido conserva procedencia `Composition` mientras no cambie,
para no sumarle IVA de nuevo cuando sea componente de otro padre. Una nueva
captura manual obtiene procedencia `Manual`, incluso al vaciar la lista y editar
el costo en el mismo guardado.

Presupuesto verificable: hasta 100 componentes por lista, precisión de seis
decimales y profundidad máxima de 32 niveles, incluidos vínculos de costo.
El preview usa una petición HTTP y una consulta de costos por conjunto,
independientemente de la cantidad de componentes; presupuesto local con caché
caliente: menos de cinco segundos para 100 componentes. La cascada viaja en
un comando SQL y escribe por nivel, sin comandos de red por producto ni escaneo
de productos ajenos a las dependencias afectadas. La prueba
`Composition_cost_preview_handles_one_hundred_components_in_one_request`
registra la duración con SQL Server real; la auditoría del store verifica el
único `ExecuteReaderAsync` del preview, aparte de autenticación y autorización.

El costo con IVA de cada componente se obtiene desde su costo y tratamiento
tributario canónicos, con una descomposición explícita de base e impuesto. No se
aplica ciegamente `CostBasisAmount × (1 + tarifa)`: esa base puede incluir ya IVA
capitalizado, que no debe sumarse dos veces. Tampoco se usa el impuesto de venta
para reconstruir IVA de compra ni el precio público como sustituto del costo.
La aplicación de Pricing debe identificar el origen/versión de esa base bruta;
si el origen no permite probar base/impuesto, la preparación falla con motivo
explícito, sin inventar costo cero ni deducirlo del precio de venta.

**Impuesto del padre:** para Combo la tarifa de venta debe ser 0 %, obligatoria,
no solo un valor inicial editable. Se usa un perfil tributario activo válido
del catálogo canónico y se valida en servidor; el código y tratamiento del perfil
no se infieren únicamente de la tarifa. Para Ingredientes el usuario selecciona
el impuesto de venta permitido por el catálogo, incluido impuesto al consumo;
no se fuerza 0 %. Esto no cambia el impuesto/costo de los componentes ni altera
documentos ya emitidos. El margen se aplica al costo compuesto, y luego el impuesto
de venta del padre con las fórmulas y redondeo de `PriceMargin`.

**Publicación confirmada:** todo precio derivado se prepara para revisión. La
propagación actualiza costo/margen/precio preparado, pero no cambia el público,
ni dispara publicación o sincronización de precios vendibles por sí sola.
Publicar sigue requiriendo la acción autorizada del publicador existente, con su
auditoría y outbox. Las precisiones de fuente, IVA y publicación quedan resueltas
con las respuestas del usuario del 2026-11-04.

La composición pertenece al tenant; su valorización usa el contexto de precios
de la sede/grupo vigente. No se toma el precio de otra sede por conveniencia.
Un precio componente faltante no vale cero por defecto: la preparación informa
qué dato falta y no declara válido un precio final incompleto. Conserva IDs,
versión de composición y de precios usados para probar concurrencia y explicar
el cálculo. La edición manual del costo derivado no puede competir con su fuente.
Una publicación concurrente valida que la preparación y las versiones de sus
componentes sigan vigentes; no publica una suma obsoleta después de una cascada.

La propagación se diseña por conjunto sobre las dependencias afectadas, con
deduplicación cuando varias ramas comparten un componente y sin lecturas por
producto. Se detectan autorreferencias/ciclos antes de aceptar composiciones.
No se añade procesamiento proporcional al catálogo completo por cada cambio.
Los casos anidados deben cerrarse con el diseño de composición antes de
habilitarlos; una cascada de precio nunca recalcula una venta confirmada.

Reporting conserva el costo reconocido histórico del inventario efectivamente
consumido. Puede explicar el costo comercial usado para preparar precio si ese
snapshot se proyecta explícitamente, pero no lo presenta como costo contable ni
lo sustituye en utilidad real de ventas pasadas.

Aceptación: exclusión Ingredientes/Combo; selección de componentes elegibles;
cantidades y unidades; misma fórmula con margen/IVA/redondeo; costo bloqueado en
UI y servidor; combo con impuesto distinto de cero rechazado; ingrediente con
IVA/consumo elegible conservado; cambio de un
componente usado por varios padres; cambio concurrente de composición/precio;
aislamiento por sede/tenant y precios faltantes; rechazo de ciclos; sin doble
descuento de stock; replay sin propagaciones duplicadas; ventas y reportes
históricos intactos; IVA capitalizado no duplicado; cambio exclusivo de precio
venta no modifica costo compuesto; ningún cambio derivado publica automáticamente;
medición de viajes SQL y latencia de la cascada afectada.

Verificación local del 2026-11-04: los nueve escenarios de
`ProductionVerticalSliceTests` pasaron, incluidos exclusión, costo bruto,
preparación sin publicación, cascada por vínculos, impuesto capitalizado,
descarte del componente, sede sin oferta, retiro y edición manual simultáneos,
y preview de 100 componentes. Los cuatro casos de inventario propio/componentes
para Ingredientes/Combo en `RestaurantCheckoutTests` verifican el único movimiento
y su replay, además de la devolución y sus efectos contables existentes.
El catálogo y la proyección siguen siendo los propietarios de clasificación y
consumo histórico respectivamente; ninguna preparación escribe inventario.

Ejemplo dorado: una unidad de componente con costo neto 10.000 e IVA de compra
19 % (costo bruto 11.900), más dos unidades con costo bruto 5.000, generan costo
compuesto 21.900. Con margen sobre venta 25 % el precio neto preparado es 29.200;
para Combo el impuesto de venta 0 % deja precio bruto 29.200 antes del redondeo
configurado. Para un terminado con Ingredientes y perfil de consumo 8 % elegido,
el precio bruto preparado sería 31.536 antes de redondeo. Estos son valores de
prueba, no tarifas predeterminadas ni clasificación tributaria de un producto real.

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
- precio costo editable, salvo cuando está vinculado a un producto principal;
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

Para un vínculo con `ProductLinks.SharesPrice=1`, el nombre físico se conserva por
compatibilidad pero la relación comparte únicamente el costo. La ficha muestra el
costo derivado bloqueado, el producto principal y el factor. Un cambio del costo
principal vuelve a preparar al hijo conservando su margen; los precios preparado y
público de ambos productos se administran y publican de forma independiente.

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

El tenant configura `InventoryCostBasis`:

```text
LatestReceiptCost
WeightedAverageCost
```

El aprovisionamiento propone `LatestReceiptCost` y permite elegir `WeightedAverageCost`; la empresa puede cambiarlo después. El promedio se calcula siempre. Esta configuración solo elige la base de la propuesta y del análisis comercial.

Si una recepción ocurre en una sede compartida, solo aumenta las existencias de la bodega receptora. El nuevo costo promedio se calcula con la suma real de existencias del grupo y se propaga como costo común a sus balances y precios preparados. Una venta que deja saldo negativo nunca se transforma en saldo cero: mientras el grupo no tenga unidades positivas se conserva el último promedio válido —también después de entradas parciales que aún dejen saldo negativo o exactamente cero—; cuando una entrada cruza de saldo no positivo a positivo, el nuevo promedio parte del costo real de esa entrada. `InventoryValuationCalculator` es el único propietario de esta fórmula y `SqlInventoryLedgerWriter` su única puerta transaccional de aplicación.

El costo positivo exigido al crear un producto ordinario inicia `AverageUnitCost` en sus balances con cantidad y valor cero. Una venta sin existencias usa ese costo inicial, conserva el último promedio al pasar por cero o negativo y registra costo de ventas contra inventario negativo. `InventoryCostBasis` sigue gobernando exclusivamente propuestas de precio y análisis comercial; el kardex y el asiento usan promedio ponderado permanente. En una entrada que cubre inventario negativo, `InventoryMovements.ValueChange` representa el cambio real de valor en libros. La diferencia entre el costo de adquisición y ese cambio se reconoce explícitamente contra costo de ventas en el asiento de la recepción, sin modificar el costo congelado de las ventas anteriores. Con existencia positiva pero valor contable cero no se infiere un costo desde el precio: esa inconsistencia requiere corrección explícita antes de vender.

## 7. Precios y rentabilidad

Es el único módulo que publica. Puede abrirse desde el menú o filtrado por entrada, proveedor o producto y utiliza el mismo caso de uso.

Muestra último costo, costo promedio, tratamiento del IVA de compra, IVA de venta, precio público actual, margen, precio preparado, diferencia, bodega y documento fuente.

- `Guardar propuesta`: actualiza `CostPrice`, margen y `SalePrice`; no publica.
- `Publicar precio`: confirma `SalePrice` y lo copia a `PublicPrice` en una transacción con auditoría, `CatalogChanges`, outbox y notificación a POS.

En la grilla, confirmar una edición de margen o precio ejecuta inmediatamente
`Guardar propuesta`; el servidor devuelve la preparación y el token de concurrencia
autoritativos sin una segunda lectura. `Seleccionar todo` representa todo el conjunto
pendiente que coincide con los filtros del servidor, no solo la página visible. La
publicación parcial o total recibe la selección, vuelve a cargar en lote las
preparaciones vigentes desde `ProductPricePreparations` y publica esos valores. No
existe un botón ni un endpoint paralelo de “publicar todo”, y el navegador nunca
envía importes como autoridad de publicación.

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
- mismo `ProductId` y código en todas las sedes del tenant, con precio por `BusinessId + ProductId`;
- recepción compartida cambia cantidad solo en su bodega y costo/preparado en todo el grupo;
- publicación desde una sede compartida publica atómicamente en todo el grupo;
- una sede independiente no recibe costo, preparado ni publicación del grupo;
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

## 12. Referencias visibles para preparar precio

La bandeja de preparación presenta juntas tres referencias sin cambiar su
propietario: costo promedio ponderado de inventario, último costo del proveedor
(mercancía neta comparable) y último costo puesto por unidad reconocido en una
recepción procesada. La base de la propuesta continúa obedeciendo
`Tenant.InventoryCostBasis`; mostrar las tres referencias no recalcula ni sustituye
esa política. Verificación del 2026-10-20: `SqlGoodsReceiptDocumentHandler`
carga `tenant.InventoryCostBasis` en el campo interno `PriceFormationCostBasis`.
Aunque el maestro de bodegas conserve una propiedad del mismo nombre, ese handler
no la usa como override. No documentar una precedencia por bodega que hoy no existe.

## 13. Revisiones de costos de recepción — diseño 2026-10-20

La asociación de comprobantes y el presupuesto de reparto pertenecen a
[Purchasing](implementation/goods-receipts-design.md#ampliación-2026-10-20-asociar-comprobantes-y-cargos-posteriores).
Este diseño conserva el circuito de preparar y publicar: una revisión de costo
puede sustituir la preparación pendiente, nunca publicar automáticamente ni
modificar una venta histórica. `PricingService` y su store siguen siendo el único
camino de publicación, con versión, alcance de sedes compartidas, auditoría y outbox.

El reemplazo de propuestas pendientes ya existe. El cambio necesario de esquema
es distinguir revisiones del mismo origen: `PriceRevisionProposals.SourceRevision`
participará en la unicidad junto a sede, recepción y línea. La recepción inicial
usa revisión 0. Conservar estados y FK actuales; una propuesta publicada permanece
histórica. No crear otro sistema de propuestas para cargos posteriores.

La revisión aplicada y el producto/alcance se bloquean y versionan para impedir
que un trabajo atrasado vuelva vigente una propuesta vieja. Publicar compara
versión y origen vigentes; si un cargo cambió la preparación entre selección y
publicación, devuelve conflicto con el estado autoritativo para revisar.

El promedio sigue siendo el promedio de inventario. Asociar un cargo sin efecto
de valoración no puede cambiarlo ni generar un «promedio para precios» paralelo.
La aclaración final del usuario admite el ajuste contable de valoración mientras
la factura y su deuda permanecen iguales. Purchasing especifica el tratamiento
propuesto y el cierre técnico pendiente de atribución entre existencias y salidas;
no permanece pendiente una autorización de principio para contabilizar el ajuste.

La publicación modifica precio y base comercial publicados. El costo reconocido
por el kardex pertenece al motor de inventario y no espera la publicación de un
precio comercial. Esta frontera evita afirmar que publicar revaloriza inventario
o recalcula el costo de ventas anteriores.
