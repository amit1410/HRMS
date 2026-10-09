Describe 'Inspect-EmployeeLifecycleStage4 authentication and safety' {
    BeforeAll {
        $scriptPath = Join-Path $PSScriptRoot '..\Inspect-EmployeeLifecycleStage4.ps1'
        $source = Get-Content -LiteralPath $scriptPath -Raw
    }

    It 'logs the tenant administrator against the E2E01 tenant host' {
        $source.Contains("`$tokens.admin = Login `$tenantHost 'e2e-admin@e2e.invalid' 'Tenant admin'") | Should Be $true
        $source.Contains("`$tokens.admin = Login `$platformHost 'e2e-admin@e2e.invalid'") | Should Be $false
        $source.Contains("`$tenantHost = 'e2e01.localhost'") | Should Be $true
    }

    It 'uses the tenant login endpoint and the expected JSON DTO' {
        $source.Contains("Invoke-Api -HostName `$HostName -Path '/api/auth/login' -Method POST -Body `$body") | Should Be $true
        $source.Contains('@{ email = $Email; password = $plain } | ConvertTo-Json -Compress') | Should Be $true
        $source.Contains('$plain = Convert-SecureStringToPlaintext $secure') | Should Be $true
    }

    It 'constructs the Host header independently from the localhost API URL' {
        $source.Contains('$request.Headers.Host = $HostName') | Should Be $true
        $source.Contains('$ApiBaseUrl.TrimEnd(''/'') + $Path') | Should Be $true
    }

    It 'keeps the inspection read-only after authentication' {
        $source.Contains("`$result = Invoke-Api -HostName `$HostName -Path '/api/auth/login' -Method POST") | Should Be $true
        $source -match "Invoke-Api[^`r`n]*-Method (PUT|PATCH|DELETE)" | Should Be $false
        $source -match "`$tokens\.(admin|employee|manager)\s*=\s*`$null" | Should Be $false
        $source.Contains('readOnlyInspection = $true') | Should Be $true
    }

    It 'does not persist passwords or bearer tokens' {
        $source.Contains('ZeroFreeBSTR') | Should Be $true
        $source -match 'Set-Content|Out-File|Export-Clixml' | Should Be $false
        $source -match 'Write-(Host|Output|Error).*\$(plain|secure|Bearer|tokens)' | Should Be $false
    }

    It 'uses the production eligibility route and classifies only the documented unassigned-leave-type 404 as optional' {
        $source.Contains('"$leaveTypeBase/eligibility"') | Should Be $true
        $source.Contains('selected LeaveType is not assigned to this policy version') | Should Be $true
        $source.Contains("return 'NOT CONFIGURED'") | Should Be $true
        $source.Contains('UNEXPECTED 404') | Should Be $true
    }

    It 'does not treat authorization failures as missing configuration' {
        $source.Contains('AUTHORIZATION FAILURE') | Should Be $true
        $source.Contains("status = 'NOT AUTHORIZED'") | Should Be $true
        $source.Contains('$result.Status -eq 401 -or $result.Status -eq 403') | Should Be $true
    }

    It 'audits every optional draft rule and the remaining read-only resources' {
        foreach ($path in @('eligibility', 'entitlement', 'request-rules', 'calendar', 'attachments', 'cancellation')) {
            $source.Contains(('"$leaveTypeBase/' + $path + '"')) | Should Be $true
        }
        $source.Contains('"$base/clubbing"') | Should Be $true
        $source.Contains("'/api/leave-balances/import/history?page=1&pageSize=100'") | Should Be $true
        $source.Contains("'/api/leave-balances/mine'") | Should Be $true
        $source.Contains("'/api/leave-requests?page=1&pageSize=100'") | Should Be $true
        $source.Contains("'/api/leave-approvals?page=1&pageSize=100'") | Should Be $true
        $source.Contains('/api/leave-reports/accounting?') | Should Be $true
        $source.Contains('/api/attendance/me/days/$today') | Should Be $true
    }

    It 'keeps the inspector PowerShell 5.1 compatible' {
        $source -match '\?\?|\?\.' | Should Be $false
    }
}

