# Decisión prevalente: Auraly sin componentes legacy

**Estado:** obligatoria para diseño, migración e implementación  
**Prioridad:** máxima; reemplaza cualquier recomendación anterior de conservar
compatibilidad permanente con nombres, tablas, contratos o componentes heredados.

---

## 1. Decisión

La versión liberada de Auraly no contendrá componentes legacy.

Xion, Pedidos OK, Xion Web, Talkio y Auraly se usan únicamente para:

- entender reglas del negocio;
- contrastar comportamientos;
- extraer datos durante una migración;
- construir pruebas de equivalencia.

No forman parte del modelo, código desplegado, contratos ni interfaz de Auraly.

```text
conocimiento anterior
       |
       v
rediseño y transformación
       |
       v
modelo canónico Auraly
```

No se implementa:

```text
copiar -> envolver -> conservar indefinidamente
```

---

## 2. Qué significa “cero legacy”

En la solución liberada no pueden quedar:

- nombres de solución, proyectos o namespaces anteriores;
- entidades, enums, propiedades o métodos con nomenclatura anterior;
- tablas, columnas, vistas, sinónimos o procedimientos de compatibilidad;
- endpoints o rutas con nombres anteriores;
- DTO y eventos duplicados “viejo/nuevo”;
- adaptadores de Xion dentro de la API productiva;
- doble escritura al modelo anterior y al nuevo;
- flags como `UseLegacyFlow`, `OldMode` o `CompatibilityMode`;
- pantallas WinForms embebidas;
- reportes ejecutando directamente consultas del esquema anterior;
- IDs anteriores usados como PK o FK de Auraly;
- instaladores, servicios, logs o carpetas con marcas anteriores;
- comentarios que ordenen volver al comportamiento anterior sin especificarlo;
- columnas JSON para guardar indiscriminadamente la fila antigua.

El historial de Git puede demostrar el origen. No se necesita conservarlo dentro
del runtime.

---

## 3. Excepciones que no son legacy

No se consideran componentes legacy:

### Datos históricos transformados

Una factura antigua puede conservar:

```text
número original
fecha original
cliente
totales
estado fiscal
referencia documental
```

porque son datos del negocio, no estructura técnica anterior.

### Identificadores externos activos

Una integración vigente puede requerir:

```text
ExternalSystem
ExternalEntityId
ExternalDocumentNumber
```

Estos nombres son genéricos y pertenecen al contrato activo. No se crean columnas
`XionId`, `PedidoOkId` o equivalentes.

### Evidencia de importación

Se conserva:

```text
ImportBatchId
SourceFingerprint
ImportedAtUtc
ImportReport
```

en un registro genérico de auditoría. No se conserva el esquema fuente dentro del
dominio.

### Documentación de análisis

Los ADR pueden mencionar Xion para explicar el hallazgo y la decisión. Eso no
autoriza usar esos nombres en implementación.

---

## 4. Migración sin contaminar producción

La migración ocurre en un proceso separado:

```text
Auraly.DataImport
  Extract
  Normalize
  Validate
  Transform
  Load
  Reconcile
```

No se denomina:

```text
XionMigration
LegacyImporter
OldDatabaseAdapter
```

El origen se selecciona por configuración:

```text
SourceProfile
SourceConnection
SourceEntity
```

El adaptador específico del origen vive en el paquete de importación, no en:

```text
Auraly.Api
Auraly.Domain.*
Auraly.Application.*
Auraly.Infrastructure.*
Auraly.Desktop
Auraly.Pos.Edge
```

Al cerrar la migración:

1. se concilian cantidades, saldos y documentos;
2. se exporta el informe firmado;
3. se conserva el respaldo fuente según política;
4. se retiran credenciales y conectores de importación;
5. se elimina el staging;
6. el paquete específico no se despliega en producción.

---

## 5. Mapeos temporales

Documentos anteriores propusieron:

```text
LegacyEntityMappings
LegacyId
```

Esos nombres y esa tabla permanente quedan revocados.

Durante una importación reanudable se utiliza staging:

```text
ImportEntityMappings
  ImportBatchId
  SourceEntityType
  SourceBusinessScope
  SourceEntityId
  TargetEntityId
  ImportedAtUtc
```

