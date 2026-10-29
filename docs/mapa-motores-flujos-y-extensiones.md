# Mapa de motores, flujos y puntos de extension

> Diseño en preparación, 2026-10-20: la evolución del PUC, comprobantes, cruces y
> fuentes de verdad se especifica en la sección 13 de
> [comprobantes abiertos](implementation/accounting-open-vouchers-design.md).
> Amplía los contratos del motor financiero existente. Asociaciones de compra y
> revisiones de costo pertenecen a [Purchasing](implementation/goods-receipts-design.md);
> su cierre técnico de valoración no autoriza otro writer de inventario ni
> un promedio paralelo. Cada documento distingue los slices locales implementados
> y su evidencia de los criterios todavía pendientes. La asociación de comprobantes
> en la recepción inicial está en verificación; no habilita revisiones posteriores.

Este documento responde qué componente es dueño de cada efecto y dónde se agrega una capacidad sin duplicar código. Es operativo; las decisiones detalladas enlazadas siguen siendo la autoridad de diseño.

Para creación de empresas, planes, pagos de suscripción, ampliaciones y cupos de usuarios, vendedores, cajas, nómina o documentos DIAN, la autoridad es `docs/decision-aprovisionamiento-suscripciones-cuotas-y-wompi.md`. El alta física continúa en `ITenantProvisioningStore`; webhook y polling convergen antes de invocarlo.

## Regla de navegación

| Necesidad | Extender | No crear ni escribir directamente |
| --- | --- | --- |
| Nuevo documento definitivo con inventario | contrato documental, `IConfirmedDocumentHandler` y registro DI del motor documental | otro engine, poller, job table o escritura desde endpoint |
| Efecto de inventario | handler documental → `SqlInventoryLedgerWriter`; operaciones dedicadas además usan `SqlInventoryOperationProcessor`. `InventoryBalances` es el saldo actual por negocio+bodega+producto y `InventoryMovements` solo el kardex historico | SQL a `InventoryBalances`/`InventoryMovements`, reconstruir existencias sumando movimientos, segundo kardex o motor |
| Nuevo documento fiscal DIAN | snapshot/regla del `FiscalProcessingCoordinator` y workers fiscales existentes | worker DIAN por módulo, tenant o tipo |
| Nuevo asiento automático | política/regla del `AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor` | asiento desde API o segundo posting service |
| Nuevo efecto de CxC/CxP, pago, aplicación, crédito o anticipo | contrato y transacción del único `SqlAccountingPostingProcessor`; el trabajo congela si además requiere asiento | handler operacional, worker financiero adicional o job lateral |
| Resolver auxiliares de un banco | PUC → `accounting.BankPostingAccount`; aceptación contable → `accounting.BankAccountsFreeze` fija la configuración en el trabajo existente; conciliación → `accounting.BankBookAccounts` | otro catálogo de tesorería, FK inverso editable, resolver de nuevo una cuenta ya aceptada o copiar saldos/asientos por sede |
| Nueva proyección de alto volumen | motor de reporting existente, con métrica, idempotencia, rebuild y benchmark documentados | consolidado preventivo o job por reporte |
| Nueva opción de dropdown | seed/maestro → store de aplicación → endpoint de catálogo → `useReferenceOptions` | array `{value,label}`, switch de labels o prompt |
| Nuevo endpoint | contrato/caso de uso de aplicación; adapter de persistencia en infraestructura | SQL o reglas de dominio en Minimal API |
| Regla dependiente de fecha/hora | `IBusinessClock` y `TimeProvider` | `DateTime.Now/UtcNow` dentro de la regla |
| Nuevo comportamiento conversacional | configuración, fact/signal/action/outcome y operación existentes según el manual | condición de tenant o vocabulario comercial en el engine |
| Nueva regla o concepto de nómina | módulo `Payroll`, rule sets versionados y calculador determinístico | salarios en `Employees`, listas locales o segundo motor contable/fiscal |
| Pago de suscripción o ampliación | ciclo de pagos existente → confirmación idempotente → suscripción/entitlements | aprovisionar desde redirect, webhook o poller por separado |
| Configuración y rotación Wompi | `IntegrationConnections` + `IIntegrationSecretProtector`; el intento fija `PaymentTransactions.MerchantConfigurationVersion` | llave plana, cuenta global hardcodeada, tabla o integración Wompi paralela |
| Espera fiscal por cupo y recuperación POS offline | `FiscalDocumentProcesses` + motor fiscal y outbox POS existentes; usar `PendingCapacity` y despertar el mismo proceso al ampliar/renovar | otra factura, numeración, cola, tabla de jobs o worker de reenvío |
| Renovación mensual y mora | `TimedProcessScheduler` reclama `ScheduledAutomationJobs` (`TenantSubscriptionLifecycle`) → orden de renovación → pago existente → factura de servicio en motor documental/fiscal + `SqlAccountingPostingProcessor` | fecha paralela en la orden, tabla/timer nuevo, auto-renovar sin cobro, CxC paralela o desactivar `Tenant.IsActive` por mora |
| Facturar productos y servicios | `SalesDocuments` es el encabezado común; `Products` → `SalesDocumentLines` conserva venta física y `BillableServices` → `SalesDocumentServiceLines` funciona online; ambos usan extensiones tipadas de contabilidad, fiscal y reporting existentes | ampliar POS/inventario para servicios, crear otro encabezado, writer, cartera, generador UBL, cola o worker DIAN |
| Entregar factura aceptada | transición `DianAccepted` → outbox de entrega → contenedor `AttachedDocument`/respuesta DIAN + representación PDF del almacén fiscal | enviar antes de aceptación, regenerar XML en la plantilla o reenviar DIAN desde correo |
| Resolver precio comercial de productos | `CommercePriceResolver` compone los calculadores canónicos de canal y promoción con las líneas del documento ya cargadas | recalcular en SQL, SQLite, pedido, endpoint o UI |
| Imprimir pedidos | `OrderService` autoriza y solicita el snapshot capturado; `SqlOrderStore` lo carga en lote; `PosPrintTemplateCatalog.Order` y el pipeline de impresión existente lo representan | consultar cada pedido/línea por separado, convertirlo en venta o crear otro renderer/servicio de impresión |

