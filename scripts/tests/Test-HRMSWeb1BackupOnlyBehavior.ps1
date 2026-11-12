$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot '..\Invoke-HRMSWeb1BackupRestoreVerification.ps1'
$source=Get-Content -LiteralPath $scriptPath -Raw
function Assert-BackupOnly([string]$Name,[bool]$Condition){if(-not $Condition){throw "FAIL: $Name"};Write-Output "PASS: $Name"}

Assert-BackupOnly 'explicit BackupOnly switch is declared' ($source -match '\[switch\]\$BackupOnly')
Assert-BackupOnly 'BackupOnly rejects combinations with restore-only modes' ($source -match 'if\(\$BackupOnly -and \(\$RestoreOnly -or \$RestoreOnlyPreflight\)\)')
Assert-BackupOnly 'BackupOnly requires only mysql and mysqldump binaries' ($source -match '\$requiredTools=if\(\$BackupOnly\)\{@\(\$mysql,\$dump\)\}')
Assert-BackupOnly 'BackupOnly applies backup capacity reserve without requiring restore volume capacity' ($source -match 'if\(\$BackupOnly\)\{\$restoreFree=\$null;\$requiredBytes=Assert-BackupCapacity \$databaseBytes \$backupFree \$backupFree\}')

$branchStart=$source.IndexOf('if($BackupOnly){', $source.IndexOf('$backupHash=(Get-FileHash'))
$restoreStart=$source.IndexOf('# A separate empty datadir, loopback-only bind, and non-live port are mandatory.', $branchStart)
Assert-BackupOnly 'BackupOnly completes after fresh HMAC and SHA-256, before restore operations' ($branchStart -gt 0 -and $restoreStart -gt $branchStart -and $source.Substring($source.IndexOf('$stage=''encrypted backup''',$a),$restoreStart-$source.IndexOf('$stage=''encrypted backup''',$a)) -match 'Test-EncryptedDump' -and $source.Substring($branchStart,$restoreStart-$branchStart) -match 'BackupVerified=\$true;RestoreVerified=\$false' -and $source.Substring($branchStart,$restoreStart-$branchStart) -match "CompletionStatus='BACKUP_ONLY_COMPLETE'")
$backupBranch=$source.Substring($branchStart,$restoreStart-$branchStart)
Assert-BackupOnly 'BackupOnly branch contains no datadir, port reservation, server launch, or import operation' ($backupBranch -notmatch 'New-Item.*isolated-mysql-data|TcpListener|Start\(\)|Import-EncryptedDump|RestorePort')
Assert-BackupOnly 'default restore and RestoreOnlyPreflight entry dispatch remains present' ($source -match 'if\(\$RestoreOnly -or \$RestoreOnlyPreflight\)' -and $source -match 'if\(\$RestoreOnlyPreflight\)')
Assert-BackupOnly 'encrypted artifact publication refuses overwrite' ($source -match '\[IO\.File\]::Move\(\$tmp,\$Destination\)')
