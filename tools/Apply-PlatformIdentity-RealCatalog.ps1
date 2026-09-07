[CmdletBinding()]
param(
    [switch]$ConfirmRealCatalogMigration,
    [string]$BackupPath
)

$ErrorActionPreference = 'Stop'
$sqlServer = 'lpc:.'
$catalogDatabase = 'HRMS_Catalog'
$historyTable = 'dbo.__EFMigrationsHistoryCatalog'
$targetMigration = '20260906130913_AddPlatformIdentity'
$expectedHistory = @(
    '20260823113202_InitialCatalog'
    '20260905142921_AddTenantDatabaseProvider'
)
$platformTables = @(
    'PlatformUsers'
    'PlatformRoles'
    'PlatformPermissions'
    'PlatformUserRoles'
    'PlatformRolePermissions'
    'PlatformRefreshTokens'
)
$tenantTablesNotExpectedInCatalog = @('Employees', 'Users', 'EmploymentHistory')
$sqlConnection = 'Server=lpc:.;Database=HRMS_Catalog;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;'
$backupFile = $null
$backupVerified = $false
$migrationAttempted = $false
$phase = 'initialization'

function Invoke-SqlRead {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Query,
        [Parameter(Mandatory)][string]$Label
    )

    Write-Host "[$Label]"
    $arguments = @('-S', $sqlServer, '-E', '-C', '-b', '-d', $Database, '-h', '-1', '-W', '-s', '|', '-Q', "SET NOCOUNT ON;`r`n$Query")
    $output = @(& sqlcmd.exe @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host ([string]$_) }
    if ($null -eq $exitCode) { throw "$Label did not provide an exit code." }
    Write-Host "Exit code: $exitCode"
    if ($exitCode -ne 0) { throw "$Label failed with exit code $exitCode." }
    return @($output | ForEach-Object { [string]$_ } | Where-Object { $_.Trim() })
}

function Get-SingleSqlValue {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Query,
        [Parameter(Mandatory)][string]$Label
    )

    $rows = @(Invoke-SqlRead -Database $Database -Query $Query -Label $Label)
    if ($rows.Count -ne 1) { throw "$Label returned $($rows.Count) rows instead of one." }
    return $rows[0].Trim()
}

function Assert-NoRows {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Rows,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Rows.Count -ne 0) { throw "$Message $($Rows -join '; ')" }
}

function Get-TenantSnapshot {
    $rows = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Pre-migration tenant snapshot' -Query 'SELECT CONVERT(nvarchar(36), Id), TenantCode, Host, ShardKey, CONVERT(nvarchar(20), Status), DatabaseProvider FROM dbo.Tenants ORDER BY TenantCode;')
    $snapshot = @()
    foreach ($row in $rows) {
        $parts = $row -split '\|', 6
        if ($parts.Count -ne 6) { throw "Unexpected tenant row shape: $row" }
        $snapshot += [pscustomobject]@{
            Id = $parts[0].Trim()
            TenantCode = $parts[1].Trim()
            Host = $parts[2].Trim()
            ShardKey = $parts[3].Trim()
            Status = $parts[4].Trim()
            DatabaseProvider = $parts[5].Trim()
        }
    }
    return @($snapshot)
}

function Assert-TenantSnapshotUnchanged {
    param([Parameter(Mandatory)][object[]]$Before)

    $after = @(Get-TenantSnapshot)
    if ($after.Count -ne $Before.Count) { throw "Tenant count changed from $($Before.Count) to $($after.Count)." }
    foreach ($beforeRow in $Before) {
        $afterRow = @($after | Where-Object { $_.Id -ceq $beforeRow.Id })
        if ($afterRow.Count -ne 1) { throw "Tenant snapshot row missing or duplicated: $($beforeRow.TenantCode)." }
        foreach ($property in @('TenantCode', 'Host', 'ShardKey', 'Status', 'DatabaseProvider')) {
            if ($afterRow[0].$property -cne $beforeRow.$property) {
                throw "Tenant $($beforeRow.TenantCode) changed in $property."
            }
        }
    }
}

