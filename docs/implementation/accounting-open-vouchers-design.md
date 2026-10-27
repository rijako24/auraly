# Comprobantes abiertos en Trazabilidad financiera

> **Ampliación técnica del 2026-10-20:** la sección 13 define el destino solicitado
> para PUC, comprobantes, cruces y fuentes de verdad. Las secciones 1–12 describen
> el alcance anterior y su evidencia; sus exclusiones de pagos, anticipos, cruces
> por partida y captura asistida de Gastos quedan sustituidas únicamente por esa
> ampliación. Su implementación continúa por los incrementos y evidencias de esta
> sección; no está cerrado el corte global. La valoración de cargos
> posteriores admite el asiento necesario, según aclaración final del usuario;
> su atribución entre existencias y salidas requiere cierre técnico en recepciones.

Fecha de investigación y cierre de propuesta: 2026-09-30.
Estado: captura y envío implementados y validados localmente. Sin despliegue. PDF y Excel por comprobante individual; exportación de listados fuera del alcance por decisión del usuario.
Incorpora guardar, ver y editar antes de contabilizar explícitamente; dos accesos manuales y Gastos separado.

## 1. Decisión de producto

**Trazabilidad financiera** es la única bandeja. Su cabecera incorpora **Nuevo comprobante**, con icono `FilePlus2`. **Ajuste de cartera** se crea desde Cuentas por cobrar o Cuentas por pagar, en el contexto del tercero y la obligación. Se trasladan las capturas de Contabilidad a sus vistas propietarias, conservando contratos, permisos, identidad e históricos. Ambos documentos aparecen en trazabilidad para consulta y seguimiento. No se duplican pantallas ni se elimina su capacidad.

Ambos permiten **Crear → Guardar → Ver / Editar → Contabilizar**. Guardar conserva la captura en servidor como **Creado**, incluso incompleta con diagnóstico, sin fuentes, jobs, reservas, efectos contables ni DIAN. Ver no cambia el estado. Editar permite guardar y volver a abrir tantas veces como se necesite antes de aceptar la contabilización, con control de versión.

**Contabilizar**, con icono `Send`, es una acción explícita del usuario sobre el creado guardado. No hay autoenvío por guardar, cerrar o abrir. Revalida permisos, cuentas, periodo, cuadre y saldo aplicable. **Enviado a contabilidad** significa aceptación durable; **Contabilizado** significa que el motor terminó. Desde la aceptación el contenido queda congelado; procesados e históricos se consultan y sus correcciones usan el flujo canónico. Un fallo permite **Reintentar contabilización** sobre la misma identidad y snapshot.

Estos dos documentos no se transmiten a DIAN. **Gastos conserva su pantalla y flujo**, incluido documento soporte cuando corresponda. Pagos, recaudos, aperturas, devoluciones y notas fiscales conservan sus propietarios; sus resultados siguen visibles en Trazabilidad. El nuevo editor no ofrece esas operaciones como modalidades. La ampliación de Gastos se analiza en [cuentas y retenciones](expenses-account-selection-and-withholdings-design.md).

El recorrido de un gasto registrado hacia su detalle y comprobante en Trazabilidad financiera ya existe y se conserva. No se añade otra consulta de comprobantes desde Gastos ni se vuelve a capturar ese gasto como comprobante manual.

La captura libre admite cuentas, terceros, descripción, centros, débitos, créditos y referencias. El ajuste admite una CxC/CxP existente, aumentar/disminuir, contrapartida, motivo y saldo resultante. Seleccionar una cuenta no simula un pago ni emite documento fiscal.

La actual **Nota débito o crédito de cartera** llama a `ConfirmAccountAdjustmentRequest`: cambia el saldo, registra `Adjustment` y genera asiento; pasa al botón **Ajuste de cartera**. Reemplazarlo por un asiento libre perdería la actualización del auxiliar. Se conservan la devolución y su nota crédito, así como la corrección fiscal específica de factura duplicada (`FiscalDocumentService.CorrectDuplicateSaleAsync`); no se crea emisión genérica de notas electrónicas aquí.

La validación actual de contrapartida comprueba cuenta activa y contabilizable, pero no demuestra por sí sola la separación semántica respecto a pagos/correcciones fiscales. Validar uso permitido en el propietario contable al implementar, sin reglas por prefijos PUC ni nombres.

El prototipo inicial `work/comprobante-abierto.html` corresponde a la exploración anterior de alcance; este documento prevalece sobre sus modalidades y etiquetas hasta actualizar esa vista.
## 2. Investigación comparativa

Fuentes públicas oficiales consultadas; no se hicieron pruebas dentro de cuentas de los competidores. “Cigo” se interpreta como **Siigo**.

| Referencia | Evidencia | Decisión para Auraly |
| --- | --- | --- |
| Foto MantisFicc aportada | Encabezado con tipo, fecha, tercero, referencia y observaciones; grilla con cuenta, centro, base, débito/crédito y totales | Conservar captura densa y rápida por teclado; mejorar búsquedas, validación y lectura |
| Mantis Web, sitio colombiano | Publica causación de gastos con impuestos, anexos, centros, auditoría y reportes PDF/Excel | Usar esas capacidades como referencia general; no afirmar que su producto o versión sea idéntico al MantisFicc fotografiado |
| Siigo Nube | Sus comprobantes CxC/CxP admiten cuenta, tercero, detalle de saldo existente o nuevo, vencimiento, centro, partidas y PDF | Referencia documental explícita por línea, búsqueda por cuenta/tercero y vista imprimible |
| Alegra | Describe partidas por cuenta y contacto, cuadre obligatorio, ajuste de documentos y creación de obligaciones con referencia/vencimiento | Mostrar el efecto sobre el documento y separar una referencia informativa de una aplicación real |

Fuentes y alcance:

