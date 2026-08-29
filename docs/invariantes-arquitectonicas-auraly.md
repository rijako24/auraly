# Invariantes arquitectonicas de Auraly

Este documento contiene reglas transversales propias de Auraly. No reemplaza los disenos detallados de cada modulo: define sus propietarios canonicos y evita que una nueva funcionalidad cree motores, escrituras, catalogos o listas paralelas.

Estas reglas aplican a backend, workers, base de datos, integraciones, POS y admin. Una excepcion requiere una decision explicita con motivo, alcance, migracion, observabilidad y condicion de retiro.

## 1. Un motor canonico por capacidad

Auraly puede tener varios motores y varias colas. La invariante es que cada capacidad de dominio tiene **un solo motor canonico**.

- Un motor es el propietario de la decision, el orden, la idempotencia, la transaccion y el estado durable de una capacidad.
- Una cola es transporte y activacion. Tener varias colas, stages o perfiles de despliegue no crea varios motores si todos desembocan en el mismo contrato de aplicacion y estado canonico.
- Un handler agrega un tipo documental al motor existente. No es un motor nuevo.
- Un worker, hosted service o adapter para Service Bus, RabbitMQ o ejecucion in-process aloja el mismo motor. No puede reimplementar sus reglas.
- Escalar significa aumentar consumidores/particiones conservando claves de orden, leases, idempotencia y contratos; no bifurcar la logica.
- Ningun endpoint, modulo o integracion puede escribir directamente una proyeccion que pertenece a un motor para evitar su pipeline.

Antes de crear un processor, engine, worker, job table, queue o background service se debe identificar la capacidad propietaria. Toda funcionalidad nueva debe extender uno de los propietarios canonicos y sus puntos de extension; crear otro motor no es un punto de extension permitido para una tarea funcional. Si ninguna capacidad vigente parece ser propietaria, se detiene la implementacion y se eleva una decision arquitectonica explicita en vez de inventar un motor, writer, job o cola desde la pantalla, endpoint o modulo solicitante.

## 2. Mapa de propietarios actuales

| Capacidad | Motor y puntos de extension canonicos | Estado durable principal | Diseno vigente |
| --- | --- | --- | --- |
| Documentos operativos con efecto fisico y efectos intrinsecos | `DocumentProcessingEngine` + `DocumentProcessingWorker`; nuevos tipos implementan `IConfirmedDocumentHandler` | `DocumentProcessingJobs`, movimiento confirmado y cursor de procesamiento | `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md` |
| Inventario | Se ejecuta dentro del motor documental. Las operaciones dedicadas convergen en `SqlInventoryOperationProcessor`; ventas, entradas y devoluciones aplican sus efectos desde su `IConfirmedDocumentHandler` canonico | `InventoryBalances`, `InventoryMovements`, `InventoryOperations` y sus lineas | `implementation/inventory-operations-engine-design.md` |
| Fiscal/DIAN | `FiscalProcessingCoordinator` + `FiscalGenerationWorker` + `FiscalSubmissionWorker` | `FiscalDocuments`, `FiscalDocumentProcesses`, snapshots, artefactos e intentos | `implementation/dian-fiscal-engine-design.md` |
| Contabilidad | `AccountingProcessingCoordinator` + `SqlAccountingPostingProcessor` | `AccountingSourceDocuments`, `AccountingPostingJobs`, `AccountingEntries` y lineas | `implementation/accounting-operational-design.md` |
| Reporting de ventas | `SalesReportingProcessingCoordinator` + `SqlSalesReportingProcessor` | `reporting.SalesReportingJobs`, hechos y consolidados | `decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md` |
| Conversacional | Pipeline determinista descrito en el manual del agente | Conversacion, estado, facts, recibos y configuracion del agente | `agent-engine-manual.md` |
| Nómina | Calculador determinístico del módulo `Payroll`; las salidas extienden los motores contable y fiscal existentes | `payroll.Employments`, conceptos, reglas, novedades, liquidaciones, pagos y períodos electrónicos | `decision-nomina-electronica-integrada.md` |

