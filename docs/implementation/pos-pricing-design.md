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
Lista o canal se asignan únicamente al cliente mediante
`CustomerBusinessPricing`; una restricción SQL impide que ambos estén presentes.

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
2. Si el cliente tiene lista vigente, escoger el detalle vigente con mayor
   cantidad mínima que no supere la cantidad. Si no existe, usar base.
3. En otro caso, si tiene canal vigente y no existe exclusión, usar el precio
   resuelto del canal. Si falta, usar base.
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

## Proyección local

SQLite contiene:

- precios base del negocio;
- listas, detalles y vigencias;
- canales y precios finales materializados;
- exclusiones;
- clientes mínimos y su asignación excluyente;
- versiones y tombstones.

No contiene costos, proveedores, fórmulas confidenciales, inventario ni datos
de otros negocios. La aplicación de un delta y el avance del cursor son una
única transacción.

