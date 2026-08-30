# Decisión de arquitectura: identificadores y numeración local de Auraly

**Estado:** aprobado para la implementación del Commerce MVP  
**Alcance:** productos, documentos, cajas offline, cloud/on-premise, integraciones y migración desde Xion  
**Prevalencia:** reemplaza recomendaciones ambiguas sobre `IDENTITY`, `NEWID()`, `NEWSEQUENTIALID()`, IDs legados y asignación central del número de factura.

---

## 1. Decisión ejecutiva

Auraly no continuará la numeración técnica de Xion ni hará que `ProductId`
empiece en 10000.

La política será:

```text
Identidad técnica interna        UUID versión 7
Tipo .NET                        Guid
Tipo SQL Server                  UNIQUEIDENTIFIER
Generación                       aplicación mediante IAuralyIdGenerator
Código visible de producto       ProductCode
Número visible de factura        asignado localmente por la caja
Unicidad del número              serie o bloque exclusivo preasignado
Referencia del sistema anterior  ExternalIdentifier / LegacyEntityMapping
Control de reintentos             ClientOperationId / IdempotencyKey
Concurrencia                      ROWVERSION
```

Ejemplo:

```text
ProductId = 019...                // técnico, global e inmutable
ProductCode = 10000               // visible y propio del negocio
Sku = REF-ABC-20                  // referencia comercial
Barcode = 7701234567890           // código de lectura
LegacyId = 10000                  // ID anterior de Xion

SalesInvoiceId = 019...           // creado por la caja
DocumentNumber = FV-C03-000184    // creado por la caja
ClientOperationId = 019...        // hace idempotente la subida
```

Estos valores no se sustituyen entre sí.

---

## 2. Qué existe hoy

La tabla actual `Products` de Auraly ya declara:

```sql
[ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID()
```

El `ProductId` actual de Auraly no es el entero que empieza en 10000. Ese número
corresponde al modelo legado o a un código visible.

Sin embargo, el repositorio mezcla:

- `NEWID()` en la mayoría de las tablas;
- `NEWSEQUENTIALID()` en algunas;
- `Guid.NewGuid()` en código;
- IDs fijos en ciertos datos semilla;
- identificadores externos de integraciones.

La mezcla funciona, pero no es una política estable para cajas offline, varias
instancias del motor, cloud/on-premise, microservicios ni importaciones
repetibles.

El cambio real es normalizar la generación, dejar de depender de `NEWID()` para
entidades nuevas y separar identidad técnica, numeración comercial y referencia
legada.

---

## 3. Alternativas evaluadas

### `INT` o `BIGINT IDENTITY`

Son compactos y rápidos, pero una caja offline no puede generar un valor global
sin reservar rangos. Cloud y on-premise pueden producir el mismo número y una
extracción a microservicios exige coordinación.

No se adoptan como identidad canónica.

### Snowflake

Es distribuido y compacto, pero exige asignar `NodeId`, controlar relojes,
clonación de instalaciones y colisiones. Es complejidad prematura para el MVP.

### GUID versión 4

Es global y funciona offline, pero es aleatorio y pierde orden temporal.
`NEWID()` y `Guid.NewGuid()` producen este comportamiento.

### `NEWSEQUENTIALID()`

Mejora inserciones en SQL Server, pero solo se genera en la base. No unifica
caja, API, motor, importador y despliegues futuros.

### ULID

Es válido, pero SQL Server no tiene tipo ULID nativo y Auraly ya usa `Guid` de
extremo a extremo. No aporta suficiente valor para cambiar todo el stack.

### UUID versión 7

Permite generación distribuida y offline, contiene tiempo para orden aproximado,
es estándar y cabe en el tipo actual `Guid`/`UNIQUEIDENTIFIER`.

Es la opción elegida.

---

## 4. Advertencia de índices en SQL Server

UUIDv7 es ordenable por su representación canónica, pero SQL Server no compara
todos los bytes de `UNIQUEIDENTIFIER` en el mismo orden visual.