Si aparece una nueva capacidad con semantica realmente distinta, primero se registra su limite, fuente de verdad, orden, idempotencia, transaccion, transportes y relacion con los motores existentes mediante una decision arquitectonica. No se crea implicitamente desde una pantalla o endpoint.

## 3. Motor documental e inventario

- Todo documento definitivo genera el movimiento/trabajo durable canonico y se procesa mediante `DocumentProcessingWorker` y `DocumentProcessingEngine`.
- Un nuevo tipo de documento que mueve inventario se integra mediante `IConfirmedDocumentHandler` y el registro DI existente. Un documento exclusivamente financiero entra directamente al unico motor contable. Ninguno crea otra tabla de jobs, poller, drain SQL, cola general ni engine.
- Los efectos de inventario se aplican solamente dentro de la transaccion ordenada del motor documental.
- `InventoryBalances` es la proyeccion autoritativa rapida y conciliable; `InventoryMovements` es el kardex durable. Ningun controller, API, admin, POS, importador o modulo los modifica por fuera de los handlers canonicos.
- Existe exactamente un `InventoryBalances` por negocio, bodega y producto, incluidas las bodegas del sistema; se aprovisiona en cero al crear cualquiera de los dos maestros. Toda consulta operativa de existencias lee esa proyeccion y nunca suma ni reconstruye el saldo desde `InventoryMovements`. Cada movimiento confirmado actualiza el balance y agrega el kardex en la misma transaccion; el conteo fisico converge por ese mismo motor.
- Conteos, ajustes, traslados, conversiones y danos reutilizan `SqlInventoryOperationProcessor`. Un nuevo tipo de operacion extiende contratos, reglas, handler y processor existentes.
- Ventas, entradas, devoluciones y otros documentos que afectan inventario conservan su handler documental canonico, pero no construyen un motor de inventario alterno.
- Un retry o fallo de DIAN/contabilidad nunca reaplica inventario, pagos, caja o cartera ya procesados.
- Los bloqueos de bodega/producto, orden canonico, costo reconocido, secuencia e idempotencia se preservan en cualquier extension.

## 4. Motor fiscal/DIAN

- Facturas, notas credito y futuros documentos fiscales comparten `FiscalProcessingCoordinator`, workers, leases, procesos, artefactos e intentos.
- Generacion y envio son stages del mismo motor fiscal; sus colas pueden ser distintas sin convertirse en implementaciones distintas.
- Service Bus y RabbitMQ son adapters de transporte. Ambos deben resolver los mismos coordinators/workers y producir la misma semantica durable.
- No se crea un worker DIAN por modulo comercial, tipo de documento, tenant o modo SaaS/on-premise.
- Un nuevo documento fiscal extiende el snapshot/contrato y las reglas del motor existente. No crea tablas paralelas de folios, estados, intentos o artefactos.
- Reintentos, timeouts ambiguos, track IDs, firma, CUFE/CUDE y estados DIAN se resuelven en el motor fiscal, nunca en controllers o UI.

## 5. Motor contable

- Todo asiento automatico converge en `AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor`.
- Un nuevo documento contabilizable agrega su regla de posting al motor existente y reutiliza `AccountingPostingJobs` y la unicidad por documento fuente.
- Su fuente inmutable pertenece a `AccountingSourceDocuments`; el job contable no depende por FK de `DocumentProcessingJobs`.
- No se crea un posting service por modulo, un asiento directo desde un endpoint ni una segunda tabla de trabajos contables.
- La contabilidad corre en su transaccion durable e idempotente. Un fallo o configuracion pendiente no reaplica los efectos operativos ya confirmados.
- Los transportes SaaS, on-premise e in-process ejecutan el mismo processor y no contienen reglas contables.

## 6. Colas y transportes

Se permiten varias colas cuando representan responsabilidades o stages diferentes: procesamiento documental, fiscal, contable, campanas, integraciones u otros trabajos durables. Se aplican estas reglas:

