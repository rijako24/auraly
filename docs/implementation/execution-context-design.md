# Contexto de ejecución web y POS

Fecha: 2026-08-05

## Decisión

El inicio de sesión autentica exclusivamente al usuario. No solicita tenant, negocio, sede, bodega ni dispositivo.

`AppUsers.TenantId` identifica el tenant de origen de la cuenta y de la sesión autenticada; no concede por sí solo acceso operativo a todos sus datos. El alcance de trabajo se obtiene de las asignaciones vigentes `UserRoles -> AppRoles.TenantId` y, cuando existe, `UserRoles.BusinessId`. Los permisos determinan acciones; las membresías determinan alcance.

## Web

1. Después del login, `GET /api/v1/execution-context/tenants` obtiene únicamente los tenants autorizados.
2. Con un solo tenant se selecciona automáticamente y no se muestra un control innecesario.
3. Con varios tenants, el selector vive en el menú del usuario junto a perfil y cierre de sesión.
4. Se restaura el último tenant local solamente si el servidor todavía lo devuelve como autorizado.
5. `GET /api/v1/execution-context/businesses`, con `X-Tenant-Id`, obtiene los negocios autorizados, incluida su `timeZone`. La misma consulta de `ExecutionBusinessesList` lee `Businesses.TimeZone`; el adaptador entrega una identidad IANA para los controles de fecha del navegador, convirtiendo identificadores Windows existentes mediante `TimeZoneInfo`. Una zona inválida no se sustituye por la del servidor o navegador. No se agrega otra consulta de contexto por formulario.
6. El negocio permanece visible en el encabezado porque cambia con mayor frecuencia y afecta precios, inventario y facturación.
7. Cada solicitud operativa envía `X-Tenant-Id` y `X-Business-Id` por el BFF.

Cambiar tenant invalida el negocio seleccionado y obliga a escoger automáticamente un negocio válido del nuevo tenant. Una preferencia de navegador nunca concede acceso.

## Sesión de dinero y empresa del usuario (decisión 13-11-2026)

El usuario decidió conservar la restricción de identidad para las operaciones
de dinero: los comandos de WorkSessions permanecen en el tenant propietario
de la cuenta autenticada. Una membresía de lectura/administración en otro tenant
no permite abrir allí una sesión de caja con esa cuenta. Se conservan
FK_WorkSessions_UserTenant, ValidateScopeAsync y las rutas POS actuales.
No se agrega un adaptador de apertura ni una sesión alternativa para eludirla.
La configuración administrativa puede leerse en el contexto seleccionado
según sus permisos; eso no autoriza por sí solo un recaudo o egreso en él.
Para registrar el pago debe usarse una cuenta de acceso de esa empresa.
La prueba local utiliza el tenant/sede AURALY del usuario existente y una base
aislada, preservando esta decisión. No se corrigen identidades del seed ni
se alteran autorizaciones para hacer pasar la prueba.
La comprobación real accounting-local-real-finance-v7 aprobó 2/2 desde la UI
contra esa base: abono CxC, gasto/CxP, contabilización balanceada, replay sin
duplicación, desaplicación, cruce y restitución de anticipo, devolución de
dinero y anulación del gasto con saldo a favor preservado. El historial mantiene
el recibo bruto y cambia su neto aplicado según las correcciones. La primera
ronda alcanzó todos los efectos pero tenía una expectativa antigua del neto;
se corrigió a 30,2498 después de desaplicar 50,0001 y devolver 20,0001 de un
recibo de 100,25. No se cambió el cálculo para mantener el test anterior.
El servidor local y login respondieron 200. Las pruebas externas fiscales no
forman parte de esta evidencia local.

## Aplicación instalada

El tenant proviene del paquete firmado de enrolamiento y queda fijado al dispositivo. El usuario no ve un selector de tenant. El negocio continúa siendo seleccionable únicamente entre los permitidos para ese tenant y usuario.

El bootstrap online del POS devuelve el `TenantId` efectivo para que todos los llamados posteriores utilicen el mismo contexto. El modo offline usa el tenant persistido por POS Edge durante el enrolamiento.

## Validación del servidor

`ExecutionContextMiddleware` es el único componente que resuelve el contexto HTTP. Reemplaza los middlewares anteriores que interpretaban `tenants.read` como acceso directo a cualquier negocio.

Cuando se envía un contexto explícito, el middleware:

