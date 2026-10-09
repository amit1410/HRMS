Describe 'Run-EmployeeLifecycleE2E security and lifecycle gates' {
    BeforeAll {
        $scriptPath = Join-Path $PSScriptRoot '..\Run-EmployeeLifecycleE2E.ps1'
        $source = Get-Content -LiteralPath $scriptPath -Raw
    }

    It 'reads the password as a SecureString and does not persist tokens' {
        $source | Should Match "Read-Host 'Platform admin password' -AsSecureString"
        $source | Should Match '\$state\.AccessToken = \$null'
        $source | Should Match '\$state\.RefreshToken = \$null'
        $source | Should Not Match 'Set-Content.*Token|Out-File.*Token|Export-Clixml'
    }

    It 'stops on a non-empty catalog before POST creation' {
        $source | Should Match 'Unsafe resume: catalog contains'
        $source | Should Match 'No tenant was created\.'
        $source.IndexOf('Unsafe resume: catalog contains') | Should BeGreaterThan -1
        $source.IndexOf('Unsafe resume: catalog contains') | Should BeLessThan $source.IndexOf('-Method POST -JsonBody $createRequest')
    }

    It 'allows an empty before collection and preserves zero-to-one comparison' {
        $source | Should Match '\[AllowEmptyCollection\(\)\]\[object\[\]\]\$Before'
        $source | Should Match 'Assert-SameTenantSnapshot -Before @\(\$beforeTenants\) -After @\(\$afterTenants\)'
        $source | Should Match 'if \(\$initialCount -eq 0\)'
    }

    It 'supports one-to-one resume comparison without creating another tenant' {
        $source | Should Match 'elseif \(\$initialCount -eq 1\)'
        $source | Should Match 'Assert-ExpectedE2ETenant -Tenant \$beforeTenants\[0\]'
        $source | Should Match '\$resumeMode = \$true'
        $source | Should Match 'Tenant creation: SKIPPED \(resume mode; existing E2E01 preserved\)'
        $source | Should Match 'if \(-not \$resumeMode\)'
    }

    It 'requires exact E2E01 identity and stops for arbitrary or multiple tenants' {
        foreach ($expected in @('tenantCode = ''E2E01''', 'tenantName = ''E2E Employee Lifecycle''', 'host = ''e2e01.localhost''', 'databaseProvider = ''MySql''', 'shardKey = ''e2e01''')) {
            $source | Should Match ([regex]::Escape($expected))
        }
        $source | Should Match 'else \{\r?\n        \$codes'
        $source | Should Match 'Unsafe resume: existing tenant is not the expected E2E01 tenant'
    }

    It 'retains tenant identity in process state after provisioning' {
        $source | Should Match '\$state\.Tenant = \[pscustomobject\]'
        $source | Should Match 'Session checkpoint: authenticated state retained in this process only'
    }

    It 'uses refresh rotation without printing either token' {
        $source | Should Match '/api/platform/auth/refresh'
        $source | Should Not Match 'Write-Host.*AccessToken|Write-Host.*RefreshToken|Write-Output.*Token'
    }

    It 'supports all HTTP verbs without requiring a body' {
        $source | Should Match "ValidateSet\('GET', 'HEAD', 'DELETE', 'POST', 'PUT', 'PATCH'\)"
    }

    It 'uses the Employees API paging and search contract for deterministic inspection' {
        $source | Should Match 'Get-AllRunnerPagedItems -BasePath ''/api/employees'' -PageSize \$E2EMaxPageSize -ApiMaximum \$E2EMaxPageSize'
        $source | Should Match '\$encodedEmployeeCode = \[Uri\]::EscapeDataString\(\$EmployeeCode\)'
        $source | Should Not Match '/api/employees\?page=1&pageSize=200'
    }

    It 'centralizes endpoint paging contracts and guards invalid requests locally' {
        $source | Should Match '\$E2EMaxPageSize = 100'
        $source | Should Match '\$E2EMasterMaxPageSize = 200'
        $source | Should Match 'Runner paging contract violation'
        $source | Should Match 'Get-AllRunnerPagedItems'
        $source | Should Not Match 'pageSize=200'
    }

    It 'includes safe API validation details in HTTP failures' {
        $source | Should Match 'foreach \(\$propertyName in @\(''title'', ''detail'', ''message''\)\)'
        $source | Should Match '\$errorsProperty = \$Response\.Json\.psobject\.Properties\[''errors''\]'
        $source | Should Match '\$detail = \$message\.Trim\(\)'
    }

    It 'does not attach content when an omitted body becomes an empty string' {
        $source | Should Match 'if \(-not \[string\]::IsNullOrWhiteSpace\(\$JsonBody\)\)'
        $source | Should Not Match 'if \(\$null -ne \$JsonBody\)'
    }

    It 'routes /me and tenant-list through bodyless GET calls' {
        $source | Should Match 'Invoke-AuthenticatedApi -Path ''/api/platform/auth/me''\)'
        $source | Should Match 'Invoke-AuthenticatedApi -Path ''/api/platform/tenants''\)'
        $source | Should Match 'Invoke-ApiRaw -Path ''/api/system/info'' -HostName \$platformHost'
    }

    It 'keeps JSON bodies on login and tenant creation' {
        $source | Should Match 'auth/login.*-Method POST -JsonBody \$loginJson'
        $source | Should Match '''/api/platform/tenants'' -Method POST -JsonBody \$createRequest'
        $source | Should Match 'auth/refresh''.*-Method POST -JsonBody \$body'
    }

    It 'does not persist or print passwords or token values' {
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml|ConvertTo-Json.*AccessToken|ConvertTo-Json.*RefreshToken'
        $source | Should Not Match 'Write-(Host|Output|Error).*\$(plainPassword|loginJson|AccessToken|RefreshToken)'
        $source | Should Match 'ZeroFreeBSTR'
    }

    It 'does not add a provisioning repair mutation to resume verification' {
        $source | Should Not Match 'retry-provisioning'
        $source | Should Match 'provisioning is incomplete and no automatic repair was attempted'
    }

    It 'enters organization setup in-process when C is selected' {
        $source | Should Match 'if \(\$choice -eq ''C''\) \{ Invoke-OrganizationStage \}'
        $source | Should Not Match 'Session ready for subsequent lifecycle stages\.'
        $source | Should Match 'Stage 2: organization setup and actor creation'
    }

    It 'uses supported tenant-admin login and invitation activation without inventing credentials' {
        $source | Should Match "'/api/auth/login'"
        $source | Should Match "expectedTenantAdminEmail = 'e2e-admin@e2e.invalid'"
        $source | Should Match 'Tenant administrator password for \$expectedTenantAdminEmail.*-AsSecureString'
        $source | Should Match "'/api/auth/set-password'"
        $source | Should Match 'Invitation URL from the configured E2E email sink'
        $source | Should Match '\$login\.user\.roles.*TenantAdmin'
        $source | Should Not Match 'Password\s*=\s*''|password\s*=\s*'''
    }

    It 'offers supported recovery for the exact active E2E tenant administrator' {
        $source | Should Match '\[L\] Login with known password, \[R\] Recover/reset password, \[A\] Activation pending'
        $source | Should Match "expectedTenantId = \[Guid\]'4056dce7-9011-445d-b785-ea1d30957878'"
        $source | Should Match "'/api/auth/forgot-password'"
        $source | Should Match "'/api/auth/forgot-password/send-otp'"
        $source | Should Match "'/api/auth/forgot-password/verify-otp'"
        $source | Should Match "'/api/auth/forgot-password/reset'"
        $source | Should Match 'Development recovery OTP was not returned'
        $source | Should Match 'recovery is permitted only for the exact disposable E2E01 tenant'
        $source | Should Match 'E2E recovery unavailable: running API is not configured for Development recovery delivery'
        $source | Should Match 'passwordRecoveryEmailProvider'
    }

    It 'keeps recovery secrets in memory and verifies the reset session' {
        $source | Should Match 'New tenant administrator password.*-AsSecureString'
        $source | Should Match 'Confirm new tenant administrator password.*-AsSecureString'
        $source | Should Match 'password confirmation did not match; no reset request was made'
        $source | Should Match '\$resetToken = \$null'
        $source | Should Match 'Tenant administrator recovery: PASS'
        $source | Should Match "'/api/auth/me'"
        $source | Should Match '\$login\.user\.roles.*TenantAdmin'
        $source | Should Not Match 'Write-(Host|Output|Error).*\$(otp|resetToken|newPassword|confirmation)'
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml'
    }

    It 'distinguishes platform identity from the seeded tenant administrator' {
        $source | Should Match "platformHost = 'platform.localhost'"
        $source | Should Match "expectedTenantAdminEmail = 'e2e-admin@e2e.invalid'"
        $source | Should Match '/api/platform/auth/login'
        $source | Should Match '/api/auth/login'
        $source | Should Not Match 'tenantEmail = Read-Host'
    }

    It 'reuses exact masters and stops on conflicts' {
        $source | Should Match 'Find-ExactMaster'
        $source | Should Match 'Ensure-Master'
        $source | Should Match 'conflicts on:'
        $source | Should Match 'No mutation was attempted'
        $source | Should Match "'organisations' 'E2E-COMPANY' 'E2E Company'"
        $source | Should Match "'work-locations' 'E2E-HQ' 'E2E-HQ'"
    }

    It 'creates distinct actors and protects the employee-manager boundary' {
        $source | Should Match "'E2E-MANAGER'"
        $source | Should Match "'E2E-EMPLOYEE'"
        $source | Should Match "'E2E-HRBP'"
        $source | Should Match "'E2E-PAYROLL'"
        $source | Should Match 'lifecycle actors are not distinct'
        $source | Should Match 'lifecycle employee cannot be their own manager'
        $source | Should Match 'reportingManagerId'
    }

    It 'allows a root manager with no manager id and validates the returned employee id' {
        $source | Should Match '\[AllowNull\(\)\]\[object\]\$ManagerId'
        $source | Should Match 'Assert-ExpectedEmployee -Employee \$manager\.Record'
        $source | Should Match 'creation/retrieval did not return a valid employee ID\.'
        $source | Should Match 'managerId = Assert-ExpectedEmployee'
        $source | Should Match 'Ensure-Employee.*\$managerId'
    }

    It 'establishes the manager account before creating the lifecycle employee' {
        $managerCall = $source.IndexOf("$manager = Ensure-Employee 'E2E-MANAGER'")
        $managerAccountCall = $source.IndexOf('$managerAccount = Ensure-PortalAccount')
        $employeeCall = $source.IndexOf("$employee = Ensure-Employee 'E2E-EMPLOYEE'")
        $managerCall | Should BeGreaterThan -1
        $managerAccountCall | Should BeGreaterThan $managerCall
        $employeeCall | Should BeGreaterThan $managerAccountCall
    }

    It 'keeps resume-safe deterministic actor and organization reuse contracts' {
        $source | Should Match 'return \[pscustomobject\]@\{ Record = \$existing; Existing = \$true \}'
        $source | Should Match 'return \[pscustomobject\]@\{ Record = \$detail; Existing = \$true \}'
        $source | Should Match 'Get-EmployeeIdentityMismatches'
        $source | Should Match 'Test-NullableGuidEqual'
        $source | Should Match 'InvitationPending.*runner will not resend or replace'
        $source | Should Match 'if \(\[string\]\$employee\.Record\.id -eq \$managerId\)'
        $source | Should Match 'if \(\[string\]\$employee\.Record\.reportingManagerId -ne \$managerId\)'
    }

    It 'assigns bounded roles and performs a safe negative authorization check' {
        $source | Should Match "'/api/role-assignments/roles\?assignableOnly=false'"
        $source | Should Match "'Manager', 'HRBP', 'Accounts'"
        $source | Should Match 'isSystemManaged'
        $source | Should Match 'effective Manager access was not provisioned from the reporting relationship'
        $source | Should Match 'employee negative authorization expected 403/401'
        $source | Should Match 'Negative authorization: PASS'
    }

    It 'establishes the authoritative reporting relationship before verifying system-managed manager access' {
        $source | Should Match 'Ensure-ManagerEmploymentRelationship'
        $source | Should Match 'employment-history'
        $source | Should Match 'employment-history.*Method POST'
        $source | Should Match 'positionChangeReasonId'
        $source.IndexOf('$employeeEmployment = Ensure-ManagerEmploymentRelationship') | Should BeLessThan $source.IndexOf('$managerAssignments = @(')
    }

    It 'does not attempt manual assignment of the system-managed Manager role' {
        $source | Should Not Match "roleBody.*roleByName\['Manager'\]"
        $source | Should Match 'Verify system-managed Manager role'
        $source | Should Not Match "Label = 'Manager'"
    }

    It 'supports resume safety and keeps tenant and actor secrets in memory only' {
        $source | Should Match 'InvitationPending.*runner will not resend or replace'
        $source | Should Match 'Select-Object -Unique'
        $source | Should Match '\$tenantState\.AccessToken = \$null; \$tenantState\.RefreshToken = \$null'
        $source | Should Not Match 'Set-Content.*Token|Out-File.*Token|Export-Clixml'
        $source | Should Match 'Get-InvitationTokenFromUrl'
        $source | Should Match 'Activation stopped: no invitation URL was supplied'
        $source | Should Match 'Tenant administrator activation'
        $source | Should Match '\$token = \$null'
    }

    It 'carries only a newly activated employee credential into the later authorization check' {
        $source | Should Match "CredentialState = 'NewlyActivated'"
        $source | Should Match "CredentialState = 'ExistingActive'"
        $source | Should Match '\$passwordForAuthorization = \$plainPassword'
        $source | Should Match '\$employeePassword = \[string\]\$employeeAccount\.Password'
        $source | Should Match 'if \(\[string\]::IsNullOrWhiteSpace\(\$employeePassword\)\)'
        $source | Should Not Match 'Set-Password.*ExistingActive|reset.*ExistingActive'
    }

    It 'keeps actor password prompts distinct and does not print credential values' {
        $source | Should Match 'Set password for \$Email'
        $source | Should Match 'Employee password for safe authorization check'
        $source | Should Not Match 'HRBP password.*Payroll|Payroll password.*HRBP'
        $source | Should Not Match 'Write-(Host|Output|Error).*\$(employeePassword|plainEmployeePassword|passwordForAuthorization)'
        $source | Should Match '\$employeePassword = \$null'
    }

    It 'keeps employee authorization login on the E2E01 tenant host and stops on 401' {
        $source | Should Match 'Invoke-TenantApi -Path ''/api/auth/login'' -Method POST -JsonBody \$employeeLoginBody -Anonymous'
        $source | Should Match '\$employeeLoginBody = \@\{ email = ''e2e-employee@e2e\.invalid''; password = \$plainEmployeePassword \}'
        $source | Should Match 'Employee authorization-check login'
        $source | Should Match '\$safeMessage = \$safeMessage -replace.*password.*redacted'
        $source | Should Not Match '/api/platform/auth/login.*employee'
    }

    It 'resumes from the actual persisted manager list/detail representations' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource

        $script:resumeGetCount = 0
        $script:resumePostCount = 0
        function Get-TenantDataOrStop {
            param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
            return $Response.Json.data
        }
        function Invoke-TenantApi {
            param(
                [Parameter(Mandatory)][string]$Path,
                [string]$Method = 'GET',
                [string]$JsonBody,
                [switch]$Anonymous
            )
            if ($Path -like '/api/employees?page=*') {
                # This is the actual list shape: no firstName/lastName properties.
                return [pscustomobject]@{ StatusCode = 200; Json = [pscustomobject]@{ success = $true; data = [pscustomobject]@{ items = @([pscustomobject]@{
                    id = '7ed1b768-ddcf-411a-ad14-bef0f958ad83'; employeeCode = 'E2E-MANAGER'; fullName = 'E2E Reporting Manager'; email = 'e2e-manager@e2e.invalid'; status = 'Active'
                }) } } }
            }
            if ($Path -eq '/api/employees/7ed1b768-ddcf-411a-ad14-bef0f958ad83' -and $Method -eq 'GET') {
                $script:resumeGetCount++
                # The detail shape is canonical persisted state; root manager has a nullable null relationship.
                return [pscustomobject]@{ StatusCode = 200; Json = [pscustomobject]@{ success = $true; data = [pscustomobject]@{
                    id = '7ed1b768-ddcf-411a-ad14-bef0f958ad83'; employeeCode = 'E2E-MANAGER'; firstName = 'E2E'; lastName = 'Reporting Manager'; email = 'e2e-manager@e2e.invalid'; departmentId = $department.id; designationId = $designation.id; reportingManagerId = $null
                } } }
            }
            if ($Path -eq '/api/employees' -and $Method -eq 'POST') { $script:resumePostCount++; throw 'Unexpected mutation during resume.' }
            throw "Unexpected mocked API call: $Method $Path"
        }

        try {
            $department = [pscustomobject]@{ id = [Guid]::NewGuid() }
            $designation = [pscustomobject]@{ id = [Guid]::NewGuid() }
            $manager = Ensure-Employee 'E2E-MANAGER' 'E2E' 'Reporting Manager' 'e2e-manager@e2e.invalid' $department $designation $null

            $manager.Existing | Should Be $true
            $manager.Record.id | Should Be '7ed1b768-ddcf-411a-ad14-bef0f958ad83'
            (Assert-ExpectedEmployee -Employee $manager.Record -Code 'E2E-MANAGER' -FirstName 'E2E' -LastName 'Reporting Manager' -Email 'e2e-manager@e2e.invalid' -Label 'Reporting Manager') | Should Be $manager.Record.id
            $script:resumeGetCount | Should Be 1
            $script:resumePostCount | Should Be 0
        }
        finally {
            Remove-Variable resumeGetCount,resumePostCount -Scope Script -ErrorAction SilentlyContinue
        }
    }

    It 'keeps genuine identity conflicts actionable and normalizes nullable GUIDs' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource
        $employee = [pscustomobject]@{ id = [Guid]::NewGuid(); employeeCode = 'E2E-MANAGER'; firstName = 'E2E'; lastName = 'Different'; email = 'e2e-manager@e2e.invalid' }

        { Assert-ExpectedEmployee -Employee $employee -Code 'E2E-MANAGER' -FirstName 'E2E' -LastName 'Reporting Manager' -Email 'e2e-manager@e2e.invalid' -Label 'Reporting Manager' } | Should Throw 'conflicts on: lastName'
        (Test-NullableGuidEqual $null $null) | Should Be $true
        (Test-NullableGuidEqual ([Guid]::Empty.ToString()) $null) | Should Be $true
        (Test-NullableGuidEqual '' $null) | Should Be $true
        (Test-NullableGuidEqual ([Guid]::NewGuid()) $null) | Should Be $false
    }

    It 'applies the same resume-safe employee comparison to all four actors' {
        foreach ($actor in @('E2E-MANAGER', 'E2E-EMPLOYEE', 'E2E-HRBP', 'E2E-PAYROLL')) {
            $source | Should Match ([regex]::Escape("Ensure-Employee '$actor'"))
        }
        $source | Should Match 'Get-EmployeeIdentityMismatches'
        $source | Should Match 'Assert-ExpectedEmployeeConfiguration'
        $source | Should Match 'Assert-ExpectedEmployeeRelationship'
    }

    It 'executes the Stage 2 manager path with Windows PowerShell-compatible statements' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource

        $script:stage2Employees = @()
        $script:stage2EmployeeSearches = @()
        $script:stage2Bodies = @()
        function Get-TenantDataOrStop {
            param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase)
            return $Response.Json.data
        }
        function Invoke-TenantApi {
            param(
                [Parameter(Mandatory)][string]$Path,
                [string]$Method = 'GET',
                [string]$JsonBody,
                [switch]$Anonymous
            )
            if ($Path -like '/api/employees?page=*') {
                $code = [Uri]::UnescapeDataString(([regex]::Match($Path, 'search=([^&]+)').Groups[1].Value))
                $script:stage2EmployeeSearches += $code
                $items = @($script:stage2Employees | Where-Object { $_.employeeCode -eq $code })
                return [pscustomobject]@{ StatusCode = 200; Json = [pscustomobject]@{ success = $true; data = [pscustomobject]@{ items = $items } } }
            }
            if ($Path -eq '/api/employees' -and $Method -eq 'POST') {
                $request = $JsonBody | ConvertFrom-Json
                $script:stage2Bodies += $request
                $created = [pscustomobject]@{
                    id = [Guid]::NewGuid().ToString()
                    employeeCode = [string]$request.employeeCode
                    firstName = [string]$request.firstName
                    lastName = [string]$request.lastName
                    email = [string]$request.email
                    departmentId = [string]$request.departmentId
                    designationId = [string]$request.designationId
                    reportingManagerId = $request.reportingManagerId
                }
                $script:stage2Employees += $created
                return [pscustomobject]@{ StatusCode = 201; Json = [pscustomobject]@{ success = $true; data = $created } }
            }
            if ($Path -match '^/api/employees/.+/portal-account$' -and $Method -eq 'GET') {
                return [pscustomobject]@{ StatusCode = 200; Json = [pscustomobject]@{ success = $true; data = [pscustomobject]@{ state = 'Active'; email = 'e2e-manager@e2e.invalid'; userId = [Guid]::NewGuid().ToString() } } }
            }
            if ($Path -match '^/api/employees/.+/employment-history$' -and $Method -eq 'GET') {
                return [pscustomobject]@{ StatusCode = 404; Json = [pscustomobject]@{ success = $false } }
            }
            if ($Path -match '^/api/employees/.+/employment-history$' -and $Method -eq 'POST') {
                $request = $JsonBody | ConvertFrom-Json
                $request.countryLocationId | Should Not BeNullOrEmpty
                return [pscustomobject]@{ StatusCode = 201; Json = [pscustomobject]@{ success = $true; data = [pscustomobject]@{
                    effectiveFrom = [string]$request.effectiveFrom; effectiveTo = $null; isSuperseded = $false; employmentStatus = 'Active'
                    departmentId = [string]$request.departmentId; designationId = [string]$request.designationId; gradeId = [string]$request.gradeId
                    countryLocationId = [string]$request.countryLocationId; workLocationId = [string]$request.workLocationId
                    positionChangeReasonId = [string]$request.positionChangeReasonId; managerId = [string]$request.managerId
                } } }
            }
            if ($Path -match '^/api/employees/.+$' -and $Method -eq 'GET') {
                $id = $Path.Substring('/api/employees/'.Length)
                $detail = $script:stage2Employees | Where-Object { $_.id -eq $id } | Select-Object -First 1
                return [pscustomobject]@{ StatusCode = 200; Json = [pscustomobject]@{ success = $true; data = $detail } }
            }
            throw "Unexpected mocked API call: $Method $Path"
        }

        try {
            $department = [pscustomobject]@{ id = [Guid]::NewGuid() }
            $designation = [pscustomobject]@{ id = [Guid]::NewGuid() }
            $manager = Ensure-Employee 'E2E-MANAGER' 'E2E' 'Reporting Manager' 'e2e-manager@e2e.invalid' $department $designation $null
            $managerId = Assert-ExpectedEmployee -Employee $manager.Record -Code 'E2E-MANAGER' -FirstName 'E2E' -LastName 'Reporting Manager' -Email 'e2e-manager@e2e.invalid' -Label 'Reporting Manager'
            $managerAccount = Ensure-PortalAccount $manager.Record 'e2e-manager@e2e.invalid'
            $employee = Ensure-Employee 'E2E-EMPLOYEE' 'E2E' 'Lifecycle Employee' 'e2e-employee@e2e.invalid' $department $designation $managerId
            $reason = [pscustomobject]@{ id = [Guid]::NewGuid() }
            $country = [pscustomobject]@{ Record = [pscustomobject]@{ id = [Guid]::NewGuid(); code = 'IN'; name = 'India' }; Existing = $true }
            $employment = Ensure-ManagerEmploymentRelationship -Employee $employee.Record -Department $department -Designation $designation -Grade ([pscustomobject]@{ id = [Guid]::NewGuid() }) -Country $country -Location ([pscustomobject]@{ id = [Guid]::NewGuid() }) -PositionChangeReason $reason -ManagerId $managerId

            $managerId | Should Match '^[0-9a-fA-F-]{36}$'
            $managerAccount.Account.state | Should Be 'Active'
            $employment.Record.managerId | Should Be $managerId
            $script:stage2EmployeeSearches[0] | Should Be 'E2E-MANAGER'
            ($script:stage2Bodies[0].PSObject.Properties.Name -contains 'reportingManagerId') | Should Be $true
            $script:stage2Bodies[0].reportingManagerId | Should BeNullOrEmpty
            $script:stage2Bodies[1].reportingManagerId | Should Be $managerId
            $employee.Record.reportingManagerId | Should Be $managerId
        }
        finally {
            Remove-Variable stage2Employees,stage2EmployeeSearches,stage2Bodies -Scope Script -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a work location without countryLocationId before posting employment history' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource
        function Get-TenantDataOrStop { param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase); return $Response.Json.data }
        function Invoke-TenantApi {
            param([Parameter(Mandatory)][string]$Path, [string]$Method = 'GET', [string]$JsonBody, [switch]$Anonymous)
            if ($Path -match '/employment-history$') { return [pscustomobject]@{ StatusCode = 404; Json = [pscustomobject]@{ success = $false } } }
            throw "Unexpected API call: $Method $Path"
        }

        $employee = [pscustomobject]@{ id = [Guid]::NewGuid() }
        $department = [pscustomobject]@{ id = [Guid]::NewGuid() }
        $designation = [pscustomobject]@{ id = [Guid]::NewGuid() }
        $grade = [pscustomobject]@{ id = [Guid]::NewGuid() }
        $location = [pscustomobject]@{ id = [Guid]::NewGuid() }
        $reason = [pscustomobject]@{ id = [Guid]::NewGuid() }
        { Ensure-ManagerEmploymentRelationship -Employee $employee -Department $department -Designation $designation -Grade $grade -Country ([pscustomobject]@{ Record = $null }) -Location $location -PositionChangeReason $reason -ManagerId ([Guid]::NewGuid()).ToString() } | Should Throw 'countryLocationId'
    }

    It 'reuses an exact deterministic country and stops on a conflicting identity' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource
        function Get-TenantDataOrStop { param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase); return $Response.Json.data }
        $script:countryPostCount = 0
        function Invoke-TenantApi {
            param([Parameter(Mandatory)][string]$Path, [string]$Method = 'GET', [string]$JsonBody, [switch]$Anonymous)
            if ($Path -eq '/api/countries?page=1&pageSize=100&search=IN') {
                return [pscustomobject]@{ Json = [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ id = [Guid]::NewGuid(); code = 'IN'; name = 'India'; isActive = $true }) } } }
            }
            $script:countryPostCount++
            throw 'Unexpected country mutation.'
        }
        (Ensure-Country 'IN' 'India').Existing | Should Be $true
        $script:countryPostCount | Should Be 0

        function Invoke-TenantApi {
            param([Parameter(Mandatory)][string]$Path, [string]$Method = 'GET', [string]$JsonBody, [switch]$Anonymous)
            return [pscustomobject]@{ Json = [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ id = [Guid]::NewGuid(); code = 'IN'; name = 'Different'; isActive = $true }) } } }
        }
        { Ensure-Country 'IN' 'India' } | Should Throw 'conflicts with expected E2E identity'
        Remove-Variable countryPostCount -Scope Script -ErrorAction SilentlyContinue
    }

    It 'continues country lookup across pages when the deterministic search has multiple results' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource
        function Get-TenantDataOrStop { param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase); return $Response.Json.data }
        $script:countryPaths = @()
        function Invoke-TenantApi {
            param([Parameter(Mandatory)][string]$Path, [string]$Method = 'GET', [string]$JsonBody, [switch]$Anonymous)
            $script:countryPaths += $Path
            if ($Path -eq '/api/countries?page=1&pageSize=100&search=IN') {
                return [pscustomobject]@{ Json = [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ id = [Guid]::NewGuid(); code = 'ID'; name = 'Indonesia'; isActive = $true }); page = 1; pageSize = 100; totalCount = 101; totalPages = 2 } } }
            }
            if ($Path -eq '/api/countries?page=2&pageSize=100&search=IN') {
                return [pscustomobject]@{ Json = [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ id = [Guid]::NewGuid(); code = 'IN'; name = 'India'; isActive = $true }); page = 2; pageSize = 100; totalCount = 101; totalPages = 2 } } }
            }
            throw "Unexpected country request: $Path"
        }
        (Ensure-Country 'IN' 'India').Existing | Should Be $true
        $script:countryPaths.Count | Should Be 2
        Remove-Variable countryPaths -Scope Script -ErrorAction SilentlyContinue
    }

    It 'continues employee lookup across pages and rejects an invalid page size before HTTP' {
        $functionSource = $source.Substring(0, [regex]::Match($source, '(?m)^try \{').Index)
        Invoke-Expression $functionSource
        { New-RunnerPagedPath -BasePath '/api/employees' -Page 1 -PageSize 101 -ApiMaximum 100 } | Should Throw 'exceeds API maximum 100'
        function Get-TenantDataOrStop { param([Parameter(Mandatory)]$Response, [Parameter(Mandatory)][string]$Phase); return $Response.Json.data }
        $script:employeePaths = @()
        function Invoke-TenantApi {
            param([Parameter(Mandatory)][string]$Path, [string]$Method = 'GET', [string]$JsonBody, [switch]$Anonymous)
            $script:employeePaths += $Path
            if ($Path -eq '/api/employees?page=1&pageSize=100&search=E2E-EMPLOYEE') {
                $pageItems = @(1..100 | ForEach-Object { [pscustomobject]@{ employeeCode = "OTHER-$($_)" } })
                return [pscustomobject]@{ Json = [pscustomobject]@{ data = [pscustomobject]@{ items = $pageItems; page = 1; pageSize = 100; totalCount = 101; totalPages = 2 } } }
            }
            if ($Path -eq '/api/employees?page=2&pageSize=100&search=E2E-EMPLOYEE') {
                return [pscustomobject]@{ Json = [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ employeeCode = 'E2E-EMPLOYEE'; id = [Guid]::NewGuid() }); page = 2; pageSize = 100; totalCount = 101; totalPages = 2 } } }
            }
            throw "Unexpected employee request: $Path"
        }
        (Get-ExpectedEmployee 'E2E-EMPLOYEE').employeeCode | Should Be 'E2E-EMPLOYEE'
        $script:employeePaths.Count | Should Be 2
        Remove-Variable employeePaths -Scope Script -ErrorAction SilentlyContinue
    }

    It 'creates or reuses root-manager employment before the lifecycle profile and subordinate employment' {
        $managerEmployment = $source.IndexOf('$managerEmployment = Ensure-EmploymentHistory')
        $employeeProfile = $source.IndexOf("$employee = Ensure-Employee 'E2E-EMPLOYEE'")
        $employeeEmployment = $source.IndexOf('$employeeEmployment = Ensure-ManagerEmploymentRelationship')
        $managerEmployment | Should BeGreaterThan -1
        $managerEmployment | Should BeLessThan $employeeProfile
        $employeeProfile | Should BeLessThan $employeeEmployment
        $source | Should Match 'ManagerId.*\$null'
        $source | Should Match 'Ensure-EmploymentHistory.*-Label.*Reporting Manager'
        $source | Should Match 'overlapping employment records'
    }

    It 'rejects subordinate creation locally when manager employment is not active for the same date' {
        $source | Should Match 'explicit local preflight'
        $source | Should Match "managerEmployment\.Record\.employmentStatus -cne 'Active'"
        $source | Should Match 'does not cover subordinate effective date'
        $source | Should Match 'managerId = \$ManagerId'
        $source | Should Match '-ManagerId \$null'
    }

    It 'keeps actor employment requirements asymmetric and never manually assigns Manager' {
        $source | Should Match 'HR/HRBP employment: NOT REQUIRED'
        $source | Should Match 'Payroll Admin employment: NOT REQUIRED'
        $source | Should Not Match "roleBody.*roleByName\['Manager'\]"
        $source | Should Match 'isSystemManaged'
    }

    It 'implements Stage 3 through the production policy, case, review, decision, and persisted-state APIs' {
        $source | Should Match 'Invoke-EmploymentConfirmationStage'
        $source | Should Match '/api/confirmation-policies/employees/.*/status'
        $source.Contains("/api/confirmation-review-cases' -Method POST") | Should Be $true
        $source | Should Match '/reviews/manager/submit'
        $source | Should Match '/reviews/hrbp/submit'
        $source | Should Match '/decision/preview'
        $source.Contains('decision" -Method POST') | Should Be $true
        $source | Should Match '/decision/history'
        $source.Contains('/history"') | Should Be $true
        $source | Should Match 'EmployeeConfirmed'
        $source | Should Match 'finalEmployment.confirmationDate'
    }

    It 'does not require contractual employment before confirmation when history is active' {
        $stage3Start = $source.IndexOf('function Invoke-EmploymentConfirmationStage')
        $decisionStart = $source.IndexOf('$decisionBody =', $stage3Start)
        $stage3PreDecision = $source.Substring($stage3Start, $decisionStart - $stage3Start)

        $stage3PreDecision | Should Match '/employment-history'
        $stage3PreDecision | Should Match '\$status\.confirmationDate'
        $stage3PreDecision | Should Not Match 'employees/\$employeeId/employment"'
        $stage3PreDecision | Should Not Match "'/api/employees/\$employeeId/employment'"
    }

    It 'uses the authoritative contractual employment resource only for final verification' {
        $source | Should Match '\$finalEmployment = Get-TenantDataOrStop.*employees/\$employeeId/employment'
        $source | Should Match 'finalEmployment\.confirmationDate'
        $source | Should Match 'finalEmployment\.jobStatus'
    }

    It 'resumes a published deterministic policy without creating another policy or version' {
        $source | Should Match '\[string\]\$version\.status -eq ''Published'''
        $source | Should Match 'REUSED existing published deterministic policy version'
        $source | Should Match 'multiple versions; no version was changed'
        $source | Should Match 'if \(\$matches\.Count -eq 1\)'
        $source | Should Not Match 'DELETE.*/api/confirmation-policies'
    }

    It 'uses supported idempotent confirmation-policy setup before the status recheck' {
        $source | Should Match 'Ensure-ConfirmationPolicyPrerequisite'
        $source | Should Match "'/api/confirmation-policies' -Method POST"
        $source | Should Match '/api/confirmation-policies/.*/versions.*-Method POST'
        $source | Should Match '/publish.*-Method POST'
        $source | Should Match 'E2E-CONFIRMATION'
        $source | Should Match 'E2E Confirmation Policy'
        $source | Should Match 'existing published deterministic policy version'
        $source | Should Match 'multiple versions; no version was changed'
        $source | Should Match 'employeeId = \$employeeId.*effectiveDate'
        $source | Should Match 'confirmationPolicyId'
        $source | Should Match 'probationDurationUnit.*Days'
    }

    It 'scopes the deterministic policy to the existing employee and preserves the existing lifecycle records' {
        $source | Should Match 'departmentId = \$departmentId'
        $source | Should Match 'dateOfJoining.ToString\(''yyyy-MM-dd''\)'
        $source | Should Match 'existing Stage 2 tenant-administrator session'
        $source | Should Match 'lifecycle employee employment'
        $source | Should Match 'no replacement was attempted'
        $source | Should Match 'no repair or deletion was attempted'
        $source | Should Not Match 'DELETE.*/api/confirmation-policies'
        $source | Should Not Match 'mysql.*INSERT|INSERT.*mysql|Invoke-Sqlcmd|mysqlsh'
    }

    It 'uses the employee detail DateOfJoining contract without a local-date fallback' {
        $source | Should Match 'Get-TenantDataOrStop \(Invoke-TenantApi -Path "/api/employees/\$EmployeeId"\)'
        $source | Should Match 'Convert-ApiDateOnlyToDateTime -Value \$employee.dateOfJoining'
        $source | Should Match "ParseExact"
        $source | Should Match "'yyyy-MM-dd'"
        $source | Should Not Match '\[DateOnly\]::Parse|\[DateOnly\]EffectiveFrom'
        $source | Should Not Match 'dateOfJoining\s*=\s*\(Get-Date\)'
    }

    It 'keeps date extraction strict and PowerShell 5.1 compatible' {
        $source | Should Match 'System\.DateOnly'
        $source | Should Match 'Windows PowerShell 5\.1'
        $source | Should Match 'InvariantCulture'
        $source | Should Match 'DateTimeStyles\]::None'
        $source | Should Match 'lifecycle employee dateOfJoining'
        $source | Should Match 'no valid date of joining for confirmation-policy setup'
    }

    It 'checks policy status and resolution dates against the same employee DOJ' {
        $source | Should Match 'confirmation status dateOfJoining'
        $source | Should Match 'policy resolution dateOfJoining'
        $source | Should Match '\$policySetup\.DateOfJoining\.AddDays\(1\)'
        $source | Should Match '\$resolveBody = \@\{ employeeId = \$employeeId\.ToString\(\); effectiveDate = \$policySetup\.DateOfJoining\.ToString'
    }

    It 'does not repair the existing employee or recreate employment on resume' {
        $source | Should Match 'no replacement was attempted'
        $source | Should Match 'no repair or deletion was attempted'
        $source | Should Not Match 'DELETE.*/api/employees|DELETE.*/employment-history'
        $source | Should Not Match 'Create employee.*existing|recreate.*employment'
    }

    It 'keeps credentials and tokens redacted while reading date sources' {
        $source | Should Not Match 'Write-(Host|Output|Error).*dateOfJoining'
        $source | Should Not Match 'Write-(Host|Output|Error).*\$(AccessToken|RefreshToken|loginJson|resolveBody)'
    }

    It 'does not emit credentials or tokens while setting up policy' {
        $source | Should Not Match 'Write-(Host|Output|Error).*\$(policyBody|versionBody|resolveBody|AccessToken|RefreshToken)'
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml'
    }

    It 'resumes fresh, already-reviewed, and already-confirmed persisted states without duplicate mutations' {
        $source.Contains('$cases.Count -eq 0') | Should Be $true
        $source.Contains('$cases.Count -gt 1') | Should Be $true
        $source.Contains('ALREADY CONFIRMED') | Should Be $true
        $source.Contains('managerReview.Count -eq 0') | Should Be $true
        $source.Contains('hrReview.Count -eq 0') | Should Be $true
        $source.Contains('duplicate confirmation review cases exist') | Should Be $true
        $source.Contains('exactly one persisted Confirmed decision') | Should Be $true
    }

    It 'uses the assigned manager and HRBP actors and denies employee confirmation' {
        $source | Should Match "ExpectedRole 'Manager'"
        $source | Should Match "ExpectedRole 'HRBP'"
        $source.Contains('Invoke-LifecycleActorApi -Actor $hr -Path "/api/confirmation-policies/employees/$employeeId/status"') | Should Be $true
        $source | Should Match 'managerReviewerEmployeeId'
        $source | Should Match 'hrbpReviewerEmployeeId'
        $source | Should Match "ExpectedRole 'Employee'"
        $source | Should Match 'privileged confirmation denial expected 401/403'
        $source | Should Match 'no credential recovery/reset was attempted'
    }

    It 'keeps tenant routing and credential/token handling process-local' {
        $source.Contains('$state.Tenant.Host') | Should Be $true
        $source.Contains('Invoke-ApiRaw -Path $Path -HostName $state.Tenant.Host -Method $Method -BearerToken $Actor.AccessToken') | Should Be $true
        $source.Contains('.AccessToken = $null') | Should Be $true
        $source | Should Not Match 'Write-(Host|Output|Error).*AccessToken'
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml'
        $source | Should Match 'actor received HTTP 401'
    }

    It 'does not report Stage 3 PASS before final persisted-state assertions' {
        $passIndex = $source.IndexOf("Write-Host 'Stage 3 employment/probation/confirmation: PASS'")
        $finalStateIndex = $source.IndexOf('$finalEmployee = Get-TenantDataOrStop')
        $decisionHistoryIndex = $source.IndexOf('$decisionHistory = Get-TenantDataOrStop')
        $passIndex | Should BeGreaterThan $finalStateIndex
        $passIndex | Should BeGreaterThan $decisionHistoryIndex
    }
}

