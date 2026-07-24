# Decisión: motor servidor de documentos, nuevos IDs y flujo definitivo de Pedidos

## Prevalencia

Este documento reemplaza cualquier decisión anterior que:

- permita procesar documentos de negocio dentro de la caja;
- permita recuperar varios pedidos dentro de una misma factura;
- consolide varios pedidos seleccionados en una sola factura;
- permita consultar pedidos del servidor offline;
- limite Pedidos a un panel interno de Facturación;
- reutilice como identificadores internos de Auraly los IDs primarios de Xion o Pedidos OK.

---

## 1. Motor servidor de documentos

### 1.1. Decisión

El motor de documentos existente en Xion debe migrarse conceptualmente a la arquitectura de Auraly.

Todas las operaciones definitivas se procesan en el servidor:

- pedidos;
- facturas de venta;
- facturas electrónicas;
- entradas de mercancía;
- compras;
- traslados;
- conteos;
- averías;
- devoluciones de venta;
- devoluciones de compra;
- movimientos de caja;
- cuentas por cobrar;
- cuentas por pagar;
- aplicaciones de pagos y abonos.

Las cajas, navegadores y dispositivos capturan intenciones y borradores. No calculan ni escriben directamente los efectos contables, fiscales o de inventario definitivos.

### 1.2. Librerías

```text
Auraly.Domain.DocumentProcessing
Auraly.Application.DocumentProcessing
Auraly.Infrastructure.DocumentProcessing
Auraly.Contracts.DocumentProcessing
```

`DocumentProcessing` coordina, pero no absorbe todas las reglas del negocio. Cada módulo conserva sus procesadores:

```text
Auraly.Application.Sales
  ConfirmSalesInvoiceProcessor

Auraly.Application.Orders
  ConfirmOrderProcessor
  InvoiceOrderProcessor

Auraly.Application.Purchasing
  PostGoodsReceiptProcessor

Auraly.Application.Inventory
  PostTransferProcessor
  PostStockCountProcessor
  PostDamageProcessor

Auraly.Application.Returns
  PostSalesReturnProcessor
  PostPurchaseReturnProcessor
```

El motor descubre procesadores mediante contratos. No debe tener un `switch` gigante con reglas de todos los documentos.

### 1.3. Pipeline común

```text
Recibir comando
      |
      v
Autenticar tenant, negocio, usuario, caja y dispositivo
      |
      v
Validar IdempotencyKey
      |
      v
Cargar parámetros y versión de configuración
      |
      v
Bloquear o verificar concurrencia
      |
      v
Asignar ID interno y consecutivo cuando corresponda
      |
      v
Ejecutar procesador específico
      |
      +--> documento
      +--> inventario
      +--> caja
      +--> cartera
      +--> auditoría
      +--> outbox
      |
      v
Commit único en Azure SQL
      |
      v
Responder resultado
      |
      v
Procesar efectos externos asíncronos
```

### 1.4. Trabajo síncrono y asíncrono

El motor tiene dos formas de ejecución, pero una sola lógica:

#### Síncrona

Se usa cuando el usuario necesita resultado inmediato:

- confirmar una venta en caja;
- recuperar y facturar un pedido puntual;
- registrar una devolución;
- confirmar una entrada o traslado.

La API invoca el procesador de Application en el servidor y devuelve el documento confirmado.

#### Asíncrona

Se usa para:

- facturar varios pedidos seleccionados;
- envío y consulta DIAN;
- sincronizaciones externas;
- reintentos;
- proyecciones y reportes;
- procesos que pueden tardar.

La API registra una solicitud durable y devuelve un `OperationId`. Un Worker o Azure Function ejecuta el mismo motor y publica progreso.

### 1.5. La caja no es el motor

La caja puede:

- calcular una vista previa;
- mantener borradores;
- capturar líneas offline;
- validar formato;
- conservar una cola de envío.

El servidor vuelve a validar y es autoridad sobre:

- totales definitivos;
- impuestos;
- consecutivos;
- inventario;
- negativos;
- pagos;
- cartera;
- estados;
- vínculos entre documentos;
- solicitud fiscal;
- auditoría.

