[CmdletBinding()]
param(
    [string]$CatalogServer = "lpc:.",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"
$oldProvider = $env:Database__CatalogProvider
$oldSkip = $env:Database__SkipInitialization
$oldCatalog = $env:ConnectionStrings__Catalog
$oldSqlTemplate = $env:Sharding__SqlServerConnectionStringTemplate
$oldMySqlTemplate = $env:Sharding__MySqlConnectionStringTemplate
$oldWorkload = $env:MSBuildEnableWorkloadResolver
$oldUrls = $env:ASPNETCORE_URLS

try {
    $securePassword = Read-Host "MySQL password" -AsSecureString
    $plainPassword = [System.Net.NetworkCredential]::new("", $securePassword).Password
    if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw "Password was empty; API was not started." }
    $env:Database__CatalogProvider = "SqlServer"
    $env:Database__SkipInitialization = "true"
    $env:ConnectionStrings__Catalog = "Server=$CatalogServer;Database=HRMS_Catalog;Integrated Security=True;TrustServerCertificate=True;"
    $env:Sharding__SqlServerConnectionStringTemplate = "Server=$CatalogServer;Database=HRMS_{shardKey};Integrated Security=True;TrustServerCertificate=True;"
    $env:Sharding__MySqlConnectionStringTemplate = "Server=127.0.0.1;Port=3306;Database=HRMS_{shardKey};User ID=root;Password=$plainPassword;"
    $env:MSBuildEnableWorkloadResolver = "false"
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    & dotnet run --project .\Backend\HRMS.API\HRMS.API.csproj --no-build -p:UseAppHost=false -p:MSBuildEnableWorkloadResolver=false
    if ($LASTEXITCODE -ne 0) { throw "API exited with code $LASTEXITCODE." }
} finally {
    foreach ($item in @(
        @{ Name = "Database__CatalogProvider"; Value = $oldProvider },
        @{ Name = "Database__SkipInitialization"; Value = $oldSkip },
        @{ Name = "ConnectionStrings__Catalog"; Value = $oldCatalog },
        @{ Name = "Sharding__SqlServerConnectionStringTemplate"; Value = $oldSqlTemplate },
        @{ Name = "Sharding__MySqlConnectionStringTemplate"; Value = $oldMySqlTemplate },
        @{ Name = "MSBuildEnableWorkloadResolver"; Value = $oldWorkload },
        @{ Name = "ASPNETCORE_URLS"; Value = $oldUrls }
    )) {
        if ($null -eq $item.Value) { Remove-Item "Env:$($item.Name)" -ErrorAction SilentlyContinue } else { Set-Item "Env:$($item.Name)" $item.Value }
    }
}
