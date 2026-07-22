param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$project = Join-Path $repoRoot 'src\Console\MimosBabySpa.Console\MimosBabySpa.Console.csproj'
$suites = Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'medidental-*.json' |
    Sort-Object Name

if ($suites.Count -eq 0)
{
    throw 'No se encontraron suites medidental-*.json.'
}

$failures = 0
foreach ($suite in $suites)
{
    Write-Host ''
    Write-Host "=== $($suite.Name) ===" -ForegroundColor Cyan

    $arguments = @('run', '--project', $project)
    if ($NoBuild)
    {
        $arguments += '--no-build'
    }

    $arguments += @('--', 'eval-seed-extractor', $suite.FullName)
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0)
    {
        $failures++
    }
}

Write-Host ''
Write-Host "[medidental-regression] suites=$($suites.Count) failed=$failures"
exit $(if ($failures -eq 0) { 0 } else { 1 })
