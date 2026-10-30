# Órdenes y recepciones de compra

> **Decisión final del usuario (2026-10-29):** costos, descuentos y comprobantes
> asociados se editan exclusivamente en el borrador de recepción. Después de
> confirmar, ninguna recepción admite cambiar esos valores ni agregar nuevos
> comprobantes, cualquiera que sea su tipo de soporte fiscal. Los trabajos de
> revisión ya aceptados conservan su procesamiento y lectura histórica; se
> retira la creación de revisiones posteriores.

Fecha de actualización: 2026-10-29

> **Ampliación histórica 2026-10-20:** la sección final define asociaciones de comprobantes
> existentes, reparto parcial entre recepciones y cargos posteriores. Su opción
> de crear cargos después de confirmar quedó sustituida por la decisión final anterior. Sustituye
> para el destino los límites de cada cargo ligado a una sola recepción y de captura únicamente
> antes de confirmar. El resto describe el alcance anterior. Esta ampliación es
> diseño; la aclaración final admite el ajuste contable de valoración sin cambiar
> la factura ni su deuda. El usuario aprobó el 22 de octubre atribuir salidas a
> las entradas más antiguas primero; el writer ya registra esa atribución.
> Los trabajos de revisión de cargos distribuidos ya aceptados se procesan por el motor documental,
> ajusta el valor de las existencias trazables por el writer de inventario y
> genera su reclasificación por el motor contable. Las recepciones antiguas sin
> origen trazable no se revalorizan automáticamente; no se añade un proceso de
> conciliación histórica a este alcance.
> La aclaración posterior del mismo día conserva **Agregar factura** como captura
> asistida y añade **Agregar comprobante**. El pago se separa de ambas capturas.

## Descuentos de compra — decisión del 2026-10-21

La ampliación conserva la recepción, su inventario, la preparación de precios y
su publicación explícita. El descuento comercial porcentual pertenece a cada
línea. Se calcula sobre cantidad por costo unitario, antes del IVA; el importe
resultante queda congelado junto con el porcentaje. Las líneas históricas y los
clientes que solo envían `DiscountAmount` conservan su aritmética anterior.

El descuento financiero por pronto pago pertenece al documento de mercancía:
porcentaje, base elegible después del descuento comercial (sin impuestos ni
cargos adicionales), fecha pactada informativa y opción explícita de anticiparlo
en el costo. Su reparto por producto se congela en la recepción. Anticiparlo
afecta la medición neta de inventario y obligación, conservando el importe del
soporte original y una conciliación visible. El IVA del descuento condicionado
conserva su base: no reutilizar el campo de descuento comercial para reducirlo.

**La obtención o pérdida nunca se decide por fechas ni por completar el pago.**
El usuario confirma en el pago el descuento realmente obtenido, incluido cero
si se perdió. Puede dejar la decisión pendiente al registrar un abono. La fecha
del pago capturado puede diferir de la fecha real de la operación: no rechazar,
conceder, perder ni contabilizar un descuento automáticamente por el calendario.
No se crea agenda, worker ni proceso automático de vencimiento para esta regla.

El motor financiero existente reconoce una sola vez la diferencia entre el
descuento anticipado y el confirmado. La decisión expresa del usuario al pagar
puede obtenerlo o perderlo, independientemente de la fecha registrada. Para un
descuento no anticipado que se obtiene, el abono extingue la obligación por su
importe completo: una parte sale del medio de pago y la otra se reconoce en el
auxiliar configurado para descuentos financieros obtenidos. Si se pierde un
descuento anticipado, la diferencia se reconoce como gasto financiero. La
confirmación prepara solamente el costo y el precio de ventas futuras por el
propietario de precios existente. **No revaloriza existencias, no modifica el
kardex ni cambia ventas, conversiones o producción anteriores.** El valor
contable de la recepción y de su factura original permanece congelado. Las
devoluciones referencian la compra y reducen la base elegible. Si ocurren
después de decidir el descuento, su saldo a favor usa el descuento realmente
obtenido y el asiento revierte la porción correspondiente de ingreso o gasto
financiero. Los pagos asistidos y los comprobantes manuales comparten validación,
reservas, aplicaciones y posting para sus pagos ordinarios. La decisión expresa
del descuento se captura en el pago asistido y se congela en su fuente.

Estado de implementación del 2026-10-26: el descuento comercial porcentual está
conectado a captura, borrador, confirmación, persistencia y consulta. Conserva el
importe anterior y la firma v1 de reintentos sin porcentaje; una modificación de
porcentaje en una recepción aceptada produce conflicto. La captura del descuento
financiero, su reparto congelado, la opción de anticiparlo, el borrador y el asiento
inicial están implementados y verificados localmente. La devolución de una compra
con descuento anticipado reduce el abono al proveedor por la porción devuelta y
conserva ese importe en cada línea. La decisión real al pagar, incluida la base
elegible después de devoluciones, la pérdida de un descuento anticipado y la
devolución posterior al pago pasaron sus pruebas SQL/API locales el 2026-10-26.
La preparación afecta solo precios futuros y deja intacta la valoración del
inventario. El egreso manual v4 también captura la decisión expresa en la partida
de la obligación. El servidor calcula y congela el ajuste, exige la contrapartida
en la cuenta vigente del PUC y envía el pago al mismo aceptador y aplicador de
cartera usados por la captura asistida. La vista consulta la cuenta según la fecha
del egreso; si la configuración de la sede es inválida, no sustituye en silencio
una cuenta global. Una decisión rechazada no registra pago. Pasaron 19 pruebas
unitarias de validación, dos casos SQL/API de descuento manual (obtenido y
anticipado perdido) y una prueba de navegador de la captura; el valor del
inventario no cambió. Esta evidencia no autoriza su publicación en producción.

La regresión integrada final del 26 de octubre pasó 111/111 escenarios de
recepciones, gastos, pagos a proveedor, cartera y devoluciones. Cuatro recorridos
de navegador contra API y SQL locales pasaron: comprobante libre con clonación,
gasto con abono, asociación de comprobante a compra procesada sin cambiar la
deuda, y edición y facturación de un pedido. El pago con decisión de descuento
pasó además sus dos variantes con replay y devolución posterior, comprobando
que no genera movimiento ni cambio de valor de inventario. La base aislada de
pruebas se actualizó con el DACPAC local y ambos servidores quedaron escuchando
en 127.0.0.1:3000 y 127.0.0.1:5097; no se hizo despliegue remoto.
Un pago con descuento ya decidido no admite la desaplicación ordinaria: esa
acción solo devuelve dinero y dejaría vigente su ingreso o gasto financiero.
La preparación y el aplicador rechazan explícitamente la operación sin cambiar
el pago ni la obligación; ninguna reversión parcial se infiere del código PUC.
La regresión dirigida de esa protección y de ambos pagos con descuento pasó
8/8 casos después de recompilar; el inicio de sesión local volvió a responder
200 con la API y la vista encendidas.

La regla nueva de generar obligación se valida solamente al aceptar una compra
nueva, después de resolver su identidad y firma de reintento. Incluye los
documentos adicionales. Una recepción aceptada por el binario anterior puede
conservar `CreatesPayable=false` en su cabecera o en un cargo de contado: repetir
su misma solicitud debe devolver la aceptación original, sin convertirla en una
obligación nueva ni rechazarla por esa regla posterior. El calculador conserva la
semántica histórica necesaria para comprobar la firma; no decide si se admite
una nueva omisión de obligación. Las recepciones aceptadas antes de esta regla
conservan su resultado original al reintentar. La regresión correspondiente incluye
cargo antiguo de contado, firma diferente y rechazo sin escrituras de una compra nueva.
Verificación local del 21 de octubre: los dos escenarios (cabecera antigua de
contado o a crédito, con cargo antiguo de contado) fallaron con el binario
anterior y pasaron con la validación posterior al replay. La compilación API
Release pasó. La corrida SQL/API de compras aprobó 46 de 49 casos; tres escenarios
de documento soporte agotaron la espera en la consulta de asignación fiscal.
SQL indicó memoria física baja. Los tres escenarios fiscales pasaron después
en una repetición aislada; no se amplió su timeout ni se redujeron las pruebas.
Esa repetición aprobó 25 de 28 casos y aún dejó tres correcciones de pagos en
cierres con timeout SQL. La recuperación de captura en navegador pasó con HTTP
simulado, tanto con productos (conserva importe y guarda la obligación nueva)
como sin productos (conserva cargos y rechaza confirmar antes de enviarlos).
También pasaron el resumen/reporte de compra y el ciclo de guardar, editar,
enviar y clonar comprobantes. Los 250 tests unitarios de interfaz y el build
frontend pasaron; esto no declara terminada la regresión global ni sustituye
los recorridos de navegador contra el backend real.

La regresión posterior del asignador compartido aprobó 193 de 199 casos; sus
cuatro fallos fiscales agotaron la espera en la misma selección por lote de
documento soporte, utilizada también por Gastos. La consulta ahora materializa
solo las solicitudes tipadas del lote antes de unir resolución, emisor y tercero,
y recompila esa selección con la cardinalidad real. Conserva ordinal, fecha,
alcance, bloqueos y reserva agrupada de consecutivos en el propietario existente.
El parámetro JSON tiene tamaño estable. La regresión de dos fechas/resoluciones
se amplía a dos y diez cargos, verificando todos los números en una lectura por
lote. El build Release pasó y la repetición SQL/API aprobó los 40 casos de
recepción, recuperación de borradores y gastos, incluidos los cuatro casos
fiscales anteriores y ambos tamaños del lote. La selección fiscal hace una
sola consulta por captura. Estos resultados no sustituyen la regresión global
ni prueban por sí solos el comportamiento bajo toda condición de memoria.
La reversión de una aceptación fallida tiene una sola salida y conserva el error
original si SQL cerró la conexión. Los conflictos de unicidad mantienen su
mensaje y excepción interna; no cambian reservas, reintentos ni efectos de compra.

El handler de recepción también agrupa la aceptación financiera de la cabecera
y los cargos mediante `SqlAccountingPostingJobWriter.InsertSourcesAsync`, dentro
de su transacción operacional. Conserva una fuente y trabajo por comprobante;
no crea un aceptador ni un procesador alterno. Cada grupo conserva el requisito
financiero histórico, incluido `CreatesPayable=false` en fuentes antiguas, y se
divide en lotes del máximo de 100 que admite el escritor canónico. Las retenciones
de esos documentos se guardan por lotes de hasta 100 documentos, sin imponer un
nuevo límite a las facturas adicionales de la captura existente: cabeceras y líneas se
materializan tipadas antes de insertar, con numeración por documento y ajustes
inmutables. No se multiplican estimaciones mediante JSON anidado. La validación
del número de filas forma parte de la misma transacción; un fallo revierte
también inventario, fuentes y el avance de la recepción.

Verificación local del 22 de octubre: las regresiones de dos y diez cargos
detectaron antes tres y once llamadas de aceptación/retenciones, respectivamente;
ahora verifican una llamada de cada clase. La primera consulta agrupada de
retenciones agotó la espera SQL y bloqueó la secuencia documental; se corrigió
materializando sus filas sin ampliar el timeout. Pasaron los tres casos enfocados
y después los 54 casos SQL/API de recepción, borradores, órdenes de compra,
devoluciones, gastos y pagos a proveedores. La prueba de revisión tributaria
compara los importes, ajustes, regla y versión persistidos de cabecera y cargo
con sus previews aprobados. Este paso no sustituye el recorrido de inventario y
preparación de precios por línea, aún pendiente de migrar al lote canónico al
implementar revisiones de costo; tampoco habilita asociaciones ni descuentos
financieros pendientes.

La auditoría posterior añadió el corte de lote sin restringir la captura
histórica de facturas adicionales. Cien cargos más la factura principal generan
101 obligaciones y se conservan en dos llamadas al aceptador contable y dos al
guardado tributario; el caso de dos cargos conserva una llamada de cada clase.
El caso de cien falló antes por dejar sus retenciones en un único lote y pasó
con la partición de hasta 100 documentos. La corrida SQL/API ampliada aprobó
80 de 80 casos, incluidos los anteriores de compras/gastos/pagos, la aceptación
contable, la validación del índice de terceros y las reglas de mora/cupos de
suscripciones. No sustituye el benchmark de trazabilidad ni la regresión global
de API y navegador pendientes.

La regresión de recuperación también reprodujo un fallo anterior de la vista
previa al repartir un cargo con cero productos. El preview ahora devuelve cero
partidas y conserva los cargos capturados; la pantalla explica que hay que
agregar productos antes de repartir. Esto no permite confirmar ni guardar en el
servidor un reparto incompleto: se mantienen sus validaciones existentes. La
prueba unitaria falló antes con `TypeError` y pasó después; no se cambió el
calculador contable, la fórmula de reparto ni ningún writer de inventario.
La aceptación exige regresiones de borrador, recepción, IVA, retenciones,
valoración/precios, pago parcial, descuento pendiente/obtenido/perdido,
devolución, concurrencia y replay desde API y vista. El cálculo opera sobre el
lote recibido, sin I/O por producto, y las nuevas persistencias conservan un
número acotado de comandos independiente del número de líneas.

Presupuesto del guardado de líneas de mercancía: un comando por lote tanto para
el borrador como para la recepción; el guardado completo de un borrador nuevo
con 1, 50 o 500 líneas usa como máximo seis comandos SQL y tarda menos de dos
segundos en la prueba aislada después del calentamiento. La lectura de detalle
conserva su número acotado de consultas y reutiliza el snapshot confirmado.
Los lectores JSON de persistencia no recortan descripciones ni otros textos:
la longitud de la columna sigue rechazando el exceso y el fallo revierte toda
la confirmación, conservando el borrador. No crear un segundo escritor de líneas.
La aplicación valida previamente las longitudes de descripción y código
tributario para responder con un error de datos; una falla de persistencia se
prueba mediante inyección controlada en la base aislada, verificando que revierta
cabecera, detalle, trabajo y eliminación del borrador.

Evidencia del bloque comercial: 75 pruebas de dominio/arquitectura y 44 de
compras/órdenes pasaron; las verificaciones de navegador usan respuestas HTTP
simuladas y se distinguen de una sesión de API y navegador integrados. Guardar
1, 50 y 500 líneas mantuvo seis consultas SQL, con 114,4, 84,6 y 58,7 ms,
respectivamente, en la medición aislada. La regresión global, la auditoría del
rediseño completo y la integración de descuentos financieros permanecen pendientes.

## Lenguaje de producto

- **Orden de compra**: compromiso planificado con un proveedor. Antes se había denominado “orden de pedido”.
- **Recepción de compra**: registro de lo que físicamente llegó. Reemplaza en la interfaz “entrada/recepción de mercancía”.
- Los nombres técnicos `PurchaseOrder` y `GoodsReceipt` permanecen estables en contratos y persistencia para evitar una ruptura artificial.

## Propietarios y flujo

```text
Orden de compra (Purchasing, sin efecto físico)
  -> borrador recuperable
  -> confirmación inmutable OCP
  -> cumplimiento acumulado por línea

Recepción de compra (Purchasing)
  -> puede recuperar una OCP abierta o parcial
  -> el usuario registra la cantidad real
  -> confirmación inmutable EMC
  -> DocumentProcessingEngine / handler GoodsReceipt
  -> inventario, costo, cuentas por pagar, contabilidad y cumplimiento OCP

Venta/devolución procesada
  -> SalesReportingProcessingCoordinator
  -> hechos de reporting
  -> ProductRotationSnapshots por negocio, bodega y producto
```

Purchasing es el propietario de la orden y su cumplimiento. Inventario sólo cambia dentro del handler canónico de `GoodsReceipt`. Reporting es el único propietario del cálculo persistido de rotación; compras y catálogo sólo leen su snapshot.

La fecha física de recepción la registra el sistema al guardar o confirmar y no se edita en la interfaz. El usuario sí informa la fecha de emisión del documento de compra. Esa fecha gobierna la causación contable, las retenciones y el inicio de la cuenta por pagar. `Suppliers.DefaultPaymentDueDays` es la condición comercial canónica del proveedor y propone el vencimiento como fecha de emisión más ese plazo. El usuario puede ajustarlo para reflejar la condición pactada en el documento concreto; el servidor exige que no sea anterior a la emisión.

