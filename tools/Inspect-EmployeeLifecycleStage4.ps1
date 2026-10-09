[CmdletBinding()]
param([string]$ApiBaseUrl = 'http://localhost:5080', [switch]$SkipInspection)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
. (Join-Path $PSScriptRoot 'LeavePolicyVersionSelection.ps1')
$client = [System.Net.Http.HttpClient]::new()
$platformHost = 'platform.localhost'
$tenantHost = 'e2e01.localhost'
$tokens = @{}

function Convert-SecureStringToPlaintext {
    param([Security.SecureString]$Value)
    $ptr = [IntPtr]::Zero
    try { $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value); return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) } }
}

function Get-ResponseShape {
    param([object]$Json, [string]$RawText, [string]$ParseError)
    if ($ParseError) { return "malformed-json(bodyLength=$($RawText.Length))" }
    if ($null -eq $Json) { return if ([string]::IsNullOrEmpty($RawText)) { 'empty-body' } else { 'json-null' } }
    if ($Json -is [System.Collections.IEnumerable] -and $Json -isnot [string]) { return "array(count=$(@($Json).Count))" }
    $properties = @($Json.PSObject.Properties.Name | Sort-Object)
    return "object(properties=$($properties -join ','))"
}

function Invoke-Api {
    param([string]$HostName, [string]$Path, [ValidateSet('GET','POST')][string]$Method = 'GET', [string]$Body, [string]$Bearer, [string]$Operation = $Path)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), [Uri]::new(($ApiBaseUrl.TrimEnd('/') + $Path)))
    $request.Headers.Host = $HostName
    if ($Bearer) { $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Bearer) }
    if ($Body) { $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json') }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $parseError = $null
        try { $json = if ($text) { $text | ConvertFrom-Json } else { $null } }
        catch { $json = $null; $parseError = $_.Exception.Message }
        [pscustomobject]@{
            Status = [int]$response.StatusCode; Json = $json; RawText = $text; ParseError = $parseError
            Operation = $Operation; Method = $Method; Path = $Path
            ResponseShape = Get-ResponseShape $json $text $parseError
        }
    } finally { $request.Dispose(); if ($response) { $response.Dispose() } }
}

function Login {
    param([string]$HostName, [string]$Email, [string]$Label)
    $secure = Read-Host "$Label password" -AsSecureString
    $plain = $null
    try {
        $plain = Convert-SecureStringToPlaintext $secure
        $body = @{ email = $Email; password = $plain } | ConvertTo-Json -Compress
        $result = Invoke-Api -HostName $HostName -Path '/api/auth/login' -Method POST -Body $body -Operation "$Label login"
        if ($result.Status -lt 200 -or $result.Status -ge 300 -or $result.ParseError -or -not $result.Json.success -or -not $result.Json.data.accessToken) {
            throw "$Label authentication failed (HTTP $($result.Status), shape $($result.ResponseShape))."
        }
        return [string]$result.Json.data.accessToken
    } finally { $plain = $null; $body = $null; $result = $null }
}

function Get-Read {
    param([string]$HostName, [string]$Path, [string]$Bearer, [string]$Label = $Path)
    $result = Invoke-Api -HostName $HostName -Path $Path -Bearer $Bearer -Operation $Label
    if ($result.Status -eq 401 -or $result.Status -eq 403) {
        throw "AUTHENTICATION/AUTHORIZATION FAILURE: $Label [GET $Path] returned HTTP $($result.Status), shape $($result.ResponseShape)."
    }
    if ($result.Status -eq 204) { throw "MALFORMED RESPONSE: $Label [GET $Path] returned HTTP 204 without the required API envelope." }
    if ($result.Status -eq 404) {
        $message = if ($result.Json) { [string]$result.Json.message } else { '' }
        throw "UNEXPECTED 404: $Label [GET $Path] returned HTTP 404, shape $($result.ResponseShape). API message: $message"
    }
    if ($result.Status -lt 200 -or $result.Status -ge 300 -or $result.ParseError -or $null -eq $result.Json -or $null -eq $result.Json.success -or -not $result.Json.success -or $null -eq $result.Json.PSObject.Properties['data']) {
        throw "MALFORMED/FAILED RESPONSE: $Label [GET $Path] returned HTTP $($result.Status), shape $($result.ResponseShape)."
    }
    if ($null -eq $result.Json.data) { throw "MALFORMED RESPONSE: $Label [GET $Path] returned HTTP 200 with null data." }
    return (, $result.Json.data)
}