Describe 'Inspect-EmployeeLifecycleStage4 optional configuration response semantics' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '..\Inspect-EmployeeLifecycleStage4.ps1') -SkipInspection
    }

    It 'accepts the production 404 for an unassigned leave type as NOT CONFIGURED' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 404; Json = [pscustomobject]@{ message = 'The selected LeaveType is not assigned to this policy version.' } } } -Scope It
        Get-OptionalConfiguration 'e2e01.localhost' '/api/test' 'token' 'eligibility' | Should Be 'NOT CONFIGURED'
    }

    It 'fails an unrelated 404 instead of treating it as optional' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 404; Json = [pscustomobject]@{ message = 'The policy version was not found.' } } } -Scope It
        { Get-OptionalConfiguration 'e2e01.localhost' '/api/test' 'token' 'eligibility' } | Should Throw 'UNEXPECTED 404'
    }

    It 'fails required reads explicitly on authorization errors' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 403; Json = [pscustomobject]@{ message = 'Forbidden.' } } } -Scope It
        { Get-Read 'e2e01.localhost' '/api/test' 'token' } | Should Throw 'AUTHORIZATION FAILURE'
    }

    It 'reports an optional read as NOT CONFIGURED when the API returns successful null data' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 200; Json = [pscustomobject]@{ success = $true; data = $null } } } -Scope It
        Get-OptionalConfiguration 'e2e01.localhost' '/api/test' 'token' 'eligibility' | Should Be 'NOT CONFIGURED'
    }

    It 'accepts an empty array in a successful collection envelope' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 200; Json = [pscustomobject]@{ success = $true; data = @() } } } -Scope It
        @(Get-Items (Get-Read 'e2e01.localhost' '/api/test' 'token' 'empty collection') 'empty collection').Count | Should Be 0
    }

    It 'accepts an empty paged collection envelope' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 200; Json = [pscustomobject]@{ success = $true; data = [pscustomobject]@{ items = @(); page = 1; pageSize = 100; totalCount = 0 } } } } -Scope It
        @(Get-Items (Get-Read 'e2e01.localhost' '/api/test' 'token' 'empty page') 'empty page').Count | Should Be 0
    }

    It 'rejects HTTP 204 for a required read' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 204; Json = $null; ResponseShape = 'empty-body' } } -Scope It
        { Get-Read 'e2e01.localhost' '/api/test' 'token' 'required read' } | Should Throw 'HTTP 204'
    }

    It 'rejects malformed JSON with operation and response shape diagnostics' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 200; Json = $null; ParseError = 'bad json'; ResponseShape = 'malformed-json(bodyLength=4)' } } -Scope It
        { Get-Read 'e2e01.localhost' '/api/test' 'token' 'malformed read' } | Should Throw 'malformed read'
    }

    It 'keeps an unexpected 404 distinct from the documented optional 404' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 404; Json = [pscustomobject]@{ message = 'policy version was not found' }; ResponseShape = 'object(properties=message)' } } -Scope It
        { Get-OptionalConfiguration 'e2e01.localhost' '/api/test' 'token' 'eligibility' } | Should Throw 'UNEXPECTED 404'
    }

    It 'does not convert an unauthorized response to missing configuration' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 401; Json = [pscustomobject]@{ message = 'Unauthorized' }; ResponseShape = 'object(properties=message)' } } -Scope It
        { Get-OptionalConfiguration 'e2e01.localhost' '/api/test' 'token' 'eligibility' } | Should Throw 'AUTHENTICATION/AUTHORIZATION FAILURE'
    }

    It 'retains an optional 204 as an explicit no-content result' {
        Mock Invoke-Api { [pscustomobject]@{ Status = 204; Json = $null; ResponseShape = 'empty-body' } } -Scope It
        { Get-OptionalConfiguration 'e2e01.localhost' '/api/test' 'token' 'eligibility' } | Should Throw 'HTTP 204'
    }

    It 'continues inventory after an unavailable optional resource without making it an empty collection' {
        $value = Read-OptionalForInventory { throw 'UNEXPECTED 404: optional resource' } 'GET /api/optional'
        $value.status | Should Be 'UNAVAILABLE'
        $value.diagnostic | Should Match 'UNEXPECTED 404'
    }
}
