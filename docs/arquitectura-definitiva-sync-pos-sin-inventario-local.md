# Arquitectura definitiva de sincronización POS

**Estado:** decisión vigente  
**Fecha:** 23 de julio de 2026  
**Prioridad:** reemplaza las propuestas anteriores que incluían inventario local o SignalR/Web PubSub como requisito del MVP.

---

## 1. Decisiones

1. La caja no descarga ni mantiene inventario.
2. La caja sí mantiene catálogo, códigos, precios, impuestos y configuración de productos.
3. El inventario se consulta únicamente online.
4. El POS no consulta el servidor por cada escaneo.
5. El MVP no mantendrá una conexión SignalR/WebSocket permanente por caja.
6. Los cambios se detectan mediante manifiestos versionados livianos.
7. Cuando cambia una versión, la caja descarga solamente el delta correspondiente.

---

## 2. Información que sí conserva la caja

SQLite local:

```text
LocalProducts
LocalProductBarcodes
LocalProductUnits
LocalProductAliases
LocalProductCategories
LocalProductPrices
LocalPriceLists
LocalPromotions
LocalTaxProfiles
LocalCashRegisterSettings
LocalScaleSettings
LocalSyncCheckpoints
LocalSaleDrafts
LocalOutbox
```

Cada producto local contiene únicamente lo necesario para:

- identificarlo;
- buscarlo;
- escanearlo;
- calcular precio;
- calcular impuestos;
- usar balanza;
- validar configuración comercial;
- guardar la fotografía de la venta.

---

## 3. Información que no conserva la caja

No se descargan:

- existencias;
- saldos por bodega;
- kardex;
- reservas;
- movimientos de otras cajas;
- costos históricos de inventario;
- conteos;
- entradas;
- traslados;
- averías;
- inventario consolidado.

Esto evita:

- bases locales demasiado grandes;
- actualizaciones continuas por cada venta;
- inconsistencias entre cajas;
- tráfico de movimientos;
- conciliaciones innecesarias en el dispositivo.

La fuente de verdad del inventario permanece en Azure SQL.

---

## 4. Consecuencia para ventas offline

Una caja offline no conoce la existencia real.

Por lo tanto:

```text
offline = venta sin validación de inventario
```

La caja:

- registra la venta;
- guarda bodega;
- guarda productos y cantidades;
- sincroniza al regresar la red;
- el servidor aplica las salidas;
- cualquier saldo negativo se reporta para conciliación.

No existe una validación local aproximada.

### Política de negativos de la caja

`AllowNegativeStockSales` tiene efecto completo cuando la caja está online.

Si está deshabilitado:

- online: consulta/valida inventario al confirmar;
- offline: la política adicional de la caja decide entre:
  - permitir la venta sin validación;
  - exigir supervisor;
  - impedir ventas offline.

Configuración:

```text
OfflineStockPolicy =
    AllowWithoutValidation
    | RequireSupervisor
    | BlockOfflineSale
```

Para una caja presencial se recomienda `AllowWithoutValidation`, porque el cliente tiene el producto físicamente.

---

## 5. Consulta de inventario online

La consulta se realiza:

- cuando el cajero abre “Consultar existencias”;
- cuando una caja con negativos deshabilitados confirma una venta;
- cuando un módulo administrativo necesita disponibilidad;
- nunca para resolver cada código de barras.

La respuesta puede mostrar:

- existencia por bodega;
- reservada;
- disponible;
- fecha de cálculo.

No se almacena como saldo operativo local.

Se puede mantener en memoria durante pocos segundos para evitar repetir una consulta dentro de la misma pantalla, pero no se sincroniza ni persiste.

---

## 6. Por qué no usar SignalR en el MVP

Azure SignalR Service y Web PubSub están diseñados para manejar conexiones masivas. Técnicamente podrían servir.

Sin embargo, en este caso cada caja mantendría una conexión permanente para recibir eventos que ocurren con poca frecuencia:

- cambio de nombre;
- nuevo código;
- cambio de precio;
- producto desactivado;
- cambio tributario;
- configuración de balanza.

Costos y complejidad:

- conexiones permanentes;
- reconexiones;
- grupos multi-tenant;
- tokens;
- monitoreo;
- límites por unidad;
- mensajes perdidos;
- catch-up obligatorio de todas formas;
- infraestructura activa incluso sin cambios.

