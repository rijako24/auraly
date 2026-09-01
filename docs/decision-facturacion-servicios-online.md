# Decisión: facturación de servicios exclusivamente online

**Estado:** aprobada
**Fecha:** 30 de agosto de 2026
**Alcance:** facturas de servicios de Auraly y futuros servicios facturables sin efecto físico

## 1. Objetivo y frontera de seguridad

La facturación de servicios se implementa como un flujo web independiente. No amplía el catálogo de productos ni convierte inventario en un catálogo genérico. Reutiliza `SalesDocuments` como encabezado canónico, sin crear otra raíz de factura; permanecen separados `SalesDocumentLines` para producto y `SalesDocumentServiceLines` para servicio. POS Edge, sincronización de cajas, bodegas, disponibilidad, kardex, valoración, despachos y devoluciones físicas de producto conservan su ruta actual.

La tabla `Services` existente sigue siendo el catálogo de agenda, duración, empleados y reservas. No es un catálogo fiscal. El nuevo catálogo `BillableServices` pertenece a ventas online y conserva código, nombre, descripción, unidad UBL, perfil tributario, moneda, precio y vigencia.

## 2. Modelo documental

El agregado transaccional consta de:

- `SalesDocuments`: encabezado común para factura de producto, comprobante y factura de servicio. `DocumentType=ServiceInvoice` exige emisión `Online`, factura fiscal y ausencia de bodega, dispositivo y sesión de caja;
- `SalesDocumentServiceLines`: número de línea, `BillableServiceId`, snapshots de código, descripción, unidad, cantidad, precio, descuento, impuestos, total y detalle informativo;
- `SalesDocumentLines` continúa siendo exclusivamente el detalle de producto, con `ProductId`, costo y efectos físicos intactos;
- `TenantSubscriptionInvoiceLinks` enlaza la factura común con suscripción, orden y pago sin contaminar el encabezado de ventas con columnas de billing;
- el detalle informativo de una suscripción congela plan, usuarios completos, vendedores, cajas, documentos DIAN, empleados de nómina, periodicidad y periodo. No se reconstruye leyendo la suscripción vigente.

En un encabezado `ServiceInvoice`, `WarehouseId`, `DeviceId` y `WorkSessionId` son nulos por restricción; su detalle no tiene `ProductId`, costo, proveedor ni disposición física. Una factura no puede mezclar líneas de producto y servicio en el primer corte. El API rechaza esa mezcla y cualquier intento de enviar una factura de servicio por contratos POS.

El tipo fuente interno es `ServiceInvoice`. Ante la DIAN el documento continúa siendo factura electrónica de venta, `FiscalDocumentType=Invoice` y código UBL/DIAN `01`. `S`, `SV` o nombres similares no son nuevos tipos fiscales. Una serie online puede tener un prefijo comercial diferenciado y una autorización fiscal dedicada únicamente cuando ese prefijo y rango hayan sido devueltos o aprobados por la DIAN. Sin autorización válida no se emite factura electrónica.

## 3. Enrutamiento a los cuatro motores

La confirmación online valida tenant, negocio, cliente, perfil tributario, totales, resolución libre/vigente y consecutivo mediante el propietario canónico de series. En una única transacción serializable persiste el documento y sus snapshots y crea exactamente una solicitud durable por cada motor aplicable.

| Motor | ¿Participa? | Contrato y efecto |
| --- | --- | --- |
| Operación/inventario | No | No existe `DocumentProcessingJob`, mensaje `auraly-document-processing`, kardex, costo, saldo, despacho ni outbox a POS. |
| Contabilidad | Sí | `AccountingSourceDocuments` y el único `AccountingPostingJob` reciben `ServiceInvoice`; causan ingreso por servicios, IVA/retenciones, recaudo o CxC y asiento balanceado con cuentas configuradas. |
| Fiscal | Sí | `FiscalDocuments.SourceDocumentType=ServiceInvoice`, snapshot UBL tipado, CUFE/QR, generación, firma, envío, consulta, artefactos y entrega usan los workers existentes. |
| Reporting | Sí | El único `SalesReportingJobs` recibe `ServiceInvoice`; proyecta hechos de servicio, impuestos, pagos y totales sin costo de inventario. |

No se crea una quinta cola, otro worker fiscal, otro libro contable ni otra cartera. La API transaccional puede publicar directamente a contabilidad, fiscal y reporting porque no existe un efecto físico previo que deba confirmarse. Cada mensaje conserva `BusinessId` como clave de orden y una identidad/hash estable; la entrega duplicada es idempotente.

## 4. Contabilidad y cartera

