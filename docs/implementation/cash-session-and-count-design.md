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
- entradas y salidas en renglones separados;
- impuestos agrupados por código y tarifa;
- total esperado, contado y diferencia;
- observación y responsables.

La cabecera muestra `ARQUEO DE CAJA · CIERRE CONFIRMADO`. La tirilla funciona
en 58 y 80 mm. La reimpresión usa exactamente el snapshot guardado.
El detalle se presenta antes del total neto y la diferencia de efectivo cierra
el comprobante con mayor jerarquía visual. El catálogo comercial usa un único
medio `Transfer`; no existen variantes paralelas para transferencia bancaria o
consignación.

## Conciliación posterior del cierre

La vista `/dashboard/cash-differences` queda consolidada como **Cierres de caja** y lista todos los cierres, no solo los que tienen diferencia. El detalle conserva ventas, devoluciones, ventas a crédito, periodo, usuario, sede, bodega y esperado/contado por medio. El POS solicita exactamente tres valores ciegos: `Cash`, `Card` y `Transfer`.

`work-sessions.differences.read` autoriza la consulta de todos los cierres del
tenant, sin restringirlos al usuario que abrió o cerró la sesión. Consultar la
sesión activa propia (`work-sessions.read`) es independiente y no es requisito
para abrir este historial. Solo `work-sessions.closures.reconcile` habilita la
edición y confirmación de la conciliación; un visor conserva acceso de solo
lectura al mismo detalle.

Las diferencias nunca se presentan como números negativos: se rotulan `Sobrante`, `Faltante` o `Cuadra` y se muestra su valor absoluto. La calculadora de denominaciones no forma parte del cierre: se abre directamente desde ventas con `Ctrl+D`, consume el catálogo `cash-denomination` y puede imprimir una tirilla independiente sin abrir, cerrar ni modificar la sesión.

La conciliación exige confirmar cada medio, registra valor verificado, motivo catalogado y snapshot inmutable. Puede reclasificar un faltante contra un sobrante —por ejemplo, transferencia registrada como efectivo— sin exceder las diferencias reales. El cierre contabiliza inicialmente cada diferencia contra `CashClosureDifferencesPending`; la conciliación lleva solo la diferencia genuina a ingreso/gasto y conserva trazabilidad e idempotencia. Si la reclasificación compensa exactamente ambos medios, los asientos del cierre ya forman la reclasificación y no se genera un asiento duplicado.


### Corrección del medio de recaudo y forma del tenant (18 de noviembre de 2026)

La corrección posterior conserva el documento comercial, `SalesPayments`, el XML,
el CUFE y los artefactos fiscales originales. No vuelve a emitir ni cobra la venta.
La captura se denomina **Corregir medio de recaudo** y exige la forma concreta del
catálogo compartido del tenant. Efectivo no pide referencia; transferencia exige
referencia; débito/crédito exigen franquicia vigente y aprobación. La factura
emitida y el recaudo corregido se presentan como datos distintos. Las diferencias
de valor siguen su proceso propio, no esta selección: toda nueva corrección conserva
el importe original, tanto en ventas como en abonos de clientes, pagos a proveedores
y movimientos de caja. La captura muestra ese importe de sólo lectura y la
aceptación lo comprueba aunque la solicitud no provenga de la interfaz. Los
snapshots ya aceptados conservan su interpretación original; no se reescriben ni
se vuelven a aceptar para aplicar esta restricción.

`WorkSessionService` / `SqlWorkSessionStore` conservan autorización, aceptación,
snapshot, hash e idempotencia de la única conciliación. Su contrato se extiende con
identidad/versionado de la forma y un ID de corrección generado en aceptación.
La versión se valida al aceptar; no congela cuentas para el procesamiento futuro.
La cuenta de origen se obtiene del auxiliar efectivamente utilizado por el pago
original o de los bindings del trabajo ya terminado, con alcance tenant/sede.
El seed de tipos conserva las identidades históricas de `BankTransfer` y
`Deposit`, aunque estén desactivadas. `PendingCollection` usa una identidad nueva
en instalaciones nuevas; si ya existe se conserva su ID por la clave catálogo/código.
Repetir el seed no sustituye tipos referenciados ni agrega opciones duplicadas.
Si esa evidencia no existe o es ambigua, se rechaza la corrección configurada sin
adivinar una cuenta de configuración actual ni reparar documentos anteriores.

El resolvedor de formas de `SqlAccountingPostingProcessor` carga todos los destinos
en un lote durante su transacción, valida uso y política canónica del auxiliar y
utiliza la cuenta vigente en ese intento. El origen aceptado se conserva. Los
bindings realmente aplicados quedan en `AccountingPostingJobs.AppliedPaymentMethodsJson`,
identificados por el ID de corrección; no se sobrescribe la fuente inmutable.
Cambiar entre dos bancos o datáfonos del mismo tipo puede producir una
reclasificación. Con igual auxiliar e importe sólo se conserva evidencia: el
trabajo termina como `CommercialEffectsApplied`, sin un asiento vacío o duplicado.
Repetir un comando terminado devuelve el resultado y no resuelve otra vez cuentas.

