# Publicacion de Talkio AI

> [!WARNING]
> Documento legado para `RG-TALKIOAI-DEV`. No usar para desplegar los ambientes Auraly nuevos y no borrar sus recursos. La guía vigente es [infrastructure/azure/README.md](infrastructure/azure/README.md).

Guia operativa para publicar la base de datos y la Azure Function sin redescubrir comandos.

## Contexto

- Workspace: `C:\Proyectos\Talkio-AI\talkio-ai`
- Function App Azure: `az-talkioai-dev`
- Resource Group: `RG-TALKIOAI-DEV`
- Subscription: `5ea009ce-23c5-4bbd-b1c8-62116d58f596`
- Function host: `https://az-talkioai-dev.azurewebsites.net`
- SQL local: `.\LOCAL`
- Base local: `talkioai`
- SqlPackage local: `C:\Users\richa\.dotnet\tools\sqlpackage.exe`
- Proyecto BD: `database\Auraly.Database\Auraly.Database.sqlproj`
- DACPAC: `database\Auraly.Database\bin\Debug\Auraly.Database.dacpac`
- Proyecto Function: `src\API\Auraly.Platform.Worker\Auraly.Platform.Worker.csproj`
- Publish profile: `az-talkioai-dev - Zip Deploy`

## Publicar BD local

Compilar DACPAC:

```powershell
dotnet build database\Auraly.Database\Auraly.Database.sqlproj
```

Publicar contra la base local:

```powershell
& 'C:\Users\richa\.dotnet\tools\sqlpackage.exe' `
  /Action:Publish `
  /SourceFile:'database\Auraly.Database\bin\Debug\Auraly.Database.dacpac' `
  /TargetConnectionString:'Server=.\LOCAL;Database=talkioai;User Id=admin;Password=masterkey;TrustServerCertificate=True;' `
  /p:BackupDatabaseBeforeChanges=False `
  /p:DoNotAlterChangeDataCaptureObjects=True `
  /p:DoNotAlterReplicatedObjects=True `
  /p:DropObjectsNotInSource=True `
  /p:BlockOnPossibleDataLoss=False `
  '/p:DoNotDropObjectTypes=Users;Logins;RoleMembership;Permissions'
```

Resultado esperado: `Successfully published database.`

## Publicar BD Azure

La cadena Azure se toma desde la Function App, seccion `ConnectionStrings`, nombre `DefaultConnection`. No imprimirla en consola.

```powershell
dotnet build database\Auraly.Database\Auraly.Database.sqlproj

$app = Get-AzWebApp -ResourceGroupName RG-TALKIOAI-DEV -Name az-talkioai-dev
$target = ($app.SiteConfig.ConnectionStrings | Where-Object { $_.Name -eq 'DefaultConnection' }).ConnectionString
if ([string]::IsNullOrWhiteSpace($target)) { throw 'No se encontro DefaultConnection en Azure.' }

& 'C:\Users\richa\.dotnet\tools\sqlpackage.exe' `
  /Action:Publish `
  /SourceFile:'database\Auraly.Database\bin\Debug\Auraly.Database.dacpac' `
  /TargetConnectionString:$target `
  /p:BackupDatabaseBeforeChanges=False `
  /p:DoNotAlterChangeDataCaptureObjects=True `
  /p:DoNotAlterReplicatedObjects=True `
  /p:DropObjectsNotInSource=True `
  /p:BlockOnPossibleDataLoss=False `
  '/p:DoNotDropObjectTypes=Users;Logins;RoleMembership;Permissions'
```

Resultado esperado: `Successfully published database.`

## Publicar Azure Function

Compilar en Release:

```powershell
dotnet publish src\API\Auraly.Platform.Worker\Auraly.Platform.Worker.csproj -c Release /p:PublishProfile="az-talkioai-dev - Zip Deploy"
```

Crear zip. Usar `tar`, no `Compress-Archive`, porque el paquete debe incluir la carpeta oculta `.azurefunctions` en la raiz:

```powershell
if (Test-Path .\publish\az-talkioai-dev.zip) {
  Remove-Item -LiteralPath .\publish\az-talkioai-dev.zip -Force
}

New-Item -ItemType Directory -Force -Path .\publish | Out-Null

tar -a -cf .\publish\az-talkioai-dev.zip `
  -C .\src\API\Auraly.Platform.Worker\bin\Release\net8.0\linux-x64\publish .
```

Validar que el zip contiene `.azurefunctions`:

```powershell
tar -tf .\publish\az-talkioai-dev.zip | Select-String -Pattern "azurefunctions" | Select-Object -First 10
```

Habilitar temporalmente SCM basic publishing para Kudu:

```powershell
Set-AzResource `
  -ResourceId "/subscriptions/5ea009ce-23c5-4bbd-b1c8-62116d58f596/resourceGroups/RG-TALKIOAI-DEV/providers/Microsoft.Web/sites/az-talkioai-dev/basicPublishingCredentialsPolicies/scm" `
  -Properties @{allow=$true} `
  -Force
```

Subir zip con Kudu OneDeploy. Usar `application/zip`; con `application/octet-stream` responde `415`.

```powershell
$profile = Get-AzWebAppPublishingProfile -ResourceGroupName RG-TALKIOAI-DEV -Name az-talkioai-dev
$zipProfile = ([xml]$profile).publishData.publishProfile |
  Where-Object { $_.publishMethod -eq 'ZipDeploy' } |
  Select-Object -First 1

if ($null -eq $zipProfile) { throw 'No se encontro perfil ZipDeploy.' }

$uri = "https://$($zipProfile.publishUrl)/api/publish?type=zip&clean=true&restart=true"

& curl.exe --ssl-no-revoke -sS -L -X POST `
  -u "$($zipProfile.userName):$($zipProfile.userPWD)" `
  -H "Content-Type: application/zip" `
  --data-binary "@publish\az-talkioai-dev.zip" `
  -w "`nHTTP_STATUS:%{http_code}`n" `
  $uri
```

Resultado esperado: HTTP `202` y un deployment id.

Verificar deployment. Reemplazar `$deploymentId` por el id devuelto:

```powershell
$deploymentId = '<DEPLOYMENT_ID>'
$basic = [Convert]::ToBase64String(
  [System.Text.Encoding]::ASCII.GetBytes("$($zipProfile.userName):$($zipProfile.userPWD)")
)

$deploymentUri = "https://$($zipProfile.publishUrl)/api/deployments/$deploymentId"
$result = Invoke-WebRequest -UseBasicParsing -Uri $deploymentUri -Headers @{ Authorization = "Basic $basic" } -TimeoutSec 60
$result.Content | ConvertFrom-Json | Select-Object id,status,status_text,deployer,received_time,start_time,end_time,complete,active
```

Resultado esperado:

- `status`: `4`
- `complete`: `True`
- `active`: `True`

Verificar Function App:

```powershell
Get-AzWebApp -ResourceGroupName RG-TALKIOAI-DEV -Name az-talkioai-dev |
  Select-Object Name, State, DefaultHostName
```

Resultado esperado: `State = Running`.

Deshabilitar SCM basic publishing al terminar:

```powershell
Set-AzResource `
  -ResourceId "/subscriptions/5ea009ce-23c5-4bbd-b1c8-62116d58f596/resourceGroups/RG-TALKIOAI-DEV/providers/Microsoft.Web/sites/az-talkioai-dev/basicPublishingCredentialsPolicies/scm" `
  -Properties @{allow=$false} `
  -Force
```

## Notas utiles

- Si Azure CLI falla con SSL, usar Azure PowerShell (`Get-AzWebApp`, `Set-AzResource`, `Get-AzWebAppPublishingProfile`) para estas publicaciones.
- Si `curl.exe` falla con revocacion SSL en Windows, usar `--ssl-no-revoke`.
- Si Kudu devuelve `InvalidPackageContentException` por `.azurefunctions`, el zip fue creado mal. Recrearlo con `tar -a -cf`.
- Si Kudu devuelve `415`, revisar que el header sea `Content-Type: application/zip`.
- No imprimir connection strings ni publishing passwords en logs o respuestas.
