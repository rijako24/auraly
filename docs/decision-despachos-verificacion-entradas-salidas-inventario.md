# Decisión: Despachos, verificación de salida y movimientos de inventario

**Estado:** incluido en el MVP de Auraly Commerce  
**Fecha:** 27 de julio de 2026  
**Fuentes auditadas:** Xion WinForms, entidades servidor/local, servicios, repositorios, permisos y reportes de Cargue de Mercancía, Aduana, Entrada de Mercancía y EnSa.  
**Prevalencia:** este documento agrega al alcance los comportamientos útiles de Cargue de Mercancía y Aduana, y formaliza la separación entre Entradas de mercancía, movimientos manuales de inventario y salidas producidas por ventas.

---

## 1. Decisión ejecutiva

Auraly Commerce incorporará cuatro capacidades claramente separadas:

1. **Entradas de mercancía:** recepción física de productos desde un proveedor, con actualización de inventario y costo y, normalmente, creación de una cuenta por pagar.
2. **Movimientos de inventario:** entradas y salidas manuales justificadas por un motivo, sin representar una compra ni una venta.
3. **Despachos:** preparación y agrupación de ventas confirmadas que se cargarán en un vehículo o se entregarán a un transportador.
4. **Verificación de despacho:** control físico con lector de códigos para confirmar que la mercancía preparada realmente está saliendo.

No se copiarán los nombres internos ni el modelo accidental de Xion.

```text
Xion                         Auraly
----------------------------------------------------------------
Entrada de Mercancía         Entradas de mercancía
EnSa                         Movimientos de inventario
EnSa Entrada                 Entrada manual de inventario
EnSa Salida                  Salida manual de inventario
Cargue de Mercancía          Despachos
Aduana                       Verificación de despacho
Pendiente de cargue          Faltante de preparación
Pendiente incluido           Faltante reasignado a otro despacho
Revisado de aduana           Despacho verificado
```

`Aduana` no se utilizará como nombre funcional en Auraly porque en Xion no representa importaciones, nacionalización ni trámites de comercio exterior. Representa un puesto de control de salida.

El término legado podrá mantenerse únicamente como:

- alias de búsqueda;
- etiqueta de migración;
- ayuda contextual durante adopción;
- identificador del sistema origen en `LegacyEntityMappings`.

---

## 2. Hallazgos de Xion

### 2.1 Cargue de Mercancía

Xion permite:

- crear un cargue con consecutivo;
- asignar conductor;
- asociar facturas y remisiones;
- consultar documentos por fecha, cliente, equipo y vendedor;
- agregar o retirar documentos;
- visualizar los productos de cada documento;
- declarar cantidades que no se cargaron;
- trasladar faltantes de un cargue anterior a uno nuevo;
- conservar observaciones;
- consultar cargues con y sin pendientes;
- imprimir reportes por documento, producto y vendedor;
- enviar el cargue a revisión;
- marcarlo como revisado con fecha.

La entidad heredada guarda:

```text
CargueDeMercancia
  NoDocumento
  Consecutivo
  ConductorId
  Revisado
  FechaRevisado
  EquipoId
  UsuarioId
  FechaDeSistema
```

Un cargue se relaciona con documentos y con cantidades pendientes.

### 2.2 Aduana

La pantalla llamada Aduana:

- busca un cargue;
- permite verificar agrupado por producto o separado por factura;
- usa lector/código de producto;
- marca productos revisados;
- puede desmarcar una verificación;
- exige revisar todo antes de guardar;
- incluye los productos pendientes traídos de otro cargue;
- conserva trabajo local para recuperarlo;
- consulta cargues ya revisados;
- registra la fecha de revisión.

El modelo heredado usa un booleano `Revisado`. Eso pierde información importante:

- cuántas unidades se esperaban;
- cuántas se verificaron;
- cuántas faltaron;
- quién escaneó cada cantidad;
- si hubo sobrantes;
- orden de escaneo;
- código utilizado.

Auraly reemplazará ese booleano por cantidades y eventos de verificación.

### 2.3 Entrada de mercancía

Xion contiene:

- proveedor;
- bodega;
- centro de costo;
- factura y fecha;
- vencimiento;
- contado/crédito;
- productos, cajas, unidades y embalaje;
- costo;
- descuentos;
- impuestos;
- totales;
- actualización de existencias;
- actualización de precios;
- transportador;
- orden de compra;
- cuenta por pagar;
- temporales;
- lotes, seriales, retenciones, fletes y descargues.

Auraly conserva lo esencial y excluye del MVP:

- lotes;
- seriales;
- retenciones;
- fletes;
- descargues;
- orden de compra como módulo independiente;
- actualización automática y silenciosa de precios de venta.

### 2.4 EnSa

Xion usa `EnSa` para movimientos manuales de entrada o salida:

- tipo entrada/salida;
- bodega;
- centro de costo;
- tercero;
- motivo;
- observación;
- líneas;
- existencia observada;
- unidades y embalaje;
- valorización por último costo, costo promedio o precio de venta;
- anulación mediante movimiento inverso;
- permisos especiales para negativos;
- consulta e informe.

En Auraly este módulo se llama **Movimientos de inventario**.

---

## 3. Fronteras funcionales

### 3.1 Entrada de mercancía no es una entrada manual

Una entrada de mercancía:

- tiene proveedor;
- representa recepción física;
- puede referenciar factura del proveedor;
- actualiza costo;
- puede crear CxP;
- puede proponer cambios en costos/precios de proveedor;
- afecta reportes de compras.

Una entrada manual:

- corrige o incorpora inventario por un motivo operativo;
- no representa una compra;
- no crea CxP;
- no actualiza precios de venta;
- no se presenta como compra en reportes.

### 3.2 Salida manual no es venta

Una salida manual:

- no factura;
- no cobra;
- no crea CxC;
- no transmite a DIAN;
- no aplica promociones;
- no atribuye utilidad comercial;
- solo reduce inventario por un motivo autorizado.

Las ventas continúan siendo propiedad de Sales y producen su propio movimiento de inventario.

### 3.3 Despacho no es movimiento de inventario

Para el MVP, preparar o verificar un despacho **no vuelve a descontar inventario**.

Las ventas que alimentan el despacho ya fueron confirmadas y su efecto de inventario ya fue procesado por el motor.

Por tanto:

```text
Factura confirmada -> movimiento de inventario
Despacho           -> estado de preparación/entrega física
Verificación       -> evidencia de lo cargado
```

Un faltante de despacho es una excepción de cumplimiento, no una devolución automática al inventario.

Si en el futuro se requiere descontar inventario únicamente al despachar, será otra política:

```text
InventoryIssuePoint = SaleConfirmation | DispatchRelease
```

`DispatchRelease` exige reservas, disponibilidad comprometida y cambios en reportes. Queda fuera del MVP.

### 3.4 Despachos no es TMS

Incluido:

- conductor;
- transportador;
- vehículo y placa opcionales;
- fecha;
- referencia de ruta/zona como texto o maestro simple;
- documentos;
- preparación;
- faltantes;
- verificación;
- liberación.

Fuera:

- optimización de rutas;
- GPS;
- tracking en tiempo real;
- liquidación de fletes;
- tarifas de transporte;
- mantenimiento de flota;
- prueba de entrega con geolocalización;
- planeación avanzada de capacidad.

---

## 4. Arquitectura modular

### 4.1 Purchasing

```text
Auraly.Domain.Purchasing
Auraly.Application.Purchasing
Auraly.Infrastructure.Purchasing
Auraly.Contracts.Purchasing
```

Propietario de:

- entradas de mercancía;
- líneas recibidas;
- documentos de proveedor relacionados;
- estados de recepción;
- diferencias de recepción.

### 4.2 Inventory

```text
Auraly.Domain.Inventory
Auraly.Application.Inventory
Auraly.Infrastructure.Inventory
Auraly.Contracts.Inventory
```

Propietario de:

- movimientos manuales;
- kardex;
- saldos;
- valorización;
- conteos;
- traslados;
- averías;
- conversiones.

### 4.3 Dispatching

```text
Auraly.Domain.Dispatching
Auraly.Application.Dispatching
Auraly.Infrastructure.Dispatching
Auraly.Contracts.Dispatching
```

Propietario de:

- despachos;
- documentos y líneas asignadas;
- faltantes;
- verificación;
- liberación;
- historial de custodia.

Dispatching consume contratos públicos de:

```text
Sales
Parties
Authorization
```

No referencia infraestructura ni entidades internas de Sales.

### 4.4 Motor

Procesadores:

```text
PostGoodsReceiptProcessor
PostManualInventoryMovementProcessor
ReverseManualInventoryMovementProcessor
ConfirmDispatchProcessor
VerifyDispatchProcessor
ReleaseDispatchProcessor
CancelDispatchProcessor
```

`DocumentProcessing` coordina y cada módulo conserva sus reglas.

---

## 5. Modelo de Entradas de mercancía

### 5.1 Agregado

```text
GoodsReceipt
  GoodsReceiptId
  TenantId
  BusinessId
  BranchId
  WarehouseId
  SupplierId
  CostCenterId?
  SupplierInvoiceNumber?
  SupplierInvoiceDate?
  ReceivedAt
  DueDate?
  PaymentTerm
  CurrencyCode
  Status
  Notes
  Subtotal
  DiscountTotal
  TaxTotal
  GrandTotal
  CreatesPayable
  PayableDocumentId?
  CreatedBy
  ConfirmedBy?
  CreatedAtUtc
  ConfirmedAtUtc?
  RowVersion
```

Estados:

```text
Draft
Submitted
Processing
Confirmed
Failed
Cancelled
Reversed
```

### 5.2 Líneas

```text
GoodsReceiptLine
  GoodsReceiptLineId
  GoodsReceiptId
  ProductId
  ProductBarcodeId?
  ProductUnitId
  CapturedQuantity
  UnitConversionFactor
  BaseQuantity
  SupplierProductCode?
  DescriptionSnapshot
  UnitCost
  CommercialDiscount
  TaxProfileId
  TaxProfileVersion
  TaxAmount
  LineSubtotal
  LineTotal
  ExpectedQuantity?
  DifferenceQuantity?
  Notes?
```

Usar `decimal`, nunca `double`.

### 5.3 Reglas

- una entrada confirmada siempre mueve inventario;
- la bodega debe estar activa y dentro del alcance;
- el proveedor debe estar activo;
- factura de proveedor duplicada se controla por proveedor, empresa y número normalizado;
- puede recibirse mercancía sin factura;
- si no hay factura, la recepción queda físicamente confirmada pero la obligación sigue la política definida;
- una entrada a crédito crea CxP;
- una compra pagada crea la obligación y su aplicación/pago dentro del mismo corte, o el modelo financiero equivalente definido;
- el costo promedio se actualiza dentro de la transacción;
- una entrada confirmada no se edita;
- se corrige mediante reversión o documento complementario;
- actualizar precios de venta es una propuesta separada y autorizada;
- no se generan retenciones, fletes ni descargues en el MVP.

### 5.4 Experiencia web

- encabezado de proveedor, bodega, factura y condición;
- grilla optimizada para lector y teclado;
- código leído agrega producto y deja foco listo;
- lectura repetida incrementa cantidad según configuración;
- edición de cantidad recalcula línea y totales;
- navegación entre cantidad, unidad, costo, descuento e impuesto;
- búsqueda por código, referencia, nombre, alias y código de proveedor;
- guardar temporal;
- recuperar temporal;
- eliminar línea;
- cancelar borrador;
- diferencias frente a cantidad esperada si existe documento origen;
- filtros y paginación en bandeja, no dentro del documento activo.

---

## 6. Modelo de Movimientos de inventario

### 6.1 Nombre

Menú:

```text
Inventario
  Movimientos de inventario
```

Acciones:

```text
Nueva entrada
Nueva salida
Consultar movimientos
```

No usar:

- `EnSa`;
- `EntradaSalida`;
- `Movimiento Ensa`;
- siglas heredadas.

### 6.2 Agregado

```text
ManualInventoryMovement
  ManualInventoryMovementId
  TenantId
  BusinessId
  BranchId
  WarehouseId
  Direction             Inbound | Outbound
  ReasonId
  CostCenterId?
  RelatedPartyId?
  DocumentNumber
  MovementDate
  Status
  Notes
  TotalValuation
  CreatedBy
  ConfirmedBy?
  ReversedByMovementId?
  CreatedAtUtc
  ConfirmedAtUtc?
  RowVersion
```

