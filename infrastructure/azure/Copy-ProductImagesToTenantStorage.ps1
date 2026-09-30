#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev','prod')][string]$Environment,
    [Parameter(Mandatory)][string]$AccessToken,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$configuration = if ($Environment -eq 'dev') {
    @{ Server='sql-auraly-dev-w5usmo6w'; Database='auraly-dev'; Storage='stauralydevw5usmo6w' }
} else {
    @{ Server='sql-auraly-prod-7sov4nxc'; Database='auraly-prod'; Storage='stauralyprod7sov4nxc' }
}

function Invoke-Storage {
    param([string[]]$Arguments)
    $output = & az storage @Arguments --account-name $configuration.Storage --auth-mode login --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure Storage command failed: $($Arguments[0..2] -join ' ')" }
    if ($output) { return ($output | ConvertFrom-Json) }
}

function Get-BlobInventory {
    param([string]$Container)
    $inventory = @{}
    $exists = Invoke-Storage -Arguments @('container','exists','--name',$Container)
    if (-not $exists.exists) { return $inventory }
    $marker = $null
    do {
        $args = @('blob','list','--container-name',$Container,'--prefix','products/',
            '--num-results','500','--show-next-marker')
        if ($marker) { $args += @('--marker',$marker) }
        $page = Invoke-Storage -Arguments $args
        if ($page -is [array]) {
            if ($page.Count -eq 0 -or
                $page[-1].PSObject.Properties.Name -notcontains 'nextMarker') {
                throw 'Azure CLI blob list did not return its continuation marker.'
            }
            $items = @($page | Select-Object -SkipLast 1)
            $marker = [string]$page[-1].nextMarker
        }
        else {
            $items = @($page.items)
            $marker = [string]$page.nextMarker
        }
        foreach ($blob in $items) {
            if ($null -eq $blob.name -or $null -eq $blob.properties.contentLength) {
                throw 'Blob inventory is missing a name or content length.'
            }
            $inventory[[string]$blob.name] = [pscustomobject]@{
                Length = [long]$blob.properties.contentLength
                Status = [string]$blob.properties.copy.status
            }
        }
    } while ($marker)
    return $inventory
}

$connection = [System.Data.SqlClient.SqlConnection]::new(
    "Server=tcp:$($configuration.Server).database.windows.net,1433;" +
    "Initial Catalog=$($configuration.Database);Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Application Name=ProductImageTenantStorageCutover;")
