# Facturación unificada: navegador y equipo con respaldo offline

Fecha: 2026-07-29

## Decisión

Auraly expone una sola experiencia de facturación y selecciona el adaptador por la
forma en que se abrió el equipo:

- Un navegador sin enrolamiento usa `Auraly.Api` y SQL Server en línea. Si no
  detecta Auraly POS, imprime mediante el diálogo del navegador.
- Auraly POS instalado, aun sin enrolar, expone impresión directa, cajón y
  balanza a la venta online. Instalar no activa respaldo offline.
- Al enrolar se habilitan además SQLite, catálogo local, login offline,
  numeración, outbox y sincronización.

No se pregunta al cajero el modo en cada inicio y no se cambia de persistencia a
mitad de una factura. La ausencia de Internet no se confunde con la ausencia de
POS Edge.

## Login y destino inicial

La PWA siempre abre un login. No existe una entrada anónima por estar enrolada y
el cierre de caja no controla la visibilidad del resto de Auraly.

| Estado del equipo | Autenticación | Destino inicial | Otros módulos |
|---|---|---|---|
| No enrolado, en línea | Servidor y cookie HttpOnly | Shell normal de Auraly | Según permisos |
| No enrolado, sin red | No disponible | Pantalla de conexión | No disponibles |
| Enrolado, en línea | Servidor; refresca el verificador local | POS si tiene `sales.create` | Disponibles según permisos |
| Enrolado, sin red | Verificador local del dispositivo | POS | Visibles pero deshabilitados si requieren servidor |

El usuario puede abrir el lanzador desde el POS sin entregar ni cerrar la caja.
Si regresa a Facturación, reanuda la sesión física y el turno que continúen
vigentes. `Cerrar caja` termina `CashSession`, pero conserva la sesión
autenticada y permite seguir navegando.

Varios usuarios online pueden operar simultáneamente el mismo `RegisterId` desde
computadores diferentes. Comparten la sesión física y la numeración, pero cada
venta conserva su cajero y turno. Una instalación POS Edge sí mantiene un solo
usuario local activo por dispositivo; cambiarlo reemplaza únicamente el lease de
esa estación y no expulsa a usuarios online ni a otros equipos.

No se descargan hashes de las contraseñas principales. El login offline usa
identidades POS previamente autorizadas y un verificador por dispositivo,
cifrado, revocable y con vigencia.

## Selección inicial

Sin una sesión local de Edge, Facturación usa la sesión web autenticada. Si no hay
una caja recordada, solicita:

1. sede (`Business`);
2. caja.

La bodega nunca se selecciona independientemente: se deriva de la caja y se
muestra para confirmación. La preferencia del navegador solamente recuerda el
`RegisterId`; el servidor vuelve a validar tenant, sede, bodega, estado
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

El navegador obtiene consecutivos operativos y fiscales dentro de una transacción
atómica de emisión. Dos usuarios pueden confirmar sobre la misma caja: el primero
que adquiere el cursor recibe N y el siguiente N+1. El navegador no posee ni
reserva el número mostrado antes de confirmar, y solo imprime después de recibir
el documento persistido con CUFE.

Edge consume rangos previamente reservados. Un rango offline no puede ser
consumido simultáneamente por el servidor. Los números confirmados no se reciclan
después de timeouts o errores; se consulta el documento por su `DocumentId` e
idempotency key.

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

## Durabilidad al cerrar Facturación

Cerrar la ventana, recargar la PWA, navegar a otro módulo o reiniciar el proceso
no cancela la venta en curso.

El autosave es parte de cada comando de captura:

- agregar o retirar una línea;
- cambiar cantidad;
- aplicar descuento;
- seleccionar cliente o vendedor;
- recuperar un pedido;
- modificar observación;
- preparar pagos que todavía no se hayan confirmado.

En un equipo enrolado, `PosDraftStore` persiste el borrador en SQLite dentro de
la misma transacción del comando. Al reabrir, POS Edge devuelve el borrador
`Active` del alcance caja/usuario y la UI lo hidrata antes de aceptar la siguiente
captura.

En el modo web, `OnlinePosClient` usa un borrador durable en SQL Server. El
servidor es propietario de:

- `SalesDraftId`;
- alcance de sede (`Business`), caja y usuario;
- líneas y snapshots comerciales;
- cliente, vendedor y pedido de origen;
- descuentos y totales;
- versión de concurrencia;
- fecha de última actividad.

La preferencia del navegador conserva solo el identificador del borrador y la
caja recordada; nunca es la copia autoritativa. Si se pierde, la API puede
resolver el borrador activo por el contexto autenticado.

Dos ventanas no sobrescriben silenciosamente el mismo borrador. Cada mutación
envía la versión esperada y un `IdempotencyKey`; una versión obsoleta produce un
conflicto explícito y obliga a recargar.

Solamente estas acciones terminan el borrador:

- emitir la factura y recibir confirmación durable;
- reiniciar la venta con confirmación;
- pausarla, lo cual crea una nueva venta activa vacía.

La durabilidad online debe implementarse antes de habilitar `OnlinePosClient` en
producción. No se puede afirmar paridad online si el estado vive solo en React.

## Reglas de transición

- Navegador sin Edge: entra online automáticamente.
- Auraly POS instalado sin enrolar: vende online y usa periféricos locales.
- Aplicación enrolada con Internet: opera mediante Edge y sincroniza de inmediato.
- Aplicación enrolada sin Internet: Edge continúa con los recursos provisionados.
- Un borrador iniciado en un adaptador termina en ese adaptador.
- Habilitar offline requiere enrolamiento; no migra silenciosamente borradores.
- Deshabilitar o reenrolar exige outbox vacía o intervención explícita.

