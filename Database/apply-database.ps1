<#
.SYNOPSIS
    Applies the HRMS database schema via EF Core migrations.

.DESCRIPTION
    Starts the backend API briefly to trigger EF Core migrations and seed data.
    The application creates/updates all tables and inserts required master data.
    Alternatively, runs the SQL script directly via sqlcmd.

.PARAMETER Server
    SQL Server instance name. Default: (localdb)\MSSQLLocalDB

.PARAMETER UseSqlScript
    If specified, runs HRMS-Database.sql via sqlcmd instead of EF Core migrations.

.EXAMPLE
    .\Database\apply-database.ps1
    .\Database\apply-database.ps1 -UseSqlScript
#>
param(
    [string]$Server = "(localdb)\MSSQLLocalDB",
    [switch]$UseSqlScript
)

$ErrorActionPreference = "Stop"

if ($UseSqlScript) {
    Write-Host "Applying schema via SQL script..." -ForegroundColor Cyan
    $scriptPath = Join-Path $PSScriptRoot "HRMS-Database.sql"
    if (!(Test-Path $scriptPath)) {
        Write-Host "ERROR: SQL script not found at $scriptPath" -ForegroundColor Red
        exit 1
    }
    sqlcmd -S $Server -E -i $scriptPath
    Write-Host "`nSQL script applied successfully." -ForegroundColor Green
} else {
    Write-Host "Applying schema via EF Core migrations (starting API briefly)..." -ForegroundColor Cyan
    $apiPath = Join-Path $PSScriptRoot "..\Backend\HRMS.API"
    if (!(Test-Path $apiPath)) {
        Write-Host "ERROR: API project not found at $apiPath" -ForegroundColor Red
        exit 1
    }

    Write-Host "The API will start, apply migrations, seed data, then you can stop it with Ctrl+C." -ForegroundColor Yellow
    Write-Host ""

    Push-Location $apiPath
    try {
        dotnet run
    } finally {
        Pop-Location
    }
}

Write-Host "`nNext step: .\start-backend.ps1" -ForegroundColor Yellow
