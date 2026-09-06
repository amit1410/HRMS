[CmdletBinding()]
param(
    [string]$Server = "lpc:.",
    [string]$Database = "HRMS_adapter-sql-tenant-test-20260906"
)

$ErrorActionPreference = "Stop"
$expectedDatabase = "HRMS_adapter-sql-tenant-test-20260906"
$oldWorkloadResolver = $env:MSBuildEnableWorkloadResolver
$connection = "Server=$Server;Database=$expectedDatabase;Integrated Security=True;TrustServerCertificate=True;"

function Invoke-SqlRead {
    param([Parameter(Mandatory = $true)][string]$Query, [string]$Db = "master")
    $output = @(& sqlcmd -S $Server -d $Db -E -C -b -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed." }
    @($output | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
}

try {
    if ($Database -cne $expectedDatabase) { throw "Only the exact disposable SQL tenant database is permitted." }
    $exists = Invoke-SqlRead "SELECT CASE WHEN DB_ID(N'$expectedDatabase') IS NULL THEN 0 ELSE 1 END;"
    if ($exists.Count -ne 1 -or $exists[0] -ne "0") { throw "Disposable SQL tenant already exists; no changes made." }
    & sqlcmd -S $Server -d master -E -C -b -Q "CREATE DATABASE [$expectedDatabase];"
    if ($LASTEXITCODE -ne 0) { throw "Disposable SQL tenant creation failed." }

    $env:MSBuildEnableWorkloadResolver = "false"
    & dotnet ef database update `
        --project .\Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj `
        --startup-project .\Backend\HRMS.API\HRMS.API.csproj `
        --context HRMS.Infrastructure.Persistence.HrmsDbContext `
        --connection $connection `
        --no-build
    if ($LASTEXITCODE -ne 0) { throw "SQL Server tenant migration failed." }
    Write-Host "Prepared only $expectedDatabase with SQL Server tenant migrations."
    Write-Host "No business data was seeded."
} finally {
    if ($null -eq $oldWorkloadResolver) { Remove-Item Env:MSBuildEnableWorkloadResolver -ErrorAction SilentlyContinue } else { $env:MSBuildEnableWorkloadResolver = $oldWorkloadResolver }
}