Una venta offline usa un `ClientOperationId`. El `DocumentId` definitivo se asigna cuando el motor servidor la acepta. Los reintentos con el mismo `ClientOperationId` devuelven el mismo resultado.

---

## 2. Identificadores nuevos de Auraly

### 2.1. Productos

Cada producto migrado recibe un nuevo:

```text
ProductId UNIQUEIDENTIFIER
```

El ID primario de Xion, Pedidos OK u otro sistema no se reutiliza como `ProductId`.

Los valores anteriores se conservan como referencias:

```text
ProductExternalIdentifiers
  ProductExternalIdentifierId
  ProductId
  SourceSystem
  SourceBusinessKey
  ExternalProductId
  ExternalSku
  CreatedAtUtc
```

Restricción única:

```text
SourceSystem + SourceBusinessKey + ExternalProductId
```

Los códigos de barras, SKU y referencias tampoco son claves primarias. Son identificadores comerciales que pueden cambiar o tener más de un valor por producto.

### 2.2. Documentos

Cada documento recibe un nuevo ID interno de Auraly:

```text
OrderId
SalesInvoiceId
GoodsReceiptId
TransferId
StockCountId
DamageId
ReturnId
AccountsReceivableDocumentId
AccountsPayableDocumentId
```

Todos se almacenan como `UNIQUEIDENTIFIER`.

La generación utilizará UUID versión 7 o un generador secuencial compatible detrás de:

```text
IAuralyIdGenerator
```

Esto mantiene unicidad distribuida y mejor localidad de índice que un GUID completamente aleatorio.

### 2.3. ID interno no es número de documento

Se separan:

```text
SalesInvoiceId = 019...       // técnico, inmutable
DocumentNumber = FV-000123    // visible para el usuario
DianNumber = SETP99000123     // fiscal, cuando aplique
ExternalDocumentId = 84579    // referencia de Xion o integración
```

El número visible se genera mediante secuencias por:

- tenant;
- empresa;
- negocio o sucursal;
- tipo de documento;
- caja o resolución cuando corresponda;
- serie o prefijo.

La numeración DIAN pertenece al módulo Fiscal y respeta resolución, prefijo y rango.

### 2.4. Mapeo legado

Los documentos importados conservan:

```text
LegacyEntityMappings
  LegacyEntityMappingId
  TenantId
  SourceSystem
  EntityType
  LegacyId
  AuralyId
  ImportedAtUtc
```

Este mapeo permite:

- reanudar importaciones;
- evitar duplicados;
- rastrear documentos;
- responder integraciones antiguas;
- conciliar estadísticas.

No se debe incluir un `LegacyId` diferente en cada tabla si el mapeo central cubre el caso. Los identificadores externos operativos que sigan activos sí tendrán columnas o tablas propias del módulo.

---

## 3. Pedidos como vista propia

### 3.1. Navegación principal

Pedidos conserva una vista propia en Auraly:

```text
/dashboard/orders
/dashboard/orders/{orderId}
```

La vista incluye:

- indicadores;
- búsqueda;
- filtros;
- tabla paginada;
- selección múltiple;
- detalle;
- estado;
- origen;
- cliente;
- vendedor;
- fecha;
- total;
- estado de facturación;
- seguimiento del procesamiento.

Pedidos no queda escondido dentro del POS.

### 3.2. Consulta siempre online

La lista, búsqueda, detalle y cantidades pendientes de pedidos se consultan siempre en línea.

La caja no descarga:

- todos los pedidos;
- estados de pedidos;
- cantidades facturadas;
- claims;
- resultados de procesos masivos.

Si no existe conexión:

- la vista muestra que Pedidos requiere conexión;
- no utiliza una lista posiblemente desactualizada;
- no permite recuperar un pedido;
- Facturación puede seguir con ventas libres según su política offline.

---

## 4. Dos modos de abrir Pedidos

La misma vista y componentes se reutilizan en dos contextos, con capacidades diferentes.

### 4.1. Modo administración

Se abre desde el menú principal:

```text
Pedidos
```

Permite:

- consultar;
- filtrar;
- seleccionar uno o varios;
- abrir detalle;
- ejecutar **Facturar seleccionados**;
- consultar progreso y resultados.

La selección es múltiple.

### 4.2. Modo recuperación desde Facturación