La retención se calcula exclusivamente en `WithholdingEngine` por medio de
`WithholdingService` al confirmar. El documento transporta ese resultado y el
procesador lo persiste como snapshot inmutable. El detalle de la recepción lee
ese snapshot para mostrar reglas, tarifas, deducciones y neto por pagar; no
recalcula valores históricos en Purchasing ni en la interfaz.

Las tablas de órdenes pertenecen al esquema `purchasing`. Las consultas y mutaciones nuevas se versionan en el proyecto de base de datos mediante procedimientos de los esquemas `purchasing` y `reporting`; los stores y handlers de C# sólo hacen invocaciones parametrizadas y no contienen SQL embebido.

## Ciclo de vida de la orden

`Draft -> Open -> PartiallyReceived -> Received`

Una orden abierta o parcial también puede pasar a `Closed` cuando un usuario autorizado cierra explícitamente el saldo con un motivo. No se permite cerrar si una recepción aceptada aún espera procesamiento. Una orden confirmada no se edita: la realidad se registra en recepciones separadas y auditables.

## Cantidades reales

Recuperar una orden propone el saldo pendiente, pero no bloquea la cantidad:

- orden 10, llegan 8: la recepción registra 8 y quedan 2;
- luego llegan 2: la orden queda recibida;
- orden 10, llegan 12: se conservan las 12 reales, pero el excedente exige motivo y el permiso `purchasing.goods-receipts.over-receive`;
- sin motivo o permiso, el servidor rechaza el excedente;
- el cumplimiento puede superar 100 %, mientras el pendiente nunca es negativo.

La recepción congela `PurchaseOrderId`, `PurchaseOrderLineId`, motivo y autorización del excedente. El handler bloquea orden y líneas, acumula lo recibido y deriva el estado en la misma transacción que aplica inventario. Esto evita dobles aplicaciones por reintentos.

Una recepción confirmada en estado `Accepted` cuenta como cantidad pendiente de aplicar mientras espera el motor. La recuperación descuenta ese pendiente y una confirmación concurrente lo valida bajo aislamiento serializable. Esto no reserva ni modifica inventario: evita que dos capturas consuman silenciosamente el mismo saldo documental de la orden.

## Rotación persistida

`reporting.ProductRotationSnapshots` guarda ventanas móviles de 30 y 90 días, ventas brutas, devoluciones, venta neta y demanda diaria de 90 días. La clave es `BusinessId + WarehouseId + ProductId`; por tanto, “Ver producto” muestra la rotación de cada bodega/sede del negocio y no mezcla existencias.

El writer canónico de reporting recalcula sólo los productos afectados al proyectar una venta o devolución. Una transacción histórica no puede hacer retroceder `WindowEndDate`. Las órdenes leen el snapshot para apoyar abastecimiento junto con stock actual y cantidades en camino; nunca recalculan ventas desde la pantalla ni desde Purchasing.

## Experiencia web

Las bandejas usan la grilla compartida, paginación de servidor y filtros por estado. Órdenes y recepciones usan el mismo selector paginado de productos del proveedor:

1. buscar o escanear por nombre, códigos, referencia o código del proveedor;
2. seleccionar agrega la línea y enfoca su cantidad;
3. flechas arriba/abajo recorren cantidades;
4. Enter en cantidad devuelve el foco al buscador.

El modo `Todo el catálogo` conserva paginación real de servidor: la primera
respuesta y cada carga incremental contienen como máximo 10 productos. La
búsqueda se difiere 250 ms mientras el usuario escribe y el query SQL primero
materializa los 10 identificadores solicitados; sólo después calcula códigos,
costos e inventario para esos productos. La orden de compra comparte este
selector y el mismo contrato, por lo que tampoco descarga ni enriquece el
catálogo completo.

El detalle de producto presenta rotación sólo como información de lectura. La edición del producto no puede escribirla.

## Contratos principales

- `GET /api/commerce/v1/purchase-orders`
- `GET /api/commerce/v1/purchase-orders/{id}`
- `GET /api/commerce/v1/purchase-orders/{id}/receipt-source`
- `GET /api/commerce/v1/parties/role-options?role=Supplier`
- `PUT /api/commerce/v1/purchase-orders/{id}/draft`
- `POST /api/commerce/v1/purchase-orders/confirm`
- `POST /api/commerce/v1/purchase-orders/{id}/close`
- `GET /api/commerce/v1/goods-receipts/products`
- `PUT /api/commerce/v1/goods-receipts/drafts/{id}`
- `POST /api/commerce/v1/goods-receipts/confirm`
- `GET /api/commerce/v1/products/{id}/rotation`

Todos derivan tenant y negocio de la identidad autenticada y validan que bodega, proveedor, producto y orden pertenezcan al mismo alcance. En una compra a crédito también validan que el vencimiento coincida con el plazo del proveedor, para que un cliente desactualizado no pueda alterar la condición calculada.

## Rollback y compatibilidad

Las columnas nuevas de recepción son opcionales; las recepciones sin orden conservan el comportamiento anterior. El despliegue puede revertir código sin perder documentos: las tablas y columnas agregadas son compatibles. No se eliminan ni reescriben recepciones confirmadas; cualquier reversión económica o física se realiza mediante los documentos compensatorios canónicos.

## Diseño propuesto: múltiples documentos de costo y costo puesto en bodega

**Estado:** slice de recepción, costos adicionales e importación implementado; pagos en moneda
extranjera y costos conocidos después de confirmar permanecen fuera de este alcance.
**Fecha:** 2026-09-03.

### Problema que resuelve

Una recepción física puede originar obligaciones independientes:

- factura de la mercancía, normalmente del proveedor principal;
- factura de flete, seguro, descargue u otro servicio, emitida por otro proveedor;
- declaración de importación y tributos aduaneros;
- factura de agencia de aduanas u otros servicios de nacionalización;
- vencimientos, monedas, retenciones, soportes e IVA diferentes por cada documento.

