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
Dos pestañas del mismo usuario comparten el borrador y deben enviar
`ExpectedVersion`.

## Seguridad y contexto

La API obtiene `UserId` y `TenantId` del JWT. El body no puede sustituirlos.
Antes de abrir el borrador se valida:

- negocio del tenant;
- sede activa;
- caja activa;
- bodega derivada de la caja;
- permiso `sales.create`;
- ausencia de un enrolamiento POS Edge activo para esa caja online.

## Contrato conectado

- `POST /api/commerce/v1/pos/drafts/active`
- `POST /api/commerce/v1/pos/drafts/{draftId}/lines`
- `PUT /api/commerce/v1/pos/drafts/{draftId}/lines/{lineId}/quantity`
- `POST /api/commerce/v1/pos/drafts/{draftId}/reset`

Cada mutación exige `Idempotency-Key` y `ExpectedVersion`. Una repetición exacta
no vuelve a aplicar el efecto. Reutilizar la clave con otro comando produce
`SalesDraftIdempotencyConflict`. Una versión vieja produce
`SalesDraftVersionConflict`.

## Snapshots y precios

Al agregar un producto se congela:

- código comercial;
- descripción;
- unidad;
- impuesto;
- precio base del negocio;
- moneda;
- origen del precio.

Se usa el precio activo de `ProductPrices` y, como respaldo, el precio base del
producto. El siguiente incremento conecta cliente, lista y canal antes de
habilitar la venta online completa.

## Evidencia

- DACPAC compila con 0 errores y 0 advertencias.
- La solución completa compila con 0 errores y 0 advertencias.
- Integración real con SQL Server y despliegue del DACPAC.
- El borrador sobrevive al cierre del primer `HttpClient`.
- El mismo comando idempotente no incrementa cantidad dos veces.
- Dos mutaciones sobre la misma versión producen un éxito y un conflicto.
- Usuario sin permiso y contexto ajeno reciben `403`.
- Reiniciar la venta conserva el borrador anterior como eliminado y crea uno
  nuevo activo.

## Pendiente inmediato

No se considera terminada todavía la paridad online de la pantalla. Falta
conectar `OnlinePosClient` y los comandos de cliente, descuento, eliminar línea,
pausar, recuperar, cobrar, emitir e imprimir desde el documento confirmado.