La devolución posterior consulta el recaudo efectivamente aplicado, no vuelve a
usar el datáfono o soporte original después de una corrección. La proyección
`accounting.SalePaymentPostingFacts` conserva sus campos originales y expone por
separado medio, importe, soporte y auxiliar efectivos. Los obtiene del snapshot
de la única conciliación y de sus bindings aplicados; no del catálogo actual.
Una corrección pendiente o una evidencia ambigua impiden reembolsar hasta resolver
ese procesamiento. La consulta retornable y el processor comparten esa evidencia.
Un trabajo comercial que no requería asiento conserva esa condición: su corrección
sin forma configurada puede aportar medio y soporte sin inventar un auxiliar
histórico. El reembolso comercial no exige un asiento anterior ni crea uno.
La selección de una forma nativa tampoco obliga a inventar un origen contable
para ese trabajo comercial: valida y resuelve el destino configurado, conserva
el medio original como evidencia y no produce una reclasificación sin asiento
de origen. Cuando la conciliación sí requiere asiento, el processor exige el
auxiliar histórico; no lo sustituye por una cuenta vigente inferida.

La aceptación de un reembolso reserva su importe desde `SalesReturns`, aunque
todavía no existan settlements. A su vez, una venta con reembolsos aceptados no
admite corregir su recaudo desde el cierre: mover nuevamente el importe bruto
perdería el efecto del reembolso. Ambas aceptaciones conservan el bloqueo de la
venta original en su transacción; no añaden otra reserva, estado ni writer.
La comprobación se incorpora a los lotes existentes, sin otro viaje SQL por pago.
El cambio requiere publicar primero el DACPAC que amplía la función; no modifica
facturas, fuentes, soportes fiscales ni asientos históricos. Tras aceptar estos
reembolsos no es seguro volver a un binario que ignore las correcciones aplicadas;
corresponde roll-forward, sin borrar ni reprocesar documentos.

La extensión es aditiva sobre el contrato existente de conciliación; los comandos
sin forma concreta y las fuentes ya aceptadas mantienen su semántica anterior.
La interfaz actual exige seleccionar una forma del tenant. No hay otra versión de
catálogo, endpoint, worker, tabla de cierre ni writer de pagos. El permiso de
conciliar permite consultar únicamente usos de captura y los cuatro tipos de
dinero; no habilita administración del catálogo ni consulta sin uso/tipo acotados.

Presupuesto del incremento: máximo 500 correcciones por comando. La validación
configurada añade un único viaje SQL al validador anterior, independientemente
del número de formas. El posting añade un lote para todos los destinos; no hay
lecturas por pago, polling ni refetch del cierre al editar un borrador. La consulta
del selector pertenece a `PaymentMethodSelect`; franquicias se consultan sólo al
abrir un editor de tarjeta. El objetivo local de preview caliente es dos segundos.
Las pruebas dirigidas ejercen venta real, preservación del original, cambio de
cuenta después de aceptar, mismo auxiliar, soporte incompleto, versión obsoleta,
alcance de catálogo y replay. Los resultados medidos se adjuntan en la entrega.

Despliegue coordinado de API/admin desde `origin/main`, después de sus checks.
Este incremento no necesita cambios de esquema ni transforma datos históricos.
Rollback: antes de aceptar nuevas correcciones se puede volver al binario anterior;
después de aceptarlas se debe completar el trabajo con el processor nuevo o aplicar
roll-forward, porque el anterior no interpreta la selección nativa. No se rebobinan
asientos ni se borran conciliaciones para volver a una versión previa. Este slice
no completa por sí solo la unificación pendiente de cierres de despacho.

Verificación local del incremento: build Debug sin errores ni advertencias,
TypeScript y ESLint focalizado correctos, regresión SQL/API de 51 casos y
verificación posterior de los ocho escenarios nativos (incluido un cierre de
25 facturas reales). El preview del propietario conservó tres consultas: en
el lote de 25 facturas tardó 431 ms, sin builds ni navegador concurrentes.
Playwright aprobó cinco casos: los cuatro tipos de pago y la apertura separada
de los modales actuales de caja/despacho. Verifica forma, soporte obligatorio,
importe fiscal de sólo lectura, móvil y ausencia de refetch del cierre al
aplicar un borrador. Evidencia en `work/release-preparation-current/closure-native-*`.

La verificación posterior del importe fijo aprobó 19 casos de integración
(`fixed-amount-regression.trx`): rechaza aumentos y reducciones desde caja y
cartera, conserva los abonos y saldos originales, y mantiene la corrección de
medios nativos y comerciales sin asiento. La captura aprobó ocho casos de
interfaz (`fixed-amount-browser.log`), cuatro medios en factura y comprobante,
con importe de sólo lectura, soporte específico y sin otra consulta del cierre.
Los casos de interfaz usan respuestas controladas; no sustituyen la validación
en el navegador contra DEV después de publicar. Los resultados se solapan con
las suites anteriores y no se suman como casos distintos.

