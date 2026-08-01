# Decisión definitiva: Auraly no modela cajas

Fecha: 2026-07-31  
Estado: obligatoria  
Prevalencia: reemplaza toda decisión anterior que use `Register`, `CashRegister`, `RegisterId`, caja online o caja offline como entidad de negocio.

## Modelo operativo

Auraly elimina el concepto de caja. Cada operación se identifica mediante:

- `TenantId`: empresa propietaria.
- `BusinessId`: sede.
- `WarehouseId`: bodega afectada.
- `UserId`: persona responsable.
- `DeviceId`: computador enrolado, cuando existe.
- `WorkSessionId`: contexto temporal de trabajo del usuario.

Un computador enrolado no es una caja. Es un dispositivo confiable que aporta almacenamiento local, impresión, balanza, credenciales, sincronización y operación offline.

## Contexto de facturación

Al abrir Facturación, el usuario selecciona la sede y la bodega autorizadas. La preferencia puede conservarse localmente, pero el backend valida siempre que:

1. la sede pertenece al tenant autenticado;
2. la bodega pertenece a la sede;
3. el usuario tiene permiso para trabajar allí;
4. el dispositivo pertenece al tenant, si la operación proviene de Edge.

Online y Edge usan el mismo contexto. Edge agrega `DeviceId` y capacidades locales. No se crea una caja para trabajar en línea.

## Numeración Auraly

La numeración operativa identifica una serie emisora, no una caja ni un usuario:

```text
<prefijo><código de serie>-<consecutivo de 8 dígitos>
```

Ejemplos:

- servidor online: `VTA00-00000042`;
- dispositivo Edge con código `03`: `VTA03-00000127`.

`00` queda reservado para la serie central del servidor. Al enrolar un dispositivo se le asigna un código corto, inmutable y único dentro del negocio. Ese código identifica la serie técnica offline; no representa un puesto físico ni devuelve el concepto de caja al dominio.

### Online

SQL Server consume el siguiente consecutivo de `BusinessId + DocumentType + SeriesCode=00` dentro de la transacción que confirma el documento. Dos usuarios o procesos concurrentes reciben números distintos. No se usa `MAX + 1`.

### Edge

Cada `DeviceId + BusinessId + DocumentType` mantiene su propia serie y cursor local durable. POS Edge consume el consecutivo en la misma transacción SQLite que congela el documento y escribe la outbox. No necesita descargar bloques de numeración operativa.

Un reintento del mismo `DocumentId` devuelve el número ya asignado. Revocar, reinstalar o cambiar el usuario del dispositivo no permite reutilizar números. El servidor valida que el `SeriesCode` pertenece al dispositivo que cargó el documento.

Cuando un Edge tiene Internet continúa usando su serie local. No alterna con la serie `00`, porque cambiar de cursor al caer la red abriría carreras y renumeraciones.

### Usuario y sesiones simultáneas

`UserId` identifica al responsable humano y nunca posee una serie. Un usuario puede mantener sesiones autorizadas en más de un equipo sin producir colisiones. Prohibir inicios de sesión simultáneos no se usará como mecanismo de numeración.

Cada documento conserva por separado `SoldByUserId` y el emisor material (`Server` o `DeviceId`). Una política futura de sesión única sería una decisión de seguridad, no de consecutivos.

## Numeración fiscal DIAN

La numeración fiscal permanece separada del número Auraly. El código del dispositivo no se agrega al número DIAN salvo que forme parte del prefijo oficialmente autorizado.

Para emitir fiscalmente offline, el dispositivo debe tener una serie fiscal exclusiva, vigente y autorizada, con cursor durable. Dos emisores desconectados no pueden compartir un mismo cursor fiscal. La autorización pertenece al negocio y la asignación técnica al dispositivo.

La habilitación legal de la emisión sin conexión debe validarse contra la normativa DIAN vigente. Evitar duplicados técnicamente no demuestra por sí solo cumplimiento tributario.

## Ventas y snapshot

Toda venta conserva como mínimo `BusinessId`, `WarehouseId`, `SoldByUserId`, `DeviceId` opcional y `WorkSessionId`.

Se elimina `RegisterId` de:

- venta y snapshot;
- borradores y ventas pausadas;
- pedidos y reclamaciones;
- pagos y movimientos de efectivo;
- series operativas y fiscales;
- autenticación de dispositivo;
- catálogo POS;
- filtros y reportes.

## Efectivo y arqueo

La responsabilidad del efectivo es por usuario y contexto de trabajo. Una sesión de efectivo identifica negocio, bodega, usuario, dispositivo opcional, fecha de negocio, apertura, movimientos y cierre.

Cerrar sesión de la aplicación no obliga a cerrar el efectivo. La entrega o arqueo sigue siendo opcional y requiere permisos. Los informes consolidan por usuario, sede, bodega, dispositivo y fecha.

## Dispositivo enrolado

`PosDevices` se transforma en `EnrolledDevices` y pertenece al tenant. No queda fijado permanentemente a una sede o bodega.

El dispositivo conserva:

- identidad, credencial, estado y revocación;
- nombre descriptivo y código corto de serie;
- capacidades de impresión y balanza;
- configuración y datos locales;
- cursores de sincronización y numeración;
- series fiscales asignadas;
- último contacto.

Cambiar de sede o bodega exige catálogo y serie válidos para ese contexto antes de emitir offline.

## Eliminación física

No se dejarán alias, columnas nulas ni entidades de compatibilidad:

- eliminar `CashRegisters` y el identificador fuerte `RegisterId`;
- eliminar `OnlineRegister` y rutas `/cash/registers/*`;
- eliminar claims, filtros y textos de caja;
- reconstruir relaciones mediante usuario, dispositivo, negocio, bodega y sesión.

## Pruebas de aceptación

1. No existe `RegisterId` en código canónico ni esquema.
2. No existe tabla `CashRegisters`.
3. Un usuario online factura eligiendo negocio y bodega.
4. Dos ventas online concurrentes no duplican números.
5. Un usuario puede vender desde dos sesiones sin colisión.
6. Dos Edge tienen códigos y consecutivos independientes.
7. Reiniciar Edge no reinicia ni duplica su cursor.
8. Una venta conserva usuario y dispositivo originales.
9. Cambiar bodega aplica inventario y política de negativos correctos.
10. El número DIAN nunca se deriva libremente del dispositivo.
11. Arqueos separan correctamente las ventas por usuario.
12. Build, DACPAC, SQL Server, POS Edge y E2E pasan sin compatibilidad legacy.
