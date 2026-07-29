# Ejecutar la rebanada DIAN

## Requisitos

- .NET SDK 8.0.405 o compatible con la solución.
- SQL Server accesible; las pruebas usan `AURALY_TEST_SQLSERVER` o `./LOCAL`.
- SqlPackage en `SQLPACKAGE_PATH` o `%USERPROFILE%/.dotnet/tools/sqlpackage.exe`.
- Node 20 para verificar el frontend existente.

## Comandos verificados

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

El DACPAC queda en `database/Auraly.Database/bin/Release/Auraly.Database.dacpac`. Las pruebas de integración crean y eliminan exclusivamente bases `AuralyServerSlice_<guid>`.

## API fiscal

Requiere JWT Bearer con `business_id`, identificador de usuario y permiso correspondiente:

- `GET /api/commerce/v1/fiscal/documents/{documentId}` — `fiscal.documents.read`.
- `GET /api/commerce/v1/fiscal/documents` — `fiscal.documents.read` y filtros/paginación.
- `POST /api/commerce/v1/fiscal/documents/{documentId}/retry` — `fiscal.retry`.

## Habilitación real

Todavía no ejecutar en producción. Para la futura prueba opcional se requieren endpoint entregado por DIAN, software registrado, PIN en un almacén secreto, `TestSetId`, certificado válido y configuración fiscal versionada. Ninguno se versiona en appsettings o en el repositorio.