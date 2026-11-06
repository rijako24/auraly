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

`resolvePosExecutionMode` es el único resolvedor del propietario de datos del
POS: sin aplicación instalada o con `EnrollmentRequired` selecciona
`OnlinePosClient` y el servidor web; para cualquier estado enrolado selecciona
`PosEdgeClient` y SQLite. Si el servicio instalado no responde, el propietario
queda indeterminado mientras reinicia; no se adivina ni se cambia de
persistencia. La conectividad no cambia ese propietario. La aplicación instalada
sólo permite que el modo online use adaptadores locales de periféricos; no crea
un segundo motor de venta.

Los pedidos son una excepción deliberada al propietario de datos de la venta:
su único propietario comercial es la API de Pedidos y SQL Server. La web usa
`OnlinePosClient`; una caja enrolada conserva `PosEdgeClient` y pasa todas las
operaciones online por el proxy local autenticado. La recuperación copia el
snapshot a un borrador SQLite de trabajo, sin crear un maestro paralelo ni
recalcular precio o inventario. Crear, recuperar, editar, guardar o facturar un
pedido requiere conexión y vuelve siempre al caso de uso canónico del servidor.
Si el cliente del pedido se creó en esta caja y aún está en el outbox, el proxy
local completa primero su subida canónica; sólo entonces envía el pedido. Un
cliente que no pudo sincronizarse deja el pedido sin guardar y muestra el fallo.

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
| Enrolado, en línea | Verificador local del dispositivo y, cuando el perfil tiene módulos de servidor, sesión web en el mismo acceso | POS para un perfil exclusivamente operativo; shell normal para un perfil administrativo | Según los permisos efectivos del servidor |
| Enrolado, sin red | Verificador local del dispositivo | POS si tiene `sales.create` | No disponibles si requieren servidor |

En un equipo enrolado hay un solo formulario de acceso. El host local valida la
identidad y mantiene la continuidad de caja. Si la proyección del usuario incluye
algún módulo que requiere servidor, el mismo envío establece además la sesión web
y abre el destino autorizado por el servidor; no se obliga al administrador a
entrar primero como cajero ni se añade un segundo enlace de login. Si el servidor
no está disponible, el acceso local conserva Facturación y nunca inventa acceso a
módulos administrativos.

La proyección de seguridad es la única propietaria de los permisos locales. El
lease offline prueba y limita el traspaso usuario-dispositivo y puede refrescar el
verificador de contraseña, pero no reemplaza ni reduce los permisos descargados
por el snapshot o sus deltas. Una actualización del formato de esa proyección
fuerza una resincronización completa una sola vez; después vuelve al cursor
incremental normal.

El usuario puede abrir el lanzador desde el POS sin entregar ni cerrar la caja.
Si regresa a Facturación, reanuda la sesión física y el turno que continúen
vigentes. `Cerrar caja` termina `CashSession`, pero conserva la sesión
autenticada y permite seguir navegando.

Varios usuarios online pueden operar simultáneamente el mismo `RegisterId` desde
computadores diferentes. Comparten la sesión física y la numeración, pero cada
venta conserva su cajero y turno. Una instalación POS Edge mantiene un solo
usuario local activo por dispositivo. Al elegir operación local se realiza un
traspaso explícito: se revocan las sesiones web activas de ese mismo usuario para
que no compitan con la concesión offline, sin cerrar la sesión física ni el turno
de caja. No se expulsan otros usuarios ni se afecta otro dispositivo enrolado por
identidad compartida.

No se descargan hashes de las contraseñas principales. El login offline usa
identidades POS previamente autorizadas y un verificador por dispositivo,
protegido por el sistema operativo y revocable por sincronización. La preparación
no expira por el paso del tiempo.

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

La pantalla de preparación fiscal no cambia el adaptador. Si el equipo ya está
enrolado, `Continuar a ventas` conserva `PosEdgeClient` para la sede y bodega del
enrolamiento; intentar seleccionar otro alcance exige reenrolar. Por tanto, entrar
directamente a Facturación o regresar desde Administración produce el mismo modo
operativo y las mismas reglas.