function Get-OptionalConfiguration {
    param([string]$HostName, [string]$Path, [string]$Bearer, [string]$Label)
    $result = Invoke-Api -HostName $HostName -Path $Path -Bearer $Bearer -Operation $Label
    if ($result.Status -eq 401 -or $result.Status -eq 403) {
        throw "AUTHENTICATION/AUTHORIZATION FAILURE: $Label [GET $Path] returned HTTP $($result.Status), shape $($result.ResponseShape)."
    }
    if ($result.Status -eq 204) { throw "MALFORMED RESPONSE: $Label [GET $Path] returned HTTP 204 without an optional API envelope." }
    if ($result.Status -eq 404) {
        $message = if ($result.Json) { [string]$result.Json.message } else { '' }
        if ($message -match '(?i)selected LeaveType is not assigned to this policy version') {
            return 'NOT CONFIGURED'
        }
        throw "UNEXPECTED 404: $Label [GET $Path] returned HTTP 404, shape $($result.ResponseShape). API message: $message"
    }
    if ($result.Status -lt 200 -or $result.Status -ge 300 -or $result.ParseError -or $null -eq $result.Json -or $null -eq $result.Json.success -or -not $result.Json.success -or $null -eq $result.Json.PSObject.Properties['data']) { throw "MALFORMED/FAILED RESPONSE: $Label [GET $Path] returned HTTP $($result.Status), shape $($result.ResponseShape)." }
    if ($null -eq $result.Json.data) { return 'NOT CONFIGURED' }
    return (, $result.Json.data)
}

function Get-OptionalAuthorizationRead {
    param([string]$HostName, [string]$Path, [string]$Bearer, [string]$Label)
    $result = Invoke-Api -HostName $HostName -Path $Path -Bearer $Bearer -Operation $Label
    if ($result.Status -eq 401 -or $result.Status -eq 403) {
        return [pscustomobject]@{ status = 'NOT AUTHORIZED'; operation = $Label; method = 'GET'; endpoint = $Path; httpStatus = $result.Status; responseShape = $result.ResponseShape }
    }
    if ($result.Status -eq 404) {
        $message = if ($result.Json) { [string]$result.Json.message } else { '' }
        throw "UNEXPECTED 404: $Label [GET $Path] returned HTTP 404, shape $($result.ResponseShape). API message: $message"
    }
    if ($result.Status -eq 204) { return [pscustomobject]@{ status = 'NO CONTENT'; httpStatus = 204 } }
    if ($result.Status -lt 200 -or $result.Status -ge 300 -or $result.ParseError -or $null -eq $result.Json -or $null -eq $result.Json.success -or -not $result.Json.success -or $null -eq $result.Json.PSObject.Properties['data']) { throw "MALFORMED/FAILED RESPONSE: $Label [GET $Path] returned HTTP $($result.Status), shape $($result.ResponseShape)." }
    return (, $result.Json.data)
}

