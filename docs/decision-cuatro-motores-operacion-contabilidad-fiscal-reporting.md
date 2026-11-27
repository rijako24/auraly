# Decisión: cuatro motores y propiedad única de efectos

**Estado:** vigente y obligatoria  
**Fecha:** 22 de agosto de 2026  
**Alcance:** Auraly Commerce Cloud y On-Premise

## 1. Prevalencia

Esta decisión reemplaza cualquier regla anterior que asigne cartera, pagos,
aplicaciones, caja financiera, asientos o consolidados al motor documental.
En particular prevalece sobre las secciones de efectos intrínsecos de:

- `decision-motor-documental-ordenado-y-efectos-intrinsecos.md`;
- `decision-motor-documentos-orden-inventario-contabilidad.md`.

Se conservan de esas decisiones la aceptación durable, el payload inmutable,
el orden por negocio, la idempotencia, el kardex canónico y la recuperación.

## 2. Topología obligatoria

El ciclo documental de Auraly tiene exactamente cuatro colas principales y
cuatro propietarios de ejecución:

| Cola | Motor | Única responsabilidad |
| --- | --- | --- |
| `auraly-document-processing` | Operación | aplicar exclusivamente inventario, kardex y costo físico de los documentos que tengan ese efecto, y guardar sus señales derivadas en outbox |
| `auraly-accounting-processing` | Finanzas y contabilidad | todos los submayores financieros y, cuando esté activa, el libro mayor |
| `auraly-fiscal-processing` | Fiscal | generar el artefacto fiscal y transmitirlo a DIAN mediante las etapas `Generation` y `Submission` |
| `auraly-sales-reporting` | Reporting | construir y reconstruir hechos y consolidados de ventas |

Las colas de retry y dead-letter son infraestructura de las cuatro anteriores;
no son motores de negocio adicionales. Generación y envío DIAN son etapas del
mismo motor fiscal, aunque puedan escalarse con consumidores distintos.

La descarga de clientes de integraciones externas es una operación explícita y
síncrona: actualiza el mapeo de origen y se concilia de inmediato o mediante la
operación manual por lote. No tiene cola, outbox, recibo ni worker de fondo. El
bot resuelve la identidad desde las tablas canónicas `Customers`, `Parties` y
`PartyContacts`; el mapeo externo solo aporta los identificadores del proveedor.

La infraestructura de campañas pertenece a otro contexto delimitado y no puede
asumir inventario, cartera, contabilidad, fiscal ni reporting.

Un documento exclusivamente financiero no se publica en la cola operacional.
Notas débito/crédito de CxC o CxP, pagos sin efecto físico, comprobantes
manuales, saldos iniciales y reclasificaciones van directamente a la cola
financiera-contable después de que la API guarda atómicamente su fuente y su
único job. No crean `DocumentProcessingJobs`, no avanzan
`BusinessProcessingCursors` y no usan payloads propietarios de inventario. Los
flujos heredados que aún aceptan recaudos mediante el motor documental deben
converger en ese mismo job; no pueden escribir el submayor desde el handler.

Las cuatro colas se particionan y ordenan por negocio. En Azure Service Bus es
obligatorio `SessionId = BusinessId`, con una ejecución secuencial dentro de la
sesión y sesiones de negocios distintos en paralelo. Esa es la implementación
SaaS que permite paralelismo entre negocios. En on-premise RabbitMQ opera con
una sola instancia consumidora por cola y `prefetch = 1`; así conserva el orden,
la entrega durable y la idempotencia sin requerir shards. No se pueden levantar
consumidores concurrentes para una misma cola on-premise. Si en el futuro se
habilita escalamiento horizontal, primero deberá implementarse y probarse el
enrutamiento determinístico por `BusinessId` a shards durables; un lock en
memoria no sustituye esa regla.

## 3. Frontera operacional

El motor operacional recibe solamente documentos con efecto físico y puede
escribir:

- el sobre fuente inmutable mínimo necesario para reproducir el efecto físico;
- estado operacional del efecto de inventario y vínculos con su documento;
- kardex, saldo y valoración de inventario mediante el writer canónico;
- outbox/señales hacia contabilidad, fiscal y reporting.

No puede escribir ni actualizar:

- `Receivables`, `Payables` ni sus transacciones;
- aplicaciones de pagos, devoluciones, anticipos o créditos;
- saldos de cliente o proveedor;
- movimientos financieros de sesión o caja;
- `AccountingEntries` o `AccountingEntryLines`;
- hechos ni consolidados de reporting.

Que el documento incluya medios de pago no convierte esos renglones declarados
en un submayor. Son hechos fuente; sus efectos financieros pertenecen al motor
contable.

## 4. Motor financiero-contable único

Existe una sola cola, un solo contrato de señal, una política canónica de tipos
financieros y un solo trabajo durable por documento fuente. No se crean workers
separados para CxC, CxP, pagos, notas, caja o libro mayor. La cartera comercial
puede operar sin que el tenant active el libro mayor, pero conserva este mismo
propietario, tablas, orden e idempotencia.

Dentro de una transacción serializable, el motor:

1. bloquea el trabajo y verifica el hash del payload inmutable;
2. aplica CxC/CxP, pagos, aplicaciones, créditos o anticipos;
3. cuando el trabajo congeló `AccountingEntryRequired=1`, crea y valida el
   comprobante de partida doble;
4. marca el trabajo `Posted` si incluyó asiento o `CommercialEffectsApplied` si
   era únicamente seguimiento comercial;
5. confirma todos los efectos que correspondan al modo congelado o ninguno.

El orden financiero usa la misma secuencia documental de origen por negocio.
Una entrega duplicada no reaplica submayor ni asiento. Al aceptar cada fuente se
congela si requiere asiento: únicamente un tenant explícitamente `Ready` y una
operación en o después de `EffectiveFrom` usan `AccountingEntryRequired=1`.
Sin contabilidad activa solo se crea trabajo para efectos comerciales que deben
subsistir —apertura y recaudo de CxC y devolución aplicada a esa cartera— y el
trabajo no se convierte retroactivamente al activar contabilidad. Los demás
documentos no crean fuente ni trabajo financiero-contable. Después de activar,
una inconsistencia sobrevenida de período o mapeo deja el único trabajo ya
creado en estado pendiente de configuración; no crea un trabajo lateral ni
revierte inventario ya confirmado.

Los documentos manuales usan el mismo carril contable y nunca el operacional:

- ajuste de cuenta: `Receivable/Payable × Increase/Decrease`;
- nota débito o crédito derivada de ese ajuste;
- saldo inicial de tercero;
- comprobante manual, reversión o reclasificación.

Un comprobante manual puro no altera submayores salvo que su contrato sea un
ajuste de cuenta explícito. Todo asiento contabilizado es inmutable. El job
contable es autosuficiente: no tiene FK hacia `DocumentProcessingJobs` y su
orden no se obtiene alterando el cursor de inventario.

El alcance mínimo completo —plan, períodos, categorías, centros de costo,
terceros, impuestos, retenciones, libros, estados básicos, base fiscal y
exógena versionada— sigue siendo obligatorio conforme a
`decision-contabilidad-minima-colombia-y-cumplimiento.md`. Esta topología cambia
el propietario de la ejecución; no recorta aquel alcance funcional.

## 5. Motor fiscal

Operación conserva únicamente el snapshot fiscal inmutable y solicita la etapa
`Generation`. El motor fiscal construye, valida y firma el artefacto. Solo tras
generarlo publica la etapa `Submission`, que transmite y consulta a DIAN.

Operación y contabilidad nunca llaman a DIAN. Un retry fiscal no repite
inventario, submayores, contabilidad ni reporting.

## 6. Motor de reporting

Reporting consume la señal del documento operacional ya confirmado. No forma
parte del commit operacional y jamás es fuente para escrituras ni decisiones
definitivas de inventario, cartera, fiscal o contabilidad. Como excepción de
lectura, los sugeridos no vinculantes de compra y producción consumen la
rotación diaria ya proyectada por Reporting. La confirmación de una operación
vuelve a validar existencias en el motor de operaciones; una proyección atrasada
no autoriza movimientos físicos ni asientos.

