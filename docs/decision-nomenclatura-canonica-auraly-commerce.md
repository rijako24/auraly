# Decisión: nomenclatura canónica de Auraly Commerce

**Estado:** obligatoria para diseño e implementación  
**Fecha:** 27 de julio de 2026  
**Objetivo:** eliminar nombres heredados, siglas ambiguas y términos que mezclan conceptos diferentes.

---

## 1. Regla general

Auraly usará:

- **español claro en la interfaz**;
- **inglés consistente en código, contratos y tablas**;
- nombres de negocio, no nombres accidentales de Xion;
- nombres completos, no siglas internas;
- un término único para cada concepto;
- alias heredados solo para migración y búsqueda.

Ejemplo:

```text
Interfaz:     Verificación de despacho
Ruta:         /dashboard/dispatches/{id}/verify
Aggregate:    Dispatch
Caso de uso:  VerifyDispatchCommand
Tabla:        DispatchVerificationEvents
Permiso:      dispatches.verify
Alias legado: Aduana
```

---

## 2. Nombres que no deben sobrevivir

No deben aparecer en proyectos, namespaces, clases, tablas, endpoints ni nuevas variables:

```text
Nombres comerciales ajenos a Auraly
Xion
PedidosOK
EnSa
Aduana
CargueDeMercancia
SalidaDeMercancia
Z...
S...
Sl...
X...                         // cuando sea prefijo técnico heredado
BdServidor
BdLocal
UnitOfWorkServidor
UnitOfWorkLocal
Grilla...
Frm...
Parametro...
Tipo...Id                    // cuando el valor es enum, no entidad
```

Excepciones:

- `Xion` y `PedidosOK` pueden aparecer en migración, documentación histórica y `SourceSystem`;
- `Aduana` puede ser alias temporal de búsqueda;
- `Kardex` se conserva en la interfaz por ser un término comercial reconocido en Colombia.

---

## 3. Organización y estructura

| Xion/Auraly anterior | Interfaz Auraly | Código | Tabla |
|---|---|---|---|
| Tenant | Organización | `Tenant` | `Tenants` |
| Empresa | Empresa | `Business` | `Businesses` |
| Negocio | Empresa o unidad de negocio, según contexto | `Business` | `Businesses` |
| Sucursal | Sede | `Branch` | `Branches` |
| Bodega | Bodega | `Warehouse` | `Warehouses` |
| Equipo | Dispositivo | `Device` | `Devices` |
| Caja | Caja / Punto de venta | `CashRegister` | `CashRegisters` |
| Parametrizar caja | Configuración de caja | `CashRegisterSettings` | `CashRegisterSettings` |
| Sesión/turno | Sesión de caja | `CashSession` | `CashSessions` |

Una empresa puede tener varias sedes; una sede puede tener varias bodegas y cajas.

---

## 4. Personas y seguridad

| Heredado | Interfaz | Código | Tabla |
|---|---|---|---|
| Persona | Persona o empresa | `Party` | `Parties` |
| Cliente | Clientes | `Customer` | `Customers` |
| Proveedor | Proveedores | `Supplier` | `Suppliers` |
| Empleado | Empleados | `Employee` | `Employees` |
| Vendedor | Vendedores | `Seller` | `Sellers` |
| Conductor | Conductores | `Driver` | `Drivers` |
| Transportador | Transportadores | `Carrier` | `Carriers` |
| Usuario | Usuarios | `UserAccount` | `UserAccounts` |
| Perfil | Perfiles de permisos | `PermissionProfile` | `PermissionProfiles` |
| Permiso | Permisos | `Permission` | `Permissions` |
| Permiso por usuario | Excepción de usuario | `UserPermissionOverride` | `UserPermissionOverrides` |

Reglas:

- `Party` no se muestra como “Party” ni “Tercero” si existe un rol más claro;
- conductor es una persona que conduce;
- transportador puede ser persona o empresa responsable del transporte;
- usuario no es sinónimo de empleado;
- perfil es plantilla; el permiso efectivo pertenece al usuario y su alcance.

---

## 5. Catálogo y precios

