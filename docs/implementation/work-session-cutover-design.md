# Corte a sesiones de trabajo y equipos enrolados

Fecha: 2026-08-02

## Decisión cerrada

Auraly Commerce no usa una entidad de caja. El contexto de una venta se compone de:

- `TenantId`: empresa propietaria de la cuenta.
- `BusinessId`: sede en la que se realiza la operación.
- `WarehouseId`: bodega que recibe los movimientos.
- `UserId`: usuario autenticado que ejecuta la venta.
- `WorkSessionId`: sesión operativa del usuario.
- `DeviceId`: equipo enrolado, únicamente cuando se utiliza POS Edge.

Una venta web usa un `WorkSession` sin dispositivo. Una venta offline usa el mismo
modelo y añade el equipo enrolado. El equipo no es una caja ni es dueño de la venta;
solo aporta identidad local, credenciales, sincronización y numeración offline.

## Sesión de trabajo

`WorkSessions` es el límite operativo común para ventas online y Edge. SQL Server
impide que un usuario tenga dos sesiones abiertas y que un equipo enrolado tenga dos
sesiones abiertas. La sesión conserva sede, bodega, usuario, equipo opcional, apertura,
actividad y cierre.

Los movimientos de medios de pago se atribuyen a la sesión. El cierre genera una
evidencia durable en `WorkSessionClosures`; cerrar sesión de usuario cierra también su
sesión de trabajo abierta. Esto reemplaza arqueos, turnos y cierres vinculados a una
caja física.

## Espacio de venta

`SalesWorkspace` resuelve las sedes y bodegas autorizadas para el usuario:

- `GET /api/commerce/v1/pos/workspace/bootstrap`
- `GET /api/commerce/v1/pos/workspace/options`
- `POST /api/commerce/v1/pos/workspace/select`

La selección se valida en backend contra el tenant autenticado, las asignaciones del
usuario y la pertenencia de la bodega a la sede. El frontend no puede fabricar un
contexto enviando identificadores arbitrarios.

## Numeración

La numeración operativa y la fiscal siguen separadas:

- Servidor online: serie operativa activa con `DeviceId = NULL`, código `00`.
- POS Edge: serie operativa asociada al `DeviceId`, capaz de operar offline y con un
  código de equipo distinto de `00`.
- Serie fiscal online: `EmitterKind = Server`, sin dispositivo.
- Serie fiscal Edge: `EmitterKind = Device`; puede existir sin asignación mientras es
  una reserva de enrolamiento, pero queda asociada antes de emitir.

El usuario no es parte del número fiscal. Dos solicitudes online concurrentes reciben
consecutivos distintos mediante la asignación transaccional del servidor. El equipo
permite generar consecutivos locales sin conexión.

## POS Edge

La base SQLite conserva el equipo, la sesión del usuario, catálogo, series, documentos,
facturas temporales, outbox y recibos remotos. El login offline abre o recupera una
sesión local para el mismo usuario. Cerrar y volver a abrir la aplicación recupera la
venta activa y no consume otro consecutivo.

Las dos lecturas internas de la columna histórica de dispositivo existen solamente en
el actualizador de esquema SQLite. No forman parte del dominio ni de contratos nuevos;
permiten actualizar bases creadas por versiones anteriores sin borrar documentos.

## Corte de SQL Server

`20260802_RemoveCashRegisterContext.sql` ejecuta una transformación unidireccional:

1. Convierte `PosDevices` en `EnrolledDevices` y deriva su tenant.
2. Convierte turnos y sesiones históricas en sesiones de trabajo cerradas.
3. Conserva movimientos y el último conteo confirmado como evidencia de cierre.
4. Conserva ventas temporales y libera reclamos de pedidos.
5. Reasigna series operativas y fiscales al servidor o al equipo.
6. Elimina columnas y tablas cuyo propietario era la caja.
7. Conserva `SupervisorCredentials`; elimina concesiones efímeras ligadas a caja.

La transformación se ejecuta dentro de una transacción y se activa únicamente cuando
existe `PosDevices`. El cuerpo está aislado dinámicamente para que una publicación
interrumpida pueda reanudarse después de que la transformación ya haya terminado.

En el primer intento de corte DacFx puede detenerse por su comprobación preventiva,
calculada antes del predespliegue. La transformación ya es durable y la misma publicación
se repite con `BlockOnPossibleDataLoss=True`; al reanalizar el esquema transformado,
DacFx completa el modelo sin desactivar la protección.

## Invariantes verificadas

- No hay proyectos ni endpoints canónicos de Cash Register.
- Dominio y contratos usan `SalesWorkspace`, `WorkSession` y `DeviceId` opcional.
- La base del servidor no conserva `RegisterId`, `CashSessionId` ni `CashierShiftId`
  en las tablas comerciales migradas.
- El POS Edge no accede directamente a SQL Server.
- Usuario, sede, bodega y dispositivo se vuelven a validar en backend.
- Online y offline comparten el flujo comercial, fiscal y de procesamiento documental.
