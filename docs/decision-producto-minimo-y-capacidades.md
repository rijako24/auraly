# Decisión: Producto mínimo y capacidades separadas

**Estado:** aprobado para el Commerce MVP  
**Objetivo:** evitar trasladar a Auraly la tabla ancha de productos de Xion y evitar que `Products` vuelva a concentrar precio, costo, inventario, proveedor, bodega y sincronización.

---

## 1. Decisión

`Products` contendrá únicamente la identidad comercial estable y las
clasificaciones básicas del producto.

Todo dato que:

- pueda tener varios valores;
- cambie por bodega, proveedor, canal, fecha o empresa;
- tenga historial propio;
- solo aplique a algunos productos;
- pertenezca a otro módulo;

se almacena en una entidad separada.

No se migrará una columna solo porque exista en Xion. Cada campo debe demostrar:

1. un flujo del MVP que lo escribe;
2. un flujo del MVP que lo lee;
3. una regla o reporte que lo necesita;
4. un dueño de dominio;
5. una prueba.

---

## 2. Núcleo de `Products`

Modelo propuesto:

```text
Product
  ProductId                 UUIDv7
  BusinessId
  ProductCode               código visible
  Name
  Description?              descripción corta
  ProductCategoryId?
  BrandId?
  BaseUnitId
  TaxProfileId
  ManageInventory
  IsWeighable
  IsActive
  CreatedAtUtc
  CreatedByUserId
  UpdatedAtUtc?
  UpdatedByUserId?
  RowVersion
```

### Obligatorios

```text
ProductId
BusinessId
ProductCode
Name
BaseUnitId
TaxProfileId
ManageInventory
IsWeighable
IsActive
auditoría
RowVersion
```

### Opcionales reales

`Description`, categoría y marca pueden quedar vacíos. No bloquean la creación
rápida ni la venta.

La interfaz no presenta los campos técnicos de auditoría.

---

## 3. Lo que no pertenece a `Products`

| Dato | Dueño correcto |
|---|---|
| códigos de barras | `ProductBarcodes` |
| SKU o referencias alternativas | `ProductIdentifiers` |
| alias de búsqueda | `ProductAliases` |
| unidad de venta y factor | `ProductUnits` |
| código y patrón de balanza | `ProductScaleConfigurations` |
| precio | `ProductPrices` / `Pricing` |
| canal de precio | `PriceChannels` |
| promoción | `Promotions` |
| proveedor principal o alternos | `SupplierProducts` |
| código del proveedor | `SupplierProducts` |
| costo negociado | `SupplierCostAgreements` |
| último costo | proyección de `GoodsReceipts` |
| costo promedio | `InventoryValuations` |
| cantidad disponible | `InventoryBalances` |
| configuración por bodega | `ProductWarehouseSettings` |
| política de negativos | `WarehouseSettings` |
| imágenes | `ProductMedia` |
| impuestos detallados | `TaxProfiles` |
| IDs de Xion/integraciones | `ProductExternalIdentifiers` |
| estado de sincronización POS | change feed / `CatalogRevisions` |

En particular, la nueva tabla no tendrá:

```text
UnitPrice
UnitCost
StockQuantity
WarehouseId
SupplierId
Barcode
ExternalProductId
RawPayloadJson
LastSyncedAt
```

como columnas directas del agregado.

---

## 4. Capacidades opcionales

### Códigos

```text
ProductBarcode
  ProductBarcodeId
  ProductId
  Barcode
  ProductUnitId?
  IsPrimary
  IsActive
```

Un producto puede tener varios códigos. El barcode no es PK y cambiarlo no
cambia `ProductId`.

### Identificadores comerciales

```text
ProductIdentifier
  ProductIdentifierId
  ProductId
  IdentifierType          Sku | Reference | ManufacturerCode
  Value
  IsPrimary
  IsActive
```

Así no se agregan nuevas columnas cada vez que aparece otra clase de referencia.

### Balanza

Solo existe para productos pesables:

```text
ProductScaleConfiguration
  ProductId
  ScaleCode
  BarcodePatternId
  EmbeddedValueType      Weight | Price
  IsActive
```

`IsWeighable = false` implica que no existe configuración de balanza activa.

### Bodega

```text
ProductWarehouseSetting
  ProductId
  WarehouseId
  IsAvailableForSale
  IsAvailableForPurchase
  ReorderPoint?
  ReorderQuantity?
  PickingLocation?
```

No guarda el saldo. El saldo se deriva del libro de inventario y su proyección.

La política de vender negativos pertenece a la bodega y la heredan todas sus
cajas; no se duplica por producto ni por caja.

### Unidades y conversiones

La unidad base vive como referencia en Producto. Las presentaciones y
conversiones viven en `ProductUnits` y en el módulo de Conversión ya definido.

Lotes y seriales siguen fuera del MVP. Por tanto no se crean campos vacíos para
ellos.

---

## 5. Formulario web

La creación no será una página interminable. Usa divulgación progresiva:

```text
Datos básicos             obligatorio
Códigos y referencias     visible y ágil
Impuestos                 obligatorio, con valor sugerido
Balanza                   solo si Es pesable
Bodegas                   pestaña
Precios por canal         pestaña
Proveedores y costos      pestaña
Historial                 pestaña de consulta
```

### Creación rápida

Permite guardar con:

```text
Código
Nombre
Unidad base
Perfil tributario
Maneja inventario
Activo
```

Después se agregan barcode, precio, bodega y proveedor sin reabrir un formulario
monolítico.

### Creación completa

Un asistente opcional guía:

1. datos básicos;
2. barcode/identificadores;
3. precio inicial por canal;
4. disponibilidad en bodegas;
5. proveedor y costo;
6. confirmación.

Cada paso guarda un borrador explícito. No se crean parcialmente registros
activos invisibles.

---

## 6. Listado

La tabla principal muestra solo:

```text
Código
Nombre
Categoría
Marca
Unidad
Precio principal
Bodegas habilitadas
Estado
Actualizado
```

Barcode, proveedor, costo, canales e inventario son columnas opcionales o vistas
relacionadas. No se intenta mostrar todo simultáneamente.

Todos los encabezados filtrables combinan condiciones y la consulta es paginada.
Los filtros pertenecen a la consulta del servidor, no a la página cargada en el
navegador.

---

## 7. Proyección local del POS

La caja no descarga la tabla administrativa completa. Recibe una proyección:

```text
LocalProduct
  ProductId
  ProductCode
  Name
  BaseUnitId
  TaxProfileSnapshot
  ManageInventory
  IsWeighable
  IsActive
  CatalogRevision
```

Y colecciones separadas:

```text
LocalProductBarcodes
LocalProductIdentifiers
LocalProductUnits
LocalProductAliases
LocalProductScaleConfigurations
LocalProductPrices
LocalPromotions
```

La caja no descarga:

- saldos de inventario;
- costos;
- proveedores;
- notas administrativas;
- auditoría;
- imágenes originales;
- campos que no participan en buscar, valorar o vender.

Las imágenes miniatura son opcionales y nunca bloquean la primera
sincronización.

---

## 8. Migración desde Xion

Se construye una matriz por campo:

```text
Campo Xion
Uso comprobado
Módulo dueño
Entidad destino
Transformación
Obligatorio
Regla de descarte
```

Clasificación:

```text
Migrar       el MVP lo usa y su significado es confiable
Transformar  el dato sirve, pero cambia de modelo
Archivar     se conserva fuera del modelo operativo
Descartar    no tiene uso ni calidad suficiente
Revisar      requiere decisión por empresa
```

No se copia la estructura de la tabla. Se migran datos útiles hacia el modelo
canónico.

Campos desconocidos o sin uso no se depositan en un JSON genérico del producto.
Si se necesita trazabilidad de la importación, el importador conserva el archivo
fuente y su reporte, no ensucia el agregado.

---

## 9. Impacto sobre la tabla actual

La tabla actual de Auraly tiene campos que deben salir del nuevo agregado:

```text
IntegrationConnectionId
ExternalProductId
Source
UnitPrice
Currency
StockQuantity
RawPayloadJson
LastSyncedAt
```

La implementación debe:

1. inventariar qué registros y escritores los usan hoy;
2. crear entidades destino;
3. migrar datos sin pérdida;
4. cambiar lecturas y escrituras;
5. mantener una vista de compatibilidad solo si es imprescindible;
6. retirar columnas después de verificar consumidores.

No se eliminan columnas directamente en el primer despliegue.

---

## 10. Pruebas

- creación rápida con los campos mínimos;
- validación de código único por negocio;
- producto con varios barcodes;
- producto pesable y no pesable;
- cambio de barcode sin cambio de identidad;
- precio distinto por canal;
- configuración distinta por bodega;
- política de negativos heredada de la bodega;
- producto con varios proveedores;
- búsqueda por código, barcode, SKU, referencia y alias;
- proyección POS sin costo ni saldo;
- delta de un solo producto;
- migración idempotente;
- ningún campo descartado tiene un consumidor activo;
- filtros combinables y paginación real.

---

## 11. Criterios de aceptación

- `Products` no funciona como tabla universal.
- Precio, costo y saldo no son columnas del producto.
- Barcode y referencias admiten múltiples valores.
- La balanza aparece desde el MVP, pero solo para productos aplicables.
- Proveedores/costos y bodegas tienen pestañas propias.
- La caja descarga una proyección mínima.
- Lotes y seriales no agregan campos vacíos.
- Todo campo legado tiene decisión explícita de migrar, transformar, archivar o
  descartar.
- La pantalla de creación permite un camino rápido y otro completo.
- El proyecto SQL sigue siendo la fuente de verdad.

---

## 12. Conclusión

El valor de Xion está en sus reglas y datos útiles, no en el ancho de su tabla
Producto.

Auraly debe construir un producto pequeño en el centro y capacidades alrededor.
Esto reduce migración, sincronización, complejidad de interfaz y acoplamiento,
sin perder barcode, balanza, bodegas, canales, proveedores, costos ni reportes.
