[CmdletBinding()]
param(
    [string]$ConfigRoot = (Join-Path $env:LOCALAPPDATA 'Anevra\HRMS\WEB1Backup\Config'),
    [string]$InstallRoot = 'C:\ProgramData\MySQL\MySQL Server 8.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$networkServiceSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-20')

function Assert-HrmsTlsAcl {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][Security.Principal.SecurityIdentifier[]]$AllowedSids,
        [Parameter(Mandatory)][bool]$Directory
    )

    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    if (-not $acl.AreAccessRulesProtected) { throw 'TLS ACL is inherited.' }
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner.Equals($currentSid) -and -not $owner.Equals($administratorsSid)) {
        throw 'TLS path owner is not approved.'
    }

    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($rules.Count -lt 3) { throw 'TLS ACL is incomplete.' }
    foreach ($rule in $rules) {
        if ($rule.IsInherited -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            -not ($AllowedSids.Value -contains $rule.IdentityReference.Value)) {
            throw 'TLS ACL contains a broad, inherited, denied, or unapproved rule.'
        }
        $required = if ($Directory -and $rule.IdentityReference.Value -eq $networkServiceSid.Value) {
            [Security.AccessControl.FileSystemRights]::ReadAndExecute
        } elseif (-not $Directory -and $rule.IdentityReference.Value -eq $networkServiceSid.Value) {
            [Security.AccessControl.FileSystemRights]::Read
        } else {
            [Security.AccessControl.FileSystemRights]::FullControl
        }
        if (($rule.FileSystemRights -band $required) -ne $required) { throw 'TLS ACL lacks a required permission.' }
    }
    foreach ($sid in $AllowedSids) {
        if (-not ($rules | Where-Object { $_.IdentityReference.Value -eq $sid.Value })) {
            throw 'TLS ACL is missing an approved principal.'
        }
    }
    return $true
}

function New-HrmsTlsDirectory {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][bool]$AllowNetworkService)

    if (Test-Path -LiteralPath $Path) { throw 'TLS output directory already exists; refusing overwrite.' }
    [void](New-Item -ItemType Directory -Path $Path)
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
        $acl.PurgeAccessRules($rule.IdentityReference)
    }

    $inheritance = if ($Path -eq $ConfigRoot) {
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    } else { [Security.AccessControl.InheritanceFlags]::None }
    foreach ($sid in @($currentSid, $systemSid, $administratorsSid)) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    if ($AllowNetworkService) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $networkServiceSid,
            [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            [Security.AccessControl.InheritanceFlags]::None,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
    $allowed = @($currentSid, $systemSid, $administratorsSid)
    if ($AllowNetworkService) { $allowed += $networkServiceSid }
    [void](Assert-HrmsTlsAcl -Path $Path -AllowedSids $allowed -Directory $true)
}

function Set-HrmsTlsFileAcl {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][bool]$AllowNetworkService)

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
        $acl.PurgeAccessRules($rule.IdentityReference)
    }
    foreach ($sid in @($currentSid, $systemSid, $administratorsSid)) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid, [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    if ($AllowNetworkService) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $networkServiceSid, [Security.AccessControl.FileSystemRights]::Read,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
    $allowed = @($currentSid, $systemSid, $administratorsSid)
    if ($AllowNetworkService) { $allowed += $networkServiceSid }
    [void](Assert-HrmsTlsAcl -Path $Path -AllowedSids $allowed -Directory $false)
}

if (-not [OperatingSystem]::IsWindows()) { throw 'This installer is supported only on Windows.' }
$configRoot = [IO.Path]::GetFullPath($ConfigRoot)
$installRoot = [IO.Path]::GetFullPath($InstallRoot)
if (Test-Path -LiteralPath $configRoot) {
    [void](Assert-HrmsTlsAcl -Path $configRoot -AllowedSids @($currentSid, $systemSid, $administratorsSid) -Directory $true)
} else {
    $parent = Split-Path -Parent $configRoot
    if (-not (Test-Path -LiteralPath $parent)) { [void](New-Item -ItemType Directory -Path $parent -Force) }
    New-HrmsTlsDirectory -Path $configRoot -AllowNetworkService $false
}