Se abre desde:

```text
Facturación -> Recuperar pedido
```

Puede implementarse como una ruta con contexto:

```text
/dashboard/orders?mode=recover&cashRegisterId=...&invoiceDraftId=...
```

o como un panel de página completa dentro del layout de Facturación.

Reglas:

- solo permite seleccionar un pedido;
- el control visual es de selección única;
- la acción se llama **Recuperar pedido**;
- carga únicamente cantidades pendientes;
- vuelve a la factura temporal activa;
- no permite selección múltiple;
- no ejecuta la facturación automáticamente;
- el cajero revisa líneas y continúa el flujo normal de pago.

Si ya hay una factura temporal con líneas, el sistema solicita guardarla o reemplazarla. La combinación automática no pertenece al MVP.

---

## 5. Recuperar un solo pedido

Flujo:

```text
Facturación
    |
    v
Recuperar pedido
    |
    v
Vista online de Pedidos en modo selección única
    |
    v
Seleccionar pedido
    |
    v
Servidor valida estado y cantidades pendientes
    |
    v
Crear claim temporal
    |
    v
Cargar en factura temporal
    |
    v
Revisar, pagar y confirmar
    |
    v
Motor servidor procesa factura
```

El pedido queda temporalmente reclamado por:

- caja;
- dispositivo;
- usuario;
- factura temporal;
- vencimiento.

Otra caja puede verlo, pero no recuperarlo mientras el claim esté vigente.

Al cancelar o vencer la factura temporal se libera el claim.

---

## 6. Facturar uno o varios pedidos seleccionados

La facturación múltiple se ejecuta exclusivamente mediante el botón:

```text
Facturar
```

ubicado en la vista propia de Pedidos.

### 6.1. Comportamiento

1. El usuario selecciona uno o varios pedidos.
2. Pulsa **Facturar**.
3. El servidor valida cada pedido.
4. Se muestra un resumen de los documentos que se crearán.
5. El usuario confirma la operación.
6. Se registra una operación masiva.
7. El motor procesa cada pedido.
8. La vista muestra progreso por pedido.

### 6.2. Una factura por pedido

La decisión inicial es:

> Cada pedido seleccionado genera su propia factura.

No se consolidan varios pedidos en una sola factura, aunque pertenezcan al mismo cliente.

Esto conserva:

- trazabilidad simple;
- totales originales;
- condiciones del pedido;
- vendedor y ruta;
- estado independiente;
- reintentos independientes;
- numeración clara;
- errores aislados.

Si posteriormente el negocio requiere consolidación, se diseñará como una función distinta y explícita.

### 6.3. Procesamiento independiente

Un lote puede terminar:

```text
Pedido P-101 -> Factura FV-501 -> Correcto
Pedido P-102 -> Sin inventario -> Rechazado
Pedido P-103 -> Factura FV-502 -> Correcto
Pedido P-104 -> Ya facturado -> Omitido
```

Un error en un pedido no revierte las facturas correctas de otros.

Cada elemento tiene:

- idempotency key;
- estado;
- intento;
- error;
- factura resultante;
- usuario;
- fechas;
- enlace al detalle.

### 6.4. Medios y condición de pago

Antes de enviar el lote, cada pedido debe tener información suficiente para facturarse:

- condición de pago;
- contado o crédito;
- medio de pago cuando corresponda;
- cliente válido;
- cupo disponible si aplica;
- datos fiscales requeridos.

Los pedidos incompletos se marcan y no entran al procesamiento hasta corregirse.

---

## 7. Motor al facturar pedidos

Por cada pedido, el motor:

1. valida que tenga saldo pendiente;
2. adquiere bloqueo o verifica `rowversion`;
3. valida cliente y condición de pago;
4. valida precios, descuentos e impuestos;
5. valida inventario según la política de caja o negocio;
6. asigna nuevo `SalesInvoiceId`;
7. asigna número de factura;
8. crea factura y líneas;
9. crea vínculos pedido-factura;
10. genera movimientos de inventario;
11. registra caja o cuenta por cobrar;
12. actualiza el pedido;
13. registra auditoría;
14. agrega solicitud fiscal al outbox;
15. confirma la transacción;
16. procesa DIAN de forma asíncrona.

