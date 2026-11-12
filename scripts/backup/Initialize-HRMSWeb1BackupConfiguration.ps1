[CmdletBinding()]
param(
    [string]$ConfigRoot = (Join-Path $env:LOCALAPPDATA 'Anevra\HRMS\WEB1Backup\Config'),
    [string]$BackupRoot = (Join-Path $env:LOCALAPPDATA 'Anevra\HRMS\Backups\WEB1'),
    [switch]$Initialize,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:OwnerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$script:SystemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$script:AdministratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')

function Set-HrmsRestrictedDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [OperatingSystem]::IsWindows()) {
        throw 'Protected WEB1 backup configuration is supported only on Windows.'
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    $alreadyExists = Test-Path -LiteralPath $fullPath
    if ($alreadyExists) {
        $item = Get-Item -LiteralPath $fullPath -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Protected path must be a real directory, not a file or reparse point.'
        }
    } else {
        [void](New-Item -ItemType Directory -Path $fullPath -Force)
    }

    if ($alreadyExists) {
        [void](Assert-HrmsRestrictedDirectoryAcl -Path $fullPath)
        return $fullPath
    }

    $acl = Get-Acl -LiteralPath $fullPath
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner.Equals($script:OwnerSid) -and -not $owner.Equals($script:AdministratorsSid)) {
        throw 'Protected directory owner is not the current user or local Administrators.'
    }

    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
        $acl.PurgeAccessRules($rule.IdentityReference)
    }
    foreach ($sid in @($script:OwnerSid, $script:SystemSid, $script:AdministratorsSid)) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $fullPath -AclObject $acl
    Assert-HrmsRestrictedDirectoryAcl -Path $fullPath
    return $fullPath
}

function Assert-HrmsRestrictedDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path)

    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw 'Protected directory still inherits its ACL.'
    }
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner.Equals($script:OwnerSid) -and -not $owner.Equals($script:AdministratorsSid)) {
        throw 'Protected directory owner verification failed.'
    }
    $allowed = @($script:OwnerSid.Value, $script:SystemSid.Value, $script:AdministratorsSid.Value)
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($rules.Count -lt 3) {
        throw 'Protected directory ACL is incomplete.'
    }
    foreach ($rule in $rules) {
        if ($rule.IsInherited -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rule.IdentityReference.Value -notin $allowed -or
            (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl)) {
            throw 'Protected directory ACL contains broad, inherited, denied, or insufficient access.'
        }
    }
    foreach ($sid in $allowed) {
        if (-not ($rules | Where-Object { $_.IdentityReference.Value -eq $sid })) {
            throw 'Protected directory is missing a required principal.'
        }
    }
    return $true
}