El procesador contable existente agrega la política `ServiceInvoice`. El perfil contable del servicio resuelve ingreso, impuesto, retenciones y contraparte. Si la factura es de contado y ya existe un pago de suscripción, reutiliza ese pago y no crea una segunda CxC. Si es a crédito, materializa la única `Receivable` y su vencimiento mediante el mismo submayor.

La FK histórica de `Receivables` hacia `DocumentProcessingJobs` contradice la arquitectura vigente para documentos sin efecto físico. La migración debe llevar la integridad de la fuente financiera al par canónico `AccountingPostingJobs(SourceDocumentId, SourceDocumentType)` o a `AccountingSourceDocuments`, con cutover y prueba de compatibilidad para facturas de producto existentes. No se crea un job operacional ficticio para satisfacer esa FK.

## 5. DIAN, numeración y correcciones

La pantalla permite seleccionar solamente series online de tipo interno `ServiceInvoice` que estén vinculadas a una autorización DIAN vigente y con saldo. La asignación y el consumo son serializables; no se usa `MAX+1`. El documento fiscal común recibe `SourceDocumentType=ServiceInvoice` y `FiscalDocumentType=Invoice`; el generador UBL extiende su lector de snapshot para líneas de servicio, conservando el mismo UBL 2.1, CUFE, firma, ZIP, transporte y estados.

`AuralyDocumentTypes` sigue siendo el único catálogo de numeración interna y asigna `ServiceInvoice → FSV`. El contrato de ventas referencia esa constante; no mantiene otra regla de prefijos ni usa el prefijo de producto `VTA`.

Una corrección económica usa las notas crédito/débito fiscales existentes con una referencia tipada a la factura y línea de servicio originales. Una factura emitida no se edita, elimina ni vuelve a borrador. La entrada canónica es la pantalla existente `Devoluciones de venta`, que busca facturas de producto y de servicio y crea una devolución/nota crédito total o parcial prellenada. El detalle de una factura puede conservar un enlace de conveniencia hacia esa pantalla, pero no es obligatorio iniciar allí.

No se crea otra pantalla ni otro encabezado de devoluciones. `SalesReturns` es el tronco común para producto y servicio: conserva identidad, negocio, numeración, idempotencia, fecha, alcance total/parcial, resolución económica, motivo, cliente, importes, estados, actor y auditoría. No conserva `WarehouseId`: en producto la bodega fuente se obtiene de la factura original inmutable y en servicio no existe. El API deja de aceptar una bodega elegida por el navegador para este caso.

El motor de producto usa la bodega de `SalesDocuments` como destino de reingreso según la política vigente. Si en el futuro se permite devolver físicamente en una bodega diferente, esa selección pertenecerá a un contexto/efecto de inventario exclusivo de producto, con autorización y traslado cuando aplique; no volverá al encabezado común. La migración audita primero que las devoluciones históricas tengan la misma bodega que su factura. Cualquier excepción real se preserva en un contexto físico de producto antes de retirar la columna; no se pierde ni se reinterpreta.

Como ambos orígenes viven en `SalesDocuments`, `SalesReturns.OriginalDocumentId` continúa siendo una FK íntegra y única. El servidor lee `SalesDocuments.DocumentType` para seleccionar el adaptador de producto o servicio; no confía en un discriminador enviado por el navegador ni necesita dos FK opcionales.

No existe una tabla genérica de detalles ni columnas `ItemType`, `ProductId`/`ServiceId` opcionales. `SalesReturnLines` continúa siendo la tabla actual exclusivamente para productos, sin relajar sus FK, costo o disposición. Se agrega `SalesServiceReturnLines` exclusivamente para servicios, con FK a `SalesDocumentServiceLines` y columnas propias de servicio, unidad, descripción, cantidad, precio, descuento, impuesto y total; no contiene `ProductId`, costo ni disposición. Las FK compuestas contra el encabezado impiden enlazar una línea de servicio con el detalle de producto.

`SalesReturnSettlements` también permanece como tronco económico común. El efectivo es un destino independiente del medio original y afecta la sesión de trabajo activa. Una transferencia exige una cuenta activa del maestro bancario enlazada a un auxiliar postable del PUC; el procesador contable acredita ese auxiliar, no un mapeo genérico. Una devolución a tarjeta es una reversión: referencia obligatoriamente el pago original de la misma tarjeta, copia su franquicia y aprobación como evidencia inmutable y no puede superar el saldo aún no reversado. `CustomerCredit` no referencia un pago: solo aplica hasta el saldo real de la cuenta por cobrar y reutiliza `SalesReturnReceivableApplications`; cualquier valor adicional se registra en otra devolución con otro destino. Los datos históricos anteriores al cutover siguen siendo legibles, pero las operaciones nuevas obedecen estas reglas.

