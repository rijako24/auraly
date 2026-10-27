# Gastos asistidos por conceptos y retenciones visibles

## Decisión prevalente: Gastos asistido — 2026-10-20

El usuario autorizó iniciar la implementación y cambió la captura: Gastos se mantiene para personas no contables. Cada línea selecciona un concepto configurado, descripción, importe, impuestos y centro de costo; no permite elegir una cuenta. El servidor resuelve la cuenta vigente del concepto por conjunto y la congela en el snapshot aceptado. El maestro de conceptos conserva su parametrización bajo `expenses.configure`; la selección de cuentas libres pertenece al comprobante manual. No se crea otro flujo de gastos ni otra fuente financiera.

El comando de línea admite `ExpenseAccountId=null` con `ConceptId` obligatorio en la captura asistida. El servidor no acepta una cuenta explícita distinta de la configurada en ese concepto. Los contratos anteriores de cuenta directa siguen siendo legibles y aceptables durante el corte para no romper integraciones; sus replays e históricos conservan hashes. El formulario nuevo envía el concepto y nunca una cuenta escogida por el usuario. Cambio de mapping entre preview y confirmación invalida el hash; concepto inactivo o ajeno se rechaza antes de aceptar. La resolución, preview, aceptación y posting siguen en `ExpenseService`/`SqlExpenseStore`/motor contable existentes.

Las secciones siguientes conservan la infraestructura y compatibilidad de contratos anteriores; la selección libre de cuenta pertenece al comprobante manual. Los conceptos guardados siguen disponibles. Aclaración posterior del usuario: se conserva «Agregar gasto» y la captura por concepto/valores. «Agregar factura» en Recepciones se conserva conforme al diseño anterior.


> **Evolución de diseño 2026-10-20:** Gastos será una captura asistida del
> comprobante descrito en la sección 13 de
> [comprobantes abiertos](accounting-open-vouchers-design.md). Conserva cálculo
> tributario, permisos y evidencia propios. La exclusión anterior de participación
> en el editor común se limita al alcance ya implementado; no gobierna el destino.
> Esta evolución no está implementada ni autoriza despliegue.

## Convergencia acordada del gasto y su comprobante

El formulario asistido propone partidas a partir de proveedor, conceptos con cuenta automática,
impuestos y retenciones. La captura libre utiliza el mismo contrato financiero
aceptado y la misma validación de cuentas/auxiliares. No se confirma un gasto y
después otro comprobante con sus mismos importes. `ExpenseService` conserva la
preparación tributaria; el processor contable conserva el único efecto financiero.

Un gasto aceptado se abre como su comprobante existente en Trazabilidad; no se
convierte en borrador modificable. Su asociación a una recepción referencia esa
fuente/partida y no invoca de nuevo `PersistAcceptedAsync`. Un comprobante creado
directamente no necesita una fila ficticia en `Expenses` para poder asociarse o
pagarse. Los filtros de gasto consultan la clasificación y fuente canónicas.

Un cambio de concepto/base/tercero que afecte el cálculo tributario invalida
el preview y exige recalcular por el propietario tributario antes del envío. Las
retenciones nunca se calculan en el posting ni se deducen del código PUC. Documento
soporte y nota de ajuste conservan los contratos fiscales existentes.

La transición adapta ambas entradas a una sola aceptación financiera; los
endpoints históricos siguen admitiendo replay y no crean fuentes nuevas para
documentos ya aceptados. Su evidencia fiscal, de retenciones y numeración permanece
inmutable. Los detalles de aplicaciones/correcciones pertenecen al diseño de
comprobantes, y los de asociación/prorrateo al de recepciones.

Fecha: 2026-09-30; ampliación de retenciones mixtas: 2026-10-02. Decisión confirmada por el usuario. El esquema y los servicios compatibles se publican antes de habilitar la captura; esto no implica certificación tributaria de una empresa.

## Decisión y alcance

Gastos conserva una sola captura. Un gasto genera su fuente contable, su CxP neta y, cuando el respaldo elegido corresponde, su documento soporte. El registro y su comprobante se consultan en la trazabilidad financiera existente. No se vuelve a crear un comprobante manual para el mismo gasto.

