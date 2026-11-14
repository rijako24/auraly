# Diseño: cierres, informes de cartera y cruce de cuentas

Fecha: 2026-09-25. Estado: diseño funcional y contable cerrado para implementación.

Actualización objetivo del 2026-11-09: la sección siguiente
modifica la presentación de abonos y agrega anticipos/saldo neto y XLSX.
Su implementación local es incremental; el estado y la evidencia se distinguen
del objetivo completo al final de esta sección.

## Cierre de presentación y saldos del 2026-11-09

Este cierre y su actualización de presentación del 14-11-2026 prevalecen sobre
las descripciones anteriores de la salida del informe.
El propietario de recibos/anticipos es la sección 14 de
[comprobantes abiertos](implementation/accounting-open-vouchers-design.md);
el informe sólo consulta sus fuentes y aplicaciones canónicas.

- Un único detallado tabular: fila por documento, emisor/tercero y sede,
  identificación/razón social, información comercial de contacto y cupo, fechas,
  plazo/mora, original, abonado/pagado, notas/ajustes y saldo. Cabeceras compactas,
  subtotales por tercero/sede/moneda y total general, como el PDF de referencia.
- Casilla Mostrar detalle de abonos, inicialmente desmarcada, despliega número,
  fecha e importe bajo su factura. No crea otro reporte ni recalcula saldos.
  Consolidado continúa siendo el otro modo del mismo visor.
- CxC incorpora anticipos disponibles y saldo neto comercial: deuda menos
  anticipo. Incluye clientes con anticipo y ninguna factura pendiente. La
  consulta agrupa obligaciones y créditos por conjunto antes de paginar; un
  crédito no se repite por cada factura. Se conserva origen/sede del anticipo.
  Un histórico sin sede demostrable se identifica como tal, no se asigna por
  inferencia a una sede actual.
- Otros créditos disponibles se identifican aparte de anticipos y reducen el
  neto sólo por su disponibilidad no aplicada. Notas/devoluciones ya aplicadas
  están en el saldo de la obligación y no se descuentan dos veces.
- Edades actuales permanecen: al día, 1–30, 31–60, 61–90, más de 90. Suman deuda
  pendiente positiva; anticipos se muestran por separado y no amortizan las
  edades hasta su aplicación explícita. Neto comercial no compensa mayor/estados.
- PDF imprimible y XLSX real, no CSV renombrado, comparten filtros/corte y
  cálculo servidor. XLSX conserva importes numéricos, fechas y referencias;
  el detalle de aplicaciones es opcional como en el visor. No se exporta sólo
  la página ni se trunca. Límite 5.000 filas exportadas; al exceder se acota.
  Las consultas operativas siguen paginadas de 50; CxP separa monedas.
- Fecha efectiva editable del recibo no sustituye su fecha de registro en el
  corte histórico. Las aplicaciones/anticipos del corte cumplen ambas fechas.
  Se conservan orden, filtros de la grilla y totales de todo el conjunto.
- Template nueva versionada para esta salida; reimpresión de documentos
  históricos conserva su versión. Logo pertenece sólo a representación.
- Aceptación adicional: 5M deuda/10M anticipo muestra -5M neto y edades 5M;
  sólo anticipo visible; aplicar 5M deja deuda cero/anticipo 5M; mismo neto antes
  y después del cruce; corte anterior/posterior a registro; sin duplicar crédito
  por factura; equivalencia XLSX/PDF/visor; monedas y permisos; primera y
  siguiente página p95 <1 s con lecturas por conjunto constantes. Estas son
  pruebas a implementar, no una medición declarada.

### Ampliación del informe de clientes del 2026-11-11

#### Presentación aprobada del 2026-11-14

El detallado actual de clientes (plantilla 4) presenta un bloque por tercero:
nombre e identificación destacados, información de contacto disponible y tablas
de sus facturas por sede/moneda, con encabezados de columnas repetidos. En cada
factura hay una sola columna «Edad de cartera» (Sin vencer, 1–30, 31–60, 61–90,
más de 90), además de edad y mora numéricas. Los cinco tramos conservan sus
importes en el resumen del cliente/sede. Los colores pastel menta, amarillo,
durazno, rosa y lavanda acompañan los textos; nunca sustituyen la clasificación
del propietario ni cambian sus límites. Pantalla, PDF e impresión y Excel
conservan el mismo alcance. Los resúmenes, edades y facturas muestran la misma
precisión de hasta cuatro decimales, sin redondear los totales a pesos enteros.
Los anticipos y otros créditos aparecen una sola
vez en el resumen, sin presentarse como facturas ficticias; un tercero con sólo
crédito conserva su bloque visible.

El número principal es el prefijo/consecutivo fiscal registrado en FiscalDocuments
para la misma venta y sede; sin documento fiscal se conserva el número interno.
Una obligación originada por comprobante manual presenta su AccountingEntries.EntryNumber.
La referencia original se muestra aparte cuando difiere; el CUFE no es el número
de factura. Los filtros exactos y la búsqueda admiten tanto el número principal
como la referencia original antes de paginar y totalizar. Las plantillas 2/3
conservan su identidad publicada. No se reescriben documentos ni movimientos.

La agrupación visual sólo recorre la página ya seleccionada o la exportación
explícita acotada. No suma filas para reconstruir subtotales ni trae toda la
cartera al abrir el visor. Los subtotales siguen siendo los del conjunto filtrado
devueltos por Receivables; los enlaces de identidad son de cardinalidad uno
respaldada por las claves de FiscalDocuments y AccountingEntries. Se mantienen
dos comandos SQL, el límite de exportación de 5.000 y el presupuesto p95 <1 s
con 10.000 posiciones. Es un cambio de lectura/presentación: rollback retira
estos consumidores, sin migración ni reparación financiera.

