# Rebanada: precios y rentabilidad

**Fecha:** 2 de agosto de 2026  
**Estado:** implementada y validada con SQL Server real

## Objetivo

Conectar el costo observado por una entrada de mercanc?a con una propuesta revisable, sin cambiar autom?ticamente el precio de venta. El precio solamente se vuelve vendible despu?s de una publicaci?n autorizada.

## Flujo vertical

1. El motor procesa la entrada de mercanc?a en el orden durable del negocio.
2. Purchasing conserva el costo observado y actualiza los efectos propios del documento.
3. Se crea una propuesta idempotente en PriceRevisionProposals.
4. La vista Productos > Precios y rentabilidad consulta propuestas paginadas.
5. El usuario edita margen sobre venta o precio de venta.
6. La API vuelve a calcular el resultado con decimal y aplica el redondeo.
7. La publicaci?n cierra el precio anterior e inserta una nueva versi?n.
8. La misma transacci?n registra auditor?a, CatalogChanges y la outbox de sincronizaci?n.
9. El worker de outbox env?a una invalidaci?n peque?a por el transporte push configurado.
10. POS Edge descarga el delta por cursor y actualiza SQLite transaccionalmente.

No existe sondeo peri?dico. Al abrir o reconectar, la puesta al d?a por cursor recupera se?ales que se hubieran perdido.

## Responsabilidades

- ProductPrices es el precio base publicado por BusinessId y ProductId.
- SupplierCostObservations conserva costos observados; no se descarga al POS.
- PriceRevisionProposals conserva la decisi?n pendiente y su concurrencia.
- PricePublicationAudits explica qui?n public?, desde qu? costo y con qu? margen.
- CatalogChanges es la secuencia durable que consume el POS.
- PosSynchronizationOutboxMessages evita perder la se?al push.

La edici?n general del producto ya no puede cambiar el precio publicado. La creaci?n conserva un precio inicial necesario para que el producto sea vendible; todo cambio posterior pasa por Pricing.

## C?lculo

Auraly usa margen sobre venta:

```text
margen = (precio - costo) / precio * 100
precio = costo / (1 - margen / 100)
```

El servidor admite dos modos excluyentes:

- Margin: el usuario digita margen y el servidor calcula precio.
- SalePrice: el usuario digita precio y el servidor calcula margen.

Despu?s se aplica Nearest, Up o Down con un incremento decimal y se recalcula el margen efectivo.

## Transacci?n de publicaci?n

La operaci?n usa aislamiento Serializable y rowversion:

- verifica negocio, tenant, producto, propuesta y versi?n;
- desactiva la versi?n anterior;
- inserta ProductPrices con el snapshot explicativo;
- cambia la propuesta a Published;
- inserta PricePublicationAudits;
- inserta CatalogChanges;
- inserta PosSynchronizationOutboxMessages;
- confirma una sola transacci?n.

Un conflicto o reintento no crea otra versi?n, auditor?a ni cambio de cat?logo.

## Seguridad

Permisos separados:

- pricing.read
- pricing.cost-basis.read
- pricing.proposals.review
- pricing.prices.publish
- pricing.bulk-publish
- pricing.rounding.manage
- pricing.history.read

El backend valida siempre los permisos y el alcance autenticado. Costos y m?rgenes nunca forman parte del contrato POS.

## Interfaz

La vista administrativa ofrece:

- resumen de pendientes, aprobados y publicados;
- b?squeda y filtro por estado;
- paginaci?n de servidor;
- selecci?n individual o masiva;
- edici?n bidireccional;
- previsualizaci?n calculada por servidor;
- guardar propuesta, rechazar y publicar;
- estados de carga, vac?o y error.

La pantalla usa los componentes visuales actuales del admin de Auraly y no reproduce el formulario de Xion.