Características:

- vive en la base o almacenamiento temporal de importación;
- no pertenece a agregados Auraly;
- no se consulta en operación normal;
- permite idempotencia y resolución de relaciones;
- se exporta al informe técnico si se necesita trazabilidad;
- se elimina después de aceptación y periodo de seguridad.

Si una integración continúa operando después del corte, su relación se mueve a
`ExternalIdentifiers`, porque ya no es un mapeo de migración.

---

## 6. Producto e IDs

El producto anterior:

```text
ProductoId = 10000
```

se transforma en:

```text
ProductId = UUIDv7
ProductCode = 10000      si el código tiene valor comercial
```

No se agrega `LegacyProductId`.

Si 10000 era solamente una PK sin significado para el usuario:

- se usa durante staging para enlazar registros;
- no se carga en `Products`;
- desaparece al retirar el staging.

La misma regla se aplica a documentos, terceros, bodegas y movimientos.

---

## 7. Base de datos

La base desplegada contiene únicamente objetos canónicos:

```text
Products
ProductBarcodes
Warehouses
CashRegisters
SalesInvoices
GoodsReceipts
ManualInventoryMovements
Dispatches
DispatchVerificationEvents
```

No contiene:

```text
Producto
EntradaDeMercancia
EnSa
CargueDeMercancia
Aduana
SalidaDeMercancia
Z*
S*
Sl*
vwLegacy*
spCompatibility*
```

### Estrategia de corte

Para un módulo:

1. crear modelo canónico;
2. transformar datos en ambiente controlado;
3. ejecutar pruebas y conciliación;
4. cambiar todos los consumidores;
5. retirar objetos y código anteriores;
6. desplegar el módulo completo.

No se libera un módulo “terminado” mientras dependa de una tabla anterior.

### Proyecto SQL

`Auraly.Database.sqlproj` es la fuente de verdad. Sus scripts de despliegue pueden
transformar datos, pero el modelo final del DACPAC no conserva objetos anteriores.

Los scripts históricos que sean necesarios para reproducibilidad se aíslan fuera
de las carpetas de objetos desplegables y no forman parte del artefacto final.

---

## 8. Código

Solo se crean proyectos:

```text
Auraly.*
```

La estructura modular definida se mantiene:

```text
Auraly.Domain.Catalog
Auraly.Application.Catalog
Auraly.Infrastructure.Catalog
Auraly.Contracts.Catalog
```

No se permiten aliases:

```csharp
using Producto = Auraly.Domain.Catalog.Product;
```

ni fachadas con nombres anteriores.

Cuando una regla útil se absorbe:

1. se expresa con nombre canónico;
2. se implementa en el módulo dueño;
3. se cubre con pruebas nuevas;
4. se elimina la dependencia del código fuente.

---

## 9. API y contratos

Las rutas comienzan desde la taxonomía nueva:

```text
/api/products
/api/goods-receipts
/api/inventory-movements
/api/dispatches
/api/dispatch-verifications
```

No se crean redirecciones permanentes para rutas anteriores porque Auraly es un
producto nuevo.

Los eventos usan:

```text
SalesInvoiceConfirmed
GoodsReceiptPosted
InventoryMovementPosted
DispatchVerified
```

No publican simultáneamente un evento anterior.

La versión de contratos se usa para evolución propia de Auraly, no para esconder
un contrato de Xion.

---

## 10. Interfaz

Menú:

```text
Productos
Entradas de mercancía
Movimientos de inventario
Despachos
Verificación de despacho
Arqueo de caja
Promociones
```

No se muestran aliases anteriores ni siquiera como texto secundario después del
corte.

Durante capacitación se puede entregar una guía externa:

```text
“La función que conocías como Aduana ahora se llama Verificación de despacho”
```

La guía no se convierte en etiqueta permanente de la aplicación.

---

## 11. Reportes

Los reportes se rediseñan sobre:

- entidades canónicas;
- proyecciones canónicas;
- fechas y estados Auraly;
- permisos Auraly.

No se copian consultas que dependan de tablas anteriores.

Para validar equivalencia se pueden ejecutar ambos reportes fuera de producción y
comparar:

