# Decisión: módulo Conversión de productos

**Estado:** incluido en el MVP de Auraly Commerce  
**Fecha:** 27 de julio de 2026  
**Referencia:** entidades, formularios, servicios, motor, kardex, asociaciones e informes de Conversión de Xion  
**Prevalencia:** este documento reemplaza cualquier exclusión anterior de “conversiones” en el alcance del MVP. Producción continúa fuera del MVP.

---

## 1. Decisión ejecutiva

Auraly Commerce incluirá un módulo de **Conversión de productos**.

El módulo permitirá transformar existencias dentro de una misma bodega mediante:

```text
Uno a muchos
    un producto de salida
    -> varios productos resultantes

Muchos a uno
    varios productos de salida
    -> un producto resultante
```

Ejemplos:

- abrir una presentación mayor y obtener presentaciones menores;
- agrupar varias unidades en una presentación comercial;
- reclasificar productos equivalentes;
- convertir referencias asociadas conservando cantidades y costo;
- registrar rendimiento o merma autorizada.

Conversión será un documento de inventario procesado por el motor del servidor. Produce:

- salidas de inventario;
- entradas de inventario;
- kardex;
- snapshots de costo;
- valoración de productos resultantes;
- trazabilidad;
- eventos;
- reportes.

No produce:

- venta;
- compra;
- cuenta por cobrar;
- cuenta por pagar;
- factura electrónica;
- orden de producción;
- consumo de mano de obra;
- programación de planta;
- lotes ni seriales en el MVP.

---

## 2. Hallazgos de Xion

### 2.1 Encabezado

`Conversion` contiene:

- número;
- fecha;
- bodega;
- centro de costo;
- motivo;
- tipo uno-a-muchos o muchos-a-uno;
- observación;
- usuario;
- equipo;
- estado procesado.

### 2.2 Detalles

Xion separa:

```text
ConversionSalida
ConversionEntrada
```

Las líneas conservan:

- producto;
- código;
- descripción;
- unidad;
- embalaje;
- cajas;
- unidades;
- cantidad en unidad principal;
- precio de conversión;
- costo;
- costo promedio;
- precio de venta;
- total.

### 2.3 Asociaciones

Antes de convertir, el producto debe pertenecer a un `AsociadoProducto` con:

```text
PermiteConversion = true
```

La asociación:

- agrupa productos compatibles;
- define un principal;
- guarda cantidad;
- exige familia y tipo de unidad compatibles;
- impide algunos tipos especiales;
- permite obtener automáticamente los posibles resultados.

### 2.4 Operación

La pantalla:

- trabaja por bodega;
- captura productos por código;
- soporta teclado;
- permite uno-a-muchos y muchos-a-uno;
- muestra existencias según permiso;
- muestra costos según permiso;
- calcula cantidades y totales;
- guarda un borrador local;
- envía el documento;
- deja el movimiento pendiente para el motor;
- permite buscar e imprimir conversiones.

### 2.5 Motor

El motor:

- toma productos de salida;
- descuenta existencia;
- crea kardex de salida;
- toma productos de entrada;
- incrementa existencia;
- crea kardex de entrada;
- marca la conversión procesada;
- marca el movimiento terminado;
- ejecuta dentro de persistencia coordinada.

### 2.6 Problemas que no se deben migrar

- identificadores enteros compuestos manualmente;
- `double` para cantidades y costos;
- copia de descripciones, códigos y precios como campos operativos mezclados;
- duplicación servidor/local;
- borradores temporales en tablas `Z` y `S`;
- lógica de UI como fuente de verdad;
- consultas y autorización desde el formulario;
- mezcla entre unidad capturada, unidad base y embalaje;
- dependencia de una única asociación por producto;
- costo resultante ambiguo;
- validación de pérdidas defectuosa;
- estado booleano `Procesada`;
- ausencia de reversión explícita;
- eliminación/modificación de líneas sin modelo de estados.

---

## 3. Hallazgo crítico: validación de pérdida defectuosa

Xion intenta:

