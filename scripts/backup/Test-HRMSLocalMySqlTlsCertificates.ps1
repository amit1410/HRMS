[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ServerDirectory,
    [string]$ConfigRoot = (Join-Path $env:LOCALAPPDATA 'Anevra\HRMS\WEB1Backup\Config')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$clientCa = Join-Path $ConfigRoot 'local-dev-ca.pem'
$caKey = Join-Path $ConfigRoot 'local-dev-ca-key.pem'
$serverCa = Join-Path $ServerDirectory 'ca.pem'
$serverCert = Join-Path $ServerDirectory 'server-cert.pem'
$serverKey = Join-Path $ServerDirectory 'server-key.pem'
$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$networkServiceSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-20')

function Assert-RestrictedPemAcl {
    param([string]$Path, [Security.Principal.SecurityIdentifier[]]$Allowed)

    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    if (-not $acl.AreAccessRulesProtected) { throw "ACL is inherited: $Path" }
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner.Equals($currentSid) -and -not $owner.Equals($administratorsSid)) {
        throw "ACL owner is not approved: $Path"
    }
    foreach ($rule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
        if ($rule.IsInherited -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rule.IdentityReference.Value -notin @($Allowed | ForEach-Object { $_.Value })) {
            throw "ACL contains a broad or unapproved rule: $Path"
        }
    }
}

foreach ($path in @($clientCa, $caKey)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'CA artifact is missing.' }
    Assert-RestrictedPemAcl -Path $path -Allowed @($currentSid, $systemSid, $administratorsSid)
}
foreach ($path in @($serverCa, $serverCert, $serverKey)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Server TLS artifact is missing.' }
    Assert-RestrictedPemAcl -Path $path -Allowed @($currentSid, $systemSid, $administratorsSid, $networkServiceSid)
}

$ca = $null
$server = $null
$chain = $null
try {
    $ca = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($clientCa, $caKey)
    $server = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($serverCert, $serverKey)
    $serverCaCert = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($serverCa, $caKey)
    if ($ca.Thumbprint -ne $serverCaCert.Thumbprint) { throw 'Client/server CA copies differ.' }

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    $chain.ChainPolicy.TrustMode = [Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
    [void]$chain.ChainPolicy.CustomTrustStore.Add($ca)
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $chainValid = $chain.Build($server)

    $now = [DateTime]::UtcNow
    $validNow = $now -ge $ca.NotBefore.ToUniversalTime() -and $now -le $ca.NotAfter.ToUniversalTime() -and
        $now -ge $server.NotBefore.ToUniversalTime() -and $now -le $server.NotAfter.ToUniversalTime()
    $san = ($server.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } |
        ForEach-Object { $_.Format($false) }) -join '; '
    $hasNames = $san.Contains('127.0.0.1') -and $san.Contains('localhost')
    $eku = $server.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
    $hasServerAuth = [bool]($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.1' })

    if (-not $chainValid -or -not $validNow -or -not $hasNames -or -not $server.HasPrivateKey -or -not $hasServerAuth) {
        throw 'TLS certificate validation failed.'
    }
    [pscustomobject]@{
        Result = 'PASS'
        ChainValid = $chainValid
        CertificatesValidNow = $validNow
        SubjectAlternativeNames = $san
        ServerAuthEku = $hasServerAuth
        PrivateKeyPresentAndMatched = $server.HasPrivateKey
        CaPrivateKeyAclRestricted = (Get-Acl -LiteralPath $caKey).AreAccessRulesProtected
        ServerPrivateKeyAclRestricted = (Get-Acl -LiteralPath $serverKey).AreAccessRulesProtected
        ServerCertificateNotAfterUtc = $server.NotAfter.ToUniversalTime().ToString('o')
    } | ConvertTo-Json -Compress
}
finally {
    if ($chain) { $chain.Dispose() }
    if ($server) { $server.Dispose() }
    if ($ca) { $ca.Dispose() }
}
