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

Una sesión de trabajo pertenece a un usuario, una sede y una bodega. El equipo que
originó una operación se conserva en el documento, pero no es propietario de la
sesión. El mismo usuario no puede mantener dos sesiones abiertas y recupera la
existente al autenticarse desde otro cliente. Un equipo enrolado tampoco puede
mantener dos sesiones abiertas.

## Online y offline

- Online: el usuario selecciona sede y bodega, abre una sesión sin dispositivo y el
  servidor asigna los consecutivos de forma transaccional.
- Edge: el equipo se enrola para un tenant, sincroniza su configuración y añade su
  `DeviceId` a la sesión. El login, catálogo, borradores, series, facturas y outbox
  necesarios permanecen disponibles localmente.

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
