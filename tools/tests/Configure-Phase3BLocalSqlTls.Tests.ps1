[CmdletBinding()]
param()

Set-StrictMode -Version 5.1
$ErrorActionPreference = 'Stop'
$scriptPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Configure-Phase3BLocalSqlTls.ps1'))
$source = Get-Content -LiteralPath $scriptPath -Raw
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw 'FAILED: PowerShell parse errors exist.' }

function Assert-Contains([string] $Text, [string] $Needle, [string] $Message) {
    if ($Text.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw "FAILED: $Message" }
}
function Assert-NotContains([string] $Text, [string] $Needle, [string] $Message) {
    if ($Text.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "FAILED: $Message" }
}
function Assert-Ordered([string] $Text, [string] $First, [string] $Second, [string] $Message) {
    $firstIndex = $Text.IndexOf($First, [StringComparison]::OrdinalIgnoreCase)
    $secondIndex = $Text.IndexOf($Second, [StringComparison]::OrdinalIgnoreCase)
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or $firstIndex -ge $secondIndex) { throw "FAILED: $Message" }
}

Assert-Contains $source 'RSACryptoServiceProvider' 'RSA CSP provider validation is missing.'
Assert-Contains $source 'Microsoft RSA SChannel Cryptographic Provider' 'SQL-compatible provider validation is missing.'
Assert-Contains $source 'Server Authentication' 'Server Authentication requirement is not documented in the source.'
Assert-Contains $source 'DnsName' 'SAN validation is missing.'
Assert-Contains $source 'NotAfter' 'Validity validation is missing.'
Assert-Contains $source 'ExistingCertificateAcl' 'Original certificate ACL is not recorded.'
Assert-Contains $source 'CreatedCertificates' 'Created certificate identities are not recorded.'
Assert-Contains $source "Add-CertificateRecord 'CA' `$ca" 'CA thumbprint is not persisted immediately after creation.'
Assert-Contains $source "Add-CertificateRecord 'Leaf' `$leaf" 'Leaf thumbprint is not persisted immediately after creation.'
Assert-Ordered $source 'New-SelfSignedCertificate -Type Custom -Subject ''CN=localhost''' "Add-CertificateRecord 'Leaf' `$leaf" 'Leaf identity is not persisted after leaf creation.'
Assert-Contains $source 'ChainDiagnostics' 'Chain diagnostics are not recorded.'
Assert-Contains $source 'BuildResult' 'X509Chain.Build result is not recorded.'
Assert-Contains $source 'ChainStatus' 'Chain status flags/messages are not recorded.'
Assert-Contains $source 'ChainElements' 'Chain element diagnostics are not recorded.'
Assert-Contains $source 'UrlRetrievalTimeout' 'Chain policy is not recorded.'
Assert-Contains $source 'NotBefore' 'Certificate validity dates are not recorded.'
Assert-Contains $source 'EvidenceWriteFailures' 'Diagnostic/evidence write failures are not retained.'
Assert-Contains $source 'throw $failureException' 'The original setup exception is not rethrown.'
Assert-Contains $source 'originalConfigActive' 'Rollback does not track whether the original SQL configuration is active.'
Assert-Contains $source "Generated certificates' 'Preserved'" 'Rollback does not preserve certificates when SQL dependency is uncertain.'
Assert-NotContains $source 'PrivateKeyPath' 'Private-key paths must not be logged.'
Assert-Contains $source '[Guid]::NewGuid()' 'Certificate export/record paths are not unique.'
Assert-NotContains $source "'HRMS-Local-Development-CA.cer'" 'A fixed temporary certificate filename remains.'
Assert-NotContains $source 'TrustServerCertificate' 'Certificate trust bypass must not be present.'
Assert-NotContains $source ' -C ' 'sqlcmd certificate bypass must not be present.'
Assert-Contains $source 'ForceEncryptionPreserved' 'Force Encryption preservation check is missing.'
Assert-Contains $source 'Restart-Service -Name $serviceName -ErrorAction Stop' 'The planned service restart is not explicit.'

