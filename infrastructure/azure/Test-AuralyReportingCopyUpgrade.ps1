#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConsolePath,
    [Parameter(Mandatory)][string]$EvidencePath
)
$ErrorActionPreference='Stop'
# Test data, deliberately fixed: this harness cannot target the live databases.
$configuration=@{ResourceGroup='RG-AURALY-DEV';SqlServer='sql-auraly-dev-w5usmo6w';Database='auraly-rc34-megafruver-rehearsal-20261126'}
$tenant=[guid]'01a06318-d4bf-7f57-9776-4695b7833f7a'
$business=[guid]'01a06318-d4fe-7bfd-b23a-41a17f2fdd88'
$rule='CopyReportingCi'+[guid]::NewGuid().ToString('N')
$created=$false;$connection=$null;$package=$null
function Import-ReleaseFunction([string]$File,[string]$Name) {
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($File,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'El script canónico no se puede analizar.'}
    $nodes=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name},$true))
    if($nodes.Count -ne 1){throw "Se requiere una función canónica única: $Name"}
    return [scriptblock]::Create($nodes[0].Extent.Text)
}
. (Import-ReleaseFunction "$PSScriptRoot/Publish-AuralyReleasePipeline.ps1" 'Complete-ReportingReleaseUpgrade')
. (Import-ReleaseFunction "$PSScriptRoot/Publish-AuralyReleasePipeline.ps1" 'Invoke-AzJson')
. (Import-ReleaseFunction "$PSScriptRoot/Publish-AuralyReleasePipeline.ps1" 'Assert-LastExitCode')
function Get-ProtectedSourceHash {
    $query=$connection.CreateCommand();$query.CommandTimeout=120
    $query.CommandText=@'
SET NOCOUNT ON; SET LOCK_TIMEOUT 10000;
IF DB_NAME()<>N'auraly-rc34-megafruver-rehearsal-20261126'
 OR NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@Business AND TenantId=@Tenant)
 THROW 51901,N'El ensayo sólo admite la copia DEV y su alcance verificado.',1;
