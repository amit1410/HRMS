[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sqlServer = 'lpc:.'
$catalogDatabase = 'HRMS_Catalog'
$historyTable = 'dbo.__EFMigrationsHistoryCatalog'
$expectedHistory = @(
    '20260823113202_InitialCatalog'
    '20260905142921_AddTenantDatabaseProvider'
    '20260906130913_AddPlatformIdentity'
)

function Invoke-SqlRead {
    param(
        [Parameter(Mandatory)][string]$Query,
        [Parameter(Mandatory)][string]$Label
    )

    Write-Host "[$Label]"
    $arguments = @('-S', $sqlServer, '-E', '-C', '-b', '-d', $catalogDatabase, '-h', '-1', '-W', '-s', '|', '-Q', "SET NOCOUNT ON;`r`n$Query")
    $output = @(& sqlcmd.exe @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host ([string]$_) }
    if ($null -eq $exitCode) { throw "$Label did not provide an exit code." }
    Write-Host "Exit code: $exitCode"
    if ($exitCode -ne 0) { throw "$Label failed with exit code $exitCode." }
    return @($output | ForEach-Object { [string]$_ } | Where-Object { $_.Trim() })
}

function Get-SingleValue {
    param([Parameter(Mandatory)][string]$Query, [Parameter(Mandatory)][string]$Label)
    $rows = @(Invoke-SqlRead -Query $Query -Label $Label)
    if ($rows.Count -ne 1) { throw "$Label returned $($rows.Count) rows instead of one." }
    return $rows[0].Trim()
}

function Assert-NoRows {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Rows,
        [Parameter(Mandatory)][string]$Message
    )
    if ($Rows.Count -ne 0) { throw "$Message $($Rows -join '; ')" }
}

function Assert-ExactHistory {
    $history = @((Invoke-SqlRead -Label 'Migration history' -Query "SELECT MigrationId FROM $historyTable ORDER BY MigrationId;") | ForEach-Object { $_.Trim() })
    if ($history.Count -ne $expectedHistory.Count -or @($expectedHistory | Where-Object { $history -notcontains $_ }).Count -ne 0) {
        throw "Migration history is unexpected: $($history -join ', ')."
    }
}

function Assert-Tenant {
    param(
        [Parameter(Mandatory)][string]$TenantCode,
        [Parameter(Mandatory)][string]$ExpectedId,
        [Parameter(Mandatory)][string]$ExpectedHost,
        [Parameter(Mandatory)][string]$ExpectedShardKey
    )

    $rows = @(Invoke-SqlRead -Label "$TenantCode preservation" -Query "SELECT CONVERT(nvarchar(36), Id), TenantCode, Host, ShardKey, DatabaseProvider FROM dbo.Tenants WHERE TenantCode = '$TenantCode';")
    if ($rows.Count -ne 1) { throw "$TenantCode expected one row, found $($rows.Count)." }
    $parts = $rows[0] -split '\|', 5
    if ($parts.Count -ne 5) { throw "$TenantCode row shape was unexpected." }
    if ($parts[0].Trim() -cne $ExpectedId -or $parts[1].Trim() -cne $TenantCode -or $parts[2].Trim() -cne $ExpectedHost -or $parts[3].Trim() -cne $ExpectedShardKey -or $parts[4].Trim() -cne 'SqlServer') {
        throw "$TenantCode identity or provider changed."
    }
}

