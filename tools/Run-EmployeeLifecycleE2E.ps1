[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'http://localhost:5080',
    [string]$ExpectedCatalog = 'HRMS_E2E_Catalog_20261007_091930',
    [switch]$NoCheckpoint
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
$selectionScript = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { $null } else { Join-Path $PSScriptRoot 'LeavePolicyVersionSelection.ps1' }
if ($selectionScript) { . $selectionScript }

# This runner deliberately has no state-file or token-file option. The state below is process-local.
$platformHost = 'platform.localhost'
$expectedTenantAdminEmail = 'e2e-admin@e2e.invalid'
$expectedTenantId = [Guid]'4056dce7-9011-445d-b785-ea1d30957878'
$client = [System.Net.Http.HttpClient]::new()
$E2EMaxPageSize = 100
$E2EMasterMaxPageSize = 200
$state = [pscustomobject]@{
    AccessToken = $null
    RefreshToken = $null
    Tenant = $null
}
$tenantState = [pscustomobject]@{
    AccessToken = $null
    RefreshToken = $null
}
$script:lifecycleEmployeeSession = $null
$script:lastStage4Operation = 'Stage 4 initialization'

function Convert-RunnerGuid {
    param([AllowNull()][object]$Value, [Parameter(Mandatory)][string]$Field)
    $raw = [string]$Value
    $parsed = [Guid]::Empty
    if ([string]::IsNullOrWhiteSpace($raw) -or -not [Guid]::TryParse($raw, [ref]$parsed) -or $parsed -eq [Guid]::Empty) {
        throw "STOP: Stage 4 [$script:lastStage4Operation] required GUID field '$Field' was missing or invalid."
    }
    return $parsed
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

function Invoke-ApiRaw {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$HostName,
        [ValidateSet('GET', 'HEAD', 'DELETE', 'POST', 'PUT', 'PATCH')][string]$Method = 'GET',
        [string]$BearerToken,
        [string]$JsonBody,
        [System.Net.Http.HttpContent]$Content
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        [Uri]::new(($ApiBaseUrl.TrimEnd('/') + $Path)))
    $request.Headers.Host = $HostName
    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $BearerToken)
    }
    # An omitted [string] parameter is represented as an empty string by PowerShell.
    # Do not turn that default into StringContent: GET/HEAD (and bodyless calls to
    # other verbs) must have no request content at all.
    if ($null -ne $Content) {
        $request.Content = $Content
    }
    elseif (-not [string]::IsNullOrWhiteSpace($JsonBody)) {
        $request.Content = [System.Net.Http.StringContent]::new($JsonBody, [Text.Encoding]::UTF8, 'application/json')
    }

    $response = $null
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $json = $null
        if (-not [string]::IsNullOrWhiteSpace($body)) {
            try { $json = $body | ConvertFrom-Json } catch { $json = $null }
        }
        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Json = $json }
    }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
    }
}

function Invoke-TenantMultipartApi {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$FileText
    )
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $bytes = [Text.Encoding]::UTF8.GetBytes($FileText)
    $file = [System.Net.Http.ByteArrayContent]::new($bytes)
    $file.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('text/csv')
    $multipart.Add($file, 'file', $FileName)
    try {
        return Invoke-ApiRaw -Path $Path -HostName $state.Tenant.Host -Method POST -BearerToken $tenantState.AccessToken -Content $multipart
    }
    finally {
        $file.Dispose(); $multipart.Dispose(); $bytes = $null; $FileText = $null
    }
}

function Refresh-PlatformToken {
    if ([string]::IsNullOrWhiteSpace($state.RefreshToken)) { throw 'The platform refresh token is unavailable.' }
    $body = @{ refreshToken = $state.RefreshToken } | ConvertTo-Json -Compress
    try {
        $response = Invoke-ApiRaw -Path '/api/platform/auth/refresh' -HostName $platformHost -Method POST -JsonBody $body
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300 -or $null -eq $response.Json -or $response.Json.success -ne $true) {
            throw 'Platform token refresh failed.'
        }
        $data = $response.Json.data
        $nextAccess = [string]$data.accessToken
        $nextRefresh = [string]$data.refreshToken
        if ([string]::IsNullOrWhiteSpace($nextAccess) -or [string]::IsNullOrWhiteSpace($nextRefresh)) { throw 'Platform token refresh returned no usable token.' }
        $state.AccessToken = $nextAccess
        $state.RefreshToken = $nextRefresh
    }
    finally {
        $body = $null
        $response = $null
        $data = $null
        $nextAccess = $null
        $nextRefresh = $null
    }
}

function Invoke-AuthenticatedApi {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateSet('GET', 'HEAD', 'DELETE', 'POST', 'PUT', 'PATCH')][string]$Method = 'GET',
        [string]$JsonBody
    )
    $response = Invoke-ApiRaw -Path $Path -HostName $platformHost -Method $Method -BearerToken $state.AccessToken -JsonBody $JsonBody
    if ($response.StatusCode -eq 401) {
        Refresh-PlatformToken
        $response = Invoke-ApiRaw -Path $Path -HostName $platformHost -Method $Method -BearerToken $state.AccessToken -JsonBody $JsonBody
    }
    return $response
}

function Get-DataOrStop {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
    $script:lastStage4Operation = $Phase
    if ($Response.StatusCode -lt 200 -or $Response.StatusCode -ge 300 -or $null -eq $Response.Json -or $Response.Json.success -ne $true) {
        $details = @()
        foreach ($propertyName in @('title', 'detail', 'message')) {
            $property = $Response.Json.psobject.Properties[$propertyName]
            if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                $details += ([string]$property.Value).Trim()
            }
        }
        $errorsProperty = $Response.Json.psobject.Properties['errors']
        if ($null -ne $errorsProperty -and $null -ne $errorsProperty.Value) {
            foreach ($error in @($errorsProperty.Value)) {
                $field = [string]$error.field
                $message = [string]$error.message
                if (-not [string]::IsNullOrWhiteSpace($message)) {
                    $detail = $message.Trim()
                    if (-not [string]::IsNullOrWhiteSpace($field)) { $detail = "$field`: $detail" }
                    $details += $detail
                }
            }
        }
        $safeDetails = @($details | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique | ForEach-Object {
            if ($_.Length -gt 300) { $_.Substring(0, 300) + '...' } else { $_ }
        })
        $suffix = ''
        if ($safeDetails.Count -gt 0) { $suffix = ' ' + ($safeDetails -join '; ') }
        throw "$Phase failed. HTTP $($Response.StatusCode).$suffix"
    }
    return $Response.Json.data
}

function Get-OptionalTenantDataOrNull {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
    $script:lastStage4Operation = $Phase
    if ($Response.StatusCode -eq 404) {
        $message = if ($null -ne $Response.Json) { [string]$Response.Json.message } else { '' }
        if ($message -match '(?i)(selected LeaveType is not assigned|configuration.*not found|rule.*not found)') { return $null }
    }
    return Get-TenantDataOrStop $Response $Phase
}

function Get-RunnerItems {
    param([AllowNull()][object]$Data, [Parameter(Mandatory)][string]$Field)
    if ($null -eq $Data) { throw "STOP: Stage 4 [$script:lastStage4Operation] response field '$Field' was null." }
    if ($null -ne $Data.items) { return @($Data.items) }
    return @($Data)
}

function Refresh-TenantToken {
    if ([string]::IsNullOrWhiteSpace($tenantState.RefreshToken)) { throw 'The tenant refresh token is unavailable.' }
    $body = @{ refreshToken = $tenantState.RefreshToken } | ConvertTo-Json -Compress
    try {
        $response = Invoke-ApiRaw -Path '/api/auth/refresh' -HostName $state.Tenant.Host -Method POST -JsonBody $body
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300 -or $null -eq $response.Json -or $response.Json.success -ne $true) {
            throw 'Tenant token refresh failed.'
        }
        $data = $response.Json.data
        $tenantState.AccessToken = [string]$data.accessToken
        $tenantState.RefreshToken = [string]$data.refreshToken
        if ([string]::IsNullOrWhiteSpace($tenantState.AccessToken) -or [string]::IsNullOrWhiteSpace($tenantState.RefreshToken)) { throw 'Tenant token refresh returned no usable token.' }
    }
    finally { $body = $null; $response = $null; $data = $null }
}

function Invoke-TenantApi {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateSet('GET', 'HEAD', 'DELETE', 'POST', 'PUT', 'PATCH')][string]$Method = 'GET',
        [string]$JsonBody,
        [switch]$Anonymous
    )
    $bearer = $tenantState.AccessToken
    if ($Anonymous) { $bearer = $null }
    $response = Invoke-ApiRaw -Path $Path -HostName $state.Tenant.Host -Method $Method -BearerToken $bearer -JsonBody $JsonBody
    if (-not $Anonymous -and $response.StatusCode -eq 401) {
        Refresh-TenantToken
        $response = Invoke-ApiRaw -Path $Path -HostName $state.Tenant.Host -Method $Method -BearerToken $tenantState.AccessToken -JsonBody $JsonBody
    }
    return $response
}

function Invoke-LifecycleActorLogin {
    param(
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$ExpectedRole,
        [Parameter(Mandatory)][string]$Label
    )
    $securePassword = $null
    $plainPassword = $null
    $loginBody = $null
    try {
        $securePassword = Read-Host "$Label password (known password required; no reset is attempted)" -AsSecureString
        $plainPassword = Convert-SecureStringToPlaintext $securePassword
        if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw "$Label authentication stopped: password was empty." }
        $loginBody = @{ email = $Email; password = $plainPassword } | ConvertTo-Json -Compress
        $login = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/login' -Method POST -JsonBody $loginBody -Anonymous) "$Label login"
        $accessToken = [string]$login.accessToken
        if ([string]::IsNullOrWhiteSpace($accessToken)) { throw "$Label login returned no usable access token." }
        if ([string]$login.user.email -ine $Email -or @($login.user.roles) -notcontains $ExpectedRole) {
            throw "$Label login returned an unexpected identity or role."
        }
        return [pscustomobject]@{ Email = $Email; Role = $ExpectedRole; TenantHost = $state.Tenant.Host; AccessToken = $accessToken }
    }
    finally {
        $plainPassword = $null; $loginBody = $null
        if ($securePassword) { $securePassword.Dispose() }; $securePassword = $null
    }
}

function Get-LifecycleEmployeeSession {
    if ($null -ne $script:lifecycleEmployeeSession -and
        [string]$script:lifecycleEmployeeSession.Email -ieq 'e2e-employee@e2e.invalid' -and
        [string]$script:lifecycleEmployeeSession.Role -ceq 'Employee' -and
        [string]$script:lifecycleEmployeeSession.TenantHost -ceq [string]$state.Tenant.Host -and
        -not [string]::IsNullOrWhiteSpace([string]$script:lifecycleEmployeeSession.AccessToken)) {
        $sessionCheck = Invoke-ApiRaw -Path '/api/auth/me' -HostName $state.Tenant.Host -BearerToken $script:lifecycleEmployeeSession.AccessToken
        $sessionUser = if ($null -ne $sessionCheck.Json) { $sessionCheck.Json.data } else { $null }
        if ($sessionCheck.StatusCode -ge 200 -and $sessionCheck.StatusCode -lt 300 -and
            $null -ne $sessionCheck.Json -and $sessionCheck.Json.success -eq $true -and
            [string]$sessionUser.email -ieq 'e2e-employee@e2e.invalid') {
            Write-Host 'Lifecycle Employee session: REUSED (Stage 3 authenticated session; no password prompt)'
            return $script:lifecycleEmployeeSession
        }

        # An expired/rejected actor token cannot be refreshed by the tenant refresh flow because
        # this actor session intentionally retains only its access token. Reauthenticate through
        # the same proven helper, with no recovery/reset path.
        $script:lifecycleEmployeeSession.AccessToken = $null
        $script:lifecycleEmployeeSession = $null
    }

    $script:lifecycleEmployeeSession = Invoke-LifecycleActorLogin -Email 'e2e-employee@e2e.invalid' -ExpectedRole 'Employee' -Label 'Lifecycle Employee leave'
    return $script:lifecycleEmployeeSession
}

function Invoke-LifecycleActorApi {
    param(
        [Parameter(Mandatory)]$Actor,
        [Parameter(Mandatory)][string]$Path,
        [ValidateSet('GET', 'HEAD', 'DELETE', 'POST', 'PUT', 'PATCH')][string]$Method = 'GET',
        [string]$JsonBody
    )
    $response = Invoke-ApiRaw -Path $Path -HostName $state.Tenant.Host -Method $Method -BearerToken $Actor.AccessToken -JsonBody $JsonBody
    if ($response.StatusCode -eq 401) { throw "STOP: $($Actor.Role) actor received HTTP 401; no credential recovery/reset was attempted." }
    return $response
}

function Get-ConfirmationReviewInputs {
    param([Parameter(Mandatory)]$Questions)
    $inputs = @()
    foreach ($question in @($Questions)) {
        $input = @{ confirmationReviewCaseQuestionId = [string]$question.id }
        switch ([string]$question.questionType) {
            'Rating' {
                $input.ratingValue = [int](@($question.ratingValues | Sort-Object displayOrder | Select-Object -First 1).ratingValue)
            }
            'YesNo' { $input.boolValue = $true }
            'Text' { $input.textValue = 'E2E lifecycle review' }
            'Dropdown' { $input.selectedOptionCodes = @([string](@($question.options | Sort-Object displayOrder | Select-Object -First 1).code)) }
            'MultiSelect' { $input.selectedOptionCodes = @([string](@($question.options | Sort-Object displayOrder | Select-Object -First 1).code)) }
            default { throw "STOP: unsupported confirmation question type '$($question.questionType)'." }
        }
        $inputs += [pscustomobject]$input
    }
    return $inputs
}

function Get-TenantDataOrStop {
    param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
    return Get-DataOrStop -Response $Response -Phase $Phase
}

function Convert-ApiDateOnlyToDateTime {
    param(
        [Parameter(Mandatory)][AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Label
    )

    $raw = [string]$Value
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "STOP: $Label is missing." }
    try {
        # The API's DateOnly JSON contract is yyyy-MM-dd. Keep this compatible with
        # Windows PowerShell 5.1, which does not provide System.DateOnly.
        return [DateTime]::ParseExact(
            $raw.Trim(),
            'yyyy-MM-dd',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None).Date
    }
    catch { throw "STOP: $Label is invalid." }
}

