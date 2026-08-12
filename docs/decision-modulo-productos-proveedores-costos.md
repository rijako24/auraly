# Decisión: módulo Productos, Proveedores y Costos

**Estado:** incluido en el MVP de Auraly Commerce  
**Fecha:** 24 de julio de 2026  
**Referencia:** módulo `ProductosProveedoresCostos` de Xion, ficha de producto, entradas de mercancía, motor, auditorías y consultas de proveedores  
**Principio:** Xion se usa como especificación funcional y fuente de reglas; Auraly no copia sus tablas, formularios ni limitaciones técnicas.

---

## 1. Decisión ejecutiva

Auraly Commerce incluirá un módulo explícito de **Productos, Proveedores y Costos**.

El módulo permitirá:

- asociar un producto con uno o varios proveedores;
- definir un proveedor principal y proveedores alternos;
- guardar el código o referencia que usa cada proveedor para el producto;
- mantener el costo negociado por negocio y, cuando sea necesario, por sucursal;
- registrar descuentos comerciales sucesivos sin columnas rígidas `Dc1...Dc5`;
- consultar costo base, costo negociado efectivo, última compra y costo promedio;
- actualizar costos individualmente o de forma masiva;
- detectar variaciones entre costo esperado y costo recibido;
- proponer, aprobar y publicar cambios;
- consultar historial completo;
- relacionar el costo con la entrada de mercancía que lo originó;
- alimentar reportes de compras, utilidad y margen;
- proteger la información de costos mediante permisos específicos.

La mejora fundamental frente a Xion es separar conceptos que allí aparecen mezclados:

```text
Término negociado con proveedor
        !=
Costo observado en una compra
        !=
Costo de valoración del inventario
        !=
Costo usado para sugerir precios de venta
```

Una edición manual del costo de referencia no cambia por sí sola el costo promedio del inventario ni reescribe documentos anteriores.

---

## 2. Lo encontrado en Xion

La entidad `ProductoProveedorCosto` usa como clave:

```text
ProductoId + SucursalId + ProveedorId
```

Contiene:

- proveedor principal;
- precio de costo base;
- cinco descuentos comerciales;
- costo después de descuentos;
- cinco descuentos financieros;
- indicador para aplicar descuentos financieros;
- flete;
- descargue;
- costo neto;
- entrada de mercancía origen;
- cambio pendiente;
- fecha de actualización.

Los formularios y servicios permiten:

- seleccionar proveedor y sucursal;
- consultar sus productos;
- agregar o quitar productos;
- editar costos en grilla;
- navegar con teclado;
- recalcular base desde neto y neto desde base;
- filtrar por marca, casa comercial, familias y diferencias de costo;
- consultar rotación;
- ver otros proveedores del producto;
- reasignar productos a otro proveedor;
- marcar proveedor principal;
- actualizar en otras sucursales del mismo grupo de costo;
- comparar costo del proveedor contra costo actual;
- propagar cambios;
- registrar auditoría;
- usar permisos de modificar y guardar;
- actualizar costos desde entradas de mercancía;
- sincronizar cambios hacia instalaciones locales.

Este comportamiento demuestra que el módulo es necesario. También revela problemas que no se deben trasladar:

- uso de `double` para dinero;
- sobrescritura de la fila vigente;
- columnas fijas para cinco descuentos comerciales y cinco financieros;
- mezcla de costo negociado, impuestos, flete, descargue y valoración;
- eliminación física de relaciones;
- actualización masiva con efectos difíciles de previsualizar;
- dependencia de estado global y formularios;
- SQL construido dentro de servicios;
- propagación implícita por grupos de sucursal;
- duplicación local/servidor.

---

## 3. Límites del módulo

El contexto recomendado es:

```text
Auraly.Domain.Procurement
Auraly.Application.Procurement
Auraly.Infrastructure.Procurement
Auraly.Contracts.Procurement
```

Dentro de `Procurement`, la capacidad se denomina `SupplierCatalog`.

Ownership:

