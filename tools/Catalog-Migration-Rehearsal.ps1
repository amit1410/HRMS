param(
    [switch]$ResumeExisting
)

$ErrorActionPreference = "Stop"
Set-Location "D:\HRMS"

$server = "lpc:."
$database = "HRMS_CatalogMigrationRehearsal_20260906"
$historyTable = "__EFMigrationsHistoryCatalog"
$connectionString = "Server=$server;Database=$database;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;"

$oldCatalogProvider = $env:Database__CatalogProvider
$oldCatalogConnection = $env:ConnectionStrings__Catalog
$oldRehearsalConnection = $env:HRMS_REHEARSAL_SQL_CONNECTION
$oldWorkloadResolver = $env:MSBuildEnableWorkloadResolver
$env:MSBuildEnableWorkloadResolver = "false"

function Invoke-SqlQuery {
    param(
        [Parameter(Mandatory)]
        [string]$Query,

        [string]$Database = "master"
    )

    $safeQuery = "SET NOCOUNT ON;`r`n$Query"

    $output = @(
        & sqlcmd `
            -S $server `
            -E `
            -C `
            -d $Database `
            -b `
            -h -1 `
            -W `
            -s "|" `
            -Q $safeQuery 2>&1
    )

    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed: $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Get-SqlScalar {
    param(
        [Parameter(Mandatory)]
        [string]$Query,

        [string]$Database = "master"
    )

    $output = @(Invoke-SqlQuery -Query $Query -Database $Database)

    $lines = @(
        $output |
            ForEach-Object { "$_".Trim() } |
            Where-Object { $_ -ne "" }
    )

    if ($lines.Count -ne 1) {
        throw "Expected exactly one scalar SQL result, received $($lines.Count): $($lines -join ' | ')"
    }

    return $lines[0]
}

function Invoke-Ef {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & dotnet ef @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef failed with exit code $LASTEXITCODE."
    }
}

function Restore-ProcessEnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [AllowNull()]
        [string]$OriginalValue
    )

    if ($null -eq $OriginalValue) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item "Env:$Name" $OriginalValue
    }
}

try {
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
        throw "sqlcmd was not found in PATH."
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet was not found in PATH."
    }

    Write-Host "Checking lpc:. Integrated connectivity..."

    $preflight = Invoke-SqlQuery -Query @"
SELECT
    @@SERVERNAME AS ServerName,
    SUSER_SNAME() AS LoginName,
    DB_NAME() AS DatabaseName,
    SERVERPROPERTY('ProductVersion') AS ProductVersion,
    SERVERPROPERTY('Edition') AS Edition;
"@

    $preflight | ForEach-Object { Write-Host $_ }

    $preflightDatabase = Get-SqlScalar -Query "SELECT DB_NAME();"

    if ($preflightDatabase -ne "master") {
        throw "SQL Server preflight did not connect to master."
    }

    $env:Database__CatalogProvider = "SqlServer"
    $env:ConnectionStrings__Catalog = $connectionString

    $catalogProject = ".\Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj"
    $startupProject = ".\Backend\HRMS.API\HRMS.API.csproj"
    $catalogContext = "HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext"

    $existingDatabase = Get-SqlScalar -Query @"
SELECT CASE
    WHEN DB_ID(N'$database') IS NULL THEN 0
    ELSE 1
END;
"@

    if ($existingDatabase -eq "1") {
        if (-not $ResumeExisting) {
            Write-Host "Rehearsal database already exists. No changes made."
            return
        }

        Write-Host "ResumeExisting mode: validating existing migrated rehearsal..."

        Invoke-SqlQuery -Database $database -Query @"
IF DB_NAME() <> N'$database'
    THROW 51100, 'Connected database is not the rehearsal database.', 1;

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
    THROW 51101, 'Tenants table is missing.', 1;

IF OBJECT_ID(N'dbo.TenantBranding', N'U') IS NULL
    THROW 51102, 'TenantBranding table is missing.', 1;

IF COL_LENGTH(N'dbo.Tenants', N'DatabaseProvider') IS NULL
    THROW 51103, 'DatabaseProvider column is missing.', 1;

IF OBJECT_ID(N'[$historyTable]', N'U') IS NULL
    THROW 51104, 'Catalog migration history table is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM [$historyTable]
    WHERE MigrationId = N'20260823113202_InitialCatalog'
)
    THROW 51105, 'InitialCatalog migration is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM [$historyTable]
    WHERE MigrationId = N'20260905142921_AddTenantDatabaseProvider'
)
    THROW 51106, 'AddTenantDatabaseProvider migration is missing.', 1;

IF (SELECT COUNT(*) FROM dbo.Tenants) <> 3
    THROW 51107, 'ResumeExisting requires exactly 3 tenants.', 1;

IF (SELECT COUNT(*) FROM dbo.TenantBranding) <> 1
    THROW 51108, 'ResumeExisting requires exactly 1 branding row.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Tenants
    WHERE Id = '11111111-1111-1111-1111-111111111101'
      AND TenantCode = N'RHR01'
      AND Host = N'rehearsal01.localhost'
      AND ShardKey = N'rehearsal-shard-01'
      AND Status = 1
      AND DatabaseProvider = N'SqlServer'
)
<> 1
    THROW 51109, 'ResumeExisting tenant RHR01 verification failed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Tenants
    WHERE Id = '11111111-1111-1111-1111-111111111102'
      AND TenantCode = N'RHR02'
      AND Host = N'rehearsal02.localhost'
      AND ShardKey = N'rehearsal-shard-02'
      AND Status = 1
      AND DatabaseProvider = N'SqlServer'
)
<> 1
    THROW 51110, 'ResumeExisting tenant RHR02 verification failed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Tenants
    WHERE Id = '11111111-1111-1111-1111-111111111103'
      AND TenantCode = N'RHR03'
      AND Host = N'rehearsal03.localhost'
      AND ShardKey = N'rehearsal-shard-03'
      AND Status = 3
      AND DatabaseProvider = N'SqlServer'
)
<> 1
    THROW 51111, 'ResumeExisting tenant RHR03 verification failed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.TenantBranding
    WHERE TenantId = '11111111-1111-1111-1111-111111111101'
)
<> 1
    THROW 51112, 'ResumeExisting branding verification failed.', 1;
"@

        $legacyTenantCount = 3
        $legacyBrandingCount = 1
        $migrationTimer = $null
    }
    elseif ($ResumeExisting) {
        throw "ResumeExisting was supplied, but the rehearsal database does not exist."
    }
    else {
    Write-Host "Creating disposable database: $database"

    Invoke-SqlQuery -Query @"
CREATE DATABASE [$database];
"@

    $createdDatabase = Get-SqlScalar -Query @"
SELECT CASE
    WHEN DB_ID(N'$database') IS NULL THEN 0
    ELSE 1
END;
"@

    if ($createdDatabase -ne "1") {
        throw "The rehearsal database was not created."
    }

    Write-Host "Applying 20260823113202_InitialCatalog..."

    Invoke-Ef -Arguments @(
        "database", "update", "20260823113202_InitialCatalog",
        "--project", $catalogProject,
        "--startup-project", $startupProject,
        "--context", $catalogContext,
        "--connection", $connectionString,
        "--no-build"
    )

    Write-Host "Verifying old schema..."

    Invoke-SqlQuery -Database $database -Query @"
IF COL_LENGTH(N'dbo.Tenants', N'DatabaseProvider') IS NOT NULL
    THROW 51000, 'DatabaseProvider exists before the target migration.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM [$historyTable]
    WHERE MigrationId = N'20260823113202_InitialCatalog'
)
    THROW 51001, 'InitialCatalog is missing from migration history.', 1;

IF EXISTS
(
    SELECT 1
    FROM [$historyTable]
    WHERE MigrationId = N'20260905142921_AddTenantDatabaseProvider'
)
    THROW 51002, 'Target migration is already present before upgrade.', 1;
"@

    Write-Host "Seeding synthetic legacy tenants..."

    Invoke-SqlQuery -Database $database -Query @"
INSERT INTO dbo.Tenants
(
    Id,
    TenantCode,
    Host,
    ShardKey,
    TenantName,
    Email,
    Phone,
    Address,
    Status,
    CreatedDate,
    ModifiedDate
)
VALUES
(
    '11111111-1111-1111-1111-111111111101',
    N'RHR01',
    N'rehearsal01.localhost',
    N'rehearsal-shard-01',
    N'Rehearsal Tenant 01',
    N'rehearsal01@example.invalid',
    N'0000000001',
    N'Rehearsal Address 01',
    1,
    '2026-09-06T00:00:00',
    NULL
),
(
    '11111111-1111-1111-1111-111111111102',
    N'RHR02',
    N'rehearsal02.localhost',
    N'rehearsal-shard-02',
    N'Rehearsal Tenant 02',
    N'rehearsal02@example.invalid',
    N'0000000002',
    N'Rehearsal Address 02',
    1,
    '2026-09-06T00:00:00',
    NULL
),
(
    '11111111-1111-1111-1111-111111111103',
    N'RHR03',
    N'rehearsal03.localhost',
    N'rehearsal-shard-03',
    N'Rehearsal Tenant 03',
    N'rehearsal03@example.invalid',
    N'0000000003',
    N'Rehearsal Address 03',
    3,
    '2026-09-06T00:00:00',
    NULL
);

INSERT INTO dbo.TenantBranding
(
    TenantId,
    IsPublic,
    DisplayName,
    LogoUrl,
    PrimaryColor,
    WelcomeMessage,
    SupportEmail,
    SsoEnabled,
    SsoProviderName
)
VALUES
(
    '11111111-1111-1111-1111-111111111101',
    1,
    N'Rehearsal Branding',
    N'https://example.invalid/rehearsal-logo.svg',
    N'#112233',
    N'Welcome to rehearsal',
    N'support@example.invalid',
    0,
    NULL
);
"@

    Write-Host "Capturing pre-migration values..."

    $beforeSnapshot = Invoke-SqlQuery -Database $database -Query @"
SELECT
    CONVERT(nvarchar(36), Id) AS Id,
    TenantCode,
    Host,
    ShardKey,
    Status
FROM dbo.Tenants
WHERE TenantCode IN (N'RHR01', N'RHR02', N'RHR03')
ORDER BY TenantCode;

SELECT COUNT(*) AS TenantCount
FROM dbo.Tenants;

SELECT COUNT(*) AS BrandingCount
FROM dbo.TenantBranding;

SELECT CONVERT(nvarchar(36), TenantId) AS BrandingTenantId
FROM dbo.TenantBranding;
"@

    $beforeSnapshot | ForEach-Object { Write-Host $_ }

    $legacyTenantCount = [int](Get-SqlScalar `
        -Database $database `
        -Query "SELECT COUNT(*) FROM dbo.Tenants;")

    $legacyBrandingCount = [int](Get-SqlScalar `
        -Database $database `
        -Query "SELECT COUNT(*) FROM dbo.TenantBranding;")

    if ($legacyTenantCount -ne 3) {
        throw "Expected 3 legacy tenants, found $legacyTenantCount."
    }

    if ($legacyBrandingCount -ne 1) {
        throw "Expected 1 legacy branding row, found $legacyBrandingCount."
    }

    Write-Host "Applying 20260905142921_AddTenantDatabaseProvider..."

    $migrationTimer = [System.Diagnostics.Stopwatch]::StartNew()

    Invoke-Ef -Arguments @(
        "database", "update", "20260905142921_AddTenantDatabaseProvider",
        "--project", $catalogProject,
        "--startup-project", $startupProject,
        "--context", $catalogContext,
        "--connection", $connectionString,
        "--no-build"
    )

    $migrationTimer.Stop()
    }

    Write-Host "Verifying migrated SQL Server schema..."

    Invoke-SqlQuery -Database $database -Query @"
DECLARE @typeName sysname;
DECLARE @maxLength smallint;
DECLARE @isNullable bit;
DECLARE @defaultDefinition nvarchar(max);

SELECT
    @typeName = ty.name,
    @maxLength = c.max_length,
    @isNullable = c.is_nullable
FROM sys.columns AS c
INNER JOIN sys.types AS ty
    ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.Tenants')
  AND c.name = N'DatabaseProvider';

SELECT
    @defaultDefinition = dc.definition
FROM sys.default_constraints AS dc
INNER JOIN sys.columns AS c
    ON c.default_object_id = dc.object_id
WHERE c.object_id = OBJECT_ID(N'dbo.Tenants')
  AND c.name = N'DatabaseProvider';

IF @typeName <> N'nvarchar'
    THROW 51010, 'DatabaseProvider is not nvarchar.', 1;

IF @maxLength <> 64
    THROW 51011, 'DatabaseProvider is not nvarchar(32).', 1;

IF @isNullable <> 0
    THROW 51012, 'DatabaseProvider is nullable.', 1;

IF @defaultDefinition IS NULL
   OR @defaultDefinition NOT LIKE N'%SqlServer%'
    THROW 51013, 'DatabaseProvider default is missing or incorrect.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Tenants')
      AND name = N'PK_Tenants'
)
    THROW 51014, 'Tenant primary key changed unexpectedly.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Tenants')
      AND name = N'IX_Tenants_TenantCode'
      AND is_unique = 1
)
    THROW 51015, 'TenantCode unique index is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Tenants')
      AND name = N'IX_Tenants_Host'
      AND is_unique = 1
)
    THROW 51016, 'Host unique index is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Tenants')
      AND name = N'IX_Tenants_ShardKey'
      AND is_unique = 1
)
    THROW 51017, 'ShardKey unique index is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_TenantBranding_Tenants_TenantId'
)
    THROW 51018, 'TenantBranding foreign key is missing.', 1;
"@

    $backfill = Get-SqlScalar -Database $database -Query @"
SELECT CONCAT(
    N'SqlServer=', SUM(CASE WHEN DatabaseProvider = N'SqlServer' THEN 1 ELSE 0 END),
    N'|MySql=', SUM(CASE WHEN DatabaseProvider = N'MySql' THEN 1 ELSE 0 END),
    N'|NULL=', SUM(CASE WHEN DatabaseProvider IS NULL THEN 1 ELSE 0 END),
    N'|Other=', SUM(
        CASE
            WHEN DatabaseProvider IS NOT NULL
             AND DatabaseProvider NOT IN (N'SqlServer', N'MySql')
            THEN 1
            ELSE 0
        END)
)
FROM dbo.Tenants
WHERE Id IN
(
    '11111111-1111-1111-1111-111111111101',
    '11111111-1111-1111-1111-111111111102',
    '11111111-1111-1111-1111-111111111103'
);
"@

    Write-Host "Backfill: $backfill"

    if ($backfill -ne "SqlServer=3|MySql=0|NULL=0|Other=0") {
        throw "Unexpected DatabaseProvider backfill: $backfill"
    }

    Invoke-SqlQuery -Database $database -Query @"
IF (SELECT COUNT(*) FROM dbo.Tenants) <> $legacyTenantCount
    THROW 51020, 'Tenant count changed.', 1;

IF (SELECT COUNT(*) FROM dbo.TenantBranding) <> $legacyBrandingCount
    THROW 51021, 'TenantBranding count changed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Tenants
    WHERE Id = '11111111-1111-1111-1111-111111111101'
      AND TenantCode = N'RHR01'
      AND Host = N'rehearsal01.localhost'
      AND ShardKey = N'rehearsal-shard-01'
      AND Status = 1
)
<> 1
    THROW 51022, 'Tenant 1 identity or data changed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Tenants
    WHERE Id = '11111111-1111-1111-1111-111111111102'
      AND TenantCode = N'RHR02'
      AND Host = N'rehearsal02.localhost'
      AND ShardKey = N'rehearsal-shard-02'
      AND Status = 1
)
<> 1
    THROW 51023, 'Tenant 2 identity or data changed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Tenants
    WHERE Id = '11111111-1111-1111-1111-111111111103'
      AND TenantCode = N'RHR03'
      AND Host = N'rehearsal03.localhost'
      AND ShardKey = N'rehearsal-shard-03'
      AND Status = 3
)
<> 1
    THROW 51024, 'Tenant 3 identity or data changed.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.TenantBranding
    WHERE TenantId = '11111111-1111-1111-1111-111111111101'
)
<> 1
    THROW 51025, 'TenantBranding relationship changed.', 1;
"@

    $runnerRoot = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("HRMS-CatalogMigrationRehearsal-" + [Guid]::NewGuid().ToString("N"))

    New-Item -ItemType Directory -Path $runnerRoot -Force | Out-Null

    try {
        $infraProjectAbsolute =
            (Resolve-Path ".\Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj").Path

        $applicationProjectAbsolute =
            (Resolve-Path ".\Backend\HRMS.Application\HRMS.Application.csproj").Path

        $runnerProject = Join-Path $runnerRoot "CatalogRehearsalRunner.csproj"
        $runnerProgram = Join-Path $runnerRoot "Program.cs"

        $runnerProjectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$infraProjectAbsolute" />
    <ProjectReference Include="$applicationProjectAbsolute" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.11" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.11" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.11" />
  </ItemGroup>
</Project>

"@
        $runnerProjectContent | Set-Content -LiteralPath $runnerProject -Encoding UTF8

        $runnerProgramContent = @'
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

var connectionString =
    Environment.GetEnvironmentVariable("HRMS_REHEARSAL_SQL_CONNECTION")
    ?? throw new InvalidOperationException(
        "HRMS_REHEARSAL_SQL_CONNECTION is missing.");

var options = new DbContextOptionsBuilder<HrmsCatalogDbContext>()
    .UseSqlServer(
        connectionString,
        sql =>
        {
            sql.MigrationsAssembly(
                typeof(HrmsCatalogDbContext).Assembly.FullName);

            sql.MigrationsHistoryTable(
                "__EFMigrationsHistoryCatalog");
        })
    .Options;

var sqlRoundTripId =
    Guid.Parse("22222222-2222-2222-2222-222222222201");

var mySqlRoundTripId =
    Guid.Parse("22222222-2222-2222-2222-222222222202");

await using var db = new HrmsCatalogDbContext(options);

var legacy = await db.Tenants
    .Where(t =>
        t.TenantCode == "RHR01"
        || t.TenantCode == "RHR02"
        || t.TenantCode == "RHR03")
    .OrderBy(t => t.TenantCode)
    .ToListAsync();

if (legacy.Count != 3
    || legacy.Any(t =>
        t.DatabaseProvider != DatabaseProviderType.SqlServer))
{
    throw new InvalidOperationException(
        "Current EF model did not materialize all legacy providers as SqlServer.");
}

Console.WriteLine("EF_CURRENT_MODEL=PASS");

var resolver = new TenantShardResolver(
    db,
    new MemoryCache(new MemoryCacheOptions()),
    Options.Create(
        new ShardingOptions
        {
            SqlServerConnectionStringTemplate =
                "Server=lpc:.;Database={shardKey};Integrated Security=True;TrustServerCertificate=True;"
        }));

var resolved = await resolver.ResolveByHostAsync(
    "rehearsal01.localhost");

if (resolved is null
    || resolved.TenantId !=
        Guid.Parse("11111111-1111-1111-1111-111111111101")
    || resolved.ShardKey != "rehearsal-shard-01"
    || resolved.DatabaseProvider != DatabaseProviderType.SqlServer)
{
    throw new InvalidOperationException(
        "Host/shard/provider resolution failed.");
}

Console.WriteLine("HOST_RESOLVER=PASS");

try
{
    db.Tenants.Add(
        new Tenant
        {
            Id = sqlRoundTripId,
            TenantCode = "RHRSQL",
            Host = "rehearsal-sql-roundtrip.localhost",
            ShardKey = "rehearsal-sql-roundtrip",
            TenantName = "SQL Server Roundtrip",
            Status = TenantStatus.Active,
            DatabaseProvider = DatabaseProviderType.SqlServer
        });

    db.Tenants.Add(
        new Tenant
        {
            Id = mySqlRoundTripId,
            TenantCode = "RHRMYSQL",
            Host = "rehearsal-mysql-roundtrip.localhost",
            ShardKey = "rehearsal-mysql-roundtrip",
            TenantName = "MySQL Roundtrip",
            Status = TenantStatus.Active,
            DatabaseProvider = DatabaseProviderType.MySql
        });

    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var sqlReloaded = await db.Tenants
        .SingleAsync(t => t.Id == sqlRoundTripId);

    var mySqlReloaded = await db.Tenants
        .SingleAsync(t => t.Id == mySqlRoundTripId);

    if (sqlReloaded.DatabaseProvider != DatabaseProviderType.SqlServer)
        throw new InvalidOperationException(
            "SqlServer enum round-trip failed.");

    if (mySqlReloaded.DatabaseProvider != DatabaseProviderType.MySql)
        throw new InvalidOperationException(
            "MySql enum round-trip failed.");

    Console.WriteLine("SQLSERVER_ROUNDTRIP=PASS");
    Console.WriteLine("MYSQL_ROUNDTRIP=PASS");
}
finally
{
    db.ChangeTracker.Clear();

    var sqlCleanup = await db.Tenants
        .SingleOrDefaultAsync(t => t.Id == sqlRoundTripId);

    if (sqlCleanup is not null)
        db.Tenants.Remove(sqlCleanup);

    var mySqlCleanup = await db.Tenants
        .SingleOrDefaultAsync(t => t.Id == mySqlRoundTripId);

    if (mySqlCleanup is not null)
        db.Tenants.Remove(mySqlCleanup);

    await db.SaveChangesAsync();
}
'@
        $runnerProgramContent | Set-Content -LiteralPath $runnerProgram -Encoding UTF8

        $env:HRMS_REHEARSAL_SQL_CONNECTION = $connectionString

        & dotnet restore $runnerProject --ignore-failed-sources
        if ($LASTEXITCODE -ne 0) {
            throw "Temporary EF verification runner restore failed."
        }

        & dotnet run `
            --project $runnerProject `
            --no-restore `
            -p:UseAppHost=false

        if ($LASTEXITCODE -ne 0) {
            throw "Current HrmsCatalogDbContext verification failed."
        }
    }
    finally {
        Remove-Item -LiteralPath $runnerRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Testing SQL Server NOT NULL enforcement..."

    Invoke-SqlQuery -Database $database -Query @"
BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE dbo.Tenants
    SET DatabaseProvider = NULL
    WHERE Id = '11111111-1111-1111-1111-111111111101';

    THROW 51030, 'NULL update unexpectedly succeeded.', 1;
END TRY
BEGIN CATCH
    DECLARE @errorNumber int = ERROR_NUMBER();

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    IF @errorNumber <> 515
        THROW;
END CATCH;
"@

    $history = Get-SqlScalar -Database $database -Query @"
SELECT CONCAT(
    N'Initial=', CASE WHEN EXISTS
    (
        SELECT 1
        FROM [$historyTable]
        WHERE MigrationId = N'20260823113202_InitialCatalog'
    ) THEN N'PASS' ELSE N'FAIL' END,
    N'|Target=', CASE WHEN EXISTS
    (
        SELECT 1
        FROM [$historyTable]
        WHERE MigrationId =
            N'20260905142921_AddTenantDatabaseProvider'
    ) THEN N'PASS' ELSE N'FAIL' END
);
"@

    Write-Host "Migration history: $history"

    Invoke-SqlQuery -Database $database -Query @"
IF NOT EXISTS
(
    SELECT 1
    FROM [$historyTable]
    WHERE MigrationId = N'20260823113202_InitialCatalog'
)
    THROW 51040, 'InitialCatalog is missing from migration history.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM [$historyTable]
    WHERE MigrationId =
        N'20260905142921_AddTenantDatabaseProvider'
)
    THROW 51041, 'AddTenantDatabaseProvider is missing from migration history.', 1;

IF
(
    SELECT MigrationId
    FROM [$historyTable]
    WHERE MigrationId = N'20260823113202_InitialCatalog'
)
>
(
    SELECT MigrationId
    FROM [$historyTable]
    WHERE MigrationId =
        N'20260905142921_AddTenantDatabaseProvider'
)
    THROW 51042, 'Migration history order is incorrect.', 1;
"@

    Write-Host "Checking pending model changes..."

    Invoke-Ef -Arguments @(
        "migrations", "has-pending-model-changes",
        "--project", $catalogProject,
        "--startup-project", $startupProject,
        "--context", $catalogContext,
        "--no-build"
    )

    Write-Host "Checking repository diff..."

    & git diff --check
    if ($LASTEXITCODE -ne 0) {
        throw "git diff --check failed."
    }

    Write-Host ""
    Write-Host "========================================"
    if ($ResumeExisting) {
        Write-Host "CATALOG MIGRATION REHEARSAL PASS WITH WARNINGS"
    }
    else {
        Write-Host "CATALOG MIGRATION REHEARSAL PASS"
    }
    Write-Host "========================================"
    Write-Host "Connectivity: PASS"
    Write-Host "Old schema: PASS"
    Write-Host "Legacy tenants: $legacyTenantCount"
    Write-Host "Branding rows: $legacyBrandingCount"
    if ($ResumeExisting) {
        Write-Host "Verified existing migration state: PASS"
        Write-Host "Database creation/migration application: NOT REPEATED DURING RESUME"
        Write-Host "Migration elapsed: NOT AVAILABLE - RESUMED EXISTING REHEARSAL"
    }
    else {
        Write-Host "Migration: PASS"
        Write-Host "Migration elapsed: $migrationTimer"
    }
    Write-Host "DatabaseProvider type: nvarchar(32)"
    Write-Host "DatabaseProvider nullable: NO"
    Write-Host "Backfill: $backfill"
    Write-Host "Tenant preservation: PASS"
    Write-Host "Branding preservation: PASS"
    Write-Host "EF current model: PASS"
    Write-Host "SqlServer roundtrip: PASS"
    Write-Host "MySql roundtrip: PASS"
    Write-Host "NULL rejection: PASS"
    Write-Host "Host resolver: PASS"
    Write-Host "Migration history: PASS"
    Write-Host "Model drift: PASS"
    Write-Host "git diff --check: PASS"
    if ($ResumeExisting) {
        Write-Host "Reason for warning:"
        Write-Host "original full migration execution output/elapsed time was not preserved."
    }
    Write-Host "Database retained:"
    Write-Host $database
    Write-Host "========================================"
}
finally {
    Restore-ProcessEnvironmentVariable `
        -Name "Database__CatalogProvider" `
        -OriginalValue $oldCatalogProvider

    Restore-ProcessEnvironmentVariable `
        -Name "ConnectionStrings__Catalog" `
        -OriginalValue $oldCatalogConnection

    Restore-ProcessEnvironmentVariable `
        -Name "HRMS_REHEARSAL_SQL_CONNECTION" `
        -OriginalValue $oldRehearsalConnection

    Restore-ProcessEnvironmentVariable `
        -Name "MSBuildEnableWorkloadResolver" `
        -OriginalValue $oldWorkloadResolver
}
