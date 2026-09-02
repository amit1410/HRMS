<#
.SYNOPSIS
    Creates the HRMS Catalog and Shard databases on SQL Server.

.DESCRIPTION
    Creates HRMS_Catalog and HRMS databases if they do not exist.
    Does NOT drop or modify existing databases.

.PARAMETER Server
    SQL Server instance name. Default: (localdb)\MSSQLLocalDB

.EXAMPLE
    .\Database\create-database.ps1
    .\Database\create-database.ps1 -Server ".\SQLEXPRESS"
#>
param(
    [string]$Server = "(localdb)\MSSQLLocalDB"
)

Write-Host "Creating HRMS databases on $Server..." -ForegroundColor Cyan

# Create Catalog database
$sqlCatalog = @"
IF DB_ID(N'HRMS_Catalog') IS NULL
    CREATE DATABASE [HRMS_Catalog];
PRINT 'HRMS_Catalog: OK';
"@

# Create Shard database
$sqlShard = @"
IF DB_ID(N'HRMS') IS NULL
    CREATE DATABASE [HRMS];
PRINT 'HRMS: OK';
"@

sqlcmd -S $Server -E -Q $sqlCatalog 2>&1
sqlcmd -S $Server -E -Q $sqlShard 2>&1

Write-Host "`nDatabases created successfully." -ForegroundColor Green
Write-Host "Next step: .\Database\apply-database.ps1" -ForegroundColor Yellow