`SalesReturnWorkspace` permanece como presentación común y conserva permisos, filtros, motivos, resolución económica, aplicaciones de pago/CxC, numeración de nota, CUDE, DIAN e historial. Debajo usa `ProductSaleReturnSource` y `ServiceInvoiceReturnSource`. El segundo no ejecuta cantidades físicas, bodega, disposición, kardex ni costo.

El correo se envía únicamente después de `DianAccepted`; sin correo válido no se crea entrega ni reintento futuro. La impresión usa la representación gráfica profesional del artefacto fiscal del mismo `DocumentId` y CUFE. Antes de emitir solo existe vista previa marcada como borrador; nunca se imprime un borrador como factura fiscal válida.

La configuración local `Periféricos` agrega un tercer flujo, `Facturas de servicios`, además de `Punto de venta` y `Facturas desde pedidos`. Reutiliza los formatos ya soportados por el renderer canónico: `Receipt` (tirilla), `HalfLetter` (media carta), `HalfLegal` (media oficio) y `Letter` (carta). Para tirilla permite 58 u 80 mm. Cada computador conserva independientemente `ServiceInvoicesOutputFormat`, `ServiceInvoicesPrinterName` y `ServiceInvoicesReceiptPaperWidthMillimeters`; cambiar servicios no modifica POS ni pedidos.

Con Auraly instalado, `Periféricos` enumera las impresoras de Windows, exige seleccionar una para el flujo y permite `Imprimir prueba`; reimprimir desde el historial usa esa misma ruta. En navegador puro se conserva el formato, pero la aplicación no puede elegir silenciosamente una impresora del sistema: abre el diálogo seguro del navegador. La interfaz lo explica y no promete impresión directa sin el componente local.

El contrato actual de impresión se versiona sin crear otra configuración paralela. La migración conserva POS y pedidos y establece media carta como valor inicial para servicios. Las rutas se resuelven por flujo (`Pos`, `OrderInvoices`, `ServiceInvoices`) y no solo por `SalesInvoice`, porque POS y pedidos ya imprimen el mismo tipo documental con configuraciones diferentes. El host local recibe el DTO imprimible sin pasarlo por sincronización, inventario ni cola operacional.

## 6. Reporting: cambios necesarios

Sí hay que tocar reporting, pero no los reportes de inventario ni la proyección de líneas de producto.

`SalesReportLineFacts.ProductId` es obligatorio y sus métricas incluyen costo reconocido, proveedor y categoría de producto; insertar servicios allí obligaría a inventar un producto o costo. Por eso permanece intacta. Se agrega el nuevo grano `reporting.ServiceSalesReportLineFacts`, con servicio, cantidad, base, descuento, impuesto, total, cliente y vendedor, pero sin producto, proveedor, categoría, bodega ni costo de inventario.

Se reutilizan `SalesReportingJobs`, `SalesReportDocuments`, hechos de impuestos y pagos y los acumulados diarios. `SalesReportDocuments` debe admitir `ServiceInvoice` y bodega opcional/una dimensión de canal que no invente una bodega. Los totales generales de ventas, impuestos, recaudo, cliente y vendedor incluyen productos y servicios y permiten filtrar `Product`, `Service` o `All`.

Los informes cambian así:

- **Hoy / Ventas / impuestos / recaudo / clientes / vendedores:** incluyen servicios y ofrecen filtro de origen;
- **Servicios:** nueva vista alimentada por `ServiceSalesReportLineFacts`, con facturas, unidades, ingreso, descuentos, impuestos y recaudo;
- **Productos, categorías, proveedores, inventario, compras, despachos y rotación:** no cambian y excluyen servicios por construcción;
- **Costo y utilidad bruta:** los servicios no se presentan con margen artificial del 100 %. Mientras no exista costo directo de servicio configurado, muestran ingreso y `Costo no modelado`; no entran al ranking de margen de producto.

El procesador y rebuild del único motor de reporting agregan el tipo fuente `ServiceInvoice`. Una reconstrucción produce exactamente los mismos hechos y acumulados que el procesamiento incremental. No se crea una tabla por pantalla; la tabla nueva se justifica por un grano distinto al producto.

## 7. Experiencia y permisos

La ruta `Facturación > Servicios` es web y requiere permisos propios `service-invoices.read` y `service-invoices.create`. No es una copia completa del POS. Reutiliza sus patrones maduros de búsqueda, edición de línea, desglose de totales, autorización de acciones sensibles, confirmación e impresión, pero elimina toda acción física o de caja.

