# Auraly Commerce — diseño de catálogo y sincronización POS

## Decisión de identidad de producto

`dbo.Products` es la única tabla que identifica un producto en Auraly. No existe
`CatalogProducts` ni un proceso para mantener dos copias sincronizadas.

La tabla existente se amplía de forma aditiva con `TenantId`, `ProductCode`,
`Reference`, `BaseUnitCode`, `TaxProfileId`, `IsWeighable`, auditoría y
`RowVersion`. Los consumidores anteriores continúan usando temporalmente sus
columnas existentes. Para los casos de uso nuevos:

- `ProductId` se genera en la aplicación mediante UUIDv7.
- `ProductCode` es un identificador comercial, no la clave técnica.
- `UnitPrice` y `StockQuantity` heredados no son fuente de verdad del POS.
- Los precios viven en `ProductPrices`.
- El inventario se deriva de `InventoryMovements`.
- Un producto canónico requiere tenant, unidad e impuesto mediante una
  restricción SQL condicional que no invalida filas anteriores.

## Modelo normalizado

Las capacidades relacionadas se almacenan en:

- `TaxProfiles`
- `ProductBarcodes`
- `ProductIdentifiers`
- `ProductScaleConfigurations`
- `PriceChannels`
- `ProductPrices`
- `Suppliers`
- `SupplierProducts`
- `SupplierCostAgreements`
- `CatalogChanges`
- `CatalogSyncSessions`
- `CatalogSyncSessionProducts`

La última tabla congela la membresía del bootstrap. Así, cambiar el código de un
producto o crear otro mientras una caja descarga páginas no altera la
paginación de la sesión. La descarga se ordena por el `ProductId` inmutable.
Los cambios posteriores al high-water mark se aplican por el flujo incremental.

Los códigos de barras, código interno, precios activos y asociaciones críticas
tienen índices únicos en SQL Server. La escritura del producto, sus hijos y el
registro de `CatalogChanges` ocurre en una sola transacción serializable.

## Seguridad

Los endpoints administrativos usan JWT Bearer configurable. La clave no se
incluye en `appsettings`; si no se configura, el host arranca en modo fail
closed y ningún JWT puede validarse. Los claims canónicos son:

- `sub` o `ClaimTypes.NameIdentifier`
- `tenant_id`
- `business_id`
- uno o más `permission`

Los endpoints POS conservan autenticación de dispositivo. La identidad resuelta
en servidor fija tenant, empresa, sede, bodega, caja y permisos; esos valores no
se confían al body.

## API

Administración:

- `POST /api/commerce/v1/products`
- `PUT /api/commerce/v1/products/{productId}`
- `GET /api/commerce/v1/products/{productId}`
- `GET /api/commerce/v1/products`
- `POST /api/commerce/v1/products/{productId}/deactivate`

La lista usa paginación keyset por código, orden ascendente o descendente y
filtros combinables por código, referencia, nombre, estado, código de barras,
proveedor, canal y rango de precio.

POS:

- `POST /api/pos/v1/catalog/sync-sessions`
- `GET /api/pos/v1/catalog/sync-sessions/{sessionId}/pages`
- `GET /api/pos/v1/catalog/changes?cursor={cursor}`
- `POST /api/pos/v1/inventory/availability`

## Bootstrap

1. POS Edge crea automáticamente una sesión si su catálogo local está vacío.
2. El servidor captura high-water mark, canal de la caja y los IDs vendibles.
3. POS descarga páginas y valida su SHA-256.
4. Cada página se guarda en tablas SQLite de staging y actualiza el checkpoint
   en la misma transacción.
5. Reiniciar el proceso conserva sesión y cursor de página.
6. La promoción reemplaza el catálogo visible en una sola transacción.
7. El cursor local se establece en el high-water mark.
8. Se aplican los cambios incrementales posteriores.

La caja recibe únicamente datos vendibles y el precio de su canal. No recibe
costos, proveedores, precios de otros canales ni existencias.

## Incremental

`CatalogChanges.CatalogChangeId` es el cursor monotónico durable. Cada respuesta
declara cursor inicial y final. POS Edge exige orden estricto, aplica upserts o
tombstones y avanza el cursor dentro de la misma transacción SQLite. Repetir una
página ya aplicada no produce efectos.

Una notificación futura solo despertará este mismo consumidor. El transporte
real de esta rebanada es sondeo HTTP incremental.

## Captura offline y balanza

SQLite permite búsqueda por código de barras, código interno, referencia,
nombre e identificadores alternos. La captura exacta no usa búsqueda difusa.
Los productos bloqueados permanecen almacenados para historia, pero quedan
excluidos de nuevas ventas.

La regla de balanza define prefijo, PLU, posición/longitud del valor, decimales y
si representa peso o precio. El parser es determinístico y no supone un
protocolo universal para todas las balanzas.

## Disponibilidad

POS Edge no almacena inventario. El endpoint valida que producto, dispositivo,
caja y bodega pertenezcan al mismo contexto. Si la bodega permite negativos,
responde que no se requiere bloqueo. Si los bloquea, calcula disponibilidad
desde `InventoryMovements` y responde al agregar o cambiar cantidad.