- [Mantis Web Colombia](https://mantisweb.com.co/): información comercial general; no se encontró un manual público verificable del formulario exacto de la foto. No se accedió al servidor visible en ella.
- [Siigo: comprobantes de CxC](https://siigonube.portaldeclientes.siigo.com/elaborar-comprobante-contable-de-cuentas-por-cobrar/) y [CxP](https://siigonube.portaldeclientes.siigo.com/elaborar-comprobante-contable-de-cuentas-por-pagar/): captura y cruce documental.
- [Siigo: configuración de comprobantes](https://siigonube.portaldeclientes.siigo.com/configurar-comprobantes-contables/): tipo, numeración y centros.
- [Alegra: comprobantes](https://ayuda.alegra.com/int/realiza-comprobantes-contables-alegra-gen): captura, ajustes y exportación a Excel.
- [Alegra: nuevas obligaciones](https://ayuda.alegra.com/int/crea-nuevos-documentos-desde-tus-comprobantes-o-asientos-contables-en-alegra): referencias de CxC/CxP.
- [Alegra Colombia](https://ayuda.alegra.com/col/crea-comprobantes-contables-y-gestiona-tus-cuentas): gestión y cuadre.
- [Alegra: egresos](https://ayuda.alegra.com/int/consulta-y-gestiona-tus-comprobantes-de-egreso): impresión/PDF y consulta de pagos.

El guardado sin envío automático es una decisión solicitada para Auraly, no una característica atribuida a estos competidores. Tampoco se trasladan sus opciones de editar/borrar asientos contabilizados: Auraly conserva inmutabilidad y correcciones auditadas.

## 3. Línea base verificada en el repositorio

| Capacidad actual | Evidencia | Diferencia que debe cubrir el cambio |
| --- | --- | --- |
| Bandeja existente | `admin/src/app/(dashboard)/dashboard/financial-traceability/page.tsx`, `AccountingApi.cs`, `SqlAccountingStore.ListDocumentsAsync` | Añadir creados sin job, acciones y detalle de captura; conservar documentos automáticos |
| Comprobante libre | `ManualDocumentsSection`, `ConfirmManualAccountingVoucherRequest`, `AccountingService.ConfirmManualVoucherAsync` | Ya admite múltiples partidas, tercero y centro; hoy confirma/publica directamente y no conserva borrador en servidor |
| Ajuste de cartera | `ConfirmAccountAdjustmentRequest` y `LoadAccountAdjustmentFactsAsync` | Ajusta una obligación existente; no es un pago ni un alta genérica de cartera |
| Pagos/recaudos | `PayablesService`, `ReceivablesService`, stores y `payments[]` | Conservan su captura; no simular pago con un asiento libre |
| Gastos | `ExpenseService`, `SqlExpenseStore.PersistAcceptedAsync` | Conserva su captura separada; su ampliación de cuentas/retenciones tiene propuesta propia |
| Motor | `AccountingProcessingCoordinator`, `AccountingProcessingPolicy`, `SqlAccountingPostingProcessor` | Única autoridad para aplicaciones financieras y libro mayor |
| Comprobante/reportes | `AccountingDocumentDialog`, `ReportViewer` | PDF y `.xlsx` del documento individual desde el visor compartido |
| Maestros | `AccountingAccounts`, `Parties`, centros, bancos, mappings y `reference.Options` | Opciones persistidas, búsqueda paginada y metadatos para efectos; ninguna inferencia por prefijos PUC |

Hallazgos que deben resolverse en el slice que los toque:

1. `SqlAccountingStore.ConfirmManualVoucherAsync` valida cuenta, centro y tercero mediante I/O por partida. Sustituir por carga/validación por conjuntos.
2. `accounting/page.tsx` precarga las primeras 100 CxC/CxP para el formulario. Reemplazar por búsqueda paginada por tercero; no trasladar ese límite oculto.
3. La confirmación manual ejecuta `refresh()` general y borra la captura. Reutilizar la respuesta autoritativa y conservar identidad/estado.
4. El filtro de estados de Trazabilidad tiene opciones locales. Migrarlo al catálogo canónico al ampliarlo.
5. El antiguo `openReport()` acumulaba páginas en memoria. Se retiró la acción de reporte del listado al limitar esta entrega a comprobantes individuales.
6. El diálogo consulta estado fiscal aun en fuentes financieras puras. El detalle agregado debe declarar si existe documento fiscal; sin consulta adicional para averiguarlo.

## 4. Bandeja y editor

### Bandeja

Conservar `/dashboard/financial-traceability`. Cabecera con título, **Nuevo comprobante** y **Reportes**. El botón **Ajuste de cartera** pertenece a Cuentas por cobrar/Cuentas por pagar, no se duplica aquí. Las filas ofrecen **Ver**, **Editar** y **Contabilizar** según estado, permisos y validación. Filtros combinables de fecha, origen, operación, estado contable, tercero, número y sede autorizada. Paginación y orden desde servidor; por defecto 25 documentos.

En Cuentas por cobrar y por pagar, el encabezado ofrece **Ajuste de cartera** al usuario con permiso de captura manual. Abre el editor completo en un modal de la misma vista: primero se elige el tercero con el combo paginado y luego una factura u obligación de ese tercero. Los campos del ajuste permanecen bloqueados hasta cargar la obligación. El detalle de una factura abre el mismo modal con tercero y obligación seleccionados, mostrando número, valor original, saldo y vencimiento antes de editar el importe. Los indicadores de saldo pendiente, saldo vencido y número de facturas/obligaciones usan los agregados de la respuesta de terceros o facturas que está visible. La pestaña de pagos muestra su historial y conteo; no carga un directorio de terceros oculto para reconstruir saldos. El saldo a favor de proveedores corresponde al agregado de la pestaña de terceros. Una respuesta fallida muestra importes sin confirmar, no ceros inventados.

Columnas: fecha, número/referencia, operación, tercero o “Varios”, descripción, débitos, créditos, estado, comprobante ASI, fiscal y acciones. El filtro **Creados / Sin enviar** muestra todos los comprobantes guardados de ese alcance, no solo los del usuario que los elaboró. **Todos** los incluye junto a documentos automáticos; los creados nunca se presentan como ausencia contable.

La fila del borrador y la del documento enviado representan el mismo recurso visible: no aparecen dos filas al enviarlo. Guardar actualiza la fila/total desde su respuesta; si queda fuera de los filtros se muestra acceso **Ver comprobante** sin alterar filtros silenciosamente.

### Editor

Pantalla amplia dentro de la misma ruta, con URL del comprobante para reabrirlo. Encabezado compacto, captura directa en las filas de la grilla, totales y acciones al pie. Las seis columnas principales —cuenta, tercero, centro, descripción u observación, débito y crédito— se editan sin seleccionar una fila ni abrir otro formulario. La grilla muestra un solo campo de texto por partida; el contrato conserva referencias históricas, pero no ofrece otra caja de texto redundante. Débito y crédito son columnas estrechas con cifras a la derecha; la primera columna muestra D o C según el movimiento.

| Zona | Campos / comportamiento |
| --- | --- |
| Encabezado | Operación, fecha contable, referencia externa, descripción, sede de origen, moneda funcional, tercero y centro predeterminados opcionales |
| Identidad | Identificador del comprobante creado desde su primer guardado; número definitivo del documento al enviar; número ASI solo al contabilizar |
| Grilla | Cuenta código/nombre, tercero, centro, descripción u observación, débito, crédito, acciones de línea |
| Detalle de línea | En la captura libre actual, la referencia es texto informativo y no aplica saldo. Una eventual selección tipada de factura/obligación, saldo y aplicación exige extender el propietario de cartera; no forma parte de este slice. |
| Soportes | Referencia a soporte existente; asociación de archivos por almacenamiento autorizado del sistema, nunca URL externa arbitraria o base64 dentro del asiento |
| Pie | Total débito, total crédito, diferencia, número de errores y **Guardar comprobante** |

Entrada por Tab/Shift+Tab, búsqueda por código/nombre/NIT, Enter para seleccionar, añadir/duplicar/eliminar partidas. No sobrescribir líneas ya personalizadas al cambiar el predeterminado. Para cantidades, componentes numéricos compartidos; para fechas, `DatePicker`. A partir de 100 partidas, virtualización de la captura, sin paginar un mismo documento.

El selector de cuentas reutiliza `PagedEntitySelect`: escribir una letra o dígito
con el campo enfocado abre el buscador con ese texto, sin clic previo. Conserva
la búsqueda paginada y su debounce; no solicita antes una página vacía. Enter
selecciona la cuenta del mismo buscador, sin otro modal o flujo de resolución.
Las opciones operativas incluyen cuentas comunes y auxiliares de la sede actual;
el filtro se aplica antes de paginar y contar. La caché se identifica por tenant
y sede. La configuración estructural del PUC conserva la vista del tenant.

En móvil, consulta, estados y acciones legibles; la grilla conserva desplazamiento horizontal. No ocultar débito/crédito ni saldo aplicado para hacerla caber. Desktop es la superficie principal de digitación contable.

Guardar permite un comprobante incompleto o descuadrado con diagnóstico visible **Creado · Por completar**; requiere sede, fecha, descripción general y descripción de cada partida (1 a 500 caracteres) válidas. Solo **Contabilizar** exige validación completa. No se agrega un estado persistido adicional “Por completar”: es el resultado de validación del creado.

## 5. Operaciones y límites del comprobante abierto

| Acceso | Datos | Efecto al contabilizar |
| --- | --- | --- |
| Nuevo comprobante | Múltiples cuentas, terceros, centros, referencias y partidas | `ManualAccountingVoucher`: asiento interno; no altera por sí solo obligaciones ni caja de sesión |
| Ajuste de cartera | CxC/CxP existente, aumento/disminución, importe, contrapartida, motivo y soporte | `AccountAdjustment`: saldo y asiento en la misma transacción; no registra pago/recaudo |

El catálogo y el servidor limitan el borrador a esos dos tipos. No se ofrece gasto/causación, pago, recaudo, apertura ni documento soporte dentro del editor. Tampoco altas genéricas de obligaciones, préstamos, anticipos nuevos, multimoneda o doble libro fiscal/NIIF.

La referencia a factura es informativa en un asiento libre y afecta saldo solo mediante el ajuste explícito. No duplicar venta, inventario, impuesto o gasto del original. Las cuentas de control no pueden simular creación/cancelación de cartera sin su operación propietaria; el uso permitido se valida por relaciones persistidas, no por prefijos PUC.

### Alcance del cruce documental investigado

La revisión de [Siigo Nube: comprobante de CxP](https://siigonube.portaldeclientes.siigo.com/elaborar-comprobante-contable-de-cuentas-por-pagar/) y [Alegra Colombia: ajuste de saldos](https://ayuda.alegra.com/col/ajuste-de-saldos-a-traves-de-comprobantes-contables) confirma que el comprobante manual puede tener, además del asiento libre, una aplicación explícita contra una obligación existente. Se elige cuenta de control, tercero y documento; el sistema muestra y limita el saldo. Siigo también distingue cruzar saldo de crear/modificar uno nuevo; Alegra permite varias aplicaciones en un comprobante. La referencia textual a una factura no equivale a esa aplicación.

Por tanto, el comprobante libre actual es válido para reclasificaciones, provisiones, depreciaciones y otros asientos respaldados, pero **no satisface aún** el caso de seleccionar una factura de gasto y saldarla o trasladar su deuda desde la misma grilla. `AccountAdjustment` sí modifica una obligación existente, desde CxP/CxC; el flujo de pagos posee la salida real de caja/banco. Una extensión futura debe decidir si introduce la aplicación tipada por línea dentro del mismo comprobante, con una sola aceptación atómica en el motor y auxiliar canónicos. Debe probar saldos parciales, varias facturas, concurrencia y repetición sin duplicar efectos. Referenciar un comprobante anterior como soporte o plantilla no vuelve a contabilizarlo; si ese comprobante originó una CxP/CxC, se selecciona la obligación vigente, no su texto o número.

El [artículo 7 sobre comprobantes de contabilidad](https://www.suin-juriscol.gov.co/imagenes/18/12/2019/1576700747867_Anexos%20Tecnicos%20Compilatorios%20-Incluidos%20en%20el%20Diario%20Oficial%2051166.pdf) exige fundamento en soportes, cuentas, fecha, origen, descripción, cuantía y numeración. La [DIAN distingue el soporte fiscal del costo o gasto según la operación](https://normograma.dian.gov.co/dian/compilacion/docs/concepto_tributario_dian_0000106_2022.htm). Cruzar una factura desde el comprobante no crea otra compra ni otro documento soporte y no corrige la factura electrónica original. Esta distinción se conserva antes de ampliar la captura.

Una partida de gasto no sustituye factura/documento soporte cuando se requieren. Esa operación se registra una sola vez desde Gastos. Las demás pantallas financieras conservan contratos y comportamiento; no se amplían sus capacidades como consecuencia de este traslado.

## 6. Guardar, enviar y reintentar

| Estado visible | Autoridad | Edición | Acción principal |
| --- | --- | --- | --- |
| Creado | Captura guardada, sin fuente/job | Sí, con versión | `Ver`, `Editar`, `Contabilizar` si valida; de lo contrario `Completar` |
| Enviado a contabilidad | Aceptación durable + job pendiente | No | `Ver detalle` |
| Procesando | Job con ejecución activa | No | `Ver detalle` |
| Contabilizado | Job `Posted` y asiento existente | No | `Ver comprobante`, PDF/Excel |
| Requiere configuración | Estado canónico de configuración pendiente | No | `Ver causa`; `Reintentar` cuando se corrija |
| Fallido | Trabajo con fallo visible; conserva causa/intento | No | `Reintentar` cuando el motor lo permita |

“Enviando…” es un indicador de la petición en curso, no otro estado durable. Los estados se proyectan del trabajo existente y no se copian a una segunda máquina de estados. La bandeja mantiene también los estados actuales de documentos automáticos, incluido efecto comercial sin asiento y ausencia contable real.

1. **Guardar:** persiste captura y devuelve detalle, versión, validación y fila de trazabilidad. No crea `AccountingSourceDocuments` ni `AccountingPostingJobs`. No reserva saldo de factura: otro usuario puede pagar mientras se prepara.
2. **Editar creado:** requiere versión actual. Dos pestañas no se sobrescriben; conflicto visible, conserva cambios locales y permite comparar con versión vigente.
3. **Contabilizar:** desde la bandeja o detalle del creado guardado, con resumen de importe/efecto. Si hay edición sin guardar, se exige guardarla antes. El servidor vuelve a validar permisos, periodo, cuentas, terceros, saldos disponibles y versión; no confirma con la validación antigua del navegador.
4. **Aceptación:** en una transacción del propietario nativo se fija el contenido, se crea fuente/job canónico y se vincula el creado con su fuente final. Después se activa el coordinador existente. No dejar captura enviada sin fuente durable, ni dos fuentes por doble clic.
5. **Respuesta incierta:** conservar ID/clave; consultar explícitamente el estado del mismo comprobante o repetir idempotentemente su envío. No crear un comprobante nuevo ni repetir aceptación con otro ID.
6. **Resultado:** eventos existentes actualizan el dueño de la consulta; cuando haga falta lectura por señal, una sola lectura acotada del documento afectado. Sin polling.
7. **Reintentar:** `accounting.postings.retry`; conserva ID, snapshot, hash y job. No vuelve a aplicar el ajuste ni ejecuta otra aceptación. Si el job tiene lease/retry programado activo, no inicia un intento paralelo. Un trabajo terminado devuelve su resultado sin efectos.

Un error de validación **antes** de aceptar deja el comprobante Creado y editable, con errores por campo. Un fallo **después** de aceptar conserva el contenido inmutable. Corregir configuración y reintentar no es editar importes. Una corrección de contenido aceptado debe usar el proceso canónico aplicable; no se añade un botón genérico “Editar fallido” o “Cancelar enviado” que libere reservas sin contrato de reversión.

El envío constituye la aprobación explícita del usuario autorizado para esta captura. No se agrega un circuito de doble aprobación, recordatorios, envío programado o envío masivo. Los comprobantes automáticos conservan su comportamiento actual; no pasan por el guardado manual.

## 7. Persistencia y contratos propuestos

Accounting posee la captura previa al envío. Se necesita un agregado de borrador persistente porque ni las fuentes inmutables ni los jobs son almacenamiento editable. No se crea una cola ni una tabla de trabajos de comprobantes.

- `accounting.VoucherDrafts` (nombre propuesto): ID estable, tenant/sede, operación, fecha local, moneda, concepto/observaciones, usuario/fechas, `RowVersion`, vínculo único a fuente final y fecha/usuario de envío.
- `accounting.VoucherDraftLines`: ordinal estable, cuenta, tercero, centro, descripción, débito/crédito y referencia tipada. Dimensiones consultables normalizadas; el ajuste conserva obligación, dirección e importe mediante su contrato tipado/versionado. No persiste modalidades de pago/gasto.
- La relación de una aplicación a obligación conserva ID de submayor, tipo, valor y referencia; el texto visible no es clave. Después del envío mandan la fuente inmutable y los encabezados/aplicaciones nativos.
- Unicidad por draft + envío; fuente final única por borrador; índices por tenant/sede/fecha/ID y vínculo de fuente; FK y versión SQL. La captura previa puede carecer de cuenta/importe; la aceptación final no.
- Soportes: reutilizar la capacidad autorizada de adjuntos del recurso dueño. El adaptador de adjuntos de plataforma encontrado no demuestra por sí solo autorización comercial: su integración y alcance deben validarse antes de habilitar upload. IDs y metadatos en el comprobante; no duplicar blobs.

Extender `AccountingService`/store y API de Accounting para guardar/leer/actualizar borrador y solicitar envío. Rutas propuestas bajo `/accounting/manual/vouchers`: colección de borradores, detalle por ID y `/{id}/send`. El endpoint actual de confirmación conserva compatibilidad para sus consumidores; la nueva UI no lo llama al guardar. El permiso de creación actual no se transforma implícitamente en permiso de envío.

La aceptación del borrador y su vínculo a la fuente manual comparten la transacción de Accounting. Se extienden sus puertos de aceptación actuales para ambos tipos; no hay integración nueva con pagos o gastos, ni llamadas HTTP entre módulos para enlazar commits.

Respuesta de guardar/enviar/reintentar: ID estable, versión, `allowedActions`, diagnósticos, estado, fuente final, número documental/ASI cuando exista y fila autoritativa de bandeja. Ampliación aditiva del contrato, no un GET global posterior al POST.

Trazabilidad extiende su proyección actual con los creados aún sin fuente, y une la captura enviada con su fuente en una sola fila. Proyecta origen (`Manual`/automático), operación, acciones, estado y datos fiscales explícitos. No reutilizar `MissingAccountingJob` para un creado. El detalle devuelve captura o snapshot + posting + asiento + aplicaciones en un único viaje acotado, sin consulta fiscal para fuentes no fiscales.

Numeración: conservar el cursor ASI actual como único número del asiento. Antes de él, mostrar referencia estable de creación y número nativo si se asignó al enviar; no consumir ASI al guardar, ni permitir al usuario editarlo. Las referencias externas pueden repetirse donde el contrato lo permita; no son la clave idempotente.

## 8. Validaciones, permisos y fiscal

- Al enviar: al menos dos partidas, un único lado positivo por línea y totales positivos iguales en la escala monetaria canónica. No cuadrar con una cuenta puente inventada ni tolerancia flotante del navegador.
- Cuenta activa contabilizable del tenant, tercero requerido por cuenta/operación, rol compatible, centro de la sede y periodo abierto. Centro vacío significa resolución automática vigente, congelada conforme a la decisión contable, no “sin centro”.
- Aplicaciones <= saldo disponible descontando reservas, en la misma moneda y tercero. Validación/reserva con locks transaccionales en orden estable. La fecha contable no antecede el origen aplicable ni cae fuera de activación/periodo autorizado.
- Gasto y cálculo de impuestos conservan sus propietarios; una partida manual de impuesto no sustituye bases fiscales ni genera certificados por sí sola.
- Sesión de caja: usar flujo nativo de pagos; un asiento contra el PUC de caja no prueba movimiento físico de un turno. Bancos reutilizan el auxiliar exclusivo y conciliación existentes.
- Leer: `accounting.read`. Crear/editar creado: `accounting.manual.create`. Proponer `accounting.manual.send` para aprobación/envío. Reintentar: permiso canónico actual. Los permisos nuevos se registran y asignan determinísticamente a ADMINISTRATOR; ACCOUNTANT recibe el alcance funcional definido, sin permisos de plataforma.
- Exportación exige el permiso de lectura y el de exportación aplicable. Un enlace directo verifica tenant/sede y recurso de nuevo. Nunca confiar en `TenantId`, etiquetas o acciones habilitadas del cliente.
- Auditoría: quién creó, modificó, envió, reintentó; fechas, versión, correlación, hash y transición. Sin payloads contables completos ni identificaciones sensibles en logs.
- Guardar y contabilizar estos dos documentos nunca solicita fiscal. Gastos y sus soportes mantienen el flujo nativo separado; ningún resultado contable equivale a aceptación DIAN.

### 8.1 Clasificación DIAN verificada y efecto sobre el diseño

Consulta oficial realizada el 2026-09-30. La clasificación depende de la operación y de las condiciones tributarias del adquirente/proveedor, no del nombre de la pantalla, de ser manual ni de elegir una cuenta de gasto. La foto no permite determinar esas condiciones.

| Caso | Tratamiento y propietario |
| --- | --- |
| Reclasificación, provisión o ajuste contable puro | Comprobante interno; no se convierte en documento soporte |
| Pago/recaudo de una obligación ya registrada | Referencia el documento original; no vuelve a documentar fiscalmente la compra/venta |
| Compra/gasto con factura del proveedor | Desde Compras/Gastos; conserva esa factura como respaldo, sin sustituirla por documento soporte |
| Adquisición a proveedor no obligado a facturar | Desde Compras/Gastos; evaluar documento soporte y modalidad exigible al adquirente; si es electrónico, emitir por el motor fiscal |
| Ajuste de cartera sin modificación del documento fiscal | Nota interna de cartera; no presentarla como nota crédito electrónica |
| Corrección/anulación de factura electrónica | Proceso fiscal de nota correspondiente, con referencia al original; no asiento genérico |
| Corrección/anulación de documento soporte | Nota de ajuste de documento soporte; no nota crédito de factura |

La [guía oficial DIAN](https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documento-soporte-en-adquisiciones-efectuadas-a-sujetos-no-obligados-a-expedir-factura-de-venta-o-documento-equivalente/) sitúa el documento soporte en adquisiciones a no obligados a facturar para respaldar costos, deducciones o impuestos descontables. La tabla aplica esa distinción al producto; no certifica la deducibilidad de cada gasto.

La [Resolución DIAN 227 de 2025](https://normograma.dian.gov.co/dian/compilacion/docs/resolucion_dian_0227_2025.htm), artículos 1.5.2.2.1, 1.5.2.2.2 y 1.5.2.3.4, compila las reglas: contempla operaciones individuales o acumulación semanal por un mismo proveedor; la transmisión individual corresponde al momento de la operación y la acumulada, como máximo al último día hábil de esa semana. El artículo 1.5.2.2.2 distingue también adquirentes obligados a transmitir electrónicamente y generación física para los sujetos allí previstos. No se asume que todas las empresas tengan la misma obligación.

El [Concepto DIAN 6698 de 2026](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_6698_2026.htm) distingue nota crédito de factura y nota de ajuste de documento soporte para sus correcciones/anulaciones. El [Concepto DIAN 12666 de 2026](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_12666_2026.htm) advierte que la generación extemporánea no deja de ser incumplimiento formal por poder acreditarse la realidad de la operación. Guardar un borrador no suspende el deber fiscal.

**Decisiones de interfaz y contrato:**

1. Guardar y contabilizar ambos documentos manuales nunca invoca fiscal; muestran **No aplica** como envío fiscal propio, sin ocultar una referencia a documento fiscal original.
2. El botón se llama **Contabilizar**, no «Enviar a DIAN». Los estados y reintentos de esos documentos son contables.
3. Gastos conserva su aceptación nativa: `ExpenseService.ConfirmAsync` activa fiscal cuando `HasFiscalSupport`; no es una modalidad del nuevo borrador manual ni se modifica su ciclo de confirmación aquí.
4. Trazabilidad conserva estado contable y fiscal separado para documentos automáticos de otros módulos. Reintento contable no reenvía fiscal, ni reintento fiscal vuelve a causar gasto/CxP.
5. El respaldo tributario se resuelve en su operación propietaria según condiciones aplicables. Un asiento interno no certifica deducibilidad ni reemplaza soporte exigible.

## 9. PDF, impresión, Excel y soportes

El detalle y la bandeja abren el **ReportViewer** común. Extender su capacidad; no crear un visor o renderer de comprobantes aislado.

**Comprobante individual:** PDF/impresión con empresa/NIT, sede, identificación del documento, fecha, estado, tercero(s), concepto, referencia, cuentas, centros, aplicaciones y totales. Encabezado repetido y páginas numeradas. Creados y enviados sin asiento llevan marca **NO CONTABILIZADO** y datos de captura; nunca se presentan como comprobante ASI definitivo. El contabilizado usa el snapshot persistido, sin recalcular saldos desde maestros actuales.

**Excel real `.xlsx`:** hojas Resumen, Partidas y Aplicaciones; importes numéricos, IDs/NIT/referencias como texto, fecha y moneda, filtros y cabecera fija. Neutralizar fórmulas en texto aportado por usuario. La exportación respeta columnas autorizadas; exportar no modifica estado ni envía.

**Listado:** sirve para buscar y abrir cada documento. La impresión, el PDF y el Excel se generan desde el comprobante abierto, uno por uno. No se ofrece exportación de la página ni del resultado filtrado.

PDF descargable y `.xlsx` del comprobante usan el visor compartido, habilitados únicamente desde el documento individual. Los demás reportes conservan sus acciones anteriores. Una exportación de listados requeriría una decisión de alcance posterior y, si el volumen lo exige, el propietario de Reporting; no forma parte de esta entrega.

Plantillas publicadas son versionadas; un cambio de formato no reescribe la versión histórica. Marca disponible al preparar la representación, separada de aceptación/contabilización. Un fallo de logo/PDF/impresora no cancela ni repite un comprobante. La política de marca de `invariantes-arquitectonicas-auraly.md` prevalece; se detectó texto anterior contradictorio en el mapa y debe alinearse antes de intervenir impresión operacional.

## 10. Presupuesto de rendimiento y propiedad de carga

Objetivos propuestos para validar al implementar; no son mediciones del sistema actual. Escenario: 100.000 documentos y 10.000 terceros por sede; captura habitual de 2–50 partidas y máximo inicial explícito de 500. Catálogos/facturas con páginas de 25–50. Ensayo aislado sin builds ni suites concurrentes.

| Camino | Presupuesto de aceptación | Viajes / volumen |
| --- | --- | --- |
| Bandeja | p95 <= 800 ms servidor | 1 GET por página, datos + total + facetas requeridas; máximo 2 comandos SQL de lectura |
| Buscar cuenta/tercero/factura | p95 <= 400 ms servidor | 1 GET por búsqueda estable; cancelar anteriores, sin cargas por cada fila |
| Guardar 50 / 500 partidas | p95 <= 1 s / 2 s servidor | 1 comando HTTP; cargas por conjuntos, <= 6 viajes SQL de validación/persistencia |
| Enviar 50 / 500 partidas | p95 <= 2 s / 3 s aceptación | 1 POST; <= 8 viajes SQL de aceptación, independiente del número de partidas |
| Posting | <= 2 s local para caso habitual; medir 500 aparte | mismo motor y transacción; cargas/escrituras por lote, sin I/O por partida |
| Documento PDF/XLSX | <= 3 s para 50 líneas en entorno acordado | reutiliza snapshot; 0 lecturas por línea y 0 reenvíos de fuente |

La bandeja tiene un solo propietario de carga. Editor/detalle comparte recurso; contadores no vuelven a pedir listas. El visor sustituye a la vista activa o usa datos ya cargados; no mantiene consultas invisibles. Push coalescido y reconexión con backoff compartido finito; cero polling. Indicadores distintos: Guardando, Enviando, Procesando, Preparando PDF/Excel.

## 11. Aceptación y secuencia de implementación futura

La entrega funcional requiere todos estos escenarios; no basta montar la pantalla:

1. Guardar un comprobante completo o incompleto: aparece Creado; cero mensajes, jobs, fuentes, reservas, asientos y cambios de cartera/caja/fiscal. Reabrir conserva líneas y versión.
2. Enviar desde fila/detalle: un solo documento nativo, job y asiento; fila estable y estados reales. Doble clic, dos pestañas y timeout no duplican nada.
3. Crear, guardar, ver, editar y reabrir ambos tipos conserva identidad y captura; consultar/guardar no contabiliza. La aceptación congela su versión.
4. Competencia por saldo entre creado y pago externo: revalidación rechaza exceso y deja creado editable. Dos envíos concurrentes no sobreaplican.
5. Ajuste de saldo no figura como pago; referencia informativa no modifica saldo; cuenta de control sin aplicación no se acepta como pago.
6. Gastos, pagos, recaudos y devoluciones conservan rutas y resultados; no aparecen como modalidades del editor manual.
7. Fallo forzado durante posting: rollback de todos sus efectos; reintento mismo snapshot produce un resultado único. Configuración pendiente se distingue de fallo técnico y de validación previa.
8. Contabilizado permanece inmutable. Retry terminado no repite efectos, marca, PDF, impuestos, inventario o DIAN.
9. Tenant/sede ajenos, permisos insuficientes, tercero/banco/centro inválidos y versión obsoleta fallan en servidor; UI con vacíos, errores, permisos y teclado comprobados.
10. PDF/XLSX de cada comprobante creado, enviado y contabilizado: estado correcto, números concordantes, formatos monetarios, varias páginas y ausencia de fórmulas ejecutables.
11. Pruebas instrumentadas de viajes para 2, 50 y 500 líneas; búsquedas paginadas, un dueño de recurso, cero GET global postmutación y cero polling.
12. Regresiones de las suites `AccountingVerticalSliceTests`, `PayablesVerticalSliceTests`, `ReceivablesVerticalSliceTests`, `ExpenseSupplierSelectionTests`, pruebas de idempotencia manual y E2E del recorrido guardar → bandeja → enviar → resultado/reintentar.
13. Traslado completo: las dos capturas manuales dejan de estar en Contabilidad; el comprobante se crea desde Trazabilidad y el ajuste desde CxC/CxP. Ambos se consultan en Trazabilidad; abrir históricos no los reenvía ni genera nuevas fuentes.
14. Guardar/editar/contabilizar/reintentar documentos manuales no crea proceso fiscal. Gastos y devoluciones conservan sus procesos fiscales y estados independientes en la bandeja.
15. Ajuste de cartera, nota crédito de factura y nota de ajuste de documento soporte se distinguen en selector, detalle y PDF; el ajuste interno no permite anular ni corregir silenciosamente una factura electrónica. La devolución conserva su flujo e impresión, y la corrección fiscal de factura duplicada conserva su propietario. El traslado no altera ni elimina ninguno de esos casos.

Orden del slice: persistencia editable/contratos y proyección de trazabilidad; guardado sin efectos; envío transaccional a propietarios existentes; editor/bandeja/estados; visor y exportaciones compartidos; validación integrada y auditoría de diff. Las capacidades incompletas no se muestran como utilizables.

Despliegue futuro aditivo por DACPAC, sin backfill ficticio de borradores de fuentes antiguas. Los documentos actuales conservan IDs, hashes, números y posting. Cutover retira el formulario duplicado, conserva contratos públicos necesarios. Rollback de UI mantiene datos/fuentes/jobs; no borra comprobantes ni devuelve a edición lo enviado. Todo despliegue requiere commit integrado en `origin/main` y una instrucción de publicación.

## 12. Implementación y límites verificados (2026-09-30)

- `accounting.VoucherDrafts` y `VoucherDraftLines` conservan exclusivamente captura editable, auditoría y versión. Guardar no crea fuentes, jobs, movimientos ni solicitudes fiscales. El payload inmutable y su job siguen siendo los de Accounting.
- El envío bloquea la captura, verifica versión, activa el propietario canónico y congela el documento en la misma transacción que acepta la fuente. Un replay de envío ya aceptado es una lectura; no republica señales.
- `accounting.manual.create` permite capturar; `accounting.manual.send` permite contabilizar. Los contratos de confirmación directa conservados requieren también `manual.send`; un ID con captura no puede saltarse la versión por esa ruta. Administrador y Contador reciben el permiso canónico; roles personalizados deben asignarlo explícitamente.
- Los ajustes se lanzan en CxC/CxP sobre una obligación concreta. La aceptación serializa por obligación y descuenta pagos aceptados y disminuciones manuales aún no aplicadas. `accounting.ObligationAvailability` consulta las aplicaciones y el índice de fuentes aceptadas por obligación, sin usar el borrador como autoridad ni tomar el job mientras se bloquea la obligación; aplicar/reintentar sigue perteneciendo al processor contable.
- Cuentas, terceros y centros se validan por conjunto. Cada fila visible reutiliza el selector PUC paginado por código/nombre; las búsquedas se activan al abrirlo y no se consulta por cada partida. Guardar 2, 50 o 500 partidas usa cuatro comandos SQL, con prueba instrumentada.
- La bandeja incluye creados, enviados e históricos. Su consulta se filtra y pagina en servidor, y conserva el total incluso cuando una página queda vacía. El filtro de tercero usa `PartySelect`. No hay polling ni precarga de toda la colección; guardar actualiza la caché con la respuesta autoritativa. El endpoint de líneas de trazabilidad permanece como contrato público existente, aunque la interfaz ya no lo usa para exportar listados.
- El visor compartido descarga PDF y XLSX reales de un comprobante a la vez. La captura sin asiento lleva `NO CONTABILIZADO`; el asiento se abre desde su detalle persistido. Texto de Excel no se interpreta como fórmula. La captura individual exporta todas sus partidas.

**Fuera del alcance actual:** exportación de listados, libro de varias hojas y adjuntos comerciales. La decisión posterior del usuario limita la entrega a imprimir y exportar un comprobante por vez. Estas capacidades no se anuncian en la interfaz ni se consideran tareas pendientes de esta entrega.

La auditoría corrigió permisos de la confirmación antigua, replay sin efectos, bloqueos de saldos pendientes, alcance de queries y una superposición del aviso PWA sobre Guardar/Contabilizar. Rollback: retirar los accesos nuevos conserva capturas y documentos enviados; nunca borrar fuentes/jobs ni reabrir aceptados. La publicación continúa requiriendo integración en `origin/main`.

Evidencia local: compilación SQL y backend sin advertencias; build Next con 84 rutas; 30 pruebas unitarias frontend. Los escenarios de navegador de PUC (3), búsqueda (1), gastos (5), devoluciones (4) y comprobante (1) pasaron. El escenario del comprobante verifica captura, error sin pérdida de datos, edición, envío explícito, bloqueo posterior y archivos PDF/XLSX descargables. Las pruebas de navegador simulan las respuestas HTTP; las pruebas de integración SQL comprueban por separado permisos, escritura, saldos e idempotencia. No equivalen a un despliegue ni a una sesión del usuario contra su base local.

La corrida aislada `tmp/expenses-validation/vouchers-isolated.trx` terminó con 8/8: comprobante editable/envío versionado, reserva de ajustes sin doble aplicación, cantidad constante de comandos, dos regresiones de edición PUC, captura de gasto con cuenta directa, precio final de producto genérico en reporting y escenario contable integral. Guardar 2/50/500 líneas tomó 220/46/51 ms y cuatro comandos SQL en cada caso. Son muestras locales instrumentadas, no un p95 de carga con 100.000 documentos. El recorrido integral de anulación de gasto, que había superado su umbral con Next y navegador concurrentes, pasó al repetirlo aislado, sin modificar el presupuesto.

Verificación final del artefacto: build Next completo, lint de los componentes tocados y `git diff --check` sin errores; comprobante probado nuevamente sobre el servidor standalone (1/1, 10,1 s de escenario). Para la prueba interactiva local se creó `AuralyVoucherDemo_20260930` desde el DACPAC en modo de desarrollo, con credenciales de demostración separadas. Frontend y API escuchan en `127.0.0.1:3000` y `127.0.0.1:5097`; ambos responden 200. El inicio de sesión real pasó en navegador (1/1), al igual que la apertura de Trazabilidad, la búsqueda PUC y un comprobante `Created` que terminó `Posted` como `ASI-0000000001`. `AuralyLocal` no fue migrada y conserva su esquema anterior.

Al reducir el alcance a un comprobante por archivo se retiraron el botón de reporte de la bandeja, su consulta y sus tipos del cliente, y las ramas de paginación sin consumidores del visor compartido. El endpoint de líneas del servidor precede este cambio y permanece como contrato público probado. El build actualizado y la prueba de navegador (1/1) verifican que el listado no ofrece exportación, mientras el comprobante individual conserva PDF y Excel.

La búsqueda por tercero del listado se resuelve en SQL antes de paginar. La respuesta de guardar actualiza la fila visible sin un GET adicional. Los ajustes de cartera conservan la pertenencia de la obligación al filtro al actualizar la fila, y sus movimientos se presentan como ajustes de saldo, no como pagos.

Riesgo de plataforma previo al slice: `npm audit --omit=dev` reporta cuatro paquetes de producción con avisos (Next crítico, sharp y xmldom altos, baseline-browser-mapping moderado). Las dependencias nuevas de exportación no aparecen en el informe. No se aplicó un upgrade transversal de framework bajo esta tarea; la publicación requiere atender el aviso de Next. El informe reproducible está en `tmp/voucher-dependency-audit-final.json`.

### Cierre del diseño original

Quedan cerrados ubicación, captura, guardado separado del envío, estados, reintento, propietarios financieros, alcance funcional, exportaciones, validaciones y criterios de aceptación. Esta propuesta no autoriza cambios en los motores ni afirma que esas brechas ya estén resueltas.

Antes de implementar hay que concretar, dentro de sus propietarios, la participación transaccional del borrador en la aceptación manual de Accounting, la capacidad comercial de adjuntos y el renderer/exportador compartido para PDF descargable/XLSX. Son dependencias técnicas identificadas, no pantallas o motores alternos. Si exigen una arquitectura diferente de la aquí descrita, se presenta esa decisión antes de codificar.

Auditoría del diseño: utiliza un motor financiero, una bandeja y un writer de asientos; separa captura mutable/fuente inmutable; mantiene tenant, permisos, concurrencia, idempotencia y rollback; detecta y exige corregir N+1, refetches y catálogo local en el recorrido. Esta revisión corresponde a la etapa de diseño original. La implementación y evidencia posterior se distinguen arriba; los criterios sin prueba siguen pendientes.

## 13. PUC configurable y comprobante como origen financiero — 2026-10-20

### 13.1 Alcance y autoridad

Esta ampliación documenta las instrucciones del usuario del 20 de octubre. El
usuario autorizó iniciar su implementación; no autorizó desplegar a producción. Conserva los cuatro motores existentes; el
único escritor de asientos y efectos financieros sigue siendo
`SqlAccountingPostingProcessor`, coordinado por `AccountingProcessingCoordinator`.
No se crea un motor de comprobantes, un libro financiero alterno ni una cola nueva.

Comprobante es la representación común del hecho financiero. Compras conserva
productos, cantidades y recepción física; Gastos prepara partidas de manera
asistida por conceptos con cuenta automática, sin selector de cuentas en su
formulario; véase la decisión prevalente del documento de Gastos. Una operación produce una sola fuente financiera, aunque pueda abrirse
desde varias pantallas. Un enlace a esa fuente no vuelve a reconocerla.

La libertad del contador significa elegir cuentas, terceros, centros y lados del
asiento. Una cuenta que controla cartera exige también identificar qué saldo abre
o cruza. Permitir un asiento libre sobre esa cuenta sin su auxiliar produciría
precisamente las dos verdades que este diseño elimina.

### 13.2 Matriz de fuentes de verdad

| Dato | Autoridad y persistencia | Consumidores y regla de conciliación |
| --- | --- | --- |
| Identidad y significado de cuenta | `AccountingAccounts`, jerarquía y configuración PUC | Editor, mappings y reportes consumen la misma cuenta; no contiene saldo editable |
| Hecho financiero aceptado | `AccountingSourceDocuments`: identidad, payload versionado, hash y modo congelados | El motor lo interpreta una sola vez; guardar borrador no lo crea |
| Asiento definitivo | `AccountingEntries` / `AccountingEntryLines` | Mayor, balance y total por cuenta/tercero/sede suman las mismas partidas contabilizadas |
| Obligación y vencimiento | `Payables` / `Receivables`, vinculados a la partida de origen | Cartera conserva detalle documental; `OutstandingAmount` es proyección de apertura y transacciones, nunca un saldo editable |
| Aplicaciones y ajustes de obligación | `PayableTransactions` / `ReceivableTransactions`, con fuente y línea | El motor escribe efecto y asiento en la misma transacción cuando corresponde; los pagos conservan sus aplicaciones documentales |
| Pago real y medios | `SupplierPayments` / `CustomerPayments`, tenders y aplicaciones existentes | Un pago tiene una sola fuente; no sumar su cabecera y su asiento como dos movimientos |
| Crédito disponible o anticipo | `SupplierCredits` / `CustomerCredits`, ampliados con naturaleza y origen por partida | Disponibilidad derivada de apertura, aplicaciones y reversos de fuentes inmutables; no crear otra tabla de anticipos con saldo autónomo |
| Caja y sesión | Documentos y movimientos de sesión canónicos | El cierre operativo reconoce aceptados según su contrato; la conciliación con libro distingue pendientes contables y evita contarlos dos veces |
| Banco en libros | Partidas contabilizadas vinculadas al banco resuelto | Extractos y cruces son evidencia de conciliación; su saldo no reemplaza al mayor |
| Cantidad y valoración de inventario | `InventoryMovements` y proyección `InventoryBalances`, escritos juntos por `SqlInventoryLedgerWriter` | Existencias leen el balance canónico; conciliación/reconstrucción usa el kardex. El libro recibe su efecto financiero, no calcula otro promedio |
| Compra y reparto de un cargo | Recepción y asociaciones/asignaciones de Purchasing | El monto repartido se deriva de asignaciones vigentes; no cambia el saldo por pagar |
| Preparación y precio publicado | `ProductPricePreparations`, `PriceRevisionProposals`, `ProductPrices` y auditorías existentes | Preparar no publica; venta histórica conserva su snapshot |
| Impuestos y fiscal | Snapshots tributarios y `FiscalDocuments`/procesos existentes | El asiento consume el cálculo congelado; no recalcula tarifas ni reemplaza estados DIAN |
| Totales de reporting | Proyecciones del motor de reporting, con origen y checkpoint | Reconstruibles, sin edición independiente ni nueva regla financiera |

Hay distintos hechos, no varias autoridades para el mismo hecho. Por ejemplo,
una factura de 5.000.000 puede tener 2.000.000 pendientes de pago y 3.000.000
pendientes de repartir: son dimensiones diferentes. Ni la cuenta ni la ficha del
tercero almacenan otro total manual de deuda.

Toda lectura presenta su corte: aceptado, aplicado o contabilizado. El total del
mayor no incluye pendientes. La disponibilidad para un nuevo cruce descuenta
aplicaciones aceptadas pendientes, aunque todavía no hayan reducido el saldo
contabilizado. No se presenta una diferencia de procesamiento como un descuadre.

### 13.3 Configuración del PUC

Decisión prevalente del usuario del 21 de octubre, contrastada con el PUC actual
 de CJ Distribuciones: el auxiliar se crea con numeración libre dentro de su rama.
Se elimina el generador obligatorio de formatos y segmentos. No existen códigos
contables obligatorios en sedes, bancos ni medios de pago.

- `AccountingAccounts` conserva código numérico único por tenant (hasta 32 dígitos,
  incluidos ceros), nombre, tipo, agrupación/movimiento, tercero, padre, naturaleza
  de saldo y función financiera. La función usa `reference.Options`; general no
  exige banco, medio ni sede. El código y tipo mantienen su identidad al editar.
- Las clases 8 y 9 conservan tipos propios `OrderDebit` y `OrderCredit`, con
  etiquetas en el catálogo existente. Se capturan, contabilizan y consultan en
  el mismo comprobante, diario y balance de prueba. No se clasifican como activo,
  pasivo o resultado ni se incorporan a los estados de situación/resultados.
  No generan cartera ni tesorería por tener un movimiento de control.
- La función **Costos de compra por distribuir** identifica explícitamente un
  activo transitorio de naturaleza débito, sin banco ni medio de pago. No es
  un anticipo ni mueve dinero por su clasificación. Su reconocimiento y reparto
  utilizan el comprobante y los motores existentes; las reglas de elegibilidad
  y no duplicación de inventario están en `goods-receipts-design.md`. El código
  del auxiliar sigue siendo libre. No se reconfiguran cuentas usadas ni se
  incorporan saldos históricos por inferencia.
- El contador escribe el sufijo que necesite: sede, concepto, vehículo u otra
  convención propia. El padre explícito debe ser una agrupación activa del mismo
  tenant, tipo y prefijo, con código más corto; esto impide ciclos. Las referencias
  no se deducen de posiciones del código ni del nombre. No se crea un maestro de
  placas ni un nuevo motor de segmentación.
- Banco, sede y medio son propiedades independientes, cuando correspondan. Un
  auxiliar compartido no fija sede: el movimiento conserva su sede operativa.
  Banco y tarjeta de crédito por pagar requieren cuenta bancaria real; no se
  exige banco para mantenimiento, gastos o cartera. La función de control exige
  tercero; naturaleza débito/crédito no restringe el lado de cada partida.
- Un único editor y los contratos existentes de crear/editar cuentas gestionan
  estas propiedades. Se retiran endpoints, DTOs, tablas y pantalla del generador
  experimental, nunca desplegado. Los clientes antiguos pueden seguir creando
  cuentas sin clasificación nueva; no se inventa su función financiera.
- Antes de uso se pueden corregir las propiedades explícitas con rowversion.
  La aceptación canónica protege las cuentas referenciadas explícitamente en el
  payload, incluso con posting pendiente; el processor protege las que resuelve
  al contabilizar. `ConfigurationLockedAt` es una marca derivada de protección,
  sin importes ni edición independiente. Replay no repite esa escritura. Una
  cuenta utilizada conserva su significado; nombre y exigencia de tercero
  mantienen los controles existentes. La fijación de identidades de mappings
  nativos al aceptar está en implementación y verificación; no se presenta la
  protección anterior como prueba de ese corte.
- Las fuentes de modo comercial explícito (`AccountingEntryRequired=0`) no
  bloquean la configuración del libro. El backfill conserva protección
  conservadora para fuentes antiguas cuyo modo es desconocido (`NULL`); no
  deduce que nunca fueron contables por la ausencia momentánea de un asiento.
- El backfill protege asientos anteriores y referencias explícitas históricas
  mediante el mismo parser de aceptación, por lotes de 100 fuentes. El corte
  requiere detener escritores antiguos; rollback conserva fuentes/asientos y
  la protección, nunca reasigna cuentas históricas.

Ejemplos del PUC entregado por el usuario: auxiliares de caja por sede, bancos por
cuenta real, y mantenimiento por concepto y vehículo. Cada rama puede tener su
propia convención de numeración. No se requiere configurar segmentos previamente.

**Preparación de la carga de CJ en DEV (2026-10-21):** el PDF contiene 1.472
cuentas. Regla aprobada: preservar `Código` y completar con `Cuenta NIC` solo
cuando esta última es numérica y prolonga el prefijo recortado; 126 códigos se completan y
el resultado tiene 1.472 códigos únicos. Se conservan las seis diferencias
reales entre las columnas. El `+` impreso al final de la cuenta NIC `15921516+`
no completa un código numérico: se conserva `Código=15921516`.
Los nombres de `Nombre` prevalecen sobre diferencias
semánticas con `Nombre NIC`; no se sustituyen nombres locales por etiquetas NIC
genéricas. El PDF no acredita números bancarios completos ni relaciones de
sede/medio: esas referencias requieren maestros existentes inequívocos.

La inspección autorizada de `auraly-dev` encontró CJ con 91 cuentas iniciales,
sin asientos ni configuración de activación, una CxP existente y el esquema anterior, sin las
columnas del auxiliar ni tipos de orden. No se escribió ni reemplazó el PUC.
Completar la carga y sus propiedades requiere primero el esquema compatible
publicado desde un commit integrado en `origin/main`; no se cambia el esquema
de DEV mediante la herramienta de carga ni se publica este checkout sucio.
La regla temporal de acceso a SQL se retiró después de la inspección.

Presupuesto del camino: crear cuenta configurada, máximo tres comandos SQL;
editar propiedades, máximo tres; edición simple conserva un comando. El selector
de padre filtra agrupaciones activas antes de contar/paginar (una consulta por
página). Cada operación de configuración individual debe terminar en menos de
dos segundos en la integración local aislada. No hay I/O por partida ni polling.

Una tarjeta no define por sí sola activo o pasivo: cobro pendiente del adquirente,
salida de una cuenta bancaria y deuda con tarjeta de crédito tienen funciones
distintas. La configuración identifica la función y la cuenta concreta. Cuentas
de compensación no se concilian como dinero ya ingresado al banco. La ampliación
de bancos y su migración tienen un único propietario en
[bancos y conciliación](bank-accounts-and-reconciliation-design.md).

Los mappings existentes siguen siendo **defaults de capturas asistidas**. En un
comprobante manual manda el auxiliar validado elegido. En un pago de obligación
manda el auxiliar histórico de esa obligación, aunque cambie el mapping vigente.
En captura asistida, cuenta bancaria, sede y medio deben resolver un único auxiliar;
si faltan o hay varios, se pide seleccionar/configurar, sin elegir el primero.

### 13.4 Tipos, terceros y contrato de captura

| Tipo visible | Efecto |
| --- | --- |
| Comprobante contable | Reconocimiento, reclasificación o cruce sin nueva salida de dinero; admite gasto y aplicación de anticipo |
| Documento soporte | Reconocimiento de adquisición con soporte fiscal aplicable; usa el motor fiscal y sus requisitos |
| Anulación de documento soporte | Selecciona soporte original; prepara reverso financiero y nota de ajuste por el propietario fiscal |
| Egreso | Salida de caja/banco, pago de obligación o anticipo; no exige una factura pendiente |

La captura financiera v4 rechaza un egreso compuesto únicamente por cuentas
generales: requiere la salida de tesorería reconocible por los auxiliares. Un
asiento puro conserva el tipo **Comprobante contable**. La validación está en
el normalizador existente, antes de aceptar fuente/trabajo; no crea otra regla
de ejecución, consulta, estado ni flujo de recuperación. La regresión reprodujo
la aceptación incorrecta antes del cambio y después pasaron siete pruebas de
normalización y siete escenarios SQL/API de tesorería: rechazo sin fuente, job,
asiento ni caja, asiento puro válido y egresos/ingresos reales por efectivo,
transferencia y tarjetas con sus permisos, rollback y replay. Se auditó el
cambio: es una validación de forma sin I/O adicional ni migración y conserva
las capturas anteriores y los snapshots ya aceptados; no reescribe historia.

`VoucherType` visible es diferente de `SourceDocumentType` técnico. Ventas,
compras, devoluciones, recaudos y aperturas conservan sus tipos técnicos, IDs,
numeración, permisos e historial. Los cuatro tipos son opciones de creación del
editor; no se renombra toda la historia a `ManualAccountingVoucher`, ni se
convierte un recaudo en egreso. Documento soporte no es sinónimo de cualquier
factura y su anulación fiscal no es borrar el asiento.

Extender los contratos y tablas `accounting.VoucherDrafts` / `VoucherDraftLines`:

- Encabezado: tipo visible, `MainPartyId`, fecha, sede, referencia, descripción,
  versión, moneda y referencia al original cuando se corrige/anula.
- Partida: identidad estable de línea, cuenta, tercero, centro, descripción,
  débito/crédito, referencia y efecto auxiliar tipado opcional. La referencia
  libre sigue siendo texto; no consume saldo.
- Efecto auxiliar: abrir obligación/crédito, aplicar saldo o ajustar obligación;
  identifica auxiliar, partida de origen, vencimiento si abre, importe y grupo
  de cruce. Cada partida afecta una posición; seleccionar varias obligaciones
  genera varias partidas, aunque compartan cuenta. El grupo conserva su total.
- Partidas de tesorería conservan cuenta, banco, medio y referencia de pago
  resueltos. La confirmación comprueba que coincidan con el auxiliar.
- Los detalles fiscales y tributarios necesarios se capturan por los contratos
  existentes. Elegir Documento soporte no basta para inventar descripción,
  identificación, base, impuesto o numeración fiscal ausentes.

El tercero principal identifica el comprobante y propone el tercero de líneas
nuevas. Cada partida conserva su tercero real; cambiar el principal no reemplaza
líneas personalizadas. Los filtros de trazabilidad distinguen principal y tercero
en partidas. Las búsquedas de pendientes usan principal como filtro inicial,
pero cada aplicación valida tercero, cuenta, moneda y sede en servidor. No se
cruzan saldos de terceros distintos por compartir encabezado.

La captura de cuenta permite escribir inmediatamente por código/nombre, con
búsqueda paginada y navegación por teclado. Intercambiar D/C actúa sobre la línea;
si existe aplicación, debe quitarla o volver a validar su dirección. Copiar un
comprobante crea otro borrador: nuevas identidades y sin aplicaciones consumidas,
numeración, aceptaciones, estados fiscales ni efectos. Conserva partidas y
referencias informativas, y revalida cuentas/terceros al enviar.

#### Documentos pendientes del tercero: importe y Agregar

Al seleccionar el tercero principal, el editor muestra **Documentos por pagar**
con las posiciones abiertas de compras, gastos y comprobantes que hayan creado
obligación. Es una consulta común de `Payables` y sus fuentes; no una lista
independiente de facturas de cada módulo. Una factura aparece aunque se haya
registrado desde captura asistida. Para un pago se utiliza el tipo **Egreso**
dentro del mismo editor; un cruce de anticipo sin salida utiliza Comprobante contable.

| Dato visible | Significado |
| --- | --- |
| Documento, origen, fecha y vencimiento | Número consultable; Compra, Gasto o fuente correspondiente |
| Cuenta: código y nombre | Auxiliar histórico de la posición que se va a cruzar |
| Tercero: identificación y nombre | Tercero de esa partida; no sustituirlo por el principal |
| Débito / Crédito de origen | Lado e importe de la partida que abrió la posición, claramente rotulados |
| Saldo pendiente / Disponible para abonar | Distingue saldo aplicado de disponibilidad descontando reservas aceptadas |
| Abono | Importe editable de esta aplicación, mayor que cero y no superior al disponible |
| Agregar | Incorpora la partida al comprobante en edición, con su vínculo documental |

El usuario escribe, por ejemplo, 200.000 frente a una CxP de 500.000 y pulsa
**Agregar**. La grilla recibe cuenta y tercero originales, documento aplicado,
descripción, débito 200.000 y crédito cero. No copia el débito de gasto de la
factura: cruza su posición acreedora. Se puede abrir el comprobante de origen
para consultar todas sus partidas, sin cargarlas automáticamente en el egreso.

Puede repetir con otras compras o gastos del mismo tercero. Cada posición tiene
su partida y referencia, aunque varias compartan cuenta. Si vuelve a agregar la
misma posición, se actualiza el importe de su partida existente en este borrador;
no se crean aplicaciones repetidas por doble clic. La lista indica **En este
comprobante** y descuenta esa selección para ayudar a capturar, sin presentarla
como una reserva financiera. Editar o quitar la partida actualiza ese importe
local. Al editar una posición ya agregada, Abono representa su nuevo total y el
máximo excluye su propia selección anterior para no descontarla dos veces.
Guardar el borrador todavía no consume saldo; la aceptación revalida y
reserva transaccionalmente según 13.7.

Agregar no paga, contabiliza ni acredita un banco implícito. El usuario completa
las contrapartidas de caja/banco del egreso y el servidor exige cuadre. Cambiar
cuenta o tercero de una partida aplicada exige quitar/reseleccionar su aplicación;
no conservar un enlace incompatible. Cambiar el principal actualiza la búsqueda
visible y conserva las partidas ya personalizadas con sus terceros explícitos.

La lista usa búsqueda y paginación del servidor por tercero, sede autorizada,
cuenta, documento y vencimiento, con orden estable y hasta 50 posiciones por
página. Cada página trae cuentas, terceros, fuente y saldos juntos; no consulta
el detalle de cada compra/gasto por separado. Solo el panel visible carga; el
editor posee el recurso y las selecciones. Se aplican los presupuestos de 10 y
13.9, sin polling. Un conflicto al enviar conserva la captura e informa las
posiciones cuyo disponible cambió; no reduce importes silenciosamente.

La consulta común `GET /accounting/open-positions` está conectada al ajuste
existente del editor. Lee `Payables`/`Receivables` y sus fuentes existentes; no agrega otra
tabla de obligaciones. Identifica al tercero por `PartyId`, también para CxC
sin rol de cliente. En el alcance inicial del editor solo ofrece COP. Expone
el auxiliar de la partida de apertura, sus débitos/créditos y el disponible de
`accounting.ObligationAvailability`; un vínculo ausente o inconsistente tiene
diagnóstico explícito, nunca una cuenta tomada de la parametrización actual.
Los filtros se aplican antes del conteo y del límite; una sola selección conserva
claves, vencimientos e importes estrechos para compartir el conteo y la página.
Se materializan como máximo 50 posiciones completas antes de enriquecer cuentas
y reservas. El resumen conserva su
conteo/suma incluso al pedir una página vacía. Presupuesto: un viaje SQL por
página, menos de 750 ms por página caliente con historial representativo,
incluidas primera, siguiente, tercero y ausencia de resultados. La fecha de
vencimiento se filtra como fecha civil guardada en el documento. Esta consulta
no habilita pagos ni resuelve el corte de apertura de 13.8 por sí sola.

Evidencia local del 21 de octubre: 34 regresiones SQL/API pasaron, incluidas
reservas de ajustes directos y desde borrador, concurrencia con pagos/recaudos,
permisos y terceros sin rol de cliente. La prueba aislada con 21.000 fuentes y
21.000 posiciones verificó un viaje SQL por lote/página y búsqueda por el índice
de obligación. Los lotes de 500 posiciones tardaron 74,8 ms (CxP) y 72,4 ms (CxC).
Primera/siguiente/tercero/sin resultados/página vacía tardaron entre 112,9 y
516,3 ms, por debajo de 750 ms, sin compiladores ni frontend concurrentes. Tres
escenarios de navegador con HTTP simulado comprobaron cuenta/disponible,
diagnóstico de apertura ausente, selección sin GET adicional, filtros actuales
por tercero/sede y acceso a la recepción. No sustituyen la regresión integrada
con API real ni la aceptación del rediseño completo.

#### Clonar comprobante completo para reutilizar cuentas

El detalle ofrece **Clonar comprobante** y abre un nuevo borrador editable con
las cuentas, tercero principal, terceros de partidas, centros, descripciones y
valores del original como propuesta. Así, para la luz del mes siguiente se
reutilizan las cuentas y se revisan fecha, factura e importe, sin digitarlas otra
vez. Es una acción del usuario, sin agenda ni generación mensual automática.

El nuevo documento tiene IDs propios, fecha contable actual propuesta y
`ClonedFromDocumentId` solo para trazabilidad. No hereda consecutivo, número de
factura externa, vencimiento, referencias bancarias de ejecución, aplicaciones
a obligaciones/anticipos, vínculos de reparto, aceptación ni estado fiscal.
Si conserva un tipo que necesita documento original o datos fiscales, queda
incompleto hasta seleccionarlos de nuevo. La descripción copiada sigue editable;
no constituye evidencia de una factura nueva.

Clonar un egreso reutiliza sus cuentas y valores, pero exige seleccionar los
pendientes actuales o definir un destino válido nuevo, como un anticipo, antes
de enviarlo. Clonar la causación de un gasto prepara
una obligación nueva y exige los datos de la nueva factura; no vuelve a abrir
la anterior. Se recalculan impuestos/retenciones por su propietario cuando
corresponda; no reutilizar un cálculo fiscal antiguo como vigente.

La operación de clonación requiere permiso de lectura del origen y captura en
la sede destino, conserva el aislamiento por tenant y usa el guardado canónico.
Una solicitud repetida con el mismo ID de nuevo borrador devuelve ese borrador;
otra clonación intencional obtiene otra identidad. Cuentas inactivas o dimensiones
incompatibles quedan señaladas para corregir antes del envío, sin sustituirlas
automáticamente. Clonar y guardar no generan pagos, trabajos contables ni DIAN.

### 13.5 Cruces, anticipos y correcciones

Ejemplos sin impuestos, en COP, sobre cuentas configuradas:

| Operación | Débito | Crédito | Efecto documental |
| --- | --- | --- | --- |
| Registrar gasto 5.000.000 | Gasto 5.000.000 | CxP proveedor 5.000.000 | Abre obligación 5.000.000 |
| Pagar 2.000.000 | CxP proveedor 2.000.000 | Banco 2.000.000 | Reduce esa obligación a 3.000.000 |
| Anticipar 1.000.000 | Anticipo proveedor 1.000.000 | Banco 1.000.000 | Abre saldo a favor, sin factura ficticia |
| Aplicar ese anticipo | CxP proveedor 1.000.000 | Anticipo proveedor 1.000.000 | Reduce deuda y anticipo; no crea otro pago ni movimiento de caja |

La lupa de **cruzar pendiente** propone el lado contrario del saldo seleccionado:
una CxP acreedora se debita para pagarla; un anticipo de proveedor deudor se
acredita para aplicarlo. No invierte el gasto ni todas las partidas del documento
original. La lupa de **asociar a compra** solo crea el vínculo de Purchasing.

Cada obligación conserva su cuenta de origen. Si una factura abre dos cuentas,
se crean dos posiciones documentales enlazadas a sus partidas; el listado puede
agruparlas para presentarlas como una factura, sin perder sus cuentas. Un ajuste
que aumenta obligación conserva su cuenta; una reclasificación entre auxiliares
extingue una posición y abre la nueva dentro de la misma fuente, con referencia
mutua. No cambia el `AccountId` de la posición histórica.

#### Convergencia de abonos desde cartera y desde el editor contable

**Revisión del código del 2026-10-20:** las pantallas de CxP, CxC, Gastos y POS
reutilizan `PortfolioPaymentWizard`. Para proveedores llama a
`PayablesService.ConfirmPaymentAsync` y para clientes a
`ReceivablesService.ConfirmPaymentAsync` (con entradas POS que conservan permisos
propios). Los servicios validan `PayableSettlement` / `ReceivableSettlement` y
`PaymentTenderBreakdown`; los stores aceptan pagos en transacción serializable.

| Etapa actual | Evidencia y propietario |
| --- | --- |
| Capturar documentos, abonos y medios | `admin/src/components/payments/portfolio-payment-wizard.tsx`; mismo componente para ambas direcciones |
| Validar identidad, moneda, permisos e importes | `PayablesService` / `ReceivablesService`, objetos de liquidación y desglose de medios existentes |
| Aceptar y reservar | `SqlPayablesStore.AcceptPaymentAttemptAsync` / `SqlReceivablesStore.AcceptPaymentAttemptAsync`: cabecera de pago, tenders, aplicaciones, fuente y job en un commit |
| Evitar sobreaplicación | Stores bloquean posiciones y descuentan aplicaciones aceptadas pendientes y ajustes de disminución ya enviados |
| Aplicar y contabilizar | `SqlAccountingPostingProcessor.ApplyPayablePaymentFinancialEffectsAsync` / `ApplyReceivablePaymentFinancialEffectsAsync`: saldo, transacción, aplicación, pago procesado y movimiento de sesión, más asiento cuando aplica |
| Consulta e historial | `SupplierPayments` / `CustomerPayments` y sus aplicaciones; saldo de `Payables` / `Receivables` |

El comprobante manual actual aún no paga esos pendientes: su validación bloquea
las cuentas efectivas mapeadas a CxC/CxP y dirige a la operación propietaria.
Los ajustes de cartera existentes modifican saldo, pero no equivalen a un pago.
Quitar el bloqueo y escribir únicamente débitos/créditos dejaría el mayor sin
actualizar cartera, historial ni caja. El diseño exige reemplazar ese bloqueo
por validación de efectos tipados para **todas** las cuentas con función auxiliar,
no solo para los dos mappings predeterminados.

**Decisión:** conservar las dos superficies y un único camino de aceptación y
aplicación para cada efecto financiero. Compartir solo el writer del asiento es
insuficiente. La captura asistida y la manual deben converger antes de comprobar
disponibilidad, reservar y persistir el pago:

```text
Cartera: documentos + abonos + medios -> cuentas resueltas por configuración
Editor: cuentas + terceros + partidas -> aplicaciones y medios explícitos
                  ↓
Validación y aceptación financiera de Payables/Receivables existentes
                  ↓
Pagos + tenders + aplicaciones + fuente/job canónicos, en una transacción
                  ↓
SqlAccountingPostingProcessor: auxiliar, caja y asiento, una sola vez
```

Contrato común de cada grupo: dirección pagar/cobrar, tercero y rol, sede de la
empresa y sede del tercero cuando aplique, moneda/fecha, `PaymentId`, fuente
padre/tipo/grupo, posiciones e importes, cuentas históricas de las posiciones,
medios y cuentas de tesorería resueltos, evidencia del medio, sesión cuando
corresponda y clave idempotente. Dirección, medio, banco y efecto son datos
normalizados por el servidor desde la semántica configurada de las partidas;
no son otra captura que el contador deba repetir. La entrada manual propone
cuentas, lados, terceros, importes y referencias a pendientes; el servidor
comprueba su correspondencia exacta. Las filas generales del asiento
no pueden repetir el efecto ya representado por un grupo de pago.

**Aclaración del usuario: las cuentas determinan la operación.** La normalización
del contrato financiero interpreta la combinación de partidas y sus funciones
configuradas, con las cuentas resueltas al aceptar. No añade un selector redundante
«esto es un pago» ni exige volver al wizard después de enviar el comprobante:

| Partidas propuestas | Efecto reconocido y propietario reutilizado |
| --- | --- |
| Débito CxP + crédito banco/caja, con pendiente seleccionado | Pago/abono al proveedor, sobre esa posición, mediante el mismo aplicador de CxP |
| Débito anticipo de proveedor + crédito banco/caja | Pago anticipado; abre crédito disponible del proveedor sin inventar factura |
| Débito banco/caja + crédito CxC, con pendiente seleccionado | Recaudo/abono del cliente, mediante el mismo aplicador de CxC |
| Débito CxP + crédito anticipo de proveedor, con posiciones seleccionadas | Aplicación del anticipo contra la deuda, sin una nueva salida ni pago |

La cuenta de tesorería resuelve banco/medio/sede; la contrapartida distingue deuda,
anticipo o recaudo. Una cuenta bancaria por sí sola no identifica al proveedor,
el importe ni la factura, y no convierte cualquier movimiento entre cuentas en
un pago de cartera. Estos datos ya vienen del tercero, importes y documento
seleccionados en la grilla. La cuenta configurada como anticipo identifica su
función sin depender de su nombre o de que la captura proceda de otra pantalla.

Seleccionar un pendiente incorpora su ID y cuenta de origen a la partida. Cuando
una reducción de CxP/CxC necesita un documento y falta ese vínculo, el editor
solicita seleccionar el pendiente; no lo adivina por importe ni aplica al más
antiguo automáticamente. Reversos conservan la referencia al efecto original.
Con varios grupos, cada partida de tesorería debe tener correspondencia unívoca
con su tercero y aplicaciones; si los datos no permiten establecerla, se señala
la asociación faltante. El sistema no reparte dinero entre terceros a ciegas.

La normalización se integra en la aceptación financiera existente y entrega los
mismos contratos de liquidación a sus propietarios. No es un motor nuevo que
reconstruya pagos después de contabilizar. Elegir una cuenta nunca escribe saldos;
solo al aceptar se reservan, y el processor aplica todos los efectos juntos.
La regresión de equivalencia compara operación, aplicaciones, cuentas, terceros,
medios, importe y efecto de sesión para las dos capturas; solo pueden diferir
identidades, numeración y metadatos propios del documento de origen.

Extender las rutinas actuales de validación y persistencia de los stores para
participar en la conexión/transacción de aceptación del comprobante. Las entradas
asistidas invocan esas mismas rutinas desde sus servicios. No copiar sus reglas
en `AccountingService`, ni llamar a `ConfirmPaymentAsync` por grupo abriendo
transacciones independientes. El comprobante completo congela su borrador,
acepta todos los grupos y crea **una fuente/job padre**; las rutinas participantes
no crean jobs, asientos o numeración hijos. Los pagos asistidos independientes
conservan su fuente `PayablePayment` / `ReceivablePayment` y su numeración actual.

**Reservas únicas:** un pago con dinero, venga de cualquiera de las pantallas,
persiste sus aplicaciones en `SupplierPaymentApplications` o
`CustomerPaymentApplications`. Las líneas del borrador no se suman además como
otra reserva de ese pago. Los índices de `VoucherDraftLines` se utilizan para
efectos sin pago (ajustes/cruces de anticipo) que no tienen aplicación en aquellas
tablas. El propietario de disponibilidad une esas clases excluyentes de efectos
y se reutiliza en ambas aceptaciones. Esta precisión sustituye la propuesta
anterior de reservar el mismo tipo de pago en una tabla distinta según pantalla.

El processor reutiliza sus aplicadores de pago por lotes para ambas fuentes. Un
asiento manual con pagos no dispara además un asiento automático de pago. Las
partidas elegidas se validan contra el efecto financiero normalizado y se escriben
en el asiento único del padre. Conservar `SourceDocumentId/Type + GroupId` en el
pago y vínculos a sus partidas: historial, reintento, cierre y reverso no pueden
suponer que el ID del pago siempre es el ID de la fuente. Adaptar también las
consultas actuales de replay que fijan `PayablePayment` o `ReceivablePayment`.

Las cuentas de cartera de ambos caminos salen de la posición original. Los
loaders nativos de pago y recaudo ya preparan partidas por obligación y cuenta
de apertura, sin volver a resolver el mapping CxP/CxC del día. Los aumentos y
disminuciones de cartera conservan también esa cuenta, incluido el tercero sin
rol de cliente. La integración pendiente de grupos manuales debe reutilizar
estas mismas partidas y su validación. El wizard
puede ocultar el detalle técnico y proponer cuentas de tesorería configuradas;
el editor las muestra y permite elegir auxiliares compatibles. Ninguno sustituye
la cuenta de una obligación histórica por el mapping vigente del día de pago.

La validación del vínculo histórico pertenece a
`accounting.ObligationOpeningAccount`, compartida por el listado paginado, los
pagos/recaudos y los ajustes del mismo processor. Comprueba tenant, sede, tercero,
lado de apertura, naturaleza, función declarada y disponibilidad de la cuenta;
una cuenta legacy sin función explícita conserva su naturaleza y vínculo real.
Un vínculo ausente o incompatible deja el trabajo en
`AccountingPendingConfiguration/OriginalPositionAccountMissing`, con el motivo
y «No se aplicó el pago/recaudo/ajuste». Se revierte hasta el savepoint anterior
a los efectos: no disminuye saldo, no registra transacción, caja ni asiento,
y la aceptación conserva su reserva. La conciliación explícita permite
reintentar ese mismo trabajo una vez. No se busca una cuenta de reemplazo.
Fuentes comerciales cuyo modo aceptado no exige asiento conservan su camino
comercial; un replay ya completado devuelve el resultado autoritativo.

La lectura de cabecera y las cuentas de todas las aplicaciones usa un comando
con dos resultados; el ajuste usa uno, sustituyendo los dos lectores anteriores.
No hay consulta por obligación ni nuevo writer, job, endpoint o saldo. La
regresión local reprodujo el uso incorrecto del mapping tanto en pagos como en
ajustes, y luego verificó dos obligaciones de auxiliares distintos, ambos lados
de ajuste, ausencia de efectos ante vínculo faltante o invertido, reserva,
diagnóstico común con la consulta, corrección explícita y replay sin duplicación.
Pasaron 85/85 pruebas SQL/API seleccionadas de pagos, recaudos, gastos,
borradores, cierres, apertura histórica, auxiliares y disponibilidad con 21.000
fuentes. Esta evidencia no certifica los grupos manuales ni el corte completo,
que permanecen pendientes.

En CxC, abonar significa **recaudo**: débito a caja/banco y crédito a la CxC
original. La selección de pendientes del editor reutiliza `Receivables` cuando
se captura ese efecto. Se presenta dentro de Comprobante contable con operación
de recaudo; no se llama Egreso ni se añade un quinto tipo visible. La operación
técnica de recaudo y sus permisos/historial siguen siendo los existentes. En CxP,
el pago se presenta como Egreso. Un cruce sin dinero conserva su identidad de
aplicación/ajuste y no crea un pago ficticio para aparecer en el historial.

Las diferencias de captura no crean excepciones a las reglas de dominio:

- Los pagos actuales son COP, de un tercero, y en CxP agrupan obligaciones de
  una sola sede del proveedor. Esa sede es distinta de la sede de la empresa.
- `PaymentTenderBreakdown` admite varios medios para una obligación, pero exige
  un solo medio cuando el grupo aplica a varias facturas; también evita repetir
  un medio. Se conserva esa regla por grupo en ambas superficies. El editor no
  la elude generando grupos ocultos; una ampliación se haría en ese mismo contrato.
- Estas reglas de abono se refieren a grupos aplicados a obligaciones. El anticipo
  sin factura acordado en 13.5 exige ampliar el contrato canónico con destino
  anticipo y validar su importe contra los medios; no pasa una factura ficticia
  para satisfacer el mínimo actual de una aplicación. Un cruce de anticipo sin
  dinero utiliza el efecto de crédito correspondiente y no crea tenders.
- La evidencia de transferencia/tarjeta también se exige al capturar mediante
  cuentas. Inferir el medio desde el PUC no inventa referencia o aprobación.
  En el contrato nativo de medios, `BankAccountId` selecciona el destino de una
  transferencia. En tarjetas el banco permanece como propiedad del auxiliar PUC
  elegido y congelado; no se envía como destino de transferencia. La traducción
  manual conserva el mismo validador común de evidencia y medios.
- Pagos vinculados a una caja utilizan su sesión autorizada y generan un solo
  movimiento por medio. Un movimiento administrativo sin sesión admitido por el
  contrato no se imputa a una caja arbitraria. POS conserva sus controles de
  usuario/dispositivo, conexión y proyección local; no se habilita pago offline.
- Un pago asistido se consulta en Trazabilidad como el mismo comprobante. Uno
  manual aparece en el historial de abonos de cartera como el mismo pago/grupo.
  El origen de captura es información, no otro saldo o movimiento a sumar.

La aceptación devuelve IDs, aplicaciones aceptadas, disponibilidad resultante y
estado para actualizar el recurso visible. No anuncia saldo aplicado hasta que
lo aplique el processor. La implementación debe retirar en este recorrido las
invalidaciones múltiples de las páginas CxP/CxC y el `load()` de Gastos tras pagar,
reutilizando respuesta y evento del propietario. Una vista oculta no vuelve a
consultar para reconstruir el mismo pago. Preservar impresión posterior al commit;
un fallo de impresora no repite aceptación ni asiento.

Para pagos del editor, las líneas de tesorería y aplicaciones se agrupan por
tercero/sede del tercero/moneda cuando corresponda. Su suma debe coincidir con pagos de deuda, anticipos y conceptos
directos identificados; se rechaza dinero sin destino. Se amplían los contratos
de los propietarios existentes para representar varios grupos dentro de **una
fuente y una transacción**. No llamar un endpoint de pago por partida ni crear
jobs hijos que puedan completar medio comprobante. Las restricciones y permisos
del wizard POS no se amplían por el permiso de comprobante manual.

Los grupos reutilizan el número de su comprobante padre y tienen identidad de
grupo, sin solicitar un consecutivo de egreso por tercero. Al adaptar las tablas
de pagos, migrar su actual unicidad sede/número para distinguir estos grupos y
conservar la unicidad de recibos nativos. La clave fuente + grupo es única; no
suponer que `PaymentId` y `SourceDocumentId` siempre son iguales. Los contratos
directos de comprobante convergen en la misma captura congelada para que ninguno
pueda omitir las reservas por partida.

Un borrador se edita. Un aceptado se conserva. Para corregir una compra financiera
ya pagada se reversan primero las aplicaciones afectadas mediante el propietario
financiero, y después se registra su corrección con referencia al original. Se
puede deshacer solo la aplicación manteniendo el dinero como anticipo si el pago
fue real; revertir el egreso exige que la salida de dinero también se haya
revertido. Nunca borrar un pago real para desbloquear edición ni modificar
cantidades recibidas para corregir una factura. Períodos cerrados se corrigen en
período permitido, conservando fechas y vínculos históricos.

La devolución real del abono usa la misma captura de desaplicación: la partida
de obligación identifica su transacción original y la contrapartida elegida
determina el destino. Un auxiliar de anticipo conserva el dinero; un auxiliar
de tesorería registra su devolución efectiva. No existe otro selector de operación.
La devolución requiere Comprobante contable, tercero/sede coincidentes, importe
balanceado y evidencia del medio por `PaymentTenderBreakdown`. Reutiliza los
permisos de pago/recaudo; cuando se asocia una sesión exige también su permiso
de movimiento. No abre un anticipo ni registra otro pago ficticio.

La normalización identifica el efecto como `PaymentReturn`, sin IDs de anticipo.
`ManualVoucherCreditTargets` lo proyecta como la misma `Unapplication` del abono
original. Comparte su presupuesto y reservas con las desaplicaciones que conservan
dinero. El processor valida la pareja, reabre la deuda con el mismo aplicador,
registra el movimiento por el writer de sesión existente y genera el asiento en
esa transacción. El importe bruto del pago y la recepción física son inmutables;
`PortfolioAppliedAmounts` descuenta lo devuelto del importe aplicado.

Verificación SQL/API local del 25 de octubre: 22/22 en
`auraly-payment-return-shared-regression.trx`, con pago nativo y manual de CxP/CxC,
devolución parcial/completa, fallo entre efecto financiero y asiento, rollback,
reserva pendiente, exceso rechazado, retry, replay, anticipos/cruces anteriores,
gasto y compra de contado. La medición aislada de 20 muestras conserva 7 comandos
de aceptación y 11 de procesamiento para 1/100 obligaciones: p95 76,1/202,8 ms
y 54,9/197,3 ms respectivamente, bajo el gate de dos segundos. Las escrituras
de desaplicación siguen en un comando por dirección; no hay I/O por partida.

Compatibilidad: las dos identidades de anticipo admiten null únicamente para
este efecto nuevo; las capturas anteriores conservan sus valores y JSON.
No cambia esquema, tipos del historial, fuentes, trabajos ni saldos. API y
workers deben comprender el nuevo efecto antes del cutover; un worker anterior
lo rechaza sin aplicarlo. Tras aceptar devoluciones, el rollback conserva un
processor compatible o usa roll-forward; no se convierte una devolución real
en anticipo. Esta evidencia no cierra las revisiones posteriores de costo,
descuentos, conciliación histórica ni auditoría global del diseño.

La verificación posterior aprobó las 949 pruebas de Foundation, la compilación
de API Debug y Next/TypeScript, y los dos recorridos de captura de escritorio y
móvil. Los recorridos reales de CxC y gasto/CxP también registraron una devolución
de 20,0001 desde la cuenta de caja elegida, después de desaplicar a anticipo,
aplicar y restituir un cruce. Conservaron el pago bruto y el crédito anterior,
incrementaron únicamente el saldo reabierto y generaron el asiento balanceado.
La anulación posterior del gasto conservó su anticipo y reconoció únicamente
el importe que aún permanecía pagado. No se simuló una aceptación DIAN.

La auditoría de esta extensión revisó contrato nullable, normalización, permisos,
cuentas y sitios originales, índice/reserva, aplicador, movimiento de sesión,
historial y editor compartidos. No añadió otro pago, saldo, writer, trabajo,
numeración ni página. La etiqueta del historial dice «Abono desaplicado» porque
el tipo técnico compartido ya no implica conservar el dinero como anticipo.
El destino se conserva en las cuentas del comprobante. La regresión real detectó
además el problema de memoria de lectura de conciliación documentado abajo.

La repetición final real pasó 9/9 (`auraly-payment-return-complete-real-regression`):
tesorería en efectivo/banco y conciliación, asiento libre con terceros/bases,
PDF/Excel/clonación/auxiliar, recaudo y devolución, gasto/pago/desaplicación/
cruce/devolución/anulación, dos recepciones con cargo de gasto o activo por
distribuir, pedidos editados/recuperados/facturados por lote y POS con impresión.
La primera página de conciliación pasó de esperas de 14–30 s a 621 ms en esa
repetición; la página expandida tomó 490 ms. La auditoría de la consulta y sus
mediciones aisladas se encuentran en la decisión propietaria de sesiones.
Estos recorridos usan API y SQL locales aislados, sin aceptar documentos reales
ante DIAN ni desplegar código remoto. No certifican los slices aún pendientes.

La anulación total de soporte valida aplicaciones, créditos y asociaciones de
costo dependientes; no libera automáticamente dinero o presupuesto repartido.
Las dependencias se deshacen explícitamente en sus propietarios antes de aceptar
la anulación. Un rechazo fiscal no elimina efectos financieros a escondidas:
fiscal y contabilidad muestran estados separados y conservan sus reintentos.

#### Aclaración de compras: captura asistida, contado y egreso

Por instrucción posterior del usuario se conservan dos acciones en recepción:
**Agregar factura**, que prepara su comprobante automáticamente con las cuentas
configuradas, y **Agregar comprobante**, que enlaza uno existente. Ambas admiten
solo asociar o prorratear; comparten el presupuesto del mismo origen. No se retira
la captura actual para exigir que todos los usuarios elaboren comprobantes a mano.

Una compra con neto positivo reconoce adquisición y CxP, tanto si se pactó contado
como crédito. La condición controla vencimiento; el estado pagado procede de
aplicaciones reales. El pago se captura como egreso en el propietario financiero,
incluso cuando ocurre el mismo día. Caja/banco se eligen allí. La cuenta automática
de la adquisición es la CxP configurada, no una salida de dinero supuesta. Aplicar
un anticipo contra la compra utiliza comprobante contable sin un segundo egreso.

El diseño corrige la causa comprobada de `SettlementSourceMissing`: hoy
`CreatesPayable=false` elimina la obligación sin aportar evidencia del pago.
Nuevas compras dejan de usar ese indicador para omitir el trabajo financiero;
fuentes históricas conservan su contrato. **Agregar factura** genera una fuente
por factura nueva; **Agregar comprobante** no genera otra fuente. El formulario
operativo y el editor contable comparten las cuentas resueltas y el mismo motor.

El contrato de confirmación, cuentas automáticas, transición de APIs, diagnóstico
de históricos y regresiones pertenecen a
[recepciones de compra](goods-receipts-design.md#compra-obligación-y-egreso-contado-deja-de-significar-pagado).
El diseño no afirma que el problema productivo ya esté corregido ni autoriza
transformar todas las compras históricas de contado en nuevas deudas.

### 13.6 Cambios de persistencia y compatibilidad

**Slice local 2026-10-21, parcial:** los comprobantes manuales nuevos del editor
usan `PayloadVersion=2` y un `LineId` estable por partida. Guardar o reordenar
conserva esa identidad; duplicar una partida o clonar el comprobante genera
identidades nuevas. El store conserva la versión y rechaza quitarla a un
borrador identificado. `AccountingJournal.ValidateSourceLineIdentities` es el
único validador de versión/identidades, consumido por aplicación y processor.
La fuente aceptada congela esos IDs y el mismo posting writer los guarda como
`AccountingEntryLines.SourceLineId`. El número de viajes no depende del número
de partidas: se extiende el JSON por lote existente.

Contratos históricos omiten versión e IDs nulos al serializarse, conservando sus
hashes. No se asignan IDs a fuentes históricas por el orden actual de una grilla.
Las columnas nuevas son compatibles y la base exige identidad no vacía y única
dentro de cada borrador/asiento cuando está presente. El rollback de escritor o
worker requiere un binario que preserve esta versión; no habilitar el editor
nuevo con workers antiguos que descarten la identidad al contabilizar.

El reenvío de un borrador enviado pasa por la aceptación canónica para validar
fuente/job y devuelve el estado existente, sin validar dimensiones mutables ni
publicar otra señal. Si el estado no cambió reutiliza la lectura ya realizada.
Una marca `SentAt` cuya fuente/job se perdió produce conflicto; no reconstruye
una aceptación ni otro asiento. Este slice aún no habilita los efectos tipados,
el selector de cargos ni la migración de vínculos de cartera/bancos de abajo.
La comprobación corrige el atajo anterior de un solo read: un replay sin cambio
de estado ejecuta tres comandos acotados al documento (lectura del borrador,
bloqueo de aceptación y aceptación canónica), sin SQL por partida. Las dos
lecturas adicionales se justifican por los casos reproducibles de fuente/job
perdidos que el atajo aceptaba silenciosamente; no se repiten validaciones de
dimensiones ni se reconstruye la vista con otra lectura si el estado coincide.
Crear un borrador también comprueba, bajo el bloqueo de su identidad y dentro
del mismo comando de lectura, que esa clave de fuente no esté ya aceptada sin
borrador. Un comprobante directo histórico no se convierte en otra captura
editable. Una fuente aceptada con borrador existente sin marca de envío se
reporta como inconsistencia, sin reconstruir marcas ni reactivar escrituras.

**Vínculo de apertura, slice local parcial:** `Payables` y `Receivables` agregan
`OpeningEntryId` y `OpeningEntryLineNumber`, con nulabilidad conjunta y FK a la
partida inmutable. La cuenta se obtiene de esa partida; no se agrega otra cuenta
editable ni otro total. Al contabilizar una fuente que abre su propia posición,
el mismo comando por lote de líneas enlaza su CxP/CxC, valida tercero/tenant,
lado, unicidad e importe funcional. Un fallo revierte apertura, transacciones y
asiento juntos. El marcador técnico en memoria distingue la partida de apertura
de retenciones u otras partidas que podrían usar el mismo auxiliar; no es un
catálogo ni se infiere el vínculo por coincidencia de cuenta/importe.

No agrega viajes SQL por documento ni por partida: conserva los comandos de
inserción existentes y usa los índices únicos de origen para hasta una posición
de cada clase en el contrato nativo actual. Modo comercial sin asiento e históricos
no conciliados mantienen ambos campos nulos. No se adivina su cuenta con mappings
vigentes ni se cambia su payload. El despliegue es aditivo; el corte de lectores
que exijan el vínculo requiere primero la conciliación explícita de apertura.
El rollback del binario anterior conserva las FK ya creadas; no habilita después
lectores estrictos para nuevas posiciones que ese binario dejaría sin vínculo.

La corrección de cierre que abre cartera de una venta ya conserva el documento
de venta para navegación y enlaza su apertura a la partida de la conciliación.
El processor identifica la venta en memoria y proyecta el neto de sus medios
corregidos; el vínculo no se decide solo por tercero o importe. La regresión local
incluye dos ventas de un mismo cliente y los modos con/sin asiento, con un único
comando de partidas y sin escritura de partidas en replay. Su evidencia y límites
se detallan en [contabilidad operativa](accounting-operational-design.md#opening-links-for-balances-created-by-closure-corrections-2026-10-22).

La captura existente de saldos iniciales incorpora ahora la referencia explícita
a una CxP/CxC histórica y su identidad estable de partida. Guardar, aprobar y
contabilizar reutilizan `accounting.OpeningPositionsValidate`; el processor
vincula la posición existente al asiento sin otra deuda ni transacción de
cartera. El contrato versionado, corte, validaciones y presupuesto se describen
en [contabilidad operativa](accounting-operational-design.md#explicit-reconciliation-of-commercial-only-positions).
La verificación local del slice pasó con 74 casos SQL/API, 847 pruebas Foundation
y cuatro escenarios de navegador, incluido el recorrido real de guardar,
reabrir, aprobar y contabilizar la apertura sin duplicar la obligación ni sus
abonos. La evidencia y el defecto de respuesta vacía corregido están en el
documento operativo enlazado. Esto no habilita ni certifica el corte completo.

Este paso aún no sustituye la unicidad nativa por fuente. La lectura de la cuenta
original en pagos y ajustes ya se completó en el slice descrito en 13.5. La
unicidad por partida y los grupos deben completarse con el contrato de efectos tipados;
no se declara terminado el corte de cartera ni la consulta de comprobantes
aplicables.

La matriz conserva el alcance de destino. Solo los slices expresamente
identificados como implementados están disponibles; los demás campos describen
el contrato pendiente del DACPAC y runtime:

| Familia existente | Cambio necesario |
| --- | --- |
| `AccountingAccounts`, maestros y configuración | Jerarquía/función/propiedades libres de 13.3; sin tabla de totales de cuenta |
| `VoucherDrafts` / `VoucherDraftLines` | Principal, tipo visible y efecto por línea (`EffectKind`, `TargetSubledgerKind`, `TargetSubledgerId`, importe e identidad); índice por destino para reservas, conservar borrador mutable y control de versión |
| `AccountingSourceDocuments` | Nueva versión de payload con identidades de líneas, cuentas/dimensiones resueltas, efectos y vínculos; inmutabilidad y hash existentes |
| `AccountingEntries` / `AccountingEntryLines` | Principal en encabezado; identidad de línea fuente y banco/medio resueltos en partidas cuando aplique; mantener cuenta/tercero/centro e importes históricos |
| `Payables` / `Receivables` | Identidad de partida fuente y vínculo de apertura que resuelve `AccountingAccountId` desde `AccountingEntryLines`, sin copiarlo como campo editable; sustituir unicidad por documento por unicidad documento + partida |
| `PayableTransactions` / `ReceivableTransactions` | Identidad de efecto/partida fuente y vínculo de reversión; permitir varios efectos del mismo comprobante sin colisionar en la unicidad actual |
| `SupplierCredits` / `CustomerCredits` | Tipo anticipo/crédito, cuenta, moneda/origen y partida; ampliar origen de CustomerCredits hoy limitado a devolución. Mantener las identidades de terceros canónicas y validar sus roles |
| Pagos, tenders y aplicaciones | Vínculo fuente + grupo/partida, auxiliar de tesorería congelado, reversión identificable; los grupos no reservan otra numeración financiera ni producen asientos adicionales |
| Purchasing y Pricing | Cambios definidos por sus documentos propietarios; nunca copiar de nuevo la obligación al asociarla |

Los importes originales provienen del snapshot aceptado. Saldos disponibles y
estados son proyecciones que solo modifica el motor a partir de efectos con
identidad única. Deben poder reconstruirse desde apertura y fuentes aplicadas,
incluidas las aplicaciones de créditos. No se crea una tabla universal de
«movimientos financieros» que replique el asiento y las transacciones existentes.

La referencia de efecto se conserva en el payload inmutable y en su transacción
auxiliar; la segunda representa el efecto aplicado, no otra captura editable.
Los tenders y aplicaciones de pagos son su detalle operacional, no otra fuente
que vuelva a contabilizarse. Las identidades nuevas son estables y se validan en
conjunto antes de aceptar; no dependen del orden actual de la grilla.

FK, índices y unicidad incluyen tenant o pertenencia demostrable a su Business.
Ninguna FK global sustituye la comprobación de tenant. Índices de lectura cubren
cuenta/tercero/estado/fecha y origen por partida; posiciones legadas reciben una
identidad estable de línea solo cuando el origen es inequívoco. Una apertura sin
asiento previo se enlaza a la línea de apertura conciliada, sin inventar un asiento
histórico. No inferir cuenta por mapping actual en un pago futuro.

El editor ampliado conserva inicialmente el alcance monetario actual de pagos
(COP). Compras importadas mantiene moneda y tasa congeladas; asociarlas usa su
importe funcional. No se habilita liquidación multimoneda por permitir una cuenta
con nombre de banco extranjero. Rechazar claramente un cruce no soportado.

#### Preparación transaccional de grupos — implementación local del 23 de octubre

`accounting.PortfolioPaymentAccept` conserva su entrada nativa de un pago y
admite el lote de grupos de una misma fuente padre. Hace las validaciones y
reservas por conjuntos, agregando todos los abonos del lote a una misma posición
antes de compararlos con `ObligationAvailability`. No abre transacciones ni
crea fuentes, trabajos, asientos o consecutivos. Los stores nativos siguen
llamando al mismo procedimiento y conservan sus contratos/hash.

`SupplierPayments` y `CustomerPayments` incorporan padre/tipo/grupo. Para recibos
nativos esos campos son nulos y su numeración sigue siendo obligatoria y única
por sede. Para un grupo, la FK exige la fuente padre, la identidad de grupo es
única y los campos de numeración propia deben ser nulos. Los tenders pueden
conservar la cuenta y partida fuente resueltas. La CxC de un tercero sin rol de
cliente requiere padre manual; no convierte una solicitud nativa de un cliente
en permiso para cobrar obligaciones de otro tercero.

Se reserva `PayloadVersion=4` para el contrato financiero manual; versión 2
conserva los comprobantes identificados anteriores y versión 3 sigue identificando
la apertura conciliada. La captura conserva referencias de posiciones, grupos,
vencimiento, sede del tercero y evidencia por partida; guardar no reserva saldo.
Clonar limpia los vínculos consumibles, grupos, vencimiento y evidencia de pago.
El envío versionado normaliza cuentas/partidas en servidor y congela sus efectos
en la fuente. Las entradas directas antiguas no pueden omitir el borrador de este
contrato. Un replay verifica y reutiliza la fuente aceptada, sin resolver de nuevo
la configuración ni generar identidades. La versión 2 conserva su protección de
cuentas de control. La versión 4 solo acepta efectos que tenga implementados;
anticipos, cruces sin dinero y soporte fiscal continúan protegidos
hasta completar sus participantes y aceptación. La UI del editor todavía no
activa por defecto esta versión nueva.

`AccountingVoucherFinancialValidation` utiliza `PortfolioSettlement` y
`PaymentTenderBreakdown` de BuildingBlocks Domain: no replica sus reglas ni
aplica efectos. Seleccionar una obligación exige su cuenta y tercero originales,
COP, fecha compatible y permiso concreto de pago/recaudo. La configuración
explícita del auxiliar determina tesorería y medio; un código o nombre nunca
reemplaza esa clasificación. Una cuenta legacy sin clasificación no se convierte
implícitamente en General. Una obligación histórica con cuenta de apertura
probada conserva esa referencia, y los mappings de control vigentes no pueden
contradecir la función declarada del auxiliar.

Las entradas nativas `PayableSettlement` y `ReceivableSettlement` adaptan sus
tipos existentes a esa misma política de aplicaciones: una a cien obligaciones,
identidades distintas e importes positivos, orden conservado y normalización a
cuatro decimales. Se mantienen sus contratos anteriores para un importe que
redondea a cero. Accounting no referencia los dominios internos de CxP/CxC;
compartir una regla no permite saltarse el límite entre módulos. La validación
de arquitectura completa detectó esas referencias directas y motivó retirarlas,
en lugar de exceptuar la regla o copiar el cálculo en el comprobante.

`SqlAccountingPostingProcessor.ApplyPortfolioPaymentsAsync` aplica tanto el pago
nativo como los grupos manuales, con un comando por dirección: agrupa importes por
posición antes de actualizar una sola vez el saldo, conserva cada aplicación y
transacción, completa pagos y registra movimientos de sesión en la transacción
del asiento padre. No existe un aplicador alterno por grupo. Un fallo revierte
todo el lote; la reserva aceptada se conserva y el replay no vuelve a procesar.
La consulta de la cuenta original utiliza `ObligationOpeningAccount` también al
procesar el padre y revierte al savepoint si ese vínculo deja de ser válido.

La versión 4 también reconoce nuevas CxP mediante crédito al auxiliar `Payable`
en un comprobante contable. Requiere tercero con rol de proveedor activo, sede
del tercero, vencimiento y referencia documental de hasta 80 caracteres. Si no
se indicó sede, se congela la primera activa ordenada por principal e identidad,
como en el registro asistido; no se vuelve a escoger al reintentar. Una partida
sin obligación seleccionada no se interpreta como abono ni crea un egreso.
Las identidades de obligación y transacción se congelan al aceptar; guardar no
abre deuda. La captura incompleta puede guardarse, pero no enviarse.

Compras, gastos, facturas de cargos y estas partidas convergen en
`OpenPayablesAsync`, dentro del mismo processor y transacción. La entrada nativa
`OpenPayableAsync` adapta una sola apertura al lote; no conserva otro INSERT.
Un lote de hasta 500 aperturas hace un comando, con validación de sede y rol por
conjunto y apertura/transacción atómicas. Las condiciones y fechas nativas no
cambian. `Payables.SourceLineId` es nullable para fuentes anteriores; la unicidad
por fuente/tipo/partida conserva un único espacio NULL para el origen nativo.
No hay backfill por inferencia. El vínculo al asiento se resuelve en el comando
de líneas ya existente, usando la identidad fuente para distinguir obligaciones
del mismo tercero, incluso en el mismo auxiliar. La cuenta sigue viviendo en
la línea inmutable y no se copia al saldo de cartera.

El vencimiento conserva la fecha real de la obligación: puede preceder a la
fecha de registro del asiento cuando ya está vencida. No se condiciona a un
plazo nuevo calculado desde la contabilización. Ausente o valor vacío/default
se rechaza antes de aceptar. La fecha contable sigue validando período y corte.

Dos casos SQL/API probaron auxiliares iguales/distintos, rechazo de vencimiento
ausente antes de aceptar, fallo inyectado en el vínculo del asiento que revierte
ambas deudas/transacciones, replay sin efectos y reintento explícito una sola
vez. El registro usa un comando para las dos aperturas. Un pago asistido posterior
las abona por el aplicador común y mantiene sus auxiliares originales. El rollback
de esquema no debe eliminar la columna ni restaurar la unicidad anterior una vez
existan fuentes con varias partidas: conservar datos y usar roll-forward; detener
escritores viejos antes de habilitar esa captura. La regresión ampliada pasó
135 casos de CxP/CxC, comprobantes, aceptación, compras, devolución de compra,
gastos, POS y sesiones. Una medición aislada de 20 muestras por tamaño verificó
50/500 partidas con 49/499 obligaciones: aceptación, siete SQL y p95 548,4/812,5
ms; contabilización, nueve SQL y p95 348,2/563,7 ms. Primer intento:
522,9/1461,2 ms al aceptar y 464,9/546,8 ms al contabilizar. Hubo siempre un
comando de aperturas y uno de líneas de asiento, con todos los vínculos probados;
el conjunto terminó con 10.960 obligaciones nuevas. Se conservan presupuestos
p95 de 2/3 s. No corrieron builds ni diagnóstico SQL durante la medición.

Las nuevas CxC se preparan por débito al auxiliar `Receivable` con vencimiento
y referencia de hasta 64 caracteres. El cliente activo conserva rol y sede;
un tercero sin ese rol conserva `PartyId`, sin inventar cliente ni sede comercial.
Un cliente inactivo no se transforma en tercero genérico. La normalización
congela ese destino y sus identidades; la cuenta se obtiene de su partida de
apertura igual que en CxP. La unicidad por fuente/tipo/partida y el espacio NULL
histórico se aplican también a `Receivables`.

`OpenReceivablesSql` es el único INSERT de nuevas CxC y sus transacciones
Opening. Las preparaciones de venta, factura de servicio, nota débito,
importación, faltante de empleado, corrección de cierre y captura manual lo
reutilizan dentro de sus comandos y transacciones existentes. En correcciones
de venta se conserva el origen de navegación de la venta y el origen contable
de la conciliación; no se cambia esa atribución al compartir la escritura.
No se agrega un límite nuevo a los cierres anteriores: el máximo de 500
partidas pertenece al contrato de captura manual.

La regresión de aperturas se amplió a 215 casos, incluidos cierres, facturas de
servicio, notas débito, devoluciones de venta y facturación individual/masiva de
pedidos. Las dos capturas de CxC probaron cliente/tercero sin rol, dos partidas
en el mismo auxiliar, límites de referencia, rechazo de cliente inactivo,
rollback de apertura/asiento, identidades estables y reintento único. Después
se recaudaron por la entrada asistida o manual según su tercero; el historial
del tercero sin rol conservó su padre y su importe real.

La revisión posterior encontró una escritura de avisos de cartera en la
corrección de cierres y la retiró: esos clientes se agrupan en memoria desde
los cambios ya aceptados y se pasan a `SqlAccountingPosSynchronizationOutbox`,
igual que los otros efectos financieros. Se mantiene la transacción del
processor. Añade un comando acotado por conciliación con clientes afectados,
sin lecturas por elemento ni una segunda consulta para reconstruir esa lista.
La prueba de cierre amplía el gate a una escritura de aperturas, una de avisos,
rollback del aviso si falla el vínculo del asiento y ningún aviso en replay.
Los checks de arquitectura exigen un único INSERT de aperturas por submayor
y un único writer de outbox financiero. Sus checks y regresiones deben repetirse
tras esta consolidación y la corrección de dependencias antes de cerrar el slice.
La suite completa de Foundation pasó después de retirar las dependencias
directas: 857 pruebas, incluidos los dos controles nuevos de writer único y
las regresiones de límite, precisión, orden y contratos de redondeo de cartera.
Después se repitieron 111 casos SQL/API con el propietario común de avisos y
los vencimientos reales anteriores al registro: todos pasaron. El cierre probó
rollback de su aviso si falla el vínculo, un solo comando de aperturas/avisos
para dos ventas del mismo cliente y ningún aviso al repetir el procesamiento.

La medición actual de aperturas pasó con 20 muestras por tamaño y submayor,
sin build, navegador ni diagnóstico SQL concurrente: siete SQL de aceptación
y nueve de contabilización tanto con 50 como con 500 partidas. CxP obtuvo p95
132,7/546,3 ms al aceptar y 67,1/408,6 ms al contabilizar; CxC sin rol de cliente,
131,4/350,3 ms y 58,1/358,4 ms respectivamente. Primeros intentos CxP:
472,0/341,1 ms y 299,9/382,9 ms; CxC: 278,7/350,3 ms y 138,9/277,0 ms.
Cada captura abrió 49/499 posiciones con sus vínculos exactos, un comando para
las aperturas y uno para las líneas. El conjunto terminó con 10.960 posiciones
nuevas de cada submayor. No hubo consultas por posición.

La revisión del editor reprodujo en navegador el downgrade involuntario de
versión 4 a 2 al reabrir una captura. Ahora conserva la versión y todos los
campos financieros recibidos; solo las capturas legacy mantienen la preparación
anterior de identidades/versionado. No se activa por defecto la captura nueva
antes de completar sus efectos y controles. Guardar reutiliza su respuesta y
la consulta del catálogo de la bandeja permanece deshabilitada mientras se edita.
Tres recorridos de navegador con API simulada pasaron sobre el build Next:
guardar/enviar/clonar legacy, reabrir/guardar financiero sin perder vínculos ni
evidencia (un GET/un PUT y ningún catálogo oculto), y pérdida de primera respuesta
con reenvío del mismo ID/rowversion, sin otro GET ni anuncio falso de envío.
El último reemplaza un escenario que aún buscaba una pantalla retirada.
TypeScript, ESLint enfocado y codificación pasaron. Esta evidencia de UI no
sustituye los recorridos reales de API ni certifica los efectos aún pendientes.

La vista de lectura `accounting.PortfolioPaymentOrigins` resuelve la identidad y
número del padre para historial, reportes y cierres. No guarda importes ni otro
saldo. Los grupos no se agregan como fuentes adicionales en Trazabilidad; sus
terceros ya están en las partidas del padre. Historial expone padre/tipo/grupo y
el botón de consulta usa ese padre sin otra búsqueda intermedia. El intento de
enviar un grupo por el endpoint nativo se rechaza como identidad de comprobante,
sin crear un recibo independiente.

Presupuesto del recorrido completo: hasta 500 partidas, máximo 250 grupos cuando
cada uno necesita al menos dos; se conserva el máximo de 100 obligaciones y diez
medios por grupo del contrato financiero. Hasta ocho comandos SQL de aceptación,
sin lectura por partida/grupo; p95 hasta 2 s para 50 partidas y 3 s para 500.
La actualización de sincronización reutiliza el outbox y dispatcher existentes,
con inserción por lote; una notificación de cliente no introduce otra consulta por
cada cliente del comprobante.

Los medios soportados por el protocolo de cartera se centralizan en
`PortfolioPaymentMethods`; servicios de CxP/CxC y normalización comparten ese
conjunto y `PaymentTenderBreakdown`. Se conservan códigos y hashes; no es un
catálogo nuevo ni contiene labels. Las opciones visibles siguen viniendo de
`reference.Options` y los códigos no soportados no se convierten en otro medio.
Los límites de evidencia también pertenecen al desglose común: referencia 120,
notas 500, franquicia 40 y aprobación 80. La captura puede guardarse incompleta,
pero enviar no amplía esos límites respecto al pago asistido.

La regresión de compatibilidad amplió la evidencia a 102 casos SQL/API de
proveedores, cartera de clientes, cuentas de origen, POS, sesiones, aceptación y
borradores. Incluye dos grupos contra una obligación: un padre, un asiento, sin
trabajos ni números hijos, historial del padre, autorización, replay y rechazo de
identidad por el endpoint nativo. Una falla después de actualizar saldos verificó
rollback de ambos grupos, reserva única compartida con el pago asistido, rechazo
de sobreabono y reintento explícito una sola vez. Dos escenarios de recaudo manual
verificaron clientes iguales/distintos, cuentas de origen, historial y movimientos
de las posiciones, una aplicación SQL por dirección y una inserción de outbox por
lote, con una notificación por cliente distinto y sin duplicar al repetir. Los
recaudos importados se pagaron después de su fecha de reconocimiento contable,
conservando la fecha histórica de la factura en su fuente de importación.
También se probó que una captura sin clasificación explícita no se convierte en
un asiento General por defecto. Las 122 pruebas de arquitectura y dominio
seleccionadas pasaron. Esta evidencia no certifica anticipos, efectos sin dinero,
las UI completas ni los demás efectos del diseño. Ocho recorridos de navegador
con API simulada verificaron consulta e impresión del recibo nativo y del padre
en CxP/CxC, incluido error/reintento de catálogo, sin buscar un comprobante hijo.
TypeScript, ESLint enfocado y compilación Next pasaron.

La medición aislada con 10.000 obligaciones y 20 muestras por tamaño comprobó
ocho comandos SQL de extremo a extremo, incluido el gate de publicación y la
respuesta autoritativa. En 50 partidas/25 grupos: primera 1.059,2 ms, p95 392,2 ms.
En 500 partidas/250 grupos: primera 649,9 ms, p95 672,1 ms. La primera medición
excedió el presupuesto (p95 4.192,6 ms); el perfil ubicó el costo en
`PortfolioPaymentAccept`, que extraía el JSON de efectos del mismo padre por cada
grupo. Validar y extraer ese padre una sola vez redujo su tiempo medio de
3.043,8 a 244,2 ms, conservando los controles de hash, fecha, scope y numeración.
El transporte de la prueba pospone el posting: las 5.500 aplicaciones permanecieron
aceptadas, sin cambiar saldo, crear movimientos aplicados o fabricar asientos.
Esta medición corresponde a aceptación, no al tiempo total del posting asíncrono.
El rollback de binarios después de habilitar grupos exige desactivar su captura
nueva y conservar un lector que entienda el padre; no se vuelve a un supuesto
`PaymentId == SourceDocumentId` para grupos ya aceptados.

### 13.7 Aceptación, aplicación y concurrencia

**Aperturas de saldos a favor, implementación local en verificación:** la
apertura nativa de `SupplierCredits` converge en `OpenSupplierCreditsSql` para
devolución de compra, devolución de cargos y anulación de gasto. Conserva sus
orígenes documentales anteriores y agrega una transacción Opening vinculada al
trabajo financiero padre real; un cargo devuelto conserva además su referencia
al cargo original. Ese detalle pertenece al submayor existente y al mismo
processor, no a otra tabla de anticipos con un saldo independiente ni a otro
motor. Se escribe en el mismo comando de cada preparación nativa, sin viajes
por crédito. La moneda procede del snapshot aceptado; no se supone COP para
una devolución de compra cuyo snapshot declare otra moneda.

`CustomerCredits` converge de igual forma en `OpenCustomerCreditsSql`, también
para el crédito originado al corregir un reembolso en el cierre. Conserva la
devolución original como referencia y enlaza Opening al trabajo real de la
conciliación. El abono nativo de una devolución a cartera conserva su contrato:
requiere deuda pendiente de la venta; no se transforma por ese nombre en un
anticipo ni se habilita una resolución económica nueva.

Naturaleza, moneda y vínculo al asiento quedan nullable en los créditos
históricos, sin backfill por inferencia. Las nuevas aperturas conocidas guardan
su naturaleza `Credit`, moneda y transacción; el vínculo se establece junto
con las líneas del asiento, por partida o documento de origen. No cambia el
importe del retorno ni el movimiento físico.

La captura financiera v4 normaliza un débito a `SupplierAdvance` o un crédito a
`CustomerAdvance`, con tesorería, como apertura `Advance` del submayor existente.
Cada grupo identifica como máximo un anticipo nuevo y puede incluir abonos de
la misma dirección y tercero. Un grupo mixto conserva la regla común de medios;
no distribuye importes entre terceros por inferencia. No exige una factura o
vencimiento ficticios. Proveedor requiere rol y sede; cliente admite rol/sede o
tercero sin rol por la misma identidad canónica de cartera.

`PortfolioPaymentAccept` participa en el commit padre y acepta cero aplicaciones
solo cuando hay un anticipo positivo que coincide con su grupo congelado. Los
pagos nativos mantienen al menos una aplicación. El total del grupo suma las
aplicaciones y el anticipo, y coincide con los medios; el anticipo no se suma como una obligación pagada. La
aplicación reutiliza `ApplyPortfolioPaymentsAsync` y las aperturas comunes de
créditos, dentro de la misma transacción de asiento, caja e historial. No se
crean documentos, trabajos ni números hijos. Abrir varios créditos usa un
comando por lote, sin SQL por partida; el vínculo al mayor usa el comando de
líneas existente. Los nuevos créditos manuales mantienen COP y su SourceLineId.

El cruce v4 sin tesorería normaliza un saldo a favor seleccionado contra hasta
100 obligaciones de su misma dirección y tercero por grupo. Los importes de las
partidas deben coincidir; no reparte entre varios créditos por suposición. Un
anticipo puede aplicarse después de registrar las facturas, con un único padre,
sin otro pago, numeración o movimiento de caja. Su reducción de CxP/CxC reutiliza
`PortfolioBalanceReductionSql`, también usado por los pagos. La escritura común
`PortfolioBalanceChangesSql` recibe cambios agregados por obligación dentro del
mismo comando del processor; también la reutilizan los ajustes individuales,
los ajustes del cierre y las aplicaciones de devoluciones de venta/compra a su
obligación. Las devoluciones conservan sus transacciones y vínculos propios;
no mantienen otro cálculo ni actualización del saldo. Un abono con saldo restante conserva `PartiallyPaid`,
incluso si antes hubo un aumento del importe original; un ajuste conserva su
política de `Open` cuando el nuevo saldo alcanza el original. No se agrega una
fuente de saldos, un viaje SQL ni un aplicador fuera del motor. Esta consolidación
no habilita por sí sola la desaplicación de pagos ni sus vínculos de reversión.
La aplicación del
crédito pertenece al mismo processor y actualiza sus transacciones vinculadas
a las transacciones de las posiciones, en el commit del asiento.

**Desaplicación de abonos, implementación local en curso (2026-10-24):** la
referencia de la partida puede identificar la transacción original de un pago
nativo o de un grupo del comprobante. El comprobante contable reabre la deuda y
abre un anticipo por el mismo importe mediante los aplicadores existentes; no
borra aplicaciones, no vuelve a registrar tesorería y no emite números hijos.
El índice de posiciones conserva la reserva derivada del original para impedir
desaplicaciones concurrentes o pendientes que excedan lo aplicado. Las reservas
de desaplicación no reducen la disponibilidad de la deuda antes de procesarse.
La transacción conserva su FK al abono original y el libro usa su variación
positiva mediante `PortfolioTransactionEffect`. Un fallo del asiento debe
revertir deuda, anticipo e historial, conservando la fuente y su reserva.

La captura visual prepara estas partidas desde el historial de CxP/CxC y usa el
mismo `VoucherDraftEditor`, sin otro formulario de contabilización. El GET de
preparación es una consulta acotada a la obligación y transacción seleccionadas,
con permisos de captura y pago; no guarda ni acepta un documento. El editor
espera la respuesta vigente al reabrir y conserva la confirmación de descarte.
`accounting.AppliedPortfolioPayments` reúne la evidencia de pago procesado y
aplicación real para lectura, aceptación y posting: no tiene estado ni writer.

El importe **pagado a la obligación** descuenta las desaplicaciones ya procesadas;
el historial del pago conserva el dinero original. `PortfolioAppliedAmounts`
es la proyección común de esos hechos con signo para listados, cartera por tercero
y reportes históricos. Reutiliza las aplicaciones originales y las transacciones
de desaplicación; no guarda otro importe ni cambia el pago. Los reportes aplican
sus cortes de fecha efectiva/procesamiento a ambos hechos y muestran la
desaplicación negativa en el detalle, de modo que sus aplicaciones suman el neto.
Las consultas filtran/paginan las obligaciones antes de enriquecer su detalle y
conservan sus viajes existentes: dos SQL por lista y dos por reporte (zona de la
sede y consulta de agregados/página/detalles). Los índices
filtrados de desaplicaciones evitan releer otros tipos de transacción para ese
neto. La regresión reprodujo seis valores pagados incorrectos antes del cambio;
después pasaron ocho escenarios de CxP/CxC, pago manual/nativo, cuatro decimales,
saldo, detalle de reporte e historial bruto intacto. La prueba de 10.000 pagos y
10.000 desaplicaciones detectó un plan de CxC que estimaba seis obligaciones en
vez de 10.000 y hacía más de un millón de lecturas. Los reportes materializan
ahora el conjunto autorizado una sola vez dentro del mismo comando SQL, con
índice y estadísticas para sus agregados; no trasladan todos los detalles al
cliente ni guardan otro saldo. La repetición aislada pasó veinte muestras por
escenario: listados CxP p95 151–153 ms y CxC 942–973 ms; reportes antes/después
de desaplicar y páginas 1/2, CxP 455–690 ms y CxC 381–638 ms. Conservan dos SQL
y el límite de dos segundos. La regresión ampliada aprobó 41 casos de CxP/CxC,
incluidas las mismas cifras netas en cartera por tercero. Para rollback tras
aceptar desaplicaciones deben conservarse estos
lectores netos o aplicar roll-forward; volver al lector bruto mostraría otra vez
importes pagados que ya no están aplicados a la obligación.

La pantalla reutiliza los agregados de su página visible para el resumen y
deshabilita la consulta del directorio de terceros al cambiar a facturas o pagos.
La aceptación nativa devuelve identidad y estado del pago, no los nuevos saldos
de la lista: por esa razón de consistencia se conserva una lectura acotada del
propietario visible al completar el wizard. Las demás familias solo se marcan
obsoletas hasta que el usuario las abra; no consultan ocultas ni hacen polling.
Guardar el comprobante manual reutiliza su respuesta sin recargar la cartera.
La prueba contra la compilación anterior reprodujo la segunda consulta de
terceros y el resumen cero de una factura con saldo. La compilación corregida
pasó ambos casos, incluidos error sin ceros inventados, reintento, vacío,
reapertura y 390 px. Pasaron los 19 recorridos de la ronda de pagos, rechazos,
impresión, filtros y comprobantes; el caso móvil se repitió después de cerrar
mediante su botón el aviso de instalación PWA que cubría el reintento. Lint
conservó cero errores y nueve advertencias previas; TypeScript/build y las
16 pruebas previas más las 253 de POS pasaron. La API Release compiló y los dos
recorridos de abono/gasto con API y SQL reales volvieron a pasar; se revisaron
sus capturas. Esta evidencia no cierra reversos, captura fiscal ni cargos tardíos.

La repetición del esquema sobre la base privada con `Modern_Spanish_CI_AS`
detectó un conflicto del backfill de destinos con `tempdb`
(`SQL_Latin1_General_CP1_CI_AS`). Se reprodujo el error 468 ejecutando únicamente
esa migración en una transacción revertida. Sus dos columnas temporales de
códigos usan ahora `COLLATE DATABASE_DEFAULT`: la misma migración pasó dos veces
en esa base sin conservar cambios del diagnóstico. El DACPAC Release compiló
y la actualización privada completa terminó, conservando los 16 movimientos de
tesorería anteriores. No se cambió intercalación del servidor ni se publicó DEV
o producción.

Frontend compiló con TypeScript; lint conservó cero errores y nueve advertencias
preexistentes. Pasaron diez recorridos simulados del editor y las vistas de
cartera, incluidos guardado sin envío, conservación de cuatro decimales,
reapertura con importe actualizado y modal a 390 px sin desbordamiento exterior.
Pasaron además dos recorridos con API/SQL reales: abono de cartera y gasto con
pago al proveedor, seguidos por desaplicación parcial de 50,0001. Guardar conservó
el saldo; contabilizar reabrió la obligación, creó el anticipo y un único asiento,
manteniendo idéntico el historial del pago original. La acción existente
**Actualizar** consultó la página visible y mostró saldo e historial nuevos.
Se revisaron las dos capturas: la tabla se desplaza dentro del editor, sin
recortar el encabezado ni los controles del modal.
La evidencia anterior incluye reserva pendiente, rollback provocado, reintento,
CxC y reaplicación del anticipo; no sustituye la regresión actual. La ronda nueva
detectó y corrigió un enlace equivocado de las correcciones del cierre: su
snapshot conserva `MovementType`, `SourceId` y `SubledgerId`, no
`SourceDocumentType`. Pasaron siete casos SQL con el bloqueo y su mensaje
específico, así como reserva, rollback y replay. La comparación con el pago original usa la fecha en el offset del
comprobante, tanto al aceptar como al aplicar; una captura UTC de la noche no
se considera del día siguiente de ese comprobante. El recorrido real de CxC
encontró el mismo defecto en la validación común de la obligación y se corrigió
allí, sin modificar fechas de fuentes históricas. La suite Foundation completa
pasó 880 casos, incluidas cuatro regresiones de misma fecha local y rechazo de
fecha local futura en ambas direcciones.

La ronda aislada posterior pasó los dos casos de volumen, sin observador SQL,
compilación ni navegador concurrentes y sin aumentar límites. Con 10.500
movimientos, preview y verificación conservaron dos SQL: p95 de 1.502,8 ms en
preview, hasta 1.372,8 ms en páginas de verificación y 1.740,8 ms al verificar
50 correcciones. Desaplicar 1/100 obligaciones conservó 7 SQL de aceptación y
12 de posting, con p95 máximo de 1.115,4/727,9 ms y 20 muestras por etapa. No
se reprodujo el timeout del cierre en esa ronda; no se modificó su consulta ni
se considera que pasar volumen sustituya los casos funcionales pendientes.
Tampoco completa la reversión de cruces sin dinero ni del egreso real. Un abono
con corrección de importe del cierre se rechaza expresamente antes de aceptar
una desaplicación; su importe efectivo y dependencias requieren completar la
integración. Una corrección de cierre no puede cambiar el importe de un abono
con desaplicaciones aceptadas. No se declara este límite como el cierre del
diseño aprobado.

**Desaplicación de cruces sin dinero, verificada localmente (25 de octubre):**
la referencia original de la partida de deuda y la partida del saldo a favor
existente identifican el cruce que se deshace. No se añade un selector de
operación. `AppliedPortfolioSettlements` compone la evidencia de pagos existente
y la pareja de transacciones de deuda/crédito de una aplicación; no tiene saldo
ni writer. La captura, aceptación y posting consultan ese mismo origen. Si no se
puede probar la pareja histórica y su cuenta, se exige conciliación; no se elige
otro crédito del tercero.

El efecto v4 conserva sus nombres para lectores anteriores y añade únicamente
el tipo original normalizado cuando es `CreditApplication`; las fuentes de pago
anteriores conservan el campo ausente. En ese caso `AdvancePositionId` identifica
el saldo a favor ya existente. `ManualVoucherCreditTargets` deriva sus dos
destinos para bloquearlos con el orden canónico; la reserva contra la transacción
original impide exceder lo aplicado, contando también fuentes pendientes.
Solo el mismo aplicador de desaplicaciones reabre la deuda y restituye el crédito.
Aplicar/restaurar el disponible comparte `CreditBalanceChangesSql`, con límites
0–importe original. Las dos transacciones nuevas se enlazan al par original y
al mismo comprobante/asiento. No hay apertura, pago, numeración hija ni caja.
`CreditUnapplication` distingue el movimiento de deuda del dinero conservado como
anticipo; no vuelve a descontar el pagado efectivo en `PortfolioAppliedAmounts`.

Aceptación: deben probarse CxP/CxC, reversión parcial/completa, pareja y cuenta
originales, reservas pendientes y concurrencia, rollback del crédito/deuda/libro,
replay sin efectos, mismo historial de dinero, reportes netos y captura visual.
El GET conserva un SQL; aceptación/posting conservan sus comandos por lote,
con un máximo de 100 originales por grupo, sin lecturas por partida. Se mantiene
el presupuesto de dos segundos con 10.000 antecedentes. La primera prueba
reprodujo dos rechazos 404 de cruces reales antes del cambio; después pasó una
ronda de nueve casos de pagos/desaplicaciones. La matriz ampliada aprobó siete
casos y la regresión siguiente 47: proveedores, clientes con/sin rol, restitución
en dos partes hasta completar el original, reserva pendiente, rollback provocado
del asiento sin variación de deuda/crédito, replay, crédito restituido utilizable
e historial de dinero intacto. También rechazó otro crédito del mismo tercero y
auxiliar sin aceptar fuente y comprobó empresas con razón social sin nombre
comercial. En CxC el reporte mantuvo el pagado efectivo y el saldo reabierto.
Las seis pruebas del normalizador pasaron, incluida la separación entre una
apertura nueva y el grupo de
restitución. Una ronda ampliada posterior aprobó 30 casos, incluida concurrencia
de restituciones y las garantías de integridad/replay de la aceptación canónica.

Esa ronda siguió a un interbloqueo real capturado entre la aceptación de un
competidor, que tenía la obligación, y el processor del ganador, que tenía su
trabajo. La comprobación de integridad pedía un bloqueo de actualización de
rango en el índice de trabajos. Ahora busca exclusivamente las claves del lote
y usa lectura para esa comprobación; los bloqueos de fuente conservan la
serialización de la aceptación. No añade reintentos, estados ni viajes SQL y
conserva los rechazos por trabajo ausente/alterado. La regresión específica
del trabajo vecino bloqueado pasó: aceptar otra fuente no espera su bloqueo de
actualización y conserva un comando SQL. La medición repetida también pasó con
10.000 antecedentes y veinte muestras: restitución de 1/100 originales, captura
176,2/204,3 ms (un SQL), aceptación 756,5/588,5 ms (siete SQL) y posting
517,3/703,9 ms (once SQL), todos p95 inferiores a dos segundos. Pagos nativos
y desaplicaciones de dinero conservaron sus cinco/once y siete/doce comandos;
el mayor p95 de esas etapas fue 1.006,7 ms.

Pasaron las 882 pruebas Foundation, lint sin errores (nueve advertencias
anteriores), preparación/POS de frontend (16/253), TypeScript y compilaciones
API Release, esquema Release y Next. Diecinueve recorridos con HTTP simulado
incluyeron los modales de ambas carteras y captura, rechazo, incertidumbre y
evidencia de pagos. Dos recorridos contra API/SQL locales registraron el abono,
desaplicaron dinero a anticipo, aplicaron ese mismo crédito y restituyeron una
parte desde la vista: cuenta y crédito originales, cuatro decimales, saldo,
un asiento por fuente e historial monetario intacto. La actualización del
esquema en esa base local conservó sus 16 movimientos anteriores de tesorería.

La auditoría visual encontró que el modal ocultaba el estado y el acceso al
asiento junto con el encabezado de página. Ahora muestra el estado autoritativo
mediante el catálogo ya cargado y reutiliza el mismo botón/visor del editor;
no consulta otro estado. Pasaron lint enfocado, otra compilación Next, los dos
modales simulados y los dos recorridos reales repetidos, incluidos
**Contabilizado** y abrir el asiento desde el modal. Se inspeccionaron las
capturas de ambas direcciones. El aviso de cargo al empleado se limita al
origen de conciliación de cierre; una obligación manual sin cliente no recibe
esa etiqueta.

Auditoría posterior de este slice: contratos, normalizador, captura, parser de
reservas, aceptación SQL, aplicadores, vistas y UI conservan el motor, fuente,
job y writer canónicos. Hay un solo actualizador del disponible de créditos y
un solo aplicador de desaplicaciones; las vistas no guardan saldos. Se revisaron
scope y permisos, pruebas de identidad original, reserva/concurrencia, rollback,
replay y compatibilidad. No hay I/O por partida, polling, otro lector de estado
ni otra operación de caja. La corrección del bloqueo permanece en la misma
aceptación y su regresión conserva un comando. Esta verificación cierra la
restitución de cruces; no cierra el reverso real del dinero, la integración con
correcciones de cierre, soporte fiscal, cargos tardíos, descuentos ni la
auditoría global del rediseño.

Compatibilidad: el esquema es aditivo y los efectos anteriores omiten la
propiedad nullable nueva. Una fuente de restitución nueva requiere el processor
actual; uno anterior no puede validar su forma y debe dejarla sin aplicar.
El cutover coordina API y workers antes de habilitar esa captura. Una vez
aceptadas fuentes nuevas, conservar sus referencias, índices y tipos de
transacción y avanzar con una corrección; no retirar columnas ni convertirlas
en desaplicaciones de dinero para volver a un binario anterior. Antes de ese
cutover, deshabilitar la captura nueva no modifica hechos existentes.

La consolidación anterior, independiente de ese slice, pasó 23 casos dirigidos
y el caso integral de gastos al repetirlo aislado después de un primer resultado
de 1.028 ms contra su límite de 1.000 ms. Pasaron tres casos de volumen: pagos
nativos de 1/100 obligaciones conservaron 5 SQL de aceptación y 11 de posting
(p95 máximo de 75,8/63,5 ms); la aceptación manual de 50/500 partidas conservó
8 SQL con 10.000 posiciones previas (p95 máximo de 163,4/631,4 ms).
Dos recorridos reales de vista —cartera y gasto— pasaron en Release, con revisión
de sus capturas. Esta evidencia precede a los cambios de desaplicación y no los
certifica, ni sustituye la regresión y auditoría global pendientes.

`accounting.SourcePositionIndex` es la proyección derivada de destinos e importes
de estos cruces aceptados. `ManualVoucherCreditTargets` es su parser común de
aceptación y backfill; este último comparte el recorrido por lotes de 100 fuentes
con el índice de terceros. No se genera desde borradores ni tiene otra edición,
saldo, estado, motor o trabajo. `ObligationAvailability` descuenta sus reservas
tanto de CxP/CxC como del crédito; las transacciones aplicadas identificadas por
EffectId dejan de contarse como reservas. Un fallo conserva la fuente/reserva y
revierte saldos, transacciones, outbox y asiento. Replay no reconstruye índices.

La verificación local inicial pasó 11 casos SQL/API: anticipos de proveedor y
cliente, con rol o solo tercero, con abono simultáneo o facturas posteriores;
cruce parcial contra una o varias obligaciones, rollback después de efectos,
rechazo del abono competidor, vínculo original perdido con pendiente explícito,
reintento y replay sin dinero duplicado. La primera inyección de fallo cambió
CommandText cuando SqlClient ya lo había preparado y no provocó el error; la
prueba se corrigió mutando el parámetro del asiento y comprobó el rollback real.
Esta evidencia antecede a la ampliación del lector paginado a los cuatro tipos,
todavía en verificación. No certifica aún la captura visual completa, filtros de
sede del tercero, reversos, movimientos directos, puente fiscal o corte total.

La suite Foundation anterior a estos cruces pasó 861 casos. La medición aislada
de aceptación de anticipos comprobó ocho SQL en 50/500 partidas, con p95
143,3/1.364,2 ms y 20 muestras por tamaño sobre 10.000 posiciones de referencia.
El caso de pagos agotó el tiempo en validación en esa ejecución; una repetición
diagnóstica pasó ambos casos, sin capturar esperas de memoria/bloqueo en las seis
muestras observadas. No se atribuye una causa sin evidencia: falta la medición
limpia y la regresión amplia del código completo. La auditoría final continúa
pendiente.

La revisión de compatibilidad corrigió el cálculo de las devoluciones de
compra/venta y de cargos: la reducción usa `ObligationAvailability` y conserva
los importes reservados para pagos y cruces aceptados. Las partidas de control
de las devoluciones reutilizan `ObligationOpeningAccount` en su lectura de
cabecera existente, sin viajes adicionales; no resuelven el auxiliar mediante
el mapping vigente. Si falta el vínculo original, el processor revierte sus
efectos financieros y deja una causa explícita de configuración pendiente.
La anulación de gastos consulta la misma disponibilidad para bloquear pagos,
cruces y ajustes aceptados pendientes. Las aplicaciones ya realizadas mantienen
el bloqueo preexistente a anular posiciones con efectos distintos de pagos,
hasta completar el reverso explícito definido en este diseño.

La ronda dirigida pasó 20 casos de aperturas, anticipos, cruces y recorridos
nativos; incluyó gasto asistido cuya anulación se bloquea por un cruce pendiente.
La siguiente ronda pasó 16 casos SQL/API, incluidos cambios de parametrización
entre la compra/venta y su devolución, reserva de un abono de COP 40.000 frente
a una devolución de compra de COP 23.800 y pago posterior de ese abono sin
duplicación. Dos casos verifican compra con vencimiento inmediato → egreso
asistido/manual → saldo cero: una CxP, un pago, un asiento por fuente y un solo
movimiento de inventario, incluso repitiendo recepción y pago. Una compra
inmediata sin contabilidad activa conserva su CxP comercial. No se reinterpretan
recepciones históricas de contado ni se inventa una salida de dinero.

La API paginada de posiciones comprende CxP, CxC y ambos saldos a favor; los
anticipos no tienen vencimiento y los históricos sin moneda/naturaleza o cuenta
conciliadas muestran su diagnóstico. La captura visual v4 incorpora consulta
por tercero/tipo, cuenta original, ambos lados de apertura, valor parcial y
vínculo de partida, grupos explícitos, referencia/vencimiento, evidencia de
tesorería y vinculación explícita a la sesión web existente. Extiende el mismo
editor y selector paginado; no consulta posiciones desde vistas ocultas ni por
cada partida. Las opciones provienen de los catálogos existentes. Nueve recorridos
de navegador sobre Next compilado y HTTP simulado pasaron: cuatro tipos de saldo,
importe parcial/superior al disponible, preservación v4, guardado/envío legacy,
reenvío idempotente, grupos/evidencia/sesión y activación explícita desde legacy.
Guardar reutilizó su respuesta, sin GET posterior, y el selector cerrado no
consultó posiciones. La revisión posterior añadió actualización de una partida
ya seleccionada, filtros de cuenta/vencimiento, consulta explícita del origen y
retomar/quitar una sesión que pudo cerrarse desde que se guardó el borrador.
La repetición sobre el artefacto Next actual pasó 12 recorridos: actualización
de una partida sin duplicarla, cuatro clases de saldo, catálogo fallido con
reintento explícito, retomar sesión, grupos/evidencia, ambos historiales con
cruce negativo y presentación móvil. Cincuenta cuentas sin metadatos no
dispararon búsquedas por partida: el editor conserva el propietario de su
metadata por lote y el selector no la vuelve a consultar ni sustituye su
diagnóstico con una caché individual. Lint enfocado, TypeScript del build y
compilación Next pasaron. Son pruebas con HTTP simulado; queda el recorrido
integral contra el servidor real. En esa verificación se conservó la captura
nueva legacy por defecto y la activación v4 explícita. La ampliación fiscal del
25 de octubre, descrita al final, usa v4 para capturas nuevas locales y conserva
la lectura/edición de las anteriores; habilita soporte con su revisión fiscal.
En esa etapa la anulación de soporte permanecía deshabilitada. La ampliación de
anulación descrita al final extiende el mismo editor; el corte global no autoriza
todavía desplegar este conjunto parcial.

El historial de CxP/CxC conserva su importe registrado y expone además
`BalanceChange`, calculado en lectura por `accounting.PortfolioTransactionEffect`: pagos,
devoluciones y aplicaciones de saldos a favor disminuyen la deuda; apertura y
ajustes conservan su signo registrado. No se guarda otro saldo ni se cambia el
importe histórico. Ambos detalles usan `PortfolioTransactions` para presentar
esa variación, sin duplicar su cálculo en React. La misma proyección inline
reemplaza los CASE equivalentes de los dos reportes históricos; no agrega I/O
por movimiento. La revisión retiró la primera implementación en C# para no
mantener esa fórmula también en otra capa. Los siete escenarios de signo pasan
a una prueba SQL del propietario común. Después de la consolidación, el refuerzo
de aceptación y el ajuste de acceso a posiciones, la suite Foundation actual
pasó sus 862 casos. Las regresiones SQL/API y de navegador descritas arriba y
abajo verifican la proyección real; no se conserva una fórmula paralela en C#.

La siguiente regresión amplia pasó 129 casos SQL/API de compras, gastos,
comprobantes, CxP/CxC y devoluciones. Incluye el diagnóstico de cuenta original
ausente con rollback financiero, la recuperación por reintento y la variación
negativa de los cruces en el detalle de ambos submayores. La proyección SQL
compartida comprobó los siete casos de apertura, pago, devolución/crédito,
aplicación de anticipo y ajuste positivo/negativo. Esta evidencia precede al
refuerzo de integridad descrito a continuación.

La auditoría del adaptador de pagos reprodujo con inyección una discrepancia:
se aceptó evidencia de un medio distinta de la congelada en el padre. Ahora
`PortfolioPaymentAccept` compara por conjuntos las aplicaciones, sus ordinales,
identidades e importes, y toda la evidencia de medios contra las matrices del
snapshot inmutable; también comprueba tercero/rol y sesión del padre. Extrae
el padre una vez y cada matriz una vez, sin otro viaje SQL ni escrituras nuevas.
Un desacuerdo revierte la aceptación completa; el adaptador conserva el motivo
controlado y explica «El pago no se realizó». Las primeras rondas del control
rechazaron la alteración, pero fallaron la prueba del mensaje porque se ocultaba
su causa en un texto genérico; se corrigió esa traducción. La ronda dirigida
final pasó 11 casos: rechazó evidencia e importes alterados antes de aceptar,
con mensaje específico de pago no realizado y sin fuente/pagos residuales.
Conservó los abonos y anticipos válidos de ambos lados, sus cruces y replay.
Su presupuesto aislado de 50/500 líneas se verifica a continuación.

La medición limpia del refuerzo pasó anticipos (p95 422,8/662,7 ms), pero el
caso de pagos de 50 líneas superó el límite: p95 3.744,2 ms, siempre ocho SQL.
Dos rondas diagnósticas posteriores pasaron; no se usan como certificación de
latencia porque tuvieron un monitor concurrente. El plan capturado mostró un
scan de 20.000 CxP con evaluación de sus aperturas antes de cruzar los 25 IDs
seleccionados; se observaron más de 160.000 lecturas lógicas en esa validación
y un grant deseado de 9.616 KB. No hubo espera de grant en las muestras, aunque
SQL señaló presión física en alguna de ellas. No se atribuye el problema solo
al equipo. La consulta conserva sus uniones, hechos y alcance, pero ahora
ordena el acceso desde las claves seleccionadas con el patrón de loops y
orden forzado ya usado en las cargas acotadas de cartera. No se cambió volumen,
presupuesto ni timeout.

La medición limpia posterior pasó ambos casos, sin monitor, builds ni navegador:
20 muestras por tamaño, ocho SQL y 10.000 posiciones sembradas por caso. Pagos
50/500 líneas: primer intento 351,5/534,8 ms y p95 131,5/619,9 ms. Anticipos:
929,7/486,6 ms y p95 145,9/421,6 ms. La validación de dimensiones de pagos quedó
en promedio 24,5/39,6 ms (máximos 28,0/48,3 ms). Los marcadores de ambos monitores
estaban cerrados y las mediciones no ejecutaron diagnóstico SQL concurrente.
Conserva las 5.500 aplicaciones o anticipos aceptados por caso, sin aplicarlos
ni fabricar asientos para medir la aceptación asíncrona. Se repite la regresión
funcional amplia después de esta corrección: sus 129 casos pasaron de nuevo,
incluidos los controles nuevos de integridad del padre y las cuatro clases de
posición. La compilación backend pasó con las cuatro advertencias previas de
`System.Security.Cryptography.Xml`; la compilación SQL pasó sin advertencias.

La regresión posterior de tarjetas reprodujo cuatro rechazos incorrectos del
editor al enviar el banco del auxiliar como destino de transferencia. La
normalización conserva esa dimensión en la cuenta elegida y solo llena el
campo bancario del medio cuando realmente es una transferencia. Los cuatro
casos pasan: débito/crédito, pago/recaudo, cuenta elegida en el asiento y banco
intacto en el PUC; no se amplió ni se duplicó el validador nativo de medios.

El historial por sede también reprodujo anticipos sin factura omitidos y un
recaudo mixto mostrado solo por el importe aplicado a facturas. La aceptación
común conserva `PartySiteId` del grupo congelado, comprobando su igualdad con
el padre y su pertenencia al tercero/tenant. Ambos lectores capturan claves
filtradas una vez, agregan solo las aplicaciones de esas claves y paginan antes
de enriquecer medios/documentos. El importe no aplicado se deriva del total y
aplicaciones; no se persiste otro saldo ni importe de anticipo para el reporte.
`20261023_ManualPaymentSites.sql` completa únicamente la dimensión explícita
del snapshot, en lotes de 100 fuentes. Las pruebas ejercen el script real dos
veces: la segunda no cambia rowversion; replay de pago no repara el campo nulo.
Los pagos nativos conservan sus aplicaciones como alcance, incluidos recaudos
entre varias sedes. El corte exige escritores manuales v4 compatibles con la
dimensión: el rollback no puede volver a un adaptador v4 que la omita; las
capturas nativas y manuales anteriores sin grupos conservan sus contratos.

La regresión amplia anterior a la asociación de comprobantes a compras pasó
**135 casos SQL/API** y la base **862 casos**, incluidos los cuatro de
tarjetas, cinco combinaciones de anticipo/abono/rol, migración y replay, rechazo
explícito de sede alterada con rollback de aceptación, contado pagado desde ambos
orígenes, cartera, compras, gastos y devoluciones. El historial pasó además dos
casos de volumen, con 10.000 pagos por lado, 20 muestras por escenario y una sola
consulta por página: CxP p95 467,2/384,4 ms en páginas uno/dos; CxC 246,4/281,4 ms.
Con fecha acotada a 100 pagos, CxP 45,7/48,7 ms y CxC 37,6/35,7 ms. No hubo
builds, navegador ni diagnóstico SQL durante la medición. No certifica todavía
reversos, movimientos directos, captura fiscal ni los cargos posteriores de compra.

**Fijación de mappings nativos, 24 de octubre, verificada en SQL/API local:**
`ResolvedAccountMappingsJson` amplía el trabajo contable existente con identidades
de configuración por categoría, sin importes ni un segundo libro. La aceptación
canónica captura las categorías del perfil activo por sede/fecha. El participante
`AccountMappingsFreeze` resuelve una vez cada sede/fecha del lote de hasta 100
fuentes, conserva las selecciones anteriores y solo completa categorías ausentes.
El processor lo reutiliza antes de su savepoint financiero para los trabajos
pendientes, incluidas las fuentes antiguas sin esta selección. No modifica
trabajos terminados ni reconstruye una configuración histórica para ellos.

La selección del override y la disponibilidad del auxiliar son reglas distintas:
`ConfiguredPostingAccountIdentity` elige la identidad sin consultar ni bloquear
todos los auxiliares; `PostingAccountReadiness` valida y bloquea únicamente las
cuentas solicitadas para el asiento. `ConfiguredPostingAccount` compone ambas
para sus consumidores actuales. `ResolvedPostingAccount` aplica esa misma
disponibilidad a la identidad del snapshot; tanto el resolver de partidas como
la importación de cartera preexistente lo reutilizan en sus consultas existentes.
Un override inválido conserva su identidad y
requiere corregir ese auxiliar: no salta a una cuenta compartida ni sustituye una
cuenta ya elegida. Las partidas explícitas y los bancos mantienen sus propietarios
de fijación existentes. El snapshot no marca como usados los auxiliares ajenos al
movimiento; su protección de significado sigue correspondiendo al uso real.

Presupuesto: conservar el único viaje de aceptación y el viaje existente del
resolver; cero lecturas por partida, deduplicando configuración por sede/fecha.
El lote de 100 fuentes conserva su gate de dos segundos: con veinte muestras
aisladas, p95 de 890,9 ms para una fecha y 568,0 ms para cien fechas, un SQL en
ambos casos. Pasaron nueve casos dirigidos: cambios de mapping, categoría ausente
completada en el retry, cuenta inactiva sin fallback, scope, concurrencia,
trabajo antiguo pendiente, replay terminado y reversos de propinas. La revisión
del diff corrigió además la importación de cartera: una cuenta fijada no disponible
deja un diagnóstico explícito y cero apertura/asiento; reactivarla permite usar su
identidad original. Ese caso y las regresiones de CxC/devoluciones pasaron 42 casos.
La ronda comercial anterior aprobó 230 casos de CxP/CxC, compras, gastos,
devoluciones, comprobantes, ventas y pedidos. Esta evidencia corresponde a la
fijación de cuentas y no cierra las capacidades pendientes del diseño completo.

Las anulaciones de propinas invierten las partidas del asiento original,
incluidos sus auxiliares, terceros y centros. No reinterpretan sus categorías
con el mapping vigente al anular. Si falta el asiento o su centro original,
quedan pendientes con causa explícita; no se inventa una cuenta histórica.
La lectura del original comparte el comando de validación de la propina y no
agrega viajes por partida. El efecto financiero continúa en el mismo processor.

**Fijación de auxiliares de pagos nativos, 23 de octubre, verificada en local:**
`SupplierPaymentTenders` y `CustomerPaymentTenders` reutilizan sus campos de
cuenta y partida. La aceptación nativa asigna una identidad estable por medio;
no acepta una cuenta ni esa identidad suministradas por el cliente. El contrato
de grupos manuales conserva sus partidas y cuentas de la fuente padre.
`PortfolioPaymentAccountsFreeze`, participante de la aceptación contable y del
reintento explícito del mismo processor, fija únicamente cuentas todavía nulas.
Usa `ConfiguredPostingAccount` para el override válido por sede/fecha y
`PaymentPostingCategory` para el perfil del medio; el posting general y las
resoluciones de otros medios comparten esas mismas funciones. Las transferencias
reutilizan la resolución bancaria ya congelada. No se crea otro saldo, fuente,
trabajo ni campo JSON de configuración.

Las partidas aceptadas conocidas no se reemplazan al cambiar mappings ni al
reintentar. Si falta una cuenta, el trabajo queda pendiente y revierte sus efectos
financieros; el mensaje identifica que el pago/recaudo **no se realizó**. El retry
explícito puede completar ese espacio una vez. Un replay de aceptación solo
devuelve su resultado: no completa cuentas ni repara datos. Los medios históricos
sin identidad de partida conservan su lector anterior; no se les inventa una
cuenta de aceptación ni se cambia un asiento histórico. Las cuentas fijadas quedan
protegidas por `ConfigurationLockedAt`, con el mismo propietario existente.

La lectura del pago trae cabecera, aplicaciones y medios en un comando acotado,
contrasta el detalle con la fuente aceptada y construye las partidas del asiento
desde esos auxiliares. Las correcciones de cierre conservan el auxiliar original
mediante una referencia interna `PostedAccount`, aislada por tenant/sede en el
mismo resolver. Una corrección de importe sin cambiar el medio también traslada
la diferencia del grupo agregado al auxiliar original, conservando el tercero.
No modifica el recibo original ni vuelve a aplicar un pago.

Presupuesto: hasta 100 obligaciones seleccionadas (límite nativo existente), un comando de fijación de
cuentas por posting nativo y ningún I/O por obligación o medio. Aceptación y
posting se miden por separado, veinte muestras en caliente por cardinalidad,
con objetivo p95 inferior a dos segundos y número constante de comandos.
La regresión reprodujo primero que un pago pendiente usaba el mapping nuevo.
La ampliación encontró una corrección de importe del cierre que no llegaba al
auxiliar original; se corrigió sin modificar el recibo original. La suite final
aprobó 72 casos SQL/API: pagos a proveedores, recaudos, anticipos, medios,
sesiones, aislamiento y correcciones. Cuatro escenarios comprueban cuenta
conocida/faltante en CxP/CxC, cambio de mapping, retry, replay y que un pago nuevo
use la configuración nueva. El cierre comprueba además los 2.000 de diferencia
de un pago de 5.000 corregido a 3.000, en la cuenta original y con su tercero.
La medición aislada, con 499 obligaciones abiertas y veinte muestras por tamaño,
aprobó 1/100 obligaciones por pago: aceptación p95 17,7/96,8 ms, cinco comandos;
posting 34,2/94,0 ms, once comandos y una escritura de partidas. El límite nativo
de cien obligaciones se conserva: el primer intento de medir 499 fue rechazado
por el contrato compartido; no se amplió el límite ni el presupuesto. Son cifras
locales, sin builds ni diagnóstico SQL concurrentes. No certifican carga
productiva ni la implementación global.

La auditoría del slice siguió los stores nativos hasta `PortfolioPaymentAccept`,
`AccountingSourcesAccept` y el mismo `SqlAccountingPostingProcessor`. No hay
otro aplicador de pagos ni escrituras de saldos en la fijación de cuentas. La
selección duplicada de la apertura de cartera importada se sustituyó también por
`ConfiguredPostingAccount`; sus 22 casos de cartera/corrección pasaron después
del ajuste. La suite de base/arquitectura aprobó 869 casos antes de ese último
ajuste de consulta; después pasaron sus 48 controles de arquitectura afectados.
Los conjuntos se localizan por claves de fuente/pago, con tenant/sede, y se
resuelven en lote; no se cargan medios de otros pagos ni se consulta por partida.
La concurrencia conserva el bloqueo del trabajo y la transacción serializable,
y los replays no alteran los detalles aceptados. La UI no añade consultas ni
propietarios de carga en este slice. Los seis recorridos reales con login,
API y SQL locales pasaron: tesorería directa, comprobante libre con base/copia,
abono de cartera, gasto con cuenta automática, recepción con descuento/cargo
existente y pedidos con edición/recuperación/facturación por lote. Se revisaron
también las capturas de tesorería y lote emitido. El conjunto tardó tres minutos;
no es una medición SQL ni una certificación de las capacidades aún pendientes.
La auditoría global del diseño ampliado sigue abierta.

La revisión posterior reprodujo una carrera entre resolver la cuenta configurada
y proteger su configuración de primer uso. Una segunda transacción podía cambiar
la función financiera después de la lectura y antes de fijarla. El resolvedor
compartido conserva ahora `UPDLOCK,HOLDLOCK` sobre la cuenta seleccionada hasta
terminar la transacción del llamador; el bloqueo de actualización evita también
la conversión simultánea de bloqueos durante esa primera protección. La prueba
usa dos conexiones, aislamiento de lectura confirmada y `LOCK_TIMEOUT 0`, sin
aserciones basadas en dormir: falló antes y pasó con el bloqueo conservado.
Después pasaron 80 casos de pagos, abonos, anticipos, tarjetas, cierres y edición
del PUC. La compilación SQL terminó sin errores ni advertencias. La medición
aislada posterior, veinte muestras para una/cien obligaciones, dio aceptación
p95 176,6/146,8 ms (cinco SQL) y posting 81,8/106,5 ms (once SQL y una escritura
de partidas), dentro del presupuesto de dos segundos. La protección permanece
en el resolvedor y la transacción existentes; no añade una consulta, propietario,
reintento ni proceso de recuperación. No cambia el formato de los datos.

El corte exige aceptación y processor compatibles con estas partidas nativas.
La publicación futura debe coordinar escritores y consumidores; un processor
anterior puede ignorar la cuenta fijada y no es un rollback válido para nuevas
capturas. Se conserva el lector compatible y se aplica roll-forward, sin borrar
identidades ni reconstruirlas por replay. Este cambio sigue solo en local.

**Slice local de tesorería directa, 23 de octubre, en verificación:** el mismo
normalizador v4 identifica las partidas de tesorería sin obligación ni anticipo.
Exige contrapartidas balanceadas en su grupo y reutiliza `PaymentTenderBreakdown`
para validar medio/evidencia. No abre CxP/CxC ni genera recibos de proveedor o
cliente, ni facturas ficticias. Congela importe con signo (débito menos crédito),
cuenta, partida, grupo e identidad del movimiento en `DirectMovements` de la misma
fuente. El mismo `SqlAccountingPostingProcessor` verifica esa correspondencia y
escribe los movimientos de sesión en lote, dentro de la transacción del asiento.
La escritura escalar de los movimientos nativos de efectivo delega al mismo
writer por lote; no se agrega otro processor, job ni tabla de movimientos.

`worksessions.OpenSessionForUser` comparte el control y bloqueo de sesión abierta
entre la aceptación nativa de pagos y el comprobante financiero. La sesión exige
tenant, sede y usuario; la captura directa en sesión exige `work-sessions.cash.manage`.
El efecto aceptado no se vuelve a validar contra una sesión ya cerrada al procesar.
La revisión de aislamiento exige además una sesión web (`DeviceId IS NULL`) al
enviar desde este editor. Una sesión de dispositivo preparado conserva su ruta
autorizada de captura y proyección local; el editor web no puede introducirle un
movimiento que falte en SQLite. El guard compartido conserva la admisión de
sesiones preparadas para los pagos nativos que ya entran por su adapter existente.
`worksessions.ManualTreasuryMovements` consulta los hechos de la fuente inmutable,
incluida una fuente pendiente. Usa la captura enviada únicamente para localizar
la sesión con un índice por tenant/sede/sesión; no usa importes del borrador.
Los lectores de totales y detalle materializan ese conjunto una vez por comando
y excluyen los mismos IDs de la proyección procesada, para no sumar ambas formas.
No hay consultas por partida ni un escaneo de fuentes ajenas a la sesión.

La ampliación aprobada por el usuario el 24 de octubre permite tesorería directa
no efectiva vinculada a la misma sesión web. Está en implementación y pruebas:
la conciliación existente ofrece sus mismos grupos de entradas/salidas en todos
los medios. Los hechos y la evidencia proceden de la fuente manual inmutable,
con una sola identidad por partida, incluso antes de terminar el asiento.
Corregir medio/evidencia reutiliza la conciliación y su motor contable; cambiar
el valor de un asiento libre requiere su corrección contable con contrapartidas
explícitas, que el cierre no puede inferir. Las entradas/salidas nativas de efectivo
conservan su restricción original de medio. El editor conserva su versión
predeterminada anterior mientras se completa v4. Esta etapa no habilita reversos,
soporte fiscal ni declara terminada la implementación global.
Presupuesto local: hasta 500 partidas del documento, resolución por conjuntos y
un comando de escritura de sesión por fuente; objetivo de dos segundos en caliente
para aceptación y procesamiento, medidos por separado sin carga concurrente.
La verificación enfocada aprobó dos casos SQL/API, entrada y salida: falla
inyectada después del efecto y antes del asiento, rollback de movimientos,
lectura del esperado desde el intento aceptado pendiente, retry y replay con una
sola fuente, trabajo, asiento y movimiento. No aparecen recibos ni CxP/CxC
ficticios. La regresión ampliada aprobó 71 casos de sesiones/cierres, pagos a
proveedores, cartera y anticipos. Incluyó las mediciones existentes de 50/500
partidas sobre 10.000 posiciones: ocho comandos por aceptación, con veinte muestras;
p95 de 161,2/500,7 ms para anticipos y 130,9/654,7 ms para abonos. Estos números
corresponden a esos caminos existentes y no certifican aún volumen de tesorería
directa. El recorrido de navegador real aprobó captura de egreso contra efectivo,
sesión vinculada, envío desde la vista, asiento y esperado reducido una sola vez.
La revisión visual real también comprobó auxiliares largos sin desbordamiento de
la página. La regresión de aislamiento reprodujo la admisión indebida de una
sesión preparada y pasó tras exigir la sesión web: 18 casos de captura directa,
tarjetas, anticipos, abonos y adapter POS preparado. Verifica rechazo sin fuente,
trabajo o movimiento cuando falta permiso o la sesión no corresponde. El adapter
nativo conserva sus sesiones preparadas. La suite Foundation posterior pasó sus
869 casos. La auditoría completa sigue en curso.
La medición aislada de tesorería directa pasó con calentamiento y veinte muestras
por tamaño: 2/500 partidas, aceptación p95 49,5/241,6 ms con siete SQL; posting
21,6/129,1 ms con nueve SQL y exactamente una escritura de sesión por fuente.
El preview mantuvo tres SQL y el esperado correcto: p95 22,0 ms con 21 movimientos
y 953,2 ms con 10.500. Replay no escribió sesión ni asiento otra vez. El primer
contador solo por base varió entre cuatro y tres consultas del preview; la
instrumentación final aísla cada etapa por su traza y exige las tres consultas
del propietario, sin ampliar el gate de dos segundos. Estos son resultados
locales, no una certificación de carga productiva.
No se ha desplegado a entornos remotos. Aceptar una fuente con `DirectMovements`
exige conservar un processor compatible; el anterior rechazaba ese arreglo no
vacío. Un rollback de UI no permite retroceder ese lector ni eliminar sus hechos.

1. **Guardar:** una transacción conserva el borrador y versión; sin asientos,
   aplicaciones, reservas de saldo ni fiscal. Admite diagnóstico incompleto.
2. **Enviar:** autoriza sede y acciones concretas, compara versión, carga cuentas,
   terceros, posiciones e impuestos por conjuntos. Valida cuadre, cuenta
   contabilizable, dimensiones, período y correspondencia exacta entre partidas
   y efectos. Congela cuentas, reglas, principal, partidas y efectos en la fuente.
3. **Reservar aplicaciones:** dentro de la aceptación serializable bloquea las
   posiciones afectadas en orden estable por tipo/ID y descuenta aceptados aún no
   aplicados. Todos los pagos usan los índices de aplicaciones de pagos
   existentes, también los capturados en el editor contable. Los ajustes
   `AccountAdjustment` reservan desde la fuente aceptada, buscando por sede y
   obligación en `IX_AccountingSourceDocuments_AdjustmentObligation`; su clave
   computada deriva del payload inmutable, sin otro saldo ni escritura. La
   consulta común `accounting.ObligationAvailability` recibe el saldo de la posición
   ya consultada/bloqueada y calcula reservas y disponible en un solo lugar, sin
   otra lectura de la obligación. Sustituye las tres fórmulas
   separadas de pagos CxP, recaudos CxC y aceptación de ajustes. No usa el borrador
   como evidencia de aceptación: las fuentes históricas/directas pueden no
   tenerlo. Un borrador editable no reserva; una fuente aceptada fallida sí,
   hasta que exista su transacción aplicada. La validación excluye únicamente el
   ID del ajuste que se está aceptando en esa misma transacción. Los cruces
   futuros por partida extenderán esta consulta con su índice de destinos
   congelados, sin volver a contar pagos ni ajustes. Las clases de efectos son
   excluyentes y la consulta carga solo posiciones del comando. No reservar dos
   veces el mismo efecto ni recorrer JSON de todos los pendientes del tenant.
4. **Persistir aceptación:** borrador congelado, fuente, trabajo y detalles
   operacionales de los grupos en la misma transacción. Una clave repetida con
   igual contenido devuelve la aceptación; con otro contenido produce conflicto.
   Nunca convertir un fallo de respuesta HTTP en otro comprobante.
5. **Procesar:** `SqlAccountingPostingProcessor` aplica auxiliares y asiento en
   su transacción existente. Un fallo revierte ambos. Las fuentes previas con
   `AccountingEntryRequired=false` conservan su modo; el nuevo editor contable
   exige configuración activa y no degrada un asiento a efecto solo comercial.
6. **Completar:** marca el trabajo y efectos aplicados en el mismo commit; a
   partir de ahí dejan de contarse como reservas pendientes. Un replay terminado
   devuelve el resultado y no recalcula impuestos, cuentas o efectos.

Un pendiente fallido conserva la reserva y su fuente; se corrige configuración
para reintentar o se cancela explícitamente por el flujo autorizado antes de
efectos. No liberar por timeout de la interfaz. Aceptación y procesamiento deben
compartir orden de bloqueos de posiciones; el processor conserva su lease/job sin
que la aceptación intente bloquear ese job en orden inverso.

La aceptación nativa bloquea primero el conjunto acotado de posiciones en el
mismo comando de lectura de replay, antes de leer encabezados de pago, jobs o
disponibilidad. Un `JOIN` a la disponibilidad con `UPDLOCK` en la posición no
garantiza ese orden: el optimizador puede leer aplicaciones/encabezados primero.
El diagnóstico SQL de pagos simultáneos evidenció ese ciclo entre `Payables` y
rangos de `SupplierPayments`/`AccountingPostingJobs`. La corrección conserva
transacción, reservas, motor y número de viajes; no amplía reintentos ni introduce
un bloqueo global. La prueba concurrente debe comprobar un aceptado y un conflicto
cuando ambos abonos juntos superan el saldo, con un único efecto aplicado.

Permisos: capturar, enviar, pagar/recaudar, corregir/anular y emitir fiscal se
comprueban según el efecto solicitado. `accounting.manual.send` por sí solo no
concede ejecución de acciones financieras/fiscales protegidas. Cada partida
valida pertenencia a la sede autorizada; el alcance inicial de un comprobante es
una sede. La dimensión de cuenta debe ser compatible con ella o ser compartida
explícitamente. No introducir consolidación intersede implícita.

**Aceptación del slice de disponibilidad:** ninguna consulta ni viaje adicional
en los tres comandos existentes; las posiciones mantienen su bloqueo de
actualización dentro de la aceptación serializable. El índice acota la lectura
JSON a ajustes de la obligación consultada. Regresión de ajuste directo y desde
borrador, coexistencia con pago/recaudo pendiente, aislamiento por sede, replay,
reserva tras fallo y liberación al aplicar. Medir 1, 50 y 500 posiciones sobre
historia ajena a esas posiciones: un comando SQL por lote y menos de 2 segundos
por lote caliente en SQL local, sin compiladores ni frontend concurrentes.
El cambio de esquema es aditivo y conserva hashes/payloads previos. Se mantiene
temporalmente `IX_VoucherDrafts_Obligation`, porque los binarios anteriores lo
nombran explícitamente: retirarlo impediría su rollback. No tiene lectores en el
runtime nuevo ni es autoridad de reservas. Su retiro exige haber migrado todos
los lectores y cerrar la ventana de rollback a esos binarios. El rollback previo
recupera también su defecto de reservas directas; requiere detener la aceptación
de ajustes directos y drenar sus pendientes antes de volver a esos lectores.

### 13.8 Transición productiva

Comprobación de compatibilidad DEV del 25 de octubre: CJ tiene 91 cuentas y
cero asientos, pero `AccountingAccounts` todavía contiene solo las diez columnas
anteriores. Ninguna de las seis columnas `ParentAccountId`, `FinancialFunction`,
`BalanceSide`, `BusinessId`, `BankAccountId` y `PaymentMethodOptionId` está
desplegada. Las 1.472 cuentas extraídas con la regla aprobada de Código/Cuenta NIC
no se consideran cargadas ni configuradas. La configuración completa requiere
primero el esquema y runtime compatibles; no se crean columnas ad hoc ni se
presenta una carga parcial como completa. La instrucción vigente es no desplegar.
Se hizo únicamente lectura, con acceso temporal limitado a la IP actual de DEV,
y esa regla de firewall se retiró al finalizar. Esto no declara que los saldos
comerciales existentes sean cero ni autoriza una apertura contable en cero.

La indicación del usuario es que el PUC todavía no se usa como contabilidad en
producción; sí se usan compras, gastos, cartera y pagos. Esto permite configurar
el plan, pero **no equivale a una base vacía**. Este trabajo no ha auditado datos
productivos y no autoriza modificarlos.

1. **Inventario de datos y contrato:** informe de fuentes/modos, obligaciones,
   aplicaciones, créditos, bancos, documentos repartidos y trabajos pendientes.
   Resolver duplicados o vínculos ambiguos mediante revisión identificable;
   nunca escoger cuentas por nombre ni borrar saldos para que cuadren.
2. **Expandir esquema y lectores:** columnas compatibles y versión nueva de
   payload. Lectores admiten históricos sin campos nuevos. Publicar servicios y
   workers compatibles antes de habilitar el editor. No cambiar hashes antiguos.
3. **Configurar PUC y enlazar auxiliares:** asignación validada de cuentas a
   posiciones abiertas y bancos; evidencia por origen. Obligaciones ya saldadas
   conservan historia aunque no sea posible enriquecerla automáticamente.
4. **Apertura y corte:** usar `AccountingOpeningBalanceBatches/Lines` existentes
   con fecha explícita; conciliar cuentas/terceros con posiciones abiertas y
   valoración de inventario al mismo corte. La apertura reconoce los saldos
   iniciales en libro, **no crea otra obligación**. No volver a procesar compras
   viejas como nuevas. `ZeroDeclared` solo es válido si el corte realmente es cero.
   La activación inicial y su consulta de preparación rechazan esa declaración
   si existen saldos de CxP/CxC, créditos disponibles, valoración de inventario
   o saldos por cuenta/tercero en el libro. También rechazan operaciones
   documentales o financieras aceptadas que no hayan terminado de procesarse o
   corregirse. La comprobación usa las tablas propietarias dentro de la misma
   consulta de preparación, sin reconstruir inventario desde el kardex ni crear
   totales nuevos. La activación conserva su transacción serializable. Un inicio
   en cero ya aceptado no se invalida por la actividad posterior; su replay
   conserva fecha, modo e identidad originales. La declaración del responsable
   sobre saldos externos a Auraly no se sustituye por valores inventados.
5. **Habilitar escritores nuevos:** un único contrato aceptado por origen. Los
   adaptadores legacy pueden seguir leyendo/reproduciendo fuentes históricas;
   para nuevas operaciones convergen en la misma aceptación. No mantener un pago
   viejo que resuelva cuentas actuales y uno nuevo que use cuentas históricas.
6. **Retirar compatibilidad:** eliminar columnas/constraints sustituidas solo
   cuando consumidores migrados, pendientes procesados y conciliaciones pasen.
   El rollback de UI no revierte dinero ni fuentes; una vez aceptada versión
   nueva, el worker de rollback debe entenderla. Conservar esquema y aplicar
   corrección hacia adelante si el binario anterior no lo hace.

Durante el corte se controla la aceptación de operaciones afectadas y se drenan
trabajos de su alcance para obtener un corte reproducible. No exigir apagar toda
la plataforma ni ejecutar un reproceso global. El procedimiento de release debe
ensayarse con copia autorizada; producción requiere autorización independiente.

### 13.9 Secuencia y aceptación de implementación

**Implementación local en curso:** se añadieron contratos, esquema, API y captura
del PUC, inicialmente por formatos/segmentos y sustituida el 21 de octubre por
captura libre en el editor de cuentas; los auxiliares conservan referencias explícitas a
las dimensiones. Se extendió el borrador con tercero principal independiente de
las partidas y con intercambio de débito/crédito. Trazabilidad filtra claves,
pagina en SQL y después carga detalles; su conteo utiliza las mismas claves y
su reporte de partidas pagina líneas antes de enriquecerlas. La clonación de
borradores y asientos contabilizados usa el guardado canónico, conserva cuentas,
terceros y valores, elimina referencias de ejecución y no crea efectos al copiar.
El mismo ID de copia devuelve su versión autoritativa, incluso después de editarla.
Las dimensiones desactivadas se conservan marcadas en el borrador y se rechazan
al enviarlo. Copiar 500 partidas usa seis comandos SQL; un replay usa uno. La
regresión SQL seleccionada pasó 40/40 y la prueba de navegador cubrió principal,
partidas, D/C, guardar/enviar, exportar y reintentar una copia tras respuesta perdida,
sin GET adicional para reconstruirla. La medición posterior de 100.000 documentos
se registra al final. Esto no constituye el cutover financiero: faltan el cierre
del corte histórico completo, efectos tipados y
convergencia de pagos/anticipos, los cuatro tipos y el slice de compras/costos.
Mientras no exista ese efecto vinculado,
las nuevas cuentas de cartera/anticipos tampoco admiten un asiento aislado que
descuadre el mayor y sus auxiliares. No habilitar ni desplegar este alcance parcial.

La aceptación de fuentes/trabajos se consolidó en `dbo.AccountingSourcesAccept`,
invocada dentro de la transacción de aceptación por el writer del módulo Accounting.
Los adaptadores documentales, nómina y cierres reutilizan ese propietario; el
replay no crea trabajos ausentes ni cambia la clasificación contable aceptada.
La creación de la CxC por faltante de caja también se trasladó al procesador
financiero, junto al asiento cuando corresponde. Una obligación histórica
coincidente conserva sus abonos. Las pruebas de arquitectura verifican ambos
propietarios únicos; la recuperación explícita de jobs legacy sigue limitada
al caso canónico preexistente y no se ejecuta en aceptación.

La búsqueda por tercero usa `accounting.SourcePartyIndex` para las fuentes
manuales aceptadas: es un índice derivado de los GUID del encabezado y las
partidas de `AccountingSourceDocuments.PayloadJson`, sin importes ni edición
independiente. `accounting.ManualVoucherPartyReferences` define una sola
extracción, compartida por la aceptación transaccional y el backfill de esquema.
El replay no escribe ni repara ese índice. Antes de activar los nuevos lectores,
el backfill debe terminar para todas las fuentes manuales históricas; una
referencia a un tercero inexistente o de otro tenant bloquea ese corte.
El backfill avanza por claves en lotes de 100 fuentes y también valida su
cobertura en lotes de 100, sin un anti-join JSON de toda la historia al terminar.
Durante el corte se detienen los escritores manuales antiguos; los
escritores compatibles mantienen el índice dentro de su propia aceptación.
La consulta reúne claves coincidentes desde ese índice, borradores y partidas
contabilizadas, y después aplica el mismo filtro/paginación canónicos. La
resolución de terceros operativos se reutiliza para filtrar y mostrar los
orígenes que no son comprobantes manuales. No agrega otro ledger ni un writer
financiero. El rollback conserva el esquema y requiere escritores compatibles
con la aceptación canónica; un binario antiguo que escriba fuentes directamente
no puede convivir con estos lectores sin reconstruir el índice antes del corte.

El listado captura una sola vez las claves filtradas y pagina antes de cargar
asiento, tercero, fiscal y totales del documento. El conteo es un agregado
separado sobre esas mismas claves dentro del único comando SQL: una aceptación
concurrente no cambia el total después de preparar la página. Solo se captura
metadato filtrado; los detalles siguen limitados a la página. La consulta de
partidas reutiliza ese mismo propietario con su vínculo al asiento.
Primero obtiene los candidatos por tercero/búsqueda y captura sus trabajos con
los filtros autorizados. Las claves candidatas, los trabajos y los asientos
son únicos por documento/tipo: si todas las candidatas ya aportaron un trabajo,
no hace falta consultar fuentes ausentes. Esta comprobación usa solo metadatos
y se realiza antes de añadir borradores. Si faltan trabajos y el estado permite
mostrar esa excepción, se reutiliza su predicado canónico sobre las fuentes;
un trabajo fuera de las fechas solicitadas no se presenta como ausente. Sin
tercero/búsqueda se conserva la clasificación acotada al alcance solicitado y
su revalidación frente a recuperación legacy. Página y conteo usan siempre las
mismas claves capturadas. El índice
`IX_AccountingPostingJobs_Scope_Date` cubre el orden estable de la bandeja. La
selección de candidatos por tercero se recompila ante el sesgo medido de cinco
frente a 50.005 coincidencias, evitando reutilizar la cardinalidad del primero.
Las claves candidatas se insertan en su tabla temporal con la clave primaria ya
definida; no se carga primero un heap para volver a construir y ordenar su índice
después. El conjunto deduplicado y sus filtros no cambian.

La clasificación de fuentes sin trabajo usa el vínculo nullable
`AccountingSourceDocuments.AccountingPostingJobId` y su índice filtrado de
valores nulos. Su FK compuesta exige el mismo tenant, sede, documento y tipo;
un vínculo no nulo prueba únicamente la existencia de ese trabajo, sin sustituir
las validaciones de hash, fecha, modo ni estado. Evita recorrer todas las fuentes
para encontrar una excepción. Los nulos históricos mantienen el anti-join
canónico: un trabajo existente no se presenta como ausente aunque falte el
vínculo. La aceptación lo fija solo para fuentes nuevas en su transacción;
un replay no lo repara. La recuperación explícita existente lo fija dentro de
su misma transacción. No hay otro writer, total financiero ni proceso nuevo.

El backfill de esquema `20261022_LinkAccountingSourcesToJobs.sql` recorre claves
en lotes de 1.000 y enlaza únicamente trabajos ya existentes del mismo alcance.
No crea trabajos ni asientos, no decide modos históricos y conserva los nulos
sin coincidencia. Debe terminar antes de medir el nuevo lector sobre la historia
migrada. La actualización de esquema es aditiva; el rollback conserva columna,
FK e índices. Escritores históricos que dejen el vínculo nulo conservan la
clasificación correcta, aunque no el presupuesto de rendimiento previsto.

La medición local de 100.000 fuentes aprobó el límite original de 800 ms p95,
con 20 muestras por escenario y una consulta por página: primera 161,6 ms,
siguiente 300,9 ms, tercero escaso 223,1 ms, tercero con 50.005 documentos
580,1 ms y búsqueda sin resultados 121,2 ms. Las migraciones de vínculo y de
terceros terminaron en 8,9 s y 53,5 s; repetir la segunda tomó 30,0 s.
Los planes capturados después de las muestras no reportaron spills. Se
conservaron volumen, timeouts, constraints y presupuestos. Para estabilizar la
memoria del equipo se limitaron temporalmente dos renderers de la interfaz de
Codex a 256 MB cada uno, restaurando y verificando sus límites originales al
terminar. No hubo build, navegador ni diagnóstico SQL durante las muestras.
Es evidencia local de este lector; no declara p95 productivo ni completa las
funciones financieras y de compras aún pendientes de esta ampliación.

La búsqueda de identificador conserva `LIKE` y la colación de la base. Solo
omite esa parte cuando un texto literal contiene letras incompatibles con un
GUID; números de documento, referencias y descripciones se siguen buscando.
Los patrones conservan la comparación anterior. La regresión compara nueve
formas de búsqueda con el contrato SQL original, incluidos comodines, tildes
y caracteres de ancho completo, y conserva una consulta por página. Los filtros
de tipo no se recortan: un valor largo no puede transformarse en otro tipo válido.

La regresión de aceptación concurrente reproduce el defecto anterior sin filtro
de tercero: la página conservaba tres documentos mientras el conteo ya devolvía
cuatro. Con las claves capturadas pasan los casos con y sin tercero, un solo
comando por lectura y actualización explícita que incluye el nuevo comprobante.
La prueba del auxiliar reutiliza el mismo observador SQL, sin una segunda
implementación de instrumentación. La suite contable ampliada aprobó 66 de 67
casos; el escenario de venta que excedió su umbral pasó después aislado, sin
cambiar el límite ni el código de ventas. Esto no sustituye la regresión global.

Verificación adicional local de esta consolidación: 28/28 pruebas SQL/API de
cierres, 104/104 de compras, pedidos, recuperación/edición y facturación
individual/masiva, y 3/3 de edición/configuración PUC. El último grupo verifica
filtrado por sede antes de paginar/contar, manteniendo la configuración del PUC
por tenant. Se ejecutaron sin build concurrente después de detectar esperas
`RESOURCE_SEMAPHORE` por presión de memoria en el equipo local; no se aumentaron
timeouts ni se cambiaron parámetros de SQL Server para obtener el resultado.

Orden por dependencias: PUC/bancos → vínculos de origen y apertura → editor y
cruces → Gastos asistido → asociaciones de compra → revisión de costos/precios.
Cada entrega incluye esquema, contratos, motor, UI y regresiones que le
corresponden. No habilitar un editor capaz de desalinear auxiliares mientras se
implementa después su contabilidad.

La validación de dimensiones del comprobante materializa una sola vez las
cuentas, terceros y centros del lote recibido y reutiliza ese conjunto en sus
controles. La consulta se recompila para el tamaño real de la captura; conserva
el mismo viaje y la misma prioridad de errores, sin consultas por partida. La
repetición SQL/API aprobó las 21 pruebas funcionales seleccionadas de borradores
y auxiliar, incluida la concurrencia del reporte y la aceptación/contabilización
de 2, 50 y 500 partidas. La aceptación conserva cuatro comandos y el posting ocho
en esos tres tamaños. La repetición aislada final aprobó la preparación de las
100.000 partidas ajenas y sus verificaciones de cantidad/cuadre, conservando
restricciones y límite de 60 segundos. Solo esa preparación sintética usa bloqueo
de tabla y joins por búsqueda, en la base desechable; el reporte no recibe esos
hints. Balance de prueba: un comando, 283,7 ms; auxiliar: un comando por página,
176,2 y 142,5 ms. Aceptar 500 partidas: cuatro comandos, 238,8 ms; contabilizarlas:
ocho, 226,2 ms. La ejecución no tuvo compiladores ni consultas de diagnóstico
concurrentes. Son muestras locales, no un p95 de producción. La medición de
Trazabilidad con 100.000 fuentes se verificó después en el escenario controlado
descrito arriba.

Además de la sección 10, medir con SQL Server real: 100.000 documentos, 10.000
terceros, 500 partidas y aplicaciones por comprobante como máximo de diseño,
50 elementos por página, incluida página siguiente y búsqueda sin resultados.
El filtro por tercero cubre tanto una coincidencia escasa como un tercero que
concentra 50.005 documentos. El plan de materialización de claves debe
comprobarse en ambas distribuciones, incluyendo spills y latencia;
no basta medir diez documentos por tercero.
La validación de 500 partidas no genera 500 consultas. Las consultas de reservas
deben medirse también con 10.000 fuentes pendientes del tenant, usando índices
y únicamente los efectos de posiciones afectadas. Agregar pendientes ajenos no
debe aumentar proporcionalmente los datos materializados. Si no cumplen el
presupuesto, optimizar la consulta del mismo propietario; no abrir otro ledger.

| Regresión obligatoria | Resultado verificable |
| --- | --- |
| Auxiliar libre y banco multisede | Código/semántica estables; propiedades desde catálogos persistidos; cuenta ajena y remapeo de cuenta usada rechazados |
| Gasto, pago parcial, anticipo y aplicación sin dinero | Mayor por cuenta/tercero coincide con auxiliares; la aplicación de anticipo no crea egreso |
| Dos cuentas en una factura | Pago conserva cada auxiliar de origen aunque cambie el mapping |
| Dos usuarios aplican el mismo saldo | Solo se acepta el importe disponible; pendiente también reserva |
| Error entre auxiliar y asiento | Rollback completo; replay produce un único efecto |
| Copiar, cambiar principal e invertir D/C | Borrador nuevo sin efectos; overrides conservados; aplicaciones incompatibles rechazadas |
| Pendientes del tercero y abono desde la lista | Compras y gastos juntos con cuenta/tercero/lado original; Agregar 200 sobre saldo 500 propone débito 200 y conserva el vínculo; doble clic no duplica |
| Clonar gasto mensual o egreso | Cuentas reutilizadas, factura/fecha revisables, aplicaciones anteriores eliminadas y cero efectos hasta enviar; retry no crea otra copia |
| Pago cruzado entre superficies | Abonar desde cartera reduce disponible en el editor y viceversa, incluso pendiente; historia y mayor contienen un único efecto por pago |
| Carrera cartera/editor | Dos abonos simultáneos sobre la misma CxP o CxC no exceden saldo; mismo orden de bloqueos y sin reservas duplicadas |
| Equivalencia asistido/manual | Para los mismos hechos y cuentas, iguales aplicaciones, saldo, movimientos de sesión y asiento; sin segundo job del pago manual |
| Historial y reverso con fuente padre | Un pago de comprobante con varios grupos se consulta y corrige por su identidad real; no depende de `PaymentId == SourceDocumentId` |
| Corrección de compra pagada | Desaplicación/reverso trazable; dinero real conservado; recepción física intacta |
| Documento soporte y anulación | Un origen financiero y un proceso fiscal; reintento de uno no repite el otro |
| Compra contado/crédito y egreso | Adquisición siempre reconocida según activación; CxP neta, pago separado con cuenta real y saldo conciliable |
| Factura asistida y comprobante existente | Ambas capturas disponibles; una fuente nueva solo para la factura que no existía |
| Cargo 5 millones repartido 3 + 2 | Deuda conserva 5; tercera asignación excedida rechazada; pago no consume reparto |
| Apertura con cartera ya existente | Un saldo inicial en libro y las mismas posiciones; ninguna deuda duplicada |
| Precios sucesivos | La nueva preparación sustituye la pendiente, conserva publicados y espera publicación explícita |
| Seguridad y rendimiento | Tenant/sede/permisos en servidor; consultas constantes por lote, paginación SQL, sin polling/refetch redundante |

Observabilidad usa IDs de fuente, partida, posición, recepción/revisión y job
existentes. Registrar duración y consultas de aceptación/procesamiento, conflictos,
pendientes y diferencias de conciliación, sin datos bancarios completos en logs.
Los estados de progreso separan guardado, contabilización, preparación y publicación.

**Cierre de esta revisión:** fuentes de verdad, PUC, tipos, cruces, transacciones,
compatibilidad y gates quedan especificados. El usuario aprobó el 22 de octubre
atribuir las salidas a las entradas más antiguas primero, conservando el origen
en devoluciones y traslados. Su implementación, la cobertura histórica conciliada
y los cargos posteriores siguen pendientes según el contrato descrito en
[recepciones](goods-receipts-design.md#ampliación-2026-10-20-asociar-comprobantes-y-cargos-posteriores).
No se declara implementado ni libre de riesgos por la existencia de este diseño.
La aclaración final del usuario admite el ajuste contable sin cambiar factura ni
deuda; no subsiste la supuesta prohibición de movimiento contable del prorrateo.

### Consulta de cierre por sesión — corrección local del 2026-10-24

La regresión ampliada aprobó 124 de 126 casos SQL/API. Sus dos fallos fueron de
rendimiento: preview de cierre e historial de pagos. La repetición aislada aprobó
los dos historiales y los otros cinco casos de volumen, pero reprodujo el preview
con 10.500 movimientos: p95 2.369,9 ms frente al gate de 2.000 ms. No se amplió
ese gate ni se omitieron partidas. El perfil posterior separó sus tres comandos:
con solo 21 movimientos, las consultas de totales y detalles tardaron p95
1.723,2/2.068,0 ms; la lectura de sesión, 18,2 ms. En esa corrida también un
caso de anticipos excedió su gate de aceptación, 2.069,1 ms para 50 partidas.

Ambas lecturas de cierre comparten `worksessions.ManualTreasuryMovements`.
La función ahora materializa primero las fuentes enviadas de tenant/sede/sesión
y después expande su JSON inmutable. Esa frontera impide expandir comprobantes
ajenos antes de aplicar el filtro de sesión. Las filas son variables transitorias
de lectura; no hay tabla, saldo, fuente, trabajo, motor ni escritor nuevo. Conserva
sus columnas y tipos, el número/referencia preferido, versión y sesión del
snapshot, y los tres comandos del preview. No cambia la ejecución de pagos.

Con el mismo grupo de pruebas, los seis casos de volumen pasaron después de la
corrección, incluidos anticipos, pagos nativos y apertura de CxP/CxC. El preview
de 21/10.500 movimientos quedó en p95 62,8/1.408,0 ms, veinte muestras tras
calentamiento y tres SQL. Los comandos de aceptación/posting conservaron siete/
nueve SQL para tesorería directa y una sola escritura de sesión por fuente.
La prueba conserva también saldo esperado, cantidad íntegra de detalles y replay
sin otra escritura. Evidencias en TEMP: `auraly-cost-context-financial-regression`,
`auraly-cost-context-performance-isolated`, `auraly-closure-preview-profile` y
`auraly-closure-preview-scoped-green`. No certifica capacidad productiva.

El DACPAC Release compila y la regresión posterior aprobó 40 casos SQL/API de
tesorería directa, sesiones, reclamos, pagos manuales, recaudos y auxiliares
congelados (`auraly-closure-scoped-functional`). Esa evidencia precede a la
ampliación no efectiva aprobada el 24 de octubre: la nueva lectura de evidencia,
sus grupos y correcciones requieren sus pruebas y medición propias.

La actualización del DACPAC sobre la base local retenida también pasó: antes y
después se comparó la proyección completa de las sesiones seleccionadas y se
conservaron sus cinco movimientos existentes. Esto verifica la sustitución de
la función inline por su implementación acotada sobre datos de prueba ya
registrados. No modifica fuentes ni constituye un despliegue remoto.

### Ampliación de conciliación no efectiva — verificación local 2026-10-24

La decisión aprobada amplía el mismo cierre para entradas y salidas de banco y
tarjeta. Los seis casos dirigidos comprueban fuente, trabajo, asiento y movimiento
únicos por partida: rollback, aceptación pendiente, cierre antes del posting,
retry tras cerrar, replay y verificación obligatoria. Corregir el medio conserva
importe y cuenta original como PostedAccount:. Cambiar una transferencia a
tarjeta de crédito debita el banco original y acredita el pasivo configurado
por SupplierPaymentMethod; las entradas conservan su categoría de recaudo.
Cambiar el importe de un asiento libre exige corregirlo desde contabilidad.

El lector materializa una vez por comando los hechos autorizados y el último
estado de decisiones/correcciones. Filtra y pagina antes de enriquecer; expande
la evidencia una vez por documento seleccionado. Excluye por identidad la
proyección procesada para no sumar dos veces. Las claves usan su límite de 200
caracteres y DATABASE_DEFAULT, sin heredar el collation de índices de OPENJSON.
El preview y la confirmación comparten una lectura de métricas y totales: dos
comandos SQL. ManualTreasuryMovements conserva tres argumentos y las primeras
once columnas; añade cuenta e identidad del asiento. Esta función y sus lectores
no forman parte de una versión desplegada anterior al rediseño.

El navegador encontró dos problemas concretos de plan: una optimización de
7.840 ms y reservas de 72.920 KB para una fila. Separar los hechos y usar búsquedas
indexadas sobre la selección acotada evita esa expansión. La validación de una
corrección presentó después 15,3 segundos y un plan que reservaba 18.208 KB; usa
ahora esa misma estrategia. No se aumentaron los timeouts ni los presupuestos.

La auditoría reprodujo también una devolución mezclada entre sesiones simultáneas
del mismo usuario. El lector usa ahora WorkSessionId y BusinessId, igual que el
total, con índice por esas claves. No cambia reintegros, documentos ni saldos. Los
casos normal y de fecha anterior, con cierres, tarjetas y devoluciones nativas,
aprobaron 59/59 en auraly-session-refund-scope-regression.

Los cargos de facturación se leen del array congelado mediante el mismo store.
La página de hasta 100 cargos conserva filtro y totales completos en SQL, con
detalle solo de las claves seleccionadas. La interfaz usa 50, un propietario de
filtro/página y caché inmutable; retiró la descarga completa del cierre. No hay
otra fuente, saldo, writer, trabajo, consulta por cargo ni polling. Se conservan
los contratos completos anteriores para impresión y clientes que los requieren.

Las dos pruebas de 250/10.000 cargos aprobaron aisladas: p95 61,4/819,8 ms,
dos SQL y veinte muestras. La ronda amplia posterior aprobó 48 de 49: el primer
GET de 10.000 cargos agotó su tiempo. El plan sintético identificó 56.664 KB
reservados para ordenar un temporal con JSON. Al retirar ese LOB del índice y
leer sus pagos mediante otra pasada constante del array, la reserva bajó a
2.064 KB. La repetición amplia del ajuste aprobó 49/49 en
auraly-closure-narrow-charge-regression: los cargos de 250/10.000 conservaron
dos SQL y p95 75,6/1.268,8 ms con veinte muestras. No se amplió el timeout.

auraly-closure-correction-performance aprobó veinte muestras por escenario:

- 10.500 movimientos: preview p95 789,8 ms, dos SQL.
- Primera petición sin filtro de una fila: 1.109,4 ms, dos SQL.
- Páginas 1/2 de 50: 1.149,0/1.483,8 ms, dos SQL.
- Preview de una/50 correcciones: 1.320,7/1.233,7 ms, dos SQL.
- Aceptar/contabilizar 500 partidas: 332,4/178,9 ms y siete/nueve SQL, una
  escritura de sesión y ningún efecto repetido en replay.

Son mediciones locales aisladas con presupuesto de dos segundos. Pasaron
compilaciones API Debug/Release, Next/TypeScript y lint (cero errores; nueve
advertencias anteriores). Las cuatro advertencias de dependencia fiscal son
anteriores. La nueva paginación, reintento explícito, totales y regreso desde caché
pasaron en navegador simulado, junto a los otros 15 casos de cierres y captura
financiera. La repetición con API local activa aprobó los 17 recorridos,
incluido el login real y el modal de periféricos. El binario final aprobó también
los dos recorridos reales de efectivo y transferencia. La consulta inicial de
comprobantes respondió en 1.406 ms, la página filtrada en 486 ms, los cargos en
61 ms y validar la referencia en 712 ms. Se inspeccionó la captura bancaria:
número de asiento, referencia corregida, importe ineditable y salida con su signo.
Actualizar el esquema de la base privada local conservó byte a byte sus doce
hechos de tesorería. No se desplegó a entornos remotos.

Auditoría de este tramo: lectura, autorización y captura conservan sus propietarios
existentes; ninguna optimización escribe movimientos ni cambia aplicaciones.
Las reglas financieras y el asiento permanecen en el motor contable. El índice
de devoluciones es aditivo; los snapshots originales y los contratos completos
se conservan. La respuesta de cargos solo hidrata la página y conserva por
separado resumen global y conteo/importe filtrados. El test de vista comprueba
reintento explícito, siguiente/anterior, regreso sin GET y cero descargas del
cierre completo. Los demás slices del diseño, la regresión integral y la
auditoría global siguen pendientes; esta evidencia no los declara terminados.

### Rechazos de pago visibles (aclaración 2026-10-21)

El historial por sede debe incluir anticipos sin factura y sumar la porción de
anticipo de un pago mixto. El encabezado del pago conserva `PartySiteId` como
dimensión del grupo aceptado, validada contra su fuente padre; no es otro saldo.
Las aplicaciones siguen siendo la autoridad del reparto entre facturas y sedes,
incluidos los recaudos nativos que abarquen varias sedes. El importe de anticipo
del historial es la diferencia entre el total del pago y sus aplicaciones, sin
persistir un segundo importe. Los pagos históricos nativos conservan el filtro
por sus aplicaciones. Un corte compatible completa únicamente la sede explícita
del grupo manual congelado; nunca deduce una sede desde la ficha actual.

Presupuesto de este lector: un comando SQL por página con sus totales y detalles;
filtrar las claves antes de agregar aplicaciones, ordenar y limitar antes de
cargar medios o documentos de la página. Verificar primera y segunda página con
10.000 pagos, 20 muestras y p95 inferior a 800 ms en integración aislada, sin
builds ni diagnósticos concurrentes. No hay consultas por pago ni polling.

Un conflicto de aceptación revierte el intento completo. El usuario debe ver en
la misma captura **El pago no se realizó**, con el motivo autorizado: documento
y saldo disponible si el abono lo supera, factura pagada/cancelada, moneda distinta,
origen todavía pendiente o tercero incorrecto. Un identificador fuera del alcance
no revela documento ni saldo. No se cierra la captura, no se imprime ni se muestra
confirmación de pago. La prueba de carrera verifica ausencia de encabezado,
aplicaciones, transacciones, fuente, job y asiento para el intento rechazado.

Una respuesta perdida o error técnico no permite afirmar que no se pagó: se
muestra **No se pudo confirmar el pago**, conservando la identidad idempotente
para verificar mediante reintento con los mismos datos. No hay reintento automático
del navegador. Una aceptación pendiente se comunica como pendiente de aplicar;
solo `Processed` permite mostrar el abono aplicado. El historial usa el estado
existente del pago, sin inventar un segundo estado financiero ni otra consulta.

La aceptación nueva devuelve `Accepted` antes de que termine el trabajo durable;
la publicación de su señal no convierte esa respuesta en `Processed`. El recibo
usa ese mismo estado, sin consultar ni ejecutar el pago otra vez. Si se imprime
un acuse pendiente, el renderizador compartido web/Edge muestra **Registro recibido ·
pendiente de aplicar**, **Abonos por aplicar** y **Valor registrado**; no afirma
facturas aplicadas ni valor pagado. `Processed` permite las etiquetas de abono
aplicado. Clientes anteriores que no envían estado imprimen **Aplicación sin
confirmar**; un estado de rechazo no es un recibo válido. Las versiones de las
plantillas térmica/hoja pasan a 3/4. Antes de liberar esta garantía deben actualizarse
también los impresores Edge que todavía usan las plantillas anteriores.

Los nombres de los medios provienen del catálogo que cargó la captura, pasado
a la representación del recibo; no se resuelven con otro diccionario ni otra
petición al imprimir. El historial comparte ese catálogo de pagos y su caché,
con carga habilitada solo mientras se consulta la pestaña de pagos.

Verificación local: 218 pruebas de Edge/impresión y 7 de la API real de cartera
pasaron, incluidas aceptación, aplicación, payload anterior sin estado y rechazo
de impresión con estado inválido. Los 36 recorridos de navegador con HTTP
simulado cubren ambas direcciones, web e instalada, formatos, no imprimir,
rechazo/resultado incierto con identidad conservada y error/reintento de catálogo.
El catálogo se carga una vez en éxito; el fallo usa los dos intentos compartidos
y un reintento explícito, sin otra lectura al abrir/imprimir el comprobante.
Compilaciones completas API/Next, TypeScript y ESLint enfocado pasaron. La
inspección visual de tirillas de 58 mm confirmó los dos mensajes y ausencia de
desbordamiento. Esta evidencia no equivale a la regresión integral pendiente de
todo el rediseño ni a una autorización de despliegue.

### Libro auxiliar por cuenta (referencia visual del usuario, 2026-10-21)

El reporte solicitado amplía la lectura canónica del diario en `SqlAccountingStore`;
no crea otro ledger ni almacena totales financieros independientes. La consulta
actual de movimientos de una sola cuenta no satisface todavía este alcance.
El usuario confirmó que debe ampliarse el auxiliar existente: `BalanceSection`
en la vista de Contabilidad abre `accountingApi.accountMovements`, que pasa por
`AccountingService.GetAccountMovementsAsync` hasta el mismo store. Conservar
ese punto de entrada; no agregar otra pantalla con una consulta financiera paralela.
La revisión también debe cubrir agrupaciones auxiliares intermedias de numeración
libre: no basta expandir siempre 1/2/4/6 dígitos y luego saltar a la hoja si existe
un padre explícito de más de seis dígitos. Nombres y jerarquía provienen del PUC.

El balance de prueba ya recorre los padres explícitos y agrega cada hoja una sola
vez por ancestro, conservando los niveles estructurales anteriores para cuentas
históricas sin clasificación. Un prefijo que no forma parte de esa cadena no
crea otra agrupación auxiliar. Los nombres se leen del PUC; si no está configurado
el nombre de un nivel estructural, se presenta su código sin inventar una etiqueta.
La consulta materializa sus agregados una vez y no escribe saldos persistidos.
La regresión funcional pasó con apertura anterior, corte final, dos auxiliares,
padres de siete/ocho dígitos, hoja de 32 dígitos, aislamiento y ausencia de doble
conteo: un comando SQL y 33,4 ms en el escenario funcional local. La variante
aislada con 100.000 partidas de otra sede en un asiento sintético pasó en 372,2 ms
y un comando, bajo el presupuesto de 750 ms. La preparación usa lotes de 1.000
dentro de una transacción que se revierte si falla; no se amplió su timeout ni
se redujo el volumen. Los intentos anteriores agotaron el tiempo de preparación
y no se contabilizan como mediciones del reporte. La paginación del auxiliar
detallado pasó posteriormente con el mismo volumen, como se detalla en 13.9.

La vista del balance incorpora **Libro auxiliar por cuentas**. Conserva el mismo
lector paginado, los filtros de tercero/centro y los totales del período. Capturar
un rango no consulta por tecla: **Aplicar rango** cambia el filtro y vuelve a la
primera página. El resumen por cuenta corresponde a todo el período; las cabeceras
del detalle indican que muestran los movimientos de la página actual. Abrir una
fila del balance usa `accountRootCode` para incluir su rama; los límites desde/hasta
siguen siendo límites de código independientes. Editarlos cambia a consulta por
rango únicamente al aplicar. No se monta otro lector ni se recarga el catálogo
de centros desde el diálogo.

La auditoría reprodujo que convertir una agrupación en un rango exacto dejaba
fuera sus auxiliares. Balance y detalle ahora consumen el mismo CTE canónico de
ancestros explícitos y niveles estructurales; el detalle filtra sus claves antes
de capturar hechos, contar, paginar y enriquecer. También lo reutiliza el contrato
anterior de arreglo. Nueve pruebas SQL/API pasaron: padres de 6/7/8 dígitos, hoja
de 32, agrupación ajena con prefijo coincidente, alcance, contrato anterior y
concurrencia. Con 100.000 partidas ajenas, cada apertura de agrupación hizo una
consulta (93,6–117,8 ms); las páginas uno/dos usaron una cada una (152,5/138,6 ms).
La compilación de backend pasó. La nueva interfaz de rango pasó compilación y
su recorrido de navegador con HTTP simulado, también tras incorporar el filtro
de rama (26,7 s): una carga por acción, sin consultas por tecla ni al ocultar la
vista, conservando catálogo compartido, totales entre páginas y diálogo móvil.
La captura de base opcional ya se conserva en borrador, fuente, partida y lectura
del auxiliar; el recorrido real de comprobante libre verificó su persistencia.
**Impresión y exportación, implementación local verificada:** el botón del
auxiliar prepara un snapshot completo mediante
`AccountingService.GetAccountMovementReportAsync` y el mismo lector SQL de la
página, sin otra consulta propietaria del diario. Conserva los filtros aplicados,
la jerarquía, el tercero de cada partida, base opcional y saldos firmados. La
misma captura alimenta `ReportViewer`, impresión y los renderizadores existentes
de PDF/Excel/CSV; descargar otro formato no vuelve a consultar contabilidad.
El lector rechaza más de 5.000 registros entre movimientos y saldos por cuenta
antes de paginar/enriquecer detalles, con un mensaje que pide reducir rango o
filtros. No entrega la primera página como informe completo. La paginación y el
contrato anterior de arreglo conservan su alcance y límites anteriores.
El permiso sigue siendo `accounting.read`, con tenant/sede en servidor. La vista
retiene un solo dueño de la carga paginada, la deshabilita al mostrar el snapshot
y reutiliza su caché al volver; el contexto de tenant/sede identifica y reinicia
ese propietario al cambiar. No remonta el componente como efecto de una mutación.

Presupuesto de aceptación: un comando SQL por preparación, hasta el límite
de registros descrito, p95 inferior a dos segundos en 200/4.998 movimientos,
veinte muestras por tamaño sin cargas concurrentes. La prueba del exceso usa
5.000 movimientos más su saldo de cuenta y debe rechazar en ese mismo comando.
Dos casos SQL/API aprobaron lectura completa de más de 200 partidas, aislamiento,
permiso y guard del lector. Los 253 tests de frontend, incluidos los dos nuevos
de snapshot completo y base nula/cero, pasaron. La medición aislada aprobó veinte
muestras por tamaño: p95 143,3 ms con 200 movimientos y 243,7 ms con 4.998,
un comando SQL en ambos tamaños. Cinco mil movimientos más su saldo de cuenta
se rechazan antes del detalle; la página de 50 continúa disponible.

El recorrido de navegador con HTTP simulado aprobó más de 200 movimientos,
rechazo recuperable de la preparación, filtros, regreso a la página anterior
sin otro GET y PDF/Excel/CSV desde la misma captura. El recorrido contra API/SQL
locales aprobó guardar, contabilizar y clonar el comprobante libre, consultar
su auxiliar y descargar PDF/Excel, comprobando tercero, base -12,3456 e importe
1.200,1234. Preparar y descargar ambos formatos hizo un solo GET contable.
La primera repetición encontró un binario local anterior (404); se recompiló
API Release y se repitió el recorrido completo con éxito. La identificación de
la partida en la prueba usa su número único, para tolerar datos de pruebas anteriores.

Se inspeccionaron visualmente primera y última página del PDF y de la impresión
del navegador de 203 movimientos, y la página del PDF real. Se conservan última
partida, secciones y totales sin recortes. El NIT procede de la marca existente
cuando está registrado; la base local sin NIT no lo inventa. Excel conserva
valores numéricos y formato de hasta cuatro decimales. El comprobante individual
comparte esa precisión; no exporta una selección local incompleta de sus partidas.
Pasaron lint (cero errores; nueve advertencias preexistentes), compilación Next
y API Release (las cuatro advertencias preexistentes de dependencia fiscal).
Esta evidencia verifica el reporte; no certifica los cruces, reversos, cuatro
tipos documentales, cargos posteriores ni la auditoría global pendientes.

Auditoría posterior de este slice: API, aplicación, lector SQL, adaptador de
presentación, visor compartido, exportadores y pruebas conservan un propietario
por responsabilidad. No se añadieron saldos, tablas, jobs ni escrituras contables;
la preparación es lectura explícita con permiso y alcance en servidor. La vista
no calcula saldos ni totales financieros, no consulta por fila y no introduce
polling. El snapshot materializado en SQL conserva coherencia ante un posting
concurrente. La ronda posterior aprobó los diez casos SQL/API de PUC/auxiliar,
incluidos jerarquía libre, contrato anterior, concurrencia y volumen de 100.000
movimientos ajenos. Retirar esta superficie no requiere revertir datos; se
conservan el diario y el contrato paginado anteriores. La auditoría del resto
del rediseño financiero continúa pendiente y no se sustituye por esta revisión.

- Filtros: fechas desde/hasta, cuenta desde/hasta, tercero/NIT y centro de costo;
  sede y tenant se validan en servidor con el mismo alcance autorizado del reporte.
- Sección por cuenta con código/nombre, saldo anterior al inicio y movimientos
  del periodo. Columnas: fecha, número del comprobante, documento relacionado,
  NIT y nombre del tercero de la partida, observación, base registrada, débito,
  crédito y saldo acumulado. El tercero principal no sustituye al de la partida.
- Saldo firmado según el ejemplo: anterior + débitos − créditos, también en
  pasivos. Totales por cuenta: saldo anterior, base, débitos, créditos y saldo final.
  Rango sin movimientos pero con saldo anterior conserva su saldo al cierre.
- La base debe proceder de evidencia inmutable de la partida. No se obtiene del
  importe del débito/crédito ni del total de la factura. Un histórico sin esa
  evidencia presenta base no registrada; no se inventa cero ni se reconstruye
  mediante una unión ambigua con impuestos del documento.
- `BaseAmount` es opcional y conserva el signo registrado a cuatro decimales;
  no determina el débito, crédito, impuesto ni saldo de la partida. Vacío y cero
  son valores distintos. La captura se conserva en el borrador y después en la
  fuente y la partida del mismo writer contable; clonar conserva ese dato.
  El auxiliar lee la partida contabilizada, sin volver a consultar o calcular
  una base tributaria. Su total por cuenta suma las bases registradas del período
  solo si todas son conocidas; con alguna base ausente, el total queda sin dato.
  Los históricos permanecen nulos. Omitir los nuevos campos nulos al serializar
  las capturas conserva los hashes de los contratos anteriores.
- Abrir el comprobante, imprimir y exportar a PDF/Excel conserva filtros, orden,
  tercero, saldo inicial y totales. Impresión/exportación son lectura del mismo
  reporte y no ejecutan contabilización ni aplicaciones.
- La pantalla pagina en SQL antes de enriquecer partidas. Conteos, apertura y
  totales son agregados con los mismos filtros; el saldo corrido no se reinicia
  al cambiar de página. Orden determinista por cuenta, fecha, número, asiento y
  número de partida. No hay consulta por cuenta ni por tercero, ni fetch-all.
- Gates: apertura fuera del periodo, dos terceros en una cuenta, abono parcial,
  anticipo y cruce sin dinero, reverso, cuentas de orden, cuenta/sede ajena,
  continuidad entre páginas y equivalencia de pantalla/impresión/exportación.
  Medir primera/siguiente página de 50 partidas con 100.000 movimientos ajenos:
  un comando SQL por página y menos de 750 ms en SQL local aislado.

Los criterios restantes conservan su puerta de implementación y auditoría;
la evidencia de consulta anterior no sustituye impresión/exportación ni cruces.

### Medición local posterior de trazabilidad, 2026-10-23

Con 100.000 fuentes y 10.000 terceros en una instancia LocalDB exclusiva de
pruebas, sin diagnóstico ni compilación concurrentes, pasó el gate de un comando
SQL por página y p95 inferior a 800 ms: primera 368,4 ms; siguiente 347,5 ms;
tercero poco frecuente 295,6 ms; tercero con 50.005 documentos 784,4 ms;
búsqueda sin coincidencias 232,5 ms. La primera ejecución del tercero frecuente
tomó 877,3 ms; el gate acordado es p95 y no se cambió. No hubo spills en los
planes capturados después de las muestras.

El índice de pertenencia manual se consulta aparte de las fuentes nativas. Las
claves reutilizan fecha y existencia de trabajo demostrada por el FK del snapshot
aceptado; el estado autoritativo se lee del trabajo al enriquecer la página. Si
se filtra por estado o se requieren partidas, se conserva el filtro sobre el
trabajo antes de paginar. El índice de alcance incluye el vínculo al trabajo;
no se accede al payload histórico para obtener ese metadato. Fuentes legadas sin
vínculo, borradores y fuentes nativas conservan su comprobación de existencia y
su clasificación, con las mismas claves para página y conteo.

Los ajustes directos sin borrador también muestran y filtran su tercero desde
la posición original. Pasaron los escenarios de ambos lados de cartera y su
cuenta original, además de 23 casos de aceptación de fuentes en la ronda dirigida
anterior. Esta evidencia no certifica los grupos financieros manuales, los cuatro
tipos, los cargos posteriores ni la auditoría completa del diseño.

### Captura fiscal del borrador manual, avance local 2026-10-25

`VoucherFiscalCapture` identifica las bases de adquisición por `SourceLineId`,
perfil y tratamiento de IVA, su partida de IVA descontable y el vínculo de cada
retención a su regla o identidad manual. No reutiliza `BaseAmount`, que conserva
su significado de evidencia contable opcional. Las bases fiscales no son otro
asiento ni una obligación editable independiente.

El mismo agregado `accounting.VoucherDrafts` guarda esta captura en una columna
JSON nullable, permitida solamente para versión 4 y tipo `SupportDocument`.
Guardar/leer conservan sus comandos y propietario actuales; una revisión del
borrador no reserva cupo, numeración ni saldos. El contrato omite el campo null
al serializar solicitudes/fuentes anteriores, para conservar sus hashes. La
copia limpia la captura fiscal y propone comprobante contable. Se validan
identidades, importes a cuatro decimales, vínculos únicos, permiso de ajustes
tributarios y presencia de revisión antes de enviar.

Pasaron 26 casos SQL/API de borradores, incluida persistencia/lectura/replay de
la captura, copia, formas inválidas, ajuste sin permiso, restricción SQL y envío
sin revisión rechazado sin efectos. También pasaron los casos previos de
guardado, contabilización, concurrencia, replay y 2/50/500 partidas. DACPAC
Release y API Debug compilaron. La actualización es aditiva; los lectores
compatibles deben mantenerse mientras existan capturas o fuentes fiscales
manuales pendientes. No se elimina evidencia para retroceder una versión.

La aceptación del soporte manual ya está conectada al mismo motor. La revisión
reutiliza `WithholdingService.ReviewPurchaseDocument` y la precisión de IVA de
`AcquisitionTaxAmounts`; no calcula impuestos en el editor. La prueba contable
comprueba adquisición, IVA descontable, retenciones y una sola CxP neta del
proveedor principal. Una revisión incompleta conserva el cálculo y explica las
partidas pendientes, sin aceptar efectos. La confirmación exige esa prueba
completa y la firma vigente de los datos revisados.

`SqlFiscalDocumentStore` participa en la transacción de aceptación existente:
reserva cupo antes de las lecturas compartidas de configuración, conservando el
orden de protección del propietario fiscal. Valida después la revisión y prueba
contable, y asigna numeración solamente tras aprobarlas. Cualquier rechazo
revierte también esa reserva; el preview no llama al paso de reserva. Guarda
los snapshots tributarios con su writer y
crea la raíz fiscal mediante `InsertPurchaseSupportSourcesAsync`. No crea un
gasto o recepción ficticios. El snapshot neutral `ManualPurchaseSupportPayload`
permite que `FiscalGenerationWorker` use firma, CUDS, XML y artefactos existentes.
La obligación y el asiento los escribe el mismo `SqlAccountingPostingProcessor`.
Repetir el envío conserva la identidad y no reserva ni publica otra vez.

Pasaron 98 pruebas de comprobantes y arquitectura. Los cuatro escenarios SQL/API
dirigidos aprobaron soporte manual sin impuestos, soporte con IVA 19% y retención
2,5%, recepción con soporte nativo y acceso mínimo al catálogo de IVA. La
variante tributaria verificó rechazo por revisión desactualizada y cupo agotado,
y un fallo inyectado al insertar el snapshot fiscal revirtió fuente, trabajo,
deuda, diario, impuestos, cupo y consecutivo. El reintento completo y su replay
dejaron un solo efecto. El XML firmado conservó total bruto y retención neteada
en la obligación. No se transmitió a la DIAN real.

El editor incorpora datos fiscales vinculados a las partidas existentes, consulta
los perfiles de IVA activos y reutiliza el editor de ajustes tributarios con su
permiso. Cualquier cambio del diario invalida la revisión. El selector de sedes
de proveedor conserva el buscador canónico y filtra por tercero antes de contar
y paginar; no carga directorios completos ni cambia identidades al abrirse.
El presupuesto del preview es cuatro comandos SQL, independientes del
número de bases, y p95 inferior a 1,5 s en veinte muestras aisladas por volumen.
Una captura con ajustes tributarios puntuales puede añadir la consulta por
conjunto de cuentas autorizadas del motor tributario, nunca una por partida.

La medición aislada aprobó veinte muestras con 1/100/499 bases (2/101/500
partidas): p95 de 30,6/40,3/59,3 ms, siempre cuatro comandos. El primer intento
con 499 bases reveló el límite anterior de cien del motor tributario. Se amplió
a quinientas en ese mismo motor, sin fragmentar el cálculo ni cambiar el límite
de captura de Gastos; la retención conserva su base acumulada. Pasaron 134
pruebas de arquitectura, comprobantes, retenciones y generación, incluidos el
umbral acumulado de quinientas bases y el rechazo de cero/501. La repetición
SQL/API aprobó 69 de 70 casos: el único fallo fue la medición de anulación de un
gasto tras desaplicar, 2.248,4 ms frente al gate existente de dos segundos. Su
repetición dirigida tuvo otra muestra de 2.071,9 ms. La medición instrumentada
posterior aprobó las cuatro variantes en 14,5/320,7/90,4/104,0 ms, con una sola
validación y 4/18/17/19 comandos totales según rechazo o efecto. Se conserva el
gate de dos segundos y se registran los intentos anteriores, sin presentar una
latencia aislada como diagnóstico del plan SQL. La variante fiscal también
verificó dos envíos simultáneos y que una sede alternativa del
proveedor se conserva tanto en la CxP como en el XML firmado.

La compilación Next/TypeScript y el lint enfocado aprobaron. En navegador con
HTTP simulado pasaron los ocho casos financieros de reapertura y los tres de
guardado y soporte en escritorio/móvil, con una sola carga de perfiles, sin
consultas por partida y conservando la revisión al guardar. Se inspeccionaron
las capturas de 1440/390 píxeles, sin desbordamientos. Retirar todas las bases
también pasó al repetir los once recorridos. Contra API y SQL locales, la captura
real revisó una adquisición de 5.000, guardó su firma y la conservó al recargar:
un preview, una escritura, cero envíos y una lectura de perfiles mientras el
diálogo estuvo visible. El borrador no creó trabajo ni deuda. Se inspeccionó su
captura visual. Este recorrido no emite a DIAN y complementa la confirmación y
generación firmada verificadas en SQL/API.

Auditoría del puente fiscal: contratos, borrador, revisión pura, participante
transaccional, aceptación, processor, generador y editor conservan sus dueños.
No hay otro gasto, recepción, job financiero, saldo, calculador o writer. La
revisión incompleta devuelve un resultado explícito; la aceptación usa su misma
prueba estricta. La normalización numérica afecta únicamente la firma de
revisión, sin cambiar la serialización del origen histórico. Se verificaron
permisos y alcance, revisión desactualizada, rollback total, concurrencia,
replay, viajes por conjunto y compatibilidad nullable. La reversión de UI debe
conservar lectores/workers compatibles mientras existan estos snapshots.
Esta auditoría corresponde al incremento fiscal; no reemplaza la revisión
completa del rediseño ni declara listos sus pendientes.

Este incremento no certifica anulación de soporte, los cuatro tipos completos,
reversos, cargos posteriores ni la auditoría global pendiente. La instrucción
de entrega continúa siendo local, sin despliegue.

#### Validación del tipo de cuenta y capturas anteriores

La revisión posterior reprodujo dos omisiones. Una captura antigua podía usar
un auxiliar explícito de tesorería y contabilizar sin registrar el efecto
financiero. Ahora la validación existente del envío exige captura financiera
para auxiliares con función distinta de General/UnallocatedPurchaseCost. El
rechazo indica que no se contabilizó ni se registró un pago. Guardar sigue siendo
posible; no se modifica el contenido de fuentes aceptadas ni sus replays. Los
auxiliares históricos sin función conservan su compatibilidad mientras se
concilia su configuración; no se clasifican por nombre o prefijo.

También se reprodujo por HTTP la asignación de Inventario a un auxiliar de
pasivo. `PostingAccountReadiness` compara el tipo de cuenta con el definido por
el perfil activo y es el mismo punto usado para guardar mappings, resolverlos y
validar identidades fijadas. No hay un catálogo paralelo de tipos ni sustitución
de cuentas inválidas. Una configuración pendiente incompatible debe corregirse
antes del corte; no se cambian asientos ya producidos.

Pasaron 36 casos de borradores, rechazo antiguo, movimientos directos y tipo de
cuenta; otra ronda aprobó los 16 de pagos, cobros, anticipos y tarjetas. Los ocho
casos de fijación/resolución aprobaron también el snapshot inválido y el válido.
En la instancia aislada usada para la línea base, veinte muestras por volumen
dieron p95 de 519,6/797,6 ms para cien fuentes con una/cien fechas, un comando SQL
por lote. Dos intentos previos tomaron la instancia predeterminada y excedieron
el gate; no se aumentó el presupuesto ni se atribuye ese tiempo a un plan que no
usa la función modificada. No se altera ni detiene esa instancia del usuario.
Esta validación comparte el resolver canónico y no agrega I/O por partida.

La auditoría fiscal reprodujo además que el soporte manual tomaba la fecha del
intento como fecha de firma. El selector existente de tiempo de firma ya toma
`ManualAcquisition.IssuedAt`, igual que las adquisiciones asistidas. La prueba
falló únicamente para ese origen antes del arreglo y luego pasaron los catorce
casos del generador, conservando fechas de emisión/anulación en sus reintentos.

El generador de notas 95 admite el snapshot tipado de anulación de adquisición
manual, conservando su adquisición original, fecha propia y referencia fiscal.
Reutiliza la proyección de líneas, CUDS, XSD, firma y artefactos de las otras notas
de ajuste. Los snapshots anteriores omiten la propiedad nullable nueva. Pasaron
23 casos del generador: adquisiciones nativas/manuales, nota de gasto y manual,
fechas históricas y rechazo de originales de otro ámbito, identidad repetida,
fechas incompatibles, ausencia de líneas/referencia y doble origen. Esta prueba
del generador no habilita todavía la aceptación ni la interfaz de anulación;
faltan la validación y reserva transaccional de sus dependencias financieras.

#### Retenciones del soporte manual en el informe existente

La regresión reprodujo una retención manual guardada y contabilizada que no
aparecía en Retenciones practicadas. `WithholdingService.ListAppliedAsync` y su
store existentes ahora incluyen ese origen. Las cifras siguen saliendo de
`DocumentWithholdingSnapshots/Lines`; el proveedor y número provienen de su
snapshot/raíz fiscal. No hay otro saldo, cálculo ni informe paralelo.

La consulta filtra ámbito, período y origen sobre los hechos tributarios; calcula
conteo/sumas por separado y pagina antes de resolver proveedor, catálogos o JSON
fiscal. Conserva un comando SQL. Un origen incompleto produce un error explícito
en vez de entregar totales con filas ocultas. Los nombres de tipo y retención
vienen de los catálogos existentes en esa misma consulta; se retiraron sus mapas
quemados del cliente. Los campos añadidos son opcionales para lectores anteriores.

Con 10.000 soportes y veinte muestras aisladas por página, p95 fue 130,8/121,6 ms
en primera/siguiente página, bajo el presupuesto de 1,5 s y siempre un SQL.
Pasaron ámbito ajeno vacío, última página vacía sin perder totales y rechazo por
falta del snapshot de origen. La ronda de nueve casos aprobó también los informes
de compras y gastos anteriores y sus anulaciones. Compilaron API y Next/TypeScript;
lint enfocado y tres recorridos de navegador con HTTP simulado aprobaron. La vista
mostró el soporte y los nombres recibidos, con una carga al abrir y sin consultar
el informe oculto. Esta evidencia verifica el informe y no sustituye la regresión
concurrente de aceptación ni la auditoría completa del rediseño.

#### Orden transaccional del soporte y lectura del borrador

Una repetición concurrente reveló un interbloqueo real: el `LEFT JOIN` del
borrador sin enviar protegía el rango ausente de `AccountingSourceDocuments`,
mientras el otro envío retenía la protección de cupo y esperaba insertar allí.
Agregar una condición al join no impidió el probe del plan. La lectura canónica
ahora captura primero la única fila del borrador en un temporal de ese comando
y consulta la fuente únicamente dentro de una rama `IF` para borradores enviados.
Conserva una consulta de transporte y el replay existente. Además de las seis
columnas originales, devuelve la referencia capturada de esa misma fila para
distinguirla de la numeración fiscal asignada por el servidor.

La reserva de cupo existente precede las lecturas compartidas de configuración;
la numeración permanece después de la prueba. No se agregan locks propietarios,
reintentos, writer, tabla persistida ni reparación de fuentes. La reserva forma
parte de la misma transacción y se revierte con cualquier rechazo o fallo fiscal.

La regresión sincroniza ambos envíos después de leer sus borradores, usa el
mismo rango ausente del índice y auxiliares nuevos aún sin movimientos. Ambos
envíos terminaron contabilizados, cada uno con una deuda y raíz fiscal. Pasaron
49 casos de borradores, tesorería, gastos, rechazos/rollback/replay, grupos de
pago y compras de contado pagadas desde CxP y desde el editor. La revisión del
diff verificó el propietario y los viajes existentes; esta evidencia no cierra
la anulación manual, cargos posteriores ni la auditoría global pendiente.

La prueba de doble envío del mismo borrador reprodujo un conflicto incorrecto:
el segundo intento conservaba la referencia vacía anterior a la asignación del
folio. El replay reconoce exclusivamente ese valor generado por el servidor;
las referencias explícitas y el resto de la captura mantienen la comparación
estricta. Los dos intentos devuelven el resultado aceptado y dejan una sola fuente,
deuda, raíz fiscal, reserva de cupo e incremento del consecutivo. La ronda final
aprobó los 49 casos anteriores con esa sincronización adicional.

#### Anulación de un soporte manual: mismo asiento y obligación

El cuarto tipo usa la obligación existente para identificar el soporte original.
No agrega otro ID editable del original. La preparación autorizada requiere lectura
y captura manual, consulta una vez el original y entrega las partidas inversas de
su asiento inmutable, incluidos centros resueltos, tercero, auxiliar y bases con
signo inverso. El selector paginado de posiciones admite `ManualSupportOnly`;
ese filtro se aplica antes de conteo y paginación, junto con tercero, cuenta y
fechas. Los soportes de gastos y recepciones conservan sus propietarios de
anulación/devolución, porque pueden tener efectos operativos además del diario.

Preparar o revisar no acepta fuente, reserva cupo ni cambia saldos. La revisión
requiere el CUDS aceptado por DIAN, apertura conciliada y ausencia de pagos,
cruces, ajustes pendientes o asociaciones a compras. Usa el snapshot tributario
original, sin recalcular retenciones con reglas actuales. La fecha se compara
por el calendario colombiano mediante `DianFiscalDateTime`, también en el
generador; una hora distinta del mismo día no constituye una fecha anterior.

La aceptación comparte reserva de cupo, revisión y asignación de folio con el
participante fiscal existente. `AccountingSourcesAccept` deriva el destino de
anulación en `SourcePositionIndex`; su unicidad por obligación y la disponibilidad
canónica evitan dos anulaciones o un abono competidor. Purchasing protege el mismo
origen al asociar costos y deja de ofrecerlo cuando tiene una anulación aceptada.
No se añade un estado editable de anulación en el borrador original.

Solo `SqlAccountingPostingProcessor` cierra la CxP mediante
`PortfolioBalanceChangesSql(Cancellation)`, registra su crédito y contabiliza
las partidas inversas en una transacción. No abre otra deuda, anticipo o pago.
El participante fiscal inserta una nota 95 con referencia al original mediante
`InsertPurchaseSupportSourcesAsync`; generación, XSD, firma y reintentos conservan
su motor. No se vuelve a insertar una retención positiva. El informe tributario
reconoce la anulación efectivamente aplicada por su transacción y fecha; conserva
el período anterior cuando la anulación pertenece a una fecha posterior.

El editor y su selector existentes capturan la anulación. Las partidas preparadas
se presentan sin edición; cambiar fecha, referencia o motivo invalida la revisión
y exige revisar/guardar antes del envío. No se crea otra página ni un propietario
de carga para documentos ocultos. Presupuesto local: preparación en un SQL y
revisión en dos, independientes de las partidas (máximo 500), p95 de 1,5 s por
acción. Las comprobaciones de este incremento se describen a continuación;
la regresión y auditoría del diseño completo continúan pendientes.

La prueba de fallo del asiento deja la aceptación durable y la CxP sin aplicar.
Reprodujo que un aumento podía aceptarse mientras la anulación esperaba; se
corrigió agregando el indicador derivado `PendingCancellationCount` a la misma
disponibilidad, sin consulta ni saldo adicionales. Ese ajuste recibe conflicto
explícito, no crea fuente y el reintento de la anulación usa el mismo processor.
Pasaron 35 casos SQL/API (borradores, gastos y soporte), además de 65 pruebas
de normalización y generación. Preparar 2/4 partidas usó un SQL, p95 2,6/4,1 ms;
revisar usó dos, p95 56,2/83,4 ms (20 muestras aisladas). El informe con 10.000
soportes conservó un SQL, p95 262,8/217,9 ms para primera/siguiente página.

Una vez aceptadas sus raíces, la captura activa las señales de los coordinators
contable y fiscal independientemente; un fallo de una publicación o de un
processor in-process no omite la otra señal. Replay no publica ninguna de nuevo.
La comprobación final aprobó ambos recorridos SQL/API, incluido fallo del asiento,
señal fiscal única, reserva, conflicto del aumento, reintento y replay. La consulta
del original y su revisión exigen lectura contable y captura; se verificó rechazo
sin ese permiso y aislamiento de tenant. No crean una recuperación ni otro worker.

Compilaron el DACPAC Release, API y Next/TypeScript; lint de los cuatro archivos
de captura/selector/contrato/prueba pasó. Dos recorridos HTTP simulados de
escritorio y móvil comprobaron selector paginado, rechazo de dependencias,
partidas inversas sin edición, revisión, invalidación, guardado y envío; los once
anteriores del editor también pasaron. La inspección visual corrigió que la
cabecera de grilla se dibujara sobre Guardar/Contabilizar, en ese mismo editor.
Se comprobaron una carga de pendientes y del borrador, sin consulta tributaria
ni refetch tras guardar/enviar. Esto no sustituye el recorrido real del navegador
contra el servidor ni una aceptación externa DIAN.

La auditoría del diff de este incremento revisó contratos, servicio, participante,
aplicador, funciones/índice y selector: no hay otro motor, pago, writer de saldo,
tabla de anulación o página. La propuesta de partidas vive en la validación de
Application; SQL sólo resuelve sus hechos y el processor existente aplica los
efectos. Las consultas están acotadas al original o a la página. Los fallos
inyectados comprobaron rollback de folio/cupo/raíz/índice durante aceptación y
de saldo/historial/asiento durante posting. Se conserva la excepción y el estado
durable; no hay fallback contable, datos inventados ni reparación en replay.
Esta revisión es de este incremento y no declara auditado todo el diff global.

Orden de corte: esquema/funciones/índice y después API/worker/UI compatibles.
Para retroceder binarios se retira primero la captura nueva y se procesan las
anulaciones aceptadas con el worker compatible. No se borran fuentes, reservas,
folios ni movimientos ni se elimina el índice con datos para simular rollback.

### Auditoría integrada local del 26 de octubre

La revisión posterior del flujo confirmó un solo `accounting.PortfolioPaymentAccept`
y un solo `SqlAccountingPostingProcessor.ApplyPortfolioPaymentsAsync` para el pago
asistido y el egreso manual. La decisión de descuento del egreso se congela en la
fuente y su preparación de precio futuro usa `ProductLinkedCostsPrepare` dentro de
la misma transacción; no escribe el kardex ni revalora existencias. El rechazo no
deja pago aplicado. El escritor contable anterior se trasladó al módulo propietario
sin dejar una segunda implementación. Continúan únicos `DocumentProcessingEngine`
y `SqlInventoryLedgerWriter`. Las consultas modificadas paginan antes de enriquecer,
reutilizan la respuesta autoritativa y no añaden polling ni lecturas por partida.

Pasaron 139 casos SQL/API de la regresión financiera y de compras antes del último
ajuste de cuenta vigente, seguidos por los casos dirigidos del egreso manual y del
pago asistido con el código final. Pasaron 19 pruebas unitarias de validación,
10 casos de suscripción/cupo y contratos de motores, 7 pruebas de recuperación de
pedidos, 10 de pagos en navegador y 3 recorridos reales de compras y pedidos en
la base local aislada actualizada al DACPAC actual. La suite Foundation ejecutó
995 casos: 994 pasaron; detectó un selector nativo en el asistente de pagos.
Tras sustituirlo por el selector compartido, su prueba arquitectónica dirigida,
lint completo, TypeScript, compilaciones Release y la regresión de 10 pagos en
navegador pasaron. `git diff --check` y la verificación UTF-8 pasaron. Los
servicios locales responden en los puertos 3000 y 5097. No hubo despliegue remoto.

El gate de rendimiento **no quedó verde en esta ronda**. La asociación de costos
con 1/499 partidas superó su presupuesto p95 de 2 s en `.\LOCAL` y en la instancia
LocalDB exclusiva. Las pruebas de comprobante manual conservaron 7 comandos de
aceptación y 9 de posting, pero algunos p95 de aceptación/preview superaron 2 s;
la consulta de verificación de cierre agotó 30 s con 10.500 movimientos. La
medición anterior documentada en este diseño sí había pasado con el mismo número
acotado de viajes. Se conservaron los umbrales y no se cambió el motor por un
benchmark inestable. Antes de cualquier promoción debe repetirse el perfil con
carga de host controlada y resolver el gate si vuelve a fallar. El PUC de CJ
continúa sin cargar en DEV: sus 1.472 cuentas requieren el esquema nuevo y sigue
vigente la instrucción de no desplegar.

**Repetición aislada del 26 de octubre:** la regresión de tesorería directa con
10.500 movimientos y páginas de verificación aprobó completa en la instancia
LocalDB exclusiva con un límite temporal de 768 MB. Ese límite se devolvió a
384 MB al terminar. La regresión de asociación de costos, repetida en las
mismas condiciones, aprobó el origen de gasto, pero el origen de activo por
distribuir superó el presupuesto de 2 s en el procesamiento de 100 partidas:
p95 2.376,5 ms; aceptación p95 698,7 ms y consulta de una partida p95 4,3 ms.
Se mantuvo el gate y se devolvió el límite de memoria a 384 MB. El host tenía
menos de 1 GB de RAM libre durante estas repeticiones; no hay evidencia
suficiente para cambiar el motor contable o elevar el umbral por esta medición.

**Verificación final con carga controlada:** con la API y el frontend locales
detenidos durante el benchmark, la asociación de costos de origen activo pasó
en 1, 100 y 499 partidas, y los seis escenarios de pago manual pasaron,
incluidos 10.500 movimientos de sesión y 500 partidas. Se conservaron los
umbrales originales y se restauró el límite de LocalDB a 384 MB. Con los
servicios locales nuevamente activos, la suite Foundation pasó 995/995, la
regresión SQL/API 139/139, los recorridos reales desde navegador 8/8 con una
omisión por fixture histórica no configurada, y el build Release y DACPAC
compilaron. Los 21 casos de pedidos y 253 casos POS pasaron. La carga del PUC
de CJ y la verificación en DEV se registrarán tras su ejecución.

**Ajuste de captura manual del 26 de octubre:** elegir el tercero principal no
consulta sus documentos pendientes. «Ver documentos» muestra debajo una grilla
y consulta la primera página mediante el servicio canónico, con cuenta, tercero,
débito, crédito, saldo y abono editable. Solo se muestran saldos abiertos; las
compras, gastos y comprobantes por pagar aparecen junto con cartera y anticipos
del tercero en la vista inicial, sin tipo preseleccionado. El tipo se puede
filtrar y limpiar; la consulta conjunta exige tercero y pagina el conjunto
antes de enriquecer sus partidas. Cuando hay tercero, la consulta valida su
pertenencia al tenant una vez dentro del mismo comando; conteo y página usan
su identificador, y solo las filas de la página leen sus datos. Un bloque
«Filtros» cerrado por defecto reúne tipo de saldo, documento, cuenta y
vencimiento desde/hasta. El tipo distingue obligaciones, cartera y anticipos,
no el origen del comprobante. El encabezado muestra fecha y descripción en la
misma fila, sin campo de referencia; se conserva la referencia por partida y la
referencia histórica del contrato. El concepto interno requerido por el contrato
se fija desde la captura, sin selector visible.
Un borrador manual no enviado puede avanzar de la captura antigua sin versión
o versión 2 a la financiera versión 4 al elegir «Usar captura financiera»;
conserva la comprobación de versión de fila e identidades estables. No se
permite retroceder de versión ni cambiar una captura ya enviada.
La captura manual nueva guarda `WorkSessionId = null` y no participa en el cierre
operativo; pagos asistidos e históricos conservan su tratamiento de sesión.
Esta decisión sustituye únicamente las indicaciones anteriores que vinculaban
una sesión nueva desde la vista manual. El tercero principal es obligatorio al
contabilizar una captura financiera nueva (versión 4); los borradores pueden
guardarse incompletos. Cada auxiliar conserva por separado su regla de tercero
obligatorio por partida. No cambia el motor financiero, el asiento ni los saldos
de terceros.

La carga preparada del PUC se ensayó dentro de una transacción revertida contra
una base local con el esquema nuevo y sin asientos: las 1.472 cuentas y sus
1.463 relaciones padre se validaron; 40 códigos coincidían con cuentas ya
existentes y se insertarían 1.432. Ese ensayo no modificó la base de DEV.

Una lectura de DEV previa al despliegue confirmó que CJ conserva 91 cuentas,
ninguna partida contabilizada y ningún maestro bancario. De los 1.472 códigos
del PDF, 48 ya existen y ninguno contradice su tipo contable; la carga añadirá
los 1.424 códigos restantes cuando el esquema nuevo esté publicado. El PDF no
vincula cuentas con maestros bancarios inexistentes y no se inventan esas
relaciones durante la importación.

**Estado de CJ en DEV al 27 de octubre:** tras publicar el esquema compatible,
se cargaron los 1.472 códigos del PDF según la regla aprobada de Código/Cuenta
NIC. La revisión posterior detectó colisiones entre códigos del PUC real y
cuentas genéricas sugeridas por el perfil: la categoría podía conservar el
tipo correcto mientras el nombre de la cuenta expresaba otro uso. Se
reasignaron, mediante la API de configuración contable, salud patronal a
`237005`, pensión patronal a `238030`, parafiscales a `237010`, fletes a
`52355002`, seguros de compra a `51306001`, manejo logístico a `51357002`,
sobrantes de transportadores a `429597`, diferencias de inventario a `539595`,
gastos operativos y otros costos directos a `519595`, y otras salidas a
`52951004`. Se añadió el auxiliar `51059503` bajo la agrupación `510595` para
aportes patronales y se le asignó esa categoría. Los 63 propósitos requeridos
tienen un auxiliar activo de la naturaleza correcta; `SupplierDebitCard` y
`SupplierCreditCard` siguen opcionales y sin asignación porque CJ no tiene
maestros bancarios configurados. No se inventó banco ni cupo de tarjeta.
La contabilidad de CJ en DEV permanece desactivada y su balance de prueba está
vacío. La comprobación de apertura en cero rechaza saldos comerciales y
operaciones pendientes; la modalidad de apertura importada requiere saldos
iniciales aprobados. No se deduce que la situación de PROD sea idéntica a DEV
ni se activa contabilidad por la ausencia de asientos previos.