| Heredado | Interfaz | Código | Tabla |
|---|---|---|---|
| Producto | Productos | `Product` | `Products` |
| Código alterno | Códigos de producto | `ProductCode` | `ProductCodes` |
| Código de barras | Códigos de barras | `ProductBarcode` | `ProductBarcodes` |
| Embalaje | Presentación / Unidad | `ProductUnit` | `ProductUnits` |
| Conversión de unidad | Factor de conversión | `UnitConversion` | `UnitConversions` |
| Familia | Categoría | `ProductCategory` | `ProductCategories` |
| Producto proveedor | Productos por proveedor | `SupplierProduct` | `SupplierProducts` |
| Costos proveedor | Historial de costos | `SupplierProductCost` | `SupplierProductCosts` |
| Lista de precios | Listas de precios | `PriceList` | `PriceLists` |
| Canal | Canal de precios | `PriceChannel` | `PriceChannels` |
| Precio producto | Precio de producto | `ProductPrice` | `ProductPrices` |
| Evento | Promoción | `Promotion` | `Promotions` |

No usar `Evento` para promociones en código nuevo.

---

## 6. Ventas y POS

| Xion | Interfaz Auraly | Código | Tabla |
|---|---|---|---|
| Facturación | Punto de venta | `PointOfSale` | no aplica como agregado |
| Salida de mercancía | Factura de venta | `SalesInvoice` | `SalesInvoices` |
| Salida detalle | Líneas de factura | `SalesInvoiceLine` | `SalesInvoiceLines` |
| Factura temporal | Venta temporal | `SalesDraft` | `SalesDrafts` |
| Medio de pago factura | Medios de pago | `SalesPayment` | `SalesPayments` |
| Factura remisión | vínculo documental legado | no entra al MVP | no entra |
| Cotización | Cotizaciones | fuera del MVP | fuera |
| Apartado | Apartados | fuera del MVP | fuera |
| Remisión | Remisiones | fuera del MVP | fuera |
| Domicilio | Entrega/Domicilio | fuera del MVP | fuera |

`SalidaDeMercancia` no debe migrarse como nombre porque en Xion representa una venta completa, no una salida manual de inventario.

Separación:

```text
SalesInvoice        documento comercial
ElectronicDocument  documento fiscal DIAN
ManualInventoryMovement(Direction.Outbound)
                    salida manual de inventario
```

---

## 7. Pedidos

| Heredado | Interfaz | Código | Tabla |
|---|---|---|---|
| Pedido | Pedidos | `Order` | `Orders` |
| Pedido detalle | Productos del pedido | `OrderLine` | `OrderLines` |
| Recuperar pedido | Recuperar pedido | `ClaimOrderForInvoice` | `OrderInvoicingClaims` |
| Facturar seleccionados | Facturar seleccionados | `InvoiceSelectedOrders` | `DocumentProcessingOperations` |
| Pedido-factura | Facturas generadas | `OrderInvoiceLink` | `OrderInvoiceLinks` |

No usar:

```text
PedidoCabecera
PedidoMaestro
PedidoTemporalServidor
ZPedido
SPedido
```

---

## 8. Compras y recepciones

| Xion | Interfaz | Código | Tabla |
|---|---|---|---|
| Orden de pedido | Órdenes de compra | `PurchaseOrder` | `PurchaseOrders` |
| Entrada de mercancía | Recepciones de compra | `GoodsReceipt` | `GoodsReceipts` |
| Entrada detalle | Productos recibidos | `GoodsReceiptLine` | `GoodsReceiptLines` |
| Factura proveedor | Factura del proveedor | `SupplierInvoiceReference` | parte de `GoodsReceipts` en MVP |
| Diferencia | Diferencia de recepción | `ReceiptDifference` | `GoodsReceiptLines` |
| Devolución entrada | Devolución a proveedor | `PurchaseReturn` | `PurchaseReturns` |
| Devolución detalle | Productos devueltos | `PurchaseReturnLine` | `PurchaseReturnLines` |

La interfaz distingue el compromiso (**Orden de compra**) de la llegada física (**Recepción de compra**). En código se conservan `PurchaseOrder` y `GoodsReceipt`; no se introducen traducciones técnicas paralelas.

---

## 9. Inventario