Validación local del incremento: compilación Release sin errores ni advertencias,
build de Admin y lint dirigido; 14 pruebas de exportación/visor; los dos escenarios
de navegador CxC/CxP, incluidos filtros, exportación completa, dos clientes,
encabezados repetidos, colores y sólo anticipo; 42 regresiones de cartera,
calendario y cortes, más la repetición satisfactoria del caso de venta/pago que
comprueba identidad fiscal, referencia interna, ambos filtros y plantilla anterior.
Se inspeccionaron el PDF generado y sus encabezados de continuación. Estas
comprobaciones no sustituyen el gate completo ni la validación posterior en DEV.

#### Cierre de créditos disponibles — implementación 2026-11-13

La plantilla 4 incorpora una fila de crédito por tercero/sede/moneda en el
detallado, y sus importes en el consolidado. Las plantillas 2/3 conservan sus
filas publicadas. El admin solicita explícitamente versión 4 en visor,
impresión y exportación. Anticipos, otros créditos y neto son campos del mismo
contrato, calculados por el propietario; no son otro saldo persistido.
El crédito procede de CustomerCreditTransactions, reutilizando la proyección
firmada PortfolioTransactionEffect también para Application. Exige apertura
histórica conciliable y moneda soportada; no usa AvailableAmount actual para
inventar un corte anterior. La ausencia de evidencia impide emitir totales
incompletos y no desencadena reparación. Una apertura con fecha efectiva
posterior al corte queda fuera. Sin sede demostrable se muestra «Sin sede
identificada»; no se asigna la sede principal actual.

La identidad del grupo es tercero/sede/moneda. La plantilla 4 proyecta el rol
de cliente actual mediante la clave única Customers(TenantId,PartyId), incluso
si el anticipo nació antes de crear ese rol. Así el filtro de cliente incluye
esos importes y no duplica grupos por rol histórico nullable. Es dimensión de
consulta; no se rellena CustomerId de movimientos/documentos anteriores ni se
cambia su sede. Las plantillas 2/3 conservan su asociación publicada. La regresión
combina un anticipo sin rol y otro posterior al rol, filtra y consolida ambos y
verifica que la primera posición permanece sin CustomerId en la base.

Los filtros propios de factura (número, emisión, edad, mora, estado y sólo
vencidas) limitan los créditos a los terceros/monedas de las facturas
seleccionadas. CxC permite cruzar entre establecimientos del mismo tercero,
como su processor canónico; la dirección capturada se conserva en su propio
grupo visible. El filtro explícito de dirección sí limita las posiciones a
esa dirección. No se distribuye ni aplica crédito. Sin esos filtros aparecen también terceros
con sólo crédito; «Sólo con saldo» incluye ese disponible. Registro y búsqueda
se aplican antes de agregar créditos. El neto del grupo es de los documentos
filtrados y todo su crédito disponible compatible, mostrado expresamente.

Presupuesto: dos comandos por reporte (zona y consulta por conjunto), páginas
de 50, exportación máxima de 5.000 posiciones/aplicaciones y p95 inferior a un
segundo en primera/siguiente página con 10.000 posiciones y créditos. Las
dimensiones y aplicaciones se enriquecen después de elegir la página; los
agregados de todo el conjunto se obtienen antes, sin descargar detalles para
paginar. Aceptación: sólo anticipo, deuda/anticipo, cruce y neto constante,
créditos no repetidos, corte de registro/fecha efectiva, alcance, salida
completa PDF/XLSX y compatibilidad 2/3. La regresión funcional
`portfolio-payroll-closure-functional` aprobó 38/38 sin omisiones, incluyendo
los dos escenarios nuevos de anticipos y el cruce concurrente, los contratos
anteriores de cartera y las cancelaciones históricas. La verificación de
volumen y navegador continúa antes del cierre de este incremento.
Rollback: volver el consumidor a plantilla 3; las columnas nuevas del contrato
son aditivas. No se modifica historia ni se escriben efectos financieros.

La consulta usa una transacción de lectura Snapshot dentro del mismo comando,
para no mezclar deuda anterior con crédito posterior si un cruce se contabiliza
entre sus agregados. El DACPAC habilita AllowSnapshotIsolation; no cambia el
aislamiento predeterminado de los demás consumidores ni agrega viajes SQL.
La lectura termina con commit/rollback y restablece ReadCommitted. La prueba
concurrente detiene el reporte después de leer deuda, contabiliza el cruce y
reanuda la lectura de créditos: debe conservar ambos importes anteriores;
una nueva consulta muestra ambos posteriores con el mismo neto. Esto no copia
configuración contable ni crea otra fuente de saldos. El esquema con la opción
habilitada debe preceder al lector; al retirar el lector puede conservarse
esa capacidad aditiva, sin reescribir documentos ni deshabilitarla con lecturas
Snapshot activas. La medición incluye toda la transacción de lectura.

La primera ejecución Snapshot aprobó 28/37 y falló nueve consultas por el
error SQL 3964: CREATE INDEX/ALTER TABLE no se permiten dentro de esa
transacción, incluso sobre temporales. Una reproducción mínima en una base
desechable confirmó SELECT INTO permitido y CREATE INDEX rechazado; esa base
se retiró. La consulta corregida prepara esquemas e índices vacíos antes de
iniciar Snapshot y obtiene el neto como proyección, sin ALTER TABLE. Conserva
la misma transacción para todos los datos, dos viajes y la precisión de cuatro
decimales del neto usado al ordenar. La regresión se repite antes de certificar.

La tanda funcional posterior aprobó 38/38, sin omisiones. La tanda de volumen
`portfolio-credit-report-budget` falló ambos escenarios: el selector CxC
registró p95 1.838,1 ms frente a 1.500 y CxP agotó el tiempo de espera.
La consulta de OpenPositions materializa ahora disponibilidad y descuento
canónicos sobre la página antes de unir sus resultados, dentro del mismo
comando, sin duplicar reglas ni agregar viajes. La segunda tanda dejó el
selector dentro del presupuesto: CxC 1.346,0/1.419,3 ms y CxP
910,3/1.125,7 ms en páginas primera/segunda, con un SQL. Esa tanda también
falló por informes/listados anteriores: CxC report-after-2 p95 2.506,0 ms y
CxP invoices-1 p95 1.239,9 ms frente a 1.000. No se elevan los límites.

