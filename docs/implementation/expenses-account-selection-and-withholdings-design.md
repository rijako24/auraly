# Gastos: cuentas directas, plantillas opcionales y retenciones visibles

Fecha: 2026-09-30; ampliación de retenciones mixtas: 2026-10-02. Decisión confirmada por el usuario. El esquema y los servicios compatibles se publican antes de habilitar la captura; esto no implica certificación tributaria de una empresa.

## Decisión y alcance

Gastos conserva una sola captura. Un gasto genera su fuente contable, su CxP neta y, cuando el respaldo elegido corresponde, su documento soporte. El registro y su comprobante se consultan en la trazabilidad financiera existente. No se vuelve a crear un comprobante manual para el mismo gasto.

El formulario confirma entre 1 y 100 líneas. La captura sigue el patrón de recepciones de compra: grilla de gastos y botón «Agregar gasto» que abre un modal. Cada registro tiene acciones para editar y quitar. El modal trabaja sobre una copia local y solo incorpora cambios al guardar; cancelar conserva la línea anterior. No se crean documentos ni llamadas de guardado por fila. Cada línea contiene cuenta de gasto buscable por código o nombre, descripción, centro de costo, base, impuesto y tratamiento de IVA. `ExpenseConcepts` sigue siendo el catálogo de gastos frecuentes: sus plantillas son atajos opcionales, nunca conceptos ocultos creados para satisfacer una FK. Los conceptos iniciales se instalan desde el perfil contable persistido; la interfaz no copia el PUC ni inventa cuentas.

La cuenta contable, el gasto frecuente y el concepto tributario son datos distintos. Una plantilla propone cuenta, descripción, centro y clasificación tributaria; el usuario puede cambiar la línea. Elegir otra cuenta elimina el vínculo de plantilla. La clasificación tributaria seleccionable procede de las reglas de compras y de los conceptos de gasto configurados, sin porcentajes ni umbrales definidos en el frontend.

Al agregar una línea, gasto frecuente empieza sin selección. La × dentro del selector retira la plantilla y los valores que propuso para cuenta, descripción, centro y clasificación; la base e impuesto digitados se conservan. El selector de cuentas abre sus resultados por encima del área desplazable del modal. El selector de proveedor muestra solo terceros activos con rol Proveedor; si no existen, indica dónde registrarlos. Un tercero con otro rol no se convierte implícitamente en proveedor porque la aceptación usa un `SupplierId` válido.

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
| Aceptación, retenciones históricas y fuente financiera | `SqlExpenseStore.PersistAcceptedAsync` / `PersistWithholdingAsync` / `SqlAccountingPostingJobWriter` |
| Apertura de CxP y asiento | `AccountingProcessingCoordinator` / `SqlAccountingPostingProcessor` |
| Numeración, snapshot fiscal, generación y transmisión | Asignadores y `FiscalProcessingCoordinator` existentes |
| Anulación y reversión del asiento original | `SqlExpenseStore.Cancellation` / posting financiero |
| Pago al proveedor | Cartera y `PortfolioPaymentWizard` |

`dbo.Expenses.ExpenseConceptId` admite NULL. Las líneas normalizadas, sus etiquetas, cuentas, centros, impuestos y clasificación se congelan en `ExpenseDocumentPayload.Lines`, dentro de la fuente contable canónica. No se crea una tabla de líneas duplicada ni un segundo writer. El snapshot fiscal copia esa misma información cuando corresponde.

El listado y el detalle admiten gastos sin concepto. El detalle expone las líneas y las retenciones históricas del payload. Los filtros de concepto en Gastos y CxP contemplan también las líneas del documento antes de contar, totalizar y paginar; no filtran únicamente la grilla recibida. Proveedor sigue siendo un ID elegido mediante el combo de terceros.

La escritura de gastos, snapshots de retenciones, fuente/trabajo financiero y respaldo fiscal conserva su transacción. No se agregan estados, motores, colas, trabajos ni escritores paralelos.

## Impuestos y retenciones

La captura nueva selecciona un perfil de IVA activo del tenant. El servidor calcula el importe con la tarifa persistida, a cuatro decimales. Una línea sin perfil no tiene IVA. `DeductibleInputVat` debita la cuenta de IVA descontable; `CapitalizedCost` suma el IVA al débito de la cuenta de gasto. Un comprobante interno con IVA no admite el tratamiento descontable.

