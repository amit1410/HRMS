$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\Invoke-HRMSWeb1BackupRestoreVerification.ps1'
$source = Get-Content -LiteralPath $scriptPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
$script:Passed = 0
$script:Failed = 0
$script:Skipped = 0
function Assert-Check([string]$Name, [bool]$Condition) {
    if ($Condition) { $script:Passed++; Write-Output "PASS $Name" }
    else { $script:Failed++; Write-Output "FAIL $Name" }
}

Assert-Check 'script parses without errors' ($parseErrors.Count -eq 0)
$restoreStart = $source.IndexOf('if($RestoreOnly -or $RestoreOnlyPreflight){', [StringComparison]::Ordinal)
$restoreEnd = $source.IndexOf('# Safety gates run before the script creates evidence or starts an isolated server.', $restoreStart, [StringComparison]::Ordinal)
$restore = $source.Substring($restoreStart, $restoreEnd - $restoreStart)
$lifecycleStart=$source.IndexOf('function Invoke-HrmsRestoreOnlyLifecycle {',[StringComparison]::Ordinal)
$productionStart=$source.IndexOf('function New-HrmsRestoreOnlyProductionOperations(',[StringComparison]::Ordinal)
$productionEnd=$source.IndexOf("if(`$env:HRMS_RESTORE_ONLY_TEST_LIBRARY",$productionStart,[StringComparison]::Ordinal)
$lifecycle=$source.Substring($lifecycleStart,$productionStart-$lifecycleStart)
$productionOps=$source.Substring($productionStart,$productionEnd-$productionStart)
$familyStart=$source.IndexOf('function Resolve-HrmsMySql80ProcessFamily(',[StringComparison]::Ordinal)
$snapshotStart=$source.IndexOf('function Get-HrmsMySql80ServiceSnapshot {',$familyStart,[StringComparison]::Ordinal)
$familyValidation=$source.Substring($familyStart,$snapshotStart-$familyStart)
Assert-Check 'production RestoreOnly entry routes through tested lifecycle and returns' ($restoreEnd -gt $restoreStart -and $restore -match 'Invoke-HrmsRestoreOnlyLifecycle' -and $restore -match '(?m)^\s*return\s*$')
Assert-Check 'production RestoreOnly path has no legacy direct execution bypass' ($restore -notmatch 'Diagnostics\.Process|Invoke-Client|Import-EncryptedDump|Stop-IsolatedMysqlGracefully|New-Item|Remove-Item')
Assert-Check 'restore-only adapter omits source backup, live catalog and MySQL80 stop operations' ($productionOps -notmatch 'mysqldump|Write-EncryptedDump|Get-HrmsDevelopmentDatabaseConfiguration|Invoke-HrmsCatalogRouteCheck|Stop-Service|Stop-MySQL80|Shutdown-MySQL80')
Assert-Check 'production operations verify pinned artifact path, SHA-256 and fresh HMAC' ($restore -match 'D:\\HRMS\\\.db-backups' -and $restore -match "-ExpectedSha256 '[0-9A-F]{64}'" -and $productionOps -match 'Test-EncryptedDump')
Assert-Check 'lifecycle rechecks process identity around import and graceful cleanup' ($lifecycle -match 'VerifyProcessIdentity' -and $lifecycle.IndexOf('ImportArtifact') -gt $lifecycle.IndexOf('VerifyProcessIdentity') -and $productionOps -match 'Test-IsolatedProcessOwnership' -and $productionOps -match 'Stop-IsolatedMysqlGracefully')
Assert-Check 'manifest write and key/login cleanup are adapter operations' ($lifecycle -match 'WriteManifest' -and $lifecycle -match 'ClearKeys' -and $lifecycle -match 'CleanupLogin' -and $productionOps -match 'restore-only-verification.json')
Assert-Check 'production restore-only operations do not invoke source database routing or mysqldump' ($restore+$lifecycle+$productionOps -notmatch 'Get-HrmsDevelopmentDatabaseConfiguration|Invoke-HrmsCatalogRouteCheck|mysqldump|Write-EncryptedDump')
Assert-Check 'port fallback requires netstat inventory, CIM consistency, verified service family and exclusive bind' ($source -match 'netstat\.exe.*-ano -p tcp' -and $source -match 'CIM and Windows TCP inventories disagree' -and $source -match 'Assert-HrmsMySql80SnapshotUnchanged' -and $source -match 'unparented or unrelated mysqld process' -and $source -match 'ExclusiveAddressUse')
Assert-Check 'MySQL80 process family requires executable, defaults config, ancestry and start identity' ($source -match 'Resolve-HrmsMySql80ProcessFamily' -and $source -match 'ProcessStartIdentity' -and $source -match 'Get-HrmsMySqlDefaultsFile' -and $lifecycle -match 'CaptureMySql80Identity' -and $lifecycle -match 'VerifyMySql80Identity')
Assert-Check 'process-start identities are normalized as 64-bit ticks without Int32 truncation' ($familyValidation -match 'ConvertTo-HrmsProcessStartIdentity' -and $familyValidation -notmatch '\[int\]\$rootRecord\.ProcessStartIdentity|\[int\]\$record\.ProcessStartIdentity')
Assert-Check 'snapshot errors expose sanitized stage categories without command-line values' ($source -match 'LastMySql80IdentityDiagnostic' -and $source -match 'could not be established \(\$\(\$script:LastMySql80IdentityDiagnostic\)\)' -and $source -notmatch 'throw[^\r\n]*\$rootRecord\.CommandLine')
Assert-Check 'production restore verifies and records base-table row counts' ($productionOps -match 'Assert-RestoredRowCounts \$tableNames \$rowCounts' -and $productionOps -match 'RestoredRowCounts')
Assert-Check 'temporary directory owner must be current user or built-in Administrators before ACL construction' ($source -match '\$ownerSid\.Equals\(\$identity\.User\)' -and $source -match '\$ownerSid\.Equals\(\$administratorsSid\)' -and $source -match 'credential_acl\.unapproved_owner')
Assert-Check 'credential file owner validation compares SID objects and recognizes only the approved elevated token owner' ($source -match 'ActualOwnerSid\.Equals\(\$CurrentUserSid\)' -and $source -match 'TokenOwnerSid\.Equals\(\$administratorsSid\)' -and $source -match 'ActualOwnerSid\.Equals\(\$TokenOwnerSid\)' -and $source -match 'Resolve-HrmsCredentialFileOwnerSid \$actualOwnerSid \$identity\.User \$identity\.Owner')
Assert-Check 'restrictive filesystem DACL is applied through Set-Acl and verified after write' ($source -match 'Set-Acl -LiteralPath \$Path -AclObject \$current -ErrorAction Stop' -and $source -match 'Read-VerifiedAcl \$Path \$ownerSid' -and $source -notmatch 'SetNamedSecurityInfo')
Assert-Check 'preflight JSON exposes only sanitized ACL category, stage, exception and error codes' ($source -match 'FailureCategory=.*aclDiagnostic\.Category' -and $source -match 'OperationStage=.*aclDiagnostic\.Stage' -and $source -match 'Win32ErrorCode=.*aclDiagnostic\.Win32ErrorCode' -and $source -notmatch 'CredentialAclFailureDetails\.Reason|aclDiagnostic\.Message|aclDiagnostic\.Path')
Assert-Check 'preflight protects once after creation and cleanup verifies absence' ($productionOps -match 'CreatePreflightRun=\{' -and $productionOps -notmatch 'CreatePreflightRun=\{[\s\S]*?Protect-Directory' -and $productionOps -match 'if\(Test-Path -LiteralPath \$full\).*could not be confirmed')