- valida usuario activo, tenant activo y negocio activo;
- valida la membresía de tenant y negocio;
- vuelve a cargar roles y permisos aplicables al contexto;
- reemplaza los claims operativos `tenant_id`, `business_id`, roles y permisos;
- configura `ITenantContext` para los componentes existentes;
- responde 403 si el contexto dejó de estar autorizado.

`tenants.read` conserva el caso administrativo global existente, pero no sustituye la selección explícita de tenant ni permite mezclar negocios de distintos tenants en una consulta.

## Configuración de pagos administrativos (13-11-2026)

La captura administrativa de CxC/CxP consulta
`GET /api/commerce/v1/accounting/settlement-configuration`. Ese adaptador usa el
contexto seleccionado ya autorizado por ExecutionContextMiddleware y llama al
mismo lector de Accounting y snapshot de formas de Catalog que el POS. Exige
lectura contable o permiso de registrar el recaudo/pago. No crea configuración,
otro catálogo, writer ni flujo de pago. El costo conserva las lecturas actuales:
estado contable, bancos sólo cuando aplica y un snapshot de catálogo por lote;
es independiente de la cantidad de formas.

La ruta de POS conserva el tenant inmutable de identidad/enrolamiento y sus
consumidores existentes. La captura web administrativa antes utilizaba esa ruta:
al seleccionar otra organización autorizada se rechazaba su configuración y la
confirmación permanecía deshabilitada. Ahora sólo esa captura usa el adaptador
administrativo. La caché distingue `execution` de `identity`, además de tenant,
sede y contexto de pago; no reutiliza configuración entre ambos alcances.
Una carga fallida se informa y su reintento explícito conserva los importes
capturados, sin polling ni confirmación antes de obtener configuración válida.
No hay migración de datos; rollback coordinado de UI/API preserva los documentos
y restablecería la limitación del acceso administrativo entre organizaciones.
`administrative-scope` aprobó los cuatro casos SQL/API: permiso de recaudo,
pago a proveedor, lectura contable y sólo lectura de cartera. Comprueba la
versión 2 del catálogo del tenant seleccionado con una consulta de snapshot,
rechazo de tenant ajeno y conservación del alcance de identidad en la ruta POS.
La matriz de navegador verificó quince casos administrativos y las veinticuatro
variantes POS web/instalada e impresión. El caso de recuperación pasó en la
repetición dirigida con el formato decimal de captura correcto: conserva el
importe al fallar/reintentar, hace sólo dos lecturas y no confirma sin
configuración. Lint dirigido y build Next completos pasaron. La regresión
adicional de informes/novedades aprobó 16/16, incluida la revisión de fecha de
corte completa y conteos de registros del informe con saldo a favor.

## Persistencia de preferencia

`selected_tenant_id` conserva el último tenant en el navegador. Al iniciar:

- si sigue autorizado, se restaura;
- si fue revocado, se usa el primer tenant autorizado;
- si solo existe uno, se activa automáticamente;
- si no hay ninguno, se muestra un error de acceso y no se abre el panel.

La validación efectiva siempre está en SQL Server y se ejecuta en la API.

## Pruebas

La cobertura incluye:

- membresía del mismo usuario en dos tenants;
- listado de negocios limitado al tenant seleccionado;
- rechazo de tenant no asignado;
- recálculo de permisos al seleccionar contexto;
- rechazo de permisos presentes solo en el token de login;
- restauración del último tenant todavía autorizado;
- descarte seguro de una preferencia revocada;
- propagación de `X-Tenant-Id` y `X-Business-Id` a través del BFF.
- zona del negocio autorizado, incluyendo configuración IANA y Windows, en una sola lectura de negocios.

## Zona horaria por sede (decisión 12-11-2026)

La ficha de sede administra el campo existente `Businesses.TimeZone`; no se
introduce otro catálogo ni otra configuración. `BusinessTimeZoneResolver`
normaliza y valida las zonas configuradas a IANA, compartido por administración
y contexto web. Un valor explícito vacío o inválido se rechaza antes de alterar
la entidad; la omisión en creación conserva el default documentado. Omitirlo
en actualización conserva el valor actual. El alta de la sede principal por
aprovisionamiento aplica la misma validación y normalización. La actualización devuelve la zona
autoritativa: el único query de negocios alimenta su provider y actualiza el
contexto visible, y la misma respuesta actualiza su copia offline, sin GET extra.
Los instantes históricos permanecen intactos. La transición de filtros de
cartera está especificada en comprobantes abiertos, sección 14.10.


