$ErrorActionPreference='Stop'
$env:HRMS_RESTORE_ONLY_TEST_LIBRARY='1'
. (Join-Path $PSScriptRoot '..\Invoke-HRMSWeb1BackupRestoreVerification.ps1')
Remove-Item Env:\HRMS_RESTORE_ONLY_TEST_LIBRARY -ErrorAction SilentlyContinue

$script:Passed=0;$script:Failed=0
$script:AllOperations=New-Object 'System.Collections.Generic.List[string]'
function Assert-Behavior([string]$Name,[bool]$Condition){if($Condition){$script:Passed++;Write-Output "PASS $Name"}else{$script:Failed++;Write-Output "FAIL $Name"}}
$script:RestoreScript=Join-Path $PSScriptRoot '..\Invoke-HRMSWeb1BackupRestoreVerification.ps1'
function Invoke-ActualRestoreEntry($Fake,[string]$Artifact) {
    try { & $script:RestoreScript -RestoreOnly -EncryptedBackupPath $Artifact -EvidenceRoot 'FAKE-ROOT' -RestoreOnlyOperations $Fake.Operations }
    finally { foreach($call in $Fake.Context.Calls){$script:AllOperations.Add($call)} }
}
function Invoke-ActualPreflightEntry($Fake,[string]$Artifact) {
    $writer=[IO.StringWriter]::new();$original=[Console]::Out;[Console]::SetOut($writer)
    try { Invoke-HrmsRestoreOnlyLifecycle -Ops $Fake.Operations -ArtifactPath $Artifact -ExpectedArtifactPath 'D:\HRMS\.db-backups\web1-recovery\web1-20261102-213800\hrms_web1-20261102-213800.sql.aes' -ExpectedSha256 '005E067878085D211FAE02BA1B4993BF7F0F145A192EC7D97DF943A9A9AAA570' -EvidenceRoot 'FAKE-ROOT' -PreflightOnly | Out-Null }
    catch {$Fake.Context.LastException=$_.Exception.Message}
    finally { [Console]::SetOut($original);foreach($call in $Fake.Context.Calls){$script:AllOperations.Add($call)} }
    return $writer.ToString()
}
function Get-PreflightReportText($Output) {
    foreach($entry in $Output){
        if($entry -is [System.Management.Automation.InformationRecord]){$candidate=[string]$entry.MessageData}
        else{$candidate=[string]$entry}
        if($candidate.StartsWith('{')){return $candidate}
    }
    return $null
}
function New-TestMySql80Identity([switch]$Child) {
    $family=@([pscustomobject]@{ProcessId=41;ParentProcessId=4;Name='mysqld.exe';ProcessStartIdentity=[long]638975000000000000;ExecutablePath='C:\mysql\mysqld.exe';CommandLine='"C:\mysql\mysqld.exe" --defaults-file="C:\ProgramData\MySQL80\my.ini" MySQL80'})
    if($Child){$family+= [pscustomobject]@{ProcessId=99;ParentProcessId=41;Name='mysqld.exe';ProcessStartIdentity=[long]638975000000100000;ExecutablePath='C:\mysql\mysqld.exe';CommandLine='"C:\mysql\mysqld.exe" --defaults-file="C:\ProgramData\MySQL80\my.ini" --server-id=2'}}
    return [pscustomobject]@{ServiceName='MySQL80';Status='Running';StartType='Auto';ProcessId=41;ExecutablePath='C:\mysql\mysqld.exe';ConfigPath='C:\ProgramData\MySQL80\my.ini';ProcessFamily=@($family)}
}
function New-FakeOperations([string]$Failure='') {
    $ctx=@{Calls=(New-Object 'System.Collections.Generic.List[string]');Manifest=$null;Failure=$Failure;Process=$null;LastException=$null}
    $ops=@{}
    $ops.ResolvePath={param($p) $p}.GetNewClosure()
    $ops.GetItem={param($p) [pscustomobject]@{PSIsContainer=$false;Length=12}}.GetNewClosure()
    $ops.GetHash={param($p) if($ctx.Failure -eq 'Hash'){'ALTERED'}else{'005E067878085D211FAE02BA1B4993BF7F0F145A192EC7D97DF943A9A9AAA570'}}.GetNewClosure()
    $ops.ReadPassphrase={'fake-passphrase'}.GetNewClosure()
    $ops.CreateRun={param($r) 'FAKE-RUN'}.GetNewClosure()
    $ops.ProtectDirectory={param($r) $ctx.Calls.Add('ProtectDirectory');if($ctx.Failure -in @('Protect','ProtectCleanup')){$script:CredentialAclFailureStage='directory ACL application';$script:CredentialAclFailureDetails=[pscustomobject]@{Stage='directory ACL application';Category='credential_acl.application_failed';ExceptionType='UnauthorizedAccessException';HResult='0x80070005';Win32ErrorCode=5};throw 'safe wrapper failure'}}.GetNewClosure()
    $ops.VerifyHmac={param($p,$pass) $ctx.Calls.Add('VerifyHmac');if($ctx.Failure -eq 'Hmac'){throw 'fake HMAC failure'};[pscustomobject]@{EncKey=@([byte]1);MacKey=@([byte]2)}}.GetNewClosure()
    $ops.CheckTools={$ctx.Calls.Add('CheckTools')}.GetNewClosure()
    $mysql80Identity=New-TestMySql80Identity
    $ops.CaptureMySql80Identity={$ctx.Calls.Add('CaptureMySQL80');if($ctx.Failure -eq 'ServiceCapture'){throw 'fake service identity unavailable'};$mysql80Identity}.GetNewClosure()
    $ops.VerifyMySql80Identity={param($baseline) $ctx.Calls.Add('VerifyMySQL80');if($ctx.Failure -eq 'ServiceChanged'){throw 'fake MySQL80 changed'}}.GetNewClosure()
    $ops.CheckPort={param($port,$pid) $ctx.Calls.Add('CheckPort');if($ctx.Failure -eq 'Port'){throw 'fake port conflict'}}.GetNewClosure()
    $ops.CreatePreflightRun={'FAKE-PREFLIGHT-TEMP'}.GetNewClosure()
    $ops.PreflightAclRoundTrip={$ctx.Calls.Add('AclRoundTrip');if($ctx.Failure -eq 'Acl'){throw 'ACL denied'}}.GetNewClosure()
    $ops.ValidateIsolatedConfiguration={param($s) $ctx.Calls.Add('ValidateConfiguration')}.GetNewClosure()
    $ops.CleanupPreflightArtifacts={param($p) $ctx.Calls.Add('CleanupPreflight');if($ctx.Failure -in @('CleanupPreflight','ProtectCleanup')){throw 'cleanup denied'}}.GetNewClosure()
    $ops.CreateDataDirectory={param($run) $ctx.Calls.Add('CreateDataDirectory')}.GetNewClosure()
    $ops.InitializeServer={param($s) $ctx.Calls.Add('InitializeServer')}.GetNewClosure()
    $ops.StartServer={param($s) $ctx.Calls.Add('StartServer');$ctx.Process=@{Running=$true};$ctx.Process}.GetNewClosure()
    $ops.WaitReady={param($s) $ctx.Calls.Add('WaitReady')}.GetNewClosure()
    $ops.VerifyProtocolIdentity={param($s) $ctx.Calls.Add('VerifyProtocolIdentity')}.GetNewClosure()
    $ops.VerifyProcessIdentity={param($s) $ctx.Calls.Add('VerifyProcessIdentity');if($ctx.Failure -eq 'Identity' -and $ctx.Calls.Count -ge 8){throw 'fake identity mismatch'}}.GetNewClosure()
    $ops.SetRestoreCredential={param($s) $ctx.Calls.Add('SetRestoreCredential')}.GetNewClosure()
    $ops.CreateLogin={param($s) $ctx.Calls.Add('CreateLogin');@{Path='FAKE-LOGIN'}}.GetNewClosure()
    $ops.CheckEmptyTarget={param($s) $ctx.Calls.Add('CheckEmptyTarget')}.GetNewClosure()
    $ops.ImportArtifact={param($s) $ctx.Calls.Add('ImportArtifact');if($ctx.Failure -eq 'Restore'){throw 'fake import failure'}}.GetNewClosure()
    $ops.VerifyRestoredData={param($s) $ctx.Calls.Add('VerifyRestoredData');if($ctx.Failure -eq 'RowCount'){throw 'fake row count verification failed'};$s.RestoredRowCounts=@([pscustomobject]@{Table='Employees';Rows=25})}.GetNewClosure()
    $ops.StopServer={param($s) $ctx.Calls.Add('StopServer');if($ctx.Failure -eq 'Cleanup'){throw 'fake cleanup failure'};$ctx.Process.Running=$false}.GetNewClosure()
    $ops.WriteManifest={param($s,$result) $ctx.Manifest=[pscustomobject]@{Result=$result;State=$s}}.GetNewClosure()
    $ops.CleanupLogin={param($l) $ctx.Calls.Add('CleanupLogin')}.GetNewClosure()
    $ops.ClearKeys={param($s) $ctx.Calls.Add('ClearKeys')}.GetNewClosure()
    return [pscustomobject]@{Operations=$ops;Context=$ctx}
}