La cobertura de créditos tiene ahora un caso independiente que reutiliza la
misma preparación de 10.000 documentos, pagos y desaplicaciones, más 10.000
créditos; los casos anteriores conservan todos sus asserts y muestras. Su
primera ejecución aislada se interrumpió durante el aprovisionamiento: SQL
no completaba ni un login de diagnóstico. No produjo mediciones certificables.
Se cerraron sólo sus procesos propios; los resultados fallidos y la interrupción
se conservan. La comprobación de volumen sigue pendiente.

El cierre funcional posterior se ejecutó en `.\LOCAL`, cuya collation de
tempdb difiere de la base de aplicación. Reprodujo el conflicto
SQL_Latin1_General_CP1_CI_AS/Modern_Spanish_CI_AS. Las columnas de texto de las
temporales explícitas usan ahora COLLATE DATABASE_DEFAULT; no se cambia el
servidor ni la collation de datos del cliente. La primera repetición dejó
pasar el cruce concurrente/entre establecimientos y falló el caso de rol nuevo
por una preparación incompleta de su sede principal. Se completó esa identidad
de prueba, conservando sin sede el anticipo histórico. La regresión final
`portfolio-collation-regression` aprobó 37/37, sin omisiones: incluye los dos
casos nuevos, contratos anteriores y cancelaciones históricas.

La interfaz del incremento aprobó 16/16 en
`portfolio-payroll-browser`, incluidos vacío/error/reintento, filtros/páginas,
exportación completa, saldo sólo a favor y neto numérico negativo. Los PDF
se renderizaron para revisar primera/última página. La suite de exportación
aprobó 5/5; lint dirigido y build Next completo pasaron. La revisión visual
posterior amplía el campo de corte y llama registros a las filas que pueden
representar créditos agregados; `administrative-scope-report-payroll` aprobó
16/16 y la imagen del caso de sólo anticipo conserva la fecha completa y un
registro, con cero documentos y neto comercial negativo. La tanda base compartida aprobó 1.218/1.218 y la regresión POS
283/283 entre pretest y suite principal.

El caso de volumen aislado `portfolio-credit-local-budget` volvió a fallar por
timeout de la consulta, con 10.000 documentos y 10.000 créditos. No se certifica
rendimiento ni se amplía el límite. La prueba captura planes/counters sólo
después de un timeout y de cerrar la instrumentación medida; ese diagnóstico
no agrega SQL al camino exitoso. Se conserva esta condición abierta hasta
identificar y corregir su causa.

La instrucción del usuario permite cambiar los filtros dentro del visor de CxC.
Prevalece sobre la instantánea fija descrita más abajo. Al abrir conserva los
filtros operativos; «Ver todos los clientes» los limpia sin depender de la página
de la grilla. El selector canónico de Parties, con autorización de cartera,
elige un cliente con todas sus sedes. Cambiarlo retira la sede heredada.

El formulario aplica conjuntamente cliente, factura exacta, búsqueda general,
emisión desde/hasta, edad mínima/máxima, mora mínima/máxima, sólo vencidas y sólo
con saldo. Capturar filtros no consulta el informe; Aplicar produce una sola
consulta y vuelve a la primera página. Los filtros de estado/registro/sede que
se heredaron siguen visibles en el alcance y se pueden retirar con la limpieza.
El rango de emisión usa días locales del negocio. From/To continúan siendo
fechas de registro: con `dateRangeVersion=2`, aprobado el 12-11-2026, usan días
de sede en directorio, facturas, historial, informe e impresión/exportación.
El admin solicita esa versión; omisión/versión 1 conserva límites UTC para
clientes anteriores. No hay reinterpretación silenciosa ni cambios de instantes.
El corte inicial del visor se propone en la zona horaria de la sede usando
`proposedAccountingDate`, sin tomar el día del navegador. Si ese contexto no
está disponible, se exige elegir el corte en lugar de inventar una zona.
La fecha de emisión se obtiene del documento inmutable mediante
`accounting.PortfolioDocumentIssueDate`, compartida por listados administrativos
y reportes CxC/CxP. Una importación conserva su IssuedAt aunque la aceptación
contable sea posterior. Los contratos anteriores sin fecha explícita conservan
la fecha del hecho fuente. Esa proyección no modifica las fechas ni los filtros
de registro, ni la condición de existencia del movimiento al corte.
El esquema con esa función se publica antes de los lectores nuevos. Retirar
los lectores no requiere borrar la función ni modificar fuentes, saldos o
fechas históricas; no hay backfill de documentos para esta corrección.

Plazo = vencimiento menos emisión; edad = corte menos emisión; mora = corte
menos vencimiento, siempre por días del calendario del negocio. Mora negativa
indica días por vencer, igual que en la referencia; no representa intereses ni
un saldo vencido. La clasificación de importes conserva los cinco tramos
existentes y sólo clasifica deuda positiva. Un documento saldado puede conservar
su diferencia de fechas sin tener saldo vencido. PortfolioAging es propietario
de la descripción temporal; SQL traduce los rangos inclusivos a límites de
instantes antes de agregar, ordenar o paginar.

Cada factura muestra emisión, vencimiento, plazo, edad, mora, original, abonado,
notas/ajustes, saldo y tramo. El bloque de cliente/sede muestra razón social,
dirección, ciudad, teléfono, correo y cupo vigentes cuando existen; no inventa
datos de contacto ni los presenta como históricos al corte. Sus subtotales y
edades proceden del conjunto filtrado completo, incluso si continúa en otra
página. El mismo contrato alimenta pantalla, impresión, PDF y XLSX; aplicaciones
siguen siendo opcionales. No se escribe cartera ni se modifican pagos.

