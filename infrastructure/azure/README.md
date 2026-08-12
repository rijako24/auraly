# Despliegue de Auraly en Azure

Esta es la guía canónica para aprovisionar y desplegar Auraly. Todos los comandos se ejecutan desde la raíz de `main`; no se debe publicar desde una rama experimental ni desde el worktree de `feature/gpt-5-mini`.

Los despliegues Bicep son incrementales. No eliminan recursos que no estén en la plantilla, pero cualquier cambio debe revisarse primero con `Validate` y `WhatIf`.

## Ambientes y recursos creados

| Componente | Compartido | DEV | PROD |
| --- | --- | --- | --- |
| Resource group | `RG-AURALY-SHARED` | `RG-AURALY-DEV` | `RG-AURALY-PROD` |
| Azure AI Foundry | `ai-auraly-shared-uo2w7msy` | Identidad con RBAC sobre el compartido | Identidad con RBAC sobre el compartido |
| Managed Identity | — | `id-auraly-dev` | `id-auraly-prod` |
| App Configuration | — | `cfg-auraly-dev-w5usmo6w` | `cfg-auraly-prod-7sov4nxc` |
| Service Bus | — | `sb-auraly-dev-w5usmo6w` | `sb-auraly-prod-7sov4nxc` |
| Storage | — | `stauralydevw5usmo6w` | `stauralyprod7sov4nxc` |
| SQL Server / base | — | `sql-auraly-dev-w5usmo6w` / `auraly-dev` | `sql-auraly-prod-7sov4nxc` / `auraly-prod` |
| Function App | — | `func-auraly-dev-w5usmo6w` | `func-auraly-prod-7sov4nxc` |
| Web API | — | `api-auraly-dev-w5usmo6w` | `api-auraly-prod-7sov4nxc` |
| Admin Static Web App | — | `admin-auraly-dev-w5usmo6w` | `admin-auraly-prod-7sov4nxc` |
| Observabilidad | — | `appi-auraly-dev`, `log-auraly-dev` | `appi-auraly-prod`, `log-auraly-prod` |

Los nombres globales tienen un sufijo determinístico. Si cambia la suscripción, no se deben copiar los sufijos anteriores: se toman de las salidas del despliegue.

Política de costo:

- Azure SQL: Basic, 5 DTU y 2 GB.
- Function App: Flex Consumption, sin instancias always-ready.
- Web API: F1 en DEV y B1 en PROD.
- Admin: Static Web Apps Free.
- App Configuration: Free y acceso con Managed Identity.
- Service Bus: Standard porque el motor usa sesiones.
- Storage: Standard LRS, claves compartidas y acceso público a blobs deshabilitados.
- Application Insights: límite de ingestión de 0,1 GB por día.

Azure AI Foundry existe una sola vez en `RG-AURALY-SHARED`. DEV y PROD consumen los despliegues `gpt-4.1-mini` y `whisper` mediante Managed Identity; no se crean cuentas ni llaves de OpenAI por ambiente.

### Línea base desplegada

La línea base registrada es `0.1.0-rc5`, generada desde el commit `3777f861fd8bab8076836d7b39fc0824dca5cb01` con árbol limpio. El manifiesto local está en `artifacts/releases/0.1.0-rc5/manifest.json`. Se publicaron las Functions de DEV y PROD, la Web API de PROD, los Admin de ambos ambientes y el esquema de ambas bases. La Web API DEV usa F1 y debe comprobarse antes de asumir que está activa, porque el plan puede suspenderse por cuota diaria.

## Ambiente legado: no borrar

`RG-TALKIOAI-DEV` es el ambiente anterior. Al 10 de agosto de 2026 sigue existiendo y contiene, entre otros, `az-talkioai-dev`, `app-auraly-dev`, la Static Web App `Auraly`, el servidor SQL `talkioai`, la base `auraly-db`, `satalkioaidev` y cuentas antiguas de Azure AI.

Este grupo no forma parte de la infraestructura Auraly nueva y **no debe modificarse ni borrarse** durante un despliegue. Su retiro exige una autorización separada, inventario, respaldo, validación de tráfico y plan de reversión. Ninguno de los scripts de esta carpeta implementa ese retiro.

`PUBLICACION.md` y `docs/azure-functions-flex-onedeploy.md` documentan el ambiente legado; no son el procedimiento vigente para DEV o PROD.

## Requisitos locales

- PowerShell 5.1 o superior.
- Azure CLI con Bicep y sesión iniciada.
- Módulos Az: `Az.Accounts`, `Az.Resources`, `Az.Sql`, `Az.Websites` y `Az.ManagedServiceIdentity`.
- .NET SDK 8, Node.js 20 y npm 10.
- `sqlpackage` y `sqlcmd` disponibles.
- Acceso a la suscripción configurada en `Deploy-Auraly.ps1`.
- Rama `main` limpia y actualizada.

