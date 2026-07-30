# Pedidos del bot como fuente única de Auraly Commerce

**Fecha:** 30 de julio de 2026  
**Estado:** decisión cerrada; prevalece sobre la sección 3 de
`orders-returns-routes-design.md`.

## Decisión

`dbo.Orders` y `dbo.OrderItems`, escritas actualmente por el bot, checkout e
integraciones, son la fuente única de pedidos de Auraly. No se crean
`CommerceOrders`, `CommerceOrderLines` ni un proceso que replique el pedido.

Un pedido confirmado por el bot queda disponible en el POS y en el módulo de
Pedidos tan pronto como se confirma su transacción. Se conservan `OrderId`,
`OrderItemId`, `BusinessId`, origen, agente, conversación, integración,
idempotencia y todos los snapshots capturados.

El módulo `Auraly.Orders` toma propiedad funcional del agregado existente
mediante contratos públicos. Durante la transición, los productores existentes
siguen escribiendo las mismas tablas. Esto no autoriza copiar su arquitectura
legacy ni exponer acceso directo desde el frontend.

`BusinessId` es la frontera directa. El backend resuelve y valida que pertenezca
al tenant autenticado; nunca confía solamente en el valor recibido del cliente.

## Persistencia

Se reutilizan:

- `Orders`;
- `OrderItems`;
- sus snapshots de cliente, producto, precios, descuentos, impuestos y totales.

Solo se agregan estructuras auxiliares con un consumidor real:

- `OrderClaims`, para impedir que dos cajas lleven simultáneamente el mismo
  pedido a una venta;
- `OrderInvoiceLinks`, con unicidad por pedido para impedir dos facturas;
- `OrderInvoiceBatchReceipts`, con el resultado durable e idempotente de
  facturación múltiple;
- mensajes en la outbox existente.

En el MVP un pedido completo produce una sola factura. No se agrega una tabla de
aplicación parcial por línea hasta confirmar el caso de uso de facturación
parcial.

Los estados actuales de `Orders` se mapean explícitamente a disponibilidad,
facturado y cancelado. El claim es un bloqueo temporal, no otro estado del
pedido.

## Una sola experiencia reutilizable

Un mismo componente y una misma API paginada alimentan:

- la bandeja compacta dentro de `/pos`;
- el modo expandido dentro del POS;
- el módulo propio `/dashboard/orders`.

La bandeja compacta muestra los pedidos disponibles más recientes con número,
cliente, hora, total y cantidad de líneas. Cada tarjeta se puede expandir para
ver el detalle sin abandonar la facturación.

La acción **Expandir pedidos** abre el espacio completo sin perder el borrador
actual. El módulo propio reutiliza ese mismo espacio, no una segunda
implementación.

El espacio completo ofrece filtros combinables por:

- número de pedido;
- estado;
- cliente por identificación, nombre o teléfono;
- vendedor;
- producto por código, referencia o nombre;
- origen;
- sede y caja de origen cuando aplique;
- fecha inicial/final.

La tabla usa paginación de servidor, ordenamiento estable, selección persistente
entre páginas y panel de detalle.

Atajos:

- `Enter`: abre o cierra el detalle del pedido enfocado;
- `Espacio`: marca o desmarca;
- `F1`: ejecuta la acción principal habilitada;
- `Escape`: cierra el modo expandido y vuelve al POS;
- lector: busca el número exacto del pedido.

## Llevar a venta

La acción **Llevar a venta** reemplaza el término ambiguo “recuperar pedido” en
la interfaz y siempre actúa sobre un solo pedido.

Requiere:

- pedido disponible;
- borrador POS vacío, o confirmación explícita para pausar primero la venta
  actual;
- conexión con el servidor;
- claim durable para caja y usuario.

El servidor copia el snapshot del pedido al borrador activo y conserva el
vínculo `OrderId`/`OrderItemId`. No recalcula el pedido con los datos actuales
del producto o cliente. Cerrar y reabrir facturación conserva el borrador y el
vínculo.

## Facturar seleccionados

La acción está disponible en el modo expandido y en el módulo propio. No carga
varios pedidos dentro de la venta actual.

- Cada pedido seleccionado produce exactamente una factura independiente.
- Dos pedidos del mismo cliente también producen dos facturas.
- No existe consolidación de pedidos en el MVP.
- Antes de ejecutar se muestran cantidad de pedidos, cantidad de facturas y
  total.
- El lote tiene una clave idempotente y cada pedido una clave hija estable.
- Cada factura recibe su propio número Auraly, número DIAN y CUFE.
- Un fallo parcial conserva el resultado individual y reintenta solo lo
  pendiente.
- Repetir la operación nunca genera una segunda factura para el mismo pedido.

La respuesta muestra una fila por pedido: emitido, ya facturado, rechazado por
validación o pendiente de reintento.

## Contratos iniciales

- `GET /api/commerce/v1/orders`
- `GET /api/commerce/v1/orders/{orderId}`
- `POST /api/pos/v1/orders/{orderId}/claim`
- `DELETE /api/pos/v1/orders/{orderId}/claim`
- `POST /api/pos/v1/orders/{orderId}/recover`
- `POST /api/commerce/v1/orders/invoice-batches`
- `GET /api/commerce/v1/orders/invoice-batches/{operationId}`
- `POST /api/commerce/v1/orders/{orderId}/cancel`

Permisos:

- `orders.read`;
- `orders.recover`;
- `orders.invoice`;
- `orders.cancel`;
- `orders.override-pricing`.

## Pruebas de aceptación

- Un pedido creado por el bot aparece sin importación ni duplicación.
- POS y módulo propio muestran el mismo `OrderId` y detalle.
- Aislamiento por `BusinessId`.
- Filtros y paginación se ejecutan en SQL Server.
- El panel compacto y el expandido reutilizan la misma fuente y contratos.
- Solo un pedido puede llevarse a la venta actual.
- Dos cajas no pueden reclamar el mismo pedido.
- Cerrar y abrir facturación conserva el pedido llevado a venta.
- Seleccionar varios pedidos crea exactamente una factura por pedido.
- Pedidos del mismo cliente no se consolidan.
- Un duplicado o reintento no crea otra factura.
- Un fallo parcial no repite las facturas ya emitidas.
- Cada vínculo pedido-factura queda trazable.