- La cola no es la fuente de verdad del negocio. SQL conserva trabajo, orden, estado e idempotencia; el broker activa.
- Cada mensaje identifica un trabajo durable y su consumidor es seguro ante entrega al menos una vez.
- El orden se define por la clave del dominio —por ejemplo negocio, entidad legal, documento o conversacion— y no por una cola global para todos los tenants.
- Retry, backoff y dead-letter pertenecen a la politica del proceso. No se agrega otra cola para esconder un error permanente o saltar un trabajo bloqueante.
- Service Bus, RabbitMQ e in-process son perfiles del mismo contrato. Las diferencias de infraestructura se resuelven en adapters.
- Una cola nueva requiere propietario, contrato, clave de idempotencia, orden, retencion, retry, dead-letter, metricas y runbook.

### 6.1 Nómina

- `Payroll` no crea un motor documental, contable o fiscal alterno.
- La liquidación se calcula sin worker y queda reproducible mediante snapshots y
  reglas versionadas.
- La aprobación crea fuentes/trabajos contables durables; solamente
  `SqlAccountingPostingProcessor` escribe asientos.
- La emisión de nómina electrónica usa coordinador, procesos, artefactos, firma,
  intentos y transportes fiscales canónicos. Solo una respuesta DIAN aceptada
  equivale a presentación; generar o firmar el snapshot mensual no basta.
- Certificado, endpoint y ambiente se reutilizan del emisor fiscal versionado;
  `SoftwareID`, PIN seguro y `TestSetId` se configuran y congelan por la familia
  de nómina.
- Horarios de agenda no son registros laborales y no alimentan el cálculo.

## 7. Catalogos, tablas y dropdowns

Todo selector de datos de negocio debe consumir un catalogo canonico persistido. No se permiten listas quemadas de opciones en TypeScript, C#, prompts, JSON de UI o componentes.

La existencia de una lista quemada actual no la convierte en patron valido. No se agregan nuevos consumidores de ella; cuando una tarea toque ese selector o su contrato, debe migrarlo al catalogo canonico dentro del mismo slice o registrar de forma explicita el bloqueo y la migracion pendiente.

### 7.1 Regla de UI

- Un `select`, combobox, radio group o dropdown que elija un valor de negocio carga sus opciones desde API/query respaldada por tabla.
- La API entrega como minimo codigo/ID estable, label, estado activo y orden; tambien scope de tenant/business y metadata cuando aplique.
- El frontend no mantiene arrays paralelos `{ value, label }`, mapas de traduccion de estados ni switches para reconstruir el catalogo.
- La validacion del backend usa el ID/codigo canonico y verifica existencia, vigencia, scope y autorizacion. No confia en el label enviado por UI.
- Valores inactivos usados historicamente se pueden mostrar en registros existentes, pero no ofrecer para nuevas selecciones.
- Catalogos grandes usan busqueda/paginacion; los pequenos pueden cachearse con una politica explicita de invalidacion y scope.
- Cualquier selector de terceros usa `Parties` como identidad canonica y filtra el rol operativo requerido (`Customer`, `Supplier`, `Seller`, `Carrier`, `Employee` o `User`). Siempre busca y pagina en servidor; un endpoint de opciones no puede precargar en segundo plano la lista completa de terceros.
- El valor persistido por cada flujo sigue siendo el identificador que le pertenece: `PartyId` cuando el contrato consume la identidad, o el ID del rol (`CustomerId`, `SupplierId`, etc.) cuando consume esa relacion operativa. El selector no crea identidades ni codigos paralelos.

Los menus de acciones de interfaz —por ejemplo editar, ver o eliminar— y opciones puramente visuales que no se persisten ni representan dominio no son catalogos de negocio.

### 7.2 Enums

Un enum es valido solamente para un conjunto tecnico o de workflow cerrado, universal, no administrable y exhaustivo en compilacion.

- Si el valor aparece en un dropdown de negocio, tiene label administrable/traducible, puede activarse, ordenarse, variar por tenant o crecer sin despliegue, debe estar respaldado por tabla.
- Cuando conviven enum y tabla, la tabla es la fuente de opciones y metadata; el enum solo aporta tipado para codigos estables. Deben existir constraint/FK y pruebas que impidan divergencia.
- Un estado interno de maquina que nunca selecciona el usuario puede permanecer como enum y constraint `CHECK` sin tabla, siempre que sea realmente cerrado.
- No usar enum para maestros como motivos, categorias, ubicaciones, impuestos, unidades, medios configurables, responsabilidades fiscales o tipos administrables.

