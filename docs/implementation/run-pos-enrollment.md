# Ejecutar enrolamiento POS Edge

## Requisitos

- SQL Server accesible.
- DACPAC de `Auraly.Database` desplegado.
- API Auraly con `ConnectionStrings:Auraly`.
- usuario autenticado con `sales.create`; para preparar modo offline también
  necesita `pos.devices.enroll`.
- host y navegador en el mismo equipo para el canje por loopback.

## Compilar

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
Set-Location admin
npm install
npx tsc --noEmit
npm run build
```

## Ejecutar API y frontend

Configure la conexión de la API mediante variables de entorno o secretos de
desarrollo; no agregue secretos a `appsettings`.

```powershell
dotnet run --project src/API/Auraly.Api/Auraly.Api.csproj
Set-Location admin
npm run dev
```

## Ejecutar un host Edge sin enrolar

El host requiere un token local de al menos 32 bytes, el origen exacto del
frontend y la URL de Auraly Server. Para desarrollo se puede usar una carpeta
temporal explícita; en instalación usa `%LOCALAPPDATA%\Auraly\PosEdge`.

```powershell
$env:PosEdge__SessionToken = "<token-local-aleatorio-de-al-menos-32-bytes>"
$env:PosEdge__AllowedOrigin = "http://localhost:3000"
$env:PosEdge__ServerUrl = "https://localhost:5057"
$env:PosEdge__Url = "http://127.0.0.1:47831"
dotnet run --project src/Pos/Auraly.Pos.Edge.Host/Auraly.Pos.Edge.Host.csproj
```

El lanzador instalado debe abrir `/pos?edgeSession=<token>` sin exponerlo a
otros orígenes. Al primer arranque:

1. el host responde `EnrollmentRequired`;
2. Auraly muestra negocio, sede y caja;
3. **Trabajar en línea** no descarga datos locales;
4. **Preparar modo offline** autoriza y canjea el enrolamiento;
5. el host guarda `enrollment.protected` y solicita reinicio;
6. el servicio Windows reinicia y comienza el catálogo.

Con `dotnet run` el reinicio debe hacerse manualmente; el reinicio automático se
validará con el instalador del servicio Windows.

## Verificaciones

```powershell
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

La integración SQL usa `AURALY_TEST_SQLSERVER` cuando está definida y, en caso
contrario, `.\LOCAL`. Solo elimina la base temporal creada por la prueba.

No abra ni copie `enrollment.protected`: está ligado al almacén de protección
del equipo/usuario que ejecuta POS Edge.
