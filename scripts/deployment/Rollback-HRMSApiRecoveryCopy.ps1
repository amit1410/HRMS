[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('B')]
    [string]$Approval,

    [switch]$ExecuteRollback,

    [string]$PackagePath,

    [string]$ContentRoot = 'D:\HRMS\.employee-import-test-artifacts-20261030\bin\HRMS.API\debug'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $PSScriptRoot '..\..\artifacts\deployments\20261101T040143Z\recovery-api'
}
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$apiDll = Join-Path $package 'HRMS.API.dll'
$expectedRecoveryManifest = '4F60E9CC1253BF4967932C50F47D394803DD0963634AE36F2C0FFA3612554422'
$packageVerifier = Join-Path $PSScriptRoot 'Test-HRMSDeploymentPackage.ps1'
& $packageVerifier -PackagePath $package -ExpectedManifestSHA256 $expectedRecoveryManifest

$audit = Join-Path $PSScriptRoot 'Test-HRMSApiCandidateConfiguration.ps1'
$project = Join-Path $PSScriptRoot '..\..\Backend\HRMS.API\HRMS.API.csproj'
& $audit -ContentRoot $ContentRoot -ApiProject $project -Profile IsolatedRecovery
if ($LASTEXITCODE -ne 0) { throw 'Recovery effective configuration audit failed.' }

$listeners = @()
try {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5080 -ErrorAction Stop)
}
catch {
    $netstatLines = @(netstat.exe -ano -p tcp | Where-Object { $_ -match ':5080\s+.*LISTENING\s+\d+\s*$' })
    if ($LASTEXITCODE -ne 0) { throw 'Listener ownership cannot be verified; rollback refused.' }
    $listeners = $netstatLines
}
if ($listeners.Count -gt 0) { throw 'Port 5080 is still owned; retire the failed candidate through its approved graceful channel first.' }

try {
    $apiProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -ieq 'HRMS.API.exe' -or $_.CommandLine -match '(?i)(?:^|[\\/])HRMS\.API\.dll(?:\s|"|$)'
    })
}
catch {
    throw 'The complete API process inventory is unavailable; rollback refused.'
}
if ($apiProcesses.Count -gt 0) { throw 'An HRMS API process is still present; rollback refused until an approved retirement and inventory check.' }

if (-not $ExecuteRollback) {
    [pscustomobject]@{ PackageHashes = 'PASS'; EffectiveNormalConfiguration = 'PASS'; Port5080Free = $true; Started = $false } | Format-List
    return
}
if ($Approval -ne 'B') { throw 'Controlled API rollback requires Approval B.' }
$phrase = Read-Host 'Type ROLLBACK HRMS NORMAL API to start the recovery copy'
if ($phrase -cne 'ROLLBACK HRMS NORMAL API') { throw 'Rollback confirmation did not match.' }

$keys = @(
    'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT', 'EmployeeBulkImportWorker__Enabled',
    'WelcomeInvitations__Enabled', 'LeaveReminders__Enabled', 'ConfirmationReminders__Enabled',
    'LeaveAccrual__Enabled', 'AttendanceDeviceWorker__Enabled', 'Database__StartupMode'
)
$saved = @{}
foreach ($key in $keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
try {
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:EmployeeBulkImportWorker__Enabled = 'false'
    $env:WelcomeInvitations__Enabled = 'false'
    $env:LeaveReminders__Enabled = 'false'
    $env:ConfirmationReminders__Enabled = 'false'
    $env:LeaveAccrual__Enabled = 'false'
    $env:AttendanceDeviceWorker__Enabled = 'false'
    $env:Database__StartupMode = 'Disabled'
    Push-Location $ContentRoot
    try { & dotnet $apiDll --urls 'http://127.0.0.1:5080'; exit $LASTEXITCODE }
    finally { Pop-Location }
}
finally {
    foreach ($key in $keys) {
        $value = $saved[$key]
        if ($null -eq $value) { [Environment]::SetEnvironmentVariable($key, $null, 'Process') }
        else { [Environment]::SetEnvironmentVariable($key, $value, 'Process') }
    }
}