| Concepto | Módulo dueño |
|---|---|
| Identidad y datos fiscales del proveedor | `Parties` |
| Producto, unidad base y códigos propios | `Catalog` |
| Asociación producto–proveedor | `Procurement.SupplierCatalog` |
| Términos y costo negociado | `Procurement.SupplierCatalog` |
| Entrada de mercancía y costo observado | `Purchasing/InventoryReceipts` |
| Costo promedio y valoración | `Inventory` |
| Canales y precios de venta | `Pricing` |
| Cuentas por pagar | `AccountsPayable` |
| Auditoría transversal | `Audit` |

El módulo consulta referencias mediante IDs y contratos; no modifica directamente agregados ajenos.

Todo continúa en la misma API, base SQL y esquema `dbo`. La separación por librerías permite extraer `Procurement` en el futuro sin rediseñar el dominio.

---

## 4. Modelo conceptual mejorado

### 4.1 Asociación producto–proveedor

```text
SupplierProduct
---------------
Id
BusinessId
ProductId
SupplierId
SupplierProductCode
SupplierBarcode
IsPrimary
IsActive
MinimumOrderQuantity?
LeadTimeDays?
Notes?
CreatedAt
CreatedBy
UpdatedAt
UpdatedBy
RowVersion
```

Para el MVP:

- `SupplierProductCode` es importante para buscar y conciliar facturas o listas del proveedor;
- `SupplierBarcode` es opcional y no reemplaza los códigos de venta del producto;
- `MinimumOrderQuantity` y `LeadTimeDays` pueden capturarse si Xion o un piloto demuestra uso;
- conversiones y empaques de compra complejos siguen fuera del MVP;
- desasociar significa desactivar, no borrar historial.

Restricciones:

- no puede repetirse una asociación activa para el mismo negocio, producto y proveedor;
- solo puede existir un proveedor principal efectivo por producto y alcance;
- proveedor y producto deben estar activos para nuevas operaciones;
- una relación usada por documentos nunca se elimina físicamente.

### 4.2 Alcance del costo

El costo se define inicialmente a nivel de negocio:

```text
Business + Product + Supplier
```

Se permite una sobreescritura por sucursal cuando el negocio realmente compra con condiciones diferentes:

```text
Business + Branch + Product + Supplier
```

Regla de resolución:

```text
condición de sucursal vigente
    -> condición del negocio vigente
    -> última compra informativa
    -> sin costo negociado
```

No se copiarán automáticamente filas por cada sucursal. Una condición de negocio se hereda; solo se almacena la excepción.

Los “grupos de costo” de Xion pueden migrarse posteriormente como ámbitos explícitos, pero no serán una propagación oculta. Si un piloto los necesita:

```text
CostScope = Business | BranchGroup | Branch
```

La interfaz siempre mostrará el alcance y el impacto antes de publicar.

### 4.3 Término negociado

```text
SupplierCostAgreement
---------------------
Id
SupplierProductId
ScopeType
ScopeId
CurrencyCode
BaseUnitCost
TaxInclusionMode
ValidFrom
ValidUntil?
Status
SourceType
SourceReference?
CreatedAt
CreatedBy
ApprovedAt?
ApprovedBy?
RowVersion
```

Estados:

```text
Draft
PendingApproval
Active
Superseded
Rejected
Expired
```

Una versión publicada es inmutable. Un cambio crea una nueva versión.

`SourceType` identifica:

- manual;
- entrada de mercancía;
- importación;
- migración de Xion;
- integración futura.

### 4.4 Ajustes y descuentos

En vez de `Dc1...Dc5`:

```text
SupplierCostAdjustment
----------------------
Id
SupplierCostAgreementId
Sequence
Type
Value
Description?
```

Tipos iniciales:

```text
PercentageDiscount
FixedDiscount
Surcharge
```

Los porcentajes sucesivos se aplican en orden:

```text
effective = base
for adjustment ordered by sequence:
    effective = apply(effective, adjustment)
```

Ejemplo:

```text
Base: 100.000
Descuento 10 % -> 90.000
Descuento 5 %  -> 85.500
```

No se suman como 15 %. El motor usa `decimal`, reglas de precisión y redondeo monetario técnico, no `double`.

Según decisiones anteriores:

- fletes no forman parte de este MVP;
- descargues no forman parte de este MVP;
- retenciones no forman parte del cálculo del costo;
- descuentos financieros especializados no se migran como diez columnas;
- si después se requieren otros componentes, se agregan como tipos explícitos y probados.

### 4.5 Proyección del costo efectivo