1. impedir que la entrada equivalente sea mayor que la salida;
2. permitir una entrada menor con autorización;
3. limitar el porcentaje de pérdida.

Sin embargo, después de rechazar el caso mayor, aparece una condición equivalente a:

```text
si entrada NO es mayor que salida:
    continuar
```

Esto hace inalcanzable la validación posterior de pérdida.

Auraly no debe reproducir ese comportamiento.

Las reglas de:

- rendimiento;
- merma;
- tolerancia;
- autorización;
- costo;

serán invariantes de dominio y se probarán independientemente de la interfaz.

---

## 4. Conversión no es Producción

### Conversión

- transformación logística/comercial;
- sin orden de fabricación;
- sin receta industrial;
- sin etapas;
- sin mano de obra;
- sin máquinas;
- sin tiempos;
- sin planeación;
- sin consumo indirecto;
- inventario entra y sale en una operación atómica.

### Producción

- receta o BOM;
- orden;
- consumos planificados/reales;
- procesos;
- responsables;
- tiempos;
- costos indirectos;
- desperdicios de producción;
- producto en proceso.

Producción permanece fuera del MVP.

Si un caso requiere receta, ejecución parcial, producto en proceso o costos indirectos, no se fuerza dentro de Conversión. Se clasifica como necesidad futura de Producción.

---

## 5. Arquitectura modular

Conversión pertenece a Inventario:

```text
Auraly.Domain.Inventory.Conversions
Auraly.Application.Inventory.Conversions
Auraly.Infrastructure.Inventory.Conversions
Auraly.Contracts.Inventory.Conversions
```

No se crea inicialmente un microservicio ni base independiente.

Dependencias:

```text
Catalog
    -> productos, códigos, unidades y estado

Inventory
    -> bodega, existencia, kardex, valoración

Identity/Authorization
    -> usuario, permisos y alcances

ReferenceData
    -> motivos

Reporting
    -> consultas y reportes

DocumentEngine
    -> procesamiento definitivo
```

Conversión no modifica directamente tablas de otros módulos fuera de los puertos y servicios de aplicación establecidos.

---

## 6. Modelo conceptual

### 6.1 Documento

```text
InventoryConversion
-------------------
Id
BusinessId
DocumentNumber
BranchId
WarehouseId
OccurredAt
ConversionType
ReasonId
ResponsiblePartyId?
Notes?
Status
Source
CreatedAt
CreatedBy
SubmittedAt?
SubmittedBy?
ConfirmedAt?
ConfirmedBy?
CancelledAt?
CancelledBy?
ReversalOfId?
IdempotencyKey
RowVersion
```

`Id` usa la política nueva de documentos de Auraly. `DocumentNumber` es legible y separado.

### 6.2 Estados

```text
Draft
Submitted
Processing
Confirmed
Failed
Cancelled
Reversed
```

Reglas:

- `Draft` puede editarse;
- `Submitted` queda bloqueado;
- `Processing` pertenece al motor;
- `Confirmed` es inmutable;
- `Failed` conserva error y permite reintento idempotente;
- `Cancelled` solo aplica antes de confirmar;
- `Reversed` referencia un documento compensatorio.

No se usa un booleano `Processed`.

### 6.3 Tipo

```text
Split       // uno a muchos
Merge       // muchos a uno
```

Una transformación muchos-a-muchos queda fuera del MVP. Si aparece, se evalúa como producción o transformación avanzada.

### 6.4 Líneas

```text
InventoryConversionLine
-----------------------
Id
ConversionId
Direction
ProductId
CapturedUnitId
CapturedQuantity
BaseQuantity
UnitCostSnapshot
TotalCostSnapshot
AllocationWeight?
ExpectedBaseQuantity?
ActualBaseQuantity
VarianceBaseQuantity
Notes?
Sequence
```

Dirección:

```text
Input     // sale de inventario
Output    // entra a inventario
```

Los nombres evitan la ambigüedad de “entrada/salida” respecto al documento.

### 6.5 Snapshots

Al confirmar se conserva:

- código;
- descripción;
- unidad;
- factor;
- costo usado;
- moneda;
- fórmula;
- versión de valoración.

Los nombres visibles pueden proyectarse desde Catálogo, pero los documentos históricos deben conservar los valores necesarios para explicar el movimiento.

---

## 7. Perfiles de conversión

Xion usa asociaciones genéricas con `PermiteConversion`. Auraly lo mejora mediante un concepto explícito:

```text
ConversionProfile
-----------------
Id
BusinessId
Name
ConversionType
Status
DefaultWarehouseId?
LossPolicy
MaximumLossPercent
CostAllocationMethod
EffectiveFrom
EffectiveTo?
CreatedBy
CreatedAt
RowVersion
```

```text
ConversionProfileLine
---------------------
Id
ConversionProfileId
Direction
ProductId
BaseQuantity
AllocationWeight?
IsEditable
Sequence
```

Ventajas:

- un producto puede participar en más de un perfil;
- no se mezcla con otras asociaciones comerciales;
- el tipo es explícito;
- las cantidades esperadas están versionadas;
- la tolerancia es visible;
- la distribución del costo es reproducible;
- los perfiles tienen vigencia;
- se puede auditar.

### 7.1 Plantilla, no obligación

El perfil:

- precarga productos;
- propone cantidades;
- valida compatibilidad;
- define método de costo.

El negocio puede permitir una conversión ad hoc con permiso especial:

```text
inventory.conversions.create-ad-hoc
```

En el MVP es preferible exigir perfil para reducir errores.

### 7.2 Restricciones

- productos activos;
- productos inventariables;
- unidad base definida;
- al menos una línea por lado;
- `Split`: exactamente un input y uno o varios outputs;
- `Merge`: uno o varios inputs y exactamente un output;
- cantidades mayores que cero;
- sin duplicados en el mismo lado;
- mismas dimensiones de unidad cuando se exige conservación física;
- perfil vigente.

Lotes y seriales continúan fuera; por tanto un producto que los exija no puede participar en el MVP.

---

## 8. Cantidades, rendimiento y merma

### 8.1 Cantidad base

Toda cantidad se convierte a unidad base:

```text
BaseQuantity = CapturedQuantity * UnitConversionFactor
```

El documento conserva ambas.

Los cálculos usan `decimal`.

### 8.2 Rendimiento

```text
YieldPercent =
    ActualEquivalentOutput
    / ExpectedEquivalentOutput
    * 100
```

### 8.3 Merma

```text
LossQuantity =
    ExpectedEquivalentOutput
    - ActualEquivalentOutput

LossPercent =
    LossQuantity
    / ExpectedEquivalentOutput
    * 100
```

No todos los perfiles permiten comparar cantidades directamente. Para productos con magnitudes diferentes, el rendimiento se define mediante la cantidad esperada del perfil.

### 8.4 Políticas

```text
NoLoss
AllowWithinTolerance
RequireApproval
```

#### `NoLoss`

La salida equivalente debe coincidir exactamente dentro de precisión técnica.

#### `AllowWithinTolerance`

La diferencia debe ser menor o igual a `MaximumLossPercent`.

#### `RequireApproval`

La diferencia puede superar el límite únicamente con:

- permiso;
- aprobador distinto cuando se configure;
- motivo;
- comentario;
- auditoría.

No existe una continuación silenciosa.

### 8.5 Ganancia aparente

Una cantidad resultante mayor que la esperada:

- se bloquea por defecto;
- no se acepta como “merma negativa”;
- exige corregir factores o cantidades;
- una excepción futura requiere caso de negocio explícito.

---

## 9. Existencia y negativos

La política:

```text
Warehouse.AllowNegativeStockSales
```

solo gobierna ventas.

Conversión:

- consulta existencia en línea;
- valida al capturar/cambiar cantidades;
- revalida al confirmar;
- bloquea si no hay disponibilidad;
- no vende a un cliente presente;
- no opera offline;
- no usa inventario local de la caja;
- no permite negativos en el MVP.

La validación definitiva ocurre dentro de la misma transacción o control de concurrencia que aplica el movimiento.