### 7.3 Diseno minimo de tablas de catalogo

Segun el alcance, un catalogo incluye:

- ID estable y `Code` unico e inmutable;
- `Name`/`Label` y, si aplica, descripcion;
- `IsActive` y `SortOrder`;
- scope global, tenant o business expresado con claves y constraints;
- metadata tipada necesaria, sin JSON generico como salida facil;
- auditoria/row version cuando sea administrable;
- foreign keys desde las entidades que lo consumen.

Los catalogos globales/oficiales se cargan mediante seeds idempotentes del proyecto SQL con codigos oficiales o IDs deterministas. Los catalogos del negocio se administran por casos de uso autorizados. No se insertan datos faltantes silenciosamente durante una consulta o startup.

## 8. Una regla y una escritura, un propietario

- Una regla no se copia entre handler, processor, controller, frontend, SQL, seed o integracion. Se invoca al propietario o se comparte un contrato estable.
- Una tabla/proyeccion tiene un unico writer logico. Otros modulos solicitan el efecto mediante el caso de uso o evento canonico.
- Un adapter traduce; no decide. Un orchestrator coordina; no reimplementa las reglas internas del motor.
- Los tests expresan expectativas y contratos, pero no copian el algoritmo productivo para calcular el mismo resultado.
- Una migracion temporal de ruta exige telemetria, compatibilidad definida y condicion de retiro. No quedan dos rutas activas indefinidamente "por seguridad".

## 9. Preflight para motores y catalogos

Antes de implementar:

1. Buscar engine, processor, worker, handler, coordinator, registros DI, jobs, colas, tablas, endpoints y tests equivalentes.
2. Identificar la capacidad y el propietario del mapa anterior.
3. Verificar si el cambio es un nuevo tipo/handler/stage del motor actual o una capacidad realmente nueva.
4. Localizar todas las escrituras a las tablas afectadas y evitar un segundo writer.
5. Para una opcion de UI, localizar tabla, seed, query/API, permisos, filtros de scope y consumidores. Si no existen, disenar el slice completo; no empezar por el array del frontend.
6. Definir idempotencia, orden, transaccion, retry, dead-letter, observabilidad y prueba de no duplicacion.
7. Actualizar la decision detallada propietaria cuando cambie el contrato o la topologia.

## 10. Definition of Done especifica

- [ ] La capacidad reutiliza el motor canonico y sus puntos de extension.
- [ ] No se agrego otro engine, processor, job table, writer o cola con responsabilidad duplicada.
- [ ] Los adapters de transporte no contienen reglas de negocio.
- [ ] Inventario, DIAN y contabilidad no pueden reaplicarse entre si durante retries.
- [ ] Toda opcion de negocio visible en UI proviene de tabla/API; no existe una lista quemada nueva.
- [ ] Los enums expuestos como catalogo estan respaldados por tabla o fueron justificados como estado tecnico cerrado.
- [ ] IDs/codigos, scopes, FKs, activacion, orden y seeds del catalogo son consistentes.
- [ ] Hay pruebas de idempotencia, concurrencia, orden, retry y no duplicacion proporcionales al proceso.
- [ ] Documentacion, DI, schema, seeds, backend y frontend quedaron alineados.
- [ ] Se audito el diff final, despues de implementar, contra `AGENTS.md`, estandares, estas invariantes y las decisiones propietarias; todo incumplimiento fue corregido y los checks afectados se repitieron.

## Referencias canonicas

- `docs/decision-motor-documental-ordenado-y-efectos-intrinsecos.md`
- `docs/decision-motor-documentos-orden-inventario-contabilidad.md`
- `docs/implementation/inventory-operations-engine-design.md`
- `docs/implementation/dian-fiscal-engine-design.md`
- `docs/implementation/accounting-operational-design.md`
- `docs/decision-despliegue-onpremise-seguridad-maestros-semillas-calidad.md`
- `docs/agent-engine-manual.md`
