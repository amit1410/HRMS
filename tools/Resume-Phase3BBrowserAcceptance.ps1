[CmdletBinding()]
param(
    [string] $StatePath = (Join-Path $PSScriptRoot 'phase3b-browser-state.json'),
    [string] $SourceRoot,
    [string] $ExpectedCommit,
    [string] $EvidenceRoot = $PSScriptRoot,
    [switch] $TestOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath 'Phase3BRecoveryMetadata.ps1')

function ConvertTo-ResumeSafeText([object] $Value) {
    ([string]$Value) -replace '(?i)(password|secret|token|connectionstring)\s*[=:]\s*[^;\s]+', '$1=<redacted>'
}
function Write-ResumeFatalDiagnostic([object] $ErrorRecord) {
    try {
        $invocation = $ErrorRecord.InvocationInfo
        $scriptName = if ($null -ne $invocation) { [string]$invocation.ScriptName } else { '' }
        $lineNumber = if ($null -ne $invocation) { [string]$invocation.ScriptLineNumber } else { '' }
        $stack = ConvertTo-ResumeSafeText $ErrorRecord.ScriptStackTrace
        $message = ConvertTo-ResumeSafeText $ErrorRecord.Exception.Message
        [Console]::Error.WriteLine(('RESUME_FATAL ScriptName="{0}" LineNumber="{1}" ScriptStackTrace="{2}" ExceptionMessage="{3}"' -f $scriptName, $lineNumber, $stack, $message))
    }
    catch {
        [Console]::Error.WriteLine('RESUME_FATAL DiagnosticCaptureFailed; original error will be rethrown.')
    }
}
trap {
    $originalError = $_
    Write-ResumeFatalDiagnostic $originalError
    throw
}

$ExpectedServer = 'localhost,1433'
$ExpectedRunId = '20260903T180116Z_150524'
$ExpectedCatalog = 'HRMS_Phase3B_Integration_20260903T180116Z_150524_catalog'
$ExpectedTenants = @(
    'HRMS_Phase3B_Integration_20260903T180116Z_150524_tenanta',
    'HRMS_Phase3B_Integration_20260903T180116Z_150524_tenantb'
)
$ExpectedApiPort = 50089
$ExpectedFrontendPort = 57298
$Fixture = @(
    [pscustomobject]@{ Host = 'tenant-a.localhost'; DisplayName = 'Phase 3B Tenant A' },
    [pscustomobject]@{ Host = 'tenant-b.localhost'; DisplayName = 'Phase 3B Tenant B' }
)

function Stop-Resume([string] $Message) { throw "Phase 3B resume stopped: $Message" }