function Get-InvitationTokenFromUrl {
    param([Parameter(Mandatory)][string]$InvitationUrl)
    try { $uri = [Uri]$InvitationUrl } catch { throw 'The invitation URL is invalid.' }
    if (-not $uri.IsAbsoluteUri -or [string]::IsNullOrWhiteSpace($uri.Query)) { throw 'The invitation URL is invalid.' }
    $tokenPart = $uri.Query.TrimStart('?') -split '&' | Where-Object { $_ -like 'token=*' } | Select-Object -First 1
    if ($null -eq $tokenPart) { throw 'The invitation URL does not contain an activation token.' }
    $token = [Uri]::UnescapeDataString($tokenPart.Substring(6))
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'The invitation URL does not contain a usable activation token.' }
    return $token
}

function Invoke-TenantAdministratorRecovery {
    if ($null -eq $state.Tenant) { throw 'Tenant administrator recovery requires a resolved tenant.' }
    if ($state.Tenant.Id -ne $expectedTenantId -or
        [string]$state.Tenant.Code -cne 'E2E01' -or
        [string]$state.Tenant.Host -cne 'e2e01.localhost' -or
        [string]$state.Tenant.Provider -cne 'MySql' -or
        [string]$state.Tenant.ShardKey -cne 'e2e01') {
        throw 'STOP: password recovery is permitted only for the exact disposable E2E01 tenant.'
    }

    $challengeBody = @{ identifier = $expectedTenantAdminEmail } | ConvertTo-Json -Compress
    $challenge = $null
    $sendBody = $null
    $send = $null
    $otp = $null
    $verifyBody = $null
    $verification = $null
    $resetToken = $null
    $secureNewPassword = $null
    $secureConfirmation = $null
    $newPassword = $null
    $confirmation = $null
    $resetBody = $null
    try {
        $runtime = Get-SystemInfo
        if ([string]$runtime.environment -cne 'Development' -or
            [string]$runtime.passwordRecoveryEmailProvider -cne 'Fake' -or
            [string]$runtime.passwordRecoveryEmailSender -cne 'DevelopmentEmailSender' -or
            $runtime.developmentOtpAvailable -ne $true) {
            throw 'E2E recovery unavailable: running API is not configured for Development recovery delivery.'
        }

        $challenge = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/forgot-password' -Method POST -JsonBody $challengeBody -Anonymous) 'Tenant administrator recovery initiation'
        $emailChannel = @($challenge.availableChannels | Where-Object { [string]$_.channel -ieq 'Email' })
        if ($emailChannel.Count -ne 1) { throw 'Tenant administrator recovery did not offer exactly one email verification channel.' }

        $sendBody = @{ challengeId = [string]$challenge.challengeId; channel = 'Email' } | ConvertTo-Json -Compress
        $send = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/forgot-password/send-otp' -Method POST -JsonBody $sendBody -Anonymous) 'Tenant administrator recovery delivery'
        $otp = [string]$send.developmentOtp
        if ([string]::IsNullOrWhiteSpace($otp)) {
            throw 'Development recovery OTP was not returned by the supported Development provider; no reset was attempted.'
        }

        $verifyBody = @{ challengeId = [string]$challenge.challengeId; otp = $otp } | ConvertTo-Json -Compress
        $verification = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/forgot-password/verify-otp' -Method POST -JsonBody $verifyBody -Anonymous) 'Tenant administrator recovery verification'
        $resetToken = [string]$verification.resetToken
        if ([string]::IsNullOrWhiteSpace($resetToken)) { throw 'Recovery verification returned no usable reset state.' }

        $secureNewPassword = Read-Host 'New tenant administrator password' -AsSecureString
        $secureConfirmation = Read-Host 'Confirm new tenant administrator password' -AsSecureString
        $newPassword = Convert-SecureStringToPlaintext $secureNewPassword
        $confirmation = Convert-SecureStringToPlaintext $secureConfirmation
        if ([string]::IsNullOrWhiteSpace($newPassword)) { throw 'Password recovery stopped: password was empty.' }
        if ($newPassword -cne $confirmation) { throw 'Password recovery stopped: password confirmation did not match; no reset request was made.' }

        $resetBody = @{ resetToken = $resetToken; newPassword = $newPassword; confirmPassword = $confirmation } | ConvertTo-Json -Compress
        $null = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/forgot-password/reset' -Method POST -JsonBody $resetBody -Anonymous) 'Tenant administrator password reset'
        Write-Host 'Tenant administrator recovery: PASS (supported OTP/reset-password flow)'
        return $newPassword
    }
    finally {
        $challengeBody = $null; $challenge = $null; $sendBody = $null; $send = $null; $otp = $null
        $verifyBody = $null; $verification = $null; $resetToken = $null; $resetBody = $null
        $newPassword = $null; $confirmation = $null
        if ($secureNewPassword) { $secureNewPassword.Dispose() }; $secureNewPassword = $null
        if ($secureConfirmation) { $secureConfirmation.Dispose() }; $secureConfirmation = $null
    }
}

function Invoke-TenantAdministratorAuthentication {
    $choice = ''
    do { $choice = (Read-Host "Tenant administrator access: [L] Login with known password, [R] Recover/reset password, [A] Activation pending").Trim().ToUpperInvariant() } while ($choice -notin @('L', 'R', 'A'))
    $secureTenantPassword = $null
    $secureInvitationUrl = $null
    $plainTenantPassword = $null
    $inviteUrl = $null
    $token = $null
    $setBody = $null
    $body = $null
    try {
        if ($choice -eq 'R') {
            $plainTenantPassword = Invoke-TenantAdministratorRecovery
            if ([string]::IsNullOrWhiteSpace($plainTenantPassword)) { throw 'Tenant administrator password was empty.' }
        }
        elseif ($choice -eq 'A') {
            $secureInvitationUrl = Read-Host 'Invitation URL from the configured E2E email sink' -AsSecureString
            $inviteUrl = Convert-SecureStringToPlaintext $secureInvitationUrl
            if ([string]::IsNullOrWhiteSpace($inviteUrl)) { throw 'Activation stopped: no invitation URL was supplied.' }
            $token = Get-InvitationTokenFromUrl $inviteUrl
            $secureTenantPassword = Read-Host "Set a NEW password for $expectedTenantAdminEmail" -AsSecureString
            $plainTenantPassword = Convert-SecureStringToPlaintext $secureTenantPassword
            if ([string]::IsNullOrWhiteSpace($plainTenantPassword)) { throw 'Activation stopped: password was empty.' }
            $setBody = @{ token = $token; password = $plainTenantPassword; confirmPassword = $plainTenantPassword } | ConvertTo-Json -Compress
            $null = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/set-password' -Method POST -JsonBody $setBody -Anonymous) 'Tenant administrator activation'
            Write-Host 'Tenant administrator activation: PASS (supported invitation/set-password flow)'
        }
        else {
            $secureTenantPassword = Read-Host "Tenant administrator password for $expectedTenantAdminEmail" -AsSecureString
            $plainTenantPassword = Convert-SecureStringToPlaintext $secureTenantPassword
            if ([string]::IsNullOrWhiteSpace($plainTenantPassword)) { throw 'Tenant administrator password was empty.' }
        }

        $body = @{ email = $expectedTenantAdminEmail; password = $plainTenantPassword } | ConvertTo-Json -Compress
        $login = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/login' -Method POST -JsonBody $body -Anonymous) 'Tenant administrator login'
        $tenantState.AccessToken = [string]$login.accessToken
        $tenantState.RefreshToken = [string]$login.refreshToken
        if ([string]::IsNullOrWhiteSpace($tenantState.AccessToken) -or [string]::IsNullOrWhiteSpace($tenantState.RefreshToken)) { throw 'Tenant login returned no usable token.' }
        if ([string]$login.user.email -ine $expectedTenantAdminEmail -or @($login.user.roles) -notcontains 'TenantAdmin') { throw 'Tenant login returned an unexpected tenant identity or role.' }
        $me = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/me') 'Tenant administrator /me'
        if ([string]$me.email -ine $expectedTenantAdminEmail -or @($me.roles) -notcontains 'TenantAdmin') { throw 'Tenant /me returned an unexpected tenant identity or role.' }
    }
    finally {
        $body = $null; $setBody = $null; $token = $null; $inviteUrl = $null; $plainTenantPassword = $null; $me = $null
        if ($secureInvitationUrl) { $secureInvitationUrl.Dispose() }; $secureInvitationUrl = $null
        if ($secureTenantPassword) { $secureTenantPassword.Dispose() }; $secureTenantPassword = $null
    }
}

function New-RunnerPagedPath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][int]$Page,
        [Parameter(Mandatory)][int]$PageSize,
        [Parameter(Mandatory)][int]$ApiMaximum,
        [string]$Query = ''
    )
    if ($Page -lt 1) { throw "Runner paging contract violation: page must be at least 1 for '$BasePath'." }
    if ($PageSize -lt 1 -or $PageSize -gt $ApiMaximum) {
        throw "Runner paging contract violation: pageSize $PageSize exceeds API maximum $ApiMaximum for '$BasePath'."
    }
    $separator = if ($BasePath.Contains('?')) { '&' } else { '?' }
    return "$BasePath${separator}page=$Page&pageSize=$PageSize$Query"
}

function Get-AllRunnerPagedItems {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][int]$PageSize,
        [Parameter(Mandatory)][int]$ApiMaximum,
        [Parameter(Mandatory)][string]$Phase,
        [string]$Query = ''
    )
    $items = @()
    $page = 1
    do {
        $path = New-RunnerPagedPath -BasePath $BasePath -Page $page -PageSize $PageSize -ApiMaximum $ApiMaximum -Query $Query
        $data = Get-TenantDataOrStop (Invoke-TenantApi -Path $path) $Phase
        $pageItems = @($data.items)
        $items += $pageItems

        $totalPages = [int]$data.totalPages
        if ($totalPages -le 0 -and $null -ne $data.totalCount) {
            $totalPages = [int][Math]::Ceiling(([int]$data.totalCount) / [double]$PageSize)
        }
        if ($totalPages -gt 0) {
            $hasNextPage = $page -lt $totalPages
        }
        else {
            $hasNextPage = $pageItems.Count -eq $PageSize
        }
        $page++
    } while ($hasNextPage)
    return $items
}

function Find-ExactMaster {
    param([Parameter(Mandatory)][string]$Kind, [Parameter(Mandatory)][string]$Code, [Parameter(Mandatory)][string]$Name)
    $encodedCode = [Uri]::EscapeDataString($Code)
    $items = @(Get-AllRunnerPagedItems -BasePath "/api/masters/$Kind" -PageSize $E2EMasterMaxPageSize -ApiMaximum $E2EMasterMaxPageSize -Query "&search=$encodedCode" -Phase "Inspect $Kind")
    $matches = @($items | Where-Object { [string]$_.code -ieq $Code -or [string]$_.name -ieq $Name })
    if ($matches.Count -eq 0) {
        $encodedName = [Uri]::EscapeDataString($Name)
        $items = @(Get-AllRunnerPagedItems -BasePath "/api/masters/$Kind" -PageSize $E2EMasterMaxPageSize -ApiMaximum $E2EMasterMaxPageSize -Query "&search=$encodedName" -Phase "Inspect $Kind")
        $matches = @($items | Where-Object { [string]$_.code -ieq $Code -or [string]$_.name -ieq $Name })
    }
    if ($matches.Count -gt 1) { throw "STOP: conflicting E2E $Kind records match '$Code'/'$Name'. No mutation was attempted." }
    if ($matches.Count -eq 1) {
        if ([string]$matches[0].code -cne $Code -or [string]$matches[0].name -cne $Name -or -not [bool]$matches[0].isActive) {
            throw "STOP: existing $Kind record conflicts with expected E2E identity '$Code'/'$Name'."
        }
        return $matches[0]
    }
    return $null
}

function Ensure-Master {
    param([Parameter(Mandatory)][string]$Kind, [Parameter(Mandatory)][string]$Code, [Parameter(Mandatory)][string]$Name)
    $existing = Find-ExactMaster -Kind $Kind -Code $Code -Name $Name
    if ($null -ne $existing) { return [pscustomobject]@{ Record = $existing; Existing = $true } }
    $body = @{ code = $Code; name = $Name; isActive = $true } | ConvertTo-Json -Compress
    $created = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/masters/$Kind" -Method POST -JsonBody $body) "Create $Kind '$Code'"
    if ([string]$created.code -cne $Code -or [string]$created.name -cne $Name) { throw "STOP: $Kind creation returned an unexpected identity." }
    return [pscustomobject]@{ Record = $created; Existing = $false }
}

function Ensure-Country {
    param([Parameter(Mandatory)][string]$Code, [Parameter(Mandatory)][string]$Name)
    $encodedCode = [Uri]::EscapeDataString($Code)
    $items = @(Get-AllRunnerPagedItems -BasePath '/api/countries' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query "&search=$encodedCode" -Phase 'Inspect countries')
    $matches = @($items | Where-Object { [string]$_.code -ieq $Code -or [string]$_.name -ieq $Name })
    if ($matches.Count -eq 0) {
        $encodedName = [Uri]::EscapeDataString($Name)
        $items = @(Get-AllRunnerPagedItems -BasePath '/api/countries' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query "&search=$encodedName" -Phase 'Inspect countries')
        $matches = @($items | Where-Object { [string]$_.code -ieq $Code -or [string]$_.name -ieq $Name })
    }
    if ($matches.Count -gt 1) { throw "STOP: conflicting country records match '$Code'/'$Name'. No mutation was attempted." }
    if ($matches.Count -eq 1) {
        if ([string]$matches[0].code -cne $Code -or [string]$matches[0].name -cne $Name -or -not [bool]$matches[0].isActive) {
            throw "STOP: existing country conflicts with expected E2E identity '$Code'/'$Name'."
        }
        return [pscustomobject]@{ Record = $matches[0]; Existing = $true }
    }
    $body = @{ code = $Code; name = $Name; isActive = $true } | ConvertTo-Json -Compress
    $created = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/countries' -Method POST -JsonBody $body) "Create country '$Code'"
    if ([string]$created.code -cne $Code -or [string]$created.name -cne $Name -or -not [bool]$created.isActive) { throw 'STOP: country creation returned an unexpected identity.' }
    return [pscustomobject]@{ Record = $created; Existing = $false }
}

