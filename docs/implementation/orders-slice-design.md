# Rebanada de pedidos de Auraly Commerce

## Decisión central

La fuente canónica es el pedido que ya crea el bot en `dbo.Orders` y `dbo.OrderItems`. No se creó un segundo maestro de pedidos ni una copia específica para POS. El dashboard, la facturación web y POS Edge consultan y procesan los mismos registros.

Un pedido es comercial y no tributario. Guarda producto, cantidad, precio pactado, descuento y total comercial, pero no guarda IVA, tarifa tributaria, CUFE, resolución ni numeración fiscal. Al convertirlo en factura se consulta la configuración vigente del producto y se construye entonces el snapshot fiscal inmutable.

## Alcance y aislamiento

- `BusinessId` delimita el pedido y todas las consultas. El `TenantId` se obtiene de la identidad autenticada y de `Businesses`; no se repite en las tablas comerciales que ya quedan determinadas por `BusinessId`.
- `Auraly.Contracts.Orders`, `Auraly.Domain.Orders` y `Auraly.Application.Orders` forman el límite del módulo.
- `Auraly.Infrastructure.Persistence` implementa almacenamiento SQL; Orders no consulta tablas internas de Sales directamente.
- La API compone los módulos mediante servicios de aplicación.

## Persistencia

Se conservaron y unificaron las tablas existentes:

- `Orders`: encabezado creado por bot o integración, cliente y valores comerciales.
- `OrderItems`: líneas comerciales sin columnas tributarias.
- `OrderClaims`: arrendamiento breve que evita recuperar simultáneamente el mismo pedido en dos ventas.
- `OrderInvoiceLinks`: relación única pedido–factura y factura–pedido.
- `OrderInvoiceBatchReceipts`: operación durable e idempotente para facturar selecciones de hasta 50 pedidos.
- `SalesDrafts.SourceOrderId`: identifica el pedido recuperado en una venta web.
- `PosDrafts.SourceOrderId`: identifica el pedido recuperado en SQLite y sobrevive reinicios.

Las restricciones SQL garantizan un único vínculo por pedido, un único vínculo por documento y una única operación por `BusinessId + IdempotencyKey`.

## Casos de uso

### Consultar

Las consultas son paginadas en servidor y combinan número, cliente, producto, estado, fuente y fechas. Un pedido de otro negocio nunca se devuelve.

### Recuperar uno

1. La caja reclama temporalmente el pedido.
2. Obtiene el detalle desde Auraly Server.
3. Resuelve cada producto contra el catálogo vigente.
4. Conserva cantidad, precio y descuento del pedido.
5. Toma impuesto y configuración vendible actuales al construir la venta.
6. Importa todas las líneas de forma atómica; nunca deja media venta visible.
7. Impide mezclarlo con productos o con otro pedido ya presente.

En POS Edge el borrador queda en SQLite. Al emitir, `SourceOrderId` viaja dentro
de la outbox durable. El servidor vincula el pedido en la transacción operacional
que procesa la venta y libera el claim; el pago se procesa por el motor contable
canónico. Un reintento no duplica factura, pago, inventario ni vínculo.

### Inventario del pedido

La reserva y la liberación nunca se fragmentan por producto:

- al confirmar el pedido, una sola `WarehouseTransfer` multilínea mueve todos los
  productos inventariables desde la bodega de venta hacia la bodega sistema `PED`;
- antes de preparar factura o comprobante, una sola `WarehouseTransfer` multilínea
  mueve todas las líneas inventariables desde `PED` hacia la bodega de venta y
  queda referenciada en `Orders.ReleaseTransferId`;
- el procesador de la venta registra únicamente `Sale` en la bodega de venta. No
  crea `TransferOut`/`TransferIn` por línea y rechaza un pedido que no haya sido
  liberado por el flujo anterior.

### Facturar varios

Una selección produce una factura independiente por pedido. La operación completa tiene idempotencia durable, conserva progreso y devuelve resultado por pedido. Un pago ya confirmado por el pedido se registra como transferencia; de lo contrario se usa el medio seleccionado. Nunca se fusionan pedidos en una sola factura.

## Seguridad

Permisos mínimos: `orders.read`, `orders.recover`, `orders.invoice`, `orders.cancel` y `orders.override-pricing`. Facturar también exige `sales.create`.

La web usa JWT de usuario. POS Edge usa identidad del dispositivo y además resuelve el usuario que inició sesión localmente contra `AppUsers`, roles, negocio y permisos actuales. El `BusinessId`, la caja y la bodega no se aceptan solo porque lleguen en el body.

## Experiencia

`/dashboard/orders` y el panel embebido de `/pos` reutilizan `OrdersWorkspace`:

- tabla moderna y paginada;
- filtros combinables;
- selección múltiple y facturación por lote;
- detalle lateral;
- recuperación de un solo pedido;
- modo compacto dentro del POS;
- expansión a espacio completo sin abandonar la venta.

## No incluido en esta rebanada

- edición comercial completa del pedido;
- rutas y despacho;
- devoluciones;
- impresión física automática de un lote desde el dashboard;
- cambios tributarios dentro del pedido, porque pertenecen deliberadamente a la factura.

Esas capacidades deben continuar en rebanadas verticales separadas y no como contratos vacíos.
