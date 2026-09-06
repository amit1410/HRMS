[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$MySqlShardKey,

    [Parameter(Mandatory)]
    [string]$ExpectedMySqlDatabase,

    [Parameter(Mandatory)]
    [string]$ExpectedSqlDatabase,

    [Parameter(Mandatory)]
    [ValidateSet(0, 1, 2)]
    [int]$MySqlLowerCaseTableNames,

    [string]$MySqlServer = "127.0.0.1",
    [int]$MySqlPort = 3306,

    [switch]$SetupOnly
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "MixedProviderValidationRunner\MixedProviderValidationRunner.csproj"

$oldCatalogProvider = $env:Database__CatalogProvider
$oldCatalogConnection = $env:ConnectionStrings__Catalog
$oldSqlServerConnection = $env:ConnectionStrings__SqlServer
$oldLegacySqlTemplate = $env:Sharding__ConnectionStringTemplate
$oldSqlTemplate = $env:Sharding__SqlServerConnectionStringTemplate
$oldMySqlTemplate = $env:Sharding__MySqlConnectionStringTemplate
$oldWorkloadResolver = $env:MSBuildEnableWorkloadResolver
$securePassword = $null
$plainPassword = $null

function Restore-ProcessEnvironmentVariable {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [AllowNull()] [string]$OriginalValue
    )

    if ($null -eq $OriginalValue) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
    } else {
        Set-Item "Env:$Name" $OriginalValue
    }
}

try {
    $env:MSBuildEnableWorkloadResolver = "false"

    dotnet restore $projectPath `
        -p:MSBuildEnableWorkloadResolver=false
    if ($LASTEXITCODE -ne 0) {
        throw "The external mixed-provider validation runner restore failed with exit code $LASTEXITCODE."
    }

    dotnet build $projectPath `
        -p:UseAppHost=false `
        -p:MSBuildEnableWorkloadResolver=false `
        --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "The external mixed-provider validation runner build failed with exit code $LASTEXITCODE."
    }

    $env:Database__CatalogProvider = "SqlServer"
    $env:ConnectionStrings__Catalog = "Server=lpc:.;Database=HRMS_Catalog;Integrated Security=True;TrustServerCertificate=True;"
    $env:ConnectionStrings__SqlServer = "Server=lpc:.;Database=HRMS;Integrated Security=True;TrustServerCertificate=True;"
    Remove-Item Env:Sharding__ConnectionStringTemplate -ErrorAction SilentlyContinue
    Remove-Item Env:Sharding__SqlServerConnectionStringTemplate -ErrorAction SilentlyContinue
    if ($SetupOnly) {
        $env:Sharding__MySqlConnectionStringTemplate = "Server=$MySqlServer;Port=$MySqlPort;Database=HRMS_{shardKey};User ID=setup-only;Password=setup-only-placeholder;"
    } else {
        $securePassword = Read-Host "MySQL password" -AsSecureString
        $plainPassword = [System.Net.NetworkCredential]::new("", $securePassword).Password
        if ([string]::IsNullOrWhiteSpace($plainPassword)) {
            throw "MySQL password was empty. Validation was not started."
        }
        $env:Sharding__MySqlConnectionStringTemplate = "Server=$MySqlServer;Port=$MySqlPort;Database=HRMS_{shardKey};User ID=root;Password=$plainPassword;"
    }
    $env:MSBuildEnableWorkloadResolver = "false"

    $runnerArguments = @(
        "--sql-database", $ExpectedSqlDatabase,
        "--mysql-shard-key", $MySqlShardKey,
        "--expected-mysql-database", $ExpectedMySqlDatabase,
        "--mysql-lower-case-table-names", $MySqlLowerCaseTableNames
    )
    if ($SetupOnly) { $runnerArguments += "--setup-only" }

    dotnet run `
        --project $projectPath `
        -p:UseAppHost=false `
        -p:MSBuildEnableWorkloadResolver=false `
        --no-build `
        --no-restore `
        -- $runnerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The external mixed-provider validation runner failed with exit code $LASTEXITCODE."
    }
}
finally {
    Restore-ProcessEnvironmentVariable -Name "Database__CatalogProvider" -OriginalValue $oldCatalogProvider
    Restore-ProcessEnvironmentVariable -Name "ConnectionStrings__Catalog" -OriginalValue $oldCatalogConnection
    Restore-ProcessEnvironmentVariable -Name "ConnectionStrings__SqlServer" -OriginalValue $oldSqlServerConnection
    Restore-ProcessEnvironmentVariable -Name "Sharding__ConnectionStringTemplate" -OriginalValue $oldLegacySqlTemplate
    Restore-ProcessEnvironmentVariable -Name "Sharding__SqlServerConnectionStringTemplate" -OriginalValue $oldSqlTemplate
    Restore-ProcessEnvironmentVariable -Name "Sharding__MySqlConnectionStringTemplate" -OriginalValue $oldMySqlTemplate
    Restore-ProcessEnvironmentVariable -Name "MSBuildEnableWorkloadResolver" -OriginalValue $oldWorkloadResolver
    $plainPassword = $null
    if ($null -ne $securePassword) {
        $securePassword.Dispose()
        $securePassword = $null
    }
}