El formulario confirma entre 1 y 100 líneas. La captura sigue el patrón de recepciones de compra: grilla de gastos y botón «Agregar gasto» que abre un modal. Cada registro tiene acciones para editar y quitar. El modal trabaja sobre una copia local y solo incorpora cambios al guardar; cancelar conserva la línea anterior. No se crean documentos ni llamadas de guardado por fila. Cada línea contiene concepto de gasto, descripción, centro de costo, base, impuesto y tratamiento de IVA. `ExpenseConcepts` es el catálogo de gastos frecuentes con su cuenta automática configurada; el formulario exige elegir un concepto existente. Los conceptos iniciales se instalan desde el perfil contable persistido; la interfaz no copia el PUC ni inventa cuentas.

La cuenta contable, el gasto frecuente y el concepto tributario son datos distintos. El concepto determina la cuenta y propone descripción, centro y clasificación tributaria; el usuario puede ajustar los datos permitidos de la línea. Configurar otra cuenta requiere editar el concepto con su permiso propio. La clasificación tributaria seleccionable procede de las reglas de compras y de los conceptos de gasto configurados, sin porcentajes ni umbrales definidos en el frontend.

Al agregar una línea, gasto frecuente empieza sin selección. La × dentro del selector retira la plantilla y los valores que propuso para cuenta, descripción, centro y clasificación; la base e impuesto digitados se conservan. El selector de proveedor muestra solo terceros activos con rol Proveedor; si no existen, indica dónde registrarlos. Un tercero con otro rol no se convierte implícitamente en proveedor porque la aceptación usa un `SupplierId` válido.

El cálculo se solicita al agregar, editar o quitar una línea válida, en una sola operación para la grilla completa; no se consulta durante cada pulsación o render. Basta con tener proveedor, fechas válidas y líneas: el número de factura sigue siendo obligatorio para confirmar, pero no para mostrar las retenciones. Al quitar la última línea se limpia el cálculo. Cambiar proveedor, fechas o tipo de respaldo con líneas presentes obtiene un cálculo nuevo; cambiar solo descripción o número de factura conserva el cálculo porque esos campos no integran su hash. El usuario confirma después de revisar base, tarifa, regla, versión, valor de retenciones, bruto y neto por pagar. La confirmación exige un cálculo vigente y el servidor lo valida de nuevo. Cancelar una edición no cambia la grilla ni invalida el cálculo previo.

Las retenciones automáticas las determina el motor tributario a partir del perfil del proveedor, concepto, base, fecha y reglas vigentes. Su cuenta procede del mapeo por tipo de retención. Un usuario con `commerce.taxation.withholdings.manage` puede ajustar o excluir una retención automática, restaurarla y agregar una retención puntual con tipo, concepto, base, tarifa, importe editable, cuenta de pasivo y motivo. La retención puntual no exige crear una regla global: queda congelada en el snapshot del documento con identificador de línea y cuenta propia. El servidor impide duplicar el mismo tipo, jurisdicción y base ya retenidos en el documento; primero se ajusta o excluye la automática. La casilla opcional «Guardar como regla para usarla después» crea una regla de aplicación manual, con cuenta predeterminada, que nunca entra al cálculo automático y sigue disponible aunque se cierre el borrador. El cálculo final se revisa en el preview y se congela con el asiento y la CxP. El motor contable consume ese resultado; no calcula retenciones.

La recepción de compra aplica el mismo editor a la factura principal y a cada documento de costo. Los ajustes de un borrador permanecen en el borrador; antes de confirmar uno ajustado, el servidor exige el hash del preview vigente para ese documento. Las reglas automáticas conservan sus tarifas configuradas; una retención puntual se captura expresamente y el servidor valida base, tarifa, cuenta y motivo. Un ajuste inválido se devuelve como error de validación del módulo, sin aceptación parcial. Los comprobantes manuales no cambian en este alcance.

