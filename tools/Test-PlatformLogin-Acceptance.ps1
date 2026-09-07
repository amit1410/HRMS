[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'http://localhost:5080'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
$platformHost = 'platform.localhost'
$tenantHost = 'demo01.localhost'
$client = [System.Net.Http.HttpClient]::new()

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$HostName,
        [ValidateSet('GET', 'POST')][string]$Method = 'GET',
        [string]$BearerToken,
        [string]$JsonBody,
        [switch]$HasBody
    )

    if ($HasBody -and $Method -notin @('POST')) {
        throw "A request body is not valid for HTTP $Method."
    }
    if ($HasBody -and $null -eq $JsonBody) {
        throw 'HasBody was specified without JSON content.'
    }

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        ([Uri]::new(($ApiBaseUrl.TrimEnd('/') + $Path))))
    $request.Headers.Host = $HostName
    if ($BearerToken) {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $BearerToken)
    }
    if ($HasBody) {
        $request.Content = [System.Net.Http.StringContent]::new($JsonBody, [System.Text.Encoding]::UTF8, 'application/json')
    }

    $response = $null
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $json = $null
        if (-not [string]::IsNullOrWhiteSpace($body)) {
            try { $json = $body | ConvertFrom-Json } catch { }
        }
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Json = $json
        }
    }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
    }
}

function Assert-Success {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
    if ($Response.StatusCode -lt 200 -or $Response.StatusCode -ge 300 -or $null -eq $Response.Json -or $Response.Json.success -ne $true) {
        $message = if ($Response.Json -and $Response.Json.message) { [string]$Response.Json.message } else { 'No response message.' }
        throw "$Phase failed with HTTP $($Response.StatusCode): $message"
    }
    return $Response.Json.data
}

function Assert-Rejected {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
    if ($Response.StatusCode -ge 200 -and $Response.StatusCode -lt 300) {
        throw "$Phase unexpectedly succeeded."
    }
    Write-Host "${Phase}: PASS (HTTP $($Response.StatusCode))"
}

function Get-RequiredProperty {
    param([Parameter(Mandatory)]$Object, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Phase)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { throw "$Phase response is missing '$Name'." }
    return $property.Value
}

function Convert-SecureStringToPlaintext {
    param([Parameter(Mandatory)][Security.SecureString]$SecureString)
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    }
}