La marca de los comprobantes se rige exclusivamente por la sección
**Facturación y marca de los comprobantes** de las invariantes arquitectónicas.
Se prepara al entrar en la vista o por la sincronización de la caja; confirmar
ventas, pedidos y devoluciones no consulta ni recibe el logo. Imprimir reutiliza
la copia disponible y un fallo de marca/impresora no cancela ni repite una
operación confirmada. La reimpresión de devoluciones comparte configuración y
transportes de facturas, conservando su snapshot y plantilla operacional propia.

| Consultar o editar la empresa propia | `tenant.profile.read/update` → `TenantsController` limita el recurso a `User.TenantId` → `TenantService`; el plan se proyecta en solo lectura desde la suscripción canónica | conceder `tenants.*` al administrador cliente, confiar en el `tenantId` del navegador o duplicar el perfil empresarial |
| Cargos de facturación | catálogo versionado `InvoiceChargeService`/`SqlInvoiceChargeStore` → `InvoiceChargeCalculation` compartido por borrador online y Edge → snapshot de la venta → writer común de Gastos | otro medio de pago, producto ficticio, cálculo en UI o tabla paralela de cargos emitidos |
| Gasto manual o asociado a una factura | `ExpenseService` (conceptos con cuenta automática y preview con `WithholdingService`, ver `implementation/expenses-account-selection-and-withholdings-design.md`) o handler de venta → `SqlExpenseStore.PersistAcceptedAsync` → fuentes/trabajos financieros canónicos → `SqlAccountingPostingProcessor` abre CxP y marca el gasto procesado | consumir cursor operativo para gastos nuevos, abrir CxP desde POS o volver a registrar gasto al pagar al proveedor |
| Recaudar CxC o pagar CxP | wizard compartido → `ReceivablesService`/`PayablesService` → aceptación transaccional de aplicaciones y `payments[]` → `AccountingSourceDocuments`/`AccountingPostingJobs` → `SqlAccountingPostingProcessor`; desde POS preparado, Edge abre o retoma la sesión local, confirma su registro en el servidor, guarda primero la proyección mínima en SQLite y luego actúa como proxy online autenticado por dispositivo y sesión. Si la llamada al servidor falla, elimina esa proyección provisional sin programar un reintento automático del pago; un reintento manual conserva la clave idempotente. El cierre preparado lee solo proyecciones confirmadas. Los historiales paginados incluyen medios y aplicaciones por factura en el mismo viaje de lectura. | aceptar en servidor antes de escribir SQLite en caja preparada, usar `DocumentProcessingJobs`, crear otro motor, refetch automático posterior o contabilizar desde UI/API |