No se asumirá sin medición:

```text
UUIDv7 estándar = NEWSEQUENTIALID() en localidad física
```

Para el MVP:

- se almacena como `UNIQUEIDENTIFIER`;
- no se inventa un UUID con bytes reordenados;
- las tablas grandes definen índices según consultas reales;
- se miden fragmentación, páginas divididas y latencia;
- no se agrega un segundo `BIGINT IDENTITY` a todas las tablas “por si acaso”.

La identidad distribuida y el diseño físico de índices son decisiones separadas.

---

## 5. Generación técnica

```csharp
public interface IAuralyIdGenerator
{
    Guid NewId();
}
```

Toda entidad Commerce se crea con ese contrato. En código nuevo no se usa
directamente `Guid.NewGuid()`, `NEWID()`, `NEWSEQUENTIALID()` o `IDENTITY`, salvo
una excepción documentada.

La solución actual usa .NET 8. Por ello:

- si el runtime final ofrece UUIDv7 nativo, se usa su API;
- mientras continúe en .NET 8, se usa una implementación RFC 9562 mantenida y
  auditada detrás de la interfaz;
- no se copia un algoritmo casual;
- el generador se prueba bajo concurrencia y retrocesos de reloj.

UUIDv7 no reemplaza `CreatedAtUtc`, `OccurredAtUtc`, `BusinessDate` ni fechas
fiscales. El tiempo embebido nunca es la prueba contable del evento.

---

## 6. Política por tipo de dato

Usan UUIDv7 los agregados y entidades con identidad:

```text
TenantId
BusinessId
WarehouseId
CashRegisterId
ProductId
CustomerId
SupplierId
OrderId
SalesInvoiceId
GoodsReceiptId
InventoryMovementId
DispatchId
PaymentId
SalesInvoiceLineId
GoodsReceiptLineId
```

Una tabla puente pura puede usar clave compuesta. Si requiere auditoría, estado,
sincronización o referencia externa, recibe UUIDv7 propio.

Los estados cerrados usan enum o código estable. Los catálogos administrables
usan UUIDv7 y un código estable.

Los IDs semilla deben ser explícitos, deterministas entre ambientes y declarados
en el proyecto SQL. No se genera un ID nuevo en cada despliegue para la misma
ciudad, permiso o tipo base.

---

## 7. Producto: identidad, código y búsqueda

```text
Product
  ProductId             UUIDv7, interno
  BusinessId
  ProductCode           código visible del negocio
  Name
  Sku?
```

```text
ProductBarcodes
  ProductBarcodeId      UUIDv7
  ProductId
  Barcode
  ProductUnitId?
  ConversionFactor
  IsPrimary
```

```text
ProductExternalIdentifiers
  ProductExternalIdentifierId UUIDv7
  ProductId
  SourceSystem
  SourceBusinessKey
  ExternalProductId
  ExternalSku?
```

El negocio puede escribir, importar o autogenerar `ProductCode`, comenzando en
1, 10000 o el valor que configure.

Restricción:

```text
UNIQUE (BusinessId, ProductCode)
```

`ProductCode` puede cambiar con auditoría; no es FK. La búsqueda acepta barcode,
código, SKU, referencia del proveedor, nombre, alias e identificador externo.
La interfaz no muestra el UUID al cajero.

---

## 8. Facturación local: el número nace en la caja

### 8.1. Cambio frente al diseño anterior

Al confirmar una venta, la caja genera y conserva:

```text
SalesInvoiceId
ClientOperationId
DocumentNumber
IssuedAtLocal
CashRegisterId
NumberAllocationId
```

El servidor no reemplaza estos valores al sincronizar. Los valida, registra y
procesa de forma idempotente.

El motor servidor sigue siendo autoridad sobre:

- validación definitiva;
- efectos de inventario, caja y cartera;
- impuestos y totales;
- estado del documento;
- envío fiscal;
- auditoría y reportes consolidados.

Que el número nazca localmente no autoriza a la caja a escribir directamente en
las tablas centrales.

