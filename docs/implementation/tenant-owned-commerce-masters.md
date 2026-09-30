# Maestros comerciales del tenant y precios por sede

## Decisión

Un tenant tiene una sola identidad de cada producto y tercero. Cambiar de sede no
crea ni selecciona otra ficha. Las tablas de maestros usan `TenantId` como clave
de aislamiento y unicidad. La sede se selecciona en las operaciones y en
`ProductPrices`, donde se conserva una fila activa por `BusinessId + ProductId`.

La creación de una sede copia únicamente los precios iniciales de la sede origen
y aprovisiona sus recursos operativos, como bodegas y balances en cero. No copia
productos, códigos de barras, identificadores, categorías, marcas, unidades,
perfiles de IVA, relaciones producto-proveedor ni terceros.
Al crear un producto se inicializa una fila de precio en cada sede activa para
que la ficha pueda venderse y editarse desde cualquiera de ellas. Es una copia
inicial: las actualizaciones posteriores respetan la configuración de precios
compartidos o independientes.

## Propiedad de datos

| Propietario | Datos |
| --- | --- |
| Tenant | Products; categorías, marcas, unidades, IVA, códigos de barras, identificadores, enlaces, ofertas, imágenes y demás metadatos del catálogo; Parties y roles Customer, Supplier, Seller, Carrier y Employee; relación producto-proveedor y perfil tributario del tercero; definición de PriceChannels. |
| Sede | ProductPrices; inventario y bodegas; documentos, ventas, compras, rutas, cajas y sesiones; proyecciones y cursores de sincronización POS. |

`BusinessId` en un documento o proyección operativa indica dónde ocurrió la
operación; no cambia la propiedad del maestro referenciado. Un canal pertenece
al tenant y aplica automáticamente en todas sus sedes actuales y futuras.
Sus precios explícitos por producto también son comunes al tenant; las
estrategias calculadas usan el precio base de la sede donde ocurre la venta.
El precio base publicado para la venta se obtiene de `ProductPrices` usando
la sede seleccionada. Cada cambio de canal sincroniza la configuración POS de
todas las sedes activas del tenant.

## Invariantes

- Cada referencia de un producto a categoría, marca, IVA o proveedor debe
  pertenecer al mismo tenant. Se validará en la escritura y, donde corresponda,
  mediante claves compuestas en la base.
- La búsqueda, el detalle y la captura de ventas validan el tenant del producto
  y leen el precio publicado de la sede actual. Un producto sin precio publicado
  allí no es vendible en esa sede.
- El IVA y demás metadatos de una venta nueva se toman de la ficha compartida;
  las líneas ya preparadas o confirmadas conservan sus snapshots.
- Una modificación de catálogo que afecte POS emite cambios para cada sede
  activa del tenant, en conjunto, y despierta el despachador de esas sedes con
  una lectura acotada del outbox. Una publicación de precio emite cambios solo
  para las sedes cuyo precio cambió.
- Crear o modificar un tercero en una sede lo hace visible en todas las sedes
  del tenant y señala sus cambios POS sin esperar otro evento. Los roles de un
  tercero no se duplican al crear sedes.

## Cutover de datos

La ausencia de tenants con varias sedes en producción evita reconciliar fichas
duplicadas entre sedes, pero no elimina la conversión de esquema. Antes de
retirar una columna `BusinessId` de un maestro, la migración asigna `TenantId`
desde `Businesses`, detecta conflictos de unicidad dentro del tenant y detiene
el despliegue si encuentra datos ambiguos. La sede de origen no se convierte en
una segunda ficha. En DEV, donde ya hay dos sedes, las copias de códigos de
barras e identificadores se consolidan por producto y tenant después de
comprobar que sus valores y estado no entran en conflicto.
Las copias de proveedor con igual identificación y configuración comercial se
consolidan solo si la copia posterior no tiene referencias; se conserva el
proveedor original y la ficha `Party` de la copia. Un proveedor con datos
distintos o referencias propias detiene la migración para revisión.
Las copias de cliente que apuntan al mismo `PartyId` en un tenant se reconcilian
antes del DACPAC: se conserva el rol más antiguo y solo se elimina una copia
con la misma configuración de factura electrónica y estado, sin referencias
por clave foránea. Una diferencia o una referencia propia detiene el despliegue.

La migración se ejecuta antes del plan DACPAC mediante el pipeline de release.
La aplicación y el esquema se publican juntos desde un commit integrado en
`origin/main`. El rollback de la aplicación requiere restaurar el esquema y los
datos de un respaldo; una versión antigua que consulte `BusinessId` de los
maestros no es compatible con el esquema final.

## Criterio de aceptación

1. Crear una segunda sede con precios compartidos y abrir la ficha del mismo
   `ProductId`: nombre, familia, área, marca, unidad, IVA, proveedor y códigos
   son idénticos; el precio se lee de la sede seleccionada.
2. Editar esos metadatos desde cualquiera de las dos sedes y comprobar que la
   otra lee la nueva ficha, sin duplicados ni copias por sede.
3. Editar o publicar el precio en una sede independiente y comprobar que el
   precio de la otra no cambia. Para sedes que comparten precios, comprobar la
   propagación existente del grupo.
4. Buscar, capturar y confirmar una venta online en la segunda sede por nombre,
   código y código de barras: el producto, IVA y precio son correctos. Repetir
   con POS Edge después de sincronizar. Una venta preparada antes de cambiar IVA
   conserva su snapshot.
5. Crear y seleccionar cliente, proveedor, vendedor y transportador desde otra
   sede del mismo tenant; otro tenant no puede verlos ni referenciarlos.
6. Ejecutar migración sobre datos de una y varias sedes, pruebas de regresión,
   build de SQL/backend/frontend y medir consultas del camino de búsqueda y
   captura. El número de viajes debe ser constante respecto al número de
   productos o sedes afectadas; las listas permanecen paginadas.
