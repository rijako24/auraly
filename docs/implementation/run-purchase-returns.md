# Ejecutar devoluciones de compra

## Requisitos

- .NET SDK y Node según la solución.
- SQL Server disponible en `AURALY_TEST_SQLSERVER` o `localhost\LOCAL`.
- DACPAC Release generado.

## Comandos

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
cd admin
npx tsc --noEmit
npm run build
npm run test:pos
```

La suite de integración crea una base aislada, despliega `database/Auraly.Database/bin/Release/Auraly.Database.dacpac`, ejecuta los escenarios y elimina únicamente esa base temporal.

## Uso web

1. Iniciar API y Admin con la configuración normal de desarrollo.
2. Ingresar con permisos de devoluciones.
3. Abrir `Operaciones > Devoluciones a proveedores`.
4. Buscar una entrada procesada.
5. Seleccionar cantidades o usar `Devolver todo lo disponible`.
6. Elegir motivo y confirmar.

El documento resultante tiene formato `DCP{serie}-{consecutivo de 8 dígitos}`.