function Get-Items([object]$Data, [string]$Operation = 'collection read') {
    if ($null -eq $Data) { throw "MALFORMED RESPONSE: $Operation returned HTTP 200 with null data; expected a collection or paged envelope." }
    if ($null -ne $Data.PSObject.Properties['items']) {
        if ($null -eq $Data.items) { throw "MALFORMED RESPONSE: $Operation returned a paged object with null items." }
        return @($Data.items)
    }
    if ($Data -is [System.Collections.IEnumerable] -and $Data -isnot [string]) { return @($Data) }
    throw "MALFORMED RESPONSE: $Operation returned shape $($Data.GetType().Name); expected a collection or paged envelope."
}
function Read-OptionalForInventory([scriptblock]$Read, [string]$Operation) {
    try { return & $Read }
    catch { return [pscustomobject]@{ status = 'UNAVAILABLE'; operation = $Operation; diagnostic = $_.Exception.Message } }
}
function Get-ConfigurationStatus([object]$Value) {
    if ($Value -is [string]) { return [string]$Value }
    if ($null -eq $Value) { return 'NOT CONFIGURED' }
    if ($Value.PSObject.Properties['status'] -and $Value.status -eq 'UNAVAILABLE') { return 'UNAVAILABLE' }
    if (@($Value).Count -eq 0) { return 'NOT CONFIGURED' }
    return 'CONFIGURED'
}
function Pick([object[]]$Items, [string]$Code, [string]$Name) { @($Items | Where-Object { [string]$_.code -ceq $Code -or [string]$_.name -ceq $Name }) }

if ($SkipInspection) { return }

