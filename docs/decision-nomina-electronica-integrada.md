# Decisión: nómina electrónica integrada

Fecha: 2026-08-26
Estado: aprobada para implementación incremental

Actualización de diseño: 2026-11-09. La sección 13 cierra el flujo solicitado
con referencias Mantis y contraste fiscal/comercial. Describe el contrato
objetivo pendiente de implementación; no certifica el runtime existente.

## 1. Objetivo y alcance certificado

Auraly incorpora un módulo `Payroll` para liquidar nómina colombiana,
contabilizar sus efectos y emitir la consolidación de nómina electrónica DIAN. La primera entrega
es deliberadamente viable: cubre trabajadores dependientes del sector privado,
periodicidad mensual o quincenal, salario ordinario, novedades, deducciones
autorizadas, aportes y provisiones comunes, comprobante, contabilización
y consolidación electrónica mensual por trabajador.

Quedan fuera de esta certificación inicial el reloj biométrico, selección y
desempeño de RR. HH., nómina pública o de regímenes especiales, múltiples
países, dispersión bancaria directa y presentación de PILA. Esas exclusiones no
permiten omitir el snapshot necesario para conciliar posteriormente PILA,
contabilidad o una integración bancaria.

Esta decisión prevalece, para el slice de nómina aquí definido, sobre las
exclusiones históricas de nómina contenidas en documentos del MVP comercial.

## 2. Propiedad y motores

`Payroll` es propietario de contratos laborales, conceptos, acuerdos de
deducción, reglas laborales versionadas, novedades, liquidaciones, líneas,
pagos y períodos electrónicos. Implementa un calculador determinístico de
nómina; no crea una cola, poller, worker ni tabla de jobs para calcularla.

Una liquidación aprobada es inmutable y exclusivamente financiera:

1. guarda su snapshot y las fuentes contables en la misma transacción;
2. activa `AccountingProcessingCoordinator`;
3. `SqlAccountingPostingProcessor` es el único escritor de asientos y
   submayores;
4. un reintento contable nunca recalcula la nómina.

La nómina electrónica extiende `FiscalProcessingCoordinator`, sus procesos,
artefactos, intentos, firma y transportes. El módulo fiscal decide generación,
CUNE, envío y consulta DIAN mediante estrategias por familia documental. No se
crea un worker DIAN de nómina. Los errores fiscales no revierten pago,
contabilidad ni liquidación.

## 3. Identidad, tenant y establecimiento

- `Tenant` es el empleador o entidad legal.
- `Party` es la persona natural canónica.
- `payroll.Employments` representa la relación laboral y exige el mismo tenant
  de la persona.
- `Business` identifica el establecimiento o centro operativo de origen y el
  alcance de contabilización, sin convertirse en empleador independiente.
- `dbo.Employees` puede enlazarse para agenda/operación, pero no almacena salario
  ni reemplaza la relación laboral.
- `EmployeeWorkingHours` y `EmployeeScheduleExceptions` son disponibilidad de
  agenda; no son evidencia de tiempo trabajado ni alimentan la nómina.

Toda consulta y escritura valida `TenantId` y el `BusinessId` autorizado. Los
documentos históricos conservan snapshots aunque cambien persona, contrato,
concepto o regla.

## 4. Catálogos y estados

Todo valor que el usuario elige se obtiene de tabla/API. Los catálogos oficiales
globales usan IDs determinísticos y seed idempotente; los conceptos y acuerdos
del empleador son maestros administrables con vigencia y auditoría.

Catálogos mínimos:

- tipos de contrato y salario;
- periodicidades de pago;
- clases de riesgo;
- tipos de trabajador y subtipo DIAN;
- medios de pago de nómina;
- naturalezas, clases de cálculo y tratamientos de conceptos;
- tipos de liquidación (`payroll-run-kind`), validados al crear el borrador;
- etiquetas de estados (`payroll-workflow-status`) para la presentación en admin;
- tipos y autoridad de deducción;
- tipos de novedad y motivos operativos aplicables;
- categorías de mapeo contable y códigos DIAN.

Los enums se limitan a estados técnicos cerrados: `Draft`, `Calculated`,
`Approved`, `Voided`; estados de pago; y estados del proceso electrónico. La
base aplica `CHECK` a esos estados. Labels, orden y opciones nunca se reconstruyen
con arrays o `switch` en frontend o backend.

## 5. Reglas y cálculo

Las reglas legales son datos versionados por vigencia. Cada conjunto conserva
valores, tarifas, topes, redondeos y origen normativo; cada liquidación registra
el conjunto utilizado. No se consulta una tabla vigente al reproducir un
comprobante histórico.

Orden de cálculo de la primera entrega:

1. salario y días reconocidos;
2. devengados variables y no salariales;
3. bases de seguridad social, prestaciones, parafiscales y retención;
4. aportes del trabajador y empleador;
5. retención laboral y deducciones autorizadas;
6. provisiones y neto a pagar.

Las novedades ejecutan el método persistido del concepto: valor fijo,
cantidad por tarifa, porcentaje de una base autorizada o valor estatutario.
Las novedades de días con naturaleza de deducción reducen los días reconocidos;
las de horas conservan cantidad y tarifa en el snapshot. Ningún método se
reconstruye desde una lista del frontend.

El motor rechaza conceptos o reglas desconocidos, bases negativas, deducciones
sin autoridad vigente, acuerdos agotados y resultados inconsistentes. Las
deducciones deben señalar ley, autorización escrita u orden judicial y conservar
evidencia; los límites protectores se evalúan antes de aprobar. No existen
fallbacks silenciosos.

## 6. Flujo e inmutabilidad

`Draft → Calculated → Approved` es el flujo normal. Calcular reemplaza únicamente
el snapshot de un borrador; aprobar exige versión/rowversion, resultado vigente,
período abierto y autorización. La aprobación es idempotente por tenant,
liquidación y clave de operación.

Una liquidación aprobada no se edita ni elimina. Las correcciones crean una
liquidación `Adjustment` que referencia el original y conserva diferencias por
concepto. Un pago registra lote y líneas contra netos cuya causación esté
contabilizada; no cambia el cálculo. El contrato objetivo de la sección 13
sustituye el pago completo de toda la liquidación por grupos de transferencia
o efectivo, conserva unicidad por trabajador liquidado y determina el auxiliar
de salida mediante la forma de pago del tenant. La implementación anterior sólo
resuelve una categoría general (`Bank` o `Cash`); esa limitación no satisface
el contrato nuevo.

El conjunto de reglas debe cubrir todo el período de la liquidación. Al aprobar,
la misma transacción bloquea las novedades y acuerdos utilizados: rechaza una
novedad consumida por otra liquidación, una autorización desactivada o fuera de
vigencia y la suma de descuentos que exceda el saldo autorizado actual. Estos
conflictos conservan el borrador calculado sin fuentes contables ni consumos
parciales; el usuario corrige el acuerdo o recalcula antes de aprobar.

El cálculo conserva identidad, contrato, salario, códigos DIAN y datos de pago en
`RunEmployees.EmployeeSnapshotJson` con `SnapshotVersion=1`. La consolidación
electrónica toma esos datos históricos, incluidos los valores nulos, para que
editar el maestro después de aprobar no cambie la fuente fiscal.

Excepción de compatibilidad para liquidaciones anteriores sin ese snapshot:
se conserva la lectura histórica del maestro actual y se emite la advertencia
estructurada `PayrollLegacyEmployeeSnapshot`, identificando tenant, sede y
liquidación sin publicar datos personales. El motivo es que los valores
originales no se pueden reconstruir automáticamente y bloquear estos documentos
rompería el flujo existente. El riesgo es una diferencia entre datos laborales
actuales e históricos; se mitiga revisando esos períodos antes de transmitir y
recalculando los borradores. La excepción se retira cuando se hayan conciliado
los períodos anteriores; no aplica a nuevas liquidaciones calculadas.

Una liquidación `Adjustment` conserva período y periodicidad del documento
original y procesa únicamente novedades nuevas como diferencias. Por ello no
repite salario ni conceptos ya aprobados; una diferencia que aumenta el valor
se registra con naturaleza devengada y una que lo disminuye con naturaleza de
deducción, manteniendo líneas y asiento trazables.

## 7. Contabilidad

El posting agrupa por `BusinessId` y usa categorías de nómina mapeadas a cuentas
activas del plan del tenant: salarios, horas/recargos, auxilio, prestaciones,
aportes del empleador, ARL, parafiscales, aportes descontados al trabajador,
retención, deducciones a terceros, préstamos, provisiones y nómina por pagar.

La aprobación crea `AccountingSourceDocuments` y `AccountingPostingJobs` en la
misma transacción que la liquidación y un mensaje outbox durable para activar el
coordinador después del commit. La clave fuente impide asientos duplicados. Si
falta un mapeo, el job queda fallido y observable; la API no inserta asientos.

Tipos contables nuevos: `PayrollAccrual`, `PayrollPayment` y
`PayrollAdjustment`.

El perfil contable colombiano incluye de forma idempotente todas las categorías
laborales. La pantalla de configuración de Nómina cruza el catálogo
`payroll-accounting-category`, las definiciones del perfil y las cuentas activas
del tenant; no mantiene una lista paralela en frontend.

La implementación mínima de nómina, sus ajustes y pagos reutiliza la resolución
de centros del único
`SqlAccountingPostingProcessor`: regla general (`All`) sin bodega y, en su
ausencia, predeterminado activo de la sede. El primer intento congela
`AccountingPostingJobs.ResolvedCostCenterId`; las líneas del asiento conservan
el identificador, código y nombre del centro. Cambiar reglas o maestros no
reclasifica los asientos anteriores. El auxiliar contable existente permite
consultar estos movimientos por centro. El objetivo de la sección 13 amplía
esa resolución a empleado/línea sin agregar un segundo writer ni porcentajes
de reparto no solicitados.

## 8. Nómina electrónica

La frecuencia de pago no define la frecuencia DIAN. Para cada mes calendario se
consolidan las liquidaciones aprobadas en un documento por `Tenant + Party +
período`. El snapshot fiscal conserva devengados, deducciones, neto, fechas de
pago, contrato, trabajador y empleador tal como fueron reportados.

El corte implementado consolida snapshots mensuales inmutables, los protege con
SHA-256 y publica cada raíz fiscal mediante el coordinador durable. La familia
`ElectronicPayroll` del pipeline existente genera el XML individual, calcula
CUNE y `SoftwareSC` con SHA-384, valida contra el XSD oficial 1.0.6, firma con el
certificado del emisor y persiste XML, firma, ZIP, intentos y respuesta DIAN.
Habilitación usa `SendTestSetAsync` y seguimiento por `GetStatusZip`; producción
usa la operación específica `SendNominaSync`, no `SendBillSync`.
Repetir la solicitud del mismo mes recupera las raíces fiscales ya preparadas y
vuelve a activar únicamente el trabajo pendiente, sin reservar nuevos números.

Reemplazo y eliminación son ajustes explícitos; nunca se sobrescribe un
documento aceptado. Este primer alcance emite documentos individuales regulares;
la UI no ofrece todavía notas de reemplazo o eliminación.

La configuración de software/test set es por entidad legal y familia fiscal,
porque facturación y nómina pueden tener credenciales y habilitaciones distintas.
Nómina reutiliza del emisor fiscal versionado el certificado, endpoint, ambiente
y datos legales, pero congela `SoftwareID`, referencia segura del PIN,
`TestSetId`, prefijo y consecutivo propios en el snapshot. La operación real en
producción exige credenciales válidas y habilitación externa de la empresa ante
DIAN; el sistema no simula esa aceptación.

## 9. Observabilidad, privacidad y rollback

Cada transición registra tenant, business, run, employment, accounting job o
fiscal process, correlation ID, intento, duración y error seguro. No se registran
salarios por persona, XML completos, cuentas bancarias, documentos de identidad,
certificados o secretos en logs.

