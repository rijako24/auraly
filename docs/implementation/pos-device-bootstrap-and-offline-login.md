# Dispositivo POS, login offline y preparación inicial


Actualizacion 2026-08-30: enrolamiento protegido y proyeccion local de usuarios
ya estan implementados. Una vez preparado el equipo, el login local no adquiere
ni exige una concesion temporal: la preparación durable permanece utilizable sin
vencimiento por paso del tiempo. El shell de escritorio y el transporte push ya
están conectados; el estado historico al final de este documento queda reemplazado
por esta nota.
Fecha: 2026-07-29

## Decisión

Auraly conserva una sola aplicación web/instalable y un único modelo de usuario,
sesión y permisos. La estrategia de autenticación cambia según el contexto:

- un acceso desde la landing o un navegador usa el login online existente, la
  sesión del servidor y cookies HttpOnly;
- una instalación no enrolada usa siempre el login online del servidor;
- una instalación enrolada admite login local desde la misma pantalla general;
- POS Edge aporta persistencia, impresión, periféricos y sincronización local,
  pero no reemplaza la autorización del servidor;
- los módulos administrativos consultan la API directamente;
- únicamente Facturación y sus capacidades explícitamente offline usan datos
  operativos locales.

No se crean dos formularios de login: `/login` es la única superficie visible y
`/pos` redirige allí cuando necesita una nueva sesión local. Un `AuralySession` normaliza la identidad,
el negocio activo, el origen de autenticación, la vigencia y los permisos.

## Primera activación

Una instalación que nunca fue enrolada requiere Internet. Antes de existir un
login local ejecuta un asistente mediante un código de activación de un solo uso:

1. valida el dispositivo con el servidor;
2. selecciona la sede (`Business`) permitida;
3. selecciona la caja;
4. obtiene de la caja la bodega, serie operativa y configuración fiscal;
5. registra impresora, balanza y preferencias locales;
6. crea claves del dispositivo protegidas por el almacén seguro del sistema;
7. sincroniza usuarios POS autorizados, permisos y manifiesto de módulos;
8. registra cursores y el estado durable de preparación;
9. habilita el login local.

Al terminar la descarga inicial, la sesión local usada para el traspaso se cierra
y la instalación vuelve a `/login`. El usuario inicia una sesión local nueva antes
de vender. Un reingreso o reintento consulta primero el estado durable de POS Edge:
si el dispositivo ya está enrolado, continúa desde ese estado y no solicita otra
autorización con la sesión web que el traspaso revocó. El login local abre
Facturación incluso si el usuario tiene permisos administrativos. Si el servidor
está conectado y el usuario tiene acceso administrativo, ese mismo envío de
credenciales obtiene además una sesión web para sus módulos, sin reutilizar la
sesión web revocada durante el traspaso. Cada servicio sigue validando su propia
sesión; la local nunca se presenta como credencial web.

La bodega no se selecciona independientemente si la caja ya tiene una bodega
asociada. La activación nunca recibe certificados fiscales ni secretos del
servidor.

## Usuarios y credenciales offline

La caja no descarga todos los usuarios de la plataforma ni hashes de sus
contraseñas principales. Descarga solamente usuarios habilitados para la sede
y el dispositivo, con la proyección mínima:

- `UserId`;
- nombre para mostrar;
- estado;
- permisos efectivos relevantes;
- negocios/cajas autorizados;
- versión y fecha de última sincronización;
- credencial offline específica de POS, ligada al dispositivo.

La credencial offline se almacena cifrada usando protección del sistema
operativo. No expira por el paso del tiempo; conserva intentos limitados y se
actualiza o retira cuando el equipo recibe una sincronización de seguridad.
Una sesión local nunca autoriza por sí sola una llamada al servidor.

Antes de mostrar el login, una caja conectada obtiene del cursor local:

- usuarios nuevos;
- cambios de permisos;
- bloqueos;
- revocaciones;
- cambios en el manifiesto de módulos.

