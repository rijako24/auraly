# Emisión durable de ventas online

## Extensión aprobada: cargos de facturación (online y Edge)

Los cargos accesorios pertenecen al documento de venta, no al catálogo de medios
de pago. La configuración se administra en **Cargos de facturación**, fuera de
Configuración general. El cambio no amplía `BillableServices` al POS ni crea
productos ficticios. Cada cargo conserva su identidad, versión de regla, base,
importe incluido en factura o registrado como gasto, proveedor y clasificación
contable al emitir. La empresa se deriva del contexto autenticado activo; no se
selecciona un cliente ni una empresa en la regla del cargo.

`InvoiceChargeCalculation`, en el dominio de Sales, es la política pura común
para servidor y Edge. La base es el total de productos después de descuentos e
impuestos y antes de cargos y retenciones. Las tarifas pueden ser digitadas,
fijas, porcentuales o por intervalos contiguos `[desde,hasta)`, desde cero y con
último límite abierto. El porcentaje se aplica a toda la base. Primero se
resuelve la tarifa y después su inclusión en factura. `InclusionMode` permite
incluir siempre, nunca (registrar gasto) o incluir según el importe de la
factura, hasta `InvoiceAmountLimit` inclusive. Esta decisión depende de la base
de la factura, nunca de la identidad ni atributos del cliente. Para el domicilio
de Aurali, COP 80.000 todavía incluye el cargo y solo por encima se registra
como gasto. **Domicilio · Agotados** usa valor digitado (sugerencia editable) y
nunca se incluye en el total a cobrar, sin umbral. Son definiciones del mismo catálogo.
Cada versión congela también cuenta, centro, tasas y datos del proveedor; una
edición de maestros no reescribe versiones anteriores. La recepción consulta en
un único lote las versiones referenciadas (máximo diez), verifica su sede y
recalcula el resultado con la política común antes de aceptar importes, cuentas
y proveedor. Una repetición ya aceptada devuelve su resultado antes de esa lectura.
Las tarifas y los datos del proveedor son datos del tenant, nunca defaults del
motor. El usuario autorizó tarifas de prueba editables únicamente para Aurali
en desarrollo; no son valores predeterminados del sistema.

El importe cobrado al cliente participa en la liquidación normal, incluido
crédito. Los pagos aplicados, excluido el cambio, distribuyen proporcionalmente
el cargo; el redondeo acumulado conserva exactamente el total y el orden estable
de los pagos. La parte a crédito no es recaudo hasta que se cobre. El costo del
proveedor es una obligación distinta de la deuda del cliente: reutiliza Gastos,
sus conceptos y el motor financiero-contable canónico. El concepto es dueño de
la cuenta de gasto y del centro predeterminado. Asumir un costo no crea una
entrada de efectivo; pagar al proveedor posteriormente sí produce el egreso
normal, sin registrar nuevamente el gasto.

El total a pagar se redondea al múltiplo de COP 100 más cercano; el punto medio
se resuelve alejándose de cero. La pantalla presenta directamente ese valor.
El subtotal exacto de líneas e impuestos permanece congelado y
`PayableRoundingAmount` explica la diferencia firmada hasta el total fiscal:
positiva al subir y negativa al bajar. `SalesPayments.Amount` conserva la parte
exacta aplicada y `RoundingAdjustment` guarda la misma diferencia para que caja,
banco o cartera reciban el total oficial. La política se usa en contado y
crédito, web, Edge y facturación masiva de pedidos. CUFE, UBL, impresión,
reporting y cierre publican el total redondeado; bases e impuestos no se
redistribuyen.

La facturación masiva conserva el botón directo existente y ofrece **Agregar
cargo** únicamente con pedidos seleccionados. El modal elige la definición y el
proveedor una vez; cada pedido recalcula la regla sobre su propio total y se
emite por el endpoint canónico de facturación. La prevalidación de cupo incluye
el importe facturado del cargo y realiza una consulta por lote.

Una devolución muestra los cargos congelados de la factura y permite conservarlos
o devolverlos. Devolver un cargo incluido aumenta la nota crédito al cliente;
devolver uno asumido no altera ese total. Si el gasto sigue procesado, el motor
contable revierte su efecto frente al proveedor: cancela la cuenta por pagar
abierta o crea un saldo a favor por la parte ya pagada. El gasto de cualquier
cargo también puede anularse directamente desde Gastos. Si ya fue anulado, un
cargo asumido no vuelve a estar disponible en la devolución; uno cobrado al
cliente sí puede devolverse al cliente, sin repetir la reversión del gasto ni
crear otro saldo a favor del proveedor. Una anulación pendiente bloquea la
selección del cargo hasta que termine. Si el cargo ya entró en una devolución,
se rechaza la anulación directa. La selección y los efectos son idempotentes y
no recalculan la configuración vigente.