function Assert-ResumeSourceMetadata {
    param([string] $Root, [string] $Commit, [string] $Head, [string] $TrackedStatus, [string] $ApiArtifactPath, [bool] $ApiArtifactExists)
    if ([string]::IsNullOrWhiteSpace($Root)) { throw 'SourceRoot is required; no fallback source is permitted.' }
    if ([string]::IsNullOrWhiteSpace($Commit) -or $Commit -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedCommit must be a full 40-character commit SHA.' }
    if ([string]::IsNullOrWhiteSpace($Head) -or $Head.Trim() -cne $Commit.Trim()) { throw "SourceRoot HEAD does not match ExpectedCommit '$Commit'." }
    if (-not [string]::IsNullOrWhiteSpace($TrackedStatus)) { throw 'SourceRoot has modified tracked files; launch stopped.' }
    if (-not $ApiArtifactExists) { throw "Verified API artifact is missing: $ApiArtifactPath" }
}

function Get-ResumeSourceFacts([string] $Root) {
    $head = (& git -C $Root rev-parse HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "SourceRoot is not a readable Git checkout: $Root" }
    $trackedStatus = (& git -C $Root status --porcelain --untracked-files=no 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect tracked source status for SourceRoot: $Root" }
    $apiArtifactPath = Join-Path $Root 'Backend\HRMS.API\bin\Debug\net10.0\HRMS.API.dll'
    [pscustomobject]@{ Root = $Root; Head = $head; TrackedStatus = $trackedStatus; ApiArtifactPath = $apiArtifactPath; ApiArtifactExists = (Test-Path -LiteralPath $apiArtifactPath -PathType Leaf) }
}

function New-ResumeLaunchPlan {
    param([string] $Root, [string] $ApiArtifactPath, [int] $ApiPort, [int] $FrontendPort)
    $frontendRoot = Join-Path $Root 'Frontend\HRMS.Web'
    if (-not (Test-Path -LiteralPath $frontendRoot -PathType Container) -or -not (Test-Path -LiteralPath (Join-Path $frontendRoot 'package.json') -PathType Leaf)) { throw "Frontend source is missing from SourceRoot: $frontendRoot" }
    [pscustomobject]@{
        Api = [ordered]@{ Executable = 'dotnet'; Arguments = @($ApiArtifactPath); WorkingDirectory = $Root; Port = $ApiPort }
        Frontend = [ordered]@{ Executable = 'npm.cmd'; Arguments = @('run', 'dev', '--', '--host', '127.0.0.1', '--port', [string]$FrontendPort, '--strictPort'); WorkingDirectory = $frontendRoot; Port = $FrontendPort }
    }
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) { Stop-Resume 'SourceRoot is required; no fallback source is permitted.' }
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) { Stop-Resume 'ExpectedCommit is required.' }
$resolvedSourceRoot = [IO.Path]::GetFullPath($SourceRoot)
if (-not (Test-Path -LiteralPath $resolvedSourceRoot -PathType Container)) { Stop-Resume "SourceRoot does not exist: $resolvedSourceRoot" }
$sourceFacts = Get-ResumeSourceFacts $resolvedSourceRoot
try { Assert-ResumeSourceMetadata -Root $resolvedSourceRoot -Commit $ExpectedCommit -Head $sourceFacts.Head -TrackedStatus $sourceFacts.TrackedStatus -ApiArtifactPath $sourceFacts.ApiArtifactPath -ApiArtifactExists $sourceFacts.ApiArtifactExists } catch { Stop-Resume $_.Exception.Message }
$apiArtifactHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceFacts.ApiArtifactPath).Hash
if ([string]::IsNullOrWhiteSpace($StatePath)) { Stop-Resume 'StatePath is required and cannot be empty.' }
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { Stop-Resume 'EvidenceRoot is required and cannot be empty.' }

$resolvedStatePath = [IO.Path]::GetFullPath($StatePath)
try {
    $ownership = Assert-Phase3BOwnership -StatePath $resolvedStatePath -ExpectedServer $ExpectedServer -ExpectedRunId $ExpectedRunId -ExpectedCatalog $ExpectedCatalog -ExpectedTenants $ExpectedTenants
}
catch { Stop-Resume $_.Exception.Message }

$processState = $ownership.ProcessState
$apiPort = Get-RequiredPhase3BApiPort $processState
$frontendPortProperty = $processState.PSObject.Properties['frontendPort']
if ($null -eq $frontendPortProperty -or $null -eq $frontendPortProperty.Value) { Stop-Resume "Required process-state property 'frontendPort' is missing." }
$frontendPort = 0
if (-not [int]::TryParse([string]$frontendPortProperty.Value, [ref]$frontendPort) -or $frontendPort -lt 1024 -or $frontendPort -gt 65535) { Stop-Resume "Required process-state property 'frontendPort' must be a valid TCP port (1024-65535)." }
if ($apiPort -ne $ExpectedApiPort -or $frontendPort -ne $ExpectedFrontendPort) { Stop-Resume 'Recorded acceptance ports do not match the authorized ports.' }