Esta puesta al día empieza al abrir, pero no bloquea el login de un usuario ya
provisionado. Sin Internet usa la última proyección local protegida. La ausencia
o corrupción de esa proyección sí bloquea; su antigüedad, por sí sola, no.
La validación local del login nunca solicita una actualización ni depende del
servidor. Para usuarios con módulos administrativos, un equipo conectado puede
abrir además la sesión web con las mismas credenciales después del login local.
Si el usuario no está en SQLite, la contraseña es incorrecta o todavía
no existe una proyección promovida, falla localmente. La sincronización de
seguridad corre por su carril independiente y los cambios quedan disponibles en
el siguiente intento, sin convertir el submit en una operación de red.

La reconciliación inicial pertenece al arranque de Edge y se ejecuta una sola vez,
en paralelo al acceso. La primera conexión del canal push no repite esa descarga;
solo una reconexión posterior puede pedir un catch-up por cambios posiblemente
perdidos. Abrir el POS reanuda la sesión operativa local sin señal de outbox ni
consulta remota; únicamente una sesión nueva encola su alta para envío asíncrono.

Cada equipo conserva una sola autenticación local activa: un login nuevo cierra
el token local anterior y su navegador vuelve al login al recibir
`LoginReplaced`. El token nuevo no se invalida por una concesión histórica ni por
una respuesta tardía originada con el token anterior.

El snapshot y sus deltas son la autoridad única de permisos locales. El lease de
compatibilidad solo acredita el traspaso y actualiza el verificador; si el usuario
ya pertenece a la proyección, aplicar el lease no toca sus permisos. El formato
local versiona esta regla para que las instalaciones afectadas por la sobrescritura
histórica pidan un snapshot completo una sola vez al reconectar y luego continúen
con deltas.

## Menú y módulos

Después del login el menú se deriva de permisos efectivos:

- Facturación puede operar offline en una caja preparada;
- módulos online aparecen según permisos;
- sin conexión, una ruta online aparece deshabilitada con `Requiere conexión`;
- con conexión, la API vuelve a validar usuario, negocio y permiso;
- el login de una instalación enrolada abre Facturación con la sesión local y,
  si hay conexión y permisos administrativos, crea también la sesión web. Si
  estaba desconectada, el acceso administrativo solicita autenticación web al
  volver la conexión;
- dentro de Facturación, el único botón de salida abre el menú Cloud cuando el
  servidor está disponible y se convierte en `Cerrar sesión` cuando el equipo
  está trabajando únicamente con el runtime local;
- un navegador no enrolado puede usar módulos online, pero no simula impresión,
  periféricos, outbox ni venta offline.

Si un usuario autenticado desde la landing abre Facturación sin una instalación
enrolada, Auraly ofrece instalar o asociar el dispositivo.

## Primera apertura de Facturación

Si POS Edge no tiene un catálogo válido, Facturación muestra una pantalla
bloqueante `Preparando tu caja`. El progreso proviene de checkpoints reales, no
de temporizadores decorativos.

Etapas:

1. comprobando dispositivo;
2. productos vendibles y códigos;
3. impuestos, unidades y balanza;
4. precios base del negocio;
5. listas y sus detalles;
6. definiciones de canal, tramos configurados y exclusiones;
7. clientes mínimos y su asignación excluyente de lista o canal;
8. validación de integridad;
9. promoción atómica;
10. caja lista.

La proyección local no incluye proveedores, inventario de otros negocios ni
información personal innecesaria. Los insumos de costo o margen exigidos por
una estrategia de canal forman parte de la única fila local del producto y no
se muestran al cajero. El precio base siempre permite vender si no existe un
precio especial.

La UI muestra etapa, porcentaje real, registros procesados, total conocido,
conexión, reanudación y un error accionable. Facturación no se habilita sobre un
staging parcial.

## Aperturas posteriores y cambios con la app cerrada

Pub/Sub no sustituye la recuperación durable. En cada apertura:

1. el dispositivo abre su conexión saliente de notificaciones;
2. obtiene un `high-water mark`;
3. solicita por HTTP todos los cambios posteriores al cursor local;
4. aplica cada página y su cursor en una sola transacción;
5. drena hasta alcanzar el marcador;
6. queda escuchando avisos de cambios nuevos;
7. ante un aviso, vuelve a consultar deltas por cursor.

Las notificaciones no contienen catálogos. Si se pierde una notificación no se
pierde el cambio. Si el cursor expiró, el servidor responde explícitamente y POS
Edge ejecuta un bootstrap completo en staging.

Con un catálogo local válido, la conciliación es siempre no bloqueante:

- Facturación se habilita inmediatamente;
- la UI muestra `Actualizando...` sin cubrir la captura;
- cada lote y cursor se aplican atómicamente;
- nuevas capturas usan los datos actualizados;
- líneas ya capturadas no se reprician silenciosamente;
- una revocación del usuario activo bloquea su sesión al recibirse.

Durante la puesta al día la caja puede usar brevemente la última versión local,
la misma semántica que tendría offline. Solo bloquean la primera sincronización,
un almacenamiento local inexistente o corrupto o una migración incompatible.

## Estado durable de preparación

POS Edge debe exponer un estado consumible por la interfaz:

- `Unenrolled`;
- `Provisioning`;
- `IdentitySyncing`;
- `LoginReady`;
- `CatalogRequired`;
- `CatalogBootstrapping`;
- `CatalogCatchingUp`;
- `Ready`;
- `OfflineReady`;
- `Blocked`;
- `ReadyUpdating`;
- `Failed`.

Incluye etapa, cursor, marcador, procesados, total, porcentaje, último éxito,
error sanitizado y si puede reanudar.

En la preparación inicial, `Ready` solo se publica después de descargar y confirmar
geografía, motivos de caja, configuración y catálogos operativos, clientes y el
catálogo completo de productos con todos sus códigos de barras. La promoción atómica
del catálogo es el último checkpoint durable; una cantidad distinta al total anunciado
invalida el bootstrap. La identidad local sigue siendo un requisito independiente de
acceso. La ausencia de configuración fiscal no impide terminar la preparación, pero
bloquea explícitamente la entrada a factura electrónica hasta que exista resolución.

## Pruebas obligatorias

- primera activación requiere Internet y código válido;
- un código usado o vencido se rechaza;
- una caja descarga solo usuarios autorizados;
- no existen hashes de contraseña principal en SQLite;
- login local repetido aun cuando hayan pasado las fechas históricas de vigencia del snapshot y de la concesión de compatibilidad;
- usuario bloqueado mientras la app está cerrada no entra después del catch-up;
- usuario creado con la app cerrada aparece después de abrir;
- permisos cambiados con la app cerrada actualizan el menú;
- los módulos online quedan deshabilitados sin conexión;
- navegador desde landing conserva autenticación online;
- navegador no enrolado no obtiene capacidades de POS Edge;
- bootstrap interrumpido reanuda desde checkpoint;
- catálogo parcial nunca queda visible;
- cambios de producto, precio, cliente, lista y canal hechos con la app cerrada
  se aplican en segundo plano sin bloquear la venta;
- una caja con catálogo válido puede vender mientras el catch-up está activo;
- notificación perdida se recupera mediante cursor;
- cursor expirado fuerza bootstrap seguro;
- costos e inventario nunca se almacenan en el catálogo local.

## Estado de implementación

Ya existen enrolamiento, bootstrap durable de catálogo, staging, promoción
atómica, cursor incremental, persistencia SQLite y sincronización de identidad.
Durante la preparación inicial, POS Edge expone a la interfaz la etapa activa,
el fallo sanitizado y el progreso durable real del catálogo; cada lote actualiza
la pantalla y una caja sin productos continúa con `0 de 0` sin quedar bloqueada.
El estado completo descrito en esta decisión y el transporte Pub/Sub real siguen
siendo trabajo incremental y no se consideran terminados hasta estar conectados
de punta a punta y cubiertos por las pruebas anteriores.