La vista de sede, bodega y documento de un equipo enrolado proyecta el alcance
autoritativo que ya entrega POS Edge. No intenta reconstruirlo mediante la sesión
web —que se retira al establecer la identidad local— ni interpreta la ausencia de
esa cookie como una caída de Auraly. Consultar o cambiar el documento
predeterminado dentro del mismo alcance no realiza viajes de red; cambiar sede o
bodega continúa exigiendo un nuevo enrolamiento controlado.

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

Navegar a Facturación/POS es una navegación normal dentro de la sesión ya
autenticada. El montaje de la pantalla no ejecuta login, no reemplaza ni invalida
la sesión y no descarga usuarios, permisos o catálogo. Solamente el formulario
de acceso crea una nueva sesión local y reemplaza la anterior. La apertura o
reanudación del turno operativo local es responsabilidad del host y no constituye
una segunda autenticación.

En una caja enrolada la UI no sincroniza datos. Lee las proyecciones locales y
envía comandos al host; el sincronizador local es el único propietario de las
subidas y bajadas de snapshots. Sus fallos y reintentos se exponen en Ctrl+L.
La vista previa y la confirmación del cierre se construyen exclusivamente con
las ventas, devoluciones, pagos de cartera y movimientos persistidos en SQLite
para el turno local; no consultan Auraly Server. El cierre ya congelado se encola
y el sincronizador lo sube después. En web no enrolada, el cierre consulta las
tablas canónicas de SQL Server. Para una devolución, abono o pago a proveedor de
caja preparada, Edge escribe primero una proyección mínima local cuando
`EconomicResolution=Refund` o se registra un pago de cartera. Un abono a la CxC
mediante devolución no genera reintegro por ningún medio; después de la
aceptación se proyecta localmente solo para los totales y el número de
devoluciones del cierre. `EconomicResolution=CustomerCredit` determina el efecto
en cartera aunque la devolución conserve `WorkSessionId` para identificar la
sesión activa que la confirmó. Si una escritura local necesaria
falla, no envía el comando al servidor. Tras la aceptación actualiza la
proyección con el importe y comprobante autoritativos; un reintento reutiliza el
identificador del documento. Un rechazo definitivo revierte la proyección local.
Una pérdida de respuesta deja el movimiento local con el importe estimado hasta
que se reintente el mismo comando; esta limitación no se resuelve con una
transacción distribuida entre SQLite y SQL Server.
Los comandos que por contrato son síncronos con servidor —guardar/actualizar un
pedido y las validaciones autoritativas de inventario restringido o saldo de
cartera— también pasan por el host con credencial del dispositivo, nunca por el
BFF ni por la cookie web del navegador instalado.

Captura, precio e inventario resuelven únicamente el producto o familia de
inventario afectada. Las familias y colecciones se cargan y procesan por lote;
quedan prohibidas las consultas por línea, por producto ya presente o por render.

El indicador visible expresa conectividad con Auraly Server. “POS Edge” permanece
como detalle técnico y no se muestra al cajero.

## Resolución de precios

`CommercePriceResolver` es el coordinador canónico y puro en memoria para precios
de producto. Recibe únicamente las líneas del documento que ya cargó el adaptador,
agrupa cantidades del mismo producto y compone `PriceChannelResolver` con
`PromotionPriceResolver`. SQL Server, SQLite y pedidos cargan sus datos, pero no
repiten ni modifican la aritmética comercial.

La búsqueda de catálogo resuelve cada candidato de forma independiente. Una venta
o pedido resuelve sus líneas juntas porque los precios por cantidad, condiciones
entre productos y mínimos del documento necesitan el contexto del documento. Esto
no consulta el catálogo completo ni agrega viajes a base; en pedidos evita resolver
la configuración una vez por producto.