La salida ampliada de CxC usa portfolio-v3; templateVersion=2 conserva el diseño
publicado previo en el mismo renderer. CxP mantiene portfolio-v2. No se crea
otro catálogo, motor, writer ni servicio de reportes. Se mantiene el presupuesto
de dos comandos SQL por consulta y p95 inferior a 1.000 ms para primera y
siguiente página con 10.000 posiciones, con/sin aplicaciones. La aceptación
incluye filtros combinados antes de paginar, varios clientes sin selección,
fechas cercanas a medianoche, mora negativa/cero/positiva, subtotales completos,
exportación íntegra y compatibilidad CxP/v2. La evidencia se registra después
de ejecutar estas comprobaciones; este párrafo define el criterio.

### Verificación de la ampliación de CxC (2026-11-11)

Se corrigió la emisión de cartera importada: la fecha del documento inmutable
ya no se confunde con su aceptación. Listado y reporte comparten esa proyección.
Los filtros de emisión/edad se evalúan sobre la fecha materializada en SQL,
antes de agregar u ordenar; no se repite la interpretación del JSON para cada
predicado. Sólo se modifican temporales de consulta, no fuentes ni movimientos.

`design-closure-report-migration-reviewed.trx` aprobó 28 escenarios seleccionados:
26 funcionales de configuración/migración y reportes, y dos benchmarks CxC/CxP.
Incluye instalación nueva, cuenta usada, migración y comandos repetidos, compra
retrodatada única/balanceada, filtros combinados, varios clientes, plazos y mora
negativa/cero/positiva, subtotales completos entre páginas, corte histórico y
plantilla anterior v2. Esa tanda no certificaba el filtro de registro
contradictorio identificado en 14.10 del diseño de comprobantes; el incremento
del 12-11-2026 implementa la transición aprobada y aporta evidencia separada.

Con 10.000 posiciones/pagos/desaplicaciones y veinte muestras por camino, todos
los informes quedaron por debajo de 1.000 ms, con dos comandos SQL. El p95
máximo de CxC fue 749,2 ms (segunda página con filtros); CxP, 682,9 ms. Listado
de facturas conserva tres comandos; historial uno. La primera tanda había
excedido el límite del informe filtrado (1.119,7 ms): no se amplió el umbral ni
se redujeron datos/muestras. La tanda final usa la fecha materializada y corre
en `.\TEST`, sin builds, navegador ni consultas diagnósticas concurrentes.

La compilación Release de esquema y backend pasó sin errores/advertencias;
frontend compila y el lint enfocado pasó sin errores. Siete pruebas de fechas
y proyección de exportación pasaron, y la suite POS aprobó 16 de preparación y
267 principales. Foundation aprobó 1.198/1.198, sin omitidas, en
`design-closure-foundation-regression.trx`, incluida la regresión de dominio y
arquitectura. `design-closure-browser-regression.log` registra 36 recorridos
aprobados: dos de reportes, tres de formas, 24 de pago/impresión POS, dos de días
de nómina y cinco de recuperación de familia. El caso del reporte comprueba
filtros sin consultas por pulsación, paginación, rango inválido, vacío,
error/reintento, corte de sede con navegador en Kiritimati, descarga completa
de 51 documentos y celdas/fechas nativas del XLSX. Se revisaron la pantalla
final y las cinco páginas del PDF de CxC, además del PDF de CxP. La fila del
PDF detallado nuevo se mantiene íntegra al cambiar de página; no se modifica
la política de los otros exports. La razón social conserva todo su texto
debajo del logo, sin encajonarla en una columna que ocupe media pantalla.

Auditoría posterior: SqlReceivablesStore/SqlPayablesStore siguen siendo los
lectores propietarios; PortfolioDocumentIssueDate y PortfolioAging separan
emisión, plazo, edad y mora sin duplicar saldos. Se verificaron ámbito de
tenant/sede, filtros antes de agregados/paginación, contactos por claves únicas,
aplicaciones de la página por lote, exportación acotada y ausencia de I/O por
documento. Visor, marca y respuesta tienen sus propietarios existentes, sin
polling, GET por fila ni refetch por entrada de filtros. No cambia el writer,
el proceso financiero, la autorización o la idempotencia. Esquema antes que
API; datos/adiciones se conservan al retirar los lectores. Se preservó el
checkout previo y se retiró únicamente el servidor temporal de pruebas.
No se desplegó a DEV ni producción. Continúan pendientes anticipos/créditos,
saldo neto y el cierre global del diseño; el contrato de registro se unifica en el incremento de zonas horarias.

### Verificación del calendario por sede (2026-11-12)

La decisión del usuario configura cada sede y adopta su calendario operativo.
Se reutiliza `Businesses.TimeZone`; administración, aprovisionamiento y contexto
web comparten la validación/normalización existente ampliada, sin otro catálogo,
reloj, motor o consulta por formulario. Se rechazan zonas inválidas antes de
modificar datos. El contexto cargado y su copia offline se actualizan con la
respuesta del guardado, sin refetch de negocios.

El admin solicita `dateRangeVersion=2` para directorios, facturas, historial,
informe, impresión y exportación CxC/CxP. Omisión/versión 1 mantiene límites UTC;
una versión distinta se rechaza. Los filtros de posiciones conservan CreatedAt
y el historial PaidAt; sólo cambian sus límites inclusivo/exclusivo. No se
reescriben documentos, pagos, cuentas ni instantes, y los DateOnly mantienen
su significado. Cambiar la zona modifica la interpretación local de instantes
previos; no es una reclasificación ni reparación financiera.

Evidencia final local, sin skips:

- `business-time-zone.trx`: 56 pruebas de edición, aprovisionamiento, rechazo,
  normalización IANA/Windows y regresión de configuración.