Para `ServiceInvoice` aplica la decisión específica de servicios online,
conciliada expresamente con el usuario: el writer canónico de emisión acepta
su fuente JSON inmutable en el mismo `SalesReportingJobs` durante el commit de
la factura. Después se activa el coordinador existente. No hay trabajo
operacional, inventario ni segundo motor. `ServiceSalesReportLineFacts`
conserva el grano de servicio separado del producto; su costo no está modelado
y no se presenta como cero ni se calcula un margen ficticio.

Para ventas mantiene una proyección física versionada con:

- documento y snapshots de cliente/vendedor/bodega;
- hechos firmados de líneas de venta y devolución;
- impuestos y medios de pago declarados;
- costo reconocido congelado;
- totales diarios conciliables.

La identidad de producto, cantidades, impuestos e importes de
cada línea provienen de la fuente inmutable validada por hash. Las líneas
operativas aportan snapshots de atribución y costo cuando existen; su ausencia
no elimina una venta que tiene fuente verificable. Un producto distinto en la
misma partida o fuera del tenant se rechaza. El costo se reconoce exclusivamente
desde las evidencias congeladas/ledger existentes; sin ellas queda desconocido,
no como un costo cero conocido. Proyectar o reconstruir no crea las líneas
operativas ausentes ni repara inventario, cartera, contabilidad o DIAN.

Las correcciones de medios confirmadas en conciliación de caja o despacho
son fuentes financieras inmutables del mismo motor de Reporting. Su aceptación
se registra en `SalesReportingJobs` junto al trabajo financiero, pero la señal
se publica después de su confirmación financiera. Reporting reemplaza únicamente
los hechos de pago de las ventas afectadas por los medios efectivos confirmados;
no agrega otra venta ni modifica impuestos, costos, inventario o documentos fiscales.
El efecto neto de cambiar efectivo por transferencia es restar del primero y sumar
al segundo. Las fuentes originales y la corrección conservan su evidencia histórica.

La fecha analítica conserva la fecha local congelada de la venta. Las entregas
parciales o no entregadas reflejan solo el recaudo efectivo; la devolución conserva
su propia proyección. Los cobros de cartera y anticipos no se convierten en ventas.
La proyección original consulta la misma evidencia confirmada si llega después de
la corrección. Los reintentos terminados devuelven el estado ya proyectado; el rebuild
explícito recupera las fuentes financieras históricas mediante el writer existente.
Las reclasificaciones agregadas sin factura identificada conservan su alcance de caja
y no inventan una distribución por factura.

La actualización de medios materializa primero las ventas solicitadas dentro del
mismo comando SQL y sólo después evalúa sus correcciones de caja o despacho.
Esto impide que el optimizador extienda los predicados correlacionados al
histórico completo de ventas. Si ninguna de esas ventas tiene una corrección
confirmada, devuelve deltas cero antes de resolver los hechos financieros. No
agrega consultas, tablas persistidas ni otro writer. La regresión con 5.000 ventas
ajenas verifica dos comandos, lecturas puntuales del documento y el presupuesto
local de dos segundos; los casos de corrección conservan su proyección y replay.

El despliegue debe ejecutar el rebuild canónico de Reporting después de migrar para
actualizar los medios de correcciones históricas. El rollback de la aplicación no
revierte asientos ni recaudos: el modelo de lectura se reconstruye con la versión
a restaurar. No se reprocesan operaciones ni se alteran fuentes financieras.

La migración de fuentes operacionales históricas conserva el vínculo al trabajo
operacional existente. Antes de planificar el DACPAC, completa únicamente los
vínculos ausentes cuya sede, documento, tipo y hash coinciden con el payload
canónico y su trabajo. No cambia estados, importes, hashes ni payloads y es
idempotente. Si falta un origen verificable, detiene el despliegue para revisar
el dato; no crea una fuente ni vuelve a confirmar el documento. La restricción
de la cola exige una referencia operacional o un JSON propio válido, excluyendo
el caso de ambos nulos. El backfill posterior usa esa misma referencia al crear
trabajos históricos faltantes. El rollback conserva estos vínculos y los hechos
pueden reconstruirse con el motor canónico, sin revertir movimientos financieros.
La retirada de tipos operacionales obsoletos conserva `ServiceInvoice`, que ya
pertenece a esta cola y tiene su propia fuente inmutable.

La granularidad inicial es híbrida y deliberadamente mínima:

| Proyección | Grano | Uso |
| --- | --- | --- |
| `SalesReportDailyTotals` | negocio + fecha local + moneda | tablero, tendencias y comparación sin recorrer documentos |
| `SalesReportDailyDimensionTotals` | negocio + fecha + dimensión + valor + moneda | rankings simples por cliente, vendedor, proveedor, producto, categoría y bodega |
| `SalesReportDocuments` | una identidad analítica mínima por venta | paginación y salto al comprobante; no replica líneas, payload UBL ni estado operacional mutable |
| `SalesReportLineFacts` | una línea firmada mínima por venta/devolución | filtros cruzados, costo histórico, rentabilidad y explicación producto a producto |
| hechos de impuesto y pago | documento + tarifa/medio | análisis fiscal comercial y formas de pago declaradas |

La fila documental no sustituye ni duplica la factura transaccional: conserva
solo claves, fecha, snapshots de dimensiones y totales congelados necesarios
para que reporting no dependa de joins operacionales. Al solicitar el soporte
completo, permisos, XML o trazabilidad operacional se abre el documento fuente.
Eliminar esa fila obligaría a consultar facturación para cada listado y rompería
el desacoplamiento; copiar sus líneas completas produciría la duplicación que
esta decisión prohíbe.

Las fórmulas canónicas son `venta neta = venta - devolución`, `impuesto neto =
impuesto generado - impuesto devuelto`, `costo neto = costo reconocido - costo
revertido` y `utilidad bruta = base neta - costo neto`. Venta es positiva y
devolución negativa en los hechos; los acumulados guardan devoluciones como
magnitud positiva y el neto ya restado. La fecha es la fecha local del negocio,
la moneda inicial es COP y nombres/categorías/proveedor son snapshots históricos.

Objetivos iniciales con datos calientes e índices vigentes: p95 menor a 300 ms
para resumen diario de hasta 24 meses, menor a 600 ms para un ranking simple y
menor a 1.5 s para filtros cruzados paginados. Un benchmark reproducible y un
dataset dorado de venta, devolución total/parcial, múltiples tarifas y múltiples
dimensiones son parte de la puerta de liberación.

La transacción de proyección es idempotente por documento y línea fuente. El
motor tiene exactamente una tabla durable de procesamiento,
`reporting.SalesReportingJobs`, con identidad de mensaje/documento, hash,
estado, intentos, lease, error y tiempos. Esa tabla es el inbox del motor y se
actualiza en la misma transacción que hechos, consolidados y checkpoint. Las
claves únicas de proyección son una defensa adicional; no sustituyen el job. No
se crea una tabla por reporte ni por dimensión.

Debe existir una operación explícita de rebuild que borre y regenere solo la
proyección seleccionada para un negocio y versión, tomando los payloads
inmutables en orden. Rebuild no publica documentos, no modifica operación y no
contabiliza.

### Cuándo crear una proyección

Una proyección física se justifica si al menos una condición se demuestra con
medición:

- alto volumen o agregaciones repetidas costosas;
- unión de múltiples documentos para una métrica frecuente;
- necesidad de snapshot histórico de dimensiones;
- objetivo p95 incumplido con índices transaccionales razonables.

Reportes pequeños consultan tablas propietarias con índices, paginación y
filtros. No se consolidan preventivamente todos los módulos.

### Contrato reproducible

Toda proyección futura debe documentar:

1. evento y versión fuente;
2. propietario y claves de idempotencia;
3. granularidad de cada tabla;
4. signos y fórmulas de métricas;
5. zona horaria y moneda;
6. snapshots frente a dimensiones vivas;
7. índices y patrón de consultas;
8. política de retry/dead-letter;
9. rebuild, reconciliación y versión;
10. dataset dorado y objetivos p95.

## 7. Publicación y consistencia

El consumidor operacional confirma su mensaje únicamente después de:

1. completar la transacción operacional;
2. guardar, dentro de esa misma transacción, una sola fila outbox por cada señal
   aplicable a fiscal, contabilidad y reporting.

