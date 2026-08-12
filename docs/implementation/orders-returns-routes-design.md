# Diseño conectado: Pedidos, Devoluciones de venta y Rutas

**Fecha:** 29 de julio de 2026  
**Estado:** decisión para las siguientes rebanadas verticales  
**Fuente funcional:** Xion, sin copiar código, interfaz, tablas ni nombres heredados.

> [!IMPORTANT]
> La sección **Rutas** de este documento fue reemplazada por
> [`routes-slice-design.md`](./routes-slice-design.md), que contiene la auditoría
> completa de Xion y el diseño definitivo de implementación.
> [!IMPORTANT]
> La decisión
> [`order-source-of-truth.md`](./order-source-of-truth.md) prevalece sobre la
> sección 3: los pedidos existentes del bot son la fuente única y no se duplican.

## 1. Conclusión y orden de implementación

No se deben construir los tres módulos a la vez.

1. **Pedidos conectado al POS y a la vista web propia.**
2. **Devoluciones de venta conectadas a inventario, caja, cartera y nota crédito.**
3. **Rutas como módulo web administrativo y operativo.**

Pedidos es primero porque el POS ya muestra su acceso y porque su flujo termina en
el motor de ventas existente. Devoluciones es segundo porque no puede reducirse a
un botón: necesita cantidades devolvibles, movimiento de inventario, resolución
económica y documento fiscal. Rutas es tercero porque depende de Party,
CustomerRole, PartyLocation y SellerRole, pero no bloquea la venta.

No se mostrará una acción habilitada en producción hasta que su caso de uso,
persistencia, permisos y pruebas de extremo a extremo estén conectados.

## 2. Auditoría funcional de Xion

### 2.1. Pedidos

Referencia principal:

- `CapaDePresentacion-union/FormulariosPos/FrmFacturacionPedido.cs`

Comportamientos útiles encontrados:

- filtros por número, estado, caja/equipo, cliente, vendedor, producto y rango de
  fechas;
- selección múltiple y selección de todos;
- detalle del pedido seleccionado sin abandonar la consulta;
- **Recuperar** actúa sobre un solo pedido;
- **Facturar** actúa sobre todos los pedidos marcados;
- solo permite recuperar/facturar estados válidos;
- permite anular pedidos disponibles o pendientes con autorización;
- muestra observación y vendedor;
- en Xion, cada pedido seleccionado produce su propia factura y consecutivo.

Problemas que no se migran:

- la UI contiene persistencia, impresión y envío fiscal;
- usa esperas artificiales entre documentos;
- mezcla consulta local y servidor;
- hace transacciones por pedido desde el formulario;
- depende de IDs, estados y nombres heredados;
- no protege la selección mediante claims y `rowversion` de forma explícita.

### 2.2. Devoluciones de venta

Referencia principal:

- `CapaDePresentacion-union/FormulariosPos/FrmDevolucion.cs`
- `CapaDeEntidades/Variables Globales/TipoDevolucionVenta.cs`
- `CapaDeEntidades/Variables Globales/ModoDevolucion.cs`

Comportamientos útiles encontrados:

- busca la factura original por caja y consecutivo;
- carga cliente, líneas, cantidades, precio, descuento e impuesto originales;
- devolución parcial o total;
- no permite devolver más que la cantidad vendida y aún disponible;
- exige un motivo activo de devolución;
- admite productos pesables;
- diferencia devolución de dinero y aplicación a cuenta por cobrar;
- registra inventario, efecto económico y detalle tributario;
- vincula el documento de devolución con la factura original.

Lo que Auraly mejora:

- buscar también por número Auraly, número DIAN, CUFE, cliente, fecha, caja y
  producto;
- usar el snapshot inmutable de la venta, nunca el producto o cliente actuales;
- calcular el saldo devolvible acumulando devoluciones anteriores;
- separar estado comercial, inventario, reembolso/cartera y fiscal;
- generar nota crédito propia y durable en el motor fiscal;
- no incluir bonos especializados;
- no incluir lotes o seriales en el MVP;
- no permitir que un rechazo fiscal borre el movimiento comercial confirmado.

### 2.3. Rutas

Referencias principales:

- `CapaDePresentacion-union/Formularios/FrmRuta.cs`
- `CapaDePresentacion-union/Formularios/FrmClientesXRuta.cs`

Comportamientos útiles encontrados:

- ruta por sede, zona, vendedor, estado y nombre;
- programación por días de la semana;
- filtros por sede, zona, vendedor, texto y cliente;
- asignación de clientes con orden obligatorio de visita;
- reordenamiento que normaliza la secuencia;
- listado e impresión de clientes en el orden de recorrido;
- permisos separados para consultar, crear, modificar y guardar.

Lo que Auraly mejora:

- una parada apunta a `PartyLocationId`, no solo a la persona; un cliente puede
  tener varias sedes/direcciones;
- reordenamiento atómico con `rowversion`;
- paginación y filtros en servidor;
- búsqueda por identificación, nombre, teléfono, ciudad y barrio;
- edición moderna con teclado y arrastrar/soltar, ambas sobre el mismo comando;
- reportes web/exportación controlada, no Crystal Reports;
- no descargar rutas al POS: es un módulo en línea y separado.

