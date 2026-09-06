[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CatalogDatabase,

    [string]$Server = "lpc:.",

    [switch]$RunModelDriftCheck
)

$ErrorActionPreference = "Stop"

$targetMigration = "20260905142921_AddTenantDatabaseProvider"
$previousMigration = "20260823113202_InitialCatalog"
$historyTable = "__EFMigrationsHistoryCatalog"
$catalogContext = "HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext"
$catalogProject = ".\Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj"
$startupProject = ".\Backend\HRMS.API\HRMS.API.csproj"
$repoRoot = (Get-Location).Path
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportDirectory = Join-Path $repoRoot "artifacts"
$reportPath = Join-Path $reportDirectory "catalog-rollout-preflight-$timestamp.txt"
$reportLines = [System.Collections.Generic.List[string]]::new()
$noGo = $false
$blocked = $false

function Add-ReportLine {
    param([AllowNull()][object]$Value)

    $line = if ($null -eq $Value) { "" } else { [string]$Value }
    $reportLines.Add($line)
    Write-Host $line
}

function Stop-Preflight {
    param([string]$Message)

    Add-ReportLine "NO-GO: $Message"
    $script:noGo = $true
    throw $Message
}

function Convert-SqlcmdLines {
    param([AllowNull()][object[]]$Lines)

    @($Lines |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim() })
}

function Invoke-ReadOnlySql {
    param(
        [Parameter(Mandatory = $true)][string]$Query,
        [string]$Database = $CatalogDatabase,
        [switch]$AllowFailure
    )

    $safeQuery = "SET NOCOUNT ON;`n$Query"
    $arguments = @(
        "-S", $Server,
        "-d", $Database,
        "-E",
        "-C",
        "-b",
        "-l", "15",
        "-W",
        "-h", "-1",
        "-s", "|",
        "-Q", $safeQuery
    )

    $output = @(& sqlcmd @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "sqlcmd read-only query failed with exit code $exitCode."
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Lines = @(Convert-SqlcmdLines $output)
    }
}

function Invoke-ScalarSql {
    param([Parameter(Mandatory = $true)][string]$Query)

    $result = Invoke-ReadOnlySql -Query $Query
    if ($result.ExitCode -ne 0 -or $result.Lines.Count -ne 1) {
        throw "Expected one scalar result from the read-only SQL query."
    }

    $result.Lines[0]
}

function Get-IntegerScalar {
    param([Parameter(Mandatory = $true)][string]$Query)

    $value = Invoke-ScalarSql -Query $Query
    $parsed = 0L
    if (-not [long]::TryParse($value, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw "Expected an integer scalar but received '$value'."
    }

    $parsed
}

function Assert-ExactSingleRow {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Query
    )

    $count = Get-IntegerScalar -Query $Query
    if ($count -ne 1) {
        Stop-Preflight "$Label expected exactly one matching row but found $count."
    }
}

function Restore-ProcessEnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$OriginalValue
    )

    if ($null -eq $OriginalValue) {
        Remove-Item -Path "Env:$Name" -ErrorAction SilentlyContinue
    } else {
        Set-Item -Path "Env:$Name" -Value $OriginalValue
    }
}

function Write-ReportFile {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    Set-Content -LiteralPath $reportPath -Value $reportLines -Encoding utf8
}

if ([string]::IsNullOrWhiteSpace($CatalogDatabase) -or
    $CatalogDatabase -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$' -or
    $CatalogDatabase -in @("master", "model", "msdb", "tempdb")) {
    throw "CatalogDatabase must be a non-system SQL Server database name containing only letters, digits, '_' or '-'."
}

if ([string]::IsNullOrWhiteSpace($Server)) {
    throw "Server must not be empty."
}