Estados:

```text
Draft
Submitted
Processing
Confirmed
Failed
Cancelled
Reversed
```

### 6.3 Líneas

```text
ManualInventoryMovementLine
  ManualInventoryMovementLineId
  ManualInventoryMovementId
  ProductId
  ProductBarcodeId?
  ProductUnitId
  CapturedQuantity
  UnitConversionFactor
  BaseQuantity
  UnitCostSnapshot
  LineValuation
  Notes?
```

### 6.4 Motivos

```text
InventoryMovementReason
  InventoryMovementReasonId
  Code
  Name
  AllowedDirection
  RequiresParty
  RequiresCostCenter
  AllowsExplicitInboundCost
  RequiresApproval
  Active
```

Ejemplos:

```text
INITIAL_BALANCE
FOUND_STOCK
MANUAL_CORRECTION_IN
INTERNAL_CONSUMPTION
SAMPLE
DONATION
LOSS_NOT_CLASSIFIED_AS_DAMAGE
MANUAL_CORRECTION_OUT
```

Averías deben registrarse en su módulo, no ocultarse bajo una salida genérica.

### 6.5 Valorización

Se mejora el modelo de Xion:

- salida manual: costo promedio autoritativo del momento;
- entrada manual normal: costo promedio vigente;
- saldo inicial u otro motivo autorizado: costo explícito con permiso;
- precio de venta no se usa para valorar inventario;
- el usuario no selecciona libremente una base que distorsione utilidad.

La línea guarda fotografía de costo y el kardex explica el cambio.

### 6.6 Negativos

La política `Warehouse.AllowNegativeStockSales` gobierna ventas, no movimientos manuales.

Para una salida manual:

- consulta existencia online al capturar o cambiar cantidad;
- revalida dentro de la transacción;
- no permite negativo;
- no opera offline;
- no existe autorización genérica para forzar saldo negativo.

Si se requiere corregir un saldo inconsistente, se realiza primero conteo/ajuste con trazabilidad.

### 6.7 Reversión

Anular un movimiento confirmado crea un movimiento inverso vinculado:

```text
Inbound  -> reversal Outbound
Outbound -> reversal Inbound
```

La reversión:

- usa las cantidades originales;
- conserva costo y referencia;
- exige motivo;
- revalida que sus efectos sean posibles;
- es idempotente;
- queda en kardex;
- no elimina el original.

---

## 7. Modelo de Despachos

### 7.1 Agregado

```text
Dispatch
  DispatchId
  TenantId
  BusinessId
  BranchId
  WarehouseId
  DispatchNumber
  ScheduledDate
  CarrierId?
  DriverId
  VehicleId?
  PlateSnapshot?
  RouteReference?
  Status
  Notes?
  CreatedBy
  ConfirmedBy?
  VerificationStartedAtUtc?
  VerifiedAtUtc?
  ReleasedAtUtc?
  RowVersion
```

Estados:

```text
Draft
Prepared
InVerification
Verified
Released
Cancelled
```

Transiciones:

```text
Draft -> Prepared -> InVerification -> Verified -> Released
Draft/Prepared -> Cancelled
InVerification -> Prepared      // reinicio autorizado
Verified -> InVerification      // reapertura excepcional y auditada
```

### 7.2 Documentos

```text
DispatchSourceDocument
  DispatchSourceDocumentId
  DispatchId
  SourceDocumentType
  SourceDocumentId
  SourceDocumentNumberSnapshot
  CustomerId
  CustomerNameSnapshot
  DeliveryAddressSnapshot
  SellerId?
  DocumentTotalSnapshot
  Status
```

Para el MVP:

```text
SourceDocumentType = SalesInvoice
```

Las remisiones continúan fuera del MVP. El contrato queda versionado para soportar otros documentos después sin copiar la entidad de Xion.

### 7.3 Líneas asignadas

```text
DispatchLine
  DispatchLineId
  DispatchId
  DispatchSourceDocumentId
  SourceLineId
  ProductId
  ProductBarcodeId?
  ProductUnitId
  DescriptionSnapshot
  ExpectedBaseQuantity
  PreviouslyDispatchedBaseQuantity
  AssignedBaseQuantity
  VerifiedBaseQuantity
  ShortageBaseQuantity
  OverageBaseQuantity
  Status
  Notes?
```

Estados de línea:

```text
Pending
PartiallyVerified
Verified
Short
Exception
```

Reglas:

- solo documentos confirmados y no anulados;
- una cantidad no puede asignarse por encima de lo pendiente de despacho;
- una factura no puede quedar en dos despachos activos con las mismas cantidades;
- se permite despacho parcial;
- cantidades se controlan en unidad base;
- un cambio posterior del producto no altera la fotografía;
- el despacho no cambia precio, impuestos, caja, cartera ni inventario;
- cancelar libera las cantidades asignadas;
- liberar deja inmutables documentos y líneas.

### 7.4 Faltantes

Xion usa registros `Agregar/Excluir` y booleanos `Pendiente`. Auraly usa:

```text
DispatchShortage
  DispatchShortageId
  SourceDispatchId
  SourceDispatchLineId
  ProductId
  MissingBaseQuantity
  ReasonId
  Notes?
  ResolutionStatus
  TargetDispatchId?
  ResolvedAtUtc?
  ResolvedBy?
```

Resoluciones:

```text
Pending
ReassignedToAnotherDispatch
CancelledByCommercialCorrection
ResolvedWithoutDispatch
```

No se modifica inventario silenciosamente. Si el faltante exige devolución, nota o ajuste comercial, se inicia el flujo del módulo correspondiente.

---

## 8. Verificación de despacho

### 8.1 Nombre de interfaz

```text
Despachos
  Preparar despachos
  Verificar despacho
  Historial de despachos
```

No se mostrará `Aduana` como nombre principal.

### 8.2 Modos

```text
Por documento
Agrupado por producto
```

Por documento conserva trazabilidad exacta de factura y cliente.

Agrupado por producto agiliza carga, pero el servidor debe distribuir cantidades verificadas de manera determinista entre líneas fuente y nunca perder el vínculo.

### 8.3 Captura con lector

Flujo:

1. abrir despacho preparado;
2. enfocar caja de escaneo;
3. leer código;
4. resolver producto, unidad y factor;
5. encontrar líneas pendientes compatibles;
6. incrementar cantidad verificada;
7. recalcular faltante;
8. mostrar confirmación visual/sonora;
9. dejar el foco listo para el siguiente producto.

La grilla permite:

- modificar cantidad;
- moverse por teclado;
- filtrar por encabezados;
- combinar filtros;
- mostrar pendientes;
- mostrar verificados;
- mostrar excepciones;
- buscar por factura, cliente, producto, referencia o código;
- deshacer último escaneo;
- reiniciar una línea con permiso;
- guardar progreso.

### 8.4 Mejoras sobre Xion

Un solo escaneo no marcará como verificada toda una cantidad esperada.

Ejemplo:

```text
Esperado:   12
Escaneado:   1
Verificado:  1
Pendiente:  11
```

Se admiten:

- incremento unitario;
- código de empaque con factor;
- cantidad digitada;
- lectura de balanza únicamente si el producto y flujo lo permiten;
- autorización para corrección manual;
- registro de cada evento.

### 8.5 Eventos

```text
DispatchVerificationEvent
  DispatchVerificationEventId
  DispatchId
  DispatchLineId
  ProductId
  Barcode?
  QuantityDelta
  EventType
  UserId
  DeviceId
  OccurredAtUtc
  IdempotencyKey
```

Tipos:

```text
Scanned
QuantityEdited
ScanUndone
LineReset
ShortageDeclared
UnexpectedProductRejected
SupervisorOverride
```

### 8.6 Productos inesperados y sobrantes

- código inexistente: no agrega línea;
- producto no incluido: muestra excepción;
- cantidad superior: bloquea por defecto;
- supervisor puede registrar observación, pero no convertirla en venta;
- todo sobrante debe resolverse antes de verificar;
- una cantidad faltante exige motivo.

### 8.7 Finalización

Para quedar `Verified`:

- todas las líneas están verificadas o tienen faltante declarado;
- no hay sobrantes sin resolver;
- el conductor y vehículo requeridos están definidos;
- el usuario tiene permiso;
- el servidor revalida documentos y cantidades;
- se guarda auditoría y outbox en un solo commit.

`Released` registra la entrega de custodia al transportador/conductor.

---

## 9. Online, offline y concurrencia

### 9.1 Entradas y movimientos

Entradas de mercancía y movimientos manuales:

- siempre online en el MVP;
- no se descargan a cajas;
- no usan inventario local;
- validan y confirman en servidor.

### 9.2 Despachos

Despachos y verificación son online en el primer MVP.

El progreso se guarda continuamente en servidor con:

- `rowversion`;
- eventos idempotentes;
- checkpoint de pantalla;
- recuperación después de recargar.

Un futuro modo offline puede reutilizar POS Edge para descargar únicamente el manifiesto activo y subir eventos, pero no entra hasta probar:

- exclusividad del despacho;
- resolución de escaneos concurrentes;
- expiración;
- revocación;
- merge de eventos;
- cierre idempotente.

### 9.3 Claims

Al iniciar verificación:

```text
DispatchVerificationClaim
  DispatchId
  DeviceId
  UserId
  ClaimedAtUtc
  ExpiresAtUtc
  HeartbeatAtUtc
```

Otra estación puede consultar, pero no editar mientras el claim esté vigente. Un supervisor puede liberar un claim vencido con auditoría.

---

## 10. Motor y transacciones

### 10.1 Confirmar entrada de mercancía

Transacción:

```text
GoodsReceipt (encabezado aceptado)
GoodsReceiptLines (detalle)
GoodsReceiptCostDocuments, líneas y distribuciones, si aplican
DocumentProcessingJob con el payload inmutable para el motor
eliminación de GoodsReceiptDraft y sus líneas, si existía
```

Si falla cualquiera de esas escrituras, la transacción completa hace rollback y no
queda encabezado, detalle, trabajo de motor ni eliminación parcial del borrador. La
confirmación no mueve inventario ni crea cuentas por pagar: el motor documental toma
el trabajo durable después del commit y aplica esos efectos en su propia etapa
idempotente y ordenada. La señal publicada después del commit solo despierta el
procesador; `DocumentProcessingJob` es la autoridad durable del trabajo.

La captura asigna una identidad de documento y una fecha de recepción estables antes
del primer intento. Un reintento técnico conserva ambos valores y el mismo payload;
una nueva entrada recibe otra identidad. Después de una aceptación, la interfaz espera
la eliminación durable de su recuperación local antes de cerrar el editor. Si recupera
una captura cuya identidad ya pertenece a una entrada aceptada, elimina esa
recuperación local: la entrada ya fue confirmada y no se transforma silenciosamente
en otra operación.

### 10.2 Confirmar movimiento manual

Transacción:

```text
ManualInventoryMovement
ManualInventoryMovementLines
InventoryTransactions
InventoryTransactionLines
InventoryBalances
CostLedger
Audit
Outbox
```

No crea:

- venta;
- compra;
- CxC;
- CxP;
- caja;
- documento fiscal.

### 10.3 Preparar despacho

Transacción:

```text
Dispatch
DispatchSourceDocuments
DispatchLines
DispatchAllocations
Audit
Outbox
```

Valida cantidades pendientes y evita doble asignación.

### 10.4 Verificar/liberar despacho

Transacción:

```text
verification quantities
shortages
status
claim release
custody record
audit
outbox
```

No crea un segundo movimiento de inventario.

### 10.5 Idempotencia

Todos los comandos confirmables incluyen:

```text
TenantId
BusinessId
UserId
DeviceId?
CorrelationId
IdempotencyKey
PayloadHash
ExpectedRowVersion
```

Misma clave y mismo payload devuelve el mismo resultado. Misma clave y payload diferente genera conflicto.

---

## 11. Permisos

### Purchasing

```text
purchasing.goods-receipts.view
purchasing.goods-receipts.create
purchasing.goods-receipts.edit-draft
purchasing.goods-receipts.confirm
purchasing.goods-receipts.cancel
purchasing.goods-receipts.reverse
purchasing.goods-receipts.view-cost
purchasing.goods-receipts.propose-price-change
purchasing.goods-receipts.export
```

