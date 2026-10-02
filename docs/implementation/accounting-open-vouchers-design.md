# Comprobantes abiertos en Trazabilidad financiera

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

En Cuentas por cobrar y por pagar, el encabezado ofrece **Ajuste de cartera** al usuario con permiso de captura manual. Abre el editor completo en un modal de la misma vista: primero se elige el tercero con el combo paginado y luego una factura u obligación de ese tercero. Los campos del ajuste permanecen bloqueados hasta cargar la obligación. El detalle de una factura abre el mismo modal con tercero y obligación seleccionados, mostrando número, valor original, saldo y vencimiento antes de editar el importe. Los indicadores de saldo pendiente, saldo vencido y número de facturas/obligaciones siguen visibles al alternar entre terceros, facturas y pagos, con una única consulta de resumen por filtros.

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
- Los ajustes se lanzan en CxC/CxP sobre una obligación concreta. La aceptación serializa por obligación y descuenta pagos aceptados y disminuciones manuales aún no aplicadas. La reserva consulta el índice filtrado de enviados y el auxiliar canónico, sin tomar el job mientras se bloquea la obligación; aplicar/reintentar sigue perteneciendo al processor contable.
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
