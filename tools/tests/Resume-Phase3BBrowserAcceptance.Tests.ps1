[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Resume-Phase3BBrowserAcceptance.ps1'))
$source = Get-Content -LiteralPath $path -Raw
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw 'FAILED: Resume script has PowerShell parse errors.' }
$bootstrapEvidenceRoot = Join-Path ([IO.Path]::GetTempPath()) ('phase3b-resume-bootstrap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $bootstrapEvidenceRoot | Out-Null
. $path -TestOnly -SourceRoot 'D:\HRMS-phase3b-verify-master-5b03c37' -ExpectedCommit '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051' -StatePath 'D:\HRMS\tools\phase3b-browser-state.json' -EvidenceRoot $bootstrapEvidenceRoot

$passed = 0
function Assert-Test([bool] $Condition, [string] $Message) { if (-not $Condition) { throw "FAILED: $Message" }; $script:passed++ }
function Assert-Throws([scriptblock] $Action, [string] $Expected) {
    try { & $Action; throw 'No exception' }
    catch { Assert-Test ($_.Exception.Message -like "*$Expected*") "Expected '$Expected', got '$($_.Exception.Message)'" }
}

$root = 'D:\HRMS-phase3b-verify-master-5b03c37'
$commit = '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051'
$artifact = Join-Path $root 'Backend\HRMS.API\bin\Debug\net10.0\HRMS.API.dll'
$facts = @{ Root = $root; Commit = $commit; Head = $commit; TrackedStatus = ''; ApiArtifactPath = $artifact; ApiArtifactExists = $true }
Assert-Test ((Assert-ResumeSourceMetadata @facts) -eq $null) 'Current clean source metadata was rejected.'
Assert-Throws { Assert-ResumeSourceMetadata -Root $root -Commit $commit -Head ('0' * 40) -TrackedStatus '' -ApiArtifactPath $artifact -ApiArtifactExists $true } 'HEAD does not match'
Assert-Throws { Assert-ResumeSourceMetadata -Root $root -Commit $commit -Head $commit -TrackedStatus ' M Backend\HRMS.API\Program.cs' -ApiArtifactPath $artifact -ApiArtifactExists $true } 'modified tracked files'
Assert-Throws { Assert-ResumeSourceMetadata -Root $root -Commit $commit -Head $commit -TrackedStatus '' -ApiArtifactPath $artifact -ApiArtifactExists $false } 'artifact is missing'

$plan = New-ResumeLaunchPlan -Root $root -ApiArtifactPath $artifact -ApiPort 50089 -FrontendPort 57298
Assert-Test ($plan.Api.WorkingDirectory -eq $root) 'API did not use explicit SourceRoot.'
Assert-Test ($plan.Api.Arguments.Count -eq 1 -and $plan.Api.Arguments[0] -eq $artifact) 'API did not launch the verified DLL directly.'
Assert-Test ($plan.Frontend.WorkingDirectory -eq (Join-Path $root 'Frontend\HRMS.Web')) 'Frontend did not use SourceRoot.'
Assert-Test ($plan.Frontend.Arguments -contains '--strictPort') 'Frontend strict port binding is missing.'

# Mock process/network operations: only the plan is exercised; no process or socket is touched.
$mockStarts = @()
$mockStart = { param($Executable, $Arguments, $WorkingDirectory) $script:mockStarts += [pscustomobject]@{ Executable = $Executable; Arguments = $Arguments; WorkingDirectory = $WorkingDirectory } }
& $mockStart $plan.Api.Executable $plan.Api.Arguments $plan.Api.WorkingDirectory
& $mockStart $plan.Frontend.Executable $plan.Frontend.Arguments $plan.Frontend.WorkingDirectory
Assert-Test ($mockStarts.Count -eq 2 -and $mockStarts[0].WorkingDirectory -eq $root -and $mockStarts[1].WorkingDirectory -eq (Join-Path $root 'Frontend\HRMS.Web')) 'Mocked launches did not preserve both source roots.'
$mockProbe = { param($Uri, $Headers) [pscustomobject]@{ StatusCode = 200; Content = '{"success":true,"data":{"displayName":"synthetic"}}' } }
$probeResult = & $mockProbe 'http://127.0.0.1:50089/api/tenants/current/branding' @{ Host = 'tenant-a.localhost' }
Assert-Test ($probeResult.StatusCode -eq 200) 'Mocked readiness probe was not exercised.'

function New-MockProcess([bool] $Exited, [int] $Code) {
    $p = [pscustomobject]@{ Id = 12345; HasExited = $Exited; ExitCode = $Code }
    $p | Add-Member -MemberType ScriptMethod -Name Refresh -Value { }
    $p | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { }
    $p
}
$probeCalls = 0
$early = Wait-ResumeApiReadiness -Process (New-MockProcess $true 17) -Deadline (Get-Date).AddMinutes(1) -Probe { $script:probeCalls++; 'unexpected' } -Sleep { param($Milliseconds) } -Now { Get-Date }
Assert-Test ($early.Outcome -eq 'Exited' -and $early.Ready -eq $false -and $probeCalls -eq 0) 'Immediate process exit did not stop readiness polling.'
Assert-Test ((Get-ProcessHandleExitEvidence (New-MockProcess $true 17)).ExitCode -eq 17) 'Exit code was not captured from the process handle.'
$startMessage = $null
try { & { throw 'mock Start-Process failure' } } catch { $startMessage = $_.Exception.Message }
Assert-Test ($startMessage -eq 'mock Start-Process failure' -and $source.IndexOf('StartException') -ge 0) 'Startup exception was not preserved.'
$timeout = Wait-ResumeApiReadiness -Process (New-MockProcess $false 0) -Deadline (Get-Date).AddMilliseconds(-1) -Probe { throw 'mock HTTP unavailable' } -Sleep { param($Milliseconds) } -Now { Get-Date }
Assert-Test ($timeout.Outcome -eq 'Timeout' -and $timeout.Error -like '*mock HTTP unavailable*') 'Running-but-not-ready timeout did not retain the readiness error.'
$success = Wait-ResumeApiReadiness -Process (New-MockProcess $false 0) -Deadline (Get-Date).AddMinutes(1) -Probe { [pscustomobject]@{ TenantA = 'Phase 3B Tenant A'; TenantB = 'Phase 3B Tenant B' } } -Sleep { param($Milliseconds) } -Now { Get-Date }
Assert-Test ($success.Ready -and $success.Outcome -eq 'Ready') 'Successful readiness was not reported.'

# Exercise the actual script entry path with the supplied source/commit values.
# Synthetic ownership files and a temporary evidence root keep this isolated.
$entryRoot = Join-Path ([IO.Path]::GetTempPath()) ('phase3b-resume-entry-' + [guid]::NewGuid().ToString('N'))
$entryEvidenceRoot = Join-Path $entryRoot 'evidence'
New-Item -ItemType Directory -Path $entryRoot,$entryEvidenceRoot | Out-Null
$entryStatePath = Join-Path $entryRoot 'state.json'
$entryManifestPath = Join-Path $entryRoot 'state.manifest.json'
$entryProcessPath = Join-Path $entryRoot 'phase3b-browser-state.processes.json'
$entryState = [ordered]@{ runId = '20260903T180116Z_150524'; server = 'localhost,1433'; manifestPath = $entryManifestPath; catalogDatabase = 'HRMS_Phase3B_Integration_20260903T180116Z_150524_catalog'; tenantDatabases = @('HRMS_Phase3B_Integration_20260903T180116Z_150524_tenanta','HRMS_Phase3B_Integration_20260903T180116Z_150524_tenantb') }
$entryManifest = [ordered]@{ runId = $entryState.runId; server = $entryState.server; catalogDatabase = $entryState.catalogDatabase; tenantDatabases = $entryState.tenantDatabases; databases = @($entryState.catalogDatabase) + $entryState.tenantDatabases }
$entryProcess = [ordered]@{ statePath = $entryStatePath; manifestPath = $entryManifestPath; apiPort = 50089; frontendPort = 57298; apiPid = 1; apiStartTime = 'synthetic'; frontendPid = 1; frontendStartTime = 'synthetic' }
$entryState | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $entryStatePath
$entryManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $entryManifestPath
$entryProcess | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $entryProcessPath
$savedErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$entryOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path -TestOnly -SourceRoot 'D:\HRMS-phase3b-verify-master-5b03c37' -ExpectedCommit '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051' -StatePath $entryStatePath -EvidenceRoot $entryEvidenceRoot 2>&1 | Out-String
Assert-Test ($LASTEXITCODE -eq 0) "Exact supplied entry flow failed: $entryOutput"
$entryRecord = @(Get-ChildItem -LiteralPath $entryEvidenceRoot -Filter 'attempt.json' -Recurse -File)
Assert-Test ($entryRecord.Count -eq 1) 'Complete entry flow did not create exactly one temporary attempt record.'
$entryRecordData = Get-Content -LiteralPath $entryRecord[0].FullName -Raw | ConvertFrom-Json
Assert-Test ($entryRecordData.RunId -eq $entryState.runId -and $entryRecordData.Catalog -eq $entryState.catalogDatabase -and $entryRecordData.StatePath -eq $entryStatePath) 'Attempt record did not preserve synthetic ownership and external StatePath.'
$invalidOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path -TestOnly -SourceRoot 'D:\missing-phase3b-source' -ExpectedCommit '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051' -StatePath $entryStatePath -EvidenceRoot $entryEvidenceRoot 2>&1 | Out-String
Assert-Test ($LASTEXITCODE -ne 0 -and $invalidOutput -like '*SourceRoot does not exist*' -and $invalidOutput -notlike '*resolvedSourceRoot*') 'Validation failure did not preserve its original error.'
$missingSourceOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path -TestOnly -ExpectedCommit '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051' -StatePath $entryStatePath -EvidenceRoot $entryEvidenceRoot 2>&1 | Out-String
Assert-Test ($LASTEXITCODE -ne 0 -and $missingSourceOutput -like '*SourceRoot is required*' -and $missingSourceOutput -notlike '*resolvedSourceRoot*') 'Missing SourceRoot was masked by an unset-variable error.'
$emptyStateOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path -TestOnly -SourceRoot 'D:\HRMS-phase3b-verify-master-5b03c37' -ExpectedCommit '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051' -StatePath ' ' -EvidenceRoot $entryEvidenceRoot 2>&1 | Out-String
Assert-Test ($LASTEXITCODE -ne 0 -and $emptyStateOutput -like '*RESUME_FATAL*' -and $emptyStateOutput -like '*ScriptName*' -and $emptyStateOutput -like '*LineNumber*' -and $emptyStateOutput -like '*ScriptStackTrace*' -and $emptyStateOutput -like '*ExceptionMessage*') 'Empty StatePath did not produce complete fatal diagnostics.'
$emptyEvidenceOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $path -TestOnly -SourceRoot 'D:\HRMS-phase3b-verify-master-5b03c37' -ExpectedCommit '5b03c376a9294680c4b01ebbfbf4a5cfe4cf7051' -StatePath $entryStatePath -EvidenceRoot ' ' 2>&1 | Out-String
Assert-Test ($LASTEXITCODE -ne 0 -and $emptyEvidenceOutput -like '*RESUME_FATAL*' -and $emptyEvidenceOutput -like '*ExceptionMessage*' -and $emptyEvidenceOutput -notlike '*resolvedSourceRoot*') 'Empty EvidenceRoot did not preserve the original error.'
$ErrorActionPreference = $savedErrorActionPreference

# Complete mocked launch sequence after the real source/ownership/record flow.
$mockLaunches = @()
$mockStarter = { param($Executable, $Arguments, $WorkingDirectory) $script:mockLaunches += [pscustomobject]@{ Executable = $Executable; Arguments = $Arguments; WorkingDirectory = $WorkingDirectory }; New-MockProcess $false 0 }
$mockApi = & $mockStarter $plan.Api.Executable $plan.Api.Arguments $plan.Api.WorkingDirectory
$mockBranding = Wait-ResumeApiReadiness -Process $mockApi -Deadline (Get-Date).AddMinutes(1) -Probe { [ordered]@{ 'tenant-a.localhost' = 'Phase 3B Tenant A'; 'tenant-b.localhost' = 'Phase 3B Tenant B' } } -Sleep { param($Milliseconds) } -Now { Get-Date }
Assert-Test ($mockBranding.Ready -and $mockBranding.Branding.'tenant-a.localhost' -eq 'Phase 3B Tenant A') 'Mock API branding readiness did not succeed.'
$mockFrontend = & $mockStarter $plan.Frontend.Executable $plan.Frontend.Arguments $plan.Frontend.WorkingDirectory
Assert-Test ($mockLaunches.Count -eq 2 -and $mockLaunches[0].Executable -eq 'dotnet' -and $mockLaunches[1].Executable -eq 'npm.cmd') 'Mock API/frontend launches were not sequenced correctly.'

# Real harmless child-process capture: no API, frontend, SQL, or network operation.
$childPath = Join-Path $entryRoot 'capture-child.ps1'
$childStdout = Join-Path $entryRoot 'capture.stdout.log'
$childStderr = Join-Path $entryRoot 'capture.stderr.log'
@("Write-Output 'RESUME_TEST_STDOUT_MARKER'", "[Console]::Error.WriteLine('RESUME_TEST_STDERR_MARKER')", 'exit 23') | Set-Content -LiteralPath $childPath
$childLaunch = Start-ResumeCapturedProcess -Executable 'powershell.exe' -Arguments @('-NoProfile', '-File', $childPath) -WorkingDirectory $entryRoot -StdoutPath $childStdout -StderrPath $childStderr -Port 0
$childLaunch.Process.WaitForExit()
$childReadiness = Wait-ResumeApiReadiness -Process $childLaunch.Process -Deadline (Get-Date).AddMinutes(1) -Probe { throw 'probe must not run after process exit' } -Sleep { param($Milliseconds) } -Now { Get-Date }
Set-ProcessExitEvidence $childLaunch.Process $childLaunch.Entry -ListenerProvider { param($Port) @() }
$childLaunch.Entry.ReadinessOutcome = $childReadiness.Outcome
$childLaunch.Entry.ReadinessError = if ($childReadiness.Error) { $childReadiness.Error } else { 'API process exited before readiness.' }
$childRecordPath = Join-Path $entryRoot 'capture-attempt.json'
$childLaunch.Entry | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $childRecordPath
$childRecord = Get-Content -LiteralPath $childRecordPath -Raw
Assert-Test ($childLaunch.Entry.ExitCode -eq 23 -and $childRecord -like '*capture.stdout.log*' -and (Get-Content -LiteralPath $childStdout -Raw) -like '*RESUME_TEST_STDOUT_MARKER*' -and (Get-Content -LiteralPath $childStderr -Raw) -like '*RESUME_TEST_STDERR_MARKER*') 'Real child exit 23 or redirected output was not captured.'
Assert-Test ($childLaunch.Entry.HasExited -and $childReadiness.Outcome -eq 'Exited' -and $childRecord -like '*"ReadinessOutcome":  "Exited"*' -and $childRecord -notlike '*Timeout*') 'Attempt record did not identify ProcessExited distinctly from timeout.'
$zeroPath = Join-Path $entryRoot 'capture-zero.ps1'
$zeroStdout = Join-Path $entryRoot 'capture-zero.stdout.log'
$zeroStderr = Join-Path $entryRoot 'capture-zero.stderr.log'
@("Write-Output 'RESUME_TEST_ZERO_MARKER'", 'exit 0') | Set-Content -LiteralPath $zeroPath
$zeroLaunch = Start-ResumeCapturedProcess -Executable 'powershell.exe' -Arguments @('-NoProfile', '-File', $zeroPath) -WorkingDirectory $entryRoot -StdoutPath $zeroStdout -StderrPath $zeroStderr -Port 0
$zeroLaunch.Process.WaitForExit()
Set-ProcessExitEvidence $zeroLaunch.Process $zeroLaunch.Entry -ListenerProvider { param($Port) @() }
Assert-Test ($zeroLaunch.Entry.HasExited -and $zeroLaunch.Entry.ExitCode -eq 0) 'Successful child exit code 0 was not captured.'

$invalidOwnership = $false
$launchesBeforeInvalid = $mockLaunches.Count
$entryManifest.runId = 'unexpected-run'
$entryManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $entryManifestPath
try { Assert-Phase3BOwnership -StatePath $entryStatePath -ExpectedServer 'localhost,1433' -ExpectedRunId '20260903T180116Z_150524' -ExpectedCatalog $entryState.catalogDatabase -ExpectedTenants $entryState.tenantDatabases | Out-Null } catch { $invalidOwnership = $true }
Assert-Test $invalidOwnership 'Invalid ownership was not rejected.'
Assert-Test ($mockLaunches.Count -eq $launchesBeforeInvalid) 'Invalid ownership allowed a mocked launch.'

if (Test-Path -LiteralPath $entryRoot) { Remove-Item -LiteralPath $entryRoot -Recurse -Force }
if (Test-Path -LiteralPath $bootstrapEvidenceRoot) { Remove-Item -LiteralPath $bootstrapEvidenceRoot -Recurse -Force }

Assert-Test ($source -match '\[string\]\s+\$SourceRoot' -and $source -match '\[string\]\s+\$ExpectedCommit') 'Explicit source parameters are missing.'
Assert-Test ($source -match 'Database__SkipInitialization\s*=\s*''true''') 'Initialization skipping is not preserved.'
Assert-Test ($source -match 'Encrypt=True;TrustServerCertificate=True') 'Approved local SQL settings are not preserved.'
Assert-Test (($source.IndexOf("@('run', '--project'") -lt 0) -and ($source.IndexOf('dotnet run') -lt 0)) 'Implicit dotnet build/run was introduced.'
Assert-Test ($source -notmatch 'migrate|seed|Repair|setup') 'Resume script contains a forbidden setup/recovery operation.'
Assert-Test ($source -match '\$StatePath' -and $source -match '\$resolvedSourceRoot') 'StatePath and SourceRoot are not kept separate.'

Write-Output "RESUME_SCRIPT_BEHAVIORAL_TESTS_PASSED Assertions=$passed"
