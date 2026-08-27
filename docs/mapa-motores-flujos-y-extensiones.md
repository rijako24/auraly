# Mapa de motores, flujos y puntos de extension

Este documento responde qué componente es dueño de cada efecto y dónde se agrega una capacidad sin duplicar código. Es operativo; las decisiones detalladas enlazadas siguen siendo la autoridad de diseño.

## Regla de navegación

| Necesidad | Extender | No crear ni escribir directamente |
| --- | --- | --- |
| Nuevo documento definitivo con inventario | contrato documental, `IConfirmedDocumentHandler` y registro DI del motor documental | otro engine, poller, job table o escritura desde endpoint |
| Efecto de inventario | handler documental → `SqlInventoryLedgerWriter`; operaciones dedicadas además usan `SqlInventoryOperationProcessor` | SQL a `InventoryBalances`/`InventoryMovements`, segundo kardex o motor |
| Nuevo documento fiscal DIAN | snapshot/regla del `FiscalProcessingCoordinator` y workers fiscales existentes | worker DIAN por módulo, tenant o tipo |
| Nuevo asiento automático | política/regla del `AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor` | asiento desde API o segundo posting service |
| Nuevo efecto de CxC/CxP, pago, aplicación, crédito o anticipo | contrato y transacción del único `SqlAccountingPostingProcessor` | handler operacional, worker financiero adicional o job lateral |
| Nueva proyección de alto volumen | motor de reporting existente, con métrica, idempotencia, rebuild y benchmark documentados | consolidado preventivo o job por reporte |
| Nueva opción de dropdown | seed/maestro → store de aplicación → endpoint de catálogo → `useReferenceOptions` | array `{value,label}`, switch de labels o prompt |
| Nuevo endpoint | contrato/caso de uso de aplicación; adapter de persistencia en infraestructura | SQL o reglas de dominio en Minimal API |
| Regla dependiente de fecha/hora | `IBusinessClock` y `TimeProvider` | `DateTime.Now/UtcNow` dentro de la regla |
| Nuevo comportamiento conversacional | configuración, fact/signal/action/outcome y operación existentes según el manual | condición de tenant o vocabulario comercial en el engine |
| Nueva regla o concepto de nómina | módulo `Payroll`, rule sets versionados y calculador determinístico | salarios en `Employees`, listas locales o segundo motor contable/fiscal |

## Documento e inventario

`API/POS/importador` → confirma documento y trabajo durable → `DocumentProcessingWorker` → `DocumentProcessingEngine` → `IConfirmedDocumentHandler` → documento e inventario → marca job procesado y outbox → publica señales contable, fiscal y reporting aplicables.

- SQL es la fuente de orden, lease, estado e idempotencia; la cola solo despierta consumidores.
- Cada tipo documental tiene un handler. El handler no es un motor nuevo.
- `SqlInventoryLedgerWriter` es el único writer lógico de balances y movimientos. Centraliza bloqueo, existencia, costo, cantidad, valor, upsert y kardex.
- `SqlInventoryOperationProcessor` calcula conteos, ajustes, traslados, conversiones y averías, pero entrega la escritura al writer común.
- POS sale, recepción, devolución de venta y devolución de compra usan el mismo writer con la política de valoración correspondiente.

Para agregar un efecto: extender el contrato/handler correcto, elegir una política de valoración existente o modelar una nueva allí, agregar prueba de idempotencia/concurrencia y no tocar las tablas desde otro componente.

## Fiscal/DIAN

Documento operativo confirmado → proceso fiscal durable → `FiscalProcessingCoordinator` → `FiscalGenerationWorker` → artefacto firmado/CUFE-CUDE → `FiscalSubmissionWorker` → consulta/resultado DIAN.

- Generación y envío pueden usar colas diferentes: siguen siendo stages del mismo motor.
- Service Bus, RabbitMQ e in-process son adapters equivalentes.
- Un retry fiscal no repite inventario, caja, cartera ni contabilidad.
- Folios, snapshots, intentos, track IDs, artefactos y estados pertenecen al módulo fiscal.

## Contabilidad

Documento contabilizable → trabajo durable único → `AccountingProcessingCoordinator` → `SqlAccountingPostingProcessor` → submayores CxC/CxP, pagos y aplicaciones + entry/lines en una transacción → estado final o error reintentable.

- `AccountingProcessingPolicy` es la lista canónica de tipos soportados.
- La unicidad por documento fuente evita doble asiento.
- Agregar un documento significa extender la política y el posting existente, con prueba; no copiar la lista ni insertar asientos desde el módulo origen.
- El motor documental no escribe saldos financieros ni aplicaciones. No existe un worker financiero distinto del motor contable.
- Un documento financiero puro guarda `AccountingSourceDocuments` y `AccountingPostingJobs` en su transacción de aceptación; no crea job, payload ni secuencia operacional.

## Nómina

Contrato y novedades → cálculo determinístico en borrador → aprobación inmutable
→ fuente/trabajo contable durable → coordinador contable existente. Al cierre
mensual, las liquidaciones aprobadas se consolidan por trabajador y período →
outbox `ElectronicPayrollPrepared` → extensión fiscal certificada → XML, CUNE,
firma, transmisión e intentos DIAN. Habilitación usa `SendTestSetAsync` y
`GetStatusZip`; producción usa `SendNominaSync`. Payroll activa el coordinador y
consulta el resultado, pero no duplica ni suplanta ese pipeline.

