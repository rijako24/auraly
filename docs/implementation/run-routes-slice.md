# Ejecutar la rebanada de rutas comerciales

## Dependencias

- SQL Server accesible y una base desplegada desde `Auraly.Database.dacpac`.
- .NET SDK 8.
- Node.js compatible con el proyecto `admin`.
- La API única `Auraly.Api`; rutas no expone un host adicional.

## Esquema

```powershell
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

Despliegue el DACPAC con el mecanismo documentado para Auraly. Las tablas nuevas son
`SalesZones`, `SalesRoutes`, `SalesRouteSchedules` y `SalesRouteStops`. El script de
postdespliegue registra los permisos `routes.*` y `route-zones.*`.

## API

La conexión debe apuntar a la misma base administrada por el proyecto SQL:

```powershell
$env:ConnectionStrings__Auraly = 'Server=.\LOCAL;Database=AuralyLocal;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project src/API/Auraly.Api/Auraly.Api.csproj --configuration Release
```

También deben suministrarse por configuración segura las claves JWT, la protección
fiscal y los transportes ya requeridos por la API completa. Rutas no introduce
secretos ni procesos independientes.

Los endpoints quedan bajo `/api/commerce/v1`:

- `GET/POST /routes`
- `GET/PUT /routes/{routeId}`
- `POST /routes/{routeId}/status`
- `GET /routes/{routeId}/candidate-sites`
- `POST /routes/{routeId}/stops`
- `DELETE /routes/{routeId}/stops/{stopId}`
- `PUT /routes/{routeId}/stops/order`
- `GET /routes/{routeId}/export`
- `GET /routes/options`
- `POST /route-zones`

## Interfaz

```powershell
cd admin
npm run dev
```

Abra `/dashboard/routes`. La opción **Rutas comerciales** solo se muestra con
`routes.read`. En la vista se puede:

1. crear una zona sin abandonar el formulario;
2. crear o editar la cabecera y programación semanal;
3. filtrar por vendedor, zona, día y estado;
4. buscar sedes de clientes candidatas;
5. agregar y retirar sedes exactas, no clientes ambiguos;
6. ordenar paradas con `Alt+ArrowUp` y `Alt+ArrowDown`;
7. navegar filas con `ArrowUp` y `ArrowDown` sin cambiar valores;
8. imprimir o exportar CSV mediante el endpoint protegido.

## Pruebas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~RoutesVerticalSliceTests
cd admin
npx tsc --noEmit
npm run check:encoding
```

Las pruebas de integración despliegan el DACPAC en una base SQL Server aislada y
eliminan únicamente esa base temporal al finalizar.
