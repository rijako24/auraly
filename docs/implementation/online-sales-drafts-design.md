# Borrador durable de venta online

Fecha: 2026-07-30

## Resultado

El navegador sin POS Edge dispone de un propietario servidor para la venta en
curso. El estado no depende de React, `localStorage` ni del equipo que abrió la
pantalla.

La misma API permite buscar productos y clientes de forma paginada, poner una
venta en espera, recuperar una venta y eliminar una venta en espera. Todas esas
operaciones respetan el contexto autenticado, la versión del borrador y la
idempotencia durable.

## Modelo

- `SalesDrafts`: encabezado, contexto validado, actor, nombre, referencia,
  observación, estado y versión.
- `SalesDraftLines`: snapshot comercial de cada línea.
- `SalesDraftMutationReceipts`: idempotencia durable por negocio.

`OrderDrafts` no se reutiliza: pertenece al flujo conversacional y no representa
una venta activa de caja.

El borrador activo es único por `BusinessId + RegisterId + UserId`. Dos usuarios
pueden vender simultáneamente en la misma caja, pero cada uno conserva su venta.
Dos pestañas del mismo usuario comparten el borrador y envían `ExpectedVersion`.

Al poner una venta en espera, el servidor cambia el borrador activo a
`Temporary` y crea el nuevo borrador activo dentro de la misma transacción. Al
recuperarla, solo reemplaza un borrador activo vacío; nunca sobrescribe una venta
en curso.

## Seguridad y contexto

La API obtiene `UserId` y `TenantId` del JWT. El body no puede sustituirlos.
Antes de consultar o modificar se valida el negocio del tenant, sede, caja,
bodega derivada, permiso `sales.create` y que la caja online no tenga un
enrolamiento POS Edge activo.

Las ventas en espera se filtran por negocio, caja y usuario. Un usuario no puede
recuperar ni eliminar la venta en espera de otro usuario.

## Contrato conectado

