# Arquitectura modular .NET y primera sincronización del POS

## Estado de esta decisión

Este documento complementa y, en caso de contradicción, prevalece sobre los documentos anteriores en estos puntos:

1. La primera descarga del catálogo local de una caja es manual y se inicia al abrir el módulo de facturación.
2. Después de esa descarga inicial, la caja recibe y recupera solamente cambios incrementales.
3. El backend se despliega inicialmente dentro de la misma API de Auraly, pero cada contexto funcional queda separado en librerías .NET, contratos, esquema de base de datos y límites de dependencia.
4. La caja no descarga ni conserva inventario. El inventario se consulta en línea únicamente cuando una regla de la operación lo requiere.

---

## 1. Decisión ejecutiva

Auraly Commerce debe construirse como un **monolito modular**, no como un conjunto de microservicios desde el MVP y tampoco como una sola aplicación con capas globales gigantes.

El despliegue inicial tendrá:

- una API;
- una base de datos Azure SQL;
- uno o más hosts de Azure Functions para procesos asíncronos;
- Azure Web PubSub para notificaciones hacia las cajas;
- librerías .NET independientes por contexto funcional;
- un esquema SQL y un `DbContext` propietario por contexto;
- contratos versionados para todas las conversaciones entre módulos.

Esta estructura da velocidad hoy y deja una ruta real de extracción a microservicios. La facilidad futura no provendrá únicamente de poner el código en proyectos distintos: dependerá de impedir referencias indebidas, cruces de tablas, entidades compartidas y llamadas directas entre infraestructuras.

---

## 2. Primera sincronización manual del módulo de facturación

### 2.1. Experiencia esperada

La primera vez que una caja abre Facturación POS:

1. El POS Edge comprueba si existe un catálogo local válido para la empresa, negocio, caja, bodega y lista de precios asignados.
2. Si no existe, muestra una pantalla de preparación, no la grilla de venta vacía.
3. La pantalla presenta:
   - empresa y negocio;
   - caja y bodega asociada;
   - lista de precios;
   - fecha de la última sincronización, si aplica;
   - cantidades estimadas de productos, códigos y precios;
   - botón **Sincronizar ahora**.
4. El usuario inicia manualmente la sincronización.
5. El sistema descarga, valida e instala el catálogo.
6. Solo después de una instalación correcta habilita la facturación.

La descarga inicial no debe arrancar silenciosamente. Así el cajero o administrador sabe que la caja se está preparando, puede verificar su configuración y puede distinguir una sincronización de un bloqueo de la aplicación.

### 2.2. Qué descarga

La caja descarga únicamente información necesaria para vender y operar sin conexión:

- productos activos y vendibles;
- nombres, referencias, descripciones cortas y campos de búsqueda;
- códigos de barras;
- presentaciones y unidades;
- reglas de lectura de balanza;
- impuestos y perfiles tributarios necesarios para calcular la venta;
- listas y reglas de precios asignadas a la caja;
- promociones admitidas por el MVP;
- configuración operativa de la caja;
- medios de pago habilitados;
- datos mínimos de clientes requeridos para búsquedas offline, si se decide habilitar ese alcance;
- versiones y revisiones de cada conjunto de datos.

No descarga:

- existencias por bodega;
- movimientos de inventario;
- costos históricos;
- documentos de otras cajas;
- reportes analíticos;
- información de otros negocios que la caja no necesite.

### 2.3. Instalación segura del catálogo

La sincronización inicial no debe escribir directamente sobre las tablas locales que usa la venta. El flujo es:

```text
Solicitar manifiesto
        |
        v
Descargar snapshot por páginas
        |
        v
Guardar en tablas staging
        |
        v
Validar conteos + versión + hashes
        |
        v
Crear índices de búsqueda
        |
        v
Intercambio atómico staging -> activo
        |
        v
Guardar checkpoint y habilitar Facturación
```

Requisitos:

- reanudación de una descarga interrumpida;
- paginación y compresión;
- idempotencia;
- validación de versión de esquema;
- intercambio atómico;
- conservación del catálogo anterior si una resincronización falla;
- separación absoluta entre catálogo, borradores y cola de ventas pendientes;
- registro del usuario, dispositivo, duración, versión y resultado.