El rollout se controla por permiso y configuración del tenant. Las tablas son
aditivas. Antes de aprobación se puede retirar el módulo sin efectos externos;
después de aprobar, el rollback consiste en deshabilitar nuevas operaciones y
procesar ajustes contables/fiscales, nunca borrar historia.

## 10. Puerta de aceptación

- cálculo reproducible con el mismo rule set y entradas;
- aislamiento de tenant/business y autorización por operación;
- deducción rechazada sin acuerdo/autoridad vigente;
- aprobación concurrente e idempotente;
- débito igual a crédito y no duplicación en retry;
- snapshot electrónico mensual único por trabajador y ajuste trazable;
- CUNE/SoftwareSC determinísticos, XSD oficial, firma y transporte específico
  `SendNominaSync` verificados;
- catálogos servidos desde tabla/API, sin listas de negocio hardcodeadas;
- build, pruebas de dominio/integración y compilación SQL exitosos.

## 11. Superficie administrativa y reportes

La navegación de Nómina separa liquidaciones, relaciones laborales, Novedades,
pagos, documentos electrónicos, reportes y configuración. Novedades reúne
ingresos y deducciones; no existe una sección de Descuentos independiente. La
pestaña
Empleado de Terceros consulta la relación laboral por `PartyId` y enlaza a Nómina;
no permite editar ni replica salario o contrato en `dbo.Employees`.

El workspace comparte encabezado, colores, radios y navegación visual con
Contabilidad; las pestañas conservan semántica y navegación por teclado. Cada
acción principal aparece en su sección. Las consultas y selecciones se aíslan
por tenant y sede, y cambiar de contexto desmonta los formularios y el detalle
anterior. Una consulta fallida presenta reintento y no se muestra como colección
vacía. La configuración enlaza los centros contables y la pestaña electrónica
enlaza la configuración DIAN existente, sin duplicar esos maestros.

Editar la serie electrónica conserva el software, referencia segura del PIN y
TestSet de nómina ya configurados, incluso cuando difieren de facturación. Una
serie nueva parte de las credenciales del emisor y exige verificar externamente
que ese software esté habilitado para nómina. La UI no equipara la preparación
del mes con aceptación DIAN; permite recuperar un período preparado aunque el
cupo actual esté agotado, dejando la reserva al motor fiscal canónico.

Los reportes de nómina son una extensión nativa de Reporting, no consultas
embebidas en la página. El catálogo versionable vive en
`reporting.PayrollReportDefinitions`; `PayrollReportingService` valida permiso,
alcance y rango, y `SqlPayrollReportingStore` es el único adaptador autorizado
para consultar snapshots aprobados, lotes confirmados, su resultado contable
y estados del motor fiscal. El resumen y el dataset de pagos reconocen como
pagado exclusivamente el lote cuyo job de pago está Posted (13.10).
La UI obtiene tanto definición como datos por esos contratos y reutiliza
`ReportViewer` para búsqueda, impresión/PDF y CSV; no contiene SQL, datasets ni
listas de reportes hardcodeadas.

El catálogo inicial incluye resumen de nómina, comprobante individual
imprimible, detalle por concepto,
deducciones, aportes patronales/parafiscales, provisiones/prestaciones, costo
laboral, pagos, estado de nómina electrónica e ingresos/retenciones. Todos
aplican `TenantId`, `BusinessId`, permiso `payroll.read`, rango máximo y filtro
opcional por trabajador. Reporting consume fuentes autorizadas e inmutables y
no recalcula la liquidación ni cambia estados operativos, contables o fiscales.
En el MVP estas consultas operativas permanecen cerca del módulo `Payroll`, como
ordena la decisión transversal de Reporting. No se crea un segundo motor ni una
cola paralela; una proyección durable se añadirá al motor de Reporting únicamente
si el benchmark demuestra que las fuentes indexadas no cumplen el objetivo.

## 12. Referencias

- Resolución DIAN 000013 de 2021 y resolución compilatoria 000227 de 2025.
- Anexo técnico DIAN de documento soporte de pago de nómina electrónica 1.0.
- Código Sustantivo del Trabajo, artículos 149 y 150.
- Ley 2101 de 2021 y Ley 2466 de 2025.
- `docs/invariantes-arquitectonicas-auraly.md`.
- `docs/implementation/accounting-operational-design.md`.
- `docs/implementation/dian-fiscal-engine-design.md`.

## 13. Cierre funcional y técnico del 9 de noviembre de 2026

Esta sección prevalece sobre las limitaciones del slice mínimo anterior.
Es contrato objetivo pendiente de implementación/migración y pruebas, no
evidencia de funcionamiento. Las decisiones son de producto/ingeniería salvo
las fuentes normativas identificadas. Las capturas especifican campos, acciones
y documentos visibles; no prueban efectos ocultos de Mantis.

### 13.1 Períodos y selección

- Liquidaciones separadas: Mensual (mes calendario), Primera quincena (1–15) y
  Segunda quincena (16–fin). El contrato mensual sólo entra en Mensual; el
  quincenal en las dos quincenas. «Quincena 2» no autoriza otra mensualidad.
- Cargar muestra elegibles con identificación, contrato, ingreso/retiro,
  periodicidad, búsqueda y todos/ninguno. La selección es entrada persistida del
  borrador; desmarcar no desactiva contratos. Antes de contabilizar se puede
  agregar un elegible omitido o retirar uno, conservando una sola selección.
- Días iniciales: 30/15, incluso en febrero/meses de 31 días. El usuario ajusta
  en Novedades dentro de 0–30/0–15. Son días de liquidación, no fechas inventadas.
  Un conflicto con ingreso/retiro/ausencias exige corregir antes de contabilizar;
  no se reconoce tiempo fuera de la relación laboral.
- Tarifa diaria ordinaria: salario mensual/divisor mensual versionado (30 en
  este alcance). Horas adicionales usan conceptos de horas, no más días.
- Se protege concurrentemente contra causaciones regulares superpuestas por
  trabajador, incluso si cambia la periodicidad o selección. Ajustes sólo
  diferencias contra original. Un cambio de periodicidad rige desde un período
  posterior sin solapamiento.

Control de superposición (13-11-2026): la aprobación regular
comprueba PartyId contra causaciones regulares Approved del mismo empleador,
incluidos contratos y sedes anteriores, y rechaza dos contratos de una misma
persona dentro de la liquidación. La aceptación se serializa por el Tenant
existente antes de bloquear el run; no se bloquea el cálculo ni se crea una
reserva, tabla, motor o estado nuevo. Es un bloqueo breve de aceptación por
empleador, no global entre tenants. La comprobación usa un índice por
TenantId/PartyId/PayrollRunId y el mismo comando transaccional de aprobación;
no agrega viajes SQL ni lecturas por trabajador. Ajustes y replay conservan
sus rutas existentes. Presupuesto: hasta 500 trabajadores y los mismos viajes
de aceptación; la latencia se mide con aprobaciones simultáneas. La migración
sólo agrega el índice: no cambia liquidaciones anteriores ni intenta reparar
superposiciones históricas. Rollback conserva documentos y movimientos;
retirar el control reabriría la posibilidad de doble causación y requiere una
decisión explícita. El calendario de nuevos borradores se valida en el mismo
creador de liquidaciones: PayrollWorkPeriod.CreateRegularDraft exige mes
completo o una de sus dos quincenas, usando los 30/15 días del catálogo vigente.
La validación es del comando de creación; la lectura/cálculo de una liquidación
persistida conserva sus límites reales, sin reinterpretar borradores previos.
Los ajustes conservan la exigencia existente de mismo período/periodicidad del
original. No se migra ni repara historia ni se agrega versión, tabla o flujo.
El creador lee una opción del catálogo en un comando adicional constante,
antes de insertar: tres comandos incluyendo su respuesta autoritativa, sin I/O
por trabajador. El formulario propone fin del mes de la sede y conserva la
fecha de pago separada. Los tests históricos siembran expresamente el borrador
de una versión anterior; la API actual rechaza su rango sin escribirlo.
Backend y build completo de admin correctos. La suite
calendar-regular-foundation aprobó 1.232/1.232, incluidos catorce casos nuevos
de calendario. La regresión SQL del creador y del formulario continúa en
verificación. El gate de cálculo masivo continúa abierto hasta su medición.
La fuente de causación regular usa fin de período, separado de la fecha prevista
de pago, y conserva el día civil en la zona de la sede mediante BusinessCalendar.
Se obtiene la zona en el comando de cabecera existente. La prueba propone pagar
siete días después y verifica que la causación permanece en el período trabajado.
Las fuentes ya aceptadas no se reescriben; ajustes conservan su fecha explícita.
La prueba SQL `Regular_approval_serializes_overlapping_periods_and_retains_the_worker_identity_after_a_contract_change`
aprobó: una aceptación y un conflicto simultáneos, seis comandos en conjunto y
708,6 ms; replay conserva una fuente/job/outbox, el período siguiente es válido
y un contrato quincenal nuevo de la misma persona no repite septiembre. La fecha
de causación permanece en septiembre aunque se proponga pagar en octubre.
La repetición `payroll-overlap-regression` aprobó 5/6 casos, incluido el journey
completo y la nueva protección. El caso de cálculo de 500 superó 5 s con
18.654,8 ms: diez comandos, de los que el guardado por lote tardó 12.893,6 ms.
No se cambió el límite ni se declara cerrado ese gate. La instrumentación
conserva los tiempos por comando para distinguir la carga y la persistencia.

### 13.2 Novedades y recálculo

La implementación del 13-11-2026 permite cantidad cero únicamente para
FixedAmount + Amount, sin cambiar valor ni identidad de la novedad. Días,
horas y los demás métodos requieren cantidad positiva; se rechaza precisión
superior a seis decimales antes de que SQL la redondee. La regla pertenece a
PayrollNoveltyInput y la reutilizan el guardado y PayrollCalculator. SQL
refuerza cantidad/valor no negativos en Novelties y RunLines; la condición por
método/tipo se comprueba con sus catálogos bajo la transacción serializable
del guardado, sin códigos GUID ni reglas contables duplicadas en SQL.

Presupuesto de ese guardado individual: dos comandos (dimensiones/validación
y escritura), más inicio/commit de la transacción, constantes y sin I/O por
trabajador. La captura inicia en cero, permite conservarlo para importe fijo
y exige capturar los días/horas correspondientes. La aceptación contempla
rechazo de negativos, horas cero y pérdida de precisión; importe fijo cero
en cantidad, cálculo, origen y desprendible; cantidades positivas existentes.
La suite de PayrollCalculator aprobó 29/29 y la tanda
`portfolio-payroll-closure-functional` aprobó 38/38 sin omisiones, incluido el
journey completo de nómina con cuota fija de cantidad cero hasta contabilidad,
pago, fiscal y reportes. La comprobación de navegador continúa. El despliegue requiere esquema compatible
antes de habilitar la captura. Después de aceptar cantidades cero no se
restaura el constraint/validador anterior ni se inventa cantidad uno para
forzar rollback: se preservan las entradas y se corrige hacia adelante.