Esos documentos no se deben sumar como si fueran nuevas líneas gravadas de la factura de mercancía. Cada uno conserva su realidad fiscal y financiera, mientras sólo la porción que sea directamente atribuible a poner el inventario en su ubicación y condición actuales incrementa su costo. IAS 2 incluye precio de compra, aranceles, impuestos no recuperables, transporte y manejo directamente atribuibles; excluye del costo los impuestos recuperables. Fuente: [IFRS Foundation, IAS 2](https://www.ifrs.org/issued-standards/list-of-standards/ias-2-inventories/).

### Decisión de dominio

Se mantienen separados tres conceptos:

```text
Recepción de compra       = qué producto y cantidad llegaron físicamente
Documento de costo       = quién cobra, qué soporte existe, moneda, impuesto y vencimiento
Asignación de costo       = cuánto del valor capitalizable impacta a cada línea recibida
```

`GoodsReceipt` continúa siendo el único documento físico y el propietario de cantidades, bodega, cumplimiento de la orden y entrada al inventario. No se crea otra recepción, motor, cola ni writer.

Para conservar lo que ya funciona, `GoodsReceipts` y `GoodsReceiptLines` siguen siendo la fuente de verdad de la **factura principal de mercancía**. Una recepción tendrá cero o más **documentos de costo adicionales** para flete, seguro, nacionalización u otros cobros. La API compone ambos orígenes en un único read model para que visualmente todos aparezcan como documentos de la recepción, pero no copia la factura principal en tablas nuevas.

Este límite evita una migración innecesaria y, sobre todo, evita dos propietarios para precio, descuento e IVA de la mercancía. El proveedor del encabezado sigue siendo el proveedor comercial y de la orden; cada documento adicional conserva su propio proveedor y vencimiento.

La recepción completa se confirma una sola vez. El payload inmutable lleva documentos, líneas y asignaciones; el handler `GoodsReceipt` aplica el costo total reconocido mediante `SqlInventoryLedgerWriter` y conserva idempotencia por recepción.

La factura principal conserva el `AccountingPostingJob` vigente. Cada documento adicional genera su propia fuente y otro job en el mismo motor contable. Así conserva proveedor, fecha de causación, período, moneda, vencimiento y eventual reversión propios. El processor produce un asiento balanceado y una cuenta por pagar por documento, usando las asignaciones congeladas para debitar inventario, gasto e IVA. No se crean otra tabla de jobs, cola, worker ni posting service. La suma de débitos a inventario de la factura principal y los documentos adicionales debe conciliar con el valor reconocido por el único movimiento operativo de la recepción.

La conciliación del ajuste por inventario negativo usa el costo total que ya lleva el movimiento operativo: base de mercancía más costos adicionales capitalizados en las líneas con movimiento. El ajuste es esa suma menos el cambio real de valor del inventario. Comparar el movimiento completo solo con la base de la factura principal vuelve a debitar los costos adicionales y reduce indebidamente el costo de ventas. Con existencias positivas, mercancía de COP 50.000 y flete capitalizado de COP 10.000 producen un solo aumento de inventario de COP 60.000: dos débitos documentales por COP 50.000 y COP 10.000, sin asiento de ajuste. Si la entrada cubre inventario negativo, el ajuste lleva a costo de ventas únicamente la diferencia entre COP 60.000 y el aumento real de valor del inventario.

`Payables` se vincula al `CostDocumentId` inmutable y conserva también `GoodsReceiptId` para navegación. Su unicidad deja de ser una sola obligación por recepción y pasa a una obligación por documento de costo. La reversión posterior referencia ambos identificadores.

Los documentos adicionales no generan `DocumentProcessingJobs` ficticios. El handler de la recepción crea sus `AccountingSourceDocuments` y `AccountingPostingJobs` directamente en las tablas canónicas del motor contable, dentro de la misma transacción que deja procesada la recepción.

### Modelo mínimo

No se crea un agregado genérico de importaciones en la primera entrega. `GoodsReceiptId` es el expediente de costeo.

#### `purchasing.GoodsReceiptCostDocuments`

Un registro sólo por factura, declaración o soporte adicional; la factura principal no se duplica aquí:

- `CostDocumentId`, `GoodsReceiptId`, `BusinessId`;
- `SupplierId` o contraparte pagable del mismo tenant/business;
- tipo de soporte del catálogo canónico;
- número normalizado, fecha de emisión y vencimiento;
- condición contado/crédito y evidencia de liquidación cuando sea contado;
- moneda original;
- en el slice de importación: tasa de reconocimiento contable, fecha, fuente y equivalente en COP;
- subtotal, impuestos, retenciones, total y neto por pagar en moneda original y COP;
- estado y snapshot inmutable al confirmar.

El soporte de cada documento adicional se limita según la política del proveedor seleccionado para ese documento. Una recepción puede incluir varios documentos de flete de proveedores distintos, y un mismo documento puede contener varios conceptos de flete; cada documento genera su propia obligación cuando corresponde.

La persistencia impide repetir el mismo número externo para un proveedor de costo adicional, incluso en otra recepción de la misma sede. Así no se causa dos veces la misma obligación. Si el soporte lo emite Auraly, el usuario deja el número vacío y el motor le asigna un consecutivo fiscal DIAN distinto a cada documento dentro de la confirmación; la validación de números externos no trata esos campos vacíos como duplicados. El número asignado se conserva en el documento de costo y su cuenta por pagar.

El documento soporte de un costo adicional usa el mismo emisor, cupo, snapshot y proceso fiscal que el documento soporte de la factura principal, con raíz fiscal propia referida al `CostDocumentId`. El motor selecciona la resolución y serie vigentes para la fecha de emisión de cada documento; si dos fechas corresponden a series distintas, reserva los consecutivos de ambas series en un único lote. La reserva de todos los soportes de una recepción, su inventario y sus obligaciones quedan en una transacción SQL. Si falta cupo, configuración o un dato DIAN, no se confirma ninguna parte de la recepción. El asiento de cada documento adicional sigue el motor contable existente: débito a inventario para la porción capitalizable de productos recibidos, débito al concepto PUC de gasto cuando no se capitaliza, IVA descontable únicamente con tratamiento fiscal permitido y créditos a retenciones y cuenta por pagar del proveedor. La selección del soporte no crea otro asiento ni modifica la cuenta PUC del concepto.

La generación fiscal de la factura principal y de cada costo adicional sólo puede adquirir trabajo después de que la recepción quede `Processed`. El observador canónico de finalización publica la señal del documento principal y, en una consulta acotada por recepción, identifica los costos adicionales con generación pendiente y publica su señal individual. Reproducir el observador es idempotente y no vuelve a contabilizar ni reservar numeración.

Un comprobante interno adicional aplica la misma restricción que la factura principal: no acredita IVA descontable por sí solo. Si el impuesto es no recuperable y el costo es directamente atribuible a la mercancía, se clasifica como mayor valor del inventario; en caso contrario se lleva al gasto correspondiente.

Con el PUC semilla, inventario usa `143505`, IVA descontable `240810` y proveedores `220505`; las retenciones van a las categorías `236570`, `236701` y `236805` según la regla tributaria. Un flete que no se capitaliza usa `513550`; seguro de compra `513095`, aduanas `514020`, manejo `513595` y otros costos directos el auxiliar `51959501`. Las empresas pueden configurar las cuentas de esas categorías y conceptos sin cambiar el proceso. La factura principal de mercancía reconoce su propio inventario y su propia cuenta por pagar; los documentos adicionales solo agregan su costo atribuible o gasto y su obligación independiente.

#### `purchasing.GoodsReceiptCostLines`

Una línea económica del documento:

- tipo de costo desde catálogo: flete, seguro, arancel, servicio aduanero, manejo u otro costo directo;
- descripción y conjunto de líneas físicas elegibles para recibir la asignación;
- importe antes de impuesto, base gravable, código/tarifa e importe del impuesto;
- tratamiento tributario ya vigente: `DeductibleInputVat`, `CapitalizedCost` o `NotApplicable`;
- tratamiento del importe base: capitalizable o gasto;
- método de distribución y, cuando aplique, criterio manual;
- importes originales y COP congelados.

La base gravable y el impuesto se guardan por separado. Esto es obligatorio para importaciones: el IVA aduanero puede tener como base el valor en aduana más el arancel y no se obtiene aplicando la tarifa al cobro del agente.

#### `purchasing.GoodsReceiptCostAllocations`

Un registro por línea de costo y línea recibida:

- importe asignado en COP;
- factor usado y snapshot de su unidad: valor, cantidad, kg, m³ o porcentaje manual;
- residuo de redondeo aplicado;
- versión de fórmula.

La suma asignada debe ser exactamente igual al importe capitalizable de la línea de costo. La asignación es calculada por backend, previsualizada en UI y congelada al confirmar.

El borrador recuperable forma parte del mismo slice: guardar la recepción persiste documentos, líneas y criterios manuales adicionales bajo el `GoodsReceiptDraftId` y su `RowVersion`. Reabrir un borrador no puede perder facturas ni recalcular silenciosamente una distribución manual. `RowVersion` protege únicamente guardar o descartar esa única copia editable; no representa un historial. Al confirmar, el payload visible es la autoridad y ninguna versión del borrador puede bloquearlo. La confirmación elimina el borrador sólo después de crear el snapshot definitivo de manera atómica.

#### Snapshot logístico en la línea recibida

Para no depender de maestros que luego cambien, cada `GoodsReceiptLine` congela opcionalmente:

- peso bruto total en kg;
- volumen total en m³.

`Products.UnitGrossWeightKg` es el peso bruto por unidad base del producto y siempre
se expresa en kilogramos, independientemente de la unidad usada para venderlo. Es
un dato del catálogo administrativo y no forma parte del catálogo descargado al
POS. En una recepción, Purchasing lo lee dentro del tenant autenticado y calcula
`cantidad base recibida × UnitGrossWeightKg`; ese total alimenta cualquier
distribución por peso y queda congelado en `GoodsReceiptLines.TotalGrossWeightKg`
al confirmar. El mismo enriquecimiento ocurre al guardar el borrador y al
confirmar, incluida una recepción recuperada desde una orden de compra, por lo
que el servidor no confía en un peso enviado por el navegador.

Mientras el documento está en edición, un método automático de prorrateo no
congela la lista de líneas elegibles: agregar, quitar o cambiar la cantidad de un
producto recalcula la distribución completa sobre todas las líneas actuales. La
confirmación vuelve a calcularla en servidor y congela únicamente el reparto
final en el snapshot inmutable.

Los productos antiguos pueden no tener todavía ese maestro. Sólo en ese caso el
formulario permite capturar el peso bruto total de la línea; nunca inventa un
valor. El volumen continúa siendo un dato explícito de la recepción cuando el
método elegido lo requiere.

### Catálogos, no listas quemadas

Los siguientes selectores se sirven desde `reference.Options` y API:

- `purchase-cost-kind`;
- `purchase-cost-allocation-method`;
- `exchange-rate-source`, únicamente cuando se implemente moneda extranjera.

El tipo de soporte amplía el catálogo existente `purchase-evidence-type`; no se crea un segundo catálogo con la misma responsabilidad. Los nuevos códigos necesarios para importación son, como mínimo, factura comercial extranjera y declaración de importación.

Los tipos técnicos inmutables pueden conservar constantes para tipado, pero la tabla sigue siendo la fuente de opciones, label, activación y orden.

### Métodos de distribución

Los ERP revisados ofrecen combinaciones de igual, cantidad, valor, peso y volumen. Business Central también conserva el cargo como valor separado ligado al movimiento original; Odoo muestra valor original, costo adicional y nuevo valor antes de validar. Fuentes: [Microsoft Business Central](https://learn.microsoft.com/en-au/dynamics365/business-central/payables-how-assign-item-charges), [Odoo Landed Costs](https://www.odoo.com/documentation/17.0/applications/inventory_and_mrp/inventory/product_management/inventory_valuation/landed_costs.html) y [Oracle Landed Cost Charges](https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/25c/fapma/landed-cost-charges.html).

| Método | Uso recomendado | Regla |
| --- | --- | --- |
| Valor | seguro, arancel agregado o costos ligados al valor | valor COP de mercancía de la línea / valor COP total elegible |
| Cantidad | cargo realmente cotizado por unidad homogénea | unidades base / unidades base totales |
| Peso | flete facturado por kg y manejo físico | kg brutos de la línea / kg brutos totales |
| Volumen | flete donde manda el espacio ocupado | m³ de la línea / m³ totales |
| Igual por línea | cargo administrativo uniforme | 1 / número de líneas elegibles |
| Manual | declaración o proveedor entrega reparto exacto | porcentajes o importes que sumen 100 % / total |

No existe un método universal de “flete”. Si el transportador cobra por peso se usa peso; si cobra por espacio, volumen; si entrega peso cobrable o reparto por guía, se registra como asignación manual soportada. No se quema un factor de peso volumétrico, porque cambia por modo y transportador.

Defaults propuestos, siempre visibles y editables antes de confirmar:

- flete: peso; volumen si el documento indica cobro volumétrico;
- seguro: valor;
- arancel: asignación directa desde cada ítem/subpartida de la declaración; valor sólo como ayuda cuando el soporte no trae el reparto;
- honorarios de agencia: valor o igual por línea según la forma real del cobro;
- manejo/descargue: peso o manual;
- mercancía: asignación directa a su línea recibida.

El algoritmo usa decimal y método de mayor residuo: calcula proporciones a alta precisión, redondea a moneda y entrega los centavos residuales en orden determinista a las mayores fracciones. Así nunca se pierde ni se duplica un peso.

### Flujo doméstico

1. Se seleccionan proveedor principal, bodega y productos como hoy.
2. Auraly presenta como primera tarjeta la factura de mercancía que ya captura hoy, sin copiar sus datos.
3. “Agregar documento” permite flete u otro costo con proveedor, soporte, fecha y vencimiento propios.
4. Cada línea de servicio separa base e IVA. La base capitalizable se distribuye; el IVA descontable no se distribuye al costo.
5. La vista previa presenta costo original, cargos asignados y nuevo costo unitario por producto.
6. Confirmar crea una recepción, la obligación principal, las obligaciones adicionales y un único efecto físico idempotente.

Ejemplo: mercancía COP 1.000.000 + IVA 190.000 y flete COP 100.000 + IVA 19.000. Si ambos IVA son descontables, el inventario recibe COP 1.100.000; IVA descontable recibe COP 209.000; las cuentas por pagar suman COP 1.309.000 repartidas entre los dos proveedores. Nunca se vuelve a calcular 19 % sobre COP 1.100.000.

### Flujo importado

Una “factura de nacionalización” no se trata como una sola bolsa. La pantalla propone separar sus componentes porque tienen naturalezas distintas:

1. **Factura comercial extranjera:** mercancía en USD u otra moneda. No se agrega IVA colombiano a esas líneas por defecto.
2. **Declaración de importación:** valor en aduana, arancel por ítem/subpartida e IVA de importación. El arancel incrementa costo; el IVA pagado en importación se registra como descontable cuando se cumplen las condiciones tributarias, de lo contrario se capitaliza.
3. **Flete y seguro:** cada factura conserva su proveedor e IVA. Su base directamente atribuible incrementa inventario; el IVA recuperable permanece impuesto descontable.
4. **Agencia de aduanas/nacionalización:** honorarios y su IVA se separan de anticipos o reembolsos de tributos para no duplicar ni el gasto ni el impuesto.

La DIAN establece que la base del IVA en la importación parte del valor en aduana y adiciona los derechos de aduana; también indica que el flete y gastos conexos hasta el lugar de importación forman parte del valor en aduana cuando no están incluidos. Fuentes: [artículo 459 explicado por DIAN](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_901412_2021.htm) y [preguntas frecuentes de valoración aduanera DIAN](https://www.dian.gov.co/aduanas/aspectecmercancias/valoracion_de_mercancias/Preguntas_frecuentes/Paginas/default.aspx).

La nacionalización no se presume “por kilo”. Los tributos se toman del detalle real de la declaración. Un agente puede cobrar sus honorarios por kilo; sólo ese servicio se distribuye por peso si el soporte lo demuestra. El IVA pagado en importación puede ser descontable bajo las condiciones de los artículos 485 y 488; la clasificación concreta debe quedar validada por el contador del negocio. Fuente: [DIAN, Concepto 8857 de 2025](https://normograma.dian.gov.co/dian/compilacion/docs/oficio_dian_8857_2025.htm).

Ejemplo simplificado con tasa contable de COP 4.000/USD:

| Componente | Producto A | Producto B | Tratamiento |
| --- | ---: | ---: | --- |
| Mercancía USD 600 / USD 400 | COP 2.400.000 | COP 1.600.000 | inventario |
| Flete COP 800.000 por volumen 25 % / 75 % | 200.000 | 600.000 | inventario |
| Arancel según ítems de declaración | 288.000 | 192.000 | inventario |
| Agencia COP 200.000 distribuida por valor 60 % / 40 % | 120.000 | 80.000 | inventario |
| **Costo puesto en bodega** | **3.008.000** | **2.472.000** | **COP 5.480.000** |

En este ejemplo, el valor en aduana es COP 4.800.000 y el arancel es COP 480.000. Si el IVA de importación soportado por la declaración es COP 1.003.200 —19 % sobre COP 5.280.000— y el IVA de la agencia es COP 38.000, ambos se llevan a IVA descontable cuando proceda: COP 1.041.200. No se suman al costo de COP 5.480.000 ni se calcula nuevamente 19 % sobre ese costo. El flete aparece una vez como costo económico y también integra la base fiscal aduanera; eso no lo duplica. El ejemplo presupone que el flete no estaba ya incluido en el precio/Incoterm; si estaba incluido, se concilia y no se capitaliza dos veces.

### Moneda extranjera

La recepción puede confirmar una factura extranjera congelando su moneda original, tasa,
fecha y fuente. El inventario, las retenciones, las propuestas de precio y los asientos se
reconocen en COP; la cuenta por pagar conserva el importe original y su
equivalente funcional inicial. El único saldo pendiente mutable es
`OutstandingAmount`, en la moneda del documento; su equivalente histórico se
  proyecta con la tasa congelada. La columna legacy `FunctionalOutstandingAmount`
  queda temporalmente en el esquema para compatibilidad de actualización; las
  posiciones nuevas la dejan NULL y ningún lector/writer actual la utiliza.
  Su retiro físico exige cerrar la ventana de rollback de binarios antiguos,
  según el diseño contable. La liquidación posterior de esa obligación y su diferencia en cambio
pertenecen al flujo de pagos, no recalculan ni reescriben esta recepción.

La implementación garantiza:

- moneda funcional COP por tenant, como hoy;
- monto original y equivalente COP en cada documento y línea;
- tasa positiva, fecha y fuente obligatorias;
- tasa bloqueada al confirmar y hash del snapshot;
- inventario, propuestas de precio, retenciones y asientos siempre basados en COP;
- cuenta por pagar conservando saldo original en USD, tasa e importe funcional inicial;
- la liquidación en moneda extranjera y su diferencia en cambio siguen pendientes
  del flujo de pagos; los pagos actuales admiten COP. No reescribir el costo
  histórico del inventario ni presentar la conversión histórica como una
  reexpresión vigente.

IAS 21 exige reconocer inicialmente una transacción extranjera en moneda funcional con la tasa spot de la fecha de la transacción. La fecha aduanera y su tasa oficial pueden diferir de la fecha/tasa contable de la factura; se almacenan por separado y nunca se reutiliza una como si fuera la otra. Fuente: [IFRS Foundation, IAS 21](https://www.ifrs.org/content/dam/ifrs/publications/pdf-standards/english/2021/issued/part-a/ias-21-the-effects-of-changes-in-foreign-exchange-rates.pdf?bypass=on).

### Regla de IVA: evitar el doble impuesto

El IVA no se calcula sobre el costo puesto en bodega. Se reconoce desde cada soporte legal:

```text
Costo de inventario por línea
  = mercancía neta en COP
  + bases capitalizables asignadas
  + impuestos no recuperables asignados

IVA descontable
  = suma de impuestos marcados DeductibleInputVat en sus documentos

Cuenta por pagar por documento
  = total de ese documento - sus retenciones
```

Por tanto:

- el IVA de la factura local de mercancía no se vuelve a aplicar después del flete;
- el IVA del flete se reconoce una sola vez desde la factura del transportador;
- el IVA de importación se reconoce desde la declaración, no desde la factura extranjera;
- un impuesto no recuperable usa `CapitalizedCost` y sí se distribuye al inventario;
- una línea sin soporte fiscal válido nunca crea IVA descontable.

Controles obligatorios para impedir doble IVA:

- cada impuesto tiene exactamente un `CostDocumentId`, una base gravable y un importe;
- una asignación de costo nunca genera impuesto: sólo reparte importes ya clasificados como capitalizables;
- la suma de IVA de la recepción se obtiene agregando documentos, no recalculando sobre el costo final;
- el backend rechaza el mismo soporte tributario referenciado en dos documentos;
- anticipos y reembolsos del agente de aduanas deben referenciar el tributo original y no crear otro IVA;
- la UI no propone 19 % para flete, agencia o importación por inferencia: muestra exactamente el impuesto del soporte o exige capturarlo;
- el tratamiento se muestra en lenguaje humano: “IVA descontable — no aumenta el costo” o “IVA no recuperable — aumenta el costo”.

La retención también se calcula por documento, proveedor y fecha. El preview único actual de `GoodsReceipt` no se reparte ni se reutiliza entre proveedores.

### Costos observados, precios y devoluciones

El “último costo del proveedor” conserva exclusivamente la mercancía comprada al proveedor principal, con su tratamiento tributario propio. No incluye flete, arancel ni honorarios de terceros. Mezclarlos dañaría la comparación de futuras listas del proveedor.

El costo utilizado para valorar inventario y preparar precios sí usa el costo puesto en bodega completo. Por tanto se conservan dos cifras visibles y nombradas:

```text
Costo de compra al proveedor != Costo puesto en bodega
```

Reporting atribuye la compra de mercancía al proveedor principal y presenta los cargos adicionales por su proveedor/tipo; no infla las compras del proveedor de mercancía con facturas de terceros.

Una devolución de compra revierte por defecto sólo mercancía, IVA y cuenta por pagar del proveedor principal. No reduce automáticamente la factura ya causada del transportador, la agencia o la DIAN. Los cargos capitalizados no recuperables que salgan con la mercancía se reconocen según la política contable de la devolución; una nota crédito real de un proveedor adicional se registra contra su propio documento. El flujo vigente que asume una sola cuenta por pagar debe modificarse antes de habilitar múltiples documentos.

Si la recepción anticipó un descuento financiero, la devolución conserva el
importe bruto y el IVA del documento, pero reduce la cancelación de la obligación
o el crédito al proveedor por la parte del descuento atribuida a las líneas
devueltas. Cada línea congela esa parte; las devoluciones parciales posteriores
consumen solamente el remanente de la línea original. Una devolución sin descuento
anticipado conserva íntegro su comportamiento anterior.
El corte de despliegue exige que los consumidores documental y contable conozcan
`FinancialDiscountAmount` antes de aceptar estas devoluciones: un consumidor
anterior ignoraría la parte congelada y podría abrir un crédito bruto incorrecto.
Antes de retroceder esos consumidores se deben drenar sus trabajos aceptados;
el nuevo dato permanece en el documento y no se elimina para volver atrás.

### Integración contable nativa y configurable

Todo efecto monetario de la recepción entra al motor contable. Purchasing captura, valida y congela la evidencia; no escoge cuentas PUC ni escribe asientos. `AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor` son los únicos propietarios de:

- inventario contable y gastos no capitalizables;
- IVA descontable o mayor valor del inventario;
- cuentas por pagar, anticipos y cuentas de compensación;
- retenciones por pagar;
- aranceles y tributos aduaneros;
- pagos y aplicaciones;
- diferencias en cambio;
- notas crédito, devoluciones y reversos.

El cálculo de retenciones termina antes de entrar a Accounting. Purchasing invoca
el motor tributario con el perfil propio del proveedor, guarda el snapshot de
bruto, líneas retenidas y neto, y crea la cuenta por pagar por ese neto. Accounting
no invoca `WithholdingEngine`, no vuelve a seleccionar reglas y no deriva una
retención: únicamente contabiliza el snapshot persistido y rechaza documentos que
no concilien. Así, reintentos o cambios posteriores de configuración no alteran
una recepción ya confirmada.

La pantalla de recepción no pide una cuenta contable. El usuario elige el tipo económico real —flete, seguro, arancel, agencia, manejo u otro costo directo— y el tratamiento permitido. El motor resuelve la cuenta efectiva mediante el sistema vigente de categorías y `AccountCategoryMappings`, con alcance tenant/business y fechas de vigencia.

Se agregan únicamente las categorías contables que representan comportamientos realmente distintos y no existen hoy:

- gasto de flete de compra no capitalizable;
- gasto de seguro de compra no capitalizable;
- gasto de agencia/manejo no capitalizable;
- gasto de arancel no capitalizable;
- tributos aduaneros por pagar o cuenta de compensación;
- anticipos entregados a proveedores/agencia, si esa forma de liquidación se habilita;
- ganancia y pérdida por diferencia en cambio.

Los importes capitalizables no requieren una cuenta de flete o nacionalización separada en el débito: usan la categoría `Inventory` ya existente y conservan tipo de costo y documento como dimensiones de trazabilidad. IVA usa `InputVat`; la obligación usa `AccountsPayable`; las retenciones reutilizan sus categorías actuales. No se crean categorías duplicadas para esos efectos.

El perfil contable colombiano aprovisiona cuentas y mappings por defecto para todas las categorías nuevas. Un contador autorizado puede reemplazarlos con vigencia, sin cambiar código ni modificar recepciones históricas. Una recepción no puede seleccionar una cuenta ad hoc para evadir esa configuración.

Asientos esperados, simplificados:

```text
Factura de mercancía
  Débito  Inventario                         base neta capitalizable
  Débito  IVA descontable                    IVA recuperable
  Crédito Cuenta por pagar proveedor         neto por pagar
  Crédito Retenciones por pagar              retenciones aplicadas

Factura de flete o agencia capitalizable
  Débito  Inventario                         base distribuida
  Débito  IVA descontable                    IVA recuperable del documento
  Crédito Cuenta por pagar proveedor         neto por pagar
  Crédito Retenciones por pagar              retenciones aplicadas

Declaración de importación
  Débito  Inventario                         arancel y tributos capitalizables
  Débito  IVA descontable                    IVA de importación recuperable
  Crédito Tributos aduaneros por pagar,
          banco, anticipo o compensación      según liquidación soportada

Pago de obligación USD
  Débito  Cuenta por pagar                    valor contable vigente
  Débito/Crédito Diferencia en cambio         variación desde reconocimiento
  Crédito Banco                               COP realmente entregados
```

Cada documento adicional lleva su propia fecha contable y se procesa de forma idempotente. Si falta período o mapping, queda en `AccountingPendingConfiguration`, visible desde la recepción y bloqueando el cierre del período como ocurre hoy. El documento no se marca falsamente como contabilizado, no se crea un fallback a gasto general y un retry posterior usa el snapshot original sin volver a mover inventario.

La capacidad de agregar facturas/costos exige que el tenant tenga el motor contable activado y que todas las categorías necesarias sean resolubles en la fecha de cada documento. La validación previa se realiza mediante un contrato de readiness del módulo Accounting; Purchasing no consulta tablas contables ni duplica reglas de mappings. Si falta configuración, la UI presenta antes de confirmar la lista exacta y el enlace a Contabilidad. Las recepciones simples existentes conservan su compatibilidad durante el cutover, pero ningún documento adicional puede confirmarse por una ruta no contable.

Para compatibilidad con el comportamiento durable existente, una falla transitoria ocurrida después del commit puede quedar pendiente y reintentarse; nunca desaparece ni duplica el efecto operacional.

### Experiencia visual

El detalle confirmado y su reporte incluyen un resumen completo en COP cuando
existen documentos adicionales: factura principal con IVA, suma de documentos
asociados con impuestos, total documental, impuestos registrados, retenciones y
neto a la confirmación. Es una suma de snapshots confirmados; no calcula impuestos,
no convierte nuevamente monedas y no representa el saldo pendiente después de pagos.
Los importes de la factura principal continúan separados y no se incrementan por
facturas de otros proveedores. Si un histórico carece del importe funcional o de
costo requerido, se muestra «No disponible», no cero ni una conversión inferida.

La suma de `RecognizedInventoryCostAmount` de las líneas se presenta como **costo
puesto de los productos**, porque también contiene líneas no inventariables que
el motor contable lleva a gasto. No se etiqueta como saldo o débito de inventario.
El valor efectivamente reconocido en inventario se verifica en sus movimientos
y en el asiento canónicos; no se deduce del nombre del producto ni del centro de costo.

La pantalla conserva el recorrido actual para la compra normal. Si no se agrega otra factura, el usuario no ve pasos ni campos nuevos obligatorios.

Debajo de los productos aparece una sección de revelado progresivo “Facturas y otros costos”. La factura principal siempre está visible; “Agregar factura o costo” abre un panel lateral corto. La distribución se calcula automáticamente con un default visible y sólo se abre en detalle cuando el usuario desea cambiarla o existe un dato faltante. No se usan pestañas que oculten errores entre una vista y otra.

```text
Recepción EMC-000123                     Costeo completo ✓

Facturas y otros costos
┌ Mercancía · ACME · INV-88 · USD 2.450 · vence 30 sep   ✓ conciliado
├ Flete · Transportes Sur · FE-991 · COP 476.000          ✓ distribuido por peso
└ Importación · Declaración 032... · COP 3.280.000         ! revisar 1 diferencia
                                           [+ Agregar factura o costo]

Resumen por producto
Producto      Mercancía COP   Flete   Arancel/otros   Costo final/u
A             2.000.000       80.000  120.000          22.000
B             3.000.000      320.000  180.000          35.000
```

Cada tarjeta muestra proveedor, soporte, moneda, total, vencimiento, cuánto aumenta inventario y estado. Al expandirla se editan sus líneas. “Ver distribución” abre el detalle de importe original, factor, asignación y nuevo costo; una advertencia explica exactamente qué dato falta. Los totales separan siempre:

- valor de mercancía;
- costos adicionales capitalizados;
- impuestos descontables;
- impuestos capitalizados;
- retenciones;
- total y neto por pagar por proveedor;
- costo final del inventario.

Después de confirmar, cada tarjeta muestra además `Contabilizado`, `Pendiente de configuración` o `Error reintentable`, con enlace al asiento o al mapping faltante para usuarios autorizados. No se expone un editor de débitos y créditos dentro de la recepción.

El botón final dice “Confirmar recepción y N cuentas por pagar”. Se bloquea si un documento no concilia, una asignación no suma, falta tasa de cambio, peso/volumen requerido, soporte, vencimiento o tratamiento tributario.

En el grid de productos se agrega una sola columna condicional “Costo final/u”; permanece oculta cuando no existen costos adicionales. Peso y volumen sólo se solicitan al escoger esos métodos, evitando cargar la compra normal con datos logísticos innecesarios.

### Alcance implementado y límites deliberados

El slice confirmado incluye compras domésticas e importadas, varios documentos,
proveedores y vencimientos, monedas y tasas congeladas, métodos de asignación por valor,
cantidad, peso, volumen, igual o manual, IVA y retenciones por documento, múltiples cuentas
por pagar y contabilización nativa mediante el motor existente.

Todos los documentos deben estar completos antes de confirmar. Se conservan estos límites:

- una recepción conserva un solo proveedor de mercancía; si llegan productos comprados a otro proveedor se crea otra recepción;
- un documento adicional pertenece a una sola recepción;
- no se pueden anexar facturas después de confirmar;
- peso o volumen se captura sólo cuando el método elegido lo requiere;
- no se crea un módulo general de facturas de proveedor ni se reutiliza `Expenses`;
- anticipos, reembolsos de agencia, pagos de obligaciones en moneda extranjera y diferencia en cambio se resuelven en sus flujos financieros propios, no dentro de la recepción.

#### Slice 3 — costos posteriores

Propuesta anterior, sustituida por la ampliación del 2026-10-20 al final de este
documento. El usuario exige conservar las ventas históricas y el flujo vigente de
preparación/publicación de precios. No se autoriza implementar el antiguo ajuste
retroactivo de costo de ventas ni actualizar el promedio directamente por SQL.

### Propiedad e integración canónica

| Regla/efecto | Propietario |
| --- | --- |
| documentos, conciliación y asignaciones | `Purchasing` / agregado `GoodsReceipt` |
| cálculo de IVA y retenciones | motores tributarios; Purchasing conserva snapshot |
| cantidad y valor de inventario | handler `GoodsReceipt` → `SqlInventoryLedgerWriter` |
| todo asiento, cuenta por pagar, anticipo, pago, diferencia en cambio y reverso | `AccountingProcessingCoordinator` / `SqlAccountingPostingProcessor` |
| propuestas de precio | costo reconocido final que ya consume el handler |
| opciones visibles | catálogo `reference.Options` + API |

No se reutiliza `Expenses` para las facturas ligadas a la recepción: produciría otro propietario de la obligación y dificultaría conciliar el costo con inventario. Tampoco se crean productos de servicio ficticios para flete o arancel.

### Criterios de aceptación de la recepción

- una recepción con tres proveedores crea tres obligaciones, un movimiento físico y un asiento balanceado sin duplicados al reintentar;
- el costo asignado por cada cargo suma exactamente su base capitalizable;
- IVA descontable nunca incrementa inventario; IVA capitalizado sí;
- flete por peso/volumen falla explícitamente si falta el snapshot correspondiente;
- la declaración distribuye el arancel conforme a sus ítems y no “por kilo” por defecto;
- USD sin tasa/fuente/fecha no puede confirmarse y USD confirmado valora inventario en COP;
- la cuenta por pagar conserva moneda original y valor funcional sin cambiar el costo histórico;
- mercancía, flete, seguro, agencia, arancel, IVA y retenciones producen fuentes contables nativas y asientos balanceados;
- cada categoría nueva tiene mapping efectivo configurable y el perfil colombiano trae un default explícito;
- una configuración contable faltante produce `AccountingPendingConfiguration`, bloquea el cierre y puede reintentarse sin reaplicar inventario;
- duplicidad se valida por negocio, proveedor, tipo y número normalizado;
- tenant, autorización, concurrencia, idempotencia, períodos y rollback tienen regresiones de integración;
- el detalle histórico reproduce exactamente documentos, impuestos, factores, asignaciones y costo final sin consultar maestros mutables.
- guardar, cerrar y recuperar un borrador conserva todas las facturas adicionales y su distribución;
- ningún documento adicional crea un `DocumentProcessingJob` o writer paralelo para simular una segunda recepción.

## Facturas agregadas

La captura usa una sola acción **Agregar factura**. El mismo documento permite
elegir el tipo de soporte y añadir flete, seguro, arancel, agencia de aduanas,
manejo, IVA de importación u otro costo directo. “Agregar nacionalización” no
es otro proceso: era una segunda entrada al mismo editor y se eliminó. El tipo
`ImportDeclaration` se conserva porque habilita la base aduanera del IVA y la
clasificación fiscal correspondiente dentro de la factura.

## Captura operativa y confirmación

La tabla de captura muestra **Costo compra** por unidad: mercancía neta, IVA que
sea mayor valor y costos adicionales capitalizables ya prorrateados. La vista
previa se recalcula mientras se edita cada factura y usa las mismas reglas de valor,
cantidad, peso, volumen, reparto igual o manual que el calculador autoritativo del
backend. La confirmación siempre recalcula y valida el snapshot antes de persistirlo.

El selector compartido de productos mantiene visible la opción activa al navegar
con flechas. En recepción, Enter agrega y enfoca cantidad; izquierda/derecha recorren
cantidad, costo unitario y descuento, arriba/abajo conservan la columna, y Enter
regresa al producto.

Aceptar una recepción persiste primero encabezado, líneas, payload y
`DocumentProcessingJob` en una sola transacción. Después intenta publicar la señal
directamente con el sender canónico de Service Bus, con un presupuesto máximo de
dos segundos; no existe un buffer intermedio en memoria. Si el broker no confirma
dentro de ese presupuesto, la respuesta no pierde el documento ya aceptado: el
hosted service canónico vuelve a publicar los jobs pendientes desde
`DocumentProcessingJobs`. Conteos, ajustes, averías, conversiones y ambos extremos
de un traslado comparten el mismo publicador; no se crea otra tabla, otro motor,
otro writer ni otro servicio desplegable.
La confirmación carga reglas y perfiles de retención una sola vez para los proveedores de la factura principal y de todos los documentos adicionales. Cada retención se calcula luego en memoria con el plan congelado; agregar documentos no aumenta el número de lecturas SQL de retención.

## Ampliación 2026-10-20: asociar comprobantes y cargos posteriores

### Decisiones cerradas y límites

Compras sigue registrando productos, cantidades, bodega y recepción. El editor
contable no recibe productos ni simula entradas de inventario. Por aclaración
expresa del usuario se conserva **Agregar factura** con la captura asistida
actual y se añade **Agregar comprobante**, que selecciona uno ya contabilizado por
tercero, documento o concepto de costo. Son dos formas de preparar o seleccionar el mismo
origen financiero; no dos contabilizaciones de la misma factura.
La factura principal de mercancía conserva su captura asistida y fuente nativa;
su valor ya incluido en el costo de mercancía no se vuelve a sumar como cargo.
No se rediseña aquí la orden de compra ni la facturación de productos.

El modal de **Agregar comprobante** devuelve una página con importe, tercero, estado,
conceptos elegibles y disponibilidad para reparto. El saldo por pagar se muestra
aparte: una factura pagada puede repartirse. Borradores o fuentes financieras
pendientes no se usan como costo definitivo. Asociar un comprobante no crea otro
gasto, otra CxP, otro soporte fiscal o pago. La fuente puede provenir de Gastos,
comprobante manual o cargo legado de compra, sin privilegios por pantalla de origen.

**Ajuste de selección del 26 de octubre:** la búsqueda por tercero muestra una
tabla paginada sin rango de fechas. Cada fila identifica el documento con tercero,
descripción/observación, cuenta y concepto de costo, importe y disponible. Solo
aparecen comprobantes contabilizados con al menos una partida de costo elegible;
un asiento de reclasificación producido por un reparto anterior no vuelve a ser
origen. Al seleccionar se consulta el detalle existente, y se muestran únicamente
las partidas elegibles para distribuir, no la obligación, el banco o el IVA.
Un comprobante con varios conceptos conserva cada partida de costo elegible.
El importe del asiento y su obligación no cambian al asociar una parte del costo.
Por peso, cada producto debe tener peso positivo: la captura permite completarlo
antes de agregar el comprobante o informa que el reparto no está disponible.
La vista previa por producto refleja tanto el reparto válido como el descuento
financiero anticipado; el motor de compras sigue recalculando y validando ambos
al confirmar. La casilla de descuento pactado sin anticipación permanece
informativa hasta la decisión posterior en el pago.
El descuento financiero se captura en una sección expandible siempre disponible:
escribir un porcentaje registra el acuerdo y la casilla independiente determina
si se anticipa en el costo. Sin porcentaje no se registra descuento.

**Agregar factura** conserva proveedor, respaldo/número, conceptos, importes,
impuestos, retenciones y reparto actuales. Al confirmar genera su comprobante
financiero por debajo, visible en Trazabilidad. No obliga al usuario operativo a
conocer el PUC. Guardar el borrador de recepción conserva sus facturas sin
contabilizarlas. La aceptación final crea cada fuente con su identidad estable
una sola vez, como ya hace el recorrido nativo de documentos de costo.

El contrato distingue captura nueva de referencia existente, mutuamente
excluyentes para cada asociación. Una referencia existente no contiene otra
factura editable. Cambiar proveedor, importe o impuesto de un comprobante ya
aceptado exige su corrección en el propietario financiero. En una misma recepción
se pueden combinar varias facturas nuevas y comprobantes existentes.

La detección de duplicados compara tenant, proveedor, tipo y número normalizado
antes de aceptar; un documento existente ofrece vincularlo, no recrearlo desde
**Agregar factura**. Doble clic o retry conserva el mismo ID de fuente. La factura
creada de forma asistida puede después repartirse en otra recepción con el mismo
presupuesto global. La UI conserva las dos acciones, sin duplicar formularios
tributarios ni añadir una tercera captura de «otros cargos» con reglas diferentes.

### Cuentas automáticas de la captura asistida

«Automáticas» significa configuradas y resueltas por el motor, no una cuenta
genérica que oculta la operación. Se reutilizan `AccountingSourceCategoryMappings`
y `AccountingAccountMappings` con su alcance/vigencia, y las dimensiones del PUC
del nuevo diseño:

| Hecho | Cuenta propuesta por configuración |
| --- | --- |
| Mercancía recibida | Inventario según categoría/regla nativa; servicios/no inventariables conservan su clasificación de gasto |
| Flete u otro concepto adicional | Categoría `PurchaseCostKind` y tratamiento configurado de inventario/gasto; respeta los débitos ya reconocidos en la recepción |
| IVA descontable | Auxiliar configurado de impuesto, solo cuando el snapshot tributario lo permite |
| Retenciones | Cuenta resuelta por tipo/regla o ajuste manual autorizado del snapshot tributario |
| Neto de proveedor | Auxiliar de CxP, con tercero y sede; independiente de contado/crédito |
| Pago posterior | Caja/banco y medio seleccionados en el egreso, compatibles con el PUC |

Para captura asistida, un default particular de proveedor se usa solo si existe
una parametrización explícita del mismo propietario; no inferirlo de su último
pago. En ausencia de override, se utiliza el mapping vigente de categoría/sede
y luego el default de tenant según el resolver canónico. El resultado debe ser
único. Las cuentas elegidas y versiones se congelan al aceptar; el worker no
cambia la cuenta si después se modifica el default.

El usuario sin función contable ve conceptos y resultado, y el contador puede
revisar las partidas. Cambiar cuentas requiere los permisos del editor contable.
Si contabilidad está activa y falta una cuenta obligatoria, la confirmación nueva
devuelve diagnóstico concreto antes de aceptar; no usa una cuenta provisional
oculta ni supone banco por marcar contado. Los trabajos históricos ya aceptados
con falta de configuración conservan su diagnóstico y reintento canónicos.

Una factura asistida capitalizada al confirmar la recepción reutiliza su asiento
nativo: no agrega después una segunda capitalización. Un comprobante externo ya
contabilizado conserva sus partidas originales; el efecto de valoración de su
asociación sigue el tratamiento de valoración y cierre técnico descritos abajo.

Si se captura una factura de 5 millones y se reparten inicialmente solo 3, el
comprobante igualmente reconoce los 5 completos. La porción no asignada conserva
su clasificación y cuenta de origen configuradas; no desaparece del débito ni
se convierte en otra obligación de 2 millones. El asiento identifica por separado
lo capitalizado y el resto, y debe cuadrar con impuestos/retenciones y neto.
Una asignación posterior usa ese mismo origen y saldo distribuible. La política
de reclasificación/valoración descrita abajo gobierna el cambio entre esas cuentas;
no se resuelve inventando una cuenta automática de pago.

### Compra, obligación y egreso: contado deja de significar pagado

**Causa anterior comprobada en código, sin inspección de datos productivos:**

- El selector actual de recepción traduce Contado a `CreatesPayable=false` y
  presenta «Total pagado», aunque no solicita cuenta bancaria, caja ni evidencia
  de un pago.
- `SqlGoodsReceiptDocumentHandler` solicita `AccountingEntryOnly` en ese caso.
  Si el tenant no tiene contabilidad efectiva para esa fecha, el writer no crea
  fuente/job; no existe un efecto comercial de cartera que conservar.
- Con contabilidad activa, `LoadGoodsReceiptFactsAsync` devuelve
  `SettlementSourceMissing` al encontrar `CreatesPayable=false`: no dispone de
  obligación ni de evidencia de liquidación a la cual acreditar. Añadir un
  mapping por sí solo no resuelve esa rama.
- `ApplyGoodsReceiptFinancialEffectsAsync` tampoco abre CxP para ese payload.
  Por tanto, no debe afirmarse que esas compras ya se pagaron o contabilizaron.

**Decisión de destino:** confirmar una compra con neto positivo genera su
comprobante de adquisición y su CxP por el neto, incluso si el acuerdo comercial
es de contado. Para el comprador es cuenta **por pagar**. El egreso es otro
documento que aplica contra esa posición. Puede registrarse el mismo día; separar
documentos no obliga a esperar una fecha posterior.

Ejemplo sin impuestos:

| Acción | Débito | Crédito |
| --- | --- | --- |
| Confirmar compra por 1.000.000 | Inventario 1.000.000 | CxP proveedor 1.000.000 |
| Confirmar egreso por 1.000.000 | CxP proveedor 1.000.000 | Banco seleccionado 1.000.000 |

El resultado es inventario 1.000.000, salida bancaria 1.000.000 y saldo de esa
obligación cero, con ambos documentos enlazados. Si hubo anticipo, el comprobante
contable aplica ese anticipo contra la CxP; no se produce otro egreso. Si el neto
es cero por tratamiento válido, se contabilizan las partidas correspondientes
sin crear una obligación ni pago artificiales por cero.

En la UI se retira la interpretación «Forma de pago Contado/Crédito» que evita
crear obligación. Puede conservarse **Condición de compra: contado/crédito**:
contado propone vencimiento en la fecha de factura y crédito conserva el plazo
capturado. Ninguna de las dos registra salida de dinero. Mostrar total, neto de
proveedor y estado/saldo derivados; «Pagado» solo cuando las aplicaciones lo
demuestren. El detalle ofrece acceso al comprobante y, al existir su obligación,
**Registrar egreso**, que abre el editor/wizard financiero propietario con la
posición seleccionada. No confirma automáticamente ni presupone banco.

La recepción y el egreso conservan identidades, autorización y aceptación propias.
Si falla/cancela el pago, la compra sigue válida y pendiente de pago. Si el motor
financiero aún no ha creado la posición, la UI muestra el estado pendiente y no
admite pagar un ID inexistente. Cada egreso valida el saldo disponible incluyendo
reservas aceptadas, como cualquier otro pago. El usuario operativo puede seguir
usando la captura asistida de pagos existente; no necesita digitar un asiento.

El contrato nuevo separa condición/vencimiento de compra y pago. Para nuevas
compras no ofrece un booleano que suprima la obligación. `CreatesPayable` se
conserva en snapshots y lectores legados, sin reinterpretar su significado.
Durante la transición, endpoints antiguos rechazan **nuevas** confirmaciones
`CreatesPayable=false` con instrucción de registrar compra y egreso; replay de una
aceptación previa devuelve su resultado histórico sin mutarlo. Los borradores
viejos de contado requieren revisión y nueva confirmación de esta condición.

**Implementación parcial 2026-10-21:** la captura nueva ya no ofrece el selector
que omitía la obligación. Conserva el vencimiento editable, incluido el mismo
día de emisión, y envía `CreatesPayable=true` al contrato compatible. Tanto guardar
como confirmar utilizan ese contrato; no hay una rama de contado que presuponga
salida de caja o banco. El detalle histórico muestra neto del proveedor, sin
llamarlo «Total pagado». El store rechaza una nueva aceptación legacy con
`CreatesPayable=false` después de buscar un replay, antes de numerar o escribir
recepción, inventario o trabajo. El replay histórico conserva su resultado y los
payloads/procesadores antiguos no se reinterpretan. Las compras nuevas siguen
por el mismo `SqlAccountingPostingProcessor`: no se añadió un escritor de CxP,
asientos o pagos. Esto aún no implementa el editor ampliado de egresos ni la
regularización de históricos descrita abajo.

La captura conserva el control optimista del borrador: ante 409 mantiene lo
digitado y solicita revisar la versión actual. No vuelve a leer una versión
ajena para sobrescribirla automáticamente con la captura anterior. Esta regla
reutiliza la versión y el conflicto del store existente; no introduce otro
guardado ni recuperación de compras.

Con contabilidad desactivada, la compra sigue creando fuente/trabajo para sus
efectos comerciales y CxP (`PreserveCommercialEffects`); no se presenta como
asiento contabilizado. Con contabilidad activa y fecha cubierta por activación,
el motor aplica CxP y asiento juntos. Contado/crédito deja de decidir si se crea
el trabajo financiero. La misma regla aplica a las facturas adicionales nuevas;
vincular un comprobante existente conserva la obligación que ya tiene.

La regresión dirigida posterior verificó las dos capturas de pago para una
compra con vencimiento en la fecha de emisión: Cuentas por pagar y comprobante
manual de egreso. Ambas usan el mismo participante de aceptación y aplicador
del motor contable; dejan la obligación en cero, una aplicación y un asiento
del pago. Repetir la recepción y el pago conserva esas cantidades y un único
movimiento de inventario. Los dos casos pasaron en la ronda de 16 pruebas
SQL/API del 23 de octubre. El caso sin contabilidad activa también pasó y
conserva la obligación comercial; esa evidencia no sustituye la conciliación
histórica ni certifica los cargos posteriores pendientes.

**Detalle y egreso, verificación local del 24 de octubre:** `GetDetailAsync`
proyecta la obligación principal desde `Payables`: identidad, saldo, estado,
moneda, vencimiento y sede del proveedor. No calcula otro saldo con los totales
de la compra. El join amplía el primer comando existente; el detalle conserva
tres comandos SQL. La unicidad de fuente/partida y el constraint que reserva
partidas explícitas para fuentes manuales garantizan como máximo una obligación
nativa de la mercancía. Tenant, sede y proveedor limitan esa lectura.

Con permiso de pago y una obligación procesada con saldo, **Registrar egreso**
abre `PortfolioPaymentWizard`, el mismo asistente de CxP y Gastos. La compra
principal queda seleccionada y los cargos conservan sus obligaciones propias.
Una compra sin obligación disponible no fabrica un identificador ni ofrece
pagarlo. Los egresos de moneda extranjera mantienen el límite del caso de uso
nativo: no se anuncia su disponibilidad en este acceso. Cambiar tenant/sede
descarta el detalle y la captura de pago de la selección anterior. El pago no
vuelve a consultar ni remonta la recepción; reabrir el detalle es una lectura
explícita de su saldo autoritativo.

La aceptación compartida `accounting.PortfolioPaymentAccept` también produce
el diagnóstico «El pago no se realizó» para conflictos de obligación/saldo.
El adaptador manual conserva ese mensaje sin agregar otro prefijo. No cambia
la reserva ni el aplicador del motor, y un rechazo ocurre antes de persistir
el nuevo pago. La regresión amplía el caso de contado a cuatro combinaciones:
pago total por cada captura y abonos alternados entre ambas. Repite cada intento,
rechaza el exceso sin fuente/pago/asiento, conserva el saldo y comprueba una sola
salida de dinero por aplicación. Las lecturas del detalle verifican también los
tres comandos antes y después del pago.

El recorrido real de navegador aprobó recepción con descuento y cargo asociado,
devolución parcial, egreso de 1.000 desde el detalle y replay: saldo de 54.000 a
53.000, asiento balanceado, lectura única al reabrir y ninguna recarga automática
de la recepción por pagar. Los siete casos reales de comprobantes, cartera,
gastos, compras, pedidos con stock y POS pasaron en 4,7 minutos. La revisión de
la captura comprobó saldo, documentos y acceso al egreso. Ese recorrido detectó
y corrigió la habilitación temprana de opciones de impresión en el diálogo
compartido, documentada en [impresión POS](pos-printer-configuration-design.md).
Esta evidencia no habilita cargos posteriores, descuentos financieros ni la
conciliación de recepciones antiguas todavía pendientes de implementación.

La regresión SQL/API ampliada aprobó 107 casos de compras, CxP/CxC y aceptación
contable. Su primer intento ejecutado aprobó 106: una importación de cartera
antigua agotó 30 segundos. Pasó aislada y la repetición completa aprobó 107/107,
sin aumentar timeouts ni cambiar la importación; su aserción ahora conserva el
comando SQL para diagnosticar otra falla. Después pasaron de nuevo los cuatro
casos de contado con el presupuesto explícito: ocho lecturas del detalle,
tres comandos por lectura y menos de dos segundos cada una, sin build,
navegador ni diagnóstico concurrentes. No es una medición de carga productiva.

La auditoría posterior de este tramo siguió cada acceso hasta el mismo
`PortfolioPaymentAccept` y `SqlAccountingPostingProcessor`. No agregó servicio
de pago, writer, tabla, job, catálogo ni cola. La única selección nueva lee la
posición existente con las claves de tenant/sede/proveedor; no escribe importes
en Purchasing ni cambia sus snapshots o hashes. Las reservas, autorización,
concurrencia e idempotencia siguen en sus propietarios y las pruebas verifican
rechazo/replay sin efectos nuevos. La vista mantiene un propietario de detalle,
sin polling, GET por partida, recarga general ni nueva consulta al pagar. El
join no multiplica filas por estar respaldado por la unicidad y constraint de
fuente nativa. El cambio es aditivo en la respuesta de detalle; un lector
anterior ignora ese campo, y la ausencia de posición no se sustituye por datos
inventados. La auditoría global del rediseño y las capacidades pendientes
enumeradas en sus secciones siguen abiertas.
Los 80 controles enfocados de arquitectura, dominio de compras, liquidaciones
y replay de devoluciones pasaron después de esa revisión.

**Recepciones históricas de contado:** el cambio no puede darlas por impagadas ni
pagar otra vez a sus proveedores. El corte requiere un informe acotado de fuentes
sin job, trabajos con `SettlementSourceMissing`, asientos existentes, obligaciones,
pagos/anticipos y posibles inclusiones en apertura. La evidencia determina la
regularización autorizada: reconocer la compra omitida y enlazar su pago real, o
conservar el reconocimiento de apertura que ya la cubre. No inventar banco,
reemitir soporte, editar payload/hash ni reprocesar inventario. Un retry aislado
no arregla la falta de evidencia de una fuente inmutable. La implementación de
esa regularización se define sobre los casos del informe antes de ejecutar datos;
esta decisión no autoriza un backfill masivo ni marca solucionado el histórico.

### Modos de asociación e importe distribuible

Solo hay dos modos de asociación:

| Modo | Contrato |
| --- | --- |
| Solo asociar | Conserva vínculo y observación; no cambia costo ni consume importe distribuible |
| Prorratear | Selecciona importe de conceptos elegibles y método; distribuye entre todos los productos de la recepción |

Se conserva `GoodsReceiptCostCalculator`: valor, cantidad, peso, volumen, igual
o manual, según las opciones actuales. Peso utiliza peso del producto por
cantidad; no se crea otra fórmula. El reparto manual debe cubrir exactamente el
importe. No se introduce un selector nuevo de subconjunto de productos.

La asignación toma como tope los **conceptos de costo** del comprobante, con IVA
capitalizable cuando corresponda; IVA descontable, retenciones y líneas de banco
o CxP no son cargos distribuibles. No usar suma de débitos y créditos como valor
de factura. En comprobantes libres, el contador identifica las partidas/conceptos
de costo; el motor valida su tratamiento y trazabilidad. La recepción no cambia
el tratamiento tributario congelado para conseguir un importe mayor.

Ejemplo: concepto elegible de flete por 5.000.000; se asignan 3.000.000 a A y
2.000.000 a B. Disponible = 0, aunque siga debiéndose todo al transportador.
Asociarlo informativamente a C no consume nada. Solicitar otro reparto positivo
falla. Un pago de 1.000.000 cambia la deuda a 4.000.000 y no libera reparto.

### Persistencia mínima y migración

1. Reutilizar `GoodsReceiptCostDocuments` como asociación a recepción: su
   `CostDocumentId` conserva identidad histórica, pero el destino agrega
   `SourceDocumentId`, `SourceDocumentType`, `AssociationMode`, revisión y versión.
   El documento financiero es la fuente enlazada. **Agregar factura** conserva
   los campos de captura asistida hasta congelar su fuente; **Agregar comprobante**
   solo referencia la fuente existente y no copia su encabezado como otra factura
   editable. El modelo permite ambas entradas con exclusión validada y completa
   el vínculo al aceptar la captura nueva. La unicidad proveedor/número debe migrarse: la identidad fiscal
   se controla en su origen; varias asociaciones pueden apuntar a él.
2. `GoodsReceiptCostLines` pasa a referenciar identidad de concepto/partida fuente
   e importe elegido, con moneda original y equivalente funcional congelados.
   Una asociación puede seleccionar varios conceptos; su suma es el importe a
   repartir. Una partida de origen no se renumera al editar otra fila.
3. `GoodsReceiptCostAllocations` conserva el reparto por línea recibida, incluyendo
   vínculo a asociación/revisión y origen. No se cambia a un reparto por saldo
   pendiente de pago. Las filas aplicadas no se borran ni se sobrescriben.
4. Registrar revisiones de costos dentro del agregado Purchasing, con ID de
   comando, número de revisión por recepción, autor, fecha, motivo, asociaciones,
   asignaciones y versión esperada. Son historia de la recepción, no otra tabla
   de trabajos. Su efecto de preparación usa el propietario de Pricing existente.
5. No guardar otro «total repartido» editable en la factura. Calcular consumido
   por suma de asignaciones vigentes y aceptadas pendientes. Índice por fuente,
   concepto y estado/revisión, además del índice de detalle por recepción.

Las claves/FK demuestran pertenencia al tenant y sede, fuente/partida y recepción.
No se permiten cargos entre tenants. El alcance inicial exige la misma sede de
origen para recibir el cargo; no crea compensaciones intersede implícitas. Una
moneda extranjera se distribuye con la tasa histórica de la fuente, sin recalcular
deuda al tipo de cambio de la recepción. Mantener escalas actuales de cantidades,
costos e importes; residuos de redondeo pertenecen al calculador canónico y suman
exactamente al monto elegido.

Los cargos legados ya crearon su fuente/CxP: el backfill enlaza esa identidad y
sus asignaciones existentes. No reprocesa recepciones ni acepta otra fuente.
Una asociación ambigua queda en informe de migración, no se deduce por parecido
de números. Solo la captura asistida nueva acepta su fuente financiera una vez;
la referencia a un cargo ya enlazado nunca vuelve a crear su CxP. Ambas entradas
convergen en el mismo motor y contrato de aplicación, sin duplicar writers.

### Slice local: comprobantes existentes en la recepción inicial

La implementación en curso agrega `ReferencedCostDocuments` al mismo comando,
borrador y payload de recepción. Reutiliza `GoodsReceiptCostDocuments`,
`GoodsReceiptCostLines` y `GoodsReceiptCostAllocations`: una referencia guarda la
FK al asiento inmutable y, al distribuir, la FK a sus partidas y el importe
funcional elegido. Los campos de captura de factura quedan nulos en esa forma;
no se copian encabezados, impuestos, importes de factura ni obligaciones nuevas.
`Associate` no tiene partidas monetarias y no consume presupuesto. Los cargos
capturados por **Agregar factura** conservan su forma y writer anteriores.

El selector pagina los comprobantes contabilizados con el lector contable
canónico y sus filtros por tercero/fecha/búsqueda. La consulta de partidas de un
comprobante y la aceptación por lote comparten
`purchasing.ReceiptCostVoucherLines`. El disponible se calcula desde el débito
original elegible menos las asignaciones aceptadas de Purchasing; los pagos no
participan. Las partidas de gasto identificadas por el contador pueden ser
capitalizadas; activos ya capitalizados, bancos, impuestos descontables,
retenciones y cuentas de control no se ofrecen como un costo nuevo. El traslado
desde un activo transitorio usa su clasificación explícita
`UnallocatedPurchaseCost` (**Costos de compra por distribuir**) del catálogo de
funciones del PUC. Es un auxiliar de activo y naturaleza débito, sin dimensiones
de banco o medio de pago. Conserva código libre, sede opcional y los controles
de primer uso existentes; no asigna una cuenta por prefijo ni modifica auxiliares
históricos utilizados. Un saldo anterior se clasifica mediante un comprobante
contable explícito, sin reetiquetar movimientos históricos.

Solo las partidas de este activo que provienen de un comprobante manual son
elegibles como costos todavía no incorporados al inventario. La recepción
reclasifica desde la cuenta original con el mismo posting de costos existentes;
no aumenta la deuda ni crea otro pago. Los activos generales permanecen fuera
del reparto. Los documentos adicionales que la recepción ya capitalizó tampoco
se ofrecen otra vez, incluso si alguien configuró Inventario con ese mismo
auxiliar transitorio. El presupuesto sigue en las asignaciones existentes de
Purchasing y el saldo del activo sigue en el mayor: no se almacena otro total.
Su conciliación y clasificación al cierre utilizan esos mismos comprobantes y
libros, sin un proceso automático nuevo.

La elegibilidad también respeta el requisito vigente de tercero de la cuenta.
Si se activa ese requisito después de contabilizar una partida que no tenía
tercero, esa partida no se ofrece ni se acepta para un reparto nuevo. Consulta
y confirmación comparten esa validación en `ReceiptCostVoucherLines`; no se
inventa el tercero ni se acepta inventario para dejar su reclasificación pendiente.

**Verificación local del activo transitorio, 25 de octubre:** la prueba SQL/API
rechaza un activo general, una partida sin el tercero requerido, el exceso sobre
el disponible y un costo ya capitalizado por la recepción. Distribuye 3.000 y
2.000 de una misma partida de 5.000, conserva su deuda de origen y soporta replay
sin duplicar efectos. El requisito de tercero tuvo una regresión roja antes de
corregirse en el lector canónico. Las pruebas de dominio validaron tipo,
naturaleza, auxiliar de movimiento y ausencia de dimensiones de tesorería; el
esquema reforzó esa misma forma.

Los dos recorridos reales de navegador, con gasto asistido y activo transitorio,
pasaron captura, descuento comercial, guardado/recuperación, reparto parcial,
confirmación, devolución, pago y replay. La vista del PUC creó un código libre
y seleccionó la función persistida; el modal mostró cuenta, tercero, ambos lados
y disponible. Tipos y lint pasaron. La actualización del DACPAC de la base
descartable local conservó sus 16 movimientos de tesorería anteriores.

La medición inicial superó el presupuesto con presión de memoria reportada por
SQL. Se repitió sin navegador, builds ni diagnóstico SQL concurrentes, con
LocalDB exclusivo limitado a 384 MB y un límite temporal reversible de 256 MB
en el renderer de Codex. Se conservaron los gates de 2 s de aceptación y motor,
y 800 ms de consulta; no se elevó ningún umbral. Con veinte muestras, veinte
productos y 1/100/499 partidas de origen, aceptación/motor/consulta p95 fueron,
en milisegundos:

| Origen | Partidas | Aceptación | Motor | Consulta |
| --- | ---: | ---: | ---: | ---: |
| Gasto | 1 | 29,0 | 257,0 | 3,4 |
| Gasto | 100 | 110,3 | 294,6 | 9,8 |
| Gasto | 499 | 1.024,8 | 1.000,6 | 50,6 |
| Activo por distribuir | 1 | 126,5 | 450,1 | 2,8 |
| Activo por distribuir | 100 | 672,2 | 479,7 | 11,5 |
| Activo por distribuir | 499 | 811,5 | 792,7 | 33,8 |

El cargo mantuvo dos comandos SQL y su consulta uno; la aceptación completa
utilizó 18–19. La auditoría de este slice verificó el índice existente de
presupuesto, bloqueo transaccional compartido, permisos/tenant, replay, un solo
lector de elegibilidad y los mismos writers de inventario y contabilidad. No
agregó una tabla, saldo, cola, motor, clasificación por prefijo ni carga paralela
en la interfaz. Esta evidencia corresponde a la asociación inicial; los cargos
posteriores y los otros pendientes del diseño conservan su alcance separado.

Una distribución inicial no se reconoce antes de la fecha contable de su
comprobante fuente. La comparación usa la fecha del asiento, igual que períodos;
un vínculo informativo sí puede referir un documento posterior. Un cargo
reconocido después pertenece a la revisión posterior, sin backdate implícito.

La aceptación materializa primero los orígenes autorizados con clave ordenada
por documento/tipo; sus partidas elegidas se parsean una vez por comprobante y
se buscan por clave, sin volver a expandir su JSON por cada partida del asiento.
Toma los bloqueos del gasto y de su fuente en el mismo orden que
anulaciones y devoluciones de cargos de venta. El presupuesto se valida bajo ese
bloqueo antes de numerar y persiste junto al job documental existente. La
reclasificación de la recepción reconoce inventario contra la cuenta de gasto
original, por la parte recibida que administra inventario, dentro del único
`SqlAccountingPostingProcessor`. La parte de productos sin inventario conserva
su gasto original. La valoración de inventario negativo usa el ajuste canónico
actual; no se agrega otra deuda, IVA, pago, fuente o job por la asociación.

Compatibilidad y corte: los cargos capturados mantienen su forma y las recepciones
históricas conservan la verificación de su firma anterior. El número enviado por la UI es solo
una pista de presentación; no identifica la fuente ni cambia la firma financiera.
El snapshot publicado toma siempre el número del asiento. Los lectores que tratan
un cargo como fuente financiera filtran `SourceEntryId IS NULL`: una asociación
no vuelve a participar en activaciones fiscales, jobs, retenciones o fuentes por
una coincidencia de GUID. La consulta de duplicados conserva ese mismo predicado
para utilizar el índice único de facturas capturadas.

Antes de habilitar esta captura, todos los workers documentales y contables deben
comprender las referencias nuevas. Una vez aceptada una, el rollback de UI puede
retirar el botón, pero debe conservar workers compatibles y las asignaciones;
un binario anterior que ignore el campo podría omitir la reclasificación. No se
borran referencias ni se reprocesa inventario para volver atrás. Este corte no se
ha desplegado a ningún entorno remoto.

Presupuesto de aceptación de este slice: hasta 100 comprobantes y 500 partidas,
una lectura SQL agrupada adicional y el mismo writer por lote de cargos. La
cantidad de viajes no crece por comprobante o partida. La consulta de un
comprobante realiza un viaje; el listado conserva la paginación y presupuesto
del lector contable. Objetivo local de referencia: consulta menor de 800 ms y
confirmación de la recepción menor de 2 s en caliente, sin carga de diagnóstico
concurrente. La aceptación HTTP y el procesamiento documental/contable se miden
por separado: el transporte determinista de tests normalmente ejecuta los motores
dentro de `PublishAsync`, mientras el transporte real solo acepta su activación.
La prueba de volumen pausa únicamente ese transporte, mide la aceptación, y luego
ejecuta el mismo `DocumentProcessingWorker` y su continuación contable, con un gate
independiente de 2 s; no omite sus efectos. La consulta del presupuesto mantiene
su gate de 800 ms. La primera medición conjunta de 499 conceptos y 20 productos
dio p95 de 3.095,1 ms: ese dato no se presenta como aceptación HTTP aislada ni como
cumplimiento del presupuesto. Las partidas contables de reclasificación se
agrupan dentro de cada fuente por cuenta, tercero y centro, conservando el detalle
de las asignaciones en Purchasing.
El snapshot nuevo de cada asignación conserva producto de recepción, factor e
importe. La partida fuente y el método pertenecen a su línea contenedora; no se
repiten por cada producto. No cambia el contrato de facturas capturadas existente.
Capturas y asociaciones no comparten la misma clave de encabezado, y un reparto
manual incompleto o con importes de más de cuatro decimales se rechaza antes de
persistir, evitando errores de PK o diferencias con la escala SQL.
La verificación funcional, de concurrencia, autorización,
compatibilidad y rendimiento de esta implementación aún está en curso. No
habilita revisiones posteriores de costos ni declara terminada la trazabilidad
FIFO, el descuento financiero, la regresión global o la auditoría completa.

Medición aislada del snapshot compacto, 23 de octubre: una entrada de
calentamiento y veinte muestras por volumen, siempre con veinte líneas de
producto. Los percentiles 95 de aceptación / motores / consulta fueron:
1 concepto: 443,7 / 1.555,6 / 4,1 ms; 100 conceptos: 455,8 / 1.197,0 / 9,9 ms;
499 conceptos: 1.180,2 / 1.583,3 / 41,1 ms. La aceptación ejecutó 17–18 comandos
totales, incluidos autorización/contexto, con exactamente dos comandos propios
del cargo en los tres volúmenes. El lector de disponible ejecutó uno. Son muestras
locales, no un certificado de carga productiva. El test verifica cada reparto
persistido y la deuda original, y ejecuta los motores después de aceptar.
La regresión SQL/API ampliada pasó 156 casos, incluido el cambio de las tablas
de costos desde su forma anterior con factura capturada, conservación de CxP,
inventario y asientos, y replay sin efectos. Esa prueba conserva el resto del
esquema actual: no pretende certificar todas las versiones históricas de la base.
La prueba enfocada de pago confirma que cancelar la deuda del gasto no libera
su importe ya distribuido.

La regresión visual contra API y SQL locales pasó el 23 de octubre: crea un gasto
asistido de 5.000, selecciona su asiento por tercero en la recepción, distribuye
3.000, guarda y reabre el borrador, confirma y consulta inventario, asiento y CxP.
El gasto conserva su obligación de 5.000 y el disponible conserva los 3.000
asignados. También prueba teclado, descuento comercial y calendarios existentes.
La auditoría visual detectó que cerrar la búsqueda de productos al perder foco
desplazaba el botón de costos durante el clic. La lista ahora se presenta como
desplegable fuera del flujo del formulario; conserva consultas, selección y
teclado. El recorrido verifica que un solo clic abre la sección de costos.
Esto no certifica todavía la regresión global ni las revisiones posteriores.

La auditoría de reintentos encontró otra causa reproducible: la firma anterior
incluía cálculos derivados del peso maestro y la configuración de retenciones.
Repetir una recepción aceptada después de cambiar el peso producía 409; incluso
sin cambios se volvía a cargar ese maestro. La regresión falla en ambos casos
con el binario previo. Las nuevas aceptaciones firman los hechos de la solicitud
con formato v2 y conservan **el mismo `GoodsReceipts.PayloadHash`**, sin otra
columna ni fuente. Excluyen el token de borrador, la prueba temporal de revisión,
el peso unitario impuesto por el servidor y la etiqueta del asiento relacionado.
Los importes, ajustes tributarios y referencias económicas sí pertenecen a la firma.

Una consulta acotada por tenant/sede e identidad comprueba esa firma antes de
cargar configuración mutable. La aceptación serializada vuelve a comprobarla
bajo bloqueo para cubrir inserciones concurrentes. Esa segunda lectura tiene
una razón de consistencia; no es una resincronización del frontend. Solo ante
una firma diferente se verifica explícitamente la forma calculada v1 histórica,
sin reescribirla ni inventar sus hechos originales. Esta compatibilidad no
reconstruye una solicitud histórica si ya cambió su configuración y dejó de
coincidir con aquella firma. El payload documental inmutable conserva su hash
y sus motores actuales. Un replay de recepción procesada no publica otra señal;
uno pendiente conserva la activación existente por si el primer publish falló
después del commit. No crea trabajos, recuperadores o reintentos automáticos.
El rollback debe conservar un lector compatible con las firmas nuevas, además
de los workers que comprenden asociaciones. La compilación Release y 49 pruebas
SQL/API de recepción, borradores, firmas antiguas, contado, retenciones y
asociación pasaron. La medición aislada posterior, con veinte muestras y veinte
productos por volumen, dio p95 de aceptación / motores / consulta de
33,0 / 277,6 / 6,4 ms para un concepto; 138,1 / 366,8 / 9,4 ms para cien;
665,4 / 601,3 / 29,5 ms para 499. Son mediciones locales, no una garantía de carga
productiva. La aceptación conserva dos comandos propios del cargo y 18–19 en
total, incluido el viaje acotado de comprobación de identidad. El lector de
disponible sigue ejecutando uno. El replay v2 completado evita la recarga de pesos
y otra señal. No se amplió ningún gate de tiempo para obtener estos resultados.

### Aceptación concurrente y edición posterior

El preview no reserva. Al confirmar la recepción o una revisión posterior se
validan y bloquean en orden estable las fuentes/conceptos afectados y la versión
de la recepción. Se calcula disponible bajo el mismo bloqueo y se persisten
asignaciones aceptadas junto con la aceptación durable correspondiente. Dos
recepciones que intentan consumir el mismo remanente no pueden excederlo. Las
asignaciones aceptadas pendientes consumen presupuesto; un retry con la misma
identidad no vuelve a consumir ni preparar precios.

Para **Solo asociar**, el comando de Purchasing persiste el vínculo versionado
sin job de inventario o contabilidad. Para una revisión que afecte costos, el
propietario acepta un documento/revisión por el motor documental existente y
conserva su identidad en `DocumentProcessingJobs`; no crea un worker o cola de
repartos. El handler se limita a los efectos autorizados para esa revisión.
El algoritmo de atribución requiere el cierre descrito abajo; no se implementa
un handler que actualice `InventoryBalances` directamente.

Confirmada la recepción, sus cantidades físicas permanecen inmutables. Revisar
costos no suma otra vez cantidades, no recalcula ventas históricas, no vuelve a
cumplir la orden de compra y no vuelve a abrir CxP. Mostrar costo vigente y acceso
a historia; no obligar al usuario a trabajar con una grilla de «costo inicial más
ajustes».

Las líneas sin peso/volumen válido no admiten ese método; se solicita completar
el dato logístico autorizado o elegir otro método existente. Cada revisión
congela los factores usados y sus versiones; cambiar el maestro después no
altera repartos históricos. Si la fuente se anula o reduce, su propietario valida
los importes ya consumidos antes de aceptar. No dejar asignaciones vigentes
mayores al concepto corregido ni eliminar asociaciones desde el motor fiscal.

### Preparación y publicación

Reutilizar el reemplazo de preparaciones pendientes ya existente. El esquema
`PriceRevisionProposals` tiene unicidad por sede, recepción y línea: agregar
`SourceRevision` a esa clave permite propuestas sucesivas de la **misma** línea.
La revisión inicial recibe valor estable 0; mantener FK a la línea recibida y
vínculo adicional a revisión para las posteriores. La novedad es la identidad de
revisión, no otro ciclo de vida de precios.

Una nueva preparación sustituye la pendiente anterior del producto y alcance de
precios. Propuestas publicadas quedan históricas. Se bloquea producto/alcance y
se compara la revisión vigente para que un trabajo antiguo entregado tarde no
sustituya una preparación nueva. `PricingService.PublishAsync` valida la versión
vigente, publica mediante su writer y conserva auditoría, catálogo y outbox.

El costo elegido para preparar precio depende de la configuración efectiva
actual (`Tenant.InventoryCostBasis`, cargada por el handler en su campo interno
`PriceFormationCostBasis`). No inferir un override por bodega por la existencia
de una columna con ese nombre: el handler actual no la consume. La revisión
no fuerza último costo cuando la política efectiva es promedio. Margen, IVA y redondeo
utilizan los calculadores existentes. Guardar/repartir no publica precio y un
reintento nunca repite una publicación. La distinción de costo comercial e
inventario permanece gobernada por
[la decisión prevalente de precios](../decision-prevalente-precios-costos-iva-publicacion.md).

### Aclaración y tratamiento del cargo sobre la valoración

**Estado al 2026-10-20, aclaración final del usuario:** «sin movimiento contable»
se refería a conservar la factura y su deuda, no a prohibir el asiento de
valoración. El ajuste contable necesario está admitido. No volver a solicitar
permiso por esa supuesta contradicción. Sigue siendo diseño, no implementación.

La revisión encontró una incompatibilidad real: `InventoryValuationCalculator`
obtiene el promedio del valor/cantidad del pool; una recepción ponderada exige
cantidad positiva. El runtime inicial no tenía una operación de valor sin
cantidad. La extensión `ValueAdjustment` del 25 de octubre conserva cantidad y
aplica el importe al valor del mismo pool por el writer existente; su evidencia
se registra en el propietario de inventario. Todavía falta conectarla a la
revisión de cargos con atribución y asiento. Añadir un cargo después no puede
reenviar la recepción, escribir un promedio suelto ni inventar un segundo
promedio comercial con el mismo nombre.

La asignación de 3 millones de una factura de 5 millones no cambia por sí misma
esa factura ni su deuda. Pero **capitalizar** parte de esos 3 millones en el
inventario sí cambia el activo y requiere su contrapartida. Esta diferencia
también debe respetarse cuando se asocia antes de confirmar una recepción un
cargo ya contabilizado en otra fuente: evitar tanto duplicar el activo como
aumentarlo sin su reconocimiento contable.

Recomendación presentada: mantener el reparto como vínculo de Purchasing y
reconocer explícitamente la valoración que corresponda a existencias actuales,
mediante los motores de inventario y contabilidad existentes. La fuente original
y la deuda no cambian; una reclasificación de costo, si procede, referencia esa
fuente. Esta recomendación se apoya en la distinción entre costo del inventario
y gasto reconocido al vender de [IAS 2](https://www.ifrs.org/issued-standards/list-of-standards/ias-2-inventories/),
consultada el 2026-10-20. La norma no define por sí sola el algoritmo de reparto
de esta aplicación.

El cierre técnico debe especificar en este propietario la
porción atribuible a existencias, tratamiento de agotados/negativos/devoluciones,
pool compartido, contrapartida y período. No cargar todo el flete a unidades
remanentes sin justificar su atribución. Los documentos y márgenes históricos
de ventas quedan intactos; ello no equivale a prohibir todo reconocimiento
contable corriente de costos correspondientes a mercancía ya vendida.

El runtime que debe extenderse es `InventoryValuationCalculator`, invocado por
`SqlInventoryLedgerWriter.PostBatchAsync`. Su pool incluye las bodegas vendibles
de las sedes activas que comparten precios cuando `SharesProductPrices=1`, y
excluye AVE. Una recepción que cubre inventario negativo ya separa adquisición
de cambio de valor, con diferencia en costo de ventas. No sustituir esa regla
por `min(cantidad actual, cantidad recibida)` ni por un cálculo por bodega.
El kardex conserva cantidades de la bodega antes/después y valores reconocidos,
pero no congela explícitamente cantidad total del pool ni modo de valoración.
Las recepciones aceptadas antes de conservar ese origen no admiten un ajuste
automático de valoración: no se infiere que todo lo disponible pertenece a la
recepción ni se reconstruye el saldo operativo sumando movimientos.
`InventoryBalances` continúa siendo la autoridad del saldo actual.

**Alcance aclarado el 2026-10-26:** el cargo posterior actualiza la valoración
de las existencias trazables que siguen disponibles y prepara el costo y precio
futuros. No modifica una transformación, conversión, producción o venta ya
realizada. Una recepción anterior a la trazabilidad que no conserva su origen
no admite valoración automática: se informa el motivo antes de aceptar el
reparto. No se inventa la cantidad remanente a partir del saldo actual ni se
implementa una conciliación histórica en este cambio. La asociación sin
reparto conserva su uso informativo.

Contraste técnico consultado el 2026-10-21: el módulo de
[costos adicionales de Odoo 19](https://raw.githubusercontent.com/odoo/odoo/19.0/addons/stock_landed_costs/models/stock_landed_cost.py)
calcula el ajuste de inventario a partir de la cantidad remanente del movimiento
de origen y referencia la factura del proveedor por separado. Su
[modelo de movimientos](https://raw.githubusercontent.com/odoo/odoo/19.0/addons/stock_account/models/stock_move.py)
obtiene esa cantidad desde el mecanismo de valoración propio. Esto respalda la
necesidad de atribución por origen; no aporta la trazabilidad ausente de Auraly
ni define automáticamente su política de costo o el tratamiento de devoluciones
y traslados. La implementación sigue en los propietarios y gates de este diseño.

#### Atribución por orden de entrada — aprobación del 2026-10-22

Implementación al 25 de octubre: el writer operacional existente registra las
porciones por origen y su detalle abierto, conserva los orígenes en traslados
y devoluciones y trata explícitamente el stock histórico sin cobertura. La
calculadora de promedio y la autoridad de cantidades/valor siguen siendo las
existentes. El detalle y las comprobaciones pertenecen al
[propietario de inventario](inventory-operations-engine-design.md#receipt-origin-attribution-in-the-existing-ledger--2026-10-25).
Esta conexión incluye el comando público de revisión posterior, su asiento y
preparación de precios para recepciones con origen trazable. Las recepciones
anteriores sin ese origen reciben una explicación antes de aceptar el reparto.
El descuento financiero y la auditoría global conservan sus propios gates.

El usuario aprobó consumir primero las entradas más antiguas y conservar su
origen en devoluciones y traslados. Es la política de atribución del cargo a
mercancía remanente; no sustituye la valoración promedio vigente ni el último
costo usado para preparar precios. Una devolución conserva el origen de la
salida referenciada, y un traslado lo transporta a su destino; no se convierte
en una compra nueva ni libera presupuesto del cargo. Los datos comerciales de
ventas anteriores permanecen inmutables. Las recepciones antiguas sin origen
suficiente no reciben valoración automática; no se inventan sus orígenes por
el saldo actual.

Aclaración del 25 de octubre: la atribución de cantidades respeta cada sede,
bodega y producto. Recibir cinco unidades en una bodega y vender tres desde
otra conserva las cinco en la primera; compartir promedio o precios no es un
traslado. Solo el traslado explícito transporta sus porciones de origen. La
corrección y sus casos de regresión quedan registrados en el propietario de
inventario. El alcance del promedio contable existente sigue gobernado por la
decisión prevalente de precios; no sustituye este inventario físico separado.

Aclaración de alcance del usuario el 25 de octubre: la recepción ingresa los
ingredientes; la fabricación los consume y registra el producto terminado por
su flujo existente. La revisión de cargos de la recepción no reabre esa
fabricación ni propaga automáticamente el ajuste hacia productos terminados.
Para atribuir el cargo a existencias del ingrediente, una salida a fabricación
consume su origen igual que las demás salidas; no se cuenta ese ingrediente
como disponible porque exista un producto terminado. La porción ya consumida
conserva el tratamiento contable corriente de mercancía salida definido arriba,
sin modificar los documentos ni los costos históricos de fabricación o venta.
No hace falta identificar descendientes de producción para este alcance.
No se inicia una conciliación histórica de compras, fabricación o pagos para
esta revisión. Las recepciones antiguas sin cobertura suficiente conservan
la asociación informativa, sin ajuste automático de valoración.

Prerrequisito de contexto de origen verificado localmente el 24 de octubre: el mismo writer conserva
la vigencia del conjunto de sedes que participa en el pool de cada producto.
Una asignación por sede/producto identifica esa vigencia y la secuencia
operacional que observó; no contiene cantidades ni valores corrientes. Cambiar
la composición del pool o detectar una escritura anterior sin ese contexto
inicia otra vigencia con su cantidad inicial como hecho histórico, sin inventar
recepciones para ese inventario. AVE tiene su contexto separado. `InventoryBalances`
sigue siendo la única autoridad de cantidad/valor. Estos metadatos no sustituyen
las asignaciones FIFO ni su conciliación; no habilitan por sí solos cargos tardíos.
Se mantienen dos comandos SQL por lote y se prueban 1/20 partidas, pool
compartido, cambios de composición, compatibilidad, rollback y replay con el
presupuesto operacional de dos segundos y sin I/O por partida.
La evidencia, la compatibilidad de esquema y la auditoría de este prerrequisito
se registran en [el propietario de inventario](inventory-operations-engine-design.md#cost-pool-context-prerequisite--2026-10-24).

Primer prerrequisito de trazabilidad: el writer canónico conserva en cada
movimiento nuevo el modo de valoración, alcance del pool y su cantidad/valor
antes y después. Son hechos inmutables del mismo kardex, obtenidos del estado
bloqueado y de los balances que ya se escriben; no otro saldo editable ni otra
consulta para reconstruir inventario. El valor posterior debe sumar los valores
redondeados por bodega que realmente se persistieron, no recalcularse como un
producto agregado que pueda diferir por redondeo. AVE queda identificada fuera
del pool vendible. Los históricos conservan campos nulos; no se hace backfill
con la configuración actual ni se consideran completos para el reparto automático.

Este prerrequisito debe conservar los dos comandos del batch del ledger, sin
lecturas ni escrituras por partida adicionales. La regresión verifica ventas
de varias partidas, recepción con pool compartido, negativos, averías y replay,
además del presupuesto operacional existente de dos segundos. Es un paso previo:
guardar estos hechos no implementa todavía las asignaciones FIFO, su conservación
en devoluciones/traslados ni la revisión posterior de costo.

El prerrequisito de procesamiento por lote extiende el mismo handler de recepción:
resolución de presentaciones y estados por conjunto, una invocación del batch del
ledger y persistencia conjunta de observaciones/propuestas. La calculadora de
valoración sigue procesando las partidas en su orden original y devuelve el
resultado de cada una; esa evidencia intermedia determina su preparación de
precio. Varias líneas del mismo producto conservan observaciones, costo anterior
y propuestas individuales; solo la última preparación queda pendiente. Los
productos no inventariables conservan su costo y propuesta sin crear kardex.
El código no cambia el payload aceptado, el hash, el precio público ni el método
de costo; mantiene también la valoración COP de payloads anteriores.

`ProductLinkedCostsPrepare` recibe las familias ordenadas en un solo comando y
conserva su contrato escalar anterior. No elimina preparaciones históricas del
hijo ni utiliza un cambio posterior del principal para recalcular una anterior:
cada solicitud toma el último costo del principal disponible hasta ese punto.
Las preparaciones vinculadas reutilizan `SourceDocumentId`/`SourceLineNumber`
para identificar la partida de recepción que las disparó, además del principal
en `SourceProductId`. El procedimiento valida esa referencia contra recepción,
sede y producto. Así un timestamp compartido por el lote no pierde el orden de
origen ni exige inventar horas diferentes; el contrato escalar conserva origen nulo.
La proyección de líneas de compra/devolución también se agrupa en el writer
actual de Reporting, con los mismos signos, importes y referencias. No se
traslada ese efecto al handler operacional ni se crea otro proyector.

Presupuesto de este paso: el caso de veinte partidas, repetidas o distintas,
no puede aumentar el número de comandos del recorrido de una partida después
del calentamiento y debe quedar bajo dos segundos en la fixture local. La
regresión conserva además el historial por partida, el replay y un fallo después
de escribir costos del proveedor, con rollback y reintento único. La prueba roja
anterior registró 57 comandos para una partida y 209 para veinte. Una primera
agrupación operacional aún dejó 19 llamadas extra en Reporting; ese resultado
no satisfizo la aceptación y motivó extender su writer canónico.

Verificación del lote el 22 de octubre: pasaron los cinco casos enfocados y los
96 casos SQL/API ampliados de recepción, borradores, órdenes, inventario,
devoluciones, precios vinculados y reporting, además de las 847 pruebas Foundation.
La repetición final de compras y precios aprobó 30 casos después de añadir el
vínculo de partida en preparaciones vinculadas. Incluye rechazo de un origen
inexistente sin preparaciones, el contrato escalar anterior, historia por partida
y publicación de 6.000 productos. En la medición enfocada, una y veinte partidas
ejecutaron 56 comandos; veinte productos distintos tomaron 734,6 ms y su replay
11 comandos, sin nuevas observaciones ni preparaciones. Son muestras locales,
no un p95 productivo. El test conserva el gate de dos segundos y compara también
los hechos del reporte contra cantidades, costos, descuentos, impuestos y totales
de las líneas operativas.

La auditoría de este paso comprobó los propietarios existentes, los bloqueos y
alcances por tenant/sede, las escalas y el orden originales, la ausencia de I/O
por partida añadido y la conservación de preparaciones anteriores. Corrigió los
joins repetidos al reemplazar pendientes y la referencia de origen del hijo.
El fault injection posterior al guardado de costos demuestra rollback de saldo,
observaciones y pendientes de precio, con un único efecto tras el reintento. La
espera de esos tests usa la fecha durable real, sin cambiar delays del motor.
El DACPAC debe preceder al binario que envía `FamiliesJson`; conserva la firma
escalar para rollback compatible. No hay backfill, reproceso físico, migración
de payloads ni cambios de interfaz en este paso. Esto no certifica los cargos
posteriores, las asignaciones FIFO ni la auditoría global del rediseño pendiente.

La misma revisión encontró un recorrido todavía proporcional a los datos en la
aceptación de facturas adicionales: `InsertCostDocumentsAsync` enviaba un INSERT
por documento, concepto y asignación. Se sustituye esa persistencia por un único
participante `purchasing.GoodsReceiptCostDocumentsInsert`, llamado por el mismo
store dentro de su transacción serializable. Recibe el snapshot ya calculado y
escribe las tres tablas existentes por conjunto, sin recalcular importes ni
alterar el hash, la captura, los permisos, las fuentes financieras o el motor.
La firma del contrato HTTP y la regla de duplicidad permanecen iguales.

El presupuesto de aceptación de este paso es un solo comando de persistencia
para entre 1 y 100 documentos, sin I/O por concepto o asignación. La prueba roja
registró 2 comandos de encabezado para dos documentos y 100 para cien, además de
los INSERT de detalle que no cuenta ese filtro. La regresión debe conservar los
conceptos repetidos, sumas y referencias; repetir la confirmación no vuelve a
invocar la persistencia y conserva el snapshot relacional completo. Un CHECK
forzado en una asignación, después de insertar documentos y conceptos, debe
revertir toda la aceptación, incluidas numeración/secuencia, recepción y job;
la misma solicitud válida puede confirmarse después. La modificación requiere
el DACPAC antes del binario; el procedimiento es aditivo y el binario anterior
puede seguir escribiendo en las mismas tablas durante un rollback.

La fixture local de navegador conservaba un procedimiento aplicado por `sqlcmd`
sin `QUOTED_IDENTIFIER`. Ese error de preparación dejó un trabajo reintentable
y bloqueó los posteriores por el orden correcto del motor. La preparación local
debe usar `ANSI_NULLS ON`, `QUOTED_IDENTIFIER ON` y verificar `sys.sql_modules`.
Corregir esos metadatos no autoriza completar jobs por SQL ni saltar el cursor:
los pendientes se reejecutan mediante `DocumentProcessingWorker`. Esto concierne
únicamente a la fixture identificada; no hubo modificación de ambientes remotos.

La primera regresión ampliada de esta persistencia aprobó 55 de 57 casos y
expuso dos supuestos de pruebas, sin justificar cambios de reglas productivas:
la importación comparaba el valor total con el importe de adquisición sin
considerar el promedio previo redondeado a seis decimales; ahora exige conciliar
exactamente el delta de balance, kardex, inventario contable y contrapartida de
costo, además de conservar los COP 90.000 de adquisición y la deuda/impuestos.
El sugerido de restaurante sembraba el viernes en curso como día histórico,
aunque el planificador excluye el día de corte. La fixture usa dos viernes ya
terminados. Se conserva la expectativa de demanda de 20 y dos días comparables.
Estas correcciones requieren repetir los 57 casos; no convierten esa primera
ejecución en aprobada ni cambian costo, fechas o sugeridos del producto.

La repetición final aprobó los **57 casos SQL/API** y las **74 compuertas de
arquitectura**. Cien facturas adicionales usaron un comando de persistencia;
dos facturas también uno. La fixture Testing ejecuta los motores derivados
sincrónicamente: la muestra completa de cien documentos tardó 31,5 segundos y
no representa la latencia de aceptación productiva ni un p95. Veinte partidas
repetidas/distintas mantuvieron los 56 comandos y tomaron 1.484,0/1.725,4 ms,
respectivamente, bajo su gate de dos segundos. Los replays no vuelven a escribir
facturas, conceptos o repartos. La prueba de asignación inválida pasó con rollback
de la aceptación y confirmación posterior única de la misma solicitud.

Auditoría de este incremento: hay un único call site del nuevo participante,
en el store de aceptación anterior; los tres INSERT dejaron de existir en C#.
No se creó tabla, servicio, endpoint, job, saldo, cálculo o cargador de interfaz.
Se conservaron validación de scope/permisos, serialización/hash, transacción,
restricciones, escalas y errores; las cadenas se leen completas para que un
exceso de longitud falle al insertar y no se recorte en `OPENJSON`. La exclusión
mutua, replay y recuperación siguen perteneciendo al mismo store/motor. El
proyecto SQL incluye el procedimiento y compiló sin advertencias ni errores;
backend/integración compiló con las cuatro vulnerabilidades NuGet preexistentes
de la dependencia fiscal (duplicadas entre restore y build). Este incremento no
habilita todavía cargos posteriores ni certifica la auditoría global pendiente.

Después de corregir la preparación de la fixture, sus cuatro trabajos pendientes
se procesaron en orden con el `DocumentProcessingWorker` existente, sin escribir
estados o cursores a mano. La repetición con navegador y API reales aprobó los
tres recorridos: recepción con descuento y CxP/asiento, recuperación/edición y
facturación por lote de pedidos, y POS con teclado e impresión de tirilla.
Se inspeccionaron las capturas de recepción y pedidos. Las pruebas usaron la
base local aislada y el artefacto web existente; no hubo cambio de UI en este
incremento, despliegue remoto ni habilitación de la demo final del rediseño.

#### Reconocimiento, reparto y factura fiscal

Regla recomendada para el diseño: un cargo elegible ya contabilizado se asigna
por su partida de costo, y su reclasificación conserva vínculo a esa partida y
a la recepción/revisión. No se vuelve a acreditar CxP, no se toca banco ni se
reconocen otra vez IVA o retenciones. El importe capitalizable excluye impuestos
recuperables e incluye los no recuperables cuando corresponda al costo.
Seleccionar «Prorratear» no convierte cualquier gasto en costo de inventario:
debe ser atribuible a llevar la mercancía a su ubicación y condición actuales.

Ejemplo sin impuestos, con toda la mercancía todavía disponible: factura de
flete por 5.000.000 ya registrada D gasto de flete / C proveedor. Asignar
3.000.000 a la recepción A genera D inventario 3.000.000 / C la cuenta original
del flete 3.000.000. La deuda continúa en 5.000.000 menos pagos; quedan 2.000.000
del concepto disponibles para asignar. Repartir el resto a B consume ese saldo,
no abre otra deuda. Si la fuente ya reconoció inventario o una cuenta transitoria
de costos por distribuir, la contrapartida debe corresponder a ese reconocimiento
real; no aplicar ciegamente el ejemplo ni volver a aumentar el activo por el mismo
importe. Una cuenta transitoria necesita conciliación y clasificación de cierre;
no es un depósito indefinido ni otra fuente de saldos.

Para un cargo nuevo capturado al confirmar la recepción, el reconocimiento y el
reparto usan la misma fuente cuando puedan determinar juntos el destino final.
Para uno existente, la revisión es un comprobante contable de ajuste vinculado,
sin modificar el comprobante original ni emitir otra factura. «Solo asociar»
continúa sin efecto contable ni de costo.

Con mercancía ya vendida, el costo atribuible se separa entre inventario existente
y costo de ventas mediante un ajuste trazable. No se sobrescriben facturas de
venta, precios ni snapshots comerciales históricos. El resultado contable puede
reflejar el costo adicional; conservar snapshots no significa omitir ese costo.
No asignar automáticamente todo a las pocas unidades restantes ni usar la cantidad
actual como prueba de que pertenecen a esa recepción. El algoritmo debe respetar
el método de valoración y los movimientos posteriores. La fecha del asiento
requiere período habilitado y tratamiento contable de hechos/errores anteriores;
no convertir cualquier corrección histórica en gasto del período actual.

Una distribución interna que no cambia la operación facturada no dispara una
nota fiscal por sí misma. Conservar factura/XML/CUFE (o documento soporte/CUDS),
impuestos y obligación originales. Si cambia realmente el precio, impuesto o
contenido fiscal de la operación, procede el documento correctivo correspondiente
por el motor fiscal existente: nota crédito/débito del emisor de la factura o
nota de ajuste del documento soporte según el caso. El asiento interno no
reemplaza el soporte fiscal requerido ni garantiza por sí solo deducibilidad.

Fuentes consultadas el 2026-10-20:

- [IFRS, IAS 2](https://www.ifrs.org/issued-standards/list-of-standards/ias-2-inventories/): costo atribuible al inventario y reconocimiento al vender.
- [Odoo 19, landed costs](https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/inventory/inventory_valuation/landed_costs.html): reparto por peso/valor y asiento al validar el ajuste.
- [Business Central, item charges](https://learn.microsoft.com/en-us/dynamics365/business-central/payables-how-assign-item-charges): cargo enlazado a recepción y ajuste de costo de salidas. Su actualización histórica no se adopta como permiso para reescribir snapshots comerciales de Auraly.
- [DIAN, Resolución 227 de 2025](https://normograma.dian.gov.co/dian/compilacion/docs/resolucion_dian_0227_2025.htm): sistema de facturación y notas; la ausencia de corrección fiscal por una mera distribución interna es la conclusión de diseño para este supuesto, no una autorización tributaria general.

La contradicción de intención queda resuelta. Antes de implementar este slice
falta completar y validar el algoritmo señalado arriba y su presupuesto de
consultas; no hace falta otra aprobación de principio sobre el asiento.

### Gates de rendimiento y aceptación

Objetivos de diseño, pendientes de medición en la implementación:

- Modal: un GET por página de 25–50, máximo dos comandos SQL, p95 servidor
  <= 500 ms con 100.000 fuentes; filtrar/paginar antes de enriquecer conceptos,
  tercero, saldo y presupuesto. Detalle de seleccionados en lote, sin N+1.
- Preview de 500 líneas/100 asociaciones: una petición por edición confirmada,
  <= cuatro viajes SQL y p95 <= 1 s. Reutiliza snapshot tributario de la fuente,
  sin preview por cada factura ni lectura de proveedor por fila.
- Aceptar revisión: una petición, <= ocho viajes SQL de validación/persistencia
  por lote y p95 <= 3 s, sin incluir procesamiento asíncrono ni publicación de
  precio. Medir conflictos simultáneos en dos recepciones y reparto residual.
- El handler tocado debe reemplazar cargas/escrituras de inventario por línea
  por los métodos de lote del writer canónico. La nueva revisión no copia el
  recorrido N+1 existente. Su benchmark se define al resolver la valoración.
- La vista tiene un propietario de carga. Mutación devuelve vínculo, costo,
  revisión y preparación autoritativos para actualizarlo. Progreso distingue
  guardado, procesamiento y preparación; publicación exige acción explícita.
- SQL Server verifica 3+2 sobre 5, exceso concurrente, retry, factura
  pagada, solo asociar, IVA descontable, moneda funcional, datos logísticos
  ausentes, revisión tardía fuera de orden y publicación concurrente.
- Comparar antes/después cantidades, ventas históricas, obligación y fiscal:
  deben permanecer iguales al asociar; los únicos efectos adicionales serán
  los de costo explícitamente aprobados. No contar una preparación como publicada.
- Compra nueva de contado y de crédito: ambas producen adquisición y obligación
  neta, sin `SettlementSourceMissing`. Con contabilidad desactivada mantienen
  efecto comercial; activarla después no repite la compra histórica.
- Egreso inmediato, parcial, anticipado y cancelado: saldo derivado correcto,
  banco explícito, sin inventario adicional, sin doble gasto ni numeración repetida.
- **Agregar factura** y **Agregar comprobante** se combinan en una recepción;
  el primero crea exactamente una fuente y el segundo crea cero fuentes nuevas.
  Factura duplicada, permiso insuficiente o mapping ausente se rechazan antes
  de aceptación; el replay anterior no recalcula ni cambia sus cuentas.
- Las cuentas/impuestos de todas las facturas nuevas se resuelven por lote; las
  referencias existentes se cargan en ese mismo conjunto. Se mantiene el máximo
  de viajes definido arriba, sin llamadas HTTP o SQL por cargo.


### Regresión real de recepción y descuento comercial — 2026-10-22

`admin/e2e/goods-receipt-browser-regression.spec.ts` usa el mismo fixture local
de contabilidad que los recorridos reales de comprobante y gasto. Prepara por
la API nativa una bodega activa si la base descartable todavía no la tiene.
El calendario de la prueba selecciona una fecha habilitada distinta de la actual;
seleccionar de nuevo la fecha actual no demuestra un cambio de vencimiento.

La prueba crea un producto desde la vista, captura diez unidades de COP 10.000
con descuento comercial del 10 %, confirma la entrada y comprueba la persistencia
del porcentaje, COP 10.000 de descuento y COP 90.000 netos. Verifica estado
procesado, asiento balanceado y una sola CxP abierta por el neto del documento,
sin pago supuesto. El recorrido pasó localmente en 49,4 segundos con el ajuste visual; su tiempo
es de navegador completo, no un benchmark SQL.

La revisión visual detectó el porcentaje ubicado bajo «Costo unitario». Se
trasladó el control existente a «Descuento», junto al importe correspondiente,
conservando cálculo, captura, foco por teclado y contratos existentes. La prueba
verifica que porcentaje e importe compartan esa columna y que costo unitario
conserve la suya. El presupuesto por lotes y la integración de comprobantes y
cargos tardíos de las secciones anteriores siguen pendientes.

La regresión complementaria pasó 9/9: guardado, recuperación y descarte nativos
con login real; detalle inmutable; totales de documentos asociados; fechas;
recuperación compatible de cargos antiguos de contado; recálculo y persistencia
del porcentaje; conflicto de versiones; y agotamiento de cupo DIAN sin perder la
captura. Las demás respuestas de estos casos complementarios son fixtures de
interfaz. Las pruebas antiguas de borrador reutilizan el login con tenant del
fixture existente y las etiquetas actuales; se retiró su limpieza global que
leía cada borrador, y el descarte por vista comprueba el ID que devolvió su propio
guardado. Ninguna prueba elimina documentos de otros escenarios.

Auditoría del ajuste visual y de Gastos: se conservaron los controles, helpers,
writers y motores propietarios; el cambio de columna no añade I/O ni cálculos.
El detalle de gasto conserva un solo dueño y prueba una consulta al reabrir,
sin consultas de vistas cerradas. El pago real y su replay usan la aceptación
nativa y `SqlAccountingPostingProcessor`; fuente, aplicación y asiento no se
recrean en la vista. No hubo cambios de esquema o contratos en estos ajustes.
Rollback de presentación conserva los documentos y pagos registrados. Esta es
la auditoría de estos ajustes; la auditoría completa del diseño ampliado y las
capacidades marcadas pendientes en sus secciones siguen abiertas.

### Revisiones de costo posteriores — verificación local del 2026-10-25

> Registro histórico de una implementación anterior. Decisión vigente del
> 2026-10-30: la recepción confirmada conserva su misma
> identidad y sus cantidades físicas. «Editar costos» guarda primero un borrador
> de cambios ligado a esa recepción; al confirmarlo, una revisión inmutable
> procesa únicamente las diferencias de costos, descuentos, facturas y
> comprobantes asociados por los motores documental, de inventario y contable
> existentes. No confirma otra entrada ni vuelve a ingresar unidades. La
> recepción y sus fuentes originales conservan su trazabilidad; cambios de
> obligaciones y documentos fiscales exigen su corrección enlazada. La revisión
> positiva anterior es solo un caso de este flujo y no habilita una ruta paralela.

El corte de implementación actual habilita la corrección de valores y
descuentos en comprobantes internos en COP. También
permite asociar, repartir y retirar comprobantes de costo existentes en
revisiones sucesivas de esa misma recepción: el saldo repartible se reserva al
aceptar y se libera al contabilizar el retiro; una asociación informativa no
produce asiento de valor. El asiento original del comprobante permanece intacto.
En una misma revisión se puede retirar y reemplazar un reparto: solo el importe
retirado por esa recepción queda disponible para su reemplazo, mientras las
otras recepciones siguen viendo la reserva hasta que se contabilice el retiro.
Una nueva edición espera a que la recepción, su revisión anterior y las
facturas asociadas terminen de contabilizarse; así no consume saldos a medio
procesar.
La comprobación del origen de inventario se limita a la bodega de la recepción;
un saldo histórico del mismo producto en otra bodega no bloquea la edición.
La medición local aislada de la solicitud completa de una edición de una
partida registró 42 comandos SQL y 938 ms; el presupuesto de regresión es no
superar los 55 comandos de la confirmación original medida con el mismo
contador. El presupuesto de ocho viajes descrito arriba corresponde al tramo
de validación/persistencia de la revisión anterior, no a toda la solicitud HTTP.
Se pueden agregar facturas electrónicas del proveedor o comprobantes internos
en COP, cada uno con su propia obligación; la revisión reclasifica su costo
desde gasto hacia inventario o costo de venta según las unidades conservadas.
Al retirar una factura de la asociación, el costo sale de la recepción y se
reclasifica a gasto sin anular su documento ni su obligación. La edición de los
importes de una factura ya contabilizada y los soportes que emite Auraly a la
DIAN requieren una corrección fiscal vinculada antes de habilitarse.
Este corte no se considera la entrega completa de la decisión.

`GoodsReceiptService.ReviseCostsAsync` aceptaba una asociación o reparto sobre
una recepción ya procesada, con identidad de revisión y clave idempotente.
`SqlGoodsReceiptStore` reserva el importe disponible de la fuente en la misma
transacción que acepta la revisión; el trabajo durable entra en
`DocumentProcessingEngine`. El handler existente usa la atribución por origen
de la recepción, `SqlInventoryLedgerWriter.ValueAdjustment`, la preparación
vigente de precios y una fuente para `SqlAccountingPostingProcessor`. El
comprobante y la deuda originales no se modifican. La devolución posterior
lee el costo revisado de la línea recibida.

La prueba SQL/API aislada pasó dos revisiones de 3.000 y 2.000 sobre una fuente
de 5.000, replay idempotente, rechazo de contenido cambiado con la misma
identidad, un movimiento y un asiento por revisión, saldo original intacto,
preparación sucesiva de precios y devolución al costo unitario revisado. Los
cuatro recorridos reales de navegador sobre backend local pasaron: recepción
con gasto, recepción con activo por distribuir, reparto posterior desde la
vista y venta POS. El procesamiento de una revisión contrasta además la suma
de su fuente inmutable con las asignaciones persistidas antes de valorar.
Un recorrido adicional confirmó en la vista que un comprobante fechado en marzo
se puede asociar a una recepción de enero y que la deuda original sigue intacta;
el modal incluye comprobantes posteriores a la fecha de la recepción.
Por defecto el filtro empieza el primer día del año de esa recepción y termina
en la fecha mayor entre la recepción y hoy; el usuario puede ajustar ambas fechas.

El ajuste automático exige origen de inventario trazable. El descuento financiero
conserva el gate definido arriba. Ninguna de estas capacidades
se infiere de la prueba de repartos positivos sobre una recepción nueva.
La aceptación de una revisión con reparto consulta por lote, en el mismo viaje
SQL que obtiene el consecutivo, que las líneas afectadas con inventario
administrado conservan el movimiento de origen y que sus saldos mantienen la
referencia vigente. Si no concilian,
responde conflicto antes de reservar el importe o crear el trabajo. El handler
vuelve a verificar al procesar, pues el inventario puede cambiar entre ambas
etapas. La regresión fuerza la pérdida temporal de esa referencia, comprueba el
rechazo sin revisión y confirma que la misma solicitud se acepta al restaurarla.