### Flujo de captura

1. La pantalla abre un borrador vacío con fecha, tipo de documento y serie online elegible.
2. El usuario busca o crea el cliente; al seleccionarlo se cargan identificación, correo de facturación, términos de pago, perfil tributario y retenciones.
3. `Agregar servicio` abre un buscador grande y accesible con búsqueda por código/nombre, resultados recientes y precio visible. No existe lector, captura por código de barras ni catálogo descargado.
4. Cada línea permite cambiar descripción visible, cantidad, precio cuando el permiso lo autorice, descuento en porcentaje o valor y eliminar. Impuesto, retención y totales se recalculan en servidor desde el servicio, cliente, fecha y reglas vigentes; el navegador no es autoridad.
5. Un resumen fijo muestra subtotal, descuentos, impuestos, retenciones, total, forma/condición de pago y vencimiento. En móvil se convierte en una barra inferior que abre el desglose.
6. `Revisar y emitir` muestra cliente, correo, serie/prefijo, líneas, impuestos, total y condición de pago. La confirmación final usa idempotencia y solo entonces consume consecutivo.
7. El resultado muestra número, estado DIAN, impresión/descarga y envío cuando corresponda. Puede enlazar a `Devoluciones de venta` con la factura preseleccionada, pero el flujo completo también comienza directamente desde esa pantalla. `Imprimir` y `Reimprimir` resuelven el flujo `ServiceInvoices` configurado en `Periféricos`; el formato no se pregunta en cada factura.

En escritorio se usa una composición de dos columnas —captura amplia y resumen fijo—; en teléfono el flujo es lineal, con objetivos táctiles de al menos 44 px, teclado numérico para cantidades/importes, foco devuelto al buscador después de agregar y errores junto al campo además del resumen. Búsqueda, cliente y emisión funcionan enteramente por teclado, pero no se agrega un atajo `Ctrl+S` oculto.

### Acciones expresamente excluidas

- pausar venta o administrar ventas temporales;
- tomar/importar un pedido;
- guardar como pedido;
- lector de código de barras, balanza o periféricos operativos distintos de la impresora configurada;
- disponibilidad, bodega, traslado, despacho o resolución de inventario;
- apertura/cierre de caja, arqueo, cajón o sesión de trabajo;
- operación offline, descarga o sincronización con POS Edge.

Puede existir autoguardado técnico del borrador para recuperar una caída del navegador, pero no aparece como “Pausar venta”, no reserva numeración y no crea una bandeja paralela de temporales.

### Historial, impresión y devolución

La misma sección contiene un historial paginado con filtros por cliente, número, fecha, estado DIAN, estado de pago y servicio. El detalle ofrece `Imprimir/descargar`, `Enviar` únicamente si hay correo válido y un acceso opcional a la pantalla común de devoluciones.

`Devoluciones de venta` sigue siendo la única bandeja. Su consulta devuelve un discriminador `ProductSale` o `ServiceInvoice`; permite buscar por factura, CUFE, cliente, producto o servicio y agrega un filtro `Todas / Productos / Servicios`. Para producto muestra la bodega derivada de la factura como información no editable y conserva reingreso, disposición, costo y sesión de caja. Para servicio muestra `Servicio` en lugar de `Producto`, oculta bodega/disposición/reingreso y resuelve el reembolso desde el pago o la CxC de servicio, no desde `SalesPayments` ni una sesión de caja. Después de confirmar crea la nota crédito correspondiente; nunca edita la factura original.

La detección del origen se hace exclusivamente en servidor con `OriginalDocumentId + BusinessId`: no se confía en un tipo enviado por el navegador. Si `SalesDocuments.DocumentType=ServiceInvoice`, el lector carga `SalesDocumentServiceLines` y devuelve al editor los snapshots originales de servicio, descripción, unidad, cantidad, precio, descuento, impuesto, total y cantidad/valor ya acreditados. El usuario selecciona el servicio y la cantidad o alcance económico por devolver; los límites se calculan contra notas crédito anteriores bajo bloqueo serializable para que dos devoluciones concurrentes no excedan el saldo.

La confirmación persiste el mismo encabezado `SalesReturns`, conserva `OriginalDocumentId` y agrega sus filas en `SalesServiceReturnLines`. No crea una raíz paralela `ServiceCreditNote`. Desde esa fuente inmutable crea exactamente un trabajo para cada motor derivado aplicable: contabilidad revierte ingreso, impuestos y pago/CxC; fiscal genera y transmite la nota crédito electrónica referenciada; reporting resta ingreso, impuestos y recaudo del hecho de servicio. No crea `DocumentProcessingJob`, `InventoryMovements`, costo reconocido, movimiento de bodega, disposición, despacho, sesión de caja ni sincronización POS. Por tanto, su único efecto económico propietario es contable, aunque fiscal y reporting deban reflejar legal y analíticamente la corrección.

