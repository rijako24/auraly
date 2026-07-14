# Despliegue de Azure Functions Flex Consumption

Procedimiento probado para publicar `MimosBabySpa.API` en la Function App Flex Consumption de Talkio AI.

## Destino actual

- Resource Group: `RG-TALKIOAI-DEV`
- Function App: `az-talkioai-dev`
- Plan: Flex Consumption `FC1`, Linux
- Runtime: `dotnet-isolated`, .NET 8
- Proyecto: `src\API\MimosBabySpa.API\MimosBabySpa.API.csproj`

## Reglas importantes

1. Flex Consumption ejecuta el codigo desde un ZIP almacenado en el contenedor de deployment. Se publica con **OneDeploy**.
2. No usar `Publish-AzWebApp` para la Function. Puede terminar sin error sin activar el paquete Flex.
3. No usar `/api/zipdeploy`: es el endpoint Kudu clasico y en Flex puede responder `404`.
4. No usar `Compress-Archive` para construir el ZIP en Windows. Puede guardar entradas con `\`; Linux no reconoce entonces `.azurefunctions` en la raiz.
5. El ZIP debe contener directamente `host.json`, `.azurefunctions/`, los DLL y el resto del publish output. No debe contener una carpeta padre.
6. Un `HTTP 202` solo significa que OneDeploy acepto la operacion. No demuestra que el paquete quedo activo.
7. No imprimir connection strings, SAS, access tokens ni publishing passwords.

## 1. Compilar y probar

Desde la raiz del repositorio:

```powershell
dotnet test src\Tests\MimosBabySpa.Tests\MimosBabySpa.Tests.csproj -c Release

dotnet publish src\API\MimosBabySpa.API\MimosBabySpa.API.csproj `
  -c Release `
  -o .deploy\function-flex-publish
```

El publish debe contener al menos:

```text
.deploy/function-flex-publish/
  .azurefunctions/
  host.json
  MimosBabySpa.API.dll
  MimosBabySpa.Application.dll