$attemptId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$evidenceDirectory = Join-Path $EvidenceRoot ('phase3b-resume-attempt-' + $attemptId)
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$recordPath = Join-Path $evidenceDirectory 'attempt.json'
$apiStdoutPath = Join-Path $evidenceDirectory 'api.stdout.log'
$apiStderrPath = Join-Path $evidenceDirectory 'api.stderr.log'
$frontendStdoutPath = Join-Path $evidenceDirectory 'frontend.stdout.log'
$frontendStderrPath = Join-Path $evidenceDirectory 'frontend.stderr.log'
$record = [ordered]@{
    AttemptId = $attemptId
    StartedUtc = [DateTime]::UtcNow.ToString('O')
    StatePath = $resolvedStatePath
    ManifestPath = $ownership.ManifestPath
    RunId = $ExpectedRunId
    Server = $ExpectedServer
    Catalog = $ExpectedCatalog
    TenantDatabases = $ExpectedTenants
    Ports = [ordered]@{ Api = $apiPort; Frontend = $frontendPort }
    Configuration = [ordered]@{ Environment = 'Development'; Provider = 'SqlServer'; SkipInitialization = $true; IntegratedAuthentication = $true; Encryption = 'Mandatory'; CertificateValidation = 'Approved local trust exception for this exact recovery connection only' }
    Source = [ordered]@{ Root = $resolvedSourceRoot; Commit = $ExpectedCommit; ApiArtifactPath = $sourceFacts.ApiArtifactPath; ApiArtifactSha256 = $apiArtifactHash }
    Api = $null
    Frontend = $null
    Status = 'Prepared'
    Failure = $null
}
function Write-ResumeRecord { $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding UTF8 -NoNewline }
function Get-ListenerEvidence([int] $Port) {
    @((Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue) | ForEach-Object { [ordered]@{ LocalAddress = $_.LocalAddress; LocalPort = $_.LocalPort; State = $_.State.ToString(); OwningProcess = $_.OwningProcess } })
}
function Get-ProcessEvidence([int] $ProcessId) {
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    $evidence = [ordered]@{ Pid = $process.Id; StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O'); Name = $process.ProcessName; Path = $process.Path; HasExited = $process.HasExited }
    try {
        $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction Stop
        if ($cim) { $evidence.ParentPid = [int]$cim.ParentProcessId }
    }
    catch { $evidence.ParentPid = $null }
    return $evidence
}
function Set-ProcessExitEvidence($Process, [object] $Entry, [scriptblock] $ListenerProvider = { param($Port) Get-ListenerEvidence $Port }) {
    if ($null -eq $Process) { return }
    try {
        $Process.Refresh()
        $handleEvidence = Get-ProcessHandleExitEvidence $Process
        $Entry.HasExited = $handleEvidence.HasExited
        if ($handleEvidence.PSObject.Properties['ExitCode']) { $Entry.ExitCode = $handleEvidence.ExitCode }
        if ($handleEvidence.PSObject.Properties['ExitCodeError']) { $Entry.ExitCodeError = $handleEvidence.ExitCodeError }
    }
    catch { $Entry.ProcessStateError = $_.Exception.Message }
    $Entry.ObservedUtc = [DateTime]::UtcNow.ToString('O')
    if ($Entry.StartedUtc) {
        try { $Entry.ElapsedMilliseconds = ([DateTime]::Parse($Entry.ObservedUtc).ToUniversalTime() - [DateTime]::Parse($Entry.StartedUtc).ToUniversalTime()).TotalMilliseconds } catch { }
    }
    $Entry.ListenerEvidence = @(& $ListenerProvider $Entry.Port)
    $Entry.Logs = [ordered]@{ Stdout = Get-LogEvidence $Entry.StdoutLog; Stderr = Get-LogEvidence $Entry.StderrLog }
    if ($Entry.HasExited) {
        try { $Process.Close(); $Process.Dispose() } catch { $Entry.ResourceCleanupError = $_.Exception.Message }
    }
}
function Get-ProcessHandleExitEvidence($Process) {
    $result = [ordered]@{ HasExited = [bool]$Process.HasExited }
    if ($result.HasExited) {
        try { $Process.WaitForExit(); Close-ResumeOutputCapture $Process; $result.ExitCode = $Process.ExitCode }
        catch { $result.ExitCodeError = $_.Exception.Message }
    }
    [pscustomobject]$result
}
function Get-LogEvidence([string] $Path) {
    $item = [ordered]@{ Path = $Path; Exists = $false; SizeBytes = $null; LastWriteTimeUtc = $null }
    try {
        $file = Get-Item -LiteralPath $Path -ErrorAction Stop
        $item.Exists = $true
        $item.SizeBytes = [int64]$file.Length
        $item.LastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('O')
    }
    catch { $item.Error = $_.Exception.Message }
    return $item
}
function Start-ResumeCapturedProcess {
    param([string] $Executable, [object[]] $Arguments, [string] $WorkingDirectory, [string] $StdoutPath, [string] $StderrPath, [int] $Port)
    $entry = [ordered]@{ Executable = $Executable; Arguments = @($Arguments); WorkingDirectory = $WorkingDirectory; StdoutLog = $StdoutPath; StderrLog = $StderrPath; Port = $Port; StartedUtc = [DateTime]::UtcNow.ToString('O'); Process = $null; ExitCode = $null; ListenerEvidence = @() }
    $stdoutStream = $null
    $stderrStream = $null
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $Executable
        $startInfo.Arguments = (($Arguments | ForEach-Object { '"' + ([string]$_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }) -join ' ')
        $startInfo.WorkingDirectory = $WorkingDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $stdoutStream = New-Object System.IO.FileStream($StdoutPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $stderrStream = New-Object System.IO.FileStream($StderrPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw 'Process.Start returned false.' }
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutStream)
        $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrStream)
        $process | Add-Member -MemberType NoteProperty -Name ResumeStdoutStream -Value $stdoutStream
        $process | Add-Member -MemberType NoteProperty -Name ResumeStderrStream -Value $stderrStream
        $process | Add-Member -MemberType NoteProperty -Name ResumeStdoutTask -Value $stdoutTask
        $process | Add-Member -MemberType NoteProperty -Name ResumeStderrTask -Value $stderrTask
        $entry.Process = Get-ProcessEvidence $process.Id
        [pscustomobject]@{ Process = $process; Entry = $entry }
    }
    catch {
        if ($stdoutStream) { try { $stdoutStream.Dispose() } catch { } }
        if ($stderrStream) { try { $stderrStream.Dispose() } catch { } }
        $entry.StartException = $_.Exception.Message
        $entry.Logs = [ordered]@{ Stdout = Get-LogEvidence $StdoutPath; Stderr = Get-LogEvidence $StderrPath }
        throw
    }
}
function Close-ResumeOutputCapture($Process) {
    foreach($propertyName in @('ResumeStdoutTask','ResumeStderrTask')) {
        $property = $Process.PSObject.Properties[$propertyName]
        if ($property -and $property.Value) { try { $property.Value.Wait() } catch { } }
    }
    foreach($propertyName in @('ResumeStdoutStream','ResumeStderrStream')) {
        $property = $Process.PSObject.Properties[$propertyName]
        if ($property -and $property.Value) { try { $property.Value.Flush(); $property.Value.Dispose() } catch { } }
    }
}
function Wait-ResumeApiReadiness {
    param([object] $Process, [datetime] $Deadline, [scriptblock] $Probe, [scriptblock] $Sleep = { param($Milliseconds) Start-Sleep -Milliseconds $Milliseconds }, [scriptblock] $Now = { Get-Date })
    $lastError = $null
    do {
        & $Sleep 500
        try { $Process.Refresh() } catch { $lastError = $_.Exception.Message }
        try {
            if ($Process.HasExited) { return [pscustomobject]@{ Ready = $false; Outcome = 'Exited'; Error = $lastError } }
        }
        catch { return [pscustomobject]@{ Ready = $false; Outcome = 'ProcessStateError'; Error = $_.Exception.Message } }
        try {
            $result = & $Probe
            if ($null -ne $result) { return [pscustomobject]@{ Ready = $true; Outcome = 'Ready'; Branding = $result; Error = $null } }
        }
        catch { $lastError = $_.Exception.Message }
    } while ((& $Now) -lt $Deadline)
    [pscustomobject]@{ Ready = $false; Outcome = 'Timeout'; Error = $lastError }
}
function Invoke-BrandingCheck([int] $Port) {
    $results = [ordered]@{}
    foreach ($item in $Fixture) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri "http://127.0.0.1:$Port/api/tenants/current/branding" -Headers @{ Host = $item.Host }
            $payload = $response.Content | ConvertFrom-Json
            if ($response.StatusCode -ne 200 -or -not $payload.success -or [string]::IsNullOrWhiteSpace([string]$payload.data.displayName)) { Stop-Resume "Branding response was not a successful nonempty response for $($item.Host)." }
            if ([string]$payload.data.displayName -cne $item.DisplayName) { Stop-Resume "Branding display name mismatch for $($item.Host)." }
            $results[$item.Host] = [ordered]@{ HttpStatus = $response.StatusCode; Success = [bool]$payload.success; DisplayName = [string]$payload.data.displayName }
        }
        catch { Stop-Resume $_.Exception.Message }
    }
    return $results
}