La autorización de ese wizard depende de la superficie: POS web y preparado usan `pos.receivables.payments.create` o `pos.payables.payments.create`, solo consultan facturas pendientes del tercero elegido y abren o retoman la sesión operativa antes de confirmar; administración conserva `receivables.read`/`receivables.payments.create` y `payables.read`/`payables.payments.create`. El rol Cajero recibe solo los permisos POS; las dos superficies invocan los mismos servicios, stores y motor contable.
| Importar cartera preexistente | lote acotado de `ReceivablesService`; al importar por identificación resuelve el cliente y su sede activa (preferentemente principal) antes de crear fuentes contables `PreexistingReceivable` → `SqlAccountingPostingProcessor` crea CxC y asiento con contrapartida elegida | inventar venta, inventario, documento DIAN, tabla de importación o saldo directo fuera del motor |

La tirilla de cierre v6 muestra por separado las ventas, devoluciones, entradas y salidas manuales y el efecto neto de otros movimientos por medio de pago. En la caja preparada, su sección de cartera identifica los recaudos y pagos con el número de recibo conservado en SQLite; las versiones de tirilla ya emitidas permanecen inmutables. Los reintegros de devoluciones guardan la proyección local antes de enviar al servidor, la retiran si la llamada falla y solo entran al cierre tras confirmación. Las devoluciones iniciadas dentro de POS conservan la sesión de esa caja; las iniciadas desde administración abren o retoman la sesión web del negocio antes de confirmar en el mismo motor de devoluciones. La API preparada obtiene el permiso real del usuario para esa sede; el store de devoluciones valida negocio, tenant, usuario y dispositivo de la sesión preparada dentro de la transacción de aceptación. Una devolución web solo puede asociarse a una sesión web.

La navegación muestra una sola entrada **Empresa**. Con `tenant.profile.read`
abre el perfil propio; con el permiso de plataforma `tenants.read` abre el listado
administrativo y sustituye el acceso propio en el menú. Sidebar, menú móvil y
búsqueda comparten esta regla en `sidebar-nav-config`; no consulta datos ni amplía
los permisos de servidor. Los contratos de perfil y administración conservan sus
rutas y autorizaciones independientes.

## Documento e inventario

`API/POS/importador` → confirma documento y trabajo durable → `DocumentProcessingWorker` → `DocumentProcessingEngine` → `IConfirmedDocumentHandler` → documento e inventario → marca job procesado y outbox → publica señales contable, fiscal y reporting aplicables.

- SQL es la fuente de orden, lease, estado e idempotencia; la cola solo despierta consumidores.
- Cada tipo documental tiene un handler. El handler no es un motor nuevo.
- `InventoryValuationCalculator` es el único propietario de las fórmulas de costo promedio, valor y costo reconocido; no conoce SQL ni tipos documentales.
- `SqlInventoryLedgerWriter` es la única puerta transaccional de valoración y escritura: bloquea y carga el conjunto, invoca la calculadora por movimiento en el orden del documento y persiste balance, revaloración común, kardex y notificación. `PostBatchAsync` admite las bodegas del mismo documento; los traslados conservan el costo de su salida dentro del lote. Conversión y producción usan dos fases dependientes, insumos y productos resultantes, en la misma transacción. Los handlers y el processor existente consumen esos lotes, sin otro writer ni fórmula.
- Ese mismo punto admite `ValueAdjustment`: importe firmado sin cantidad para valorar existencias del pool positivo, sin otra entrada física. Conserva los dos comandos del lote, valida el sobre de la sesión activa y devuelve el cambio real del valor almacenado. La revisión de cargos sobre recepciones trazables entra por `GoodsReceiptService` → `SqlGoodsReceiptStore` → el mismo motor documental y `SqlGoodsReceiptDocumentHandler` → este writer y `SqlAccountingPostingProcessor`; no abre otra CxP ni altera el comprobante original. Una recepción antigua sin origen trazable no se revaloriza automáticamente.
- La atribución por origen entra por ese mismo `PostBatchAsync`: `InventoryOriginAttribution` consume las entradas abiertas de la bodega y producto afectados; compartir costo promedio o precios no consume existencias ni orígenes de otra bodega. El writer conserva porciones inmutables en `inventory.MovementOrigins` y detalle abierto derivado en `inventory.OriginPositions`. `InventoryBalances` sigue suministrando cantidad/valor; su contexto nullable y secuencia observada de cobertura detectan escritores anteriores sin inventar recibos históricos. Traslados explícitos transportan porciones y devoluciones restauran el origen de su salida. Fabricación consume ingredientes y registra sus productos por el flujo actual, sin propagación retroactiva de cargos a productos terminados. No agrega comandos, workers, fuentes de saldo o un writer alterno.
- `SqlInventoryOperationProcessor` coordina conteos, ajustes, traslados, conversiones y averías, incluida la asignación explícita de merma, pero no calcula costo promedio ni escribe balances por fuera del writer común.
- `InventoryDemandResolver` es el único propietario de agrupar y convertir la demanda de presentaciones que comparten inventario con un producto padre. Los validadores online, Edge y de pedidos cargan hechos en lote y lo invocan internamente; el ledger writer conserva la resolución transaccional final.
- POS sale, recepción, devolución de venta y devolución de compra usan el mismo writer con la política de valoración correspondiente.
- El mismo writer conserva en `inventory.CostPoolEpochs` el contexto histórico del conjunto de sedes valorado y en `inventory.CostPoolHeads` su referencia actual y secuencia observada. No asignan secuencias ni almacenan un segundo saldo: `InventoryBalances` sigue siendo la autoridad de cantidad/valor. El vínculo nullable del kardex admite históricos y escritores anteriores sin inventar atribución por recepción. Las asignaciones de origen y los cargos posteriores siguen gobernados por `goods-receipts-design.md`.