try {
    Write-Host 'PLATFORM LOGIN ACCEPTANCE'
    Write-Host "API transport: $ApiBaseUrl"
    Write-Host "Platform Host: $platformHost"

    Write-Host '[1/9] API availability'
    $health = Invoke-Api -Path '/health' -HostName $platformHost
    if ($health.StatusCode -ne 200 -or $null -eq $health.Json -or $health.Json.status -ne 'Healthy') {
        throw "HRMS.API health check failed with HTTP $($health.StatusCode)."
    }
    Write-Host 'API availability: PASS'

    $email = Read-Host 'Platform admin email'
    $securePassword = Read-Host 'Platform admin password' -AsSecureString
    $plainPassword = $null
    $loginData = $null
    try {
        $plainPassword = Convert-SecureStringToPlaintext $securePassword
        $loginJson = @{ email = $email; password = $plainPassword } | ConvertTo-Json -Compress
        Write-Host '[2/9] Platform login'
        $login = Invoke-Api -Path '/api/platform/auth/login' -HostName $platformHost -Method POST -JsonBody $loginJson -HasBody
        $loginData = Assert-Success -Response $login -Phase 'Platform login'
    }
    finally {
        $plainPassword = $null
        if ($securePassword) { $securePassword.Dispose() }
        $loginJson = $null
    }

    $accessToken = [string](Get-RequiredProperty -Object $loginData -Name 'accessToken' -Phase 'Platform login')
    $refreshToken = [string](Get-RequiredProperty -Object $loginData -Name 'refreshToken' -Phase 'Platform login')
    $loginUser = Get-RequiredProperty -Object $loginData -Name 'user' -Phase 'Platform login'
    if ([string]::IsNullOrWhiteSpace($accessToken) -or [string]::IsNullOrWhiteSpace($refreshToken)) { throw 'Platform login returned an empty token.' }
    if ([string]$loginUser.email -ine $email) { throw 'Platform login returned a different platform email.' }
    Write-Host 'Platform login: PASS (tokens retained in memory only)'

    Write-Host '[3/9] Platform identity (/me)'
    $me = Assert-Success -Response (Invoke-Api -Path '/api/platform/auth/me' -HostName $platformHost -BearerToken $accessToken) -Phase 'Platform /me'
    if ([string]$me.email -ine $email -or @($me.roles) -notcontains 'PlatformSuperAdmin' -or @($me.permissions).Count -ne 3) { throw 'Platform /me identity or grants were unexpected.' }
    if ($me.PSObject.Properties.Name -contains 'tenantId') { throw 'Platform /me exposed tenant identity.' }
    Write-Host 'Platform /me: PASS'

    Write-Host '[4/9] Platform tenant permission'
    $tenants = @(Assert-Success -Response (Invoke-Api -Path '/api/platform/tenants' -HostName $platformHost -BearerToken $accessToken) -Phase 'Platform tenant list')
    $tenantCodes = @($tenants | ForEach-Object { [string]$_.tenantCode })
    if ($tenantCodes -notcontains 'DEMO01' -or $tenantCodes -notcontains 'DEMO02') { throw 'Platform tenant list did not contain DEMO01 and DEMO02.' }
    Write-Host 'PlatformTenant.View: PASS (DEMO01 and DEMO02 visible)'

    Write-Host '[5/9] Host and scheme isolation'
    foreach ($hostName in @('demo01.localhost', 'demo02.localhost', 'unknown.localhost', 'platform.localhost.evil.example', 'evilplatform.localhost')) {
        Assert-Rejected -Response (Invoke-Api -Path '/api/platform/tenants' -HostName $hostName -BearerToken $accessToken) -Phase "Platform API via $hostName"
    }
    Assert-Rejected -Response (Invoke-Api -Path '/api/auth/me' -HostName $tenantHost -BearerToken $accessToken) -Phase 'Platform token on tenant API'
    Write-Host 'PlatformBearer host/scheme isolation: PASS'

    Write-Host '[6/9] Refresh rotation and replay rejection'
    $refresh1 = $refreshToken
    $refreshData = Assert-Success -Response (Invoke-Api -Path '/api/platform/auth/refresh' -HostName $platformHost -Method POST -JsonBody (@{ refreshToken = $refresh1 } | ConvertTo-Json -Compress) -HasBody) -Phase 'Platform refresh'
    $accessToken2 = [string](Get-RequiredProperty -Object $refreshData -Name 'accessToken' -Phase 'Platform refresh')
    $refresh2 = [string](Get-RequiredProperty -Object $refreshData -Name 'refreshToken' -Phase 'Platform refresh')
    Assert-Rejected -Response (Invoke-Api -Path '/api/platform/auth/refresh' -HostName $platformHost -Method POST -JsonBody (@{ refreshToken = $refresh1 } | ConvertTo-Json -Compress) -HasBody) -Phase 'Consumed refresh-token replay'
    Write-Host 'Refresh rotation/replay: PASS'

    Write-Host '[7/9] Logout and revocation'
    $logout = Invoke-Api -Path '/api/platform/auth/logout' -HostName $platformHost -Method POST -BearerToken $accessToken2 -JsonBody (@{ refreshToken = $refresh2 } | ConvertTo-Json -Compress) -HasBody
    Assert-Success -Response $logout -Phase 'Platform logout' | Out-Null
    Assert-Rejected -Response (Invoke-Api -Path '/api/platform/auth/refresh' -HostName $platformHost -Method POST -JsonBody (@{ refreshToken = $refresh2 } | ConvertTo-Json -Compress) -HasBody) -Phase 'Logged-out refresh-token replay'
    Write-Host 'Logout/revocation: PASS'

    Write-Host '[8/9] Tenant-token boundary'
    Write-Host 'Tenant token -> platform API: DEFERRED (no tenant credentials are accepted by this runner).'
    Write-Host 'Platform token -> tenant API: PASS (verified above).'

    Write-Host '[9/9] Acceptance summary'
    Write-Host 'PlatformRefreshTokens may contain issued/revoked rows after this run.'
    Write-Host 'No tenant creation or tenant-data mutation is performed by this runner.'
    Write-Host 'PLATFORM LOGIN ACCEPTANCE API PASS'
}
catch {
    Write-Error "PLATFORM LOGIN ACCEPTANCE FAILED: $($_.Exception.Message)"
    exit 1
}
finally {
    $accessToken = $null
    $accessToken2 = $null
    $refreshToken = $null
    $refresh1 = $null
    $refresh2 = $null
    if ($client) { $client.Dispose() }
}
