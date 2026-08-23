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
| `auraly-accounting-processing` | Contabilidad | todos los submayores financieros y el libro mayor |
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
contable después de que la API guarda atómicamente su fuente y su único job
contable. No crean `DocumentProcessingJobs`, no avanzan
`BusinessProcessingCursors` y no usan payloads propietarios de inventario.

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

## 4. Motor contable único

Existe una sola cola, un solo contrato de señal, una política canónica de tipos
contabilizables y un solo trabajo durable contable por documento fuente. No se
crean workers separados para CxC, CxP, pagos, notas, caja o libro mayor.

Dentro de una transacción serializable, el motor contable:

1. bloquea el trabajo y verifica el hash del payload inmutable;
2. aplica CxC/CxP, pagos, aplicaciones, créditos o anticipos;
3. crea y valida el comprobante de partida doble;
4. marca el trabajo completo;
5. confirma todos los efectos o ninguno.

El orden financiero usa la misma secuencia documental de origen por negocio.
Una entrega duplicada no reaplica submayor ni asiento. Mientras el tenant no
esté explícitamente `Ready`, o el documento sea anterior a `EffectiveFrom`, la
operación no crea trabajo ni publica mensaje contable. Después de activar, una
inconsistencia sobrevenida de período o mapeo deja el único trabajo ya creado
en estado pendiente de configuración; no crea un trabajo lateral ni revierte
inventario ya confirmado.

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
parte del commit operacional y jamás es fuente para decisiones de inventario,
cartera, fiscal o contabilidad.

Para ventas mantiene una proyección física versionada con:

- documento y snapshots de cliente/vendedor/bodega;
- hechos firmados de líneas de venta y devolución;
- impuestos y medios de pago declarados;
- costo reconocido congelado;
- totales diarios conciliables.

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
cuenta o aplicación de pagos— consultan el estado del trabajo contable y no dan
por aplicado un efecto mientras siga pendiente.

## 8. Pruebas obligatorias

- operación completada sin escritura en tablas financieras ni de reporting;
- una señal por motor aplicable y entrega duplicada idempotente;
- submayor y libro mayor en el mismo commit contable;
- fallo entre submayor y asiento revierte ambos;
- venta de crédito, pago parcial, devolución y aplicación;
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

Un nuevo efecto financiero extiende el contrato y procesador contable existentes.
Un nuevo documento fiscal extiende el motor fiscal. Un reporte pequeño agrega una
consulta indexada; una proyección nueva exige evidencia y el contrato anterior.
No se crea una quinta cola dentro del ciclo documental, otro motor financiero ni
una tabla de jobs específica sin una nueva decisión que reemplace expresamente
esta arquitectura.