Esto exige una extensión real aunque la pantalla y el encabezado sean los mismos: actualmente la vista, DTO, FK y SQL reciben `WarehouseId` del navegador y suponen `SalesDocuments`, `ProductId`, `SalesDocumentLines`, `SalesPayments`, disposición de inventario y devolución en efectivo mediante sesión abierta. El servidor pasa a derivar la bodega del documento original; las demás condiciones permanecen en el detalle/adaptador de productos y no se vuelven opcionales globalmente. El detalle/adaptador de servicios usa sus tablas propietarias y entrega un view model común; así no se debilitan las validaciones físicas que ya funcionan.

Las acciones sensibles tienen permisos independientes: `service-invoices.price.override`, `service-invoices.discount`, `service-invoices.issue`, `service-invoices.print` y `sales-returns.create`. El servidor los exige aunque el botón esté oculto. El tenant y negocio se resuelven desde la identidad autenticada y no desde identificadores confiados al navegador.

Este enfoque coincide con patrones observados en facturadores online: selección de cliente que carga condiciones fiscales, líneas de producto/servicio editables, descuento explícito, confirmación/envío/impresión y corrección desde la factura mediante nota crédito. Se evita trasladar al flujo de servicios la complejidad de un POS físico.

## 8. Pruebas y puerta de liberación

Son obligatorias:

1. factura de producto antes/después con el mismo payload produce idénticos movimientos, costo, fiscal, contabilidad y reporting;
2. factura de servicio crea cero `DocumentProcessingJobs`, `InventoryMovements`, movimientos de sesión, despachos y mensajes POS;
3. servicio crea un solo job contable, fiscal y de reporting, incluso con reintentos concurrentes;
4. asiento balanceado y CxC/pago único; no se duplica el recaudo de suscripción;
5. CUFE, UBL `01`, autorización, rango, vigencia, firma y envío DIAN por el motor existente;
6. un prefijo no autorizado o agotado bloquea antes de numerar;
7. reporting general concilia producto + servicio y los informes físicos permanecen idénticos;
8. rebuild de reporting concilia con incremental y no inventa costo/margen;
9. aislamiento por tenant/negocio y permisos de lectura/creación;
10. UI probada online en escritorio y teléfono; sin conexión no confirma ni sincroniza;
11. la única pantalla de devoluciones lista productos y servicios, y devolución total/parcial de servicio reutiliza las reglas económicas/fiscales y crea nota crédito sin alterar inventario;
12. sin correo válido no existe entrega, intento ni tarea posterior;
13. no aparecen ni son invocables por API pausa, temporales, pedido, lector, bodega, caja o periféricos;
14. editar precio/descuento/eliminar y emitir valida permisos en servidor y recalcula totales sin confiar en el navegador;
15. impresión corresponde al artefacto fiscal emitido; la vista previa de borrador está marcada y no consume numeración.
16. tirilla, media carta, media oficio y carta renderizan sin recortes y conservan CUFE, QR, impuestos y totales; tirilla cubre 58 y 80 mm;
17. cambiar impresora o formato de servicios no altera POS ni pedidos, y reimpresión usa la configuración vigente del computador;
18. sin host local nunca se selecciona o imprime silenciosamente en una impresora; con host local se valida la impresora instalada y la prueba de impresión.
19. la matriz actual de devoluciones de producto conserva la bodega derivada de la factura, disposición, costo, sesión y movimientos sin aceptar sustitución desde el navegador;
20. búsqueda y filtros de la pantalla común aíslan tenant/negocio y una factura de servicio no puede llegar al handler operacional de devoluciones físicas.
21. el servidor detecta `ServiceInvoice` sin confiar en el navegador, carga sus servicios y saldo acreditable, y dos devoluciones concurrentes no exceden cantidad ni valor disponible;
22. `SalesReturns` cuyo `OriginalDocumentId` apunta a un `SalesDocuments(ServiceInvoice)` crea una sola reversión contable, nota fiscal y proyección negativa, y cero efectos de inventario/operación;
23. el cutover conserva todas las devoluciones históricas como producto, audita diferencias de bodega antes de retirar la columna común y las restricciones impiden dos orígenes o detalles de ambos tipos.

No se despliega mientras cualquiera de estas pruebas falle o mientras no exista una resolución DIAN real compatible para validar el escenario externo correspondiente.
