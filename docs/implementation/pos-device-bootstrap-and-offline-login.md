# Dispositivo POS, login offline y preparación inicial

Fecha: 2026-07-29

## Decisión

Auraly conserva una sola aplicación web/instalable y un único modelo de usuario,
sesión y permisos. La estrategia de autenticación cambia según el contexto:

- un acceso desde la landing o un navegador usa el login online existente, la
  sesión del servidor y cookies HttpOnly;
- una instalación enrolada como caja admite login local cuando no hay conexión;
- POS Edge aporta persistencia, impresión, periféricos y sincronización local,
  pero no reemplaza la autorización del servidor;
- los módulos administrativos consultan la API directamente;
- únicamente Facturación y sus capacidades explícitamente offline usan datos
  operativos locales.

No se crearán dos PWAs ni dos menús. Un `AuralySession` normaliza la identidad,
el negocio activo, el origen de autenticación, la vigencia y los permisos.

## Primera activación

Una instalación que nunca fue enrolada requiere Internet. Antes de existir un
login local ejecuta un asistente mediante un código de activación de un solo uso:

1. valida el dispositivo con el servidor;
2. selecciona el negocio y la sede permitidos;
3. selecciona la caja;
4. obtiene de la caja la bodega, serie operativa y configuración fiscal;
5. registra impresora, balanza y preferencias locales;
6. crea claves del dispositivo protegidas por el almacén seguro del sistema;
7. sincroniza usuarios POS autorizados, permisos y manifiesto de módulos;
8. registra cursores y vigencias;
9. habilita el login local.

La bodega no se selecciona independientemente si la caja ya tiene una bodega
asociada. La activación nunca recibe certificados fiscales ni secretos del
servidor.

## Usuarios y credenciales offline

La caja no descarga todos los usuarios de la plataforma ni hashes de sus
contraseñas principales. Descarga solamente usuarios habilitados para el negocio
y el dispositivo, con la proyección mínima:

- `UserId`;
- nombre para mostrar;
- estado;
- permisos efectivos relevantes;
- negocios/cajas autorizados;
- versión y vigencia;
- credencial offline específica de POS, ligada al dispositivo.

La credencial offline se almacena cifrada usando protección del sistema
operativo. Tiene expiración, intentos limitados y posibilidad de revocación.
Una sesión local nunca autoriza por sí sola una llamada al servidor.

Antes de mostrar el login, una caja conectada obtiene del cursor local:

- usuarios nuevos;
- cambios de permisos;
- bloqueos;
- revocaciones;
- cambios en el manifiesto de módulos.

Esta puesta al día empieza al abrir, pero no bloquea el login de un usuario ya
provisionado. Sin Internet usa el último paquete firmado y vigente. Un paquete
vencido no se acepta silenciosamente.

## Menú y módulos

Después del login el menú se deriva de permisos efectivos:

- Facturación puede operar offline en una caja preparada;
- módulos online aparecen según permisos;
- sin conexión, una ruta online aparece deshabilitada con `Requiere conexión`;
- con conexión, la API vuelve a validar usuario, negocio y permiso;
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
6. canales y precios materializados;
7. clientes mínimos y su asignación excluyente de lista o canal;
8. validación de integridad;
9. promoción atómica;
10. caja lista.

La proyección local no incluye costos, márgenes, proveedores, inventario ni
información personal innecesaria. El precio base siempre permite vender si no
existe un precio especial.

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
un almacenamiento local inexistente o corrupto, una migración incompatible o
una credencial offline vencida.

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

## Pruebas obligatorias

- primera activación requiere Internet y código válido;
- un código usado o vencido se rechaza;
- una caja descarga solo usuarios autorizados;
- no existen hashes de contraseña principal en SQLite;
- login offline válido y expirado;
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

Ya existen bootstrap durable de catálogo, staging, promoción atómica, cursor
incremental y persistencia SQLite. Aún faltan el enrolamiento, credenciales
offline, sincronización de usuarios/permisos, estado de progreso enriquecido,
shell de login/menú y transporte Pub/Sub real. Ninguno se considera terminado
hasta estar conectado a la interfaz y cubierto por las pruebas anteriores.
