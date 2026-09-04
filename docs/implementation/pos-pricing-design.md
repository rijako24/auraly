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

Las listas (`PriceLists`, `PriceListItems`) y los canales (`PriceChannels`,
`PriceChannelItems`, `PriceChannelExclusions`) pertenecen al negocio.
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

