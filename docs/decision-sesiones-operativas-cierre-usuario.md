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

Cada usuario mantiene una sesión web abierta y puede mantener una sesión local por
equipo enrolado. Pestañas y navegadores web recuperan el mismo `WorkSessionId` web;
un Edge recupera únicamente el suyo. Cambiar la autenticación activa no abre ni
cierra trabajo. Una nueva sesión del mismo canal/dispositivo solo puede comenzar
después del cierre operativo explícito de la anterior.

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
verifican comprobante por comprobante desde los pagos, devoluciones y movimientos que
ya pertenecen a la sesión; cada uno queda marcado como verificado o no encontrado. El
servidor vuelve a obtener esas fuentes dentro de la transacción, exige una decisión
exactamente una vez por comprobante y calcula el valor verificado, por lo que el cliente
no puede omitir comprobantes ni alterar sus importes. Un faltante y un sobrante se pueden
cruzar mediante la reclasificación existente, sin crear otro motor ni otra conciliación.

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