Un despachador de infraestructura publica cada fila outbox con `MessageId`
estable y la marca enviada de forma idempotente. No se intenta una transacción
distribuida SQL + broker. Una caída después del commit y antes de publicar deja
la outbox pendiente; una caída después de publicar y antes de marcarla causa una
entrega duplicada segura en el motor destino.

Los consumidores toleran entrega al menos una vez. Cada motor es independiente:
un atraso de reporting no bloquea una venta; un atraso DIAN no repite inventario;
un error contable no autoriza otro motor financiero.

Las pantallas que requieren consistencia financiera —cierre de caja, estado de
cuenta o aplicación de pagos— consultan el estado del trabajo. `Posted` y
`CommercialEffectsApplied` son terminales para sus respectivos modos; ninguna
pantalla da por aplicado un efecto mientras siga pendiente.

## 8. Pruebas obligatorias

- operación completada sin escritura en tablas financieras ni de reporting;
- una señal por motor aplicable y entrega duplicada idempotente;
- submayor y libro mayor en el mismo commit contable;
- fallo entre submayor y asiento revierte ambos;
- venta de crédito, pago parcial, devolución y aplicación;
- la misma matriz de CxC con contabilidad desactivada, sin asientos y sin
  contabilización retroactiva al activar;
- recepción a crédito, pago parcial y devolución al proveedor;
- nota débito/crédito y comprobante manual por el mismo carril contable;
- documento financiero puro sin job, payload ni cursor operacional;
- fiscal `Generation` antes de `Submission`;
- reporting atrasado sin bloquear operación;
- un único `SalesReportingJobs` por mensaje y replay sin un segundo job;
- caída entre commit y publicación recuperada desde outbox sin doble efecto;
- rebuild igual a procesamiento incremental;
- conciliación venta neta, impuestos, costo y utilidad;
- aislamiento por tenant y negocio;
- Service Bus probado con sesiones por negocio; RabbitMQ probado bajo su perfil
  on-premise de consumidor único; in-process debe aprobar la misma matriz antes
  de declararse equivalente.

## 9. Regla para extensiones

Un nuevo efecto financiero extiende el contrato y procesador financiero-contable existentes.
Un nuevo documento fiscal extiende el motor fiscal. Un reporte pequeño agrega una
consulta indexada; una proyección nueva exige evidencia y el contrato anterior.
No se crea una quinta cola dentro del ciclo documental, otro motor financiero ni
una tabla de jobs específica sin una nueva decisión que reemplace expresamente
esta arquitectura.

## 10. Separación de la ejecución SaaS de la API

**Diseño aprobado para implementación. Base inspeccionada `72075d0c`.** Las
secciones anteriores gobiernan la ejecución vigente hasta completar el relevo.
Esta sección especifica la migración y las pruebas que habilitan cada corte.
El objetivo medible es reducir la contención y la latencia HTTP de la API durante
el procesamiento de documentos, conservando el tiempo de negocio desde la
aceptación hasta inventario, cartera, DIAN, reportes y notificación al POS.

### 10.1 Topología y propiedad de código

En cada ambiente SaaS se añade **una Function App de motores**, con cuatro
funciones de Service Bus y un endpoint de salud. SQL, el namespace, las cuatro
colas de §2 y las claves de negocio siguen siendo los mismos. La API conserva
autenticación, autorización, aceptación durable y publicación. La Function del
agente conserva su URL, WhatsApp, campañas, Wompi, procesos programados y los
publicadores que requieren sus operaciones. RabbitMQ conserva el consumidor
único por cola del perfil on-premise. La consola sigue siendo propietaria de la
ejecución explícita de mantenimiento y rebuild.

| Entrada propuesta en el host de motores | Destino de aplicación o infraestructura ya existente | Comportamiento cuyo traslado se debe probar |
| --- | --- | --- |
| `ProcessDocument` | `DocumentProcessingWorker.ProcessOneAsync` → `DocumentProcessingEngine` → `IConfirmedDocumentHandler` | Orden y cursor por negocio, transacción del handler, resultado terminal y señales derivadas. |
| `ProcessAccounting` | Posting → `SqlAccountingPostingProcessor.ProcessAsync`. Activación de preparación de despacho propuesta en §10.3 → aplicación de Despachos. | Enrutar por contrato. El posting conserva submayor y asiento según el modo congelado, gate SQL y `SqlAccountingReportingCompletionObserver` para las correcciones. |
| `ProcessFiscal` | `FiscalGenerationWorker`, `FiscalSubmissionWorker`, `FiscalProcessingCoordinator` | Un disparador distingue las dos etapas. Conservar lease, `GetResumeAtAsync`, `NextAttemptAt`, envío programado, artefacto y notificación. |
| `ProcessSalesReporting` | `SqlSalesReportingProcessor.ProcessAsync` | Conservar tipo, negocio, documento y `SourceVersion`. El escritor incremental y el checkpoint siguen siendo canónicos. |

Crear un ejecutable aislado `src/API/Auraly.Commerce.Worker` y agregarlo a
`Auraly.Commerce.sln`. Solo contiene `Program.cs`, funciones, configuración de
bindings, health y composición. Cada invocación obtiene su propio scope. Clientes
de Service Bus y emisores reutilizables viven durante el host. Las conexiones y
transacciones de cada unidad de trabajo conservan los límites del módulo.

1. Extraer de `Auraly.Api/ServiceBusDocumentProcessingHostedService.cs` el codec,
   la construcción del mensaje y la publicación documental a infraestructura
   compartida. Reutilizar `ServiceBusCommerceProcessingPublisher` y los tres
   codecs que ya están en `Auraly.Platform.Infrastructure/Processing`. Consumir
   desde ambos hosts la misma serialización, incluido el significado económico
   de la señal. Cubrir mensajes archivados de la versión anterior.
2. Extraer de `ServiceBusFiscalProcessingHostedService` la secuencia de etapas
   a la biblioteca fiscal, con sus puertos actuales para stores, publicador y reloj.
   Reutilizarla desde los adaptadores SaaS y RabbitMQ, preservando la política
   propia del transporte de cada uno. El tipo de Azure pasa solo por el adaptador.
3. Compartir los registros DI por capacidad en las bibliotecas de
   infraestructura existentes. Separar el registro de **productores** del de
   **ejecutores**. La API sigue necesitando servicios fiscales para sus comandos
   y consultas. El host de motores solo resuelve el grafo de sus cuatro entradas,
   incluidos todos los handlers documentales, factories SQL, credenciales fiscales,
   observadores y sincronización. Probar resolución y unicidad por `DocumentType`
   desde el contenedor real. Evitar copiar `Program.cs`, referenciar el ejecutable
   web o introducir otro mediador, repositorio, motor o modelo de dominio.
4. El adaptador valida codec, sesión y alcance antes del caso de uso. Procesar
   un mensaje por invocación con sesiones, liquidación manual del mensaje,
   prefetch inicial cero y cancelación propagada. Confirmar después del resultado
   durable. Conservar abandono, razón de dead-letter y registro del quinto fallo.
   Hoy el broker permite diez entregas y el adaptador interviene en la quinta.
   Un cierre del host, mensaje inválido o lock perdido puede aumentar entregas
   sin un intento de negocio. Separar ambos contadores en métricas y pruebas.
   Usar `ServiceBusReceivedMessage` y `ServiceBusMessageActions`, conexión por
   identidad, `IsSessionsEnabled=true` y `AutoCompleteMessages=false`. Partir de
   los cinco minutos de lock del broker y diez de renovación actuales. Demostrar
   una sola ejecución simultánea por sesión con el binding elegido.
   El lease SQL y la idempotencia siguen siendo obligatorios entre procesos.

### 10.2 Revisión de activación, recuperación y temporizadores

| Mecanismo en la base | Decisión para la migración |
| --- | --- |
| Bucle documental en la API. Consulta al arrancar y cada 15 s, máximo 100 candidatos en esa llamada | **No trasladar literalmente.** Resolver la diferencia de arquitectura descrita abajo antes de apagarlo. |
| Mensaje fiscal programado para una etapa con próxima fecha conocida | Mantener el mensaje programado del mismo coordinador. Conservar la distinción entre reentrega de la misma señal y un intento programado legítimo. |
| Despacho con `Channel` y próximo vencimiento de lease calculado por SQL | Sustituir la activación local por un mensaje de la operación concreta. Reclamo por ID y negocio, lease durable y reanudación por evento según §10.3. |
| Sincronización POS con canal local y lectura de pendientes al arrancar | Resolver el cruce entre procesos según §10.3. Arrancar el bucle completo en cada instancia de Functions multiplicaría lectores. |
| Correo y PDF en `PlatformEmailOutboxHostedService`, timer del agente `TimedProcessDispatcher` | Permanecen en sus hosts. El PDF de correo utiliza Chromium en la API. No incorporar correo, instalador de Chromium ni scheduler conversacional al proyecto de motores. |