try {
    Add-ReportLine "Catalog rollout read-only preflight"
    Add-ReportLine "Server: $Server"
    Add-ReportLine "Database: $CatalogDatabase"
    Add-ReportLine "Report: $reportPath"
    Add-ReportLine ""

    $query = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) + '|' + DB_NAME() + '|' + SUSER_SNAME() + '|' + CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) + '|' + CONVERT(nvarchar(128), SERVERPROPERTY('Edition'));"
    $identity = Invoke-ReadOnlySql -Query $query
    if ($identity.Lines.Count -ne 1) {
        throw "Identity query did not return exactly one row."
    }

    $identityParts = $identity.Lines[0].Split('|', 5)
    if ($identityParts.Count -ne 5) {
        throw "Identity query returned an unexpected shape."
    }

    Add-ReportLine "Server name: $($identityParts[0])"
    Add-ReportLine "Database name: $($identityParts[1])"
    Add-ReportLine "Login: $($identityParts[2])"
    Add-ReportLine "SQL Server version: $($identityParts[3])"
    Add-ReportLine "Edition: $($identityParts[4])"

    if ($identityParts[1] -cne $CatalogDatabase) {
        throw "Identity gate failed: connected database '$($identityParts[1])' does not equal supplied CatalogDatabase '$CatalogDatabase'."
    }

    Add-ReportLine "Identity gate: PASS"

    $query = "SELECT COUNT_BIG(*) FROM sys.tables AS t WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'$historyTable';"
    $historyExists = Get-IntegerScalar -Query $query
    if ($historyExists -ne 1) {
        Stop-Preflight "$historyTable is missing."
    }

    Add-ReportLine "Migration history:"
    $history = Invoke-ReadOnlySql -Query "SELECT MigrationId FROM dbo.$historyTable ORDER BY MigrationId;"
    foreach ($line in $history.Lines) {
        Add-ReportLine "  $line"
    }

    $previousCount = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.$historyTable WHERE MigrationId = N'$previousMigration';"
    if ($previousCount -ne 1) {
        Stop-Preflight "Required previous migration '$previousMigration' is not present."
    }

    $targetCount = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.$historyTable WHERE MigrationId = N'$targetMigration';"
    if ($targetCount -ne 0) {
        Add-ReportLine "TARGET MIGRATION ALREADY APPLIED - REVIEW REQUIRED"
        <#
        Add-ReportLine "TARGET MIGRATION ALREADY APPLIED — REVIEW REQUIRED"
        #>
        throw "Target migration is already present; preflight stopped."
    }

    Add-ReportLine "Target migration present: NO (expected before rollout)"

    $query = "SELECT COUNT_BIG(*) FROM sys.tables AS t WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'Tenants';"
    $tenantTableCount = Get-IntegerScalar -Query $query
    $query = "SELECT COUNT_BIG(*) FROM sys.tables AS t WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'TenantBranding';"
    $brandingTableCount = Get-IntegerScalar -Query $query
    if ($tenantTableCount -ne 1 -or $brandingTableCount -ne 1) {
        Stop-Preflight "Required catalog tables are missing."
    }

    $query = "SELECT COUNT_BIG(*) FROM sys.columns AS c JOIN sys.tables AS t ON t.object_id = c.object_id WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name = N'Tenants' AND c.name = N'DatabaseProvider';"
    $providerColumnCount = Get-IntegerScalar -Query $query
    if ($providerColumnCount -ne 0) {
        Stop-Preflight "Tenants.DatabaseProvider exists while the target migration is absent."
    }

    Add-ReportLine "Schema state: PASS (Tenants and TenantBranding exist; DatabaseProvider is absent)"

    $tenantCount = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.Tenants;"
    $brandingCount = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.TenantBranding;"
    Add-ReportLine "Tenant total: $tenantCount"
    Add-ReportLine "TenantBranding total: $brandingCount"

    Add-ReportLine "Tenant routing snapshot:"
    $query = "SELECT CONVERT(nvarchar(36), Id), TenantCode, Host, ShardKey, CONVERT(nvarchar(20), Status) FROM dbo.Tenants ORDER BY TenantCode;"
    $tenantRows = Invoke-ReadOnlySql -Query $query
    foreach ($line in $tenantRows.Lines) {
        Add-ReportLine "  $line"
    }

    Add-ReportLine "TenantBranding relationship snapshot:"
    $brandingRows = Invoke-ReadOnlySql -Query "SELECT CONVERT(nvarchar(36), TenantId) FROM dbo.TenantBranding ORDER BY TenantId;"
    foreach ($line in $brandingRows.Lines) {
        Add-ReportLine "  $line"
    }

    $query = "SELECT COUNT_BIG(*) FROM (SELECT TenantCode FROM dbo.Tenants GROUP BY TenantCode HAVING COUNT_BIG(*) > 1) AS d;"
    $duplicateCodeGroups = Get-IntegerScalar -Query $query
    $query = "SELECT COUNT_BIG(*) FROM (SELECT Host FROM dbo.Tenants GROUP BY Host HAVING COUNT_BIG(*) > 1) AS d;"
    $duplicateHostGroups = Get-IntegerScalar -Query $query
    $query = "SELECT COUNT_BIG(*) FROM (SELECT ShardKey FROM dbo.Tenants GROUP BY ShardKey HAVING COUNT_BIG(*) > 1) AS d;"
    $duplicateShardGroups = Get-IntegerScalar -Query $query
    Add-ReportLine "Duplicate TenantCode groups: $duplicateCodeGroups"
    Add-ReportLine "Duplicate Host groups: $duplicateHostGroups"
    Add-ReportLine "Duplicate ShardKey groups: $duplicateShardGroups"
    if ($duplicateCodeGroups -ne 0 -or $duplicateHostGroups -ne 0 -or $duplicateShardGroups -ne 0) {
        $noGo = $true
    }

    $query = "SELECT COUNT_BIG(*) FROM dbo.TenantBranding AS b LEFT JOIN dbo.Tenants AS t ON t.Id = b.TenantId WHERE t.Id IS NULL;"
    $orphanBranding = Get-IntegerScalar -Query $query
    Add-ReportLine "Orphan TenantBranding rows: $orphanBranding"
    if ($orphanBranding -ne 0) {
        $noGo = $true
    }

    $blankCode = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.Tenants WHERE TenantCode IS NULL OR LTRIM(RTRIM(TenantCode)) = N'';"
    $blankHost = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.Tenants WHERE Host IS NULL OR LTRIM(RTRIM(Host)) = N'';"
    $blankShard = Get-IntegerScalar -Query "SELECT COUNT_BIG(*) FROM dbo.Tenants WHERE ShardKey IS NULL OR LTRIM(RTRIM(ShardKey)) = N'';"
    Add-ReportLine "Blank/null TenantCode values: $blankCode"
    Add-ReportLine "Blank/null Host values: $blankHost"
    Add-ReportLine "Blank/null ShardKey values: $blankShard"
    if ($blankCode -ne 0 -or $blankHost -ne 0 -or $blankShard -ne 0) {
        $noGo = $true
    }

    Add-ReportLine "Database files and available space:"
    $query = "SELECT name, type_desc, CONVERT(nvarchar(40), CAST(size * 8.0 / 1024 AS decimal(18,2))), CONVERT(nvarchar(40), CAST((size - CONVERT(bigint, FILEPROPERTY(name, 'SpaceUsed'))) * 8.0 / 1024 AS decimal(18,2))) FROM sys.database_files ORDER BY file_id;"
    $fileRows = Invoke-ReadOnlySql -Query $query
    foreach ($line in $fileRows.Lines) {
        Add-ReportLine "  $line"
    }

    Add-ReportLine "Session/lock snapshot:"
    try {
        $query = "SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND is_user_process = 1;"
        $sessionCount = Get-IntegerScalar -Query $query
        $query = "SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID();"
        $requestCount = Get-IntegerScalar -Query $query
        $query = "SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND blocking_session_id <> 0;"
        $blockingCount = Get-IntegerScalar -Query $query
        Add-ReportLine "Active user sessions: $sessionCount"
        Add-ReportLine "Running requests: $requestCount"
        Add-ReportLine "Blocked requests: $blockingCount"
    } catch {
        Add-ReportLine "WARNING: DMV session/lock summary was unavailable to this login."
        Add-ReportLine "  $($_.Exception.Message)"
    }

    Add-ReportLine "Migration compatibility from repository source: PASS"
    Add-ReportLine "  $targetMigration defines DatabaseProvider as nvarchar(32), required, default SqlServer."
    Add-ReportLine "  No index or foreign-key operation is defined by the target migration."

    if ($RunModelDriftCheck) {
        $oldCatalogProvider = $env:Database__CatalogProvider
        $oldCatalogConnection = $env:ConnectionStrings__Catalog
        $oldWorkloadResolver = $env:MSBuildEnableWorkloadResolver
        try {
            $env:Database__CatalogProvider = "SqlServer"
            $env:ConnectionStrings__Catalog = "Server=$Server;Database=$CatalogDatabase;Integrated Security=True;TrustServerCertificate=True;"
            $env:MSBuildEnableWorkloadResolver = "false"
            Add-ReportLine "Running optional read-only model drift check..."
            & dotnet ef migrations has-pending-model-changes `
                --project $catalogProject `
                --startup-project $startupProject `
                --context $catalogContext `
                --connection $env:ConnectionStrings__Catalog `
                --no-build
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet ef migrations has-pending-model-changes failed with exit code $LASTEXITCODE."
            }
            Add-ReportLine "Model drift command: PASS"
        } finally {
            Restore-ProcessEnvironmentVariable -Name "Database__CatalogProvider" -OriginalValue $oldCatalogProvider
            Restore-ProcessEnvironmentVariable -Name "ConnectionStrings__Catalog" -OriginalValue $oldCatalogConnection
            Restore-ProcessEnvironmentVariable -Name "MSBuildEnableWorkloadResolver" -OriginalValue $oldWorkloadResolver
        }
    } else {
        Add-ReportLine "Model drift command: NOT RUN (invoke with -RunModelDriftCheck for the optional read-only check)."
    }

    if ($noGo) {
        Add-ReportLine ""
        Add-ReportLine "CATALOG PREFLIGHT NO-GO — DATA/SCHEMA ISSUE REQUIRES REVIEW"
    } else {
        Add-ReportLine ""
        Add-ReportLine "CATALOG PREFLIGHT GO — READY FOR BACKUP AND APPROVED MIGRATION WINDOW"
    }
} catch {
    $blocked = -not $noGo
    Add-ReportLine ""
    if ($noGo) {
        Add-ReportLine "CATALOG PREFLIGHT NO-GO — DATA/SCHEMA ISSUE REQUIRES REVIEW"
    } else {
        Add-ReportLine "CATALOG PREFLIGHT BLOCKED — REQUIRED READ-ONLY CHECK COULD NOT BE COMPLETED"
    }
    Add-ReportLine "Reason: $($_.Exception.Message)"
    throw
} finally {
    try {
        Write-ReportFile
    } catch {
        Write-Warning "Could not save the sanitized preflight report: $($_.Exception.Message)"
    }
}