Comprobación inicial:

```powershell
git switch main
git pull --ff-only
git status --short
az account show --query '{subscription:id,user:user.name}' -o table
Get-AzContext
```

No se deben escribir contraseñas, tokens, cadenas de conexión ni SAS en archivos versionados, historial de consola o parámetros Bicep permanentes.

## Crear un release reproducible

El mismo release debe desplegarse primero en DEV y después en PROD. La versión tiene formato SemVer, por ejemplo `0.1.0-rc6`.

```powershell
$version = '0.1.0-rc6'
.\infrastructure\azure\New-AuralyRelease.ps1 -Version $version
Get-Content ".\artifacts\releases\$version\manifest.json"
```

El script exige un árbol Git limpio, hace restore bloqueado, build determinístico, pruebas, build del Admin y crea:

- `auraly-function-<version>.zip`
- `auraly-api-<version>.zip`
- `auraly-database-<version>.dacpac`
- `manifest.json` con commit, herramientas, tamaños y SHA-256

Los releases son inmutables: no se reemplaza una carpeta existente. Para una corrección se crea una versión nueva.

## Aprovisionamiento inicial o cambio de infraestructura

Este apartado crea o actualiza recursos. No publica los binarios de Function/API ni el esquema de la base.

Primero solicitar los secretos sin dejarlos en texto plano:

```powershell
$version = '0.1.0-rc6'
$sqlPassword = Read-Host 'SQL administrator password' -AsSecureString
$jwtSecret = Read-Host 'JWT secret' -AsSecureString
$whatsAppVerifyToken = Read-Host 'WhatsApp verify token' -AsSecureString
```

Desplegar una sola vez el recurso compartido y luego cada ambiente. Antes de `Apply`, ejecutar `Validate` y revisar `WhatIf`:

```powershell
.\infrastructure\azure\Deploy-Auraly.ps1 -Mode Validate -Scope Shared -ReleaseVersion $version -IncludeSharedAudio
.\infrastructure\azure\Deploy-Auraly.ps1 -Mode WhatIf  -Scope Shared -ReleaseVersion $version -IncludeSharedAudio
.\infrastructure\azure\Deploy-Auraly.ps1 -Mode Apply   -Scope Shared -ReleaseVersion $version -IncludeSharedAudio

.\infrastructure\azure\Deploy-Auraly.ps1 -Mode Validate -Scope Dev -ReleaseVersion $version -SqlAdministratorPassword $sqlPassword -JwtSecret $jwtSecret -WhatsAppVerifyToken $whatsAppVerifyToken -SeedAppConfiguration
.\infrastructure\azure\Deploy-Auraly.ps1 -Mode WhatIf  -Scope Dev -ReleaseVersion $version -SqlAdministratorPassword $sqlPassword -JwtSecret $jwtSecret -WhatsAppVerifyToken $whatsAppVerifyToken -SeedAppConfiguration
.\infrastructure\azure\Deploy-Auraly.ps1 -Mode Apply   -Scope Dev -ReleaseVersion $version -SqlAdministratorPassword $sqlPassword -JwtSecret $jwtSecret -WhatsAppVerifyToken $whatsAppVerifyToken -SeedAppConfiguration
```

Para PROD se repiten las últimas tres líneas con `-Scope Prod`. No usar `-Scope All` en una liberación normal: la promoción debe permitir validar DEV antes de tocar PROD.

Si el administrador Entra no puede inferirse desde la sesión, pasar también `-SqlEntraAdministratorLogin` y `-SqlEntraAdministratorObjectId`. Conviene entregar explícitamente una contraseña SQL administrada; si se omite, el script genera una aleatoria que no podrá reutilizarse después para publicar el DACPAC.

## Desplegar DEV

### 1. Base de datos DEV

Obtener el `ClientId` de la identidad y solicitar la contraseña SQL:

```powershell
$version = '0.1.0-rc6'
$sqlPassword = Read-Host 'DEV SQL administrator password' -AsSecureString
$identity = Get-AzUserAssignedIdentity -ResourceGroupName 'RG-AURALY-DEV' -Name 'id-auraly-dev'

.\infrastructure\azure\Publish-AuralyDatabase.ps1 `
  -ResourceGroupName 'RG-AURALY-DEV' `
  -ServerName 'sql-auraly-dev-w5usmo6w' `
  -DatabaseName 'auraly-dev' `
  -SqlAdministratorLogin 'auralyadmin' `
  -SqlAdministratorPassword $sqlPassword `
  -ManagedIdentityName 'id-auraly-dev' `
  -ManagedIdentityClientId $identity.ClientId `
  -DacpacPath ".\artifacts\releases\$version\auraly-database-$version.dacpac"
```

