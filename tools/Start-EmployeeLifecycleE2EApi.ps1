[CmdletBinding()]
param(
    [int]$Port = 5080,
    [string]$ExpectedCatalog = 'HRMS_E2E_Catalog_20261007_091930'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Backend\HRMS.API\HRMS.API.csproj'
$logDirectory = Join-Path $repoRoot 'logs'
$stdout = Join-Path $logDirectory 'e2e-api.log'
$stderr = Join-Path $logDirectory 'e2e-api-error.log'
$expectedTenantId = [Guid]'4056dce7-9011-445d-b785-ea1d30957878'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "API project was not found: $project" }
if (-not (Test-Path -LiteralPath $logDirectory -PathType Container)) { New-Item -ItemType Directory -Path $logDirectory | Out-Null }
if ($ExpectedCatalog -notmatch '^[A-Za-z0-9_]+$') { throw 'ExpectedCatalog must be a safe database identifier.' }

function Get-LocalSetting {
    param([Parameter(Mandatory)][string]$Name)
    $environmentName = $Name.Replace(':', '__')
    $environmentValue = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) { return $environmentValue }

    [xml]$projectXml = Get-Content (Join-Path $repoRoot 'Backend\HRMS.API\HRMS.API.csproj') -Raw
    $userSecretsId = [string]$projectXml.Project.PropertyGroup.UserSecretsId
    $secretFile = Join-Path $env:APPDATA ("Microsoft\UserSecrets\$userSecretsId\secrets.json")
    if (-not (Test-Path -LiteralPath $secretFile -PathType Leaf)) {
        throw "Missing local user-secrets file for the API; configure $Name through the existing secure source."
    }
    $secrets = Get-Content $secretFile -Raw | ConvertFrom-Json
    $property = $secrets.psobject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Missing required local setting $Name in the existing secure source."
    }
    return [string]$property.Value
}

function Set-MySqlDatabaseName {
    param([Parameter(Mandatory)][string]$ConnectionString, [Parameter(Mandatory)][string]$DatabaseName)
    if ($ConnectionString -notmatch '(?i)(^|;)\s*(Database|Initial Catalog)\s*=') {
        throw 'The configured MySQL catalog connection string has no database component.'
    }
    return [regex]::Replace($ConnectionString, '(?i)(^|;)\s*(Database|Initial Catalog)\s*=\s*[^;]*', {
        param($match) "$($match.Groups[1].Value)Database=$DatabaseName"
    })
}

function Assert-E2EApiIdentity {
    param([Parameter(Mandatory)]$Info)
    if ([string]$Info.environment -cne 'Development') { throw 'E2E API identity mismatch: environment is not Development.' }
    if ([string]$Info.passwordRecoveryEmailProvider -cne 'Fake' -or
        [string]$Info.passwordRecoveryEmailSender -cne 'DevelopmentEmailSender' -or
        $Info.developmentOtpAvailable -ne $true) { throw 'E2E API identity mismatch: development email recovery configuration is not active.' }
    if ([string]$Info.catalogDatabase -cne $ExpectedCatalog) {
        throw "E2E API identity mismatch: expected isolated catalog $ExpectedCatalog."
    }
    if ([string]$Info.databaseProvider -notmatch 'MySql') { throw 'E2E API identity mismatch: catalog provider is not MySql.' }
    $tenants = @($Info.tenants | Where-Object { [string]$_.tenantCode -ceq 'E2E01' })
    if ($tenants.Count -ne 1 -or
        [Guid]$tenants[0].tenantId -ne $expectedTenantId -or
        [string]$tenants[0].databaseProvider -cne 'MySql' -or
        [string]$tenants[0].shardKey -cne 'e2e01') {
        throw 'E2E API identity mismatch: E2E01 identity is missing or incorrect.'
    }
}

$listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
if ($listener) {
    $owners = @($listener | Select-Object -ExpandProperty OwningProcess -Unique)
    throw "Port $Port is already in use by process $($owners -join ', '). Stop that exact API process after inspection, then rerun this launcher."
}

$names = @(
    'ASPNETCORE_ENVIRONMENT',
    'DOTNET_ENVIRONMENT',
    'ASPNETCORE_URLS',
    'Database__SkipInitialization',
    'Database__CatalogProvider',
    'ConnectionStrings__Catalog',
    'Sharding__MySqlConnectionStringTemplate',
    'Email__Provider',
    'PasswordRecoveryProviders__EmailProvider',
    'PasswordRecoveryProviders__SmsProvider'
)
$old = @{}
foreach ($name in $names) { $old[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

try {
    $catalogConnection = Set-MySqlDatabaseName -ConnectionString (Get-LocalSetting 'ConnectionStrings:Catalog') -DatabaseName $ExpectedCatalog
    $mySqlTemplate = Get-LocalSetting 'Sharding:MySqlConnectionStringTemplate'
    if ($mySqlTemplate -notmatch '\{shardKey\}') { throw 'The configured MySQL sharding template must contain {shardKey}.' }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    # Run the supported idempotent migration/seeding path so existing E2E01 tenants receive
    # newly added role-permission grants on startup. This does not recreate the tenant or
    # assign permissions to an individual user.
    $env:Database__SkipInitialization = 'false'
    $env:Database__CatalogProvider = 'MySql'
    $env:ConnectionStrings__Catalog = $catalogConnection
    $env:Sharding__MySqlConnectionStringTemplate = $mySqlTemplate
    $env:Email__Provider = 'Fake'
    $env:PasswordRecoveryProviders__EmailProvider = 'Fake'
    $env:PasswordRecoveryProviders__SmsProvider = 'Fake'

    # Windows PowerShell 5.1 may inherit both PATH and Path. Start-Process tries
    # to rebuild that environment as a case-insensitive dictionary and fails on
    # the duplicate. cmd.exe preserves the inherited environment and lets the
    # child receive these explicit E2E values.
    $command = 'start "" /b cmd.exe /d /c ""dotnet" run --project "' + $project + '" --no-build --no-launch-profile -p:UseAppHost=false -p:MSBuildEnableWorkloadResolver=false 1>"' + $stdout + '" 2>"' + $stderr + '""'
    & cmd.exe /d /c $command
    if ($LASTEXITCODE -ne 0) { throw "The isolated E2E API could not be started (cmd exit $LASTEXITCODE)." }
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $infoResponse = Invoke-RestMethod -Uri "http://localhost:$Port/api/system/info" -Headers @{ Host = 'platform.localhost' }
            $info = $infoResponse.data
            if ($null -ne $info) { Assert-E2EApiIdentity -Info $info; break }
        } catch {
            if ((Get-Date) -ge $deadline) { throw "E2E API identity validation failed: $($_.Exception.Message)" }
        }
    } while ((Get-Date) -lt $deadline)
    if ($null -eq $info) { throw 'E2E API identity validation timed out.' }
    Write-Host "Started isolated E2E API on http://localhost:$Port."
    Write-Host "Logs: $stdout and $stderr"
}
finally {
    foreach ($name in $names) {
        if ($null -eq $old[$name]) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
        else { Set-Item "Env:$name" $old[$name] }
    }
}