Para consultas rápidas se puede mantener:

```text
SupplierEffectiveCost
---------------------
SupplierCostAgreementId
EffectiveUnitCost
CalculatedAt
FormulaVersion
```

Es una proyección recalculable, no una segunda fuente de verdad.

### 4.6 Costo observado en compras

Cada línea confirmada de entrada conserva un snapshot:

```text
ReceiptLineCostSnapshot
-----------------------
ReceiptId
ReceiptLineId
ProductId
SupplierId
SupplierProductId?
Quantity
BaseUnitCost
AppliedAdjustmentsJson
EffectiveUnitCost
TaxSnapshot
CurrencyCode
DocumentDate
```

El documento nunca consulta el costo vigente para reconstruir el pasado.

Al confirmar una entrada:

1. se conserva el costo realmente recibido;
2. se actualiza la última compra;
3. Inventory recalcula su valoración según la política definida;
4. se crea la CxP cuando corresponda;
5. si difiere del acuerdo activo, se registra una variación;
6. opcionalmente se propone una nueva versión de costo;
7. nunca se publica un cambio sin la regla de aprobación configurada.

### 4.7 Costos que debe mostrar Auraly

La UI distingue:

| Nombre | Significado |
|---|---|
| Costo base negociado | Valor antes de descuentos |
| Costo efectivo negociado | Resultado del acuerdo vigente |
| Último costo de compra | Snapshot de la última entrada confirmada |
| Costo promedio | Valoración actual calculada por Inventario |
| Variación | Diferencia absoluta y porcentual contra referencia |
| Costo sugerido para precio | Base seleccionada por la política de Pricing |

No se usará el nombre ambiguo “costo neto” sin indicar qué incluye.

---

## 5. Proveedor principal y alternos

Cada producto puede tener:

- cero o un proveedor principal efectivo por alcance;
- varios proveedores alternos activos.

Cambiar el principal:

1. valida permisos;
2. muestra el proveedor anterior y el nuevo;
3. no altera costos ni documentos;
4. se ejecuta transaccionalmente;
5. garantiza unicidad;
6. deja auditoría;
7. publica un evento de dominio.

```text
PrimarySupplierChanged
```

El proveedor principal sirve para:

- sugerir proveedor en entradas;
- priorizar búsquedas;
- reportar productos sin abastecimiento preferido;
- alimentar futuras órdenes de compra.

No obliga a comprarle ni determina por sí solo el costo promedio.

---

## 6. Flujos web

### 6.1 Vista principal

Ruta conceptual:

```text
Compras > Productos y costos por proveedor
```

Filtros:

- negocio;
- sucursal o alcance heredado;
- proveedor;
- producto, referencia o código de barras;
- código del proveedor;
- categoría;
- marca;
- principal/alterno;
- activo/inactivo;
- con/sin costo;
- cambios pendientes;
- variación porcentual;
- actualización por rango de fechas.

Columnas:

- selección;
- producto;
- referencia;
- código del proveedor;
- proveedor principal;
- alcance;
- costo base;
- descuentos resumidos;
- costo efectivo;
- última compra;
- costo promedio;
- variación;
- vigencia;
- estado;
- última modificación.

La tabla soporta:

- navegación completa por teclado;
- edición controlada;
- recálculo inmediato;
- pegado de varias filas;
- validación por celda y por lote;
- selección masiva;
- panel de errores;
- columnas fijadas;
- guardado como borrador;
- previsualización antes de publicar.

### 6.2 Ficha del producto

Pestaña **Proveedores y costos**:

- proveedor principal;
- proveedores alternos;
- códigos del proveedor;
- costo vigente por alcance;
- última compra;
- costo promedio;
- historial;
- agregar, desactivar o cambiar principal según permiso.

### 6.3 Ficha del proveedor

Pestaña **Catálogo y costos**:

- productos asociados;
- agregar productos;
- desactivar asociaciones;
- actualizar costos;
- importar archivo;
- ver pendientes;
- ver productos de los cuales es principal;
- consultar variaciones e historial.

### 6.4 Entrada de mercancía

Al capturar un producto:

- busca por códigos propios o del proveedor;
- comprueba si existe asociación;
- muestra costo negociado;
- permite digitar el costo del documento;
- calcula variación;
- permite crear la asociación con permiso;
- conserva el snapshot;
- al confirmar ejecuta efectos mediante el motor.