# Load functions only. The library guard returns before any script entry point.
$env:HRMS_RESTORE_ONLY_TEST_LIBRARY = '1'
. $scriptPath
Remove-Item Env:\HRMS_RESTORE_ONLY_TEST_LIBRARY -ErrorAction SilentlyContinue

# Model the elevated Windows token owner case using synthetic SIDs and an
# in-memory credential-file security descriptor. No ownership is assigned on disk.
$currentUserSid=[Security.Principal.WindowsIdentity]::GetCurrent().User
$administratorsSid=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$syntheticFileSecurity=[Security.AccessControl.FileSecurity]::new()
$syntheticFileSecurity.SetOwner($administratorsSid)
$syntheticFileOwner=$syntheticFileSecurity.GetOwner([Security.Principal.SecurityIdentifier])
$resolvedOwner=Resolve-HrmsCredentialFileOwnerSid $syntheticFileOwner $currentUserSid $administratorsSid
Assert-Check 'elevated token owner SID is accepted for a synthetic credential file' ($resolvedOwner.Equals($administratorsSid) -and $script:CredentialFileOwnerIdentity -eq 'elevated_token_owner')
$adminOwnedAcl=New-RestrictedAcl $false $administratorsSid.Value
$adminOwnedAcl.SetOwner($administratorsSid)
Assert-RestrictiveAcl $adminOwnedAcl $administratorsSid.Value $false
$adminAclSids=@($adminOwnedAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])|ForEach-Object {$_.IdentityReference.Value}|Sort-Object -Unique)
$expectedAdminAclSids=@(@($administratorsSid.Value,'S-1-5-18')|Sort-Object -Unique)
Assert-Check 'elevated-owner credential DACL remains Administrators-and-SYSTEM only' (($adminAclSids -join ',') -eq ($expectedAdminAclSids -join ','))
$unapprovedOwnerRejected=$false
try{[void](Resolve-HrmsCredentialFileOwnerSid $syntheticFileOwner $currentUserSid $currentUserSid)}catch{$unapprovedOwnerRejected=$true}
Assert-Check 'Administrators owner is rejected when it is not the current token owner' ($unapprovedOwnerRejected -and $script:CredentialAclFailureDetails.Category -eq 'credential_acl.unapproved_owner')
$script:CredentialAclFailureDetails=$null