Recuperar una venta pausada o un pedido es una operación de hidratación: copia
cantidad, precio público, descuento público, total y origen comercial tal como
quedaron guardados, sin
invocar el resolver. No existe una guarda posterior. La siguiente mutación normal
—agregar o eliminar, cambiar cantidad o seleccionar otro cliente— vuelve a usar el
resolver canónico sobre el borrador vigente, igual que cualquier venta nueva.

El contrato de Pedidos siempre usa valores públicos incluidos impuestos. POS
Edge ya conserva esa representación. El borrador online usa valores netos
internamente y convierte en su frontera de entrada/salida; por eso una tarifa de
IVA distinta puede cambiar la separación entre base e impuesto al recuperar,
pero nunca el total público pactado del pedido.

La paridad offline significa mismo resultado con el mismo snapshot de catálogo,
canales, promociones y hora efectiva. Sin conexión no puede incluir cambios del
servidor que el equipo todavía no haya sincronizado.

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
- En todos los modos, una emisión confirmada instala y muestra de inmediato el
  siguiente borrador vacío. La impresión es un efecto posterior independiente:
  navegador, aplicación instalada enrolada o no enrolada usan el comprobante ya
  emitido y un fallo del periférico no reactiva ni conserva la venta anterior.
- La cartera siempre consulta el validador canónico de SQL Server: desde navegador
  se invoca dentro del checkout y desde Edge conectado mediante el endpoint
  autenticado del dispositivo. Sin servidor, Edge rechaza el crédito antes de
  emitir; nunca autoriza con el cupo potencialmente obsoleto del catálogo local.
  La autorización comprueba cliente habilitado y saldo disponible. La fecha de
  vencimiento no la calcula ni la envía el cliente: el servidor la deriva del
  plazo vigente del perfil al crear la cuenta por cobrar. Esa fecha gobierna
  exclusivamente el vencimiento y la mora de la cartera.
- La disponibilidad de inventario conserva su política propia: con servidor
  valida existencia actual y sin servidor aplica la regla offline provisionada.
- Un borrador iniciado en un adaptador termina en ese adaptador.
- Habilitar offline requiere enrolamiento; no migra silenciosamente borradores.
- Un nuevo enrolamiento aceptado elimina todos los datos operativos locales,
  incluida la outbox pendiente, según `pos-enrollment-design.md`. Reiniciar la
  aplicación o reintentar una descarga del mismo enrolamiento no ejecuta esa limpieza.


## Presentación Tiendas (2026-11-05)

El modo persistido de sede tiene tres opciones del catálogo `point-of-sale-mode`:
Venta directa (`DirectSale`), Tiendas (`Store`) y Restaurante y bar (`RestaurantBar`).
Tiendas cambia exclusivamente la presentación web de venta directa. Comparte su
`SalesDraftId` online, propietario, versión, idempotencia y captura por ID,
cantidades, pausas, precios, cobro, emisión e impresión. No crea otro carrito,
borrador, tabla de ventas, emisor ni API de confirmación. Tiendas y Restaurante
funcionan en línea; únicamente Venta directa permite preparar una caja local
para vender sin conexión. La aplicación instalada también puede abrir esas
vistas en línea y usar sus impresoras, sin cambiar la caja SQLite enrolada.
La proyección local del catálogo conserva sus metadatos compatibles, sin activar
la presentación Tiendas en Edge.

El consumo de ingredientes y combos pertenece al motor documental compartido,
según la decisión de inventario de `restaurant-pos-design.md`, §7. La captura y
facturación conservan sus contratos: producto y cantidad. La composición se
resuelve en el motor; la disponibilidad multiplica y acumula componentes antes
de confirmar una venta con política de no negativos.

`/pos` mantiene una sola instancia del flujo de venta directa. Tiendas presenta
las tarjetas, categorías y superficie de líneas extraídas del restaurante como
componentes de presentación compartidos. Las acciones invocan los handlers del
POS vigente. Los datos y callbacks de restaurante conservan su propietario.