function Invoke-HrmsBackupSetupSelfTest {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('hrms-web1-backup-selftest-' + [Guid]::NewGuid().ToString('N'))
    $configPath = Join-Path $root 'config'
    $backupPath = Join-Path $root 'backups'
    try {
        [void](Set-HrmsRestrictedDirectoryAcl $configPath)
        [void](Set-HrmsRestrictedDirectoryAcl $backupPath)
        $credentialPath = Join-Path $configPath 'synthetic.credential.xml'
        $syntheticSecret = 'synthetic-only-password-NotARealCredential'
        $credential = [Management.Automation.PSCredential]::new(
            'synthetic-read-only',
            (ConvertTo-SecureString $syntheticSecret -AsPlainText -Force))
        $credential | Export-Clixml -LiteralPath $credentialPath
        $loaded = Import-Clixml -LiteralPath $credentialPath
        if ($loaded.UserName -ne $credential.UserName -or $loaded.GetNetworkCredential().Password -cne $syntheticSecret) {
            throw 'DPAPI credential round-trip test failed.'
        }
        $config = [ordered]@{
            SchemaVersion = 1
            TenantCode = 'WEB1'
            TenantId = '16313e68-2e72-4a6a-b689-79b271cec295'
            Host = 'web1.localhost'
            Shard = 'web1'
            Provider = 'MySql'
            CatalogDatabase = 'hrms_catalog'
            TenantDatabase = 'hrms_web1'
            CredentialProtection = 'Windows DPAPI CurrentUser Export-Clixml'
            Encryption = [ordered]@{
                Algorithm = 'AES-256-GCM'
                Status = 'PENDING_SECURITY_AND_KEY_CUSTODY_APPROVAL'
                ApprovalReference = $null
                KeyCustodyReference = $null
                KeyRecoveryEvidenceReference = $null
            }
            Retention = [ordered]@{
                Days = 30
                OwnerApproved = $true
                OwnerApprovalDate = '2026-11-05'
                OrganizationPolicyReference = $null
                Status = 'PENDING_ORGANIZATION_POLICY_REFERENCE'
            }
        }
        $json = $config | ConvertTo-Json -Depth 6
        if ($json.Contains($syntheticSecret) -or $json.Contains($credential.GetNetworkCredential().Password)) {
            throw 'Synthetic secret was serialized into the non-secret configuration.'
        }
        if (-not (Assert-HrmsRestrictedDirectoryAcl $configPath) -or -not (Assert-HrmsRestrictedDirectoryAcl $backupPath)) {
            throw 'Restricted directory ACL verification failed.'
        }
        Write-Output 'PASS: restricted ACLs, DPAPI credential round-trip, and secret-free config serialization.'
    } finally {
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }
}

if ($SelfTest) {
    Invoke-HrmsBackupSetupSelfTest
    return
}

if (-not $Initialize) {
    throw 'Choose -Initialize for the interactive setup or -SelfTest for synthetic-only checks.'
}

$configRootFull = Set-HrmsRestrictedDirectoryAcl $ConfigRoot
$backupRootFull = Set-HrmsRestrictedDirectoryAcl $BackupRoot
$configFile = Join-Path $configRootFull 'web1-backup-config.json'
$catalogCredentialFile = Join-Path $configRootFull 'catalog-readonly.credential.xml'
$tenantCredentialFile = Join-Path $configRootFull 'tenant-readonly.credential.xml'
foreach ($existingPath in @($configFile, $catalogCredentialFile, $tenantCredentialFile)) {
    if (Test-Path -LiteralPath $existingPath) {
        throw 'Protected WEB1 configuration already exists; setup will not overwrite credentials or configuration.'
    }
}

$catalogServer = (Read-Host 'Catalog MySQL server endpoint (from approved WEB1 configuration)').Trim()
$tenantServer = (Read-Host 'WEB1 tenant MySQL server endpoint (from approved catalog route/configuration)').Trim()
$catalogPortText = (Read-Host 'Catalog MySQL port [3306]').Trim()
$tenantPortText = (Read-Host 'Tenant MySQL port [3306]').Trim()
$tlsCaPath = (Read-Host 'TLS CA certificate path for server identity verification').Trim()
if ([string]::IsNullOrWhiteSpace($catalogServer) -or [string]::IsNullOrWhiteSpace($tenantServer)) {
    throw 'Both catalog and tenant endpoints are required.'
}
if (-not $catalogPortText) { $catalogPortText = '3306' }
if (-not $tenantPortText) { $tenantPortText = '3306' }
$catalogPort = 0
$tenantPort = 0
if (-not [int]::TryParse($catalogPortText, [ref]$catalogPort) -or $catalogPort -notin 1..65535 -or
    -not [int]::TryParse($tenantPortText, [ref]$tenantPort) -or $tenantPort -notin 1..65535) {
    throw 'MySQL port is invalid.'
}
if (-not (Test-Path -LiteralPath $tlsCaPath -PathType Leaf)) {
    throw 'A readable TLS CA certificate file is required for endpoint identity verification.'
}

$catalogCredential = Get-Credential -Message 'Enter the existing least-privilege read-only MySQL catalog credential'
$tenantCredential = Get-Credential -Message 'Enter the existing least-privilege read-only MySQL WEB1 tenant credential'
if (-not $catalogCredential -or -not $tenantCredential) {
    throw 'Credential setup was cancelled.'
}