Para agregar un efecto: extender el contrato/handler correcto, elegir una política de valoración existente o modelar una nueva allí, agregar prueba de idempotencia/concurrencia y no tocar las tablas desde otro componente.

## Precios comerciales

Líneas del documento → `CommercePriceResolver` → `PriceChannelResolver` por
cantidad acumulada del producto → `PromotionPriceResolver` sobre el documento →
precio efectivo, origen, canal y descuento promocional.

- SQL Server y SQLite son adaptadores de carga; no poseen aritmética de precios.
- POS online, POS Edge y creación de pedidos llaman al mismo coordinador.
- La búsqueda de catálogo usa `independentLines`; ventas y pedidos usan el contexto
  documental completo.
- La búsqueda online del POS pagina de 50 en 50: cada página ejecuta una sola vez
  `OnlineSalesProductSearch`, carga productos y política comercial en resultsets del
  mismo comando e invoca una vez `CommercePriceResolver`. El cliente recibe nombre,
  precio efectivo, origen y descuento promocional ya resueltos; no consulta ni
  recalcula por producto.
- Recuperar un pedido o venta pausada hidrata su snapshot sin resolver. La siguiente
  mutación comercial usa nuevamente el coordinador común.

## Fiscal/DIAN

Documento operativo confirmado → proceso fiscal durable → `FiscalProcessingCoordinator` → `FiscalGenerationWorker` → artefacto firmado/CUFE-CUDE → `FiscalSubmissionWorker` → consulta/resultado DIAN.

- Generación y envío pueden usar colas diferentes: siguen siendo stages del mismo motor.
- Service Bus, RabbitMQ e in-process son adapters equivalentes.
- Un retry fiscal no repite inventario, caja, cartera ni contabilidad.
- Folios, snapshots, intentos, track IDs, artefactos y estados pertenecen al módulo fiscal.
- Recepción, cargos, gastos, devolución, anulación y habilitación de documento
  soporte insertan su raíz, snapshot y proceso mediante
  `SqlFiscalDocumentStore.InsertPurchaseSupportSourcesAsync`, dentro de su
  transacción de aceptación. Conservan su numeración, cupo y construcción del
  snapshot; el participante no publica ni aplica efectos financieros. El soporte
  manual usa ese mismo participante desde la aceptación contable y aporta un
  snapshot neutral de adquisición, sin crear otro gasto o recepción. Su deuda y
  diario los aplica el processor contable existente. Los límites y la evidencia
  vigentes están en `accounting-open-vouchers-design.md`.
- Gastos, compras y ventas guardan las retenciones ya calculadas mediante
  `SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync`, dentro de su transacción
  existente. Comparte la escritura por lote de cabeceras y líneas; el cálculo
  continúa en `WithholdingService` y contabilidad solo consume los snapshots.

