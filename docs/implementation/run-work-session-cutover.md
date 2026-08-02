# Ejecutar el corte a sesiones de trabajo

## Requisitos

- .NET SDK 8.
- Node.js 20 o compatible con el frontend actual.
- SQL Server accesible.
- `SqlPackage` instalado.

## Línea base y compilación

Desde la raíz:

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release
```

## Publicación nueva

```powershell
C:\Users\richa\.dotnet\tools\sqlpackage.exe `
  /Action:Publish `
  /SourceFile:database\Auraly.Database\bin\Release\Auraly.Database.dacpac `
  /TargetServerName:.\LOCAL `
  /TargetDatabaseName:AuralyCommerce `
  /TargetTrustServerCertificate:True `
  /TargetEncryptConnection:False `
  /p:CreateNewDatabase=True `
  /p:BlockOnPossibleDataLoss=True
```

## Actualización desde el modelo anterior

Hacer respaldo verificado antes de cualquier despliegue productivo. Publicar el mismo
DACPAC con `CreateNewDatabase=False`, `DropObjectsNotInSource=True` y
`BlockOnPossibleDataLoss=True`.

En el primer intento DacFx puede detenerse después de que el predespliegue haya
transformado correctamente el modelo histórico. No se debe editar datos ni ejecutar
scripts manuales: se repite exactamente el mismo comando. La migración detecta que el
corte ya fue aplicado, no vuelve a transformar datos y DacFx completa el modelo al
recalcular el plan sobre el esquema nuevo.

Después se debe comprobar:

```sql
SELECT OBJECT_ID(N'dbo.CashRegisters'),
       OBJECT_ID(N'dbo.CashSessions'),
       OBJECT_ID(N'dbo.CashMovements'),
       OBJECT_ID(N'dbo.PosDevices');

SELECT COUNT(*) FROM dbo.EnrolledDevices;
SELECT COUNT(*) FROM dbo.WorkSessions;
SELECT COUNT(*) FROM dbo.WorkSessionMovements;
SELECT COUNT(*) FROM dbo.WorkSessionClosures;
```

Los cuatro primeros valores deben ser `NULL`.

## Pruebas

```powershell
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

Desde `admin`:

```powershell
.\node_modules\.bin\tsc.cmd --noEmit
npm run test:pos
npm run build
```

`npm run lint` todavía abre el asistente interactivo de Next.js porque el repositorio
no tiene ESLint configurado. No cuenta como una validación automatizable hasta agregar
una configuración explícita en una rebanada separada.

## Validación manual mínima

1. Iniciar sesión web y abrir `/pos`.
2. Seleccionar sede y bodega.
3. Confirmar que la venta online abre una sesión sin equipo.
4. En un equipo enrolado, desconectar el servidor y autenticar localmente.
5. Crear una venta, cerrar la aplicación y volver a abrirla.
6. Confirmar que reaparecen sesión, borrador, serie y outbox.
7. Recuperar conexión y verificar una sola carga al servidor.