function Get-EnsureStatus {
    param([Parameter(Mandatory)]$Result)
    if ($Result.Existing) { return 'REUSED' }
    return 'CREATED'
}

function Get-ExpectedEmployee {
    param([Parameter(Mandatory)][string]$EmployeeCode)
    $encodedEmployeeCode = [Uri]::EscapeDataString($EmployeeCode)
    $items = @(Get-AllRunnerPagedItems -BasePath '/api/employees' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query "&search=$encodedEmployeeCode" -Phase 'Inspect E2E employees')
    $matches = @($items | Where-Object { [string]$_.employeeCode -ieq $EmployeeCode })
    if ($matches.Count -gt 1) { throw "STOP: duplicate employee code '$EmployeeCode' exists." }
    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}

function Normalize-EmployeeIdentityText {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    return ([string]$Value).Trim()
}

function Get-EmployeeIdentityMismatches {
    param(
        [Parameter(Mandatory)]$Employee,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$FirstName,
        [Parameter(Mandatory)][string]$LastName,
        [Parameter(Mandatory)][string]$Email
    )

    $expected = @{
        employeeCode = $Code
        firstName = $FirstName
        lastName = $LastName
        email = $Email
    }
    $mismatches = @()
    foreach ($field in @('employeeCode', 'firstName', 'lastName', 'email')) {
        $actualValue = Normalize-EmployeeIdentityText $Employee.$field
        $expectedValue = Normalize-EmployeeIdentityText $expected[$field]
        if ($actualValue -ine $expectedValue) {
            $mismatches += $field
        }
    }
    return $mismatches
}

function Test-NullableGuidEqual {
    param(
        [AllowNull()][object]$Actual,
        [AllowNull()][object]$Expected
    )

    $actualText = Normalize-EmployeeIdentityText $Actual
    $expectedText = Normalize-EmployeeIdentityText $Expected
    $actualIsNull = [string]::IsNullOrWhiteSpace($actualText) -or $actualText -eq [Guid]::Empty.ToString()
    $expectedIsNull = [string]::IsNullOrWhiteSpace($expectedText) -or $expectedText -eq [Guid]::Empty.ToString()
    if ($actualIsNull -and $expectedIsNull) { return $true }
    if ($actualIsNull -or $expectedIsNull) { return $false }
    try { return ([Guid]$actualText -eq [Guid]$expectedText) } catch { return $false }
}

function Assert-ExpectedEmployeeRelationship {
    param(
        [Parameter(Mandatory)]$Employee,
        [Parameter(Mandatory)][AllowNull()][object]$ExpectedManagerId,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-NullableGuidEqual $Employee.reportingManagerId $ExpectedManagerId)) {
        throw "STOP: $Label conflicts on: reportingManagerId."
    }
}

function Assert-ExpectedEmployeeConfiguration {
    param(
        [Parameter(Mandatory)]$Employee,
        [Parameter(Mandatory)]$Department,
        [Parameter(Mandatory)]$Designation,
        [Parameter(Mandatory)][string]$Label
    )

    $mismatches = @()
    if (-not (Test-NullableGuidEqual $Employee.departmentId $Department.id)) { $mismatches += 'departmentId' }
    if (-not (Test-NullableGuidEqual $Employee.designationId $Designation.id)) { $mismatches += 'designationId' }
    if ($mismatches.Count -gt 0) {
        throw "STOP: $Label conflicts on: $($mismatches -join ', ')."
    }
}

function Assert-ExpectedEmployee {
    param(
        [Parameter(Mandatory)]$Employee,
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$FirstName,
        [Parameter(Mandatory)][string]$LastName,
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$Label
    )
    $employeeId = [string]$Employee.id
    if ([string]::IsNullOrWhiteSpace($employeeId) -or $employeeId -eq [Guid]::Empty.ToString()) {
        throw "STOP: $Label creation/retrieval did not return a valid employee ID."
    }
    try { $null = [Guid]$employeeId } catch { throw "STOP: $Label creation/retrieval did not return a valid employee ID." }
    $mismatches = @(Get-EmployeeIdentityMismatches -Employee $Employee -Code $Code -FirstName $FirstName -LastName $LastName -Email $Email)
    if ($mismatches.Count -gt 0) {
        throw "STOP: $Label conflicts on: $($mismatches -join ', ')."
    }
    return $employeeId
}

function Assert-EmployeeJoiningDate {
    param(
        [Parameter(Mandatory)]$Employee,
        [Parameter(Mandatory)][string]$EffectiveFrom,
        [Parameter(Mandatory)][string]$Label
    )

    if ($null -eq $Employee.dateOfJoining) { return }
    try {
        $joiningDate = [DateTime]::Parse([string]$Employee.dateOfJoining).Date
        $effectiveDate = [DateTime]::Parse($EffectiveFrom).Date
    }
    catch { throw "STOP: $Label has an invalid dateOfJoining." }
    if ($joiningDate -gt $effectiveDate) {
        throw "STOP: $Label dateOfJoining '$($joiningDate.ToString('yyyy-MM-dd'))' is after E2E effective date '$EffectiveFrom'."
    }
}

function Ensure-Employee {
    param(
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$FirstName,
        [Parameter(Mandatory)][string]$LastName,
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)]$Department,
        [Parameter(Mandatory)]$Designation,
        [Parameter(Mandatory)][AllowNull()][object]$ManagerId,
        [string]$EffectiveFrom = (Get-Date).Date.ToString('yyyy-MM-dd')
    )
    $existing = Get-ExpectedEmployee -EmployeeCode $Code
    if ($null -ne $existing) {
        $detail = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$($existing.id)") "Inspect employee '$Code'"
        Assert-ExpectedEmployee -Employee $detail -Code $Code -FirstName $FirstName -LastName $LastName -Email $Email -Label "Employee '$Code'" | Out-Null
        Assert-ExpectedEmployeeConfiguration -Employee $detail -Department $Department -Designation $Designation -Label "Employee '$Code'"
        Assert-ExpectedEmployeeRelationship -Employee $detail -ExpectedManagerId $ManagerId -Label "Employee '$Code'"
        Assert-EmployeeJoiningDate -Employee $detail -EffectiveFrom $EffectiveFrom -Label "Employee '$Code'"
        return [pscustomobject]@{ Record = $detail; Existing = $true }
    }
    $managerIdValue = $null
    if ($null -ne $ManagerId) { $managerIdValue = [string]$ManagerId }
    $body = @{
        employeeCode = $Code; firstName = $FirstName; lastName = $LastName; email = $Email
        dateOfJoining = $EffectiveFrom; status = 'Active'
        departmentId = [string]$Department.id; designationId = [string]$Designation.id
        reportingManagerId = $managerIdValue
    } | ConvertTo-Json -Compress
    $created = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/employees' -Method POST -JsonBody $body) "Create employee '$Code'"
    if ([string]$created.employeeCode -cne $Code) { throw "STOP: employee creation returned an unexpected identity for '$Code'." }
    $detail = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$($created.id)") "Verify employee '$Code'"
    Assert-ExpectedEmployee -Employee $detail -Code $Code -FirstName $FirstName -LastName $LastName -Email $Email -Label "Employee '$Code'" | Out-Null
    Assert-ExpectedEmployeeConfiguration -Employee $detail -Department $Department -Designation $Designation -Label "Employee '$Code'"
    Assert-ExpectedEmployeeRelationship -Employee $detail -ExpectedManagerId $ManagerId -Label "Employee '$Code'"
    Assert-EmployeeJoiningDate -Employee $detail -EffectiveFrom $EffectiveFrom -Label "Employee '$Code'"
    return [pscustomobject]@{ Record = $detail; Existing = $false }
}

function Ensure-PortalAccount {
    param([Parameter(Mandatory)]$Employee, [Parameter(Mandatory)][string]$Email)
    $accountResponse = Invoke-TenantApi -Path "/api/employees/$($Employee.id)/portal-account"
    if ($accountResponse.StatusCode -eq 200) {
        $account = Get-TenantDataOrStop $accountResponse 'Inspect actor portal account'
        if ([string]$account.state -eq 'Active') {
            if ([string]$account.email -ine $Email) { throw "STOP: active portal account for employee '$($Employee.id)' conflicts with expected E2E identity '$Email'." }
            # An active account already has a persisted credential, but this runner cannot know it
            # and must not reset it just to make resume work. The authorization check will ask for
            # the credential known by the operator.
            return [pscustomobject]@{ Account = $account; Password = $null; CredentialState = 'ExistingActive'; Existing = $true }
        }
        if ([string]$account.state -eq 'InvitationPending') { throw "STOP: actor '$Email' has a pending invitation; the runner will not resend or replace it automatically." }
    }
    $body = @{ loginEmail = $Email } | ConvertTo-Json -Compress
    $created = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$($Employee.id)/portal-account" -Method POST -JsonBody $body) "Create portal account '$Email'"
    $inviteUrl = [string]$created.developmentInviteUrl
    if ([string]::IsNullOrWhiteSpace($inviteUrl)) { throw "Actor '$Email' requires the supported invitation activation URL, which was not returned by the development API." }
    $token = ([Uri]$inviteUrl).Query.TrimStart('?') -split '&' | Where-Object { $_ -like 'token=*' } | Select-Object -First 1
    if ($token) { $token = [Uri]::UnescapeDataString($token.Substring(6)) } else { $token = $null }
    if ([string]::IsNullOrWhiteSpace($token)) { throw "Actor '$Email' invitation did not contain a usable activation token." }
    $securePassword = Read-Host "Set password for $Email" -AsSecureString
    $plainPassword = $null
    $passwordForAuthorization = $null
    try {
        $plainPassword = Convert-SecureStringToPlaintext $securePassword
        if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw "Activation stopped: password for $Email was empty." }
        $setBody = @{ token = $token; password = $plainPassword; confirmPassword = $plainPassword } | ConvertTo-Json -Compress
        $null = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/set-password' -Method POST -JsonBody $setBody -Anonymous) "Activate actor '$Email'"
        # The supported activation call has now persisted this credential. Keep the operator-supplied
        # value only in process memory so a newly activated employee can use the same credential for
        # the later authorization check without a misleading second prompt.
        $passwordForAuthorization = $plainPassword
    }
    finally { $token = $null; $setBody = $null; $plainPassword = $null; if ($securePassword) { $securePassword.Dispose() }; $securePassword = $null }
    return [pscustomobject]@{ Account = $created.account; Password = $passwordForAuthorization; CredentialState = 'NewlyActivated'; Existing = $false }
}

function Assert-ExpectedEmployment {
    param(
        [Parameter(Mandatory)]$Record,
        [Parameter(Mandatory)][AllowNull()][object]$ExpectedManagerId,
        [Parameter(Mandatory)]$Department,
        [Parameter(Mandatory)]$Designation,
        [Parameter(Mandatory)]$Grade,
        [Parameter(Mandatory)]$Country,
        [Parameter(Mandatory)]$Location,
        [Parameter(Mandatory)]$PositionChangeReason,
        [Parameter(Mandatory)][string]$EffectiveFrom,
        [Parameter(Mandatory)][string]$Label
    )
    if ([bool]$Record.isSuperseded) { throw "STOP: $Label matched a superseded employment record." }
    $effectiveDate = [DateTime]::Parse($EffectiveFrom).Date
    $recordFrom = [DateTime]::Parse([string]$Record.effectiveFrom).Date
    if ($recordFrom -gt $effectiveDate) { throw "STOP: $Label starts after effective date '$EffectiveFrom'." }
    if (-not [string]::IsNullOrWhiteSpace([string]$Record.effectiveTo) -and [DateTime]::Parse([string]$Record.effectiveTo).Date -lt $effectiveDate) {
        throw "STOP: $Label ended before effective date '$EffectiveFrom'."
    }
    if ([string]$Record.employmentStatus -cne 'Active') { throw "STOP: $Label is not Active." }
    foreach ($pair in @(
        @{ Name = 'departmentId'; Expected = $Department.id },
        @{ Name = 'designationId'; Expected = $Designation.id },
        @{ Name = 'gradeId'; Expected = $Grade.id },
        @{ Name = 'countryLocationId'; Expected = $Country.Record.id },
        @{ Name = 'workLocationId'; Expected = $Location.id },
        @{ Name = 'positionChangeReasonId'; Expected = $PositionChangeReason.id })) {
        if (-not (Test-NullableGuidEqual $Record.($pair.Name) $pair.Expected)) { throw "STOP: $Label conflicts on: $($pair.Name)." }
    }
    if (-not (Test-NullableGuidEqual $Record.managerId $ExpectedManagerId)) { throw "STOP: $Label conflicts on: managerId." }
}

function Ensure-EmploymentHistory {
    param(
        [Parameter(Mandatory)]$Employee,
        [Parameter(Mandatory)]$Department,
        [Parameter(Mandatory)]$Designation,
        [Parameter(Mandatory)]$Grade,
        [Parameter(Mandatory)]$Country,
        [Parameter(Mandatory)]$Location,
        [Parameter(Mandatory)]$PositionChangeReason,
        [Parameter(Mandatory)][AllowNull()][object]$ManagerId,
        [string]$EffectiveFrom = (Get-Date).Date.ToString('yyyy-MM-dd'),
        [string]$Label = 'Employee'
    )

    $historyResponse = Invoke-TenantApi -Path "/api/employees/$($Employee.id)/employment-history"
    $history = @()
    if ($historyResponse.StatusCode -eq 200) {
        $history = @(Get-TenantDataOrStop $historyResponse "Inspect $Label employment history")
    }
    elseif ($historyResponse.StatusCode -ne 404) {
        $null = Get-TenantDataOrStop $historyResponse "Inspect $Label employment history"
    }

    $effectiveDate = [DateTime]::Parse($EffectiveFrom).Date
    $covering = @($history | Where-Object {
        -not [bool]$_.isSuperseded -and
        ([DateTime]::Parse([string]$_.effectiveFrom).Date -le $effectiveDate) -and
        ([string]::IsNullOrWhiteSpace([string]$_.effectiveTo) -or [DateTime]::Parse([string]$_.effectiveTo).Date -ge $effectiveDate)
    })
    if ($covering.Count -gt 1) { throw "STOP: $Label has overlapping employment records on '$EffectiveFrom'." }
    if ($covering.Count -eq 1) {
        Assert-ExpectedEmployment -Record $covering[0] -ExpectedManagerId $ManagerId -Department $Department -Designation $Designation -Grade $Grade -Country $Country -Location $Location -PositionChangeReason $PositionChangeReason -EffectiveFrom $EffectiveFrom -Label $Label
        return [pscustomobject]@{ Record = $covering[0]; Existing = $true }
    }

    if ($null -eq $Country.Record -or [string]::IsNullOrWhiteSpace([string]$Country.Record.id)) {
        throw 'STOP: a valid countryLocationId is required before creating employment history when workLocationId is present.'
    }
    try { $null = [Guid]$Country.Record.id; $null = [Guid]$Location.id } catch { throw 'STOP: employment geography returned an invalid country/location ID.' }

    $body = @{
        effectiveFrom = $EffectiveFrom
        departmentId = [string]$Department.id
        designationId = [string]$Designation.id
        gradeId = [string]$Grade.id
        workLocationId = [string]$Location.id
        countryLocationId = [string]$Country.Record.id
        managerId = $ManagerId
        positionChangeReasonId = [string]$PositionChangeReason.id
        changeReason = 'NewJoining'
        employmentType = 'FullTime'
        employmentStatus = 'Active'
    } | ConvertTo-Json -Compress
    $created = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$($Employee.id)/employment-history" -Method POST -JsonBody $body) "Create $Label employment history"
    Assert-ExpectedEmployment -Record $created -ExpectedManagerId $ManagerId -Department $Department -Designation $Designation -Grade $Grade -Country $Country -Location $Location -PositionChangeReason $PositionChangeReason -EffectiveFrom $EffectiveFrom -Label $Label
    return [pscustomobject]@{ Record = $created; Existing = $false }
}