El script crea una regla de firewall temporal para la IP del operador, publica con `BlockOnPossibleDataLoss=True` y `DropObjectsNotInSource=False`, autoriza la Managed Identity con lectura, escritura y ejecución, y elimina la regla temporal en `finally`.

### 2. Function DEV (Flex Consumption)

Flex Consumption se despliega con OneDeploy desde un blob privado. No usar `az functionapp deployment source config-zip`: ese flujo puede quedar bloqueado y no es el mecanismo correcto para este plan.

Este bloque es el procedimiento completo para DEV. Detecta si el operador ya tenía el rol y solo retira el permiso si lo creó durante esta ejecución:

```powershell
$version = '0.1.0-rc6'
$resourceGroup = 'RG-AURALY-DEV'
$storage = 'stauralydevw5usmo6w'
$function = 'func-auraly-dev-w5usmo6w'
$container = 'auraly-deploy'
$blob = "auraly-function-$version-$([guid]::NewGuid().ToString('N')).zip"
$zip = (Resolve-Path ".\artifacts\releases\$version\auraly-function-$version.zip").Path
$role = 'Storage Blob Data Contributor'
$operatorObjectId = (az ad signed-in-user show --query id -o tsv).Trim()
$storageScope = (az storage account show -g $resourceGroup -n $storage --query id -o tsv).Trim()
$existingRoleId = az role assignment list --assignee-object-id $operatorObjectId `
  --scope $storageScope --role $role --query '[0].id' -o tsv
$existingRoleId = "$existingRoleId".Trim()
$createdRoleId = $null
$blobUploaded = $false
$packageUri = $null