**Corrección a la propuesta inicial.** La [decisión de transporte documental](decision-motor-documental-ordenado-y-efectos-intrinsecos.md)
excluye un Timer Function que sondee SQL y exige confirmación durable del broker.
La [decisión de dead-letter](decision-dead-letter-motor-documental.md) también
excluye polling SQL. En el runtime el publicador limita el envío a dos segundos
y registra el fallo sin devolverlo al llamador. El bucle compensa esa ventana.
Conservar un trabajo en SQL permite recuperarlo, pero por sí solo no despierta a
ningún consumidor. Un reintento del cliente o un arranque futuro tampoco
garantizan la recuperación autónoma del último documento de un negocio inactivo.

La consulta actual combina el siguiente job disponible por cursor con jobs
completados que tienen outbox pendiente, une el payload y obtiene una bandera
desde JSON. `TOP(100)` limita la salida, no las lecturas ni la ordenación.
El envío posterior es secuencial, uno por candidato, y un fallo puede cancelar
el resto de la pasada. Cada instancia consumidora de API inicia el mismo bucle.
Reenvía el `MovementId` original. La deduplicación configurada durante diez
minutos puede aceptar la operación de envío sin entregar otra copia. No usar
un identificador aleatorio para ocultar esa situación.

Hay además una diferencia verificable en la reconstrucción de señales: la
consulta condiciona `EconomicEffectsEnabled` al literal `PosSale`, mientras
el contrato actual identifica las ventas como `SalesInvoice` o `SalesReceipt`.
La señal ordinaria lleva la bandera y el handler documental de venta comprueba el snapshot.
En la implementación resolver el dato desde la fuente inmutable con la misma
regla en envío inicial y recuperación. Probar las dos grafías históricas de la
propiedad JSON y una factura de habilitación realista. Es un riesgo deducido
del código; no prueba que se haya contabilizado una factura de habilitación.

**Recuperación aprobada para el corte documental.** La cola activa el motor.
Si un proceso cae entre el commit y el envío inmediato, un único publicador
de respaldo por ambiente recupera intenciones pendientes de la outbox. Esta
excepción se limita a publicación y tiene estas condiciones:

- Registrar la intención de envío en la transacción que crea el job. La
  primera señal reutiliza la fila de `DocumentProcessingJobs`. Las
  continuaciones documentales conservan `ServerOutboxMessages` y su FK al
  job. Los trabajos financieros usan su fuente y su job nativos. Verificar
  la durabilidad desde POS online, Edge, pedidos y el agente cuando corresponda.
- Mantener el intento inmediato. El recuperador solo seleccionará intenciones
  pendientes y vencidas mediante índice, orden estable y lote acotado; obtener
  las claves antes del payload. Reclamar por lote con lease corto en SQL y
  liberar la transacción antes de ir al broker. Marcar solo los envíos
  confirmados. Una caída después del envío permite repetir el mismo mensaje.
  Lotes compatibles por cola/sesión y tamaño del broker, sin envío paralelo
  desordenado para un negocio. Un fallo aislado conserva las demás intenciones.
- Implementar el cambio aditivo mínimo para identidad
  estable, fecha disponible y reclamo en el registro propietario, con plan real,
  coste en reposo y latencia de recuperación medidos en DEV. Quince segundos
  es la línea base a comparar, no un SLA ni la frecuencia aprobada. Registrar
  alcance, coste, alarma, propietario y retiro al contar con activación durable
  por evento probada. Los otros motores pueden ensayarse por separado.

**Diferencia con §7.** La venta escribe `sales.document.processed` y la compra
`purchasing.goods-receipt.processed`. El observador actual calcula los destinos y
marca conjuntamente las filas operacionales del documento. Los coordinadores
pueden generar un `SignalId` nuevo en cada solicitud. Antes de afirmar que hay
una fila y un acuse por destino, probar caída después del primer envío y antes del
último. Para converger a §7, guardar identidad por intención/destino y versión o
etapa cuando aplique, usarla en las reentregas y marcar por esa identidad. No
reutilizar el ID de una generación para una nueva presentación fiscal programada.
Excluir `FiscalDocument.DianAccepted`, que tiene otro consumidor. Inventariar
filas heredadas pendientes, preparar conversión idempotente si se necesita y
ensayarla sobre copia. Evitar reinterpretar eventos ya despachados.

**Dos pruebas de interrupción que distinguen entrega de efecto.**

1. El [store de generación fiscal](../src/Infrastructure/Auraly.Infrastructure.Persistence/SqlFiscalGenerationWorkStore.cs)
   confirma el XML firmado y pasa a `PendingSubmission`. Después, el
   [consumidor](../src/API/Auraly.Commerce.Worker/CommerceEngineFunctions.cs)
   solicita el envío. Si muere entre ambos pasos, al repetir generación
   `AcquireAsync` no adquiere ese estado y su `GetResumeAtAsync` consulta
   únicamente `PendingGeneration`. El adaptador compartido debe resolver la
   continuación por negocio, documento y estado durable: ante una generación
   repetida con artefacto firmado pendiente de envío, asegurar la activación
   idempotente de esa etapa antes de confirmar la entrega. Probar la caída
   antes del primer envío y después del acuse del broker. Verificar el mismo
   hash y número, una sola firma y el tratamiento vigente de resultados DIAN
   ambiguos o ya aceptados.
2. Entregar el movimiento N+1 antes que N para el mismo negocio y simular
   pérdida de lock antes de entrar al handler. La validación de secuencia de
   SQL devuelve `NotReady` antes de ejecutar el documento. El contador de
   entregas del broker por sí solo no acredita cinco fallos de negocio ni
   autoriza avanzar el cursor. La activación pendiente debe seguir siendo
   recuperable cuando corresponda su turno. Probar también cinco fallos reales
   de N: SQL debe registrar el fallo terminal y liberar el cursor en la misma
   transacción. Reentregar N después del commit y antes del acuse debe
   conservar evidencia de dead-letter y cero efectos derivados de N.

### 10.3 Tres cruces entre procesos

**Frontera de ejecución.** En SaaS, aprobar un despacho y reintentar una
contabilización son comandos. La API verifica permisos, alcance y precondiciones,
acepta la intención durable y devuelve el estado conocido. La ejecución de
motores entra siempre por Service Bus, también en fallos, reintentos y replay.
Un `BackgroundService` o `Task.Run` dentro de la API seguiría consumiendo sus
recursos y una llamada HTTP de la API a la Function añadiría otro camino.

**Despacho.** Actualmente hay dos activaciones para la devolución. El endpoint
guarda `DispatchSettlementOperations` y despierta un `Channel` local. Dentro
del proceso API, `SalesReturnService.ConfirmAsync` ya publica la señal para una
devolución pendiente y el hosted service llama además a `ProcessOneAsync`.
También crea un movimiento `DispatchCashDifference` y lo ejecuta directamente.
Esto prueba dos caminos de ejecución. La existencia de dos caminos por sí sola
no demuestra un doble asiento en datos.

La creación agrupa facturas existentes. Mis entregas registra el resultado,
el medio declarado y su evidencia. El cierre del transportador guarda la
declaración y prepara la revisión administrativa. Esos pasos conservan los
efectos originales de las facturas y dejan pendiente la aplicación del recaudo
o de la corrección del medio. La aprobación administrativa congela la decisión
y publica una sola activación idempotente del despacho. Desde esa operación
los motores reciben las señales que correspondan a las devoluciones, saldos,
asientos y proyecciones. Un gasto aprobado sigue su flujo financiero propio.

Cierres de caja reutiliza los estados persistidos. El cierre declarado aparece
como «Pendiente de validar». La respuesta a la aprobación devuelve «Procesando»
desde que SQL acepta la operación, incluso si la cola está detenida. El
consumidor mantiene ese estado mientras espera los resultados necesarios.
La transacción contable confirma «Liquidado» y guarda el resultado conciliado,
con o sin diferencias. Un problema que agota sus reintentos o exige una
intervención conserva el estado «Requiere atención» y su motivo. Verificar
los tres resultados desde el modal con su acción «Actualizar» y probar que
una solicitud repetida conserva la operación original.