### Inventory

```text
inventory.movements.view
inventory.movements.create-inbound
inventory.movements.create-outbound
inventory.movements.edit-draft
inventory.movements.confirm
inventory.movements.cancel
inventory.movements.reverse
inventory.movements.view-cost
inventory.movements.set-explicit-inbound-cost
inventory.movements.export
```

### Dispatching

```text
dispatches.view
dispatches.create
dispatches.edit-draft
dispatches.prepare
dispatches.cancel
dispatches.verify
dispatches.correct-verification
dispatches.declare-shortage
dispatches.reassign-shortage
dispatches.release
dispatches.reopen
dispatches.export
```

Los scopes aplican por empresa, sede y bodega. Menú y botones reflejan permisos, pero la API siempre vuelve a autorizar.

---

## 12. Reportes y consultas

Todas las bandejas:

- filtros por encabezado;
- combinación de filtros;
- paginación de servidor;
- orden determinista;
- exportación controlada;
- scopes de seguridad.

### Entradas

- entradas por fecha, proveedor, empresa, sede y bodega;
- productos recibidos;
- diferencias;
- costo recibido;
- impuestos;
- contado/crédito;
- CxP originada;
- entradas reversadas.

### Movimientos

- entradas/salidas por motivo;
- movimiento por producto;
- usuario;
- tercero;
- centro de costo;
- valoración;
- reversos;
- kardex.

### Despachos

- preparados, en verificación, verificados y liberados;
- documentos por despacho;
- productos y cantidades;
- conductor/transportador;
- tiempos de preparación y verificación;
- faltantes;
- productos reasignados;
- reaperturas;
- verificaciones manuales;
- historial de custodia.

Los reportes de despacho no se mezclan con ventas ni descuentan inventario otra vez.

---

## 13. Diseño web

### 13.1 Entradas de mercancía

```text
/dashboard/purchasing/goods-receipts
/dashboard/purchasing/goods-receipts/new
/dashboard/purchasing/goods-receipts/{id}
```

### 13.2 Movimientos

```text
/dashboard/inventory/movements
/dashboard/inventory/movements/new?direction=inbound
/dashboard/inventory/movements/new?direction=outbound
/dashboard/inventory/movements/{id}
```

### 13.3 Despachos

```text
/dashboard/dispatches
/dashboard/dispatches/new
/dashboard/dispatches/{id}
/dashboard/dispatches/{id}/verify
```

La vista de despacho tendrá:

- encabezado;
- transportador/conductor/vehículo;
- documentos disponibles;
- documentos seleccionados;
- productos consolidados;
- faltantes;
- resumen;
- acciones según estado.

La verificación tendrá:

- entrada de escaneo siempre enfocada;
- progreso total;
- documento/producto actual;
- grilla virtualizada;
- pendientes y excepciones;
- sonido/estado visual;
- botón de finalizar sujeto a validación.

No se migran visualmente las múltiples grillas WinForms. Se conserva el comportamiento y se reduce la carga cognitiva.

---

## 14. Datos SQL

Tablas nuevas bajo `dbo` y administradas solo por `Auraly.Database.sqlproj`:

```text
GoodsReceipts
GoodsReceiptLines

ManualInventoryMovements
ManualInventoryMovementLines
InventoryMovementReasons

Dispatches
DispatchSourceDocuments
DispatchLines
DispatchShortages
DispatchVerificationEvents
DispatchVerificationClaims
DispatchCustodyEvents
```

Índices mínimos:

```text
GoodsReceipts:
  TenantId, BusinessId, SupplierId, SupplierInvoiceNumber
  TenantId, WarehouseId, ReceivedAt

ManualInventoryMovements:
  TenantId, WarehouseId, MovementDate
  TenantId, ReasonId, MovementDate

Dispatches:
  TenantId, BusinessId, DispatchNumber UNIQUE
  TenantId, WarehouseId, Status, ScheduledDate
  DriverId, ScheduledDate

DispatchLines:
  DispatchId, Status
  SourceLineId
  ProductId

DispatchVerificationEvents:
  DispatchId, OccurredAtUtc
  TenantId, IdempotencyKey UNIQUE
```

Las restricciones únicas incluyen tenant/negocio cuando corresponda.

---

## 15. Migración desde Xion

### 15.1 Nombres

| Xion | Auraly |
|---|---|
| `EntradaDeMercancia` | `GoodsReceipt` |
| `EntradaDeMercanciaDetalle` | `GoodsReceiptLine` |
| `EnSa` | `ManualInventoryMovement` |
| `EnSaDetalle` | `ManualInventoryMovementLine` |
| `TipoEnSa.Entrada` | `Inbound` |
| `TipoEnSa.Salida` | `Outbound` |
| `CargueDeMercancia` | `Dispatch` |
| `CargueDeMercanciaFactura` | `DispatchSourceDocument` |
| `CargueDeMercanciaPendiente` | `DispatchShortage` / asignación |
| `ZAduana` | no se migra como tabla |
| `ZAduanaDetalle` | eventos/checkpoint de verificación |
| `Revisado` | estado + cantidades verificadas |
| `FechaRevisado` | `VerifiedAtUtc` |
| `ConductorId` | `DriverId` |

### 15.2 Datos

Los documentos históricos pueden migrarse para consulta si el piloto los necesita.

Para operación:

- migrar entradas abiertas o necesarias para conciliación;
- migrar movimientos que expliquen saldo inicial solo si hay calidad suficiente;
- migrar despachos no liberados;
- convertir faltantes abiertos;
- no migrar temporales locales abandonados;
- no reutilizar IDs como IDs Auraly;
- conservar mapeo legado.

### 15.3 Conciliación

- conteo de entradas;
- totales por proveedor;
- inventario y costo por bodega;
- movimientos por tipo/motivo;
- despachos por estado;
- documentos por despacho;
- cantidades esperadas/verificadas/faltantes;
- muestras manuales;
- informe de excepciones.

---

## 16. Pruebas obligatorias

### Entradas de mercancía

- temporal y recuperación;
- código repetido incrementa cantidad;
- cambio de cantidad recalcula;
- unidad/empaque;
- factura duplicada;
- recepción sin factura;
- contado/crédito;
- CxP;
- costo promedio;
- confirmación idempotente;
- reintento conserva identidad, fecha y payload;
- aceptación limpia durablemente la recuperación local;
- captura local obsoleta de una entrada aceptada se elimina;
- reversión;
- permisos;
- concurrencia;
- fallo antes y después de outbox.

### Movimientos

- entrada y salida;
- motivo por dirección;
- costo autorizado;
- salida sin existencia;
- intento de usar política de negativos de venta;
- doble confirmación;
- reversión;
- salida cuya reversión sí es posible;
- entrada cuya reversión falla por inventario consumido;
- permisos;
- kardex y reporte.

### Despachos

- crear con una y varias facturas;
- documento no confirmado;
- documento anulado;
- doble asignación;
- cantidades parciales;
- quitar documento;
- cancelar libera asignaciones;
- conductor obligatorio;
- filtros y paginación;
- dos usuarios modifican el mismo despacho;
- idempotencia.

### Verificación

- escaneo unitario;
- empaque;
- edición de cantidad;
- por factura;
- agrupado por producto;
- mismo producto en varias facturas;
- código inesperado;
- exceso;
- faltante;
- reasignación a otro despacho;
- deshacer;
- reinicio de navegador;
- claim vencido;
- reapertura autorizada;
- liberar;
- no crea inventario duplicado;
- auditoría de cada cambio.

### Integración

- venta confirmada aparece disponible;
- venta anulada se retira o bloquea;
- devolución no deja cantidad despachable incorrecta;
- Sales e Inventory no son escritos directamente por Dispatching;
- reportes concilian;
- Cloud y On-Premise ejecutan los mismos contratos.

---

## 17. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Confundir Aduana con comercio exterior | renombrar a Verificación de despacho |
| Descontar inventario dos veces | Dispatching no genera movimiento en MVP |
| Un escaneo marca todas las unidades | cantidades verificadas, no booleano |
| Mismo documento en dos despachos | asignación transaccional y constraints |
| Faltante oculto | entidad explícita, motivo y resolución |
| Despacho editado mientras se verifica | estado, `rowversion` y claim |
| Salida manual usada para ocultar avería | motivos y permisos; averías en módulo propio |
| Precio de venta usado como costo | valorización controlada por Inventory |
| Reversión deja negativo | revalidación y error operable |
| Reintroducir remisiones/rutas avanzadas | exclusiones explícitas |
| Copiar cientos de campos Xion | fotografías mínimas y contratos por módulo |
| Hacer verificación offline demasiado pronto | online primero; Edge futuro detrás de contrato |

---

## 18. Orden de implementación

Estos módulos no deben adelantarse al núcleo.

Orden:

1. catálogo, unidades, códigos y proveedores;
2. bodegas, saldos, kardex y costo;
3. entrada de mercancía + inventario + CxP;
4. movimientos manuales de inventario;
5. ventas y pedidos confirmados;
6. Despachos;
7. Verificación de despacho;
8. reportes y conciliación;
9. modo offline de verificación, solo si se prioriza después.

Despachos puede diseñarse desde ahora, pero su implementación comienza cuando Sales ofrece contratos estables de documentos y cantidades despachables.

---

## 19. Criterios de aceptación

- `Aduana` no aparece como dominio de comercio exterior.
- Cargue se presenta como Despachos.
- Entradas de mercancía, movimientos manuales y despachos no se confunden.
- Entrada confirmada mueve inventario y genera CxP cuando aplica.
- Movimiento manual no crea compra, venta ni cartera.
- Salida manual nunca hereda permiso de negativos de ventas.
- Despacho no descuenta inventario dos veces.
- Solo se asignan cantidades despachables.
- Verificación registra cantidades y eventos.
- El lector queda preparado para el siguiente producto.
- Cambiar cantidad recalcula progreso y faltante.
- No se finaliza con sobrantes sin resolver.
- Los faltantes son trazables y reasignables.
- El motor procesa confirmaciones de forma atómica e idempotente.
- Confirmados no se eliminan.
- Permisos y scopes se aplican en API.
- Consultas filtran por encabezados, combinan filtros y paginan.
- Reportes concilian con documentos fuente.
- Tablas y columnas usan nombres Auraly.
- El proyecto SQL continúa siendo la única autoridad del esquema.

---

## 20. Decisión final

Entradas de mercancía, movimientos de inventario, Despachos y Verificación de despacho quedan incluidos en el MVP.

La separación final es:

```text
Purchasing
  Entradas de mercancía

Inventory
  Existencias
  Kardex
  Movimientos de inventario
    Entradas manuales
    Salidas manuales
  Conteos
  Traslados
  Averías
  Conversiones

Dispatching
  Despachos
  Verificación de despacho
  Faltantes
  Historial de custodia
```

Se conserva el conocimiento operativo de Xion, pero se eliminan siglas, nombres ambiguos, booleanos insuficientes y campos heredados que pertenecen a funcionalidades excluidas.


## Precisión de sede, documentos y transportador — 17 de noviembre de 2026

Por decisión del usuario, el despacho pertenece a la sede seleccionada en el
contexto general. No se pide otra sede ni una bodega en su creación. Puede agrupar
facturas y comprobantes procesados de distintas bodegas de esa misma sede;
cada documento mantiene la bodega original. La verificación no vuelve a mover
inventario y las devoluciones conservan la factura fuente como origen del motor
existente. No se crea un segundo despacho ni un flujo alterno por bodega.

`DispatchService` y `SqlDispatchStore` mantienen el único writer. El contrato de
creación deja de exigir WarehouseId; la columna histórica del encabezado se hace
nullable y conserva sus valores anteriores, sin backfill ni asignaciones ficticias.
Los nuevos encabezados no tienen una bodega propia; detalle y manifiesto obtienen
la bodega de cada factura. Se retira el filtro obligatorio de bodega al adjuntar,
conservando la validación transaccional de sede, estado procesado, tipo permitido
y exclusividad del documento en un despacho activo.

El selector de transportadores pagina sobre el tenant, usuarios activos y el
permiso de ejecutar entregas en la sede (o asignación global). El mismo predicado
valida la identidad elegida al crear. El filtro opcional del transportador usa
@DriverUserId, distinto del @UserId del actor: la colisión anterior impedía la
consulta. La vista conserva el nombre recién seleccionado sin otro GET por ID.

