# Conteo y conciliación unificada de inventario

## Decisión

El inventario físico se divide en dos experiencias relacionadas, pero separadas:

1. `Conteo` permite abrir un alcance, capturar, guardar y recuperar borradores.
2. `Conciliación de inventario` permite elegir borradores listos, agruparlos y
   guardar o aplicar el resultado.

No se muestran listas ni equipos. El término visible y persistente es
`borrador`. La conciliación se abre con un botón junto a `Nueva operación` y
ocurre completa dentro de un solo diálogo, sin diálogos anidados.

La conciliación no crea otro motor de inventario. Cada aplicación genera un
documento canónico `StockCount`; el motor ordenado conserva la autoridad única
sobre saldos, kárdex, costos, numeración y cierre transaccional.

## Conteo y borradores

Al crear un inventario se define una sola bodega, un alcance general o parcial,
un motivo y el nombre del primer borrador. En alcance general el servidor incluye
automáticamente todos los productos activos que manejan inventario. En alcance
parcial se seleccionan los productos.

Cada borrador pertenece a un usuario, tiene nombre, versión y estas cantidades:

- `Conteo inicial`: obligatorio para considerar contado un producto.
- `Conteo de verificación`: opcional; si existe, es la cantidad final aportada
  por ese borrador.
- `Motivo pendiente`: opcional cuando todavía no existe conteo inicial.

El borrador se puede guardar incompleto en cualquier momento. `Listo para
conciliar` exige al menos un producto contado, pero permite conservar otros
productos pendientes. La edición usa versión optimista y sólo la puede hacer su
propietario. Un borrador seleccionado por una conciliación activa queda
inmutable; para continuar se crea otro borrador.

La vista de conteo es lineal. El conteo inicial siempre está presente y la
verificación se activa opcionalmente, sin pestañas de coordinación.

## Recuperación de borradores

`Operaciones > Inventarios` muestra una sección `Borradores de inventario` con:

- nombre del borrador;
- inventario y bodega;
- propietario;
- productos contados sobre productos incluidos;
- última edición;
- estado;
- acción `Continuar`.

`Continuar` carga directamente el borrador en Conteo. No abre la conciliación.
Los documentos `StockCount` aplicados continúan en el historial normal de
operaciones debajo de esta sección.

## Conciliación de inventario

La acción superior `Conciliación de inventario` abre un solo diálogo con dos
pasos.

### 1. Selección

El diálogo muestra de una vez todos los borradores abiertos, con su bodega,
alcance, avance, propietario y estado. Sólo los marcados `Listo para conciliar`
son elegibles. El primer borrador elegido fija el inventario; los borradores de
otros inventarios siguen visibles, pero quedan deshabilitados para evitar mezclar
bodegas o alcances. El administrador decide cuáles participan y pulsa
`Conciliar seleccionados`.

La selección guarda el identificador y versión de cada borrador. Una nueva
conciliación reemplaza la activa anterior. Nunca se mezclan bodegas, sesiones o
alcances.

### 2. Resultado agrupado

El resultado tiene únicamente dos pestañas:

- `Contados`;
- `No contados`.

No existe pestaña de conflictos. Para cada producto se usa la verificación del
borrador cuando existe y, en caso contrario, su conteo inicial. Si el producto
aparece en varios borradores seleccionados, todas esas cantidades finales se
suman. Se conservan las fuentes para mostrar el cálculo, por ejemplo:

`Borrador A: 3 + Borrador B: 5 = 8`.

Un producto queda en `No contados` cuando pertenece al alcance del inventario y
ningún borrador seleccionado aporta conteo inicial.

Ambas pestañas muestran existencia del sistema, diferencia y valorización. El
usuario puede cambiar entre `Precio costo` y `Costo promedio`; los costos sólo
se exponen con el permiso correspondiente.

## Acciones de Contados

`Guardar como borrador` crea un borrador nuevo, poblado con todos los productos
contados y la cantidad sumada como conteo inicial. Permite revisar o continuar
el resultado sin afectar existencias.

`Aplicar contados` genera y acepta un documento normal `StockCount` limitado a
esos productos. Los movimientos procesados después de la última captura del
producto se agregan una sola vez antes de confirmar la cantidad final, evitando
perder actividad ocurrida mientras se terminaba el inventario.

## Acciones de No contados