### Presentación y captura de fechas locales

`useBusinessDates` enlaza los formateadores y la propuesta de día al contexto
ya cargado; no obtiene negocios, no añade reloj operativo ni hace I/O. Su falta
de zona produce un error explícito en los consumidores migrados. Las fechas
DateOnly conservan su día en `formatDate`, mientras los instantes se presentan
en la zona recibida explícitamente. La firma opcional de los formateadores
anteriores permite migrar los demás consumidores sin cambiar de golpe sus
contratos; esos consumidores siguen en el inventario de fechas pendiente.

Cartera migra vencimientos, movimientos, historial y la propuesta Elegir hoy
del pago administrativo y sus filtros a ese enlace. Sus claves de
consulta incluyen zona cuando ésta cambia los límites/edades del resultado;
el cambio autoritativo de zona solicita una nueva lectura al único propietario
visible, sin invalidación global ni remontar vistas. El detalle inmutable sólo
cambia su formato y no vuelve a consultarse. El modal POS conserva su clave de
contexto y captura existentes; sólo el administrativo incluye zona en su alcance.

DatePicker conserva el único calendario y recibe `today` de sus consumidores
de negocio para navegación y Elegir hoy. Opera fechas de calendario mediante
un portador UTC neutral: esto no convierte el día elegido en un instante ni
usa medianoche UTC como fecha financiera. Evita que un día omitido en la zona
del navegador se desplace a otro, aunque exista en la sede. Nómina, gastos y
los filtros de cartera migrados suministran ese día; los restantes consumidores
se revisan por su propietario. No se añade un temporizador ni polling.

Aceptación de este incremento de UI: fechas visibles iguales con navegador
lejano, cambio de sede con días distintos, selección Elegir hoy, calendario en
zona con día omitido, plazo del proveedor que atraviesa DST, fuente UTC/+offset
coherente y cero nuevas lecturas de contexto. Se mantiene render/página acotado
(42 días del calendario y páginas de cartera existentes), sin I/O por fecha.


Al cambiar sólo de sede, BusinessSwitcher conserva las listas de tenants y
negocios del mismo usuario/tenant: su alcance no cambia. Acceso y datos
operativos conservan el reset existente; el nuevo acceso se autoriza de nuevo
en servidor. Evita eliminar la consulta activa de membresías y volver a hacer
GET para los mismos metadatos. Cambio de tenant y cierre de sesión conservan
su limpieza; el contexto no concede acceso por una preferencia local.


### Auditoría del incremento de presentación (2026-11-12)

Se revisó el diff de los formateadores, hook, calendario, selectores de sede,
claves de consulta y consumidores migrados: cartera, nómina y captura de gastos.
La zona sigue teniendo un único propietario, Businesses.TimeZone; el hook sólo
consume el contexto existente. No agrega catálogo, dependencia, reloj de dominio,
cola, writer, estado financiero ni consulta. Los contratos de fecha y los
instantes ya guardados permanecen intactos. Los lectores paginados y el comando
financiero conservan sus propietarios, autorización, alcance y replay.

El cambio de zona modifica la clave del único lector visible cuando afecta sus
límites/edades; el detalle inmutable sólo cambia de formato. El cambio de sede
conserva metadatos de membresía, pero descarta acceso y datos operativos. No hay
I/O por fila, vistas ocultas adicionales, polling ni nuevas lecturas SQL. El
calendario sigue acotado a 42 días. Las propuestas no confirman pagos ni alteran
el cálculo contractual. El efecto de carga de mappings conserva su dependencia
explícita de tenant/sede sin recrear innecesariamente el callback.

La revisión detectó tres DatePicker administrativos que aún proponían el día
del navegador. La regresión se ejecutó contra el bundle previo y falló para
cliente y proveedor: esperaba 11 de noviembre y mostraba 12, con reloj UTC fijo
y navegador UTC+14. La corrección pasa el mismo día de sede a fecha del pago y
ambos filtros; los formatos de instantes alternativos también reciben su zona.
El camino POS no calcula esa propuesta y conserva su captura e impresión.
La captura visual administrativa se inspeccionó con los tres valores locales.

