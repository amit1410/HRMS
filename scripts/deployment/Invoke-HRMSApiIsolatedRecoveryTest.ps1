[CmdletBinding()]
param(
    [switch]$StartTestApi,

    [ValidateSet('B')]
    [string]$Approval,

    [string]$PackagePath = 'D:\HRMS\artifacts\deployments\20261101T040143Z\recovery-api',

    [string]$ContentRoot = 'D:\HRMS\.employee-import-test-artifacts-20261030\bin\HRMS.API\debug'
)

$ErrorActionPreference = 'Stop'
$expectedManifest = '4F60E9CC1253BF4967932C50F47D394803DD0963634AE36F2C0FFA3612554422'
$packageVerifier = Join-Path $PSScriptRoot 'Test-HRMSDeploymentPackage.ps1'
$configurationAudit = Join-Path $PSScriptRoot 'Test-HRMSApiCandidateConfiguration.ps1'
$apiProject = Join-Path $PSScriptRoot '..\..\Backend\HRMS.API\HRMS.API.csproj'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$apiDll = Join-Path $package 'HRMS.API.dll'

& $packageVerifier -PackagePath $package -ExpectedManifestSHA256 $expectedManifest
& $configurationAudit -ContentRoot $ContentRoot -ApiProject $apiProject -Profile IsolatedRecovery
if ($LASTEXITCODE -ne 0) { throw 'Isolated recovery configuration did not pass.' }

$listenerKnown = $false
$portInUse = $false
try {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5091 -ErrorAction Stop)
    $listenerKnown = $true
    $portInUse = ($listeners.Count -gt 0)
}
catch {
    $lines = @(netstat.exe -ano -p tcp | Where-Object { $_ -match ':5091\s+.*LISTENING\s+\d+\s*$' })
    if ($LASTEXITCODE -eq 0) { $listenerKnown = $true; $portInUse = ($lines.Count -gt 0) }
}
if (-not $listenerKnown -or $portInUse) { throw 'Loopback test port 5091 is occupied or cannot be verified.' }

try {
    $apiProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -ieq 'HRMS.API.exe' -or $_.CommandLine -match '(?i)(?:^|[\\/])HRMS\.API\.dll(?:\s|"|$)'
    })
}
catch {
    throw 'Complete API process inventory is unavailable; isolated startup refused.'
}
if ($apiProcesses.Count -gt 0) {
    [pscustomobject]@{ PackageIntegrity = 'PASS'; IsolatedConfiguration = 'PASS'; Port5091Free = $true; CompetingApiProcesses = $apiProcesses.Count; StartPerformed = $false; Result = 'BLOCKED' } | Format-List
    throw 'An existing HRMS API process has unverified worker state; isolated test startup refused.'
}

if (-not $StartTestApi) {
    [pscustomobject]@{ PackageIntegrity = 'PASS'; IsolatedConfiguration = 'PASS'; Port5091Free = $true; CompetingApiProcesses = 0; StartPerformed = $false; Result = 'READY_FOR_SEPARATE_TEST_APPROVAL' } | Format-List
    return
}
if ($Approval -ne 'B') { throw 'Starting the isolated recovery API requires Approval B to explicitly include this smoke test.' }
$phrase = Read-Host 'Type START ISOLATED HRMS API TEST to start the loopback-only recovery API'
if ($phrase -cne 'START ISOLATED HRMS API TEST') { throw 'Test startup confirmation did not match.' }

$saved = @{}
$keys = @(
    'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT', 'EmployeeBulkImportWorker__Enabled',
    'WelcomeInvitations__Enabled', 'LeaveReminders__Enabled', 'ConfirmationReminders__Enabled',
    'LeaveAccrual__Enabled', 'AttendanceDeviceWorker__Enabled', 'Database__StartupMode'
)
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
    try { & dotnet $apiDll --urls 'http://127.0.0.1:5091'; exit $LASTEXITCODE }
    finally { Pop-Location }
}
finally {
    foreach ($key in $keys) {
        $value = $saved[$key]
        if ($null -eq $value) { [Environment]::SetEnvironmentVariable($key, $null, 'Process') }
        else { [Environment]::SetEnvironmentVariable($key, $value, 'Process') }
    }
}