Si dos conversiones consumen la misma existencia:

- solo puede confirmarse la que conserve disponibilidad;
- la otra queda `Failed` con error recuperable;
- no deja efectos parciales.

---

## 10. Conservación y distribución del costo

### 10.1 Fuente del costo

Los inputs usan el costo de valoración que Inventory determine al confirmar:

- costo promedio;
- capa de costo futura;
- otra política configurada.

No usan precio público ni costo digitado arbitrariamente.

```text
InputTotalCost =
    sum(InputBaseQuantity * InventoryUnitCost)
```

### 10.2 Invariante

```text
InputTotalCost
= OutputAllocatedCost
 + RecognizedConversionVariance
```

La diferencia permitida solo puede provenir de precisión monetaria o de una política de merma explícita.

### 10.3 Un solo output

```text
OutputTotalCost = InputTotalCost - RecognizedVariance

OutputUnitCost =
    OutputTotalCost / OutputBaseQuantity
```

### 10.4 Varios outputs

El perfil define el método:

```text
ByExpectedQuantity
ByAllocationWeight
```

#### Por cantidad esperada

Aplica cuando los productos representan presentaciones comparables:

```text
OutputShare =
    OutputExpectedBaseQuantity
    / Sum(ExpectedBaseQuantities)
```

#### Por peso de distribución

Cada output tiene un porcentaje/peso:

```text
AllocationWeight > 0
Sum(AllocationWeight) = 100 %
```

```text
OutputAllocatedCost =
    AllocatableCost * AllocationWeight
```

### 10.5 Merma y costo

Políticas:

```text
AbsorbLossIntoOutputs
RecordConversionVariance
```

#### Absorber

El costo de los inputs se distribuye completamente entre las unidades obtenidas. Al producir menos, aumenta el costo unitario del output.

#### Registrar variación

Una porción queda como pérdida de conversión identificada. No se implementa asiento contable completo si Contabilidad está fuera, pero sí:

- valor;
- motivo;
- autorización;
- reporte;
- evento para integración futura.

La política debe estar definida en el perfil, no escogerse informalmente al confirmar.

### 10.6 Precisión

- dinero con `decimal`;
- cantidades con `decimal`;
- precisión definida por unidad;
- residuo monetario asignado de forma determinística;
- fórmula versionada;
- snapshots inmutables;
- pruebas doradas.

---

## 11. Flujo web

### 11.1 Vista de consultas

Ruta:

```text
Inventario > Conversiones
```

Columnas:

- número;
- fecha;
- tipo;
- bodega;
- perfil;
- motivo;
- responsable;
- costo input;
- costo output;
- merma;
- estado;
- usuario;
- procesado en;
- acciones.

La vista cumple el estándar transversal:

- filtros por encabezado;
- filtros combinables;
- ordenamiento;
- paginación de servidor;
- permisos;
- vistas guardadas;
- exportación.

### 11.2 Nuevo documento

Encabezado:

- negocio;
- sucursal;
- bodega;
- fecha;
- tipo;
- perfil;
- motivo;
- responsable;
- observación.

Paneles:

```text
Productos a consumir
Productos resultantes
Resumen y validaciones
```

### 11.3 Captura

Cada grilla:

- recibe lector de código;
- busca por código de barras, alterno, referencia o nombre;
- agrega y deja listo el foco;
- permite cambiar cantidad;
- recalcula inmediatamente;
- permite eliminar línea antes de enviar;
- navega con Enter, Tab y flechas;
- muestra errores por línea;
- usa virtualización, no paginación de servidor;
- conserva el documento completo.

### 11.4 Perfil seleccionado

Al seleccionar perfil:

- precarga líneas;
- establece proporciones;
- muestra vigencia;
- marca campos editables;
- muestra tolerancia;
- muestra método de costo;
- no permite mezclar tipo incompatible.

### 11.5 Resumen

- cantidades esperadas;
- cantidades reales;
- rendimiento;
- merma;
- existencia disponible;
- costo de inputs;
- costo distribuido;
- variación;
- advertencias;
- aprobaciones necesarias.