$expected='D:\HRMS\.db-backups\web1-recovery\web1-20261102-213800\hrms_web1-20261102-213800.sql.aes';$hash='005E067878085D211FAE02BA1B4993BF7F0F145A192EC7D97DF943A9A9AAA570'
$fake=New-FakeOperations
$ok=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null;$ok=$true}catch{}
Assert-Behavior 'successful restore reaches PASS and graceful cleanup' ($ok -and $fake.Context.Manifest.Result -eq 'PASS' -and $fake.Context.Calls -contains 'ImportArtifact' -and $fake.Context.Calls -contains 'StopServer')
Assert-Behavior 'successful lifecycle carries verified base-table row counts into evidence state' ($fake.Context.Manifest.State.RestoredRowCounts.Count -eq 1 -and $fake.Context.Manifest.State.RestoredRowCounts[0].Table -eq 'Employees' -and $fake.Context.Manifest.State.RestoredRowCounts[0].Rows -eq 25)
$fake=New-FakeOperations;$preflightOutput=@(Invoke-ActualPreflightEntry $fake $expected);$threw=[bool]$fake.Context.LastException
$preflightJson=Get-PreflightReportText $preflightOutput
$preflightResult=if($preflightJson){$preflightJson|ConvertFrom-Json}else{$null}
Assert-Behavior 'successful preflight passes security gates and reports runtime-only stages' (-not $threw -and $preflightResult.Result -eq 'PASS' -and $fake.Context.Calls -contains 'AclRoundTrip' -and $fake.Context.Calls -contains 'ValidateConfiguration' -and $fake.Context.Calls -contains 'CleanupPreflight' -and $fake.Context.Calls -notcontains 'StartServer' -and $fake.Context.Calls -notcontains 'ImportArtifact')
$fake=New-FakeOperations 'Hmac';$oldPreference=$ErrorActionPreference;$ErrorActionPreference='Stop';$failedReport=@(Invoke-ActualPreflightEntry $fake $expected);$threw=[bool]$fake.Context.LastException;$ErrorActionPreference=$oldPreference
$failedJson=Get-PreflightReportText $failedReport;$failedObject=if($failedJson){$failedJson|ConvertFrom-Json}else{$null}
Assert-Behavior 'HMAC failure emits sanitized category without stale ACL diagnostics and blocks before startup' ($threw -and $failedObject.FailureStage -eq 'fresh encrypted-backup HMAC and passphrase' -and $failedObject.FailureCategory -eq 'encrypted_artifact.authentication_failed' -and $null -eq $failedObject.OperationStage -and $null -eq $failedObject.Win32ErrorCode -and $failedObject.CleanupStatus -eq 'SUCCEEDED' -and $fake.Context.Calls -notcontains 'StartServer' -and $fake.Context.Calls -contains 'CleanupPreflight')
$fake=New-FakeOperations 'Protect';$script:CredentialAclFailureStage='directory ACL application';$script:CredentialAclFailureDetails=[pscustomobject]@{Stage='directory ACL application';Category='credential_acl.application_failed';ExceptionType='UnauthorizedAccessException';HResult='0x80070005';Win32ErrorCode=5};$failedReport=@(Invoke-ActualPreflightEntry $fake $expected);$failedJson=Get-PreflightReportText $failedReport;$failedObject=if($failedJson){$failedJson|ConvertFrom-Json}else{$null}
Assert-Behavior 'directory ACL application failure exposes only sanitized diagnostic fields and cleans the run' ($failedObject.FailureCategory -eq 'credential_acl.application_failed' -and $failedObject.OperationStage -eq 'directory ACL application' -and $failedObject.ExceptionType -eq 'UnauthorizedAccessException' -and $failedObject.HResult -eq '0x80070005' -and $failedObject.Win32ErrorCode -eq 5 -and $failedObject.CleanupStatus -eq 'SUCCEEDED' -and $failedJson -notmatch 'FAKE-PREFLIGHT-TEMP|safe wrapper failure')
$fake=New-FakeOperations 'ProtectCleanup';$failedReport=@(Invoke-ActualPreflightEntry $fake $expected);$failedJson=Get-PreflightReportText $failedReport;$failedObject=if($failedJson){$failedJson|ConvertFrom-Json}else{$null}
Assert-Behavior 'directory protection failure with unconfirmed cleanup reports BLOCKED and never starts server' ($failedObject.CleanupStatus -eq 'BLOCKED' -and @($failedObject.Stages|Where-Object {$_.Stage -eq 'temporary preflight artifact cleanup' -and $_.Result -eq 'BLOCKED'}).Count -eq 1 -and $fake.Context.Calls -contains 'CleanupPreflight' -and $fake.Context.Calls -notcontains 'StartServer')
$fake=New-FakeOperations 'Hash';$null=Invoke-ActualPreflightEntry $fake $expected;$threw=[bool]$fake.Context.LastException
Assert-Behavior 'altered backup hash blocks preflight before passphrase or server operations' ($threw -and $fake.Context.Calls.Count -eq 0)
$fake=New-FakeOperations 'Acl';$failedReport=@(Invoke-ActualPreflightEntry $fake $expected);$threw=[bool]$fake.Context.LastException;$failedJson=Get-PreflightReportText $failedReport;$failedObject=if($failedJson){$failedJson|ConvertFrom-Json}else{$null}
Assert-Behavior 'ACL round-trip failure reports its first failed stage and successful cleanup' ($threw -and $failedObject.FailureStage -eq 'credential-file ACL round-trip' -and $failedObject.CleanupStatus -eq 'SUCCEEDED' -and $fake.Context.Calls -contains 'CleanupPreflight' -and $fake.Context.Calls -notcontains 'StartServer')
$fake=New-FakeOperations 'CleanupPreflight';$failedReport=@(Invoke-ActualPreflightEntry $fake $expected);$threw=[bool]$fake.Context.LastException;$failedJson=Get-PreflightReportText $failedReport;$failedObject=if($failedJson){$failedJson|ConvertFrom-Json}else{$null}
Assert-Behavior 'temporary artifact cleanup failure is separated from first failed stage' ($threw -and $failedObject.FailureStage -eq 'temporary preflight artifact cleanup' -and $failedObject.CleanupStatus -eq 'BLOCKED' -and $fake.Context.Calls -notcontains 'StartServer')
$preflightHost=if($PSVersionTable.PSEdition -eq 'Desktop'){'powershell.exe'}else{'pwsh.exe'}
$preflightPsi=[Diagnostics.ProcessStartInfo]::new();$preflightPsi.FileName=$preflightHost;$preflightPsi.UseShellExecute=$false;$preflightPsi.RedirectStandardOutput=$true;$preflightPsi.RedirectStandardError=$true;$preflightPsi.CreateNoWindow=$true
$preflightScript=[IO.Path]::GetFullPath($script:RestoreScript);$preflightPsi.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$preflightScript+'" -RestoreOnlyPreflight -EncryptedBackupPath "D:\HRMS\preflight-invalid-fixture.sql.aes"'
$preflightProcess=[Diagnostics.Process]::new();$preflightProcess.StartInfo=$preflightPsi;[void]$preflightProcess.Start();$preflightStdout=$preflightProcess.StandardOutput.ReadToEnd();$preflightStderr=$preflightProcess.StandardError.ReadToEnd();$preflightProcess.WaitForExit();$entryReport=$null
foreach($line in ($preflightStdout -split "`r?`n")){if($line.StartsWith('{')){$entryReport=$line;break}}
$entryObject=if($entryReport){$entryReport|ConvertFrom-Json}else{$null}
Assert-Behavior 'script entry emits sanitized failure report and nonzero exit code' ($preflightProcess.ExitCode -eq 1 -and $entryObject.Result -eq 'BLOCKED' -and $entryObject.FailureStage -like 'artifact selection*' -and $entryObject.CleanupStatus -eq 'NOT_REQUIRED' -and $preflightStdout -notmatch 'fake-passphrase|non-secret-test-placeholder|EncKey|MacKey')
Assert-Behavior 'success path never invokes source backup, live MySQL, or MySQL80 stop operations' (@($fake.Context.Calls | Where-Object {$_ -match 'mysqldump|Source|LiveMySql|3306|StopNormal|StopMySQL80|ShutdownMySQL80'}).Count -eq 0)

