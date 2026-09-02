#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateSet('Audit', 'Apply')]
    [string]$Action = 'Audit',

    [string]$Confirmation = '',

    [string]$ValidationConnectionString = ''
)

$ErrorActionPreference = 'Stop'
$canonicalTenantId = [Guid]'A0A10000-0000-0000-0000-000000000000'
$expectedConfirmation = 'PURGE_NON_AURALY_PROD'
$serverName = 'sql-auraly-prod-7sov4nxc.database.windows.net'
$databaseName = 'auraly-prod'

if ($Action -eq 'Apply' -and $Confirmation -cne $expectedConfirmation) {
    throw "Apply requires the exact confirmation '$expectedConfirmation'."
}

function Quote-SqlIdentifier {
    param([Parameter(Mandatory)][string]$Value)
    return '[' + $Value.Replace(']', ']]') + ']'
}

function Invoke-ReaderRows {
    param(
        [Parameter(Mandatory)][System.Data.SqlClient.SqlConnection]$Connection,
        [System.Data.SqlClient.SqlTransaction]$Transaction,
        [Parameter(Mandatory)][string]$CommandText,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 600
    $command.CommandText = $CommandText
    if ($null -ne $Transaction) { $command.Transaction = $Transaction }
    foreach ($entry in $Parameters.GetEnumerator()) {
        [void]$command.Parameters.AddWithValue($entry.Key, $entry.Value)
    }
    $reader = $command.ExecuteReader()
    try {
        $rows = [Collections.Generic.List[object]]::new()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($index = 0; $index -lt $reader.FieldCount; $index++) {
                $row[$reader.GetName($index)] = if ($reader.IsDBNull($index)) {
                    $null
                } else {
                    $reader.GetValue($index)
                }
            }
            $rows.Add([pscustomobject]$row)
        }
        return @($rows)
    }
    finally {
        $reader.Dispose()
        $command.Dispose()
    }
}

function Invoke-NonQuery {
    param(
        [Parameter(Mandatory)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory)][System.Data.SqlClient.SqlTransaction]$Transaction,
        [Parameter(Mandatory)][string]$CommandText,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.Transaction = $Transaction
    $command.CommandTimeout = 600
    $command.CommandText = $CommandText
    foreach ($entry in $Parameters.GetEnumerator()) {
        [void]$command.Parameters.AddWithValue($entry.Key, $entry.Value)
    }
    try { return $command.ExecuteNonQuery() }
    finally { $command.Dispose() }
}

function Get-OwnershipPredicate {
    param([Parameter(Mandatory)][object[]]$Path)

    $parts = [Collections.Generic.List[string]]::new()
    $closings = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Path.Count; $index++) {
        $edge = $Path[$index]
        $childAlias = "d$index"
        $parentAlias = "d$($index + 1)"
        $parentName = "$(Quote-SqlIdentifier $edge.ParentSchema).$(Quote-SqlIdentifier $edge.ParentTable)"
        $joins = @($edge.Columns | ForEach-Object {
            "$childAlias.$(Quote-SqlIdentifier $_.ChildColumn)=$parentAlias.$(Quote-SqlIdentifier $_.ParentColumn)"
        }) -join ' AND '
        $parts.Add("EXISTS (SELECT 1 FROM $parentName AS $parentAlias WHERE $joins AND ")
        $closings.Add(')')
    }
    $parts.Add("d$($Path.Count).[TenantId]<>@AuralyTenantId")
    for ($index = $closings.Count - 1; $index -ge 0; $index--) {
        $parts.Add($closings[$index])
    }
    return $parts -join ''
}

$isValidation = -not [string]::IsNullOrWhiteSpace($ValidationConnectionString)
if ($isValidation) {
    $connection = [System.Data.SqlClient.SqlConnection]::new($ValidationConnectionString)
}
else {
    $accessToken = "$(& az account get-access-token --resource 'https://database.windows.net/' --query accessToken --output tsv)".Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'Azure CLI did not return an Azure SQL access token.'
    }
    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=tcp:$serverName,1433;Initial Catalog=$databaseName;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;")
    $connection.AccessToken = $accessToken
}

