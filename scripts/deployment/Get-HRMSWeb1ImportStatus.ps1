[CmdletBinding()]
param(
    [ValidateSet(5080, 5090)]
    [int]$Port = 5090
)

$ErrorActionPreference = 'Stop'
$secureToken = Read-Host 'WEB1 Employee.Import bearer token (hidden)' -AsSecureString
$tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
$token = $null
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)
    $headers = @{ Authorization = "Bearer $token"; Host = 'web1.localhost' }
    $uri = "http://127.0.0.1:$Port/api/employees/bulk-upload/b89d04aa-1419-4c85-a7e3-48ab1d92d111"
    $response = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -TimeoutSec 15
    $data = $response.data
    if (-not $response.success -or $null -eq $data) { throw 'The authorized status endpoint did not return an operation.' }

    $rows = @($data.rows)
    [pscustomobject]@{
        Tenant = 'WEB1 (authorized request host)'
        OperationId = $data.operationId
        State = $data.state
        Total = $data.totalRows
        Succeeded = @($rows | Where-Object status -eq 'Succeeded').Count
        Failed = @($rows | Where-Object status -eq 'Failed').Count
        Skipped = @($rows | Where-Object status -eq 'Skipped').Count
        Processing = @($rows | Where-Object status -eq 'Processing').Count
        Pending = @($rows | Where-Object status -eq 'Pending').Count
        LastUpdatedAtUtc = $data.lastUpdatedAtUtc
        Lease = 'Not exposed by this endpoint; refresh authorized read-only preflight for lease status.'
    } | Format-List
}
finally {
    $headers = $null
    $token = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer)
    $secureToken.Dispose()
}