La primera tanda amplia registró 68/77 aprobadas; se corrigió la recarga de
contexto y se actualizaron fixtures antiguos de contratos/respuestas y etiquetas.
Se mantuvieron las comprobaciones de saldos, selección, permisos, errores y
conservación de datos: no se deshabilitaron validaciones productivas para pasar
las pruebas. La siguiente tanda tuvo 28/29; el único fallo era un selector de
texto ambiguo en Gastos, corregido a coincidencia exacta. La tanda complementaria
pasó 57/57. Logs: business-dates-browser.log,
business-dates-browser-recheck.log y business-dates-browser-final.log.
La reproducción anterior del calendario está en business-dates-picker-before.log.

Rollback: revertir los enlaces de presentación/captura no necesita migrar ni
reparar fechas. Los filtros de cartera pueden volver al contrato versión 1
según su decisión propietaria. Cambiar la zona tiene efecto sobre la presentación
y agrupación por día de instantes históricos, sin modificar su valor guardado.
Se preservaron los cambios ajenos del checkout. El inventario transversal de
14.10 de comprobantes sigue vigente: esta evidencia no certifica todos los
consumidores restantes ni cierra los gates de nómina/DIAN del diseño general.


La tanda business-dates-picker-after.log aprobó los primeros 25 escenarios
(nómina y pagos administrativos) y detectó una regresión introducida al enlazar
el formateador de instantes: siete variantes POS fallaron antes de interrumpir
la tanda. POS no carga el contexto administrativo y no debe exigirlo para
presentar Vence. Se corrigió ese call site conservando el formateador existente
para POS y pasando zona explícita sólo en la presentación administrativa.
Ambos reutilizan la misma función base de formato; no se añadió un resolvedor,
consulta ni fallback de zona. La regresión queda conservada en el log, y exige
repetir los dos calendarios administrativos y las 24 variantes POS antes de
cerrar este incremento.


Cierre local de este incremento: business-dates-pos-preserved-browser.log pasó
26/26 sin skips, incluidos los dos calendarios administrativos y las 24
variantes POS web/instalada online e impresión. Complementa los 25 casos
administrativos aprobados antes de corregir ese único call site y la tanda
57/57 de sedes, gastos, días de nómina, informes y pagos. La cobertura reunida
contiene 83 escenarios únicos de navegador; no es una tanda única ni una
certificación del diseño completo. business-dates-reviewed-unit.log pasó 11/11;
business-dates-pos-unit.log conserva 283/283 del motor POS, que este ajuste no
modificó. El build completo final y tipos pasaron
(business-dates-pos-preserved-build.log); lint de los archivos revisados quedó
sin errores ni advertencias después de corregir las dependencias del efecto de
nómina. tsconfig.json y next-env.d.ts se restauraron byte a byte.

La auditoría posterior volvió a comprobar el único call site POS y los dos
administrativos, los límites de captura, las claves y propietarios de carga,
las pruebas y documentos. No se incorporaron cuentas/configuraciones congeladas,
motores o escrituras alternas ni consultas nuevas en esta presentación. Se
cerró el servidor temporal propio. La memoria se liberó cerrando sólo procesos
propios y el navegador de inicio sin ventanas previamente identificado; no se
modificaron parámetros de SQL ni servicios. No se desplegó a DEV/producción ni
se alteraron documentos de esos entornos.

La regresión de bordes históricos detectó que TimeZoneInfo en Windows, aun
recibiendo un ID IANA, entrega para São Paulo 2018 un inicio un milisegundo
anterior y modela incorrectamente el salto de Apia de 2011. La búsqueda binaria
sobre ese dato no puede corregir la fuente. Se justifica por esta reproducción
la dependencia NodaTime 3.3.7 (TZDB 2026f) en el BuildingBlock de dominio: el
helper puro BusinessCalendar resuelve límites y fechas desde TZDB, sin reglas
por país, parches de compatibilidad ni otro reloj. TimeZoneInfo conserva la
identidad del contrato existente; los cálculos migrados y BusinessClock usan
el helper. El cambio no reescribe instantes. Un día omitido tiene intervalo
vacío, y el día siguiente empieza en el mismo instante. Se conservan esperados
exactos de UTC de las pruebas; no se ajustan a la limitación del sistema operativo.

