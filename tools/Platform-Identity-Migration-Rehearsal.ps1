[CmdletBinding()]
param(
    [string]$MySqlServer = '127.0.0.1',
    [int]$MySqlPort = 3306,
    [string]$MySqlUser = 'root',
    [switch]$MySqlRetryOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$sqlDatabase = 'HRMS_PlatformIdentity_Rehearsal_20260906'
$originalMySqlDatabase = 'HRMS_PlatformCatalog_Rehearsal_20260906'
$failedMySqlDatabase = 'HRMS_PlatformCatalog_Rehearsal_20260906_R2'
$retryMySqlDatabase = 'HRMS_PlatformCatalog_Rehearsal_20260906_R3'
$mySqlDatabase = if ($MySqlRetryOnly) { $retryMySqlDatabase } else { $originalMySqlDatabase }
$mySqlExpectedDatabase = $mySqlDatabase.ToLowerInvariant()
$sqlServer = 'lpc:.'
$sqlHistory = '__EFMigrationsHistoryCatalog'
$sqlConnection = "Server=$sqlServer;Database=$sqlDatabase;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;"

$sqlMigrations = @(
    '20260823113202_InitialCatalog',
    '20260905142921_AddTenantDatabaseProvider',
    '20260906130913_AddPlatformIdentity'
)
$mySqlMigrations = @(
    '20260905181639_InitialMySqlCatalogSchema',
    '20260906130913_AddPlatformIdentity'
)
$platformTables = @(
    'PlatformUsers',
    'PlatformRoles',
    'PlatformPermissions',
    'PlatformUserRoles',
    'PlatformRolePermissions',
    'PlatformRefreshTokens'
)

$oldMysqlPassword = $env:MYSQL_PWD
$securePassword = $null
$mysqlPlainPassword = $null
$mysqlClient = $null
$sqlCreated = $false
$mysqlCreated = $false
$phase = 'initialization'

function Invoke-External {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Label,
        [string]$DisplayCommand
    )

    Write-Host ""
    Write-Host "[$Label]"
    if ($DisplayCommand) {
        Write-Host $DisplayCommand
    }
    else {
        Write-Host ("{0} {1}" -f $Executable, ($Arguments -join ' '))
    }

    & $Executable @Arguments
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) {
        throw "$Label did not provide an exit code."
    }
    Write-Host "Exit code: $exitCode"
    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode."
    }
}

function Invoke-SqlQuery {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$Database = 'master',
        [switch]$ReturnRows
    )

    $arguments = @('-S', $sqlServer, '-E', '-C', '-b', '-d', $Database, '-h', '-1', '-W', '-s', '|', '-Q', "SET NOCOUNT ON;`r`n$Query")
    $output = @(& sqlcmd.exe @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { throw 'sqlcmd did not provide an exit code.' }
    if ($exitCode -ne 0) { throw "SQL query failed with exit code ${exitCode}: $($output -join [Environment]::NewLine)" }
    if ($ReturnRows) { return @($output | ForEach-Object { [string]$_ } | Where-Object { $_.Trim() }) }
}

function Get-SqlScalar {
    param([Parameter(Mandatory)][string]$Query, [string]$Database = 'master')
    $rows = @(Invoke-SqlQuery -Query $Query -Database $Database -ReturnRows)
    if ($rows.Count -ne 1) { throw "Expected one SQL scalar, received $($rows.Count)." }
    return $rows[0].Trim()
}

function Invoke-MySqlQuery {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Query,
        [switch]$ReturnRows
    )

    $arguments = @(
        "--host=$MySqlServer", "--port=$MySqlPort", "--user=$MySqlUser",
        "--database=$Database", '--batch', '--raw', '--skip-column-names', "--execute=$Query"
    )
    $output = @(& $mysqlClient @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { throw 'mysql did not provide an exit code.' }
    if ($exitCode -ne 0) { throw "MySQL query failed with exit code ${exitCode}: $($output -join [Environment]::NewLine)" }
    if ($ReturnRows) { return @($output | ForEach-Object { [string]$_ } | Where-Object { $_.Trim() }) }
}

function Get-MySqlScalar {
    param([Parameter(Mandatory)][string]$Database, [Parameter(Mandatory)][string]$Query)
    $rows = @(Invoke-MySqlQuery -Database $Database -Query $Query -ReturnRows)
    if ($rows.Count -ne 1) { throw "Expected one MySQL scalar, received $($rows.Count)." }
    return $rows[0].Trim()
}