El writer común `SqlExpenseStore.PersistAcceptedAsync` persiste gastos manuales
y gastos originados por cargos, retenciones y fuentes financieras en la misma
transacción de aceptación. Los nuevos gastos entran directamente a
`AccountingSourceDocuments`/`AccountingPostingJobs`, sin consumir el cursor
operativo ni crear `DocumentProcessingJobs`. El handler documental de Expense
permanece exclusivamente para drenar documentos históricos ya aceptados por esa
ruta; podrá retirarse cuando no existan trabajos históricos pendientes. El motor
contable es el único dueño de la CxP y de la transición del gasto a `Processed`.

Un cargo usa `AppliedChargeId` como identidad del gasto y `SourceInvoiceId` como
referencia interna a la venta. Su `SupplierDocumentNumber` es nulo: no se inventa
una factura del proveedor. Los gastos manuales conservan la exigencia de ese
número y su unicidad filtrada. El retry compara la solicitud original y no
vuelve a calcular retenciones ni requiere maestros actualmente activos.

La descripción congelada del gasto de cargo incluye el nombre del cargo y el
número de la factura de venta. La consulta de cuentas por pagar obtiene concepto,
descripción y origen desde el gasto vinculado y la factura original. Las cuentas
originadas por recepción y por factura adicional conservan el vínculo a la
recepción; la vista de cartera usa ese vínculo para abrir su detalle. El filtro
por concepto se aplica únicamente a obligaciones de gastos en la pestaña de
facturas, sin alterar los saldos de otras obligaciones. El selector busca los
conceptos de forma paginada bajo el permiso de lectura de cuentas por pagar.

Hasta diez aplicaciones de cargo por factura reutilizan operaciones en lote de numeración,
fuentes contables, retenciones y, cuando la política del proveedor lo exige,
reserva de cupo y documentos soporte. Las firmas individuales de estos helpers
conservan su comportamiento y delegan al lote de tamaño uno. Las alertas usan
los coordinadores fiscal y contable existentes después del commit.

La consulta independiente lee snapshots de ventas de la sede autenticada en
periodos de hasta 31 días y páginas de hasta 100 filas. Une la obligación real
para mostrar su saldo; no introduce otra tabla de cargos emitidos. El cierre
congela su desglose y reutiliza el endpoint de snapshot, incluyendo el permiso
de supervisión del tenant. Sus filas de cargo son informativas y no alteran
conteos ni decisiones de conciliación.

El historial tiene dos viajes SQL (scope/zona y reporte). Materializa únicamente
el periodo de la sede en una tabla temporal de consulta y ordena sus claves antes
de devolver el JSON de la página. Esto evita repetir la expansión del snapshot y
la reserva excesiva de memoria por estimaciones fijas de `OPENJSON`. La regresión
de 30 facturas exige menos de dos segundos para la consulta completa por API.

En POS, **Cargos de facturación** se abre junto al total o con `Shift + F8`;
`F8` conserva el cobro. El modal consulta únicamente su página visible y las
mutaciones reemplazan el borrador con la respuesta autoritativa. El botón
**Agregar cargo** crea una aplicación independiente, por lo que una misma
definición puede agregarse varias veces con distinto valor o proveedor. Un cargo
puede quitarse o editarse; el proveedor único activo se selecciona automáticamente.
Los cargos pertenecen a la facturación: guardar un borrador con cargos como
pedido se rechaza explícitamente para no perderlos. Una venta temporal sí los
conserva. Quitar el último producto limpia los cargos asociados.

Cada aplicación se guarda por `AppliedChargeId` en
`sales.InvoiceChargeDraftSelections`. Al emitir, el POS calcula una sola vez y
sube el snapshot final dentro de `PosSaleUploadRequest.Charges`; recepción valida
integridad, tenant y referencias versionadas, pero no vuelve a ejecutar la fórmula.
El flujo canónico de gastos acepta las aplicaciones asumidas por la empresa y el
motor contable crea el asiento y la cuenta por pagar al proveedor de forma
idempotente.

El cierre incorpora **Cargos de facturación** y el desglose dentro del medio de
pago correspondiente, con factura, cargo, proveedor y cantidad de cargos. Es un
desglose de pagos existentes: no crea otro movimiento `SalePayment` ni suma dos
veces la venta. La vista de resultados respeta el conteo ciego. Los cargos
asumidos por la empresa se identifican sin atribuirles un ingreso de caja.