Se preservan pagos, anulación y consulta del comprobante en sus recorridos actuales. Crear/ver/editar/contabilizar diferido de documentos manuales es una capacidad distinta; esta ampliación no convierte los gastos aceptados en borradores editables. No incluye ejecución bancaria, nómina, inventario, activos fijos, recurrencias, nuevas monedas ni importación de XML.

## Propietarios y persistencia

| Responsabilidad | Propietario |
| --- | --- |
| Permisos, validación de captura, preview y confirmación | `ExpenseService` |
| Resolución por conjunto de cuentas, conceptos, centros e impuestos | `SqlExpenseStore.ResolveAsync` |
| Reglas, perfiles, bases mínimas, tarifas y cálculo | `WithholdingService` / `WithholdingEngine` |
| Aceptación y fuente financiera | `SqlExpenseStore.PersistAcceptedAsync` / `SqlAccountingPostingJobWriter` |
| Escritura de retenciones históricas | `SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync`, participante de la transacción de gastos, compras y ventas |
| Apertura de CxP y asiento | `AccountingProcessingCoordinator` / `SqlAccountingPostingProcessor` |
| Numeración, snapshot fiscal, generación y transmisión | Asignadores y `FiscalProcessingCoordinator` existentes |
| Anulación y reversión del asiento original | `SqlExpenseStore.Cancellation` / posting financiero |
| Pago al proveedor | Cartera y `PortfolioPaymentWizard` |

`dbo.Expenses.ExpenseConceptId` admite NULL. Las líneas normalizadas, sus etiquetas, cuentas, centros, impuestos y clasificación se congelan en `ExpenseDocumentPayload.Lines`, dentro de la fuente contable canónica. No se crea una tabla de líneas duplicada ni un segundo writer. El snapshot fiscal copia esa misma información cuando corresponde.

El listado y el detalle admiten gastos sin concepto. El detalle expone las líneas y las retenciones históricas del payload. Los filtros de concepto en Gastos y CxP contemplan también las líneas del documento antes de contar, totalizar y paginar; no filtran únicamente la grilla recibida. Proveedor sigue siendo un ID elegido mediante el combo de terceros.

La escritura de gastos, snapshots de retenciones, fuente/trabajo financiero y respaldo fiscal conserva su transacción. No se agregan estados, motores, colas, trabajos ni escritores paralelos.

La persistencia de retenciones comparte un único participante por lotes de hasta
100 documentos. Cada productor conserva el cálculo, las validaciones, las fechas
y su transacción; el participante no calcula impuestos ni confirma la transacción.
Cabeceras y líneas se proyectan juntas por lote, sin SQL por concepto. Ventas
conserva sus campos históricos; gastos y compras conservan además los ajustes y
las retenciones puntuales. No se reescriben snapshots anteriores ni se cambian
contratos, esquema o dependencias. Una prueba arquitectónica exige este escritor
único. La regresión local aprobó 88 controles de arquitectura/validación y 37
escenarios SQL/API de gastos, compras, soportes y facturación. Los previews de
1/20/100 líneas conservaron tres SQL; las confirmaciones conservaron 24 y tardaron
1.694/1.759/1.760 ms. Las recepciones de veinte productos tardaron 1.644/1.456 ms,
dentro de su límite de dos segundos. Esta evidencia no certifica los pendientes
del diseño ampliado ni una aceptación real de DIAN.
El fallo inyectado después de insertar la cabecera tributaria revirtió también
inventario, costos, preparación de precios y aceptación contable; reintento y
replay conservaron un solo snapshot. Pasaron ambos escenarios de rollback del
handler de recepción. La revisión posterior contrastó los tres productores,
sus campos persistidos y el participante: un solo escritor, sin commit propio,
I/O por línea, cambio de fórmula, nuevo estado ni reconstrucción histórica.

## Impuestos y retenciones

La captura nueva selecciona un perfil de IVA activo del tenant. El servidor calcula el importe con la tarifa persistida, a cuatro decimales. Una línea sin perfil no tiene IVA. `DeductibleInputVat` debita la cuenta de IVA descontable; `CapitalizedCost` suma el IVA al débito de la cuenta de gasto. Un comprobante interno con IVA no admite el tratamiento descontable.