Los candidatos usan desde/hasta, inicialmente el día de la sede, con páginas de
20. DateRangeVersion=2 aplica los límites UTC de días locales mediante
SqlBusinessLocalDates; no usa la zona del computador. El contrato previo conserva
su semántica. Se cuentan y ordenan los encabezados antes de enriquecer únicamente
la página con líneas y dirección. Presupuesto: dos viajes acotados (zona de sede
y comando único de conteo/página), p95 menor a un segundo en primera/segunda
página sobre 2.000 documentos y 6.000 líneas, sin carga de compilación concurrente.

Las consultas de la creación sólo se montan al abrirla y se separan por tenant y
sede. La página detrás del modal se pausa. Crear invalida el estado sin volver a
consultar candidatos que van a cerrarse; el manifiesto y la lista se cargan por sus
propietarios visibles. Se retiran las consultas periódicas de despachos: Actualizar
es explícito. Cada comando de detalle devuelve resumen, no el manifiesto completo;
por consistencia se permite una sola lectura del detalle activo tras ese comando,
sin la segunda lectura manual que existía en los handlers.

Despliegue coordinado de esquema, API y administración. El cambio de nulabilidad
es aditivo y el DACPAC lo vuelve a aplicar sólo si el esquema lo requiere. Los
encabezados históricos y sus facturas no se reescriben. Tras crear despachos sin
bodega de encabezado no se puede volver a exigir NOT NULL ni ejecutar binarios que
supongan una sola bodega; el rollback requiere detener escrituras y restaurar el
paquete/respaldo compatible, o corregir hacia adelante, sin inventar una bodega.


## Diseño de cierre ciego y liquidación — 17 de noviembre de 2026

Estado: captura ciega y tirilla implementadas y verificadas localmente; aceptación
de gastos y recaudos ampliados en verificación focalizada. Sustituye la propuesta
intermedia de dos actores que pudieran originar el cierre indistintamente. No
certifica una promoción a DEV o producción.

El transportador cierra y el administrativo valida. No son dos
liquidaciones alternativas. El transportador siempre entrega su declaración por
cada despacho; Liquidar es la revisión administrativa de ese mismo registro.

- Mientras está en ruta sólo existe el despacho y sus entregas, pagos y novedades.
  No se crea un cierre provisional ni se mantiene otro saldo que se alimente por
  cada factura.
- Al terminar, el transportador asignado y autenticado captura los importes reales
  por medio monetario, sin precarga, total esperado ni aviso de descuadre. No se
  fuerza a explicar una diferencia que la captura no debe revelar. El cierre es
  por despacho, de modo que puede cerrar varios desde su usuario en el día.
- Confirmar congela declaración, actor, fecha y cantidades, usando el registro
  DispatchSettlements y la unicidad existente por DispatchId. El despacho queda
  cerrado operativamente y pendiente de validación (los estados existentes
  PendingSettlement/PendingReview), sin aceptar nuevos repartos o otro cierre.
- Se imprime una tirilla de cierre de despacho con identificación, responsable,
  fecha y valores declarados. No es una factura ni una recepción ya validada. La
  impresión usa el catálogo/pipeline canónico; fallar o repetir impresión no
  registra otra declaración ni vuelve a ejecutar efectos financieros.
- El administrativo con dispatches.settle puede abrir Liquidar durante la ruta
  para consultar su avance; sólo después del cierre puede finalizar la validación.
  Mantiene la vista de valores esperados, declaración, recibido, gastos,
  consignaciones y soportes. Valida o explica diferencias y acepta una sola vez
  mediante el Settle existente. El transportador no obtiene ese permiso.
- El registro aparece en Cierres de caja como tipo Despacho al confirmarse la
  declaración, con origen en DispatchSettlements; la revisión entra al mismo
  módulo de despacho. No se crea una WorkSession artificial por despacho ni otro
  motor de conciliación. Los cierres de caja habituales siguen representando
  sesiones reales; recibir efectivo de un despacho se incluye una sola vez en
  la caja real del receptor, mediante el flujo existente.
- Tarjetas y transferencias se verifican por soporte y se distinguen del efectivo
  físico. Los medios/identidades de recaudo recorren el catálogo y los pagos
  canónicos; declarar un total no crea un nuevo pago ni determina una cuenta PUC.
- Una vez declarado se consulta/reimprime; una vez validado no se acepta ni se
  contabiliza de nuevo. Concurrencia, doble clic y reintento devuelven el mismo
  resultado o un conflicto explícito, conservando el responsable original.

No se habilita ahora una segunda entrada de cierre administrativo excepcional;
si posteriormente hace falta, requiere su propia decisión de autorización y
trazabilidad, dentro de este mismo propietario. El botón Liquidar no crea otro
cierre si ya existe la declaración.

Precisión del usuario durante la verificación: una venta ya facturada a crédito
conserva esa condición bloqueada en la entrega. El transportador confirma la
entrega y la factura firmada; no recibe allí anticipos ni cambia el medio de pago.
El recaudo posterior pertenece a Cartera/Recibo de caja. También se conserva esa
condición si la venta tenía una parte pagada al facturar: no se cobra otra vez ese
importe. No se consulta el catálogo monetario para ese formulario. Una visita no
entregada registra su motivo sin exigir una firma. Los recaudos previamente
aceptados conservan su evidencia y procesamiento; la nueva captura no los borra
al editar una entrega. La prueba de conservación de esos registros históricos no
representa el recorrido nuevo de una entrega a crédito sin recaudo.

La presentación muestra únicamente «Crédito» en el botón deshabilitado; la
restricción es de comportamiento, no un sufijo «bloqueado» en su nombre. Las
tres variantes de navegador cubren crédito total, crédito con pago previo y
visita no entregada. Registrar gastos sigue disponible después de la última
entrega mientras no exista la declaración del cierre; confirmar esa declaración
lo bloquea, conforme al mismo estado autorizado por el servidor.

Las correcciones de medio de pago en cierres de caja conservan la factura y el
soporte originales, conforme a la decisión de sesiones operativas. El ajuste
contable y su antes/después pertenecen a la conciliación canónica existente;
no se reescribe el documento fiscal para hacer cuadrar una caja.


### Precisiones del usuario sobre validación y gastos (17 de noviembre)

El administrativo puede abrir Liquidar en cualquier momento para consultar el
avance completo (entregas, recaudos, devoluciones y gastos). Mientras la ruta siga
abierta se identifica como despacho sin cerrar y no se permite finalizar. Al
cerrar el transportador aparecen su declaración y los faltantes/sobrantes por
medio. Ver el avance no crea un cierre, pago ni asiento.

El transportador sólo reporta el gasto con concepto, importe, descripción y soporte.
El concepto reutiliza ExpenseConcept y su cuenta, centro de costo y parametrización
del módulo Gastos; no habrá otro catálogo financiero de peajes o combustible ni
un PUC que deba elegir el transportador. Registrar el reporte no confirma un gasto,
abre cartera ni acepta una fuente contable.

La aprobación administrativa es el momento de crear el gasto por ExpenseService y
su writer/aceptación canónicos, vinculado al reporte del despacho. La aprobación
no puede quedar confirmada sin la aceptación y vínculo correspondiente. Debe
completar tercero real y datos de soporte/tributarios que exija el motor existente;
no se inventa proveedor ni se atribuye la compra al transportador por defecto.
Sólo el importe aprobado pagado con efectivo del despacho reduce su efectivo
esperado. La declaración del transportador permanece inmutable al revisar gastos.

Rechazar conserva reporte, responsable, soporte y motivo; no crea gasto contable
ni reduce efectivo esperado. Ejemplo: recaudo 100.000 y gasto reportado de 10.000
rechazado dejan 100.000 esperados; si se reciben 90.000 existe un faltante de 10.000,
no un gasto aprobado implícitamente. Aprobar/rechazar y reintentar no puede generar
otro gasto o descontarlo dos veces. Ningún gasto pendiente permite finalizar la
validación administrativa.

Cierres de caja tendrá un tipo Despacho, con su identificador canónico, actor,
fecha y estado. Al abrirlo se utiliza un modal propio de despacho con toda la
entrega y liquidación, reutilizado también por Liquidar desde Despachos. El modal
actual de cierres de sesión/caja se conserva; no se transforma en una pantalla
mezclada ni recibe controles específicos de transportadores. Este cambio visual
de bandeja no crea otro writer, sesión de caja o proceso de cierre.

La fila identifica explícitamente el tipo Caja o Despacho. Esa identidad decide
qué modal se abre: Caja conserva su modal actual y Despacho abre exclusivamente
el suyo. No se agregan pestañas, campos de despacho ni condiciones de liquidación
al modal de caja existente. El modal de despacho presenta identificación y
transportador, entregas, recaudos por medio, devoluciones, gastos con su revisión,
declaración original y recepción con faltantes/sobrantes. Entrar desde Cierres de
caja o desde Liquidar consulta el mismo despacho y la misma liquidación; no crea
otro registro ni permite validar dos veces. Sólo se carga el detalle del modal
abierto. La aceptación debe verificar ambos tipos de fila, la apertura del modal
correcto y la regresión del cierre de caja habitual.

La ampliación incorpora al reporte `ExpenseConceptId`, el vínculo único con
`Expenses` y la identidad/versionado de la forma de efectivo. Aprobar usa el mismo
`ExpenseService`, formulario y transacción de aceptación de Gastos; la aceptación,
el vínculo y el cambio a Aprobado se confirman juntos. El proveedor y su sede,
respaldo y retenciones se completan con datos reales. El reporte puede describirse
sin concepto mientras está sin conexión; sólo la aprobación autoriza su
clasificación financiera. Rechazar exige motivo y no registra gasto ni descuento.

El importe aprobado es el neto efectivamente pagado con efectivo del despacho.
El mismo motor contable registra el costo/gasto, IVA y retenciones aplicables y
acredita la forma de efectivo vigente al contabilizar. No abre una CxP impagada
por ese mismo dinero ni origina otro pago, writer o job. La fuente inmutable guarda
los hechos del pago, no una copia de la configuración. Los campos nuevos nulos se
omiten al serializar los contratos históricos. La anulación aislada de ese gasto
pagado queda bloqueada: no demuestra la devolución del efectivo y no se crea por
inferencia otro flujo de reembolso.

No se fabrican documentos para reportes históricos aprobados sin vínculo. Se
conservan y la recepción final exige conciliar su documento explícitamente.
Tampoco permite finalizar con reportes pendientes o gastos aceptados aún sin
procesar. El formulario consume el resultado autoritativo de aceptación sin
releer el despacho; Actualizar permite consultar después su procesamiento.

El aprobador conserva los permisos de liquidar, consultar y crear gastos. El
catálogo compartido permite a quien liquida y crea gastos consultar únicamente
las formas de efectivo del uso `SupplierPayments`, sin exigir acceso a CxP ni
conceder por esa excepción tarjetas o transferencias. La regresión SQL verifica
ese usuario mínimo, una sola aceptación/asiento, cuenta de costo y efectivo
vigentes al contabilizar y rechazo del cambio de tipo de una forma ya utilizada
por un gasto de despacho.

### Implementación de bandeja y modal por origen (17 de noviembre)

La separación de presentación ya está implementada. La consulta de bandeja
`WorkSessionService.ListClosureInboxAsync` reúne encabezados de
`WorkSessionClosures` y `DispatchSettlements`; no agrega tablas persistentes,
sesiones ficticias, estados ni escrituras financieras. Cada fila conserva el ID
de su cierre y su origen. El tipo Caja entrega el contrato habitual al modal
actual, cuyo cuerpo y comandos no cambian. El tipo Despacho abre
`DispatchSettlementDialog`, también utilizado por Revisar y liquidar desde
Despachos. Este componente consume `DispatchDeliveryService` y sus comandos
existentes; no contiene otra implementación de liquidación.

La consulta de ambos orígenes cuenta, ordena y pagina encabezados antes de leer
sus detalles. Usa días locales por sede. Los cierres de caja conservan el alcance
autorizado existente del tenant; los de despacho exigen `dispatches.settle` y se
restringen a la sede seleccionada, que también gobierna sus comandos. No se
concede ese permiso al transportador por mostrar la bandeja. El índice
`IX_DispatchSettlements_Business_ClosedAt` permite filtrar las declaraciones
existentes por sede y fecha sin modificar sus importes o identidad.

