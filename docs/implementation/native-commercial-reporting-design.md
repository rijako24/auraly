# Reporting comercial nativo

Estado: implementación incremental aprobada. Este documento complementa, sin
reemplazar, `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.

## Regla de propiedad

Las pantallas de `Hoy`, ventas, vendedores, clientes e impacto por proveedor
leen exclusivamente `reporting.*`. Operación, pedidos, rutas, inventario y
facturación no calculan indicadores para la interfaz. El motor de reporting es
el único propietario de agregados, costo, utilidad e impacto.

No se crea una tabla por informe. Las vistas reutilizan documentos, hechos de
línea, pagos y totales diarios existentes. Una nueva proyección física solo se
agrega para un grano nuevo, como pedido, visita o asignación planificada.

## Atribución histórica de una venta

Al confirmar cada línea de factura, `SalesDocumentLines` fija una vez:

- código y nombre del producto;
- categoría;
- proveedor atribuido, usando primero la relación principal activa;
- costo unitario reconocido por el kardex.

`AttributionSnapshotVersion=1` distingue un snapshot capturado, incluso cuando
no existe proveedor o categoría. Las filas heredadas conservan versión `0` y
pueden usar el catálogo vigente durante una reconstrucción. Las proyecciones
nuevas se escriben con versión `2` y nunca reinterpretan un snapshot versión 1.

## Atribución nativa del pedido

Los pedidos comerciales guardan como columnas tipadas `WarehouseId`,
`OrdersWarehouseId`, `ReservationTransferId`, `SellerId`, `RouteId`,
`RouteStopId`, `PartySiteId`, `CapturedByUserId`, `CapturedOffline` y
`RequiresStockReview`. Estas propiedades dejan de pertenecer a
`CustomAttributesJson`.

La creación valida que el usuario tenga un vendedor comercial activo y que la
ruta, parada, cliente y sede pertenezcan a esa asignación. El despliegue incluye
un backfill idempotente para pedidos anteriores; las lecturas mantienen un
fallback temporal al JSON únicamente para compatibilidad durante el cutover.

Cuando un pedido se factura, la proyección de la venta conserva el `SellerId`
comercial del pedido y su nombre histórico. Para una venta sin pedido, resuelve
el vendedor por el tercero de la cuenta que registró la venta. El usuario de
caja nunca reemplaza silenciosamente al vendedor que originó el pedido.

## Visitas comerciales

Cada visita confirmada escribe, dentro de su transacción operativa, una fuente
inmutable `RouteVisit` en el inbox existente `SalesReportingJobs`. Después del
commit se publica la señal en `auraly-sales-reporting`; un replay idempotente
republica un trabajo durable pendiente sin duplicar el hecho.

`reporting.CommercialReportVisitFacts` conserva vendedor, ruta, zona, cliente,
sede, resultado, observación y pedido asociado. La API y la pantalla de visitas
leen exclusivamente este grano proyectado.

## Pedidos por vendedor

La creación de un pedido captura una fuente inmutable `SellerOrder` en el mismo
commit operativo y publica, después del commit, sobre la cola existente de
reporting. `reporting.CommercialReportOrderFacts` conserva el grano pedido con
vendedor, cliente, ruta, valor y estado; el informe de vendedores obtiene de
allí pedidos, clientes atendidos, confirmados y pendientes de revisión.

## Siguientes granos

La cobertura planificada ingresará por la cola existente
`auraly-sales-reporting` y su único inbox `SalesReportingJobs`. No consultarán
tablas operativas desde la interfaz ni crearán otro motor o cola. Antes de
habilitar sus vistas se deben completar conjuntamente productor durable,
payload inmutable, proyección idempotente, reconciliación y prueba de rebuild.
