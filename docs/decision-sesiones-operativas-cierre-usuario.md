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
- `WarehouseId`;
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
- fecha, sede, bodega, usuario y dispositivo opcional.

El cierre puede imprimir una tirilla denominada **Cierre de sesión**. Nunca se muestra “cierre de caja”.

El siguiente usuario abre una nueva sesión en el mismo equipo. Si la aplicación se interrumpe, la sesión abierta se recupera y no se reemplaza silenciosamente.

Un Edge permite una sola sesión operativa activa en la instalación. La web puede mantener sesiones concurrentes identificadas por `WorkSessionId`; esto no afecta la numeración documental.

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

## Pruebas obligatorias

1. Dos usuarios consecutivos en el mismo Edge crean sesiones diferentes.
2. Una caída y reinicio recupera la sesión abierta.
3. Toda venta y pago queda asociado al usuario y sesión correctos.
4. El cierre totaliza por medio de pago sin mezclar sesiones.
5. La tirilla identifica usuario, sede y bodega, y no contiene “caja”.
6. No existen tablas, APIs ni componentes canónicos de arqueo o cierre de caja.