- `POST /api/commerce/v1/pos/drafts/active`
- `POST /api/commerce/v1/pos/drafts/products/search`
- `POST /api/commerce/v1/pos/drafts/customers/search`
- `POST /api/commerce/v1/pos/drafts/temporaries/search`
- `POST /api/commerce/v1/pos/drafts/{draftId}/items`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/quantity`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/discount`
- `POST /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/remove`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/customer`
- `POST /api/commerce/v1/pos/drafts/{draftId}/reset`
- `POST /api/commerce/v1/pos/drafts/{draftId}/pause`
- `POST /api/commerce/v1/pos/drafts/temporaries/{draftId}/recover`
- `POST /api/commerce/v1/pos/drafts/temporaries/{draftId}/remove`

Cada mutación exige `Idempotency-Key` y `ExpectedVersion`. Una repetición exacta
no vuelve a aplicar el efecto. Reutilizar la clave con otro comando produce
`SalesDraftIdempotencyConflict`. Una versión vieja produce
`SalesDraftVersionConflict`.

## Captura, precios e inventario

La captura acepta código de barras, código interno, `Sku`, referencia e
identificadores alternos. En cada línea se congela código, descripción, unidad,
impuesto, precio base, precio aplicado, moneda y origen.

Dentro de `SalesDraftLines`, precio aplicado y descuentos se almacenan sin IVA
para el cálculo interno; los campos `Net`, `Tax` y `Total` exponen la composición
vigente. La misma línea conserva además `PublicUnitPrice`,
`PublicDiscountAmount` y `PublicLineTotal`, el snapshot público exacto fijado al ingresar o editar el
producto. Al guardar o actualizar un pedido, el adaptador online copia ese
snapshot sin recalcularlo. Al recuperar un pedido conserva sus importes públicos
y deriva únicamente la composición interna con la tarifa vigente.

Al completar una venta, la respuesta autoritativa instala inmediatamente el
`nextDraft` vacío y retira de la interfaz cliente, líneas, pagos y pedido de
origen. La impresión consume en paralelo el comprobante emitido: no prolonga el
lease del pedido, no bloquea el borrador siguiente y un fallo de impresión se
reporta como tal sin reinterpretar la emisión confirmada.

La resolución usa, en orden:

1. lista de precios exclusiva del cliente y su escala por cantidad;
2. canal de precios exclusivo del cliente, respetando exclusiones;
3. precio activo del negocio en `ProductPrices`;
4. precio base del producto como respaldo.

Seleccionar o retirar un cliente recalcula todas las líneas
transaccionalmente. Cambiar cantidad recalcula la escala de lista. Si la bodega
bloquea negativos, capturar o cambiar cantidad valida la disponibilidad en SQL
Server antes de modificar el borrador.

Las búsquedas usan paginación del servidor y nunca cargan todo el catálogo en
memoria. Productos se pueden encontrar por nombre, código interno, `Sku`,
referencia, código de barras e identificador alterno. Clientes se pueden
encontrar por identificación o nombre. Todos los resultados quedan limitados al
`BusinessId` validado.

Los buscadores de grillas y selectores remotos usan el mismo control: texto local,
300 ms de debounce, una sola consulta con el término confirmado y spinner dentro
de la caja mientras espera o consulta. `keepPreviousData` conserva la grilla y
evita bloquear la interacción durante la búsqueda.

### Camino crítico de captura

La captura directa por código o lector ejecuta una sola mutación. La pantalla no
hace una búsqueda de catálogo previa: envía selector, cliente y cantidad en el
mismo comando y el propietario servidor resuelve producto, lista o canal,
promociones e inventario dentro de la operación canónica. Una cantidad explícita
como `3*CODIGO` tampoco genera una segunda mutación de línea.

La búsqueda paginada queda reservada al selector visual de productos. En POS
Edge se conserva una única consulta remota de disponibilidad cuando la bodega
bloquea negativos, porque esa validación protege inventario central; la
resolución final de precios sigue perteneciendo al repricing del borrador.

El criterio de regresión del camino vacío es: una pulsación de Enter produce
cero solicitudes de búsqueda y exactamente una solicitud de alta. El costo de
recalcular las líneas existentes puede crecer con el borrador porque las reglas
de lista y promoción dependen de la cantidad, pero no puede existir trabajo
proporcional a productos ajenos ni viajes cliente-servidor adicionales.

Medición local reproducible del endpoint Edge con
`Selected_customer_channel_reprices_all_lines_when_accumulated_quantity_reaches_a_tier`
y el logger HTTP detallado: 105,8 ms para la primera captura después de iniciar
el host de prueba y 7,0 ms para la captura caliente siguiente. Estas cifras
validan el camino local y no incluyen ni prometen la latencia de la red de una
bodega que bloquee inventario negativo.

## Evidencia

- Solución y DACPAC: 0 errores y 0 advertencias.
- 38 pruebas de integración correctas con SQL Server real y despliegue DACPAC.
- 109 pruebas de fundación correctas.
- El borrador sobrevive al cierre del primer cliente HTTP.
- La venta en espera sobrevive a un nuevo cliente HTTP y se recupera completa.
- Pausar repetidamente con la misma clave no crea dos ventas activas.
- No se puede recuperar una venta sobre otra venta activa con líneas.
- La venta en espera se elimina sin afectar el nuevo borrador activo.
- Eliminar una venta en espera exige `sales.drafts.paused.delete`; sin el permiso,
  el flujo solicita autorización POS y consume la aprobación para esa venta y
  operación exactas.
- Idempotencia y concurrencia optimista evitan dobles efectos.
- Cliente, lista, descuento, cantidad y eliminación recorren API y SQL Server.
- Abrir **Editar líneas** no exige permiso. Descripción usa
  `sales.lines.change-description`; costo documental permitido, margen,
  descuentos y precio usan `sales.change-price`; el descuento general prorrateado
  usa `sales.lines.prorated-discount`. Las tres capacidades se validan en servidor
  y solo pertenecen inicialmente al administrador.
- El costo de una línea con inventario permanece inmutable. Costo y margen solo
  se editan en productos que no manejan inventario.
- `sales.lines.cost-margin.read` controla únicamente la lectura: sin él se
  conservan las cajas del editor con guiones para no alterar el diseño. La edición
  de costo o margen exige además `sales.change-price` y que la línea no maneje
  inventario.
- La vista de devoluciones abierta desde POS usa el negocio de la estación como
  contexto explícito; no depende del estado del selector del dashboard.
- Una bodega que bloquea negativos impide captura sin existencia.
- Usuario sin permiso recibe solicitud de autorización en acciones sensibles y un
  contexto ajeno recibe `403`.

## Pendiente inmediato

La paridad online de la pantalla todavía no está terminada. Falta conectar el
cliente web a estos contratos y completar cobro, emisión, numeración online,
procesamiento, impresión desde el documento confirmado y recuperación visual al
reabrir la pantalla.

El modo online no se expone en la interfaz antes de cerrar ese recorrido. Así se
evita ofrecer una caja que permita capturar productos, pero no terminar y
persistir una factura real.