$fake=New-FakeOperations 'Hmac';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'HMAC failure blocks before process startup' ($threw -and $fake.Context.Manifest.Result -eq 'BLOCKED' -and $fake.Context.Calls -notcontains 'StartServer')
$fake=New-FakeOperations;$threw=$false;try{Invoke-ActualRestoreEntry $fake 'WRONG-ARTIFACT'|Out-Null}catch{$threw=$true}
Assert-Behavior 'wrong artifact is rejected before filesystem or MySQL operations' ($threw -and $fake.Context.Calls.Count -eq 0)
$fake=New-FakeOperations 'Port';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'port conflict fails before isolated process startup' ($threw -and $fake.Context.Calls -notcontains 'StartServer')
$fake=New-FakeOperations 'Identity';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'process identity mismatch prevents import and graceful stop' ($threw -and $fake.Context.Calls -notcontains 'ImportArtifact' -and $fake.Context.Calls -notcontains 'StopServer')
$fake=New-FakeOperations 'Restore';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'restore failure records BLOCKED and attempts owned graceful cleanup' ($threw -and $fake.Context.Manifest.Result -eq 'BLOCKED' -and $fake.Context.Calls -contains 'StopServer')
$fake=New-FakeOperations 'Cleanup';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'cleanup failure reports BLOCKED without force termination' ($threw -and $fake.Context.Manifest.Result -eq 'BLOCKED' -and $fake.Context.Calls -notcontains 'ForceStop')
Assert-Behavior 'all actual-entry scenarios avoid source backup, live MySQL and normal service operations' (@($script:AllOperations | Where-Object {$_ -match 'mysqldump|Source|LiveMySql|3306|StopNormal|StopMySQL80|ShutdownMySQL80'}).Count -eq 0)

