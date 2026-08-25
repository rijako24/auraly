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

## Informes semánticos cerrados

Los informes no son variantes visuales de una misma consulta:

- **Ventas** explica venta bruta, devoluciones, venta neta, costo, utilidad,
  recaudo y comprobantes; permite navegar por producto, categoría, cliente,
  vendedor, sede y proveedor.
- **Vendedores** presenta el embudo agenda → visita → pedido → factura, sus
  conversiones y la utilidad resultante. No incluye metas ni comisiones.
- **Clientes y cobertura** compara la visita planeada, el cierre operativo
  (visitada u omitida), el faltante sin cierre y la visita que produjo pedido,
  por ruta, zona y vendedor.
- **Impacto de proveedores** relaciona sell-in (recepciones menos devoluciones
  de compra) con sell-out, utilidad, penetración en clientes y crecimiento
  contra el período anterior equivalente.
- **Visitas** conserva su grano de evento y su trazabilidad individual.

## Granos físicos necesarios

`CommercialCoveragePlan` se captura dentro de cada mutación transaccional de
ruta y se proyecta en `CommercialCoverageAssignmentFacts`. Cada fila representa
una combinación horario–parada con snapshots de ruta, zona, vendedor, cliente,
sede y coordenadas, además del intervalo
`[ValidFromBusinessDate, ValidToBusinessDateExclusive)`. Así una edición futura
no reescribe el plan histórico.

`GoodsReceipt` y `PurchaseReturn` reutilizan la tubería documental y la única
cola de reporting. Sus fuentes inmutables se proyectan en
`PurchaseReportDocuments` y `PurchaseReportLineFacts`; las devoluciones se
guardan con signo negativo. Se conservan proveedor, bodega, producto, moneda,
cantidades y valores históricos. Los agregados se calculan desde estos hechos;
no se crea una tabla por pantalla.

`SellerOrder` evoluciona por versiones de fuente. Una nueva versión solo se
crea cuando cambia el hash del snapshot y actualiza idempotentemente el mismo
hecho de pedido. Conserva ruta, zona, parada, sede, canal, captura offline y
marcas de confirmación, cancelación y facturación.

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
