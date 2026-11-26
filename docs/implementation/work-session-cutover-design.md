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
impide duplicados por usuario web y por usuario/equipo enrolado. La sesión conserva
sede, usuario, equipo opcional, apertura, actividad y cierre. La bodega pertenece a
cada documento y no forma parte de la identidad de la sesión.

Los movimientos de medios de pago se atribuyen a la sesión. El cierre genera una
evidencia durable en `WorkSessionClosures`. Login y logout no abren ni cierran la
sesión de trabajo; entrar al punto de venta abre o recupera la única sesión del
usuario y solo el cierre operativo explícito la termina. Esto reemplaza arqueos,
turnos y cierres vinculados a una caja física.

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

La base SQLite conserva el equipo, autenticación local, sesiones operativas, catálogo,
series, documentos, facturas temporales, outbox y recibos remotos. El login solo
autentica. Entrar al POS abre o recupera la sesión del usuario en ese dispositivo,
genera localmente su ID definitivo y encola su alta antes de aceptar documentos.
Cerrar y volver a abrir la aplicación recupera la venta activa y no consume otro
consecutivo. El servidor confirma el mismo ID; nunca genera ni sustituye uno de Edge.

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
- Usuario, sede, bodega del documento y dispositivo se vuelven a validar en backend.
- La cola Edge preserva orden por `WorkSession`: su apertura precede sus documentos
  y el cierre espera todos los documentos de esa misma sesión. Una sesión en reintento
  no bloquea aperturas ni documentos de otra sesión; cada flujo avanza contra sus
  propias dependencias. Si una instalación anterior dejó un documento físicamente
  después de su cierre, el ordenador restablece el orden semántico documento-cierre.
- Online y offline comparten el flujo comercial, fiscal y de procesamiento documental.


## Recaudo de pedidos y despacho (decisión del 26 de noviembre de 2026)

El usuario retiró la excepción de recaudo diferido por origen pedido. Toda venta
nueva usa `CollectionMode=Session`, también al facturar pedidos individualmente,
en lote o después de recuperarlos en POS web o Edge. `SalesDocuments.WorkSessionId`
es la sesión de quien factura y el pago elegido participa en su cierre. El crédito
sigue siendo cartera; no se inventa efectivo para una factura a crédito.

Los emisores reutilizan el valor predeterminado del contrato `PosSaleUploadRequest`.
Se eliminó la regla que elegía `Dispatch` por tener `SourceOrderId` y las dos
prevalidaciones redundantes de esa regla. El motor financiero ya escribe el
`SalePayment` en la sesión cuando la atribución es `Session`: se conserva ese
camino y no se agrega otra escritura ni un traslado de dinero.

La atribución guardada en documentos ya emitidos y sus repeticiones idempotentes
no se recalcula. `Dispatch` permanece legible y validado para esos documentos;
esta decisión no reclasifica históricos ni vuelve a recibir pagos. El despacho
conserva la ruta existente de venta previamente cobrada: confirmar la entrega o
mantener el medio actual no duplica el ingreso en la sesión del transportador.
El vínculo canónico del pedido, la reserva de inventario y los motores contable,
fiscal y de reporting conservan sus contratos.


### Recaudo de cartera desde la app de transportador

Decisión aprobada el 26 de noviembre de 2026: cualquier forma configurada de
crédito es elegible, con independencia de su nombre. La app permite seleccionar
un medio de recaudo únicamente si la factura mantiene saldo de cartera disponible;
una factura pagada solo confirma entrega y conserva su pago. Los abonos previos
se respetan y el recaudo no supera el saldo disponible.

Confirmar entrega registra el recaudo para la sesión de despacho del transportador,
desglosado por forma, sin reescribir la sesión de quien facturó. El usuario confirmó
expresamente que la cartera y los efectos financieros se aplican al aprobar el
cierre administrativo, conservando ese punto de aplicación actual. Se reutiliza
el pago nativo vinculado a la obligación y el único motor financiero; no se repite
la venta, el ingreso comercial, los impuestos ni otra reclasificación del mismo
importe. El registro y la aplicación deben volver a validar el saldo y conservar
idempotencia frente a pagos por otros canales. Este objetivo no cambia el
comportamiento ya existente de facturar a crédito con efectivo recibido en cero.

### Aceptación conjunta de la corrección y su proyección

Las correcciones preparadas en Caja o Despacho no alteran la proyección antes de
confirmar la conciliación administrativa. Esa confirmación guarda la fuente
financiera y los trabajos durables contable y de reporting en una misma
transacción SQL, mediante sus writers canónicos. No se realizan dos envíos
independientes que puedan dejar uno de los trabajos sin registrar.

Los motores conservan su procesamiento asíncrono: reporting solo aplica la
corrección cuando el resultado financiero es `Posted` o
`CommercialEffectsApplied`. Una entrega anticipada no cambia los hechos ni
consume un intento; el observador contable activa el trabajo durable existente
después del resultado financiero. Esto conserva el ajuste idempotente de medios
sin proyectar una corrección financiera fallida. La proyección de la venta
original continúa al facturar y no espera al cierre.

La corrección individual desde Facturas conserva su punto de aceptación directa:
para un comprobante de venta con atribución `Session`, sesión cerrada y conciliación
pendiente, Guardar acepta únicamente esa corrección con `sales.payments.correct`.
No exige conciliar los demás documentos. El motor financiero aplica la corrección
y reporting espera ese resultado; la conciliación posterior reconoce lo aplicado
y no lo ejecuta otra vez. No se reescribe el documento fiscal original. Los
documentos ya emitidos con atribución `Dispatch` conservan su revisión existente
en el despacho; esta implementación no convierte su contrato histórico a `Session`.
