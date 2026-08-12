# Rutas comerciales — diseño definitivo basado en Xion y mejorado para Auraly

Fecha: 2026-08-09  
Estado: listo para implementación vertical  
Prevalencia: este documento reemplaza las decisiones anteriores sobre Rutas contenidas en `orders-returns-routes-design.md` y en la versión diferida de este archivo.

## 1. Decisión

Rutas será un módulo web exclusivamente online dentro de **Comercio > Rutas comerciales**. Se construirá como una rebanada vertical completa sobre el modelo canónico existente de `Party`, `Customer`, `PartySite`, `CommerceSeller` y `Business`.

No se copiarán formularios, tablas, IDs, UI ni arquitectura de Xion. Xion se usa como fuente funcional para conservar lo valioso y corregir sus limitaciones.

La primera entrega cubre:

- zonas comerciales;
- creación, edición, activación y desactivación de rutas;
- vendedor responsable;
- programación por días;
- asignación de establecimientos de clientes;
- recorrido ordenado;
- filtros, paginación, impresión y exportación;
- permisos, concurrencia y auditoría.

No incluye todavía GPS, geocodificación, optimización automática, ejecución de visita, despacho, cargue, recaudo en calle, metas, comisiones, portafolios ni descarga a POS Edge.

## 2. Auditoría funcional de Xion

### 2.1 Fuentes revisadas

- `CapaDePresentacion-union/Formularios/FrmRuta.cs`
- `CapaDePresentacion-union/Formularios/FrmClientesXRuta.cs`
- `CapaDePresentacion-union/Formularios/FrmClienteXRutaBuscar.cs`
- `CapaDeLogica/Servicios/Basico/RutaService.cs`
- `CapaDeLogica/Servicios/Personas/ClienteService.cs`
- `CapaDeDatos/Repositorios Servidor/Basicos/RutaRepository.cs`
- `CapaDeDatos/Repositorios Servidor/Basicos/RutaClienteRepository.cs`
- `CapaDeDatos/Repositorios Servidor/Personas/ClienteRepository.cs`
- `CapaDeEntidades/Servidor/Personas/Vendedores/Ruta.cs`
- `CapaDeEntidades/Servidor/Personas/Vendedores/RutasClientes.cs`
- `CapaDeEntidades/Servidor/Personas/Vendedores/Zona.cs`

### 2.2 Comportamientos valiosos encontrados

Xion permite:

- crear una ruta por sucursal, zona, vendedor, nombre, estado y días de semana;
- exigir al menos un día de visita;
- filtrar rutas por sucursal, zona, vendedor, cliente y texto;
- buscar clientes por nombre, documento, país, departamento, ciudad, barrio y tipo;
- seleccionar varios clientes y anexarlos al final del recorrido;
- asignar un orden de visita obligatorio;
- cambiar una posición y normalizar de nuevo toda la secuencia;
- detectar clientes que quedarían en rutas incompatibles del mismo vendedor y días coincidentes;
- mostrar documento, nombre, dirección, ciudad, barrio y teléfono;
- consultar e imprimir el recorrido ordenado;
- auditar cambios de encabezado y clientes;
- separar permisos de consulta, creación, modificación y guardado.

### 2.3 Problemas de Xion que no se conservan

- `RutaId` y `OrdenId` numéricos pequeños y generados buscando el siguiente consecutivo;
- días como siete columnas booleanas incrustadas en la ruta;
- la parada apunta a `ClienteId`, no a la sede o dirección concreta;
- la descripción se valida globalmente y no por Business;
- se impide que un vendedor tenga cualquier segunda ruta en un día coincidente;
- consultas limitadas en memoria y resultados truncados a 200;
- formulario, persistencia, auditoría e impresión acoplados;
- reemplazo de colecciones completas sin `rowversion` explícito;
- edición manual de números de orden con riesgo de colisiones;
- filtros y combos cargados de manera monolítica;
- Crystal Reports e impresión dependiente del equipo;
- ausencia de una regla sólida para dos usuarios reordenando al tiempo.