function Assert-ExpectedTenants {
    param([Parameter(Mandatory)][object[]]$Snapshot)

    $expected = @(
        [pscustomobject]@{ TenantCode = 'DEMO01'; Id = '11111111-1111-1111-1111-111111111111'; Host = 'demo01.localhost'; ShardKey = 'demo01'; DatabaseProvider = 'SqlServer' }
        [pscustomobject]@{ TenantCode = 'DEMO02'; Id = '22222222-2222-2222-2222-222222222222'; Host = 'demo02.localhost'; ShardKey = 'demo02'; DatabaseProvider = 'SqlServer' }
    )
    foreach ($expectedTenant in $expected) {
        $actual = @($Snapshot | Where-Object { $_.TenantCode -ceq $expectedTenant.TenantCode })
        if ($actual.Count -ne 1) { throw "$($expectedTenant.TenantCode) expected one row, found $($actual.Count)." }
        foreach ($property in @('Id', 'Host', 'ShardKey', 'DatabaseProvider')) {
            if ($actual[0].$property -cne $expectedTenant.$property) {
                throw "$($expectedTenant.TenantCode) mismatch in $property."
            }
        }
    }
}

function Get-BackupTarget {
    if ($BackupPath) {
        return [IO.Path]::GetFullPath($BackupPath)
    }

    $defaultDirectory = Get-SingleSqlValue -Database 'master' -Label 'SQL Server default backup directory' -Query "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));"
    if ([string]::IsNullOrWhiteSpace($defaultDirectory)) {
        throw 'SQL Server did not report a default backup directory; provide -BackupPath explicitly.'
    }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    return Join-Path $defaultDirectory.TrimEnd('\') "HRMS_Catalog_PrePlatformIdentity_$stamp.bak"
}

function Invoke-DotNetEfMigration {
    param([Parameter(Mandatory)][string]$ConnectionString)

    $arguments = @(
        'ef', 'database', 'update', $targetMigration,
        '--project', 'Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj',
        '--startup-project', 'Backend\HRMS.API\HRMS.API.csproj',
        '--context', 'HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext',
        '--connection', $ConnectionString,
        '--no-build'
    )
    Write-Host '[Apply exact Platform Identity migration]'
    Write-Host 'dotnet ef database update 20260906130913_AddPlatformIdentity --project Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj --startup-project Backend\HRMS.API\HRMS.API.csproj --context HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext --connection <integrated-security HRMS_Catalog connection> --no-build'
    & dotnet @arguments
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { throw 'dotnet EF did not provide an exit code.' }
    Write-Host "dotnet EF exit code: $exitCode"
    if ($exitCode -ne 0) { throw "The exact Platform Identity migration failed with exit code $exitCode." }
}

function Get-PostFailureEvidence {
    try {
        $history = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Failure evidence: migration history' -Query "SELECT MigrationId FROM $historyTable ORDER BY MigrationId;")
        Write-Host "Failure evidence history: $($history -join ', ')"
        $tables = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Failure evidence: platform tables' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($platformTables -join "','")') ORDER BY name;")
        Write-Host "Failure evidence platform tables: $($tables -join ', ')"
    }
    catch {
        Write-Warning "Could not collect failure evidence: $($_.Exception.Message)"
    }
}

try {
    if (-not $ConfirmRealCatalogMigration) {
        throw 'Refusing to run without -ConfirmRealCatalogMigration.'
    }
    $confirmation = Read-Host 'Type exactly APPLY PLATFORM IDENTITY TO HRMS_CATALOG to continue'
    if ($confirmation -cne 'APPLY PLATFORM IDENTITY TO HRMS_CATALOG') {
        throw 'Interactive confirmation did not match exactly; no mutation was attempted.'
    }

    $phase = '[1/7] Tool and SQL connectivity preflight'
    Write-Host $phase
    if (-not (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue)) { throw 'sqlcmd.exe was not found.' }
    $serverIdentity = @(Invoke-SqlRead -Database 'master' -Label 'SQL Server local integrated connectivity' -Query 'SELECT @@SERVERNAME, SUSER_SNAME();')
    if ($serverIdentity.Count -ne 1) { throw 'SQL Server identity result was unexpected.' }
    $databaseIdentity = Get-SingleSqlValue -Database $catalogDatabase -Label 'Real catalog database identity' -Query 'SELECT DB_NAME();'
    if ($databaseIdentity -cne $catalogDatabase) { throw "Database identity mismatch: expected $catalogDatabase, received $databaseIdentity." }

    $phase = '[2/7] Pre-migration safety checks'
    Write-Host $phase
    $history = @((Invoke-SqlRead -Database $catalogDatabase -Label 'Pre-migration history' -Query "SELECT MigrationId FROM $historyTable ORDER BY MigrationId;") | ForEach-Object { $_.Trim() })
    if ($history.Count -ne $expectedHistory.Count -or @($expectedHistory | Where-Object { $history -notcontains $_ }).Count -ne 0 -or $history -contains $targetMigration) {
        throw "Pre-migration history is not the expected two-migration state: $($history -join ', ')."
    }
    $platformPresence = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Pre-migration platform-table absence' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($platformTables -join "','")') ORDER BY name;")
    Assert-NoRows -Rows $platformPresence -Message 'Platform Identity tables already exist:'
    $coreMissing = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Pre-migration catalog schema' -Query @"
