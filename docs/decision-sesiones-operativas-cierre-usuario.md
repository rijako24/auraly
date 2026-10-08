# Decisión: sesiones operativas y cierre por usuario

Fecha: 2026-07-31  
Estado: obligatoria  
Prevalencia: reemplaza la sección de efectivo y arqueo de `decision-eliminar-caja-contexto-usuario-dispositivo.md` y cualquier diseño basado en cierre, entrega o arqueo de caja.

## Decisión

Auraly no modela arqueo, entrega ni cierre de caja. La unidad de responsabilidad operativa y financiera es `WorkSession`.

Una sesión se abre con:

- `WorkSessionId`;
- `TenantId`;
- `BusinessId`;
- `WarehouseId` solo en documentos; es nullable e histórico en `WorkSessions`;
- `UserId`;
- `DeviceId` opcional;
- fecha y hora de apertura;
- estado.

Ventas, pagos, devoluciones, anulaciones y movimientos de efectivo conservan el `WorkSessionId` que los originó.

## Cierre

Al cerrar la sesión operativa se genera un cierre por usuario con:

- ventas y devoluciones;
- totales por medio de pago;
- ventas, abonos a cartera y pagos a proveedores guardan la transferencia con
  el mismo código `Transfer`; el cierre agrega esos movimientos en un solo medio;
- el corte de datos normaliza los abonos, pagos, movimientos y cierres anteriores
  a `Transfer` antes de publicar el código nuevo. Conserva el cierre original en
  auditoría y su fuente contable inmutable. Si ya se contabilizó una diferencia
  falsa, exige primero un comprobante manual correctivo contabilizado. Después
  del corte no hay una ruta de compatibilidad para `BankTransfer`;
- efectivo esperado y contado cuando corresponda;
- diferencias;
- anulaciones y reimpresiones relevantes;
- fecha, sede, usuario y dispositivo opcional; las bodegas quedan en el detalle de
  los documentos, no como propietarias del cierre.

El cierre puede imprimir una tirilla denominada **Cierre de sesión**. Nunca se muestra “cierre de caja”.

La vista central de cierres consulta esa misma sesión canónica. Si está abierta,
muestra sede, usuario, apertura y duración, y abre el mismo diálogo de conteo y
cierre utilizado por el POS. Cerrar desde esa vista no cierra la autenticación: el
próximo flujo que maneje dinero abre o recupera una nueva sesión según estas reglas.
Si no existe una sesión abierta, la vista informa **Caja cerrada** y puede mostrar el
último cierre como referencia, sin fabricar una sesión.

El contador de denominaciones es una ayuda de captura y puede imprimir una tirilla
independiente con empresa, sede, usuario, fecha, cantidad y subtotal por denominación
y total contado. Imprimir el conteo no abre, cierra ni modifica la sesión.

El siguiente usuario abre una nueva sesión en el mismo equipo. Si la aplicación se interrumpe, la sesión abierta se recupera y no se reemplaza silenciosamente.

Si la sesión conserva ventas pausadas, el cierre exige el permiso ordinario
`work-sessions.close-with-paused-sales`. Un usuario que solo tenga
`work-sessions.close` debe obtener autorización POS antes de abrir el conteo y antes
de cerrar. El servidor vuelve a comprobar las ventas pausadas dentro de la transacción
de cierre para cubrir carreras; el rol Cajero no recibe este permiso por defecto y el
rol Administrador sí lo recibe siempre.

Cada usuario mantiene una sesión web abierta y puede mantener una sesión local por
equipo enrolado. Pestañas y navegadores web recuperan el mismo `WorkSessionId` web;
un Edge recupera únicamente el suyo. Cambiar la autenticación activa no abre ni
cierra trabajo. Una nueva sesión del mismo canal/dispositivo solo puede comenzar
después del cierre operativo explícito de la anterior.

En la caja preparada, el abono, pago a proveedor o reintegro de una devolución
solo entra al cierre local después de la aceptación del servidor. La proyección
local se termina aunque el navegador cierre su petición justo después de esa
aceptación; una respuesta inválida se deja identificada para reintentar con la
misma clave, sin dar por aceptado un movimiento que no confirmó el servidor.

## Eliminación

Se eliminan del modelo canónico:

- `CashSession`;
- `CashierShift`;
- `CashCount` y sus cursores;
- entrega de cajero;
- cierre de caja;
- arqueo de caja;
- permisos, rutas y textos asociados a una caja.

La conciliación de efectivo se conserva como parte del cierre de sesión del usuario.