function Ensure-ManagerEmploymentRelationship {
    param(
        [Parameter(Mandatory)]$Employee, [Parameter(Mandatory)]$Department, [Parameter(Mandatory)]$Designation,
        [Parameter(Mandatory)]$Grade, [Parameter(Mandatory)]$Country, [Parameter(Mandatory)]$Location,
        [Parameter(Mandatory)]$PositionChangeReason, [Parameter(Mandatory)][string]$ManagerId,
        [string]$EffectiveFrom = (Get-Date).Date.ToString('yyyy-MM-dd')
    )
    return Ensure-EmploymentHistory -Employee $Employee -Department $Department -Designation $Designation -Grade $Grade -Country $Country -Location $Location -PositionChangeReason $PositionChangeReason -ManagerId $ManagerId -EffectiveFrom $EffectiveFrom -Label 'Lifecycle Employee'
}

function Invoke-OrganizationStage {
    Write-Host 'Stage 2: organization setup and actor creation'
    Invoke-TenantAdministratorAuthentication
    $null = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/me') 'Tenant /me'
    Write-Host "Tenant authentication: PASS ($expectedTenantAdminEmail on e2e01.localhost; tokens remain memory-only)"

    $company = Ensure-Master 'organisations' 'E2E-COMPANY' 'E2E Company'
    $country = Ensure-Country 'IN' 'India'
    $location = Ensure-Master 'work-locations' 'E2E-HQ' 'E2E-HQ'
    $department = Ensure-Master 'departments' 'E2E-HR' 'E2E-HR'
    $designation = Ensure-Master 'designations' 'E2E-EMPLOYEE' 'E2E-EMPLOYEE'
    $grade = Ensure-Master 'grades' 'E2E-GRADE' 'E2E-GRADE'
    $positionChangeReason = Ensure-Master 'position-change-reasons' 'E2E-NEW-JOINING' 'E2E New Joining'
    # The API intentionally rejects back-dated employment changes. Use one business date for
    # the whole deterministic E2E organization graph so the manager covers the subordinate.
    $e2eEffectiveFrom = (Get-Date).Date.ToString('yyyy-MM-dd')
    Write-Host "Company/legal entity: $(Get-EnsureStatus $company)"
    Write-Host "Country/location: $(Get-EnsureStatus $country) ($($country.Record.code) / $($country.Record.name))"
    Write-Host "Work location: $(Get-EnsureStatus $location)"
    Write-Host "Department: $(Get-EnsureStatus $department)"
    Write-Host "Designation: $(Get-EnsureStatus $designation)"
    Write-Host "Grade: $(Get-EnsureStatus $grade)"

    try {
        $manager = Ensure-Employee 'E2E-MANAGER' 'E2E' 'Reporting Manager' 'e2e-manager@e2e.invalid' $department.Record $designation.Record $null $e2eEffectiveFrom
        $managerId = Assert-ExpectedEmployee -Employee $manager.Record -Code 'E2E-MANAGER' -FirstName 'E2E' -LastName 'Reporting Manager' -Email 'e2e-manager@e2e.invalid' -Label 'Reporting Manager'
        $managerAccount = Ensure-PortalAccount $manager.Record 'e2e-manager@e2e.invalid'
        if ([string]::IsNullOrWhiteSpace([string]$managerAccount.Account.userId)) { throw 'STOP: Reporting Manager portal account did not return a valid user ID.' }
        $managerEmployment = Ensure-EmploymentHistory -Employee $manager.Record -Department $department.Record -Designation $designation.Record -Grade $grade.Record -Country $country -Location $location.Record -PositionChangeReason $positionChangeReason.Record -ManagerId $null -EffectiveFrom $e2eEffectiveFrom -Label 'Reporting Manager'
    }
    catch {
        throw "Stage 2 / Reporting Manager preparation failed: $($_.Exception.Message)"
    }

    $employee = Ensure-Employee 'E2E-EMPLOYEE' 'E2E' 'Lifecycle Employee' 'e2e-employee@e2e.invalid' $department.Record $designation.Record $managerId $e2eEffectiveFrom
    # This is an explicit local preflight: manager history must cover the exact subordinate date
    # before the API receives the subordinate request.
    if ($null -eq $managerEmployment -or $managerEmployment.Record.employmentStatus -cne 'Active') {
        throw "STOP: Reporting Manager employment does not cover subordinate effective date '$e2eEffectiveFrom'."
    }
    $employeeEmployment = Ensure-ManagerEmploymentRelationship -Employee $employee.Record -Department $department.Record -Designation $designation.Record -Grade $grade.Record -Country $country -Location $location.Record -PositionChangeReason $positionChangeReason.Record -ManagerId $managerId -EffectiveFrom $e2eEffectiveFrom
    if ([string]$employee.Record.id -eq [string]$manager.Record.id) { throw 'STOP: lifecycle employee cannot be their own manager.' }
    if ([string]$employee.Record.reportingManagerId -ne $managerId) { throw 'Reporting relationship does not point Employee to Reporting Manager.' }
    $managerAssignments = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/role-assignments/users/$($managerAccount.Account.userId)") 'Verify system-managed Manager role')
    $managerAccessReconciled = $false
    if (-not (@($managerAssignments | Where-Object { [string]$_.roleName -eq 'Manager' -and $_.isCurrentlyEffective -and $_.isSystemManaged }).Count)) {
        $null = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$managerId/employment-history/reconcile-manager-access" -Method POST) 'Reconcile system-managed Manager access'
        $managerAccessReconciled = $true
        $managerAssignments = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/role-assignments/users/$($managerAccount.Account.userId)") 'Verify reconciled system-managed Manager role')
    }
    if (-not (@($managerAssignments | Where-Object { [string]$_.roleName -eq 'Manager' -and $_.isCurrentlyEffective -and $_.isSystemManaged }).Count)) { throw 'STOP: effective Manager access was not provisioned from the reporting relationship.' }

    # Later actors are intentionally provisioned only after the authoritative relationship
    # and system-managed Manager role have both been verified.
    $hr = Ensure-Employee 'E2E-HRBP' 'E2E' 'HR Business Partner' 'e2e-hrbp@e2e.invalid' $department.Record $designation.Record $null $e2eEffectiveFrom
    $payroll = Ensure-Employee 'E2E-PAYROLL' 'E2E' 'Payroll Administrator' 'e2e-payroll@e2e.invalid' $department.Record $designation.Record $null $e2eEffectiveFrom
    $actorIds = @($manager.Record.id, $employee.Record.id, $hr.Record.id, $payroll.Record.id) | ForEach-Object { [string]$_ } | Select-Object -Unique
    if (@($actorIds).Count -ne 4) { throw 'STOP: lifecycle actors are not distinct.' }
    $employeeAccount = Ensure-PortalAccount $employee.Record 'e2e-employee@e2e.invalid'
    $hrAccount = Ensure-PortalAccount $hr.Record 'e2e-hrbp@e2e.invalid'
    $payrollAccount = Ensure-PortalAccount $payroll.Record 'e2e-payroll@e2e.invalid'
    $roles = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/role-assignments/roles?assignableOnly=false') 'Inspect supported roles'
    $roleByName = @{}; foreach ($role in @($roles)) { $roleByName[[string]$role.name] = $role }
    foreach ($requiredRole in @('Manager', 'HRBP', 'Accounts')) { if (-not $roleByName.ContainsKey($requiredRole)) { throw "STOP: supported role '$requiredRole' is not available." } }
    $today = (Get-Date).ToString('yyyy-MM-dd')
    foreach ($assignment in @(
        @{ Account = $hrAccount.Account; Role = $roleByName['HRBP']; Label = 'HR/HRBP' },
        @{ Account = $payrollAccount.Account; Role = $roleByName['Accounts']; Label = 'Payroll Admin' })) {
        $existingAssignments = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/role-assignments/users/$($assignment.Account.userId)") "Inspect $($assignment.Label) role access")
        if (-not (@($existingAssignments | Where-Object { [string]$_.roleName -eq [string]$assignment.Role.name -and $_.isCurrentlyEffective }).Count)) {
            $roleBody = @{ roleId = [int]$assignment.Role.id; effectiveFrom = $today; reason = 'E2E lifecycle actor setup'; scopes = @() } | ConvertTo-Json -Compress
            $null = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/role-assignments/users/$($assignment.Account.userId)" -Method POST -JsonBody $roleBody) "Assign $($assignment.Label) role"
        }
    }
    if ([string]$employee.Record.id -eq $managerId) { throw 'STOP: lifecycle employee cannot be their own manager.' }
    Write-Host "Reporting Manager employee: $(Get-EnsureStatus $manager)"
    Write-Host "Reporting Manager account: $(if ($managerAccount.Existing) { 'REUSED' } else { 'ACTIVATED' })"
    Write-Host "Reporting Manager employment: $(Get-EnsureStatus $managerEmployment)"
    Write-Host "Lifecycle Employee employment: $(if ($employeeEmployment.Existing) { 'REUSED' } else { 'CREATED' })"
    Write-Host 'Reporting relationship: VERIFIED'
    Write-Host "Reporting Manager access: $(if ($managerAccessReconciled) { 'RECONCILED' } else { 'VERIFIED' })"
    if ($managerAccessReconciled) { Write-Host 'Reporting Manager access: VERIFIED' }
    Write-Host 'HR/HRBP employment: NOT REQUIRED (role/access scope is account-role based)'
    Write-Host 'Payroll Admin employment: NOT REQUIRED (role/access scope is account-role based)'
    Write-Host 'Organization setup: PASS'; Write-Host 'Manager actor: PASS'; Write-Host 'Employee actor: PASS'; Write-Host 'HR/HRBP actor: PASS'; Write-Host 'Payroll Admin actor: PASS'; Write-Host 'Reporting relationship: PASS'; Write-Host 'Role/access verification: PASS'

    # Fresh activation returns the exact credential just persisted by the supported set-password flow.
    # Resume of an already-active account returns no credential, so request the known password only then.
    $employeePassword = [string]$employeeAccount.Password
    $secureEmployeePassword = $null
    $plainEmployeePassword = $null; $employeeAccessToken = $null
    try {
        if ([string]::IsNullOrWhiteSpace($employeePassword)) {
            $secureEmployeePassword = Read-Host 'Employee password for safe authorization check' -AsSecureString
            $employeePassword = Convert-SecureStringToPlaintext $secureEmployeePassword
        }
        $plainEmployeePassword = $employeePassword
        $employeeLoginBody = @{ email = 'e2e-employee@e2e.invalid'; password = $plainEmployeePassword } | ConvertTo-Json -Compress
        $employeeLogin = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/auth/login' -Method POST -JsonBody $employeeLoginBody -Anonymous) 'Employee authorization-check login'
        $employeeAccessToken = [string]$employeeLogin.accessToken
        if ([string]::IsNullOrWhiteSpace($employeeAccessToken)) { throw 'Employee authorization-check login returned no access token.' }
        $negative = Invoke-ApiRaw -Path '/api/role-assignments' -HostName $state.Tenant.Host -BearerToken $employeeAccessToken
        if ($negative.StatusCode -notin @(401, 403)) { throw "STOP: employee negative authorization expected 403/401, received HTTP $($negative.StatusCode)." }
    }
    finally { $employeeAccessToken = $null; $employeeLoginBody = $null; $plainEmployeePassword = $null; $employeePassword = $null; if ($secureEmployeePassword) { $secureEmployeePassword.Dispose() }; $secureEmployeePassword = $null }
    Write-Host 'Negative authorization: PASS (employee received authorization denial for role-management access)'
    Write-Host 'Stage checkpoint: [C]ontinue next lifecycle stage, [X] exit safely'
    do { $next = (Read-Host 'Choice').Trim().ToUpperInvariant() } while ($next -notin @('C', 'X'))
    if ($next -eq 'C') { Invoke-EmploymentConfirmationStage }
    else { Write-Host 'Exited at safe checkpoint.' }
}

function Get-SystemInfo {
    # Development-only API inspection proves the catalog fan-out can read every shard and seed summary.
    $response = Invoke-ApiRaw -Path '/api/system/info' -HostName $platformHost
    return Get-DataOrStop -Response $response -Phase 'System persistence inspection'
}