Describe 'Run-EmployeeLifecycleE2E Stage 3 state machine contract' {
    BeforeAll {
        $source = Get-Content (Join-Path $PSScriptRoot '..\Run-EmployeeLifecycleE2E.ps1') -Raw
    }

    It 'covers every supported resume point with inspect-before-mutate guards' {
        @(
            'policySetup', 'status', 'caseQuery', '$cases.Count -eq 0', '$cases.Count -gt 1',
            'managerReview.Count -eq 0', 'hrReview.Count -eq 0', 'decision/preview',
            'ALREADY DECIDED/CONFIRMED', 'existingConfirmedDecisions.Count -gt 0',
            'exactly one persisted Confirmed decision'
        ) | ForEach-Object { $source.Contains($_) | Should Be $true }
    }

    It 'uses the production decision-history DTO for outcome verification' {
        $source | Should Match 'decisionHistory\.decisions.*outcome.*Confirmed'
        $source | Should Match 'finalCase\.case\.status.*Decided'
        $source | Should Not Match 'finalCase\.case\.outcome'
        $source | Should Match 'decisionHistory\.decisions.*newCaseStatus.*Decided'
    }

    It 'executes employee denial before the legitimate decision on active cases' {
        $negativeIndex = $source.IndexOf("privileged confirmation denial expected 401/403")
        $previewIndex = $source.IndexOf("decision/preview")
        $decisionIndex = $source.IndexOf('Submit HR/HRBP confirmation decision')
        $negativeIndex | Should BeLessThan $previewIndex
        $negativeIndex | Should BeLessThan $decisionIndex
    }

    It 'does not duplicate any mutation when the persisted state is already terminal' {
        $terminalStart = $source.IndexOf('if ([string]$caseDetail.case.status -eq ''Decided'')')
        $terminalEnd = $source.IndexOf('else {', $terminalStart)
        $terminalBlock = $source.Substring($terminalStart, $terminalEnd - $terminalStart)
        $terminalBlock | Should Not Match "-Method POST"
    }

    It 'reports preview blockers instead of mutating when confirmation is not permitted' {
        $source | Should Match 'blockingReasons'
        $source | Should Match 'no decision mutation was attempted'
        $source | Should Match 'permittedOutcomes'
    }

    It 'keeps response parsing PowerShell 5.1 compatible and never logs secrets' {
        $source | Should Match 'ParseExact'
        $source | Should Match 'InvariantCulture'
        $source | Should Match 'DateTimeStyles\]::None'
        $source | Should Not Match 'Write-(Host|Output|Error).*AccessToken|Write-(Host|Output|Error).*RefreshToken'
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml'
    }
}