Los costos solo aparecen con permiso.

### 11.6 Guardar borrador

Un borrador:

- se guarda en servidor;
- puede recuperarse;
- no mueve inventario;
- admite edición;
- tiene `RowVersion`;
- conserva usuario y fechas.

Conversión no requiere borrador offline.

### 11.7 Confirmar

Al confirmar:

1. valida permisos;
2. valida perfil y vigencia;
3. valida cantidades;
4. valida tolerancia/aprobación;
5. consulta existencias;
6. calcula costo;
7. muestra resumen final;
8. envía con `IdempotencyKey`;
9. bloquea edición;
10. el motor procesa.

---

## 12. Motor del servidor

El motor es la única autoridad para una conversión definitiva.

Procesamiento:

```text
Load Submitted conversion
    -> verify idempotency
    -> validate status
    -> validate permissions snapshot/approval
    -> validate products and warehouse
    -> lock/check input availability
    -> resolve inventory costs
    -> validate yield/loss
    -> allocate output costs
    -> create input kardex
    -> decrease inputs
    -> create output kardex
    -> increase outputs
    -> update valuation
    -> record variance
    -> mark Confirmed
    -> append audit/outbox
    -> commit
```

Todo ocurre atómicamente.

Si falla:

- no cambia inventario;
- no queda kardex parcial;
- no duplica movimientos;
- conserva error técnico sanitizado;
- permite reintento seguro.

### 12.1 Idempotencia

Claves:

```text
ConversionId
IdempotencyKey
ProcessingAttempt
```

Una solicitud repetida devuelve el resultado ya confirmado.

### 12.2 Orden de movimientos

Aunque se creen kardex de input y output, ambos pertenecen al mismo documento y transacción.

Si el mismo producto aparece en ambos lados:

- el caso debe estar permitido por el perfil;
- se registra trazabilidad de ambos movimientos;
- la existencia final aplica el neto;
- el costo sigue la regla completa;
- no se colapsa de forma que se pierda auditoría.

---

## 13. Cancelación y reversión

### Antes de confirmar

- se puede cancelar;
- no produce inventario;
- queda auditado.

### Después de confirmar

No se edita ni elimina.

Se crea:

```text
InventoryConversionReversal
```

Reglas:

- referencia la conversión original;
- por defecto revierte completa;
- valida que los productos resultantes sigan disponibles;
- crea kardex inverso;
- usa costos originales;
- es idempotente;
- requiere permiso y motivo;
- marca original `Reversed`.

La reversión parcial queda fuera del MVP.

---

## 14. Configuración

### 14.1 Motivos

Semillas editables:

- cambio de presentación;
- despiece comercial;
- agrupación;
- reclasificación;
- corrección autorizada;
- otro controlado.

Cada motivo define:

- estado;
- requiere observación;
- requiere aprobación;
- permite merma;
- límite.

### 14.2 Parámetros por negocio

```text
RequireConversionProfile
DefaultLossPolicy
DefaultMaximumLossPercent
DefaultCostAllocationMethod
RequireDifferentApproverAboveLossPercent
```

No se parametrizan reglas incompatibles con invariantes.

---

## 15. Permisos

```text
inventory.conversions.view
inventory.conversions.create
inventory.conversions.edit-draft
inventory.conversions.submit
inventory.conversions.confirm
inventory.conversions.cancel
inventory.conversions.reverse
inventory.conversions.view-stock
inventory.conversions.view-cost
inventory.conversions.allow-loss
inventory.conversions.approve-loss
inventory.conversions.create-ad-hoc
inventory.conversions.export
inventory.conversion-profiles.view
inventory.conversion-profiles.manage
```

Alcances:

- negocio;
- sucursal;
- bodega.

El usuario:

- no ve el menú sin permiso;
- ve acciones deshabilitadas cuando corresponda;
- no accede por URL;
- no recibe costos sin permiso;
- no puede aprobar su propia excepción si se exige separación.

La API y el motor vuelven a validar.