## Contabilidad

Documento contabilizable → trabajo durable único → `AccountingProcessingCoordinator` → `SqlAccountingPostingProcessor` → submayores CxC/CxP, pagos y aplicaciones + entry/lines en una transacción → estado final o error reintentable.

- `AccountingProcessingPolicy` es la lista canónica de tipos soportados.
- La unicidad por documento fuente evita doble asiento.
- Agregar un documento significa extender la política y el posting existente, con prueba; no copiar la lista ni insertar asientos desde el módulo origen.
- El motor documental no escribe saldos financieros ni aplicaciones. No existe un worker financiero distinto del motor contable.
- La CxC por faltante de empleado sigue ese mismo propietario: el cierre congela
  el reclamo y su fuente; `SqlAccountingPostingProcessor` crea la obligación y
  su apertura junto con el asiento. No queda una escritura de cartera en cierres.
- Un documento financiero puro guarda `AccountingSourceDocuments` y `AccountingPostingJobs` en su transacción de aceptación; no crea job, payload ni secuencia operacional.
- `dbo.AccountingSourcesAccept` es la única escritura de aceptación de esas fuentes y trabajos.
  Los adaptadores del documento operacional, comprobante manual, apertura, gasto,
  pago, factura de servicios, nómina, propina y cierre la invocan dentro de su
  transacción existente. `SqlAccountingPostingJobWriter` reside en Accounting
  Infrastructure y adapta los contratos a ese mismo procedimiento por lotes de
  hasta 100 fuentes. La aceptación no aplica saldos ni asientos: esos efectos
  siguen dentro de `SqlAccountingPostingProcessor`. Repetir una aceptación
  conserva fuente, trabajo y modo congelados. El reintento explícito existente
  de fuentes históricas sin trabajo conserva su recuperación en Accounting;
  esa recuperación no se ejecuta desde aceptación ni desde un replay.
  Los auxiliares de los nuevos medios nativos se fijan en los detalles de pago
  existentes mediante `PortfolioPaymentAccountsFreeze`, llamado por esa
  aceptación y el retry del mismo processor. `ConfiguredPostingAccount` y
  `PaymentPostingCategory` comparten la resolución de configuración; no hay un
  segundo motor ni un saldo de tesorería independiente. Los lectores históricos
  y el corte están definidos en `accounting-open-vouchers-design.md`, 13.7.
  Para los mappings generales, `AccountMappingsFreeze` fija identidades de
  categorías en el mismo trabajo, una vez por sede/fecha del lote. Aceptación y
  procesamiento pendiente lo reutilizan; `ResolvedPostingAccount` consume esa
  selección tanto en las partidas automáticas como en la importación de cartera.
  `ConfiguredPostingAccountIdentity` selecciona el override y
  `PostingAccountReadiness` valida únicamente los auxiliares realmente usados.
  Las anulaciones de propina toman sus cuentas y centros del asiento original,
  sin una segunda selección de banco ni reconstrucción de mappings históricos.
- `accounting.PortfolioPaymentAccept` es el participante transaccional de pagos
  CxP y recaudos CxC: valida el conjunto de posiciones con
  `accounting.ObligationAvailability` y guarda encabezado, medios y aplicaciones
  en las tablas existentes. Los dos stores nativos lo invocan dentro de su
  transacción; no contiene commit, job, saldo ni asiento. Replay conserva su
  contrato. La numeración reutiliza `SqlOperationalDocumentAllocator` mediante
  su entrada de serie existente, aislada por tenant y sin provisionar una serie
  para sustituir una faltante o agotada. La captura financiera manual versionada
  normaliza cuentas y vínculos y acepta sus grupos por ese mismo participante,
  dentro del commit de la fuente padre. El processor aplica pagos nativos y grupos
  con su misma rutina por lote; no crea trabajos ni números hijos. La vista de
  lectura `accounting.PortfolioPaymentOrigins` conserva el vínculo al padre en
  historial, reportes y cierres. Las aperturas manuales de CxP y las asistidas
  convergen en `SqlAccountingPostingProcessor.OpenPayablesAsync`: un lote, una
  escritura de obligación/transacción y vínculo a la partida fuente en el mismo
  comando del asiento. Las nuevas CxC comparten `OpenReceivablesSql` con las
  preparaciones nativas, incluidos cierres; los avisos de cartera de todas esas
  fuentes convergen en `SqlAccountingPosSynchronizationOutbox`. La política
  común de aplicaciones vive en `PortfolioSettlement` de BuildingBlocks Domain,
  sin referencias de Accounting a dominios internos de otros módulos. El efecto
  firmado de los movimientos del historial se proyecta desde
  `accounting.PortfolioTransactionEffect` en detalle y reportes de ambos submayores; no se
  persiste otra variación ni se calcula su signo por separado en cada vista.
  `accounting.PortfolioAppliedAmounts` proyecta el importe neto que permanece
  aplicado después de desaplicaciones para listados, cartera por tercero y
  reportes. El historial del dinero pagado conserva su importe bruto. No hay
  otro total editable; los reportes filtran una vez en su mismo comando y
  enriquecen únicamente la página seleccionada.