Referencia de la dependencia y sus datos: https://www.nodatime.org/versions.
La política de cambios de horario y días omitidos pertenece a DateTimeZone.MapLocal
(https://www.nodatime.org/3.3.x/api/NodaTime.DateTimeZone.html). Actualizar el paquete
requiere revisar su TZDB y repetir regresiones de calendarios; no se descarga
ningún dato de zona durante una operación. Los helpers se ejecutan en memoria,
sin aumentar viajes SQL ni red. La regresión SQL y los presupuestos se repetirán
con el helper definitivo antes de cerrar el incremento.

La primera corrida completa de dominio detectó los dos defectos históricos
(1.208 aprobadas/2 fallidas). La nueva fuente TZDB conserva los esperados UTC
exactos: São Paulo 2018-11-04 empieza a 03:00Z y el 30 de diciembre de 2011 en
Apia tiene intervalo vacío. calendar-tzdb-foundation-final.trx aprobó 1.211/1.211,
sin omisiones, con cartera y las demás regresiones del proyecto Foundation.
Se añadió el rechazo explícito de una fecha de pago omitida, antes de numerar
o aceptar la fuente; ambos adaptadores de cartera traducen ese error a validación.
El build del servidor con la dependencia fijada pasó sin errores/advertencias.
Las pruebas SQL se repiten sobre el binario definitivo. Se liberaron sólo los
siete nodos de restauración propios cuyo proceso propietario ya había terminado;
no se alteraron aplicaciones del usuario ni los parámetros de SQL.

La tanda ampliada calendar-tzdb-financial-reviewed.trx ejerció 41 escenarios:
38 aprobaron y tres de readiness detectaron contaminación entre fixtures. Los
escenarios de lectura creaban sedes activas sin centro y las dejaban activas;
la validación del tenant correctamente las detectó después. Se conserva la
validación productiva: los tres fixtures de lectura desactivan exclusivamente
sus sedes auxiliares al terminar (también ante fallo), con una escritura por
conjunto fuera de las mediciones. No se elimina historia ni se cambia la sede
principal del fixture. La comprobación se repetirá en la misma tanda combinada.

Los presupuestos sí pasaron en esa corrida, sin diagnósticos concurrentes:
100.000 fuentes, v2 p95 máximo 738,1 ms (límite 800), dos SQL por página;
informes de 10.000, CxC máximo 686,6 ms y CxP 609,8 ms, dos SQL. Historial local
con fecha: dos SQL, p95 máximo 74,0 ms en CxC y 40,1 ms en CxP. Sin fecha,
un SQL; facturas, tres; selector administrativo, uno, p95 máximo 479,8 ms y
primera lectura máxima 1.016,0 ms. No se repite este benchmark sólo por corregir
la limpieza del fixture: no cambió el camino productivo medido.

La auditoría del reloj configurado también retiró la sustitución silenciosa de
un ID explícito inválido en BusinessClock: se valida la configuración antes de
resolver la zona. Se conserva su contrato de ausencia de configuración. Los
nuevos tests del reloj verifican un único acceso a Business, ningún acceso a la
integración de calendario para una sede configurada, Apia, DST, ID Windows y
rechazo explícito de una zona inválida. El snapshot y la cartera reutilizan
BusinessCalendar; no se agrega otro reloj, consulta ni configuración congelada.
La representación UTC previamente publicada de PaidAt se conserva en la
respuesta y el encabezado operativo de los nuevos pagos nativos. La fuente
contable conserva el offset de la fecha elegida al transformar PaidOn; no se
normaliza ese calendario antes de seleccionar activación, mappings y período.
Se reutiliza el contrato datetimeoffset de fuentes existente, no una copia de
la zona ni de configuración. Los días inexistentes se rechazan antes de
numeración/aceptación. La grilla de trazabilidad conserva su formato de fecha numérica;
sólo cambia el calendario elegido, no agrega una hora a esa columna.

La repetición calendar-fixture-isolation-reviewed.trx aprobó 38/38 sin omisiones,
con los tres escenarios de readiness corregidos y las pruebas de migración de
IVA, instalación nueva, compra nueva contabilizada una sola vez, replay,
aislamiento, cierres, períodos de consulta y conservación de cortes históricos.
El arreglo fue exclusivamente la preparación/limpieza de datos de las pruebas;
no se relajó la comprobación contable. Los presupuestos de volumen de la tanda
anterior siguen vigentes; el binario productivo medido no cambió por esa limpieza.
La compilación definitiva del servidor pasó sin errores ni advertencias.