try {
    $connection.Open()
    if ($isValidation) {
        if ($connection.Database -notlike 'AuralyPurgeValidation_*') {
            throw "Local validation is restricted to databases named 'AuralyPurgeValidation_*'."
        }
    }
    elseif ($connection.Database -cne $databaseName -or $connection.DataSource -notlike "*$serverName*") {
        throw "Resolved unexpected production SQL target '$($connection.DataSource)/$($connection.Database)'."
    }

    $tenants = Invoke-ReaderRows -Connection $connection -CommandText @'
SELECT TenantId,TenantKey,Name,IsActive
FROM dbo.Tenants
ORDER BY Name,TenantId;
'@
    $canonical = @($tenants | Where-Object { $_.TenantId -eq $canonicalTenantId })
    if ($canonical.Count -ne 1 -or $canonical[0].TenantKey -cne '@auraly' -or
        $canonical[0].Name -cne 'AURALY' -or -not $canonical[0].IsActive) {
        throw 'The canonical active AURALY tenant was not found with its expected immutable identity.'
    }

    $targets = @($tenants | Where-Object { $_.TenantId -ne $canonicalTenantId })
    Write-Output "Target: $($connection.DataSource)/$($connection.Database)"
    Write-Output "Canonical tenant preserved: AURALY ($canonicalTenantId)"
    Write-Output "Non-AURALY tenants found: $($targets.Count)"
    foreach ($tenant in $targets) {
        Write-Output "PURGE $($tenant.TenantId) | $($tenant.Name) | $($tenant.TenantKey)"
    }

    $activeUsers = Invoke-ReaderRows -Connection $connection -CommandText @'
SELECT UserId,Username,Email
FROM dbo.AppUsers
WHERE TenantId=@AuralyTenantId AND IsActive=1
ORDER BY Username,UserId;
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
    $technicalUsers = Invoke-ReaderRows -Connection $connection -CommandText @'
SELECT COUNT(*) AS Total
FROM dbo.AppUsers
WHERE TenantId=@AuralyTenantId
  AND ((NormalizedUsername=N'ADMIN' AND NormalizedEmail=N'ADMIN@AURALY.AI')
    OR (FirstName=N'Administrador' AND LastName=N'Auraly'
        AND NormalizedEmail LIKE N'RETIRED+%@INVALID.AURALY.LOCAL'));
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
    $fictitiousEmployees = Invoke-ReaderRows -Connection $connection -CommandText @'
SELECT COUNT(*) AS Total
FROM dbo.Employees employeeValue
JOIN dbo.Businesses businessValue ON businessValue.BusinessId=employeeValue.BusinessId
WHERE businessValue.TenantId=@AuralyTenantId
  AND employeeValue.EmployeeId='A0A10000-0000-0000-0000-000000000003';
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
    $occasionalSuppliers = Invoke-ReaderRows -Connection $connection -CommandText @'
SELECT COUNT(*) AS Total
FROM dbo.Suppliers supplierValue
JOIN dbo.Businesses businessValue ON businessValue.BusinessId=supplierValue.BusinessId
WHERE businessValue.TenantId=@AuralyTenantId AND supplierValue.IsActive=1
  AND supplierValue.Identification=N'OCASIONAL'
  AND supplierValue.Name=N'Gasto ocasional / sin proveedor';
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
    $roles = Invoke-ReaderRows -Connection $connection -CommandText @'
SELECT NormalizedName
FROM dbo.AppRoles
WHERE TenantId=@AuralyTenantId AND IsActive=1
  AND NormalizedName IN(N'CASHIER',N'SUPERVISOR',N'SELLER',N'ADMINISTRATIVE',N'ACCOUNTANT',N'ADMINISTRATOR')
ORDER BY NormalizedName;
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
    Write-Output "Active AURALY users: $($activeUsers.Count)"
    foreach ($user in $activeUsers) { Write-Output "USER $($user.UserId) | $($user.Username) | $($user.Email)" }
    Write-Output "Obsolete technical administrator rows: $($technicalUsers[0].Total)"
    Write-Output "Fictitious Equipo AURALY employee rows: $($fictitiousEmployees[0].Total)"
    Write-Output "Active occasional-expense suppliers: $($occasionalSuppliers[0].Total)"
    Write-Output "Canonical active roles: $($roles.Count) | $($roles.NormalizedName -join ', ')"
    if ($Action -eq 'Audit') {
        Write-Output 'Audit completed without modifying data.'
        return
    }

    $transaction = $connection.BeginTransaction([Data.IsolationLevel]::Serializable)
    try {
        $lockedTenants = Invoke-ReaderRows -Connection $connection -Transaction $transaction -CommandText @'
SELECT TenantId,TenantKey,Name,IsActive
FROM dbo.Tenants WITH (UPDLOCK,HOLDLOCK)
ORDER BY Name,TenantId;
'@
        $lockedCanonical = @($lockedTenants | Where-Object { $_.TenantId -eq $canonicalTenantId })
        if ($lockedCanonical.Count -ne 1 -or $lockedCanonical[0].TenantKey -cne '@auraly' -or
            $lockedCanonical[0].Name -cne 'AURALY' -or -not $lockedCanonical[0].IsActive) {
            throw 'AURALY changed between audit and purge; transaction aborted.'
        }

        $tableRows = Invoke-ReaderRows -Connection $connection -Transaction $transaction -CommandText @'
SELECT t.object_id AS ObjectId,s.name AS SchemaName,t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.is_ms_shipped=0;
'@
        $tables = @{}
        foreach ($row in $tableRows) { $tables[[int]$row.ObjectId] = $row }
        $tenantTable = @($tableRows | Where-Object {
            $_.SchemaName -eq 'dbo' -and $_.TableName -eq 'Tenants'
        })
        if ($tenantTable.Count -ne 1) { throw 'dbo.Tenants metadata could not be resolved uniquely.' }
        $tenantObjectId = [int]$tenantTable[0].ObjectId

        $foreignKeyRows = Invoke-ReaderRows -Connection $connection -Transaction $transaction -CommandText @'
SELECT fk.object_id AS ForeignKeyId,fk.name AS ForeignKeyName,
       fk.parent_object_id AS ChildObjectId,fk.referenced_object_id AS ParentObjectId,
       fkc.constraint_column_id AS Ordinal,
       childColumn.name AS ChildColumn,parentColumn.name AS ParentColumn,
       childColumn.is_nullable AS ChildColumnIsNullable,
       fk.is_disabled AS IsDisabled,fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns childColumn
  ON childColumn.object_id=fkc.parent_object_id AND childColumn.column_id=fkc.parent_column_id
JOIN sys.columns parentColumn
  ON parentColumn.object_id=fkc.referenced_object_id AND parentColumn.column_id=fkc.referenced_column_id
ORDER BY fk.object_id,fkc.constraint_column_id;
'@
        $edges = [Collections.Generic.List[object]]::new()
        foreach ($group in ($foreignKeyRows | Group-Object ForeignKeyId)) {
            $first = $group.Group[0]
            $child = $tables[[int]$first.ChildObjectId]
            $parent = $tables[[int]$first.ParentObjectId]
            $edges.Add([pscustomobject]@{
                ForeignKeyId = [int]$first.ForeignKeyId
                ForeignKeyName = [string]$first.ForeignKeyName
                ChildObjectId = [int]$first.ChildObjectId
                ParentObjectId = [int]$first.ParentObjectId
                ChildSchema = [string]$child.SchemaName
                ChildTable = [string]$child.TableName
                ParentSchema = [string]$parent.SchemaName
                ParentTable = [string]$parent.TableName
                AllRequired = -not ($group.Group.ChildColumnIsNullable -contains $true)
                WasDisabled = [bool]$first.IsDisabled
                WasUntrusted = [bool]$first.IsNotTrusted
                Columns = @($group.Group | Sort-Object Ordinal | ForEach-Object {
                    [pscustomobject]@{
                        ChildColumn = [string]$_.ChildColumn
                        ParentColumn = [string]$_.ParentColumn
                    }
                })
            })
        }

        $paths = @{}
        $depths = @{}
        $paths[$tenantObjectId] = @()
        $depths[$tenantObjectId] = 0
        do {
            $added = 0
            $candidates = @($edges | Where-Object {
                $paths.ContainsKey($_.ParentObjectId) -and -not $paths.ContainsKey($_.ChildObjectId)
            } | Group-Object ChildObjectId)
            foreach ($candidate in $candidates) {
                $edge = @($candidate.Group | Sort-Object `
                    @{Expression = { -not $_.AllRequired }; Ascending = $true},
                    @{Expression = { $depths[$_.ParentObjectId] }; Ascending = $true},
                    ForeignKeyName)[0]
                $paths[$edge.ChildObjectId] = @($edge) + @($paths[$edge.ParentObjectId])
                $depths[$edge.ChildObjectId] = 1 + $depths[$edge.ParentObjectId]
                $added++
            }
        } while ($added -gt 0)

        $unreachableTenantTables = @($tableRows | Where-Object {
            $objectId = [int]$_.ObjectId
            -not $paths.ContainsKey($objectId) -and
            (Invoke-ReaderRows -Connection $connection -Transaction $transaction -CommandText `
                'SELECT TOP (1) 1 AS Found FROM sys.columns WHERE object_id=@ObjectId AND name=N''TenantId'';' `
                -Parameters @{'@ObjectId'=$objectId}).Count -gt 0
        })
        if ($unreachableTenantTables.Count -gt 0) {
            throw "Tenant-scoped tables are not connected to dbo.Tenants: $($unreachableTenantTables.TableName -join ', ')."
        }

        $reachableIds = @($paths.Keys | ForEach-Object { [int]$_ })
        $constraints = @($edges | Where-Object {
            $reachableIds -contains $_.ChildObjectId -and -not $_.WasDisabled
        })
        foreach ($edge in $constraints) {
            $tableName = "$(Quote-SqlIdentifier $edge.ChildSchema).$(Quote-SqlIdentifier $edge.ChildTable)"
            $constraintName = Quote-SqlIdentifier $edge.ForeignKeyName
            [void](Invoke-NonQuery -Connection $connection -Transaction $transaction `
                -CommandText "ALTER TABLE $tableName NOCHECK CONSTRAINT $constraintName;")
        }

        $deletedByTable = [Collections.Generic.List[object]]::new()
        $ownedTables = @($tableRows | Where-Object {
            [int]$_.ObjectId -ne $tenantObjectId -and $paths.ContainsKey([int]$_.ObjectId)
        } | Sort-Object @{Expression = { $depths[[int]$_.ObjectId] }; Descending = $true}, SchemaName, TableName)
        foreach ($table in $ownedTables) {
            $objectId = [int]$table.ObjectId
            $qualifiedName = "$(Quote-SqlIdentifier $table.SchemaName).$(Quote-SqlIdentifier $table.TableName)"
            $predicate = Get-OwnershipPredicate -Path @($paths[$objectId])
            $deleted = Invoke-NonQuery -Connection $connection -Transaction $transaction `
                -CommandText "DELETE d0 FROM $qualifiedName AS d0 WHERE $predicate;" `
                -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
            if ($deleted -gt 0) {
                $deletedByTable.Add([pscustomobject]@{
                    Table = "$($table.SchemaName).$($table.TableName)"
                    Rows = $deleted
                })
            }
        }
        $deletedTenants = Invoke-NonQuery -Connection $connection -Transaction $transaction `
            -CommandText 'DELETE FROM dbo.Tenants WHERE TenantId<>@AuralyTenantId;' `
            -Parameters @{'@AuralyTenantId'=$canonicalTenantId}

        [void](Invoke-NonQuery -Connection $connection -Transaction $transaction -CommandText @'
DECLARE @Now DATETIME2(7)=SYSUTCDATETIME();
CREATE TABLE #RetiredUsers(UserId UNIQUEIDENTIFIER PRIMARY KEY);
INSERT #RetiredUsers(UserId)
SELECT UserId
FROM dbo.AppUsers
WHERE TenantId=@AuralyTenantId
  AND (
    (NormalizedUsername=N'ADMIN' AND NormalizedEmail=N'ADMIN@AURALY.AI')
    OR (FirstName=N'Administrador' AND LastName=N'Auraly'
        AND NormalizedEmail LIKE N'RETIRED+%@INVALID.AURALY.LOCAL'));

UPDATE sessionValue
SET Status=N'Revoked',RevokedAt=@Now,RevocationReason=N'IdentityRetired',UpdatedAt=@Now
FROM dbo.AuthenticationSessions sessionValue
JOIN #RetiredUsers retired ON retired.UserId=sessionValue.UserId
WHERE sessionValue.Status=N'Active';

UPDATE tokenValue
SET RevokedAt=@Now
FROM dbo.RefreshTokens tokenValue
JOIN #RetiredUsers retired ON retired.UserId=tokenValue.UserId
WHERE tokenValue.RevokedAt IS NULL;

DELETE assignment
FROM dbo.UserRoles assignment
JOIN #RetiredUsers retired ON retired.UserId=assignment.UserId;

UPDATE userValue
SET Username=CONCAT(N'retired-',LEFT(REPLACE(CONVERT(NVARCHAR(36),userValue.UserId),N'-',N''),12)),
    NormalizedUsername=UPPER(CONCAT(N'retired-',LEFT(REPLACE(CONVERT(NVARCHAR(36),userValue.UserId),N'-',N''),12))),
    Email=CONCAT(N'retired+',REPLACE(CONVERT(NVARCHAR(36),userValue.UserId),N'-',N''),N'@invalid.auraly.local'),
    NormalizedEmail=UPPER(CONCAT(N'retired+',REPLACE(CONVERT(NVARCHAR(36),userValue.UserId),N'-',N''),N'@invalid.auraly.local')),
    IsActive=0,AccessFailedCount=0,LockoutEnd=NULL,UpdatedAt=@Now
FROM dbo.AppUsers userValue
JOIN #RetiredUsers retired ON retired.UserId=userValue.UserId;

DECLARE @ReplacementUserId UNIQUEIDENTIFIER=(
  SELECT TOP(1) userValue.UserId
  FROM dbo.AppUsers userValue
  JOIN dbo.UserRoles assignment ON assignment.UserId=userValue.UserId
  JOIN dbo.AppRoles roleValue ON roleValue.RoleId=assignment.RoleId
  WHERE userValue.TenantId=@AuralyTenantId AND userValue.IsActive=1
    AND roleValue.NormalizedName=N'ADMINISTRATOR'
    AND NOT EXISTS(SELECT 1 FROM #RetiredUsers retired WHERE retired.UserId=userValue.UserId)
  ORDER BY userValue.CreatedAt,userValue.UserId);

IF EXISTS(SELECT 1 FROM #RetiredUsers) AND @ReplacementUserId IS NULL
  THROW 51000, 'No existe un administrador real de AURALY para reasignar referencias técnicas.', 1;

DELETE tokenValue FROM dbo.RefreshTokens tokenValue JOIN #RetiredUsers retired ON retired.UserId=tokenValue.UserId;
DELETE sessionValue FROM dbo.AuthenticationSessions sessionValue JOIN #RetiredUsers retired ON retired.UserId=sessionValue.UserId;
DELETE leaseValue FROM dbo.OfflineAuthenticationLeases leaseValue JOIN #RetiredUsers retired ON retired.UserId=leaseValue.UserId;
DELETE resetValue FROM dbo.PasswordResetRequests resetValue JOIN #RetiredUsers retired ON retired.UserId=resetValue.UserId;
DELETE pushValue FROM dbo.PosApprovalPushSubscriptions pushValue JOIN #RetiredUsers retired ON retired.UserId=pushValue.UserId;
DELETE credentialValue FROM dbo.SupervisorCredentials credentialValue JOIN #RetiredUsers retired ON retired.UserId=credentialValue.UserId;
DELETE loginValue FROM dbo.UserExternalLogins loginValue JOIN #RetiredUsers retired ON retired.UserId=loginValue.UserId;
DELETE assignment FROM dbo.UserRoles assignment JOIN #RetiredUsers retired ON retired.UserId=assignment.UserId;

DECLARE @ReferenceSql NVARCHAR(MAX)=N'';
SELECT @ReferenceSql +=
  N'UPDATE childValue SET '+QUOTENAME(childColumn.name)+N'=@ReplacementUserId FROM '
  +QUOTENAME(childSchema.name)+N'.'+QUOTENAME(childTable.name)+N' childValue JOIN #RetiredUsers retired ON retired.UserId=childValue.'+QUOTENAME(childColumn.name)+N';'
FROM sys.foreign_keys foreignKey
JOIN sys.foreign_key_columns foreignKeyColumn ON foreignKeyColumn.constraint_object_id=foreignKey.object_id
JOIN sys.tables childTable ON childTable.object_id=foreignKey.parent_object_id
JOIN sys.schemas childSchema ON childSchema.schema_id=childTable.schema_id
JOIN sys.columns childColumn ON childColumn.object_id=foreignKey.parent_object_id AND childColumn.column_id=foreignKeyColumn.parent_column_id
WHERE foreignKey.referenced_object_id=OBJECT_ID(N'dbo.AppUsers')
  AND NOT (childSchema.name=N'dbo' AND childTable.name IN(
    N'RefreshTokens',N'AuthenticationSessions',N'OfflineAuthenticationLeases',N'PasswordResetRequests',
    N'PosApprovalPushSubscriptions',N'UserExternalLogins',N'UserRoles'))
  AND NOT (childSchema.name=N'dbo' AND childTable.name=N'SupervisorCredentials' AND childColumn.name=N'UserId')
  AND NOT (childSchema.name=N'dbo' AND childTable.name=N'AuditLogs');
EXEC sys.sp_executesql @ReferenceSql,N'@ReplacementUserId UNIQUEIDENTIFIER',
  @ReplacementUserId=@ReplacementUserId;

DELETE userValue FROM dbo.AppUsers userValue JOIN #RetiredUsers retired ON retired.UserId=userValue.UserId;

DELETE serviceValue
FROM dbo.EmployeeServices serviceValue
WHERE serviceValue.EmployeeId='A0A10000-0000-0000-0000-000000000003';

DELETE exceptionValue
FROM dbo.EmployeeScheduleExceptions exceptionValue
WHERE exceptionValue.EmployeeId='A0A10000-0000-0000-0000-000000000003';

UPDATE dbo.EmployeeWorkingHours
SET IsActive=0,UpdatedAt=@Now
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';

DELETE FROM dbo.EmployeeWorkingHours
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';

UPDATE dbo.Reservations SET EmployeeId=NULL
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';
UPDATE dbo.BusinessAvailabilityBlocks SET EmployeeId=NULL
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';
UPDATE dbo.BusinessInboundContacts SET EmployeeId=NULL
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';

IF EXISTS(SELECT 1 FROM payroll.Employments WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003')
  THROW 51000, 'Equipo AURALY tiene vínculos de nómina inesperados; eliminación abortada.', 1;

UPDATE dbo.Employees
SET IsActive=0,UpdatedAt=@Now
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';

DELETE FROM dbo.Employees
WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003';

UPDATE scheduling
SET RequireEmployee=0,UpdatedAt=@Now
FROM dbo.BusinessSchedulingSettings scheduling
JOIN dbo.Businesses businessValue ON businessValue.BusinessId=scheduling.BusinessId
WHERE businessValue.TenantId=@AuralyTenantId;
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId})

        foreach ($edge in $constraints) {
            $tableName = "$(Quote-SqlIdentifier $edge.ChildSchema).$(Quote-SqlIdentifier $edge.ChildTable)"
            $constraintName = Quote-SqlIdentifier $edge.ForeignKeyName
            [void](Invoke-NonQuery -Connection $connection -Transaction $transaction `
                -CommandText "ALTER TABLE $tableName WITH CHECK CHECK CONSTRAINT $constraintName;")
        }

        $remaining = Invoke-ReaderRows -Connection $connection -Transaction $transaction -CommandText @'
SELECT TenantId,TenantKey,Name,IsActive
FROM dbo.Tenants;
'@
        if ($remaining.Count -ne 1 -or $remaining[0].TenantId -ne $canonicalTenantId -or
            $remaining[0].TenantKey -cne '@auraly' -or $remaining[0].Name -cne 'AURALY' -or
            -not $remaining[0].IsActive) {
            throw 'Post-purge invariant failed: production must contain exactly active AURALY.'
        }

        $canonicalState = Invoke-ReaderRows -Connection $connection -Transaction $transaction -CommandText @'
SELECT
  (SELECT COUNT(*) FROM dbo.AppUsers WHERE TenantId=@AuralyTenantId
     AND ((NormalizedUsername=N'ADMIN' AND NormalizedEmail=N'ADMIN@AURALY.AI')
       OR (FirstName=N'Administrador' AND LastName=N'Auraly' AND NormalizedEmail LIKE N'RETIRED+%@INVALID.AURALY.LOCAL'))) AS TechnicalUsers,
  (SELECT COUNT(*) FROM dbo.Employees
     WHERE EmployeeId='A0A10000-0000-0000-0000-000000000003') AS FictitiousEmployees,
  (SELECT COUNT(*) FROM dbo.Suppliers supplierValue JOIN dbo.Businesses businessValue ON businessValue.BusinessId=supplierValue.BusinessId
     WHERE businessValue.TenantId=@AuralyTenantId AND supplierValue.IsActive=1
       AND supplierValue.Identification=N'OCASIONAL' AND supplierValue.Name=N'Gasto ocasional / sin proveedor') AS OccasionalSuppliers,
  (SELECT COUNT(*) FROM dbo.AppRoles WHERE TenantId=@AuralyTenantId AND IsActive=1
     AND NormalizedName IN(N'CASHIER',N'SUPERVISOR',N'SELLER',N'ADMINISTRATIVE',N'ACCOUNTANT',N'ADMINISTRATOR')) AS CanonicalRoles;
'@ -Parameters @{'@AuralyTenantId'=$canonicalTenantId}
        if ($canonicalState[0].TechnicalUsers -ne 0 -or $canonicalState[0].FictitiousEmployees -ne 0 -or
            $canonicalState[0].OccasionalSuppliers -lt 1 -or $canonicalState[0].CanonicalRoles -ne 6) {
            throw 'Post-purge invariant failed for AURALY users, employees, supplier, or canonical roles.'
        }

        $transaction.Commit()
        Write-Output "Deleted tenants: $deletedTenants"
        Write-Output "Tables with deleted dependent rows: $($deletedByTable.Count)"
        foreach ($item in $deletedByTable | Sort-Object Table) {
            Write-Output "DELETED $($item.Rows) | $($item.Table)"
        }
        Write-Output 'Purge committed: only AURALY remains; technical identity and fictitious employee were physically deleted; supplier and six roles remain.'
    }
    catch {
        try { $transaction.Rollback() } catch { Write-Warning "Rollback reporting failed: $($_.Exception.Message)" }
        throw
    }
    finally {
        $transaction.Dispose()
    }
}
finally {
    $connection.Dispose()
}