- La decisión expresa de descuento financiero en un pago asistido de proveedor
  entra por `accounting.PortfolioPaymentAccept`, que congela el importe firmado
  junto a la aplicación. `SqlAccountingPostingProcessor` usa el mismo lote para
  extinguir la obligación, contabiliza su contrapartida en el auxiliar
  configurable y llama una sola vez a `ProductLinkedCostsPrepare` para preparar
  el precio futuro. No llama al writer de inventario. La devolución posterior
  conserva su handler y su fuente inmutable; usa la decisión ya procesada para
  el saldo a favor y revierte la proporción del descuento en el asiento.
  Los cruces sin dinero normalizados preparan sus destinos mediante
  `ManualVoucherCreditTargets` en esa aceptación; `SourcePositionIndex` conserva
  la proyección derivada para `ObligationAvailability`, compartida con abonos.
  Su backfill usa el mismo recorrido acotado del índice de terceros. Solo el
  processor reduce saldos y registra transacciones/asiento: la reducción positiva
  de CxP/CxC comparte `PortfolioBalanceReductionSql` con pagos. Ese adaptador y
  los ajustes individuales/de cierre usan `PortfolioBalanceChangesSql`, dentro
  del mismo processor y comando transaccional. La política de estado distingue
  abonos de ajustes sin duplicar la escritura de saldo. No crea pagos ni
  caja para representar el cruce. La desaplicación de abonos nativos/manuales
  extiende ese mismo índice con vínculo a la transacción original y tipo de
  efecto. El processor reabre la obligación y conserva el dinero como anticipo
  mediante los mismos aplicadores; no crea otro egreso. Su captura visual e
  integración completa con correcciones siguen en curso. La restitución de un
  cruce sin dinero extiende ese mismo aplicador: `AppliedPortfolioSettlements`
  prueba el original y su crédito, `CreditBalanceChangesSql` comparte la escritura
  de disponible con las aplicaciones y `CreditUnapplication` conserva ambos
    vínculos. Está verificada localmente en SQL/API y vistas; no abre otro crédito
    ni mueve caja. La anulación de un pago o recaudo extiende esa misma
  desaplicación y los movimientos directos de tesorería: el código interno
  `PaymentReturn` conserva compatibilidad con comprobantes ya aceptados y
  comparte presupuesto con el abono original. El processor reabre la deuda,
  revierte el dinero y registra el asiento en su misma transacción, sin otro
  pago ni anticipo. El puente de soporte manual y su anulación
  extienden los participantes fiscal/contable y el editor existentes; su contrato
  y evidencia local están en `accounting-open-vouchers-design.md`. Las acciones
  del historial de CxP/CxC preparan juntas las aplicaciones originales en ese
  editor: la única acción de anulación no vincula una sesión nueva y usa el
  mismo processor. El historial
  proyecta el neto desde `PortfolioAppliedAmounts` después de paginar.
- Las aperturas nativas de saldo a favor del proveedor convergen en
  `SqlAccountingPostingProcessor.OpenSupplierCreditsSql`. El detalle Opening
  de `SupplierCreditTransactions` conserva el trabajo padre real y permite
  enlazar la cuenta desde la línea inmutable, también para cargos devueltos.
  `CustomerCredits` comparte `OpenCustomerCreditsSql` para aperturas nativas,
  correcciones de reembolso y anticipos manuales. Los anticipos v4 preparan
  esas aperturas y los grupos de pago en el mismo motor, participante de
  aceptación y aplicador financiero existentes; no producen facturas ficticias
  ni números hijos. Ambos detalles Opening enlazan el mayor desde su partida.
  No es otro saldo ni un motor de anticipos. La API paginada de los cuatro tipos
  y los cruces sin dinero tienen pruebas SQL/API dirigidas; la captura visual
  v4 está en verificación. Los historiales incluyen anticipos por la sede
  explícita del grupo aceptado; el corte copia solo esa dimensión del snapshot
  y no repara metadatos durante replay. La captura fiscal manual usa el participante
  anterior; la anulación de pagos usa el aplicador anterior. No se rellenan saldos históricos
  por inferencia.