Presupuesto: una petición de bandeja, dos viajes SQL para una página sólo de
despachos (zonas y conteo/página), tres cuando contiene caja (más los totales por
conjunto de los cierres de esa página); hasta 100 encabezados por página. Objetivo
local menor a dos segundos con 400 cierres de prueba. No hay lectura por fila ni
consultas periódicas. Sólo el modal abierto carga ejecución; sus mutaciones
reutilizan la ejecución completa devuelta y marcan las otras vistas para una
lectura al volver a mostrarse. La revisión administrativa consulta online y no
sustituye un error por una copia offline del despacho.

La validación focalizada cubre permisos por origen, tenant/sede, límite local del
día, orden/paginación y número de consultas, apertura independiente de ambos
modales, móvil y recepción del efectivo. También conserva la regresión de
contabilización única del faltante. Esta separación no certifica las ampliaciones
pendientes de captura ciega, nuevos medios del transportador, tirilla y aceptación
contable de gastos descritas anteriormente.

El despliegue de esta separación requiere API y frontend coordinados. El esquema
sólo añade el índice; no transforma cierres históricos. El rollback de este slice
es volver al lector y componentes anteriores; el índice puede conservarse o
retirarse sin revertir dinero, documentos ni liquidaciones. No autoriza un
despliegue a DEV o producción.

### Captura compartida del soporte de recaudo (17 de noviembre)

La entrega utiliza `CardPaymentDetailsDialog` y `TransferPaymentDetailsDialog`,
los mismos componentes de POS y pedidos. En la entrega, pulsar Débito, Crédito o
Transferencia abre un único modal con el selector de la forma del tenant y sus
datos: franquicia/aprobación para tarjetas y referencia para transferencias.
Si hay una sola forma se selecciona automáticamente; si hay varias se elige
dentro de ese mismo modal. Los campos del soporte y Guardar se habilitan al
tener una forma válida. Efectivo no abre otro modal. El banco y auxiliar se obtienen
de la forma configurada; no hay otra selección contable independiente.

Tarjetas requieren franquicia del catálogo y aprobación; transferencia requiere
referencia y muestra su cuenta configurada. Los límites de la entrega son 80
caracteres de aprobación y 120 de referencia. Las observaciones de la entrega
conservan su campo propio. Guardar estos datos sólo actualiza el borrador; la
escritura sigue entrando por `RecordDispatchDeliveryRequest` al confirmar.
Cancelar una edición conserva la forma, los datos y la foto previamente
guardados. Elegir otra forma reinicia sólo el soporte del borrador del modal;
guardar ese cambio confirma la nueva forma y retira la foto anterior del
borrador de entrega. Volver a elegir la misma forma no abre otro modal.
«Datos del pago» permite editarlos expresamente. La selección nativa conserva
su único propietario de consulta; el modal compartido sólo recibe su campo y
el estado habilitado. La entrega no añade otra superficie de captura de tarjetas.
El diálogo estándar gobierna Escape cuando aloja el modal, para que cerrar el
selector no cierre además el formulario padre; el POS conserva su alojamiento.
Dialog, Popover y Select deben compartir la misma instancia de
`@radix-ui/react-dismissable-layer`. El lockfile y el override fijan la versión
1.1.19, ya requerida por Dialog: coexistían 1.1.11 y 1.1.19 y cada instancia se
consideraba la última capa, cerrando selector y modal con un mismo Escape.
No se implementa otro gestor de foco o cierre en la aplicación. La regresión
comprueba Escape dentro del catálogo, cancelación sin perder el borrador previo,
interacción después del cierre y los consumidores compartidos de pedidos.

El selector paginado es el único propietario de la consulta: una primera página
de hasta diez formas del tipo visible, caché por tenant/sede/uso/tipo y paginación
explícita. Abrir o cerrar el soporte no consulta nuevamente la ejecución ni crea
pagos. La franquicia se consulta sólo al abrir tarjeta. Se comprueban las cuatro
clases, varias formas de un tipo, cancelación, móvil y cantidad de peticiones en
`dispatch-payment-details.spec.ts`. Esta verificación de captura no certifica
la integración financiera, gastos o cierre ciego pendientes del diseño completo.

La regresión local de captura pasó en los cuatro medios y la de los componentes
compartidos en los cuatro medios de pedidos. Usa respuestas API controladas: no
es evidencia de contabilización de un despacho real. Comprueba selección única
y múltiple, borradores cancelados, reinicio del soporte al cambiar de forma,
límites móviles, ausencia de bloqueo de interacción al cerrar y un solo GET de
ejecución. Abrir el catálogo después de la selección automática reutiliza su
primera página. El soporte usa el diálogo estándar cuando nace dentro de la
entrega; se conserva la superficie de POS/pedidos y sus valores por defecto.
La auditoría de este ajuste no añade escrituras, permisos, endpoints, tablas,
cuentas, estados ni efectos fiscales. Se revierte junto con los componentes de
captura, sin modificar entregas confirmadas ni sus soportes persistidos.

### Verificación real de la captura y móvil (17 de noviembre)

La captura se verificó también con API, SQL Server y Blob emulado locales, sin
interceptar peticiones del navegador. Un usuario con sólo `dispatches.read` y
`dispatches.delivery.execute`, con rol Transportador, registra efectivo, débito,
crédito y transferencia con imagen, una devolución parcial y una visita fallida.
Conserva cuatro documentos pendientes para la prueba manual. Las ventas de esa
prueba se confirmaron mediante el checkout existente; no se insertaron ventas
procesadas, saldos ni movimientos desde el preparador local.

En 390×844 se verificaron las selecciones y el envío completo; en 390×430 se
comprobó que Guardar siga siendo alcanzable con menos espacio. Son pruebas de
navegador con interacción táctil emulada, no de un teclado físico Android/iOS.
Los botones de confirmación tienen al menos 44 px, el pie de acciones permanece
visible al recorrer el formulario y los nombres del selector se ajustan en varias
líneas. El modal hijo conserva la capa de interacción y oscurece el formulario
padre sin modificar la superficie habitual de POS/pedidos.

Al reabrir se recuperan el tipo, datos del soporte, imagen y devoluciones ya
guardados. Para una forma nativa existente se consulta su identidad una vez
(página de tamaño uno), porque el manifiesto no incluye su banco/auxiliar de
captura. La respuesta se reutiliza y no abre implícitamente otro modal ni vuelve
a subir la imagen. Cambiar de forma retira sólo el soporte del borrador anterior.
Si un documento contiene varias formas, este editor de captura única impide
sobrescribirlas accidentalmente y remite a la revisión del despacho. No cambia
la capacidad multiforma del contrato del servidor.

Las pruebas `DispatchDeliveryPaymentsTests` comprueban los cuatro medios con SQL
real: identidad/versionado, normalización antes de persistir, rechazo de versión
obsoleta, forma ajena, soporte incompleto e importe incorrecto; aislamiento por
tenant/transportador, autorización y replay sin duplicar ni mover inventario o
asientos desde la entrega. El error controlado de configuración devuelve 409
después de revertir la transacción. La autorización de adjuntos se ejecuta antes
de resolver almacenamiento; consulta no autoriza cargar archivos. La captura
rechaza imágenes que excedan 8 MB y conserva su validación en la misma API.

`dispatch.EffectivePayments` y `dispatch.PaymentTotals` se incluyen expresamente
en el proyecto desplegable. La revisión posterior mantiene un único writer de
entrega y las mismas tablas; las pruebas anteriores de separación de bandeja no
sustituyen la verificación de las ampliaciones financieras pendientes. Este
resultado certifica captura/persistencia y presentación móvil locales. Las
pruebas posteriores verificaron declaración ciega, tirilla y aprobación/rechazo
de gastos por el motor de Gastos; esto no certifica por sí solo la recepción
financiera final del despacho ni una promoción a DEV/producción.

### Bloqueo encontrado en recepción de ventas de contado (17 de noviembre)

La prueba local con ventas creadas por checkout confirmó que tres ventas de
25.000 ya pertenecían a la misma sesión de caja del receptor. Recibir el despacho
con un gasto aprobado de 1.000 añadió un `CashIn` de 74.000 en esa misma sesión.
`ClosurePaymentTotalsSql` incluye tanto los pagos originales como ese movimiento:
la recepción no demuestra un segundo ingreso de dinero y duplica su atribución.
La cuenta de caja no debe cuadrarse agregando otro cobro de ventas ya pagadas.

El usuario aclaró que una persona factura los pedidos y el transportador es otra
persona. En el recorrido de cobro al entregar, emitir la factura no demuestra que
el facturador recibiera dinero; por tanto no corresponde asumir un traslado
físico entre dos cajas. Debe cerrarse la atribución del cobro de esos pedidos sin
confundirlos con pedidos ya pagados o ventas cobradas en mostrador. No basta
excluir cualquier venta por el solo hecho de incorporarse después a un despacho:
eso también podría alterar una sesión de caja ya cerrada.

No se implementó un arreglo limitado a que ambas sesiones coincidan ni se creó
cartera para las ventas de contado. La decisión inicialmente aplazada sobre
«Por recaudar» fue sustituida por el acuerdo del 18 de noviembre, descrito al
final de este documento; no autoriza modificar documentos fiscales emitidos.
La recepción financiera no está certificada mientras persista esta discrepancia.

Propuesta posterior del usuario, entonces en análisis: facturar un pedido no
ingresa dinero en la caja del facturador; la recepción entra una sola vez al
liquidar el despacho. Para el recorrido con cobro en entrega, se recomienda que
el cierre ciego siga siendo sólo declaración y que el comando administrativo
`Settle` registre el efectivo realmente recibido. Antes de implementar se debe
distinguir ese recorrido de pedidos previamente pagados y pedidos recuperados y
cobrados en mostrador: tener `SourceOrderId` por sí solo no prueba dónde se cobró.
La atribución de caja, revisión del medio real y conciliación contable deben
conservar una sola fuente de cobro; no basta ocultar un importe en la pantalla.
Esta propuesta no se ha aplicado a movimientos, cuentas ni documentos existentes.

El entorno local `Testing` omite por diseño `DispatchSettlementHostedService`;
por ello la prueba de navegador llegó a `SettlementProcessing` sin ejecutar el
worker y no acreditó `Closed`. Las pruebas SQL con el worker canónico tienen una
evidencia distinta. No debe confundirse una aceptación HTTP de recepción con la
finalización de su procesamiento financiero.


### Acuerdo de implementación: recaudo y cierre únicos (18 de noviembre)

La tirilla de declaración v2 identifica transportador, placa (o ausencia explícita),
número y fecha del despacho, cantidad de documentos y fecha de cierre en la zona
de la sede. No imprime el GUID del usuario ni valores esperados del cierre ciego.
El usuario sigue en la auditoría interna. La versión se congela en el JSON del
cierre existente; reimprimir sólo representa esa versión y no ejecuta comandos.
Los cierres anteriores sin versión conservan v1 y su contenido original. No se
añaden tablas, viajes de lectura ni consultas de marca; los datos proceden del agregado
autorizado ya cargado. La versión se lee en el mismo comando mediante el ID único
del cierre; no requiere modificar el esquema. El cambio no reescribe snapshots,
hashes, pagos o historia; el rollback requiere el binario que conozca v2 o corregir
hacia adelante, sin cambiar el JSON aceptado.

Las decisiones siguientes sustituyen las propuestas anteriores sobre atribución
al facturador y encabezados de cierre separados. Están aprobadas por el usuario;
su descripción no acredita implementación ni pruebas hasta cerrar la evidencia.

- Toda venta originada en un pedido, tanto factura electrónica como comprobante,
  conserva su sesión de emisión pero no atribuye recaudo al facturador, aunque se
  recupere desde POS. El transportador registra el dinero real en una sesión de
  recaudo del despacho. Las ventas directas y los históricos ya emitidos conservan
  su atribución; incorporarlos a un despacho no borra dinero de una sesión cerrada.
- «Por recaudar» es una forma administrable del catálogo del tenant, con auxiliar
  explícito y un tipo técnico estable para su semántica. Es opcional para pedidos
  de contado facturados como comprobante, no crea CxC ni solicita cupo de crédito.
  La venta, impuestos y costo se contabilizan de inmediato en el motor actual.
  No es una espera fiscal o contable ni una quinta forma predeterminada obligatoria.