function Assert-ConfirmationPolicyEditorMatchesE2E {
    param(
        [Parameter(Mandatory)]$Editor,
        [Parameter(Mandatory)][Guid]$DepartmentId,
        [Parameter(Mandatory)][DateTime]$EffectiveFrom
    )

    if ([string]$Editor.policy.code -cne 'E2E-CONFIRMATION' -or
        [string]$Editor.policy.name -cne 'E2E Confirmation Policy' -or
        $Editor.policy.isActive -ne $true) {
        throw 'STOP: deterministic E2E confirmation policy conflicts with the existing production policy identity.'
    }
    if ([int]$Editor.version.priority -ne 1 -or
        (Convert-ApiDateOnlyToDateTime -Value $Editor.version.effectiveFrom -Label 'confirmation policy effectiveFrom') -ne $EffectiveFrom -or
        -not [string]::IsNullOrWhiteSpace([string]$Editor.version.effectiveTo)) {
        throw 'STOP: deterministic E2E confirmation policy has incompatible effective dates or priority.'
    }
    if ([int]$Editor.probation.probationDuration -ne 1 -or
        [string]$Editor.probation.probationDurationUnit -cne 'Days' -or
        [int]$Editor.probation.triggerDaysBeforeEnd -ne 0 -or
        [string]$Editor.probation.requiredApproval -cne 'ManagerAndHrbp' -or
        [string]$Editor.probation.approvalSequence -cne 'Parallel') {
        throw 'STOP: deterministic E2E confirmation policy has incompatible probation or approval settings.'
    }

    $sets = @($Editor.applicability)
    if ($sets.Count -ne 1 -or [Guid]$sets[0].departmentId -ne $DepartmentId -or
        $null -ne $sets[0].gender -or $null -ne $sets[0].holdingCompanyId -or
        $null -ne $sets[0].lobId -or $null -ne $sets[0].organisationId -or
        $null -ne $sets[0].subDepartmentId -or $null -ne $sets[0].sectionId -or
        $null -ne $sets[0].subSectionId -or $null -ne $sets[0].functionId -or
        $null -ne $sets[0].subFunctionId -or $null -ne $sets[0].gradeId -or
        $null -ne $sets[0].designationId -or $null -ne $sets[0].employeeTypeId -or
        $null -ne $sets[0].countryLocationId -or $null -ne $sets[0].workLocationId -or
        $null -ne $sets[0].costCenterId) {
        throw 'STOP: deterministic E2E confirmation policy scope does not match the lifecycle employee department.'
    }
}

function Ensure-ConfirmationPolicyPrerequisite {
    param([Parameter(Mandatory)][Guid]$EmployeeId)

    if ([string]::IsNullOrWhiteSpace([string]$tenantState.AccessToken)) {
        throw 'STOP: the existing Stage 2 tenant-administrator session is unavailable for supported confirmation-policy administration.'
    }

    $employee = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$EmployeeId") 'Inspect lifecycle employee for confirmation-policy setup'
    try { $departmentId = [Guid]$employee.departmentId } catch { throw 'STOP: lifecycle employee has no valid department scope for confirmation-policy setup.' }
    try { $dateOfJoining = Convert-ApiDateOnlyToDateTime -Value $employee.dateOfJoining -Label 'lifecycle employee dateOfJoining' } catch { throw 'STOP: lifecycle employee has no valid date of joining for confirmation-policy setup.' }

    $encodedCode = [Uri]::EscapeDataString('E2E-CONFIRMATION')
    $policies = @(Get-AllRunnerPagedItems -BasePath '/api/confirmation-policies' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query "&search=$encodedCode" -Phase 'Inspect E2E confirmation policies')
    $matches = @($policies | Where-Object { [string]$_.code -ieq 'E2E-CONFIRMATION' })
    if ($matches.Count -gt 1) { throw 'STOP: conflicting deterministic E2E confirmation policies exist; no policy mutation was attempted.' }

    if ($matches.Count -eq 1) {
        $policy = $matches[0]
        if ([string]$policy.name -cne 'E2E Confirmation Policy' -or $policy.isActive -ne $true) {
            throw 'STOP: existing deterministic E2E confirmation policy conflicts on name or active state; no replacement was attempted.'
        }
    }
    else {
        $policyBody = @{ code = 'E2E-CONFIRMATION'; name = 'E2E Confirmation Policy'; description = 'Deterministic disposable-tenant confirmation policy'; isActive = $true } | ConvertTo-Json -Compress
        $policy = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/confirmation-policies' -Method POST -JsonBody $policyBody) 'Create deterministic E2E confirmation policy'
    }

    $versionsData = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/confirmation-policies/$($policy.id)/versions") 'Inspect deterministic E2E confirmation policy versions'
    $versions = @($versionsData.items)
    if ($versions.Count -gt 1) { throw 'STOP: deterministic E2E confirmation policy has multiple versions; no version was changed.' }

    if ($versions.Count -eq 1) {
        $version = $versions[0]
        $editor = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/confirmation-policies/$($policy.id)/editor?versionId=$($version.id)") 'Inspect deterministic E2E confirmation policy editor'
        Assert-ConfirmationPolicyEditorMatchesE2E -Editor $editor -DepartmentId $departmentId -EffectiveFrom $dateOfJoining
        if ([string]$version.status -eq 'Draft') {
            $null = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/confirmation-policies/$($policy.id)/versions/$($version.id)/publish" -Method POST) 'Publish deterministic E2E confirmation policy version'
            Write-Host 'Confirmation policy prerequisite: PUBLISHED existing deterministic policy version'
        }
        elseif ([string]$version.status -eq 'Published') { Write-Host 'Confirmation policy prerequisite: REUSED existing published deterministic policy version' }
        else { throw 'STOP: deterministic E2E confirmation policy version is not publishable.' }
    }
    else {
        $versionBody = @{
            effectiveFrom = $dateOfJoining.ToString('yyyy-MM-dd'); effectiveTo = $null; priority = 1
            probation = @{
                probationDuration = 1; probationDurationUnit = 'Days'; triggerDaysBeforeEnd = 0
                maxExtensions = 0; extensionDuration = 0; extensionDurationUnit = 'Months'
                requiredApproval = 'ManagerAndHrbp'; confirmRatingThreshold = $null; autoConfirmOnApproval = $true
                overallRatingMethod = 'AverageOfRatings'; approvalSequence = 'Parallel'; requireReviewerComments = $false
            }
            applicability = @(@{ departmentId = $departmentId.ToString() })
        } | ConvertTo-Json -Depth 10 -Compress
        $editor = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/confirmation-policies/$($policy.id)/versions" -Method POST -JsonBody $versionBody) 'Create deterministic E2E confirmation policy version'
        Assert-ConfirmationPolicyEditorMatchesE2E -Editor $editor -DepartmentId $departmentId -EffectiveFrom $dateOfJoining
        $version = $editor.version
        $null = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/confirmation-policies/$($policy.id)/versions/$($version.id)/publish" -Method POST) 'Publish deterministic E2E confirmation policy version'
        Write-Host 'Confirmation policy prerequisite: CREATED AND PUBLISHED deterministic policy'
    }

    return [pscustomobject]@{ Employee = $employee; DepartmentId = $departmentId; DateOfJoining = $dateOfJoining; PolicyId = [Guid]$policy.id; VersionId = [Guid]$version.id }
}

function Invoke-EmploymentConfirmationStage {
    Write-Host 'Stage 3: employment/probation/confirmation'

    $managerRecord = Get-ExpectedEmployee 'E2E-MANAGER'
    $employeeRecord = Get-ExpectedEmployee 'E2E-EMPLOYEE'
    $hrRecord = Get-ExpectedEmployee 'E2E-HRBP'
    if ($null -eq $managerRecord -or $null -eq $employeeRecord -or $null -eq $hrRecord) {
        throw 'STOP: Stage 3 requires the existing Stage 2 employee actors; no replacement actor will be created.'
    }
    $employeeId = [Guid]$employeeRecord.id
    $managerId = [Guid]$managerRecord.id
    $hrId = [Guid]$hrRecord.id
    if ($employeeId -eq $managerId -or $employeeId -eq $hrId -or $managerId -eq $hrId) { throw 'STOP: Stage 3 actor identities are not distinct.' }

    $manager = $null; $hr = $null; $employee = $null
    try {
        $manager = Invoke-LifecycleActorLogin -Email 'e2e-manager@e2e.invalid' -ExpectedRole 'Manager' -Label 'Reporting Manager'
        $hr = Invoke-LifecycleActorLogin -Email 'e2e-hrbp@e2e.invalid' -ExpectedRole 'HRBP' -Label 'HR/HRBP'

        $policySetup = Ensure-ConfirmationPolicyPrerequisite -EmployeeId $employeeId
        $status = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-policies/employees/$employeeId/status") 'Inspect lifecycle employee confirmation status'
        if ($status.employeeId -ne $employeeId) { throw 'STOP: confirmation status returned a different employee identity.' }
        if ($status.isPolicyManaged -ne $true -or [string]::IsNullOrWhiteSpace([string]$status.probationEndDate)) {
            throw 'STOP: no resolved production confirmation policy/probation end date applies to the existing lifecycle employee.'
        }
        if ([Guid]$status.confirmationPolicyId -ne $policySetup.PolicyId -or
            (Convert-ApiDateOnlyToDateTime -Value $status.dateOfJoining -Label 'confirmation status dateOfJoining') -ne $policySetup.DateOfJoining -or
            (Convert-ApiDateOnlyToDateTime -Value $status.probationEndDate -Label 'confirmation status probationEndDate') -ne $policySetup.DateOfJoining.AddDays(1)) {
            throw 'STOP: confirmation status does not reflect the deterministic policy, existing joining date, and one-day probation calculation.'
        }
        $resolveBody = @{ employeeId = $employeeId.ToString(); effectiveDate = $policySetup.DateOfJoining.ToString('yyyy-MM-dd') } | ConvertTo-Json -Compress
        $resolved = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path '/api/confirmation-policies/resolve' -Method POST -JsonBody $resolveBody) 'Recheck lifecycle confirmation policy resolution'
        if ([string]$resolved.status -cne 'Resolved' -or [int]$resolved.probationDuration -ne 1 -or
            [string]$resolved.probationDurationUnit -cne 'Days' -or
            (Convert-ApiDateOnlyToDateTime -Value $resolved.dateOfJoining -Label 'policy resolution dateOfJoining') -ne $policySetup.DateOfJoining -or
            (Convert-ApiDateOnlyToDateTime -Value $resolved.probationEndDate -Label 'policy resolution probationEndDate') -ne $policySetup.DateOfJoining.AddDays(1)) {
            throw 'STOP: production confirmation policy resolution did not return the expected effective date or probation calculation.'
        }

        $history = @(Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/employees/$employeeId/employment-history") 'Inspect lifecycle employee employment history')
        if ($history.Count -eq 0 -or @($history | Where-Object { $_.employmentStatus -eq 'Active' }).Count -eq 0) { throw 'STOP: lifecycle employee has no active persisted employment history.' }
        if ([string]$employeeRecord.employeeCode -cne 'E2E-EMPLOYEE' -or [string]$employeeRecord.email -ine 'e2e-employee@e2e.invalid') { throw 'STOP: lifecycle employee identity changed.' }

        $caseQuery = "/api/confirmation-review-cases?employeeId=$employeeId&page=1&pageSize=100"
        $caseList = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path $caseQuery) 'Inspect lifecycle confirmation review cases'
        $cases = @($caseList.items)
        if ($cases.Count -gt 1) { throw 'STOP: duplicate confirmation review cases exist for the lifecycle employee; no repair or deletion was attempted.' }

        # EmployeeEmployment is intentionally optional before a decision. The production decision service
        # creates it transactionally when applying Confirmed/NotConfirmed. Use the policy status projection
        # for resume detection here; the /employment resource is authoritative only after a decision.
        if ($null -ne $status.confirmationDate) {
            if ($cases.Count -ne 1) { throw 'STOP: employment is already confirmed but the persisted review case is missing.' }
            $caseId = [Guid]$cases[0].id
            if ([string]$cases[0].status -cne 'Decided') { throw 'STOP: confirmation date is present but the review case is not Decided.' }
            Write-Host 'Probation/review state: ALREADY CONFIRMED (resumed persisted state)'
        }
        else {
            if ($cases.Count -eq 0) {
                $createBody = @{ employeeId = $employeeId.ToString() } | ConvertTo-Json -Compress
                $createdCase = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path '/api/confirmation-review-cases' -Method POST -JsonBody $createBody) 'Initialize lifecycle probation review case'
                $caseId = [Guid]$createdCase.case.id
            }
            else { $caseId = [Guid]$cases[0].id }

            $caseDetail = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId") 'Inspect lifecycle confirmation review case'
            if ([Guid]$caseDetail.case.employeeId -ne $employeeId) { throw 'STOP: review case is not for the existing lifecycle employee.' }
            if ([Guid]$caseDetail.case.managerReviewerEmployeeId -ne $managerId) { throw 'STOP: review case manager assignment is not the Reporting Manager.' }
            if ($null -eq $caseDetail.case.hrbpReviewerEmployeeId -or [Guid]$caseDetail.case.hrbpReviewerEmployeeId -ne $hrId) { throw 'STOP: review case HRBP assignment is not the existing HR/HRBP actor.' }

            # The case DTO intentionally does not expose Outcome. A decided case is terminal, and its
            # authoritative outcome is the decision-history DTO. Treat a decided/confirmed resume as a
            # read-only terminal state; never probe it by POSTing another decision.
            $existingDecisionHistory = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/decision/history") 'Inspect existing confirmation decision history'
            $existingConfirmedDecisions = @($existingDecisionHistory.decisions | Where-Object { [string]$_.outcome -eq 'Confirmed' })
            if ([string]$caseDetail.case.status -eq 'Decided') {
                if ($existingConfirmedDecisions.Count -ne 1 -or $null -eq $status.confirmationDate) {
                    throw 'STOP: review case is terminal but its Confirmed decision and confirmation projection are inconsistent.'
                }
                Write-Host 'Probation/review state: ALREADY DECIDED/CONFIRMED (resumed persisted state)'
            }
            elseif ($existingConfirmedDecisions.Count -gt 0 -or $null -ne $status.confirmationDate) {
                throw 'STOP: confirmation decision history conflicts with the non-terminal review case/projection.'
            }
            else {

                $managerReview = @($caseDetail.reviews | Where-Object { [string]$_.reviewerType -eq 'ReportingManager' -and [string]$_.status -eq 'Submitted' })
                if ($managerReview.Count -eq 0) {
                    $form = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $manager -Path "/api/confirmation-review-cases/$caseId/reviewer-form?reviewerType=ReportingManager") 'Inspect manager reviewer form'
                    $reviewBody = @{ overallComments = 'E2E manager approval'; responses = @(Get-ConfirmationReviewInputs $form.questions) } | ConvertTo-Json -Depth 10 -Compress
                    $null = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $manager -Path "/api/confirmation-review-cases/$caseId/reviews/manager/submit" -Method POST -JsonBody $reviewBody) 'Submit Reporting Manager review'
                }

                $caseDetail = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId") 'Reinspect review case after manager action'
                $hrReview = @($caseDetail.reviews | Where-Object { [string]$_.reviewerType -eq 'Hrbp' -and [string]$_.status -eq 'Submitted' })
                if ($hrReview.Count -eq 0) {
                    $form = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/reviewer-form?reviewerType=Hrbp") 'Inspect HRBP reviewer form'
                    $reviewBody = @{ overallComments = 'E2E HRBP approval'; responses = @(Get-ConfirmationReviewInputs $form.questions) } | ConvertTo-Json -Depth 10 -Compress
                    $null = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/reviews/hrbp/submit" -Method POST -JsonBody $reviewBody) 'Submit HR/HRBP review'
                }

                # Run the negative authorization check before the legitimate decision so it cannot be
                # mistaken for, or occur after, the workflow mutation it is meant to guard.
                $employee = Invoke-LifecycleActorLogin -Email 'e2e-employee@e2e.invalid' -ExpectedRole 'Employee' -Label 'Lifecycle Employee authorization check'
                $negative = Invoke-LifecycleActorApi -Actor $employee -Path "/api/confirmation-review-cases/$caseId/decision" -Method POST -JsonBody (@{ outcome = 'Confirmed' } | ConvertTo-Json -Compress)
                if ($negative.StatusCode -notin @(401, 403)) { throw "STOP: lifecycle employee privileged confirmation denial expected 401/403, received HTTP $($negative.StatusCode)." }
                Write-Host 'Negative authorization: PASS (Employee denied confirmation decision)'
                $employee.AccessToken = $null; $employee = $null

                $preview = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/decision/preview" -Method POST) 'Preview confirmation decision'
                if ($preview.isReadyForDecision -ne $true -or @($preview.permittedOutcomes | ForEach-Object { [string]$_ }) -notcontains 'Confirmed') {
                    $reasons = @($preview.blockingReasons | ForEach-Object { [string]$_ }) -join '; '
                    throw "STOP: production decision preview is not ready for Confirmed; no decision mutation was attempted. Reasons: $reasons"
                }
                $decisionBody = @{ outcome = 'Confirmed'; decisionReason = 'E2E lifecycle confirmation' } | ConvertTo-Json -Compress
                $decision = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/decision" -Method POST -JsonBody $decisionBody) 'Submit HR/HRBP confirmation decision'
                if ([string]$decision.outcome -cne 'Confirmed' -or [string]$decision.newStatus -cne 'Decided') { throw 'STOP: confirmation decision response was not terminal Confirmed.' }
            }
        }

        # On already-confirmed resumes the case is terminal, so this is a pure authorization readback.
        if ($null -eq $employee) {
            $employee = Invoke-LifecycleActorLogin -Email 'e2e-employee@e2e.invalid' -ExpectedRole 'Employee' -Label 'Lifecycle Employee authorization check'
            $negative = Invoke-LifecycleActorApi -Actor $employee -Path "/api/confirmation-review-cases/$caseId/decision" -Method POST -JsonBody (@{ outcome = 'Confirmed' } | ConvertTo-Json -Compress)
            if ($negative.StatusCode -notin @(401, 403)) { throw "STOP: lifecycle employee privileged confirmation denial expected 401/403, received HTTP $($negative.StatusCode)." }
            Write-Host 'Negative authorization: PASS (Employee denied confirmation decision)'
        }

        # Final persisted-state verification is deliberately read-only and uses supported APIs.
        $finalEmployee = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/employees/$employeeId") 'Verify lifecycle employee identity'
        $finalEmployment = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/employees/$employeeId/employment") 'Verify confirmed employment'
        $finalStatus = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-policies/employees/$employeeId/status") 'Verify confirmation status'
        $finalCase = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId") 'Verify persisted review case'
        $reviewHistory = @(Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/history") 'Verify review audit history')
        $decisionHistory = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-review-cases/$caseId/decision/history") 'Verify decision history'
        if ([Guid]$finalEmployee.id -ne $employeeId -or [string]$finalEmployee.employeeCode -cne 'E2E-EMPLOYEE' -or [string]$finalEmployee.email -ine 'e2e-employee@e2e.invalid') { throw 'STOP: final employee identity verification failed.' }
        if ($null -eq $finalEmployment.confirmationDate -or [string]$finalEmployment.jobStatus -cne 'Confirmed') { throw 'STOP: final employment is not confirmed according to the persisted employment record.' }
        if ($finalStatus.confirmationDate -ne $finalEmployment.confirmationDate -or [string]$finalStatus.jobStatus -cne 'Confirmed') { throw 'STOP: confirmation status does not match persisted employment.' }
        $confirmedDecisions = @($decisionHistory.decisions | Where-Object { [string]$_.outcome -eq 'Confirmed' -and [string]$_.newCaseStatus -eq 'Decided' })
        if ([string]$finalCase.case.status -cne 'Decided' -or $confirmedDecisions.Count -ne 1) { throw 'STOP: final review case is not Decided/Confirmed according to the production case and decision-history DTOs.' }
        if (@($decisionHistory.decisions | Where-Object { [string]$_.outcome -eq 'Confirmed' }).Count -ne 1) { throw 'STOP: expected exactly one persisted Confirmed decision.' }
        if (@($reviewHistory | Where-Object { [string]$_.eventType -eq 'EmployeeConfirmed' }).Count -lt 1) { throw 'STOP: EmployeeConfirmed audit event is missing.' }
        if (@($finalCase.reviews | Where-Object { [string]$_.status -eq 'Submitted' }).Count -lt 1) { throw 'STOP: submitted reviewer records are missing.' }
        Write-Host 'Stage 3 employment/probation/confirmation: PASS'
    }
    finally {
        # Keep the authenticated employee actor in process memory for Stage 4. The token is
        # never persisted, printed, or copied to a file; Stage 4 validates the identity and
        # tenant metadata before reusing it. Manager/HR sessions are no longer needed here.
        if ($employee) { $script:lifecycleEmployeeSession = $employee }
        if ($manager) { $manager.AccessToken = $null }; if ($hr) { $hr.AccessToken = $null }
        $manager = $null; $hr = $null; $employee = $null
    }

    Write-Host 'Stage checkpoint: [C]ontinue next lifecycle stage, [X] exit safely'
    do { $next = (Read-Host 'Choice').Trim().ToUpperInvariant() } while ($next -notin @('C', 'X'))
    if ($next -eq 'C') { Invoke-LeaveLifecycleStage }
    else { Write-Host 'Exited at safe checkpoint.' }
}