---

## 16. Eventos

```text
InventoryConversionDrafted
InventoryConversionSubmitted
InventoryConversionConfirmed
InventoryConversionFailed
InventoryConversionCancelled
InventoryConversionReversed
InventoryConversionLossApproved
ConversionProfileCreated
ConversionProfileChanged
ConversionProfileDeactivated
```

Consumidores:

- Reporting;
- alertas;
- auditoría;
- futuras integraciones contables;
- sincronización de consultas.

Las cajas no necesitan recibir conversiones ni inventario. Solo reciben deltas de producto/precio/configuración si otra regla los modifica.

---

## 17. Reportes

### Consultas

- conversiones por rango;
- por bodega;
- por tipo;
- por perfil;
- por motivo;
- por usuario;
- por estado;
- por producto input;
- por producto output;
- pendientes/fallidas;
- reversadas.

### Métricas

- cantidad consumida;
- cantidad resultante;
- rendimiento;
- merma;
- costo consumido;
- costo asignado;
- variación;
- conversiones con autorización;
- productos más convertidos.

### Trazabilidad

Desde una conversión:

- encabezado;
- inputs;
- outputs;
- kardex;
- existencias antes/después;
- costo;
- aprobaciones;
- intentos del motor;
- reversión.

Los reportes cumplen filtros por encabezado, combinación, ordenamiento, paginación, totales y exportación.

---

## 18. On-Premise

Conversión usa la misma implementación funcional.

Cloud:

- API;
- Azure SQL;
- motor/Worker;
- outbox.

On-Premise:

- IIS/API;
- SQL Server;
- `Auraly.Worker`;
- outbox SQL.

No depende de Azure.

Conversión requiere conexión con el servidor Auraly de la instalación. Si el equipo está sin conexión:

- puede conservar datos no enviados solo si se implementa un borrador local futuro;
- no confirma;
- no consulta inventario local;
- no genera movimientos.

Para el MVP, los borradores son online.

---

## 19. Migración desde Xion

### 19.1 Mapeo

| Xion | Auraly |
|---|---|
| `Conversion.ConversionId` | mapa legado + nuevo `Id` |
| `Fecha` | `OccurredAt` |
| `BodegaId` | `WarehouseId` |
| `CentroCostoId` | referencia opcional si aplica |
| `MotivoId` | `ReasonId` |
| `TipoConversion` | `Split` / `Merge` |
| `Procesada` | estado derivado |
| `ConversionSalida` | líneas `Input` |
| `ConversionEntrada` | líneas `Output` |
| `UnidadesPrincipal` | `BaseQuantity` |
| `PrecioConversion` | costo legado para conciliación |
| `Total` | total legado para conciliación |
| `AsociadoProducto.PermiteConversion` | `ConversionProfile` |
| `AsociadoProductoDetalle.Cantidad` | cantidad esperada |

### 19.2 Históricos confirmados

Se migran como documentos históricos inmutables:

- no vuelven a afectar inventario;
- enlazan al kardex legado;
- conservan número;
- conservan usuario/fecha;
- registran estado migrado;
- permiten consulta.

### 19.3 Pendientes

No se migran automáticamente como confirmables.

Se clasifican:

- descartado;
- convertido a borrador para revisión;
- resuelto en Xion antes del corte.

### 19.4 Perfiles

Las asociaciones se convierten en perfiles solo cuando:

- están activas;
- permiten conversión;
- sus productos existen;
- las cantidades son válidas;
- la unidad es compatible;
- la regla tiene sentido.

Conflictos generan informe, no decisiones silenciosas.

### 19.5 Conciliación

- cantidad input;
- cantidad output;
- bodega;
- kardex;
- costo legado;
- estado;
- usuario;
- fecha;
- asociación.

La validación defectuosa de pérdida no se conserva como regla.

---

## 20. Pruebas obligatorias

### 20.1 Dominio