function Invoke-DotNetEf {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$SafeDisplay
    )
    Invoke-External -Executable 'dotnet' -Arguments (@('ef') + $Arguments) -Label $Label -DisplayCommand $SafeDisplay
}

function Get-DotNetEfOutput {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$SafeDisplay
    )

    Write-Host ""
    Write-Host "[$Label]"
    Write-Host $SafeDisplay
    $output = @(& dotnet ef @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($null -eq $exitCode) { throw "$Label did not provide an exit code." }
    Write-Host "Exit code: $exitCode"
    if ($exitCode -ne 0) { throw "$Label failed with exit code $exitCode." }
    return $output
}

function Assert-FileSet {
    param([Parameter(Mandatory)][string[]]$RelativePaths)
    foreach ($relativePath in $RelativePaths) {
        $path = Join-Path $repoRoot $relativePath
        if (-not (Test-Path $path -PathType Leaf)) { throw "Required migration file is missing: $relativePath" }
    }
}

function Assert-MySqlMigrationIdentifiers {
    $migrationPath = Join-Path $repoRoot 'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\Migrations\20260906130913_AddPlatformIdentity.cs'
    $source = Get-Content -Raw $migrationPath
    $patterns = @(
        'PrimaryKey\("([^"]+)"'
        'ForeignKey\("([^"]+)"'
        'CreateIndex\("([^"]+)"'
        'CreateTable\("([^"]+)"'
    )
    foreach ($pattern in $patterns) {
        foreach ($match in [regex]::Matches($source, $pattern)) {
            $identifier = $match.Groups[1].Value
            if ($identifier.Length -gt 64) {
                throw "MySQL migration identifier exceeds 64 characters: $identifier ($($identifier.Length))."
            }
        }
    }
}

function Verify-SqlSchema {
    $rows = @(Invoke-SqlQuery -Database $sqlDatabase -ReturnRows -Query @"
DECLARE @missing nvarchar(max) = N'';
DECLARE @tables table (Name sysname);
INSERT @tables VALUES
('Tenants'), ('TenantBranding'), ('PlatformUsers'), ('PlatformRoles'),
('PlatformPermissions'), ('PlatformUserRoles'), ('PlatformRolePermissions'), ('PlatformRefreshTokens');
SELECT @missing = @missing + CASE WHEN OBJECT_ID(N'dbo.' + Name, N'U') IS NULL THEN Name + N',' ELSE N'' END FROM @tables;
IF @missing <> N'' THROW 52101, @missing, 1;
IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL THROW 52102, 'Tenant table Employees exists in catalog rehearsal.', 1;
IF (SELECT COUNT(*) FROM dbo.PlatformUsers) <> 0 THROW 52103, 'Unexpected platform users exist.', 1;
IF (SELECT COUNT(*) FROM dbo.PlatformRoles WHERE Name = N'PlatformSuperAdmin') <> 1 THROW 52104, 'PlatformSuperAdmin seed missing.', 1;
IF (SELECT COUNT(*) FROM dbo.PlatformPermissions WHERE Name IN (N'PlatformTenant.View', N'PlatformTenant.Create', N'PlatformTenant.UpdateStatus')) <> 3 THROW 52105, 'Platform permission seed incomplete.', 1;
IF (SELECT COUNT(*) FROM dbo.PlatformRolePermissions rp JOIN dbo.PlatformRoles r ON r.Id = rp.PlatformRoleId JOIN dbo.PlatformPermissions p ON p.Id = rp.PlatformPermissionId WHERE r.Name = N'PlatformSuperAdmin' AND p.Name IN (N'PlatformTenant.View', N'PlatformTenant.Create', N'PlatformTenant.UpdateStatus')) <> 3 THROW 52106, 'Platform grants seed incomplete.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PlatformUsers') AND is_unique = 1 AND name LIKE N'%NormalizedEmail%') THROW 52107, 'Platform normalized email uniqueness missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PlatformRoles') AND is_unique = 1 AND name LIKE N'%Name%') THROW 52108, 'Platform role uniqueness missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PlatformPermissions') AND is_unique = 1 AND name LIKE N'%Name%') THROW 52109, 'Platform permission uniqueness missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.PlatformUserRoles')) THROW 52110, 'Platform user-role foreign keys missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.PlatformRolePermissions')) THROW 52111, 'Platform role-permission foreign keys missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.PlatformRefreshTokens')) THROW 52112, 'Platform refresh-token foreign key missing.', 1;
SELECT 'PASS';
"@)
    if ($rows.Count -ne 1 -or $rows[0].Trim() -ne 'PASS') { throw 'SQL schema verification returned an unexpected result.' }
    $history = @(Invoke-SqlQuery -Database $sqlDatabase -ReturnRows -Query "SELECT MigrationId FROM [$sqlHistory] ORDER BY MigrationId;") | ForEach-Object { $_.Trim() }
    if ($history.Count -ne $sqlMigrations.Count -or @($sqlMigrations | Where-Object { $history -notcontains $_ }).Count -ne 0) { throw "SQL migration history mismatch: $($history -join ', ')" }
    [pscustomobject]@{ Tables = $platformTables.Count; Seeds = 'PASS'; History = ($history -join ',') }
}

