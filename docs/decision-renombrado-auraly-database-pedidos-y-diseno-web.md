# Decisión: renombrado Auraly, proyecto de base de datos, Pedidos y diseño web

## Prevalencia

Este documento complementa las decisiones anteriores y reemplaza cualquier instrucción que:

- conserve nombres `Auraly`, `Mimos`, `Talkio` o `TalkioAI`;
- use migraciones de Entity Framework para modificar la base;
- trate Pedidos únicamente como integración externa o listado de consulta;
- proponga copiar visualmente las pantallas WinForms de Xion.

---

## 1. Renombrado total a Auraly

La solución, proyectos, ensamblados, namespaces, carpetas y textos técnicos deben utilizar la marca `Auraly`.

### 1.1. Nombres finales

| Actual | Final |
|---|---|
| `Auraly.Commerce.sln` | `Auraly.Commerce.sln` |
| `Auraly.WebAPI` | `Auraly.Api` |
| `Auraly.Platform.Worker` — actualmente Azure Functions | `Auraly.Functions` |
| `Auraly.Platform.Console` | `Auraly.Tools.Console` |
| `Auraly.Platform.Tests` | `Auraly.Tests` |
| `Auraly.Database.sln` | `Auraly.Database.sln` |
| `Auraly.Database.sqlproj` | `Auraly.Database.sqlproj` |
| `Auraly.Platform.Domain` | `Auraly.Domain.Platform` como transición |
| `Auraly.Platform.Application` | `Auraly.Application.Platform` como transición |
| `Auraly.Platform.Infrastructure` | `Auraly.Infrastructure.Platform` como transición |
| `HashGen` | `Auraly.Tools.HashGen` |
| `TestOpenAI` | `Auraly.Tests.OpenAI` o se elimina si ya no tiene propósito |

Los proyectos `Platform` son transitorios para el código actual. Los nuevos módulos y el código que se vaya extrayendo usarán:

```text
Auraly.Domain.Orders
Auraly.Application.Orders
Auraly.Infrastructure.Orders
Auraly.Contracts.Orders

Auraly.Domain.Sales
Auraly.Application.Sales
Auraly.Infrastructure.Sales
Auraly.Contracts.Sales
```

No se agregará funcionalidad nueva de Commerce dentro de las librerías transitorias `Platform`.

### 1.2. Alcance de la eliminación de nombres anteriores

La revisión debe cubrir:

- nombres de archivos y directorios;
- `.sln`, `.csproj` y `.sqlproj`;
- `AssemblyName` y `RootNamespace`;
- namespaces, `using` y nombres completamente calificados;
- referencias entre proyectos;
- nombres de paquetes;
- textos como `Creado por Talkio`;
- configuración, variables y secretos cuyos nombres contengan la marca anterior;
- Dockerfiles y archivos de despliegue;
- workflows de CI/CD;
- nombres de artefactos;
- scripts PowerShell;
- documentación;
- nombres visibles en Swagger, logs y telemetría;
- proyectos de pruebas y namespaces de pruebas;
- recursos Azure, evaluando migración controlada si el nombre físico no puede cambiarse sin recrearlos.

El criterio de aceptación será:

```powershell
rg -i "mimos|mimosbabyspa|talkio|talkioai" .
```

El resultado debe quedar vacío, exceptuando únicamente archivos históricos que se hayan decidido conservar fuera de compilación y que estén expresamente documentados. La preferencia es no conservar excepciones.

### 1.3. Ejecución segura

El renombrado debe hacerse como un hito independiente antes de incorporar los módulos grandes.

Orden:

1. establecer una línea base con build y pruebas verdes;
2. renombrar solución, proyectos y carpetas;
3. actualizar referencias de proyectos;
4. cambiar namespaces y nombres de ensamblado;
5. cambiar configuración, scripts y CI;
6. renombrar el proyecto SQL y scripts de despliegue;
7. eliminar textos de marca antigua;
8. compilar backend, Functions, herramientas y frontend;
9. ejecutar pruebas;
10. ejecutar búsqueda global de nombres prohibidos.