`WithholdingEngine.CalculateDocument` usa la misma elegibilidad y cálculo de línea que el motor existente. Agrupa las bases coincidentes de cada regla antes de aplicar el mínimo y redondear. Distribuir una misma operación entre cuentas o centros no divide el umbral; una regla por concepto no grava las líneas de otro concepto. ReteIVA conserva como base el IVA de las líneas coincidentes. El snapshot por regla/version/base/tarifa se reconcilia con las bases clasificadas y con el neto.

`POST /expenses/preview` requiere `expenses.create`, no administración de impuestos. Carga cuentas/configuración por conjunto y usa `PrepareCalculationPlanAsync` para reglas y perfiles. No recibe responsabilidades tributarias desde el navegador ni sobrescribe la jurisdicción del proveedor con `CO`.

Un perfil ausente bloquea la confirmación automática y explica dónde completarlo; si el usuario autorizado agrega una retención puntual validada, puede confirmar con el diagnóstico visible de que solo se aplicó esa retención manual. El diagnóstico diferencia un perfil que no aplica retención, una base inferior al mínimo y la ausencia de reglas coincidentes. Un cero con configuración revisable se muestra expresamente; no se presenta automáticamente como una exención legal. La decisión tributaria de la empresa sigue perteneciendo al responsable de su configuración.

La confirmación recalcula y compara `CalculationHash`. Un cambio de resultado, versión aplicada, cuentas o configuración resuelta devuelve conflicto antes de aceptar. Las referencias de línea se vuelven a comprobar en la transacción serializable. El replay de un gasto ya aceptado ocurre antes de leer conceptos o recalcular impuestos y devuelve la aceptación histórica sin repetir señales ni efectos.

### Consolidado de retenciones practicadas

Contabilidad → Retenciones presenta dos pestañas: **Retenciones aplicadas** abre el consolidado y **Configuración** conserva las reglas y responsabilidades tributarias. Cada pestaña carga solo su recurso visible. El consolidado consulta `DocumentWithholdingSnapshots` y sus líneas ya causadas. El período corresponde a la fecha de reconocimiento en Colombia. El informe incluye gastos, recepción principal y cada factura adicional por separado, con documento, proveedor, identificación, base, tarifa, valor, tipo, jurisdicción y origen automático o puntual. El servidor limita el período a un año y pagina el detalle; resume renta y ReteIVA aparte de ReteICA. La consulta pertenece al módulo tributario, conserva el filtro por tenant y negocio y no crea un segundo motor, estado de pago ni copia de las líneas. El índice por negocio y fecha acota el período; una solicitud obtiene totales y página en un viaje SQL.

