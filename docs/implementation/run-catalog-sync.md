# Ejecutar la rebanada de catálogo

## Requisitos

- .NET SDK compatible con la solución.
- SQL Server accesible.
- `SqlPackage.exe`.
- La variable `AURALY_TEST_SQLSERVER` si la instancia no es `.\LOCAL`.

## Compilar

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
```

## Ejecutar pruebas

```powershell
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

Las pruebas de integración crean una base aislada, publican el DACPAC mediante
SqlPackage, ejecutan API y POS Edge y eliminan exclusivamente esa base.

## Configurar la API

Variables mínimas:

```text
ConnectionStrings__Auraly=<SQL Server connection string>
Authentication__Jwt__Issuer=<issuer>
Authentication__Jwt__Audience=<audience>
Authentication__Jwt__SigningKey=<secret supplied by secure configuration>
```

No guardar `SigningKey` en archivos versionados. En SaaS debe provenir del
almacén seguro de la plataforma; on-premise debe inyectarse mediante secreto de
despliegue.

## Inspeccionar SQLite

Las tablas nuevas comienzan con `PosCatalog`. Las tablas de facturas, series y
outbox creadas por rebanadas anteriores no se eliminan ni recrean.

Estados:

- `Empty`: dispara bootstrap automático.
- `Bootstrapping`: staging no visible y checkpoint durable.
- `Ready`: catálogo promovido y cursor incremental activo.

Para simular una interrupción, detenga el proceso después de una página y
vuelva a ejecutar `SynchronizeAsync`; el estado durable conserva la sesión y el
cursor. Para simular trabajo sin red, no invoque sincronización: `CaptureAsync`
y `SearchAsync` operan solamente sobre SQLite.

## Verificar que no existe un segundo producto

```sql
SELECT name
FROM sys.tables
WHERE name IN (N'Products', N'CatalogProducts');
```

El resultado esperado contiene únicamente `Products`.