## 3. Modelo canónico

### 3.1 Frontera

`BusinessId` representa la sede comercial y es la frontera directa del módulo. No se repite `TenantId` en las tablas de Rutas porque el Tenant se resuelve a través de Business y siempre se valida desde la identidad autenticada.

Una ruta pertenece exactamente a un Business. No se comparte una misma ruta entre sedes. Si dos sedes usan recorridos similares, cada una conserva su propia ruta.

### 3.2 Identidades existentes que se reutilizan

- `CustomerId`: rol de cliente dentro del Business.
- `PartyId`: identidad común del tercero.
- `PartySiteId`: establecimiento, dirección o sede concreta de la Party.
- `SellerId`: rol vendedor de `CommerceSellers` dentro del Business.

La ruta almacena `SellerId`, no `SellerPartyId`. La Party se obtiene desde el rol vendedor. Esto evita saltarse las reglas comerciales específicas del vendedor.

La parada almacena `CustomerId` y `PartySiteId`. El servidor valida transaccionalmente que:

1. el Customer pertenezca al mismo Business de la ruta;
2. el PartySite pertenezca a la Party de ese Customer;
3. Customer, Party y PartySite estén activos.

### 3.3 SalesZones

Maestro simple propiedad del módulo:

- `ZoneId` UUIDv7;
- `BusinessId`;
- `Code`;
- `Name`;
- `IsActive`;
- `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`;
- `RowVersion`.

Restricciones:

- código único por Business;
- nombre normalizado único por Business mientras esté activo;
- una zona usada por rutas no se elimina físicamente;
- se administra desde **Configuración > Maestros > Zonas comerciales**, sin crear otra opción de primer nivel en el menú.

La zona es una clasificación comercial. No reemplaza país, departamento, ciudad ni barrio y no se infiere automáticamente de la dirección.

### 3.4 SalesRoutes

- `RouteId` UUIDv7;
- `BusinessId`;
- `Code` corto y estable;
- `Name`;
- `ZoneId` opcional;
- `SellerId` obligatorio en el MVP;
- `Status`: `Active` o `Inactive`;
- `Notes` opcional;
- `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`;
- `RowVersion`.

Restricciones:

- código único por Business;
- nombre normalizado único por Business para rutas activas;
- vendedor activo y perteneciente al mismo Business;
- una ruta activa debe tener al menos un día activo y una parada activa antes de considerarse `Ready` para operación;
- se puede guardar inicialmente como `Draft` mientras se completa. `Draft`, `Ready` e `AttentionRequired` son estados de preparación calculados, no estados comerciales almacenados.

### 3.5 SalesRouteSchedules

Los días no serán siete columnas booleanas. Cada programación es una fila:

- `RouteScheduleId` UUIDv7;
- `RouteId`;
- `DayOfWeek` de 1 a 7, usando ISO: lunes 1 y domingo 7;
- `RunOrder` positivo, por defecto 1;
- `PlannedStartTime` opcional;
- `IsActive`;
- auditoría y `RowVersion`.

Reglas:

- un solo registro activo por ruta y día;
- un vendedor puede tener varias rutas el mismo día;
- `RunOrder` permite definir cuál recorrido atiende primero ese vendedor;
- no puede repetirse el mismo `RunOrder` para vendedor, Business y día;
- cambiar vendedor o calendario ejecuta nuevamente la validación de conflictos.

Esto mejora la regla de Xion, que bloqueaba cualquier segunda ruta del mismo vendedor en días coincidentes.

### 3.6 SalesRouteStops

- `RouteStopId` UUIDv7;
- `RouteId`;
- `CustomerId`;
- `PartySiteId`;
- `Sequence` entero positivo;
- `OperationalNote` opcional;
- `IsActive`;
- `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`;
- `RemovedBy`, `RemovedAt` opcionales;
- `RowVersion`.

Restricciones:

- establecimiento único dentro de una ruta activa;
- secuencia única y continua entre 1 y N;
- una misma Party puede aparecer varias veces si son establecimientos distintos;
- un mismo establecimiento no puede estar programado en dos rutas activas con días coincidentes dentro del mismo Business, sin importar el vendedor;
- el mismo establecimiento sí puede estar en rutas de días diferentes;
- retirar una parada es lógico y compacta la secuencia;
- una parada usada por documentos históricos nunca se elimina físicamente.

La regla por establecimiento evita visitas duplicadas. Es más precisa que Xion, que comparaba al cliente general y solo detectaba conflictos del mismo vendedor.

## 4. Concurrencia y reordenamiento

Toda modificación exige `RowVersion`.

El comando de reordenamiento recibe:

- `RouteId`;
- `ExpectedRouteRowVersion`;
- la lista completa y ordenada de `RouteStopId` activos.

Dentro de una sola transacción SQL Server:

1. bloquea la ruta;
2. valida la versión;
3. comprueba que la lista contiene exactamente las paradas activas, sin faltantes ni duplicados;
4. asigna valores temporales para evitar colisiones del índice único;
5. escribe la secuencia definitiva 1..N;
6. actualiza la ruta para generar una nueva versión;
7. confirma toda la operación.

Si otro usuario cambió la ruta, responde `409 Conflict`; la interfaz conserva el borrador local, muestra qué cambió y permite recargar. Nunca mezcla silenciosamente dos órdenes.

Para asignaciones simultáneas se toma un bloqueo lógico por `BusinessId + PartySiteId + días afectados`. La validación de solapamiento y la escritura ocurren en la misma transacción.

## 5. Casos de uso

### 5.1 Listar y filtrar

Filtros combinables y ejecutados en SQL Server:

- código o nombre de ruta;
- estado;
- preparación calculada;
- zona;
- vendedor por código o nombre;
- día de semana;
- cliente por identificación, nombre o teléfono;
- establecimiento por nombre, dirección, ciudad o barrio;
- fecha de actualización.

La consulta usa paginación del servidor, ordenamiento estable y no descarga todas las rutas para filtrar en memoria.

### 5.2 Crear ruta

1. Capturar código, nombre y zona.
2. Seleccionar vendedor.
3. Elegir uno o varios días y su orden de recorrido.
4. Guardar borrador.
5. Agregar establecimientos.
6. Ordenar recorrido.
7. Activar cuando esté completo.

Código sugerido automáticamente a partir de una secuencia comercial por Business, pero editable antes de guardar. El código visible no es el UUID interno.

### 5.3 Buscar y asignar establecimientos

La búsqueda devuelve `CustomerId + PartySiteId`, nunca solo Party:

- identificación y nombre del cliente;
- código y nombre del establecimiento;
- dirección, ciudad, barrio y teléfono;
- rutas activas y días donde ya está asignado;
- indicador de conflicto con el calendario actual.

Permite búsqueda sin exigir un filtro previo, con mínimo de caracteres y paginación/keyset. Los primeros resultados se cargan automáticamente.

Si la Party existe pero no es Customer del Business actual, la vista puede ofrecer **Asociar como cliente de esta sede** cuando el usuario tenga permiso. La venta y la relación global de Party no se bloquean.

La asignación múltiple anexa los establecimientos al final y genera secuencias consecutivas. Los conflictos no se esconden: aparecen deshabilitados con explicación.

### 5.4 Cambiar calendario o vendedor

Antes de confirmar, el servidor devuelve todos los establecimientos que entrarían en conflicto. La UI permite:

- cancelar;
- retirar explícitamente los conflictivos;
- moverlos a otra ruta mediante un comando transaccional futuro.

La primera rebanada no reasigna automáticamente ni elimina clientes silenciosamente, corrigiendo el flujo ambiguo de Xion.

### 5.5 Desactivar

Desactivar una ruta:

- conserva calendario y paradas;
- la excluye de planificación operativa;
- no modifica pedidos, facturas ni ventas históricas;
- permite reactivarla solo después de revalidar vendedor, sitios, calendario y conflictos.

## 6. API versionada

- `GET /api/commerce/v1/routes`
- `POST /api/commerce/v1/routes`
- `GET /api/commerce/v1/routes/{routeId}`
- `PUT /api/commerce/v1/routes/{routeId}`
- `POST /api/commerce/v1/routes/{routeId}/activate`
- `POST /api/commerce/v1/routes/{routeId}/deactivate`
- `GET /api/commerce/v1/routes/{routeId}/candidate-sites`
- `POST /api/commerce/v1/routes/{routeId}/stops`
- `DELETE /api/commerce/v1/routes/{routeId}/stops/{routeStopId}`
- `PUT /api/commerce/v1/routes/{routeId}/stops/order`
- `GET /api/commerce/v1/routes/{routeId}/print`
- `GET /api/commerce/v1/routes/{routeId}/export`
- `GET /api/commerce/v1/route-options/sellers`
- `GET /api/commerce/v1/route-options/zones`

Las mutaciones aceptan `Idempotency-Key` cuando puedan repetirse por timeout. `BusinessId` se toma del contexto autenticado; si aparece en un contrato se compara y nunca se confía únicamente en el body.

## 7. Permisos

- `routes.read`
- `routes.create`
- `routes.update`
- `routes.activate`
- `routes.deactivate`
- `routes.stops.manage`
- `routes.export`
- `route-zones.read`
- `route-zones.manage`

El backend valida siempre. La UI oculta rutas de menú no autorizadas y muestra acciones deshabilitadas cuando aporta claridad.

## 8. Experiencia web

### 8.1 Vista principal

Ubicación: **Comercio > Rutas comerciales**.

Diseño de escritorio:

- encabezado con métricas: activas, borradores, con alertas y establecimientos asignados;
- filtros compactos y combinables;
- lista paginada de rutas a la izquierda;
- panel de detalle y constructor de recorrido a la derecha;
- en pantallas pequeñas, lista y detalle se convierten en vistas apiladas.

Cada ruta muestra código, nombre, vendedor, zona, chips de días, cantidad de paradas, preparación y última actualización.

### 8.2 Constructor de recorrido

Dos paneles:

- **Disponibles:** buscador paginado de clientes y establecimientos;
- **Recorrido:** paradas ordenadas con nombre, sede, dirección, barrio, ciudad, teléfono y observación.

No se edita directamente el número de secuencia. Se reordena mediante arrastre o teclado sobre el mismo comando de aplicación.

Reglas de teclado comunes con las demás grillas de Auraly:

- `↑` y `↓`: cambian la fila enfocada sin alterar datos;
- `Enter`: abre o confirma la acción principal de la fila;
- `Espacio`: selecciona o deselecciona en Disponibles;
- `Alt + ↑` y `Alt + ↓`: reordenan una parada;
- `Delete`: solicita confirmación para retirar la parada;
- `Escape`: cierra el editor o regresa al buscador;
- después de agregar o retirar, el foco permanece en una fila predecible y visible.

Los componentes son Auraly; no se usan checkbox, dropdown o diálogo nativos sin estilo.

### 8.3 Impresión y exportación

La representación imprimible web contiene:

- Business;
- ruta, zona, vendedor y días;
- fecha de generación y usuario;
- paradas en secuencia;
- identificación, cliente, establecimiento, dirección, barrio, ciudad y teléfono;
- espacio operativo para observaciones.

La exportación inicial es CSV UTF-8 controlado por permiso. No se migra Crystal Reports.

## 9. Relación con pedidos, ventas e historial

Xion vincula ruta a pedidos y facturas. Auraly conservará esa capacidad sin hacer que el CRUD de Rutas dependa del motor documental:

- pedidos originados en una visita podrán capturar `RouteId`, `RouteStopId` y `SellerId` como atribución;
- al facturar, la venta conserva la atribución recibida del pedido o del contexto comercial;
- cambiar vendedor, calendario o parada no reescribe documentos históricos;
- una venta directa sin ruta sigue siendo válida;
- la primera rebanada de Rutas no modifica automáticamente pedidos ni facturas existentes.

La conexión documental se implementará mediante contratos públicos del módulo, no leyendo tablas internas de Rutas desde Orders o Sales.

## 10. Pruebas obligatorias

### Dominio y aplicación

- código y nombre requeridos;
- al menos un día para activar;
- vendedor activo del Business;
- establecimiento perteneciente al Customer;
- Customer perteneciente al Business;
- secuencia continua;
- días ISO correctos;
- solapamiento por establecimiento;
- varias sedes de una misma Party;
- varias rutas del vendedor el mismo día con distinto `RunOrder`;
- duplicado de `RunOrder` rechazado;
- desactivar y reactivar con revalidación.

### SQL Server real

- DACPAC compilado y desplegado;
- índices únicos y FKs;
- aislamiento por Business;
- filtros combinados y paginación;
- asignación múltiple;
- reorder atómico;
- dos reorders concurrentes: uno gana y otro recibe conflicto;
- dos asignaciones simultáneas del mismo sitio y día: una sola gana;
- retiro compacta la secuencia;
- ningún cambio altera documentos históricos.

### API y permisos

- autenticación requerida;
- Business del body incorrecto rechazado;
- cada permiso probado;
- idempotencia;
- `409` por versión antigua;
- exportación protegida.

### Frontend

- filtros y paginación reales;
- creación completa;
- búsqueda y asignación de varias sedes;
- navegación `↑`/`↓`;
- reordenamiento `Alt + ↑`/`Alt + ↓`;
- arrastre produce el mismo comando;
- foco estable después de agregar, mover o retirar;
- alertas de conflicto comprensibles;
- componentes visuales consistentes;
- accesibilidad básica.

### Arquitectura

- proyectos canónicos dentro de `Auraly.Commerce.sln`;
- Domain sin infraestructura;
- Routes sin lectura directa de tablas internas de Orders o Sales;
- API, persistencia y UI con consumidores reales;
- sin TODO, `NotImplementedException`, mocks permanentes ni nombres legacy en el alcance nuevo.

## 11. Rebanada vertical de implementación

La implementación se divide internamente en pasos, pero no se declara lista hasta conectar todos:

1. tablas `SalesZones`, `SalesRoutes`, `SalesRouteSchedules` y `SalesRouteStops` en `Auraly.Database`;
2. contratos, dominio, aplicación e infraestructura `Auraly.Routes`;
3. composición en la API única;
4. permisos y datos semilla;
5. endpoints paginados y mutaciones;
6. maestro de zonas dentro del panel unificado;
7. vista **Rutas comerciales** y constructor de recorrido;
8. impresión/exportación;
9. pruebas SQL Server, concurrencia, API, frontend y arquitectura;
10. evidencia reproducible y servicios locales listos para prueba.

No se crearán proyectos o interfaces sin consumidores. No se simulará el módulo con datos quemados.

## 12. Decisiones cerradas

- Rutas es online; no se descarga a POS Edge.
- Business es la sede y la frontera del módulo.
- Vendedor se referencia mediante `SellerId`.
- La parada representa un establecimiento mediante `CustomerId + PartySiteId`.
- Una Party con varias sedes puede participar mediante paradas diferentes.
- Zonas comerciales es un maestro simple del módulo.
- Calendario se modela por filas, no por siete booleanos.
- Un vendedor puede atender varias rutas el mismo día mediante `RunOrder`.
- Un establecimiento no puede duplicarse en rutas activas de días coincidentes.
- Reordenar es atómico y protegido por `RowVersion`.
- No se editan números de orden manualmente.
- No se borra historial al cambiar o desactivar una ruta.
- GPS, optimización y ejecución de visita quedan fuera del MVP.
- Xion es referencia funcional, nunca dependencia ni compatibilidad.