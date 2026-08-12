# Ejecutar Entradas de mercanc?a

## Requisitos

- .NET 8
- Node compatible con el proyecto Admin
- SQL Server accesible en `.\LOCAL`
- SqlPackage instalado en `%USERPROFILE%\.dotnet\tools\sqlpackage.exe`

## Compilar

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
cd admin
npx tsc --noEmit
npm run build
```

## Pruebas

```powershell
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
cd admin
npm run test:pos
```

La suite de integraci?n compila el proyecto SQL, despliega el DACPAC en una base temporal de SQL Server y elimina ?nicamente esa base al finalizar.

## Uso

1. Iniciar Auraly.Api con su conexi?n SQL Server.
2. Iniciar Admin.
3. Autenticarse con un usuario que tenga `purchasing.goods-receipts.read`.
4. Abrir **Operaciones > Entradas de mercanc?a**.
5. Crear una entrada, seleccionar proveedor y bodega.
6. Buscar o escanear productos asociados al proveedor.
7. Guardar el borrador para recuperarlo o confirmar con el permiso correspondiente.
8. Consultar el efecto procesado en inventario, cuentas por pagar y precios/rentabilidad.
