#Requires -Version 7.2
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$server = 'sql-auraly-prod-7sov4nxc'
$database = 'auraly-prod'
$resourceGroup = 'RG-AURALY-PROD'
$rule = "github-commerce-audit-$([guid]::NewGuid().ToString('N').Substring(0,10))"
$connection = $null

function Read-Aggregate {
    param([string]$Label, [string]$Sql)
    $command = $connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 120
    $reader = $command.ExecuteReader()
    try {
        while ($reader.Read()) {
            $row = [ordered]@{ check = $Label }
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $value = $reader.GetValue($i)
                $row[$reader.GetName($i)] = if ($value -is [DBNull]) { $null } else { $value }
            }
            $row | ConvertTo-Json -Compress -Depth 4 | Write-Output
        }
    }
    finally {
        $reader.Dispose()
        $command.Dispose()
    }
}

try {
    $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
    $parsedIp = $null
    if (-not [Net.IPAddress]::TryParse($ip, [ref]$parsedIp)) { throw 'Invalid runner IP.' }
    az sql server firewall-rule create --resource-group $resourceGroup --server $server --name $rule --start-ip-address $ip --end-ip-address $ip --output none
    if ($LASTEXITCODE -ne 0) { throw 'Could not open temporary SQL access.' }
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) { throw 'Could not obtain SQL token.' }
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=tcp:$server.database.windows.net,1433;Initial Catalog=$database;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Application Name=CommerceCutoverReadOnlyAudit;")
    $connection.AccessToken = $token
    $connection.Open()

    Read-Aggregate 'scope' @'
SELECT (SELECT COUNT_BIG(*) FROM dbo.Tenants) AS Tenants,
       (SELECT COUNT_BIG(*) FROM dbo.Businesses) AS Businesses,
       (SELECT COUNT_BIG(*) FROM (SELECT TenantId FROM dbo.Businesses GROUP BY TenantId HAVING COUNT(*)>1) x) AS TenantsWithMultipleBusinesses,
       CASE WHEN COL_LENGTH(N'dbo.Products',N'BusinessId') IS NULL THEN 1 ELSE 0 END AS ProductCutoverAlreadyApplied;
'@
    if (-not (Read-Aggregate 'legacy_schema' "SELECT CASE WHEN COL_LENGTH(N'dbo.Products',N'BusinessId') IS NULL THEN 0 ELSE 1 END AS Present" | Select-String '"Present":1')) {
        throw 'Expected pre-cutover production schema is absent; audit query must be adapted.'
    }

    Read-Aggregate 'master_rows' @'
DECLARE @names TABLE (Name SYSNAME PRIMARY KEY);
INSERT @names VALUES (N'Products'),(N'ProductCategories'),(N'ProductBrands'),(N'ProductUnits'),
    (N'TaxProfiles'),(N'ProductBarcodes'),(N'ProductIdentifiers'),(N'ProductLinks'),
    (N'ProductAliases'),(N'ProductImages'),(N'ProductOffers'),
    (N'ProductSearchTerms'),(N'ProductRecommendationRules'),
    (N'Suppliers'),(N'SupplierProducts'),(N'PriceChannels'),
    (N'Customers'),(N'Employees'),(N'CommerceSellers'),(N'Carriers'),
    (N'CounterpartyTaxProfiles');
CREATE TABLE #audit (TableName SYSNAME, Rows BIGINT, MissingBusiness BIGINT, TenantMismatch BIGINT);
DECLARE @name SYSNAME, @sql NVARCHAR(MAX);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @names;
OPEN c;
FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@name),N'U') IS NULL OR COL_LENGTH(N'dbo.' + @name,N'BusinessId') IS NULL
        THROW 52100, 'Expected legacy master table or BusinessId is missing.', 1;
    SET @sql=N'INSERT #audit SELECT N''' + REPLACE(@name,N'''',N'''''') + N''',COUNT_BIG(*),
        COALESCE(SUM(CASE WHEN b.BusinessId IS NULL THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END),0),'
        + CASE WHEN COL_LENGTH(N'dbo.' + @name,N'TenantId') IS NOT NULL
          THEN N'COALESCE(SUM(CASE WHEN m.TenantId IS NOT NULL AND m.TenantId<>b.TenantId THEN CONVERT(BIGINT,1) ELSE CONVERT(BIGINT,0) END),0)'
          ELSE N'CONVERT(BIGINT,0)' END
        + N' FROM dbo.' + QUOTENAME(@name) + N' m LEFT JOIN dbo.Businesses b ON b.BusinessId=m.BusinessId;';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM c INTO @name;
END
CLOSE c;
DEALLOCATE c;
SELECT * FROM #audit ORDER BY TableName;
'@

    Read-Aggregate 'channels_and_images' @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.PriceChannels) AS Channels,
 (SELECT COUNT_BIG(*) FROM dbo.PriceChannelItems) AS ChannelItems,
 (SELECT COUNT_BIG(*) FROM dbo.PriceChannelExclusions) AS ChannelExclusions,
 (SELECT COUNT_BIG(*) FROM dbo.ProductImages) AS Images,
 (SELECT COALESCE(SUM(CONVERT(BIGINT,DATALENGTH(MediaUrl))),0) FROM dbo.ProductImages) AS ImageUrlBytes,
 (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ProductImageId,ProductId,ProductOfferId,MediaUrl,AltText,DisplayOrder,IsPrimary,IsActive,CreatedAt,UpdatedAt)) FROM dbo.ProductImages) AS ImageDataChecksum,
 (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(PriceChannelId,Code,Name,Strategy,Value,IsActive,CreatedAt)) FROM dbo.PriceChannels) AS ChannelDataChecksum,
 (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(PriceChannelItemId,PriceChannelId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt)) FROM dbo.PriceChannelItems) AS ChannelItemChecksum,
 (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(PriceChannelExclusionId,PriceChannelId,ScopeType,ProductId,ProductCategoryId,ProductBrandId,CreatedAt)) FROM dbo.PriceChannelExclusions) AS ChannelExclusionChecksum;