- `portfolio-local-dates-group-reviewed.trx`: 23 escenarios SQL/API, incluidos
  los benchmarks CxC/CxP de 10.000 documentos/pagos/desaplicaciones. Verifica
  medianoche en Colombia/Tokio, días DST de 23/25 horas en Nueva York, comienzo
  inclusivo/final exclusivo, versiones anterior/nueva, impresión/exportación,
  autorización, consolidados, subtotales y paginación. También pasa la regresión
  de directorio/facturas que había detectado la contradicción de registro.
- `time-zone-browser.log`: 10 pruebas de sedes, selección administrativa y visor,
  incluida la actualización de zona sin GET adicional y uso de versión 2.
- `time-zone-frontend-unit.log`: 7 pruebas de fechas y exportación; lint y tipos
  sin errores. Build completo de admin y build backend sin errores; los archivos
  de configuración que Next ajusta durante build se restauraron byte a byte.

La primera tanda conservada en `portfolio-local-dates-reviewed.trx` aprobó
18 funcionales y falló dos presupuestos: informe CxC de 1.057,4 ms frente a
1.000 ms y primera apertura del selector de proveedor de 3.081,2 ms frente a
2.000 ms. Se liberó memoria local cerrando el Edge de inicio sin ventanas y
terminando procesos propios de compilación/pruebas; no se cambiaron los
parámetros de SQL. El selector pasó al repetir, pero CxC aún marcó 1.096,6 ms
(`portfolio-local-dates-print-performance.trx`). Se corrigió el lector existente:
los subtotales se agregan una vez por tercero/sede/moneda y se unen después de
paginar, retirando ventanas sobre cada fila ancha del informe. No hay otra
aritmética ni tabla persistida. Se conserva la captura filtrada, la página y
los hechos/aplicaciones de sus lectores propietarios.

La tanda final conserva los umbrales, 10.000 documentos y 20 muestras. P95
máximo de informes: CxC 609,9 ms, CxP 592,9 ms, con 2 comandos SQL. Facturas:
3 comandos; historial sin fechas locales 1, con fechas locales 2, máximos
67,4/40,0 ms para CxC/CxP filtrados. Selector: 1 comando, primera apertura
máxima 1.012,5 ms y p95 máximo 441,0 ms. No hubo builds, navegador o consultas
diagnósticas durante esas mediciones.

Auditoría posterior del incremento: se revisaron propietarios y diff de
configuración, API, contratos, validación, lectores, admin y pruebas. Se conserva
scope tenant/sede, autorización y una sola escritura por configuración. No
cambia aceptación, idempotencia, concurrencia ni efectos de inventario/contabilidad.
No hay cola, writer, tabla de negocio, motor alterno, N+1, fetch-all de detalles,
GET por fila, refetch redundante o polling nuevo. Se reutilizan el provider y
query existentes para contexto, y las consultas paginadas aplican filtros antes
de agregados/página y enriquecen sólo la página. Las agregaciones temporales
son derivadas de ese conjunto, no otro saldo. Rollback del contrato: el cliente
puede usar versión 1 sin migrar datos; la zona puede editarse sin reescritura
histórica. Se preservaron los cambios previos del checkout y se cerró el servidor
temporal de UI. No se desplegó a DEV ni producción.

Este incremento cierra configuración y rangos de cartera. La unificación de
fechas de los demás módulos y los otros pendientes funcionales siguen en el
inventario 14.10 del diseño propietario; no están certificados por esta tanda.

### Estado local del detallado y la exportación

El visor y la plantilla imprimible `portfolio-v2` ya usan tabla compacta con
documentos, fechas, original, pagado, ajustes, saldo y edad. La casilla de detalle
inicia desmarcada. `IncludeApplications` es aditivo y conserva `true` por defecto
para consumidores anteriores; el visor envía su elección explícita. El store
omite las aplicaciones cuando no se solicitaron y no cambia el cálculo de pagos,
saldos, filtros, totales ni los cinco tramos. Las fechas proyectadas conservan
el calendario de la zona del negocio en pantalla, impresión y archivo.

La descarga completa usa `/report/export` y el mismo `PrintReportAsync` del
propietario CxC/CxP. PDF y XLSX reutilizan `report-export.ts`, con una proyección
de los datos del servidor; no calculan otro saldo ni exportan sólo la página.
Los subtotales por moneda proceden del servidor y las filas de aplicaciones no
repiten importes de su factura. `PortfolioReportLimits` es la única política del
máximo de 5.000 filas de documento más aplicación. La consulta completa obtiene
a lo sumo 5.001 documentos y 5.001 aplicaciones y el caso de uso rechaza el
exceso; nunca entrega una historia truncada. Encabezados y totales de presentación
no son aplicaciones adicionales. La página operativa mantiene su límite de 50
y las lecturas de hechos siguen siendo por conjunto, sin otro GET por fila.

La impresión resuelve la marca mediante `ITenantService` después de consultar y
autorizar el informe. El visor comparte la copia del propietario de marca de
impresión para la representación y la descarga PDF, sin lectura de logo por
documento ni cambios al procesamiento operacional.

Las seis regresiones de backend de CxC/CxP, el límite de filas y la proyección
de exportación pasaron localmente. Los dos escenarios de navegador verificaron
la descarga completa de 51 documentos, los importes numéricos y fechas nativas
del XLSX, el PDF y la reutilización de la marca. La inspección de las páginas
renderizadas del PDF comprobó encabezados, alineación y ausencia de recortes.
La revisión del corte histórico corrigió además la clasificación de CxP: una
cancelación posterior no convierte un documento ya saldado en cancelado en
un corte anterior. La evidencia fechada se consulta por conjunto, separada de
los agregados de movimientos.