Diseño propuesto para eliminar las tres dependencias del proceso web:

1. El cierre ciego del transportador y las ediciones administrativas previas
   conservan sus estados declarativos. Al aprobar, guardar en la misma transacción
   la operación, el alcance, el actor, los datos congelados y su intención de
   activación. Reutilizar la clave idempotente y el conflicto actual por datos
   distintos. Aplicar el protocolo de entrega de §10.2 al commit sin publicación.
2. Propuesta de contrato: activar la preparación de la conciliación en la cola
   contable existente con un discriminador explícito y versionado que remita al
   ID de operación y negocio. La Function enruta la preparación al caso de uso
   de **Despachos** en su biblioteca. El mensaje de posting actual sigue entrando
   al procesador contable. La preparación conserva un solo propietario y una
   sola tabla de operaciones. Esta variante del contrato es nueva: desplegar
   lectores compatibles antes del primer productor, también en el camino de
   rollback. No disfrazar la preparación de comprobante ni relajar la validación
   del codec contable para aceptar cualquier tipo.
3. En la preparación, reclamar exactamente la operación del mensaje por ID y
   negocio. El `TOP(1)` global del claim actual no sirve para esta entrada.
   Leer entregas, devoluciones vinculadas y pagos por conjunto. Aceptar las
   devoluciones con sus IDs deterministas mediante el servicio de Devoluciones:
   su señal existente activa el motor documental. Los trabajos financieros que
   correspondan según el modo de cobro vigente usan la cola contable. Vincular
   un documento ya aceptado o mantener el medio de pago no genera otro recaudo.
   Si falla la preparación a mitad de lote, reanudar sobre la misma operación.
4. Registrar o poder derivar de la decisión congelada **todos los IDs esperados**
   antes de habilitar el cierre. Hoy `dispatch.SettlementDocuments` enumera
   documentos ya materializados. Un conjunto vacío tras una preparación parcial
   no demuestra que todos hayan terminado. Verificar por conjunto que están
   aceptados todos los esperados y que tienen los estados terminales requeridos.
   Una devolución física necesita su resultado documental y el financiero cuando
   corresponda. Si falta un resultado, completar la invocación tras dejar
   durable su continuación. El último prerrequisito, al confirmar su transacción,
   habilita la señal pendiente. Probar ambas carreras, incluida la finalización
   del hijo antes de registrar la espera del padre. Evitar esperar conservando
   el lock de la sesión, abandonar en bucle o sondear factura por factura.
5. En el primer corte conservar el contrato vigente de `DispatchCashDifference`
   y activarlo por la cola documental con el movimiento ya creado. Su handler
   prepara los jobs contable y de corrección de reporting. La fuente sigue siendo
   única, incluso con diferencia cero. Solo el motor contable completa el cierre
   en su transacción; la corrección se proyecta después del resultado financiero.
   Enviar en el futuro esta fuente puramente financiera directamente a contabilidad
   sería otra simplificación: hoy el procedimiento de cierre exige, para una
   diferencia distinta de cero, un job documental completado. Ese cambio necesita
   migrar y probar esa precondición y las operaciones en vuelo. Véanse el
   [diseño contable vigente](implementation/accounting-operational-design.md) y
   la [validación del cierre](../database/Auraly.Database/StoredProcedures/DispatchSettlementOperationComplete.sql).

**Reintento contable explícito.** Antes del cambio, `SqlAccountingStore.RetryPostingAsync`
resolvía o recuperaba la fuente, ejecutaba `ProcessAsync` y releía el posting. En SaaS debe
validar el permiso `accounting.postings.retry` y el tenant/negocio, reutilizar la
recuperación existente y solicitar el mismo trabajo por el coordinador con gate.
Un terminal devuelve el resultado existente. Para trabajo aceptado proponer
HTTP 202 y la vista de estado autoritativa, compatibles con el cuerpo de datos
vigente; mantener el resultado de fuente inexistente. Actualizar en el mismo
cambio `accounting.ts`, `voucher-draft-editor`, `document-processing-actions`
y `accounting-document-dialog`. Una sola mutación, estado pendiente visible,
resultado final por evento existente verificado o actualización explícita.
Probar la solicitud repetida, el doble clic, el modo sin asiento, el fallo de
configuración y el rol sin permiso. Acordar y verificar compatibilidad de los
clientes instalados antes de activar el comportamiento asíncrono.
Separar en DI la aceptación del reintento y la ejecución del posting. El store
que usa la API pierde la dependencia concreta de `SqlAccountingPostingProcessor`.
El consumidor llama al procesador una vez por entrega, y el job existente
decide si quedan efectos. Nunca devolver «contabilizado» por el mero acuse del
broker. Conservar los motivos de configuración pendiente e intervención.

**Prueba de frontera.** En DEV, con los consumidores deshabilitados y la API
activa, aprobar una conciliación y solicitar un reintento válido. Deben quedar
aceptados, con cero movimientos nuevos de inventario, submayor, asientos o
proyección de corrección. Reactivar los consumidores con la API de comandos
detenida: ambos deben llegar a su resultado durable. Repetir tras duplicar
mensajes, invertir la llegada entre colas e interrumpir la preparación después
del primer hijo. Verificar un único efecto por documento. Una prueba de
arquitectura debe impedir que endpoints, stores HTTP o servicios alojados en la
API SaaS vuelvan a ejecutar esos procesadores. Probar el host real; el transporte
in-process aislado de las pruebas no demuestra esta frontera.

**Aviso a la caja.** `SqlPosSynchronizationOutboxDispatcher.DispatchPendingAsync`
solo escribe en un `Channel` **del proceso**. Su lector es el hosted service de
la API. La contabilidad y `FiscalStatusSynchronizationNotifier` lo invocan
después de operar. Compartir el registro DI en otra máquina dejaría el aviso
sin lector. Extraer la publicación acotada del ámbito de ese mismo dispatcher y
reutilizarla desde el consumidor con tenant y negocio verificados. La API conserva
el lector local para sus productores. Ambos deben compartir un único algoritmo
de reclamo, coalescencia por stream/destino y acuse por cursor después del envío
a Web PubSub. Tratar solapamiento, caída y fallo de push sobre la outbox durable:
reintentar la entrega pendiente sin volver a aplicar efectos financieros.
Comprobar que un fallo no pueda quedar oculto tras confirmar el mensaje y que
un dispositivo desconectado pueda ponerse al día mediante su cursor. No ejecutar
el barrido de todos los negocios en cada arranque frío ni iniciar un timer POS.

**Descarga incremental de la caja instalada.** La autoridad del protocolo es la
[decisión de sincronización POS](decision-pos-sync-push-sin-polling.md). La
API sigue sirviendo los deltas a las versiones instaladas de Edge. La Function
App adquiere la responsabilidad de avisar por los cambios que confirman sus
motores. Antes del corte, inventariar cada escritura de invalidación, su
transacción, el ámbito afectado y el host que activa la publicación. Verificar
también los handlers que escriben la outbox directamente en SQL, como
`SqlGoodsReceiptDocumentHandler`, que registra un cambio de catálogo al
confirmar una recepción con productos que tienen precio activo.

| Cambio | Comportamiento que debe conservar la caja |
| --- | --- |
| Producto, código de barras, baja o publicación de precios | Descargar la página de cambios del catálogo del negocio. En una venta nueva deben reflejarse el código, la vigencia y el precio efectivo, incluidas las escalas del canal. Una propuesta de precio pendiente conserva el precio de venta publicado. |
| Alta, edición, desactivación o cambio de canal de un cliente | Descargar los cambios de clientes desde su propio cursor. Buscar el cliente y comprobar el canal y el precio en una venta nueva. Un cliente creado sin conexión debe conservar su identificador al subir y recibir la versión del servidor. |
| Vendedor, configuración de caja, permisos o estado fiscal | Mantener el flujo y el destino ya definidos para cada aviso. Verificar cambios originados tanto en la API como en un motor, con dos negocios y un aviso dirigido a una sola caja. |