Las tablas o la base local de `Drafts` y `Outbox` nunca se reemplazan al instalar un catálogo.

### 2.4. Funcionamiento después de la primera sincronización

Una vez instalado el snapshot:

1. POS Edge abre su conexión saliente con Azure Web PubSub.
2. El servidor publica notificaciones cuando cambia un producto, código, precio o configuración relevante.
3. La notificación contiene identificadores y revisiones; no necesita contener toda la entidad.
4. La caja solicita el delta concreto por HTTP, lo aplica de forma idempotente y avanza su checkpoint.
5. Al reconectar o abrir nuevamente el módulo, la caja consulta desde su último checkpoint para recuperar cambios que no recibió.

Por tanto, Web PubSub reduce la latencia, mientras que el change feed persistente garantiza la recuperación. El socket no es la fuente de verdad.

### 2.5. Cuándo se requiere otra sincronización completa

El sistema conserva una acción administrativa **Reconstruir catálogo local**, pero no debe usarla como mecanismo normal.

Una resincronización completa puede exigirse cuando:

- cambió de empresa, negocio, caja o lista de precios;
- la caja fue reprovisionada;
- cambió de forma incompatible el esquema local;
- el checkpoint quedó por fuera de la ventana de retención del feed;
- se detectó corrupción;
- soporte la ordenó explícitamente.

Si existe un catálogo anterior válido, la reconstrucción ocurre en staging y la caja conserva el catálogo activo hasta completar el reemplazo.

### 2.6. Estados mínimos

```text
NotProvisioned
ReadyForInitialSync
InitialSyncRunning
CatalogReady
RecoveringDeltas
ResyncRequired
SyncFailed
```

Facturación solo puede abrir en `CatalogReady`. Durante una venta ya abierta, una actualización de catálogo no debe alterar retroactivamente líneas capturadas: cada línea conserva el precio, impuesto y descripción usados cuando se agregó, salvo que el cajero ejecute explícitamente una recotización autorizada.

---

## 3. Inventario y operación offline

La caja no conoce el inventario local.

- Si la política de la caja permite negativos, captura la línea sin consultar disponibilidad.
- Si la política bloquea negativos, consulta en línea al buscar/agregar el producto y cada vez que cambia la cantidad.
- La confirmación conserva una validación transaccional final para evitar carreras entre cajas, pero esa no reemplaza la validación temprana.
- Si la caja está offline y su política exige disponibilidad, se aplica la política offline configurada: bloquear, exigir autorización o vender sin validación.

No se enviarán cambios de inventario por Web PubSub ni se incluirán en el snapshot inicial.

---

## 4. Estructura física de la solución .NET

### 4.1. Convención recomendada

Para conservar la convención solicitada `Auraly.Application.xx`, cada contexto tendrá proyectos propios:

```text
src/
  Hosts/
    Auraly.Api/
    Auraly.Workers/
    Auraly.Functions/

  Modules/
    Catalog/
      Auraly.Domain.Catalog/
      Auraly.Application.Catalog/
      Auraly.Infrastructure.Catalog/
      Auraly.Contracts.Catalog/

    Pricing/
      Auraly.Domain.Pricing/
      Auraly.Application.Pricing/
      Auraly.Infrastructure.Pricing/
      Auraly.Contracts.Pricing/

    Inventory/
      Auraly.Domain.Inventory/
      Auraly.Application.Inventory/
      Auraly.Infrastructure.Inventory/
      Auraly.Contracts.Inventory/

  Shared/
    Auraly.SharedKernel/
    Auraly.BuildingBlocks/

tests/
  Architecture/
    Auraly.Tests.Architecture/
  Modules/
    Catalog/
      Auraly.Tests.Catalog.Unit/
      Auraly.Tests.Catalog.Integration/
```

El nombre del proyecto y su `RootNamespace` deben coincidir. No se debe crear un gran `Auraly.Application`, `Auraly.Domain` o `Auraly.Infrastructure` donde terminen mezclados todos los módulos.

### 4.2. Contextos iniciales

Los contextos se introducen según el orden del MVP, sin crear proyectos vacíos meses antes de necesitarlos:

