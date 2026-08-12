# Decisión: sincronización inicial automática del POS

## Prevalencia

Este documento corrige cualquier especificación anterior que describa la primera sincronización como manual.

La decisión definitiva es:

> Cuando una caja abre el módulo de Facturación por primera vez y no tiene un catálogo local válido, la sincronización inicial comienza automáticamente. No requiere que el cajero presione un botón.

Después de completar esa primera instalación, la caja descarga únicamente cambios incrementales.

---

## Experiencia de apertura

1. POS Edge valida el aprovisionamiento del dispositivo.
2. Comprueba si existe un catálogo válido para la empresa, negocio, caja, bodega y lista de precios asignados.
3. Si no existe, muestra **Preparando facturación** e inicia la sincronización automáticamente.
4. Muestra progreso, elementos descargados, velocidad y reintentos.
5. Descarga, valida e instala el catálogo.
6. Recupera los cambios ocurridos durante la descarga.
7. Guarda el checkpoint actual.
8. Habilita Facturación.

No debe presentarse una grilla de venta vacía ni un botón obligatorio **Sincronizar ahora**. Si el proceso falla, sí debe existir **Reintentar** y una vista técnica para soporte.

La primera preparación requiere conexión. Cuando ya existe un snapshot válido, la caja puede abrir y vender offline según sus políticas.

---

## Viabilidad con muchos productos

Es viable incluso con catálogos grandes, siempre que la API no consulte y serialice el catálogo completo individualmente para cada caja.

### Arquitectura

```text
Catalog + Pricing
        |
        v
PosSync crea proyección vendible en revisión R
        |
        v
Snapshot inmutable dividido y comprimido
        |
        v
Azure Blob Storage
        |
        v
Caja descarga bloques directamente
        |
        v
Staging local -> validación -> intercambio atómico
        |
        v
Aplicar deltas desde R -> revisión actual
        |
        v
Facturación habilitada
```

La API entrega un manifiesto pequeño y direcciones temporales autorizadas. Los archivos grandes se descargan directamente desde Blob Storage; no mantienen ocupada una Azure Function ni consumen conexiones largas de la API.

Varias cajas que compartan empresa, negocio, alcance de catálogo y lista de precios reutilizan el mismo snapshot base. La configuración exclusiva de cada caja se descarga como un paquete pequeño separado.

### Contenido mínimo

El snapshot contiene:

- productos vendibles;
- referencias y campos de búsqueda;
- códigos de barras;
- unidades y presentaciones;
- reglas de balanza;
- impuestos necesarios para calcular;
- listas y reglas de precios asignadas;
- promociones soportadas;
- configuración operativa mínima.

No contiene:

- inventario;
- movimientos;
- costos históricos;
- fotografías;
- descripciones extensas no usadas por el POS;
- reportes;
- información de otros negocios.

### Medidas de rendimiento

- bloques paginados y comprimidos con Brotli o gzip;
- descargas reanudables por bloque;
- hashes por bloque y hash final;
- inserciones locales por lotes;
- creación de índices después de la carga masiva;
- tablas staging;
- intercambio atómico;
- reintentos con espera aleatoria;
- límite de descargas concurrentes por caja;
- reutilización y caché de snapshots;
- generación anticipada del snapshot cuando cambia una revisión importante.

No se debe habilitar el POS con medio catálogo: el lector podría reportar falsamente que un código no existe. Primero se instala todo el conjunto vendible asignado. Solo los datos secundarios que no intervienen en búsqueda, precio o cálculo pueden continuar en segundo plano.

---

## Consistencia durante una descarga larga

Si el snapshot corresponde a la revisión `R`, el sistema:

1. instala completamente `R`;
2. solicita el change feed posterior a `R`;
3. aplica esos deltas en orden e idempotentemente;
4. guarda el checkpoint más reciente;
5. abre Facturación.

De esta forma, los productos o precios que cambien mientras la caja descarga no se pierden.

---

## Después de la primera sincronización

El funcionamiento normal será:

1. POS Edge abre una conexión saliente con Azure Web PubSub.
2. El servidor avisa únicamente cambios que afectan a esa caja.
3. La caja obtiene por HTTP el producto, precio o configuración puntual.
4. Aplica el cambio y avanza su checkpoint.
5. Al reconectar, consulta el change feed desde el último checkpoint para cubrir avisos perdidos.

Web PubSub reduce la latencia; el change feed persistente conserva la confiabilidad.

Una resincronización completa posterior solo se exige por:

- cambio de empresa, negocio, caja o lista de precios;
- reprovisionamiento;
- cambio incompatible del esquema local;
- checkpoint fuera de la retención;
- corrupción detectada;
- orden explícita de soporte.

La reconstrucción posterior ocurre en staging y conserva el catálogo anterior hasta que el nuevo quede validado.

---

## Estados mínimos

```text
NotProvisioned
InitialSyncRequired
InitialSyncRunning
ApplyingCatchUpDeltas
CatalogReady
RecoveringDeltas
ResyncRequired
SyncFailed
```

El módulo de Facturación solamente habilita la captura cuando llega a `CatalogReady`.

---

## Criterios de aceptación

- La primera apertura dispara la sincronización sin intervención del cajero.
- Una apertura normal posterior no vuelve a descargar todo.
- Varias cajas pueden reutilizar el mismo snapshot.
- La API no transmite directamente archivos grandes.
- Una descarga interrumpida se reanuda.
- Un error no elimina un catálogo válido anterior.
- Los borradores y el outbox local nunca se reemplazan junto con el catálogo.
- El inventario no forma parte de la sincronización.
- Los cambios ocurridos durante la descarga se aplican antes de abrir Facturación.
- Un cambio de un solo producto genera un delta, no un snapshot completo.