### 8.2. Por qué no se usa `MAX + 1`

Dos cajas desconectadas pueden leer el mismo máximo y emitir el mismo siguiente
número. Tampoco basta con:

```text
último número guardado localmente + 1
```

si varias cajas comparten la misma serie.

La unicidad offline requiere propiedad exclusiva previa sobre los números.

### 8.3. Estrategia adoptada

> **Decisión desplazada (27 de julio de 2026):** el diseño de bloques descrito
> en las secciones 8.3 a 8.7 se conserva solo como contexto histórico y no debe
> implementarse. La regla prevalente está en
> `decision-series-prefijos-numeracion-fiscal-offline.md`: una resolución DIAN
> completa se asigna de manera exclusiva a un dispositivo enrolado, sin pool,
> prefetch, standby ni reparto de rangos.

El servidor entrega anticipadamente a cada caja un bloque no superpuesto:

```text
InvoiceNumberAllocation
  InvoiceNumberAllocationId
  TenantId
  BusinessId
  BranchId
  CashRegisterId
  FiscalResolutionId?
  DocumentType
  Prefix
  RangeFrom
  RangeTo
  NextNumber
  AllocatedAtUtc
  ExpiresAtUtc?
  Status
  RowVersion
```

Ejemplo:

```text
Caja 1 -> prefijo FV, números 1..500
Caja 2 -> prefijo FV, números 501..1000
Caja 3 -> prefijo C03, números 1..500
```

Las dos modalidades válidas son:

1. **Serie exclusiva por caja:** cada caja tiene un prefijo distinto y su propia
   secuencia.
2. **Bloques exclusivos de una serie compartida:** cada caja recibe intervalos
   que nunca se solapan.

La configuración fiscal define cuál es permitida. Para documentos no fiscales se
prefiere una serie exclusiva por caja porque es más simple de operar.

### 8.4. Consumo local transaccional

La caja almacena el bloque en su base local. Al confirmar:

1. inicia transacción local;
2. bloquea el registro de asignación;
3. verifica que exista número disponible;
4. toma `NextNumber`;
5. incrementa `NextNumber`;
6. crea la factura, sus líneas y la operación de sincronización;
7. confirma todo en una sola transacción.

Un fallo no puede consumir el número sin guardar al menos el registro del intento.
Un documento anulado conserva su número y estado; el número no se recicla.

### 8.5. Unicidad central

Restricciones recomendadas:

```text
UNIQUE (
  TenantId,
  BusinessId,
  FiscalResolutionId,
  Prefix,
  DocumentNumberValue
)

UNIQUE (
  BusinessId,
  CashRegisterId,
  ClientOperationId
)

UNIQUE (SalesInvoiceId)
```

Si no hay resolución fiscal, se usa un valor normalizado de serie/documento para
que la restricción no dependa de un `NULL`.

El número formateado se presenta como:

```text
Prefix + NumberValue con relleno configurado
```

La unicidad se aplica a los componentes, no solamente al texto renderizado.

### 8.6. Prefetch y agotamiento

Cuando una caja está online y su bloque baja del umbral configurado, solicita el
siguiente bloque en segundo plano.

```text
AllocationSize = configurable
ReplenishmentThreshold = configurable
```

La primera sincronización del POS incluye la asignación activa.

Si la caja queda offline y agota el rango:

- no inventa otro prefijo;
- no usa números negativos;
- no reinicia en 1;
- no toma el rango de otra caja;
- bloquea la emisión de ese tipo de factura;
- permite únicamente el flujo de contingencia que esté configurado y autorizado.

El tamaño del bloque debe cubrir el pico razonable de ventas durante una caída,
sin reservar innecesariamente todo el rango disponible.

### 8.7. Cajas reemplazadas o reinstaladas

Una asignación pertenece al `CashRegisterId`, no al nombre del computador.

Al reinstalar:

- la caja se vuelve a registrar;
- recupera del servidor asignaciones y último estado sincronizado;
- un bloque dudoso no se reasigna automáticamente;
- los números no utilizados pueden quedar anulados o liberarse solo mediante un
  procedimiento auditado compatible con la política fiscal;
- clonar la base local no crea una caja válida nueva.

### 8.8. Facturación electrónica

Se separan:

```text
SalesInvoiceId        identidad Auraly
DocumentNumber        número emitido por la caja
FiscalResolutionId   resolución/rango que autoriza el número
Cufe                  identificador fiscal calculado
DianStatus            estado de envío/validación
```

La caja puede registrar una venta sin red y reservar su número, pero eso no
equivale por sí solo a una factura electrónica validada por DIAN.

El módulo fiscal debe manejar explícitamente:

- emisión normal;
- envío pendiente;
- reintento idempotente;
- contingencia autorizada;
- rechazo;
- nota crédito/débito;
- agotamiento o vencimiento de resolución.

No se documenta una venta offline como “aceptada por DIAN” hasta recibir la
respuesta correspondiente.

---

## 9. Sincronización e idempotencia

Flujo:

1. La caja crea `LocalDraftId` mientras la venta es temporal.
2. Al confirmar crea una sola vez `SalesInvoiceId` y `ClientOperationId`.
3. Consume un número de su asignación.
4. Persiste factura y mensaje de salida en la misma transacción local.
5. Reintenta siempre con los mismos IDs y número.
6. El servidor registra la solicitud de forma idempotente.
7. Si ya fue procesada, devuelve el mismo resultado.
8. Si es nueva, verifica caja, asignación, rango, número y firma del payload.
9. El motor procesa el documento conservando su identidad.

El UUID evita colisiones técnicas; el bloque evita colisiones de numeración
visible. Se necesitan ambos.

---

## 10. Cloud, on-premise y microservicios

Cada instalación tiene:

```text
InstallationId UUIDv7
```

Los eventos incluyen:

```text
EventId
InstallationId
TenantId
EntityId
OccurredAtUtc
CorrelationId
```

No se incrusta tenant, región, caja o servidor dentro de los bits del ID. Esa
información se conserva en columnas explícitas.

UUIDv7 evita rangos técnicos de enteros y permite mover agregados a otro servicio
sin cambiar su identidad. Los rangos de facturación siguen siendo configuración
de negocio/fiscal, no generadores de PK.

---

## 11. Migración desde Xion

```text
Xion.ProductoId = 10000
            |
            +--> Auraly.ProductId = nuevo UUIDv7
            +--> Auraly.ProductCode = código útil, si corresponde
            +--> LegacyEntityMappings.LegacyId = "10000"
```

Restricción:

```text
UNIQUE (
  TenantId,
  SourceSystem,
  SourceBusinessKey,
  EntityType,
  LegacyId
)
```

La importación:

- nunca reutiliza el entero como UUID;
- no supone que el ID es global entre empresas;
- reusa el mismo `AuralyId` al reanudarse;
- resuelve hijos mediante el mapa;
- reporta huérfanos y duplicados.

Para facturas históricas se conservan número, prefijo, resolución y caja de
origen. No se les asigna un número nuevo.

---

## 12. Proyecto de base de datos y adopción

El proyecto SQL continúa siendo la única fuente de verdad.

En tablas nuevas:

```sql
[ProductId] UNIQUEIDENTIFIER NOT NULL
```

La aplicación envía el ID. Un `DEFAULT` puede mantenerse temporalmente como
defensa para escritores heredados, pero debe documentarse y retirarse; el código
nuevo no depende de él.

Adopción:

1. módulos Commerce nuevos nacen con UUIDv7;
2. Producto se normaliza al implementar su nuevo módulo;
3. Facturación local crea su ID y número en la caja;
4. módulos existentes migran cuando se toquen;
5. IDs históricos no se reescriben solamente por ser UUIDv4.

---

## 13. Seguridad

UUID no es autorización. Toda consulta valida:

```text
TenantId
BusinessId
permiso
alcance del usuario
```