La medición con 10.000 posiciones conserva el contrato de dos viajes SQL por
página del informe, con y sin aplicaciones. Los hechos de aplicaciones de la
página se seleccionan antes de enriquecer sus comprobantes y la exportación
conserva su límite. La medición local limpia del 2026-11-10 aprobó ambos lados:
10.000 posiciones/pagos/desaplicaciones, veinte muestras por camino y sin builds
ni diagnóstico concurrentes. El p95 máximo fue 299,2 ms en historial (un viaje
SQL), 486,4 ms en listado de facturas (tres) y 558,9 ms en reporte (dos), con
primeras/siguientes páginas, filtros y detalles activados/desactivados. Se
mantienen los presupuestos originales de 800 ms para historial y 1.000 ms para
listados/informes; no se redujeron volumen ni muestras. La evidencia reproducible
es `PortfolioPaymentHistoryPerformanceTests`, ejecutada con `dotnet vstest` sobre
el ensamblado compilado; esto certifica ese escenario local, no producción.

La revisión del 2026-11-14 conserva expresamente los dos viajes del informe:
el primero valida la sede y obtiene su calendario; el segundo mantiene el
corte consistente de datos y totales. La forma de la consulta se prepara con
`SqlQueryShape`, extraído del lector de ventas existente y compartido con el
historial y el selector administrativo. Sólo pliega presencia de filtros y
Booleanos; fechas, identidades, importes y textos permanecen parametrizados.
No interpreta texto entre comillas, identificadores ni comentarios. El selector
usa `PostingAccountEligibility` en su vista previa; los bloqueos de primera
utilización de `PostingAccountReadiness` corresponden exclusivamente al posting.
Estas optimizaciones no agregan viajes, cachés de saldos ni escritores. La
medición acumulada aprobó ambos historiales de 10.000 pagos y la primera carga
del selector. Con 10.000 posiciones y 10.000 créditos, el informe aprobó veinte
muestras por camino: p95 890,5/804,2 ms sin aplicaciones y 870,2/839,2 ms con
aplicaciones, en primeras/segundas páginas y dos SQL. Conserva el límite de
1.000 ms; la evidencia es la regresión local del 14-11-2026. No certifica otros
volúmenes ni producción.
La validación de la apertura de cada crédito y su disponible se agrupan juntos
por tercero/sede/moneda, usando los movimientos al corte y el predicado canónico
de efectos. No se copia otra tabla temporal con todo el historial ni se vuelve
a unir para cada total. Los créditos sin prueba siguen bloqueando el informe;
los grupos sin disponible no generan una fila artificial.
Aún faltan en el modelo del
informe los anticipos/créditos disponibles, clientes con sólo saldo a favor y
neto comercial. La ampliación de contacto/cupo quedó probada arriba. No se
declara implementada esa parte ni se amplía la evidencia fiscal o de producción.

La ronda diagnóstica `portfolio-credit-budget-plans` no llegó al informe:
agotó 120 segundos en el seed sintético de volumen y después falló la limpieza
del fixture. No produjo planes del informe y no prueba su causa de latencia.
La consulta de estado posterior informó memoria física baja (563.640 KB
disponibles), sin solicitudes de usuario activas. No se aumentó el límite de
latencia ni se modificó la configuración de SQL. El presupuesto sigue abierto.

La revisión posterior de volumen conservó los límites y encontró estimaciones
muy inferiores a las filas reales: el join de identidad mediante COALESCE
producía candidatos cartesianos y repetía rol/sede por documento. El lector
materializa ahora candidatos de la sede, acotados también por cliente/dirección
antes del enriquecimiento, y obtiene una sola dimensión por tercero/dirección.
Las proyecciones financieras siguen usando las funciones canónicas y Snapshot;
no se agrega otro saldo, writer, proceso ni viaje. Los temporales de carga única
son heaps y sus anchos siguen los maestros (nombre 200, identificación 64,
dirección 160), sin truncar datos admitidos. Se mantiene el índice de grupos y
el de las aplicaciones de página. Las primeras repeticiones todavía excedieron
el p95; la prueba conserva veinte muestras, 10.000 facturas y 10.000 créditos.
La compilación es correcta y ambos escenarios funcionales de créditos/cruce
aprobaron en portfolio-calendar-functional; esa tanda se abortó tras un timeout
de otro comprobante de 498 conceptos y no certifica rendimiento ni todo el slice.
El diagnóstico de fallos reejecuta sólo la consulta de lectura en una conexión
no reutilizable, después de las mediciones; guarda el perfil real/estadísticas
sin depender de que el plan permanezca en caché y no agrega SQL en casos verdes.
El intento de recompilar los filtros en cada lectura empeoró el tiempo y se
retiró. El gate de volumen permanece abierto hasta la repetición definitiva.

## Cierre de sesión

- La sesión operativa canónica abre o se retoma cuando el usuario inicia una operación que maneja dinero. Un abono o pago a proveedor puede ser la primera operación; no depende de una venta previa. La vista de cierres no abre sesiones al consultarse.
- En el historial, la fecha de **cierre** es la principal y la de **inicio** aparece debajo. El filtro de conciliación inicia en `Pending` y permite `Partial`, `Reconciled`, `ReconciledWithDifferences` y todos. La API existente aplica el filtro y la paginación en SQL.
- Los importes y campos de conteo usan formato colombiano con separación de miles; el dato enviado sigue siendo decimal sin separadores. La tirilla vigente ya usa `es-CO` y no se cambia su versión histórica.

## Faltante que puede convertirse en cuenta por cobrar

El cierre ya conserva la diferencia y crea una fuente contable `WorkSessionCashDifference`. Al cerrar se mueve a la categoría transitoria `CashClosureDifferencesPending` (cuenta predeterminada 13809505); al conciliar, el faltante residual pasa a `CashShortageExpense` (cuenta predeterminada 53959501), salvo que se registre a cargo del empleado. La opción se ofrece **al confirmar la conciliación** de un faltante de efectivo, sin alterar el arqueo ni crear una venta. El sobrante solo admite una observación y no genera cuenta por cobrar.