```

## 2. Crear un ZIP portable

Usar `System.IO.Compression` y convertir siempre las rutas internas a `/`:

```powershell
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = (Resolve-Path '.deploy\function-flex-publish').Path.TrimEnd('\')
$zipPath = Join-Path (Resolve-Path '.deploy').Path 'function-flex.zip'

if (Test-Path -LiteralPath $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

$zip = [IO.Compression.ZipFile]::Open(
  $zipPath,
  [IO.Compression.ZipArchiveMode]::Create)

try {
  Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object {
    $entryName = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $zip,
      $_.FullName,
      $entryName,
      [IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
}
finally {
  $zip.Dispose()
}
```

## 3. Validar el ZIP antes de publicarlo

Esta validacion es obligatoria:

```powershell
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)

try {
  $names = @($archive.Entries.FullName)
  $hasHost = $names -contains 'host.json'
  $hasFunctionsMetadata = $names -contains '.azurefunctions/function.deps.json'
  $backslashEntries = @($names | Where-Object { $_.Contains('\') }).Count
}
finally {
  $archive.Dispose()
}

if (-not $hasHost -or -not $hasFunctionsMetadata -or $backslashEntries -ne 0) {
  throw "ZIP invalido: host=$hasHost; azurefunctions=$hasFunctionsMetadata; backslashes=$backslashEntries"
}
```

Resultado requerido:

- `host.json` en la raiz.
- `.azurefunctions/function.deps.json` en la raiz logica.
- `backslashEntries = 0`.

Si Azure devuelve `InvalidPackageContentException: Cannot find required .azurefunctions directory at root level`, el ZIP esta mal construido. No volver a desplegarlo; recrearlo con separadores `/`.

## 4. Publicar mediante ARM OneDeploy

El siguiente bloque obtiene la conexion del deployment storage sin imprimirla, sube un origen temporal llamado `released-package.zip`, crea una SAS de lectura por una hora y solicita OneDeploy con `remoteBuild=false`.

```powershell
$ErrorActionPreference = 'Stop'
$resourceGroup = 'RG-TALKIOAI-DEV'
$functionName = 'az-talkioai-dev'

$settings = Get-AzFunctionAppSetting `
  -ResourceGroupName $resourceGroup `
  -Name $functionName

$deploymentConnection = [string]$settings['DEPLOYMENT_STORAGE_CONNECTION_STRING']
if ([string]::IsNullOrWhiteSpace($deploymentConnection)) {
  throw 'No existe DEPLOYMENT_STORAGE_CONNECTION_STRING.'
}

$storage = New-AzStorageContext -ConnectionString $deploymentConnection
$container = 'satalkioaidev'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$blob = "manual-$stamp/released-package.zip"

Set-AzStorageBlobContent `
  -File $zipPath `
  -Container $container `
  -Blob $blob `
  -Context $storage `
  -Force | Out-Null

$packageUri = New-AzStorageBlobSASToken `
  -Container $container `
  -Blob $blob `
  -Permission r `
  -ExpiryTime (Get-Date).ToUniversalTime().AddHours(1) `
  -FullUri `
  -Context $storage

$azureContext = Get-AzContext
$profile = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile
$client = New-Object Microsoft.Azure.Commands.ResourceManager.Common.RMProfileClient($profile)
$accessToken = $client.AcquireAccessToken($azureContext.Subscription.TenantId).AccessToken

$function = Get-AzWebApp -ResourceGroupName $resourceGroup -Name $functionName
$oneDeployUri = "https://management.azure.com$($function.Id)/extensions/onedeploy?api-version=2022-09-01"
$body = @{
  location = $function.Location
  properties = @{
    packageUri = $packageUri
    remoteBuild = $false
  }
} | ConvertTo-Json -Depth 5

$response = Invoke-WebRequest `
  -Uri $oneDeployUri `
  -Method PUT `
  -Headers @{ Authorization = "Bearer $accessToken" } `
  -ContentType 'application/json' `
  -Body $body `
  -UseBasicParsing

if ($response.StatusCode -notin 200, 201, 202) {
  throw "OneDeploy no fue aceptado: HTTP $($response.StatusCode)"
}
```

En instalaciones recientes de `Az.Accounts`, puede sustituirse el bloque de `RMProfileClient` por `Get-AzAccessToken`. El bloque anterior queda documentado porque funciona con la version actualmente instalada en este entorno.

## 5. Sincronizar triggers y reiniciar

Hacerlo despues de que el deployment haya terminado correctamente:

```powershell
$syncUri = "https://management.azure.com$($function.Id)/syncfunctiontriggers?api-version=2022-03-01"

$sync = Invoke-WebRequest `
  -Uri $syncUri `
  -Method POST `
  -Headers @{ Authorization = "Bearer $accessToken" } `
  -UseBasicParsing

if ($sync.StatusCode -ne 200) {
  throw "No fue posible sincronizar triggers: HTTP $($sync.StatusCode)"
}

Restart-AzFunctionApp `
  -ResourceGroupName $resourceGroup `
  -Name $functionName `
  -Force | Out-Null
```

## 6. Verificacion post-deploy

No declarar el despliegue exitoso solo porque el upload respondio `202`.

Verificar, en este orden:

1. La Function App esta `Running`.
2. `syncfunctiontriggers` devolvio `200`.
3. Un mensaje real o un smoke test entra al worker.
4. `InboundMessageReceipts.Status` termina en `Processed`.
5. `InboundMessageReceipts.LastError` queda vacio.
6. La respuesta saliente aparece en `Messages` y llega a WhatsApp.

Consulta de comprobacion:

```sql
SELECT TOP (10)
    ProviderMessageId,
    Status,
    AttemptCount,
    UpdatedAtUtc,
    LastError
FROM dbo.InboundMessageReceipts
WHERE BusinessId = 'c1d15a00-0000-0000-0000-000000000010'
ORDER BY ReceivedAtUtc DESC;
```

Un deployment se considera valido cuando el recibo nuevo queda `Processed`; un host `Running` con recibos `Failed` no es suficiente.

## Diagnostico de fallos conocidos

### `customerReadable could not be mapped`

La base ya contiene configuracion del motor nuevo, pero la Function sigue ejecutando un `MimosBabySpa.Application.dll` anterior. Volver a publicar por OneDeploy y comprobar el resultado con un recibo real.

### Core Tools interpreta `net8.0` como `80.0`

No aceptar el cambio a `80.0` ni usar `--force`: Azure rechazara ese runtime. Usar el proceso de ZIP portable + ARM OneDeploy de este documento o actualizar Core Tools de forma controlada.

### Azure CLI falla por certificado SSL

Si `az` falla con `CERTIFICATE_VERIFY_FAILED`, no desactivar globalmente la validacion TLS. Usar el contexto autenticado de Az PowerShell y OneDeploy como se muestra arriba.

### Seguridad SCM

Este flujo ARM no necesita dejar SCM Basic Auth habilitado. Si se habilito temporalmente durante un diagnostico, cerrarlo siempre:

```powershell
$function = Get-AzWebApp -ResourceGroupName $resourceGroup -Name $functionName
$policyId = "$($function.Id)/basicPublishingCredentialsPolicies/scm"
Set-AzResource -ResourceId $policyId -Properties @{ allow = $false } -Force | Out-Null
```

Confirmar al final que `allow = false`.