$clientCaPath = Join-Path $configRoot 'local-dev-ca.pem'
$caKeyPath = Join-Path $configRoot 'local-dev-ca-key.pem'
if ((Test-Path -LiteralPath $clientCaPath) -or (Test-Path -LiteralPath $caKeyPath)) {
    throw 'A local-development CA already exists; refusing to overwrite it.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$installDirectory = Join-Path $installRoot "HRMS-LocalTLS-$stamp"
$serverCaPath = Join-Path $installDirectory 'ca.pem'
$serverCertPath = Join-Path $installDirectory 'server-cert.pem'
$serverKeyPath = Join-Path $installDirectory 'server-key.pem'
New-HrmsTlsDirectory -Path $installDirectory -AllowNetworkService $true

$now = [DateTimeOffset]::UtcNow
$caRsa = [Security.Cryptography.RSA]::Create(3072)
$serverRsa = [Security.Cryptography.RSA]::Create(3072)
$caCertificate = $null
$leafCertificate = $null
$leafWithKey = $null
$chain = $null
$loaded = $null
$loadedChain = $null
try {
    $caRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=HRMS Local Development Root CA', $caRsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $caRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 1, $true))
    $caRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            ([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
             [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign), $true))
    $caRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($caRequest.PublicKey, $false))
    $caCertificate = $caRequest.CreateSelfSigned($now.AddMinutes(-5), $now.AddYears(5))

    $serverRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=localhost', $serverRsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $serverRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $serverRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            ([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
             [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment), $true))
    $eku = [Security.Cryptography.OidCollection]::new()
    [void]$eku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    $serverRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $true))
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddIpAddress([Net.IPAddress]::Parse('127.0.0.1'))
    $san.AddDnsName('localhost')
    $serverRequest.CertificateExtensions.Add($san.Build($true))
    $serverRequest.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($serverRequest.PublicKey, $false))

    $serial = [byte[]]::new(16)
    [Security.Cryptography.RandomNumberGenerator]::Fill($serial)
    $serial[0] = $serial[0] -band 0x7f
    if (($serial | Measure-Object -Sum).Sum -eq 0) { $serial[15] = 1 }
    $leafCertificate = $serverRequest.Create($caCertificate, $now.AddMinutes(-5), $now.AddYears(2), $serial)
    $leafWithKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey(
        $leafCertificate, $serverRsa)

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    $chain.ChainPolicy.TrustMode = [Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
    [void]$chain.ChainPolicy.CustomTrustStore.Add($caCertificate)
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $chainValid = $chain.Build($leafWithKey)
    $sanText = ($leafWithKey.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } |
        ForEach-Object { $_.Format($false) }) -join '; '
    $hasNames = $sanText.Contains('127.0.0.1') -and $sanText.Contains('localhost')
    if (-not $chainValid -or -not $hasNames -or $now.UtcDateTime -gt $leafWithKey.NotAfter.ToUniversalTime()) {
        throw 'In-memory certificate validation failed.'
    }

    [IO.File]::WriteAllText($clientCaPath, $caCertificate.ExportCertificatePem(), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($caKeyPath, $caRsa.ExportPkcs8PrivateKeyPem(), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($serverCaPath, $caCertificate.ExportCertificatePem(), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($serverCertPath, $leafWithKey.ExportCertificatePem(), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($serverKeyPath, $serverRsa.ExportPkcs8PrivateKeyPem(), [Text.UTF8Encoding]::new($false))

    Set-HrmsTlsFileAcl -Path $clientCaPath -AllowNetworkService $false
    Set-HrmsTlsFileAcl -Path $caKeyPath -AllowNetworkService $false
    Set-HrmsTlsFileAcl -Path $serverCaPath -AllowNetworkService $true
    Set-HrmsTlsFileAcl -Path $serverCertPath -AllowNetworkService $true
    Set-HrmsTlsFileAcl -Path $serverKeyPath -AllowNetworkService $true

    $loaded = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($serverCertPath, $serverKeyPath)
    $loadedCa = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($serverCaPath, $caKeyPath)
    $loadedChain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    $loadedChain.ChainPolicy.TrustMode = [Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
    [void]$loadedChain.ChainPolicy.CustomTrustStore.Add($loadedCa)
    $loadedChain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $fileChainValid = $loadedChain.Build($loaded)
    $fileSan = ($loaded.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } |
        ForEach-Object { $_.Format($false) }) -join '; '
    $validNow = [DateTime]::UtcNow -ge $loaded.NotBefore.ToUniversalTime() -and
        [DateTime]::UtcNow -le $loaded.NotAfter.ToUniversalTime()
    if (-not $loaded.HasPrivateKey -or -not $fileChainValid -or -not $validNow -or
        -not $fileSan.Contains('127.0.0.1') -or -not $fileSan.Contains('localhost')) {
        throw 'On-disk certificate verification failed.'
    }

    [pscustomobject]@{
        Result = 'PASS'
        ClientCaPath = $clientCaPath
        ServerCertificatePath = $serverCertPath
        ServerCaPath = $serverCaPath
        ServerCertificateSubject = $loaded.Subject
        ServerCertificateNotAfterUtc = $loaded.NotAfter.ToUniversalTime().ToString('o')
        ChainValid = $fileChainValid
        CertificateValidNow = $validNow
        SubjectAlternativeNames = $fileSan
        PrivateKeyPresentAndMatched = $loaded.HasPrivateKey
        ServerAuthEku = $true
        PrivateKeyAclRestricted = (Get-Acl -LiteralPath $serverKeyPath).AreAccessRulesProtected
        CaPrivateKeyAclRestricted = (Get-Acl -LiteralPath $caKeyPath).AreAccessRulesProtected
        ServerDirectory = $installDirectory
    } | ConvertTo-Json -Depth 3 -Compress
}
finally {
    foreach ($certificate in @($loadedChain, $loaded, $chain, $leafWithKey, $leafCertificate, $caCertificate)) {
        if ($null -ne $certificate) { $certificate.Dispose() }
    }
    $serverRsa.Dispose()
    $caRsa.Dispose()
}
