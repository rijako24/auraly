# Conteo y conciliación unificada de inventario

## Decisión

El inventario físico se divide en dos experiencias relacionadas, pero separadas:

1. `Conteo` permite buscar productos, capturar, aplicar de inmediato o guardar y recuperar borradores.
2. `Conciliación de inventario` permite elegir borradores listos, agruparlos y
   guardar o aplicar el resultado.

No se muestran listas ni equipos. El término visible y persistente es
`borrador`. La conciliación se abre con un botón junto a `Nueva operación` y
ocurre completa dentro de un solo diálogo, sin diálogos anidados.

La conciliación no crea otro motor de inventario. Cada aplicación genera un
documento canónico `StockCount`; el motor ordenado conserva la autoridad única
sobre saldos, kárdex, costos, numeración y cierre transaccional.

## Conteo y borradores

La pantalla de conteo pide bodega, motivo y observaciones, y muestra de inmediato
el buscador canónico de productos y la grilla. No existe un paso previo para
agregar el alcance ni un campo permanente con el nombre del borrador. Cada
producto elegido entra a la grilla, el foco pasa a `Contar` y, al confirmar la
cantidad con Enter, vuelve al buscador para capturar el siguiente producto.

`Aplicar inventario` genera directamente un documento canónico `StockCount`.
`Guardar borrador` abre entonces —y sólo entonces— la solicitud del nombre, crea
un inventario de alcance general y conserva en segundo plano todos los productos
inventariables de la bodega. Así la captura sigue siendo ágil y la conciliación
posterior puede identificar correctamente los productos no contados.

Cada borrador pertenece a un usuario, tiene nombre, versión y estas cantidades:

- `Contar`: obligatorio para considerar contado un producto.
- `Recontar`: etapa opcional; al iniciarla se bloquea `Contar`, se activa sólo
  la columna de reconteo y esa cantidad pasa a ser la final del borrador.
- `Motivo pendiente`: opcional cuando todavía no existe conteo inicial.

Guardar exige al menos un producto contado. En la etapa `Contar` deja el
borrador listo para conciliar; si se inició `Recontar`, puede guardarse todavía
incompleto como `En progreso` y sólo queda listo cuando todos los productos
contados tienen su segunda lectura. Los demás productos del alcance general
permanecen pendientes sin obligar al usuario a verlos en la grilla de captura.
La edición usa versión optimista y sólo la puede hacer su propietario. Un
borrador seleccionado por una conciliación activa queda inmutable.

La vista de conteo es lineal y conserva su etapa en el servidor. Inicialmente
sólo `Contar` está habilitado. La acción `Recontar` inicia la segunda lectura,
enfoca el primer producto sin reconteo y deshabilita la primera columna. Al
reabrir, el borrador restaura `Contar` o `Recontar` y enfoca el primer campo sin
valor; si el conteo visible está completo, el foco vuelve al buscador.

## Recuperación de borradores

`Operaciones > Inventarios` separa `Documentos de inventario` y `Borradores` en
dos pestañas. La pestaña de borradores muestra:

- nombre del borrador;
- inventario y bodega;
- propietario;
- productos contados sobre productos incluidos;
- última edición;
- estado;
- acción `Editar`.

`Editar` abre la misma ventana de `Nueva operación` con la grilla y los valores
del borrador cargados, incluidos sus reconteos y su etapa actual. No abre la
conciliación; el usuario puede guardar otra vez, iniciar/completar el reconteo,
aplicar el inventario o cerrar.
Los documentos `StockCount` aplicados permanecen exclusivamente en la pestaña
de documentos; no se apilan debajo de los borradores.

## Conciliación de inventario

La acción superior `Conciliación de inventario` abre un solo diálogo con dos
pasos.

### 1. Selección

El diálogo muestra una grilla paginada de borradores abiertos, con filtros por
nombre/producto y rango de última actualización, además de bodega, avance,
propietario y estado. Sólo los marcados `Listo para conciliar`
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
suman. La misma fila muestra como fuentes visuales todos los borradores que
aportaron al producto, por ejemplo:

`Borrador A: 3 + Borrador B: 5 = 8`.

Un producto queda en `No contados` cuando es un producto inventariable activo
del negocio y ningún borrador seleccionado aporta conteo inicial. Esta consulta
se hace contra todo el catálogo inventariable de la base de datos, sin limitarse
al alcance con el que se creó el conteo ni a los productos que ya tenían saldo o
líneas de captura. Los productos no contados se presentan con cantidad propuesta
`0`.

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
2. La captura abre directamente buscador y grilla, sin selección previa de productos.
3. `Contar` es obligatorio; `Recontar` es una etapa opcional y exclusiva que se restaura al reabrir.
4. El nombre sólo se solicita al ejecutar `Guardar borrador`.
5. Un borrador aparece en `Operaciones > Inventarios > Borradores`, se puede editar en la misma ventana de operación y conserva foco, conteos y reconteos.
6. `Aplicar inventario` produce un `StockCount` idempotente sin exigir guardar antes.
7. Documentos y borradores se muestran en pestañas separadas.
8. La conciliación está junto a `Nueva operación` y usa un solo diálogo.
9. La selección filtra por nombre y rango de fecha y se pagina en servidor.
10. Se pueden seleccionar borradores de cualquier usuario de la misma sesión.
11. Un producto repetido suma la cantidad final de todos los borradores elegidos y muestra cada borrador de origen.
12. El resultado sólo tiene `Contados` y `No contados`.
13. Guardar Contados crea un borrador poblado con cantidades consolidadas.
14. No contados incluye todos los productos inventariables activos del negocio
    que no fueron contados, aunque no pertenecieran al alcance inicial, y los
    presenta con cantidad propuesta cero.
15. Guardar No contados crea un borrador poblado con productos pendientes.
16. Aplicar Contados produce un `StockCount` normal para esa sección.
17. Aplicar No contados produce un `StockCount` normal con cantidades cero y exige confirmación explícita.
18. Precio costo y costo promedio valorizan ambas pestañas.
19. Los movimientos posteriores a la captura no se pierden al aplicar.
20. El motor ordenado conserva autoridad exclusiva sobre saldos y kárdex.

## Referencia Xion

Xion separa `Guardar`, `Pendientes`, `Reconteo` y `Consolidar Inventario`; guarda
capturas temporales y su consolidado suma cantidades por producto. Se conserva
esa intención operativa, con mejoras explícitas: borradores nombrados por
usuario, versiones optimistas, selección visible, dos resultados simples,
valorización y aplicación mediante el documento canónico de Auraly.
