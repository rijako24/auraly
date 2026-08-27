# Decisión: nómina electrónica integrada

Fecha: 2026-08-26
Estado: aprobada para implementación incremental

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
concepto. Un pago registra lote y líneas contra netos aprobados; no cambia el
cálculo. La primera entrega confirma el lote completo de una liquidación y
aplica una unicidad por trabajador liquidado para impedir doble pago. El medio
de pago resuelve desde catálogo su categoría contable de salida (`Bank` o
`Cash`); no existe un `switch` de negocio en la aplicación.

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

La navegación de Nómina separa liquidaciones, relaciones laborales, novedades y
descuentos, pagos, documentos electrónicos, reportes y configuración. La pestaña
Empleado de Terceros consulta la relación laboral por `PartyId` y enlaza a Nómina;
no permite editar ni replica salario o contrato en `dbo.Employees`.

Los reportes de nómina son una extensión nativa de Reporting, no consultas
embebidas en la página. El catálogo versionable vive en
`reporting.PayrollReportDefinitions`; `PayrollReportingService` valida permiso,
alcance y rango, y `SqlPayrollReportingStore` es el único adaptador autorizado
para consultar snapshots aprobados, pagos confirmados y estados del motor fiscal.
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