function Ensure-LeaveConfiguration {
    param([Parameter(Mandatory)][Guid]$EmployeeId)

    $today = (Get-Date).Date
    $dateText = $today.ToString('yyyy-MM-dd')
    $leaveTypes = @(Get-AllRunnerPagedItems -BasePath '/api/leave-types' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query '&isActive=true' -Phase 'Inspect leave types')
    $typeMatches = @($leaveTypes | Where-Object { [string]$_.code -ceq 'E2E-LEAVE' -or [string]$_.name -ceq 'E2E Lifecycle Leave' })
    if ($typeMatches.Count -gt 1) { throw 'STOP: duplicate deterministic E2E leave types exist; no repair was attempted.' }
    if ($typeMatches.Count -eq 1 -and ([string]$typeMatches[0].code -cne 'E2E-LEAVE' -or [string]$typeMatches[0].name -cne 'E2E Lifecycle Leave' -or $typeMatches[0].isActive -ne $true)) {
        throw 'STOP: deterministic E2E leave type conflicts with the expected identity.'
    }
    $leaveType = if ($typeMatches.Count -eq 1) { $typeMatches[0] } else {
        $body = @{ code = 'E2E-LEAVE'; name = 'E2E Lifecycle Leave'; description = 'Deterministic disposable-tenant lifecycle leave'; defaultUnit = 'Day'; isPaid = $true; isCompOff = $false; isActive = $true } | ConvertTo-Json -Compress
        Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/leave-types' -Method POST -JsonBody $body) 'Create deterministic leave type'
    }

    $periodCode = "E2E-LEAVE-$($today.Year)"
    $periods = @(Get-AllRunnerPagedItems -BasePath '/api/leave-periods' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query '&isActive=true' -Phase 'Inspect leave periods')
    $periodMatches = @($periods | Where-Object { [string]$_.code -ceq $periodCode -or [string]$_.name -ceq "E2E Leave Period $($today.Year)" })
    if ($periodMatches.Count -gt 1) { throw 'STOP: duplicate deterministic E2E leave periods exist; no repair was attempted.' }
    if ($periodMatches.Count -eq 1 -and ([string]$periodMatches[0].code -cne $periodCode -or [string]$periodMatches[0].name -cne "E2E Leave Period $($today.Year)" -or $periodMatches[0].isActive -ne $true)) { throw 'STOP: deterministic E2E leave period conflicts with the expected identity.' }
    $period = if ($periodMatches.Count -eq 1) {
        if ((Convert-ApiDateOnlyToDateTime $periodMatches[0].startDate 'leave period start') -gt $today -or (Convert-ApiDateOnlyToDateTime $periodMatches[0].endDate 'leave period end') -lt $today) { throw 'STOP: deterministic E2E leave period does not cover the current business date.' }
        $periodMatches[0]
    } else {
        $body = @{ code = "E2E-LEAVE-$($today.Year)"; name = "E2E Leave Period $($today.Year)"; startDate = "$(Get-Date -Year $today.Year -Month 1 -Day 1 -Format yyyy-MM-dd)"; endDate = "$(Get-Date -Year $today.Year -Month 12 -Day 31 -Format yyyy-MM-dd)"; isActive = $true } | ConvertTo-Json -Compress
        Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/leave-periods' -Method POST -JsonBody $body) 'Create deterministic leave period'
    }

    $policies = @(Get-AllRunnerPagedItems -BasePath '/api/leave-policies' -PageSize $E2EMaxPageSize -ApiMaximum $E2EMaxPageSize -Query '&isActive=true' -Phase 'Inspect leave policies')
    $policyMatches = @($policies | Where-Object { [string]$_.code -ceq 'E2E-LEAVE' -or [string]$_.name -ceq 'E2E Lifecycle Leave Policy' })
    if ($policyMatches.Count -gt 1) { throw 'STOP: duplicate deterministic E2E leave policies exist; no repair was attempted.' }
    if ($policyMatches.Count -eq 1 -and ([string]$policyMatches[0].code -cne 'E2E-LEAVE' -or [string]$policyMatches[0].name -cne 'E2E Lifecycle Leave Policy')) { throw 'STOP: deterministic E2E leave policy conflicts with the expected identity.' }
    $policy = if ($policyMatches.Count -eq 1) { $policyMatches[0] } else {
        $body = @{ code = 'E2E-LEAVE'; name = 'E2E Lifecycle Leave Policy'; description = 'Deterministic disposable-tenant lifecycle leave policy'; isActive = $true } | ConvertTo-Json -Compress
        Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/leave-policies' -Method POST -JsonBody $body) 'Create deterministic leave policy'
    }

    $versionsData = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$($policy.id)/versions") 'Inspect deterministic leave policy versions'
    $versionSelection = Resolve-DeterministicLeavePolicyVersion $versionsData
    if ($versionSelection.DuplicateCount -gt 0) { Write-Host "Leave policy version response: deduplicated $($versionSelection.DuplicateCount) duplicate representation(s) by persisted id." }
    $version = $versionSelection.Version
    if ($versionSelection.Kind -eq 'Published') {
        $publishedVersion = $version
        $configuredTypes = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$($policy.id)/versions/$($publishedVersion.id)/leave-types") 'Verify published leave policy types')
        if (@($configuredTypes | Where-Object { [string]$_.id -ieq ([string]$leaveType.id) }).Count -ne 1) { throw 'STOP: published deterministic leave policy does not include the deterministic leave type.' }
        $policyTest = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/leave-policies/test' -Method POST -JsonBody (@{ employeeId = $EmployeeId.ToString(); leaveTypeId = ([Guid]$leaveType.id).ToString(); date = $today.ToString('yyyy-MM-dd') } | ConvertTo-Json -Compress)) 'Verify leave policy resolution'
        if ([Guid]$policyTest.policyId -ne [Guid]$policy.id -or [Guid]$policyTest.policyVersionId -ne [Guid]$publishedVersion.id -or [string]$policyTest.status -cne 'Published') { throw 'STOP: published deterministic leave policy is not the resolved production policy for the lifecycle employee.' }
        return [pscustomobject]@{ LeaveType = $leaveType; Period = $period; Policy = $policy; Version = $publishedVersion; Reused = $true }
    }
    if ($versionSelection.Kind -eq 'None') {
        # Version creation is a first-version fallback only.  A persisted Draft
        # is authoritative for this deterministic policy, even when its number
        # is not the highest version number.  Resolve-DeterministicLeavePolicyVersion
        # stops before this branch when more than one Draft is present.
        if ($versionSelection.DistinctCount -ne 0) {
            throw "STOP: deterministic leave policy inventory was not empty but no selectable version exists; no new version was created. $($versionSelection.Diagnostics)"
        }
        $versionBody = @{ effectiveFrom = $today.ToString('yyyy-MM-dd'); priority = 100 } | ConvertTo-Json -Compress
        $version = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$($policy.id)/versions" -Method POST -JsonBody $versionBody) 'Create deterministic leave policy version'
    }
    $versionId = Convert-RunnerGuid $version.id 'leave policy version id'
    $policyId = Convert-RunnerGuid $policy.id 'leave policy id'
    if ([string]$version.status -cne 'Draft') { throw "STOP: selected deterministic leave policy version is not an editable Draft; no configuration mutation was attempted. Version $versionId has status '$($version.status)'." }
    $typeId = Convert-RunnerGuid $leaveType.id 'leave type id'
    $employeeDetail = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/employees/$EmployeeId") 'Inspect lifecycle employee detail for leave policy applicability'
    $employeeDepartmentId = Convert-RunnerGuid $employeeDetail.departmentId 'employee detail department id'

    # Every configuration endpoint validates the draft version's ModifiedDate token.  A
    # successful mutation changes that token, so the next mutation must use a fresh GET.
    # This helper intentionally performs one mutation only: a 409 is a safe stop, never a
    # blind replay with the stale payload.
    function Get-CurrentLeaveVersion {
        param([Guid]$PolicyId, [Guid]$VersionId, [string]$Phase = 'Reload leave policy version')
        return Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$PolicyId/versions/$VersionId") $Phase
    }
    function Invoke-LeaveConfigurationMutation {
        param([string]$Path, [string]$Body, [string]$Phase, [Guid]$PolicyId, [Guid]$VersionId)
        $current = Get-CurrentLeaveVersion -PolicyId $PolicyId -VersionId $VersionId -Phase "$Phase (refresh concurrency token)"
        if ([string]::IsNullOrWhiteSpace([string]$current.concurrencyToken)) { throw "STOP: Stage 4 [$Phase] draft version returned no concurrency token." }
        $payload = $Body | ConvertFrom-Json
        $payload.concurrencyToken = [string]$current.concurrencyToken
        $null = Get-TenantDataOrStop (Invoke-TenantApi -Path $Path -Method PUT -JsonBody ($payload | ConvertTo-Json -Compress)) $Phase
        return Get-CurrentLeaveVersion -PolicyId $PolicyId -VersionId $VersionId
    }

    $configuredTypes = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types") 'Inspect leave policy types')
    if ($configuredTypes.Count -gt 0 -and ($configuredTypes.Count -ne 1 -or (Convert-RunnerGuid $configuredTypes[0].id 'configured leave type id') -ne $typeId)) { throw 'STOP: deterministic draft contains conflicting leave-type selection; no unrelated configuration was replaced.' }
    if ($configuredTypes.Count -eq 0) {
        $body = @{ leaveTypeIds = @($typeId.ToString()) } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types" $body 'Configure leave policy type' $policyId $versionId
    }

    $applicability = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/applicability") 'Inspect leave policy applicability')
    $matchingGroup = @($applicability | Where-Object { [Guid]$_.departmentId -eq $employeeDepartmentId })
    if ($applicability.Count -gt 0 -and ($applicability.Count -ne 1 -or $matchingGroup.Count -ne 1)) { throw 'STOP: deterministic draft contains conflicting applicability groups; no unrelated configuration was replaced.' }
    if ($applicability.Count -eq 0) {
        $body = @{ groups = @(@{ departmentId = $employeeDepartmentId.ToString() }) } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/applicability" $body 'Configure leave policy applicability' $policyId $versionId
    }

    $eligibility = Get-OptionalTenantDataOrNull (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/eligibility") 'Inspect leave eligibility'
    if ($null -eq $eligibility -or [string]$eligibility.eligibilityMode -cne 'Immediate' -or [string]$eligibility.probationMode -cne 'AfterConfirmation' -or [string]$eligibility.noticePeriodMode -cne 'Allowed') {
        $body = @{ eligibilityMode = 'Immediate'; probationMode = 'AfterConfirmation'; noticePeriodMode = 'Allowed' } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/eligibility" $body 'Configure leave eligibility' $policyId $versionId
    }

    $entitlement = Get-OptionalTenantDataOrNull (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/entitlement") 'Inspect leave entitlement'
    if ($null -eq $entitlement -or [string]$entitlement.entitlementMode -cne 'Allocated' -or [string]$entitlement.entitlementSource -cne 'ExternalGrant' -or [decimal]$entitlement.entitlementQuantity -ne 10 -or [string]$entitlement.accrualFrequency -cne 'None' -or $entitlement.proratePartialPeriod -ne $false -or $entitlement.carryForwardEnabled -ne $false) {
        $body = @{ entitlementMode = 'Allocated'; entitlementSource = 'ExternalGrant'; entitlementQuantity = 10; accrualFrequency = 'None'; proratePartialPeriod = $false; carryForwardEnabled = $false } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/entitlement" $body 'Configure leave entitlement' $policyId $versionId
    }

    $requestRules = Get-OptionalTenantDataOrNull (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/request-rules") 'Inspect leave request rules'
    if ($null -eq $requestRules -or [decimal]$requestRules.minimumRequestQuantity -ne 1 -or [decimal]$requestRules.maximumRequestQuantity -ne 1 -or [decimal]$requestRules.maximumConsecutiveQuantity -ne 1 -or [int]$requestRules.minimumAdvanceNoticeDays -ne 0 -or [string]$requestRules.backdatedRequestMode -cne 'NotAllowed' -or [string]$requestRules.partialDayMode -cne 'FullDayOnly') {
        $body = @{ minimumRequestQuantity = 1; maximumRequestQuantity = 1; maximumConsecutiveQuantity = 1; minimumAdvanceNoticeDays = 0; backdatedRequestMode = 'NotAllowed'; partialDayMode = 'FullDayOnly' } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/request-rules" $body 'Configure leave request rules' $policyId $versionId
    }

    $calendar = Get-OptionalTenantDataOrNull (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/calendar") 'Inspect leave calendar rules'
    if ($null -eq $calendar -or [string]$calendar.holidayTreatment -cne 'Exclude' -or [string]$calendar.weekOffTreatment -cne 'Exclude' -or [string]$calendar.sandwichMode -cne 'Disabled' -or $calendar.applyToPrefix -ne $false -or $calendar.applyToSuffix -ne $false -or $calendar.applyToBetween -ne $false) {
        $body = @{ holidayTreatment = 'Exclude'; weekOffTreatment = 'Exclude'; sandwichMode = 'Disabled'; applyToPrefix = $false; applyToSuffix = $false; applyToBetween = $false } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/calendar" $body 'Configure leave calendar rules' $policyId $versionId
    }

    $attachments = Get-OptionalTenantDataOrNull (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/attachments") 'Inspect leave attachments'
    if ($null -eq $attachments -or [string]$attachments.attachmentRequirement -cne 'None') {
        $body = @{ attachmentRequirement = 'None' } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/attachments" $body 'Configure leave attachments' $policyId $versionId
    }

    $cancellation = Get-OptionalTenantDataOrNull (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/cancellation") 'Inspect leave cancellation'
    if ($null -eq $cancellation -or $cancellation.withdrawAllowed -ne $true -or $cancellation.cancelAllowed -ne $true -or $cancellation.modifyAllowed -ne $false) {
        $body = @{ withdrawAllowed = $true; cancelAllowed = $true; modifyAllowed = $false } | ConvertTo-Json -Compress
        $version = Invoke-LeaveConfigurationMutation "/api/leave-policies/$policyId/versions/$versionId/leave-types/$typeId/cancellation" $body 'Configure leave cancellation' $policyId $versionId
    }

    $clubbing = @(Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/clubbing") 'Inspect leave clubbing rules')
    if ($clubbing.Count -gt 0) { throw 'STOP: deterministic draft contains conflicting clubbing rules; no unrelated configuration was replaced.' }
    $validation = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/validate" -Method POST) 'Validate deterministic leave policy version'
    if ($validation.isValid -ne $true) { throw 'STOP: deterministic leave policy version is not valid for publication.' }
    $published = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-policies/$policyId/versions/$versionId/publish" -Method POST -JsonBody (@{ acknowledgeOverlap = $false } | ConvertTo-Json -Compress)) 'Publish deterministic leave policy version'
    return [pscustomobject]@{ LeaveType = $leaveType; Period = $period; Policy = $policy; Version = $published; Reused = $false }
}

function Ensure-LeaveOpeningBalance {
    param([Parameter(Mandatory)]$Configuration)
    $filename = 'E2E-LEAVE-BALANCE.csv'
    $history = Get-TenantDataOrStop (Invoke-TenantApi -Path '/api/leave-balances/import/history') 'Inspect leave balance import history'
    $matches = @($history | Where-Object { [string]$_.fileName -ceq $filename })
    if ($matches.Count -gt 1) { throw 'STOP: duplicate deterministic leave balance imports exist; no repair was attempted.' }
    if ($matches.Count -eq 1) {
        $batch = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-balances/import/$($matches[0].id)") 'Inspect deterministic leave balance import'
        if ([string]$batch.status -in @('Failed','Rejected')) { throw "STOP: deterministic leave balance import is in terminal status '$($batch.status)'; no retry was attempted." }
        if ([string]$batch.status -notin @('Committed','Completed')) {
            if ([int]$batch.invalidRows -ne 0 -or [string]$batch.status -cne 'Validated') { throw "STOP: deterministic leave balance import is not safely committable (status '$($batch.status)', invalid rows $($batch.invalidRows))." }
            $batch = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-balances/import/$($matches[0].id)/commit" -Method POST) 'Commit deterministic leave balance import'
        }
        Write-Host 'Leave configuration: REUSED'
        return
    }
    $effective = (Get-Date).ToString('yyyy-MM-dd')
    $csv = "EmployeeCode,LeaveTypeCode,LeavePeriod,OpeningBalance,EffectiveDate,Remarks`r`nE2E-EMPLOYEE,$([string]$Configuration.LeaveType.code),$([string]$Configuration.Period.code),10,$effective,E2E lifecycle opening balance`r`n"
    $validated = Get-TenantDataOrStop (Invoke-TenantMultipartApi -Path '/api/leave-balances/import/validate' -FileName $filename -FileText $csv) 'Validate deterministic leave balance import'
    $committed = Get-TenantDataOrStop (Invoke-TenantApi -Path "/api/leave-balances/import/$($validated.id)/commit" -Method POST) 'Commit deterministic leave balance import'
    if ([int]$committed.importedRows -ne 1) { throw 'STOP: deterministic leave balance import did not import exactly one row.' }
    Write-Host 'Leave configuration: CREATED'
}

function Select-LeaveDate {
    param([Parameter(Mandatory)][Guid]$LeaveTypeId)
    for ($offset = 1; $offset -le 60; $offset++) {
        $candidate = (Get-Date).Date.AddDays($offset)
        $body = @{ leaveTypeId = $LeaveTypeId.ToString(); startDate = $candidate.ToString('yyyy-MM-dd'); endDate = $candidate.ToString('yyyy-MM-dd'); idempotencyKey = 'E2E-LEAVE-REQUEST-V1' } | ConvertTo-Json -Compress
        $preview = Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path '/api/leave-requests/preview' -Method POST -JsonBody $body
        if ($preview.StatusCode -ge 200 -and $preview.StatusCode -lt 300 -and $null -ne $preview.Json -and $preview.Json.success -eq $true) {
            $data = $preview.Json.data
            if ([decimal]$data.chargeableQuantity -eq 1 -and @($data.requestDays).Count -eq 1) { return $candidate }
        }
    }
    throw 'STOP: no valid one-working-day leave date was found in the next 60 production calendar days.'
}

function Invoke-LeaveLifecycleStage {
    Write-Host 'Stage 4: leave lifecycle'
    $employeeRecord = Get-ExpectedEmployee 'E2E-EMPLOYEE'; $managerRecord = Get-ExpectedEmployee 'E2E-MANAGER'
    if ($null -eq $employeeRecord -or $null -eq $managerRecord) { throw 'STOP: Stage 4 requires the existing lifecycle employee and reporting manager.' }
    $employeeId = Convert-RunnerGuid $employeeRecord.id 'lifecycle employee id'; $managerId = Convert-RunnerGuid $managerRecord.id 'reporting manager id'
    $script:leaveEmployee = $null; $manager = $null
    try {
        $script:leaveEmployee = Get-LifecycleEmployeeSession
        $manager = Invoke-LifecycleActorLogin -Email 'e2e-manager@e2e.invalid' -ExpectedRole 'Manager' -Label 'Reporting Manager leave'
        $script:lastStage4Operation = 'Ensure deterministic leave configuration'
        $configuration = Ensure-LeaveConfiguration -EmployeeId $employeeId
        Ensure-LeaveOpeningBalance -Configuration $configuration
        $balancesBefore = @(Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path '/api/leave-balances/mine') 'Verify leave eligibility and balance before request')
        $before = @($balancesBefore | Where-Object { [string]$_.leaveTypeCode -ceq 'E2E-LEAVE' })
        if ($before.Count -ne 1 -or [decimal]$before[0].availableQuantity -lt 1) { throw 'STOP: lifecycle employee is not eligible with at least one available leave day.' }
        Write-Host 'Leave eligibility: PASS'; Write-Host 'Leave balance before request: VERIFIED'

        $requestsData = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path '/api/leave-requests?page=1&pageSize=100') 'Inspect lifecycle employee leave requests'
        $requests = Get-RunnerItems $requestsData 'leave request items'
        $matching = @($requests | Where-Object { [string]$_.leaveTypeId -ieq $configuration.LeaveType.id.ToString() -and [string]$_.idempotencyKey -ceq 'E2E-LEAVE-REQUEST-V1' })
        if ($matching.Count -gt 1) { throw 'STOP: duplicate deterministic leave requests exist; no repair was attempted.' }
        if ($matching.Count -eq 1) { $requestId = Convert-RunnerGuid $matching[0].id 'existing leave request id'; Write-Host 'Leave request: REUSED' }
        else {
            $leaveDate = Select-LeaveDate -LeaveTypeId ([Guid]$configuration.LeaveType.id)
            $body = @{ leaveTypeId = ([Guid]$configuration.LeaveType.id).ToString(); startDate = $leaveDate.ToString('yyyy-MM-dd'); endDate = $leaveDate.ToString('yyyy-MM-dd'); idempotencyKey = 'E2E-LEAVE-REQUEST-V1' } | ConvertTo-Json -Compress
            $submitted = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path '/api/leave-requests' -Method POST -JsonBody $body) 'Submit lifecycle employee leave request'
            $requestId = Convert-RunnerGuid $submitted.requestId 'submitted leave request id'; Write-Host 'Leave request: CREATED'
        }
        $detail = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path "/api/leave-requests/$requestId") 'Verify lifecycle leave request'
        if ([Guid]$detail.employeeId -ne $employeeId -or [Guid]$detail.leaveTypeId -ne [Guid]$configuration.LeaveType.id -or [string]$detail.status -notin @('PendingApproval','Approved')) { throw 'STOP: leave request projection is inconsistent with the deterministic employee workflow.' }
        $negative = Invoke-ApiRaw -Path "/api/leave-requests/$requestId/approve" -HostName $state.Tenant.Host -BearerToken $script:leaveEmployee.AccessToken -Method POST
        if ($negative.StatusCode -notin @(401,403)) { throw "STOP: employee self-approval denial expected 401/403, received HTTP $($negative.StatusCode)." }
        $inboxData = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $manager -Path '/api/leave-approvals?page=1&pageSize=100') 'Verify manager leave inbox'
        $inbox = Get-RunnerItems $inboxData 'leave approval items'
        $inboxMatch = @($inbox | Where-Object { [string]$_.requestId -ieq $requestId.ToString() })
        if ($inboxMatch.Count -ne 1) { throw 'STOP: Reporting Manager cannot see exactly one subordinate leave request in the production approval inbox.' }
        if ([string]$detail.status -eq 'PendingApproval') { $null = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $manager -Path "/api/leave-requests/$requestId/approve" -Method POST) 'Approve lifecycle leave request'; Write-Host 'Manager approval: PASS' } else { Write-Host 'Manager approval: REUSED' }
        $final = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path "/api/leave-requests/$requestId") 'Verify final leave request'
        if ([string]$final.status -cne 'Approved' -or @($final.events | Where-Object { [string]$_.eventType -eq 'Approved' }).Count -ne 1) { throw 'STOP: final leave request is not Approved with exactly one approval event.' }
        $balancesAfter = @(Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path '/api/leave-balances/mine') 'Verify leave balance after approval')
        $after = @($balancesAfter | Where-Object { [string]$_.leaveTypeCode -ceq 'E2E-LEAVE' })
        if ($after.Count -ne 1 -or [decimal]$after[0].consumedQuantity -ne ([decimal]$before[0].consumedQuantity + 1) -or [decimal]$after[0].reservedQuantity -ne 0) { throw 'STOP: approved leave balance did not move from one reserved day to one consumed day.' }
        $accountingPath = "/api/leave-reports/accounting?employeeId=$employeeId&leaveTypeId=$([Guid]$configuration.LeaveType.id)&page=1&pageSize=100"
        $accountingData = Get-TenantDataOrStop (Invoke-TenantApi -Path $accountingPath) 'Verify leave accounting history'
        $accounting = Get-RunnerItems $accountingData 'leave accounting items'
        if (@($accounting | Where-Object { [string]$_.eventType -match 'Consumption' }).Count -ne 1) { throw 'STOP: expected exactly one consumption entry for the deterministic leave request.' }
        Write-Host 'Leave balance/ledger: PASS'
        $attendance = Invoke-ApiRaw -Path "/api/attendance/me/days/$($final.startDate)" -HostName $state.Tenant.Host -BearerToken $script:leaveEmployee.AccessToken
        if ($attendance.StatusCode -eq 200 -and $attendance.Json.success -eq $true) {
            if ([string]$attendance.Json.data.day.attendanceStatus -cne 'OnLeave') { throw 'STOP: synchronous approved-leave attendance state is not OnLeave.' }
            Write-Host 'Attendance interaction: PASS'
        } elseif ($attendance.StatusCode -in @(401,403)) { Write-Host 'Attendance interaction: DEFERRED TO ATTENDANCE STAGE (production access scope)' }
        else { throw "STOP: attendance leave-side verification failed with HTTP $($attendance.StatusCode)." }
        Write-Host 'Negative authorization: PASS (Employee denied self-approval; Manager team scope verified)'
        $repeat = Get-TenantDataOrStop (Invoke-LifecycleActorApi -Actor $script:leaveEmployee -Path '/api/leave-requests' -Method POST -JsonBody (@{ leaveTypeId = ([Guid]$configuration.LeaveType.id).ToString(); startDate = $final.startDate; endDate = $final.endDate; idempotencyKey = 'E2E-LEAVE-REQUEST-V1' } | ConvertTo-Json -Compress)) 'Verify leave submission idempotency'
        if ($repeat.requestId -ne $requestId -or $repeat.isReplay -ne $true) { throw 'STOP: leave submission replay did not reuse the existing request.' }
        Write-Host 'Stage 4 leave lifecycle: PASS'
    }
    finally { if ($manager) { $manager.AccessToken = $null }; $manager = $null; $script:leaveEmployee = $null }
    Write-Host 'Stage checkpoint: [C]ontinue next lifecycle stage, [X] exit safely'
    do { $next = (Read-Host 'Choice').Trim().ToUpperInvariant() } while ($next -notin @('C','X'))
    if ($next -eq 'C') { Write-Host 'Next lifecycle stage is not implemented yet; remaining in controlled checkpoint.' } else { Write-Host 'Exited at safe checkpoint.' }
}

