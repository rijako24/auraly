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
GoodsReceipt
GoodsReceiptLines
InventoryTransactions
InventoryTransactionLines
InventoryBalances
CostLedger
AccountsPayable, si aplica
SupplierCostHistory
Audit
Outbox
```

Si falla un efecto obligatorio, no se confirma parcialmente.

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
