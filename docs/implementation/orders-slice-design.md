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

Una edición reemplaza el detalle pero mueve inventario por diferencia agregada:
si una cantidad no cambia no crea traslados; los aumentos producen como máximo
una transferencia multilínea `VEN -> PED` y las disminuciones como máximo una
`PED -> VEN`, dentro de la misma transacción. El presupuesto del endpoint de
actualización es menor a un segundo para una edición sin cambio de reserva. Si
el pedido confirmado conserva la misma identidad comercial, líneas y reservas,
el writer actualiza únicamente los metadatos y su trabajo durable de reportería;
no vuelve a resolver catálogo, disponibilidad ni reemplaza el detalle.

### Facturar varios

Una selección produce una factura independiente por pedido. La operación completa tiene idempotencia durable, conserva progreso y devuelve resultado por pedido. Un pago ya confirmado por el pedido se registra como transferencia; de lo contrario se usa el medio seleccionado. Nunca se fusionan pedidos en una sola factura.

La API y la interfaz limitan cada lote a 50 pedidos. La selección masiva conserva
ese límite visible para que el usuario no prepare una solicitud que el servidor
rechazará. `Imprimir al facturar` está activo de forma predeterminada y puede
desmarcarse antes de emitir; esta preferencia sólo omite la vista previa o la
impresora física y nunca cambia checkout, numeración, cartera, inventario ni el
envío fiscal.

La vista ofrece únicamente `Efectivo` y `Crédito`. `Efectivo` entra directamente al lote. Para `Crédito`, el mismo `POST /api/commerce/v1/orders/invoice` ejecuta primero una validación agrupada de todos los clientes y del valor acumulado de sus pedidos; es una sola consulta SQL para la selección completa. Si un cliente no tiene crédito habilitado o el valor agregado supera su cupo disponible, la respuesta identifica los clientes rechazados y no crea operación, borrador, liberación de inventario, factura ni cartera. Si todos cumplen, los pedidos se emiten uno por uno con idempotencia independiente.

La prevalidación masiva no reemplaza la validación transaccional. Cada pedido a crédito envía `OnlineSalesCreditTerms` al checkout de ventas existente, que vuelve a validar el saldo bajo bloqueo y genera la cuenta por cobrar por el canal canónico. No existe un writer ni una ruta de cartera específica para pedidos.

## Seguridad

Permisos mínimos: `orders.read`, `orders.recover`, `orders.invoice`, `orders.cancel` y `orders.override-pricing`. Facturar también exige `sales.create`.

La web usa JWT de usuario. POS Edge usa identidad del dispositivo y además resuelve el usuario que inició sesión localmente contra `AppUsers`, roles, negocio y permisos actuales. El `BusinessId`, la caja y la bodega no se aceptan solo porque lleguen en el body.

## Experiencia

`/dashboard/orders` y el panel embebido de `/pos` reutilizan `OrdersWorkspace`:

- tabla moderna y paginada;
- filtros combinables;
- selección múltiple y facturación por lote;
- control explícito `Imprimir al facturar`, activo por defecto;
- detalle lateral;
- recuperación de un solo pedido;
- modo compacto dentro del POS;
- expansión a espacio completo sin abandonar la venta.

El perfil operativo de vendedor ruta consume la misma consulta canónica desde
la pestaña `Pedidos` de su recorrido diario. La vista filtra por bodega, usuario
autenticado y límites UTC del día operativo, y permite abrir el mismo detalle de
líneas sin navegar fuera del recorrido. Los atributos heredados de un pedido se
leen únicamente cuando contienen JSON válido; una carga histórica malformada no
puede derribar ni la lista ni el detalle. El cliente web sólo presenta mensajes
de problema JSON controlados y nunca imprime una página HTML de error dentro de
la interfaz.

La búsqueda en vivo del catálogo de captura solicita una sola página de diez
productos por interacción, después de 250 ms de espera, y muestra únicamente
nombre, código, referencia, unidad, existencia y precio resuelto. Al llegar al
final visible solicita automáticamente las siguientes diez filas; no descarga el
catálogo completo ni hace enriquecimiento por producto. El presupuesto del
camino servidor es menor a un segundo. La preparación offline sigue siendo una
operación explícita y separada que descarga páginas grandes para instalar un
snapshot completo; nunca se ejecuta al buscar o abrir el selector en línea.

## No incluido en esta rebanada

- edición comercial completa del pedido;
- rutas y despacho;
- devoluciones;
- cambios tributarios dentro del pedido, porque pertenecen deliberadamente a la factura.

Esas capacidades deben continuar en rebanadas verticales separadas y no como contratos vacíos.