```text
ventas
compras
impuestos
costos
utilidad
cartera
inventario
```

La consulta anterior se elimina al aprobar la conciliación.

---

## 12. Despliegues existentes de Auraly

La solución actual todavía contiene nombres Talkio/Mimos. La migración a la nueva
solución debe ser completa:

- `.sln`;
- `.csproj`;
- namespaces;
- assemblies;
- carpetas;
- configuración;
- recursos Azure;
- proyectos SQL;
- frontend;
- pruebas;
- pipelines;
- observabilidad;
- instalador Desktop;
- servicio POS Edge.

No se mantiene una solución puente con proyectos mezclados en la rama que se
libere.

El cambio puede realizarse por commits internos, pero el criterio de salida es
cero referencias desplegables.

---

## 13. Estrategia de entrega

“Cero legacy” no obliga a implementar todo en un solo commit. Obliga a que cada
vertical liberada quede limpia.

Secuencia segura:

```text
rama de implementación
  -> modelo canónico
  -> importador temporal
  -> pruebas/conciliación
  -> cambio de consumidores
  -> eliminación anterior
  -> release
```

No se hace un rollout prolongado con escritura doble.

Si un módulo no puede completar su corte, no se anuncia como migrado ni se mezcla
parcialmente con el producto nuevo.

---

## 14. Control automático

CI analiza código y artefactos desplegables buscando:

```text
Talkio
Mimos
Auraly
Xion
PedidosOK
PedidoOk
EnSa
Aduana
CargueDeMercancia
SalidaDeMercancia
Legacy
OldFlow
Compatibility
```

Ámbitos:

- nombres de archivo y carpeta;
- solución/proyectos;
- namespaces;
- SQL desplegable;
- rutas;
- JSON de configuración;
- frontend;
- instaladores;
- nombres de servicios;
- imágenes y manifiestos;
- assembly metadata.

Exclusiones explícitas:

- documentación de análisis;
- pruebas del importador;
- artefactos temporales de importación no desplegables;
- historial Git.

Toda exclusión tiene propietario y fecha de retiro.

Además, el pipeline inspecciona:

- contenido del DACPAC;
- artefacto frontend;
- paquete Desktop;
- imagen/container;
- manifiestos de infraestructura.

No basta con que el texto desaparezca del código fuente.

---

## 15. Pruebas de corte

### Datos

- conteo por empresa y entidad;
- totales monetarios;
- saldos de inventario;
- cuentas por cobrar/pagar;
- impuestos;
- documentos huérfanos;
- duplicados;
- barcodes;
- usuarios y permisos;
- numeración fiscal.

### Funcional

- cada flujo del MVP funciona solo con el modelo canónico;
- desconectar/eliminar staging no rompe la aplicación;
- retirar acceso al origen no rompe reportes;
- no hay consultas a objetos anteriores;
- no hay fallback silencioso.

### Despliegue

- base limpia desde cero;
- actualización desde la versión anterior soportada;
- instalación cloud;
- instalación on-premise;
- Auraly Desktop;
- POS offline;
- restore y recuperación.

### Búsqueda cero-legacy

El release falla si un artefacto productivo contiene un término prohibido.

---

## 16. Definition of Done

Un módulo absorbido desde Xion está terminado solamente cuando:

- comportamiento necesario está documentado;
- modelo canónico está implementado;
- datos están transformados;
- resultados están conciliados;
- UI fue rediseñada;
- permisos funcionan;
- pruebas pasan;
- código anterior no se compila ni despliega;
- objetos anteriores no existen en el DACPAC final;
- importador específico fue retirado del release;
- búsqueda CI no encuentra nombres prohibidos;
- operación no depende del origen.

“Funciona mediante el adaptador anterior” no es terminado.

---

## 17. Decisión final

Auraly absorbe conocimiento, no componentes anteriores.

La migración puede utilizar staging y herramientas temporales para ser segura e
idempotente, pero esos elementos no forman parte del producto liberado.

El resultado final tendrá:

- nombres Auraly;
- arquitectura Auraly;
- entidades Auraly;
- datos transformados;
- pruebas Auraly;
- cero dependencias, compatibilidad o nomenclatura legacy.
