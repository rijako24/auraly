# Ejecutar la rebanada de Terceros y Maestros

## Requisitos

- .NET SDK definido por `global.json`.
- Node.js y npm compatibles con `admin/package.json`.
- SQL Server accesible por las pruebas de integración.
- `sqlpackage` disponible según la configuración de la solución.

Las pruebas de servidor despliegan el DACPAC en una base aislada. No usan EF
InMemory, SQLite ni `EnsureCreated` para sustituir SQL Server.

## Compilar y probar

Desde la raíz del repositorio:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --disable-build-servers
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release --disable-build-servers
```

Para el frontend:

```powershell
cd admin
npx tsc --noEmit
npm run test:pos
npm run build
```

## Ejecutar API y admin

Configurar la cadena de SQL Server y los secretos de autenticación mediante el
mecanismo local existente; no copiarlos a `appsettings` versionados.

```powershell
dotnet run --project src/API/Auraly.Api/Auraly.Api.csproj
```

En otra terminal:

```powershell
cd admin
npm run dev
```

Rutas principales:

- `/dashboard/parties`
- `/dashboard/settings/masters`
- `/pos`

## Probar creación rápida desde POS Edge

1. Iniciar Auraly Server y enrolar el dispositivo con su Business.
2. Iniciar `Auraly.Pos.Edge.Host` con la URL del servidor y credencial de
   dispositivo configuradas por el flujo de enrolamiento.
3. Abrir el selector de cliente en facturación.
4. Seleccionar **Nuevo cliente**.
5. Elegir país, departamento/estado y ciudad; escribir el barrio libremente.
6. Guardar. El host crea la Party/Customer en el servidor y descarga la proyección
   de clientes/precios antes de seleccionarla.

Sin conexión, la búsqueda de clientes ya sincronizados sigue disponible; el alta
queda deshabilitada para evitar identidades duplicadas.

## Inspección SQL

La identidad y los roles se pueden comprobar con consultas de solo lectura:

```sql
SELECT p.PartyId, p.DisplayName, c.CustomerId, s.SupplierId
FROM dbo.Parties p
LEFT JOIN dbo.Customers c ON c.PartyId = p.PartyId
LEFT JOIN dbo.Suppliers s ON s.PartyId = p.PartyId
ORDER BY p.CreatedAt DESC;

SELECT TOP (100) *
FROM dbo.PosSynchronizationOutboxMessages
WHERE StreamName = 'Customers'
ORDER BY CreatedAt DESC;
```

Un cambio de cliente debe crear la invalidación en la misma transacción. El POS no
hace polling: recibe la notificación disponible y descarga el delta por cursor.
