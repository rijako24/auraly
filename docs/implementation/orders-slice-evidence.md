# Evidencia de la rebanada de pedidos

Fecha de ejecución: 2026-07-31.

## Resultado verificado

- `Auraly.Commerce.sln`: compilación Release con 0 errores y 0 advertencias.
- DACPAC `Auraly.Database`: generado dentro del build completo.
- Fundación: 113/113 pruebas aprobadas.
- Host POS Edge: 9/9 pruebas aprobadas.
- Frontend Next.js: build aprobado; rutas `/dashboard/orders` y `/pos` generadas.
- TypeScript: `npx tsc --noEmit` aprobado después del build.
- SQL real, recuperación y autorización POS: 3/3 pruebas aprobadas con despliegue de DACPAC.
- SQL real, vínculo de pedido desde carga POS: aprobado, incluido reintento sin duplicado.
- SQL real, recuperación web y facturación por lote: cubiertos por `OrderRecoveryTests` y `OrderBatchInvoiceTests`.

## Escenarios demostrados

- el esquema de Orders y OrderItems no contiene totales ni tarifas de IVA;
- el pedido recuperado conserva precio y descuento, pero la venta usa el IVA vigente del producto;
- recuperación SQLite atómica y durable;
- rechazo de mezcla con una venta existente;
- `SourceOrderId` sobrevive borrador → emisión → outbox;
- la carga al servidor crea un único `OrderInvoiceLink` y libera el claim;
- repetir `DocumentId` no duplica el vínculo;
- selección de pedidos produce documentos separados;
- idempotencia del lote y protección ante concurrencia;
- endpoint POS exige dispositivo autorizado y usuario local activo con permisos;
- usuario desconocido recibe 403;
- pruebas de arquitectura detectan proyectos canónicos sin consumidor y mantienen Orders conectado.

## Observaciones

La integración SQL usa una base temporal creada desde el DACPAC, no EF InMemory ni SQLite como sustituto del servidor. SQLite se usa únicamente donde corresponde: persistencia local de POS Edge.

La impresión automática de múltiples facturas desde un lote administrativo, rutas y devoluciones quedan fuera de esta rebanada y deben implementarse como flujos conectados posteriores.

## Incidencias corregidas durante la verificación

- Se aislaron los datos tributarios de las pruebas SQL para evitar dependencia del orden de ejecución.
- Se actualizaron los constructores históricos de `OrderSnapshot` para eliminar el argumento `TaxTotal` en vez de conservar una sobrecarga legacy.
- Se corrigieron las pruebas que aún buscaban semillas en `database/MimosBabySpa.Database`; ahora usan `database/Auraly.Database`.
- Se verificó explícitamente que `SourceOrderId` llegue a la outbox SQLite y al vínculo único del servidor.