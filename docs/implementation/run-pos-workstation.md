# Ejecutar Auraly POS Workstation

## Requisitos

- Windows 10/11.
- .NET 8.
- Node compatible con el proyecto `admin`.
- Una impresora ESC/POS instalada en Windows para la prueba física.

## Configuración mínima

POS Edge requiere:

- negocio, sede, bodega, usuario, dispositivo y caja;
- código de caja único por negocio, por ejemplo `03`;
- serie operativa Auraly de factura;
- serie y autorización fiscal DIAN;
- impresora y ancho de papel;
- token de sesión loopback;
- origen web permitido;
- clave técnica protegida.

La serie operativa de la caja 03 produce números como:

```text
VTA03-00000001
```

El prefijo y consecutivo DIAN se configuran aparte.

## Proteger la clave técnica

La clave técnica no se guarda en texto plano. POS Edge usa ASP.NET Data Protection con llaves protegidas por Windows DPAPI para el usuario que ejecuta la caja.

Configure primero:

```text
PosEdge__SecretKeyDirectory=C:\ProgramData\Auraly\PosEdge\keys
```

Entregue la clave mediante entrada estándar al comando de provisión:

```powershell
$technicalKey = Read-Host -AsSecureString
$plain = [System.Net.NetworkCredential]::new("", $technicalKey).Password
$protected = $plain | dotnet run --project src\Pos\Auraly.Pos.Edge.Host -- --protect-fiscal-key
$plain = $null
```

Guarde únicamente el resultado en:

```text
PosEdge__Fiscal__ProtectedTechnicalKey
```

El mismo usuario de Windows y el mismo directorio de llaves deben ejecutar POS Edge. Copiar solo el texto protegido a otro usuario o equipo no permite descifrarlo.

## Ejecutar

```powershell
dotnet run --project src\Pos\Auraly.Pos.Edge.Host
```

En otra terminal:

```powershell
cd admin
npm run dev
```

El instalador de escritorio debe abrir la ruta `/pos` con el token efímero de sesión en el fragmento de la URL. POS Edge escucha únicamente en loopback y valida token y origen.

## Verificaciones automatizadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
cd admin
npx tsc --noEmit
npm run build
```

La impresión física depende del modelo y configuración de la impresora. Las pruebas automatizadas validan los bytes ESC/POS, CUFE, QR y la llamada al adaptador, pero no sustituyen una certificación con el hardware del piloto.