Web PubSub despierta a Edge. Cada caja conserva cuánto ha aplicado aunque otra
caja del mismo negocio ya haya recibido el aviso. En particular,
[`CustomerChangesAsync`](../src/Infrastructure/Auraly.Infrastructure.Persistence/SqlCatalogStore.PosCustomers.cs)
lee las filas de `PosSynchronizationOutboxMessages` por cursor. Marcar
`PublishedAt` acredita el envío al canal. Esas filas deben seguir disponibles
para la descarga dentro de la retención del protocolo. Aplicar los datos y
avanzar el cursor en la misma transacción SQLite permite reanudar una página
interrumpida.

Hay dos puntos concretos que verificar en Edge:

- **Primera conexión.** El arranque inicia la descarga y la negociación de
  push en paralelo. El código actual solo solicita ponerse al día al conectar
  por segunda vez. Probar un cambio confirmado *después* de la lectura inicial
  y *antes* de la primera suscripción efectiva, sin producir cambios posteriores.
  La caja debe alcanzarlo. Si queda atrasada, cerrar esa ventana al establecer
  la suscripción usando el cursor, conservando un solo propietario de descarga.
- **Costo por página.** [El sincronizador](../src/Pos/Auraly.Pos.Edge.Infrastructure/PosCatalogSynchronizer.cs)
  consulta la versión anterior en SQLite por cada cliente y, con el registro de
  eventos habilitado, por cada producto, antes de confirmar la página. Leer
  las versiones previas por conjunto cuando hagan falta y emitir los eventos
  de aplicación después del commit. Medir lecturas SQLite y peticiones HTTP
  por separado con una página llena y con varias páginas.

### 10.4 Revisión de rendimiento y N+1

No se ha medido aún el beneficio del nuevo host. SQL seguirá siendo compartido.
Un mismo negocio conserva su ejecución secuencial por cola. Más instancias
pueden aumentar CPU, conexiones y bloqueos en SQL antes de mejorar HTTP.

| Camino | Riesgo observado | Diseño y verificación |
| --- | --- | --- |
| API → aceptación | Mover CPU libera el host, pero cada ida adicional al broker, SQL o a otra API encarece la venta. | Cero HTTP entre hosts. Preservar escritura atómica y presupuesto de publicación. Medir aceptación HTTP y finalización del job por separado. |
| Job → fiscal/contabilidad/reporting | El observador lee soportes por conjunto y luego emite comandos por destino. El gate contable amplía a dependencias en una consulta. Un comando independiente por documento no justifica volver a leer catálogos o cabeceras por hijo. | Medir ventas con 0/1/máximo de cargos, compras con 1/100 soportes y liquidaciones de tamaño creciente. Mantener el límite de negocio. Para lotes grandes, paginar por clave en SQL y publicar conservando orden. Medir consultas, mensajes y lotes por separado. |
| Outbox → cajas | Se lee por ámbito y el dispatcher actual marca cada invalidación individualmente. El conjunto puede crecer con destinos. | Acotar la página en SQL, agrupar por stream/destino y marcar en conjunto los cursores enviados. Bloqueo de reclamo corto, sin transacción abierta durante Web PubSub. Medir con 1, 10 y 70 destinatarios. |
| Despacho → devoluciones/pagos | Las lecturas iniciales se agrupan, cada documento tiene su transacción independiente. Cambiar a ejecución remota puede añadir esperas o lecturas por factura. | Comprobar límites reales del contrato antes de iterar; usar páginas acotadas cuando no exista máximo. Una consulta de dependencias por conjunto. Prohibir GET por factura para comprobar avance. |
| Recuperación y replay | El lector de trabajo equipara `DeadLettered` y `Completed` y el worker llama al observador en ambos casos. Un duplicado completado también puede volver a emitir señales derivadas. | Separar terminal fallido de completado. Consultar intenciones pendientes por movimiento. Un terminal fallido jamás genera trabajo derivado, un terminal completado sin entregas pendientes responde sin reemitir. Probar ambos incluso si el broker vuelve a entregar el mensaje pasada su ventana de deduplicación. |

**Protocolo antes/después.** Usar el mismo conjunto reproducible, SKU, índices,
volumen histórico, mezcla de comandos y concurrencia, en corridas separadas.
Probar reposo, primera solicitud en frío, operación caliente, ráfaga de un
negocio y varios negocios simultáneos. Cubrir POS, factura masiva de pedidos,
devolución, conciliación y una grilla paginada de control bajo carga. Repetir
al menos tres corridas comparables por escenario, registrar tamaño de muestra,
p50/p95/p99, throughput, errores, CPU/memoria de API, conexiones SQL, lecturas
lógicas, bloqueos, latencia desde encolado y edad del job más antiguo. Atribuir
telemetría por host, cola, etapa y versión; correlacionar por negocio, movimiento,
documento y entrega. Mantener payload fiscal, certificados y datos de cliente
fuera de logs y etiquetas de métricas.

