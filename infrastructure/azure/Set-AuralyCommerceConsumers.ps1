#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev','prod')][string]$Environment,
    [Parameter(Mandatory)][ValidateSet('function','api')][string]$Target,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$ReleaseVersion
)

$ErrorActionPreference = 'Stop'
$suffix = if ($Environment -eq 'dev') { 'w5usmo6w' } else { '7sov4nxc' }
$group = "RG-AURALY-$($Environment.ToUpperInvariant())"
$api = "api-auraly-$Environment-$suffix"
$engines = "func-auraly-engines-$Environment-$suffix"
$sql = "sql-auraly-$Environment-$suffix"
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$functionOrder = @('ProcessSalesReporting','ProcessAccounting','ProcessDocument','ProcessFiscal')
$apiWorkers = @('SalesReporting','Accounting','DocumentProcessing','Fiscal')
$durableSettings = @(
    'Auraly__DocumentProcessing__DurableActivation__Enabled=true',
    'Auraly__DocumentProcessing__DurableContinuation__Enabled=true',
    'Auraly__Dispatch__SettlementQueue__Enabled=true'
)

function Invoke-AzChecked {
    param([string[]]$Arguments)
    & az @Arguments --only-show-errors
    if ($LASTEXITCODE) { throw "Azure no completó $($Arguments[0]) $($Arguments[1])." }
}

function Wait-Healthy {
    param([string]$Url, [string]$Version)
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri $Url -TimeoutSec 10
            $actualVersion = if ($health.version) { $health.version } else { $health.release.version }
            if ($health.status -eq 'healthy' -and (!$Version -or $actualVersion -eq $Version)) { return $health }
        }
        catch {
            if ($attempt -eq 60) { throw }
        }
        Start-Sleep -Seconds 5
    }
    throw "No se confirmó la salud de $Url."
}

function Stop-Writer {
    param([string]$Kind, [string]$Name)
    Invoke-AzChecked @($Kind,'stop','-g',$group,'-n',$Name,'-o','none')
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        $state = Invoke-AzChecked @($Kind,'show','-g',$group,'-n',$Name,'--query','state','-o','tsv')
        if ("$state".Trim() -eq 'Stopped') { return }
        Start-Sleep -Seconds 3
    }
    throw "No se confirmó la detención de $Name."
}

function Set-RuntimeSettings {
    param([string]$Kind, [string]$Name, [string[]]$Settings)
    Invoke-AzChecked (@($Kind,'config','appsettings','set','-g',$group,'-n',$Name,'--settings') + $Settings + @('-o','none'))
}

function Adopt-OpenDispatchSettlements {
    $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 10).Trim()
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($ip,[ref]$parsed)) { throw 'No se identificó la IP del equipo.' }
    $rule = 'engines-cutover-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
    try {
        Invoke-AzChecked @('sql','server','firewall-rule','create','-g',$group,'-s',$sql,'-n',$rule,
            '--start-ip-address',$ip,'--end-ip-address',$ip,'-o','none')
        $token = (Invoke-AzChecked @('account','get-access-token','--resource','https://database.windows.net/',
            '--query','accessToken','-o','tsv')).Trim()
        $connection = [System.Data.SqlClient.SqlConnection]::new(
            "Server=tcp:$sql.database.windows.net,1433;Initial Catalog=auraly-$Environment;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;")
        try {
            $connection.AccessToken = $token
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = @'
SET NOCOUNT ON;
UPDATE operation
SET ActivationVersion=0,ActivationAvailableAt=SYSUTCDATETIME(),NextAttemptAt=SYSUTCDATETIME(),
    Attempts=0,Status=N'Pending'
FROM dbo.DispatchSettlementOperations operation
JOIN dispatch.Closures closure ON closure.DispatchId=operation.DispatchId AND closure.BusinessId=operation.BusinessId
WHERE operation.ActivationVersion IS NULL AND operation.Status IN(N'Pending',N'Processing')
  AND NOT EXISTS(SELECT 1 FROM dbo.DocumentProcessingJobs job WHERE job.DocumentId=closure.DispatchSettlementId
      AND job.DocumentType=N'DispatchCashDifference' AND job.BusinessId=operation.BusinessId);
SELECT @@ROWCOUNT;
'@
            $adopted = $command.ExecuteScalar()
            Write-Information "Conciliaciones anteriores incorporadas en ${Environment}: $adopted." -InformationAction Continue
        }
        finally { $connection.Dispose(); $token = $null }
    }
    finally {
        Invoke-AzChecked @('sql','server','firewall-rule','delete','-g',$group,'-s',$sql,'-n',$rule,'-o','none')
    }
}

& git -C $repository merge-base --is-ancestor HEAD origin/main
if ($LASTEXITCODE -or (& git -C $repository status --porcelain)) {
    throw 'El relevo requiere un commit limpio integrado en origin/main.'
}
$apiUrl = "https://$api.azurewebsites.net/health"
$engineUrl = "https://$engines.azurewebsites.net/api/health"
$apiVersion = Invoke-AzChecked @('webapp','config','appsettings','list','-g',$group,'-n',$api,
    '--query',"[?name=='Release__Version'].value | [0]",'-o','tsv')
if ("$apiVersion".Trim() -ne $ReleaseVersion) { throw 'La API no pertenece al release solicitado.' }
[void](Wait-Healthy $engineUrl $ReleaseVersion)

$disabledTriggers = @($functionOrder | ForEach-Object { "AzureWebJobs.$_.Disabled=true" })
try {
    Stop-Writer functionapp $engines
    Set-RuntimeSettings functionapp $engines ($durableSettings + $disabledTriggers +
        @('AzureWebJobs.RecoverDocumentInitialSignals.Disabled=true'))
    Stop-Writer webapp $api
    if ($Target -eq 'function') { Adopt-OpenDispatchSettlements }
    $apiEnabled = if ($Target -eq 'api') { 'true' } else { 'false' }
    Set-RuntimeSettings webapp $api ($durableSettings + @($apiWorkers | ForEach-Object { "Auraly__${_}__Worker__Enabled=$apiEnabled" }))
    Invoke-AzChecked @('webapp','start','-g',$group,'-n',$api,'-o','none')
    [void](Wait-Healthy $apiUrl '')
    Invoke-AzChecked @('functionapp','start','-g',$group,'-n',$engines,'-o','none')
    [void](Wait-Healthy $engineUrl $ReleaseVersion)
    if ($Target -eq 'function') {
        foreach ($function in $functionOrder) {
            Set-RuntimeSettings functionapp $engines @("AzureWebJobs.$function.Disabled=false")
            [void](Wait-Healthy $engineUrl $ReleaseVersion)
        }
    }
    Set-RuntimeSettings functionapp $engines @('AzureWebJobs.RecoverDocumentInitialSignals.Disabled=false')
    [void](Wait-Healthy $engineUrl $ReleaseVersion)
    [pscustomobject]@{ Environment=$Environment; Consumer=$Target; Release=$ReleaseVersion }
}
catch {
    if ($Target -eq 'function') {
        $cutoverError = $_
        Write-Warning 'Se interrumpió el relevo. Se restablecerá el consumidor compatible de la API.'
        Invoke-AzChecked @('functionapp','start','-g',$group,'-n',$engines,'-o','none')
        & $PSCommandPath -Environment $Environment -Target api -ReleaseVersion $ReleaseVersion
        throw $cutoverError
    }
    throw
}
