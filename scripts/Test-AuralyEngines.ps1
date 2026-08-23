param(
    [ValidateSet("All", "Operational", "Accounting", "Fiscal", "Reporting", "EndToEnd", "Architecture")]
    [string]$Suite = "All",
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "artifacts/test-results/engine-certification"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
$project = "tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj"
$suites = if ($Suite -eq "All") {
    @("Architecture", "Operational", "Accounting", "Fiscal", "Reporting", "EndToEnd")
} else {
    @($Suite)
}

try {
    dotnet build database/Auraly.Database/Auraly.Database.sqlproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Database project build failed." }

    dotnet build Auraly.Commerce.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }

    foreach ($engineSuite in $suites) {
        $trxName = "engine-$($engineSuite.ToLowerInvariant()).trx"
        dotnet test $project -c $Configuration --no-build `
            --filter "EngineCertification=$engineSuite" `
            --logger "trx;LogFileName=$trxName" `
            --results-directory $ResultsDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Engine certification suite '$engineSuite' failed."
        }
    }

    if ($Suite -eq "All") {
        dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj `
            -c $Configuration --no-build `
            --logger "trx;LogFileName=engine-code-architecture.trx" `
            --results-directory $ResultsDirectory
        if ($LASTEXITCODE -ne 0) { throw "Architecture and code audit failed." }
    }

    Write-Host "Engine certification completed: $($suites -join ', ')."
    Write-Host "Evidence: $ResultsDirectory"
}
finally {
    Pop-Location
}
