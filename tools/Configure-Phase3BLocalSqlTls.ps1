[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$instanceKey = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQLServer\SuperSocketNetLib'
$serviceName = 'MSSQLSERVER'
$serviceAccount = 'NT SERVICE\MSSQLSERVER'
$originalThumbprint = '6BF1D04942BB6AD07E28E69CF5178D04B3A0B9A3'
$originalKeyPath = 'C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys\fad662b360941f26a1193357aab3c12d_98b3371a-d4ed-431b-89a2-06557f283acf'
$recordPath = Join-Path $PSScriptRoot ('phase3b-tls-original-config-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N') + '.json')

function Stop-TlsSetup([string] $Message) { throw "TLS setup stopped: $Message" }

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-TlsSetup 'Administrator rights are required for LocalMachine certificate stores, SQL configuration, and service restart.'
}
$service = Get-Service -Name $serviceName
if ($service.Status -ne 'Running') { Stop-TlsSetup "MSSQLSERVER is not running: $($service.Status)." }
$connections = @(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -eq 1433 -or $_.RemotePort -eq 1433 })
if ($connections.Count -gt 0) { Stop-TlsSetup "Established TCP clients are present on port 1433; no restart was attempted." }

$existing = Get-ChildItem Cert:\LocalMachine\My\$originalThumbprint
$existingAcl = Get-Acl -LiteralPath $originalKeyPath
$settings = Get-ItemProperty -LiteralPath $instanceKey
$original = [ordered]@{
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    Instance = $serviceName
    CertificateRegistryValue = [string] $settings.Certificate
    ForceEncryption = [int] $settings.ForceEncryption
    ExistingCertificateThumbprint = $existing.Thumbprint
    ExistingCertificateSubject = $existing.Subject
    ExistingCertificateIssuer = $existing.Issuer
    ExistingCertificateSan = @($existing.DnsNameList.Unicode)
    ExistingCertificateAcl = @($existingAcl.Access | ForEach-Object { "$( $_.IdentityReference ):$( $_.FileSystemRights ):$( $_.AccessControlType ):Inherited=$( $_.IsInherited )" })
}
function Get-AclSignature($Acl) {
    @($Acl.Access | ForEach-Object { "$( $_.IdentityReference ):$( $_.FileSystemRights ):$( $_.AccessControlType ):Inherited=$( $_.IsInherited )" } | Sort-Object)
}