WITH expected(TableName, ColumnName) AS
(
    SELECT * FROM (VALUES
        ('Tenants', 'Id'), ('Tenants', 'TenantCode'), ('Tenants', 'Host'),
        ('Tenants', 'ShardKey'), ('Tenants', 'Status'), ('Tenants', 'DatabaseProvider')
    ) v(TableName, ColumnName)
)
SELECT CONCAT(e.TableName, '.', e.ColumnName)
FROM expected e
WHERE OBJECT_ID(N'dbo.' + e.TableName, N'U') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.' + e.TableName, N'U') AND c.name = e.ColumnName);
"@)
    Assert-NoRows -Rows $coreMissing -Message 'Required catalog schema is incomplete:'
    foreach ($table in @('Tenants', 'TenantBranding')) {
        if ((Get-SingleSqlValue -Database $catalogDatabase -Label "Catalog table $table" -Query "SELECT CASE WHEN OBJECT_ID(N'dbo.$table', N'U') IS NULL THEN 0 ELSE 1 END;") -ne '1') { throw "Required catalog table is missing: $table" }
    }
    $tenantSnapshot = @(Get-TenantSnapshot)
    Assert-ExpectedTenants -Snapshot $tenantSnapshot
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Duplicate tenant codes' -Query 'SELECT TenantCode FROM dbo.Tenants GROUP BY TenantCode HAVING COUNT(*) > 1;') -Message 'Duplicate TenantCode values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Duplicate tenant hosts' -Query 'SELECT Host FROM dbo.Tenants GROUP BY Host HAVING COUNT(*) > 1;') -Message 'Duplicate Host values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Duplicate tenant shard keys' -Query 'SELECT ShardKey FROM dbo.Tenants GROUP BY ShardKey HAVING COUNT(*) > 1;') -Message 'Duplicate ShardKey values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Blank tenant codes' -Query "SELECT TenantCode FROM dbo.Tenants WHERE TenantCode IS NULL OR LTRIM(RTRIM(TenantCode)) = '';" ) -Message 'Blank TenantCode values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Invalid tenant providers' -Query "SELECT TenantCode FROM dbo.Tenants WHERE DatabaseProvider IS NULL OR LTRIM(RTRIM(DatabaseProvider)) = '' OR DatabaseProvider NOT IN ('SqlServer', 'MySql');") -Message 'Invalid DatabaseProvider values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Tenant-table contamination' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($tenantTablesNotExpectedInCatalog -join "','")') ORDER BY name;") -Message 'Tenant-domain tables found in the catalog:'

    $phase = '[3/7] Backup target and explicit confirmation'
    Write-Host $phase
    $backupFile = Get-BackupTarget
    $backupDirectory = Split-Path -Parent $backupFile
    if (-not (Test-Path $backupDirectory -PathType Container)) { throw "Backup directory does not exist: $backupDirectory" }
    if (Test-Path $backupFile) { throw "Backup target already exists; refusing to overwrite: $backupFile" }
    Write-Host "Backup target: $backupFile"

    $phase = '[4/7] SQL Server backup and verification'
    Write-Host $phase
    $escapedBackupFile = $backupFile.Replace("'", "''")
    Invoke-SqlRead -Database 'master' -Label 'Create copy-only catalog backup' -Query "BACKUP DATABASE [$catalogDatabase] TO DISK = N'$escapedBackupFile' WITH COPY_ONLY, CHECKSUM;" | Out-Null
    Invoke-SqlRead -Database 'master' -Label 'Verify catalog backup' -Query "RESTORE VERIFYONLY FROM DISK = N'$escapedBackupFile' WITH CHECKSUM;" | Out-Null
    $backupVerified = $true
    Write-Host 'REAL HRMS_CATALOG BACKUP VERIFIED'

    $phase = '[5/7] Apply exact Platform Identity migration'
    Write-Host $phase
    $migrationAttempted = $true
    Invoke-DotNetEfMigration -ConnectionString $sqlConnection

    $phase = '[6/7] Post-migration verification'
    Write-Host $phase
    $postHistory = @((Invoke-SqlRead -Database $catalogDatabase -Label 'Post-migration history' -Query "SELECT MigrationId FROM $historyTable ORDER BY MigrationId;") | ForEach-Object { $_.Trim() })
    $expectedPostHistory = @($expectedHistory + $targetMigration)
    if ($postHistory.Count -ne $expectedPostHistory.Count -or @($expectedPostHistory | Where-Object { $postHistory -notcontains $_ }).Count -ne 0) { throw "Post-migration history is unexpected: $($postHistory -join ', ')." }
    $postTables = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Post-migration platform tables' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($platformTables -join "','")') ORDER BY name;")
    if ($postTables.Count -ne $platformTables.Count -or @($platformTables | Where-Object { $postTables -notcontains $_ }).Count -ne 0) { throw 'Post-migration Platform Identity table verification failed.' }
    if ((Get-SingleSqlValue -Database $catalogDatabase -Label 'PlatformUsers count' -Query 'SELECT COUNT(*) FROM dbo.PlatformUsers;') -ne '0') { throw 'Unexpected PlatformUser exists after migration.' }
    if ((Get-SingleSqlValue -Database $catalogDatabase -Label 'PlatformSuperAdmin seed' -Query "SELECT COUNT(*) FROM dbo.PlatformRoles WHERE Name = 'PlatformSuperAdmin';") -ne '1') { throw 'PlatformSuperAdmin seed is missing.' }
    if ((Get-SingleSqlValue -Database $catalogDatabase -Label 'Platform permission seeds' -Query "SELECT COUNT(*) FROM dbo.PlatformPermissions WHERE Name IN ('PlatformTenant.View', 'PlatformTenant.Create', 'PlatformTenant.UpdateStatus');") -ne '3') { throw 'Platform permission seeds are incomplete.' }
    if ((Get-SingleSqlValue -Database $catalogDatabase -Label 'Platform role-permission seeds' -Query "SELECT COUNT(*) FROM dbo.PlatformRolePermissions rp JOIN dbo.PlatformRoles r ON r.Id = rp.PlatformRoleId JOIN dbo.PlatformPermissions p ON p.Id = rp.PlatformPermissionId WHERE r.Name = 'PlatformSuperAdmin' AND p.Name IN ('PlatformTenant.View', 'PlatformTenant.Create', 'PlatformTenant.UpdateStatus');") -ne '3') { throw 'Platform role-permission seeds are incomplete.' }
    if ((Get-SingleSqlValue -Database $catalogDatabase -Label 'PlatformSuperAdmin exact grant count' -Query "SELECT COUNT(*) FROM dbo.PlatformRolePermissions rp JOIN dbo.PlatformRoles r ON r.Id = rp.PlatformRoleId WHERE r.Name = 'PlatformSuperAdmin';") -ne '3') { throw 'PlatformSuperAdmin has an unexpected number of grants.' }
    Assert-TenantSnapshotUnchanged -Before $tenantSnapshot
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Post-migration tenant-table contamination' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($tenantTablesNotExpectedInCatalog -join "','")') ORDER BY name;") -Message 'Tenant-domain tables appeared in the catalog:'

    $phase = '[7/7] Rollout completion'
    Write-Host $phase
    Write-Host 'REAL HRMS_CATALOG BACKUP VERIFIED'
    Write-Host 'PLATFORM IDENTITY REAL CATALOG MIGRATION APPLIED'
    Write-Host 'REAL CATALOG PLATFORM IDENTITY SCHEMA VERIFIED'
    Write-Host 'REAL CATALOG PLATFORM AUTHORIZATION SEEDS VERIFIED'
    Write-Host 'DEMO01/DEMO02 PRESERVATION VERIFIED'
    Write-Host 'PLATFORM IDENTITY REAL CATALOG ROLLOUT PASS'
    Write-Host 'READY FOR PLATFORM SUPERADMIN BOOTSTRAP REVIEW'
    Write-Host "Backup file: $backupFile"
    Write-Host "Migration applied: $targetMigration"
    Write-Host 'PlatformUsers count: 0'
    Write-Host 'DEMO01 modified: NO'
    Write-Host 'DEMO02 modified: NO'
    Write-Host 'HRMS tenant database modified: NO'
    Write-Host 'PlatformSuperAdmin user created: NO'
    Write-Host 'Tenant created: NO'
}
catch {
    Write-Error "REAL CATALOG PLATFORM IDENTITY ROLLOUT FAILED at $phase`: $($_.Exception.Message)"
    if ($backupFile) { Write-Host "Backup file: $backupFile" }
    Write-Host "Backup verified: $(if ($backupVerified) { 'YES' } else { 'NO' })"
    if ($migrationAttempted) { Get-PostFailureEvidence }
    Write-Host 'Automatic rollback: NO'
    Write-Host 'Automatic restore: NO'
    exit 1
}