if ($TestOnly) { Write-ResumeRecord; return }

Write-ResumeRecord
if (@(Get-NetTCPConnection -LocalPort $apiPort -ErrorAction SilentlyContinue).Count -gt 0 -or @(Get-NetTCPConnection -LocalPort $frontendPort -ErrorAction SilentlyContinue).Count -gt 0) {
    $record.Status = 'PORT_OCCUPIED'
    $record.Failure = 'A recorded acceptance port is occupied; no process was launched.'
    Write-ResumeRecord
    Stop-Resume $record.Failure
}

$repoRoot = $resolvedSourceRoot
$launchPlan = New-ResumeLaunchPlan -Root $repoRoot -ApiArtifactPath $sourceFacts.ApiArtifactPath -ApiPort $apiPort -FrontendPort $frontendPort
$apiArgs = $launchPlan.Api.Arguments
$template = "Server=$($ownership.State.server);Database=HRMS_Phase3B_Integration_$ExpectedRunId`_{shardKey};Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$catalog = "Server=$($ownership.State.server);Database=$($ownership.State.catalogDatabase);Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$apiPort"
$env:Database__Provider = 'SqlServer'
$env:Database__SkipInitialization = 'true'
$env:ConnectionStrings__Catalog = $catalog
$env:Sharding__ConnectionStringTemplate = $template
$env:Cors__AllowedOrigins__0 = "http://localhost:$frontendPort"
$env:Cors__WorkspaceOriginTemplates__0 = "http://{workspace}.localhost:$frontendPort"
$env:Jwt__SecretKey = 'Phase3B-local-only-do-not-reuse-32-bytes-minimum'
$apiProcess = $null
$frontendProcess = $null
try {
    $apiLaunch = Start-ResumeCapturedProcess -Executable $launchPlan.Api.Executable -Arguments $apiArgs -WorkingDirectory $launchPlan.Api.WorkingDirectory -StdoutPath $apiStdoutPath -StderrPath $apiStderrPath -Port $apiPort
    $apiProcess = $apiLaunch.Process
    $record.Api = $apiLaunch.Entry
    $record.Api.Readiness = 'Pending'
    $record.Status = 'API_LAUNCHED'
    Write-ResumeRecord
    $readiness = Wait-ResumeApiReadiness -Process $apiProcess -Deadline (Get-Date).AddSeconds(45) -Probe { Invoke-BrandingCheck $apiPort }
    if (-not $readiness.Ready) {
        $record.Api.Readiness = 'Failed'
        $record.Api.ReadinessOutcome = $readiness.Outcome
        $record.Api.ReadinessError = if ($readiness.Error) { $readiness.Error } elseif ($readiness.Outcome -eq 'Exited') { 'API process exited before readiness.' } else { 'API did not become ready before the timeout.' }
        Set-ProcessExitEvidence $apiProcess $record.Api
        $record.Status = if ($readiness.Outcome -eq 'Exited') { 'API_PROCESS_EXITED' } else { 'API_NOT_READY' }
        $record.Failure = "API readiness failed ($($readiness.Outcome)); inspect the captured API stdout/stderr logs."
        Write-ResumeRecord
        Stop-Resume $record.Failure
    }
    $record.Api.Readiness = 'Ready'
    $record.Api.Branding = $readiness.Branding
    $record.Api.ListenerEvidence = @(Get-ListenerEvidence $apiPort)
    Write-ResumeRecord

    $env:VITE_API_BASE_URL = "http://localhost:$apiPort"
    $env:VITE_API_ORIGIN_TEMPLATE = "http://{workspace}.localhost:$apiPort"
    $env:VITE_API_CSP_CONNECT_SRC = "http://tenant-a.localhost:$apiPort http://tenant-b.localhost:$apiPort"
    $frontendArgs = @('run', 'dev', '--', '--host', '127.0.0.1', '--port', [string]$frontendPort, '--strictPort')
    $frontendLaunch = Start-ResumeCapturedProcess -Executable $launchPlan.Frontend.Executable -Arguments $launchPlan.Frontend.Arguments -WorkingDirectory $launchPlan.Frontend.WorkingDirectory -StdoutPath $frontendStdoutPath -StderrPath $frontendStderrPath -Port $frontendPort
    $frontendProcess = $frontendLaunch.Process
    $record.Frontend = $frontendLaunch.Entry
    $record.Frontend.ApiBaseUrl = "http://localhost:$apiPort"
    $record.Frontend.ApiOriginTemplate = "http://{workspace}.localhost:$apiPort"
    $record.Frontend.CspConnectSrc = @("http://tenant-a.localhost:$apiPort", "http://tenant-b.localhost:$apiPort")
    $record.Status = 'FRONTEND_LAUNCHED'
    Write-ResumeRecord
    $webDeadline = (Get-Date).AddSeconds(30)
    $frontendChecks = [ordered]@{}
    do {
        Start-Sleep -Milliseconds 500
        foreach ($item in $Fixture) {
            try {
                $web = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri "http://$($item.Host):$frontendPort/"
                $requiredCsp = "connect-src 'self' http://tenant-a.localhost:$apiPort http://tenant-b.localhost:$apiPort;"
                if ($web.StatusCode -ne 200 -or $web.Content -notlike "*$requiredCsp*") { Stop-Resume "Frontend response/CSP verification failed for $($item.Host)." }
                $frontendChecks[$item.Host] = [ordered]@{ HttpStatus = $web.StatusCode; CspConnectSrc = $requiredCsp }
            }
            catch { }
        }
    } while ($frontendChecks.Count -lt $Fixture.Count -and (Get-Date) -lt $webDeadline)
    if ($frontendChecks.Count -lt $Fixture.Count) { $record.Status = 'FRONTEND_NOT_READY'; $record.Failure = 'Frontend did not become ready with the recorded port/origins/CSP.'; Set-ProcessExitEvidence $frontendProcess $record.Frontend; Write-ResumeRecord; Stop-Resume $record.Failure }
    $record.Frontend.Readiness = 'Ready'
    $record.Frontend.Checks = $frontendChecks
    $record.Frontend.ListenerEvidence = @(Get-ListenerEvidence $frontendPort)
    $record.Status = 'READY'
    Write-ResumeRecord
    Write-Output "Phase 3B resume ready. Evidence: $recordPath"
    foreach ($item in $Fixture) { Write-Output "Browser URL: http://$($item.Host):$frontendPort" }
}
catch {
    $failure = $_.Exception.Message
    if ($record.Status -eq 'Prepared') { $record.Status = 'LAUNCH_FAILED' }
    if ($null -eq $record.Failure) { $record.Failure = $failure }
    if ($record.Api) { Set-ProcessExitEvidence $apiProcess $record.Api }
    if ($record.Frontend) { Set-ProcessExitEvidence $frontendProcess $record.Frontend }
    Write-ResumeRecord
    throw
}