| Xion | Interfaz | Código | Tabla |
|---|---|---|---|
| Existencias | Existencias | `InventoryBalance` | `InventoryBalances` |
| Kardex | Kardex de inventario | `InventoryLedger` | proyección/consulta |
| Producto Kardex | Movimiento de inventario | `InventoryTransaction` | `InventoryTransactions` |
| EnSa | Movimientos de inventario | `ManualInventoryMovement` | `ManualInventoryMovements` |
| EnSa Entrada | Entrada manual | `Inbound` | enum `InventoryMovementDirection` |
| EnSa Salida | Salida manual | `Outbound` | enum `InventoryMovementDirection` |
| Motivo EnSa | Motivo de movimiento | `InventoryMovementReason` | `InventoryMovementReasons` |
| Inventario físico | Conteos de inventario | `StockCount` | `StockCounts` |
| Traslado | Traslados entre bodegas | `StockTransfer` | `StockTransfers` |
| Avería | Averías | `InventoryDamage` | `InventoryDamages` |
| Conversión | Conversión de productos | `InventoryConversion` | `InventoryConversions` |

No usar genéricamente:

```text
Movimiento
Entrada
Salida
Detalle
Encabezado
```

Usar el nombre completo del agregado.

---

## 10. Despachos

| Xion | Interfaz | Código | Tabla |
|---|---|---|---|
| Cargue de mercancía | Despachos | `Dispatch` | `Dispatches` |
| Cargue factura | Documentos del despacho | `DispatchSourceDocument` | `DispatchSourceDocuments` |
| Cargue pendiente | Faltante de preparación | `DispatchShortage` | `DispatchShortages` |
| Pendiente incluido | Faltante reasignado | `ReassignedDispatchShortage` | vínculo en `DispatchShortages` |
| Aduana | Verificación de despacho | `DispatchVerification` | estado/eventos |
| Aduana detalle | Eventos de verificación | `DispatchVerificationEvent` | `DispatchVerificationEvents` |
| Cargue revisado | Despacho verificado | `Verified` | estado |
| Entrega al conductor | Liberar despacho | `ReleaseDispatch` | `DispatchCustodyEvents` |

Menú:

```text
Despachos
  Preparar despachos
  Verificar despacho
  Historial de despachos
```

No utilizar “Aduana” salvo alias temporal para clientes migrados.

---

## 11. Caja y dinero

| Xion | Interfaz | Código | Tabla |
|---|---|---|---|
| Apertura | Apertura de caja | `OpenCashSession` | `CashSessions` |
| Cierre | Arqueo de caja | `CashCount` / `CloseCashSession` | `CashCounts` |
| Entrada/salida dinero | Movimientos de caja | `CashMovement` | `CashMovements` |
| Tipo entrada/salida | Tipo de movimiento de caja | `CashMovementType` | `CashMovementTypes` |
| Total esperado | Saldo esperado | `ExpectedAmount` | campo |
| Total contado | Total contado | `CountedAmount` | campo |
| Diferencia | Diferencia de caja | `CashDifference` | campo/resultado |

No llamar “cierre” a toda la funcionalidad: el proceso incluye conteo, conciliación, diferencia, aprobación y cierre de sesión.

---

## 12. Cartera

| Xion | Interfaz | Código | Tabla |
|---|---|---|---|
| Cuenta por cobrar | Cuentas por cobrar | `Receivable` | `Receivables` |
| Kardex CxC | Movimientos de cuenta por cobrar | `ReceivableTransaction` | `ReceivableTransactions` |
| Abono | Abonos recibidos | `ReceivableApplication` | `ReceivableApplications` |
| Cuenta por pagar | Cuentas por pagar | `Payable` | `Payables` |
| Kardex CxP | Movimientos de cuenta por pagar | `PayableTransaction` | `PayableTransactions` |
| Pago proveedor | Pagos a proveedores | `PayableApplication` | `PayableApplications` |

En la interfaz se escriben los nombres completos. `CxC` y `CxP` pueden aparecer como abreviatura secundaria en reportes, nunca como nombre principal de entidades.

---

## 13. Devoluciones

| Heredado | Interfaz | Código | Tabla |
|---|---|---|---|
| Devolución factura | Devolución de venta | `SalesReturn` | `SalesReturns` |
| Devolución entrada | Devolución a proveedor | `PurchaseReturn` | `PurchaseReturns` |
| Cambio | Cambio de mercancía | flujo coordinado | vínculos entre documentos |
| Nota crédito | Nota crédito electrónica | `ElectronicCreditNote` | `ElectronicDocuments` |
| Reembolso | Reembolso | `Refund` | `Refunds` |