- Liquidar prepara el cálculo editable. Novedades permite elegir liquidación y
  trabajador y administrar ingresos y deducciones juntos, sin sección Descuentos.
  La vista del 16-11-2026 excluye borradores sin calcular y muestra primero todos
  los trabajadores de la liquidación, con identificación, contrato, días y totales.
  Entrar a uno abre su detalle en esa misma pantalla: conceptos, cantidades,
  bases, valores y observaciones; permite ajustar días y agregar, editar o retirar
  novedades manuales. La captura reutiliza el formulario existente dentro del
  detalle, sin otra lista global ni un modal adicional. Las autorizaciones siguen
  siendo el soporte de las deducciones, no un segundo cálculo de descuentos.
  Si guardar una novedad funciona pero falla el recálculo, la entrada permanece
  visible y editable como pendiente de recalcular; los totales conservan el último
  cálculo aceptado. Cambiar de trabajador o liquidación cierra su editor.
  Se conserva el paso existente «Aprobar y contabilizar», seguido de pago cuando
  el motor confirme la causación. El ajuste visual no certifica los campos
  contractuales/perfiles/centros por línea pendientes en 13.4 ni la paginación
  histórica pendiente de 13.7. Un período sin elegibles explica que los contratos
  deben coincidir en vigencia y periodicidad; no transforma un mensual en quincenal.
  El lector canónico ReadRunAsync devuelve las novedades de cada trabajador:
  las que se superponen al período y las referenciadas por sus partidas. También
  incluye una entrada guardada aún sin recalcular y conserva la referencia de una
  novedad cuyas fechas se corrigieron. Resuelve el conjunto autorizado antes de
  enriquecerlo y usa el mismo comando SQL para detalle completo y respuesta parcial;
  no consulta por trabajador ni trae novedades de otros períodos o contratos.
  GetOptions deja de consultar el historial de novedades; su miembro Novelties
  conserva la forma del contrato, vacío, como los antiguos selectores laborales.
  La UI consume sólo las novedades del trabajador y aplica la respuesta del
  guardado a ese mismo detalle antes del recálculo. No agrega endpoint, tabla,
  writer, motor, polling ni GET posterior al guardado. El despliegue de API y web
  se hace coordinadamente. Esto no cierra la paginación de los otros historiales
  generales de nómina pendientes en 13.7.
  La verificación local de este lector aprobó cinco escenarios SQL/API, incluido
  el ciclo completo hasta pago, fiscal y reportes y los lotes de 2/100/500/501.
  Cálculo de 500: 2.196 ms; recálculo individual p95: 712 ms, con diez comandos;
  lectura del detalle: un comando. Se comprobaron novedades consumidas y de
  importe cero, separación por trabajador y exclusión de otro período. La
  regresión de pantalla cubre lista/detalle, días, alta/edición/retiro, fallo de
  recálculo sin perder la captura, lectura histórica, cambio de sede/período y
  respuesta tardía que no cierra una segunda captura. La revisión con datos
  locales confirmó persistencia después de recargar y salario del snapshot
  mostrado en el encabezado, sin consultar ni inventar un salario histórico.
- Cada línea conserva identidad, concepto/naturaleza, cantidad/unidad, tarifa,
  base, valor, centro, observación y soporte según su método. Dos préstamos del
  mismo concepto siguen separados por origen. Importe fijo admite cantidad
  visible cero; días/horas requieren cantidad válida, sin inventar cantidad 1.
- Sueldo mensual se corrige en contrato con vigencia; Novedades ajusta días y
  registra diferencias identificadas. No sobrescribe salario sin base trazable.
  Los días finales derivan de días base menos ausencias identificadas: editar
  días finales modifica esa misma entrada, sin cobrar la ausencia dos veces.
- Manuales se agregan/editan/retiran del borrador. Salud/pensión y resultados
  estatutarios se recalculan; su no aplicación requiere condición contractual o
  regla explícita, no borrado arbitrario. Una manual no suplanta una automática.
- Guardar devuelve cálculo autoritativo del trabajador y totales afectados.
  Recalcular conserva selección/entradas y sólo reemplaza resultados editables;
  el borrado actual de RunEmployees/RunLines no puede borrar ediciones.
- La versión/huella de todas las entradas invalida el cálculo al modificarlas.
  Contabilizar verifica vigencia bajo concurrencia, incluida una novedad nueva
  que no estuviera referenciada en el snapshot anterior.

### 13.3 Descuentos y obligaciones

Incremento en implementación (13-11-2026): editar/retirar novedades reutiliza
PayrollService y SqlPayrollStore, Novelties.RowVersion y el estado Voided
existente. Sólo Draft/Approved son editables; Consumed permanece inmutable.
La transacción serializable y la huella canónica de novedades impiden aprobar
un cálculo anterior a una modificación. No se elimina historia ni se modifica
una fuente aceptada. La respuesta devuelve la novedad autoritativa y actualiza
el propietario del workspace sin otro GET. Las liquidaciones afectadas deben
recalcularse antes de contabilizar. Presupuesto: dos comandos para guardar y
uno para retirar, constantes respecto al historial, sin I/O por trabajador.
Aceptación: edición, retiro, versión obsoleta, aislamiento tenant/sede,
inmutabilidad consumida y recálculo obligatorio, con regresión SQL/API/UI.
El contrato es aditivo y no requiere migrar filas. Rollback conserva las
novedades retiradas y sus versiones; no las reactiva ni borra.

La ficha del trabajador ofrece agregar/editar/retirar manuales y recalcular
ese trabajador. El mismo CalculateRun recibe RecalculateEmploymentIds con
versión y hasta 500 IDs distintos, sin combinar cambios de selección/días.
Extiende el cálculo parcial y su persistencia por lote existentes; conserva
los días base y aplica las ausencias vigentes. No simula una edición de días
para recalcular, porque conservar un total anterior podría cancelar una
ausencia nueva. La carga mantiene los mismos comandos por conjunto y la
respuesta parcial actualiza el detalle/totales del único propietario frontend.
Guardar o retirar desde el trabajador actualiza primero la novedad con su
respuesta y después solicita ese recálculo al mismo endpoint canónico, si el
usuario tiene permiso y la liquidación editable está afectada. Son dos etapas:
la captura se cierra al guardarse y el progreso del cálculo tiene su propietario
existente. Si el cálculo falla, la entrada permanece guardada, se informa esa
distinción y se reintenta explícitamente Recalcular trabajador; no se repite la
creación. Fuera de una liquidación seleccionada se conserva la indicación de
recalcular. Las autorizaciones también reutilizan su respuesta, sin recargar
el workspace después de guardar/desactivar. Los campos de días adoptan el
resultado nuevo por identidad de cálculo, sin remontar el componente ni hacer
otra consulta, y conservan el borrador si el intento falla.

Verificación del incremento: `payroll-novelty-worker-functional` aprobó el
journey completo; la repetición en `portfolio-payroll-collation-functional`
también aprobó ese caso con las cuatro comprobaciones de aislamiento añadidas
(guardar/retirar contra tenant o sede ajenos). Se verificaron versiones
obsoletas, preservación de la captura al fallar el recálculo, retiro repetido sin
cambiar rowversion, inmutabilidad consumida y ausencia de aprobación con huella
vieja. Cambiar la ausencia de dos a tres días conserva base 30 y produce 27;
editar luego los días a cero conserva la entrada de ausencia y su origen.
El resto del journey conserva contabilización, pago, fiscal y reportes.

La ronda de navegador `portfolio-payroll-browser` aprobó 16/16, incluidas
edición/retiro desde Novedades, importe fijo con cantidad cero, recálculo por
trabajador sin GET de reconstrucción, recuperación del cálculo sin repetir la
captura y cambio de sede mientras responde el guardado sin emitir el segundo
comando en el contexto nuevo. La suite base aprobó 1.218/1.218 y el build completo
de backend/Next y lint dirigido pasaron. La revisión del incremento conserva
PayrollService/SqlPayrollStore/PayrollCalculator y sus snapshots, tablas y
fuentes canónicas; no añade motor, writer, cola, estados ni efectos financieros
al editar o retirar. Los restantes pendientes de la sección 13 continúan
explícitos, incluidos paginación histórica, perfiles/afiliaciones y calendario.

- La línea que exige autorización incorpora referencia, autoridad, evidencia,
  vigencia, beneficiario, total y cuota mediante DeductionAgreements existente.
  El usuario resuelve todo ahí; no se generan cuotas futuras al guardar.
- Autorización no es saldo de préstamo. La amortización referencia la partida
  financiera original; no crea deuda/desembolso ni saldo financiero en el acuerdo.
- Crear/editar/recalcular no consume autorización ni cartera. Aceptar Contabilizar
  congela el snapshot y compromete cuotas una vez en la transacción de aceptación.
  Payroll conserva el compromiso de autorización. La disponibilidad financiera
  pendiente extiende las referencias/protección canónicas de AccountingSourcesAccept,
  sin otra tabla de reservas ni cola.
- SqlAccountingPostingProcessor amortiza obligación y contabiliza en la misma
  transacción. Pendiente/fallido conserva el compromiso y no se muestra como
  amortización contabilizada; retry usa la misma fuente sin consumir otra cuota.
- Quitar una cuota del borrador no elimina autorización, deuda ni historia.
  Fuentes aceptadas no se reabren ni liberan automáticamente por fallo: ajustes
  enlazados conservan el mecanismo existente.
- Autoridad y límites dependen del régimen de descuento; consentimiento no
  habilita cualquier importe. Se conservan los controles de CST 149–151.

### 13.4 Contratos, perfiles y cuentas

- Completar EPS/AFP/cesantías/ARL/caja, aplicar auxilio, perfil, cargo/sitio/centro,
  terminación prevista y retiro real/motivo, prueba, cotizante/subtipo,
  riesgo/código PILA, pensionado, jefe, datos bancarios y corte de vacaciones.
  Identidad/foto/ciudad reutilizan Parties. Corte de vacaciones no agrega
  un workflow de solicitud/aprobación.
- Operativo/Administrativo son perfiles del tenant asociados al contrato, con
  mapping débito/crédito por concepto y parámetros aplicables versionados.
  Reutilizan conceptos y reglas legales; cambiar clasificación no cambia por
  sí mismo tarifas legales. Las tasas de las fotos no se copian como ley.
- Salud/pensión se calculan sobre IBC; tarifa ordinaria del empleado 4%/4%
  parametrizada. No se calcula sobre auxilio ni se prorratea dos veces una base
  ya proporcional. Cotizante/integral/exención/riesgo tienen reglas expresas.
- Cuenta: mapping del concepto en perfil contractual, después mapping canónico
  aplicable como configuración explícita; nunca código/nombre inferido.
  Por decisión del 2026-11-13 (comprobantes, 14.12), la cuenta automática se
  resuelve al contabilizar con su vigencia efectiva, no se congela al aceptar.
  Un mapping explícito inválido no cae silenciosamente a otro. Si falta una
  cuenta válida, queda pendiente de configuración y se revierten los efectos
  financieros del intento, sin repetir la liquidación ni la aceptación.
- Centro: explícito de línea, contractual, resolución canónica de la operación.
  El resultado contabilizado conserva auxiliar y centro por partida; el centro
  mantiene su resolución existente, sin reparto porcentual nuevo.
- Neto por pagar identifica trabajador; EPS/AFP/ARL/caja identifican entidad
  beneficiaria y conservan contrato/trabajador. Provisión y devengado pagadero
  son naturalezas diferentes.
- La configuración cubre los conceptos de la foto 1: salario, transporte,
  salud/pensión, incapacidad empresa/EPS/laboral, maternidad/paternidad, permiso
  remunerado/no remunerado, suspensión, movilidad/alimentación, prima,
  cesantías/intereses, vacaciones disfrutadas/pagadas, anticipos, préstamos y
  otras CxC del trabajador. Cada uno declara unidad, base, naturaleza, cuentas,
  tratamiento fiscal y método permitido. Capturar cantidad/valor mediante un
  método manual explícito no promete un autocálculo legal no certificado.
  Incapacidades/licencias/vacaciones conservan fechas y clasificación exigidas
  por su nodo oficial; no se envían todas como OtroConcepto por defecto.

### 13.5 Contabilización y pago por grupo

- Contabilizar reutiliza aprobación/fuente/job/outbox existentes; no incorpora
  otra aprobación visible ni enum. Approved es fuente aceptada, no asiento
  terminado; presentación y elegibilidad derivan del trabajo contable.
- Causación: fin del período reconocido (o fecha del hecho de ajuste), separada
  de fecha prevista y efectiva de pago; no usar fecha prevista automáticamente.
- Sólo Posted habilita pago del flujo solicitado. Accounting desactivado o
  CommercialEffectsApplied no equivalen a nómina contabilizada ni activan
  contabilidad de oficio.
  Esta barrera ya se valida en la consulta transaccional de aceptación del lote
  existente: el trabajo debe corresponder al tenant, sede, liquidación y tipo de
  causación/ajuste y estar `Posted`. No añade consultas por trabajador ni otro
  estado. La regresión del flujo completo rechaza `Pending`,
  `AccountingPendingConfiguration` y `CommercialEffectsApplied` sin lote,
  fuente/trabajo de pago ni outbox, y verifica después el pago contabilizado.
  El incremento de 13.9 añade la separación de grupos del contrato congelado.
  El incremento de 13.10 añade formas del tenant y auxiliares originales;
  las restantes ampliaciones de esta sección conservan su estado explícito.
