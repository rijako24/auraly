# Propiedad de datos Commerce sin tenant redundante

Fecha: 2026-07-28

## Regla

`Businesses.BusinessId` es globalmente único y resuelve obligatoriamente un
`TenantId`. Una tabla cuyo agregado pertenece a un negocio persiste
`BusinessId`, no una copia adicional de `TenantId`. Un hijo conserva únicamente
la clave de su agregado si esa relación determina el negocio sin ambigüedad.

El contexto autenticado sí mantiene tenant y negocio. La eliminación de la
columna no elimina la validación: cada operación comprueba que el negocio,
usuario/dispositivo, caja, bodega y recurso pertenecen al tenant autenticado.

## Matriz inicial de propiedad

| Tabla | Aggregate root | Dueño real | BusinessId | TenantId necesario | Cambio |
|---|---|---|---:|---:|---|
| Tenants | Tenant | Tenant | No | Sí | Ninguno |
| Businesses | Business | Tenant | PK | Sí | Conservar FK obligatoria |
| BusinessLocations | Location | Business | Sí | No | Retirar si existe |
| Products | Product | Business | Sí | No | Retirar columna/FK y ajustar restricción |
| TaxProfiles | TaxProfile | Business | Sí | No | Retirar |
| ProductBarcodes | Product | Product padre | No necesario | No | Retirar tenant/business redundantes |
| ProductIdentifiers | Product | Product padre | No necesario | No | Retirar tenant/business redundantes |
| ProductScaleConfigurations | Product | Product padre | No | No | Sin cambio |
| ProductPrices | BasePrice | Business | Sí | No | Retirar tenant/canal; precio base |
| PriceLists | PriceList | Business | Sí | No | Nueva |
| PriceListItems | PriceList | Padre | No | No | Nueva |
| PriceChannels | PriceChannel | Business | Sí | No | Retirar tenant/default |
| PriceChannelItems | PriceChannel | Padre + product configurado | No | No | Nueva; tramo explícito, no precio materializado |
| PriceChannelExclusions | PriceChannel | Padre | No | No | Nueva |
| Customers | Customer | Tenant/persona global | No | Sí cuando el cliente pueda compartirse | Nueva frontera mínima |
| CustomerBusinesses | CustomerBusiness | Business | Sí | No | Nueva |
| CustomerBusinessPricing | CustomerBusiness | Business | Sí | No | Nueva; lista/canal excluyentes |
| Suppliers | Supplier | Business | Sí | No | Retirar tenant |
| SupplierProducts | SupplierProduct | Supplier/Product | No necesario | No | Retirar tenant/business |
| SupplierCostAgreements | SupplierProduct | Padre | No | No | Sin cambio |
| Warehouses | Warehouse | Business | Sí | No | Retirar |
| CashRegisters | Register | Business | Sí | No | Retirar tenant y canal de caja |
| PosDevices | Device | Register | Derivable | No | Retirar tenant/business/location/warehouse redundantes |
| FiscalAuthorizations | Authorization | Business | Sí | No | Retirar |
| FiscalSeries | Series | Register/Authorization | Derivable | No | Retirar tenant/business |
| SalesDocuments | SalesDocument | Business | Sí | No | Retirar tenant |
| SalesDocumentLines | SalesDocument | Padre | No | No | Sin cambio |
| SalesPayments | SalesDocument | Padre | No | No | Sin cambio |
| FiscalSnapshots | SalesDocument | Padre | No | No | Sin cambio |
| DocumentProcessingJobs | Documento operativo | Business/documento | Sí | No | Autoridad única del trabajo, orden e idempotencia |
| InventoryMovements | Inventory | Business | Sí | No | Retirar tenant |
| ServerOutboxMessages | SalesDocument | Padre | No | No | Retirar tenant |
| CatalogChanges | Product/Business | Business | Sí | No | Retirar tenant |
| CatalogSyncSessions | Device | Device/Business | Business derivable, se conserva para snapshot de alcance | No | Retirar tenant/canal de caja |
| RegisterSessions | RegisterSession | Business/Register | Sí | No | Nueva |
| CashMovements | RegisterSession | Padre | No | No | Nueva |
| CashCounts | RegisterSession | Padre | No | No | Nueva |
| Receivables | SalesDocument | Business/documento | Business derivable | No | Nueva |
| PrintJobs | SalesDocument/local | Documento/dispositivo | Derivable | No | Nueva en SQLite; auditoría servidor posterior |

## Estrategia de actualización

1. Agregar o verificar las FK que permiten resolver el dueño.
2. Actualizar consultas y escrituras para usar `BusinessId` y joins de
   autorización, sin confiar en el body.
3. Eliminar índices y restricciones basados en tenant redundante.
4. Eliminar columnas en un script predeployment idempotente para permitir
   actualizar una base creada por el DACPAC anterior.
5. Publicar el DACPAC sobre una copia de esquema anterior y sobre una base vacía.
6. Probar dos negocios del mismo tenant y negocios de tenants diferentes.
7. Mantener la identidad autenticada con tenant para detectar cruces.

No se realiza una eliminación mecánica: cada FK y consulta se modifica en el
mismo commit que cambia su responsabilidad.