try {
    Write-Host 'PLATFORM SUPERADMIN BOOTSTRAP VERIFICATION'
    if (-not (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue)) { throw 'sqlcmd.exe was not found.' }

    $identity = @(Invoke-SqlRead -Label 'Catalog database identity' -Query 'SELECT DB_NAME();')
    if ($identity.Count -ne 1 -or $identity[0].Trim() -cne $catalogDatabase) { throw "Catalog identity is not $catalogDatabase." }

    Assert-ExactHistory

    $platformUserRows = @(Invoke-SqlRead -Label 'Platform user safe fields' -Query 'SELECT CONVERT(nvarchar(36), Id), Email, NormalizedEmail, FirstName, LastName, IsActive, SecurityRevision, CONVERT(nvarchar(33), CreatedAtUtc, 126) FROM dbo.PlatformUsers;')
    if ($platformUserRows.Count -ne 1) { throw "PlatformUsers count is $($platformUserRows.Count), expected 1." }
    $userParts = $platformUserRows[0] -split '\|', 8
    if ($userParts.Count -ne 8) { throw 'PlatformUser result shape was unexpected.' }
    $platformUserId = $userParts[0].Trim()
    $email = $userParts[1].Trim()
    if ([string]::IsNullOrWhiteSpace($email) -or $userParts[5].Trim() -ne '1' -or $userParts[6].Trim() -ne '0') {
        throw 'PlatformUser active/security-revision verification failed.'
    }
    Write-Host "PlatformUser: $platformUserId | $email | $($userParts[3].Trim()) $($userParts[4].Trim()) | IsActive=$($userParts[5].Trim()) | SecurityRevision=$($userParts[6].Trim()) | CreatedAt=$($userParts[7].Trim())"
    Write-Host 'REAL PLATFORM USER VERIFIED'

    $roleAssignments = @(Invoke-SqlRead -Label 'Platform user role assignment' -Query "SELECT r.Name FROM dbo.PlatformUserRoles ur INNER JOIN dbo.PlatformRoles r ON r.Id = ur.PlatformRoleId WHERE ur.PlatformUserId = '$platformUserId';")
    if ($roleAssignments.Count -ne 1 -or $roleAssignments[0].Trim() -cne 'PlatformSuperAdmin') { throw 'PlatformSuperAdmin role assignment is not exactly one.' }
    Write-Host 'PlatformSuperAdmin user-role assignments: 1'
    Write-Host 'PLATFORM SUPERADMIN ROLE ASSIGNMENT VERIFIED'

    if ((Get-SingleValue -Label 'PlatformSuperAdmin role count' -Query "SELECT COUNT(*) FROM dbo.PlatformRoles WHERE Name = 'PlatformSuperAdmin';") -ne '1') { throw 'PlatformSuperAdmin role count is not exactly one.' }
    if ((Get-SingleValue -Label 'Platform permission count' -Query "SELECT COUNT(*) FROM dbo.PlatformPermissions WHERE Name IN ('PlatformTenant.View', 'PlatformTenant.Create', 'PlatformTenant.UpdateStatus');") -ne '3') { throw 'Expected platform permission count is not three.' }
    $effectivePermissions = @(Invoke-SqlRead -Label 'Effective platform permissions' -Query "SELECT p.Name FROM dbo.PlatformUserRoles ur INNER JOIN dbo.PlatformRoles r ON r.Id = ur.PlatformRoleId INNER JOIN dbo.PlatformRolePermissions rp ON rp.PlatformRoleId = r.Id INNER JOIN dbo.PlatformPermissions p ON p.Id = rp.PlatformPermissionId WHERE ur.PlatformUserId = '$platformUserId' ORDER BY p.Name;")
    $expectedPermissions = @('PlatformTenant.Create', 'PlatformTenant.UpdateStatus', 'PlatformTenant.View')
    if ($effectivePermissions.Count -ne 3 -or @($expectedPermissions | Where-Object { $effectivePermissions -notcontains $_ }).Count -ne 0) { throw "Effective platform permissions are unexpected: $($effectivePermissions -join ', ')." }
    if ((Get-SingleValue -Label 'PlatformSuperAdmin grant count' -Query "SELECT COUNT(*) FROM dbo.PlatformRolePermissions rp INNER JOIN dbo.PlatformRoles r ON r.Id = rp.PlatformRoleId WHERE r.Name = 'PlatformSuperAdmin';") -ne '3') { throw 'PlatformSuperAdmin grant count is not three.' }
    Write-Host 'Effective platform permissions: 3'
    Write-Host 'PLATFORM SUPERADMIN PERMISSIONS VERIFIED'

    $refreshTokenCount = Get-SingleValue -Label 'Platform refresh-token count' -Query 'SELECT COUNT(*) FROM dbo.PlatformRefreshTokens;'
    Write-Host "PlatformRefreshTokens count: $refreshTokenCount"
    if ($refreshTokenCount -ne '0') { throw 'Unexpected platform refresh tokens exist before login acceptance.' }

    $tenantCount = Get-SingleValue -Label 'Tenant count' -Query 'SELECT COUNT(*) FROM dbo.Tenants;'
    Write-Host "Tenant count: $tenantCount"
    Assert-Tenant -TenantCode 'DEMO01' -ExpectedId '11111111-1111-1111-1111-111111111111' -ExpectedHost 'demo01.localhost' -ExpectedShardKey 'demo01'
    Assert-Tenant -TenantCode 'DEMO02' -ExpectedId '22222222-2222-2222-2222-222222222222' -ExpectedHost 'demo02.localhost' -ExpectedShardKey 'demo02'
    Write-Host 'DEMO01/DEMO02 PRESERVATION VERIFIED'

    $bootstrapSource = Get-Content -Raw (Join-Path $PSScriptRoot 'PlatformAdminBootstrap\Program.cs')
    if ($bootstrapSource -match 'catalog\.Tenants|catalog\.Users|HrmsDbContext|TenantProvisioning') {
        throw 'Bootstrap source contains a tenant identity/data access path.'
    }
    Write-Host 'Bootstrap source tenant identity writes: NONE'

    Write-Host ''
    Write-Host 'PLATFORM SUPERADMIN BOOTSTRAP VERIFICATION PASS'
    Write-Host 'READY FOR PLATFORM LOGIN ACCEPTANCE'
    Write-Host 'PlatformUsers count: 1'
    Write-Host 'PlatformSuperAdmin user-role assignments: 1'
    Write-Host 'Effective platform permissions: 3'
    Write-Host 'PlatformRefreshTokens count: 0'
    Write-Host 'Tenant created: NO'
    Write-Host 'Migration applied: NO'
    Write-Host 'Data modified by verification: NO'
}
catch {
    Write-Error "PLATFORM SUPERADMIN BOOTSTRAP VERIFICATION FAILED: $($_.Exception.Message)"
    exit 1
}