- La entrega registra la forma real y su soporte. La conciliación administrativa
  reubica el valor desde el auxiliar original al destino vigente, por el motor
  financiero existente. Con igual auxiliar e importe no crea otro asiento. No se
  vuelve a reconocer la venta ni se modifica el XML/CUFE/factura fiscal original.
  La factura a crédito conserva su condición y sólo confirma entrega y soporte.
- `WorkSessions` y `WorkSessionClosures` son los encabezados comunes, con tipo
  Normal/Dispatch y vínculo a DispatchId. Cada despacho tiene su sesión de recaudo
  y un cierre ciego, separado de la sesión POS. `DispatchSettlements` deja de ser
  un segundo encabezado persistido; su transición conserva identidades y datos
  históricos mediante migración versionada, sin repetir dinero ni asientos.
  Entregas, soportes, gastos y devoluciones permanecen en sus tablas propietarias.
- El administrativo observa el avance en cualquier momento. El transportador
  declara a ciegas una sola vez; sólo después se valida el cierre. Los gastos
  reportados no son un gasto financiero hasta su aprobación por Gastos. Rechazar
  no reduce efectivo. Una devolución de mercancía no recibida económicamente no
  crea un anticipo del cliente ni un reembolso ficticio: revierte el auxiliar de
  la venta mediante la devolución canónica. La recepción administrativa no suma
  otro ingreso a la sesión del administrativo.
- El modal de despacho es independiente pero sigue el diseño del cierre de caja:
  cabecera, medios con esperado/contado/diferencia, grupos plegables y verificación
  individual. Añade declaración, gastos y devoluciones. Se conservan catálogos,
  selectores, componentes de soporte, pipeline de impresión y reglas de permisos.

El corte debe preservar original y corrección, exclusión de doble despacho de un
mismo documento ante concurrencia, idempotencia de entregas y conciliación, y
saldos operativos/contables en cada retry. No introduce otro motor, worker,
writer financiero ni cola. Las sesiones normales deben excluir explícitamente
sesiones de despacho al recuperar una sesión POS. El catálogo no expone «Por
recaudar» como dinero real al recibir, pagar o cerrar.

Presupuesto: carga por despacho acotada por su contrato, páginas y agregados
antes de enriquecer bandejas; ningún I/O por factura para resolver cuentas o
atribuciones. El registro de cada venta sigue siendo una transacción documental
independiente. Se miden confirmación, recaudo, declaración y conciliación de forma
separada, con objetivo local de dos segundos para la operación interactiva. La
regresión incluye web/Edge, pedido recuperado, los cuatro medios, mismo/diferente
auxiliar, crédito, aprobación/rechazo de gastos, devoluciones parciales/totales,
fallo de configuración/período y replay sin duplicar inventario ni finanzas.

Sólo se despliega a DEV después de la regresión y desde un commit integrado en
origin/main. Producción sigue sin autorización. La migración de Megafruver se
ensaya localmente y conserva lo ya contabilizado, documentos DIAN y cartera;
ninguna transición reconstruye historia por el nombre de la forma o por tenant.


### Avance verificado y compuertas abiertas del corte común (18 de noviembre)

El encabezado común, sesión por despacho, historial de aceptación de entregas y
atribución persistida CollectionMode están implementados. La migración versionada
conserva el identificador y todos los campos del cierre anterior, admite la ausencia
histórica de declaración de tarjetas/transferencias y no repite efectos. Se integra
al pipeline de release existente, después de detener API/Worker y antes de calcular
el plan DACPAC. No hay otro encabezado financiero persistido ni writer de cierre:
`worksessions.ClosureAccept` inserta ambos tipos, `dispatch.Closures` sólo proyecta.
El mismo corte retira explícitamente `DispatchSettlementOperationClaimMark`:
`DispatchSettlementOperationClaimGet` ya adquiere y marca el trabajo atómicamente
y no conserva un segundo escritor. El publicador usa `DropObjectsNotInSource=False`,
por lo que eliminar sólo el archivo del proyecto dejaría ese procedimiento en
instalaciones existentes. El retiro es idempotente, también después de migrar el
encabezado, y no ejecuta ni repite efectos financieros.
`dispatch-obsolete-writer-verified.trx` aprueba las cuatro variantes de esquema y
declaraciones históricas, con dos ejecuciones de la migración en cada caso;
conserva identificadores/importes y cero asientos/movimientos nuevos. La prueba
ampliada fallaba antes del retiro (`dispatch-obsolete-writer-before.trx`). El
backend y el DACPAC Release compilan con cero errores y advertencias.

El documento técnico DispatchCashDifference existente incorpora los recaudos
confirmados al mismo processor contable. Su nombre técnico conserva las fuentes
históricas; no hay otro tipo, worker, cola o motor de reclasificación. Las colecciones
conservan IDs y valores; el processor resuelve todas las formas en un lote al
contabilizar, revierte los auxiliares efectivamente utilizados en la venta y netea
las partidas. Igual cuenta e importe no generan otro asiento. El resultado aplicado
congela la evidencia de forma/banco utilizada. `dispatch.EffectivePayments` presenta
esa evidencia para un cierre terminado, sin cambiar la fuente fiscal ni consultar
secretos/blob por fila. El helper de resolución usa diccionarios por documento;
no calcula los totales mediante recorridos anidados por factura. El límite deriva
de 500 documentos por despacho y 20 pagos por entrega, hasta 10.000 recaudos.

La finalización de la operación reutiliza `DispatchSettlementOperationComplete`
desde la transacción del processor al completar su trabajo, incluso cuando no
corresponde generar asiento. Un bloqueo de configuración usa la transición
Attention existente y conserva la misma fuente para el reproceso explícito. Una
respuesta tardía no puede degradar una operación Completed a Pending/Attention.
La recepción no crea CashIn en la sesión administrativa. El número de despacho
nuevo conserva todo el UUIDv7: su prefijo temporal truncado causaba colisiones al
crear dos despachos seguidos. Los números históricos se conservan.

Evidencia local disponible en `work/release-preparation-current`:

- `dispatch-common-regression.trx`: 39 casos SQL/API del encabezado, dos formatos
  históricos, repetición de migración, entrega/edit/replay, cuatro medios, gastos,
  permisos, cierre normal y bandeja por página. Primera página de 50 entre 400
  encabezados: 161 ms, dos viajes para despacho y tres para caja.
- `dispatch-attribution-final.trx`: nueve casos de pedidos por los cuatro medios,
  recuperación desde POS y venta directa; conservan inventario/asiento y excluyen
  el recaudo del emisor. Se rechaza la conversión implícita de comprobante a FE.
- `dispatch-common-ui-final.log`: seis casos de navegador del patrón de cierre,
  móvil, formato monetario, carga única y captura de efectivo/débito/crédito/transferencia.
- `dispatch-collections-finance.trx`: diez casos de regresión aprobados de cierre
  normal y crédito histórico; sus seis fallos iniciales pertenecían a la nueva
  preparación sin rol persistido de transportador. Se conserva ese resultado.
- `dispatch-collections-native.trx`: un caso financiero aprobado y cinco fallos
  por la colisión real de numeración UUIDv7; no se presenta esa ejecución como verde.
- `dispatch-collections-state.trx`: los seis casos financieros nuevos más la
  bandeja aprobaron después de corregir la causa. Cada caso crea dos despachos,
  de modo que la regresión comprueba la colisión dentro del propio caso. Incluye
  mismo auxiliar sin asiento, los cuatro destinos, FE, bloqueo de configuración,
  cambio de cuenta al reprocesar y replay. El último endurecimiento de identidad
  nativa/tipo de forma en entrega requiere su comprobación focalizada posterior.

Auditoría del corte todavía abierta; no autoriza despliegue. Deben cerrarse, por
el mismo motor de devoluciones, la reversión del recaudo no recibido y sus
redondeos/cargos, sin CustomerCredit ni reembolso ficticio. El query financiero
reserva ese vínculo por pago original. La ampliación ReverseUncollected del motor
canónico de devoluciones está implementada y tiene nueve casos iniciales aprobados
en dispatch-uncollected-vertical.trx; el documento propietario de devoluciones
detalla sus límites y la regresión ampliada. La opción técnica opcional Por recaudar
y su captura sólo en comprobantes de pedido se verifican en el avance posterior.
Las ventas previamente recaudadas y las devoluciones posteriores deben evaluarse
por separado: los resultados y límites actuales se detallan al final de este documento. No
se aceptará una segunda representación de dinero ni se borrará historia para
resolver estos casos. La migración mecánica del formato anterior completo,
volumen representativo de conciliación, regresión final del paquete, ensayo
Megafruver, revisión completa del diff e integración en origin/main siguen como
compuertas previas a DEV. No se ha desplegado este corte a DEV ni a producción.


Comprobación focalizada posterior: `dispatch-native-final.trx` aprueba diez casos
(seis financieros y cuatro capturas) después de exigir la identidad nativa para
recaudos de pedidos y bloquear el tipo de forma al utilizarla. La compilación de
`dispatch-native-final-backend-build.log` termina sin errores ni advertencias.
`dispatch-native-final-ui-browser.log` aprueba cinco escenarios, incluidos los
números completos en móvil y los tres modales de evidencia. TypeScript y ESLint
se verifican por separado; el error inicial de TypeScript era un nombre de dato
de la prueba que ocultaba `document`, corregido usando el global del navegador.

Hallazgos identificados en la primera auditoría del conjunto (los resultados
posteriores, al final de este documento, determinan cuáles siguen abiertos):
la devolución posterior a una reclasificación debe presupuestar los medios reales
del despacho y no asumir el método original de la factura. Debe cubrirse además
la venta parcialmente financiada: CreditAmount no equivale siempre al total de
la factura, y no puede perderse su porción de contado al atribuir el recaudo. La
valoración de retornos debe reutilizar la aritmética canónica, incluida la última
fracción/redondeo y los cargos, sin conservar las fórmulas proporcionales de dos
decimales en captura/cierre. La prueba de migración histórica actualmente cubre
los formatos del encabezado dentro del esquema nuevo: falta demostrar también
el ALTER/backfill desde la forma física anterior de las tablas comunes, con sus
conteos y totales derivados. Estos hallazgos no se ocultan con compatibilidad por
tenant/fecha, saldos sintéticos, asientos inventados ni eliminación de historia.


### Auditoría de devolución y atribución de diferencias

`dispatch-collected-refund.trx` aprueba 17 casos, incluidos crédito parcial,
reserva de devoluciones, y tarjeta real después de recaudar (comprobante/FE).
El detalle de identidad del reembolso está en el diseño propietario de devoluciones.
La ampliación posterior de la matriz verifica el aislamiento Normal/Dispatch y
faltantes/sobrantes contra el auxiliar real de efectivo; su resultado debe quedar
verde antes del release. La aceptación comparte el guard de sesiones normales para
cartera/comprobantes y restringe las capturas POS a Kind Normal. No cambia las
sesiones de emisión guardadas en documentos históricos.

Una diferencia del despacho se atribuye a la única forma nativa de efectivo usada;
si hay varias o no hubo efectivo, el administrativo selecciona la forma de efectivo
que la asume. El selector reutiliza el catálogo de recibos. Su ID forma parte del
comando idempotente y de la operación existente; el processor resuelve su auxiliar
vigente dentro del lote de recaudos, sin viaje por documento. El tipo se bloquea al
aceptar y la cuenta al contabilizar. No se elige una caja predeterminada en silencio.
La fecha del hecho de conciliación representa ReceivedAt en la zona horaria de la
sede, obtenida en la misma consulta; no toma el día UTC para elegir el período.
Estas ampliaciones no cierran las demás compuertas pendientes del corte.


### Devolución de una venta recaudada antes del despacho

Decisión del usuario del 18 de noviembre: el administrativo registra el reembolso
real desde Devoluciones y lo vincula antes de conciliar. No se registra otro pago
desde la entrega ni se convierte el importe en un anticipo. El flujo operativo de
pedidos no recibe dinero al crear el pedido; este caso corresponde a ventas cuyo
recaudo ya existe en una sesión anterior.