function Assert-ExpectedRuntimeIdentity {
    param([Parameter(Mandatory)]$Info)
    if ([string]$Info.catalogDatabase -cne $ExpectedCatalog) {
        throw "E2E API identity mismatch: expected isolated catalog $ExpectedCatalog."
    }
    if ([string]$Info.databaseProvider -notmatch 'MySql') {
        throw 'E2E API identity mismatch: expected MySql catalog provider.'
    }
    $e2eTenants = @($Info.tenants | Where-Object { [string]$_.tenantCode -ceq 'E2E01' })
    if ($e2eTenants.Count -ne 1 -or
        [Guid]$e2eTenants[0].tenantId -ne $expectedTenantId -or
        [string]$e2eTenants[0].databaseProvider -cne 'MySql' -or
        [string]$e2eTenants[0].shardKey -cne 'e2e01') {
        throw 'E2E API identity mismatch: expected exactly one E2E01 tenant with the authorized identity.'
    }
    if ($null -eq $Info.platformUser -or $Info.platformUser.exists -ne $true) {
        throw 'E2E API identity mismatch: expected platform administrator is not present in the live catalog.'
    }
}

function Assert-SameTenantSnapshot {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Before,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$After
    )
    $beforeById = @{}
    foreach ($item in $Before) { $beforeById[[string]$item.id] = $item }
    foreach ($item in $After) {
        $id = [string]$item.id
        if (-not $beforeById.ContainsKey($id)) { continue }
        $old = $beforeById[$id]
        foreach ($property in @('tenantCode', 'tenantName', 'host', 'databaseProvider', 'shardKey', 'status')) {
            if ([string]$old.$property -cne [string]$item.$property) {
                throw "Existing tenant '$($old.tenantCode)' changed during the run."
            }
        }
    }
}