No debe mezclarse este cambio mecánico con rediseños de tablas o reglas del negocio porque haría imposible revisar el diff con seguridad.

---

## 2. Base de datos: una base y un solo proyecto SQL

### 2.1. Fuente de verdad

La base seguirá administrándose mediante:

```text
database/
  Auraly.Database.sln
  Auraly.Database/
    Auraly.Database.sqlproj
    Tables/
    Views/
    StoredProcedures/
    Functions/
    Scripts/
      PreDeployment.sql
      PostDeployment.sql
      Migrations/
      Seeds/
```

`Auraly.Database.sqlproj` es la fuente de verdad del esquema.

Se conserva:

- una sola base Azure SQL;
- esquema `dbo`;
- un único DACPAC;
- un único proceso de publicación;
- scripts predeployment y postdeployment;
- scripts explícitos para transformaciones de datos.

### 2.2. No habrá migraciones EF

Entity Framework se utiliza para mapear y consultar, no para gobernar el esquema.

Por tanto:

- no se ejecuta `Database.Migrate()`;
- no se generan migraciones con `dotnet ef migrations add`;
- no existe una carpeta `Migrations` de EF en Infrastructure;
- no se publican cambios de tablas desde la API;
- no se mantiene una tabla `__EFMigrationsHistory`;
- los paquetes EF Design/Tools se retiran de los proyectos donde solo se usaban para migraciones, después de verificar otros usos.

El proyecto SQL compila el DACPAC y el pipeline lo publica con los scripts de protección existentes.

### 2.3. Papel del DbContext

Existirá un solo `AuralyDbContext` de ejecución para simplificar:

- consultas;
- unit of work;
- seguimiento de cambios;
- transacciones entre pedidos, venta, caja, inventario y cartera;
- outbox.

El `DbContext` debe mantenerse alineado con el proyecto SQL mediante pruebas de integración. No tiene autoridad para crear o alterar el esquema.

### 2.4. Organización de tablas

Todas permanecen en `dbo`. La propiedad lógica se conserva mediante carpetas y documentación:

```text
Tables/
  Platform/
  Catalog/
  Pricing/
  Orders/
  Sales/
  Cash/
  Inventory/
  Purchasing/
  Payables/
  Receivables/
  Returns/
  Fiscal/
  PosSync/
  Reporting/
```

La ruta del archivo puede reflejar el módulo aunque el objeto continúe siendo `[dbo].[Orders]`.

---

## 3. Pedidos como módulo principal

### 3.1. Estado actual encontrado

Auraly ya contiene:

- `Orders`;
- `OrderItems`;
- `OrderDrafts`;
- `OrderDraftItems`;
- eventos de conexión;
- pedidos originados por bot, admin o API;
- pagos relacionados;
- integración `PedidosOkCommerceAdapter`;
- listado y detalle web;
- búsqueda de productos y clientes en Pedidos OK;
- consulta de historial;
- creación de pedidos externos con vendedor, ruta, empresa, bodega y equipo.

Esto es una base útil, pero el módulo web actual está orientado principalmente a consultar pedidos creados por otros flujos. Todavía no reemplaza la experiencia operativa de Pedidos OK.

### 3.2. Objetivo

`Orders` será el módulo unificado de toma y administración de pedidos de Auraly:

- vendedor;
- bot;
- administrador;
- API;
- dispositivo móvil;
- importación o integración externa.

Pedidos OK deja de ser el dueño funcional. Se convierte temporalmente en fuente de conocimiento y adaptador de compatibilidad mientras Auraly absorbe las capacidades.

### 3.3. Librerías

```text
Auraly.Domain.Orders
Auraly.Application.Orders
Auraly.Infrastructure.Orders
Auraly.Contracts.Orders
```

Dependencias:

- Orders usa contratos de Catalog para identificar productos;
- Orders usa Pricing para cotizar;
- Orders usa Parties para clientes;
- Orders consulta Inventory si la política lo exige;
- Orders solicita a Sales convertir un pedido en factura;
- Orders solicita a Receivables validaciones de crédito cuando corresponda;
- Orders no referencia Infrastructure de esos módulos.

---

## 4. Modelo funcional de Pedidos

### 4.1. Encabezado mínimo

Un pedido necesita campos explícitos, no esconder reglas centrales en `CustomAttributesJson`:

- `OrderId`;
- número interno legible;
- tenant;
- empresa y negocio;
- cliente;
- vendedor;
- ruta;
- bodega;
- lista de precios;
- origen;
- estado;
- moneda;
- fecha del pedido;
- fecha solicitada de entrega;
- dirección o modalidad de entrega;
- notas;
- subtotal;
- descuentos;
- impuestos;
- total;
- versión de concurrencia;
- dispositivo de origen;
- identificador e idempotencia externa;
- usuario creador y modificador;
- fechas UTC.

Los snapshots de nombre, documento, dirección y datos de producto se conservan porque un documento histórico no debe cambiar cuando se edita el maestro.

### 4.2. Líneas

Cada línea necesita:

- producto;
- código leído;
- SKU o referencia;
- unidad y factor de conversión;
- cantidad solicitada;
- cantidad atendida;
- cantidad cancelada;
- cantidad pendiente;
- precio unitario;
- origen del precio;
- descuento;
- impuesto;
- total;
- observación;
- orden de captura;
- revisión.

### 4.3. Estados

Estados mínimos:

```text
Draft
PendingConfirmation
Confirmed
Processing
PartiallyFulfilled
Fulfilled
Cancelled
Rejected
SyncPending
SyncFailed
```

La relación con facturación no debe expresarse únicamente cambiando el estado. Se registra el vínculo entre pedido, factura y cantidades atendidas para soportar facturación parcial futura.

### 4.4. Tablas complementarias

El diseño debe evaluar:

- `OrderStatusHistory`;
- `OrderInvoiceLinks`;
- `OrderItemFulfillments`;
- `OrderNotes`;
- `OrderSyncAttempts`;
- `OrderAssignments`;
- outbox e idempotency receipts.

No todas deben entrar al primer incremento, pero estado, auditoría, sincronización y vínculo con factura sí son parte del MVP.

---

## 5. Funcionalidades del MVP de Pedidos

### Captura

- crear pedido desde web;
- guardar borrador automáticamente;
- recuperar borrador;
- seleccionar o crear cliente;
- elegir bodega, vendedor, ruta y lista de precios según permisos;
- buscar por código de barras, SKU, referencia, nombre y alias;
- lector de código de barras con captura continua;
- balanza cuando el producto lo requiera;
- agregar producto y dejar el campo listo para el siguiente;
- edición de cantidad, precio autorizado y descuento;
- navegación completa por teclado;
- recálculo inmediato de línea y totales;
- eliminar línea;
- vaciar pedido con confirmación;
- observaciones de encabezado y línea;
- evitar duplicados por doble clic o reintento.

### Reglas comerciales

- precio por empresa, negocio, cliente y lista;
- impuestos;
- descuentos con permisos;
- validación de cliente;
- validación de cupo cuando la venta sea a crédito;
- consulta de inventario en línea si la política del pedido lo exige;
- permitir o bloquear cantidades no disponibles según configuración;
- no alterar las líneas ya capturadas cuando cambie el catálogo;
- recotización explícita y auditable.

### Ciclo de vida

- confirmar;
- cancelar con motivo;
- reabrir si el estado y permiso lo admiten;
- historial de cambios;
- convertir en factura;
- conservar relación pedido-factura;
- reflejar pendientes si la factura no atiende todo;
- notificar fallos de sincronización;
- reintentar operaciones idempotentemente.

### Offline

Para vendedores móviles o cajas:

- catálogo y precios locales mediante PosSync;
- clientes mínimos según alcance y seguridad;
- borradores locales;
- pedidos confirmados en outbox;
- identificadores generados en cliente;
- idempotency key;
- sincronización al recuperar red;
- resolución explícita de conflicto de precio, cliente o producto;
- nunca duplicar el pedido por reintento.

El inventario no se descarga. Si una política exige disponibilidad y no existe conexión, se aplica la política offline configurada.

### Consulta

- listado paginado;
- filtros por fechas, estado, cliente, vendedor, ruta, bodega y origen;
- búsqueda por número interno o externo;
- resumen de valor y cantidades;
- detalle completo;
- timeline;
- documentos y pagos relacionados;
- estado de sincronización;
- exportación autorizada.

---

## 6. Rediseño web del módulo Pedidos

No se copiarán formularios, botones, colores, ventanas ni distribución de Pedidos OK o Xion. Se reutilizan reglas, flujos, validaciones y conocimiento.

### 6.1. Bandeja operativa

La pantalla principal tendrá:

- indicadores compactos;
- filtros persistentes;
- tabla densa con columnas configurables;
- vistas guardadas;
- búsqueda global;
- badges de estado y sincronización;
- acciones rápidas según estado;
- panel lateral de detalle sin abandonar la lista;
- opción de vista tipo tablero únicamente si aporta al proceso.

No debe ser un tablero lleno de tarjetas grandes si el usuario necesita revisar cientos de pedidos.

### 6.2. Mesa de captura

```text
+---------------------------------------------------------------+
| Cliente | Bodega | Lista | Vendedor | Ruta | Fecha entrega    |
+---------------------------------------------------------------+
| Escanear o buscar producto...                                 |
+---------------------------------------------------------------+
| Producto | Código | Unidad | Cant. | Precio | Desc. | Total   |
| Producto | Código | Unidad | Cant. | Precio | Desc. | Total   |
|                                                               |
+----------------------------------------------+----------------+
| Notas / estado de red / guardado             | Subtotal       |
|                                              | Descuento      |
|                                              | Impuestos      |
|                                              | TOTAL          |
+----------------------------------------------+----------------+
| Cancelar | Guardar borrador | Confirmar pedido               |
+---------------------------------------------------------------+
```

Características:

- foco siempre predecible;
- optimizada para teclado y escáner;
- editor de celda, no una ventana por producto;
- recálculo inmediato;
- guardado automático;
- indicador offline/sincronizando;
- resumen fijo;
- advertencias junto al campo que las causa;
- acciones destructivas protegidas.

En móvil se reorganiza en una columna, mantiene una barra de captura fija y permite editar líneas mediante panel inferior. No se intenta comprimir la tabla de escritorio.

### 6.3. Detalle

El detalle web debe mostrar:

- encabezado y estado;
- cliente;
- líneas y cantidades atendidas;
- totales;
- timeline;
- pagos;
- facturas relacionadas;
- sincronización externa;
- auditoría;
- acciones válidas para el estado actual.

La pantalla actual de solo lectura puede servir como punto de partida visual, pero requiere timeline, operaciones y vínculos de cumplimiento.

---

## 7. Patrón web para todos los módulos

La migración de Xion no será una conversión pantalla por pantalla. Cada tipo de trabajo tendrá un patrón web.

### Maestros

Productos, clientes, proveedores, bodegas, cajas y listas:

- listado con búsqueda y filtros;
- creación y edición en página o panel amplio;
- pestañas por grupo de información;
- validación contextual;
- historial;
- acciones masivas solo donde sean seguras.

Producto requiere pestañas para:

- datos generales;
- códigos de barras;
- unidades y presentaciones;
- impuestos;
- precios;
- bodegas como información consultada en línea;
- balanza;
- configuración de venta;
- auditoría.

### Documentos con líneas

Pedidos, facturas, entradas, traslados, conteos, averías y devoluciones:

