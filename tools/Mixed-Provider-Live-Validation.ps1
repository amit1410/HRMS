[CmdletBinding()]
param([string]$BaseUrl = "http://localhost:5000")

$ErrorActionPreference = "Stop"
$headers = @{ Host = "adaptersql.localhost" }
$sqlHeaders = @{ Host = "adaptersql.localhost" }
$mySqlHeaders = @{ Host = "adaptermysql.localhost" }

function Get-SystemInfo([hashtable]$RequestHeaders) {
    Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/system/info" -Headers $RequestHeaders
}

try {
    $health = Invoke-WebRequest -Method Get -Uri "$BaseUrl/health" -Headers @{ Host = "localhost" }
    if ($health.StatusCode -ne 200) { throw "Health endpoint did not return HTTP 200." }
    Write-Host "Health: PASS"

    $sqlInfo = Get-SystemInfo $sqlHeaders
    $mySqlInfo = Get-SystemInfo $mySqlHeaders
    $sqlTenants = @($sqlInfo.data.tenants)
    $mySqlTenants = @($mySqlInfo.data.tenants)
    foreach ($tenant in @("ADAPTERSQL", "ADAPTERMYSQL", "DEMO01")) {
        if (-not (@($sqlTenants | Where-Object { $_.tenantCode -eq $tenant }).Count -eq 1)) { throw "System-info response did not contain $tenant." }
    }
    $demo02 = @($sqlTenants | Where-Object { $_.tenantCode -eq "DEMO02" })
    if ($demo02.Count -ne 1 -or $demo02[0].status -ne "Suspended") { throw "DEMO02 suspended state was not preserved." }
    try {
        $demo02Response = Invoke-WebRequest -Method Get -Uri "$BaseUrl/api/tenants/current/branding" -Headers @{ Host = "demo02.localhost" }
        $demo02StatusCode = $demo02Response.StatusCode
    } catch {
        $demo02StatusCode = [int]$_.Exception.Response.StatusCode
    }
    if ($demo02StatusCode -ne 404) { throw "DEMO02 did not preserve the expected unavailable-workspace response." }
    Write-Host "Catalog host/regression summaries: PASS"
    Write-Host "DEMO02 suspended HTTP behavior: PASS"
    Write-Host "Read-only tenant fan-out: PASS"
    Write-Host "SQL validation host response: $($sqlTenants.Count) summaries"
    Write-Host "MySQL validation host response: $(@($mySqlTenants).Count) summaries"
    Write-Warning "The current API exposes one aggregate provider name in /api/system/info, not one EF ProviderName per tenant. Catalog metadata and HTTP reachability do not prove both provider names."
    throw "LIVE HTTP TENANT-DATABASE PROOF REQUIRES SEPARATE PROVIDER EVIDENCE"
} catch {
    throw
}