# Filesystem backed credential ACL round trip; fake material is never printed.
$login = $null
try {
    $login = New-MySqlLoginFile 'hrms_acl_test' 'non-secret-test-placeholder' 34060 $false
    $acl = Get-Acl -LiteralPath $login.Path
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $actualOwner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $ownerApproved = $actualOwner.Equals($identity.User) -or
        ($identity.Owner -and $identity.Owner.Equals($administratorsSid) -and $actualOwner.Equals($identity.Owner))
    Assert-Check 'credential file has restrictive verified ACL' ($acl.AreAccessRulesProtected -and $ownerApproved)
} catch {
    if ($script:CredentialAclFailureStage -eq 'directory ACL application') { $script:Skipped++; Write-Output 'SKIP credential ACL write denied by execution environment' }
    else { Assert-Check 'credential file has restrictive verified ACL' $false }
}
finally { if ($login) { Remove-MySqlLoginFile $login } }

# Socket policy tests use injected inventories and bind results; they do not probe
# a real port or enumerate live MySQL processes.
$knownEmpty=[pscustomobject]@{CimAvailable=$false;CimRows=@();NetstatAvailable=$true;NetstatRows=@()}
$testMysqlIdentity=[pscustomobject]@{ServiceName='MySQL80';Status='Running';StartType='Auto';ProcessId=7380;ExecutablePath='C:\mysql\mysqld.exe';ConfigPath='C:\mysql\my.ini';ProcessFamily=@([pscustomobject]@{ProcessId=7380;ParentProcessId=4;ProcessStartIdentity=73800;ExecutablePath='C:\mysql\mysqld.exe';CommandLine='mysqld --defaults-file=C:\mysql\my.ini'})}
$emptyInventory={param($port) $knownEmpty}.GetNewClosure();$freeBind={param($port) $true};$knownMysql={$testMysqlIdentity}.GetNewClosure()
$portCheckPassed=$false;try{$portCheckPassed=[bool](Assert-RestoreOnlyPortAvailable 34061 $testMysqlIdentity $emptyInventory $freeBind $knownMysql)}catch{}
Assert-Check 'empty fallback inventory plus successful exclusive bind passes' $portCheckPassed
$occupied=[pscustomobject]@{CimAvailable=$false;CimRows=@();NetstatAvailable=$true;NetstatRows=@([pscustomobject]@{LocalPort=34061;State='LISTENING';OwnerPid=999})}
$occupiedInventory={param($port) $occupied}.GetNewClosure();$occupiedRejected=$false;try{[void](Assert-RestoreOnlyPortAvailable 34061 $testMysqlIdentity $occupiedInventory $freeBind $knownMysql)}catch{$occupiedRejected=$true}
Assert-Check 'occupied port owner fails closed' $occupiedRejected
$unknownInventory=[pscustomobject]@{CimAvailable=$false;CimRows=@();NetstatAvailable=$false;NetstatRows=@()}
$unknownQuery={param($port) $unknownInventory}.GetNewClosure();$indeterminateRejected=$false;try{[void](Assert-RestoreOnlyPortAvailable 34061 $testMysqlIdentity $unknownQuery $freeBind $knownMysql)}catch{$indeterminateRejected=$true}
Assert-Check 'unavailable socket inventory fails closed' $indeterminateRejected