function Assert-ExpectedE2ETenant {
    param([Parameter(Mandatory)]$Tenant)
    $checks = @{
        id = $expectedTenantId
        tenantCode = 'E2E01'
        tenantName = 'E2E Employee Lifecycle'
        host = 'e2e01.localhost'
        databaseProvider = 'MySql'
        shardKey = 'e2e01'
    }
    foreach ($property in $checks.Keys) {
        if ([string]$Tenant.$property -cne $checks[$property]) {
            throw "Unsafe resume: existing tenant is not the expected E2E01 tenant ($property mismatch). No tenant was created."
        }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($ExpectedCatalog)) { throw 'ExpectedCatalog must be supplied.' }

    # This gate deliberately runs before Read-Host asks for a password. A 401 against another
    # catalog is a configuration failure, not an authentication failure.
    $runtimeInfo = Get-SystemInfo
    Assert-ExpectedRuntimeIdentity -Info $runtimeInfo
    Write-Host "E2E API identity: PASS ($ExpectedCatalog; MySql; E2E01/$expectedTenantId; platform admin present)"

    $email = Read-Host 'Platform admin email'
    $securePassword = Read-Host 'Platform admin password' -AsSecureString
    $plainPassword = $null
    $loginJson = $null
    try {
        $plainPassword = Convert-SecureStringToPlaintext $securePassword
        if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw 'Platform admin password was empty.' }
        $loginJson = @{ email = $email; password = $plainPassword } | ConvertTo-Json -Compress
        $loginResponse = Invoke-ApiRaw -Path '/api/platform/auth/login' -HostName $platformHost -Method POST -JsonBody $loginJson
        $loginData = Get-DataOrStop -Response $loginResponse -Phase 'Platform login'
        $state.AccessToken = [string]$loginData.accessToken
        $state.RefreshToken = [string]$loginData.refreshToken
        if ([string]::IsNullOrWhiteSpace($state.AccessToken) -or [string]::IsNullOrWhiteSpace($state.RefreshToken)) { throw 'Platform login returned no usable token.' }
    }
    finally {
        $plainPassword = $null
        $loginJson = $null
        $loginResponse = $null
        $loginData = $null
        if ($securePassword) { $securePassword.Dispose() }
        $securePassword = $null
    }

    $null = Get-DataOrStop -Response (Invoke-AuthenticatedApi -Path '/api/platform/auth/me') -Phase 'Platform /me'
    Write-Host 'Platform login: PASS'
    Write-Host 'Platform /me: PASS'

    $beforeInfo = Get-SystemInfo
    $beforeTenants = @(Get-DataOrStop -Response (Invoke-AuthenticatedApi -Path '/api/platform/tenants') -Phase 'Initial platform tenant list')
    $initialCount = $beforeTenants.Count
    Write-Host "Initial tenant count: $initialCount"
    $resumeMode = $false
    if ($initialCount -eq 0) {
        Write-Host 'Runner mode: Fresh (empty catalog; tenant creation is allowed).'
    }
    elseif ($initialCount -eq 1) {
        Assert-ExpectedE2ETenant -Tenant $beforeTenants[0]
        $resumeMode = $true
        $state.Tenant = [pscustomobject]@{ Id = [Guid]$beforeTenants[0].id; Code = [string]$beforeTenants[0].tenantCode; Provider = [string]$beforeTenants[0].databaseProvider; ShardKey = [string]$beforeTenants[0].shardKey; Host = [string]$beforeTenants[0].host }
        Write-Host 'Runner mode: Resume (the exact expected disposable E2E01 tenant already exists).'
    }
    else {
        $codes = @($beforeTenants | ForEach-Object { [string]$_.tenantCode }) -join ', '
        throw "Unsafe resume: catalog contains $initialCount tenants ($codes). No tenant was created."
    }

    $createRequest = @{
        tenantName = 'E2E Employee Lifecycle'
        tenantCode = 'E2E01'
        host = 'e2e01.localhost'
        databaseProvider = 'MySql'
        shardKey = 'e2e01'
        email = 'e2e01@e2e.invalid'
        firstName = 'E2E'
        lastName = 'Administrator'
        initialAdminEmail = 'e2e-admin@e2e.invalid'
    } | ConvertTo-Json -Compress

    if (-not $resumeMode) {
        $createdResponse = Invoke-AuthenticatedApi -Path '/api/platform/tenants' -Method POST -JsonBody $createRequest
        $created = Get-DataOrStop -Response $createdResponse -Phase 'Tenant creation'
        $createRequest = $null
        if ([string]$created.tenantCode -cne 'E2E01' -or [string]$created.databaseProvider -cne 'MySql') { throw 'Tenant creation returned unexpected identity.' }
        $state.Tenant = [pscustomobject]@{ Id = [Guid]$created.id; Code = [string]$created.tenantCode; Provider = [string]$created.databaseProvider; ShardKey = [string]$created.shardKey; Host = [string]$created.host }
        $createdResponse = $null
        $created = $null
        Write-Host 'Tenant creation: PASS'
    }
    else {
        $createRequest = $null
        Write-Host 'Tenant creation: SKIPPED (resume mode; existing E2E01 preserved)'
    }

    $afterTenants = @(Get-DataOrStop -Response (Invoke-AuthenticatedApi -Path '/api/platform/tenants') -Phase 'Final platform tenant list')
    $afterInfo = Get-SystemInfo
    if ($afterTenants.Count -ne 1) { throw "Final tenant count was $($afterTenants.Count), expected 1." }
    if ($afterTenants[0].id -ne $state.Tenant.Id -or [string]$afterTenants[0].tenantCode -cne 'E2E01') { throw 'Created tenant was not found in the final tenant list.' }
    Assert-ExpectedE2ETenant -Tenant $afterTenants[0]
    if ($afterTenants[0].status -cne 'Active') { throw 'E2E01 is not Active; provisioning is incomplete and no automatic repair was attempted.' }
    if ([int]$afterInfo.tenantCount -ne 1) { throw 'System persistence inspection did not observe exactly one tenant.' }
    if ($afterInfo.databaseProvider -notmatch 'MySql') { throw 'System persistence inspection did not observe the MySQL provider.' }
    Assert-SameTenantSnapshot -Before @($beforeTenants) -After @($afterTenants)

    $summary = @($afterInfo.tenants | Where-Object { [string]$_.tenantCode -eq 'E2E01' })
    if ($summary.Count -ne 1 -or [int]$summary[0].userCount -lt 1) { throw 'Tenant shard was not readable with required seeded administrator data.' }

    Write-Host "Tenant ID: $($state.Tenant.Id)"
    Write-Host "Tenant code: $($state.Tenant.Code)"
    Write-Host "Provider: $($state.Tenant.Provider)"
    Write-Host "Routing/shard: $($state.Tenant.ShardKey)"
    Write-Host 'Migrations: PASS (provisioning completed and shard is queryable)'
    Write-Host 'Seed: PASS (system inspection found seeded administrator data)'
    Write-Host 'Final tenant count: 1'
    Write-Host "E2E catalog: $ExpectedCatalog"
    $tenantModeMessage = 'Existing catalog/tenants/shards: PASS (precondition was empty and no pre-existing rows changed)'
    if ($resumeMode) { $tenantModeMessage = 'Existing catalog/tenants/shards: PASS (exact E2E01 identity resumed; no tenant was created)' }
    Write-Host $tenantModeMessage
    Write-Host 'Provisioning verification: PASS'
    Write-Host 'Session checkpoint: authenticated state retained in this process only.'

    if (-not $NoCheckpoint) {
        Write-Host 'Checkpoint menu: [C]ontinue later stages in this process, [X] exit safely'
        do { $choice = (Read-Host 'Choice').Trim().ToUpperInvariant() } while ($choice -notin @('C', 'X'))
        if ($choice -eq 'C') { Invoke-OrganizationStage }
        else { Write-Host 'Exited at safe checkpoint.' }
    }
}
catch {
    $safeMessage = $_.Exception.Message
    $safeMessage = $safeMessage -replace '(?i)(password|token|otp|connection string|connectionstring|invitation url|invite url)[^.;]*', '$1 redacted'
    Write-Error "E2E RUNNER STOPPED: Stage 4 operation '$script:lastStage4Operation': $safeMessage"
    exit 1
}
finally {
    $plainPassword = $null; $loginJson = $null; $createRequest = $null
    $state.AccessToken = $null; $state.RefreshToken = $null
    $tenantState.AccessToken = $null; $tenantState.RefreshToken = $null
    if ($client) { $client.Dispose() }
}