- encabezado compacto;
- campo persistente de escaneo/búsqueda;
- grilla editable;
- navegación por teclado;
- recálculo;
- panel de totales;
- borrador automático;
- timeline;
- acciones según estado.

Se comparte un `DocumentLineGrid` técnico, pero cada módulo conserva reglas propias. No se crea un componente genérico que contenga la lógica de todos los documentos.

### Operación POS

- modo de pantalla completa;
- foco permanente en captura;
- botones grandes para acciones frecuentes;
- atajos visibles;
- medios de pago optimizados;
- estado de caja, red y sincronización;
- mínima navegación administrativa.

### Procesos

Inventario, sincronización, DIAN y cierres:

- bandeja de trabajo;
- estados;
- errores accionables;
- reintentos;
- timeline;
- filtros;
- acciones por lote controladas.

### Reportes

- filtros arriba;
- indicadores;
- gráfica únicamente cuando facilite comparar;
- tabla detallada;
- exportación;
- rangos de meses;
- empresa y negocio;
- definición visible de cada métrica.

---

## 8. Inventario de pantallas web del MVP

| Módulo | Pantallas principales |
|---|---|
| Catalog | productos, editor completo, códigos, unidades |
| Pricing | listas, editor de precios, cambios programados |
| Orders | bandeja, captura, detalle/timeline |
| Sales/POS | facturación, recuperación de temporal, pagos |
| Cash | parametrización, apertura, cierre, movimientos |
| Inventory | existencias online, kardex, conteo |
| Purchasing | entradas, recepción y vínculo con cuenta por pagar |
| Transfers | creación, despacho, recepción |
| Damages | registro, autorización y consulta |
| Payables | cartera de proveedor, documento y aplicación |
| Receivables | cartera de cliente, abonos y aplicación |
| Returns | devolución de venta y compra |
| Fiscal | documentos DIAN, estados, errores y reintentos |
| PosSync | dispositivos, primera sincronización y salud |
| Reporting | ventas, compras, utilidad y comparaciones |

Cada pantalla debe diseñarse desde su tarea, frecuencia, volumen y dispositivo objetivo antes de escribir componentes.

---

## 9. Orden actualizado de implementación

1. Renombrado completo a Auraly.
2. Validación de `Auraly.Database.sqlproj` y pipeline DACPAC.
3. Creación de las reglas de arquitectura por librerías.
4. Diseño del sistema web compartido para documentos y captura.
5. Catalog y Pricing.
6. Orders, absorbiendo Pedidos OK y el módulo actual.
7. Cash y Sales/POS.
8. PosSync y operación offline.
9. Inventory, Purchasing y Payables.
10. Receivables y Returns.
11. Fiscal DIAN propio.
12. Reporting.

Orders entra antes de facturación porque permite validar catálogo, precios, clientes, grilla de captura, offline e idempotencia con menor riesgo fiscal. Los componentes técnicos probados allí podrán reutilizarse en POS sin mezclar los dominios.

---

## 10. Criterios de aceptación

- La solución principal se llama `Auraly.Commerce.sln`.
- Ningún proyecto, namespace o ensamblado conserva `Mimos` o `Talkio`.
- Azure Functions se identifica como `Auraly.Functions`, no como API.
- El frontend conserva el nombre `auraly-admin`.
- La base se compila y publica únicamente desde `Auraly.Database.sqlproj`.
- La aplicación no ejecuta migraciones EF.
- Existe una sola base, `dbo` y un `AuralyDbContext` de ejecución.
- Orders tiene sus cuatro librerías modulares.
- Pedidos del bot, admin, vendedor, API y offline usan el mismo núcleo.
- La integración Pedidos OK es un adaptador temporal, no el dominio.
- Pedidos puede crearse, editarse, guardarse, confirmarse, cancelarse y convertirse en factura.
- La interfaz web no copia WinForms.
- Los documentos con líneas soportan lector, teclado, edición y recálculo.
- Cada módulo tiene diseño web orientado a la tarea.