El beneficio de recibir el cambio en segundos no justifica inicialmente esa complejidad.

SignalR puede agregarse después como acelerador, sin cambiar el modelo de revisiones ni deltas.

---

## 7. Alternativa recomendada: manifiestos versionados

Cada ámbito publica un manifiesto muy pequeño:

```json
{
  "catalogRevision": 1847,
  "pricingRevision": 933,
  "taxRevision": 71,
  "cashConfigRevision": 28,
  "scaleConfigRevision": 8,
  "generatedAt": "2026-07-23T16:00:00Z"
}
```

La caja conserva:

```json
{
  "catalogRevision": 1846,
  "pricingRevision": 933,
  "taxRevision": 71,
  "cashConfigRevision": 28,
  "scaleConfigRevision": 8
}
```

Compara y detecta:

```text
catalogRevision 1846 -> 1847
```

Solo descarga ese delta.

---

## 8. Dónde se sirve el manifiesto

El manifiesto no debe consultar Azure SQL.

Opciones:

1. Blob Storage versionado.
2. Azure Front Door/CDN delante de Blob.
3. Redis como respaldo para manifiestos dinámicos.
4. Endpoint de sincronización que use cache y `ETag`, nunca una consulta comercial.

Recomendación:

```text
Azure SQL
   -> Outbox
   -> Delta Builder
   -> Blob Storage
   -> Front Door/CDN
   -> cajas
```

El administrador modifica un producto una vez. El backend genera el delta y reemplaza el manifiesto estático. Las cajas leen desde almacenamiento/cache, no desde la base.

---

## 9. ETag y respuestas pequeñas

La caja envía:

```http
If-None-Match: "manifest-1847-933-71-28-8"
```

Si nada cambió:

```http
304 Not Modified
```

No se envía JSON completo, no se consulta SQL y no se calcula catálogo.

Si cambió:

```http
200 OK
ETag: "manifest-1848-934-71-28-8"
```

La caja descarga únicamente los paquetes modificados.

---

## 10. Frecuencia adaptativa

No todas las cajas consultan al mismo tiempo ni con la misma frecuencia.

Propuesta inicial:

| Estado | Frecuencia |
|---|---|
| Turno abierto y POS activo | 60 segundos |
| Turno abierto sin actividad | 3 a 5 minutos |
| Pantalla administrativa | 2 a 5 minutos |
| Sin turno | 10 a 30 minutos |
| Sin internet | backoff progresivo |
| Al recuperar conexión | inmediato |
| Al abrir turno | inmediato |
| Antes de primera venta | inmediato |

Cada intervalo agrega `jitter` aleatorio para evitar que miles de cajas consulten en el mismo segundo.

Ejemplo:

```text
60 segundos ± 15 segundos
```

---

## 11. Escalabilidad

### Ejemplo

100.000 cajas activas con comprobación promedio cada 60 segundos:

```text
≈ 1.667 solicitudes por segundo
```

Eso sería elevado para una API que consulta SQL, pero es un patrón normal para:

- CDN;
- Front Door;
- Blob Storage;
- objetos pequeños cacheados;
- respuestas `304`.

Además:

- muchas cajas del mismo negocio consultan el mismo objeto;
- Front Door puede responder desde cache;
- no se ejecuta lógica de producto;
- no se transmite inventario;
- no se descarga delta si no cambió;
- cajas inactivas reducen frecuencia.

### Partición

Manifiestos por ámbito:

```text
/tenant/{tenantId}/business/{businessId}/catalog-manifest
/tenant/{tenantId}/price-list/{priceListId}/pricing-manifest
/tenant/{tenantId}/cash-register/{cashRegisterId}/config-manifest
```

Una caja consulta únicamente los manifiestos que le corresponden.

---

## 12. Generación de cambios

### Transacción

Cambiar producto guarda:

- producto;
- códigos;
- precios/configuración aplicable;
- revisión;
- outbox.

### Delta Builder

Una Function procesa el evento:

- agrupa cambios;
- identifica scope;
- crea delta;
- comprime;
- calcula hash;
- lo guarda;
- actualiza manifiesto.

Importar mil precios genera:

- una revisión;
- uno o pocos paquetes;
- no mil consultas posteriores.

---

## 13. Tipos de deltas

### Catálogo

- producto nuevo;
- nombre/descripción;
- referencia;
- códigos;
- unidades;
- categoría;
- activo/inactivo;
- producto por peso.

