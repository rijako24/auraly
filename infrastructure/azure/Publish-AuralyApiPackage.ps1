[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$AppName,
    [Parameter(Mandatory)][string]$PackagePath,
    [ValidateRange(1, 1800)][int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "No existe el paquete de API: $PackagePath"
}

# Linux may keep assemblies mapped while ZIP deployment replaces their files.
# Stop the process first and wait for extraction, not application startup.
$previousDeploymentId = [string](& az webapp log deployment list --resource-group $ResourceGroup `
    --name $AppName --query '[0].id' --output tsv)
if ($LASTEXITCODE -ne 0) { throw 'No se pudo identificar la publicación anterior de la API.' }
$previousDeploymentId = ($previousDeploymentId ?? '').Trim()
& az webapp stop --resource-group $ResourceGroup --name $AppName --output none
if ($LASTEXITCODE -ne 0) { throw 'No se pudo detener la API antes de reemplazar sus archivos.' }
$appJson = & az webapp show --resource-group $ResourceGroup --name $AppName --output json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar el estado de la API.' }
$app = $appJson | ConvertFrom-Json
$state = if ($null -ne $app.state) { $app.state } else { $app.properties.state }
if ("$state".Trim() -ne 'Stopped') {
    throw 'No se confirmó la detención de la API; no se reemplazarán sus archivos.'
}
$deploymentArguments = @('--resource-group', $ResourceGroup, '--name', $AppName,
    '--src-path', $PackagePath, '--type', 'zip', '--clean', 'true', '--restart', 'false',
    '--async', 'true', '--track-status', 'false', '--timeout', ($TimeoutSeconds * 1000).ToString(), '--output', 'json')
# Keep a wall-clock deadline independent of the CLI's internal polling. Use
# structured arguments through a child PowerShell host on Windows and Linux;
# no command text contains resource names or filesystem paths.
$startInfo = [Diagnostics.ProcessStartInfo]::new([Environment]::ProcessPath)
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', @'
$publicationArguments = ConvertFrom-Json $env:AURALY_API_PUBLICATION_ARGUMENTS
& az webapp deploy @publicationArguments
exit $LASTEXITCODE
'@)) { $startInfo.ArgumentList.Add($argument) }
$startInfo.Environment['AURALY_API_PUBLICATION_ARGUMENTS'] = ConvertTo-Json -InputObject $deploymentArguments -Compress
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$started = $false
try {
    $started = $process.Start()
    if (-not $started) { throw 'No se pudo iniciar la publicación de la API.' }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'La publicación superó su tiempo máximo; la API permanece detenida.'
    }
    $deploymentJson = $stdout.GetAwaiter().GetResult()
    $diagnostics = $stderr.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($diagnostics)) { Write-Information $diagnostics -InformationAction Continue }
    if ($process.ExitCode -ne 0) {
        throw 'Falló la publicación del paquete; la API permanece detenida.'
    }
}
finally {
    if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
# Azure accepts the upload asynchronously so its HTTP response does not wait
# through extraction. The CLI still waits for ZIP deployment completion with
# the explicit timeout above. Bind its result to a new completed deployment;
# an old successful record or an acceptance alone must never restart writers.
$deployment = $deploymentJson | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($deployment.id) -or $deployment.id -eq $previousDeploymentId -or
    $deployment.status -ne 4 -or $deployment.complete -isnot [bool] -or -not $deployment.complete) {
    throw 'Azure no confirmó la extracción completa de esta publicación; la API permanece detenida.'
}
& az webapp start --resource-group $ResourceGroup --name $AppName --output none
if ($LASTEXITCODE -ne 0) { throw 'No se pudo iniciar la API después de publicar el paquete completo.' }