'@

    Read-Aggregate 'cross_scope_references' @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.ProductImages i JOIN dbo.Products p ON p.ProductId=i.ProductId JOIN dbo.Businesses ib ON ib.BusinessId=i.BusinessId WHERE ib.TenantId<>p.TenantId) AS ImagesWrongProductTenant,
 (SELECT COUNT_BIG(*) FROM dbo.ProductImages i JOIN dbo.ProductOffers o ON o.ProductOfferId=i.ProductOfferId JOIN dbo.Businesses ib ON ib.BusinessId=i.BusinessId WHERE ib.TenantId<>o.TenantId OR i.ProductId<>o.ProductId) AS ImagesWrongOffer,
 (SELECT COUNT_BIG(*) FROM dbo.PriceChannelItems i JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId JOIN dbo.Businesses cb ON cb.BusinessId=c.BusinessId JOIN dbo.Products p ON p.ProductId=i.ProductId WHERE cb.TenantId<>p.TenantId) AS ChannelItemsWrongTenant,
 (SELECT COUNT_BIG(*) FROM dbo.PriceChannelExclusions e JOIN dbo.PriceChannels c ON c.PriceChannelId=e.PriceChannelId JOIN dbo.Businesses cb ON cb.BusinessId=c.BusinessId LEFT JOIN dbo.Products p ON p.ProductId=e.ProductId LEFT JOIN dbo.ProductCategories pc ON pc.ProductCategoryId=e.ProductCategoryId LEFT JOIN dbo.ProductBrands pb ON pb.ProductBrandId=e.ProductBrandId LEFT JOIN dbo.Businesses pcb ON pcb.BusinessId=pc.BusinessId LEFT JOIN dbo.Businesses pbb ON pbb.BusinessId=pb.BusinessId WHERE (p.ProductId IS NOT NULL AND p.TenantId<>cb.TenantId) OR (pc.ProductCategoryId IS NOT NULL AND pcb.TenantId<>cb.TenantId) OR (pb.ProductBrandId IS NOT NULL AND pbb.TenantId<>cb.TenantId)) AS ChannelExclusionsWrongTenant,
 (SELECT COUNT_BIG(*) FROM dbo.ProductPrices pp JOIN dbo.Businesses b ON b.BusinessId=pp.BusinessId JOIN dbo.Products p ON p.ProductId=pp.ProductId WHERE b.TenantId<>p.TenantId) AS PricesWrongTenant,
 (SELECT COUNT_BIG(*) FROM dbo.ProductBarcodes x JOIN dbo.Products p ON p.ProductId=x.ProductId JOIN dbo.Businesses b ON b.BusinessId=x.BusinessId WHERE b.TenantId<>p.TenantId) AS BarcodesWrongTenant,
 (SELECT COUNT_BIG(*) FROM dbo.ProductIdentifiers x JOIN dbo.Products p ON p.ProductId=x.ProductId JOIN dbo.Businesses b ON b.BusinessId=x.BusinessId WHERE b.TenantId<>p.TenantId) AS IdentifiersWrongTenant,
 (SELECT COUNT_BIG(*) FROM dbo.SupplierProducts x JOIN dbo.Products p ON p.ProductId=x.ProductId JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId JOIN dbo.Businesses b ON b.BusinessId=x.BusinessId JOIN dbo.Businesses sb ON sb.BusinessId=s.BusinessId WHERE b.TenantId<>p.TenantId OR b.TenantId<>sb.TenantId) AS SupplierProductsWrongTenant;
'@

    Read-Aggregate 'new_unique_key_collisions' @'
SELECT
 (SELECT COUNT_BIG(*) FROM (SELECT b.TenantId,c.Code FROM dbo.PriceChannels c JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId GROUP BY b.TenantId,c.Code HAVING COUNT(*)>1) x) AS ChannelCodes,
 (SELECT COUNT_BIG(*) FROM (SELECT b.TenantId,x.Barcode FROM dbo.ProductBarcodes x JOIN dbo.Businesses b ON b.BusinessId=x.BusinessId GROUP BY b.TenantId,x.Barcode HAVING COUNT(*)>1) x) AS Barcodes,
 (SELECT COUNT_BIG(*) FROM (SELECT b.TenantId,x.IdentifierType,x.Value FROM dbo.ProductIdentifiers x JOIN dbo.Businesses b ON b.BusinessId=x.BusinessId GROUP BY b.TenantId,x.IdentifierType,x.Value HAVING COUNT(*)>1) x) AS Identifiers,
 (SELECT COUNT_BIG(*) FROM (SELECT b.TenantId,x.Identification FROM dbo.Suppliers x JOIN dbo.Businesses b ON b.BusinessId=x.BusinessId GROUP BY b.TenantId,x.Identification HAVING COUNT(*)>1) x) AS Suppliers,
 (SELECT COUNT_BIG(*) FROM (SELECT x.TenantId,x.Code FROM dbo.TaxProfiles x GROUP BY x.TenantId,x.Code HAVING COUNT(*)>1) x) AS TaxProfiles,
 (SELECT COUNT_BIG(*) FROM (SELECT x.TenantId,x.PartyId FROM dbo.Customers x GROUP BY x.TenantId,x.PartyId HAVING COUNT(*)>1) x) AS Customers;
'@
}
finally {
    if ($connection) { $connection.Dispose() }
    az sql server firewall-rule delete --resource-group $resourceGroup --server $server --name $rule --output none 2>$null
}
