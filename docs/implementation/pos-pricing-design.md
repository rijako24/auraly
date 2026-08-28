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
`ResolvedPriceChannelItems`, `PriceChannelExclusions`) pertenecen al negocio.
La vigencia del canal se configura únicamente en su asignación al cliente
mediante `CustomerPricingSettings`; los precios por producto y cantidad no
tienen un rango de vigencia propio.

Un canal que depende de costo o margen se calcula en servidor. POS Edge recibe
solo el resultado materializado y nunca la fórmula o el costo.

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
5. Evaluar promoción con una precedencia explícita, sin acumular beneficios no
   combinables.
6. Aplicar precio manual autorizado, si existe.
7. Aplicar descuento manual autorizado y calcular impuestos/totales.

Lista y canal nunca se combinan. La ausencia de especial nunca bloquea la venta.

## Cambio de cliente, cantidad y catálogo

- Cambiar cliente vuelve a resolver las líneas abiertas sin precio manual
  congelado y conserva un resumen de origen anterior/nuevo.
- Cambiar cantidad reevalúa escalas y disponibilidad cuando la bodega bloquea
  negativos.
- Un delta de precios no repricia silenciosamente una línea ya capturada.
- Un documento confirmado conserva su fotografía de precio e impuestos.

## Propietario de la resolución online

`dbo.CustomerProductPriceResolve` es el único resolvedor de precio de cliente
para la búsqueda, captura y recálculo online de Venta y Pedidos. Ambos flujos
deben enviar `BusinessId`, `WarehouseId`, `CustomerId`, `ProductId` y cantidad;
no deben volver a implementar la fórmula del canal en consultas paralelas.

Si el cliente no tiene una asignación vigente, el canal está inactivo, el
producto está excluido o no existe una escala aplicable, el resolvedor retorna
el precio público vigente. La ausencia de precio especial nunca bloquea la
venta.

## Proyección local

SQLite contiene:

- precios base del negocio;
- listas, detalles y vigencias;
- canales y precios finales materializados;
- exclusiones resueltas: un producto excluido por categoría, marca o identidad
  no recibe una fila de canal y el resolvedor local conserva el precio base;
- clientes mínimos y su asignación excluyente;
- versiones y tombstones.

No contiene costos, proveedores, fórmulas confidenciales, inventario ni datos
de otros negocios. La aplicación de un delta y el avance del cursor son una
única transacción.