Edge conserva configuración y proveedor elegible en SQLite y usa su outbox de
ventas existente. Alta, edición e inactivación generan la invalidación
`Configuration` por el canal push canónico; bootstrap y reconexión recuperan lo
pendiente sin polling. Desactivar o reactivar un proveedor elegido por un cargo
publica también `Configuration` desde el writer de terceros. Las banderas de
elegibilidad se refrescan; tarifas y clasificación financiera siguen congeladas
en la versión del cargo hasta su siguiente edición. Una factura emitida conserva su snapshot al sincronizar,
aunque exista una versión posterior. Reenvíos no repiten cargo, gasto, CxP,
inventario, pagos ni movimientos de sesión.

Criterios de aceptación: escenarios reales de venta sin cargo, cliente/empresa
en ambos lados del umbral y exactamente en él, rangos, digitado, efectivo,
tarjeta, transferencia, pagos mixtos, crédito, posterior recaudo, gasto/CxP y
pago al proveedor, cierre/reconciliación, aislamiento, concurrencia, reintento,
desconexión y reconexión. La prueba fiscal debe reconciliar el total y sus
impuestos, y la reimpresión debe conservar el documento emitido.

Presupuesto: el cálculo es local y lineal en hasta 32 rangos y 11 participaciones
de pago; no realiza I/O. Las lecturas de configuración y cargos se hacen por
lote o página, nunca por cargo. La confirmación conserva el umbral local de dos
segundos del motor documental; el reporte pagina como máximo 100 filas y la
descarga de configuración se realiza por lotes acotados. Se mide el recorrido
sin builds ni suites concurrentes antes de publicar.

La confirmación multilínea usa `SqlInventoryLedgerWriter.PostBatchAsync`: carga
solamente los productos afectados y sus pools de costo, mantiene el orden de
valoración de las líneas y escribe saldos y kardex en lote dentro de la misma
transacción documental. El método unitario delega en el mismo writer. La
contabilidad resuelve categorías/cuentas y escribe líneas de asiento y pagos por
conjunto; las aplicaciones de cartera y proveedores conservan sus validaciones,
bloqueos e idempotencia en el processor financiero existente. La representación
JSON se lee directamente con `OPENJSON ... WITH`, sin multiplicar las
estimaciones de filas con una expansión anidada por elemento.

Compatibilidad y despliegue: primero se publica el esquema aditivo y los
lectores del servidor, después admin y el instalador Edge. Los payloads sin
cargos conservan su serialización anterior. Si hay que detener nuevas
aplicaciones, se inactivan los cargos mediante su configuración; los documentos
emitidos y pendientes siguen usando su versión congelada. Después de emitir
facturas con cargos no se deben reinstalar lectores anteriores que desconocen
estos totales: la reversión requiere conservar la lectura y sincronización de
los documentos ya aceptados. No se borran versiones, gastos ni cierres para
revertir una publicación.

Contexto original: 2026-07-30. Alineado al modelo de sesiones vigente: 2026-09-19.

## Decisión de contexto

Una venta web pertenece a la sede activa y al turno del cajero. `BusinessId`,
`WorkSessionId` y `SoldByUserId` conservan el contexto operativo:

- negocio y sede;
- bodega;
- serie operativa Auraly;
- serie fiscal DIAN;
- resolución o autorización vigente;
- cursores de ambos consecutivos;
- sesión de caja y responsabilidad por cajero.

`DeviceId` es nulo en una venta web porque el navegador no representa un equipo
POS Edge enrolado. El documento conserva `SourceMode=Online`, `BusinessId`,
`WorkSessionId` y `SoldByUserId`. No existe `RegisterId` en el esquema vigente.

## Propiedad del cálculo de líneas

El comando canónico que agrega un producto, y los comandos explícitos que editan
cantidad, precio o descuento, son los únicos propietarios de la fórmula comercial
de la línea. Allí se calcula y cierra `PublicLineTotal`. Desde ese momento, pausar
una venta, guardarla como pedido, recuperarla, facturarla y pagarla transportan el
snapshot sin exigir que `cantidad × precio - descuentos` reconstruya exactamente
el total cerrado. Esta diferencia es válida en cantidades fraccionarias y en los
límites de redondeo monetario.

Las etapas posteriores solo validan la estructura y las identidades que les
pertenecen, además de la consistencia fiscal interna del documento congelado. No
reprecian, no crean descuentos de compensación y no rechazan un snapshot por esa
ecuación. Una edición comercial posterior sí vuelve a invocar el propietario
canónico y produce un nuevo total cerrado.

## Flujo transaccional

`POST /api/commerce/v1/pos/drafts/{draftId}/complete` recibe la versión esperada,
los medios de pago y `Idempotency-Key`.