La conciliación posterior reutiliza el mismo `WorkSessionClosure` y su snapshot
inmutable. El efectivo se verifica como un total contado. Tarjeta y transferencia se
verifican comprobante por comprobante desde los pagos y movimientos que
ya pertenecen a la sesión; cada uno queda marcado como verificado o no encontrado. El
detalle de ventas y devoluciones es informativo en todos los medios: no exige esos dos estados.
Los abonos, pagos a proveedores, entradas y salidas en efectivo sí conservan la
verificación individual. El esperado de efectivo usado al conciliar es el mismo
`ExpectedCash` persistido al cerrar la sesión, incluida su base inicial; no se
compara el conteo físico con el neto aislado de los movimientos de efectivo. El
servidor vuelve a obtener esas fuentes dentro de la transacción, exige una decisión
exactamente una vez por comprobante y calcula el valor verificado de los medios no
efectivos; el efectivo se toma del conteo físico confirmado. El cliente no puede omitir
comprobantes ni cambiar los importes de los documentos mediante el conteo. Un faltante y un sobrante se pueden
cruzar mediante la reclasificación existente, sin crear otro motor ni otra conciliación.
La confirmación coteja las claves y suma los importes en SQL en un solo viaje; no
materializa todas las filas de la sesión en memoria del servidor.
Las devoluciones y salidas conservan su signo en medios distintos de efectivo;
su neto verificado puede ser negativo. El efectivo físico contado permanece no negativo.

## Correcciones durante la conciliación

El diálogo existente conserva **Verificado** y **No encontrado** por comprobante.
**Corregir** es una tercera acción, no un tercer estado de verificación: abre una
ventana breve con el documento original, medio e importe registrados, y permite
indicar el medio y el importe realmente recibido o entregado, con motivo obligatorio.
La fila muestra después ambos valores y el efecto en el cuadre. No se presentan
todas las filas como campos editables. Los seis grupos de efectivo —facturas y
comprobantes, devoluciones, abonos a cartera, pagos a proveedores, entradas y salidas—
muestran cantidad y total y se expanden por separado. Al expandir, se solicitan
100 movimientos por página al servidor y el siguiente lote se carga al llegar al
final; nunca se traen todas las facturas para calcular un total visible. Los
totales y cantidades del grupo cubren toda la sesión aunque solo se haya cargado
la primera página. Las verificaciones ya decididas se conservan al cargar más
páginas. El efectivo físico confirmado puede corregirse como conteo, sin cambiar
por ello el importe de un documento.

Por compatibilidad con versiones de escritorio ya instaladas, la lectura
`/payment-verifications` conserva temporalmente su respuesta de arreglo completo.
La vista nueva usa `/payment-verifications/page`, con resumen agregado y páginas
de máximo 100 filas filtradas en SQL. Ambas lecturas comparten la misma consulta
y autorización; no escriben ni tienen motores distintos. Se retira la lectura
anterior cuando no haya clientes instalados que consuman ese contrato. No debe
utilizarse en pantallas nuevas por su costo para sesiones grandes.

La corrección de un medio conserva el comprobante original y su snapshot; registra
el movimiento corregido asociado a su identificador y aplica la reclasificación por
la fuente contable `WorkSessionClosureReconciliation` existente. Corregir un importe
no es un simple cambio del total del cierre: se valida el documento fuente y el
motor contable registra un ajuste ligado a la aplicación original y actualiza el
saldo de CxC/CxP que corresponda. El recibo y la aplicación originales permanecen
inmutables para auditoría. Si la nueva cuantía excede
lo aplicable a la factura, falta tercero para abrir cartera, o no se puede precisar
qué aplicación corregir, la operación se bloquea y pide resolver esa situación; no
se transforma la diferencia en ingreso/gasto ni se inventa un abono. Los valores
fiscales, productos e impuestos de la factura no se reescriben.

La confirmación de la conciliación es la única aceptación de sus correcciones: el
servidor vuelve a leer y bloquear las fuentes, valida importes y saldos, registra
actor, fecha, motivo, antes/después e idempotencia, y crea el trabajo contable
canónico en la misma transacción. Un fallo de aceptación no modifica parcialmente
cartera, caja ni comprobantes; un fallo posterior del procesamiento contable queda
visible y reintentable por el motor existente. Tras confirmar, el cierre y sus correcciones quedan
inmutables. Las diferencias genuinas que subsistan se muestran como sobrante o
faltante con observación y motivo. No se crea todavía cuenta por cobrar al empleado.

## Pruebas obligatorias

1. Dos usuarios consecutivos en el mismo Edge crean sesiones diferentes.
2. Una caída y reinicio recupera la sesión abierta.
3. Toda venta y pago queda asociado al usuario y sesión correctos.
4. El cierre totaliza por medio de pago sin mezclar sesiones.
5. La tirilla identifica usuario, sede y dispositivo opcional, y no contiene “caja”.
6. No existen tablas, APIs ni componentes canónicos de arqueo o cierre de caja.
7. Tarjeta y transferencia no se concilian sin decidir cada comprobante individual y
   el total verificado coincide con los importes canónicos del servidor.
8. Cerrar desde la vista central deja la autenticación activa y el siguiente flujo de
   dinero abre una sesión nueva.
9. La tirilla de conteo reproduce cada denominación y el total sin alterar el cierre.