- Al seleccionar la liquidación se muestran primero los grupos contractuales
  de transferencia y efectivo, contraídos, con cantidad y total pendientes.
  No se exige adivinar una forma para descubrir a sus trabajadores. Expandir
  cada grupo muestra integrantes y permite escoger una forma del tenant
  compatible con su tipo. Cada grupo conserva su fecha, referencia y origen;
  confirmar uno deja el otro disponible en la misma ventana, conservando su
  captura. Son lotes independientes del mismo comando, con identidades
  idempotentes distintas; no existe un pago global ni un nuevo flujo financiero.
  Mientras un lote se confirma se bloquean otros envíos y el cierre de la
  ventana para evitar respuestas competidoras sobre los pendientes.
  Las formas se consultan paginadas por uso Payroll y tipo desde el selector
  existente: cero consultas al catálogo con grupos contraídos, una página por
  selector abierto, sin consultas por trabajador ni GET del detalle después
  de pagar. La respuesta autoritativa actualiza pendientes e historial en sus
  propietarios actuales. Se paga el neto completo por integrante; sin abonos parciales
  por trabajador. Neto cero no exige egreso. Neto negativo se rechaza con el
  control existente de deducciones superiores al devengado y exige corregir
  las entradas; no se convierte automáticamente en préstamo/CxC ni se arrastra
  a otro período. Los límites protectores y de autorización siguen vigentes.
- Banco/caja de empresa es origen; banco del empleado es destino y no filtra
  por coincidencia. Transferencia exige destino completo. Cada grupo conserva
  la identidad de la forma, fecha efectiva, referencia e integrantes. Por la
  decisión expresa del usuario del 2026-11-10, el auxiliar de esa forma se
  resuelve con la configuración vigente al contabilizar el pago, aunque ya
  esté confirmado y en cola; resolución, evidencia y asiento comparten la
  transacción del processor. Un pago completado conserva su auxiliar aplicado
  y el replay no vuelve a resolverlo. No se copia la configuración completa
  de la forma en cada grupo; rige la sección 14 del diseño de comprobantes.
- Cancela exactamente la cuenta por pagar causada por trabajador, sin repetir
  gastos/aportes/provisiones. Unicidad protege aceptación concurrente/replay.
- Confirmar registra el pago, no envía dinero al banco. Confirmed impide repetir
  mientras se procesa; Pagado exige posting completado. Fallo retoma el mismo
  trabajo; no oculta otros grupos. Pendiente/completitud se deriva por empleado,
  no por existencia de un lote cualquiera.

### 13.6 Fiscal y documentos

- DSNE mensual por empleador + trabajador + mes, acumulando quincenas sin
  duplicarlas. Hecho fiscal: pago o abono en cuenta exigible, no «todos pagados».
  La fuente aceptada que reconoce ese hecho se soporta en su mes aunque se
  desembolse después. Un borrador no equivale a hecho exigible. Un error técnico
  del posting no cambia el mes del hecho ni autoriza omitirlo del plazo fiscal:
  el corte toma la fuente válida inmutable y expone el error contable aparte.
  La condición Posted del pago no se reutiliza como condición del DSNE.
- El corte fiscal autorizado reúne las fuentes de todas las sedes del mismo
  empleador, conservando sus dimensiones originales y una raíz por trabajador/
  mes. No genera un DSNE por cada sede para el mismo hecho. Los cambios de sede
  y contratos sucesivos se consolidan conservando su detalle; cualquier límite
  fiscal del anexo se valida explícitamente, sin descartar una relación laboral.
  El permiso de sede no habilita consultar otras: consolidar exige alcance del
  empleador. La configuración fiscal identifica la sede emisora de la raíz
  existente; no se crea otro documento o pipeline por sede.
- Fecha prevista no prueba pago. Conservar fecha cierta del hecho exigible y
  fechas efectivas por trabajador en lotes. Para abono sin desembolso se utiliza
  fecha cierta de ese hecho como fecha de soporte: decisión de mapeo derivada
  de la doctrina, no afirmación de transferencia realizada.
- T5.4 exige FechaPago (1–N) y sólo enumera Forma=1; no inventar Forma=2.
  Método y destino se traducen al catálogo oficial. XSD y aceptación DIAN del
  abono sin desembolso son gates fiscales pendientes, no probados por este texto.
- El origen temporal de cada fecha fiscal se conserva (pago o abono), no se
  sustituye por la fecha prevista. Las fechas efectivas futuras no se inventan.
  Un desembolso posterior a un abono correctamente reportado no obliga por sí
  solo a volver a reportar el devengado. Todos los instantes se traducen con
  IBusinessClock/zona configurada, no medianoche UTC impuesta a una fecha local.
- Provisiones no se envían indiscriminadamente como devengados: se aplica el
  tratamiento fiscal/momento exigible de cada concepto. Desembolsar después
  no vuelve a reportar salario; corrección real usa nota de ajuste fiscal.
  No sobrescribir XML/CUNE.
- Desprendible laboral y DSNE son documentos distintos. Previo a contabilizar,
  vista preliminar identificada; definitivo con datos históricos congelados,
  columnas de devengados/deducciones, cantidades, líneas repetidas y firma.
  Mostrar neto por pagar si aún no se pagó.
- Bandejas con filtros/acciones de fotos 4/6, Excel de resúmenes, novedades,
  seguridad social/provisiones y resumen bancario; PDF individual/masivo con o
  sin espacio de firma. Importación usa plantilla validada de esas mismas
  novedades por lote. No implica dispersión automática, firma digital del
  trabajador ni archivo bancario propietario no suministrado.

### 13.7 Migración, rendimiento y aceptación

- Corte aditivo/versionado: históricos siguen legibles sin inventar afiliaciones,
  perfiles ni autorizaciones. Nuevas contabilizaciones exigen configuración
  completa para sus conceptos/condiciones aplicables; fuentes aceptadas terminan
  con sus snapshots, sin recálculo.
  Formas del tenant: sección 14 del diseño de comprobantes; su sección 14.0
  conserva la presentación POS y exige catálogo y reglas canónicas comunes
  también en POS/Edge. Sin doble escritura.
- Cálculo masivo carga y persiste por lote, sin I/O por trabajador/concepto.
  Recálculo individual trabaja sólo entradas afectadas y agregados acotados.
  Un propietario de carga en UI, comandos devuelven estado autoritativo,
  actualización por push/acción explícita, sin polling.
- Objetivos a medir (no resultados): página 50 + totales y recálculo individual
  p95 <1 s; cálculo 100/500 empleados <2/<5 s; aceptación contable/pago de 500
  <2 s y procesamiento local contable <2 s, etapas medidas separadamente.
  Viajes de lectura constantes entre 100/500, objetivo <=8 por conjunto.
  Máximo 500 trabajadores por comando en este corte, sin trocear una
  contabilización a escondidas.
- Exportación explícita con filtros idénticos, máximo 5.000 filas de detalle;
  PDF masivo además <=500 trabajadores. Exceso exige acotar, sin truncamiento
  ni GET por desprendible. Benchmark de primera/siguientes páginas y 50/100/500
  sin diagnóstico concurrente.
- Aceptación: febrero/31 días; 30/15; solapamiento; selección; edición/recalcular;
  cuota fija; novedad concurrente; autoridad/saldo/obligación; fallo/retry sin
  doble consumo; perfiles/centros/beneficiarios; pago transferencia sin efectivo
  y viceversa; pendientes no pagados; neto cero; cuenta original; consolidación
  quincenal mensual y abono sin pago; provisión no exigible; histórico/tenant;
  benchmark y paridad visual de las ocho fotos de nómina.

### 13.8 Fuentes consultadas el 9 de noviembre de 2026

