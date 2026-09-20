# Reporting comercial nativo

Estado: implementación incremental aprobada. Este documento complementa, sin
reemplazar, `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.

## Regla de propiedad

Las pantallas de `Hoy`, ventas, vendedores, clientes e impacto por proveedor
leen exclusivamente `reporting.*`. Operación, pedidos, rutas, inventario y
facturación no calculan indicadores para la interfaz. El motor de reporting es
el único propietario de agregados, costo, utilidad e impacto.

No se crea una tabla por informe. Las vistas reutilizan documentos, hechos de
línea, pagos y totales diarios existentes. Solamente el motor documental de
operaciones crea el trabajo durable y, al completar, solicita la proyección.
Pedidos, rutas, visitas, despachos, contabilidad, servicios y sus APIs no llaman
directamente al motor de reporting.

## Atribución histórica de una venta

El writer canónico proyecta las líneas, los medios de pago y los grupos de
impuestos de una factura por conjuntos: una escritura SQL por conjunto, sin
consultas por línea. Los cargos incluidos en factura forman parte del documento,
los impuestos y los totales por cliente, vendedor y bodega. No se atribuyen a un
producto, categoría o proveedor de mercancía; su detalle se consulta en Cargos de
facturación. Los cargos asumidos como gasto no incrementan las ventas. La regla
de inclusión llega congelada en el documento y reporting no la recalcula.

Al confirmar cada línea de factura, `SalesDocumentLines` fija una vez:

- código y nombre del producto;
- categoría;
- proveedor atribuido, usando primero la relación principal activa;
- costo unitario reconocido por el kardex.

Código, nombre y valores comerciales provienen del snapshot de la línea vendida
(incluido el pedido recuperado); el catálogo vigente no los reinterpreta.
Categoría y proveedor sí se consultan al confirmar, exclusivamente para fijar
la atribución histórica de reporting. Una línea marcada como producto genérico
conserva ese indicador y no genera movimiento de inventario aunque el producto
sea reconfigurado después en el catálogo.

`AttributionSnapshotVersion=1` distingue un snapshot capturado, incluso cuando
no existe proveedor o categoría. Las filas heredadas conservan versión `0` y
pueden usar el catálogo vigente durante una reconstrucción. Las proyecciones
nuevas se escriben con versión `2` y nunca reinterpretan un snapshot versión 1.

La utilidad realizada por línea se calcula exclusivamente desde el snapshot de
la venta: `TotalAmount - (Quantity * DocumentUnitCost)`. `TotalAmount` ya es el
valor efectivo de la línea después del descuento. El margen es esa utilidad
dividida por `TotalAmount`. Ni la utilidad ni el margen consultan el costo o el
precio vigente del catálogo.

## Atribución nativa del pedido

Los pedidos comerciales guardan como columnas tipadas `WarehouseId`,
`OrdersWarehouseId`, `ReservationTransferId`, `SellerId`, `RouteId`,
`RouteStopId`, `PartySiteId`, `CapturedByUserId`, `CapturedOffline` y
`RequiresStockReview`. Estas propiedades dejan de pertenecer a
`CustomAttributesJson`.

La creación autoriza exclusivamente mediante `orders.create`; no exige que la
cuenta esté asociada a un vendedor comercial. Si la cuenta sí tiene vendedor,
el pedido conserva esa atribución. Si el pedido proviene de una ruta, conserva
el vendedor propietario de la ruta y una cuenta de vendedor no puede usar una
ruta ajena. Un pedido sin ruta creado por una cuenta administrativa conserva
`CapturedByUserId` y deja `SellerId` vacío, sin atribuir ventas ficticias. El despliegue incluye
un backfill idempotente para pedidos anteriores; las lecturas mantienen un
fallback temporal al JSON únicamente para compatibilidad durante el cutover.

Cuando un pedido se factura, la proyección de la venta conserva el `SellerId`
comercial del pedido y su nombre histórico. Para una venta sin pedido, resuelve
el vendedor por el tercero de la cuenta que registró la venta. El usuario de
caja nunca reemplaza silenciosamente al vendedor que originó el pedido.

## Informes semánticos cerrados

Los informes no son variantes visuales de una misma consulta:

- **Ventas** explica venta bruta, devoluciones, venta neta, costo, utilidad,
  recaudo y comprobantes; permite navegar por producto, categoría, cliente,
  vendedor, sede y proveedor.
- **Vendedores** atribuye las ventas facturadas al vendedor histórico del
  documento. No proyecta agendas, visitas ni pedidos.
- **Impacto de proveedores** relaciona sell-in (recepciones menos devoluciones
  de compra) con sell-out, utilidad, penetración en clientes y crecimiento
  contra el período anterior equivalente.

## Granos físicos necesarios

`GoodsReceipt` y `PurchaseReturn` reutilizan la tubería documental y la única
cola de reporting. Sus fuentes inmutables se proyectan en
`PurchaseReportDocuments` y `PurchaseReportLineFacts`; las devoluciones se
guardan con signo negativo. Se conservan proveedor, bodega, producto, moneda,
cantidades y valores históricos. Los agregados se calculan desde estos hechos;
no se crea una tabla por pantalla.

## Aislamiento por identidad

El alcance se resuelve en servidor mediante `AppUsers.PartyId` y la relación
canónica del negocio con `CommerceSellers` o `Suppliers`:

- un vendedor solo puede leer sus ventas, clientes, rutas, visitas y pedidos;
- un proveedor solo puede leer las líneas de sus productos y su propio impacto;
- filtros, URL, detalle y futuras exportaciones no pueden ampliar ese alcance;
- una identidad asociada simultáneamente a vendedor y proveedor, inexistente o
  ambigua falla cerrada;
- una cuenta sin esas asociaciones conserva el alcance administrativo que le
  otorgue `sales.reports.read`.

Las restricciones se aplican antes de ejecutar cada consulta semántica. No son
filtros cosméticos del navegador.

## Fuera de alcance

Metas, comisiones, geocercas, seguimiento GPS y predicciones quedan fuera de
este corte. No se inventan datos históricos de cobertura: la interfaz informa
la primera fecha realmente disponible en la proyección.
