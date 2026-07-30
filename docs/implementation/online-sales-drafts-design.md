# Borrador durable de venta online

Fecha: 2026-07-29

## Resultado

El navegador sin POS Edge ya dispone de un propietario servidor para la venta
en curso. El estado no depende de React, `localStorage` ni del equipo que abrió
la pantalla.

## Modelo

- `SalesDrafts`: encabezado, contexto validado, actor, estado y versión.
- `SalesDraftLines`: snapshot comercial de cada línea.
- `SalesDraftMutationReceipts`: idempotencia durable por negocio.

`OrderDrafts` no se reutiliza: pertenece al flujo conversacional y no representa
una venta activa de caja.

El borrador activo es único por `BusinessId + RegisterId + UserId`. Dos usuarios
pueden vender simultáneamente en la misma caja, pero cada uno conserva su venta.
Dos pestañas del mismo usuario comparten el borrador y envían `ExpectedVersion`.

## Seguridad y contexto

La API obtiene `UserId` y `TenantId` del JWT. El body no puede sustituirlos.
Antes de abrir o modificar un borrador se valida el negocio del tenant, sede,
caja, bodega derivada, permiso `sales.create` y que la caja online no tenga un
enrolamiento POS Edge activo.

## Contrato conectado

- `POST /api/commerce/v1/pos/drafts/active`
- `POST /api/commerce/v1/pos/drafts/{draftId}/lines`
- `POST /api/commerce/v1/pos/drafts/{draftId}/capture`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/quantity`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/discount`
- `POST /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/remove`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/customer`
- `POST /api/commerce/v1/pos/drafts/{draftId}/reset`

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

## Evidencia

- Solución y DACPAC: 0 errores y 0 advertencias.
- 37 pruebas de integración correctas con SQL Server real y despliegue DACPAC.
- 109 pruebas de fundación correctas.
- El borrador sobrevive al cierre del primer cliente HTTP.
- Idempotencia y concurrencia optimista evitan dobles efectos.
- Cliente, lista, descuento, cantidad y eliminación recorren API y SQL Server.
- Una bodega que bloquea negativos impide captura sin existencia.
- Usuario sin permiso y contexto ajeno reciben `403`.

## Pendiente inmediato

La paridad online de la pantalla todavía no está terminada. Falta conectar
`OnlinePosClient` a estos contratos y completar búsqueda paginada de
productos/clientes, pausar, recuperar, cobrar, emisión, numeración online e
impresión desde el documento confirmado. El modo online no se expone en la UI
antes de cerrar ese recorrido.