1. El usuario con permiso para conciliar marca **Registrar cuenta por cobrar al empleado** si desea imputar el faltante de efectivo. El deudor es el empleado activo vinculado al usuario de la sesión mediante el mismo `PartyId`; una cuenta de acceso sin ese vínculo no basta. El importe es el faltante residual confirmado. La observación de la conciliación documenta el motivo; no se exige adjuntar archivos ni una aprobación adicional en esta pantalla. La casilla registra una deuda de cartera, no un descuento automático de nómina.
2. Conforme a la decisión prevalente de motor financiero único del 20 de octubre, la aceptación de conciliación congela el reclamo, su identidad y su única fuente/trabajo contable. `SqlAccountingPostingProcessor` abre la obligación en `Receivables` junto con el asiento en su transacción; si la fuente se aceptó sin contabilidad activa, el mismo procesador aplica únicamente el efecto comercial. No se escribe cartera desde el store de cierres. Para un empleado, la obligación se identifica por `PartyId` tras comprobar al aceptar que tiene un registro `Employee` activo; no se crea otro motor ni se disfraza al trabajador como cliente. Los filtros de clientes y sus pagos existentes siguen usando `CustomerId`. Una obligación histórica ya creada por la aceptación anterior se conserva si coincide con el reclamo y su apertura, incluidos sus abonos posteriores; el procesador no vuelve a abrirla.
3. El motor contable existente contabiliza el faltante confirmado: **débito** a la categoría configurable `EmployeeShortagesReceivable` (subcuenta del grupo 1365, distinta de `EmployeeLoansReceivable`), **crédito** a `CashClosureDifferencesPending`. No se vuelve a mover caja. Cuando no se marca la casilla, el faltante se carga a `CashShortageExpense`. El pago o reversión posterior requiere una operación de cartera trazable; nunca se resuelve con una venta ficticia.
4. Unicidad por cierre/línea/deudor y clave idempotente, bloqueo de concurrencia por saldo reclamable, permiso de conciliación, auditoría y asiento balanceado son obligatorios. Una configuración PUC faltante detiene el asiento y señala la categoría faltante; no se asigna una cuenta por defecto silenciosa.

