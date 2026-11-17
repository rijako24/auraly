# Diseño canónico de precios para Auraly POS

Fecha: 2026-07-28

## Modelo

`ProductPrices` contiene exclusivamente el precio base vendible de un producto
en un negocio:

```text
ProductPriceId
BusinessId
ProductId
SalePrice
CurrencyCode
EffectiveFrom
EffectiveTo
IsActive
RowVersion
auditoría
```

No contiene tenant, lista, canal, costo ni inventario. La publicación vendible
exige un precio base vigente.

Las listas (`PriceLists`, `PriceListItems`) pertenecen al negocio. La definición
de un canal (`PriceChannels`, `PriceChannelItems`, `PriceChannelExclusions`)
pertenece al tenant y aplica en todas sus sedes. La sede seleccionada aporta
su precio base.
La vigencia del canal se configura únicamente en su asignación al cliente
mediante `CustomerPricingSettings`; los precios por producto y cantidad no
tienen un rango de vigencia propio.

Un canal se calcula mediante `PriceChannelResolver`, compartido por servidor y
POS Edge. La caja recibe la definición y únicamente los tramos configurados; no
recibe una matriz materializada canal por producto. Los insumos del producto
para estrategias de costo o margen forman parte de su única fila de catálogo
local y no se exponen en la UI de caja.

## Resolución

Entrada:

- `BusinessId`, `ProductId`, `CustomerId?`;
- cantidad, instante, moneda y versión de catálogo;
- contexto de promoción;
- precio manual autorizado opcional.

Salida:

- precio base y resuelto;
- origen `Base`, `PriceList`, `PriceChannel`, `Promotion` o `ManualOverride`;
- IDs de lista, canal, regla o promoción aplicados;
- versión, vigencia y razón técnica.

Algoritmo:

1. Obtener el precio base vigente por negocio y producto.
2. Si el cliente tiene lista vigente, escoger el detalle con mayor
   cantidad mínima que no supere la cantidad. Si no existe, usar base.
3. En otro caso, si tiene canal vigente y no existe exclusión por producto,
   marca o categoría —incluido cualquier ancestro del nodo asignado al
   producto—, usar el precio resuelto del canal. La UI denomina área, línea,
   grupo y subgrupo a las profundidades 0 a 3 de esa única jerarquía. Para
   precios por cantidad se escoge la mayor cantidad mínima aplicable. Si falta
   o existe una exclusión, usar base.
4. Sin asignación, usar base.
5. Evaluar todas las promociones activas en orden determinista. Promociones que
   afectan líneas distintas se aplican independientemente. En una misma línea
   solo se acumulan cuando todas las involucradas son combinables.
6. Si el tenant permite combinar promoción y canal, calcular la promoción sobre
   el precio de canal; si no lo permite, una promoción aplicable se calcula
   sobre el público y reemplaza al canal para esa línea.
7. Aplicar precio manual autorizado, si existe.
8. Aplicar descuento manual autorizado y calcular impuestos/totales.

Lista y canal nunca se combinan. La ausencia de especial nunca bloquea la venta.

## Cambio de cliente, cantidad y catálogo

- Cambiar cliente vuelve a resolver las líneas abiertas sin precio manual
  congelado y conserva un resumen de origen anterior/nuevo.
- Cambiar cantidad reevalúa escalas y disponibilidad cuando la bodega bloquea
  negativos.
- Un delta de precios no repricia silenciosamente una línea ya capturada.
- Un documento confirmado conserva su fotografía de precio e impuestos, con
  descuento total y descuento promocional separados hasta `SalesDocumentLines`.

## Propietario de la resolución online

`PriceChannelResolver` es el único motor de fórmulas de canal para los flujos
online de POS, búsqueda/captura de pedidos y POS Edge. La composición
promocional pertenece a `PromotionPriceResolver`, función pura llamada por
búsqueda, captura y recálculo online y por POS Edge. Los adaptadores y
procedimientos SQL solo cargan precio público, configuración de canal,
clasificación, disponibilidad y promociones; no contienen reglas de precedencia
ni fórmulas duplicadas.