`WithholdingEngine.CalculateDocument` usa la misma elegibilidad y cálculo de línea que el motor existente. Agrupa las bases coincidentes de cada regla antes de aplicar el mínimo y redondear. Distribuir una misma operación entre cuentas o centros no divide el umbral; una regla por concepto no grava las líneas de otro concepto. ReteIVA conserva como base el IVA de las líneas coincidentes. El snapshot por regla/version/base/tarifa se reconcilia con las bases clasificadas y con el neto.

`POST /expenses/preview` requiere `expenses.create`, no administración de impuestos. Carga cuentas/configuración por conjunto y usa `PrepareCalculationPlanAsync` para reglas y perfiles. No recibe responsabilidades tributarias desde el navegador ni sobrescribe la jurisdicción del proveedor con `CO`.

Un perfil ausente bloquea la confirmación automática y explica dónde completarlo; si el usuario autorizado agrega una retención puntual validada, puede confirmar con el diagnóstico visible de que solo se aplicó esa retención manual. El diagnóstico diferencia un perfil que no aplica retención, una base inferior al mínimo y la ausencia de reglas coincidentes. Un cero con configuración revisable se muestra expresamente; no se presenta automáticamente como una exención legal. La decisión tributaria de la empresa sigue perteneciendo al responsable de su configuración.

La confirmación recalcula y compara `CalculationHash`. Un cambio de resultado, versión aplicada, cuentas o configuración resuelta devuelve conflicto antes de aceptar. Las referencias de línea se vuelven a comprobar en la transacción serializable. El replay de un gasto ya aceptado ocurre antes de leer conceptos o recalcular impuestos y devuelve la aceptación histórica sin repetir señales ni efectos.

## Compatibilidad

Los clientes anteriores pueden seguir enviando un concepto y base/IVA de una sola partida. Los snapshots históricos sin `Lines` conservan su interpretación original. Las propiedades nuevas nulas se omiten al serializar para preservar los hashes de idempotencia anteriores, incluido el fallback histórico basado en la retención congelada.

Un comando antiguo nuevo con comprobante interno e IVA positivo se rechaza con una instrucción para usar líneas y seleccionar mayor valor del gasto. No se mantiene la inferencia incorrecta de IVA descontable por compatibilidad accidental. Esta validación ocurre después del replay: no impide recuperar una aceptación histórica.

Los gastos provenientes de cargos de factura conservan su contrato y aceptación por lote. Sus pagos y devoluciones siguen utilizando el asiento original. La anulación de un gasto multilínea invierte las partidas históricas y localiza la primera partida acreedora del neto, sin depender de la antigua posición fija según existencia de IVA ni de los mapeos contables actuales.

## Respaldo y DIAN

El respaldo se selecciona desde `reference.Options` y se valida contra la política del proveedor:

- Factura electrónica del proveedor: referencia obligatoria; no genera otro documento fiscal.
- Documento soporte electrónico del adquirente: usa numeración, cupo, datos del proveedor, snapshot y coordinador fiscal existentes.
- Comprobante interno: genera efectos financieros sin documento fiscal y no habilita IVA descontable.