SELECT (SELECT
 (SELECT * FROM dbo.SalesReturns WHERE BusinessId=@Business ORDER BY ReturnId FOR JSON PATH) Returns,
 (SELECT SourceDocumentId,SourceDocumentType,PayloadHash,AccountingEntryRequired FROM dbo.AccountingSourceDocuments WHERE BusinessId=@Business ORDER BY SourceDocumentId,SourceDocumentType FOR JSON PATH) Sources,
 (SELECT * FROM dbo.AccountingEntries WHERE BusinessId=@Business ORDER BY EntryId FOR JSON PATH) Entries,
 (SELECT line.* FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries e ON e.EntryId=line.EntryId WHERE e.BusinessId=@Business ORDER BY line.EntryId,line.LineNumber FOR JSON PATH) EntryLines,
 (SELECT * FROM dbo.InventoryMovements WHERE BusinessId=@Business ORDER BY InventoryMovementId FOR JSON PATH) Inventory,
 (SELECT WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence FROM dbo.InventoryBalances WHERE BusinessId=@Business ORDER BY WarehouseId,ProductId FOR JSON PATH) Balances,
 (SELECT ReceivableId,SourceDocumentId,OriginalAmount,OutstandingAmount,Status FROM dbo.Receivables WHERE BusinessId=@Business ORDER BY ReceivableId FOR JSON PATH) Receivables,
 (SELECT PayableId,SourceDocumentId,OriginalAmount,OutstandingAmount,Status FROM dbo.Payables WHERE BusinessId=@Business ORDER BY PayableId FOR JSON PATH) Payables,
 (SELECT PaymentId,PayloadHash,TotalAmount,Status FROM dbo.CustomerPayments WHERE BusinessId=@Business ORDER BY PaymentId FOR JSON PATH) Receipts,
 (SELECT PaymentId,PayloadHash,TotalAmount,Status FROM dbo.SupplierPayments WHERE BusinessId=@Business ORDER BY PaymentId FOR JSON PATH) Payments,
 (SELECT CustomerCreditId,OriginalAmount,AvailableAmount,Status FROM dbo.CustomerCredits WHERE BusinessId=@Business ORDER BY CustomerCreditId FOR JSON PATH) CustomerCredits,
 (SELECT SupplierCreditId,OriginalAmount,AvailableAmount,Status FROM dbo.SupplierCredits WHERE BusinessId=@Business ORDER BY SupplierCreditId FOR JSON PATH) SupplierCredits,
 (SELECT DocumentId,FiscalNumber,UniqueCode,IssuedAt FROM dbo.FiscalDocuments WHERE BusinessId=@Business ORDER BY DocumentId FOR JSON PATH) FiscalRoots
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
'@
    $null=$query.Parameters.AddWithValue('@Tenant',$tenant);$null=$query.Parameters.AddWithValue('@Business',$business)
    try {return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([string]$query.ExecuteScalar())))}finally {$query.Dispose()}
}
function Open-CopyConnection {
    $token=az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    Assert-LastExitCode 'No se obtuvo la identidad SQL del ensayo'
    $opened=[Data.SqlClient.SqlConnection]::new("Server=tcp:$($configuration.SqlServer).database.windows.net,1433;Database=$($configuration.Database);Encrypt=True;TrustServerCertificate=False;Connect Timeout=30;Application Name=ReportingReleaseCopyRehearsal;")
    $opened.AccessToken=$token;$opened.Open();return $opened
}
try {
    $ip=(Invoke-RestMethod 'https://api.ipify.org').Trim()
    $parsed=$null;if(-not[Net.IPAddress]::TryParse($ip,[ref]$parsed)){throw 'La IP del runner no es válida.'}
    az sql server firewall-rule create -g $configuration.ResourceGroup -s $configuration.SqlServer -n $rule --start-ip-address $ip --end-ip-address $ip -o none
    Assert-LastExitCode 'No se pudo habilitar el acceso limitado a la copia';$created=$true
    $connection=Open-CopyConnection
    $before=Get-ProtectedSourceHash
    $connection.Dispose();$connection=$null
    $publish=(Resolve-Path -LiteralPath $ConsolePath).Path
    foreach($name in @('appsettings.json','appSettings.json')) {
        $settings=Join-Path $publish $name
        if(Test-Path -LiteralPath $settings){Remove-Item -LiteralPath $settings}
    }
    $version=dotnet (Join-Path $publish 'Auraly.Console.dll') reporting-projection-version
    if($LASTEXITCODE -or "$version" -notmatch '^[1-9][0-9]*$'){throw 'La versión no proviene del binario canónico.'}
    $ReleaseVersion='0.1.1-copy-rehearsal'
    $package=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('auraly-reporting-copy-'+[guid]::NewGuid().ToString('N'))))
    $null=[IO.Directory]::CreateDirectory($package)
    $releasePath=$package
    $archive=Join-Path $package "auraly-reporting-maintenance-$ReleaseVersion.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($publish,$archive)
    $manifest=@{reportingProjectionVersion=[int]$version;artifacts=@(@{name=(Split-Path $archive -Leaf);sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()})}
    $token=az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    Assert-LastExitCode 'No se obtuvo la identidad del mantenimiento'
    $watch=[Diagnostics.Stopwatch]::StartNew()
    Complete-ReportingReleaseUpgrade -AccessToken $token
    $watch.Stop()
    $noOp=[Diagnostics.Stopwatch]::StartNew()
    Complete-ReportingReleaseUpgrade -AccessToken $token
    $noOp.Stop();$token=$null
    $connection=Open-CopyConnection
    $after=Get-ProtectedSourceHash
    if($after -ne $before){throw 'El ensayo alteró una fuente financiera, fiscal o de inventario.'}
    @{commit=$env:GITHUB_SHA;atUtc=[DateTimeOffset]::UtcNow.ToString('o');database=$configuration.Database;productionWrites=0;nativeReleaseHookCompleted=$true;secondCallCompleted=$true;protectedFinancialUnchanged=$true;protectedFinancialSha256=$before;elapsedMs=$watch.ElapsedMilliseconds;secondCallMs=$noOp.ElapsedMilliseconds}|ConvertTo-Json|Set-Content -LiteralPath $EvidencePath
}finally {
    try {
        if($connection){$connection.Dispose()}
        if($package -and(Test-Path -LiteralPath $package)) {
            $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
            if(-not $package.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $package -Leaf) -notmatch '^auraly-reporting-copy-[a-f0-9]{32}$'){throw 'Ruta temporal de ensayo inválida.'}
            Remove-Item -LiteralPath $package -Recurse -Force
        }
    }finally {
        if($created) {
            az sql server firewall-rule delete -g $configuration.ResourceGroup -s $configuration.SqlServer -n $rule -o none
            Assert-LastExitCode 'No se pudo retirar el acceso temporal de la copia'
        }
    }
}
