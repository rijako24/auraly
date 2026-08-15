# Auraly

Auraly es una plataforma multi-tenant para operar ventas, punto de venta, inventario, cartera, pagos, contabilidad, fiscalidad y automatizaciones empresariales.

## Arquitectura

- `admin/`: aplicación web Next.js, incluido el POS.
- `src/API/Auraly.Api/`: única API web desplegable.
- `src/API/Auraly.Platform.Worker/`: procesamiento asíncrono del motor.
- `src/Modules/`: módulos de dominio, aplicación, contratos e infraestructura.
- `src/Pos/` y `src/Desktop/`: operación local y desconectada del POS.
- `database/Auraly.Database/`: esquema SQL y scripts de despliegue.
- `tests/` y `src/Tests/`: regresiones unitarias, arquitectónicas e integradas.

La solución canónica es `Auraly.Commerce.sln`. No existe una segunda API.

## Validación local

```powershell
dotnet restore Auraly.Commerce.sln --locked-mode
dotnet build Auraly.Commerce.sln -c Release --no-restore

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj -c Release --no-build
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj -c Release --no-build
dotnet test src/Tests/Auraly.Platform.Tests/Auraly.Platform.Tests.csproj -c Release --no-build
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj -c Release --no-build

Set-Location admin
npm install
npm run check:encoding
npm run lint
npm run test:pos
npm run build
```

## Despliegue

El procedimiento canónico y los recursos de DEV/PROD están documentados en [infrastructure/azure/README.md](infrastructure/azure/README.md). Los releases se generan desde un árbol limpio y se promueven primero a DEV.

No se versionan secretos, perfiles personales de publicación ni artefactos generados.