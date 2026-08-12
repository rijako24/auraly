# Sesiones, usuarios, relevos y arqueos de caja

Fecha: 2026-07-29.

## Decisión

Auraly separa cuatro conceptos:

- La sesión autenticada identifica a quien está usando la aplicación.
- `CashSession` representa la apertura física de una caja y solo termina con
  `Cerrar caja`.
- `CashierShift` identifica qué usuario fue responsable de las operaciones de
  esa caja durante un intervalo.
- `CashCount` registra una entrega opcional o el arqueo final.

Cerrar sesión en Auraly nunca cierra la caja, no crea un arqueo y no obliga a
entregarla. `Entregar caja` y `Cerrar caja` son acciones explícitas y
opcionales.

## Inicio de la aplicación

Auraly utiliza una sola aplicación web instalable. El enrolamiento local
determina su experiencia inicial:

### Equipo no enrolado como POS Edge

1. Muestra el login normal contra el servidor.
2. Abre el shell administrativo de Auraly.
3. Construye el menú con los permisos del usuario.
4. Facturación funciona en modo en línea después de seleccionar negocio, sede
   y caja.
5. Puede instalarse como PWA para evitar que el usuario escriba una URL, pero no
   conserva operación transaccional offline.

### Equipo enrolado como POS Edge

1. Muestra siempre un login; nunca abre una caja anónima.
2. Con conexión, valida contra el servidor y actualiza en segundo plano el
   verificador local, los permisos y los datos requeridos por el POS.
3. Sin conexión, valida localmente una identidad previamente provisionada para
   ese dispositivo.
4. Si el usuario posee `sales.create`, abre `/pos` como módulo inicial.
5. Si hay Internet, el lanzador y menú permiten abrir los demás módulos
   autorizados.
6. Sin Internet, los módulos exclusivos del servidor aparecen deshabilitados y
   el POS local continúa disponible.

No se descargan las contraseñas ni todos los hashes de las cuentas del tenant.
Solo se provisionan al dispositivo las identidades autorizadas para operar ese
POS, con un verificador local específico, cifrado y revocable.

La primera configuración y la primera sincronización obligatoria sí muestran
una experiencia de preparación bloqueante. Las puestas al día posteriores se
realizan en segundo plano y no impiden iniciar una venta con el catálogo local
válido.

## Concurrencia de usuarios

Una caja online admite varios usuarios desde computadores distintos. Todos
comparten `RegisterId`, `CashSession` y numeración, pero cada usuario conserva su
propio `CashierShift` activo. Cada venta guarda su `SoldByUserId` y
`CashierShiftId`; el arqueo puede consolidar por caja y desglosar por cajero.

Los consecutivos operativos y fiscales no se reservan en el navegador. La API
los asigna atómicamente dentro de la emisión: si dos usuarios confirman al mismo
tiempo, uno recibe N y el otro N+1. El número mostrado antes de emitir es solo
informativo. Un número confirmado nunca se recicla ni cambia de cajero.

La exclusividad se aplica a la estación física enrolada, no a la entidad caja:

1. POS Edge mantiene un solo usuario local operando ese dispositivo;
2. si otro usuario inicia sesión en el mismo dispositivo, reemplaza únicamente
   la sesión local de esa estación;
3. no expulsa usuarios online ni sesiones del mismo usuario en otros equipos;
4. la misma `CashSession` permanece abierta;
5. no se solicita conteo ni se imprime tirilla por el cambio de login;
6. la sesión local anterior no puede confirmar nuevas operaciones.

El control local de POS Edge se implementa con un lease de estación y no con un
índice que limite toda la caja a un cajero.

Cerrar caja no cierra la sesión autenticada. Después del cierre el usuario puede
navegar por Auraly o volver a abrir la caja si tiene permiso.

## Entrega opcional con autorización de supervisor

El cajero puede solicitar `Entregar caja`, pero no puede aprobar su propia
solicitud por el solo hecho de estar operando el POS.

La aprobación exige `cash.handoff.approve` y admite:

- usuario y contraseña del supervisor;
- una credencial imprimible y escaneable del supervisor.

La credencial de barras:

- contiene un identificador opaco y entropía aleatoria, no una contraseña;
- se almacena mediante salt y hash;
- puede rotarse y revocarse;
- se muestra una sola vez al provisionarla;
- no aparece en logs.

Una autorización correcta genera un token de 90 segundos ligado a negocio,
caja, cajero solicitante y permiso. Se consume una sola vez dentro de la misma
transacción que confirma la entrega. Quedan auditados solicitante, supervisor,
receptor, instante, diferencias y caja.

La entrega cuenta el contenido completo del cajón: fondo inicial más movimientos
acumulados por medio de pago. Una diferencia requiere explicación.

## Cierre y reapertura

`Cerrar caja`:

- requiere `cash.close`;
- concilia el contenido completo;
- termina el turno y la sesión física;
- congela la tirilla y su hash;
- imprime el arqueo final.

La misma caja puede abrirse otra vez el mismo día. Esa apertura crea otra
`CashSession` y el cierre posterior recibe otro número. El reporte diario
consolida todas las sesiones por `BusinessDate`.

## Venta y trazabilidad

Cada venta conserva:

- usuario que vendió;
- sesión de caja;
- turno de cajero;
- fecha operativa;
- movimientos por medio de pago.

El motor de documentos registra esas relaciones y los movimientos en la misma
transacción idempotente que procesa líneas, inventario y pagos. Repetir una
carga no duplica movimientos ni cambia de cajero.

## Referencia funcional de Xion

Xion solicita una credencial en una ventana, verifica usuario activo,
capacidad de autorizar y permiso concreto, y registra la auditoría. Auraly
conserva esas reglas útiles, pero elimina la contraseña alternativa expuesta y
la contraseña maestra calculable presentes en la implementación histórica.

El informe Z de Xion consolida ventas por cajero, entradas, salidas, pagos,
impuestos, descuentos, devoluciones y diferencias. Auraly usa esa cobertura
como referencia funcional sin copiar Crystal Reports ni su tabla acumuladora.

## Tirilla Auraly

El cierre produce `ARQ{caja}-{consecutivo}` y congela:

- negocio, sede, caja y sesión;
- apertura, cierre y duración;
- usuario que abrió y usuario que cerró;
- rango de documentos Auraly y DIAN;
- ventas, devoluciones, descuentos y neto;
- resumen por cajero;
- conciliación por medio: esperado, contado y diferencia;
- entradas y salidas;
- impuestos agrupados por código y tarifa;
- total esperado, contado y diferencia;
- observación y responsables.

La cabecera muestra `ARQUEO DE CAJA · CIERRE CONFIRMADO`. La tirilla funciona
en 58 y 80 mm. La reimpresión usa exactamente el snapshot guardado.