| Contexto | Responsabilidad principal |
|---|---|
| Organization | tenant, empresa, negocio, sedes y configuración base |
| Parties | clientes, proveedores e identificación |
| Catalog | producto, códigos de barras, unidades, presentaciones, balanza y atributos |
| Pricing | listas, precios, descuentos y promociones admitidas |
| Inventory | bodegas, saldos, kardex, conteos, traslados y averías |
| Purchasing | compras y entradas de mercancía |
| Payables | cuentas por pagar y aplicaciones |
| Sales | borradores, facturas, líneas, descuentos y medios de pago |
| Cash | cajas, apertura, cierre, movimientos y parámetros de caja |
| Receivables | cuentas por cobrar, abonos y aplicaciones |
| Returns | devoluciones de venta y de compra |
| Fiscal | numeración, documento electrónico DIAN, firma, envío y estados |
| PosSync | dispositivos, snapshots, change feed, checkpoints y outbox de cajas |
| Reporting | proyecciones de ventas, compras, utilidad y rangos |

No todos necesitan extraerse algún día. La separación permite extraer únicamente los que lo justifiquen, por ejemplo Fiscal, PosSync o Reporting.

### 4.3. Reglas de referencias

```text
Contracts  -----> BuildingBlocks mínimo

Domain     -----> SharedKernel mínimo

Application -----> Domain
Application -----> Contracts propios
Application -----> Contracts públicos de otros módulos, solo si es inevitable

Infrastructure --> Application
Infrastructure --> Domain

Api/Functions ---> métodos de registro de cada módulo
```

Reglas obligatorias:

1. `Domain` no referencia Entity Framework, Azure, HTTP, Functions ni otro módulo.
2. `Application` define los puertos que necesita; `Infrastructure` los implementa.
3. Ningún módulo referencia `Infrastructure` de otro módulo.
4. Ningún módulo usa el `DbContext`, repositorio o entidad EF de otro.
5. La comunicación funcional ocurre mediante contratos públicos, comandos expuestos o eventos de integración.
6. Los contratos no publican entidades internas del dominio.
7. `SharedKernel` solo contiene conceptos verdaderamente universales como `Money`, identificadores base, `Result` y eventos; no contiene Producto, Factura, Bodega ni Cliente.
8. No se permite un repositorio genérico compartido que oculte los límites del dominio.

Estas reglas deben verificarse mediante pruebas de arquitectura en CI, por ejemplo con `NetArchTest` o `ArchUnitNET`.

### 4.4. Registro en el mismo host

La API es únicamente el composition root:

```csharp
builder.Services
    .AddCatalogModule(configuration)
    .AddPricingModule(configuration)
    .AddInventoryModule(configuration)
    .AddSalesModule(configuration)
    .AddFiscalModule(configuration)
    .AddPosSyncModule(configuration);

app.MapCatalogEndpoints();
app.MapPricingEndpoints();
app.MapInventoryEndpoints();
app.MapSalesEndpoints();
app.MapFiscalEndpoints();
app.MapPosSyncEndpoints();
```

Los métodos anteriores pertenecen a cada módulo. El host no construye repositorios ni contiene reglas del negocio.

---

## 5. Separación de datos

### 5.1. Un esquema y un DbContext por contexto

Aunque todos residan inicialmente en Azure SQL:

```text
org.*
parties.*
catalog.*
pricing.*
inventory.*
purchasing.*
payables.*
sales.*
cash.*
receivables.*
returns.*
fiscal.*
possync.*
reporting.*
```

Ejemplos:

- `CatalogDbContext` solo mapea `catalog.*`;
- `InventoryDbContext` solo mapea `inventory.*`;
- `FiscalDbContext` solo mapea `fiscal.*`;
- cada módulo conserva sus propias migraciones.

### 5.2. Cruces prohibidos

- No usar navegación EF entre módulos.
- No escribir directamente en tablas de otro esquema.
- No reutilizar una misma entidad persistente en dos contextos.
- No realizar lógica de comandos mediante joins entre esquemas.
- No permitir que Reporting se convierta en una puerta trasera para modificar datos.

Un módulo guarda el identificador externo que necesita y mantiene su propia representación mínima. Las proyecciones de Reporting sí pueden combinar información porque son modelos de lectura reconstruibles.

### 5.3. Transacciones y consistencia

En el MVP, una operación crítica puede coordinar varios módulos dentro de la misma instancia y base de datos, pero debe hacerlo a través de interfaces de aplicación y una transacción explícita, no manipulando tablas ajenas.