try {
    $tokens.admin = Login $tenantHost 'e2e-admin@e2e.invalid' 'Tenant admin'
    $tokens.employee = Login $tenantHost 'e2e-employee@e2e.invalid' 'Lifecycle employee'
    $tokens.manager = Login $tenantHost 'e2e-manager@e2e.invalid' 'Reporting manager'

    $types = Get-Items (Get-Read $tenantHost '/api/leave-types?page=1&pageSize=100' $tokens.admin 'leave types') 'GET /api/leave-types?page=1&pageSize=100'
    $periods = Get-Items (Get-Read $tenantHost '/api/leave-periods?page=1&pageSize=100' $tokens.admin 'leave periods') 'GET /api/leave-periods?page=1&pageSize=100'
    $policies = Get-Items (Get-Read $tenantHost '/api/leave-policies?page=1&pageSize=100' $tokens.admin 'leave policies') 'GET /api/leave-policies?page=1&pageSize=100'
    $employees = Get-Items (Get-Read $tenantHost '/api/employees?page=1&pageSize=100&search=E2E' $tokens.admin 'E2E employees') 'GET /api/employees?page=1&pageSize=100&search=E2E'
    $employee = @($employees | Where-Object { $_.employeeCode -ceq 'E2E-EMPLOYEE' })[0]
    $manager = @($employees | Where-Object { $_.employeeCode -ceq 'E2E-MANAGER' })[0]
    $leaveTypes = @(Pick $types 'E2E-LEAVE' 'E2E Lifecycle Leave')
    $leavePolicies = @(Pick $policies 'E2E-LEAVE' 'E2E Lifecycle Leave Policy')
    $today = (Get-Date).Date.ToString('yyyy-MM-dd')
    $activePeriods = @($periods | Where-Object { $_.isActive -eq $true -and [string]$_.startDate -le $today -and [string]$_.endDate -ge $today })

    $policyDetails = @()
    foreach ($policy in $leavePolicies) {
        $versionResponse = Get-Read $tenantHost "/api/leave-policies/$($policy.id)/versions" $tokens.admin "policy $($policy.code) versions"
        $versionNormalization = Get-LeavePolicyVersionInventory $versionResponse
        if ($versionNormalization.MissingIdentityCount -gt 0) { throw "MALFORMED RESPONSE: policy $($policy.code) versions [GET /api/leave-policies/$($policy.id)/versions] contained $($versionNormalization.MissingIdentityCount) item(s) without a persisted version id." }
        if ($versionNormalization.UnknownStatusCount -gt 0) { throw "MALFORMED RESPONSE: policy $($policy.code) versions [GET /api/leave-policies/$($policy.id)/versions] contained unknown version status values." }
        $versions = @($versionNormalization.Items)
        $versionDetails = @()
        foreach ($version in $versions) {
            $base = "/api/leave-policies/$($policy.id)/versions/$($version.id)"
            $selectedLeaveTypes = Read-OptionalForInventory { Get-Items (Get-Read $tenantHost "$base/leave-types" $tokens.admin "policy version $($version.versionNumber) leave types") "policy version $($version.versionNumber) leave types" } "GET $base/leave-types"
            $applicability = Read-OptionalForInventory { Get-Items (Get-Read $tenantHost "$base/applicability" $tokens.admin "policy version $($version.versionNumber) applicability") "policy version $($version.versionNumber) applicability" } "GET $base/applicability"
            $ruleDetails = @()
            foreach ($selectedLeaveType in @($selectedLeaveTypes | Where-Object { $_.PSObject.Properties['id'] })) {
                $leaveTypeBase = "$base/leave-types/$($selectedLeaveType.id)"
                $ruleDetails += [pscustomobject]@{
                    leaveTypeId = $selectedLeaveType.id
                    leaveTypeCode = $selectedLeaveType.code
                    eligibility = Read-OptionalForInventory { Get-OptionalConfiguration $tenantHost "$leaveTypeBase/eligibility" $tokens.admin 'eligibility' } "GET $leaveTypeBase/eligibility"
                    entitlement = Read-OptionalForInventory { Get-OptionalConfiguration $tenantHost "$leaveTypeBase/entitlement" $tokens.admin 'entitlement' } "GET $leaveTypeBase/entitlement"
                    requestRules = Read-OptionalForInventory { Get-OptionalConfiguration $tenantHost "$leaveTypeBase/request-rules" $tokens.admin 'request rules' } "GET $leaveTypeBase/request-rules"
                    calendar = Read-OptionalForInventory { Get-OptionalConfiguration $tenantHost "$leaveTypeBase/calendar" $tokens.admin 'calendar rules' } "GET $leaveTypeBase/calendar"
                    attachments = Read-OptionalForInventory { Get-OptionalConfiguration $tenantHost "$leaveTypeBase/attachments" $tokens.admin 'attachment rules' } "GET $leaveTypeBase/attachments"
                    cancellation = Read-OptionalForInventory { Get-OptionalConfiguration $tenantHost "$leaveTypeBase/cancellation" $tokens.admin 'cancellation rules' } "GET $leaveTypeBase/cancellation"
                }
            }
            $clubbing = Read-OptionalForInventory { Get-Items (Get-Read $tenantHost "$base/clubbing" $tokens.admin "policy version $($version.versionNumber) clubbing") "policy version $($version.versionNumber) clubbing" } "GET $base/clubbing"
            $versionDetails += [pscustomobject]@{
                id = $version.id; versionNumber = $version.versionNumber; status = $version.status; effectiveFrom = $version.effectiveFrom; effectiveTo = $version.effectiveTo
                hasCurrentConcurrencyToken = -not [string]::IsNullOrWhiteSpace([string]$version.concurrencyToken)
                leaveTypes = $selectedLeaveTypes
                leaveTypesStatus = Get-ConfigurationStatus $selectedLeaveTypes
                applicability = $applicability
                applicabilityStatus = Get-ConfigurationStatus $applicability
                rules = $ruleDetails
                clubbing = $clubbing
                clubbingStatus = Get-ConfigurationStatus $clubbing
                configurationCompleteness = [pscustomobject]@{
                    leaveTypes = Get-ConfigurationStatus $selectedLeaveTypes
                    applicability = Get-ConfigurationStatus $applicability
                    rules = if (@($ruleDetails | Where-Object { (Get-ConfigurationStatus $_.eligibility) -ne 'CONFIGURED' -or (Get-ConfigurationStatus $_.entitlement) -ne 'CONFIGURED' -or (Get-ConfigurationStatus $_.requestRules) -ne 'CONFIGURED' -or (Get-ConfigurationStatus $_.calendar) -ne 'CONFIGURED' -or (Get-ConfigurationStatus $_.attachments) -ne 'CONFIGURED' -or (Get-ConfigurationStatus $_.cancellation) -ne 'CONFIGURED' }).Count -gt 0) { 'PARTIAL' } elseif ($ruleDetails.Count -eq 0) { 'NOT CONFIGURED' } else { 'CONFIGURED' }
                    clubbing = Get-ConfigurationStatus $clubbing
                }
            }
        }
        $policyDetails += [pscustomobject]@{ id = $policy.id; code = $policy.code; name = $policy.name; isActive = $policy.isActive; versionInventoryClassification = $versionNormalization.Classification; draftCount = $versionNormalization.DraftCount; publishedCount = $versionNormalization.PublishedCount; conflictingPublishedVersionNumberCount = $versionNormalization.ConflictingPublishedVersionNumberCount; versionResponseRawCount = $versionNormalization.RawCount; distinctPersistedVersionCount = $versions.Count; duplicateRepresentationCount = $versionNormalization.DuplicateCount; versions = $versionDetails }
    }

    $imports = Get-Items (Get-Read $tenantHost '/api/leave-balances/import/history?page=1&pageSize=100' $tokens.admin 'balance import history') 'GET /api/leave-balances/import/history?page=1&pageSize=100'
    $balances = Get-Items (Get-Read $tenantHost '/api/leave-balances/mine' $tokens.employee 'employee balances') 'GET /api/leave-balances/mine'
    $requests = Get-Items (Get-Read $tenantHost '/api/leave-requests?page=1&pageSize=100' $tokens.employee 'employee leave requests') 'GET /api/leave-requests?page=1&pageSize=100'
    $deterministicRequests = @($requests | Where-Object { $_.idempotencyKey -ceq 'E2E-LEAVE-REQUEST-V1' })
    $requestDetails = @($deterministicRequests | ForEach-Object { Get-Read $tenantHost "/api/leave-requests/$($_.id)" $tokens.employee })
    $approvals = Get-Items (Get-Read $tenantHost '/api/leave-approvals?page=1&pageSize=100' $tokens.manager 'manager approvals') 'GET /api/leave-approvals?page=1&pageSize=100'
    $accounting = if ($employee -and $leaveTypes.Count -eq 1) { Get-Items (Get-Read $tenantHost "/api/leave-reports/accounting?employeeId=$($employee.id)&leaveTypeId=$($leaveTypes[0].id)&page=1&pageSize=100" $tokens.admin 'leave accounting') "GET /api/leave-reports/accounting" } else { @() }
    $attendance = Read-OptionalForInventory { Get-OptionalAuthorizationRead $tenantHost "/api/attendance/me/days/$today" $tokens.employee 'employee attendance' } "GET /api/attendance/me/days/$today"

    [pscustomobject]@{
        readOnlyInspection = $true; tenant = 'E2E01'; inspectedAtUtc = [DateTime]::UtcNow.ToString('O')
        employee = if ($employee) { [pscustomobject]@{ id = $employee.id; code = $employee.employeeCode; status = $employee.status } } else { $null }
        manager = if ($manager) { [pscustomobject]@{ id = $manager.id; code = $manager.employeeCode; status = $manager.status } } else { $null }
        leaveTypeMatches = @($leaveTypes | Select-Object id,code,name,isActive,defaultUnit,isPaid,isCompOff)
        activePeriods = @($activePeriods | Select-Object id,code,name,startDate,endDate,isActive)
        leavePolicyMatches = $policyDetails
        employeeBalances = $balances | Select-Object id,employeeId,leaveTypeId,leavePeriodId,grantedQuantity,reservedQuantity,consumedQuantity,availableQuantity
        balanceImports = $imports | Select-Object id,fileName,status,totalRows,validRows,invalidRows,importedRows,uploadedAtUtc,completedAtUtc
        deterministicRequests = @($requestDetails | Select-Object id,employeeId,leaveTypeId,startDate,endDate,status,chargeableQuantity,idempotencyKey,events)
        managerApprovalMatches = @($approvals | Where-Object { $deterministicRequests.id -contains $_.requestId } | Select-Object requestId,status,employeeId,startDate,endDate)
        accounting = @($accounting | Select-Object id,leaveRequestId,transactionType,quantity,eventType,createdAtUtc)
        attendance = $attendance
    } | ConvertTo-Json -Depth 12
}
finally { $tokens.Clear(); $client.Dispose() }