Si el cliente no tiene una asignación vigente, el canal está inactivo, el
producto está excluido o no existe una escala aplicable, el resolvedor retorna
el precio público vigente. La ausencia de precio especial nunca bloquea la
venta.

## Proyección local

SQLite contiene:

- precios base del negocio;
- listas, detalles y vigencias;
- definiciones de canales;
- tramos por cantidad solo para los productos configurados en cada canal;
- exclusiones por producto, marca o categoría, evaluadas por el resolvedor;
- clientes mínimos y su asignación excluyente;
- configuración de combinación del tenant y promociones activas;
- alcance de promociones ya filtrado por tenant y por sede
  (`PromotionBusinessScopes` o todas las sedes);
- versiones y tombstones.

No contiene proveedores, inventario de otros negocios ni datos de otros tenants.
Los insumos de costo estrictamente necesarios para resolver una estrategia
forman parte del catálogo local protegido y no se presentan al cajero. La
aplicación de un delta y el avance del cursor son una única transacción.


## Selección colectiva de sincronización de precios por cantidad (2026-11-17)

La vista presenta «Sincronizar precio · marcar todos» y una casilla por fila para excluir tramos individuales.
Es una selección colectiva de `PriceChannelItems.AutoUpdatePrice`, no otra regla
ni una propiedad persistida del canal. El estado mixto conserva configuraciones
existentes hasta que el usuario marque o desmarque todos y guarde. Puede cambiar después filas concretas; las excepciones prevalecen sobre marcar todos dentro del mismo guardado. Los productos
agregados desde esa vista heredan la selección colectiva guardada; un canal vacío
no tiene una política de sincronización independiente de sus productos.

El comando de configuración existente aplica el cambio por conjunto sobre los
tramos activos de ese canal y tenant. Requiere el mismo permiso en todas sus
sedes y rechaza habilitar tramos sin margen. No modifica importes, márgenes,
tramos históricos ni el esquema; omitir la selección conserva los flags existentes.
La respuesta confirma la selección aplicada y la UI actualiza su único cache,
sin un GET posterior por producto ni un comando por fila. Las excepciones
individuales se acotan a 500 por guardado; marcar todos se aplica directamente
al conjunto del canal sin ese límite. Ambas selecciones se validan antes de
escribir. Agregar/retirar una
condición conserva su contrato de acknowledgement: una sola invalidación por
sede recarga una vez el detalle normalizado por servidor y su conteo del canal.
Las consultas de detalle se activan solo para el canal abierto o su editor y su
clave incluye sede, que determina el costo mostrado.

`SqlPricingStore.RefreshAutomaticChannelPricesAsync` sigue siendo el propietario
existente del recálculo dentro de la publicación. Preparar costos o recibir una
compra no modifica el precio vendible. Publicar carga por lote únicamente los
productos publicados y sus tramos activos habilitados; conserva el margen de
cada tramo y usa el costo publicado más impuestos mediante `PriceMargin`.
Persistencia, auditoría y señal de configuración a todas las sedes permanecen en
la transacción existente; un fallo revierte el lote completo. Los tramos no
habilitados conservan su importe y las ventas ya confirmadas no se recalculan.

Presupuesto: marcar todos en el editor no hace I/O; guardar hace una petición
al endpoint existente y un UPDATE por conjunto dentro de su comando SQL, sin
viajes por tramo ni nuevo refetch. La publicación mantiene sus dos viajes de
carga/escritura de canales, independientes del número de productos del lote.
La regresión dirigida cubre mezcla inicial, marcar/desmarcar, margen/precio
conservados al preparar, recálculo al publicar, otro canal fijo, excepciones por tramo, destinos ajenos rechazados, permisos y replay.
Rollback: revertir la vista y el parámetro opcional no requiere migrar datos;
los flags conservan el formato y significado anteriores. No despliega producción.