El consolidado es soporte de revisión, no una declaración ni un pago. Los borradores fiscales `RETENCIONES` y `FORM-350` ya pertenecen a `ComplianceReportingService` y conservan su generación y artefactos: este detalle documental no crea otra ruta para presentarlos. [DIAN exige declaración mensual de retenciones nacionales en el formulario 350](https://www.dian.gov.co/atencionciudadano/formulariosinstructivos/Formularios/2026/Formulario_350_2026.pdf), mientras que la información exógena de pagos y retenciones practicadas se informa separadamente en el [formato 1001](https://normograma.dian.gov.co/dian/compilacion/docs/resolucion_dian_0227_2025.htm). ReteICA sigue la jurisdicción municipal, fuera del 350. Los proveedores o conceptos sin movimiento no se convierten en filas ficticias. Los gastos anulados antes del cierre del período no integran los totales; una anulación posterior no reescribe el período histórico y debe conciliarse con su asiento de reversión antes de declarar. El informe todavía no cubre nómina, autorretenciones ni un formulario DIAN completo, y no afirma haber presentado o pagado esas obligaciones.

## Compatibilidad

Los clientes anteriores pueden seguir enviando un concepto y base/IVA de una sola partida. Los snapshots históricos sin `Lines` conservan su interpretación original. Las propiedades nuevas nulas se omiten al serializar para preservar los hashes de idempotencia anteriores, incluido el fallback histórico basado en la retención congelada.

Un comando antiguo nuevo con comprobante interno e IVA positivo se rechaza con una instrucción para usar líneas y seleccionar mayor valor del gasto. No se mantiene la inferencia incorrecta de IVA descontable por compatibilidad accidental. Esta validación ocurre después del replay: no impide recuperar una aceptación histórica.

Los gastos provenientes de cargos de factura conservan su contrato y aceptación por lote. Sus pagos y devoluciones siguen utilizando el asiento original. La anulación de un gasto multilínea invierte las partidas históricas y localiza la primera partida acreedora del neto, sin depender de la antigua posición fija según existencia de IVA ni de los mapeos contables actuales.

## Respaldo y DIAN

### Anulación después de desaplicar

La aceptación de la anulación consulta el dinero neto todavía aplicado mediante
`accounting.PortfolioAppliedAmounts`, dentro de la misma validación SQL y bajo
el bloqueo existente de la obligación. El historial bruto del pago permanece
intacto. Un abono desaplicado conserva su anticipo; un cruce desaplicado restituye
su crédito original. La anulación no crea otro saldo a favor por esos importes.
Si permanece un cruce activo, un ajuste sin conciliar, una reserva pendiente o
un costo distribuido, se explica el conflicto antes de aceptar la anulación.
El saldo a favor de la anulación corresponde únicamente al dinero neto aplicado
que todavía pertenece al gasto. La ejecución conserva el mismo
`SqlAccountingPostingProcessor`, fuente, trabajo, reversión y writer de créditos;
no borra aplicaciones ni repite movimientos de caja.

Verificado localmente: la regresión reprodujo tres rechazos incorrectos antes
del cambio. Después pasaron 53 escenarios SQL/API, incluidos los cuatro casos
de restitución total/parcial de dinero o crédito. Cada anulación ejecutó una
validación SQL y su HTTP con motor tardó entre 196,5 y 1.664,2 ms, por debajo
de dos segundos. TypeScript, lint enfocado y API Release pasaron. Dos recorridos
de navegador con API/SQL reales comprobaron cartera y gasto asistido; el segundo
restituyó un cruce, anuló el gasto desde la vista, conservó el anticipo de
50,0001 y el pago bruto de 100,25, y abrió únicamente el crédito de 50,2499 por
el dinero que seguía aplicado. Se inspeccionó el detalle anulado y su acceso a
la reversión. La auditoría del cambio confirmó el lector neto compartido,
transacción/bloqueos existentes, validaciones de dependencias, replay y único
processor; no agregó SQL por elemento, escritura de saldos, estado, contrato,
esquema ni consulta de UI. No sustituye la auditoría global todavía pendiente.

Desde que se acepta la anulación hasta que termina el motor,
`accounting.ObligationAvailability` reserva también el saldo del gasto nativo
en `CancellationPending`. Reutiliza ese estado existente y el mismo indicador
`PendingCancellationCount` de las anulaciones manuales. Ajustes, pagos y cruces
consultan esa autoridad; la aceptación de desaplicaciones y su captura también
rechazan explícitamente la obligación reservada. No se abre otro índice de
reservas, saldo, trabajo ni writer. El bloqueo de la obligación conserva el
orden transaccional del pago y de la anulación; el preview no sustituye la
revalidación al aceptar.
El estado histórico `Paid` de una obligación parcialmente pagada no implica
que su gasto anulado pueda reabrirse. La misma función proyecta `SourceCancelled`
desde el estado definitivo del gasto; ajustes, cruces y desaplicaciones lo
rechazan con ese motivo explícito. Es un dato derivado, no un segundo estado
persistido. Se conservan el pago original y el saldo a favor de la anulación.

Verificación del 25 de octubre: la regresión reprodujo aumentos y disminuciones
aceptados incorrectamente durante la espera del motor, y después una
desaplicación que también se aceptaba. Los cuatro escenarios SQL/API pasaron
con rechazo 409 y motivo específico, sin otra fuente ni variación del saldo;
la anulación terminó mediante el processor existente. Incluye captura y envío
de un borrador anterior después de la anulación definitiva. Los siete
benchmarks del lector y motor compartidos pasaron: 21.000 fuentes, primera y
segunda página de 50 en un comando SQL, y lotes de hasta 500 partidas sin I/O
por elemento. La devolución de dinero de 100 obligaciones conservó siete SQL
al aceptar y once al procesar, con p95 de 367,9 y 220,5 ms (20 muestras).
Esta verificación es del camino financiero; no cierra los cargos posteriores,
descuentos financieros ni la auditoría global del diseño.
La regresión conjunta final pasó 22 casos SQL/API y las 949 pruebas de
fundamentos/arquitectura. Las dos pruebas de tarjetas ajustaron su preparación:
la API debe rechazar el mapping inválido; solo la base desechable de la prueba
inyecta después un override histórico inválido para verificar que el pago
permanezca sin aplicar. No se relajó esa validación productiva. El recorrido de
navegador con API/SQL reales volvió a pasar creación, pago, replay,
desaplicación, devolución real y anulación. Se inspeccionó el detalle final:
gasto cancelado, obligación histórica pagada con saldo cero y acceso a su
reversión. La ampliación del esquema local conservó sus 23 movimientos de
tesorería previos.

La auditoría posterior de este cambio siguió función, aceptación, captura,
aplicación y presentación: el estado se deriva únicamente de `Expenses`, los
saldos siguen en el processor, el rechazo no crea fuente, y los lectores
conservan tenant/sede y un viaje SQL. Los replays conservan sus resultados;
no se introdujeron colas, tablas, writers, endpoints, pantallas, polling ni
configuración nuevos. La compatibilidad exige conservar función/procedimiento
y binarios compatibles; volver a los predicados anteriores reabre esta ventana.

### Evidencia fiscal

El respaldo se selecciona desde `reference.Options` y se valida contra la política del proveedor:

- Factura electrónica del proveedor: referencia obligatoria; no genera otro documento fiscal.
- Documento soporte electrónico del adquirente: usa numeración, cupo, datos del proveedor, snapshot y coordinador fiscal existentes.
- Comprobante interno: genera efectos financieros sin documento fiscal y no habilita IVA descontable.

Se verificó el [anexo técnico DIAN de documento soporte, versión 1.1](https://www.dian.gov.co/impuestos/factura-electronica/Documents/Anexo-Tecnico-Documento-Soporte-No-Obligados.pdf), especialmente 8.1, 8.2, 14.3.1 y 16.2.2. El soporte proyecta `WithholdingTaxTotal` para 05/ReteIVA y 06/ReteRenta, agrupado por tributo y tarifa. ReteICA permanece en el snapshot y asiento: el catálogo de este perfil fiscal no incluye su código.

Las retenciones no disminuyen `LegalMonetaryTotal/PayableAmount` del soporte; el neto de cartera sí las descuenta. La nota de ajuste conserva las líneas y referencia del soporte original. Su esquema CreditNote 2.1 no admite `WithholdingTaxTotal`: no se inventa ese nodo en la nota. La reversión de todas las retenciones ocurre en el asiento original y queda respaldada por el snapshot de anulación.

La selección de retenciones del generador comparte esta proyección para los
tres orígenes de adquisición: gasto, recepción y documento de costo adicional.
Lee el snapshot inmutable del único origen fiscal, sin consultar maestros ni
recalcular importes. La regresión local del 25 de octubre reprodujo la omisión
de las retenciones en XML de recepción y costo adicional antes de corregirla.
Después pasaron 71 casos de generación, XSD, auxiliares y arquitectura,
incluida la anulación de gasto sin nodos tributarios incompatibles. La auditoría
del cambio verificó un solo generador y proyector tributario, ningún I/O nuevo,
sin cambios en saldos, pagos, numeración, cupos ni hashes de los snapshots.

Las pruebas comprueban XML y XSD del repositorio; no equivalen a aceptación real en DIAN ni a una validación profesional de tarifas de una empresa. No se cambia la versión de plantillas de impresión ni se crean certificados alternos; impresión/exportación y trazabilidad mantienen sus propietarios actuales.

La revisión de bases de adquisición, ajustes de retenciones y diagnóstico de
perfil corresponde a `WithholdingService.ReviewPurchaseDocument`. Gastos lo
reutiliza con el mismo plan cargado por lote, permisos y hash de revisión; no
cambia la forma ni el orden de los campos firmados del preview anterior. Es el
punto reutilizable para la captura fiscal manual pendiente, sin otro calculador.
La regresión SQL/API pasó los siete casos de esta tanda, incluidos perfil,
retenciones automáticas y manuales, preview/aceptación/replay/anulación y las
cuatro variantes de desaplicación. Con 1/20/100 líneas mantuvo tres comandos de
preview y 24 de confirmación: 2/5/9 ms y 128/70/104 ms, respectivamente. No hay
I/O en la revisión pura ni nuevas lecturas por línea. La auditoría comprobó una
sola política de perfil/ajustes y conservó transacción, numeración, permisos,
publicación de señales y traducción de errores del módulo.

## Interfaz y carga

La captura elige proveedor y sede activa en un único combo paginado: cada opción
muestra nombre, identificación y sede, independiente de si existe cartera. La aceptación valida que pertenezca
al proveedor y la congela en el payload para que el motor contable abra la cuenta
por pagar en esa sede. Si un proveedor tiene varias sedes, no se infiere una por
su nombre ni por obligaciones previas.

`ExpenseForm` utiliza `SupplierSiteSelect`, el catálogo de conceptos, `DatePicker` y `FormattedNumberInput`. Las opciones de respaldo, tratamiento y estados proceden de catálogos persistidos. Los proveedores con sus sedes se buscan por páginas en servidor; la carga de opciones de Gastos omite esos directorios completos. El detalle y el cálculo reutilizan `ExpenseBreakdown`; durante la captura las líneas se muestran una sola vez en la grilla y debajo quedan retenciones y totales. El concepto conserva su etiqueta en memoria; abrir el editor de una línea no consulta una cuenta. El selector de cuenta de retenciones puntuales sigue perteneciendo al editor tributario autorizado.

Con una línea capturada, «Agregar retención» se muestra antes de los totales: se habilita con preview vigente y permiso de gestión tributaria, incluso sin reglas configuradas ni perfil tributario. El mismo modal permite elegir una regla manual guardada como plantilla y cambiar sus campos para este documento. La cuenta se busca por código o nombre entre cuentas de pasivo activas; la casilla de guardar regla está apagada por defecto. Una regla automática coincidente aparece sola y se puede editar, excluir o restaurar. El preview bloquea la edición, pero permite cerrar el modal; desde que se pulsa «Confirmar gasto», el cierre queda protegido hasta terminar la confirmación o fallar su validación.

El listado, opciones y detalle tienen un único dueño de consulta por empresa. No hay polling. El preview se solicita por las acciones explícitas de agregar, editar o quitar una línea, o al confirmar un documento modificado, y bloquea la edición mientras está en curso para impedir respuestas antiguas sobre datos nuevos. Una creación necesita una sola lectura de la página filtrada, porque cambia su composición, totales y paginación. La edición de una plantilla reutiliza la respuesta guardada y actualiza el catálogo en memoria. El acuse de anulación no contiene el detalle resultante ni la página: se consulta cada recurso visible una sola vez para conservar el estado real, saldo y pertenencia a filtros. Al completar un pago se actualiza una vez la página de Gastos; el detalle cerrado se consulta solamente al abrirlo de nuevo.

La apertura explícita de un detalle usa `staleTime=0` en su consulta propietaria:
la frescura global de un minuto no puede conservar el saldo anterior a un abono.
Durante esa lectura se ocultan el saldo y las acciones financieras anteriores;
si falla, se muestra el error y el reintento explícito del mismo recurso. No se
consulta un detalle cerrado ni se agrega polling. El resumen y el detalle
conservan hasta cuatro decimales, igual que el desglose, sin alterar importes ni
formatos de impresión. El estado `PartiallyPaid` se presenta como «Pago parcial»
mediante el diccionario compartido de etiquetas.

El calendario se aloja dentro del diálogo activo, como el combo paginado. Escape dentro del calendario cierra únicamente el calendario para conservar la captura; el atajo «Elegir hoy» respeta los límites mínimo/máximo; el ajuste está en `DatePicker`, no en un calendario alterno para Gastos.

## Aceptación, rendimiento y publicación

Presupuesto: preview de 20 líneas <800 ms y de 100 <1.5 s; confirmación de 20 <2 s y de 100 <3 s en la medición local aislada. La previsualización automática usa tres comandos SQL para 1/20/100 líneas: resolución, reglas y perfiles. Con retenciones puntuales se agrega una consulta por conjunto para validar todas las cuentas seleccionadas. La confirmación no debe aumentar sus viajes al agregar líneas, incluyendo el motor financiero en la prueba instrumentada; la inicialización del primer documento puede consumir un viaje adicional. No hay SQL/HTTP por línea.

La regresión cubre cuenta directa sin concepto ficticio, plantillas existentes, retención sobre base acumulada, IVA mixto, CxP neta, cambio de reglas, replay, permisos, referencias inválidas, anulación multilínea, XML/nota y consumidores de cargos. La prueba de navegador verifica selección de concepto sin pedir ni consultar su cuenta automática, calendario, cálculo por cambio de línea, invalidación por edición, perfil incompleto, conflicto y una sola recarga del listado al aceptar, campos monetarios formateados, cero/referencia obligatoria, límites de calendario, bloqueo durante envío, alta/edición/eliminación en grilla, cancelación sin cambios ni consultas por fila, conservación de datos ante error y una lectura de cada recurso visible después de anular. El formulario se cierra únicamente después del acuse exitoso del servidor; la edición de conceptos y la anulación bloquean cambios/cierre mientras su comando está pendiente.

**Regresión real de navegador, local 2026-10-22:** en una base descartable
publicada con el DACPAC y seeds oficiales, la captura sin cuenta seleccionable
creó un gasto de COP 1.000,50. El wizard existente aplicó COP 100,25 en efectivo;
el motor contable dejó la misma CxP en COP 900,25 y un asiento balanceado con el
tercero del proveedor. Reenviar la misma clave devolvió el mismo pago y el
historial conservó una sola aplicación. La reapertura mostró el saldo y «Pago
parcial» mediante exactamente una lectura del detalle. Una falla de lectura
simulada ocultó «Pagar» y los importes anteriores; se conservaron el único retry
acotado del proveedor de consultas y el reintento explícito del usuario.
`accounting-real-local.spec.ts` y `support/real-accounting-workspace.ts` ejercen
login, BFF, API y SQL reales, con URL local y tenant/sede explícitos. Solo la
etapa final de fallo de lectura usa interceptación. La captura y pago no la usan.
Tipos, lint y build de la interfaz pasaron. La regresión conjunta pasó los
13 casos de captura/anulación y los ocho de checkout, incluidos rechazo 409 y
resultado incierto 503. Esta evidencia corresponde al pago nativo de proveedores,
sin envío externo DIAN; no sustituye las pruebas de los grupos del editor manual.
Su implementación y límites vigentes se describen en el propietario de
[comprobantes abiertos](accounting-open-vouchers-design.md).

La nulabilidad histórica de `ConceptId` conserva los contratos anteriores de cuenta directa; la captura asistida nueva exige concepto y envía `ExpenseAccountId=null`. Todos los consumidores de fuentes financieras y fiscales deben admitir `Lines` antes de emitir comandos nuevos. No se debe regresar a un worker anterior con documentos multilínea pendientes: sus lectores desconocen esas líneas. Ante rollback de interfaz se conserva el lector compatible y se drenan los trabajos con su propietario actual; la nulabilidad del concepto no se revierte sobre datos nuevos.

La instrucción vigente del usuario limita esta ampliación a implementación y
pruebas locales, sin despliegue. Un despliegue posterior requiere autorización
y un commit integrado en `origin/main`.