La auditoría posterior revisó contratos, aceptación, resolución, persistencia,
permisos, presentación y pruebas del incremento. Conserva los motores y tablas
existentes; no introduce I/O por elemento, polling ni otro propietario de carga.
Las propiedades opcionales nuevas omiten null al serializar para conservar el
hash de reintentos anteriores. La aceptación inmoviliza el tipo técnico mediante
`TypeLockedAt`; cambiar la cuenta mantiene su política de vigencia al procesar.
Las comprobaciones de uso original, uso corregido y pago de gastos del despacho
consultan por identidad de la forma, con índices filtrados independientes en
`DispatchDeliveryPayments.PaymentMethodId`, `ReviewedPaymentMethodId` y
`DispatchExpenses.CashPaymentMethodId`. El guard conserva un solo lote de guardado
y el presupuesto existente de dos segundos; no carga el historial para decidir
si una forma ya fue usada ni añade consultas por sede o por recaudo.
Cuando el cierre ya contabilizó diferencias, la conciliación usa sus bindings
aplicados; una evidencia histórica ausente bloquea el ajuste, sin un fallback
al mapping actual. Esta evidencia no acredita la unificación financiera de
despachos, la regresión de todo el checkout ni un despliegue a DEV/producción.

La presentación del cierre de despacho debe seguir el patrón del cierre de caja
existente: cabecera, secciones por medio con esperado/contado/diferencia, grupos
plegables, verificación individual y confirmación. Mantiene un modal separado
para la información del despacho y añade únicamente declaración del transportador,
gastos y devoluciones. No se sustituye por un dashboard o diseño visual diferente.


### Encabezado común de despacho — corte en validación (18 de noviembre)

`WorkSessions.Kind` distingue Normal/Dispatch. El vínculo único `DispatchId`
identifica la sesión de recaudo de cada recorrido; la sesión normal del usuario
puede permanecer abierta simultáneamente. `worksessions.DispatchSessionEnsure`
la recupera dentro de la misma transacción de la entrega. No genera otra
consulta de red ni otro movimiento para representar la misma entrega.
`worksessions.ClosureAccept` es el único insert del encabezado y los totales,
compartido por los dos tipos de cierre. Exige una transacción, sesión abierta,
tenant/sede, snapshot/hash UTF-8 coherentes y declaración del transportador para
el tipo Dispatch. La ruta normal de cierre y conciliación filtra Kind=Normal.

`dispatch.Closures` es una vista de esos encabezados comunes, no otra tabla ni
un writer financiero. La declaración ciega permanece inmutable; la revisión
administrativa conserva sus valores separados en el mismo encabezado. Aceptar
la recepción no abre la sesión normal del administrativo ni inserta CashIn.
Los gastos siguen entrando por Gastos y sus documentos existentes. La unificación
del encabezado no acredita por sí sola que la reclasificación y las devoluciones
de despacho estén completas: siguen siendo compuertas previas al despliegue.

La aceptación administrativa conserva su instante `RequestedAt` y los documentos
financieros usan ese origen temporal. Se retiró el parámetro interno `businessDate`
que ya no utilizaba el store, junto con la consulta de reloj previa del endpoint.
El contrato HTTP no cambia y el ajuste no introduce otra fecha, sesión ni viaje
SQL; la tirilla conserva su consulta de zona horaria para representar los instantes.

La migración `20261118_UnifyDispatchWorkSessionClosures.sql` corre antes del plan
DACPAC en el pipeline de release existente, con API y Worker detenidos. Conserva
el ID del cierre y todos los campos originales; guarda también la fila histórica
en el snapshot. Las declaraciones inexistentes quedan NULL, nunca cero. Compara
los campos antes de retirar la tabla anterior, no reproduce documentos ni
asientos y falla si el encabezado anterior tiene operaciones aún sin completar.
El reintento conserva los mismos IDs y no agrega sesiones. La recuperación es
roll-forward con binarios compatibles; si se requiere volver al release anterior,
se restaura el respaldo verificado del corte y sus binarios como unidad. No se
reanuda el writer antiguo contra el esquema nuevo.

La regresión local `dispatch-common-regression.trx` tiene 39 pruebas aprobadas:
instalación nueva, migración desde ambos formatos históricos (con/sin declaración
de tarjetas y transferencias), repetición, cuatro medios, edición seguida de retry antiguo,
concurrencia del cierre, gastos, permisos, sesión normal y bandeja paginada.
400 encabezados: primera página de 50 en 161 ms, dos viajes para despacho y tres
para caja. El historial de entregas conserva recibos de aceptación y un solo
resultado IsCurrent; un replay antiguo devuelve el estado actual sin sustituir
sus pagos. Esta evidencia corresponde al encabezado; la posterior atribución de
pedidos exige sus propias pruebas. No se ha desplegado este corte.