No usar una entidad genérica `Devolucion` para ambos sentidos.

---

## 14. Fiscal

| Heredado | Interfaz | Código | Tabla |
|---|---|---|---|
| Facturación electrónica | Documentos electrónicos | `ElectronicDocument` | `ElectronicDocuments` |
| Factura electrónica | Factura electrónica de venta | `ElectronicSalesInvoice` | discriminador |
| Nota crédito | Nota crédito electrónica | `ElectronicCreditNote` | discriminador |
| Nota débito | Nota débito electrónica | `ElectronicDebitNote` | discriminador |
| Documento POS | Documento equivalente electrónico POS | `ElectronicPosDocument` | discriminador |
| Resolución | Resoluciones de numeración | `NumberingResolution` | `NumberingResolutions` |
| Certificado | Certificados de firma | `SigningCertificate` | metadatos; secreto externo |
| Envío DIAN | Transmisiones DIAN | `DianSubmission` | `DianSubmissions` |
| Respuesta DIAN | Respuestas DIAN | `DianResponse` | `DianResponses` |

No confundir:

```text
SalesInvoice
ElectronicDocument
DocumentNumber
DianNumber
```

---

## 15. Motor del servidor

| Heredado | Interfaz/operación | Código | Tabla |
|---|---|---|---|
| Motor | Procesamiento de documentos | `DocumentProcessing` | prefijo funcional |
| Movimiento por procesar | Operación pendiente | `DocumentProcessingOperation` | `DocumentProcessingOperations` |
| Tipo proceso motor | Tipo de operación | `DocumentOperationType` | enum/contrato |
| Procesado | Confirmado/Completado | estado específico | campo `Status` |
| Error motor | Error de procesamiento | `ProcessingFailure` | operación/item |
| Reprocesar | Reintentar | `RetryDocumentOperation` | comando |
| Conciliar | Reconciliar operación | `ReconcileDocumentOperation` | comando |

No usar:

```text
MotorService
ProcesarTodo
TipoProcesoMotor
MovimientoXProcesar
```

El orquestador es `DocumentProcessing`; la regla pertenece al procesador del módulo.

---

## 16. Sincronización POS

| Heredado | Interfaz | Código | Tabla |
|---|---|---|---|
| Motor local | Auraly POS Edge | `Auraly.PosEdge` | aplicación |
| Base local | Catálogo local | `PosLocalStore` | SQLite |
| Pendientes por subir | Operaciones pendientes | `LocalOutboxOperation` | `LocalOutbox` |
| Versión catálogo | Revisión de catálogo | `CatalogRevision` | checkpoint |
| Actualizar productos | Sincronizar catálogo | `ApplyCatalogDelta` | caso de uso |
| Primera sincronización | Preparando facturación | `BootstrapPosCatalog` | caso de uso |
| Caja registrada | Dispositivo aprovisionado | `ProvisionedPosDevice` | `PosDevices` |

El término `sync` se acepta en código técnico, pero la interfaz dice “Sincronización”.

---

## 17. Reportes

| Heredado | Interfaz | Código |
|---|---|---|
| Informe | Reporte | `Report` |
| Consultar informe | Consultar reporte | `ReportQuery` |
| Parámetros búsqueda | Filtros | `ReportFilter` |
| Grilla informe | Resultados | `ReportRow` |
| Exportar informe | Exportar | `ExportReport` |

No crear una clase por cada formato visual heredado. Los reportes se construyen sobre consultas, proyecciones y exportadores.

---

## 18. Convenciones de código

### Agregados y entidades

```text
Singular PascalCase
SalesInvoice
GoodsReceipt
StockTransfer
Dispatch
```

### Tablas

```text
Plural PascalCase en dbo
SalesInvoices
GoodsReceipts
StockTransfers
Dispatches
```

### IDs

```text
SalesInvoiceId
GoodsReceiptId
DispatchId
```

No usar:

```text
IdFactura
Factura_Id
ID
CodigoInternoId
```

### Fechas

```text
CreatedAtUtc
ConfirmedAtUtc
OccurredAtUtc
BusinessDate
DueDate
```

No usar ambiguamente:

```text
Fecha
FechaSistema
FechaRegistro
```

### Cantidades y dinero

```text
CapturedQuantity
BaseQuantity
UnitPrice
UnitCost
Subtotal
TaxAmount
GrandTotal
```