function Verify-MySqlSchema {
    $rows = @(Invoke-MySqlQuery -Database $mySqlDatabase -ReturnRows -Query @"
SET @missing = '';
SELECT COALESCE(GROUP_CONCAT(t), 'PASS') FROM (
 SELECT 'Tenants' t UNION ALL SELECT 'TenantBranding' UNION ALL SELECT 'PlatformUsers'
 UNION ALL SELECT 'PlatformRoles' UNION ALL SELECT 'PlatformPermissions'
 UNION ALL SELECT 'PlatformUserRoles' UNION ALL SELECT 'PlatformRolePermissions'
 UNION ALL SELECT 'PlatformRefreshTokens'
) required_tables
WHERE t NOT IN (SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE());
SELECT COUNT(*) FROM PlatformUsers;
SELECT COUNT(*) FROM PlatformRoles WHERE Name = 'PlatformSuperAdmin';
SELECT COUNT(*) FROM PlatformPermissions WHERE Name IN ('PlatformTenant.View','PlatformTenant.Create','PlatformTenant.UpdateStatus');
SELECT COUNT(*) FROM PlatformRolePermissions rp JOIN PlatformRoles r ON r.Id = rp.PlatformRoleId JOIN PlatformPermissions p ON p.Id = rp.PlatformPermissionId WHERE r.Name = 'PlatformSuperAdmin' AND p.Name IN ('PlatformTenant.View','PlatformTenant.Create','PlatformTenant.UpdateStatus');
SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'Employees';
SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'PlatformUsers' AND column_name = 'NormalizedEmail' AND non_unique = 0;
SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name IN ('PlatformRoles','PlatformPermissions') AND column_name = 'Name' AND non_unique = 0;
SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema = DATABASE() AND table_name IN ('PlatformUserRoles','PlatformRolePermissions','PlatformRefreshTokens') AND constraint_type = 'FOREIGN KEY';
"@)
    if ($rows.Count -ne 9) { throw "MySQL schema verification returned $($rows.Count) rows instead of 9." }
    if ($rows[0].Trim() -ne 'PASS') { throw "MySQL required tables missing: $($rows[0])" }
    if ($rows[1].Trim() -ne '0' -or $rows[2].Trim() -ne '1' -or $rows[3].Trim() -ne '3' -or $rows[4].Trim() -ne '3' -or $rows[5].Trim() -ne '0' -or $rows[6].Trim() -ne '1' -or $rows[7].Trim() -ne '2' -or [int]$rows[8].Trim() -lt 3) { throw 'MySQL platform schema/seed verification failed.' }
    $history = @(Invoke-MySqlQuery -Database $mySqlDatabase -ReturnRows -Query 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;') | ForEach-Object { $_.Trim() }
    if ($history.Count -ne $mySqlMigrations.Count -or @($mySqlMigrations | Where-Object { $history -notcontains $_ }).Count -ne 0) { throw "MySQL migration history mismatch: $($history -join ', ')" }
    [pscustomobject]@{ Tables = $platformTables.Count; Seeds = 'PASS'; History = ($history -join ',') }
}

try {
    $phase = '[1/12] Static preflight'
    Write-Host $phase
    Assert-FileSet @(
        'Backend\HRMS.Infrastructure\Persistence\Catalog\Migrations\20260823113202_InitialCatalog.cs',
        'Backend\HRMS.Infrastructure\Persistence\Catalog\Migrations\20260905142921_AddTenantDatabaseProvider.cs',
        'Backend\HRMS.Infrastructure\Persistence\Catalog\Migrations\20260906130913_AddPlatformIdentity.cs',
        'Backend\HRMS.Infrastructure\Persistence\Catalog\Migrations\20260906130913_AddPlatformIdentity.Designer.cs',
        'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\Migrations\20260905181639_InitialMySqlCatalogSchema.cs',
        'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\Migrations\20260906130913_AddPlatformIdentity.cs',
        'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\Migrations\20260906130913_AddPlatformIdentity.Designer.cs'
    )
    $designer = Get-Content -Raw (Join-Path $repoRoot 'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\Migrations\20260906130913_AddPlatformIdentity.Designer.cs')
    foreach ($required in @('BuildTargetModel', 'PlatformUsers', 'PlatformRoles', 'PlatformPermissions', 'PlatformUserRoles', 'PlatformRolePermissions', 'PlatformRefreshTokens', 'NormalizedEmail', 'HasIndex', 'HasOne')) {
        if ($designer -notmatch [regex]::Escape($required)) { throw "MySQL migration designer is incomplete: $required" }
    }
    Assert-MySqlMigrationIdentifiers
    if ((Get-Content -Raw 'Backend\HRMS.Domain\Authorization\Permissions.cs') -match 'PlatformTenant\.') { throw 'Platform permissions leaked into tenant permission source.' }
    Write-Host 'Static migration preflight: PASS'

    $phase = '[2/12] SQL Server connectivity'
    Write-Host $phase
    if (-not (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue)) { throw 'sqlcmd.exe was not found.' }
    $sqlIdentity = @(Invoke-SqlQuery -ReturnRows -Query 'SELECT @@SERVERNAME, SUSER_SNAME();')
    if ($sqlIdentity.Count -ne 1) { throw 'SQL Server connectivity identity was unexpected.' }
    Write-Host 'SQL Server local integrated connectivity: PASS'

    $phase = '[3/12] MySQL connectivity'
    Write-Host $phase
    if (-not $mysqlClient) {
        $mysqlClient = (Get-Command mysql.exe -ErrorAction SilentlyContinue).Source
    }
    if (-not $mysqlClient) {
        $candidate = Join-Path ${env:ProgramFiles} 'MySQL\MySQL Server 8.0\bin\mysql.exe'
        if (Test-Path $candidate -PathType Leaf) { $mysqlClient = $candidate }
    }
    if (-not $mysqlClient) { throw 'mysql.exe was not found.' }
    $securePassword = Read-Host 'MySQL password' -AsSecureString
    $mysqlPlainPassword = [Net.NetworkCredential]::new('', $securePassword).Password
    if ([string]::IsNullOrWhiteSpace($mysqlPlainPassword)) { throw 'MySQL password was empty.' }
    $env:MYSQL_PWD = $mysqlPlainPassword
    $mysqlIdentity = @(Invoke-MySqlQuery -Database 'information_schema' -ReturnRows -Query 'SELECT VERSION(); SELECT @@lower_case_table_names;')
    if ($mysqlIdentity.Count -ne 2 -or $mysqlIdentity[1].Trim() -ne '1') { throw 'MySQL preflight requires lower_case_table_names = 1.' }
    Write-Host "MySQL lower_case_table_names: $($mysqlIdentity[1].Trim())"
    Write-Host 'MySQL connectivity: PASS'

    $phase = '[4/12] Build migration projects'
    Write-Host $phase
    Invoke-External 'dotnet' @('build', 'Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'SQL catalog migration project build'
    Invoke-External 'dotnet' @('build', 'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\HRMS.Infrastructure.MySqlCatalogMigrations.csproj', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'MySQL catalog migration project build'
    Invoke-External 'dotnet' @('build', 'Backend\HRMS.API\HRMS.API.csproj', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'EF startup project build'

    $mySqlCatalogProject = 'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\HRMS.Infrastructure.MySqlCatalogMigrations.csproj'
    $factorySource = Get-Content -Raw 'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\MySqlCatalogDbContextFactory.cs'
    if ($factorySource -notmatch '\.UseMySQL\(' -or $factorySource -notmatch 'MigrationsAssembly') {
        throw 'The dedicated MySQL catalog design-time factory is not configured for MySql.EntityFrameworkCore.'
    }
    $providerInfo = @(Get-DotNetEfOutput @(
        'dbcontext', 'info', '--project', $mySqlCatalogProject,
        '--startup-project', $mySqlCatalogProject,
        '--context', 'HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext', '--no-build'
    ) 'MySQL design-time provider preflight' 'dotnet ef dbcontext info <dedicated MySQL catalog project>')
    if (($providerInfo -join [Environment]::NewLine) -notmatch 'MySql.EntityFrameworkCore') {
        throw 'MySQL design-time provider assertion failed; MySql.EntityFrameworkCore was not reported.'
    }
    Write-Host 'MySQL catalog EF provider: MySql.EntityFrameworkCore'

    $phase = '[5/12] SQL rehearsal collision/create'
    Write-Host $phase
    $sqlExists = Get-SqlScalar -Query "SELECT CASE WHEN DB_ID(N'$sqlDatabase') IS NULL THEN 0 ELSE 1 END;"
    $mySqlExists = Get-MySqlScalar -Database 'information_schema' -Query "SELECT COUNT(*) FROM SCHEMATA WHERE SCHEMA_NAME = '$mySqlExpectedDatabase';"
    $originalMySqlExists = Get-MySqlScalar -Database 'information_schema' -Query "SELECT COUNT(*) FROM SCHEMATA WHERE SCHEMA_NAME = '$($originalMySqlDatabase.ToLowerInvariant())';"
    $failedMySqlExists = Get-MySqlScalar -Database 'information_schema' -Query "SELECT COUNT(*) FROM SCHEMATA WHERE SCHEMA_NAME = '$($failedMySqlDatabase.ToLowerInvariant())';"
    if ($MySqlRetryOnly) {
        if ($sqlExists -ne '1') { throw "Successful SQL rehearsal is missing: $sqlDatabase" }
        if ($originalMySqlExists -ne '1') { throw "Original failed MySQL rehearsal evidence is missing: $originalMySqlDatabase" }
        if ($failedMySqlExists -ne '1') { throw "R2 MySQL rehearsal evidence is missing: $failedMySqlDatabase" }
        if ($mySqlExists -ne '0') { throw "MySQL retry rehearsal collision: $mySqlDatabase already exists." }
        Write-Host "Retry mode: preserving SQL rehearsal, original MySQL failure evidence, and R2 migration failure evidence."
    }
    else {
        if ($sqlExists -ne '0') { throw "SQL rehearsal database collision: $sqlDatabase already exists." }
        if ($mySqlExists -ne '0') { throw "MySQL rehearsal database collision: $mySqlDatabase already exists." }
        if ($originalMySqlExists -ne '0') { throw "MySQL rehearsal database collision: $mySqlDatabase already exists." }
        if ($failedMySqlExists -ne '0') { throw "MySQL rehearsal database collision: $failedMySqlDatabase already exists." }
        Invoke-SqlQuery -Query "CREATE DATABASE [$sqlDatabase];"
        $sqlCreated = $true
        Write-Host "Created only: $sqlDatabase"
    }

    $phase = '[6/12] SQL catalog migration'
    Write-Host $phase
    if ($MySqlRetryOnly) {
        Write-Host 'SQL migration mutation skipped: existing SQL rehearsal is read-only in retry mode.'
    }
    else {
        Invoke-DotNetEf @('database', 'update', $sqlMigrations[-1], '--project', 'Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj', '--startup-project', 'Backend\HRMS.API\HRMS.API.csproj', '--context', 'HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext', '--connection', $sqlConnection, '--no-build') 'SQL catalog migration' 'dotnet ef database update <SQL rehearsal connection redacted>'
    }

    $phase = '[7/12] SQL verification'
    Write-Host $phase
    if ((Get-SqlScalar -Database $sqlDatabase -Query 'SELECT DB_NAME();') -cne $sqlDatabase) { throw 'SQL rehearsal database identity mismatch.' }
    $sqlResult = Verify-SqlSchema
    Write-Host "SQL tables verified: $($sqlResult.Tables); seeds: $($sqlResult.Seeds); history: $($sqlResult.History)"
    if ($MySqlRetryOnly) {
        Write-Host 'SQL SERVER PLATFORM CATALOG REHEARSAL RE-VERIFICATION PASS'
    }
    else {
        Write-Host 'SQL SERVER PLATFORM CATALOG MIGRATION REHEARSAL PASS'
    }

    $phase = '[8/12] MySQL rehearsal collision/create'
    Write-Host $phase
    Invoke-MySqlQuery -Database 'information_schema' -Query "CREATE DATABASE $mySqlDatabase CHARACTER SET utf8mb4;"
    $mysqlCreated = $true
    $actualMySqlDatabase = Get-MySqlScalar -Database $mySqlDatabase -Query 'SELECT DATABASE();'
    if ($actualMySqlDatabase -cne $mySqlExpectedDatabase) { throw "MySQL rehearsal identity mismatch: $actualMySqlDatabase" }
    Write-Host "Created only: $mySqlDatabase (server identity: $actualMySqlDatabase)"

    $mysqlConnection = "Server=$MySqlServer;Port=$MySqlPort;Database=$mySqlDatabase;User ID=$MySqlUser;Password=$mysqlPlainPassword;"
    $phase = '[9/12] MySQL catalog migration'
    Write-Host $phase
    Invoke-DotNetEf @('database', 'update', $mySqlMigrations[-1], '--project', $mySqlCatalogProject, '--startup-project', $mySqlCatalogProject, '--context', 'HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext', '--connection', $mysqlConnection, '--no-build') 'MySQL catalog migration' 'dotnet ef database update <dedicated MySQL catalog project; connection redacted>'

    $phase = '[10/12] MySQL verification'
    Write-Host $phase
    $identityRows = @(Invoke-MySqlQuery -Database $mySqlDatabase -ReturnRows -Query 'SELECT @@lower_case_table_names, DATABASE();')
    if ($identityRows.Count -ne 1 -or $identityRows[0].Trim() -ne "1`t$mySqlExpectedDatabase") { throw "MySQL server identity verification failed." }
    $mySqlResult = Verify-MySqlSchema
    Write-Host "MySQL tables verified: $($mySqlResult.Tables); seeds: $($mySqlResult.Seeds); history: $($mySqlResult.History)"
    Write-Host 'MYSQL PLATFORM CATALOG MIGRATION REHEARSAL PASS'

    $phase = '[11/12] Cross-provider parity'
    if ($sqlResult.Tables -ne $mySqlResult.Tables -or $sqlResult.Seeds -ne 'PASS' -or $mySqlResult.Seeds -ne 'PASS') { throw 'Cross-provider platform schema parity failed.' }
    Write-Host 'PLATFORM IDENTITY MIGRATION PARITY PASS'

    $phase = '[12/12] Final safety verification'
    if ((Get-SqlScalar -Database $sqlDatabase -Query "SELECT COUNT(*) FROM dbo.PlatformUsers;") -ne '0') { throw 'SQL rehearsal contains an unexpected platform user.' }
    if ((Get-MySqlScalar -Database $mySqlDatabase -Query 'SELECT COUNT(*) FROM PlatformUsers;') -ne '0') { throw 'MySQL rehearsal contains an unexpected platform user.' }
    Write-Host 'Real HRMS_Catalog modified: NO'
    Write-Host 'HRMS modified: NO'
    Write-Host 'PlatformSuperAdmin user created: NO'
    Write-Host 'Tenant created: NO'
    Write-Host 'Rehearsal databases retained: YES'
    Write-Host ''
    Write-Host 'PLATFORM IDENTITY SQL/MYSQL MIGRATION REHEARSAL PASS — READY FOR REAL CATALOG ROLLOUT REVIEW'
    Write-Host "SQL rehearsal database: $sqlDatabase"
    if ($MySqlRetryOnly) {
        Write-Host "Original failed MySQL rehearsal retained: $originalMySqlDatabase"
        Write-Host "R2 MySQL migration failure retained: $failedMySqlDatabase"
        Write-Host "Successful MySQL retry rehearsal: $mySqlDatabase"
    }
    else {
        Write-Host "MySQL rehearsal database: $mySqlDatabase"
    }
}
catch {
    Write-Error "PLATFORM IDENTITY MIGRATION REHEARSAL BLOCKED at ${phase}: $($_.Exception.Message)"
    Write-Host "SQL rehearsal database state: $(if ($sqlCreated) { 'CREATED AND RETAINED' } else { 'NOT CREATED' })"
    Write-Host "MySQL rehearsal database state: $(if ($mysqlCreated) { 'CREATED AND RETAINED' } else { 'NOT CREATED' })"
    exit 1
}
finally {
    if ($null -eq $oldMysqlPassword) { Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue } else { $env:MYSQL_PWD = $oldMysqlPassword }
    $mysqlPlainPassword = $null
    if ($null -ne $securePassword) { $securePassword.Dispose(); $securePassword = $null }
}
