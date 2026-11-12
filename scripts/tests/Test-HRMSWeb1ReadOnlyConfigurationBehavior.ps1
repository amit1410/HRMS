$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot '..\Invoke-HRMSWeb1BackupRestoreVerification.ps1'
$passed=0
function Assert-ReadOnly([string]$Name,[bool]$Condition){if(-not $Condition){throw "FAIL: $Name"};$script:passed++;Write-Output "PASS $Name"}

# Invoke the normal script entry point with deliberately incomplete synthetic
# environment configuration. This must fail before credential creation or any
# MySQL process launch, and its report must remain sanitized.
$oldCatalog=$env:ConnectionStrings__Catalog;$oldTenant=$env:Sharding__MySqlConnectionStringTemplate
try{
    $env:ConnectionStrings__Catalog='synthetic-value'
    Remove-Item Env:\Sharding__MySqlConnectionStringTemplate -ErrorAction SilentlyContinue
    $output=@(& $scriptPath -ReadOnlyConfigurationCheck 2>&1)
    $report=$output | ForEach-Object {try{$_ | ConvertFrom-Json}catch{$null}} | Where-Object {$_.Result -eq 'BLOCKED'} | Select-Object -First 1
    Assert-ReadOnly 'partial environment configuration is classified by the complete entry point' ($null -ne $report -and $report.Category -eq 'configuration.partial_environment' -and $report.DatabaseWritesPerformed -eq $false)
    Assert-ReadOnly 'read-only diagnostic withholds error text and secrets' ($report.ErrorTextWithheld -eq $true -and -not ($report | ConvertTo-Json -Compress).Contains('synthetic-value'))

    try{$output=@(& $scriptPath -BackupOnly -MysqlBin (Join-Path $env:TEMP 'hrms-missing-tools-fixture') 2>&1)}catch{$output=@($_.Exception.Message)}
    Assert-ReadOnly 'BackupOnly fails closed on synthetic missing tools before database work' (($output -join "`n") -match 'Required MySQL 8.0 tool is missing')

    $env:ConnectionStrings__Catalog='Server=127.0.0.1;Port=3306;Database=hrms_catalog;User ID=synthetic;Password=synthetic'
    $env:Sharding__MySqlConnectionStringTemplate='Server=127.0.0.1;Port=3306;Database=hrms_{shardKey};User ID=synthetic;Password=synthetic'
    $output=@(& $scriptPath -ReadOnlyConfigurationCheck -MysqlBin (Join-Path $env:TEMP 'hrms-missing-tools-fixture') 2>&1)
    $report=$output | ForEach-Object {try{$_ | ConvertFrom-Json}catch{$null}} | Where-Object {$_.Result -eq 'BLOCKED'} | Select-Object -First 1
    $reportJson=$report|ConvertTo-Json -Compress
    $aclDenied=($report.Category -eq 'configuration.credential_protection_failed' -and $report.CredentialProtectionCategory -eq 'credential_acl.application_failed' -and $report.ErrorId -match 'SetAclCommand' -and $report.HResult -eq '0x80070005' -and $report.Win32ErrorCode -eq 5)
    $aclSucceeded=($report.Category -eq 'configuration.mysql_client_missing')
    $credentialStages=@('credential temporary path construction','credential temporary directory creation','directory owner read','directory owner validation','directory ACL construction','directory ACL application','directory DACL readback','directory DACL verification','credential option-file path construction','credential option-file initialization','credential file ACL retrieval','credential file owner validation','credential file DACL construction','credential file ACL application','credential file DACL readback','credential file DACL verification','credential option-file content write')
    $scriptSource=Get-Content -LiteralPath $scriptPath -Raw
    $stagesPresent=$true;foreach($credentialStage in $credentialStages){if(-not $scriptSource.Contains("'$credentialStage'")){$stagesPresent=$false;break}}
    Assert-ReadOnly 'synthetic credentials use Set-Acl and report fail-closed credential substages without secret data' ($null -ne $report -and $report.CredentialProtectionDiagnosticVersion -eq 'acl-substage-v2-owner-sid' -and ($aclDenied -or $aclSucceeded) -and ($aclSucceeded -or $report.Stage -in $credentialStages) -and $stagesPresent -and $scriptSource.Contains('Set-Acl -LiteralPath $Path -AclObject $current') -and $scriptSource -notmatch 'SetNamedSecurityInfo' -and $report.ProtectedOptionFileUsed -eq $false -and $report.DatabaseWritesPerformed -eq $false -and $reportJson -notmatch 'synthetic|Password|Server=')
}finally{
    if($null -eq $oldCatalog){Remove-Item Env:\ConnectionStrings__Catalog -ErrorAction SilentlyContinue}else{$env:ConnectionStrings__Catalog=$oldCatalog}
    if($null -eq $oldTenant){Remove-Item Env:\Sharding__MySqlConnectionStringTemplate -ErrorAction SilentlyContinue}else{$env:Sharding__MySqlConnectionStringTemplate=$oldTenant}
}
Write-Output "PASS: $passed synthetic entry-point checks. No database or MySQL process access performed."