La consulta `POST /pos/catalog/products/search` reutiliza el servicio y la consulta
canónicos de búsqueda/precio. Categoría y familia se aplican antes del límite de
50 productos. La familia se resuelve desde `ProductLinks`, con principal y hermanos,
sin inferir relaciones por nombre ni cambiar inventario. La proyección tipada de
imagen, familia, color y talla pertenece a `catalog.ProductPresentation`; se resuelve
por página y acompaña el snapshot/delta de catálogo existente. No hay consultas
adicionales por tarjeta ni inventario local. La búsqueda local consume esa misma
proyección en SQLite; catálogo y familia no tienen un sincronizador independiente.
El modo viaja por el snapshot/stream de configuración ya existente.

El constructor primario de `PosCatalogItem` define el único contrato JSON vigente.
La descarga debe incluir todos sus campos; un dato opcional puede tener valor
nulo, pero omitir su campo invalida el mensaje. No se sustituyen campos ausentes
por costos cero, flags predeterminados ni otro formato de producto.
El digest de integridad cubre el producto completo, incluida presentación,
composición e identidad/factor de inventario. API y Edge deben publicarse con
este mismo contrato y digest; no se acepta el digest parcial anterior.

El catálogo visible tiene un propietario de consulta, búsqueda con espera de 250 ms,
cancelación de solicitudes obsoletas y paginación explícita. Agregar una prenda usa
la respuesta autoritativa del comando de captura; no recarga catálogo o borrador.
Cada combinación sigue siendo un producto vendible distinto, con saldo propio
cuando no comparte inventario. Los atributos opcionales no generan combinaciones,
no agrupan líneas, no renombran productos ni modifican precios o impuestos.

Presupuesto de aceptación: páginas de 50 sobre un catálogo de 10.000 productos,
una petición de productos por cambio de búsqueda/categoría/página/familia y ninguna
por tarjeta; categorías una vez al entrar. Objetivo p95 de consulta online <500 ms
en entorno controlado, separado de carga de imágenes. El alta/cambio de cantidad,
cobro e impresión conservan los presupuestos y validaciones existentes. La prueba
local verifica además familias de más de una página y persistencia tras reinicio.

La preparación verifica todas las columnas del producto vigente en las tablas
local y de descarga. Si falta alguna, el catálogo deja de estar listo; se aplican
las ampliaciones de esquema existentes y el sincronizador canónico descarga el
catálogo actual completo. Una estructura que no puede completarse con esas
ampliaciones falla explícitamente y permanece inválida. No hay un modo alterno
para operar con otro formato. Las ventas, borradores y outbox se preservan y la
promoción del snapshot sigue siendo atómica. La comprobación usa las columnas
del mismo writer y no agrega lecturas por producto ni viajes de red por campo.
La recarga consulta los datos vigentes del producto y repuebla los atributos
registrados; no se completa la actualización dejando vacíos los valores que ya
existen en el servidor. Color y talla conservan su asignación explícita: crear
sus catálogos no permite inferir valores para productos que nunca los tuvieron.
La promoción del catálogo copia columnas por nombre, tanto en la tabla de
productos como en códigos e identificadores; no depende del orden físico que
dejen las migraciones de SQLite.
Las aperturas posteriores conservan el cursor incremental normal. Las señales
push se agrupan con el notificador compartido y no se añade polling.

La búsqueda materializa primero los IDs de la página y después resuelve sus
costos, clasificación, imágenes y atributos. La unicidad filtrada
`UX_ProductPrices_Active` respalda el único precio activo por sede/producto;
la selección conserva su vigencia y no agrega un segundo cálculo de precios.
Medición local controlada (SQL Server, Release, 10.000 productos, páginas de 50,
20 lecturas calientes alternando primera/siguiente, después de dos lecturas de
calentamiento): p95 de 464 ms frente a 849 ms antes de acotar el enriquecimiento.
La prueba conserva el umbral de 500 ms y la paginación se comprueba por API.
El E2E verifica una consulta inicial de categorías y otra de productos, sin
recargar el catálogo al capturar ni cambiar cantidades.