Para enlaces públicos sensibles se usan tokens aleatorios con expiración y
propósito, no el ID de la entidad.

La asignación de números se firma o protege contra alteración local. El servidor
rechaza una factura si:

- el rango no pertenece a esa caja;
- está vencido, revocado o agotado;
- el número ya existe con otro documento;
- el payload cambió para el mismo `ClientOperationId`;
- la resolución no corresponde.

El rechazo no borra la evidencia local; genera una excepción operativa auditable.

---

## 14. Pruebas obligatorias

### Generador

- versión 7 y variante RFC correctas;
- nunca `Guid.Empty`;
- sin duplicados bajo concurrencia;
- múltiples IDs por milisegundo;
- sustitución por generador determinista en tests;
- manejo definido de retroceso de reloj.

### Producto

- `ProductId` no depende de `ProductCode`;
- dos negocios pueden usar el mismo código;
- un negocio no lo duplica según su política;
- cambiar código, SKU o barcode no cambia `ProductId`;
- varios barcodes apuntan al mismo producto.

### Facturación local

- dos cajas nunca reciben bloques solapados;
- 100 hilos en una caja no repiten número;
- reiniciar la caja conserva `NextNumber`;
- caída entre confirmación y sincronización no duplica;
- reintento conserva ID, operación y número;
- anular no libera ni recicla;
- agotar rango bloquea emisión normal;
- prefetch no entrega dos veces el mismo bloque;
- caja clonada o revocada no consume un rango;
- conflicto central queda en excepción auditable;
- el servidor conserva el `SalesInvoiceId` local.

### Migración

- reejecutar conserva mapeos;
- IDs iguales de empresas diferentes no colisionan;
- facturas históricas conservan su numeración;
- huérfanos no se enlazan por aproximación.

### Rendimiento

- carga representativa en SQL Server;
- fragmentación y páginas divididas;
- tiempo de inserción y búsqueda;
- sincronización de lotes;
- recuperación después de desconexión prolongada.

---

## 15. Reglas automatizables

CI debe detectar en código Commerce nuevo:

```text
Guid.NewGuid()
DEFAULT NEWID()
DEFAULT NEWSEQUENTIALID()
IDENTITY(
MAX(DocumentNumber)
int ProductId
long SalesInvoiceId
```

Se excluyen de forma explícita migraciones, importadores y fixtures de
compatibilidad.

También se comprueba que contratos diferencien:

```text
Id
Code
Number
ExternalId
ClientOperationId
```

---

## 16. Criterios de aceptación

- `ProductId` no representa el consecutivo 10000 de Xion.
- Productos nuevos usan UUIDv7 generado por la aplicación.
- El 10000 puede conservarse como `ProductCode` o `LegacyId`.
- Todos los agregados Commerce usan la misma estrategia técnica.
- Una factura confirmada localmente conserva su `SalesInvoiceId`.
- La caja genera el número usando una asignación exclusiva.
- Ninguna caja calcula el número con `MAX + 1`.
- Bloques o series de cajas distintas no se solapan.
- Número visible, UUID y CUFE son conceptos distintos.
- Reintentos offline son idempotentes.
- Agotamiento de rango no inventa numeración.
- SQL Server conserva `UNIQUEIDENTIFIER`.
- La estrategia de índices se prueba y no se presume.
- El proyecto SQL sigue siendo la fuente de verdad.

---

## 17. Conclusión

La mejor identidad técnica para Auraly es UUIDv7, generado por la aplicación y
encapsulado por `IAuralyIdGenerator`.

La mejor numeración offline no es un GUID visible: es una serie exclusiva o un
bloque de números preasignado a la caja. Así cada caja puede facturar sin red,
mantener un consecutivo comprensible y garantizar que otra caja no emita el mismo
número.

Esta combinación resuelve productos, facturas locales, sincronización,
cloud/on-premise, migración desde Xion y futura separación por servicios sin
mezclar PK técnicas con códigos comerciales o fiscales.