El frontend no replica esta lógica.

---

## 8. Tablas del motor

Todas se agregan en `Auraly.Database.sqlproj`, esquema `dbo`.

```text
DocumentProcessingOperations
  OperationId
  TenantId
  OperationType
  Status
  RequestedByUserId
  RequestedAtUtc
  StartedAtUtc
  CompletedAtUtc
  TotalItems
  SuccessfulItems
  FailedItems
  IdempotencyKey

DocumentProcessingItems
  OperationItemId
  OperationId
  SourceDocumentType
  SourceDocumentId
  ResultDocumentType
  ResultDocumentId
  Status
  AttemptCount
  ErrorCode
  ErrorMessage
  StartedAtUtc
  CompletedAtUtc

DocumentNumberSequences
  DocumentNumberSequenceId
  TenantId
  BusinessId
  DocumentType
  Prefix
  CurrentNumber
  RangeFrom
  RangeTo
  RowVersion

LegacyEntityMappings
ProductExternalIdentifiers
OrderInvoicingClaims
OrderInvoiceLinks
OrderItemInvoiceLinks
```

El proyecto SQL es la única fuente de verdad. No se generan migraciones EF.

---

## 9. Contratos principales

```text
Auraly.Contracts.DocumentProcessing
  ProcessDocumentCommand
  ProcessDocumentResult
  StartBatchDocumentProcessingCommand
  BatchOperationStatus
  BatchOperationItemStatus

Auraly.Contracts.Orders
  SearchOnlineOrdersQuery
  GetRecoverableOrderQuery
  ClaimOrderForInvoiceCommand
  ReleaseOrderClaimCommand
  StartInvoiceSelectedOrdersCommand

Auraly.Contracts.Sales
  ConfirmInvoiceCommand
  ConfirmInvoiceResult
```

Los contratos incluyen `TenantId`, `BusinessId`, `UserId`, `CorrelationId`, `IdempotencyKey` y versión.

---

## 10. Interfaz web

### Vista propia

```text
+-------------------------------------------------------------------+
| Pedidos                                      [Facturar]            |
+-------------------------------------------------------------------+
| Buscar | Fechas | Estado | Cliente | Vendedor | Ruta | Bodega     |
+-------------------------------------------------------------------+
| [x] P-101 | Cliente A | $120.000 | Confirmado | Pendiente         |
| [x] P-102 | Cliente B | $340.000 | Confirmado | Pendiente         |
| [ ] P-103 | Cliente C | $ 80.000 | Facturado  | FV-499            |
+-------------------------------------------------------------------+
| 2 seleccionados | Total $460.000                                  |
+-------------------------------------------------------------------+
```

El botón **Facturar** abre resumen, confirma y muestra progreso.

### Desde Facturación

```text
+-------------------------------------------------------------------+
| Recuperar pedido                                      [Volver]     |
+-------------------------------------------------------------------+
| Buscar número o cliente                                           |
+-------------------------------------------------------------------+
| ( ) P-101 | Cliente A | $120.000 | Pendiente                      |
| ( ) P-102 | Cliente B | $340.000 | Pendiente                      |
+-------------------------------------------------------------------+
|                                      [Recuperar pedido]            |
+-------------------------------------------------------------------+
```

Solo existe una selección activa.

---

## 11. Criterios de aceptación

- El motor servidor procesa todos los documentos definitivos.
- La caja nunca escribe directamente efectos de inventario, caja o cartera.
- Cada producto migrado recibe un nuevo `ProductId`.
- Cada documento migrado o nuevo usa un ID interno de Auraly.
- Los IDs anteriores se conservan como mapeos externos.
- ID interno, consecutivo visible y número DIAN son conceptos distintos.
- Pedidos conserva su vista propia.
- Pedidos se consulta siempre en línea.
- Desde Facturación solo se recupera un pedido.
- Recuperar no factura automáticamente.
- La selección múltiple solo está disponible en la vista propia.
- El botón **Facturar** procesa los pedidos seleccionados.
- Cada pedido seleccionado genera una factura independiente.
- Un error no revierte los demás pedidos del lote.
- El progreso puede consultarse por pedido.
- Todo cambio de base pertenece a `Auraly.Database.sqlproj`.