Rollback: devolver la sede a Venta directa mediante su actualización habitual;
conserva el mismo borrador y los productos/atributos. El esquema es aditivo y
puede permanecer durante la reversión de binarios. Una versión anterior de la
UI debe recibir la sede ya en Venta directa antes de retirarse Tiendas.

La navegación a Restaurante transfiere el bootstrap ya autorizado a su entrada
mediante la caché de navegación por tenant y usuario, consumida una sola vez.
Así no vuelve a consultar las mismas opciones durante esa transición. La sede
activa prevalece sobre una venta directa recordada en la aplicación instalada.
En Tiendas, el resumen y los documentos pendientes conservan su altura natural
dentro del panel desplazable; las ventas pausadas y los pedidos no quedan
debajo de un panel que intercepte sus botones.

### Verificación de Tiendas y revisión posterior (2026-11-05)

La sede local `Tiendas · prueba local` conserva nueve camisetas (S/M/L y tres
colores) con imágenes, familia y stock propio. Se ejecutaron contra API y SQL
reales los tres recorridos de `pos-store-real-local.spec.ts`: edición de ficha
con talla/color/nombre manual, carga de imagen y familia, captura y cantidad,
edición de línea, pausa/recuperación, faltante/exacto/cambio, emisión sin imprimir,
y pedido guardado/recuperado/cobrado desde el mismo borrador. Todos aprobaron.
La regresión fuerza una galería pendiente: guardar se detiene hasta tener su
resultado, sin convertir una carga pendiente o fallida en una lista vacía que
borre imágenes. Después comprueba que sus identidades se conservan al guardar.
El editor mantiene su consulta propietaria; la validación no agrega un GET.

Otros nueve recorridos automatizados aprobaron edición de sede sin 404, entrada
web e instalada a Restaurante con una sola lectura de bootstrap, catálogo Tiendas
sin refetch al capturar, pagos mixtos, configuración física/ambas por estación y
confirmación del actualizador. La autorización se ejerció además con un cajero
local sin `sales.drafts.restart`, sin caja preparada y con servidor disponible:
clave incorrecta rechazada, secundaria válida aceptada y acción ejecutada.
Esto no certifica entrega de una notificación a un teléfono ni papel físico.

La auditoría revisó el diff completo: mismo borrador, servicios de catálogo,
handler documental, writer de inventario, jobs y outboxes; sin otra API de
facturación ni contrato público de venta. La disponibilidad y el consumo leen
componentes por conjunto, multiplican/acumulan mediante el resolvedor compartido
y preservan orden, tenant, permisos, versión e idempotencia. Las respuestas de
configuración de impresora/comanda actualizan sus cachés sin segunda lectura;
no se incorporó polling ni un propietario de carga oculto. El rollback del motor
se rige por la condición de trabajos versión 2 de `restaurant-pos-design.md`.

Evidencia de esta revisión: 89/89 integraciones SQL de venta, producción,
enrolamiento y autorización; 1063/1063 de la suite Foundation completa;
218/218 de caja local; 259/259 del POS web más sus 16 previas y 15/15 de edición
de línea; ESLint de los archivos afectados y builds Release
SQL/API y producción web aprobados. Una ejecución SQL aislada posterior verificó
13/13 ventas de composición, incluida sincronización tras reinicio y versión
congelada, con aserción de motor menor de dos segundos: 57,7–835,9 ms, media
200,1 ms, sin otros builds o suites concurrentes. Las mediciones de la aplicación
completa bajo presión de memoria fueron variables y no se usan como benchmark.
La matriz adicional de 16 ventas reales en las sedes locales verificó el kardex
exacto y reintento sin doble consumo en los tres modos web y Venta directa offline.
Se ensayaron los 115 lotes de postdeployment sobre la copia local con rollback;
el reporte final de DEV agrega las columnas de ventas sin reconstruir sus tablas.

