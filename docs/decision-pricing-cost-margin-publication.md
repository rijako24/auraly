# Decisión: costo, utilidad, precio de venta y publicación a cajas

**Fecha:** 31 de julio de 2026
**Estado:** vigente y obligatoria
**Referencia funcional:** Xion, ficha de producto, entrada de mercancía, productos-proveedores-costos y actualización de precios de venta

## 1. Prevalencia

Este documento complementa `decision-modulo-productos-proveedores-costos.md` y `implementation/pos-pricing-design.md`.

Ante contradicción, reemplaza las reglas antiguas que:

- excluyen listas de precios;
- asignan lista o canal a caja, sede o empresa;
- descargan únicamente un precio especial de caja;
- confunden costo de proveedor, costo promedio y precio de venta.

La regla vigente es:

- `ProductPrices` contiene el precio base por `BusinessId + ProductId`;
- lista o canal son configuraciones excluyentes del cliente;
- sin precio especial se usa siempre el precio base del negocio;
- la ausencia de precio especial nunca bloquea una venta;
- POS Edge descarga precios vendibles, pero nunca costos ni margen interno.

## 2. Hallazgo exacto en Xion

Xion separa funcionalmente:

1. costo del proveedor;
2. costo aplicado al producto/sucursal;
3. costo promedio;
4. utilidad o margen;
5. precio sugerido;
6. precio público efectivo.

La entrada de mercancía actualiza el costo observado del proveedor y marca el producto como pendiente. El formulario `FrmProductosActualizarPreciosVentaProducto` consulta esos pendientes, calcula precios sugeridos y permite guardar la propuesta o actualizar el precio público mediante permisos diferentes.

Xion permite edición bidireccional:

- cambiar utilidad recalcula precio de venta;
- cambiar precio de venta recalcula utilidad;
- después del redondeo vuelve a calcular la utilidad efectiva.

La fórmula utilizada no es un recargo sobre costo. Es margen sobre venta:

```text
MarginPercent = (SalePrice - CostBasis) / SalePrice * 100
SalePrice = CostBasis / (1 - MarginPercent / 100)
```

Ejemplo:

```text
Costo:               80.000
Margen sobre venta:     20 %
Precio calculado:    100.000
```

Un recargo del 20 % produciría 96.000 y no es equivalente. La interfaz Auraly mostrará **Margen de utilidad sobre venta** para evitar ambigüedad, aunque comercialmente pueda abreviarse como “Utilidad %”.

Xion también conserva hasta cuatro escalas rígidas, descuentos fijos en columnas y redondeos por precio/sucursal. Auraly conserva la capacidad, pero no esas columnas ni formularios.

## 3. Conceptos canónicos

### Costos

| Concepto | Dueño | Significado |
|---|---|---|
| Costo negociado | Procurement | Acuerdo vigente con un proveedor |
| Costo observado | Purchasing | Costo inmutable de una línea de entrada |
| Último costo de compra | Purchasing/Reporting | Último costo observado confirmado |
| Costo promedio | Inventory | Valoración ponderada autoritativa del saldo |
| Costo base de fijación | Pricing | Snapshot elegido para calcular o analizar un precio |

Cambiar uno no sobrescribe silenciosamente los demás.

### Precio

`ProductPrices` conserva exclusivamente versiones publicadas del precio base vendible:

```text
ProductPriceId
BusinessId
ProductId
SalePrice
CurrencyCode
CostBasisType
CostBasisAmount
TargetMarginPercent?
EffectiveMarginPercent
InputMode              // Margin | SalePrice
RoundingRuleId?
ValidFrom
ValidUntil?
IsActive
PublishedBy
PublishedAt
RowVersion
```

El costo y margen aquí son snapshots explicativos de la publicación. No reemplazan el costo promedio ni el acuerdo del proveedor.

### Propuesta

Una variación de costo crea o actualiza una propuesta, nunca el precio publicado:

```text
PriceRevisionProposal
---------------------
ProposalId
BusinessId
ProductId
SourceDocumentId?
SourceCostType
SourceCostAmount
CurrentSalePrice
CurrentEffectiveMargin
SuggestedTargetMargin
SuggestedSalePrice
RoundedSuggestedSalePrice
EffectiveMarginAfterRounding
AbsoluteVariation
PercentageVariation
Status
CreatedAt
ReviewedBy?
ReviewedAt?
RowVersion
```

