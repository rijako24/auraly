# Inventario administrativo y averías

Fecha: 2026-08-09

Actualizado: 2026-08-25

## Alcance conectado

Esta rebanada extiende el modelo canónico existente; no crea un segundo inventario. `InventoryBalances`, `InventoryMovements`, `InventoryOperations` y el motor ordenado continúan siendo las únicas fuentes operativas.

- Existencias paginadas y filtrables por bodega, producto, código y nombre.
- Kárdex paginado por bodega, producto, documento, movimiento y fechas.
- Historial paginado de conteos, ajustes, traslados, conversiones y averías.
- Costos y valorización visibles solamente con `inventory.costs.read`.
- Avería como documento definitivo `Damage`, numeración Auraly `AVE00-00000001`, línea `DAMAGE` y movimiento `InventoryDamage`.
- La avería retira inventario vendible al costo promedio vigente; no edita saldos directamente.
- El documento se acepta, encola y procesa exactamente una vez; un saldo insuficiente sigue la política común de reintento y dead letter sin adelantar documentos posteriores del mismo negocio.
- Un evento `inventory.operation.processed` se escribe en la outbox del servidor dentro de la misma transacción.

## Contratos HTTP

- `GET /api/commerce/v1/inventory/warehouses`
- `GET /api/commerce/v1/inventory/balances`
- `GET /api/commerce/v1/inventory/movements`
- `GET /api/commerce/v1/inventory/operations`
- `POST /api/commerce/v1/inventory-damages/confirm`
- `GET|POST /api/commerce/v1/inventory/physical-counts`
- `GET /api/commerce/v1/inventory/physical-counts/{countId}`
- `POST /api/commerce/v1/inventory/physical-counts/{countId}/start`
- `PUT /api/commerce/v1/inventory/physical-counts/{countId}/lists/{listId}/pre-count`
- `PUT /api/commerce/v1/inventory/physical-counts/{countId}/lists/{listId}/count`
- `POST /api/commerce/v1/inventory/physical-counts/{countId}/close`

Todas las consultas usan el Business autenticado y paginación del servidor. El cuerpo no puede cambiar el alcance del usuario.

## Interfaz

`/dashboard/inventory` concentra tres contextos estables: Existencias, Kárdex y Operaciones. Operaciones lista y filtra inventarios físicos, ajustes, traslados, conversiones y averías. `Nueva operación` es una acción global que abre el formulario específico sin convertir cada captura en una pestaña principal. Usa los componentes visuales de Auraly, selector de bodega no nativo, búsqueda combinada y estados vacíos/carga. El menú requiere `inventory.read`; confirmar averías requiere `inventory.damages.confirm`.

## Coordinación de inventario físico

El diseño canónico de la siguiente iteración está cerrado en
`docs/implementation/inventory-physical-count-unified-design.md`. Ese diseño
reemplaza la presentación basada en listas por sesiones con alcance, capturas de
usuario, revisión por producto, pendientes y conflictos. Esta sección documenta
el comportamiento de la implementación vigente hasta completar el cutover.

Un inventario físico se abre sobre un único alcance, sin exigir listas previas al usuario. El alcance parcial permite escoger una parte del catálogo y el general incluye automáticamente todos los productos inventariables activos del negocio. Durante la captura, los avances se presentan como borradores; la vista mantiene juntos el borrador activo, los productos no contados y un único consolidado que puede abrirse y cerrarse sin abandonar el conteo. Las listas internas siguen siendo una partición técnica compatible para coordinar capturas disjuntas, pero no constituyen inventarios separados ni se muestran simultáneamente como formularios de conteo.

`InventoryPhysicalCounts`, `InventoryPhysicalCountLists` e `InventoryPhysicalCountLines` son estado de coordinación y captura, no otro libro de inventario. Al solicitar el cierre, la capa HTTP congela el consolidado, crea y acepta un solo documento canónico `StockCount` mediante `InventoryOperationService`, y publica su señal; no modifica existencias ni declara cerrado el inventario físico. El handler de inventario del motor ordenado es el único que modifica `InventoryBalances`, escribe `InventoryMovements` y cambia la coordinación a `Closed`, todo dentro de la misma transacción de procesamiento. La numeración CTI y el trabajo durable se reservan al aceptar el documento.

Cada conteo conserva la secuencia procesada en la que se capturó. El cierre suma al valor contado los movimientos posteriores a esa captura antes de confirmar el `StockCount`; por eso una venta, recepción, ajuste o traslado ocurrido mientras otros equipos terminan no se pierde ni se vuelve a contar como diferencia. La creación bloquea que un producto participe simultáneamente en dos inventarios físicos activos de la misma bodega.

Los permisos se separan por responsabilidad: `inventory.physical-counts.manage` crea, inicia y cierra; `inventory.physical-counts.capture` guarda y envía preconteos/conteos; el cierre también exige `inventory.counts.confirm` porque genera el documento definitivo.

## Numeración y permisos

El proyecto SQL provisiona las series operativas CTI, AJI, TRB, CNV y AVE para negocios que aún no poseen una serie activa. No son prefijos DIAN. Los permisos se asignan al rol Administrator mediante el postdeployment.
## Cierre de la captura operativa

La acción `Inventario > Nueva operación` es el único punto de entrada para inventarios físicos, ajustes, traslados, conversiones y averías. No replica reglas de negocio: consume los casos de uso canónicos y todos los documentos confirmados entran al mismo motor ordenado mediante su señal RabbitMQ.

Reglas de teclado comunes a sus grillas:

- Flecha abajo y flecha arriba desplazan la fila activa sin modificar cantidades.
- La celda principal de cada fila es cantidad y conserva el foco durante la edición.
- Enter confirma el valor actual y avanza a la cantidad de la fila siguiente; desde la última línea regresa al buscador.
- Escape regresa al buscador sin borrar el documento.
- Los campos de cantidad usan entrada decimal de texto para impedir que las flechas alteren el número de forma nativa.
- Agregar o eliminar una línea conserva un foco predecible y desplaza la línea activa al área visible.

El selector usa `GET /api/commerce/v1/inventory/products` con paginación del servidor. Incluye productos inventariables activos aunque su saldo sea cero, restringe la bodega al Business autenticado y oculta el costo promedio sin `inventory.costs.read`.

El conteo se ejecuta en dos pasos: preparar congela el saldo base y confirmar registra las cantidades físicas. Después de preparar no se permite alterar la lista de productos. Ajustes admiten cantidad positiva o negativa; el costo explícito solo aplica a entradas. Traslados exigen bodegas distintas. Conversión exige entradas y salidas coherentes con separación o unificación. Averías solo admiten cantidades positivas.