$original.ExistingCertificateAcl = Get-AclSignature $existingAcl
$record = [ordered]@{
    Original = $original
    CreatedCertificates = [ordered]@{}
    ChainDiagnostics = $null
    Failure = $null
    Rollback = [ordered]@{ StartedUtc = $null; Outcomes = @(); EvidenceWriteFailures = @() }
}
function Write-TlsRecord {
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding UTF8 -NoNewline
}
function Write-TlsRecordSafely {
    try { Write-TlsRecord; return $true }
    catch { $record.Rollback.EvidenceWriteFailures += $_.Exception.Message; return $false }
}
function Get-CertificateEnhancedKeyUsageIds($Certificate) {
    $ekuProperty = $Certificate.PSObject.Properties['EnhancedKeyUsageList']
    if ($null -eq $ekuProperty -or $null -eq $ekuProperty.Value) { return @() }
    $ids = @(
        foreach ($eku in @($ekuProperty.Value)) {
            if ($null -eq $eku) { continue }
            $objectIdProperty = $eku.PSObject.Properties['ObjectId']
            if ($null -eq $objectIdProperty) { throw "Certificate EKU item type '$($eku.GetType().FullName)' lacks ObjectId." }
            [string]$objectIdProperty.Value
        }
    )
    return $ids
}
function Add-CertificateRecord([string] $Role, $Certificate) {
    $record.CreatedCertificates[$Role] = [ordered]@{ Thumbprint = $Certificate.Thumbprint }
    Write-TlsRecord
    $record.CreatedCertificates[$Role] = [ordered]@{
        Thumbprint = $Certificate.Thumbprint
        Subject = $Certificate.Subject
        Issuer = $Certificate.Issuer
        NotBefore = $Certificate.NotBefore.ToUniversalTime().ToString('O')
        NotAfter = $Certificate.NotAfter.ToUniversalTime().ToString('O')
        HasPrivateKey = $Certificate.HasPrivateKey
        KeyAlgorithm = $Certificate.PublicKey.Oid.FriendlyName
        KeySize = $Certificate.PublicKey.Key.KeySize
        DnsNames = @($Certificate.DnsNameList.Unicode)
        EnhancedKeyUsages = Get-CertificateEnhancedKeyUsageIds $Certificate
    }
    Write-TlsRecord
}
function Get-TlsChainDiagnostics($Certificate) {
    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    $built = $chain.Build($Certificate)
    $status = @($chain.ChainStatus | ForEach-Object { [ordered]@{ Flags = $_.Status.ToString(); Message = $_.StatusInformation.Trim() } })
    $elements = @($chain.ChainElements | ForEach-Object {
        [ordered]@{
            Subject = $_.Certificate.Subject
            Issuer = $_.Certificate.Issuer
            Thumbprint = $_.Certificate.Thumbprint
            NotBefore = $_.Certificate.NotBefore.ToUniversalTime().ToString('O')
            NotAfter = $_.Certificate.NotAfter.ToUniversalTime().ToString('O')
            Status = @($_.ChainElementStatus | ForEach-Object { [ordered]@{ Flags = $_.Status.ToString(); Message = $_.StatusInformation.Trim() } })
        }
    })
    [ordered]@{
        BuildResult = [bool]$built
        ChainStatus = $status
        Elements = $elements
        Policy = [ordered]@{
            RevocationMode = $chain.ChainPolicy.RevocationMode.ToString()
            RevocationFlag = $chain.ChainPolicy.RevocationFlag.ToString()
            VerificationFlags = $chain.ChainPolicy.VerificationFlags.ToString()
            UrlRetrievalTimeout = $chain.ChainPolicy.UrlRetrievalTimeout.ToString()
        }
    }
}
function Add-RollbackOutcome([string] $Action, [string] $Result, [string] $Detail) {
    $record.Rollback.Outcomes += [ordered]@{ Action = $Action; Result = $Result; Detail = $Detail }
    [void](Write-TlsRecordSafely)
}

Write-TlsRecord

