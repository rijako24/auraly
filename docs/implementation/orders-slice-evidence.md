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
- el pedido recuperado hidrata literalmente la fotografía guardada, aun si el producto quedó inactivo; una edición posterior vuelve a aplicar las reglas vigentes;
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
- Se corrigieron las pruebas que aún buscaban semillas en `database/Auraly.Database`; ahora usan `database/Auraly.Database`.
- Se verificó explícitamente que `SourceOrderId` llegue a la outbox SQLite y al vínculo único del servidor.

## Regresión de la vista con el cambio contable — 2026-10-22

`admin/e2e/orders-real-local.spec.ts` usa autenticación, BFF, API y SQL reales,
sin interceptar solicitudes. Exige las URLs locales y tenant/sede explícitos del
fixture aislado. Crea su cliente con sede por la API canónica y su producto por
la vista; comparte esos preparadores con Gastos y Recepción de compra. No cambia
maestros existentes ni inserta estados financieros directamente en SQL.

El recorrido guarda dos pedidos, edita cantidad y observación en Pedidos,
recupera uno en Facturación, cambia su cantidad y guarda sobre el mismo pedido
con confirmación explícita. Comprueba cliente/sede, precio pactado y liberación
del claim. Después emite dos comprobantes independientes desde la selección,
verifica su vínculo y sus asientos balanceados y repite las mismas claves sin
crear otra factura. La prueba completa pasó en 54,2 segundos; con las aserciones
adicionales de sede pasó nuevamente en la regresión conjunta. Se revisó su
captura visual `work/pedidos-reales-facturados.png`.

La primera versión usó un producto propio sin control de inventario. La regresión
del 2026-10-24 lo cambió a inventariable y preparó veinte unidades mediante la API
canónica de ajuste, con el motivo activo del catálogo del fixture. Después de
editar/recuperar y facturar cuatro unidades entre los dos pedidos, la misma
consulta paginada de balances verifica dieciséis disponibles, también tras los
replays. El recorrido pasó en 45,7 segundos con la API Release actualizada y su
captura visual fue revisada. Complementa los 112 casos SQL/API de inventario,
compras, devoluciones, producción y consumidores de pedidos; no prueba envío
externo DIAN. Si un intento interrumpido
deja un borrador, solo permite reiniciarlo mediante Sales después de comprobar
que proviene de un pedido marcado por esta misma regresión local; conserva el
pedido y no recorre ni elimina otros documentos.

La venta directa de `pos-browser-regression.spec.ts` utiliza el mismo fixture
local y un producto propio. Selecciona la bodega por la vista, busca y captura
con teclado, cambia cantidad, cobra en efectivo, genera la tirilla mediante el
renderer existente y verifica asiento balanceado y borrador limpio. Pasó en
54,4 segundos. Este recorrido no verifica una impresora física. Las esperas de
preparación observan la venta lista o el botón de continuación, evitando actuar
sobre el encabezado transitorio de carga.

La revisión de presentación con login local encontró un fallo previo en los tres
tamaños de `orders-dispatch-style-regression.spec.ts`: exige la misma tipografía
para Pedidos y Despachos, pero `HEAD` ya conserva `text-2xl font-semibold` en
Pedidos y `text-3xl font-bold` en Despachos. La ronda autenticada llega a ambas
pantallas y falla en esa aserción de Despachos. Se conserva la comprobación;
no se declara aprobada ni se cambia la presentación durante la implementación
financiera. Este hallazgo no es un fallo del recorrido de recuperar, editar o
facturar pedidos descrito arriba. La prueba reutiliza ahora el login y alcance
locales canónicos, con tenant/sede explícitos, en vez de credenciales por defecto.