## 3. Pedidos canónicos

### 3.1. Frontera

El módulo nuevo es `Auraly.Orders`. La tabla `Orders` del bot o del checkout
existente no se convierte automáticamente en el pedido canónico del ERP.

Los pedidos originados en bot, app móvil o integración entran mediante un
adaptador y conservan:

- `SourceSystem`;
- `ExternalOrderId`;
- `SourceCorrelationId`.

La importación es idempotente. El pedido canónico es el único que puede ser
reclamado, recuperado o facturado por Auraly Commerce.

### 3.2. Modelo SQL mínimo

- `CommerceOrders`
- `CommerceOrderLines`
- `CommerceOrderClaims`
- `CommerceOrderInvoiceLinks`
- `CommerceOrderLineInvoiceApplications`
- eventos en la outbox existente

Todas las tablas usan `BusinessId` como frontera directa. `TenantId` no se repite
cuando `BusinessId` determina inequívocamente el propietario. Las consultas
siguen resolviendo el `BusinessId` desde la identidad autenticada, no desde un
valor confiado del body.

Estados mínimos:

- `Draft`
- `Confirmed`
- `PartiallyInvoiced`
- `Invoiced`
- `Cancelled`

El claim no es un estado del pedido. Es un bloqueo temporal con caja, usuario,
fecha de expiración y versión.

Cada línea conserva:

- producto, código, descripción y unidad de venta;
- cantidad pedida, facturada, cancelada y pendiente;
- precio, descuento, impuesto y total capturados;
- `rowversion`.

### 3.3. Consulta web/POS

La misma API paginada alimenta:

- `/dashboard/orders`, vista administrativa propia;
- diálogo amplio dentro de `/pos`.

Filtros combinables:

- número de pedido;
- estado;
- cliente por identificación, nombre o teléfono;
- vendedor;
- producto por código, referencia o nombre;
- origen;
- sede;
- caja de origen cuando aplique;
- fecha inicial/final;
- con saldo pendiente;
- reclamado por otra caja.

La tabla usa keyset pagination o cursor estable, ordenamiento en servidor,
selección persistente entre páginas y panel lateral de detalle.

Atajos del diálogo:

- `Enter`: abre detalle del pedido enfocado;
- `Espacio`: marca/desmarca;
- `F1`: ejecuta la acción principal habilitada;
- `Escape`: vuelve al POS;
- lector: busca número exacto de pedido.

### 3.4. Recuperar uno

`Recuperar pedido` exige:

- un único pedido;
- borrador POS vacío, o decisión explícita de pausar el borrador actual;
- conexión con servidor;
- cantidades pendientes;
- claim vigente de la caja/usuario;
- contexto de negocio, sede, bodega y caja compatible.

El servidor crea el claim y devuelve un snapshot. POS Edge guarda el snapshot y
los vínculos por línea dentro del borrador local. Perder la red después de
recuperarlo no pierde el borrador, pero la emisión requiere reconciliar el claim
y cantidades con el servidor.

### 3.5. Facturar seleccionados

`Facturar seleccionados` no significa recuperar varios en la venta actual.

- Xion factura cada pedido marcado por separado.
- Auraly conserva además la decisión aprobada de consolidar pedidos compatibles
  del mismo cliente.
- Pedidos de clientes distintos generan facturas independientes.
- Antes de confirmar se muestra cuántas facturas se crearán, su agrupación,
  totales, medios/condición de pago y exclusiones.
- Cada grupo usa una `IdempotencyKey`.
- El motor procesa cada factura exactamente una vez.
- Un fallo parcial conserva resultados por grupo y permite reintentar únicamente
  lo pendiente.

La consolidación nunca pierde `OrderId` ni `OrderLineId`; si la UI agrupa un mismo
producto, el servidor conserva las aplicaciones por línea.

### 3.6. API inicial

- `GET /api/commerce/v1/orders`
- `GET /api/commerce/v1/orders/{orderId}`
- `POST /api/pos/v1/orders/{orderId}/claim`
- `DELETE /api/pos/v1/orders/{orderId}/claim`
- `POST /api/pos/v1/orders/{orderId}/recover`
- `POST /api/commerce/v1/orders/invoice-batches`
- `GET /api/commerce/v1/orders/invoice-batches/{operationId}`
- `POST /api/commerce/v1/orders/{orderId}/cancel`

Permisos:

- `orders.read`
- `orders.recover`
- `orders.invoice`
- `orders.cancel`
- `orders.override-pricing`

## 4. Devolución de venta conectada

### 4.1. Entrada desde el POS

La acción `Devolución` abre una vista amplia y conectada, no agrega líneas a una
venta normal.

Flujo:

1. localizar factura original;
2. mostrar snapshot, pagos, estado fiscal y devoluciones previas;
3. elegir parcial o total;
4. capturar cantidades con teclado/lector;
5. seleccionar motivo;
6. elegir destino físico: vendible, inspección o avería;
7. resolver dinero: reembolso permitido o aplicación a cartera;
8. autorizar cuando corresponda;
9. confirmar una sola vez;
10. imprimir tirilla de devolución;
11. procesar nota crédito fiscal de forma durable.

