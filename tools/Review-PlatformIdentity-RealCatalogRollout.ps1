[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sqlServer = 'lpc:.'
$catalogDatabase = 'HRMS_Catalog'
$historyTable = 'dbo.__EFMigrationsHistoryCatalog'
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

function Invoke-SqlRead {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Query,
        [Parameter(Mandatory)][string]$Label
    )

    Write-Host "[$Label]"
    $arguments = @(
        '-S', $sqlServer,
        '-E',
        '-C',
        '-b',
        '-d', $Database,
        '-h', '-1',
        '-W',
        '-s', '|',
        '-Q', "SET NOCOUNT ON;`r`n$Query"
    )
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

try {
    Write-Host 'REAL CATALOG PLATFORM IDENTITY ROLLOUT REVIEW'
    Write-Host "SQL Server: $sqlServer"
    Write-Host "Catalog database: $catalogDatabase"
    Write-Host ''

    if (-not (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue)) {
        throw 'sqlcmd.exe was not found.'
    }

    $serverIdentity = @(Invoke-SqlRead -Database 'master' -Label 'SQL Server local integrated connectivity' -Query 'SELECT @@SERVERNAME AS ServerName, SUSER_SNAME() AS LoginName;')
    if ($serverIdentity.Count -ne 1) { throw 'SQL Server connectivity identity was unexpected.' }

    $databaseIdentity = Get-SingleSqlValue -Database $catalogDatabase -Label 'Real catalog database identity' -Query 'SELECT DB_NAME();'
    if ($databaseIdentity -cne $catalogDatabase) {
        throw "Real catalog identity mismatch: expected $catalogDatabase, received $databaseIdentity."
    }
    Write-Host 'REAL HRMS_CATALOG IDENTITY VERIFIED'

    $history = @((Invoke-SqlRead -Database $catalogDatabase -Label 'Real catalog migration history' -Query "SELECT MigrationId FROM $historyTable ORDER BY MigrationId;") | ForEach-Object { $_.Trim() })
    if ($history -contains '20260906130913_AddPlatformIdentity') {
        throw 'AddPlatformIdentity is already present in the real catalog history.'
    }
    if ($history.Count -ne $expectedHistory.Count -or @($expectedHistory | Where-Object { $history -notcontains $_ }).Count -ne 0) {
        throw "Real catalog migration history differs from the expected pre-rollout state: $($history -join ', ')."
    }
    Write-Host "REAL CATALOG MIGRATION HISTORY VERIFIED: $($history -join ', ')"

    $coreColumns = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Real catalog core schema' -Query @"
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
   OR NOT EXISTS (
       SELECT 1 FROM sys.columns c
       WHERE c.object_id = OBJECT_ID(N'dbo.' + e.TableName, N'U')
         AND c.name = e.ColumnName);
"@)
    # The query above is read-only and uses a statement-local VALUES expression.
    Assert-NoRows -Rows $coreColumns -Message 'Missing required catalog columns:'
    foreach ($table in @('Tenants', 'TenantBranding')) {
        $tableExists = Get-SingleSqlValue -Database $catalogDatabase -Label "Catalog table $table" -Query "SELECT CASE WHEN OBJECT_ID(N'dbo.$table', N'U') IS NULL THEN 0 ELSE 1 END;"
        if ($tableExists -ne '1') { throw "Required catalog table is missing: $table" }
    }
    Write-Host 'REAL CATALOG TENANT SCHEMA VERIFIED'

    $platformPresence = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Platform table absence' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($platformTables -join "','")') ORDER BY name;")
    Assert-NoRows -Rows $platformPresence -Message 'Platform Identity tables already exist:'
    Write-Host 'PLATFORM IDENTITY TABLE ABSENCE VERIFIED'

    $tenantRows = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Real tenant rows' -Query 'SELECT CONVERT(nvarchar(36), Id), TenantCode, Host, ShardKey, CONVERT(nvarchar(20), Status), DatabaseProvider FROM dbo.Tenants ORDER BY TenantCode;')
    $tenantRecords = @()
    foreach ($row in $tenantRows) {
        $parts = $row -split '\|', 6
        if ($parts.Count -ne 6) { throw "Unexpected tenant row shape: $row" }
        $record = [pscustomobject]@{
            Id = $parts[0].Trim()
            TenantCode = $parts[1].Trim()
            Host = $parts[2].Trim()
            ShardKey = $parts[3].Trim()
            Status = $parts[4].Trim()
            DatabaseProvider = $parts[5].Trim()
        }
        $tenantRecords += $record
        Write-Host ("Tenant: {0} | {1} | {2} | {3} | {4} | {5}" -f $record.Id, $record.TenantCode, $record.Host, $record.ShardKey, $record.Status, $record.DatabaseProvider)
    }
    Write-Host "Tenant count: $($tenantRecords.Count)"

    $expectedTenants = @(
        [pscustomobject]@{ TenantCode = 'DEMO01'; Id = '11111111-1111-1111-1111-111111111111'; Host = 'demo01.localhost'; ShardKey = 'demo01'; DatabaseProvider = 'SqlServer' }
        [pscustomobject]@{ TenantCode = 'DEMO02'; Id = '22222222-2222-2222-2222-222222222222'; Host = 'demo02.localhost'; ShardKey = 'demo02'; DatabaseProvider = 'SqlServer' }
    )
    foreach ($expected in $expectedTenants) {
        $actual = @($tenantRecords | Where-Object { $_.TenantCode -ceq $expected.TenantCode })
        if ($actual.Count -ne 1) { throw "$($expected.TenantCode) preservation check expected one row, found $($actual.Count)." }
        foreach ($property in @('Id', 'Host', 'ShardKey', 'DatabaseProvider')) {
            if ($actual[0].$property -cne $expected.$property) {
                throw "$($expected.TenantCode) preservation mismatch for $property."
            }
        }
    }
    Write-Host 'DEMO01 preservation: PASS'
    Write-Host 'DEMO02 preservation: PASS'

    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Duplicate tenant codes' -Query 'SELECT TenantCode FROM dbo.Tenants GROUP BY TenantCode HAVING COUNT(*) > 1;') -Message 'Duplicate TenantCode values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Duplicate tenant hosts' -Query 'SELECT Host FROM dbo.Tenants GROUP BY Host HAVING COUNT(*) > 1;') -Message 'Duplicate Host values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Duplicate tenant shard keys' -Query 'SELECT ShardKey FROM dbo.Tenants GROUP BY ShardKey HAVING COUNT(*) > 1;') -Message 'Duplicate ShardKey values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Invalid tenant provider values' -Query "SELECT TenantCode + '|' + ISNULL(DatabaseProvider, '<NULL>') FROM dbo.Tenants WHERE DatabaseProvider IS NULL OR LTRIM(RTRIM(DatabaseProvider)) = '' OR DatabaseProvider NOT IN ('SqlServer', 'MySql');") -Message 'Invalid DatabaseProvider values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Blank tenant hosts' -Query "SELECT TenantCode FROM dbo.Tenants WHERE Host IS NULL OR LTRIM(RTRIM(Host)) = '';" ) -Message 'Blank Host values found:'
    Assert-NoRows -Rows @(Invoke-SqlRead -Database $catalogDatabase -Label 'Blank tenant shard keys' -Query "SELECT TenantCode FROM dbo.Tenants WHERE ShardKey IS NULL OR LTRIM(RTRIM(ShardKey)) = '';" ) -Message 'Blank ShardKey values found:'
    Write-Host 'REAL CATALOG TENANT INTEGRITY VERIFIED'

    $contamination = @(Invoke-SqlRead -Database $catalogDatabase -Label 'Tenant-table contamination' -Query "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name IN ('$($tenantTablesNotExpectedInCatalog -join "','")') ORDER BY name;")
    Assert-NoRows -Rows $contamination -Message 'Tenant-domain tables unexpectedly exist in HRMS_Catalog:'
    Write-Host 'CATALOG/TENANT SCHEMA ISOLATION VERIFIED'

    Write-Host ''
    Write-Host 'PLATFORM IDENTITY PRE-ROLLOUT REAL CATALOG AUDIT PASS'
    Write-Host 'READY FOR EXPLICIT REAL CATALOG MIGRATION APPROVAL'
    Write-Host ''
    Write-Host 'Real catalog modified: NO'
    Write-Host 'HRMS modified: NO'
    Write-Host 'DEMO01 modified: NO'
    Write-Host 'DEMO02 modified: NO'
    Write-Host 'PlatformSuperAdmin created: NO'
    Write-Host 'Tenant created: NO'
    Write-Host 'Migration applied: NO'
}
catch {
    Write-Error "REAL CATALOG ROLLOUT REVIEW BLOCKED: $($_.Exception.Message)"
    exit 1
}