Dentro de una transacción serializable, el servidor:

1. bloquea el borrador del usuario;
2. valida que siga activo y tenga la versión esperada; si procede de un pedido,
   vuelve a verificar bajo bloqueo que `OrderInvoiceLinks` aún no lo vincule a
   otra factura. Una caja pudo haberlo importado al borrador antes de que otra
   ruta lo facturara. En ese caso rechaza el borrador obsoleto antes de reservar
   consecutivos, crear `SalesDocuments` o iniciar DIAN;
3. valida que los pagos cubran exactamente el total;
4. resuelve las series operativa y fiscal del emisor servidor de la sede;
5. consume atómicamente ambos consecutivos;
6. congela el snapshot comercial, fiscal y UBL;
7. calcula CUFE y QR con la clave técnica de la resolución;
8. cambia el borrador a `Issuing`;
9. crea el siguiente borrador activo vacío;
10. persiste el payload exacto y el recibo idempotente.

Después, el mismo motor usado por POS Edge:

- recalcula y verifica el CUFE;
- persiste el documento recibido;
- crea líneas y resúmenes agrupados por impuesto y tarifa;
- registra la salida de inventario;
- registra pagos;
- valida la sesión abierta del cajero;
- atribuye la venta al turno del cajero;
- registra movimientos de caja;
- publica el evento mediante outbox;
- completa el recibo de procesamiento.

## Concurrencia e idempotencia

Dos usuarios pueden vender simultáneamente desde computadores diferentes usando
sesiones propias en la misma sede. Los cursores pertenecen a sus series y se
bloquean en SQL Server; uno recibe N y el otro N+1.

El payload preparado se conserva antes de invocar el motor. Un reintento exacto:

- devuelve el mismo `DocumentId`;
- conserva número Auraly, número DIAN y CUFE;
- no vuelve a consumir consecutivos;
- no duplica líneas, impuestos, inventario, pagos, caja ni outbox.

Reutilizar la misma venta con otra clave o contenido produce conflicto explícito.
El crédito se declara mediante `Credit` y el cliente seleccionado con cupo
habilitado; no es un registro ficticio en `Payments`. Enviar `Credit` como medio
de pago se rechaza antes de reservar numeración.

## Persistencia

- `DocumentSeriesCursors`: siguiente consecutivo operativo por serie.
- `FiscalSeriesCursors`: siguiente consecutivo fiscal por serie.
- `OnlineSalesCheckoutReceipts`: payload exacto, hash, clave idempotente,
  siguiente borrador y estado.
- `SalesDocuments`: sede, sesión y cajero responsables; `DeviceId` nulo para
  online y `SourceMode` explícito.

El proyecto `Auraly.Database.sqlproj` continúa siendo el único dueño del esquema.

## Retenciones y valor por cobrar

La selección o modificación del cliente solicita una vista previa de la
liquidación al caso de uso canónico de venta. Tanto el servidor online como POS
Edge delegan la selección de reglas y el cálculo en `WithholdingEngine`; la UI
no contiene fórmulas fiscales ni decide reglas por su cuenta.

Al completar la venta, el backend vuelve a validar los datos autoritativos y
congela la liquidación como snapshot del documento. Inventario, pagos,
contabilidad e impresión consumen ese mismo resultado y no recalculan la
retención. El total bruto, las retenciones y el neto por cobrar se presentan en
la pantalla y en todos los formatos soportados: tirilla, media carta, medio
oficio y carta.

## Evidencia histórica del checkout original

- DACPAC: 0 errores y 0 advertencias.
- Fundación: 109 pruebas correctas.
- Checkout online: 3 pruebas correctas sobre SQL Server real.
- Venta online completa con documento, línea, resumen de impuesto, inventario,
  pago, movimiento de caja, recibo del motor y outbox.
- Reintento exacto sin duplicados.
- Conflicto cuando cambia el contenido.
- Dos cajeros concurrentes en una caja sin colisión de numeración.
- Un valor `Credit` incorrectamente enviado como medio de pago se rechaza sin
  consumir consecutivos. El contrato de financiación usa `Credit` por separado.

## Integración vigente del POS

`OnlinePosClient` conecta la pantalla POS al checkout, conserva el contexto de
venta y devuelve recibo y siguiente borrador. La impresión y reimpresión usan
el recibo confirmado y los renderizadores compartidos de `Auraly.Pos.Printing`.
El historial requiere `sales.reprint`, consulta el snapshot por tenant y negocio
sin depender del cajero, sesión o bodega que originaron la venta, y audita la
reimpresión después de imprimir. No reconstruye la venta a partir de precios ni
cargos actuales.