Estados:

```text
PendingReview
Approved
Published
Rejected
Superseded
```

## 4. Edición bidireccional

### El usuario modifica utilidad

1. Selecciona la base de costo visible y autorizada.
2. Digita margen entre 0 y menos de 100.
3. El servidor calcula precio sin redondear.
4. Aplica la regla de redondeo del negocio.
5. Recalcula el margen efectivo usando el precio redondeado.
6. La UI muestra antes/después y no publica hasta confirmar.

### El usuario modifica precio de venta

1. Digita el precio deseado.
2. El servidor calcula el margen efectivo contra la base de costo seleccionada.
3. Si costo es cero o desconocido, muestra margen como no calculable; no inventa 100 %.
4. Aplica advertencias de margen mínimo o precio bajo costo según configuración y permisos.
5. Publica solamente después de confirmación autorizada.

Todas las operaciones usan `decimal`; el frontend puede previsualizar, pero la API recalcula y valida.

## 5. Flujo de entrada de mercancía

Al confirmar una entrada, el motor crítico procesa en su turno:

1. snapshot del costo recibido;
2. entrada de inventario;
3. nuevo costo promedio y valor del saldo;
4. última compra;
5. costo observado del proveedor;
6. cuenta por pagar;
7. variación contra costo negociado y precio vigente;
8. solicitud durable de propuesta de precio;
9. outbox y finalización del documento.

La entrada **no modifica `ProductPrices`**.

Después del commit, Pricing crea la propuesta idempotente. El usuario la revisa desde:

```text
Productos > Revisión de precios
```

La vista también se abre filtrada desde la entrada procesada. Permite:

- comparar costo anterior, nuevo y promedio;
- conservar el precio actual;
- digitar utilidad y calcular precio;
- digitar precio y calcular utilidad;
- aplicar una utilidad a varias filas;
- usar redondeo;
- aprobar, rechazar o publicar seleccionados;
- ver impacto y permisos antes de confirmar.

No habrá dos botones ambiguos como “Guardar” y “Actualizar”. Auraly usará:

- **Guardar propuesta**;
- **Publicar precio**.

## 6. Ficha de producto

La sección comercial de producto mostrará:

- precio base publicado del negocio;
- costo base de fijación seleccionado;
- último costo de compra;
- costo promedio;
- margen efectivo actual;
- utilidad objetivo opcional;
- precio calculado y redondeado;
- fecha/usuario de publicación;
- historial;
- propuestas pendientes.

Desde la ficha, un usuario con permiso puede publicar directamente un precio manual. Debe pasar por el mismo servicio de publicación, auditoría, cambio de catálogo y notificación que la revisión masiva.

El formulario actual no debe seguir eliminando y recreando asociaciones de proveedor y costos al editar datos generales del producto. Esas capacidades se modifican mediante comandos propios y versiones inmutables.

## 7. Listas, canales y cantidad

No se recrean `PrecioPublico1...4`.

- El precio base es una versión en `ProductPrices`.
- Las escalas por cantidad pertenecen a `PriceListItems`.
- Los canales conservan resultados materializados en `ResolvedPriceChannelItems`.
- Lista o canal se asignan exclusivamente al cliente y nunca ambos.
- Sin coincidencia de escala, producto de canal o cliente, se usa el precio base.

Cuando cambia el precio base, Pricing identifica listas y canales derivados que requieren recalcularse. Sus nuevos resultados se publican en la misma operación lógica o quedan explícitamente pendientes; nunca quedan silenciosamente inconsistentes.

## 8. Redondeo

El redondeo se configura por negocio, no mediante cuatro columnas:

```text
PricingRoundingRule
-------------------
RoundingRuleId
BusinessId
Name
Increment
Mode               // Nearest | Up | Down
CurrencyCode
IsDefault
IsActive
```

Ejemplo: incremento 50, modo `Up` transforma 10.021 en 10.050.

El precio almacenado y enviado al POS es el resultado redondeado. El margen efectivo se calcula después del redondeo.

