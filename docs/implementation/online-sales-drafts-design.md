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
- `POST /api/commerce/v1/pos/drafts/{draftId}/lines`
- `POST /api/commerce/v1/pos/drafts/{draftId}/capture`
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

## Evidencia

- Solución y DACPAC: 0 errores y 0 advertencias.
- 38 pruebas de integración correctas con SQL Server real y despliegue DACPAC.
- 109 pruebas de fundación correctas.
- El borrador sobrevive al cierre del primer cliente HTTP.
- La venta en espera sobrevive a un nuevo cliente HTTP y se recupera completa.
- Pausar repetidamente con la misma clave no crea dos ventas activas.
- No se puede recuperar una venta sobre otra venta activa con líneas.
- La venta en espera se elimina sin afectar el nuevo borrador activo.
- Idempotencia y concurrencia optimista evitan dobles efectos.
- Cliente, lista, descuento, cantidad y eliminación recorren API y SQL Server.
- Una bodega que bloquea negativos impide captura sin existencia.
- Usuario sin permiso y contexto ajeno reciben `403`.

## Pendiente inmediato

La paridad online de la pantalla todavía no está terminada. Falta conectar el
cliente web a estos contratos y completar cobro, emisión, numeración online,
procesamiento, impresión desde el documento confirmado y recuperación visual al
reabrir la pantalla.

El modo online no se expone en la interfaz antes de cerrar ese recorrido. Así se
evita ofrecer una caja que permita capturar productos, pero no terminar y
persistir una factura real.