**Puerta propuesta.** Conservar los presupuestos absolutos existentes, incluido
el motor local de inventario <2 s en los escenarios documentados. A igual carga
y tasa de éxito, exigir una mejora reproducible del p95 HTTP bajo contención
superior a la variación entre corridas de la base. Los percentiles de finalización
y el atraso sostenido de cada motor deben mantenerse dentro de sus presupuestos;
fijar sus valores numéricos con la base registrada **antes de programar**, donde
aún no exista SLO. Una respuesta HTTP más rápida con trabajo acumulándose no
satisface la meta. Comparar número de comandos SQL por lote y planes reales
al multiplicar histórico y líneas. La medición de rendimiento va separada de
profiling intensivo. Reducir concurrencia si se satura SQL. Empezar DEV con
concurrencia fija conservadora, una llamada por sesión y escalado acotado por la
capacidad SQL del ambiente; no extrapolar las 16 sesiones actuales por cada
instancia nueva. Registrar valores ganadores y costes de frío antes de promover.
En Flex los triggers de Service Bus pueden escalar independientemente. Ejecutar
también la carga de los cuatro motores simultáneos: el presupuesto de conexiones
y bloqueos de la base compartida aplica a la suma de las instancias de las
cuatro funciones, la API y los demás usuarios de SQL. Medir con el rol real
del entorno y con el HTTP de la API bajo carga.
[Escalado de Functions por evento](https://learn.microsoft.com/azure/azure-functions/event-driven-scaling).

### 10.5 Infraestructura, CI, despliegue y reversión

1. **SDK y artefacto.** El repo fija SDK 8 en `global.json`; CI usa 8.0.x.
   Proponer Functions v4 isolated .NET 10 LTS para el nuevo host. Resolver
   explícitamente la selección del SDK para ese proyecto y probar referencias
   a las bibliotecas actuales, restore, build y publish reproducibles en Linux.
   Registrar versiones compatibles de Worker/SDK/ServiceBus. No retargetear el
   POS, agente ni la API como efecto colateral. Verificar metadatos de los cuatro
   bindings, recursos de firma/XML y health desde el ZIP publicado.
2. **DEV y PROD en Bicep.** Ampliar `infrastructure/azure/main.bicep` y salidas de
   cada entorno con app y plan Flex propios, contenedor de publicación separado,
   identidad administrada, storage del host y telemetría con nombre de rol propio.
   Una app por plan Flex. Dar a la identidad del motor acceso SQL mediante el
   procedimiento administrado de aprovisionamiento del ambiente, recibir/enviar
   solo en las colas necesarias, leer configuración, materiales fiscales y
   artefactos necesarios y publicar en el hub existente. Validar red, DNS y RBAC
   con esa identidad real en DEV. Ninguna dependencia de OpenAI, tokens WhatsApp,
   credenciales de agente o permisos de administrador SQL. Los productores del
   agente y API conservan su capacidad de publicar. Los nombres de las cuatro
   colas y la conexión del binding deben estar en settings visibles al runtime
   de Functions, incluso antes de construir DI. Verificar que los hosts no
   compitan por el mismo identificador de host/storage.
3. **Release.** Agregar componente `engines-function`, ZIP y SHA al manifiesto:
   `New-AuralyRelease.ps1`, `Test-AuralyReleaseArtifacts.ps1`,
   `Resolve-AuralyDeploymentComponents.ps1`, `Publish-AuralyReleasePipeline.ps1`,
   `Test-AuralyDeploymentReadiness.ps1`, workflows de calidad y
   `deploy-auraly-release.yml`. Reutilizar OneDeploy y la verificación de
   `functions.metadata` parametrizados por app; claves webhooks, settings
   de WhatsApp y comprobaciones del agente siguen asociadas a su artefacto.
   Validar cambios compartidos que exijan publicar API y motores juntos.
   Las rutas de despliegue rápido deben respetar el mismo contrato. Mantener
   legibles los manifiestos archivados anteriores para rollback.
4. **Orden de activación.** Desplegar primero con los nuevos triggers
   explícitamente deshabilitados (`AzureWebJobs.<nombre>.Disabled`), incluido
   cualquier publicador de recuperación autorizado. Comprobar salud, versión,
   metadatos y permisos. El primer release retira los cuatro lectores de la API
   SaaS. El pipeline detiene y verifica los escritores antes del DACPAC. En
   ese mismo paso incorpora las conciliaciones abiertas que aún requieren
   preparación y reanuda las publicaciones documentales pendientes. Publica
   los paquetes compatibles y habilita reporting, contabilidad, documental,
   fiscal y recuperación, en ese orden. Medir en DEV la interrupción HTTP,
   los jobs en vuelo y la renovación de locks durante el reinicio de cada
   host. RabbitMQ sigue siendo el transporte de las instalaciones locales.
5. **Prod y rollback.** Promover el artefacto inmutable ya integrado y validado
   en DEV según el procedimiento de release y autorización vigentes. Registrar
   versiones, interruptores, backlog, DLQ y jobs preexistentes por cola antes
   del corte. Ante doble efecto, cruce de negocio, pérdida de señal, fallo fiscal,
   pérdida de notificación o incumplimiento sostenido de presupuesto, deshabilitar
   el consumidor afectado y esperar sus locks. Conservar las intenciones y
   los mensajes, corregir el host o publicar un artefacto de Function que
   entienda los contratos activos. El primer relevo se recupera publicando
   hacia adelante. Ensayar la pausa y la reanudación con datos creados por
   la nueva versión, mensajes programados e intenciones pendientes.

### 10.6 Evidencia exigida para la revisión de implementación

| Fase | Escenarios y criterio de salida |
| --- | --- |
| Local / CI | Tests de arquitectura de referencias y registros, codecs y scopes reales; regresiones existentes `DocumentProcessingWorkerTests`, `DocumentProcessingOrderingTests`, `RabbitMqDocumentProcessingTests`, `AccountingVerticalSliceTests.DispatchCollections` y coordinadores fiscales. Probar contrato HTTP/UI y rollback de los cambios aditivos. |
| Dos procesos + SQL y Service Bus con sesiones en DEV | Dos negocios concurrentes, dos instancias, orden por sesión, sesiones/payload incompatibles, cancelación, lock expirado, quinto intento, job fallido y siguiente documento. Interrumpir antes de publicar, tras envío sin acuse, tras commit antes del acuse y entre dos destinos. Verificar salida durable incluso con el sistema en reposo. |
| Flujos de negocio en DEV | Venta contado/crédito, pedido a cartera domicilio, cambio en entrega, cierre ciego y aprobación; saldo, asiento y proyección una vez. Comprobante manual y retry; habilitación exclusivamente fiscal, documentos soporte asociados y DIAN en ambiente de prueba. Verificar en navegador el POS abierto en otro proceso y sin recarga manual. Comprobar que WhatsApp/campañas siguen en el host del agente. |
| Caja instalada en DEV | Con API y motores en procesos separados, mantener una caja conectada y desconectar otra del mismo negocio. Cambiar un producto, publicar un precio con escala, cambiar el canal de un cliente y generar una invalidación desde el motor. Probar primera suscripción, aviso duplicado, reinicio a mitad de página y reconexión con varias páginas pendientes. Verificar en ambas cajas la búsqueda y el precio de una venta nueva, el aislamiento frente a otro negocio y la conservación de la venta local pendiente. Comparar cursores aplicados, número de descargas y lecturas por página; un reinicio con cursor válido debe continuar de forma incremental. |
| Capacidad y operación | Adjuntar antes/después de §10.4, planes SQL, viajes por lote, memoria e instancias, antigüedad de cola, pendientes parciales, alertas y ensayo de rollback. En PROD verificar salud y observar documentos reales con lectura, sin ventas ficticias ni reenvíos fiscales de prueba. |

El PR de implementación debe vincular por cada fila la prueba y el resultado.
Un HTTP 200 en health o una corrida con el transporte en memoria no demuestra
el relevo de consumidores. Hoy esta sección aporta diseño y auditoría estática;
las mediciones, decisiones de recuperación y ensayos de despliegue quedan como
criterios de entrada y salida, no como pruebas ya ejecutadas.

**Dictamen de entrada para la base inspeccionada.** Se puede iniciar la fase
reversible: levantar la línea base, probar los contratos actuales y preparar la
composición compartida y el artefacto con los triggers deshabilitados. El
relevo del recuperador exige demostrar que se publican las intenciones pendientes
tras un commit sin aviso (§10.2). Las ventanas de fallo fiscal, orden documental y POS,
los límites de SQL y la reversión tienen criterios de aceptación en §§10.2–10.6.
El visto bueno operativo por cola requiere resultados del host publicado en DEV.

**Orden de trabajo para el PR.** Primero capturar línea base y fijar los
presupuestos pendientes. Extraer y probar la composición compartida
bajo el host actual. En un cambio revisable añadir el host y el artefacto
deshabilitado. Probar los cruces entre procesos antes del relevo de la cola
afectada. Por último ejecutar el ensayo DEV, la comparación A/B y la reversión.
Durante la convivencia, cada despliegue debe identificar qué proceso recibe
cada cola, qué proceso recupera envíos y qué versión entiende los contratos.
Al terminar el corte el conteo de invocaciones de los cuatro consumidores
Service Bus en la API debe ser cero, también con despachos y reintentos manuales.
Mantener el camino RabbitMQ cubierto al retirar la implementación antigua SaaS.

**Artefacto local de los cuatro motores.** El proyecto `src/API/Auraly.Commerce.Worker` fija
su SDK .NET 10 en el propio directorio y tiene una comprobación de publicación
independiente en CI mientras la solución general utiliza el SDK 8. El paquete
incluye Health y los cuatro disparadores con sesiones, acuse manual y los
contratos de los motores existentes. La publicación POS desde un motor usa
el dispatcher compartido por negocio. Su exclusión de publicación utiliza
un lock de sesión SQL con espera acotada, un presupuesto de diez segundos,
sin transacción abierta durante el envío, y acusa los avisos publicados por
página. Las filas de cambio permanecen disponibles para la descarga por cursor.
La primera suscripción de Edge pide recuperar cambios si su descarga ya
había empezado. Los registros de cambios de producto y cliente se emiten
después de aplicar la página, con una lectura previa por conjunto.

El módulo Bicep opcional `commerce-worker` provisiona el host con una
identidad propia y los cuatro disparadores apagados. El pipeline concede el
acceso SQL y activa los disparadores después de publicar la API productora.
Despacho usa la cola contable en SaaS y el consumidor local en RabbitMQ.

La lectura de un movimiento recuperado ya resuelve la habilitación fiscal
desde el payload persistido. El commit agrega una continuación a la outbox
existente cuando el handler no dejó un evento pendiente. Un movimiento
completado que ya agotó su publicación devuelve el resultado existente. Esta
parte cubre la interrupción entre el commit documental y los motores derivados.
El mismo publicador recupera la continuación si el mensaje documental
desaparece o agota sus entregas en el broker.

La intención de entrega inicial de Documental queda en el propio
`DocumentProcessingJobs`. Al aceptar el documento, la confirmación de
publicación empieza nula en esa misma fila. Esta forma usa la transacción
existente de cada productor y conserva `ServerOutboxMessages` para las
continuaciones que nacen después de procesar. La búsqueda acotada reclama
únicamente la siguiente secuencia de cada negocio. Después del commit de
un documento, su consumidor intenta publicar el sucesor. El timer usa el
mismo publicador para recuperar el envío si ese proceso se detiene. SQL
reclama las filas antes de contactar al broker y confirma, por lote, los IDs
que el broker aceptó bajo ese mismo lease. El ID de la primera entrega es
`MovementId`; los reintentos de negocio usan la identidad del movimiento y
su contador de fallos persistido.

El índice filtrado y los campos de publicación son aditivos. La Function
nueva provisiona deshabilitado `RecoverDocumentInitialSignals`. La API SaaS
publica con el registro durable y la Function recupera las publicaciones
después del relevo coordinado.

En la continuación documental, `Auraly:DocumentProcessing:DurableContinuation:Enabled`
guarda el plan antes del primer envío. Cada fila `doc.send.*` de
`ServerOutboxMessages` representa un destino, con su tipo, etapa, versión de
origen e identidad de mensaje. Se conserva el orden del gate contable. Las
filas solo se crean mientras el evento operacional permanezca pendiente y
el plan se congela bajo el bloqueo del job. Las filas confirmadas se acusan
por identidad en bloque. Si falla un envío, se acusan las entregas previas
y se registra el error del destino pendiente. Una caída antes de confirmar
el acuse repite el mismo ID de broker. Al agotar los destinos se cierra el
evento operacional en la misma transacción del último acuse.

El camino nuevo usa tres consultas de gestión del plan para un documento
recién completado y dos en una reentrega con plan existente. Las lecturas
para resolver los destinos se mantienen por conjunto. `FiscalDocument.DianAccepted`
conserva su propietario y la presentación fiscal programada sigue usando su
propia identidad. La Function de motores activa la continuación durable
en su composición. Una versión posterior del consumidor debe conservar
las identidades de las publicaciones que ya se registraron.

Al completar el job, su transacción habilita la recuperación en una de las
filas operacionales pendientes. El índice filtrado permite al timer único
seleccionar hasta 100 avisos vencidos antes de recuperar sus señales. La
primera búsqueda espera dos minutos desde el commit. El reclamo es por dos
minutos, con un presupuesto de envío de 90 segundos por lote y hasta cuatro
sesiones concurrentes. El broker recibe un ID derivado de la fila y su contador de envíos
confirmados. Si no hay confirmación, el siguiente reclamo conserva el ID. Si
confirma y el documento aún tiene destinos pendientes, el respaldo vuelve a
intentar a los 5, 10 y 20 minutos. Al cuarto aviso confirmado termina el
respaldo automático y el evento sigue visible como pendiente para
intervención. Monitorear en DEV y PROD pendientes agotados
(`ProcessedAt IS NULL AND RecoveryPublicationCount=4`) junto con antigüedad
del backlog y DLQ. La resolución del incidente usa la señal documental
existente y conserva el plan. El aviso solo activa el consumidor documental.
Su job completado impide repetir los efectos de operación.

Antes de activar la opción junto a `DurableActivation`, inventariar las
continuaciones heredadas y ensayar el corte con sus datos y leases abiertos.
Las pruebas locales no sustituyen el ensayo de recuperación desde la DLQ en
Service Bus ni la medición de su coste en reposo.

El reintento contable explícito pasa por el coordinador y su gate para el
documento solicitado. La API espera hasta cinco segundos a que la cola
confirme la recepción antes de responder `202`. Si el consumidor ya produjo
un resultado, devuelve ese resultado con `200`. El ID del mensaje se deriva de la sede, el documento,
su tipo y el contador de intentos del job. Una respuesta de publicación no
confirmada permite solicitar nuevamente el mismo intento y conserva el ID.
Una vez confirmado, `AccountingPostingJobs.ExplicitRetryAttempt` registra ese
contador. Un nuevo contador o un estado terminal retira el indicador de cola.
Esta marca aditiva conserva el estado contable y el último motivo de error.
La lectura inicial, el gate exacto y la confirmación con lectura del estado
forman tres viajes SQL para un job existente que requiere publicación. Una
solicitud repetida ya confirmada o terminal usa solo la lectura inicial.
Las lecturas exactas del intento usan snapshot. Si el consumidor ya tiene
bloqueada la fila, el acuse de recepción del broker basta para responder
`202` tras una espera SQL de hasta medio segundo. La siguiente consulta
recupera el estado confirmado por el motor o por la marca, según corresponda.

Compras, gastos, trazabilidad y comprobantes leen el indicador junto con sus
datos actuales y pueden actualizarlo por acción explícita. El documento
subyacente conserva el resultado del motor contable como única autoridad.
El contrato mantiene el cuerpo anterior y añade `retryQueued` cuando la cola
aceptó el intento y aún no hay resultado. Verificar en DEV el corte con el
consumidor detenido, la reanudación real y los clientes instalados antes de
promover el cambio.

### Conciliación de despachos y relevo

Al aceptar la conciliación en modo Service Bus, la transacción registra la
primera activación en `DispatchSettlementOperations`. El mensaje
`DispatchSettlementPreparation.v1` viaja por la cola contable y lleva el
negocio, la operación y su versión. La recepción del mensaje reclama esa
operación concreta. El mismo registro conserva el plan de cada fase y la
lista de documentos esperados. La fase de devoluciones termina antes de
calcular el plan de pagos. Después se espera que esos pagos tengan resultado
financiero para aceptar la diferencia de caja. Los IDs deterministas y los
servicios canónicos se conservan en una reentrega.

Cuando un hijo termina, su transacción reevalúa el conjunto esperado. Si el
hijo terminó antes de registrar la espera, esa misma evaluación al avanzar el
padre detecta el resultado. El procesamiento devuelve la identidad de la
continuación disponible al disparador tras confirmar la transacción. El
disparador reclama y publica esa operación por negocio e ID en la misma cola
contable. La recuperación selecciona intenciones de
activación vencidas por índice. Una conciliación en espera de hijos queda
fuera de esa consulta. La conciliación muestra el motivo de un hijo con
intervención requerida y el último resultado de esa operación.

El relevo usa `engines-function` como componente del manifiesto. El
aprovisionamiento independiente crea la identidad de motores, su plan Flex
y su almacenamiento. La publicación usa el ZIP archivado y verifica el
registro de sus cuatro triggers y el temporizador. La base agrega el
principal administrado con lectura, escritura y ejecución para las
bibliotecas actuales. El release arranca con los consumidores nuevos
deshabilitados en el primer despliegue. Para el relevo se comprueba el
release saludable, se detienen los escritores previos, se incorporan las
conciliaciones abiertas que aún no tienen su documento final y se activan
los consumidores nuevos desde el pipeline. La publicación rápida de API
comprueba el estado de la Function y sus disparadores. Una pausa de motores
conserva las activaciones registradas para reanudarlas en esa misma Function.

Puntos de partida para verificar el análisis contra el código:

- [Registro de productores en la API](../src/API/Auraly.Api/Program.cs),
  [consumidores](../src/API/Auraly.Commerce.Worker/CommerceEngineFunctions.cs),
  [activación documental y timer](../src/API/Auraly.Commerce.Worker/DocumentInitialSignalRecoveryFunction.cs)
  y [consulta y clasificación del trabajo](../src/Infrastructure/Auraly.Infrastructure.Persistence/SqlDocumentProcessingWorkSource.cs).
- [Observador de finalización](../src/Infrastructure/Auraly.Infrastructure.Persistence/SqlDocumentProcessingCompletionObserver.cs),
  [dependencias contables](../src/Modules/Accounting/Auraly.Commerce.Accounting.Infrastructure/SqlAccountingProcessingSignalGate.cs)
  y [proyección después del posting](../src/Infrastructure/Auraly.Infrastructure.Persistence/SqlAccountingReportingCompletionObserver.cs).
- [Liquidación de despacho](../src/Modules/Dispatching/Auraly.Application.Dispatching/DispatchSettlementPreparation.cs),
  [reintento contable](../src/Modules/Accounting/Auraly.Commerce.Accounting.Infrastructure/SqlAccountingStore.cs)
  y [salida de notificaciones del POS](../src/Infrastructure/Auraly.Infrastructure.Persistence/SqlPosSynchronizationOutboxDispatcher.cs).
- [Publicación del release](../infrastructure/azure/Publish-AuralyReleasePipeline.ps1)
  e [infraestructura por ambiente](../infrastructure/azure/main.bicep).

Referencias del proveedor: [Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-plan),
[binding de Service Bus](https://learn.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger),
[deduplicación](https://learn.microsoft.com/azure/service-bus-messaging/duplicate-detection)
y [ciclo de soporte de .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
