# Decisión: sesiones de trabajo y equipos enrolados sin caja

Estado: aceptada
Fecha: 2026-08-02

## Contexto

El diseño anterior atribuía sede, bodega, numeración, turno y venta a una caja.
En una aplicación web ese agregado duplica el contexto que ya aportan el usuario, la
sede y la bodega, e impide usar la facturación online con naturalidad.

## Decisión

Se elimina `CashRegister` del modelo canónico de Auraly Commerce.

El contexto de facturación queda definido por:

- empresa: `TenantId`;
- sede: `BusinessId`;
- bodega: `WarehouseId`;
- actor: `UserId`;
- operación: `WorkSessionId`;
- equipo offline opcional: `DeviceId`.

Una sesión de trabajo pertenece a un tenant, un usuario, una sede y una bodega. El equipo que
originó una operación se conserva en el documento, pero no es propietario de la
sesión. Dentro del mismo tenant, el usuario no puede mantener dos sesiones abiertas
y recupera la existente al autenticarse desde otro cliente. Un equipo enrolado
tampoco puede mantener dos sesiones abiertas.

Un usuario de plataforma puede conservar el mismo `UserId` al administrar otros
tenants, pero ese selector administrativo no cambia el tenant propietario del usuario.
Punto de Venta usa siempre `AppUsers.TenantId`, conservado en el claim inmutable
`identity_tenant_id`. Si el tenant seleccionado no coincide, la entrada al POS se
rechaza antes de cargar la sede o abrir una sesión; nunca se cambia silenciosamente al
tenant propietario. Un usuario del tenant Auraly puede operar el POS de Auraly, pero
no el de un tenant que solamente está administrando. Por eso toda apertura,
recuperación, venta, movimiento y conciliación se resuelve por el `TenantId + UserId`
propietarios. La base de datos refuerza la pertenencia con una clave foránea compuesta
y la exclusividad con un índice único filtrado para sesiones abiertas.

La migración inicial puede encontrar sesiones históricas cerradas creadas por usuarios
de plataforma bajo un tenant ajeno. Se conservan únicamente como auditoría y las claves
foráneas compuestas de usuario/equipo se crean sin revalidar esas filas; aun sin confianza
retroactiva, SQL Server exige la relación en toda inserción o actualización futura. El
pipeline ignora solamente la diferencia de estado `WITH NOCHECK` para que DacFx no intente
reescribir el pasado. Esta excepción se retira cuando una auditoría confirme cero sesiones
históricas discordantes en todos los ambientes; no autoriza rutas nuevas ni fallbacks de
tenant.

El contexto operativo inmutable de una jornada es `TenantId + UserId + WorkSessionId`.
El `WorkSessionId` solo cambia después de un cierre explícito; un nuevo login desde
otro navegador reemplaza únicamente la sesión de autenticación y recupera la misma
jornada abierta del usuario en el tenant. Los documentos con contexto de caja deben
coincidir además en `BusinessId`, cuya pertenencia al tenant se refuerza con claves
foráneas compuestas.

La sesión de autenticación se identifica por `TenantId + UserId + ClientId`. El login
es el único flujo ordinario que reemplaza otra autenticación activa para ese usuario y
tenant. El middleware y las peticiones protegidas solo aceptan o deniegan; una
renovación presentada desde otro `ClientId` se rechaza sin revocar la sesión legítima.
También pueden finalizarla el logout explícito, su vencimiento o una desactivación
administrativa del usuario o tenant.

## Online y offline

- Online: el usuario selecciona sede y bodega, abre una sesión sin dispositivo y el
  servidor asigna los consecutivos de forma transaccional.
- Edge: el equipo se enrola para un tenant, sincroniza su configuración y añade su
  `DeviceId` a la sesión. El login, catálogo, borradores, series, facturas y outbox
  necesarios permanecen disponibles localmente.

Una aplicación instalada que todavía no está enrolada conserva el modo online: los
motivos de entrada y salida se consultan al servidor y los movimientos se confirman
en el motor documental de la API; el host local atiende los periféricos, incluida la
impresión directa de ventas online sin abrir el diálogo del navegador. Después
del enrolamiento, esos motivos se leen de SQLite y cada entrada o salida se confirma
primero en SQLite y su outbox para sincronizarse de forma idempotente. La UI selecciona
uno de estos clientes según el estado de enrolamiento y no mezcla sus escrituras.

La primera configuración muestra explícitamente si la instalación está enrolada y
ofrece el enrolamiento solo cuando todavía no lo está. Al confirmar el enrolamiento,
el Edge completa la descarga inicial antes de habilitar ventas y luego mantiene los
cambios mediante invalidaciones push recuperables. El desenrolamiento solo se ejecuta
desde la administración Athena con `tenants.devices.revoke`: se publica al grupo del
dispositivo, el Edge elimina únicamente su credencial protegida, conserva los datos
locales para auditoría y reinicia en modo online. Si el equipo estaba desconectado,
el rechazo de sus credenciales al reconectar produce el mismo corte local.

No existe un modo funcional distinto llamado caja. La diferencia es únicamente si la
sesión utiliza o no un equipo enrolado.

## Numeración

La serie operativa online usa código `00` y no tiene dispositivo. Cada equipo offline
usa una serie propia y un código de equipo distinto de `00`. La serie fiscal distingue
emisión `Server` y `Device`. El identificador interno, el número operativo y el número
fiscal siguen siendo conceptos separados.

## Cierre

El cierre es de la sesión del usuario. Los pagos y movimientos se atribuyen al
`WorkSessionId`; el cierre conserva totales y evidencia imprimible. No existen cierre
de caja, turno de caja ni arqueo propiedad de una caja.

## Compatibilidad

El proyecto SQL ejecuta un corte unidireccional que conserva dispositivos, documentos,
series, ventas temporales, movimientos y cierres históricos antes de retirar las tablas
y columnas anteriores. SQLite conserva una lectura de la columna histórica solamente
en su actualizador de esquema para no perder datos locales.

## Prevalencia

Esta decisión prevalece sobre cualquier ADR, especificación o ejemplo anterior que use
`CashRegister`, caja, turno de caja, serie por caja o selección de caja. Donde esos
documentos digan caja debe interpretarse equipo enrolado si se habla de capacidad
offline, o sesión de trabajo si se habla del usuario y su operación.