### Estados

Usar enums específicos:

```text
SalesInvoiceStatus
GoodsReceiptStatus
DispatchStatus
```

No usar un enum global `Estado` para todos los módulos.

### DTO y contratos

```text
CreateDispatchRequest
DispatchResponse
VerifyDispatchCommand
DispatchVerifiedIntegrationEvent
```

Evitar:

```text
DtoGeneral
RequestModel
Respuesta
ObjetoEnviar
DatosGrilla
```

---

## 19. Convenciones de interfaz

### Menú

Usar sustantivos del negocio:

```text
Productos
Precios
Inventario
Compras
Ventas
Pedidos
Despachos
Caja
Cuentas por cobrar
Cuentas por pagar
Documentos electrónicos
Reportes
Configuración
```

### Acciones

Usar verbos claros:

```text
Crear
Guardar temporal
Confirmar
Cancelar borrador
Reversar
Verificar
Liberar despacho
Registrar abono
Registrar pago
Reintentar
Reconciliar
```

Evitar botones:

```text
Procesar
Ejecutar
Enviar
Aceptar
Adicionar
```

cuando pueda expresarse la acción real.

### Eliminación

```text
Eliminar línea         permitido en borrador
Cancelar borrador      documento sin efectos
Reversar documento     documento confirmado
```

No mostrar “Eliminar” para un documento confirmado.

---

## 20. Rutas web canónicas

```text
/dashboard/products
/dashboard/pricing/channels
/dashboard/inventory/balances
/dashboard/inventory/ledger
/dashboard/inventory/movements
/dashboard/inventory/counts
/dashboard/inventory/transfers
/dashboard/inventory/damages
/dashboard/inventory/conversions
/dashboard/purchasing/goods-receipts
/dashboard/purchasing/purchase-returns
/dashboard/sales/point-of-sale
/dashboard/sales/invoices
/dashboard/orders
/dashboard/dispatches
/dashboard/dispatches/{id}/verify
/dashboard/cash/sessions
/dashboard/receivables
/dashboard/payables
/dashboard/fiscal/documents
/dashboard/reports
/dashboard/security/users
/dashboard/security/permissions
```

No introducir rutas heredadas como:

```text
/ensa
/aduana
/cargue
/salidas
/zfactura
```

---

## 21. Compatibilidad y migración

Los nombres anteriores se guardan únicamente en:

```text
LegacyEntityMappings
ProductExternalIdentifiers
SourceSystem
LegacyEntityType
LegacyId
MigrationNotes
```

Ejemplo:

```text
SourceSystem     = Xion
LegacyEntityType = CargueDeMercancia
AuralyEntityType = Dispatch
```

No agregar columnas como:

```text
XionId
IdViejo
CodigoAnterior
NoDocumentoXion
```

en cada tabla si `LegacyEntityMappings` cubre el caso.

---

## 22. Pruebas de nomenclatura

CI debe detectar:

- proyectos o namespaces nuevos con marcas heredadas o Xion;
- clases nuevas con prefijos `Z`, `S`, `Sl`;
- `EnSa`, `Aduana` o `CargueDeMercancia` fuera de migración;
- tablas no registradas en el mapa de propiedad;
- contratos con nombres genéricos;
- entidades con `double` para dinero/cantidades;
- rutas web heredadas;
- nuevas migraciones EF;
- referencias a Infrastructure desde Domain/Application.

Las excepciones necesitan:

- justificación;
- responsable;
- fecha de eliminación;
- prueba de compatibilidad.

---

## 23. Decisión final

Se renombrará todo lo que pueda mejorar claridad sin borrar el lenguaje natural del negocio.

Principio:

> El cliente debe reconocer el proceso; el código debe expresar el dominio; ninguno debe heredar los accidentes técnicos de Xion.

Se conservan en interfaz términos útiles como:

```text
Bodega
Recepción de compra
Kardex
Arqueo
Avería
```

Se reemplazan:

```text
EnSa                 -> Movimientos de inventario
Cargue de mercancía  -> Despachos
Aduana               -> Verificación de despacho
Cierre               -> Arqueo de caja
Evento               -> Promoción
Salida de mercancía  -> Factura de venta
```

En código y base se usarán exclusivamente los nombres canónicos definidos en este documento.