`DispatchDeliveryService` y el mismo store aceptan hasta 500 identificadores de
devoluciones, distintos y ordenados. El comando de conciliación los conserva en
`DispatchSettlementOperations.ReturnIdsJson`; no existe otro proceso ni tabla de
reembolsos. En la transacción existente se bloquean el despacho y sus ventas,
se comprueba sede/tenant, venta original, aceptación posterior a la asignación,
procesamiento operativo y financiero terminado, valor, productos, cantidades y
tratamiento de inventario. Los documentos elegidos deben cubrir exactamente la
devolución capturada; si falta el reembolso de la porción cobrada, no se acepta la
conciliación. Una aplicación real a cartera y un reembolso real conservan sus
resoluciones diferentes; no se infieren movimientos nuevos.

La operación enlaza las fuentes financieras existentes. El worker no crea otra
devolución para esa venta, y el processor conserva su único writer. Los totales
del despacho distinguen el importe realmente reembolsado de la aplicación a
cartera, también en una venta mixta. Repetir la conciliación exige los mismos
identificadores y devuelve el estado sin duplicar dinero, inventario ni asientos.

El cierre administrativo ofrece la captura existente de Devoluciones en otra
pestaña, con el número de venta precargado para búsqueda. Un solo selector
paginado consulta los documentos de la venta elegida, al abrirlo, con sus permisos
habituales; no se hacen consultas por cada venta de la grilla. Los vínculos
aceptados viajan en otro resultset de la consulta de ejecución existente, sin un
viaje adicional. El presupuesto conserva siete comandos para la lectura de un
despacho y un lote de aceptación, hasta 500 documentos/10.000 líneas. Se prueban
los caminos afectados antes de publicar; este texto no certifica por sí solo
pruebas ni un despliegue.

Evidencia del incremento en `work/release-preparation-current`: el esquema y el
backend compilaron sin advertencias; `linked-return-first.trx` aprobó tres casos
de devolución administrativa (comprobante parcial, comprobante no entregado y
factura mixta). Cada aceptación y su respuesta usaron ocho comandos SQL y tardaron
entre 66 y 396 ms, sin navegador ni builds concurrentes. Se comprobaron rechazo
sin vínculo y con ID inexistente, aislamiento en la consulta por venta y tenant,
repetición con los mismos documentos y ausencia de otro reembolso, asiento,
anticipo o entrada de inventario.

`linked-return-dispatch-regression.trx` aprobó 36 casos afectados, incluidos los
cuatro formatos de cierre histórico. Candidatos conservó dos comandos con
2.000 encabezados/6.000 líneas y p95 27,82 ms para las páginas primera y segunda;
ejecución conservó siete comandos, 35,91 ms con 500 documentos/1.500 líneas.
`linked-return-browser-first.log` aprobó cuatro casos de interfaz; la ampliación
de reapertura de `linked-return-browser-reopen.log` aprobó el caso nuevo sin
repetir los tres de disposición sin cambios. Los resultados se solapan y no se
suman como casos distintos. Son pruebas con API simulada y navegador real,
incluido móvil de 390 px; todavía no son validación contra el backend de DEV.

Despliegue: primero el esquema nullable y las funciones, después API y frontend.
Las operaciones históricas conservan NULL y no se reprocesan. Antes de aceptar
vínculos se puede volver al binario anterior; después debe corregirse hacia
adelante o restaurarse coherentemente la base y los binarios. Un worker anterior
no conoce los documentos vinculados y no debe procesar estas operaciones.

### Devoluciones anteriores a la asignación y coordinación de la entrega

El cierre del diseño solicitado el 18 de noviembre aplica estas reglas en los
propietarios existentes, sin reparar documentos antiguos ni crear otro proceso:

- Candidatos y asignación descuentan las devoluciones ya aceptadas, incluidas
  las pendientes de procesamiento que reservan mercancía. Una devolución total
  retira la venta de candidatos y la aceptación comprueba nuevamente el saldo.
- `DispatchLines` conserva cantidad y valor netos asignados. El encabezado guarda
  el total neto y `CreditAmountSnapshot`, que preserva la condición de crédito
  después de sus devoluciones previas; los abonos de cartera no se convierten en
  un nuevo recaudo del transportador.
- La captura de otra devolución usa las cantidades/importes originales y los
  retornos acumulados por `SalesReturnAmountCalculator`, incluidos el residuo de
  la última fracción y el redondeo. La pantalla muestra el disponible y el
  manifiesto no vuelve a incluir lo que ya había sido devuelto.
- Después de asignar una venta, las nuevas devoluciones se capturan en la entrega.
  En borrador se puede retirar primero la venta y devolverla normalmente. Para
  ventas ya cobradas, el reembolso administrativo acordado sigue usando
  Devoluciones: debe corresponder a mercancía declarada en la entrega y aún no
  reembolsada; sus documentos reales se vinculan antes de conciliar.
- El bloqueo del despacho precede al de la venta en ambas aceptaciones. La
  validación de devolución amplía la consulta existente, no agrega viajes SQL.
  El conteo y la página de candidatos siguen compartiendo un comando después de
  resolver la zona horaria; el enriquecimiento de líneas queda limitado a la
  página. Se conservan 500 documentos y 10.000 líneas por manifiesto.

El corte versionado rellena el nuevo campo de crédito con el valor original que
usaban los manifiestos existentes, sin cambiar sus cantidades, total, pagos ni
asientos. Las nuevas asignaciones guardan el neto. No hay una condición por
tenant, versión o fecha en el motor. El cambio requiere esquema y binarios
coordinados; tras aceptar fuentes nuevas, el rollback es corrección hacia adelante
o restauración coherente de base y binarios, nunca reescritura del histórico.

El motivo de la devolución procede del catálogo activo `DeliveryReturn` de la
sede. Se carga sólo al abrir el editor y se valida por conjunto en el comando
existente. Editar únicamente el pago conserva el motivo ya aceptado, aunque
haya dejado de estar activo; una captura nueva debe elegir un motivo vigente.

La espera del coordinador de liquidación reutiliza el vencimiento durable del
lease. Al reiniciar programa una sola activación para el próximo vencimiento;
no introduce polling ni otra cola. Una operación entregada al motor contable
permanece bajo ese propietario y no vuelve a reclamarse por el despacho.

Evidencia: `dispatch-coordination-final.trx` aprueba 21 escenarios de netos,
fracciones, concurrencia, crédito, reembolsos y upgrade; la regresión focalizada
`dispatch-return-catalog-verified.trx` aprueba diez escenarios, incluido el rechazo
de un motivo inexistente. El reinicio antes de vencer el lease se reprodujo en
rojo y pasó en `dispatch-close-gaps-green.trx`; los dos fallos de esa ejecución
se corrigieron y revalidaron en las ejecuciones focalizadas posteriores.

### Evidencia de Edge, catálogo y aislamiento sin conexión

El contrato de venta conserva la atribución del recaudo como un hecho inmutable,
no como una copia de configuración contable. Los emisores nuevos de pedidos,
web y Edge, guardan CollectionMode Dispatch. El valor Session es cero y se omite
al serializar: un documento antiguo en cola que no tenía ese campo conserva su
representación exacta, hash y atribución. El servidor no vuelve a deducir ese hecho
por tenant, fecha ni por consultar la configuración actual. Las cuentas automáticas
siguen resolviéndose al contabilizar. El cierre local lee la atribución del mismo
JSON ya persistido y suma CollectedAmount, incluido el redondeo, sin otra consulta
por venta ni nueva tabla local.

OrderCollectionPaymentPolicy es una validación pura del contrato compartida por
los emisores y la aceptación; no es otro motor. Por recaudar usa el catálogo nativo
con cuenta explícita, sólo para el importe completo de un comprobante de pedido,
sin crédito ni evidencia de dinero recibido. No se crea auxiliar predeterminado
ni se modifica el catálogo inicial de cuatro formas. No se ofrece como dinero en
POS directo, factura electrónica, recibo, cartera, proveedor o nómina. Su uso en
comprobantes de pedido no abre una cuenta por cobrar.

Evidencia conservada en work/release-preparation-current:

- edge-order-custody-foundation-final.trx: 46 pruebas aprobadas del servicio de
  finalización local, incluidos cinco medios en pedido recuperado, rechazo de
  Por recaudar fuera del contexto permitido y estabilidad de documentos antiguos.
- edge-order-custody-host-final.trx: seis pruebas aprobadas del cierre local,
  exclusión del dinero de despacho, conservación del histórico, cartera e impresión.
- edge-order-custody-sql.trx: 41 de 42 pruebas aprobadas. La ampliación de origen
  reutilizaba un consecutivo entre dos escenarios; corregida la prueba,
  edge-order-custody-source-final.trx aprueba esos dos casos sin repetir el conjunto.
  El backend Debug y el esquema Release compilan sin errores ni advertencias.
- El recorrido de candidatos, con 2.000 encabezados y 6.000 líneas, usa dos comandos
  SQL y p95 64,28 ms para las páginas primera y segunda. La ejecución de 500
  documentos / 1.500 líneas usa siete comandos y 52,48 ms. La matriz también
  comprueba dos reordenamientos consecutivos, replay sin escritura adicional y
  rechazo de una versión obsoleta; la actualización se hace por conjunto.
- dispatch-scope-browser.log conserva once aprobados y cuatro fallos de selectores
  de prueba. dispatch-scope-browser-final.log aprueba los cinco casos focalizados
  después de corregir los selectores: cuatro de aislamiento y uno de Por recaudar.
  Son quince escenarios distintos aprobados entre ambos recorridos, no una nueva
  regresión total por cada ajuste de prueba.

La misma cola local tiene un índice por tenant, sede y usuario, y lectura ordenada
limitada a veinte comandos independientes por señal. El propietario PwaProvider
conserva la sincronización por conectividad, visibilidad, foco o acción explícita;
no hay polling. Si quedan comandos, muestra el pendiente y permite continuar.
La caché de ejecución y los soportes tienen el mismo alcance; los errores HTTP de
autorización no se sustituyen por caché. La vista oculta sus datos al fallar y su
clave de consulta también incluye el usuario. Las mutaciones aprovechan la
respuesta autoritativa y no vuelven a consultar la ejecución para reconstruirla.
Las escrituras IndexedDB sólo se consideran exitosas al confirmar la transacción.

El cambio de formato local conserva los registros antiguos sin identidad. No los
asigna al usuario actual ni los envía con otra identidad. Muestra un aviso de que
requieren revisión. Antes de un corte real debe demostrarse que las instalaciones
no conservan comandos antiguos pendientes, o acordarse y ensayarse su atribución
explícita. No se borran ni se presume quién los creó. No hay un proceso automático
de reparación añadido a esta implementación.

Estado de las compuertas del paquete, verificado el 19 de noviembre de 2026:

1. El lote de pedidos distingue el pago confirmado de otros canales del bot del
   pedido operativo sin cobro, como especifica el diseño propietario de Pedidos.
   El estado, importe y moneda se comprueban en la consulta transaccional de
   origen; doce casos focalizados están aprobados. La decisión final del usuario
   conserva la recuperación y facturación habituales y retira la propuesta de
   restringirlas por el registro del bot. No se agrega esa restricción ni se
   cambia ahora la integración del bot; su comportamiento previo se conserva.
2. El reembolso administrativo desde Devoluciones tiene evidencia SQL, de
   interfaz local y de navegador contra DEV: `dev-011rc2-administrative-refund.log`.
   La venta cobrada conserva su pago; el reembolso real queda vinculado al
   despacho y su liquidación/replay no repiten la salida de dinero. El saldo de
   la sesión antes y después de liquidar es idéntico.
3. Las devoluciones previas, cargos, última fracción, datáfonos y modo comercial
   conservan sus pruebas focalizadas. `private-evidence-and-ci-isolation.trx`
   aprueba 42 casos, incluidos los caminos financieros de despacho y su reinicio
   durante el lease. La prueba real `dev-011rc2-dispatch-final.log` completa
   cuatro medios, devolución parcial, no entrega, crédito, soporte privado,
   aprobación/rechazo de gastos, cierre ciego, corrección, verificación y replay.
   La sesión del facturador no recibe dinero duplicado.