### 6.5 Reasignación

La reasignación de Xion se reemplaza por una operación explícita:

- seleccionar productos;
- proveedor origen;
- proveedor destino;
- copiar o no términos comerciales;
- seleccionar alcance;
- definir si el destino queda como principal;
- previsualizar conflictos;
- ejecutar por lote;
- resultado individual por producto;
- operación idempotente y auditada.

No se elimina automáticamente el proveedor anterior. Se desactiva solo si el usuario lo selecciona.

### 6.6 Importación

El MVP debe admitir CSV/XLSX si el piloto maneja listas extensas:

- plantilla descargable;
- proveedor y alcance explícitos;
- correspondencia por código Auraly, código de barras o código del proveedor;
- validación previa;
- filas aceptadas y rechazadas;
- sin publicación parcial silenciosa;
- opción de guardar lote como borrador;
- idempotencia por archivo y fila;
- informe descargable.

La importación no crea productos nuevos automáticamente salvo permiso y flujo separado.

---

## 7. Actualización de costos y precios de venta

Cambiar un costo negociado no debe modificar precios de venta sin control.

Flujo:

```text
Publicar costo
    -> SupplierCostChanged
    -> Pricing evalúa canales derivados de costo
    -> genera propuesta de nuevos precios
    -> usuario autorizado revisa/publica
    -> POS recibe deltas del precio efectivo
```

Excepcionalmente un negocio puede configurar actualización automática por fórmula, pero debe:

- estar habilitada explícitamente;
- tener límites de variación;
- dejar versión y auditoría;
- no publicar precios incoherentes;
- ejecutar pruebas de margen mínimo;
- generar deltas solo para productos afectados.

La caja recibe precios de venta efectivos. Nunca recibe costos de compra, márgenes internos ni acuerdos con proveedores.

---

## 8. Permisos

Claves mínimas:

```text
procurement.supplier-products.view
procurement.supplier-products.create
procurement.supplier-products.edit
procurement.supplier-products.deactivate
procurement.supplier-products.change-primary
procurement.supplier-costs.view
procurement.supplier-costs.view-sensitive
procurement.supplier-costs.create-draft
procurement.supplier-costs.submit
procurement.supplier-costs.approve
procurement.supplier-costs.reject
procurement.supplier-costs.bulk-update
procurement.supplier-costs.import
procurement.supplier-costs.export
procurement.supplier-costs.view-history
procurement.supplier-costs.reassign
```

La distinción `view`/`view-sensitive` permite que ciertos usuarios conozcan asociaciones sin ver costos.

Los permisos se limitan por negocio y sucursal. La UI oculta o deshabilita controles, y la API vuelve a autorizar cada comando y consulta.

---

## 9. Auditoría y eventos

Toda modificación registra:

- usuario;
- fecha/hora;
- instalación;
- negocio y alcance;
- producto y proveedor;
- valores anteriores y nuevos;
- razón;
- origen;
- lote o documento relacionado;
- aprobación;
- `CorrelationId`.

Eventos:

```text
SupplierProductAssociated
SupplierProductDeactivated
PrimarySupplierChanged
SupplierCostDrafted
SupplierCostSubmitted
SupplierCostApproved
SupplierCostActivated
SupplierCostRejected
SupplierCostExpired
SupplierCostVarianceDetected
```

La outbox garantiza publicación posterior al commit. Los consumidores deben ser idempotentes.

---

## 10. Reportes mínimos

- productos por proveedor;
- proveedores por producto;
- productos sin proveedor;
- productos sin proveedor principal;
- productos sin costo vigente;
- costos vigentes por negocio/sucursal;
- historial de costos;
- variación de costo por período;
- última compra frente a costo negociado;
- costo promedio frente a última compra;
- productos con cambios pendientes;
- margen estimado por canal de precio;
- cambios aprobados por usuario;
- asociaciones inactivadas o reasignadas.

Los reportes respetan permiso de costo sensible y alcance.

---

## 11. Migración desde Xion

Mapeo inicial:

| Xion | Auraly |
|---|---|
| `ProductoId` | `ProductId` mediante mapa legado |
| `ProveedorId` | `SupplierId`/`PartyId` mediante mapa |
| `SucursalId` | alcance de sucursal |
| `Principal` | `SupplierProduct.IsPrimary` |
| `PrecioCosto` | `BaseUnitCost` |
| `Dc1...Dc5` | ajustes porcentuales ordenados y no nulos |
| `PrecioCostoConDescuento` | proyección validada |
| `PrecioCostoNeto` | valor legado de referencia para conciliación |
| `EntradaId` | referencia al documento legado si existe |
| `Pendiente` | borrador o pendiente de revisión |
| `FechaActualizacion` | fecha de versión |

No se migran como componentes activos:

- flete;
- descargue;
- retenciones;
- descuentos financieros sin regla vigente comprobada;
- filas duplicadas;
- relaciones con producto, proveedor o sucursal inexistentes.

Proceso:

1. normalizar productos, proveedores y sucursales;
2. detectar duplicados;
3. identificar principal por producto y alcance;
4. resolver conflictos con informe;
5. convertir descuentos comerciales;
6. recalcular con `decimal`;
7. comparar contra valores Xion dentro de tolerancia;
8. importar acuerdos vigentes;
9. reconstruir historial desde auditorías y entradas cuando sea confiable;
10. conservar el valor legado para conciliación, no como fuente futura.

Si Xion tiene más de un principal para el mismo alcance, no se elige silenciosamente. Se genera una excepción de migración.

---

## 12. Pruebas obligatorias

### Dominio

- asociación única;
- proveedor principal único;
- descuentos sucesivos;
- precisión `decimal`;
- vigencias sin solapamiento;
- transición de estados;
- resolución de alcance;
- desactivación sin pérdida histórica.

### Aplicación

- permisos por acción y alcance;
- creación y aprobación;
- concurrencia con `RowVersion`;
- actualización masiva;
- reasignación parcial;
- idempotencia;
- eventos y outbox;
- importación con errores.

### Integración SQL

- constraints de unicidad;
- transacción al cambiar principal;
- publicación de versión;
- consultas por proveedor/producto;
- DACPAC limpio y actualización;
- rendimiento de lotes.

### Integración con entradas

- precarga de costo;
- búsqueda por código del proveedor;
- variación;
- snapshot inmutable;
- actualización de última compra;
- propuesta de costo;
- actualización de inventario y CxP una sola vez.

### Integración con Pricing/POS

- propuesta de precio;
- aprobación;
- margen mínimo;
- delta solo de productos afectados;
- costo nunca enviado al POS.

### Migración

- equivalencia de descuentos;
- conciliación de costos;
- duplicados;
- múltiples principales;
- referencias huérfanas;
- repetición segura del proceso.

### E2E web

- navegación por teclado;
- edición y recálculo;
- filtros;
- borrador y publicación;
- acción deshabilitada sin permiso;
- acceso API rechazado;
- importación;
- historial;
- reasignación.

---

## 13. Criterios de aceptación

El módulo está listo cuando:

- un producto admite principal y alternos sin duplicidad;
- el usuario consulta la relación desde producto y proveedor;
- los costos tienen versión e historial;
- el costo efectivo se calcula con precisión;
- los cambios masivos muestran impacto antes de publicar;
- una entrada conserva su propio costo;
- última compra y costo promedio no se confunden;
- Pricing recibe eventos sin acoplamiento;
- el POS nunca recibe información sensible;
- todos los permisos se aplican en UI y API;
- migración y conciliación con Xion producen informe;
- las pruebas obligatorias pasan en Cloud y On-Premise.

---

## 14. Decisión final

Productos–Proveedores–Costos es parte del MVP porque conecta la ficha de producto con proveedores, entradas, inventario, cuentas por pagar, utilidad y precios.

La referencia funcional de Xion se conserva:

- principal y alternos;
- costos por alcance;
- descuentos;
- actualización masiva;
- reasignación;
- historial;
- permisos;
- integración con entradas.

Auraly lo mejora mediante:

- entidades con responsabilidades separadas;
- versiones inmutables;
- `decimal`;
- alcances heredables;
- aprobación;
- snapshots;
- auditoría;
- eventos;
- imports seguros;
- pruebas completas;
- separación entre costo negociado, costo de compra, valoración y precio de venta.