# Behavioral test: extract only the recording functions. The setup script itself is never dot-sourced.
$sourceAst = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
$helperAst = $sourceAst.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-CertificateEnhancedKeyUsageIds' }, $true)
$recordAst = $sourceAst.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Add-CertificateRecord' }, $true)
if ($null -eq $helperAst -or $null -eq $recordAst) { throw 'FAILED: Recording function definitions could not be isolated.' }
Invoke-Expression $helperAst.Extent.Text
Invoke-Expression $recordAst.Extent.Text

$script:writeCount = 0
function Write-TlsRecord { $script:writeCount++ }
function New-SyntheticCertificate([string] $Thumbprint, $EkuList) {
    [pscustomobject]@{
        Thumbprint = $Thumbprint
        Subject = 'CN=synthetic'
        Issuer = 'CN=synthetic issuer'
        NotBefore = [DateTime]::UtcNow.AddDays(-1)
        NotAfter = [DateTime]::UtcNow.AddDays(1)
        HasPrivateKey = $true
        PublicKey = [pscustomobject]@{
            Oid = [pscustomobject]@{ FriendlyName = 'RSA' }
            Key = [pscustomobject]@{ KeySize = 2048 }
        }
        DnsNameList = [pscustomobject]@{ Unicode = @('synthetic') }
        EnhancedKeyUsageList = $EkuList
    }
}
function Assert-Equal($Actual, $Expected, [string] $Message) {
    if ($Actual -ne $Expected) { throw "FAILED: $Message (actual=$Actual expected=$Expected)" }
}

$record = [ordered]@{ CreatedCertificates = [ordered]@{} }
$script:writeCount = 0
$caSynthetic = New-SyntheticCertificate 'CA00000000000000000000000000000000000001' $null
Add-CertificateRecord 'CA' $caSynthetic
Assert-Equal $record.CreatedCertificates.CA.Thumbprint $caSynthetic.Thumbprint 'CA thumbprint was not recorded.'
Assert-Equal @($record.CreatedCertificates.CA.EnhancedKeyUsages).Count 0 'Absent CA EKU was not handled as empty.'
Assert-Equal $script:writeCount 2 'CA identity and metadata were not persisted in order.'

$record = [ordered]@{ CreatedCertificates = [ordered]@{} }
$script:writeCount = 0
$leafSynthetic = New-SyntheticCertificate 'LEAF000000000000000000000000000000000001' @([pscustomobject]@{ ObjectId = '1.3.6.1.5.5.7.3.1' })
Add-CertificateRecord 'Leaf' $leafSynthetic
Assert-Equal $record.CreatedCertificates.Leaf.Thumbprint $leafSynthetic.Thumbprint 'Leaf thumbprint was not recorded.'
Assert-Equal $record.CreatedCertificates.Leaf.EnhancedKeyUsages '1.3.6.1.5.5.7.3.1' 'Leaf EKU was not extracted from its item object.'

$record = [ordered]@{ CreatedCertificates = [ordered]@{} }
$script:writeCount = 0
$badSynthetic = New-SyntheticCertificate 'BAD0000000000000000000000000000000000001' @([pscustomobject]@{ FriendlyName = 'missing ObjectId' })
$failed = $false
try { Add-CertificateRecord 'Bad' $badSynthetic } catch { $failed = $true }
if (-not $failed) { throw 'FAILED: Missing EKU ObjectId did not fail the metadata extraction.' }
Assert-Equal $record.CreatedCertificates.Bad.Thumbprint $badSynthetic.Thumbprint 'Certificate ownership was lost after metadata extraction failure.'
Assert-Equal $script:writeCount 1 'Certificate identity was not persisted before optional metadata extraction.'

$staticAssertionCount = 27
$behavioralAssertionCount = 9
Write-Output "TLS_SETUP_TESTS_PASSED StaticAssertions=$staticAssertionCount BehavioralAssertions=$behavioralAssertionCount TotalAssertions=$($staticAssertionCount + $behavioralAssertionCount)"
