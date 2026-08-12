# Evidencia de la rebanada de rutas comerciales

Fecha de verificación: 2026-08-09.

## Resultado implementado

La rebanada está conectada desde la interfaz hasta SQL Server:

`admin -> Auraly.Api -> RouteService -> SqlRouteStore -> Auraly.Database`

Se incorporaron cuatro proyectos físicos del módulo (`Contracts`, `Domain`,
`Application` e `Infrastructure`) y todos pertenecen a `Auraly.Commerce.sln`.
El dominio no referencia infraestructura y la API es el único host HTTP.

La solución guarda:

- zonas comerciales por negocio;
- rutas con vendedor, estado y control `rowversion`;
- uno o varios días, orden de recorrido y hora sugerida;
- paradas vinculadas a `CustomerId` y `PartySiteId` exactos;
- auditoría de creación, modificación y retiro lógico.

Las restricciones de aplicación y SQL impiden:

- códigos activos duplicados dentro del negocio;
- el mismo vendedor, día y orden de recorrido en dos rutas activas;
- la misma sede de cliente en rutas activas que se superponen por día;
- actualizaciones con una versión obsoleta;
- operaciones cruzadas entre negocios o tenants.

## Interfaz y teclado

`/dashboard/routes` utiliza los componentes visuales existentes de Auraly. No
contiene `select` o `checkbox` nativos. La tabla principal pagina y combina filtros
en el servidor. El espacio de paradas ofrece búsqueda paginada, explicación de
conflictos, reordenamiento, eliminación confirmada, impresión y CSV protegido.

Las flechas verticales solo mueven el foco entre filas. La modificación de orden
requiere `Alt` más la flecha, evitando cambios accidentales al recorrer la grilla.

## Ejecuciones realizadas

### Solución y base de datos

`dotnet build Auraly.Commerce.sln --configuration Release --nologo --verbosity:minimal`

Resultado: correcto, **0 errores y 0 advertencias**. El mismo build produjo
`Auraly.Database.dacpac` correctamente.

### Dominio y arquitectura

`dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build`

Resultado: **162/162 pruebas correctas**, incluidas 7 reglas nuevas de rutas y las
pruebas de arquitectura/conectividad existentes.

### Integración SQL Server real

`dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~RoutesVerticalSliceTests`

Resultado: **2/2 escenarios correctos** sobre SQL Server con DACPAC desplegado.
Cubren creación, zona, programación, candidatos, dos paradas, estado preparado,
reordenamiento, filtros, permisos, duplicidad, superposición y concurrencia.

### Frontend

`npx tsc --noEmit`

Resultado: correcto.

`npm run check:encoding`

Resultado: `UTF-8 source encoding verified`.

`npm run build`

Resultado: no concluyente. Next.js quedó detenido dentro de su worker de build sin
emitir un error de TypeScript o de la página de rutas; se terminó por tiempo. Este
resultado no se presenta como aprobado y debe diagnosticarse en una tarea de
estabilización del frontend completo.

## Validación local

La publicación completa del DACPAC sobre `AuralyLocal` fue bloqueada, como debía,
por `BlockOnPossibleDataLoss`: el plan heredado quería reconstruir tablas con datos
de agentes y pedidos. No se deshabilitó esa protección. Las cuatro tablas nuevas
de rutas que el despliegue ya había creado se conservaron y se ejecutó únicamente
el seed idempotente versionado `SeedRoutePermissions.sql`. Se comprobaron 4 tablas,
9 permisos y sus asignaciones a los roles administradores.

La API única quedó comprobada escuchando en `http://localhost:5097` contra esa base;
el administrador quedó disponible en `http://127.0.0.1:3000`. Para esta revisión
visual se desactivaron los consumidores de fondo porque el RabbitMQ instalado
rechazó las credenciales locales disponibles; esto no altera las rutas, que son
online y transaccionales.
La automatización visual integrada no logró adquirir el navegador por una
restricción ACL del worktree, por lo que no se inventa evidencia visual automática.

## Pendiente explícito

Esta rebanada no optimiza visitas, geocodifica ni crea entregas. Tampoco convierte
rutas en un segundo módulo de clientes: consume `Party`, `Customer`, `PartySite` y
`CommerceSeller` canónicos. La siguiente evolución puede conectar pedidos/entregas
a las rutas sin cambiar las claves actuales.