Además:

- todo efecto asíncrono se registra en un outbox dentro de la misma transacción;
- todo consumidor mantiene inbox/idempotencia;
- los eventos incluyen `EventId`, `TenantId`, `CorrelationId`, `CausationId`, `OccurredAtUtc` y versión;
- las integraciones externas, incluida DIAN, nunca forman parte de una transacción SQL abierta;
- la extracción futura reemplazará la coordinación local por una saga u orquestación sin reescribir el dominio.

---

## 6. Contratos entre módulos

Cada módulo expone una superficie pequeña:

```text
Auraly.Contracts.Catalog
  ProductChangedV1
  ProductSellingDataV1
  GetProductSellingData

Auraly.Contracts.Inventory
  CheckAvailability
  AvailabilityCheckedV1
  CommitSaleInventory

Auraly.Contracts.Fiscal
  RequestElectronicInvoiceV1
  ElectronicInvoiceAcceptedV1
  ElectronicInvoiceRejectedV1

Auraly.Contracts.PosSync
  CatalogSnapshotManifestV1
  CatalogChangeEnvelopeV1
  DeviceCheckpointV1
```

No se debe asumir que, por ejecutarse hoy en proceso, el contrato puede ser una clase rica con referencias al dominio. Debe poder serializarse y versionarse desde el principio.

---

## 7. Diseño del módulo PosSync

### 7.1. Responsabilidades

`PosSync` es propietario de:

- registro y aprovisionamiento de dispositivos;
- asociación del dispositivo con caja y ámbito de datos;
- generación de manifiestos de snapshot;
- generación o materialización de páginas del catálogo;
- change feed durable;
- checkpoints por dispositivo;
- autorización de descargas;
- revisión mínima disponible;
- orden de resincronización;
- publicación de avisos mediante Web PubSub;
- recepción idempotente del outbox de ventas offline.

No es propietario de Producto o Precio. Obtiene proyecciones de venta publicadas por Catalog y Pricing.

### 7.2. Modelo de cambios

```text
CatalogRevision
ChangeId
TenantId
BusinessId
EntityType
EntityId
Operation
EntityRevision
AudienceKey
OccurredAtUtc
PayloadVersion
```

`AudienceKey` permite dirigir el cambio solo a cajas afectadas por negocio, lista de precios u otra configuración. El servidor no publica todos los cambios a todas las cajas.

### 7.3. Endpoints conceptuales

```text
POST /pos/devices/{deviceId}/initial-sync/manifest
GET  /pos/sync/snapshots/{snapshotId}/pages/{page}
GET  /pos/devices/{deviceId}/changes?after={checkpoint}
POST /pos/devices/{deviceId}/checkpoints
POST /pos/devices/{deviceId}/outbox
POST /pos/devices/{deviceId}/request-resync
```

La primera llamada es iniciada por el botón **Sincronizar ahora**. Las consultas incrementales posteriores son automáticas.

---

## 8. Ruta real hacia microservicios

Un módulo estará listo para extraerse cuando:

1. no tenga referencias a infraestructura o entidades internas de otros módulos;
2. sea propietario de sus tablas y migraciones;
3. sus llamadas públicas tengan contratos versionados;
4. publique eventos mediante outbox;
5. consuma eventos idempotentemente;
6. tenga pruebas unitarias y de integración propias;
7. su configuración se registre mediante una única extensión;
8. su API pueda mapearse sin conocer detalles del host.

La extracción consistirá principalmente en:

1. mover sus proyectos a una solución o servicio;
2. mover su esquema a una base propia;
3. sustituir el bus en proceso por Azure Service Bus;
4. enrutar sus endpoints mediante gateway;
5. migrar datos y cambiar configuración.

Si para extraerlo hay que desenredar navegaciones EF, joins, repositorios compartidos o entidades comunes, la separación inicial habrá sido solamente cosmética.

---

## 9. Estrategia de implementación

### Fase 0: reglas y esqueleto

- crear convenciones de proyectos y namespaces;
- crear `SharedKernel` mínimo;
- crear pruebas de arquitectura;
- definir contrato de eventos, outbox e inbox;
- establecer esquema por módulo;
- documentar las reglas en `AGENTS.md`.