try {
  if (-not $existingRoleId) {
    $createdRoleId = (az role assignment create --assignee-object-id $operatorObjectId `
      --assignee-principal-type User --scope $storageScope --role $role `
      --query id -o tsv).Trim()
  }

  # RBAC puede tardar unos minutos en propagarse.
  for ($attempt = 1; $attempt -le 12; $attempt++) {
    az storage blob upload --account-name $storage --container-name $container `
      --name $blob --file $zip --auth-mode login --overwrite true
    if (-not $LASTEXITCODE) {
      $blobUploaded = $true
      break
    }
    if ($attempt -eq 12) { throw 'No se pudo subir el paquete de Function.' }
    Start-Sleep -Seconds 10
  }

  $expiry = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mmZ')
  $packageUri = (az storage blob generate-sas --account-name $storage `
    --container-name $container --name $blob --permissions r --expiry $expiry `
    --auth-mode login --as-user --full-uri -o tsv).Trim()
  if (-not $packageUri) { throw 'No se pudo crear el SAS de delegación.' }

  # Evita que az.cmd interprete los & de la URL SAS.
  $azPython = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\python.exe'
  & $azPython -IBm azure.cli deployment group create `
    --name "auraly-dev-function-$version" `
    --resource-group $resourceGroup `
    --template-file '.\infrastructure\azure\function-onedeploy-v2.bicep' `
    --parameters "functionAppName=$function" 'location=eastus2' "packageUri=$packageUri"
  if ($LASTEXITCODE) { throw 'OneDeploy falló.' }

  az functionapp config appsettings set -g $resourceGroup -n $function `
    --settings "Release__Version=$version"
  if ($LASTEXITCODE) { throw 'No se pudo registrar Release__Version.' }
}
finally {
  $packageUri = $null
  if ($blobUploaded) {
    az storage blob delete --account-name $storage --container-name $container `
      --name $blob --auth-mode login
  }
  if ($createdRoleId) {
    az role assignment delete --ids $createdRoleId
  }
}
```

Nunca habilitar claves compartidas ni acceso público al contenedor para simplificar el despliegue. El SAS existe solo en memoria, es de solo lectura y caduca en dos horas.

### 3. Web API DEV

```powershell
az webapp deploy -g 'RG-AURALY-DEV' -n 'api-auraly-dev-w5usmo6w' `
  --src-path ".\artifacts\releases\$version\auraly-api-$version.zip" `
  --type zip --restart true --clean true

az webapp config appsettings set -g 'RG-AURALY-DEV' -n 'api-auraly-dev-w5usmo6w' `
  --settings "Release__Version=$version"
```

El plan DEV es F1 y puede suspenderse al agotar su cuota diaria; esto no indica por sí solo un defecto del release.

### 4. Admin DEV

El workflow `.github/workflows/azure-static-web-apps-auraly.yml` despliega únicamente DEV desde `main` cuando cambia `admin/**`. Una publicación de PROD requiere `workflow_dispatch` explícito con `environment=prod`. Requiere los secretos de GitHub:

- `AZURE_STATIC_WEB_APPS_API_TOKEN_AURALY_DEV`
- `AZURE_STATIC_WEB_APPS_API_TOKEN_AURALY_PROD`

Para dispararlo manualmente:

```powershell
gh workflow run azure-static-web-apps-auraly.yml --ref main
gh run list --workflow azure-static-web-apps-auraly.yml --limit 5
```

Para ejecutar DEV manualmente usa `gh workflow run azure-static-web-apps-auraly.yml --ref main -f environment=dev`. PROD solo se publica con `-f environment=prod`, después de aprobar exactamente el mismo release validado en DEV.

## Desplegar PROD

Solo promover exactamente el mismo `$version` y los mismos hashes aprobados en DEV. Verificar que el commit del manifiesto sea el esperado:

```powershell
$version = '0.1.0-rc6'
$manifest = Get-Content ".\artifacts\releases\$version\manifest.json" | ConvertFrom-Json
$manifest | Select-Object version,commit,dirty,createdUtc
$manifest.artifacts | Format-Table name,bytes,sha256
```

Repetir el procedimiento de base de datos con:

- Resource group: `RG-AURALY-PROD`
- Server: `sql-auraly-prod-7sov4nxc`
- Database: `auraly-prod`
- Identity: `id-auraly-prod`

Desplegar Function con el mismo OneDeploy, sustituyendo:

- Storage: `stauralyprod7sov4nxc`
- Function: `func-auraly-prod-7sov4nxc`
- Resource group: `RG-AURALY-PROD`

Desplegar Web API:

```powershell
az webapp deploy -g 'RG-AURALY-PROD' -n 'api-auraly-prod-7sov4nxc' `
  --src-path ".\artifacts\releases\$version\auraly-api-$version.zip" `
  --type zip --restart true --clean true

az webapp config appsettings set -g 'RG-AURALY-PROD' -n 'api-auraly-prod-7sov4nxc' `
  --settings "Release__Version=$version"
```

El Admin PROD se publica con el workflow descrito arriba.

## Validación posterior

Comprobar versión, salud, configuración y que no quedaron permisos temporales:

```powershell
$devFunction  = 'https://func-auraly-dev-w5usmo6w.azurewebsites.net/api/health'
$prodFunction = 'https://func-auraly-prod-7sov4nxc.azurewebsites.net/api/health'
$prodSwagger  = 'https://api-auraly-prod-7sov4nxc.azurewebsites.net/swagger/index.html'

Invoke-RestMethod $devFunction
Invoke-RestMethod $prodFunction
Invoke-WebRequest $prodSwagger -UseBasicParsing | Select-Object StatusCode
```

Además:

- Ejecutar las pruebas de consola contra DEV y luego un smoke test controlado contra PROD.
- Confirmar que `Release__Version` coincide con el manifiesto.
- Confirmar que Function y API leen App Configuration con Managed Identity.
- Confirmar acceso a SQL sin cadenas con contraseña en App Settings.
- Confirmar que el blob temporal de OneDeploy fue eliminado.
- Confirmar que el rol temporal del operador sobre Storage fue retirado.
- Revisar errores y throttling en Application Insights.
- No considerar un HTTP 200 suficiente: validar una conversación y una reserva completa.

## Reversión

- Function y API: volver a desplegar los ZIP de la última versión aprobada y restaurar su `Release__Version`.
- Admin: revertir en `main` el cambio defectuoso y dejar que el workflow publique el commit de reversión.
- Base de datos: no existe rollback destructivo automático. Antes de cambios incompatibles, verificar Point-in-Time Restore y preparar una migración compensatoria. `DropObjectsNotInSource=False` protege objetos no presentes en el DACPAC, pero no reemplaza un plan de datos.
- Infraestructura: corregir Bicep y volver a desplegar incrementalmente después de `WhatIf`; no borrar el resource group como mecanismo de rollback.

## Archivos operativos

- `Deploy-Auraly.ps1`: valida, previsualiza y aplica infraestructura.
- `main.bicep`: recursos aislados por ambiente y RBAC.
- `shared-ai.bicep`: Azure AI Foundry compartido.
- `New-AuralyRelease.ps1`: release reproducible y manifiesto.
- `Publish-AuralyDatabase.ps1`: DACPAC, permisos de identidad y firewall temporal.
- `function-onedeploy-v2.bicep`: publicación de Function Flex mediante OneDeploy.
- `Sync-AuralySqlFirewall.ps1`: sincroniza IP salientes estables de API/Function cuando aplique.

Los scripts no eliminan el ambiente legado. Cualquier tarea de retiro debe documentarse y aprobarse como operación independiente.