# The validator is exercised with tampered/truncated encrypted artifacts; no decrypt/import is invoked.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('hrms-hmac-test-' + [Guid]::NewGuid().ToString('N'))
$passphrase='non-secret-test-placeholder';$salt=New-CryptoRandomBytes 16;$iv=New-CryptoRandomBytes 16
$derive=[Security.Cryptography.Rfc2898DeriveBytes]::new($passphrase,$salt,600000,[Security.Cryptography.HashAlgorithmName]::SHA256)
$enc=$derive.GetBytes(32);$macKey=$derive.GetBytes(32);$derive.Dispose()
$aes=[Security.Cryptography.Aes]::Create();$aes.Key=$enc;$aes.IV=$iv;$aes.Mode='CBC';$aes.Padding='PKCS7';$plain=[Text.Encoding]::UTF8.GetBytes('simulated encrypted artifact');$cipher=$aes.CreateEncryptor().TransformFinalBlock($plain,0,$plain.Length);$aes.Dispose()
$header=[Text.Encoding]::ASCII.GetBytes("HRMSWEB1-AES256-HMAC-SHA256-v1`n")+$salt+$iv
$hmac=[Security.Cryptography.HMACSHA256]::new($macKey);$tag=$hmac.ComputeHash($header+$cipher);$hmac.Dispose()
[IO.File]::WriteAllBytes($fixture,$header+$cipher+$tag)
$validMac=$false;try{$info=Test-EncryptedDump $fixture $passphrase;$validMac=$true;[Array]::Clear($info.EncKey,0,$info.EncKey.Length);[Array]::Clear($info.MacKey,0,$info.MacKey.Length)}catch{}
Assert-Check 'valid fresh HMAC accepted' $validMac
$wrongPassphraseRejected=$false;$wrongPassCategory=$null
try{$null=Test-EncryptedDump $fixture 'different-test-placeholder'}catch{$wrongPassphraseRejected=$true;$wrongPassCategory=$_.Exception.Data['HrmsHmacFailureCategory']}
Assert-Check 'incorrect passphrase fails authentication without exposing secrets' ($wrongPassphraseRejected -and $wrongPassCategory -eq 'encrypted_artifact.authentication_failed')
[IO.File]::WriteAllBytes($fixture,$header+($cipher[0..($cipher.Length-1)])+$tag)
$corruptBytes=[IO.File]::ReadAllBytes($fixture);$corruptBytes[$header.Length+1]=$corruptBytes[$header.Length+1] -bxor 1;[IO.File]::WriteAllBytes($fixture,$corruptBytes)
$corruptRejected=$false;$corruptCategory=$null
try{$null=Test-EncryptedDump $fixture $passphrase}catch{$corruptRejected=$true;$corruptCategory=$_.Exception.Data['HrmsHmacFailureCategory']}
Assert-Check 'corrupted ciphertext fails with same non-oracular authentication category' ($corruptRejected -and $corruptCategory -eq 'encrypted_artifact.authentication_failed')
[IO.File]::WriteAllBytes($fixture,$header+$cipher+([byte[]]::new(32)))
$invalidMacRejected=$false;try{$null=Test-EncryptedDump $fixture $passphrase}catch{$invalidMacRejected=$true}
Assert-Check 'invalid HMAC rejected' $invalidMacRejected
[IO.File]::WriteAllBytes($fixture,$header+$cipher)
$missingMacRejected=$false;try{$null=Test-EncryptedDump $fixture $passphrase}catch{$missingMacRejected=$true}
Assert-Check 'missing HMAC rejected' $missingMacRejected
Remove-Item -LiteralPath $fixture -Force
Assert-Check 'encrypted fixture cleanup removes temporary artifact' (-not (Test-Path -LiteralPath $fixture))
$aesBytes=$enc+$macKey;[Array]::Clear($enc,0,$enc.Length);[Array]::Clear($macKey,0,$macKey.Length);[Array]::Clear($aesBytes,0,$aesBytes.Length)
Write-Output ("SUMMARY Passed={0} Failed={1} Skipped={2} Total={3}" -f $script:Passed,$script:Failed,$script:Skipped,($script:Passed+$script:Failed+$script:Skipped))
if ($script:Failed -gt 0) { exit 1 }