La [prohibición de descuentos salariales sin autorización individual o mandamiento judicial](https://apps.procuraduria.gov.co/gd_734/docs/c_sustra.html) impide tratar un faltante como deducción automática. La creación de una CxC exige evidencia de un derecho real de cobro; no se infiere del descuadre.

## Informes de cuentas por cobrar y por pagar

Cada módulo tendrá un único botón **Reportes** en su encabezado. Abre directamente el visor con **Detallado** y permite cambiar a **Consolidado** dentro de ese mismo visor. El visor adopta los filtros activos de la cartera, sin repetir el formulario; solo permite elegir la fecha de corte, que no existe en la grilla operativa. La impresión prepara el informe completo en la misma ventana, sin abrir otra pestaña. No se crea un tercer informe de movimientos: los abonos y pagos se consultan dentro del detallado.

| Informe | Cuentas por cobrar | Cuentas por pagar |
| --- | --- | --- |
| Detallado | Cliente y sede → factura → fecha, número e importe de cada abono aplicado; original, total abonado y saldo por factura y por cliente/sede. | Proveedor y sede → factura de compra o gasto → fecha, comprobante e importe de cada pago aplicado; original, total pagado y saldo por obligación y por proveedor/sede. |
| Consolidado | Una fila por cliente y sede: número de facturas, original, total abonado, saldo pendiente y vencido. | Una fila por proveedor y sede, separada además por moneda: número de obligaciones, original, total pagado, saldo pendiente y vencido. |

Ambos informes aplican los filtros activos del módulo: tercero y sede, búsqueda, fecha de registro desde/hasta, estado y solo vencidas; CxP también conserva el concepto. La fecha de emisión sigue visible como dato del documento. La fecha de corte se elige en el visor. El rango de registro usa la misma versión de calendario que la grilla operativa (admin: días de sede, dateRangeVersion=2; contrato anterior: UTC), para que una selección no cambie el conjunto al abrir el reporte. El corte se interpreta en la zona horaria configurada para la sede, incluida su medianoche final; determina qué facturas y aplicaciones existían a esa fecha. Un abono posterior no reduce retroactivamente el saldo. Sin tercero seleccionado se incluyen todos, no solo los que aparecen en la grilla de la página. El estado y el vencimiento se evalúan al corte y los filtros se aplican en servidor antes de paginar y totalizar. Los totales del encabezado representan todo el conjunto filtrado, no la página visible. El informe conserva una instantánea de los filtros al abrirse; el usuario los cambia en la cartera y vuelve a abrirlo si necesita otros. El filtro interno **solo con saldo** sigue disponible en el contrato del servidor para otros consumidores, pero no se añade un control redundante al visor.

El detallado pagina facturas u obligaciones y carga las aplicaciones de esa misma página por lote; la paginación nunca divide la historia de pagos de una factura. Agrupa visualmente cada página por cliente/proveedor y sede, con una sección de documentos y abonos/pagos aplicados por documento. El consolidado pagina clientes/proveedores con sede y moneda. Ambos permiten ordenar columnas en servidor e imprimir el reporte completo con una presentación coherente con el visor vigente. La consulta normal descarga solo la página visible. La impresión es una acción explícita: una consulta del servidor reutiliza los filtros, totales y ordenamiento del informe, obtiene hasta 5.001 filas en el mismo viaje y prepara una vista imprimible solo si hay como máximo 5.000; si supera ese límite solicita acotar los filtros, sin truncar datos. No se hace una lectura por tercero, factura o pago. `ReceivablesService` y `PayablesService` conservan la autoridad sobre importes, aplicaciones, permisos y filtros; Reporting compone la presentación sin recalcular saldos ni crear un ledger alterno. Objetivo de respuesta de página y totales: menos de un segundo en el volumen operativo medido, con índices y plan de ejecución verificados.

Las edades de cartera son una sección del mismo informe, no una pestaña, filtro ni reporte aparte. Usan el saldo pendiente al corte de cada obligación y la fecha de vencimiento en la zona horaria de la sede: **al día** (vence en el corte o después), **1–30**, **31–60**, **61–90** y **más de 90 días** de vencimiento. El servidor agrupa los importes filtrados completos antes de paginar, separa monedas y devuelve también el tramo por factura y por cliente/proveedor con sede. El detallado muestra el tramo junto al documento; el consolidado muestra los importes de cada tercero. Los cinco importes suman el saldo pendiente positivo y los cuatro vencidos suman el total vencido. En el encabezado, el nombre de la empresa queda debajo del logo y el título del informe a la derecha del logo.

Las filas de **Recaudos** y **Pagos** ofrecen **Ver e imprimir** a quienes tienen permiso de lectura contable. La acción abre el comprobante persistido del pago mediante el mismo visor de Trazabilidad financiera y permite imprimir su reporte; solo consulta el asiento de la fila elegida, sin regenerar el recaudo, el pago ni el comprobante.

El saldo al corte incluye únicamente movimientos cuya fecha efectiva y cuya fecha de aplicación/registro son anteriores al final del día de corte. Así un pago registrado después no altera retroactivamente el informe aunque su fecha de pago se haya escrito hacia atrás. **Notas y ajustes** muestra la diferencia entre original, pagos y saldo; hace visibles devoluciones, notas crédito y ajustes de cartera sin llamarlos pagos. La consulta del informe y la exportación deben usar el mismo cálculo autoritativo.

CxC opera en COP. CxP admite varias monedas y nunca suma USD y COP como un único importe: los subtotales y el consolidado se separan por `CurrencyCode`. La sede es parte de la agrupación; una obligación histórica asignada a la sede principal aparece allí conforme a la migración de cartera aprobada.

Cuando se imprime el informe con corte en el día actual, el servidor limita los movimientos al instante en que inicia la consulta para que las aplicaciones nuevas no aparezcan a mitad del documento.

## Cruce de CxP con CxC

Estado verificado el 2026-11-14: el normalizador actual admite aplicar un saldo
a favor a obligaciones de la misma dirección. No habilita todavía compensar
una CxC con una CxP; rechaza grupos que mezclan esas direcciones sin el saldo
a favor requerido. Capturar sólo sus dos partidas no sustituye la aplicación
de ambas obligaciones. El objetivo de esta sección permanece pendiente y no
forma parte de la certificación del cruce de anticipos.

Se agrega una pestaña **Cruce de cuentas** debajo de cuentas por cobrar. Se selecciona un proveedor y el servidor resuelve su `PartyId`; solo se muestran sus roles de proveedor y cliente realmente vinculados a ese mismo tercero. No se hace cruce por coincidencia de nombre o NIT. La primera versión exige mismo tenant, mismo negocio, COP, saldos abiertos, obligaciones ciertas y exigibles o acuerdo documentado, y permiso administrativo `portfolio.offsets.create` junto con lectura de ambas carteras.

El usuario elige facturas de ambos lados y montos; el máximo a cruzar es el menor de los dos saldos seleccionados. Antes de confirmar ve el comprobante de compensación y el saldo residual de cada factura. La captura y aceptación extienden el comprobante, sus vínculos tipados y las reservas existentes; no aplican los saldos. `SqlAccountingPostingProcessor` es el único propietario de aplicar ambas carteras y contabilizar el asiento, juntos en su transacción financiera. La partida económica es **débito al auxiliar original de CxP y crédito al auxiliar original de CxC** por el mismo importe; 220505 y 130505 son ejemplos, no cuentas obligatorias. No mueve efectivo, banco, ingreso, gasto ni IVA de las facturas originales. No se presenta como un pago en efectivo ni como una factura nueva a la DIAN. Un fallo antes del commit financiero deja ambos saldos intactos; un reintento de la misma clave devuelve el resultado autoritativo sin repetir efectos.

La revisión del 12-11-2026 corrige aquí la atribución anterior de escritura a cartera antes del asiento y la propuesta de otra fuente `PortfolioOffset`: contradecían las invariantes vigentes y la instrucción del usuario de reutilizar un único flujo. Rigen los propietarios de la sección 13 de [comprobantes abiertos](implementation/accounting-open-vouchers-design.md). Esta sección conserva el objetivo funcional del cruce entre CxP y CxC; no certifica que ese objetivo completo ya esté implementado ni crea un writer, tabla o motor nuevo.

La [compensación civil requiere obligaciones recíprocas, líquidas y exigibles](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=3644); para presentar activos y pasivos netos se requiere [derecho exigible e intención de liquidar por neto o simultáneamente](https://www.ifrs.org/issued-standards/list-of-standards/ias-32-financial-instruments-presentation/). El cruce liquida aplicaciones identificadas; no oculta saldos de otras facturas mediante una resta visual. Las [retenciones se revisan al pago o abono en cuenta, el que ocurra primero](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_900613_2022.htm); el cruce verifica las retenciones ya causadas y no las duplica.

## Aceptación

- Historial de cierres: filtro inicial pendiente, cuatro estados y todos, fecha de cierre arriba, paginación correcta, moneda `es-CO`.
- Faltante: sin deudor acreditado solo gasto; reclamo parcial y total, abonos, reversión, sin descuento de nómina; suma de gasto y CxC igual al faltante, asiento balanceado y sin segunda salida de caja.
- Informes: detallado y consolidado para CxC y CxP, filtro por tercero/sede independiente de la grilla, corte histórico, solo con saldo, totales correctos entre páginas, aplicaciones visibles, monedas separadas, cero consultas por fila e impresión fiel.
- Cruce: tercero con ambos roles, múltiples facturas, saldo parcial, concurrencia, reintento idempotente, fallo atómico, asiento balanceado, sin caja ni documento fiscal nuevo.
