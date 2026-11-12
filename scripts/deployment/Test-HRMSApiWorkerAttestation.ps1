[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Normal', 'Import')]
    [string]$Profile,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 65535)]
    [int]$Port
)

$ErrorActionPreference = 'Stop'
$snapshotResolver = Join-Path $PSScriptRoot 'Resolve-HRMSApiListenerSnapshot.ps1'
. $snapshotResolver
$cimStatus = 'success'
$listenerRecords = @()
try {
    # Filter socket records by state before counting. Established/closing connections are
    # deliberately irrelevant; multiple address-family records from one PID are one owner.
    $listenerRecords = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction Stop |
        Where-Object { $_.State -eq 'Listen' })
}
catch {
    $cimStatus = Get-HRMSCimListenerQueryStatus -FullyQualifiedErrorId ([string]$_.FullyQualifiedErrorId)
}

$netstatSucceeded = $false
$netstatRecords = @()
try {
    $netstat = Join-Path $env:SystemRoot 'System32\netstat.exe'
    $netstatLines = @(& $netstat -ano -p tcp 2>$null)
    $netstatSucceeded = ($LASTEXITCODE -eq 0)
    if ($netstatSucceeded) {
        $netstatRecords = @(ConvertFrom-HRMSNetstatListenerLines -Lines $netstatLines)
    }
} catch { $netstatSucceeded = $false }

$listenerSnapshot = Resolve-HRMSApiListenerSnapshot -Port $Port -CimStatus $cimStatus `
    -CimRecords $listenerRecords -NetstatSucceeded $netstatSucceeded -NetstatRecords $netstatRecords
if ($null -eq $listenerSnapshot.ProcessId) {
    throw ("{0}: Listener ownership could not be established." -f $listenerSnapshot.Category)
}
$pidValue = [int]$listenerSnapshot.ProcessId
try {
    $process = Get-Process -Id $pidValue -ErrorAction Stop
    $startIdentity = $process.StartTime.ToUniversalTime().ToFileTimeUtc()
}
catch {
    throw 'listener.process_identity_unavailable: Listener owner process identity could not be verified.'
}

$secureToken = Read-Host 'WEB1 Employee.Import bearer token (hidden)' -AsSecureString
$tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
$token = $null
$random = $null
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $nonceBytes = New-Object byte[] 32
    $random.GetBytes($nonceBytes)
    $challenge = [BitConverter]::ToString($nonceBytes).Replace('-', '')
    $headers = @{
        Authorization = "Bearer $token"
        Host = 'web1.localhost'
        'X-HRMS-Attestation-Challenge' = $challenge
    }
    $uri = "http://127.0.0.1:$Port/api/deployment/worker-state?profile=$($Profile.ToLowerInvariant())"
    $valid = $false
    $report = $null
    foreach ($attempt in 1..3) {
        $challenge = [BitConverter]::ToString($nonceBytes).Replace('-', '')
        $headers['X-HRMS-Attestation-Challenge'] = $challenge
        try {
            $response = Invoke-WebRequest -Method Get -Uri $uri -Headers $headers -UseBasicParsing -TimeoutSec 5
            $candidate = $response.Content | ConvertFrom-Json
            $echoedChallenge = $response.Headers['X-HRMS-Attestation-Response']
            if ([int]$candidate.processId -eq $pidValue -and
                [long]$candidate.processStartIdentity -eq [long]$startIdentity -and
                [string]$echoedChallenge -ceq $challenge) {
                $report = $candidate
                $valid = [bool]$candidate.attestationValid
                break
            }
        } catch { }
        $random.GetBytes($nonceBytes)
    }
    $random.Dispose()

    [pscustomobject]@{
        PID = $pidValue
        ProcessStartIdentity = if ($report) { [long]$report.processStartIdentity } else { [long]$startIdentity }
        EmployeeBulkImportWorkerEnabled = if ($report) { [bool]$report.employeeBulkImportWorkerEnabled } else { $null }
        AttestationValid = $valid
        ListenerDiscoveryDiagnostic = $listenerSnapshot.Category
    } | Format-List
    if (-not $valid) { exit 2 }
}
catch {
    [pscustomobject]@{
        PID = $pidValue
        ProcessStartIdentity = [long]$startIdentity
        EmployeeBulkImportWorkerEnabled = $null
        AttestationValid = $false
    } | Format-List
    exit 2
}
finally {
    if ($random) { $random.Dispose() }
    $headers = $null
    $token = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer)
    $secureToken.Dispose()
}
