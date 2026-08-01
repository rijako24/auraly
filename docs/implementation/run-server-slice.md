# Ejecutar la segunda rebanada

## Requisitos

- .NET SDK 8.
- SQL Server o SQL Server Express accesible con permisos para crear una base.
- `SqlPackage`.
- En Windows, el valor predeterminado de las pruebas es `.\LOCAL`.

No se usa Entity Framework para crear la base del servidor. Las pruebas
despliegan el DACPAC de `Auraly.Database`.

## Compilar y probar

Desde la raíz:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

Para otra instancia SQL:

```powershell
$env:AURALY_TEST_SQLSERVER = '.\SQLEXPRESS'
$env:SQLPACKAGE_PATH = 'C:\ruta\sqlpackage.exe'
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

Cada ejecución crea `AuralyServerSlice_<uuid>`, despliega el DACPAC y elimina
únicamente esa base temporal al terminar.

## Desplegar el DACPAC manualmente

```powershell
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
sqlpackage /Action:Publish `
  /SourceFile:database/Auraly.Database/bin/Release/Auraly.Database.dacpac `
  /TargetConnectionString:"Server=.\LOCAL;Database=AuralyLocal;Integrated Security=True;TrustServerCertificate=True" `
  /p:DropObjectsNotInSource=False `
  /p:BlockOnPossibleDataLoss=True
```

Los scripts existentes de predeployment y postdeployment se preservan. Los
scripts PowerShell del proyecto usan autenticación integrada por defecto y no
incluyen credenciales locales embebidas.

## Iniciar Auraly.Api

Configure la conexión y una clave técnica de desarrollo mediante secretos de
usuario, variables protegidas o el proveedor seguro del entorno. Este ejemplo
usa marcadores, nunca valores reales:

```powershell
$env:ConnectionStrings__Auraly = 'Server=.\LOCAL;Database=AuralyLocal;Integrated Security=True;TrustServerCertificate=True'
$env:Auraly__Fiscal__TechnicalKeys__0__BusinessId = '<business-guid>'
$env:Auraly__Fiscal__TechnicalKeys__0__FiscalAuthorizationId = '<authorization-guid>'
$env:Auraly__Fiscal__TechnicalKeys__0__Version = 'v1'
$env:Auraly__Fiscal__TechnicalKeys__0__Environment = 'Test'
$env:Auraly__Fiscal__TechnicalKeys__0__Value = '<secret-from-secure-store>'
dotnet run --project src/API/Auraly.Api/Auraly.Api.csproj
```

La API expone:

- `GET /health`
- `POST /api/pos/v1/sales`

## Inspección

Outbox local, con una herramienta SQLite:

```sql
SELECT MessageId, DocumentId, Status, AttemptCount, NextAttemptAt,
       RemoteStatus, ServerReceiptId, LastError
FROM Outbox
ORDER BY CreatedAt;
```

Servidor:

```sql
SELECT DocumentId, Status, CufeReceived, CufeCalculated, LastError
FROM dbo.SalesDocuments;

SELECT DocumentId, Status, AttemptCount, AcquiredAt, CompletedAt, LastError
FROM dbo.DocumentProcessingJobs;

SELECT DocumentId, Quantity FROM dbo.InventoryMovements;
SELECT DocumentId, Amount FROM dbo.SalesPayments;
SELECT DocumentId, Status FROM dbo.ServerOutboxMessages;
```

## Reproducir recuperación y conflicto

El E2E principal crea SQLite físico, emite dos ventas offline, reinicia POS
Edge, sube una venta, repite la carga y altera la cantidad de la segunda copia:

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release `
  --filter FullyQualifiedName~Physical_sqlite_restarts_uploads_once_and_preserves_conflict
```

Los casos de número, fecha, cliente, cantidad, precio, descuento, impuesto,
total, prefijo y autorización se ejecutan con:

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release `
  --filter FullyQualifiedName~Fiscal_mutation_is_preserved_as_integrity_conflict
```

La recuperación por timeout y la ausencia de recibo durable están cubiertas por
`PosToServerRecoveryTests`: el mensaje nunca queda `Uploaded` sin recibo.