Describe 'Run-EmployeeLifecycleE2E Stage 4 leave lifecycle contract' {
    BeforeAll {
        $source = Get-Content (Join-Path $PSScriptRoot '..\Run-EmployeeLifecycleE2E.ps1') -Raw
        $selectionSource = Get-Content (Join-Path $PSScriptRoot '..\LeavePolicyVersionSelection.ps1') -Raw
        $source = $source + "`n" + $selectionSource
    }

    It 'uses the production leave configuration, request, approval, balance, accounting, and attendance APIs' {
        $source | Should Match 'Stage 4: leave lifecycle'
        $source | Should Match '/api/leave-types'
        $source | Should Match '/api/leave-policies'
        $source | Should Match '/api/leave-balances/import/validate'
        $source | Should Match '/api/leave-balances/mine'
        $source | Should Match '/api/leave-requests/preview'
        $source | Should Match '/api/leave-requests'' -Method POST'
        $source | Should Match '/api/leave-approvals'
        $source | Should Match '/api/leave-reports/accounting'
        $source | Should Match '/api/attendance/me/days'
    }

    It 'uses actual serialized status and transaction values' {
        @('PendingApproval','Approved','Consumption','OnLeave','Committed','Completed') | ForEach-Object { $source | Should Match $_ }
        $source | Should Match 'ReservedQuantity|reservedQuantity'
        $source | Should Match 'ConsumedQuantity|consumedQuantity'
    }

    It 'inspects before mutation and stops on deterministic conflicts or duplicates' {
        @('duplicate deterministic E2E leave types', 'duplicate deterministic E2E leave policies', 'multiple versions', 'duplicate deterministic leave balance imports', 'duplicate deterministic leave requests') | ForEach-Object { $source | Should Match $_ }
        $source | Should Match 'matching.Count -eq 1'
        $source | Should Match 'matching.Count -gt 1'
        $source | Should Match 'Leave request: REUSED'
        $source | Should Match 'Manager approval: REUSED'
    }

    It 'creates a leave-policy version only when the inventory is empty' {
        $noneBranch = [regex]::Match($source, 'if \(\$versionSelection\.Kind -eq ''None''\) \{(?s:.*?)\n    \}')
        $noneBranch.Success | Should Be $true
        $noneBranch.Value.Contains('-Method POST -JsonBody $versionBody') | Should Be $true
        $noneBranch.Value | Should Match 'Create deterministic leave policy version'
        $createIndex = $source.IndexOf('Create deterministic leave policy version')
        $noneIndex = $source.IndexOf("if (`$versionSelection.Kind -eq 'None')")
        $createIndex | Should BeGreaterThan $noneIndex
        $source | Should Match 'stops before this branch when more than one Draft is present'
    }

    It 'selects a valid production working date through preview and does not invent attendance rows' {
        $source | Should Match 'for \(\$offset = 1; \$offset -le 60; \$offset\+\+\)'
        $source | Should Match 'chargeableQuantity -eq 1'
        $source | Should Match 'no valid one-working-day leave date'
        $source | Should Match 'attendance/me/days'
        $source | Should Not Match 'attendance.*POST|attendance.*Method POST'
    }

    It 'covers manager scope, self-approval denial, balance transition, ledger idempotency, and terminal resume' {
        $source | Should Match 'self-approval denial expected 401/403'
        $source | Should Match 'manager leave inbox'
        $source | Should Match 'consumedQuantity.*\+ 1'
        $source | Should Match 'eventType.*Consumption'
        $source | Should Match 'isReplay -ne \$true'
        $source | Should Match 'Stage 4 leave lifecycle: PASS'
    }

    It 'keeps credentials and secrets process-local and remains PowerShell 5.1 compatible' {
        $source | Should Not Match 'Write-(Host|Output|Error).*AccessToken|Write-(Host|Output|Error).*RefreshToken'
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml|Invoke-Sqlcmd|mysqlsh'
        $source | Should Match 'Windows PowerShell 5\.1'
        $source | Should Match 'ParseExact'
    }

    It 'reloads the authoritative draft token after every leave-policy mutation' {
        $source | Should Match 'function Get-CurrentLeaveVersion'
        $source | Should Match 'function Invoke-LeaveConfigurationMutation'
        $source | Should Match 'return Get-CurrentLeaveVersion -PolicyId \$PolicyId -VersionId \$VersionId'
        $source | Should Not Match 'concurrencyToken = \$version\.concurrencyToken\).*Configure leave'
        foreach ($label in @('Configure leave policy type', 'Configure leave policy applicability', 'Configure leave eligibility', 'Configure leave entitlement', 'Configure leave request rules', 'Configure leave calendar rules', 'Configure leave attachments', 'Configure leave cancellation')) {
            $source | Should Match ([regex]::Escape($label))
        }
    }

    It 'models N to N+1 reload without replaying a stale 409 payload' {
        $script:serverRevision = 'N'
        $runnerRevision = 'N'
        $script:mutationCalls = @()
        function Invoke-MockedMutation {
            param([string]$ExpectedRevision)
            $script:mutationCalls += $ExpectedRevision
            if ($ExpectedRevision -ne $script:serverRevision) { return 409 }
            $script:serverRevision = 'N+1'
            return 200
        }
        $first = Invoke-MockedMutation $runnerRevision
        $first | Should Be 200
        $runnerRevision = $serverRevision
        $second = Invoke-MockedMutation $runnerRevision
        $second | Should Be 200
        $script:mutationCalls | Should Be @('N', 'N+1')
        $source | Should Match 'fresh GET'
        $source | Should Match 'a 409 is a safe stop'
    }

    It 'contains resume guards for partial and terminal deterministic state' {
        foreach ($expected in @('configuredTypes.Count -ne 1', 'matchingGroup.Count -ne 1', 'published deterministic leave policy does not include', 'Resolve-DeterministicLeavePolicyVersion', 'versionSelection.Kind', 'duplicate deterministic E2E leave policies exist', 'duplicate deterministic E2E leave types exist')) {
            $source | Should Match ([regex]::Escape($expected))
        }
        $source | Should Match 'versionSelection.Kind -eq ''Published'''
        $source | Should Match 'return \[pscustomobject\]@\{ LeaveType = \$leaveType; Period = \$period; Policy = \$policy; Version = \$publishedVersion; Reused = \$true'
    }

    It 'reuses the authenticated Stage 3 employee session before prompting again' {
        $source | Should Match 'function Get-LifecycleEmployeeSession'
        $source | Should Match '\$script:lifecycleEmployeeSession = \$employee'
        $source | Should Match 'Stage 3 authenticated session; no password prompt'
        $source | Should Match '\$script:leaveEmployee = Get-LifecycleEmployeeSession'
        $source | Should Match 'Invoke-ApiRaw -Path ''/api/auth/me'' -HostName \$state\.Tenant\.Host -BearerToken \$script:lifecycleEmployeeSession\.AccessToken'
        $source | Should Match 'sessionCheck\.StatusCode -ge 200'
        $source | Should Not Match 'Get-LifecycleEmployeeSession\r?\n.*Read-Host'
    }

    It 'binds the reusable session to the existing portal identity and tenant host' {
        $source | Should Match "Email -ieq 'e2e-employee@e2e.invalid'"
        $source | Should Match "Role -ceq 'Employee'"
        $source | Should Match 'TenantHost -ceq \[string\]\$state\.Tenant\.Host'
        $source | Should Match "Invoke-LifecycleActorLogin -Email 'e2e-employee@e2e.invalid' -ExpectedRole 'Employee'"
        $source | Should Match '\$request\.Headers\.Host = \$HostName|\$request\.Headers\.Host = \$HostName'
    }

    It 'preserves process-memory-only token cleanup and leaves 401 handling explicit' {
        $source | Should Match 'actor received HTTP 401; no credential recovery/reset was attempted'
        $source | Should Match '\$manager\.AccessToken = \$null'
        $source | Should Match 'AccessToken = \$null'
        $source | Should Not Match 'Set-Content|Out-File|Export-Clixml'
        $source | Should Not Match 'Invoke-TenantAdministratorRecovery.*Employee|reset.*employee'
    }
}
