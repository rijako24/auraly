# Ejecutar la reconciliación de clientes externos

## Requisitos

- .NET SDK definido por `global.json`.
- Node.js y npm compatibles con `admin/package.json`.
- SQL Server disponible para las pruebas de integración.
- `sqlpackage` disponible para desplegar el DACPAC.

## Compilar y probar

Desde la raíz del repositorio:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build --disable-build-servers
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build --disable-build-servers
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build --disable-build-servers
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --disable-build-servers
```

Para el frontend:

```powershell
cd admin
npx tsc --noEmit
npm run test:pos
npm run build
```

Las pruebas del servidor despliegan el DACPAC en una base SQL Server aislada; no
usan EF InMemory ni SQLite como sustituto del servidor.

## Ejecutar la superficie administrativa

Configurar la conexión SQL y los secretos de autenticación mediante el mecanismo
local existente. No guardar secretos en archivos versionados.

```powershell
dotnet run --project src/API/Auraly.Api/Auraly.Api.csproj
```

En otra terminal:

```powershell
cd admin
npm run dev
```

Abrir `/dashboard/parties` y seleccionar **Importaciones**. La ruta directa es
`/dashboard/parties/imports`.

Desde allí se puede:

- filtrar por texto y estado;
- reconciliar un registro;
- reintentar un conflicto después de corregir la identidad;
- procesar hasta cien pendientes por operación;
- inspeccionar Party, Customer y error de conciliación.

## Inspección SQL

```sql
SELECT ExternalCommerceCustomerId, Name, PhoneNormalized,
       ReconciliationStatus, PartyId, CustomerId,
       ReconciliationError, ReconciledAt
FROM dbo.ExternalCommerceCustomers
ORDER BY LastSyncedAt DESC;

SELECT TOP (100) NotificationId, BusinessId, Stream,
       AvailableThroughCursor, OccurredAt, PublishedAt
FROM dbo.PosSynchronizationOutboxMessages
WHERE Stream = N'Customers'
ORDER BY OccurredAt DESC;
```

Un resultado `Linked` debe tener PartyId y CustomerId. Un resultado `Conflict`
debe conservar ambos nulos y explicar la ambigüedad. Repetir la misma conciliación
no debe crear otra Party, Customer o notificación.
