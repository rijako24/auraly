# Ejecutar la rebanada DIAN

## Requisitos

- .NET SDK 8.0.405 o compatible.
- SQL Server accesible; las pruebas usan `AURALY_TEST_SQLSERVER` o `./LOCAL`.
- SqlPackage en `SQLPACKAGE_PATH` o `%USERPROFILE%/.dotnet/tools/sqlpackage.exe`.
- Node 20 para el frontend.
- Windows Certificate Store para una ejecución fiscal on-premise real.

## Validación reproducible

Ejecutar en este orden. No ejecutar `tsc` y `next build` en paralelo porque ambos administran `.next/types`.

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --no-restore --maxcpucount:1 --nodeReuse:false
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release

cd admin
npm run build
npx tsc --noEmit
```

El DACPAC queda en `database/Auraly.Database/bin/Release/Auraly.Database.dacpac`. Las pruebas de integración crean y eliminan únicamente bases `AuralyServerSlice_<guid>`.

## API servidor

Usuario JWT:

- `GET /api/commerce/v1/fiscal/documents/{documentId}` — `fiscal.documents.read`.
- `GET /api/commerce/v1/fiscal/documents` — lectura, filtros y paginación SQL.
- `POST /api/commerce/v1/fiscal/documents/{documentId}/retry` — `fiscal.retry`.

Dispositivo POS:

- `POST /api/pos/v1/sales` — `sales.create`.
- `GET /api/pos/v1/fiscal/statuses?cursor=...&pageSize=100` — `fiscal.status.sync`.

La autenticación POS usa `X-Auraly-Device-Id` y `X-Auraly-Device-Secret`. El servidor deriva empresa, sede, bodega y caja del dispositivo autenticado; no confía en esos datos solamente porque lleguen en el body.

## API local de POS Edge

Protegida por `X-Auraly-Edge-Session` y limitada a loopback/origen configurado:

- `GET /edge/v1/sales/{documentId}/fiscal-status` devuelve el estado durable local.
- `POST /edge/v1/sales/{documentId}/reprint` exige `sales.reprint` y usa el snapshot original.

`PosServerSynchronizationHostedService` intenta primero cargar outbox pendiente y luego solicita estados fiscales. Los fallos de red conservan los datos y se reintentan sin renumerar.

## Configuración real de habilitación

`FiscalIssuerConfigurations` debe contener endpoint DIAN de habilitación, `TestSetId`, software, referencia de PIN y referencia de certificado. Para on-premise:

- `CertificateProvider = WindowsCertificateStore`.
- `CertificateKeyReference = StoreLocation/StoreName`.
- `CertificateThumbprint` identifica el certificado.
- `SoftwarePinReference = env://NOMBRE_VARIABLE`.

El worker se controla con `Auraly:Fiscal:Worker:Enabled` (activo por defecto). No versionar PIN, PFX, PEM, contraseña ni clave privada.

No apuntar esta rama a producción. La prueba real solo puede ejecutarse después de aprovisionar credenciales de habilitación válidas.