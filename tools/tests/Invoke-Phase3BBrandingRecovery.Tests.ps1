[CmdletBinding()]
param()

Set-StrictMode -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath '..\Phase3BRecoveryMetadata.ps1')

$server = 'localhost,1433'
$runId = '20260903T180116Z_150524'
$catalog = 'HRMS_Phase3B_Integration_20260903T180116Z_150524_catalog'
$tenants = @(
    'HRMS_Phase3B_Integration_20260903T180116Z_150524_tenanta',
    'HRMS_Phase3B_Integration_20260903T180116Z_150524_tenantb'
)
$recoveryScriptPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Invoke-Phase3BBrandingRecovery.ps1'))
$recoverySource = Get-Content -LiteralPath $recoveryScriptPath -Raw

function Assert-Test([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "FAILED: $Message" }
}

function Assert-Throws([scriptblock] $Action, [string] $ExpectedText) {
    $threw = $false
    $message = $null
    try { & $Action }
    catch { $threw = $true; $message = $_.Exception.Message }
    Assert-Test $threw "Expected an exception containing '$ExpectedText'."
    Assert-Test ($message -like "*$ExpectedText*") "Expected '$ExpectedText', got '$message'."
}

function Assert-Contains([string] $Text, [string] $Needle, [string] $Message) {
    if ($Text.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw "FAILED: $Message" }
}
function Assert-NotContains([string] $Text, [string] $Needle, [string] $Message) {
    if ($Text.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "FAILED: $Message" }
}
function Assert-Ordered([string] $Text, [string] $First, [string] $Second, [string] $Message) {
    $firstIndex = $Text.IndexOf($First, [StringComparison]::OrdinalIgnoreCase)
    $secondIndex = $Text.IndexOf($Second, [StringComparison]::OrdinalIgnoreCase)
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or $firstIndex -ge $secondIndex) { throw "FAILED: $Message" }
}

Assert-Contains $recoverySource '[switch] $AllowUntrustedServerCertificate' 'The certificate-validation exception is not explicit opt-in.'
Assert-Contains $recoverySource "@('-Nm', '-C')" 'Approved exception does not require mandatory encryption with certificate trust.'
Assert-Contains $recoverySource "@('-Ns')" 'Default strict encryption arguments are missing.'
Assert-Contains $recoverySource 'The certificate-validation exception is restricted to localhost,1433' 'Exception target restriction is missing.'
Assert-NotContains $recoverySource "@('-Ns', '-C')" 'Strict encryption and certificate trust were combined.'
Assert-Ordered $recoverySource 'Assert-Phase3BOwnership' 'function Invoke-Sql' 'SQL invocation is not after ownership validation.'
Write-Output 'PASS recovery encryption opt-in and target restriction'

function New-MetadataFixture([string] $Root) {
    $statePath = Join-Path -Path $Root -ChildPath 'state.json'
    $manifestPath = Join-Path -Path $Root -ChildPath 'state.manifest.json'
    $processPath = Join-Path -Path $Root -ChildPath 'state.processes.json'
    [pscustomobject]@{
        StatePath = $statePath
        ManifestPath = $manifestPath
        State = [ordered]@{ runId = $runId; server = $server; manifestPath = $manifestPath; catalogDatabase = $catalog; tenantDatabases = $tenants }
        Manifest = [ordered]@{ runId = $runId; server = $server; catalogDatabase = $catalog; tenantDatabases = $tenants; databases = @($catalog) + $tenants }
        ProcessState = [pscustomobject]@{ statePath = $statePath; manifestPath = $manifestPath; apiPort = 50089; frontendPort = 57298; apiPid = 1; apiStartTime = 'not-used'; frontendPid = 1; frontendStartTime = 'not-used' }
    }
}

$root = Join-Path -Path ([IO.Path]::GetTempPath()) -ChildPath ('hrms-branding-metadata-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $fixture = New-MetadataFixture $root
    $fixture.State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture.StatePath
    $fixture.Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture.ManifestPath
    $fixture.ProcessState | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'phase3b-browser-state.processes.json')

    Assert-Test ((Get-RequiredPhase3BApiPort $fixture.ProcessState) -eq 50089) 'Authoritative process-state API port was not read.'
    Write-Output 'PASS supported API-port metadata'

    $ownership = Assert-Phase3BOwnership -StatePath $fixture.StatePath -ExpectedServer $server -ExpectedRunId $runId -ExpectedCatalog $catalog -ExpectedTenants $tenants
    Assert-Test ($null -eq $ownership.State.PSObject.Properties['statePath']) 'Current supported state schema unexpectedly requires statePath.'
    Write-Output 'PASS current supported metadata schema'

    $explicit = Resolve-Phase3BStatePath -StatePath $fixture.StatePath -ScriptRoot 'D:\other-working-directory'
    $default = Resolve-Phase3BStatePath -StatePath $null -ScriptRoot $root
    Assert-Test ($explicit -eq [IO.Path]::GetFullPath($fixture.StatePath)) 'Explicit StatePath was not preserved.'
    Assert-Test ($default -eq [IO.Path]::GetFullPath((Join-Path $root 'phase3b-browser-state.json'))) 'Default StatePath was not initialized from script root.'
    Write-Output 'PASS explicit/default StatePath'

    $missing = New-MetadataFixture $root
    $missing.State = [ordered]@{ runId = $runId; server = $server; catalogDatabase = $catalog; tenantDatabases = $tenants }
    $missing.State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $missing.StatePath
    Assert-Throws { Assert-Phase3BOwnership -StatePath $missing.StatePath -ExpectedServer $server -ExpectedRunId $runId -ExpectedCatalog $catalog -ExpectedTenants $tenants } "Required state property 'manifestPath' is missing"
    Write-Output 'PASS missing required properties'

    $fixture.State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture.StatePath
    $fixture.Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture.ManifestPath
    $conflict = New-MetadataFixture $root
    $conflict.Manifest.runId = '20260903T180117Z_150524'
    $conflict.Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $conflict.ManifestPath
    Assert-Throws { Assert-Phase3BOwnership -StatePath $conflict.StatePath -ExpectedServer $server -ExpectedRunId $runId -ExpectedCatalog $catalog -ExpectedTenants $tenants } 'Run ownership does not match'
    $conflict.Manifest['runId'] = $runId
    $conflict.Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $conflict.ManifestPath
    $conflict.State['catalogDatabase'] = 'HRMS_Phase3B_Integration_other_catalog'
    $conflict.State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $conflict.StatePath
    Assert-Throws { Assert-Phase3BOwnership -StatePath $conflict.StatePath -ExpectedServer $server -ExpectedRunId $runId -ExpectedCatalog $catalog -ExpectedTenants $tenants } 'Catalog ownership does not match'
    Write-Output 'PASS conflicting run IDs and database targets'

    $fixture.State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture.StatePath
    $fixture.Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixture.ManifestPath
    Push-Location ([IO.Path]::GetTempPath())
    try {
        $fromAnotherWorkingDirectory = Assert-Phase3BOwnership -StatePath ([IO.Path]::GetFullPath($fixture.StatePath)) -ExpectedServer $server -ExpectedRunId $runId -ExpectedCatalog $catalog -ExpectedTenants $tenants
        Assert-Test ($fromAnotherWorkingDirectory.ManifestPath -eq [IO.Path]::GetFullPath($fixture.ManifestPath)) 'Explicit StatePath failed from another working directory.'
    }
    finally { Pop-Location }
    Write-Output 'PASS invocation from another working directory'
    Write-Output 'ALL_METADATA_TESTS_PASSED'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