### Fase 1: venta mínima con catálogo

- Catalog;
- Pricing;
- Cash y parámetros de caja;
- Sales con borradores, recuperación, eliminación, descuentos, búsquedas y medios de pago;
- balanza;
- grilla optimizada para lector y teclado.

### Fase 2: PosSync y offline

- aprovisionamiento;
- sincronización inicial manual;
- base local;
- deltas;
- Web PubSub;
- outbox de ventas;
- resolución de duplicados;
- operación offline.

### Fase 3: inventario y compras

- Inventory;
- Purchasing;
- Payables;
- traslados, conteos y averías;
- validación temprana de existencias según política de caja.

### Fase 4: cartera, devoluciones y fiscal

- Receivables;
- Returns;
- Fiscal DIAN propio;
- estados, reintentos, contingencias y trazabilidad.

### Fase 5: reportes mínimos

- ventas;
- compras;
- utilidad;
- comparaciones por rango de meses;
- empresa y negocio;
- proyecciones reconstruibles.

---

## 10. Modelo y effort recomendados en Codex

### Recomendación única

Si se va a mantener una sola configuración durante la implementación:

> **Modelo: `gpt-5.6-sol` — Reasoning effort: `xhigh`.**

La razón es que este trabajo no es CRUD aislado. Combina rediseño de un ERP existente, límites de dominio, concurrencia de inventario, sincronización offline, firma y facturación DIAN, migraciones y decisiones que pueden crear deuda estructural si se resuelven de forma local.

`xhigh` es preferible como valor habitual porque conserva alta profundidad sin pagar el costo máximo en cada cambio mecánico.

### Ajuste por tipo de tarea

| Trabajo | Modelo | Effort |
|---|---|---|
| arquitectura base, límites y migraciones | `gpt-5.6-sol` | `max` |
| DIAN, criptografía, idempotencia, offline y concurrencia | `gpt-5.6-sol` | `max` |
| implementación normal de un módulo | `gpt-5.6-sol` | `xhigh` |
| endpoints, mapeos, pruebas y tareas repetitivas ya definidas | `gpt-5.6-terra` | `high` |
| revisión cruzada antes de integrar un hito | `gpt-5.6-sol` | `xhigh` o `max` |

No se recomienda `max` para todo el proyecto: aumenta tiempo y consumo en tareas donde la arquitectura ya decidió el camino. Tampoco se recomienda iniciar la base con un modelo rápido o effort bajo; una falsa economía en esta fase puede producir acoplamientos costosos.

### Forma de trabajar con Codex

No entregar todo el ERP en una única instrucción. Trabajar por hitos verticales verificables:

1. un contexto o flujo de negocio por tarea;
2. criterios de aceptación explícitos;
3. documentos de Xion que deben inspeccionarse;
4. tablas y reglas que se reutilizan conceptualmente;
5. pruebas que deben quedar verdes;
6. prohibiciones de dependencia;
7. revisión del diff y prueba de arquitectura antes de continuar.

El primer encargo de implementación debe ser **Fase 0 + esqueleto de Catalog/Pricing/PosSync**, sin construir todavía todos los módulos. Así se valida que la separación es real antes de multiplicar el patrón.

Fuentes oficiales consultadas para la selección:

- OpenAI, [Model selection](https://developers.openai.com/api/docs/guides/latest-model)
- OpenAI, [Models](https://developers.openai.com/api/docs/models)

---

## 11. Criterios de aceptación de esta arquitectura

- Una caja nueva no factura hasta que un usuario complete manualmente la primera sincronización.
- Una caja ya preparada no repite el snapshot; recupera deltas desde su checkpoint.
- Un cambio de un solo producto no obliga a descargar el catálogo completo.
- La caja no almacena ni sincroniza inventario.
- Los borradores y ventas pendientes sobreviven a una reconstrucción del catálogo.
- Cada contexto tiene Domain, Application, Infrastructure y Contracts propios.
- Ningún `DbContext` mapea tablas de otro contexto.
- Ningún módulo referencia Infrastructure de otro.
- Las pruebas de arquitectura impiden violaciones en CI.
- La API actúa como host y composition root, no como capa de negocio.
- Fiscal, PosSync o Reporting pueden extraerse sin mover entidades de otros módulos.