4. El paquete se integró en `origin/main` y se publicó en DEV como `0.1.1-rc2`,
   commit `92edad4503a7e99af187724453f1ba416c6694a9`; los cinco componentes y la
   comprobación de entorno terminaron correctamente en la ejecución
   [44471727629](https://github.com/rijako24/auraly/actions/runs/44471727629).
   La repetición del despliegue de esquema/migraciones sobre la copia histórica
   aislada conserva 151.813 filas operativas/financieras y 35.070 fiscales/de
   configuración, sin altas ni bajas. Sólo cambian los dos `UpdatedAt` de seed
   identificados. Las cinco tablas de configuración nativa conservan sus hashes
   completos. No se suman suites superpuestas como casos distintos.

Producción no está autorizada ni se ha desplegado. El paquete inmutable y el
plan revisado de Megafruver están preparados para una promoción explícita,
conservando historia y los controles de concurrencia del corte. Los pendientes
fiscales históricos, incluido el comprador sin identificación verificable de
FVL12368, siguen siendo excepciones de datos previos: esta validación técnica no
certifica su aceptación DIAN ni autoriza inventar datos o reenviar documentos.


La comprobación focalizada final del ciclo de vida del proveedor de sincronización
(`dispatch-scope-lifecycle-browser.log`) aprueba cuatro escenarios. Una operación
anterior no continúa sincronizando otros recursos después de cambiar el contexto,
y el indicador se cierra al terminar su etapa incluso si cambió la identidad.


### Prueba real del camino sin conexión y del reintento del soporte

La prueba de transferencia con imagen descubrió que useMutation esperaba la red
antes de ejecutar la persistencia local. Sólo esa mutación offline usa ahora
networkMode always. No se habilitan otras operaciones remotas sin conexión.
La cola publica su respuesta autoritativa al sincronizar; el propietario de la
vista la aplica a la misma caché. Se desactiva el GET automático de reconexión
para que no sobrescriba la entrega local antes de terminar el envío. El evento
local no es otro motor, transporte de backend ni fuente de verdad financiera.

Los soportes de una operación se leen en una sola transacción IndexedDB. Después
de cada subida exitosa, su URL queda persistida en la misma operación y se retira
únicamente ese archivo de los pendientes antes de subir el siguiente o enviar
el comando. Una respuesta perdida reutiliza exactamente el mismo payload, URL y
clave; no vuelve a subir la imagen ni cambia el hash del comando ya aceptado.
Un error de envío se muestra y conserva la operación, sin darla por confirmada.
El replay usa el envío HTTP del mismo adaptador y endpoint, sin el fallback de
captura local. Perder conexión entre el soporte y el comando nunca convierte una
respuesta optimista sin conexión en confirmación del servidor. No se agrega otra
cola, motor ni mecanismo de reintento; el lote sigue limitado a veinte comandos
independientes por señal y sólo elimina cada uno tras la respuesta del servidor.
Web Locks mantiene un único emisor por identidad entre pestañas durante todo el
lote, incluidos los soportes. Una segunda pestaña conserva el pendiente visible,
sin esperar ni enviar otro payload con la misma clave y otra URL. El bloqueo se
libera al finalizar o cerrar la pestaña. Si el navegador no ofrece esta capacidad,
se informa el impedimento y se conservan los comandos; no hay un emisor alterno.

La regresión `dispatch-outbox-concurrency-verified.log` verifica ocho escenarios con
IndexedDB real y API simulada: aislamiento de caché y permisos, lote de veinte,
conservación de pendientes ajenos e históricos, fallo del segundo soporte con
recarga, pérdida de conexión antes de enviar entrega o gasto y dos pestañas
enviando la misma operación. Los fallos previos se conservan en
`dispatch-proof-connection-before.log` y `dispatch-outbox-concurrent-before.log`. El
reintento sube sólo los archivos que faltan y conserva la misma clave del comando.
Estas pruebas locales no sustituyen la verificación posterior contra DEV.
El paquete standalone aprueba seis recorridos afectados en
`dispatch-outbox-standalone-browser.log`. El ajuste final para navegadores sin
Web Locks se verificó junto al caso de dos pestañas en
`dispatch-outbox-final-browser.log`: conserva el pendiente y muestra el motivo,
sin enviar ni subir soportes; una cola vacía no genera ese aviso. Ese caso añade
un noveno escenario de la cola, no otra regresión completa. TypeScript, ESLint de
los archivos afectados, build optimizado y preparación standalone finales están
aprobados. Los resultados de ejecuciones solapadas no se suman.

- dispatch-response-browser.log conserva ocho aprobados y el caso offline fallido
  que descubrió la espera de red.
- dispatch-transfer-offline-final.log aprueba ese camino después de corregirlo.
- dispatch-transfer-retry-browser.log aprueba dos recorridos: transferencia con
  imagen sin señal y respuesta fallida seguida de reintento explícito. Comprueba
  una sola subida, payload idéntico, actualización de la vista y un solo GET de
  ejecución; no repite la regresión general por esa corrección.
- release-payroll-pricing-portfolio.trx aprueba 34 escenarios SQL del resto del
  paquete: perfiles/selección/novedades/contabilización/pago de nómina, publicación
  de precios/canales y pago compartido desde compra y comprobante.
- spanish-inventory-sql.trx conserva tres casos de autorización de inventario y el
  recorrido válido completo de terceros aprobados. Sus tres fallos de selección
  del endpoint para probar mensajes se corrigieron en
  spanish-public-validation-final.trx (cuatro aprobados).

Estas comprobaciones se complementan con las compuertas financieras, de upgrade
y de navegador descritas arriba; por sí solas no sustituyen esas evidencias.

La auditoría posterior encontró y corrigió la devolución que seguía usando el
datáfono original después de corregir el recaudo en un cierre normal. También
verificó el modo sin contabilidad con y sin forma nativa, sin exigir ni fabricar
un asiento histórico. La evidencia y la regla vigente están en los diseños de
devoluciones y cierre de caja. Once escenarios iniciales y diecinueve de regresión
dirigida están aprobados; ocho revalidaciones finales incluyen las dos variantes
comerciales, los datáfonos, permisos y el presupuesto de lectura del despacho.
No se suman esas ejecuciones como casos distintos.

TypeScript y el build de Next.js/standalone terminaron correctamente; ESLint
terminó sin errores y con seis advertencias en archivos ajenos al incremento.
El build de backend y DACPAC terminó sin errores ni advertencias. El reembolso
administrativo y la emisión por lote de pedidos con pago confirmado tienen ahora
la evidencia focalizada indicada en sus secciones propietarias. Las devoluciones
previas a la asignación y el reinicio durante el lease tienen la evidencia
posterior indicada arriba. El cierre del release exige completar la validación
real en DEV, el ensayo completo del upgrade y la auditoría del paquete completo.
Producción no está autorizada. La consulta agregada de sólo lectura del
18 de noviembre confirmó cero despachos, fuentes, entregas y liquidaciones
inconclusas en Megafruver; no existen manifiestos de ese tenant que puedan haber
originado capturas de entrega con la aplicación anterior. La evidencia se conserva
en `work/release-preparation-current/megafruver-production-dispatch-preflight.json`.

### Soportes privados y referencia estable del despacho

La carga de soportes reutiliza `IBlobStorageService`/`BlobStorageService`; se retira
`AzureBlobObjectStorage`, que intentaba crear acceso público incompatible con la
política del almacenamiento. No cambia permisos del contenedor ni infraestructura.
El documento conserva `EvidenceUrl` sin firma. La respuesta HTTP agrega
`EvidenceReadUrl` temporal mediante `IMediaUrlResolver.ResolveBusinessBatchAsync`;
la interfaz usa ese enlace exclusivamente para visualizar. El enlace dura 15
minutos; Actualizar o reabrir obtiene otro lote, sin polling. Nunca vuelve a enviar
ni persiste el SAS como parte de la entrega, gasto, huella o cierre inmutable.
El campo nulo se omite al serializar el contrato almacenado para conservar las
huellas de cierres anteriores.

Cada ejecución reúne soportes de pagos y gastos y los resuelve en un lote. La
firma solo reconoce URLs del almacenamiento configurado y del contenedor de la
sede autorizada; no firma URLs de otras sedes. No consulta existencia por objeto.
Presupuesto: cero consultas SQL adicionales, cero lecturas Blob por soporte y
como máximo una solicitud de clave de delegación en frío por lote; la clave
compartida se reutiliza con exclusión mutua. Una carga conserva las dos operaciones
del writer existente (crear contenedor privado si falta y subir archivo).
`ProductImageTenantStorageTests` verifica 50 soportes, aislamiento, firma de solo
lectura, reapertura y cancelación. `DispatchDeliveryPaymentsTests` ejercita la
carga HTTP y confirma referencia estable, ausencia de SAS persistido, autorización
y replay sin efectos duplicados.

No requiere migración de datos, tablas ni reprocesamiento financiero. El rollback
debe conservar el writer privado: volver al cargador público reproduciría el fallo
en Azure. Los documentos anteriores siguen conservando sus referencias y sus
huellas; no se reescriben sus archivos ni sus cierres.


### Ajuste de captura y consulta del despacho (20 de noviembre)

La ruta pasa a ser un filtro de los documentos por la sede de destino del
cliente incluida en sus paradas activas. No se solicita ni se guarda como dato
del encabezado de un despacho nuevo. Los despachos históricos conservan su ruta.
La búsqueda de factura o cliente queda visible; desde/hasta, ruta y tipo de
documento se pueden expandir y contraer sin borrar sus valores ni la selección.
Cambiar el rango de fechas conserva la regla de limpiar la selección anterior.
El selector de rutas reutiliza la consulta paginada de `RouteService` /
`SqlRouteStore`, con permiso `dispatches.create`, solamente rutas activas de la
sede autorizada y páginas de diez. Se retira la antigua proyección no paginada
de rutas y bodegas del store de despachos. Contraer los filtros no consulta datos.

El selector de transportadores consume la proyección canónica de terceros con
rol `Carrier`, activos tanto el tercero como el rol. Al crear se resuelve su
usuario activo vinculado y su permiso de entregas dentro de la transacción;
no se convierte automáticamente un usuario administrativo en transportador.
`Mis entregas` limita lectura y acceso directo al creador y al asignado. La
revisión administrativa usa explícitamente `review=true` y `dispatches.settle`.

Documentos y verificación se consultan con páginas independientes de diez,
ordenadas y limitadas en SQL antes de materializar sus detalles. Sus totales
corresponden al despacho completo. Verificar permite la cantidad pendiente
completa o una cantidad parcial, incluida fraccionaria; conserva el comando
idempotente existente y actualiza la fila y resumen devueltos por éste, sin GET
posterior del manifiesto. El consecutivo nuevo usa `DocumentSeries` y el
asignador operacional existente, con prefijo DSP; no renumera históricos.

Evidencia local: `DispatchCaptureTests` y los cuatro casos de medios de
`DispatchDeliveryPaymentsTests.Native_delivery_persists_its_form_and_support_once_and_rejects_stale_or_foreign_requests`
pasan diez casos sin omisiones. Sobre 2.000 documentos / 6.000 líneas, candidatos
mantiene dos comandos SQL y p95 de 44 ms. El detalle de 500 documentos / 1.500
líneas devuelve diez más diez con cuatro comandos y 29/15 ms en páginas primera
y segunda (presupuesto: menos de dos segundos). Verificar 20,5 y repetir mantiene
una sola aplicación y dos comandos por intento. Estas cifras no corresponden a
DEV ni sustituyen la verificación del despliegue.

Rollback: conservar series, consecutivos, terceros y vínculos ya usados; no
borrar ni renumerar documentos. Revertir presentación no revierte declaraciones,
verificaciones ni efectos financieros. La tirilla conserva la versión congelada
en el cierre y el renderer v1, además del nuevo v2; ningún cambio reescribe los
cierres históricos. No se crean tablas, motores ni colas para estos ajustes.


La corrección administrativa de una forma de pago requiere adicionalmente
`sales.payments.correct` (Corregir medios de pago al conciliar cierres de caja y
despachos). Las plantillas iniciales y la actualización asignan este permiso solo
a Administrador y Administrativo; Cajero, Supervisor, Contador y Transportador no
lo reciben. El permiso no reemplaza conciliación/liquidación ni permite editar
facturas electrónicas. El servidor lo exige al validar correcciones, confirmar
una conciliación que las contenga (incluida la reclasificación agregada) y corregir
un recaudo del despacho. Las acciones de Facturas y de ambos cierres se ocultan
sin la capacidad. La captura inicial del cobro real por el transportador conserva
su permiso y flujo de entregas; no se convierte en una corrección administrativa.