- [DIAN, Resolución 227/2025, arts. 1.5.3.2.1–2 y 1.5.3.4.1.1](https://normograma.dian.gov.co/dian/compilacion/docs/resolucion_dian_0227_2025.htm):
  periodicidad mensual por beneficiario y primeros diez días del mes siguiente.
  [Anexo T5.4, Diario Oficial](https://normograma.com/documentospdf/PDF/R_DIAN_0227_2025_ANEXOT5.4.pdf):
  contrato técnico; no prescribe navegación.
- [DIAN, Concepto 17521/2025](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_17521_2025.htm):
  exigibilidad frente a provisión condicionada.
- [CST, arts. 149–151](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=199983):
  autoridad/límites conservados al unificar pantallas.
- [UGPP, ABC salario integral 2026](https://www.ugpp.gov.co/wp-content/uploads/2026/02/ABC-Trabajadores-dependientes-que-devengan-salario-integral.pdf):
  tarifas/base/responsables; importes anuales no pertenecen a código.
- [Siigo, crear/liquidar/contabilizar/pagar](https://siigonube.portaldeclientes.siigo.com/crear-liquidar-y-contabilizar-o-pagar-la-nomina-electronica-pro-plus/):
  selección, períodos y novedades.
- [Alegra, pago de nómina](https://ayuda.alegra.com/col/realiza-los-pagos-de-tu-nomina):
  causación previa, fecha/cuenta de origen, registro separado de transferencia real.

### 13.9 Incremento: selección del grupo pendiente (2026-11-11)

El lote existente selecciona exclusivamente pendientes cuyo medio contractual
congelado coincide con el solicitado. `payroll.PayableRunEmployees` es la consulta
compartida por detalle, aceptación y respuesta: exige causación Posted del mismo
tenant/sede/liquidación, neto positivo y ausencia de un pago comprometido. No
compara el banco del trabajador con el de la empresa. Transferencia requiere
destino completo en el snapshot; efectivo no lo exige. El límite es 500
trabajadores y no se trocea un pago automáticamente.

La selección reutiliza el código DIAN de medio de pago ya congelado en el
snapshot laboral: debe identificar inequívocamente un medio soportado del
catálogo. No cambia el calculador ni el formato del snapshot, ni copia otra
configuración. Una identidad ausente o ambigua no recibe un medio por defecto.
Cambiar después el contrato no cambia el grupo de esa causación.

La aceptación serializa pagos mediante la fila de liquidación existente. Inserta
exactamente los integrantes capturados bajo ese bloqueo y conserva la unicidad
de PaymentLines. Cada grupo produce la fuente PayrollPayment y el job/outbox
existentes; el processor conserva la escritura financiera. No añade estados,
workers, colas, tablas de pago ni writers. Replay de un lote cuya señal se publicó
devuelve el resultado sin publicarla otra vez; si aún no se publicó, conserva el
envío pendiente del outbox actual. La fecha de la fuente representa el mediodía
de la zona del negocio, conservando el día elegido y sin imponer medianoche UTC.

Detalle devuelve elegibilidad en su mismo comando SQL. Confirmar devuelve lote,
versión, nombre del medio y pendientes restantes en la misma transacción. UI
reutiliza ambos resultados en sus propietarios de caché actuales: no refresca
opciones, liquidaciones ni detalle tras guardar. Lista, cantidad y total del
grupo no declaran pagados otros grupos. Actualizar pendientes/reintentar una
consulta fallida exige acción explícita, sin polling. Confirmar no envía dinero
al banco.

Presupuesto: detalle de hasta 500 trabajadores en un comando y p95 menor a un
segundo; aceptación en dos comandos por lote y menos de dos segundos, separada
del procesamiento contable. No certifica cálculo de 500 empleados ni el resto
de 13.7. El despliegue requiere función SQL antes de API/frontend, sin backfill
ni modificación de fuentes aceptadas. Rollback retira API/frontend nuevos antes
de la función y conserva lotes/snapshots.

Al cerrar 13.9 seguían pendientes formas del tenant, cuenta vigente al
contabilizar y auxiliar original por trabajador; 13.10 amplía esas capacidades.
Continúan pendientes estado pagado derivado del posting y otros alcances de
13.1–13.7. Este incremento no declara cerrado el flujo completo de nómina.

Validación y auditoría del incremento:

- Esquema Release y API compilan con cero errores/advertencias; las 16 pruebas
  Foundation de nómina aprobaron. Lint de las superficies modificadas no tiene
  errores y conserva una advertencia previa de dependencias de useCallback en
  configuración de mappings, fuera del formulario de pago. No se suprimió.
- SQL/API ejerció liquidaciones de 2/100/500 trabajadores: contratos actuales
  deliberadamente distintos del snapshot, sólo transferencia en su primer lote,
  efectivo pendiente, rechazo de destino incompleto y tenant/sede ajenos,
  replay, dos confirmaciones concurrentes de efectivo, fechas en zona de negocio
  y asientos equilibrados por grupo. El débito del grupo de transferencia no
  incluye al trabajador de efectivo. También aprobó la regresión vertical
  existente de configuración, cálculo, aprobación, contabilidad, fiscal y reportes.
- La última tanda `payroll-payment-groups-single-read.trx` quedó con tres
  aprobadas y una fallida: el caso de 100 trabajadores completó sus aserciones
  funcionales, pero su aceptación tardó 2.135,1 ms y excedió dos segundos.
  Los casos de 2/500 tardaron 1.447,5/1.850,4 ms. Se comprobaron dos comandos de
  aceptación en cada tamaño. No se presenta esta tanda como completamente verde.
  El detalle usó un comando en veinte muestras por tamaño; p95 de
  3,2/147,3/90,1 ms. Las muestras de aceptación no demuestran el p95 de 13.7.
  Un diagnóstico posterior mostró 766.372 KB disponibles y presión física SQL
  verdadera, sin presión virtual. No prueba una causa única del incumplimiento.
  No se amplió el presupuesto ni se repitió la medición sin cambios.
- Navegador aprobó el pago de transferencia, persistencia del grupo de efectivo
  al reabrir y rechazo de destino incompleto. Una sola lectura de opciones,
  liquidaciones y detalle durante confirmación/reapertura prueba reutilización
  de la respuesta. Los dos escenarios finales aprobaron también con ventana de
  740 px de alto, con desplazamiento del modal y fecha capturada mediante
  calendario. Se revisó la captura visual del grupo de efectivo pendiente.
  Frontend/TypeScript compilan y los includes temporales de Next se retiraron.

La auditoría revisó el diff completo de este incremento: función/proyecto SQL,
contratos, servicio, store, API cliente, formulario, fixtures y documentación.
La elegibilidad tiene un propietario, el cálculo/snapshot laboral queda intacto,
la escritura sigue en PaymentBatches/PaymentLines y el mismo motor de fuentes,
jobs/asientos. Tenant/sede, permisos, bloqueo de liquidación, unicidad, replay y
rollback se conservan; no hay I/O por trabajador, polling, recuperación nueva
ni refetch después del comando. El remanente se deriva del conjunto capturado
bajo bloqueo, sin volver a consultar elegibilidad tras insertar. La advertencia
de lint y el gate de latencia se informan; este último impide declarar el
incremento terminado/publicable, además de los pendientes funcionales señalados.


### 13.10 Incremento: forma del tenant y cancelación del auxiliar original (2026-11-11)

El formulario de pago consulta `reference.PaymentMethods` por `usage=Payroll`
mediante el selector paginado existente, con búsqueda y cuenta visible. Varias
formas de transferencia pueden elegir distintas cuentas de la empresa y pagar
el mismo grupo contractual; no se compara el banco de la empresa con el del
trabajador. El catálogo técnico laboral conserva su función de clasificación
Cash/Transfer. No se modifica la presentación del POS.

`SqlPayrollStore` valida identidad, uso, tipo, estado y alcance del auxiliar
mediante la vista/readiness del catálogo, en el comando de aceptación ya
existente. La fuente conserva ID/tipo de la forma, sin una copia de su cuenta.
PaymentBatches conserva metadatos identificadores y, al contabilizar, evidencia
de versión, nombre, cuenta y banco utilizados. Las columnas son aditivas y
anulables para lotes históricos. El índice tenant/forma acota el control que
impide cambiar el tipo de una forma utilizada.

`SqlAccountingPostingProcessor.ResolveConfiguredPaymentMethodsAsync` incorpora
PaymentBatches al resolvedor transaccional existente. Los pagos de nómina
no requieren ni guardan una copia general de mappings al aceptar; la decisión
del 2026-11-13 en comprobantes 14.12 extiende esta política a las demás fuentes. Usa la configuración
vigente al contabilizar, incluso si cambió después de confirmar. La cuenta y
el banco se validan por la política compartida de pagos de salida. Un intento
sin configuración válida conserva el job pendiente y revierte la evidencia
mediante el savepoint existente. No crea otro job, writer, worker, motor ni
mecanismo de recuperación. Un job terminado retorna antes de resolver otra vez.

El débito se obtiene del asiento Posted de la causación/ajuste original del
mismo tenant, sede y liquidación. Una sola lectura por lote agrupa los netos
aceptados y cruza categoría, ordinal, tercero e importe de la fuente original
con sus líneas contabilizadas. El motor preserva ese orden de líneas. No
infiere cuentas por prefijo PUC ni usa el mapping actual de NetPayrollPayable.
Si el auxiliar original no es identificable de manera única, el job conserva
el estado pendiente con error específico. Las fuentes antiguas sin forma
conservan su categoría de salida por compatibilidad en el mismo processor, con
cuenta vigente al contabilizar, y cancelan igualmente el auxiliar original.
El resolvedor ignora caches derivados antiguos de mappings para ese débito/crédito
sin reescribirlos; los asientos ya terminados no se recalculan.

Presupuesto incremental: un comando adicional para resolver todas las formas
del lote y uno para cargar todos los auxiliares originales, independientes del
número de trabajadores (máximo 500); no hay I/O por trabajador. La aceptación
mantiene dos comandos y detalle uno, con los presupuestos de 13.9. El catálogo
pagina y filtra en servidor, y el componente compartido es su único propietario
de carga. Confirmar sigue reutilizando el resultado autoritativo en caché.

Orden de actualización: esquema aditivo, API/processor y frontend. No hay
backfill ni reescritura de fuentes. Retirar el frontend no borra lotes; antes
de volver a un processor anterior hay que deshabilitar nuevas confirmaciones
y terminar todos los jobs PayrollPayment pendientes, con o sin forma. No se debe ejecutar
un binario antiguo sobre fuentes nuevas como supuesto rollback compatible.

La última tanda de este incremento cumple los presupuestos de aceptación y
detalle de 13.9 en sus muestras, sin ampliar los límites. No certifica el p95
de aceptación ni el cálculo masivo del contrato completo de 13.7. Continúan
pendientes las ampliaciones de contratos, perfiles, novedades y liquidación de
13.1–13.7. Este incremento no declara terminado el diseño completo.


El resultado de pago tiene un único propietario de lectura:
`payroll.PaymentBatchPostingStatus`. Deriva IsPaid del lote Confirmed y del job
PayrollPayment Posted, con tenant/sede/tipo de fuente concordantes. No persiste
otro estado Paid ni agrega un writer. Historial, replay y reportes reutilizan
esta vista; el resumen conserva Pendiente y el dataset de pagos no incluye
lotes aún pendientes de asiento. El reporte muestra el nombre aplicado de la
forma cuando existe. Las definiciones y el layout de reportes permanecen; se
corrige el dato autoritativo que alimenta su estado/importe pagado.

La captura devuelve el resultado del job dentro del SELECT existente de su
transacción. No realiza un GET posterior para adivinar si el procesamiento ya
terminó. La pantalla distingue Confirmado de Pagado y muestra el estado
contable mediante las etiquetas compartidas; una actualización explícita
consulta el resultado actual. Ni UI ni Reporting derivan pago de una mera
reserva de PaymentLines.

La vista previa exige contabilidad Ready. La aceptación vuelve a comprobarlo,
con vigencia para la fecha elegida y bloqueo dentro de la transacción existente,
antes de reservar integrantes. Desactivar contabilidad tras causar una nómina
no permite crear un pago comercial sin asiento. Replay de un lote terminado
conserva su resultado aunque cambie esa configuración.


Validación final y auditoría posterior de 13.10:

- `payroll-current-accounts-paid.trx`: 13/13 escenarios SQL/API aprobados.
  Incluye 2/100/500 trabajadores con contrato compatible, 2/500 con forma del
  tenant, regresión vertical completa de nómina y resolución compartida de
  ventas/financiación, cartera cliente/proveedor y recibos manuales. Comprueba
  cambio de cuenta tras confirmar, cache anterior en un job compatible en cola,
  auxiliar original aunque cambie su mapping, rollback de evidencia al desactivar
  la forma, preservación de tipo, rechazo con contabilidad deshabilitada,
  reportes pendientes/pagados, replay y concurrencia sin doble pago.
- Aceptación usa dos comandos: 2/100/500 compatibles tardaron
  1.304,7/1.389,4/1.478,4 ms; 2/500 con forma, 985,5/1.309,4 ms. En los lotes
  de 500 el comando de guardado/aceptación tardó 1.147,4/1.024,1 ms y el de
  lectura 285,8/270,0 ms. Cada detalle usa un comando, veinte muestras por caso;
  p95 de 58,3/21,7/88,2 ms compatibles y 5,0/126,6 ms con forma. El posting
  consulta auxiliares originales en un comando por lote y resuelve todas sus
  formas en uno; los contadores lo verifican sin I/O por trabajador.
- Antes de retirar el congelamiento de mappings del contrato compatible, la
  tanda `payroll-forms-and-shared-posting.trx` tenía 11 aprobadas y dos fallidas
  sólo por latencia de 500: 3.885,4 ms compatible y 2.418,4 ms con forma.
  Sus aserciones funcionales habían pasado. La tanda final corresponde a código
  modificado y no elimina esa evidencia ni demuestra una causa única de toda
  variación de latencia. No se ejecutaron builds ni navegador de pruebas durante
  las mediciones SQL. Estos valores son muestras de aceptación, no un p95 de
  aceptación ni certificación del resto de 13.7.
- Foundation: 115/115 pruebas enfocadas aprobadas. Esquema Release y API/host
  compilados en la tanda SQL/API sin errores ni advertencias. Frontend y
  TypeScript compilan. Lint del incremento no tiene errores y conserva la
  advertencia previa de dependencias de useCallback en mappings; no se suprime.
- Navegador: cuatro escenarios finales aprobados (flujo de transferencia,
  destino incompleto, catálogo vacío, consulta fallida/reintento). Dos formas de
  transferencia muestran sus cuentas y seleccionan el mismo grupo. Confirmar
  conserva efectivo pendiente; historial y actualización explícita distinguen
  confirmado de pagado. Confirmación/reapertura mantienen una lectura de
  opciones/runs/detalle y una del catálogo; actualizar explícitamente añade
  exactamente una de cada recurso necesario. Modal/fecha/estado deshabilitado
  se comprobaron en ventana de 740 px. Las cinco regresiones de recuperación de
  familia de producto también aprobaron; no se modificó ese slice aquí.

La auditoría posterior revisó los diffs de contratos/servicio/stores y reportes,
esquema/proyecto/función/vista, freezer, resolvedor y hechos del processor,
catálogo, cliente API/formulario, pruebas y documentos propietarios. Mantiene
PaymentBatches/PaymentLines, fuentes/jobs/outbox, coordinador y writer de
asientos existentes. No añade motor, cola, job table, dependencia ni ruta de
pago. La elegibilidad reside en PayableRunEmployees y el resultado pagado en
PaymentBatchPostingStatus; UI/reportes no duplican sus reglas. Las cuentas se
validan por readiness, los auxiliares originales por fuente/asiento/tercero,
ordinal, importe y hash concordantes. Un auxiliar ambiguo o ausente no se adivina.

Tenant/sede se verifican en aceptación y posting, con FKs y scope en lecturas;
permisos conservan su propietario de aplicación. Se revisaron bloqueo de run,
unicidad de integrantes, identidad de replay, publicación del outbox y savepoint
que revierte evidencia de cuenta si no hay posting. Las fuentes históricas no
se reescriben; cache derivado previo no cambia la cuenta vigente de salida. El
rollback y el orden de actualización están descritos arriba. No hay fallback
silencioso, consultas por trabajador, segundo GET tras confirmar, polling nuevo
ni consultas duplicadas desde vistas de pago ocultas. Catálogo y detalle tienen
un propietario de carga y los progresos corresponden a sus etapas.

No se declara cerrada la paginación histórica de nómina: GetOptions y el selector
de liquidaciones aún cargan colecciones históricas del slice previo. La nueva
consulta de formas sí pagina en servidor y la captura/detalle del documento
activo se acotan a 500. Migrar historial/selector a consulta paginada del mismo
store y propietario de caché forma parte del cierre pendiente del módulo, junto
con los alcances funcionales de 13.1–13.7 y las mediciones allí exigidas. Tampoco
se habilita un tenant sin configurar sus formas de nómina como supuesto fallback
al catálogo anterior: debe verificarse esa configuración antes del cutover del
frontend. Las APIs compatibles conservan su contrato durante esa transición.


### 13.11 Incremento: auxilio contractual y persistencia del cálculo por lote (2026-11-11)

El contrato incorpora AppliesTransportAllowance. La columna es aditiva, no
nula, con valor inicial true porque conserva la condición que usaba el
calculador anterior; no inventa afiliaciones ni un perfil. Omitir el campo en
un cliente compatible conserva el valor existente al editar y el comportamiento
anterior al crear. La pantalla lo captura explícitamente. Activarlo permite al
calculador evaluar sus condiciones existentes (salario, integral y reglas);
no fuerza un auxilio que esas condiciones excluyan ni cambia las tarifas.

PayrollCalculator mantiene el único cálculo de salario, auxilio, IBC,
aportes y prestaciones. Desactivar el auxilio elimina esa línea automática y su efecto
sobre bases prestacionales que ya lo incorporaban. No cambia el IBC ni
prorratea dos veces salud/pensión. El resultado y el snapshot laboral conservan
la condición utilizada y la versión del contrato. No se copia otra
configuración del tenant.

LoadCalculationData limita la lectura a 501 candidatos para rechazar más de
500 antes de calcular o guardar; no trocea el comando. Las novedades se leen
sólo para los contratos del conjunto elegible. SaveCalculation conserva la
transacción del run y reemplaza resultados editables como antes, pero inserta
trabajadores y partidas por conjunto mediante los TVP
payroll.CalculationEmployeeInput y payroll.CalculationLineInput, sin SQL por
trabajador ni por concepto. Reutiliza el patrón DataTable/Structured existente
en persistencia: ambos son tipos de parámetro del proyecto SQL, sin datos
persistidos, no otros writers ni tablas operativas. Los metadatos calculados
de cada concepto se transportan una sola vez por ID mediante JSON acotado;
no son otro catálogo. No se guarda ni vuelve a copiar un bloque JSON de
partidas por trabajador ni se relee configuración para reconstruir los
metadatos. El join parte del conjunto acotado. El orden de partidas, hashes,
snapshots y totales conserva el contrato existente. Reutiliza el límite técnico existente
de PayrollPaymentLimits; no duplica el tope en el calculador. No añade motor,
job, estado, tabla persistente ni writer.

La versión laboral capturada al cargar entradas se verifica bajo bloqueo en
la inserción del conjunto; un cambio concurrente revierte todo el guardado,
incluido el borrado de resultados previos. Contabilizar vuelve a verificar
esa versión antes de aceptar la fuente. Un borrador calculado sin ese token
requiere recálculo en el corte; fuentes ya aprobadas conservan el replay y sus
snapshots. Esto cubre cambios de contrato, no declara resuelta la invalidez de
todas las novedades/reglas nuevas de 13.2. El guardado de contrato también
rechaza trasladar por ID un contrato de otra sede.

UI reutiliza la respuesta del cálculo en los propietarios actuales de run y
resumen, sin recargar opciones ni volver a pedir el detalle. Editar condiciones
de un contrato sin cambiar su posición en la página reutiliza la respuesta.
Crear o cambiar activación/nombre puede desplazar filas entre páginas y exige
una lectura acotada de la página autoritativa: la respuesta individual no
contiene esa pertenencia/orden. No cambia una revisión para remontar la tabla
ni refresca opciones/runs ajenos. Los selectores ocultos sólo quedan marcados
como stale; no consultan durante el guardado.

Presupuesto para el cálculo completo: 100/500 trabajadores <2/<5 segundos,
lecturas constantes <=8, comandos SQL totales <=11 incluyendo guardado y
detalle de respuesta. La prueba mide el caso completo separado de builds y
navegador; no certifica el resto de 13.7 ni un p95 de aceptación.

Actualización: esquema antes de API/calculador y frontend. No se reescriben
fuentes aprobadas ni artefactos fiscales. Retirar el frontend conserva el dato.
Un calculador anterior desconoce la exclusión contractual: antes de retroceder
binarios deben deshabilitarse cálculo y aceptación; no se deben procesar
contratos con exclusión mediante el binario anterior como rollback supuesto.
No se elimina la columna ni se cambia la decisión del contrato para revertir.

Continúan pendientes selección persistida de trabajadores, edición de días y
conceptos por liquidación, afiliaciones/perfiles y la paginación histórica del
módulo. Este incremento no declara terminada toda la sección 13.

Validación final y auditoría posterior del incremento:

- Foundation: 20/20 pruebas de nómina aprobadas en
  payroll-transport-foundation-final.trx. Incluyen 15/30 días, exclusión
  contractual, elegibilidad salarial/integral, IBC sin auxilio y bases de
  prima/cesantías/vacaciones conservadas por el calculador existente.
- SQL/API: payroll-transport-typed-index-final.trx aprobó 10/10 escenarios.
  Verifica 2/100/500 trabajadores, rechazo de 501 sin resultados, compatibilidad
  del campo omitido, aislamiento por sede y rollback del cálculo anterior ante
  un contrato modificado. Rechaza aprobar un cálculo laboral obsoleto y permite
  recalcularlo. Cantidades, tarifas, bases, importes y metadatos vuelven de SQL
  iguales al resultado del calculador, incluidos nulos y tarifas de ocho decimales.
- Cálculo completo de 2/100/500: 307,4/393,3/1.824,3 ms, con 11 comandos en
  cada tamaño y ocho lecturas, sin consulta por trabajador/concepto. Los tipos
  de entrada conservan claves por trabajador y por trabajador/número de partida.
  No se ejecutaron builds, navegador ni diagnóstico SQL durante esta medición.
- La misma tanda conserva cuenta vigente, auxiliar original, grupos congelados,
  replay, reportes y concurrencia del pago. Para 500, aceptación compatible/con
  forma tardó 1.759,7/1.316,3 ms en dos comandos; detalle p95 de veinte muestras,
  76,9/92,9 ms en un comando. El cruce del auxiliar original materializa su
  agregado por tercero/cuenta una sola vez dentro de la misma lectura del
  processor, evitando recomputarlo por trabajador; no añade otro resolvedor.
- Frontend/TypeScript y esquema Release compilan. Lint: cero errores y una
  advertencia previa de dependencias useCallback en mappings, sin supresión.
  payroll-transport-ui-reviewed.log contiene ocho recorridos de navegador
  aprobados: guardar/reabrir auxilio, teclado, deshabilitados, error transitorio,
  permisos, vacío/reintento, respuesta del cálculo y regresión de pago por grupo.
  Editar y calcular conservan una lectura de cada recurso inicial; reutilizan
  las respuestas sin segundo GET. El resumen no conserva otra copia del detalle.

Las tandas previas no certificaban latencia: se registraron excesos y timeouts
con lotes JSON, y un caso de 500 excedió cinco segundos antes de completar el
contrato/indexación de los TVP. Las capturas y repeticiones instrumentadas fueron
diagnóstico, no mediciones de aceptación. Se retiró esa instrumentación, no se
subieron los límites ni los timeouts y la tanda final usa el código modificado.
Los resultados finales son muestras de esas cargas; no certifican un p95 de
cálculo/aceptación, recálculo individual ni el resto de 13.7.

La auditoría revisó dominio, contratos, store de nómina, processor, esquema/tipos
y proyecto SQL, API cliente, pantalla, pruebas y documentos propietarios. Conserva
PayrollCalculator, SqlPayrollStore y sus tablas/transacciones, y extiende la
lectura del mismo processor contable. No añade motores, colas, jobs, writers ni
catálogos paralelos. La regla de auxilio tiene un propietario; SQL transporta
resultados y UI captura la condición. Se revisaron permisos, tenant/sede,
versiones, locks, unicidad, replay aprobado y rollback. Los errores de una
transacción ya abortada no se enmascaran intentando revertirla otra vez.
No hay I/O por fila, polling ni remount/refetch después del comando; los estados
de progreso siguen la operación activa. Los includes y servidores temporales
de verificación se retiran, conservando las aplicaciones locales existentes.

### 13.12 Incremento: selección persistida por liquidación (2026-11-11)

La lista de candidatos y la carga del calculador comparten la función
`payroll.EligibleRunEmployments`: tenant/sede, contrato activo, periodicidad y
solapamiento contractual con el período. El nuevo listado consulta páginas de
hasta 50, búsqueda general y filtro de contrato combinados, orden estable de
servidor por trabajador, identificación, contrato, inicio o fin. Cuenta los
resultados filtrados en SQL y enriquece únicamente la página. Elegibles y
seleccionados del período son contadores globales separados del resultado
filtrado: controlan Todos/Ninguno y el límite de cálculo, no suman sólo filas
visibles. La periodicidad se obtiene del catálogo contractual existente.

El mismo POST calculate admite `rowVersion`, `selectAll` y hasta 500 cambios
por EmploymentId. Los cambios ganan sobre Todos/Ninguno; sin esos campos se
usa la selección persistida. La UI prepara la selección localmente y la
confirma al calcular, como indica el formulario; no escribe por cada checkbox.
Todos/Ninguno comprende los elegibles del período, incluidas otras páginas y
filtros. Las modificaciones de selección requieren la versión mostrada y el
servidor rechaza IDs no elegibles incluso si se mandan para desmarcarlos.

`payroll.RunEmploymentInputs` es entrada del agregado Runs, independiente de
RunEmployees/RunLines, que siguen siendo resultados reemplazables. La misma
transacción serializable comprueba versión de run y contratos, reemplaza los
resultados y confirma la selección. No crea otro writer, motor ni cola.
SelectionInitialized distingue el comportamiento anterior (todos los elegibles)
de una selección ya confirmada. Su valor inicial falso conserva contratos API
sin body/con body vacío y no reescribe liquidaciones aprobadas. Después del
primer cálculo con esta versión, nuevos elegibles no se incorporan por sorpresa:
el usuario puede seleccionarlos o usar Todos. Desmarcar no desactiva contratos.

El frontend tiene un propietario visible de candidatos, montado únicamente en
Liquidaciones editables con permiso de calcular. Conserva cambios entre páginas
y búsquedas; permite descartarlos y bloquea contabilizar mientras haya selección
pendiente o cálculo en curso. La respuesta del comando actualiza detalle,
resumen y páginas de candidatos conocidas, sin segundo GET. Cambiar campos contractuales
que afectan elegibilidad o identificación invalida candidatos sin consultar vistas ocultas; volver a la lista o Actualizar
nómina es la acción explícita que obtiene la elegibilidad actual. Una selección
preparada con otra versión no se aplica silenciosamente al actualizar.

Presupuesto de aceptación: una llamada SQL por página de 50 sobre 501 contratos,
p95 de veinte muestras de primera y segunda página menor de un segundo. El
cálculo conserva once comandos, ocho lecturas y límite de 500; cargas de 100/500
menores de 2/5 segundos. No se ejecutan builds u otros diagnósticos durante las
mediciones. La incorporación de selección no certifica edición individual de
días/conceptos ni los pendientes de 13.1–13.7.

Cutover: esquema antes de API/frontend; no borrar entradas al retirar la UI.
Un binario anterior ignora la selección confirmada: antes de retroceder API se
deben suspender cálculo/aprobación de borradores con selección para impedir que
reincorpore trabajadores omitidos. Los snapshots aprobados permanecen intactos.

Continúan pendientes edición de días y conceptos por liquidación, huella completa
de novedades/configuración, validación de solapamientos por trabajador, límites
de período de 13.1, afiliaciones/perfiles y paginación histórica. La selección de
este incremento no declara terminado el diseño Mantis completo.

Validación y auditoría posterior de 13.12:

- Esquema Release: cero advertencias/errores. Suite SQL de nómina y grupos de
  pago: diez casos correctos; el flujo vertical se repitió después de añadir
  comprobaciones HTTP de filtros/orden y cálculo con body vacío/sin body.
- Servicio con SQL local: páginas de 50 sobre 501 contratos, veinte muestras
  alternando primera/segunda página, p95 21,18 ms y un comando por consulta.
  Cálculo de 2/100/500 trabajadores: 70,7/192,1/1.211,5 ms y once comandos;
  rechazo de 501 sin escritura. Son muestras locales, no un p95 de cálculo ni
  certificación del resto de 13.7.
- Regresiones de selección: subconjunto desde una plantilla mayor de 500,
  conservar selección al recalcular, retirar/reincorporar, desmarcar sin
  desactivar, errores sin modificar resultados, IDs duplicados/nulos/no
  elegibles y aislamiento de tenant/sede. Una versión antigua del cliente o
  del cálculo no puede sobrescribir los resultados/selección actuales.
- Frontend/TypeScript y diez escenarios de navegador correctos. Incluyen
  teclado/foco, búsqueda/contrato/orden de servidor, páginas, vacío, errores y
  reintentos, selección pendiente conservada al actualizar, descarte explícito,
  estados deshabilitados y lectura sin selector. Calcular no añade GET de
  candidatos/detalle/workspace; aprobar no consulta candidatos ya cerrados.
  La captura final se revisó visualmente: ancho suficiente, encabezado fijo y
  desplazamiento acotado. Lint: cero errores, una advertencia previa en el
  useCallback de configuración contable, fuera del camino modificado.
- Auditoría archivo por archivo de contratos/API, caso de uso, store y función
  de elegibilidad, esquema/proyecto SQL, frontend, pruebas y documentación.
  La selección conserva el único calculador, store/transacción y TVP por lote;
  entradas y resultados tienen responsabilidades separadas. No se añadieron
  jobs, colas, writers, catálogos, estados de aprobación/pago ni reglas de
  cuentas. Aprobar/pagar conserva motores e idempotencia existentes. El
  metadata batch de partidas conserva DianConceptCode de hasta 64 caracteres,
  coherente con catálogo y RunLines. Se revisaron permisos, aislamiento,
  concurrencia, compatibilidad, rollback y cantidad de viajes; sin I/O por
  trabajador, polling ni refetch de mutaciones que ya devuelven el detalle.

Evidencia: work/payroll-selection-schema.log,
work/payroll-selection-isolated-integration.log, work/payroll-selection-http.log,
work/payroll-selection-ui-final-build.log, work/payroll-selection-ui-final-lint.log
 y work/payroll-selection-e2e-final.log; TRX en
 tests/Auraly.ServerSlice.IntegrationTests/TestResults y métricas JSON en
 work/payroll-selection-dotnet/bin/Auraly.ServerSlice.IntegrationTests/release/TestResults.

### 13.13 Incremento en validación: entradas de días y recálculo individual

Este incremento continúa en implementación y auditoría. No declara terminado
ni publicable el contrato completo de la sección 13.

RunEmploymentInputs conserva IsSelected, BaseWorkedDays (salario) y
TransportBaseWorkedDays (auxilio de transporte). Desmarcar conserva
los días introducidos para una futura reincorporación; los resultados siguen
siendo reemplazables. La selección inicial carga 30/15, sin prorratear de forma
oculta un ingreso/retiro. PayrollWorkPeriod calcula el máximo contractual en
base nominal de treinta días y cierra febrero en treinta, conservando las
fechas reales. Por ejemplo, ingreso el 19 de febrero hasta fin de mes admite
doce treintavas. Es una decisión técnica del alcance 30/15, consistente con la
referencia oficial del [Concepto 322391 de 2021](https://www1.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=173389),
que expone ese criterio en el ámbito de empleo público; no amplía el alcance a
regímenes especiales ni convierte esa respuesta en una norma privada general.

La decisión aprobada permite días independientes por concepto: salario 30 y
transporte 25 son entradas válidas. Novedades los edita en la cantidad de cada
partida; los botones de editar/retirar son íconos con etiqueta accesible. Los
30/15 iniciales dependen del contrato y no fuerzan todas las cantidades. Salud
y pensión siguen derivadas de su base; las novedades manuales conservan su
captura existente de días, horas o importe, sin convertirlos en días globales.
No hay botón permanente de recálculo en Novedades: guardar, editar o retirar
ya actualiza al trabajador. Sólo un fallo de ese cálculo ofrece Reintentar
cálculo, sobre el mismo comando y sin volver a guardar la novedad. El error
de presentación se conserva por liquidación/trabajador y se retira al recibir
su resultado correcto; no crea estado durable ni otro proceso. El recálculo
general permanece en Liquidaciones.
La grilla de Liquidaciones usa un mínimo de columna cero también antes del
breakpoint de escritorio. En móvil sus tarjetas y la selección se contienen
en el viewport; sólo la tabla conserva desplazamiento horizontal. Búsqueda,
Todos/Ninguno, paginación y cálculo deben permanecer accesibles a 390 px,
sin cambiar el propietario de consultas ni el comportamiento del comando.
La regresión puntual `payroll-mobile-selection-e2e.log` aprobó cuatro casos:
el mismo recorrido paginado a 1440/390 px, lectura y falta de elegibles. Conserva
los conteos de consultas y el cálculo sin refetch. La revisión real a 390 px
redujo la selección de 850,6 a 301,6 px; verificó Ninguno/Todos, cálculo y
transporte 24→25 sin alterar salario 30. Build y lint aprobados. No añade I/O
ni cambia una ruta de servidor que necesite otro benchmark.

Editar días finales usa PayrollCalculator.WithConceptWorkedDays: actualiza
solo la base del concepto indicado, sumando las ausencias identificadas.
Recalcular vuelve a restarlas una sola vez en cada base independiente. Cambiar
salario conserva transporte y viceversa. El ConceptId debe corresponder a los
roles canónicos BasicSalary/TransportAllowance; el calculador rechaza otros
conceptos y transporte no elegible. WithFinalWorkedDays conserva el contrato
anterior como edición de salario. La entrada admite cuatro decimales, como su columna;
una base no representable no se redondea silenciosamente. Las ausencias de
días no pueden añadir simultáneamente una deducción monetaria. Su partida de
valor cero conserva cantidad y origen para revisión y consumo. La contabilidad
ya excluye importes cero en su proyección existente.
Salario/transporte de valor cero conservan sus filas operativas y metadatos
ConceptWorkedDays para poder restaurar días. Los ajustes no exponen esos
editores y conservan la exclusión anterior de partidas automáticas cero.
El cálculo completo también rechaza días finales no representables con cuatro
decimales; no delega un redondeo silencioso al TVP o a la columna SQL.

Los ingresos fijos/por cantidad se resuelven antes de porcentajes de ingresos;
estos últimos comparten esa base sin componerse entre sí. Después se calculan
las deducciones sobre los ingresos completos. La protección del mínimo usa los
importes efectivamente calculados y elimina el cálculo aproximado paralelo.
El resultado no depende del orden de identidad de ingresos/deducciones.

El mismo POST calculate incorpora WorkedDaysChanges con ConceptId opcional
(omitido significa salario), hasta 500 empleados distintos y una edición por
empleado en el comando. Una edición sin cambios
de selección opera únicamente sobre los trabajadores indicados, previamente
liquidados, y conserva las demás entradas/resultados. El TVP de empleados V2
es aditivo; se conserva el tipo V1 durante el cutover. El lote existente también
reemplaza las partidas, sin un comando de borrado separado. Totales y huella
del run se agregan sobre sus hasta 500 resultados; el guardado devuelve dentro
de su transacción el trabajador afectado y los totales, con EmployeeCount e
IsPartialCalculation. El cálculo completo conserva la respuesta completa y
los clientes anteriores siguen enviando body vacío o sin body.
Los días de transporte y los metadatos de edición viajan como JSON acotado en
ese mismo comando SQL por conjunto; no crean otro TVP, endpoint ni escritura
por trabajador. El snapshot del resultado incorpora ConceptWorkedDays; UI
habilita los editores desde ese resultado autoritativo y reutiliza la respuesta.

Despliegue: la columna nullable y su índice se publican mediante el DACPAC
antes de API/web. No se rellenan entradas ni snapshots históricos. Un recálculo
explícito toma la base salarial previa cuando aún no hay base de transporte y
guarda ambas de forma independiente. Los aprobados siguen inmutables. Rollback:
conservar columna y datos; una vez usados días distintos, no volver al calculador
anterior que los colapsaría. Corregir mediante roll-forward o impedir nuevas
ediciones mientras se repone la versión, sin recalcular ni recontabilizar historia.
Presupuesto del incremento: máximo 500 empleados, diez comandos SQL por cálculo,
una lectura SQL del detalle, p95 individual <1 s y cálculo de 500 <5 s, medidos
sin otra carga diagnóstica. No añade GET posterior, polling ni I/O por fila.

ReadRunAsync es la lectura compartida del detalle completo y de la respuesta
acotada. Resuelve los IDs autorizados antes de proyectar trabajadores/partidas,
usando los índices existentes y un plan por el alcance solicitado. No devuelve
las líneas de los demás trabajadores en la respuesta individual.
La organización física de RunEmployees usa la clave única (run, contrato), y
RunLines la clave única (resultado del trabajador, número de línea), como índices
clustered. Sus IDs conservan las mismas PK únicas no clustered y todas sus FK.
No agrega índices duplicados ni modifica identidades o importes; busca reducir
las lecturas dispersas observadas al guardar/leer cientos de resultados. La
migración de ambas estructuras debe verificarse con filas y referencias existentes.

ApplicableCalculationConcepts es el propietario de conceptos vigentes para el
run. El cálculo carga los automáticos y los usados por novedades seleccionadas;
no hidrata el catálogo ajeno al cálculo. CalculationContextVersions y
CalculationNoveltyVersions comparten el conjunto versionado con la aceptación:
configuración de cálculo/conceptos automáticos, y por trabajador novedad,
concepto manual y autorización. Empleo, días/fechas y tarifa de riesgo se
comprueban además en su origen. Una novedad nueva no referenciada invalida el
resultado. Los locks serializables protegen la validación y la aceptación.

Los datos laborales mantienen SnapshotVersion=1 con campos aditivos. El lector
fiscal existente conserva su contrato. Los borradores anteriores necesitan
recálculo completo para incorporar entradas/huellas; un replay ya aprobado
conserva su respuesta y efectos. No se incluyen cuentas, mappings ni formas
de pago en la huella o en una copia de configuración: sus reglas de resolución
vigente al contabilizar permanecen en sus propietarios actuales.

La consolidación electrónica selecciona una sola vez los resultados aprobados
con algún importe positivo, dentro del alcance del período. Un trabajador con
devengados, deducciones, aportes y provisiones todos en cero no genera documento
ni consume cupo, aunque su resultado operativo conserve novedades de valor cero.
No descarta silenciosamente ajustes con aportes/provisiones positivos: conserva
su evaluación en el pipeline y los gates fiscales pendientes. La proyección monetaria electrónica
excluye partidas de valor cero, manteniendo el comportamiento anterior a su
conservación operativa. Esto no sustituye el gate de clasificación oficial de
ausencias/licencias de 13.4 ni certifica el corte general de 13.6.

La pantalla comparte una única mutación de cálculo por tenant/sede, utilizada
por Liquidaciones y Novedades. El estado en curso bloquea otra confirmación,
incluso al cambiar de pestaña; no añade cola ni reintento. La respuesta parcial
se integra con el detalle autoritativo ya cargado, conservando compañeros y
actualizando resumen/versiones. La lectura del detalle sólo permanece activa
cuando es visible o se abre el formulario de pago. Novedades reúne la revisión
por trabajador y las autorizaciones; no crea una sección financiera paralela.
Actualizar explícitamente marca el detalle oculto como desactualizado sin
consultarlo: su único propietario lo lee una vez al volver a mostrarlo.

Presupuesto: recálculo individual p95 menor de un segundo con 500 trabajadores,
diez comandos y ocho comandos de lectura, sin I/O por elemento ni GET posterior
al comando. Hubo tandas con 508 ms y otras superiores a 30 s. Se localizaron
pausas tanto en escrituras como en consumo del detalle, con lecturas físicas y
presión de memoria; las capturas con XE son diagnóstico, no certificación. No se
ampliaron los límites de tiempo ni se atribuyó toda variación a una sola causa.

La corrección de novedades manuales se completa por la captura y el lector
acotado de 13.2; las pruebas de calendario/solapamientos conservan sus propios
escenarios. Las cifras históricas anteriores no certifican el rendimiento de
un lector modificado: su verificación de 500 trabajadores y recálculo individual
debe repetirse para ese cambio puntual. Continúan abiertos los gates generales
de perfiles/afiliaciones, centros por línea e historiales descritos en 13.4 y 13.7.

Evidencia y auditoría del incremento, sin declarar cerrado el diseño completo:

- Verificación puntual de días por concepto e íconos (16-11-2026):
  `payroll-concept-days-foundation.trx` (32 aprobadas),
  `payroll-concept-days-sql.trx` (5 aprobadas: flujo completo y lotes
  2/100/500/501), `payroll-concept-days-e2e.log` (17 aprobadas), build y lint.
  Con 500 trabajadores, el cálculo completo tomó 1.585 ms y el p95 de veinte
  recálculos individuales 660 ms, conservando diez comandos y una lectura SQL
  del detalle. Se verificaron persistencia, ausencias, rechazo de edición de
  salud, compañeros intactos, selección y exclusión fiscal/contable de ceros.
  `payroll-concept-days-upgrade-proof.json` conserva la misma huella de entradas,
  resultados y partidas previos antes, después y al repetir el despliegue local.
  `payroll-concept-days-local.png` muestra salario 30/transporte 25 después de
  recargar, sin modificar el cálculo de otro trabajador.
  Tras retirar el botón permanente se ejecutaron sólo nueve escenarios
  afectados (`payroll-automatic-novelties-final-e2e.log`), todos aprobados,
  además de build/lint. Verifican que Reintentar cálculo aparece sólo después
  de un fallo, conserva el error al volver al trabajador, desaparece al
  completarse y no guarda dos veces la novedad. La vista final con datos
  persistidos está en `payroll-novelties-final-local.png`.
- La presentación de grupos antes del selector extiende exclusivamente
  PaymentForm y el lector PayableRunEmployees existente. No cambia aceptación
  ni posting. `payroll-visible-payment-groups-e2e.log` contiene seis casos
  puntuales aprobados: dos lotes en la misma ventana, identidades distintas,
  bloqueo concurrente/cierre, captura del otro grupo conservada, cero lecturas
  ocultas y dos páginas de formas (una por tipo), error/reintento de catálogo,
  catálogo vacío y destino incompleto. No vuelve a ejecutar la regresión
  completa; conserva la base anterior y los checks del camino afectado.
  El recorrido real en navegador local creó/liquidó/contabilizó una nómina
  sintética de enero de 2027, mostró ambos grupos contraídos y registró cada
  uno desde la misma ventana. La falta inicial del período 2027 bloqueó el
  pago correctamente; crear ese período local y reintentar la misma causación
  por Trazabilidad financiera la dejó Posted. No se reescribió el trabajo ni
  se eludió la validación. `payroll-payment-groups-local-ledger-proof.json`
  comprueba dos trabajadores pagados exactamente una vez, cero pendientes y
  tres asientos balanceados. No se envió dinero ni nómina electrónica.
- `payroll-worked-days-foundation-audit.trx`: 1.195 pruebas aprobadas, incluidas
  dominio, arquitectura y regresiones comerciales/fiscales. La suite tardó
  4 min 23 s; las dos verificaciones arquitectónicas de proyectos consumieron
  la mayor parte. No requirió modificar ni desactivar esas verificaciones.
- `payroll-worked-days-ui-audit-build.log`: compilación Next/TypeScript correcta.
  Lint sin errores, con una advertencia anterior en el callback de mappings.
  `payroll-worked-days-e2e-final.log`: 12 escenarios aprobados. Cubren los días,
  error/reintento sin pérdida de captura, campo vacío, bloqueo transversal del
  cálculo, compañeros conservados, historial en lectura, selección paginada,
  auxilio y grupos de pago. Revisión visual de la pantalla de Novedades hecha.
- Los selectores nuevos usan las liquidaciones del workspace autorizado y las
  naturalezas de CatalogOptions; no crean un catálogo o propietario de carga.
  La paginación del historial general continúa siendo un gate de 13.7.
- Cálculo y escritura conservan PayrollCalculator/SqlPayrollStore y sus tablas,
  sin otro motor, writer, cola, estado durable ni endpoint de recálculo. Se
  revisaron alcance tenant/sede, versiones, precisión, respuesta transaccional,
  huellas de entradas y compatibilidad de los campos aditivos. Replay aprobado
  conserva efectos; cuentas y formas siguen resolviéndose al contabilizar.
  Se corrigió además la republicación de una aprobación ya terminada: el replay
  consulta la evidencia PublishedAt existente, igual que el pago, y omite una
  nueva solicitud/actualización cuando la señal ya está publicada. Conserva el
  camino existente de una publicación que aún no hubiera terminado.
- Columnas y tipos de entrada son aditivos. El cambio de índices clustered
  reconstruye la distribución física de las dos tablas; su despliegue requiere
  evaluar volumen, log y ventana de bloqueo. No se ha publicado. El cutover
  debe coordinar frontend/API: un writer
  anterior no conoce los días editables. Un rollback conserva esas entradas y
  no debe recalcularlas mediante un binario anterior ni retirar columnas/tipos.
- La consolidación fiscal todavía conserva sus reservas/inserciones por
  trabajador dentro de la transacción heredada. La emisión masiva y su número
  de viajes requieren completar el gate fiscal de 13.6; el filtro de resultados
  vacíos no certifica ese camino ni agrega otra ruta de emisión.
- `payroll-worked-days-layout-reviewed.trx`: migración aprobada desde las claves
  clustered anteriores, con datos existentes. Compara íntegramente encabezado,
  entradas, resultados, partidas, lote y líneas de pago, incluidas identidades,
  importes, snapshots, rowversions y huellas. Verifica cuatro índices y las FK
  esperadas habilitadas/confiables. Se ejecuta aparte de los benchmarks porque
  publicar el DACPAC inicia un proceso SqlPackage.
- La última tanda sin XE, `payroll-worked-days-layout-certification.trx`, aprobó
  8 de 10 escenarios: todos los de cálculo, HTTP, selección, novedades y el
  recorrido contable/fiscal con un trabajador en cero. Cálculo de 500: 798 ms;
  recálculo individual de 20 muestras: p95 91,3 ms, diez comandos. No certifica
  aún todo el pago: las aceptaciones de 500 sin forma del tenant (4.413 ms) y
  dos con forma del tenant (2.814 ms) excedieron los dos segundos. Las otras
  tres variantes de pago pasaron. Los límites permanecen activos.
- Durante la comparación de esquema se observó RESOURCE_SEMAPHORE: mínimo de
  13.312 KB frente a 12.840 KB disponibles en el pool de trabajo, junto con la
  señal process_physical_memory_low. La espera se resolvió posteriormente y la
  migración pasó sin modificar configuración global. Se propuso una reserva
  temporal y reversible para investigar la variabilidad; no está autorizada ni
  ejecutada a esta fecha. La entrega no declara resuelto ese bloqueo de calidad.


### Propuestas de fecha en UI según la sede (2026-11-12)

Los valores iniciales de períodos/reportes, contrato, concepto, reglas,
autorización, novedad y mes electrónico toman el día del contexto de sede
mediante useBusinessDates, sin leer otro recurso ni cambiar el calendario
contractual, cálculo o vigencias. DatePicker usa ese mismo día al elegir hoy.
Las fechas elegidas conservan su contrato DateOnly y no se reescriben al
presentar. Pago conserva captura explícita y el origen temporal fiscal de
13.10; no se infiere un desembolso por cambiar un valor inicial. La regresión
comprueba navegador en otra zona, dos sedes con días diferentes y fecha de
calendario existente en sede pero omitida en la zona del navegador.

### Disponibilidad del auxiliar original al pagar (2026-11-12)

La auditoría contable reprodujo un pago marcado Posted/IsPaid aunque el
auxiliar de la causación original estaba inactivo. El vínculo por fuente,
hash, ordinal, tercero e importe era correcto, pero esa consulta no comprobaba
su disponibilidad. El caso rojo `payroll-original-availability-reproduction`
observó IsPaid=true cuando esperaba el trabajo pendiente.

La misma consulta por lote verifica ahora cuenta del tenant, alcance de sede,
IsActive y AllowsPosting, con bloqueo en la PK hasta terminar la transacción.
No aplica el mapping actual de NetPayrollPayable para cancelar una causación
anterior, ni cambia su función/clasificación. La ausencia de un auxiliar
disponible usa PayrollOriginalPayableMissing y el estado pendiente existentes.
El savepoint revierte la evidencia aplicada de forma/cuenta: no queda un
asiento ni un pago contabilizado. No se añadió tabla, flujo, estado ni writer.

El escenario focalizado pasó después del arreglo: desactiva únicamente datos
de la base aislada, comprueba pendiente, AccountId nulo y cero asientos,
restaura la disponibilidad y reintenta por el endpoint canónico. Se paga una
sola vez en el auxiliar original y se conserva el grupo de efectivo pendiente.
No existe ni se agregó un comando HTTP de retiro de cuentas; la simulación
se limita a la fixture y restaura su valor original en finally.

La captura de un nuevo mapping de NetPayrollPayable usa función Payable según
la política de propósitos compartida. La cuenta alternativa de la prueba se
configuró coherentemente; no se cambió el criterio que exige cancelar el
auxiliar de la causación original. Los auxiliares/porcentajes/contratos de
cuentas históricas no se normalizan para superar una validación.

Presupuesto: un solo comando para todos los auxiliares del lote, sin lectura
por trabajador ni refetch/polling. El caso de dos trabajadores añade un
reintento bloqueado deliberado; verifica dos lecturas del original y tres
resoluciones de forma en sus tres intentos. Cada intento sigue realizando una
sola resolución de cada recurso, y los lotes de 500 mantienen su recorrido.
Compiló backend sin advertencias ni errores; la repetición transversal se
registra en la auditoría contable al terminar. Volver al binario anterior
reintroduciría el uso de auxiliares no disponibles; un rollback conserva los
documentos/asientos y requiere detener ese comando hasta corregir la versión.

La repetición `accounting-authority-reviewed-final` pasó 292/292, incluidas
las cinco variantes de grupos y la regresión vertical de nómina. Para 500
trabajadores, aceptar con/sin forma tardó 130,91/282,89 ms en dos comandos;
la lectura obtuvo p95 86,81/106,39 ms en veinte muestras. El recálculo
individual sobre 500 dio p95 157,76 ms, veinte muestras y diez comandos.
Esta evidencia satisface esos gates en la repetición actual sin reserva de
memoria, cambios globales de SQL ni modificación de sus umbrales. No certifica
los campos, perfiles, novedades o gates fiscales todavía pendientes de 13.