$fake=New-FakeOperations 'ServiceCapture';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'missing MySQL80 baseline identity blocks before server startup' ($threw -and $fake.Context.Calls -notcontains 'StartServer')
$fake=New-FakeOperations 'ServiceChanged';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'changed MySQL80 identity blocks lifecycle completion after isolated cleanup' ($threw -and $fake.Context.Manifest.Result -eq 'BLOCKED' -and $fake.Context.Calls -contains 'VerifyMySQL80')
$fake=New-FakeOperations 'RowCount';$threw=$false;try{Invoke-ActualRestoreEntry $fake $expected|Out-Null}catch{$threw=$true}
Assert-Behavior 'restored row count verification failure blocks and cleans up only isolated instance' ($threw -and $fake.Context.Manifest.Result -eq 'BLOCKED' -and $fake.Context.Calls -contains 'StopServer')

function Invoke-FakePortGate($Snapshot,$ExpectedIdentity,$CurrentIdentity,[bool]$BindResult) {
    $socket={param($p) $Snapshot}.GetNewClosure();$bind={param($p) $BindResult}.GetNewClosure();$processes={$CurrentIdentity}.GetNewClosure()
    Assert-RestoreOnlyPortAvailable -Port 34060 -ExpectedMySql80Identity $ExpectedIdentity -SocketInventory $socket -BindProbe $bind -MySqlProcessInventory $processes
}
$rootOnly=New-TestMySql80Identity
$verifiedChild=New-TestMySql80Identity -Child
$childDefaultsWhitespace='"C:\mysql\mysqld.exe" --defaults-file "C:\ProgramData\MySQL80\my.ini" --server-id=2'
$parsedChildDefaults=Get-HrmsMySqlDefaultsFile $childDefaultsWhitespace
Assert-Behavior 'quoted whitespace-separated child defaults-file argument is accepted' ([string]::Equals($parsedChildDefaults,[IO.Path]::GetFullPath('C:\ProgramData\MySQL80\my.ini'),[StringComparison]::OrdinalIgnoreCase))
$quotedCompleteOption='"C:\mysql\mysqld.exe" "--defaults-file=C:\ProgramData\MySQL80\my.ini" --server-id=2'
$parsedQuotedOption=Get-HrmsMySqlDefaultsFile $quotedCompleteOption
Assert-Behavior 'quoted complete defaults-file option token is accepted' ([string]::Equals($parsedQuotedOption,[IO.Path]::GetFullPath('C:\ProgramData\MySQL80\my.ini'),[StringComparison]::OrdinalIgnoreCase))
$quotedChild=New-TestMySql80Identity -Child;$quotedChild.ProcessFamily[1].CommandLine=$quotedCompleteOption
$quotedFamily=@(Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $quotedChild.ProcessFamily)
Assert-Behavior 'confirmed quoted child option passes full MySQL80 family identity validation' ($quotedFamily.Count -eq 2)
$badDefaultsCases=@('"C:\mysql\mysqld.exe"','mysqld --defaults-file','mysqld --defaults-file=','mysqld --defaults-file=""','mysqld --defaults-file="C:\ProgramData\MySQL80\my.ini" --defaults-file="C:\Other\my.ini"','mysqld --defaults-file="C:\ProgramData\MySQL80\my.ini" --defaults-file','mysqld "--defaults-file=C:\ProgramData\MySQL80\my.ini" --defaults-file=C:\Other\my.ini','mysqld --defaults-file=relative.ini','mysqld --defaults-file="C:\ProgramData\MySQL80\my.ini')
$allBadDefaultsRejected=$true;foreach($badDefaults in $badDefaultsCases){try{$null=Get-HrmsMySqlDefaultsFile $badDefaults;$allBadDefaultsRejected=$false}catch{}}
Assert-Behavior 'missing, empty, duplicate, conflicting and malformed defaults-file arguments are rejected' $allBadDefaultsRejected
$emptyFallback=[pscustomobject]@{CimAvailable=$false;CimRows=@();NetstatAvailable=$true;NetstatRows=@()}
$portPassed=$false;try{$portPassed=Invoke-FakePortGate $emptyFallback $verifiedChild $verifiedChild $true}catch{}
Assert-Behavior 'netstat fallback plus exclusive loopback bind and known service PID passes' $portPassed
$recordsOutOfOrder=@($verifiedChild.ProcessFamily[1],$verifiedChild.ProcessFamily[0])
$resolvedFamily=@(Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $recordsOutOfOrder)
Assert-Behavior 'MySQL80 child with 64-bit process-start ticks is accepted when the full identity matches' ($resolvedFamily.Count -eq 2 -and $resolvedFamily[1].ParentProcessId -eq 41 -and $resolvedFamily[1].ProcessStartIdentity -eq 638975000000100000)
$childOnlyOverflow=New-TestMySql80Identity -Child;$childOnlyOverflow.ProcessFamily[0].ProcessStartIdentity=4100
$childOverflowFamily=@(Resolve-HrmsMySql80ProcessFamily 41 $childOnlyOverflow.ExecutablePath $childOnlyOverflow.ConfigPath $childOnlyOverflow.ProcessFamily)
Assert-Behavior 'large child start identity remains valid when the service root identity is small' ($childOverflowFamily.Count -eq 2 -and $childOverflowFamily[1].ProcessStartIdentity -eq 638975000000100000)
$dmtfStart=ConvertTo-HrmsProcessStartIdentity '20261030193155.000000+000'
Assert-Behavior 'DMTF process creation time normalizes to positive 64-bit UTC ticks' ($dmtfStart -gt [int]::MaxValue)
$badChild=New-TestMySql80Identity -Child;$badChild.ProcessFamily[1].ExecutablePath='C:\other\mysqld.exe';$rejected=$false;try{$null=Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $badChild.ProcessFamily}catch{$rejected=$true}
Assert-Behavior 'MySQL80 child with a different executable is rejected' $rejected
$badChild=New-TestMySql80Identity -Child;$badChild.ProcessFamily[1].ExecutablePath='C:\other\mysqld.exe';$script:LastMySql80IdentityDiagnostic=$null;try{$null=Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $badChild.ProcessFamily}catch{}
Assert-Behavior 'identity mismatch diagnostic is stage-specific and excludes command-line contents' ($script:LastMySql80IdentityDiagnostic -eq 'mysql80.process.child_executable_mismatch' -and $script:LastMySql80IdentityDiagnostic -notmatch 'C:\\|my.ini')
$badChild=New-TestMySql80Identity -Child;$badChild.ProcessFamily[1].CommandLine='"C:\mysql\mysqld.exe" --defaults-file="C:\Other\my.ini"';$rejected=$false;try{$null=Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $badChild.ProcessFamily}catch{$rejected=$true}
Assert-Behavior 'MySQL80 child with a different defaults configuration is rejected' $rejected
$badChild=New-TestMySql80Identity -Child;$badChild.ProcessFamily[1].ParentProcessId=202;$rejected=$false;try{$null=Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $badChild.ProcessFamily}catch{$rejected=$true}
Assert-Behavior 'MySQL80 process with an unverified parent is rejected' $rejected
$badChild=New-TestMySql80Identity -Child;$badChild.ProcessFamily[1].ProcessStartIdentity=0;$rejected=$false;try{$null=Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $badChild.ProcessFamily}catch{$rejected=$true}
Assert-Behavior 'MySQL80 child without a process-start identity is rejected' $rejected
$inconsistent=[pscustomobject]@{CimAvailable=$true;CimRows=@();NetstatAvailable=$true;NetstatRows=@([pscustomobject]@{LocalPort=34060;State='LISTENING';OwnerPid=41})}
$blocked=$false;try{[void](Invoke-FakePortGate $inconsistent $rootOnly $rootOnly $true)}catch{$blocked=$_.Exception.Message -like '*Restore-only blocked:*'}
Assert-Behavior 'inconsistent socket inventories fail closed' $blocked
$unknownOwner=[pscustomobject]@{CimAvailable=$false;CimRows=@();NetstatAvailable=$true;NetstatRows=@([pscustomobject]@{LocalPort=34060;State='LISTENING';OwnerPid=0})}
$blocked=$false;try{[void](Invoke-FakePortGate $unknownOwner $rootOnly $rootOnly $true)}catch{$blocked=$_.Exception.Message -like '*Restore-only blocked:*'}
Assert-Behavior 'listener with unknown ownership fails closed' $blocked
$unrelated=@($verifiedChild.ProcessFamily)+@([pscustomobject]@{ProcessId=202;ParentProcessId=88;Name='mysqld.exe';ProcessStartIdentity=20200;ExecutablePath='C:\mysql\mysqld.exe';CommandLine='"C:\mysql\mysqld.exe" --defaults-file="C:\ProgramData\MySQL80\my.ini"'})
$blocked=$false;try{$null=Resolve-HrmsMySql80ProcessFamily 41 $rootOnly.ExecutablePath $rootOnly.ConfigPath $unrelated}catch{$blocked=$_.Exception.Message -like '*unparented or unrelated*'}
Assert-Behavior 'unrelated mysqld process without verified service ancestry fails closed' $blocked
$changedChild=New-TestMySql80Identity -Child;$changedChild.ProcessFamily[1].ProcessStartIdentity++
$blocked=$false;try{[void](Invoke-FakePortGate $emptyFallback $verifiedChild $changedChild $true)}catch{$blocked=$_.Exception.Message -like '*process-family identity changed*'}
Assert-Behavior 'changed child process start identity fails closed' $blocked
$blocked=$false;try{[void](Invoke-FakePortGate $emptyFallback $rootOnly $rootOnly $false)}catch{$blocked=$_.Exception.Message -like '*Restore-only blocked:*'}
Assert-Behavior 'exclusive loopback bind failure fails closed' $blocked
$parsed=@(ConvertFrom-HrmsNetstatTcpRows @('  TCP    0.0.0.0:34060    0.0.0.0:0    LISTENING    7380','  TCP    [::]:3306    [::]:0    LISTENING    7380') 34060)
Assert-Behavior 'supported netstat parser captures wildcard listener PID and ignores unrelated port' ($parsed.Count -eq 1 -and $parsed[0].OwnerPid -eq 7380 -and $parsed[0].LocalPort -eq 34060)
$unknownParsed=$false;try{$null=ConvertFrom-HrmsNetstatTcpRows @('  TCP    127.0.0.1:34060    0.0.0.0:0    LISTENING    0') 34060}catch{$unknownParsed=$_.Exception.Message -like '*ownership is unknown*'}
Assert-Behavior 'netstat listener with PID zero is rejected as unknown ownership' $unknownParsed
$malformedParsed=$false;try{$null=ConvertFrom-HrmsNetstatTcpRows @('  TCP    invalid') 34060}catch{$malformedParsed=$_.Exception.Message -like '*unparseable row*'}
Assert-Behavior 'unparseable netstat TCP row fails closed' $malformedParsed

Write-Output ("SUMMARY Passed={0} Failed={1} Total={2}" -f $script:Passed,$script:Failed,($script:Passed+$script:Failed))
if($script:Failed -gt 0){exit 1}
