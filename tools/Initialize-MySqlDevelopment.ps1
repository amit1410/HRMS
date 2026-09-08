[CmdletBinding()]
param(
    [string]$MySqlHost = '127.0.0.1',
    [int]$MySqlPort = 3306,
    [string]$MySqlUser = 'root'
)

$ErrorActionPreference = 'Stop'

Write-Host 'Creating hrms_catalog if it does not exist.'
Write-Host 'The MySQL client will prompt for the password; it is not passed on the command line.'
& mysql --host=$MySqlHost --port=$MySqlPort --user=$MySqlUser --password --database=mysql --execute="CREATE DATABASE IF NOT EXISTS hrms_catalog CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
if ($LASTEXITCODE -ne 0) {
    throw "MySQL catalog creation failed with exit code $LASTEXITCODE."
}

Write-Host 'hrms_catalog is ready. Set ConnectionStrings__Catalog and Sharding__MySqlConnectionStringTemplate, then start the API.'