### Precios

- precio base;
- lista;
- vigencia;
- promoción;
- descuento por cantidad.

### Impuestos

- perfil;
- tasa;
- vigencia;
- reglas.

### Caja

- negativos;
- medios;
- permisos locales;
- impresión;
- operación offline.

### Balanza

- patrones;
- producto;
- configuración de periférico;
- precisión.

No existe delta de inventario para POS.

---

## 14. Aplicación local

Cuando detecta cambio:

1. descarga delta;
2. valida tenant;
3. valida scope;
4. verifica hash y firma;
5. abre transacción SQLite;
6. actualiza registros afectados;
7. reconstruye índices necesarios;
8. guarda checkpoint;
9. activa versión;
10. informa estado.

Si falla:

- revierte transacción;
- conserva versión anterior;
- reintenta;
- no deja producto parcialmente actualizado.

---

## 15. Precios durante una venta

Si el precio cambia mientras hay productos en pantalla:

- líneas existentes conservan el precio mostrado;
- nuevos escaneos usan el precio nuevo;
- Auraly puede avisar que existen diferencias;
- nunca repricia silenciosamente.

Al recuperar una venta temporal:

- conserva precios;
- muestra si existe una versión nueva;
- permite actualizar con acción explícita.

El servidor conserva el precio realmente cobrado aunque la venta haya sido offline.

---

## 16. Cambios urgentes

Un producto puede necesitar bloqueo inmediato.

Sin SignalR, opciones:

### SLA normal

Propagación dentro del intervalo activo, aproximadamente un minuto.

### Revisión antes de cobrar

Cuando online, la caja puede consultar solamente el manifiesto antes del pago. Es una petición cacheada, no una consulta de inventario o productos.

### Configuración crítica

La caja consulta manifiestos críticos con mayor frecuencia:

- producto retirado;
- impuestos;
- caja bloqueada;
- resolución fiscal.

### Push futuro

SignalR/Web PubSub se puede añadir como:

```text
“despierta ahora y consulta el manifiesto”
```

Nunca transportaría el catálogo ni reemplazaría checkpoints.

---

## 17. Seguridad

- dispositivo registrado;
- acceso limitado al tenant y scopes;
- manifiestos firmados;
- paquetes con hash;
- URLs temporales o gateway autorizado;
- SQLite cifrado;
- checkpoint auditable;
- revocación de caja;
- no exponer rutas de otros negocios;
- no incluir costos ni datos que la caja no necesita.

---

## 18. Observabilidad

El portal muestra:

- revisión central;
- revisión por caja;
- última consulta;
- última actualización;
- deltas pendientes;
- catálogo desactualizado;
- precios desactualizados;
- error;
- tiempo offline.

Ejemplo:

| Caja | Catálogo | Precio | Última revisión | Estado |
|---|---:|---:|---|---|
| Caja 1 | 1848 | 934 | hace 20 s | Actualizada |
| Caja 2 | 1847 | 934 | hace 2 min | Catálogo pendiente |
| Caja 3 | 1841 | 929 | hace 1 día | Offline |

---

## 19. Criterios de aceptación

- la caja no tiene tablas de existencia;
- una venta de otra caja no produce tráfico de inventario;
- consultar existencia requiere conexión;
- escanear funciona sin conexión;
- cambiar un precio incrementa revisión;
- la caja detecta el cambio sin consultar SQL;
- si nada cambió recibe `304`;
- descarga únicamente productos afectados;
- mil cambios se agrupan;
- jitter evita estampidas;
- delta fallido conserva versión anterior;
- reiniciar conserva catálogo y precios;
- precio ya mostrado no cambia silenciosamente;
- SignalR no es requisito para operar;
- el sistema puede añadir push después sin rediseñar deltas.

---

## 20. Recomendación final

Para el MVP:

```text
Catálogo/precios/configuración:
    SQLite local + manifiestos + deltas

Inventario:
    solo Azure + consulta online bajo demanda

Detección:
    ETag adaptativo desde Blob/Front Door

Tiempo real:
    no SignalR inicialmente
```

Esta arquitectura es más simple, económica y escalable para muchos negocios y cajas.

SignalR solo se justificaría cuando exista un requerimiento medido de propagación en segundos o control bidireccional permanente. Antes de eso, un manifiesto cacheado de pocos bytes resuelve el problema con mucha menos infraestructura.