`Guardar como borrador` crea un borrador nuevo poblado con todos los productos
no contados y sus cantidades pendientes. No crea una lista vacía: preserva cada
producto para poder recuperar el trabajo y contarlo después.

`Aplicar todos en cero` genera y acepta un documento normal `StockCount` con
todos esos productos y cantidad contada `0`. La interfaz exige una confirmación
explícita que indica que las existencias se ajustarán a cero.

Las aplicaciones de `Contados` y `No contados` son independientes y pueden
producir documentos distintos. La sesión se cierra cuando todas las secciones
con productos de la conciliación activa fueron aplicadas. Si una sección se
guarda como borrador, el inventario permanece abierto para continuar.

## Estados

### Inventario físico

`Open -> Reconciling -> Closed`

También existe `Cancelled`. `Closed` sólo se alcanza desde el handler del motor
ordenado después de procesar todas las aplicaciones requeridas.

### Borrador

`InProgress -> Ready`

`Discarded` se reserva para anulación administrativa.

### Conciliación

`Active -> Applied`

Una conciliación activa anterior pasa a `Superseded` al seleccionar otra
combinación de borradores.

### Aplicación por sección

`null -> Processing -> Applied`

El identificador de documento es estable para reintentos idempotentes.

## Persistencia y propietarios

- `InventoryPhysicalCounts`: sesión, alcance y documento final de referencia.
- `InventoryPhysicalCountLists`: almacenamiento físico histórico de los
  borradores; el nombre técnico no se expone a contratos ni interfaz.
- `InventoryPhysicalCountLines`: conteo inicial, verificación, usuario, tiempo y
  secuencia por borrador y producto. La llave permite repetir el mismo producto
  en borradores diferentes.
- `InventoryPhysicalCountReconciliations`: selección, secuencia de snapshot,
  cantidad de productos por sección y documentos de aplicación.
- `InventoryPhysicalCountReconciliationDrafts`: versiones de borradores
  seleccionadas.

`InventoryPhysicalCountService` coordina el caso de uso y llama a
`InventoryOperationService`. `SqlInventoryPhysicalCountStore` es propietario de
los borradores y la conciliación. `SqlInventoryOperationDocumentHandler` sigue
siendo el único propietario de los cambios de saldo y completa la coordinación
después de procesar el `StockCount`.

## Seguridad y concurrencia

- `inventory.read`: consultar inventarios y borradores.
- `inventory.physical-counts.capture`: crear y editar borradores propios.
- `inventory.physical-counts.manage`: crear inventarios, seleccionar borradores,
  guardar resultados y aplicar conciliaciones.
- `inventory.counts.confirm`: aceptar las aplicaciones `StockCount`.
- `inventory.costs.read`: consultar precio costo, costo promedio y valorización.

Todas las consultas se limitan por `BusinessId` y tenant. La preparación de la
conciliación valida versiones; la aplicación reutiliza un identificador de
documento si se reintenta después de una respuesta perdida.

## Criterios de aceptación

1. Un inventario general incluye automáticamente todo producto inventariable.
2. El conteo inicial es obligatorio y la verificación es opcional.
3. Un borrador incompleto se guarda, aparece en `Operaciones > Inventarios` y se
   puede continuar.
4. La conciliación está junto a `Nueva operación` y usa un solo diálogo.
5. Se pueden seleccionar borradores de cualquier usuario de la misma sesión.
6. Un producto repetido suma la cantidad final de todos los borradores elegidos.
7. El resultado sólo tiene `Contados` y `No contados`.
8. Guardar Contados crea un borrador poblado con cantidades consolidadas.
9. Guardar No contados crea un borrador poblado con productos pendientes.
10. Aplicar Contados produce un `StockCount` normal para esa sección.
11. Aplicar No contados produce un `StockCount` normal con cantidades cero y
    exige confirmación explícita.
12. Precio costo y costo promedio valorizan ambas pestañas.
13. Los movimientos posteriores a la captura no se pierden al aplicar.
14. El motor ordenado conserva autoridad exclusiva sobre saldos y kárdex.

## Referencia Xion

Xion separa `Guardar`, `Pendientes`, `Reconteo` y `Consolidar Inventario`; guarda
capturas temporales y su consolidado suma cantidades por producto. Se conserva
esa intención operativa, con mejoras explícitas: borradores nombrados por
usuario, versiones optimistas, selección visible, dos resultados simples,
valorización y aplicación mediante el documento canónico de Auraly.