$ca = $null
$leaf = $null
$registryChanged = $false
$serviceRestartAttempted = $false
$serviceRestartCompleted = $false
$originalConfigActive = $true
$newCertificates = @()
$caExportPath = $null
try {
    $ca = New-SelfSignedCertificate -Type Custom -Subject 'CN=HRMS Local Development CA' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeySpec KeyExchange -Provider 'Microsoft RSA SChannel Cryptographic Provider' -KeyExportPolicy NonExportable -KeyUsage CertSign,CRLSign,DigitalSignature -TextExtension @('2.5.29.19={critical}{text}CA=TRUE&pathlength=1') -CertStoreLocation Cert:\LocalMachine\My
    $newCertificates = @($ca)
    Add-CertificateRecord 'CA' $ca
    $leaf = New-SelfSignedCertificate -Type Custom -Subject 'CN=localhost' -DnsName 'localhost' -Signer $ca -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeySpec KeyExchange -Provider 'Microsoft RSA SChannel Cryptographic Provider' -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -TextExtension @('2.5.29.19={critical}{text}CA=FALSE','2.5.29.37={text}1.3.6.1.5.5.7.3.1') -CertStoreLocation Cert:\LocalMachine\My

    $newCertificates += $leaf
    Add-CertificateRecord 'Leaf' $leaf
    $now = Get-Date
    if ($ca.Subject -ne 'CN=HRMS Local Development CA' -or $ca.NotBefore -gt $now -or $ca.NotAfter -le $now -or -not $ca.HasPrivateKey -or $ca.PublicKey.Oid.FriendlyName -ne 'RSA' -or $ca.PublicKey.Key.KeySize -lt 2048 -or @($ca.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.19'}).Count -eq 0) { Stop-TlsSetup 'Generated CA failed validity, key, private-key, or CA basic-constraints checks.' }
    if ($ca.PrivateKey -isnot [System.Security.Cryptography.RSACryptoServiceProvider] -or $ca.PrivateKey.CspKeyContainerInfo.ProviderName -ne 'Microsoft RSA SChannel Cryptographic Provider') { Stop-TlsSetup 'Generated CA is not backed by the required RSA SChannel CSP provider.' }
    if ($leaf.Subject -ne 'CN=localhost' -or @($leaf.DnsNameList.Unicode) -notcontains 'localhost' -or $leaf.NotBefore -gt $now -or $leaf.NotAfter -le $now -or -not $leaf.HasPrivateKey -or $leaf.PublicKey.Oid.FriendlyName -ne 'RSA' -or $leaf.PublicKey.Key.KeySize -lt 2048 -or (Get-CertificateEnhancedKeyUsageIds $leaf) -notcontains '1.3.6.1.5.5.7.3.1') { Stop-TlsSetup 'Generated leaf certificate failed SAN, validity, key, private-key, or Server Authentication checks.' }
    if ($leaf.PrivateKey -isnot [System.Security.Cryptography.RSACryptoServiceProvider]) { Stop-TlsSetup 'Generated leaf certificate is not backed by a supported RSA CSP private key.' }
    if ($leaf.PrivateKey.CspKeyContainerInfo.ProviderName -ne 'Microsoft RSA SChannel Cryptographic Provider') { Stop-TlsSetup 'Generated leaf certificate is not using the required SQL-compatible RSA SChannel provider.' }
    $leafKeyName = $leaf.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
    if ([string]::IsNullOrWhiteSpace($leafKeyName)) { Stop-TlsSetup 'Generated leaf private-key container could not be identified.' }
    $leafKeyPath = Join-Path 'C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys' $leafKeyName
    $leafAcl = Get-Acl -LiteralPath $leafKeyPath
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($serviceAccount, 'Read', 'Allow')
    $leafAcl.SetAccessRule($rule)
    Set-Acl -LiteralPath $leafKeyPath -AclObject $leafAcl

    $caExportPath = Join-Path ([IO.Path]::GetTempPath()) ('HRMS-Local-Development-CA-' + [Guid]::NewGuid().ToString('N') + '.cer')
    Export-Certificate -Cert $ca -FilePath $caExportPath -Force | Out-Null
    Import-Certificate -FilePath $caExportPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Remove-Item -LiteralPath $caExportPath -Force
    $caExportPath = $null
    $record.ChainDiagnostics = Get-TlsChainDiagnostics $leaf
    Write-TlsRecord
    if (-not $record.ChainDiagnostics.BuildResult) { Stop-TlsSetup 'Generated leaf certificate chain did not validate after local CA trust was installed.' }
    $originalAfter = Get-ChildItem Cert:\LocalMachine\My\$originalThumbprint
    $originalAfterAclSignature = Get-AclSignature (Get-Acl -LiteralPath $originalKeyPath)
    if ($originalAfter.Thumbprint -ne $originalThumbprint -or ($originalAfterAclSignature -join '|') -ne ($original.ExistingCertificateAcl -join '|')) { Stop-TlsSetup 'Existing localhost certificate or ACL changed unexpectedly.' }
    Set-ItemProperty -LiteralPath $instanceKey -Name Certificate -Value $leaf.Thumbprint
    $registryChanged = $true
    $serviceRestartAttempted = $true
    Restart-Service -Name $serviceName -ErrorAction Stop
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    if ((Get-Service -Name $serviceName).Status -ne 'Running') { Stop-TlsSetup 'MSSQLSERVER did not return to Running state.' }
    $serviceRestartCompleted = $true
    [ordered]@{Status='CONFIGURED';Service=$serviceName;LeafThumbprint=$leaf.Thumbprint;CaThumbprint=$ca.Thumbprint;OriginalRecord=$recordPath;ForceEncryptionPreserved=[int]((Get-ItemProperty -LiteralPath $instanceKey).ForceEncryption) } | ConvertTo-Json -Compress
}
catch {
    $failureException = $_.Exception
    $record.Failure = [ordered]@{ Message = $failureException.Message; Type = $failureException.GetType().FullName }
    try {
        if ($leaf -and $null -eq $record.ChainDiagnostics) { $record.ChainDiagnostics = Get-TlsChainDiagnostics $leaf }
        [void](Write-TlsRecordSafely)
    }
    catch { $record.Rollback.EvidenceWriteFailures += $_.Exception.Message }
    $record.Rollback.StartedUtc = [DateTime]::UtcNow.ToString('O')
    [void](Write-TlsRecordSafely)
    if ($caExportPath -and (Test-Path -LiteralPath $caExportPath)) {
        try { Remove-Item -LiteralPath $caExportPath -Force; Add-RollbackOutcome 'Temporary CA export' 'Removed' 'Unique temporary path removed.' } catch { Add-RollbackOutcome 'Temporary CA export' 'Failed' $_.Exception.Message }
    }
    if ($registryChanged) {
        try { Set-ItemProperty -LiteralPath $instanceKey -Name Certificate -Value $original.CertificateRegistryValue; Add-RollbackOutcome 'SQL certificate binding' 'Restored' 'Original binding restored before certificate removal.' } catch { Add-RollbackOutcome 'SQL certificate binding' 'Failed' $_.Exception.Message }
    }
    if ($serviceRestartCompleted) {
        $originalConfigActive = $false
        try {
            Restart-Service -Name $serviceName -ErrorAction Stop
            (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
            if ((Get-Service -Name $serviceName).Status -ne 'Running') { throw 'MSSQLSERVER did not return to Running state.' }
            $originalConfigActive = $true
            Add-RollbackOutcome 'MSSQLSERVER original configuration' 'Restored' 'Service restarted before generated certificate removal.'
        }
        catch { Add-RollbackOutcome 'MSSQLSERVER original configuration' 'Failed' $_.Exception.Message }
    }
    if ($originalConfigActive) {
        foreach ($certificate in $newCertificates) {
            if ($certificate -and (Test-Path -LiteralPath ('Cert:\LocalMachine\Root\' + $certificate.Thumbprint))) { try { Remove-Item -LiteralPath ('Cert:\LocalMachine\Root\' + $certificate.Thumbprint) -Force; Add-RollbackOutcome "Trusted certificate $($certificate.Thumbprint)" 'Removed' 'Removed after SQL binding rollback.' } catch { Add-RollbackOutcome "Trusted certificate $($certificate.Thumbprint)" 'Failed' $_.Exception.Message } }
            if ($certificate -and (Test-Path -LiteralPath ('Cert:\LocalMachine\My\' + $certificate.Thumbprint))) { try { Remove-Item -LiteralPath ('Cert:\LocalMachine\My\' + $certificate.Thumbprint) -Force; Add-RollbackOutcome "Personal certificate $($certificate.Thumbprint)" 'Removed' 'Removed after SQL binding rollback.' } catch { Add-RollbackOutcome "Personal certificate $($certificate.Thumbprint)" 'Failed' $_.Exception.Message } }
        }
    }
    else { Add-RollbackOutcome 'Generated certificates' 'Preserved' 'Not removed because original SQL configuration is not active; SQL dependency state is not proven safe.' }
    [void](Write-TlsRecordSafely)
    if ($serviceRestartAttempted -and -not $serviceRestartCompleted) { throw $failureException }
    throw $failureException
}