- `Split` exige un input;
- `Merge` exige un output;
- cantidades positivas;
- unidad base;
- perfiles vigentes;
- productos válidos;
- rendimiento;
- merma exacta;
- tolerancia;
- aprobación;
- ganancia aparente;
- distribución de costo;
- residuo monetario;
- transiciones de estado;
- inmutabilidad.

### 20.2 Aplicación

- crear;
- recuperar borrador;
- editar con `RowVersion`;
- enviar;
- confirmar;
- cancelar;
- reintentar;
- revertir;
- permisos;
- alcances;
- idempotencia.

### 20.3 SQL real

- documento y líneas;
- constraints;
- concurrencia;
- índices;
- outbox;
- transacción;
- DACPAC limpio;
- actualización.

### 20.4 Inventario

- existencia suficiente;
- insuficiente;
- consumo concurrente;
- input/output mismo producto;
- producto no inventariable;
- kardex doble;
- antes/después;
- sin movimientos parciales;
- valoración;
- reversión.

### 20.5 Costos

- costo promedio input;
- output único;
- outputs múltiples;
- pesos;
- cantidad esperada;
- merma absorbida;
- variación;
- precisión;
- moneda;
- reversión al costo original.

### 20.6 UI/E2E

- lector de código;
- búsqueda;
- foco siguiente;
- edición de cantidad;
- recálculo;
- perfil;
- `Split`;
- `Merge`;
- error de existencia;
- error de merma;
- aprobación;
- costos ocultos;
- guardar/recuperar;
- confirmar;
- estado;
- filtros;
- paginación;
- informe.

### 20.7 Motor

- evento repetido;
- caída antes del commit;
- caída después del commit;
- reintento;
- bloqueo;
- error de costo;
- error de inventario;
- outbox;
- estado final.

### 20.8 Migración

- tipos;
- asociaciones;
- cantidades;
- costos;
- procesadas;
- pendientes;
- huérfanos;
- duplicados;
- conciliación;
- repetición segura.

### 20.9 Cloud/On-Premise

- misma suite funcional;
- Worker;
- SQL;
- permisos;
- rendimiento;
- recuperación.

---

## 21. Criterios de aceptación

Conversión está lista cuando:

- soporta `Split` y `Merge`;
- utiliza perfiles versionados;
- permite lector y teclado;
- recalcula cantidades y costo;
- valida inventario online;
- no permite negativos;
- controla merma;
- distribuye costo conservando valor;
- procesa atómicamente;
- crea kardex completo;
- no duplica por reintento;
- permite consulta paginada y filtrada;
- restringe existencias/costos por permiso;
- permite reversión completa;
- genera reportes;
- concilia históricos de Xion;
- pasa pruebas Cloud y On-Premise.

---

## 22. Alcance confirmado

### Incluido

- configuración de perfiles;
- uno-a-muchos;
- muchos-a-uno;
- misma bodega;
- lector y teclado;
- unidades y cantidad base;
- motivos;
- responsable;
- borrador online;
- validación de existencia;
- rendimiento y merma;
- costo y distribución;
- motor;
- kardex;
- reversión total;
- permisos;
- auditoría;
- reportes;
- migración.

### Fuera

- producción;
- BOM/recetas industriales;
- etapas;
- mano de obra;
- máquinas;
- producto en proceso;
- conversiones entre bodegas;
- muchos-a-muchos;
- lotes;
- seriales;
- vencimientos;
- conversión offline;
- reversión parcial;
- asientos contables completos.

---

## 23. Decisión final

Conversión debe entrar al MVP porque es una operación real de inventario que Xion ya resolvía mediante:

- asociaciones;
- uno-a-muchos;
- muchos-a-uno;
- captura por código;
- validación de existencia;
- costos;
- kardex;
- motor;
- informes.

Auraly absorberá ese conocimiento, pero lo mejorará con:

- perfiles explícitos;
- IDs nuevos;
- estados;
- `decimal`;
- cantidades base;
- control real de merma;
- conservación de costo;
- snapshots;
- atomicidad;
- idempotencia;
- reversión;
- permisos;
- reportes paginados;
- pruebas completas.

Conversión queda incluida y Producción continúa fuera del MVP.