La acción se habilita cuando exista el caso de uso completo. Se reservará el
atajo después de validar que no colisione con el mapa final.

### 4.2. Modelo mínimo

- `SalesReturns`
- `SalesReturnLines`
- `SalesReturnApplications`
- `CustomerRefunds`
- `ReturnReasons`
- vínculos a `SalesDocuments`, `InventoryMovements`, caja, CxC y artefactos
  fiscales existentes.

No se duplica el snapshot completo del producto actual. Cada línea guarda
únicamente los datos originales necesarios para explicar y procesar la
devolución.

Reglas:

- la suma devuelta por línea nunca supera lo vendido;
- precio, descuento e impuesto parten de la venta original;
- el movimiento de inventario es una entrada y se procesa exactamente una vez;
- efectivo genera salida de caja en la sesión correspondiente;
- crédito reduce CxC antes de crear un saldo a favor;
- factura electrónica solicita nota crédito vinculada;
- devolución confirmada no se elimina ni renumera.

## 5. Rutas

### 5.1. Frontera y modelo

Módulo `Auraly.Routes`:

- `SalesRoutes`
- `SalesRouteSchedules`
- `SalesRouteStops`

`SalesRoute`:

- `RouteId`, `BusinessId`, código, nombre, estado;
- `LocationId` opcional;
- `ZoneId` opcional;
- `SellerPartyId` opcional;
- auditoría y `rowversion`.

`SalesRouteSchedule`:

- día de semana;
- activo;
- orden o ciclo si posteriormente se confirma.

`SalesRouteStop`:

- `RouteStopId`;
- `RouteId`;
- `CustomerPartyId`;
- `PartyLocationId`;
- secuencia única por ruta;
- estado;
- observación operativa opcional;
- auditoría y `rowversion`.

La primera rebanada no agrega geocodificación, optimización automática,
seguimiento GPS ni despacho. Esas capacidades requieren casos de uso propios.

### 5.2. Experiencia web

Ubicación:

- menú **Comercio > Rutas**;
- maestros simples como Zona permanecen en **Configuración > Maestros**.

Vista:

- lista paginada con filtros en encabezados;
- panel de edición de datos y días;
- constructor de recorrido con búsqueda de clientes;
- selección explícita de la sede/dirección del cliente;
- reordenamiento por teclado o arrastre;
- mapa solo cuando exista geocodificación real;
- impresión/exportación controlada del recorrido.

Permisos:

- `routes.read`
- `routes.create`
- `routes.update`
- `routes.deactivate`
- `routes.stops.manage`
- `routes.export`

## 6. Pruebas obligatorias por rebanada

### Pedidos

- filtros combinados y paginación SQL Server;
- aislamiento por `BusinessId`;
- recuperar exactamente un pedido;
- no recuperar con borrador ocupado sin pausar;
- claim concurrente entre dos cajas;
- vencimiento/liberación de claim;
- selección entre páginas;
- consolidación del mismo cliente;
- separación de clientes diferentes;
- idempotencia del lote;
- fallo parcial y reintento;
- vínculo pedido-línea-factura;
- facturación parcial y saldo restante;
- cancelación con permiso;
- funcionamiento del diálogo POS solo en línea.

### Devoluciones

- búsqueda por todas las numeraciones y CUFE;
- parcial, total y devoluciones acumuladas;
- límite por línea bajo concurrencia;
- precio, descuento e impuesto originales;
- producto pesable;
- motivo requerido;
- entrada a vendible, inspección o avería;
- reembolso/caja y aplicación a CxC exactamente una vez;
- nota crédito ligada a factura;
- rechazo fiscal sin pérdida comercial;
- tirilla basada en snapshot;
- permisos de usuario y supervisor.

### Rutas

- CRUD y filtros paginados;
- asignación a `PartyLocation`;
- dos sedes del mismo cliente en rutas distintas cuando se permita;
- restricción de duplicados definida por negocio;
- secuencia continua y única;
- reorder concurrente;
- cambio de vendedor, días y estado;
- permisos backend/UI;
- exportación ordenada;
- aislamiento por `BusinessId`.

Cada rebanada conserva además build sin advertencias, DACPAC desplegado en SQL
Server real, pruebas de arquitectura y ausencia de componentes desconectados.

## 7. Decisiones que quedan cerradas

- Pedidos se ve como panel dentro del POS y como vista web propia.
- Recuperar actúa sobre un pedido.
- Facturar seleccionados actúa sobre todos los marcados y muestra el plan antes
  de ejecutar.
- La consulta de pedidos es siempre en línea.
- Devolución no es una venta negativa ni una edición de factura.
- Devolución siempre referencia el snapshot original en el MVP.
- No se trae el bono de devolución de Xion.
- Rutas referencia la ubicación concreta de Party.
- Rutas no se descarga a POS Edge en la primera versión.
- El módulo `Orders` del bot es una fuente/adaptador, no la tabla canónica del ERP.