Compatibilidad histórica, 2026-11-06: los tres casos de reconstrucción de costos
aprobaron en SQL aislado. La simulación de una línea anterior a esta ampliación
deja también `RecognizedCostAmount` nulo, porque esa columna no existía: conserva
la comprobación de costo por kardex y la de costo genérico explícitamente
desconocido. No se cambia el contrato, el cálculo ni los datos reales del reporte.

### Ajuste de espacio y entrada de Tiendas (2026-11-06)

El tipo de sede se copia del contexto autorizado antes de presentar el cliente
online. La entrada automática a Tiendas no muestra transitoriamente la tabla de
Venta directa ni agrega una consulta para conocer ese tipo.

Desde 768 px, captura, líneas y resumen comparten una columna desplazable y el
catálogo permanece a su lado. Las líneas conservan un mínimo de 180 px y un
máximo de 45dvh: no compiten con el resumen mediante dos filas que puedan
comprimirlas a cero. Código y Agregar ocupan una sola fila. Tiendas usa únicamente
el buscador del catálogo, también con F1; cliente conserva un solo selector,
subtotal/impuestos permanecen visibles y Cargos es un botón de icono junto al
cliente, antes del número de líneas. Acciones de captura conserva la tipografía
de Venta directa (mayúsculas, tamaño y espaciado) en su único título expandible.
Venta directa también muestra Cargos como botón de icono a la derecha de Buscar
cliente, en la misma fila. Conserva nombre accesible, tooltip con Shift + F8,
habilitación y el único handler de cargos existente; no agrega consultas.
El acceso a familia es un botón de ojo dentro de la tarjeta, a la derecha del
precio, separado del botón de agregar para evitar capturas al consultar tallas.
Se conservan los mismos comandos y atajos de captura,
edición, cobro, pausa y pedidos.

La regresión de navegador observa el DOM durante una apertura con sesión lenta,
comprueba ausencia de la tabla de Venta directa y verifica visibilidad y acceso
real por toque a cantidades en 1280×720, 1536×864, 1024×768 y 938×868. Conserva
las aserciones de una carga inicial de catálogo, sin refetch al agregar o cambiar
cantidad, y ausencia de desbordamiento horizontal en móvil.

Verificación del ajuste visual posterior (2026-11-06): build de producción y
lint de los archivos afectados aprobados; 2/2 recorridos de captura de Tiendas y
cargos de Venta directa verifican posición/tamaño del icono, totales visibles,
tipografía, acceso por teclado, comandos originales y conteos de lecturas.
La ficha real de la camiseta azul pasó 1/1 comprobando que portada y miniatura
tienen píxeles cargados, sin conformarse con que exista el elemento `img`.
El pago real local a 1037×520 muestra sus cinco medios y enfoca el valor en
efectivo; faltante bloquea y exacto/cambio habilitan confirmar. Esa comprobación
no emitió una venta ni cambió la API o el flujo de facturación.

Las imágenes de productos se suben mediante el servicio canónico de medios y
se almacenan como rutas relativas en el contenedor privado del tenant. El
resolver obtiene la cuenta y la firma de la configuración del ambiente; no
convierte DEV en un servidor de imágenes para otros ambientes ni incorpora
URLs de demostración en la aplicación. La carga de una URL externa conserva
su requisito HTTPS. Para demostrar la independencia del almacenamiento, la
prueba real de DEV subió y leyó 24 imágenes de productos DEMO desde su propia
cuenta Blob y comprobó los bytes descargados. La prueba equivalente en
producción requiere una sesión autorizada de ese ambiente y no implica
publicar código en producción.

La captura, cargos, guardado e impresión conservan sus propietarios, permisos,
versiones, idempotencia y fronteras. Las modificaciones de presentación no
agregan lecturas, polling ni recursos ocultos consultando. La reversión visual
sigue siendo compatible con los contratos actuales.