$connection.AccessToken = $AccessToken
try {
    $connection.Open()
    $images = @{}
    $lastId = [guid]::Empty
    do {
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = @'
SELECT TOP (500) i.ProductImageId,p.TenantId,i.MediaUrl
FROM dbo.ProductImages i
JOIN dbo.Products p ON p.ProductId=i.ProductId
WHERE i.ProductImageId>@LastId
ORDER BY i.ProductImageId;
'@
        [void]$command.Parameters.Add('@LastId',[System.Data.SqlDbType]::UniqueIdentifier)
        $command.Parameters['@LastId'].Value = $lastId
        $reader = $command.ExecuteReader()
        $count = 0
        try {
            while ($reader.Read()) {
                $count++
                $lastId = $reader.GetGuid(0)
                $tenantId = $reader.GetGuid(1)
                $mediaRef = $reader.GetString(2)
                if ($mediaRef.StartsWith('https://',[StringComparison]::OrdinalIgnoreCase)) { continue }
                if (-not $mediaRef.StartsWith('products/',[StringComparison]::Ordinal)) {
                    throw 'A product image has an unsupported relative storage reference.'
                }
                if (-not $images.ContainsKey($tenantId)) {
                    $images[$tenantId] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                }
                [void]$images[$tenantId].Add($mediaRef)
            }
        }
        finally {
            $reader.Dispose()
            $command.Dispose()
        }
    } while ($count -eq 500)

    if ($images.Count -eq 0) {
        Write-Output 'Product image storage cutover: no relative product image references.'
        return
    }

    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT TenantId,BusinessId FROM dbo.Businesses ORDER BY TenantId,BusinessId;'
    $reader = $command.ExecuteReader()
    $businesses = @{}
    try {
        while ($reader.Read()) {
            $tenantId = $reader.GetGuid(0)
            if (-not $images.ContainsKey($tenantId)) { continue }
            if (-not $businesses.ContainsKey($tenantId)) { $businesses[$tenantId] = [Collections.Generic.List[guid]]::new() }
            $businesses[$tenantId].Add($reader.GetGuid(1))
        }
    }
    finally {
        $reader.Dispose()
        $command.Dispose()
    }

    foreach ($tenantId in $images.Keys) {
        $destination = "tenant-$($tenantId.ToString('N'))"
        $sources = [Collections.Generic.List[object]]::new()
        $sourceInventory = @{}
        foreach ($businessId in $businesses[$tenantId]) {
            $source = "business-$($businessId.ToString('N'))"
            $inventory = Get-BlobInventory $source
            if ($inventory.Count -eq 0) { continue }
            foreach ($name in $inventory.Keys) {
                if ($sourceInventory.ContainsKey($name)) {
                    throw 'Two business containers contain the same product image path; manual reconciliation is required.'
                }
                $sourceInventory[$name] = $inventory[$name]
            }
            $sources.Add([pscustomobject]@{ Container=$source; Count=$inventory.Count })
        }
        $targetInventory = Get-BlobInventory $destination
        $missingReferences = @($images[$tenantId] | Where-Object {
            -not $sourceInventory.ContainsKey($_) -and -not $targetInventory.ContainsKey($_)
        })
        if ($missingReferences.Count -gt 0) {
            $message = "Product image tenant $tenantId has $($missingReferences.Count) referenced blobs missing from source and target storage."
            if ($Environment -eq 'prod') { throw $message }
            Write-Warning $message
        }
        $missing = @($sourceInventory.Keys | Where-Object { -not $targetInventory.ContainsKey($_) })
        foreach ($name in $sourceInventory.Keys) {
            if ($targetInventory.ContainsKey($name) -and
                ($targetInventory[$name].Length -ne $sourceInventory[$name].Length -or
                 $targetInventory[$name].Status -in @('failed','pending'))) {
                throw 'A target product image blob conflicts with the source or has not completed copying.'
            }
        }
        Write-Output "Product image tenant $tenantId`: referenced=$($images[$tenantId].Count), source=$($sourceInventory.Count), missing=$($missing.Count)."
        if ($DryRun -or $missing.Count -eq 0) { continue }

        [void](Invoke-Storage -Arguments @('container','create','--name',$destination))
        foreach ($source in $sources) {
            [void](Invoke-Storage -Arguments @('blob','copy','start-batch',
                '--destination-container',$destination,
                '--source-container',$source.Container,
                '--pattern','products/*'))
        }
        $verified = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            $targetInventory = Get-BlobInventory $destination
            $pending = @($sourceInventory.Keys | Where-Object {
                -not $targetInventory.ContainsKey($_) -or
                $targetInventory[$_].Length -ne $sourceInventory[$_].Length -or
                $targetInventory[$_].Status -eq 'pending'
            })
            $failed = @($sourceInventory.Keys | Where-Object {
                $targetInventory.ContainsKey($_) -and $targetInventory[$_].Status -eq 'failed'
            })
            if ($failed.Count -gt 0) { throw 'A product image blob copy failed.' }
            if ($pending.Count -eq 0) { $verified = $true; break }
            Start-Sleep -Seconds 10
        }
        if (-not $verified) { throw 'Timed out verifying copied product image blobs.' }
        Write-Output "Product image tenant $tenantId`: verified=$($sourceInventory.Count)."
    }
}
finally {
    $connection.Dispose()
}
