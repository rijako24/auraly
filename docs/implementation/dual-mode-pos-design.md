# Facturación unificada: navegador y equipo con respaldo offline

Fecha: 2026-07-29

## Decisión

Auraly expone una sola experiencia de facturación y selecciona el adaptador por la
forma en que se abrió el equipo:

- Un navegador sin enrolamiento usa `Auraly.Api` y SQL Server en línea.
- La aplicación instalada y enrolada usa el host local de Auraly POS. El host
  conserva SQLite, impresión, balanza, catálogo, numeración y outbox, y sincroniza
  con el servidor cuando existe conexión.

No se pregunta al cajero el modo en cada inicio y no se cambia de persistencia a
mitad de una factura. La ausencia de Internet no se confunde con la ausencia de
POS Edge.

## Selección inicial

Sin una sesión local de Edge, Facturación usa la sesión web autenticada. Si no hay
una caja recordada, solicita:

1. negocio;
2. sede;
3. caja.

La bodega nunca se selecciona independientemente: se deriva de la caja y se
muestra para confirmación. La preferencia del navegador solamente recuerda el
`RegisterId`; el servidor vuelve a validar tenant, negocio, sede, bodega, estado
y permisos en cada operación.

El usuario puede cambiar de caja en modo web. Un equipo enrolado cambia su caja
offline únicamente mediante un nuevo enrolamiento controlado.

## Habilitación del respaldo offline

“Habilitar operación sin conexión” es una acción administrativa, no un interruptor
de sesión. Debe validar caja, dispositivo, numeración disponible y permisos antes
de provisionar secretos, usuarios, catálogo y rangos. El primer bootstrap bloquea
la venta offline hasta completar y promover el catálogo. Las puestas al día
posteriores ocurren en segundo plano.

## Concurrencia y numeración

El navegador obtiene consecutivos operativos y fiscales dentro de la transacción
de emisión del servidor. Edge consume rangos previamente reservados. Un rango
offline no puede ser consumido simultáneamente por el servidor.

La caja configurada es una entidad de negocio; el dispositivo es otra entidad.
Una venta conserva su origen (`OnlineUser` o `EdgeDevice`) y el actor que la
emitió. No se crean dispositivos ficticios para ventas web.

## Adaptadores

La vista POS depende de un contrato común para salud, catálogo, borradores,
temporales, captura, cobro y emisión.

- `PosEdgeClient` llama al host local.
- `OnlinePosClient` llama a la API mediante el BFF autenticado.

El indicador visible expresa conectividad con Auraly Server. “POS Edge” permanece
como detalle técnico y no se muestra al cajero.

## Reglas de transición

- Navegador sin Edge: entra online automáticamente.
- Aplicación enrolada con Internet: opera mediante Edge y sincroniza de inmediato.
- Aplicación enrolada sin Internet: Edge continúa con los recursos provisionados.
- Un borrador iniciado en un adaptador termina en ese adaptador.
- Habilitar offline requiere enrolamiento; no migra silenciosamente borradores.
- Deshabilitar o reenrolar exige outbox vacía o intervención explícita.