- `Payroll` es el único propietario de relaciones laborales, reglas, conceptos,
  acuerdos de deducción, liquidaciones, comprobantes y períodos electrónicos.
- El cálculo es síncrono y determinístico; no crea cola ni tabla de jobs.
- Una aprobación no mueve inventario y no entra al motor documental.
- Agenda/disponibilidad de empleados no representa asistencia laboral.
- Contabilidad y fiscal se extienden por sus puntos canónicos; no se escriben
  asientos ni estados DIAN desde Payroll.
- Diseño propietario: `decision-nomina-electronica-integrada.md`.

## Reporting

Documento comercial completado → `SalesReportingProcessingCoordinator` → cola
`auraly-sales-reporting` → proyección idempotente → hechos y consolidados.

- Reporting no bloquea ni decide operación, fiscal o contabilidad.
- `reporting.SalesReportingJobs` es su única tabla durable de procesamiento por documento; checkpoints, hechos y consolidados no son colas de trabajo.
- Nómina aprobada/pagada/emitida → `PayrollReportingService` → definición en
  `reporting.PayrollReportDefinitions` → consulta autorizada de snapshots por
  `SqlPayrollReportingStore` → `ReportViewer`. Es un reporte operativo nativo de
  Reporting cercano al módulo propietario; no consulta desde la página, no
  recalcula nómina y no crea una cola o proyección paralela sin benchmark.
- Ventas usa proyección física por su volumen, costo y agregaciones.
- Reportes pequeños consultan tablas propietarias indexadas.
- Una nueva proyección exige métrica versionada, benchmark, idempotencia,
  reconciliación y rebuild; no crea automáticamente otra tabla de jobs.

## Conversacional

Canal inbound → recibo/idempotencia/debounce → conversación y `SettingsJson` → posición determinista → plan estructurado del LLM → coordinación determinista → `IAgentOperation` → outcomes/efectos → persistencia y respuesta.

El LLM propone; el coordinador valida y decide. Reglas comerciales, facts, signals, acciones y copy configurable viven en configuración. Operaciones consultan catálogo, reservas, precios y pagos mediante sus casos de uso propietarios. **DT-009 está diferida:** este documento describe la topología actual, no autoriza una refactorización interna del motor.

## Catálogos y dropdowns

Flujo: `reference.Options`/maestro específico → seed o administración autorizada → `IReferenceOptionStore` → `ReferenceOptionService` → `GET /api/commerce/v1/reference-options/{catalogCode}` → `useReferenceOptions` → selector.

Los códigos son estables; label, descripción, activación y orden pertenecen a tabla. Un enum puede tipar códigos, pero no reemplaza la tabla cuando el usuario elige. Mapas de icono, color y layout pueden quedar en UI porque son presentación.

Catálogos iniciales: medios de pago, tipos de documento de venta, presentaciones de compra, tipos de operación de inventario y tipos de bot. Al tocar otro selector heredado, se agrega su slice completo en esta ruta; no se crea otro endpoint genérico ni otra lista local.

## Fronteras y persistencia

- API: autenticación, autorización, binding, error HTTP y llamada al caso de uso.
- Application: orquestación y políticas; depende de contratos, no de SQL/HTTP.
- Domain: invariantes y tipos sin infraestructura.
- Infrastructure: consultas, transacciones, brokers y proveedores externos.
- Database: schemas, constraints, índices y seeds desplegables.

`Auraly.Api` no admite SQL directo ni sentencias DML embebidas. La persistencia se implementa con EF Core/LINQ o mediante procedimientos almacenados versionados en el proyecto DACPAC; una prueba arquitectónica exige un baseline de cero. Las migraciones de tablas heredadas en `dbo` al schema propietario conservan compatibilidad, cutover, rollback y medición.

## Colas, retries y observabilidad

Una cola nueva requiere propietario, job durable, clave de orden, idempotency key, retry/backoff, dead-letter, métricas y runbook. El consumidor tolera entrega al menos una vez. Nunca usar una cola paralela para saltarse un bloqueo permanente ni duplicar un motor por ambiente.

Registrar correlation/job/document/business ID, intento, transición, duración y error seguro. No registrar secretos, tokens, payloads fiscales completos ni datos personales innecesarios.

## Checklist para ubicar un cambio

1. Buscar contrato, engine/coordinator, handler, writer, tabla, endpoint, DI y tests existentes.
2. Nombrar el propietario de la decisión y de cada escritura.
3. Seguir la tabla de navegación; si ninguna capacidad parece propietaria, detener la implementación y elevar la decisión. Una funcionalidad no crea otro motor/cola como atajo.
4. Confirmar tenant/business scope, autorización, idempotencia, orden, tiempo y compatibilidad.
5. Implementar el slice completo y actualizar este mapa solo si cambió la topología.
6. Pasar build, pruebas, lint y compuertas de arquitectura.
7. Auditar el diff final contra `AGENTS.md`, estándares, invariantes y decisiones propietarias; corregir incumplimientos y repetir los checks afectados.

## Referencias

- `docs/invariantes-arquitectonicas-auraly.md`
- `docs/estandares-de-ingenieria.md`
- `docs/decision-motor-documental-ordenado-y-efectos-intrinsecos.md`
- `docs/decision-motor-documentos-orden-inventario-contabilidad.md`
- `docs/implementation/inventory-operations-engine-design.md`
- `docs/implementation/dian-fiscal-engine-design.md`
- `docs/implementation/accounting-operational-design.md`
- `docs/agent-engine-manual.md`