## 9. Publicación y notificación a cajas

Publicar precio es una única transacción SQL:

1. cerrar la versión anterior;
2. insertar la nueva versión de `ProductPrices`;
3. marcar propuesta publicada si existe;
4. registrar auditoría;
5. insertar `CatalogChanges` con cursor monotónico;
6. insertar un mensaje durable de outbox dirigido al `BusinessId`.

Después del commit, un worker .NET publica una señal al grupo del negocio mediante la abstracción de notificaciones configurada:

- Azure Web PubSub en SaaS;
- SignalR o transporte equivalente en on-premise;
- recuperación por cursor al abrir o reconectar; nunca polling.

La señal contiene únicamente:

```text
BusinessId
ChangeStream = Catalog
AvailableThroughCursor
```

No contiene costos, margen, catálogo completo ni secretos.

Cada caja:

1. abre su conexión cuando la aplicación está activa;
2. recibe la señal;
3. llama a `GET /api/pos/v1/catalog/changes?cursor=...`;
4. descarga solo los cambios posteriores;
5. aplica precio y cursor en una transacción SQLite;
6. informa su cursor aplicado para observabilidad.

Si la caja estaba cerrada, perdió la señal o Pub/Sub no está disponible, al abrir ejecuta una puesta al día incremental en segundo plano. Puede seguir facturando con su catálogo válido mientras descarga, salvo que una política explícita determine que la versión está demasiado atrasada.

La notificación es optimización; `CatalogChanges` es la fuente durable que impide perder precios.

## 10. Cambios necesarios sobre la implementación actual

La base existente es útil, pero todavía faltan capacidades:

- `ProductPriceInput` solo contiene importe y moneda; debe admitir modo de entrada y snapshot de margen/costo.
- `ProductPrices` carece de información de publicación y explicación del cálculo.
- no existen propuestas de revisión de precio.
- la edición general del producto desactiva precios y elimina/recrea costos; debe separarse.
- `CatalogChanges` registra el upsert durable, pero todavía no existe el mensaje de notificación producido por publicación de precio.
- el transporte real de esta rama es sincronización incremental; Pub/Sub no debe declararse implementado hasta tener productor, consumidor y pruebas.
- documentos antiguos sobre canales/listas contienen reglas superadas y deben leerse con esta prevalencia.

## 11. Permisos mínimos

```text
pricing.read
pricing.cost-basis.read
pricing.proposals.review
pricing.prices.publish
pricing.bulk-publish
pricing.rounding.manage
pricing.history.read
```

Ver costos y publicar precios son permisos distintos. El cajero recibe únicamente el precio efectivo.

## 12. Pruebas obligatorias

- cálculo desde margen;
- cálculo de margen desde precio;
- margen inválido igual o superior a 100;
- costo cero o desconocido;
- redondeo y margen efectivo posterior;
- precisión decimal;
- entrada cambia costo observado y promedio, no precio publicado;
- propuesta idempotente por entrada/línea/producto;
- publicación individual y masiva;
- concurrencia de dos publicaciones del mismo producto;
- versión anterior cerrada y una sola activa;
- auditoría y permisos;
- `CatalogChanges` y outbox en la misma transacción;
- fallo antes del commit no notifica;
- señal perdida y recuperación por cursor;
- caja cerrada recibe el delta al abrir;
- caja activa recibe señal y descarga únicamente el producto afectado;
- POS no recibe costos ni márgenes;
- lista/canal del cliente y fallback al precio base;
- cambio de precio no repricia silenciosamente una factura abierta;
- SQL Server real, SQLite físico y prueba E2E servidor-caja.

## 13. Orden de implementación

1. Kernel decimal de cálculo y redondeo.
2. Versionado explicable de precio base.
3. Entrada de mercancía, costo promedio, proveedor y cuenta por pagar.
4. Propuestas de revisión originadas por la entrada.
5. Publicación individual y masiva.
6. `CatalogChanges` más outbox y notificación real.
7. Descarga incremental y actualización SQLite demostrada.
8. UI de ficha de producto y Revisión de precios.

Ninguna etapa se considera completa sin productor, consumidor y pruebas conectadas.