- La búsqueda de terceros manuales reutiliza `accounting.SourcePartyIndex`,
  derivado por esa aceptación mediante `accounting.ManualVoucherPartyReferences`.
  El backfill de esquema comparte esa extracción y valida tenant. No existe una
  carga, edición, job o writer independiente de ese índice ni contiene saldos.

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
- Los cobros posteriores de CxC y pagos de CxP son movimientos financieros, no
  ventas nuevas: no se agregan a `SalesReportPaymentFacts` ni al ingreso de
  ventas. Sus encabezados, aplicaciones y medios por fila son la fuente de
  reportes financieros operativos; el libro mayor y cierre de sesión conservan
  sus vistas contables y de caja respectivas.
- La rotación de producto es una proyección persistida de esos hechos en
  `reporting.ProductRotationSnapshots`, con clave negocio + bodega + producto.
  `SqlSalesReportingProjectionWriter` es su único writer; Purchasing, Catalog y
  las pantallas sólo leen el snapshot.
- La rotación diaria para sugeridos de restaurante se persiste en
  `reporting.ProductDailyRotationSnapshots` por negocio, bodega, producto y
  fecha local. Su `NetQuantity` suma las unidades vendidas y resta las devueltas
  (hechos con signo); conserva `ProjectionVersion` y `CalculatedAt`. La actualiza
  idempotentemente el mismo `ProductRotationRefresh` del motor de Reporting en
  la transacción de la proyección documental. El despliegue rellena los 56 días
  históricos de sedes restaurante sin crear otro worker. Compras y producción
  sólo leen ese snapshot y los días con actividad del consolidado diario por
  bodega; no recorren ventas ni devoluciones crudas. Un sugerido nunca confirma
  inventario: la operación valida las existencias actuales al confirmar.
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

Las personas no pasan por `reference.Options`: su propietario es el workspace de `Parties`. Los combos de clientes, proveedores, vendedores, transportadores, empleados, meseros, usuarios o cualquier tercero consumen `GET /api/commerce/v1/parties/role-options`, una proyección paginada mínima y aislada por tenant/business. Para un rol específico, la consulta une `Parties` únicamente con la tabla de ese rol; `Any` deduplica los identificadores de las tablas de roles activas. La respuesta retorna `PartyId`, ID del rol, nombre e identificación; proveedor agrega sólo política de soporte y plazo de pago porque sus formularios los necesitan. La página y el detalle administrativo continúan usando el read model completo de `Parties`, que no debe reutilizarse para dropdowns. El selector del **tipo de rol** (Cliente, Mesero, etc.) consume el catálogo global `party-role` de `reference.Options`; contiene definiciones y labels, nunca personas.

El alta de Mesero extiende `CommercialPartyRoleService`/`SqlCommercialPartyRoleStore` y su recibo canónico de creación. La especialización se guarda en `parties.Waiters` por tenant/Party; `PartyWorkspaceService`/`SqlPartyWorkspaceStore` editan su ficha compartida usando la versión de Party. No hay asignación ni habilitación comercial de Mesero por sede. Los usuarios y sus roles se vinculan manualmente por los servicios de Identity/Parties existentes; crear Mesero no asigna acceso ni reutiliza una cuenta por coincidencia de correo. Las tablas nuevas de este propietario usan el schema `parties`, sin mover las tablas heredadas ni aumentar la deuda de `dbo`.

## Diseño gastronómico

La extensión de restaurante se rige por
[POS para restaurantes y bares](implementation/restaurant-pos-design.md).
Ese documento concentra captura gastronómica independiente bajo Sales, rol
Mesero bajo Parties, recetas/menú público bajo Catalog y pérdidas mediante
Inventory/Accounting. Conserva la caja local y el emisor existente; comandas
sin efecto físico usan Sales y sincronización, no el motor de inventario.
No duplicar sus reglas en este mapa.

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