Se verificó el [anexo técnico DIAN de documento soporte, versión 1.1](https://www.dian.gov.co/impuestos/factura-electronica/Documents/Anexo-Tecnico-Documento-Soporte-No-Obligados.pdf), especialmente 8.1, 8.2, 14.3.1 y 16.2.2. El soporte proyecta `WithholdingTaxTotal` para 05/ReteIVA y 06/ReteRenta, agrupado por tributo y tarifa. ReteICA permanece en el snapshot y asiento: el catálogo de este perfil fiscal no incluye su código.

Las retenciones no disminuyen `LegalMonetaryTotal/PayableAmount` del soporte; el neto de cartera sí las descuenta. La nota de ajuste conserva las líneas y referencia del soporte original. Su esquema CreditNote 2.1 no admite `WithholdingTaxTotal`: no se inventa ese nodo en la nota. La reversión de todas las retenciones ocurre en el asiento original y queda respaldada por el snapshot de anulación.

Las pruebas comprueban XML y XSD del repositorio; no equivalen a aceptación real en DIAN ni a una validación profesional de tarifas de una empresa. No se cambia la versión de plantillas de impresión ni se crean certificados alternos; impresión/exportación y trazabilidad mantienen sus propietarios actuales.

## Interfaz y carga

La captura elige proveedor y sede activa en un único combo paginado: cada opción
muestra nombre, identificación y sede, independiente de si existe cartera. La aceptación valida que pertenezca
al proveedor y la congela en el payload para que el motor contable abra la cuenta
por pagar en esa sede. Si un proveedor tiene varias sedes, no se infiere una por
su nombre ni por obligaciones previas.

`ExpenseForm` utiliza `SupplierSiteSelect`, `AccountSelect`, `DatePicker` y `FormattedNumberInput`. Las opciones de respaldo, tratamiento y estados proceden de catálogos persistidos. Los proveedores con sus sedes y las cuentas se buscan por páginas en servidor; la carga de opciones de Gastos omite esos directorios completos. El detalle y el cálculo reutilizan `ExpenseBreakdown`; durante la captura las líneas se muestran una sola vez en la grilla y debajo quedan retenciones y totales. `AccountSelect` entrega la opción seleccionada para conservar su etiqueta en memoria; abrir el editor de una línea no vuelve a consultar su cuenta.

Con una línea capturada, «Agregar retención» se muestra antes de los totales: se habilita con preview vigente y permiso de gestión tributaria, incluso sin reglas configuradas ni perfil tributario. El mismo modal permite elegir una regla manual guardada como plantilla y cambiar sus campos para este documento. La cuenta se busca por código o nombre entre cuentas de pasivo activas; la casilla de guardar regla está apagada por defecto. Una regla automática coincidente aparece sola y se puede editar, excluir o restaurar. El preview bloquea la edición, pero permite cerrar el modal; desde que se pulsa «Confirmar gasto», el cierre queda protegido hasta terminar la confirmación o fallar su validación.

El listado, opciones y detalle tienen un único dueño de consulta por empresa. No hay polling. El preview se solicita por las acciones explícitas de agregar, editar o quitar una línea, o al confirmar un documento modificado, y bloquea la edición mientras está en curso para impedir respuestas antiguas sobre datos nuevos. Una creación necesita una sola lectura de la página filtrada, porque cambia su composición, totales y paginación. La edición de una plantilla reutiliza la respuesta guardada y actualiza el catálogo en memoria. El acuse de anulación no contiene el detalle resultante ni la página: se consulta cada recurso visible una sola vez para conservar el estado real, saldo y pertenencia a filtros. Al completar un pago se actualiza una vez la página de Gastos; el detalle cerrado se consulta solamente al abrirlo de nuevo.

El calendario se aloja dentro del diálogo activo, como el combo paginado. Escape dentro del calendario cierra únicamente el calendario para conservar la captura; el atajo «Elegir hoy» respeta los límites mínimo/máximo; el ajuste está en `DatePicker`, no en un calendario alterno para Gastos.

## Aceptación, rendimiento y publicación

Presupuesto: preview de 20 líneas <800 ms y de 100 <1.5 s; confirmación de 20 <2 s y de 100 <3 s en la medición local aislada. La previsualización automática usa tres comandos SQL para 1/20/100 líneas: resolución, reglas y perfiles. Con retenciones puntuales se agrega una consulta por conjunto para validar todas las cuentas seleccionadas. La confirmación no debe aumentar sus viajes al agregar líneas, incluyendo el motor financiero en la prueba instrumentada; la inicialización del primer documento puede consumir un viaje adicional. No hay SQL/HTTP por línea.

La regresión cubre cuenta directa sin concepto ficticio, plantillas existentes, retención sobre base acumulada, IVA mixto, CxP neta, cambio de reglas, replay, permisos, referencias inválidas, anulación multilínea, XML/nota y consumidores de cargos. La prueba de navegador verifica selección de cuenta, calendario, cálculo por cambio de línea, invalidación por edición, perfil incompleto, conflicto y una sola recarga del listado al aceptar, campos monetarios formateados, cero/referencia obligatoria, límites de calendario, bloqueo durante envío, alta/edición/eliminación en grilla, cancelación sin cambios ni consultas por fila, conservación de datos ante error y una lectura de cada recurso visible después de anular. El formulario se cierra únicamente después del acuse exitoso del servidor; la edición de conceptos y la anulación bloquean cambios/cierre mientras su comando está pendiente.

El esquema debe publicarse antes de habilitar captura sin concepto. Todos los consumidores de fuentes financieras y fiscales deben admitir `Lines` antes de emitir comandos nuevos. No se debe regresar a un worker anterior con documentos multilínea pendientes: sus lectores desconocen esas líneas. Ante rollback de interfaz se conserva el lector compatible y se drenan los trabajos con su propietario actual; la nulabilidad del concepto no se revierte sobre datos nuevos.

Todo despliegue permanece sujeto a un commit integrado en `origin/main`. La publicación solicitada para esta ampliación se limita a DEV; no autoriza cambios en la base productiva.