$catalogTemporary = "$catalogCredentialFile.$([Guid]::NewGuid().ToString('N')).tmp"
$tenantTemporary = "$tenantCredentialFile.$([Guid]::NewGuid().ToString('N')).tmp"
$configTemporary = "$configFile.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $catalogCredential | Export-Clixml -LiteralPath $catalogTemporary
    $tenantCredential | Export-Clixml -LiteralPath $tenantTemporary
    foreach ($credentialPath in @($catalogTemporary, $tenantTemporary)) {
        $fileAcl = Get-Acl -LiteralPath $credentialPath
        $fileAcl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($fileAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
            $fileAcl.PurgeAccessRules($rule.IdentityReference)
        }
        foreach ($sid in @($script:OwnerSid, $script:SystemSid, $script:AdministratorsSid)) {
            $fileRule = [Security.AccessControl.FileSystemAccessRule]::new(
                $sid,
                [Security.AccessControl.FileSystemRights]::FullControl,
                [Security.AccessControl.AccessControlType]::Allow)
            [void]$fileAcl.AddAccessRule($fileRule)
        }
        Set-Acl -LiteralPath $credentialPath -AclObject $fileAcl
    }
    $config = [ordered]@{
        SchemaVersion = 1
        TenantCode = 'WEB1'
        TenantId = '16313e68-2e72-4a6a-b689-79b271cec295'
        Host = 'web1.localhost'
        Shard = 'web1'
        Provider = 'MySql'
        CatalogDatabase = 'hrms_catalog'
        CatalogServer = $catalogServer
        CatalogPort = $catalogPort
        CatalogCredentialFile = [IO.Path]::GetFileName($catalogCredentialFile)
        TenantDatabase = 'hrms_web1'
        TenantServer = $tenantServer
        TenantPort = $tenantPort
        TenantCredentialFile = [IO.Path]::GetFileName($tenantCredentialFile)
        TlsCaPath = [IO.Path]::GetFullPath($tlsCaPath)
        CredentialProtection = 'Windows DPAPI CurrentUser Export-Clixml'
        CredentialPrivilegeCheck = 'MUST_VERIFY_READ_ONLY_GRANTS_BEFORE_BACKUP'
        BackupMethod = 'mysqldump 8.0.46, --single-transaction --quick, routines/events/triggers, one schema only'
        Encryption = [ordered]@{
            Algorithm = 'AES-256-GCM'
            Status = 'PENDING_SECURITY_AND_KEY_CUSTODY_APPROVAL'
            ApprovalReference = $null
            KeyCustodyReference = $null
            KeyRecoveryEvidenceReference = $null
        }
        BackupRoot = $backupRootFull
        Retention = [ordered]@{
            Days = 30
            OwnerApproved = $true
            OwnerApprovalDate = '2026-11-05'
            OrganizationPolicyReference = $null
            Status = 'PENDING_ORGANIZATION_POLICY_REFERENCE'
            AutomaticDeletionEnabled = $false
        }
    }
    $config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configTemporary -Encoding utf8
    Move-Item -LiteralPath $catalogTemporary -Destination $catalogCredentialFile
    Move-Item -LiteralPath $tenantTemporary -Destination $tenantCredentialFile
    Move-Item -LiteralPath $configTemporary -Destination $configFile
    [void](Assert-HrmsRestrictedDirectoryAcl $configRootFull)
    [void](Assert-HrmsRestrictedDirectoryAcl $backupRootFull)
    Write-Output 'Protected WEB1 backup configuration created. Credential values were not displayed.'
    Write-Output "Configuration reference: $configFile"
    Write-Output "Backup destination: $backupRootFull"
    Write-Output 'No database connection or write was attempted. Verify the catalog route and read-only grants before backup.'
} finally {
    foreach ($temporary in @($catalogTemporary, $tenantTemporary, $configTemporary)) {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
    }
}
