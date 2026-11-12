param(
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..\.db-backups\web1-recovery'),
    [string]$MysqlBin = 'C:\Program Files\MySQL\MySQL Server 8.0\bin',
    [string]$TlsCaPath = (Join-Path $env:LOCALAPPDATA 'Anevra\HRMS\WEB1Backup\Config\local-dev-ca.pem'),
    [switch]$SelfTest,
    [switch]$SelfTestInvalidCredentials,
    [switch]$InspectIsolatedInstance,
    [switch]$ShutdownIsolatedInstance,
    [switch]$ReadOnlyConfigurationCheck,
    [switch]$RestoreOnly,
    [switch]$RestoreOnlyPreflight,
    [switch]$BackupOnly,
    [string]$EncryptedBackupPath,
    [int]$TargetPid,
    [long]$ExpectedProcessStartIdentity,
    [string]$ExpectedDataDir,
    [int]$ExpectedPort = 34060,
    [string]$ExpectedServerUuid,
    [hashtable]$RestoreOnlyOperations
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Initialize-HrmsDiagnosticState([switch]$Reset) {
    $defaults=@{
        LastMySqlDiagnostic=$null;LastMySqlFailure=$null;LastMySqlArgumentNames=@();LastMySqlLaunchMetadata=$null
        CurrentCatalogStage=$null;LastBackupDiagnosticCategory=$null;LastRestoreDiagnostic=$null
        CredentialAclFailureDetails=$null;CredentialAclFailureStage=$null;CredentialFileOwnerIdentity=$null;LastMySql80IdentityDiagnostic=$null
        ApprovedTlsCaPath=$null
    }
    foreach($entry in $defaults.GetEnumerator()){
        $existing=Get-Variable -Name $entry.Key -Scope Script -ErrorAction SilentlyContinue
        if($Reset -or $null -eq $existing){Set-Variable -Name $entry.Key -Value $entry.Value -Scope Script}
    }
}

function Reset-HrmsMySqlInvocationState {
    Initialize-HrmsDiagnosticState
    $script:LastMySqlDiagnostic=$null
    $script:LastMySqlFailure=$null
    $script:LastMySqlArgumentNames=@()
    $script:LastMySqlLaunchMetadata=$null
}

function Get-HrmsMySqlFailureState {
    Initialize-HrmsDiagnosticState
    return $script:LastMySqlFailure
}

function Resolve-HrmsMySqlFailureCategory([string]$Stage) {
    Initialize-HrmsDiagnosticState
    $failure=Get-HrmsMySqlFailureState
    if($failure){return $failure.Category}
    if($script:LastMySqlDiagnostic -match 'Authentication rejected'){return 'mysql.authentication_rejected'}
    if($script:LastMySqlDiagnostic -match 'TLS negotiation failed'){return 'mysql.tls_failed'}
    if($Stage -like 'catalog*'){return 'source.catalog_connection_failed'}
    if($Stage -like 'tenant*'){return 'source.tenant_connection_failed'}
    return 'configuration.read_only_check_failed'
}

function Resolve-HrmsTlsCaPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'A fully qualified CA certificate path is required for VERIFY_IDENTITY.'
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $approvedPath = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Anevra\HRMS\WEB1Backup\Config\local-dev-ca.pem'))
    if (-not [string]::Equals($fullPath, $approvedPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'TLS verification must use the protected HRMS local-development CA file.'
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw 'The configured TLS CA certificate file is unavailable.'
    }
    $acl = Get-Acl -LiteralPath $fullPath
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $allowed = @($currentSid.Value, 'S-1-5-18', 'S-1-5-32-544')
    if (-not $acl.AreAccessRulesProtected -or (-not $owner.Equals($currentSid) -and $owner.Value -ne 'S-1-5-32-544')) {
        throw 'The configured TLS CA file ACL or owner is not approved.'
    }
    foreach ($rule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) {
        if ($rule.IsInherited -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rule.IdentityReference.Value -notin $allowed) {
            throw 'The configured TLS CA file ACL contains an unapproved rule.'
        }
    }
    $pem = [IO.File]::ReadAllText($fullPath)
    $match = [regex]::Match($pem, '-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----', [Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) { throw 'The configured TLS CA file is not a PEM certificate.' }
    $bytes = [Convert]::FromBase64String(($match.Groups[1].Value -replace '\s', ''))
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes)
    try {
        $constraints = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
        $now = [DateTime]::UtcNow
        if (-not $constraints -or -not $constraints.CertificateAuthority -or
            $now -lt $certificate.NotBefore.ToUniversalTime() -or $now -gt $certificate.NotAfter.ToUniversalTime()) {
            throw 'The configured TLS certificate is not a currently valid CA certificate.'
        }
    } finally { $certificate.Dispose() }
    $script:ApprovedTlsCaPath = $fullPath
    return $fullPath
}

Initialize-HrmsDiagnosticState -Reset

function Invoke-Client {
    param([string]$Exe, [string[]]$ClientArguments, [string]$LoginFile, [string]$Sql, [PSCredential]$Credential=$null, [string]$User, [string]$Password, [int]$Port=3306, [string]$Database, [switch]$RequireTls, [switch]$SqlOnStandardInput)
    Reset-HrmsMySqlInvocationState
    $p=$null;$temporaryLogin=$null
    $script:LastMySqlFailure=$null
    $safeArgumentNames=New-Object 'System.Collections.Generic.List[string]'
    $phase='pre-launch: ProcessStartInfo creation'
    try {
        $psi = New-Object Diagnostics.ProcessStartInfo
        $psi.FileName = $Exe
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = [bool]$SqlOnStandardInput
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $phase='pre-launch: argument construction'
        if($Credential){$User=$Credential.UserName;$phase='pre-launch: SecureString conversion';$Password=Get-SecureText $Credential.Password}
        if($User -and -not $LoginFile){$temporaryLogin=New-MySqlLoginFile $User $Password $Port ([bool]$RequireTls) $script:ApprovedTlsCaPath;$LoginFile=$temporaryLogin.Path}
        if($LoginFile){Add-ProcessArgument $psi "--defaults-file=$LoginFile";$safeArgumentNames.Add('--defaults-file')}
        if($Credential -or $User){Add-ProcessArgument $psi '--host=127.0.0.1';$safeArgumentNames.Add('--host')}
        if($Port){Add-ProcessArgument $psi "--port=$Port"}
        if($Port){$safeArgumentNames.Add('--port')}
        if($Credential -or $User){Add-ProcessArgument $psi '--protocol=tcp';$safeArgumentNames.Add('--protocol')}
        if($User){$safeArgumentNames.Add('--user (protected option file)')}
        if($RequireTls){
            if([string]::IsNullOrWhiteSpace($script:ApprovedTlsCaPath)){throw 'VERIFY_IDENTITY requires an approved CA certificate.'}
            Add-ProcessArgument $psi '--ssl-mode=VERIFY_IDENTITY';$safeArgumentNames.Add('--ssl-mode')
            Add-ProcessArgument $psi "--ssl-ca=$($script:ApprovedTlsCaPath)";$safeArgumentNames.Add('--ssl-ca')
        }
        if($Database){Add-ProcessArgument $psi "--database=$Database";$safeArgumentNames.Add('--database')}
        foreach ($arg in $ClientArguments) { Add-ProcessArgument $psi $arg;$safeArgumentNames.Add(($arg -split '=',2)[0]) }
        if($SqlOnStandardInput){$safeArgumentNames.Add('SQL on redirected stdin')}
        else{Add-ProcessArgument $psi "--execute=$Sql";$safeArgumentNames.Add('--execute')}
        $script:LastMySqlArgumentNames=@($safeArgumentNames.ToArray())
        $script:LastMySqlLaunchMetadata=[pscustomobject]@{CredentialOptionFileUsed=[bool]$LoginFile;DefaultsOptionFirst=([bool]$LoginFile -and $safeArgumentNames.Count -gt 0 -and $safeArgumentNames[0] -eq '--defaults-file');ArgumentNames=@($safeArgumentNames.ToArray())}
        $phase='pre-launch: environment setup'
        Clear-MySqlPasswordEnvironment $psi
        $p = New-Object Diagnostics.Process
        $p.StartInfo = $psi
        $phase='process-start'
        [void]$p.Start()
        $Password=$null
        $phase='process-exit: stream setup and SQL exchange'
        $outTask = $p.StandardOutput.ReadToEndAsync(); $errTask = $p.StandardError.ReadToEndAsync()
        if($SqlOnStandardInput){$p.StandardInput.WriteLine($Sql);$p.StandardInput.Close()}
        $p.WaitForExit()
        $out = $outTask.GetAwaiter().GetResult(); $err = $errTask.GetAwaiter().GetResult()
        if ($p.ExitCode -ne 0) {
            $failure=Resolve-SanitizedMySqlClientFailure $p.ExitCode $err
            $script:LastMySqlFailure=$failure
            $script:LastMySqlDiagnostic = "Executable '$Exe'; process started; exit code $($failure.ExitCode); MySQL error $($failure.ErrorNumber): $($failure.Category)"
            throw 'MySQL client returned a nonzero exit code.'
        }
        return $out
    } catch {
        if(-not $script:LastMySqlDiagnostic -or $phase -ne 'process-exit: stream setup and SQL exchange') {
            $line=$_.InvocationInfo.ScriptLineNumber
            $type=$_.Exception.GetType().FullName
            $started=if($p -and $p.Id){'process started'}else{'process start not confirmed'}
            $script:LastMySqlDiagnostic = "Executable '$Exe'; $phase failed; $started; exception $type at script line $line; message and process output withheld."
        }
        throw $script:LastMySqlDiagnostic
    } finally {
        if($temporaryLogin){Remove-MySqlLoginFile $temporaryLogin}
        $Password=$null
    }
}

function Resolve-SanitizedMySqlClientFailure([int]$ExitCode,[string]$ErrorText) {
    $number=$null;$category='mysql.client_failed'
    if($ErrorText -match '(?im)\bERROR\s+(\d+)(?:\s+\([^)]+\))?:\s*(.*)$'){
        $number=[int]$Matches[1];$safeText=[string]$Matches[2]
        if($safeText -match '(?i)access denied|authentication'){$category='mysql.authentication_rejected'}
        elseif($safeText -match '(?i)ssl|tls|certificate|secure connection'){$category='mysql.tls_failed'}
        elseif($safeText -match '(?i)can.t connect|connection refused|timed out|lost connection'){$category='mysql.transport_failed'}
        elseif($safeText -match '(?i)unknown database'){$category='mysql.database_unavailable'}
        elseif($safeText -match '(?i)unknown option|unknown variable'){$category='mysql.option_rejected'}
    }
    return [pscustomobject]@{ExitCode=$ExitCode;ErrorNumber=$number;Category=$category;ErrorTextWithheld=$true}
}

function Invoke-HrmsCatalogRouteCheck([string]$MysqlExe,[string]$LoginPath) {
    $script:CurrentCatalogStage='catalog authentication'
    $catalogOutput=Invoke-Client $MysqlExe @('--batch','--skip-column-names') $LoginPath 'SELECT DATABASE(),@@port,@@version,@@server_uuid;' -Database 'hrms_catalog' -RequireTls
    $identity=ConvertFrom-SourceIdentity $catalogOutput 'hrms_catalog' '3306'
    $script:CurrentCatalogStage='catalog route lookup'
    $route=Invoke-Client $MysqlExe @('--batch','--skip-column-names') $LoginPath "SELECT TenantCode,Host,ShardKey,DatabaseProvider,Status FROM hrms_catalog.Tenants WHERE Host='web1.localhost';" -Database 'hrms_catalog' -RequireTls
    $script:CurrentCatalogStage='catalog route validation'
    try{[void](Assert-CatalogRoute $route)}catch{$script:LastBackupDiagnosticCategory='source.catalog_route_mismatch';throw 'Catalog route did not resolve exactly to active WEB1/web1/MySql.'}
    return [pscustomobject]@{Identity=$identity;RouteVerified=$true;Stage='catalog route verification'}
}

function ConvertFrom-SourceIdentity([string]$Output,[string]$ExpectedDatabase='hrms_web1',[string]$ExpectedPort='3306') {
    $lineList=New-Object 'System.Collections.Generic.List[string]'
    if(-not [string]::IsNullOrWhiteSpace($Output)){
        foreach($line in ($Output -split "`r`n|`n|`r")){$lineList.Add($line)}
        while($lineList.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lineList[$lineList.Count-1])){$lineList.RemoveAt($lineList.Count-1)}
    }
    $fieldCounts=@();foreach($line in $lineList){$fieldCounts+= $line.Split([char[]]@([char]9)).Count}
    $safeArgs=if($script:LastMySqlArgumentNames){$script:LastMySqlArgumentNames -join ','}else{'unavailable'}
    if($lineList.Count -ne 1){
        $script:LastMySqlDiagnostic="Source identity validation failed; stdout rows=$($lineList.Count); field counts=$($fieldCounts -join ','); argument names=$safeArgs."
        throw "Source validation failed: stdout row count $($lineList.Count) after removing trailing blank lines; exactly one row expected."
    }
    $parts=$lineList[0].Split([char[]]@([char]9))
    if($parts.Count -ne 4 -or $parts.Where({[string]::IsNullOrWhiteSpace($_)}).Count -gt 0){
        $script:LastMySqlDiagnostic="Source identity validation failed; stdout rows=1; field counts=$($fieldCounts -join ','); argument names=$safeArgs."
        throw 'Source validation failed: expected database, port, version, and server identity fields.'
    }
    if($parts[0] -ne $ExpectedDatabase){$script:LastMySqlDiagnostic="Source identity validation failed; database field mismatch; stdout rows=1; field counts=4; argument names=$safeArgs.";throw "Source validation failed: connected database was not $ExpectedDatabase."}
    if($parts[1] -ne $ExpectedPort){$script:LastMySqlDiagnostic="Source identity validation failed; port field mismatch; stdout rows=1; field counts=4; argument names=$safeArgs.";throw "Source validation failed: connected server port was not $ExpectedPort."}
    if($parts[2] -ne '8.0.46' -or [string]::IsNullOrWhiteSpace($parts[3])){throw 'Source validation failed: MySQL server version or instance identity was invalid.'}
    return $parts
}

function ConvertFrom-MySqlBatchField([string]$Field) {
    $decoded=New-Object Text.StringBuilder
    for($i=0;$i -lt $Field.Length;$i++){
        $ch=$Field[$i]
        if($ch -ne '\'){[void]$decoded.Append($ch);continue}
        $i++
        if($i -ge $Field.Length){throw 'Restore identity validation failed: malformed MySQL batch escape.'}
        switch($Field[$i]){
            '\' {[void]$decoded.Append('\');break}
            't' {[void]$decoded.Append([char]9);break}
            'n' {[void]$decoded.Append([char]10);break}
            'r' {[void]$decoded.Append([char]13);break}
            '0' {[void]$decoded.Append([char]0);break}
            default {throw 'Restore identity validation failed: unsupported MySQL batch escape.'}
        }
    }
    return $decoded.ToString()
}

function Get-CanonicalDirectoryIdentity([string]$Path) {
    try {
        $full=[IO.Path]::GetFullPath($Path)
        $root=[IO.Path]::GetPathRoot($full)
        while($full.Length -gt $root.Length -and ($full.EndsWith([string][IO.Path]::DirectorySeparatorChar) -or $full.EndsWith([string][IO.Path]::AltDirectorySeparatorChar))){$full=$full.Substring(0,$full.Length-1)}
        return $full
    } catch {throw 'Restore identity validation failed: datadir path was invalid.'}
}

function Test-RestoreServerIdentity([string]$Output,[string]$ExpectedDataDir,[int]$ExpectedPort) {
    if($ExpectedPort -eq 3306){throw 'Restore identity validation failed: isolated port must differ from source port 3306.'}
    $rows=@()
    if(-not [string]::IsNullOrWhiteSpace($Output)){$rows=@($Output -split "`r`n|`n|`r")}
    while($rows.Count -gt 0 -and [string]::IsNullOrWhiteSpace($rows[$rows.Count-1])){if($rows.Count -eq 1){$rows=@()}else{$rows=@($rows[0..($rows.Count-2)])}}
    if($rows.Count -ne 1){throw 'Restore identity validation failed: stdout must contain exactly one row.'}
    $fields=$rows[0].Split([char[]]@([char]9))
    if($fields.Count -ne 2 -or [string]::IsNullOrWhiteSpace($fields[0]) -or [string]::IsNullOrWhiteSpace($fields[1])){throw 'Restore identity validation failed: expected exactly two nonempty batch fields.'}
    $dataDir=ConvertFrom-MySqlBatchField $fields[0]
    $portText=ConvertFrom-MySqlBatchField $fields[1]
    if($dataDir.IndexOfAny([char[]]@([char]0,[char]9,[char]10,[char]13)) -ge 0){throw 'Restore identity validation failed: datadir contained a control character.'}
    $actualPath=Get-CanonicalDirectoryIdentity $dataDir
    $expectedPath=Get-CanonicalDirectoryIdentity $ExpectedDataDir
    if(-not [string]::Equals($actualPath,$expectedPath,[StringComparison]::OrdinalIgnoreCase)){throw 'Restore identity validation failed: datadir did not match the isolated restore directory.'}
    $actualPort=0
    if(-not [int]::TryParse($portText,[ref]$actualPort) -or $actualPort -lt 1 -or $actualPort -gt 65535 -or $actualPort -ne $ExpectedPort){throw 'Restore identity validation failed: returned port did not match the expected isolated port.'}
    return $true
}

function Add-ProcessArgument([Diagnostics.ProcessStartInfo]$StartInfo,[string]$Argument) {
    if($StartInfo.GetType().GetProperty('ArgumentList')) { [void]$StartInfo.ArgumentList.Add($Argument); return }
    $slashes=0;$quoted=New-Object Text.StringBuilder;[void]$quoted.Append('"')
    foreach($ch in $Argument.ToCharArray()){
        if($ch -eq '\'){$slashes++;continue}
        if($ch -eq '"'){[void]$quoted.Append((('\' * (2*$slashes+1)) -join ''));[void]$quoted.Append('"');$slashes=0;continue}
        if($slashes){[void]$quoted.Append((('\' * $slashes) -join ''));$slashes=0};[void]$quoted.Append($ch)
    }
    if($slashes){[void]$quoted.Append((('\' * (2*$slashes)) -join ''))};[void]$quoted.Append('"')
    if($StartInfo.Arguments){$StartInfo.Arguments+=' '};$StartInfo.Arguments+=$quoted.ToString()
}

function Clear-MySqlPasswordEnvironment([Diagnostics.ProcessStartInfo]$StartInfo) {
    # ProcessStartInfo inherits the parent environment; explicitly prevent MYSQL_PWD propagation.
    [void]$StartInfo.EnvironmentVariables.Remove('MYSQL_PWD')
}

function Get-SafeWin32ErrorCode([Exception]$Exception) {
    if($null -eq $Exception){return $null}
    $value=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$Exception.HResult),0)
    if(([long]$value -band 0xFFFF0000L) -eq 0x80070000L){return [int]($value -band 0xFFFF)}
    return $null
}

function Set-CredentialFailureDiagnostic([System.Management.Automation.ErrorRecord]$ErrorRecord,[string]$Stage) {
    $exception=$ErrorRecord.Exception
    $inner=$exception.InnerException
    $category=if($Stage -match 'ACL|DACL|owner'){'credential_acl.operation_failed'}elseif($Stage -match 'directory|temp path'){'credential_temp_storage.operation_failed'}else{'credential_option_file.operation_failed'}
    $script:CredentialAclFailureDetails=[pscustomobject]@{
        Stage=$Stage;Category=$category;ExceptionType=$exception.GetType().FullName
        InnerExceptionType=if($inner){$inner.GetType().FullName}else{$null}
        ErrorId=$ErrorRecord.FullyQualifiedErrorId;ScriptLineNumber=$ErrorRecord.InvocationInfo.ScriptLineNumber
        HResult=('0x{0:X8}' -f $exception.HResult)
        InnerHResult=if($inner){'0x{0:X8}' -f $inner.HResult}else{$null}
        Win32ErrorCode=if($null -ne (Get-SafeWin32ErrorCode $exception)){Get-SafeWin32ErrorCode $exception}elseif($inner){Get-SafeWin32ErrorCode $inner}else{$null}
        CleanupStatus=$null
    }
}

function Assert-RestrictiveAcl([Security.AccessControl.FileSystemSecurity]$Acl,[string]$ExpectedOwnerSid,[bool]$Directory) {
    $verifyStage=if($Directory){'directory DACL verification'}else{'credential file DACL verification'}
    $script:CredentialAclFailureStage=$verifyStage
    if($null -eq $Acl -or [string]::IsNullOrWhiteSpace($ExpectedOwnerSid)){throw 'Credential ACL verification failed: ACL or expected owner unavailable.'}
    try {
        if(-not $Acl.AreAccessRulesProtected){throw 'Credential ACL verification failed: inherited permissions remain enabled.'}
        $ownerSid=$Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        if($ownerSid -ne $ExpectedOwnerSid){throw 'Credential ACL verification failed: unexpected object owner.'}
        $rules=$Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])
        $allowed=@($ExpectedOwnerSid,'S-1-5-18')
        $seen=@{}
        foreach($rule in $rules){
            $sid=$rule.IdentityReference.Value
            if($rule.IsInherited){throw 'Credential ACL verification failed: inherited access rule detected.'}
            if($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow){throw 'Credential ACL verification failed: deny or unsupported access rule detected.'}
            if($sid -notin $allowed){throw 'Credential ACL verification failed: unexpected principal has access.'}
            if(($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl){throw 'Credential ACL verification failed: approved principal lacks required control rights.'}
            if($Directory){
                $required=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
                if(($rule.InheritanceFlags -band $required) -ne $required){throw 'Credential ACL verification failed: directory permissions do not protect descendants.'}
            } elseif($rule.InheritanceFlags -ne [Security.AccessControl.InheritanceFlags]::None){throw 'Credential ACL verification failed: file ACL has inheritance flags.'}
            $seen[$sid]=$true
        }
        foreach($sid in $allowed){if(-not $seen.ContainsKey($sid)){throw 'Credential ACL verification failed: required principal access is missing.'}}
    } catch {$script:CredentialAclFailureDetails=[pscustomobject]@{Stage=$verifyStage;Category='credential_acl.verification_failed';ExceptionType=$_.Exception.GetType().FullName;ErrorId=$_.FullyQualifiedErrorId;ScriptLineNumber=$_.InvocationInfo.ScriptLineNumber;HResult=('0x{0:X8}' -f $_.Exception.HResult);Win32ErrorCode=(Get-SafeWin32ErrorCode $_.Exception)};throw 'Credential ACL verification failed closed; no credential was written.'}
}

function New-RestrictedAcl([bool]$Directory,[string]$OwnerSid) {
    if([string]::IsNullOrWhiteSpace($OwnerSid)){throw 'Credential ACL construction failed: owner unavailable.'}
    if($Directory){$acl=[Security.AccessControl.DirectorySecurity]::new()}else{$acl=[Security.AccessControl.FileSecurity]::new()}
    $acl.SetAccessRuleProtection($true,$false)
    # New temporary objects are created by the current identity. Preserve that
    # ownership and verify it after applying the DACL; SetOwner requires
    # privileges that an ordinary owner does not need for a restrictive DACL.
    foreach($sidText in @($OwnerSid,'S-1-5-18')){
        $sid=[Security.Principal.SecurityIdentifier]::new($sidText)
        $inheritance=if($Directory){[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit}else{[Security.AccessControl.InheritanceFlags]::None}
        $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inheritance,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    return $acl
}

function Resolve-HrmsCredentialFileOwnerSid([Security.Principal.SecurityIdentifier]$ActualOwnerSid,[Security.Principal.SecurityIdentifier]$CurrentUserSid,[Security.Principal.SecurityIdentifier]$TokenOwnerSid) {
    if($null -eq $ActualOwnerSid -or $null -eq $CurrentUserSid){throw 'Credential ACL verification failed: owner identity unavailable.'}
    if($ActualOwnerSid.Equals($CurrentUserSid)){$script:CredentialFileOwnerIdentity='current_user';return $ActualOwnerSid}
    $administratorsSid=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    if($TokenOwnerSid -and $TokenOwnerSid.Equals($administratorsSid) -and $ActualOwnerSid.Equals($TokenOwnerSid)){$script:CredentialFileOwnerIdentity='elevated_token_owner';return $ActualOwnerSid}
    $script:CredentialFileOwnerIdentity='unapproved'
    $script:CredentialAclFailureDetails=[pscustomobject]@{Stage='credential file owner validation';Category='credential_acl.unapproved_owner';ExceptionType='SecurityException';HResult='0x80070005';Win32ErrorCode=5}
    throw 'Credential ACL verification failed: credential file owner is not the current user or the elevated process token owner.'
}

function Set-RestrictedAcl([string]$Path,[Security.AccessControl.FileSystemSecurity]$Acl,[string]$Stage) {
    $script:CredentialAclFailureDetails=$null
    try {
        # Preserve the current owner/group and other descriptor sections while
        # replacing only the DACL. Set-Acl follows the Windows/.NET ACL path
        # verified for Windows PowerShell 5.1 and PowerShell 7.
        $current=Get-Acl -LiteralPath $Path -ErrorAction Stop
        $current.SetAccessRuleProtection($true,$false)
        $existingRules=$current.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])
        $existingSids=@($existingRules | ForEach-Object {$_.IdentityReference.Value} | Select-Object -Unique)
        foreach($sidText in $existingSids){$sid=[Security.Principal.SecurityIdentifier]::new($sidText);$current.PurgeAccessRules($sid)}
        foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){[void]$current.AddAccessRule($rule)}
        Set-Acl -LiteralPath $Path -AclObject $current -ErrorAction Stop
    }
    catch {
        if($script:CredentialAclFailureDetails -and $script:CredentialAclFailureDetails.Stage -eq $Stage){throw}
        $e=$_.Exception;$inner=$e.InnerException
        $script:CredentialAclFailureDetails=[pscustomobject]@{Stage=$Stage;Category='credential_acl.application_failed';ExceptionType=$e.GetType().FullName;InnerExceptionType=if($inner){$inner.GetType().FullName}else{$null};ErrorId=$_.FullyQualifiedErrorId;ScriptLineNumber=$_.InvocationInfo.ScriptLineNumber;HResult=('0x{0:X8}' -f $e.HResult);InnerHResult=if($inner){'0x{0:X8}' -f $inner.HResult}else{$null};Win32ErrorCode=(Get-SafeWin32ErrorCode $e)}
        throw 'Credential ACL application failed closed.'
    }
}

function Read-VerifiedAcl([string]$Path,[string]$OwnerSid,[bool]$Directory) {
    $readStage=if($Directory){'directory DACL readback'}else{'credential file DACL readback'}
    $script:CredentialAclFailureStage=$readStage
    try{$actual=Get-Acl -LiteralPath $Path -ErrorAction Stop}catch{$script:CredentialAclFailureDetails=[pscustomobject]@{Stage=$readStage;Category='credential_acl.readback_failed';ExceptionType=$_.Exception.GetType().FullName;ErrorId=$_.FullyQualifiedErrorId;ScriptLineNumber=$_.InvocationInfo.ScriptLineNumber;HResult=('0x{0:X8}' -f $_.Exception.HResult);Win32ErrorCode=(Get-SafeWin32ErrorCode $_.Exception)};throw 'Credential ACL readback failed closed.'}
    Assert-RestrictiveAcl $actual $OwnerSid $Directory
    return $actual
}

function Protect-Directory([string]$Path) {
    $script:CredentialAclFailureDetails=$null
    $script:CredentialAclFailureStage='directory owner read'
    try{$ownerSid=(Get-Acl -LiteralPath $Path -ErrorAction Stop).GetOwner([Security.Principal.SecurityIdentifier])}
    catch{$script:CredentialAclFailureDetails=[pscustomobject]@{Stage='directory owner read';Category='credential_acl.owner_read_failed';ExceptionType=$_.Exception.GetType().FullName;HResult=('0x{0:X8}' -f $_.Exception.HResult);Win32ErrorCode=(Get-SafeWin32ErrorCode $_.Exception)};throw 'Credential directory owner read failed closed.'}
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$administratorsSid=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    if(-not $ownerSid.Equals($identity.User) -and -not $ownerSid.Equals($administratorsSid)){$script:CredentialAclFailureDetails=[pscustomobject]@{Stage='directory owner validation';Category='credential_acl.unapproved_owner';ExceptionType='SecurityException';HResult='0x80070005'};throw 'Credential directory owner is not an approved identity; protection failed closed.'}
    $script:CredentialAclFailureStage='directory ACL construction'
    try{$desired=New-RestrictedAcl $true $ownerSid.Value}catch{$script:CredentialAclFailureDetails=[pscustomobject]@{Stage='directory ACL construction';Category='credential_acl.construction_failed';ExceptionType=$_.Exception.GetType().FullName;HResult=('0x{0:X8}' -f $_.Exception.HResult);Win32ErrorCode=(Get-SafeWin32ErrorCode $_.Exception)};throw 'Credential ACL construction failed closed.'}
    $script:CredentialAclFailureStage='directory ACL application'
    Set-RestrictedAcl $Path $desired 'directory ACL application'
    $script:CredentialAclFailureStage='directory ACL readback'
    [void](Read-VerifiedAcl $Path $ownerSid.Value $true)
}

function Protect-CredentialFile([string]$Path) {
    $script:CredentialAclFailureStage='credential file ACL retrieval'
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    $desired=Get-Acl -LiteralPath $Path -ErrorAction Stop
    $script:CredentialAclFailureStage='credential file owner validation'
    $actualOwnerSid=$desired.GetOwner([Security.Principal.SecurityIdentifier])
    $ownerSid=Resolve-HrmsCredentialFileOwnerSid $actualOwnerSid $identity.User $identity.Owner
    $script:CredentialAclFailureStage='credential file DACL construction'
    $desired.SetAccessRuleProtection($true,$false)
    foreach($rule in @($desired.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))){$desired.PurgeAccessRules($rule.IdentityReference)}
    foreach($sidText in @($ownerSid.Value,'S-1-5-18')){
        $sid=[Security.Principal.SecurityIdentifier]::new($sidText)
        $rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]::None,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        [void]$desired.AddAccessRule($rule)
    }
    $script:CredentialAclFailureStage='credential file ACL application'
    Set-RestrictedAcl $Path $desired 'credential file ACL application'
    $script:CredentialAclFailureStage='file ACL readback'
    [void](Read-VerifiedAcl $Path $ownerSid.Value $false)
}

function Assert-SnapshotRowCounts([string[]]$Expected,[string[]]$Actual) {
    if($null -eq $Expected -or $null -eq $Actual -or $Expected.Count -eq 0 -or $Expected.Count -ne $Actual.Count){throw 'Restore verification failed: snapshot row-count manifest was missing or malformed.'}
    for($i=0;$i -lt $Expected.Count;$i++){
        $expectedCount=0L;$actualCount=0L
        if(-not [long]::TryParse($Expected[$i],[ref]$expectedCount) -or -not [long]::TryParse($Actual[$i],[ref]$actualCount) -or $expectedCount -ne $actualCount){throw 'Restore verification failed: restored table row count differs from the consistent snapshot manifest.'}
    }
}

function Assert-RestoredRowCounts([string[]]$TableNames,[string[]]$Actual) {
    if($null -eq $TableNames -or $null -eq $Actual -or $TableNames.Count -eq 0 -or $TableNames.Count -ne $Actual.Count){throw 'Restore verification failed: restored row-count inventory was missing or malformed.'}
    foreach($value in $Actual){$count=0L;if(-not [long]::TryParse($value,[ref]$count) -or $count -lt 0){throw 'Restore verification failed: restored row count was malformed.'}}
}

function ConvertFrom-HrmsMySqlConnectionString([string]$Value,[string]$ConfigKey) {
    if([string]::IsNullOrWhiteSpace($Value)){$script:LastBackupDiagnosticCategory='configuration.missing_connection_string';throw "Required database configuration is missing ($ConfigKey)."}
    try{$builder=[System.Data.Odbc.OdbcConnectionStringBuilder]::new($Value)}catch{$script:LastBackupDiagnosticCategory='configuration.connection_string_invalid';throw "Database configuration is invalid ($ConfigKey); value withheld."}
    foreach($key in @('server','port','database','user id','password')){if(-not $builder.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$builder[$key])){$script:LastBackupDiagnosticCategory='configuration.connection_string_incomplete';throw "Database configuration is incomplete ($ConfigKey); value withheld."}}
    $port=0;if(-not [int]::TryParse([string]$builder['port'],[ref]$port) -or $port -ne 3306){$script:LastBackupDiagnosticCategory='configuration.endpoint_mismatch';throw "Database configuration endpoint is invalid ($ConfigKey)."}
    if(-not [string]::Equals([string]$builder['server'],'127.0.0.1',[StringComparison]::OrdinalIgnoreCase)){$script:LastBackupDiagnosticCategory='configuration.endpoint_mismatch';throw "Database configuration endpoint is not the approved local MySQL endpoint ($ConfigKey)."}
    return [pscustomobject]@{Server=[string]$builder['server'];Port=$port;Database=[string]$builder['database'];User=[string]$builder['user id'];Password=[string]$builder['password']}
}

function Get-HrmsDevelopmentDatabaseConfiguration {
    $catalogEnv=[string]$env:ConnectionStrings__Catalog;$tenantEnv=[string]$env:Sharding__MySqlConnectionStringTemplate
    if($catalogEnv -or $tenantEnv){
        if(-not $catalogEnv -or -not $tenantEnv){$script:LastBackupDiagnosticCategory='configuration.partial_environment';throw 'Both database configuration environment variables must be configured together.'}
        $catalogText=$catalogEnv;$tenantText=$tenantEnv;$source='environment'
    } else {
        $projectPath=Join-Path $PSScriptRoot '..\Backend\HRMS.API\HRMS.API.csproj'
        try{$project=[xml](Get-Content -LiteralPath $projectPath -Raw -ErrorAction Stop);$secretId=[string]$project.Project.PropertyGroup.UserSecretsId}catch{$secretId=''}
        if([string]::IsNullOrWhiteSpace($secretId)){$script:LastBackupDiagnosticCategory='configuration.user_secrets_id_missing';throw 'HRMS API User Secrets configuration could not be resolved.'}
        $secretsPath=Join-Path $env:APPDATA (Join-Path 'Microsoft\UserSecrets' (Join-Path $secretId 'secrets.json'))
        try{$secrets=Get-Content -LiteralPath $secretsPath -Raw -ErrorAction Stop|ConvertFrom-Json}catch{$script:LastBackupDiagnosticCategory='configuration.user_secrets_unavailable';throw 'HRMS API User Secrets configuration is unavailable or invalid.'}
        $catalogProperty=$secrets.PSObject.Properties['ConnectionStrings:Catalog'];$tenantProperty=$secrets.PSObject.Properties['Sharding:MySqlConnectionStringTemplate']
        if(-not $catalogProperty -or -not $tenantProperty){$script:LastBackupDiagnosticCategory='configuration.user_secrets_incomplete';throw 'HRMS API User Secrets do not contain both required database configuration keys.'}
        $catalogText=[string]$catalogProperty.Value;$tenantText=[string]$tenantProperty.Value;$source='api-user-secrets'
    }
    $catalog=ConvertFrom-HrmsMySqlConnectionString $catalogText 'ConnectionStrings:Catalog'
    $tenantTemplate=ConvertFrom-HrmsMySqlConnectionString $tenantText 'Sharding:MySqlConnectionStringTemplate'
    $tenantDatabase=$tenantTemplate.Database.Replace('{shardKey}','web1')
    if($catalog.Database -ne 'hrms_catalog' -or $tenantDatabase -ne 'hrms_web1' -or $catalog.Server -ne $tenantTemplate.Server -or $catalog.Port -ne $tenantTemplate.Port){$script:LastBackupDiagnosticCategory='configuration.tenant_route_mismatch';throw 'Database configuration does not resolve exactly to local WEB1 catalog and hrms_web1 tenant database.'}
    return [pscustomobject]@{Catalog=$catalog;Tenant=[pscustomobject]@{Server=$tenantTemplate.Server;Port=$tenantTemplate.Port;Database=$tenantDatabase;User=$tenantTemplate.User;Password=$tenantTemplate.Password};Source=$source}
}

function New-MySqlLoginFile([string]$User,[string]$Password,[int]$Port=3306,[bool]$RequireTls=$false,[string]$TlsCaPath) {
    if([string]::IsNullOrWhiteSpace($User)){throw 'MySQL credential validation failed: username missing.'}
    $directory=$null;$path=$null;$script:CredentialAclFailureDetails=$null;$script:CredentialFileOwnerIdentity=$null
    try {
        $script:CredentialAclFailureStage='credential temporary path construction'
        $directory=Join-Path ([IO.Path]::GetTempPath()) ('hrms-mysql-' + [Guid]::NewGuid().ToString('N'))
        $script:CredentialAclFailureStage='credential temporary directory creation'
        [void](New-Item -ItemType Directory -Path $directory -ErrorAction Stop)
        Protect-Directory $directory
        $escape={param([string]$Value) return $Value.Replace('\','\\').Replace('"','\"').Replace("`r",'\r').Replace("`n",'\n')}
        $escapedUser=& $escape $User
        $lines=@('[client]',('user="{0}"' -f $escapedUser),'host=127.0.0.1',("port=$Port"),'protocol=tcp')
        if($RequireTls){
            $caPath=if(-not [string]::IsNullOrWhiteSpace($TlsCaPath)){$TlsCaPath}else{$script:ApprovedTlsCaPath}
            if([string]::IsNullOrWhiteSpace($caPath) -or -not (Test-Path -LiteralPath $caPath -PathType Leaf)){
                throw 'VERIFY_IDENTITY requires an existing approved CA certificate file.'
            }
            $escapedCa=& $escape ([IO.Path]::GetFullPath($caPath).Replace('\','/'))
            $lines+=('ssl-ca="{0}"' -f $escapedCa)
            $lines+='ssl-mode=VERIFY_IDENTITY'
        }
        if($null -ne $Password -and $Password.Length -gt 0){$escapedPassword=& $escape $Password;$lines+=('password="{0}"' -f $escapedPassword)}
        $script:CredentialAclFailureStage='credential option-file path construction'
        $path=Join-Path $directory 'client.cnf'
        $script:CredentialAclFailureStage='credential option-file initialization'
        [IO.File]::WriteAllText($path,'',[Text.UTF8Encoding]::new($false))
        Protect-CredentialFile $path
        $script:CredentialAclFailureStage='credential option-file content write'
        [IO.File]::WriteAllLines($path,[string[]]$lines,[Text.UTF8Encoding]::new($false))
        Protect-CredentialFile $path
        return [pscustomobject]@{Directory=$directory;Path=$path}
    } catch {
        $failureRecord=$_;$failureStage=$script:CredentialAclFailureStage
        if(-not $script:CredentialAclFailureDetails -or -not $script:CredentialAclFailureDetails.Category){Set-CredentialFailureDiagnostic $failureRecord $failureStage}
        $cleanupStatus='NOT_REQUIRED'
        if($directory -and (Test-Path -LiteralPath $directory)){
            try{Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction Stop;if(Test-Path -LiteralPath $directory){throw 'cleanup not confirmed'};$cleanupStatus='SUCCEEDED'}catch{$cleanupStatus='BLOCKED'}
        }
        $script:CredentialAclFailureDetails | Add-Member -NotePropertyName CleanupStatus -NotePropertyValue $cleanupStatus -Force
        throw "Unable to create protected temporary MySQL credential file ($failureStage; $($failureRecord.Exception.GetType().FullName))."
    }
}

function Remove-MySqlLoginFile($LoginFile) {
    if($LoginFile -and $LoginFile.Directory -and (Test-Path -LiteralPath $LoginFile.Directory)){
        $full=[IO.Path]::GetFullPath($LoginFile.Directory);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if($full.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -match '^hrms-mysql-[0-9a-f]{32}$'){
            Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
        } else {throw 'Temporary credential cleanup refused: directory ownership path was not recognized.'}
    }
}

function Assert-CatalogRoute([string]$Output) {
    $rows=@($Output -split "`r?`n" | Where-Object { $_.Length -gt 0 })
    if($rows.Count -ne 1){throw 'Catalog route validation failed: expected exactly one host match.'}
    $fields=$rows[0].Split([char]9)
    if($fields.Count -ne 5 -or $fields[0] -ne 'WEB1' -or $fields[1] -ne 'web1.localhost' -or $fields[2] -ne 'web1' -or $fields[3] -ne 'MySql' -or $fields[4] -ne '1'){
        throw 'Catalog route validation failed: route did not resolve exactly to active WEB1/web1/MySql.'
    }
    return $true
}

function Assert-InnoDbOnly([string]$Inventory) {
    $rows=@($Inventory -split "`r?`n" | Where-Object { $_.Length -gt 0 })
    $base=$rows | Where-Object { ($_ -split "`t")[-1] -ne 'VIEW' }
    if($base.Count -eq 0){throw 'Source validation failed: no base tables were found.'}
    $nonInnoDb=@($base | Where-Object { ($_ -split "`t")[-1] -ne 'InnoDB' })
    if($nonInnoDb.Count -gt 0){throw 'Source validation failed: non-InnoDB base table detected; online backup is blocked.'}
    return $base.Count
}

function Assert-BackupCapacity([long]$DatabaseBytes,[long]$BackupFreeBytes,[long]$RestoreFreeBytes) {
    if($DatabaseBytes -lt 1){throw 'Capacity validation failed: database size was not positive.'}
    $required=[long][Math]::Max(1073741824.0,[double]$DatabaseBytes*4.0)
    if($BackupFreeBytes -lt $required -or $RestoreFreeBytes -lt $required){throw 'Capacity validation failed: backup or isolated-restore volume lacks the required reserve.'}
    return $required
}

function Get-ApiProcessSnapshot {
    $processes=Get-CimInstance Win32_Process -ErrorAction Stop
    return @($processes | Where-Object { $_.Name -in @('HRMS.API.exe','dotnet.exe') -and $_.CommandLine -match 'HRMS\.API(\.dll)?' } | ForEach-Object { '{0}|{1}|{2}' -f $_.ProcessId,$_.CreationDate.ToUniversalTime().Ticks,$_.ExecutablePath } | Sort-Object)
}

function Get-SecureText([Security.SecureString]$Secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function New-CryptoRandomBytes([int]$Count) {
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try {$bytes=New-Object byte[] $Count;$rng.GetBytes($bytes);return ,$bytes}
    finally {$rng.Dispose()}
}

function Get-SafeMySqlErrorSummary([string]$StandardError) {
    $number='unavailable'
    if($StandardError -match '(?i)\b(?:ERROR|Error|Got error:)\s*#?\s*(\d{1,6})\b'){$number=$Matches[1]}
    $category='unclassified mysqldump failure'
    if($StandardError -match '(?i)access denied for user|authentication (?:failed|rejected)'){$category='authentication rejected'}
    elseif($StandardError -match '(?i)ssl|tls|certificate|secure connection'){$category='TLS negotiation failed'}
    elseif($StandardError -match '(?i)command denied|privilege|not allowed to|access denied to|requires? .*privilege'){$category='insufficient database privileges'}
    elseif($StandardError -match '(?i)unknown option|unknown variable|option .* not supported'){$category='unsupported client option'}
    elseif($StandardError -match '(?i)can.t connect|connection refused|timed out|lost connection'){$category='connection failure'}
    [pscustomobject]@{ErrorNumber=$number;Category=$category}
}

function Write-EncryptedDump {
    param([string]$DumpExe, [string]$LoginFile, [string]$Destination, [string]$Passphrase)
    $salt = New-CryptoRandomBytes 16
    $iv = New-CryptoRandomBytes 16
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new($Passphrase,$salt,600000,[Security.Cryptography.HashAlgorithmName]::SHA256)
    $encKey = $derive.GetBytes(32); $macKey = $derive.GetBytes(32); $derive.Dispose()
    $header = [Text.Encoding]::ASCII.GetBytes("HRMSWEB1-AES256-HMAC-SHA256-v1`n") + $salt + $iv
    $tmp = "$Destination.partial"
    $psi=[Diagnostics.ProcessStartInfo]::new(); $psi.FileName=$DumpExe; $psi.UseShellExecute=$false
    $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true; $psi.CreateNoWindow=$true
    Clear-MySqlPasswordEnvironment $psi
    $safeArgumentNames=New-Object 'System.Collections.Generic.List[string]'
    Add-ProcessArgument $psi "--defaults-file=$LoginFile";$safeArgumentNames.Add('--defaults-file')
    foreach($a in @('--databases','hrms_web1','--routines','--events','--triggers','--hex-blob','--quick','--skip-comments','--single-transaction','--skip-lock-tables','--no-tablespaces')){
        Add-ProcessArgument $psi $a
        if($a.StartsWith('--')){$safeArgumentNames.Add(($a -split '=',2)[0])}else{$safeArgumentNames.Add('database-name')}
    }
    $p=[Diagnostics.Process]::new();$p.StartInfo=$psi;[void]$p.Start()
    $errTask=$p.StandardError.ReadToEndAsync()
    $aes=[Security.Cryptography.Aes]::Create();$aes.KeySize=256;$aes.Mode='CBC';$aes.Padding='PKCS7';$aes.Key=$encKey;$aes.IV=$iv
    $fs=[IO.File]::Open($tmp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {
        $fs.Write($header,0,$header.Length)
        $crypto=[Security.Cryptography.CryptoStream]::new($fs,$aes.CreateEncryptor(),[Security.Cryptography.CryptoStreamMode]::Write,$true)
        $read=$p.StandardOutput.BaseStream; $buffer=New-Object byte[] 65536
        while(($n=$read.Read($buffer,0,$buffer.Length)) -gt 0){$crypto.Write($buffer,0,$n)}
        $crypto.FlushFinalBlock();$crypto.Dispose()
        $p.WaitForExit();$err=$errTask.GetAwaiter().GetResult();$dumpExitCode=$p.ExitCode
        if($dumpExitCode -eq 0){$fs.Flush($true)}
    } finally { $fs.Dispose();$aes.Dispose() }
    if($dumpExitCode -ne 0){
        [void](Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue)
        $summary=Get-SafeMySqlErrorSummary $err
        $script:LastMySqlDiagnostic="mysqldump exit code $dumpExitCode; MySQL error number $($summary.ErrorNumber); category: $($summary.Category); argument names=$($safeArgumentNames -join ','); stderr withheld."
        throw $script:LastMySqlDiagnostic
    }
    $mac=[Security.Cryptography.HMACSHA256]::new($macKey);$check=[IO.File]::OpenRead($tmp);$buf=New-Object byte[] 65536
    try { while(($n=$check.Read($buf,0,$buf.Length)) -gt 0){$mac.TransformBlock($buf,0,$n,$buf,0)|Out-Null};$mac.TransformFinalBlock([byte[]]::new(0),0,0);$tag=$mac.Hash }
    finally {$check.Dispose();$mac.Dispose();[Array]::Clear($encKey,0,$encKey.Length);[Array]::Clear($macKey,0,$macKey.Length)}
    $append=[IO.File]::Open($tmp,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$append.Write($tag,0,$tag.Length);$append.Flush($true)}finally{$append.Dispose()}
    # File.Move is atomic on the same volume and fails if the destination exists.
    [IO.File]::Move($tmp,$Destination)
}

function Test-EncryptedDump {
    param([string]$Path,[string]$Passphrase)
    $derive=$null;$encKey=$null;$macKey=$null;$transferred=$false
    $f=$null
    try {
        try {$f=[IO.File]::OpenRead($Path)} catch [IO.IOException] { $e=$_.Exception; $e.Data['HrmsHmacFailureCategory']='encrypted_artifact.io_error'; throw }
        $fixed=[Text.Encoding]::ASCII.GetBytes("HRMSWEB1-AES256-HMAC-SHA256-v1`n")
        $head=New-Object byte[] ($fixed.Length+32); if($f.Read($head,0,$head.Length) -ne $head.Length){$e=[IO.InvalidDataException]::new('Encrypted backup header is truncated.');$e.Data['HrmsHmacFailureCategory']='encrypted_artifact.format_error';throw $e}
        for($i=0;$i -lt $fixed.Length;$i++){if($head[$i] -ne $fixed[$i]){$e=[IO.InvalidDataException]::new('Encrypted backup format is invalid.');$e.Data['HrmsHmacFailureCategory']='encrypted_artifact.format_error';throw $e}}
        $salt=$head[$fixed.Length..($fixed.Length+15)];$iv=$head[($fixed.Length+16)..($fixed.Length+31)]
        if(($f.Length-$head.Length) -lt 48){$e=[IO.InvalidDataException]::new('Encrypted backup is too short.');$e.Data['HrmsHmacFailureCategory']='encrypted_artifact.format_error';throw $e}
        $derive=[Security.Cryptography.Rfc2898DeriveBytes]::new($Passphrase,$salt,600000,[Security.Cryptography.HashAlgorithmName]::SHA256)
        $encKey=$derive.GetBytes(32);$macKey=$derive.GetBytes(32);$derive.Dispose()
        $cipherLength=$f.Length-$head.Length-32;$h=[Security.Cryptography.HMACSHA256]::new($macKey);$h.TransformBlock($head,0,$head.Length,$head,0)|Out-Null
        $buf=New-Object byte[] 65536;$remaining=$cipherLength
        while($remaining -gt 0){$n=$f.Read($buf,0,[Math]::Min($buf.Length,$remaining));if($n -le 0){$e=[IO.InvalidDataException]::new('Encrypted backup is truncated.');$e.Data['HrmsHmacFailureCategory']='encrypted_artifact.format_error';throw $e};$h.TransformBlock($buf,0,$n,$buf,0)|Out-Null;$remaining-=$n}
        $saved=New-Object byte[] 32;if($f.Read($saved,0,32)-ne 32){$e=[IO.InvalidDataException]::new('Encrypted backup authentication tag is missing.');$e.Data['HrmsHmacFailureCategory']='encrypted_artifact.format_error';throw $e}
        $h.TransformFinalBlock([byte[]]::new(0),0,0);$actual=$h.Hash
        $diff=0;for($i=0;$i -lt 32;$i++){$diff=$diff -bor ($saved[$i] -bxor $actual[$i])}
        if($diff -ne 0){$e=[Security.Cryptography.CryptographicException]::new('Encrypted backup authentication failed.');$e.Data['HrmsHmacFailureCategory']='encrypted_artifact.authentication_failed';throw $e}
        $result=[pscustomobject]@{Header=$head;Salt=$salt;IV=$iv;CipherOffset=$head.Length;CipherLength=$cipherLength;EncKey=$encKey;MacKey=$macKey}
        $transferred=$true
        return $result
    } catch {
        if(-not $_.Exception.Data.Contains('HrmsHmacFailureCategory')){$category=if($_.Exception -is [IO.IOException]){'encrypted_artifact.io_error'}elseif($_.Exception -is [Security.Cryptography.CryptographicException]){'encrypted_artifact.authentication_failed'}else{'encrypted_artifact.implementation_error'};try{$_.Exception.Data['HrmsHmacFailureCategory']=$category}catch{}}
        throw
    } finally {
        if($f){$f.Dispose()}
        if($derive){$derive.Dispose()}
        if(-not $transferred){if($encKey){[Array]::Clear($encKey,0,$encKey.Length)};if($macKey){[Array]::Clear($macKey,0,$macKey.Length)}}
        # Clear releases mutable key buffers. PowerShell/.NET strings, pipeline copies,
        # and runtime-managed transient buffers cannot be reliably zeroed; use the
        # smallest possible lifetime and never emit those values to diagnostics.
    }
}

function ConvertFrom-HrmsNetstatTcpRows([string[]]$Lines,[int]$Port) {
    $rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($line in $Lines){
        if($line -match '^\s*TCP\s+'){
            if($line -notmatch '^\s*TCP\s+(\S+)\s+(\S+)\s+(\S+)\s+(\d+)\s*$'){throw 'Restore-only blocked: Windows TCP listener inventory contained an unparseable row.'}
            $local=$Matches[1];$state=[string]$Matches[3];$owner=0
            $colon=$local.LastIndexOf(':');if($colon -lt 0){throw 'Restore-only blocked: Windows TCP inventory contained an invalid local endpoint.'}
            $portValue=0;if(-not [int]::TryParse($local.Substring($colon+1),[ref]$portValue)){throw 'Restore-only blocked: Windows TCP inventory contained an invalid local port.'}
            if(-not [int]::TryParse($Matches[4],[ref]$owner) -or $owner -lt 0){throw 'Restore-only blocked: Windows TCP inventory contained an unknown socket owner.'}
            if($portValue -eq $Port){if($owner -eq 0){throw 'Restore-only blocked: isolated-port socket ownership is unknown.'};$rows.Add([pscustomobject]@{LocalPort=$portValue;State=$state;OwnerPid=$owner})}
        }
    }
    return @($rows.ToArray())
}

function Get-HrmsRestoreOnlySocketInventory([int]$Port) {
    $cimRows=@();$cimAvailable=$false;$cimFailure=$null
    try {
        $cimRows=@(Get-NetTCPConnection -LocalPort $Port -ErrorAction Stop | ForEach-Object {
            [pscustomobject]@{LocalPort=[int]$_.LocalPort;State=[string]$_.State;OwnerPid=[int]$_.OwningProcess}
        })
        $cimAvailable=$true
    } catch {$cimFailure=$_.Exception.GetType().FullName}

    $netstatPath=Join-Path $env:SystemRoot 'System32\netstat.exe'
    try {$lines=@(& $netstatPath -ano -p tcp 2>$null);$exit=$LASTEXITCODE} catch {throw 'Restore-only blocked: Windows TCP listener inventory failed.'}
    if($exit -ne 0){throw 'Restore-only blocked: Windows TCP listener inventory returned an error.'}
    $netRows=ConvertFrom-HrmsNetstatTcpRows $lines $Port
    return [pscustomobject]@{CimAvailable=$cimAvailable;CimFailure=$cimFailure;CimRows=@($cimRows);NetstatAvailable=$true;NetstatRows=@($netRows)}
}

function ConvertFrom-HrmsWindowsCommandLine([string]$CommandLine) {
    # Parse the CRT/CommandLineToArgvW quoting rules: runs of backslashes before
    # quotes are decoded by parity, and whitespace separates only outside quotes.
    if($null -eq $CommandLine){throw 'Malformed command line.'}
    $args=New-Object 'System.Collections.Generic.List[string]';$i=0
    while($i -lt $CommandLine.Length){
        while($i -lt $CommandLine.Length -and [char]::IsWhiteSpace($CommandLine[$i])){$i++}
        if($i -ge $CommandLine.Length){break}
        $builder=New-Object Text.StringBuilder;$quoted=$false
        while($i -lt $CommandLine.Length){
            if([char]::IsWhiteSpace($CommandLine[$i]) -and -not $quoted){break}
            $slashes=0
            while($i -lt $CommandLine.Length -and $CommandLine[$i] -eq [char]92){$slashes++;$i++}
            if($i -lt $CommandLine.Length -and $CommandLine[$i] -eq '"'){
                for($n=0;$n -lt [Math]::Floor($slashes/2);$n++){[void]$builder.Append([char]92)}
                if(($slashes % 2) -eq 1){[void]$builder.Append('"');$i++}
                else{$quoted=-not $quoted;$i++}
                continue
            }
            for($n=0;$n -lt $slashes;$n++){[void]$builder.Append([char]92)}
            if($i -lt $CommandLine.Length -and -not ([char]::IsWhiteSpace($CommandLine[$i]) -and -not $quoted)){[void]$builder.Append($CommandLine[$i]);$i++}
        }
        if($quoted){throw 'Unclosed quote.'}
        $args.Add($builder.ToString())
    }
    return $args.ToArray()
}

function Get-HrmsMySqlDefaultsFile([string]$CommandLine) {
    try{$tokens=@(ConvertFrom-HrmsWindowsCommandLine $CommandLine)}catch{$script:LastMySql80IdentityDiagnostic='mysql80.defaults_file.argument_malformed';throw 'Restore-only blocked: MySQL defaults-file argument was malformed.'}
    $found=New-Object 'System.Collections.Generic.List[string]'
    $firstOption=if($tokens.Count -gt 0 -and ([string]$tokens[0] -match '(?i)^--defaults-file(?:=|$)')){0}else{1}
    for($i=$firstOption;$i -lt $tokens.Count;$i++){
        $token=[string]$tokens[$i]
        if($token -match '(?i)^--defaults-file(?:=(.*))?$'){
            if($Matches[1] -ne $null){$found.Add([string]$Matches[1]);continue}
            if(($i+1) -ge $tokens.Count){$found.Add('');continue}
            $i++;$found.Add([string]$tokens[$i])
        }
    }
    if($found.Count -eq 0){$script:LastMySql80IdentityDiagnostic='mysql80.defaults_file.argument_missing';throw 'Restore-only blocked: MySQL defaults-file identity was unavailable.'}
    if($found.Count -ne 1){$script:LastMySql80IdentityDiagnostic='mysql80.defaults_file.argument_ambiguous';throw 'Restore-only blocked: MySQL defaults-file identity was ambiguous.'}
    $value=$found[0]
    if([string]::IsNullOrWhiteSpace($value) -or $value.IndexOfAny([char[]]@('"',"`r","`n")) -ge 0 -or -not [IO.Path]::IsPathRooted($value)){$script:LastMySql80IdentityDiagnostic='mysql80.defaults_file.argument_malformed';throw 'Restore-only blocked: MySQL defaults-file argument was malformed.'}
    try{return [IO.Path]::GetFullPath($value)}catch{$script:LastMySql80IdentityDiagnostic='mysql80.defaults_file.path_invalid';throw 'Restore-only blocked: MySQL defaults-file identity was invalid.'}
}

function ConvertTo-HrmsProcessStartIdentity([object]$Value) {
    $identity=0L
    try {
        if($Value -is [datetime]){$identity=$Value.ToUniversalTime().Ticks}
        elseif($null -eq $Value){throw 'missing'}
        else {
            $text=[string]$Value
            if([long]::TryParse($text,[ref]$identity)){}
            elseif($text -match '^\d{14}\.\d{6}[+-]\d{3}$'){$identity=[Management.ManagementDateTimeConverter]::ToDateTime($text).ToUniversalTime().Ticks}
            else {
                $date=[datetime]::MinValue
                if(-not [datetime]::TryParse($text,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$date)){throw 'invalid'}
                $identity=$date.ToUniversalTime().Ticks
            }
        }
    } catch {$script:LastMySql80IdentityDiagnostic='mysql80.process.start_identity_unreadable';throw 'Restore-only blocked: MySQL process-start identity was unavailable or malformed.'}
    if($identity -le 0){$script:LastMySql80IdentityDiagnostic='mysql80.process.start_identity_nonpositive';throw 'Restore-only blocked: MySQL process-start identity was invalid.'}
    return [long]$identity
}

function Resolve-HrmsMySql80ProcessFamily([int]$ServicePid,[string]$ExpectedExe,[string]$ExpectedConfig,[object[]]$Processes) {
    $script:LastMySql80IdentityDiagnostic=$null
    if($ServicePid -le 0 -or [string]::IsNullOrWhiteSpace($ExpectedExe) -or [string]::IsNullOrWhiteSpace($ExpectedConfig)){$script:LastMySql80IdentityDiagnostic='mysql80.service.identity_fields_missing';throw 'Restore-only blocked: MySQL80 service identity fields are incomplete.'}
    try{$exePath=[IO.Path]::GetFullPath($ExpectedExe)}catch{$script:LastMySql80IdentityDiagnostic='mysql80.service.executable_path_invalid';throw 'Restore-only blocked: MySQL80 configured executable path was invalid.'}
    try{$configPath=[IO.Path]::GetFullPath($ExpectedConfig)}catch{$script:LastMySql80IdentityDiagnostic='mysql80.service.defaults_file_invalid';throw 'Restore-only blocked: MySQL80 configured defaults-file path was invalid.'}
    $records=@($Processes);if($records.Count -eq 0){$script:LastMySql80IdentityDiagnostic='mysql80.process.inventory_empty';throw 'Restore-only blocked: MySQL server process inventory is empty.'}
    $root=@($records|Where-Object {[int]$_.ProcessId -eq $ServicePid})
    if($root.Count -ne 1){$script:LastMySql80IdentityDiagnostic='mysql80.process.service_pid_not_unique';throw 'Restore-only blocked: service PID does not resolve to exactly one MySQL process.'}
    $family=New-Object 'System.Collections.Generic.List[object]'
    $pending=New-Object 'System.Collections.Generic.List[object]';foreach($record in $records){if([int]$record.ProcessId -ne $ServicePid){$pending.Add($record)}}
    $rootRecord=$root[0]
    if([string]$rootRecord.Name -ne 'mysqld.exe'){$script:LastMySql80IdentityDiagnostic='mysql80.process.service_pid_image_name_mismatch';throw 'Restore-only blocked: MySQL80 service PID was not a mysqld.exe process.'}
    if(-not $rootRecord.PSObject.Properties['ProcessStartIdentity']){$script:LastMySql80IdentityDiagnostic='mysql80.process.service_start_identity_missing';throw 'Restore-only blocked: MySQL80 service process-start identity was missing.'}
    $rootStart=ConvertTo-HrmsProcessStartIdentity $rootRecord.ProcessStartIdentity
    try{$rootExe=[IO.Path]::GetFullPath([string]$rootRecord.ExecutablePath)}catch{$script:LastMySql80IdentityDiagnostic='mysql80.process.service_executable_path_invalid';throw 'Restore-only blocked: MySQL80 service executable path was invalid.'}
    if(-not [string]::Equals($rootExe,$exePath,[StringComparison]::OrdinalIgnoreCase)){$script:LastMySql80IdentityDiagnostic='mysql80.process.service_executable_mismatch';throw 'Restore-only blocked: MySQL80 service process executable did not match service configuration.'}
    $rootConfig=Get-HrmsMySqlDefaultsFile ([string]$rootRecord.CommandLine)
    if(-not [string]::Equals($rootConfig,$configPath,[StringComparison]::OrdinalIgnoreCase)){$script:LastMySql80IdentityDiagnostic='mysql80.process.service_defaults_file_mismatch';throw 'Restore-only blocked: MySQL80 service process defaults-file did not match service configuration.'}
    $rootRecord.ProcessStartIdentity=$rootStart
    $family.Add($rootRecord)
    while($pending.Count -gt 0){
        $progress=$false
        for($i=$pending.Count-1;$i -ge 0;$i--){
            $record=$pending[$i];$parent=@($family|Where-Object {[int]$_.ProcessId -eq [int]$record.ParentProcessId})
            if($parent.Count -eq 0){continue}
            if([string]$record.Name -ne 'mysqld.exe'){$script:LastMySql80IdentityDiagnostic='mysql80.process.child_image_name_mismatch';throw 'Restore-only blocked: MySQL child process image name did not match mysqld.exe.'}
            if(-not $record.PSObject.Properties['ProcessStartIdentity']){$script:LastMySql80IdentityDiagnostic='mysql80.process.child_start_identity_missing';throw 'Restore-only blocked: MySQL child process-start identity was missing.'}
            $childStart=ConvertTo-HrmsProcessStartIdentity $record.ProcessStartIdentity
            try{$childExe=[IO.Path]::GetFullPath([string]$record.ExecutablePath)}catch{$script:LastMySql80IdentityDiagnostic='mysql80.process.child_executable_path_invalid';throw 'Restore-only blocked: MySQL child executable path was invalid.'}
            if(-not [string]::Equals($childExe,$exePath,[StringComparison]::OrdinalIgnoreCase)){$script:LastMySql80IdentityDiagnostic='mysql80.process.child_executable_mismatch';throw 'Restore-only blocked: MySQL child executable did not match MySQL80.'}
            $childConfig=Get-HrmsMySqlDefaultsFile ([string]$record.CommandLine)
            if(-not [string]::Equals($childConfig,$configPath,[StringComparison]::OrdinalIgnoreCase)){$script:LastMySql80IdentityDiagnostic='mysql80.process.child_defaults_file_mismatch';throw 'Restore-only blocked: MySQL child defaults-file did not match MySQL80.'}
            $record.ProcessStartIdentity=$childStart
            $family.Add($record);$pending.RemoveAt($i);$progress=$true
        }
        if(-not $progress){$script:LastMySql80IdentityDiagnostic='mysql80.process.family_parent_relationship_unverified';throw 'Restore-only blocked: an unparented or unrelated mysqld process was found.'}
    }
    return @($family.ToArray()|Sort-Object ProcessId)
}

function Get-HrmsMySql80ServiceSnapshot {
    $script:LastMySql80IdentityDiagnostic=$null;$stage='mysql80.service.query'
    try {
        $service=Get-CimInstance Win32_Service -Filter "Name='MySQL80'" -ErrorAction Stop
        if($null -eq $service){$script:LastMySql80IdentityDiagnostic='mysql80.service.not_found';throw 'MySQL80 service query returned no service.'}
        if($service.State -ne 'Running'){$script:LastMySql80IdentityDiagnostic='mysql80.service.not_running';throw 'MySQL80 service is not running.'}
        if([int]$service.ProcessId -le 0){$script:LastMySql80IdentityDiagnostic='mysql80.service.pid_invalid';throw 'MySQL80 service has no valid process ID.'}
        $stage='mysql80.service.executable_configuration'
        $pathMatch=[regex]::Match([string]$service.PathName,'^\s*(?:"([^"]+)"|(\S+))')
        if(-not $pathMatch.Success){$script:LastMySql80IdentityDiagnostic='mysql80.service.executable_path_missing';throw 'MySQL80 configured executable was unavailable.'}
        $exe=if($pathMatch.Groups[1].Success){$pathMatch.Groups[1].Value}else{$pathMatch.Groups[2].Value}
        $config=Get-HrmsMySqlDefaultsFile ([string]$service.PathName)
        $stage='mysql80.process.inventory'
        $processes=@(Get-CimInstance Win32_Process -Filter "Name='mysqld.exe'" -ErrorAction Stop | ForEach-Object {
            $created=$_.CreationDate;$start=ConvertTo-HrmsProcessStartIdentity $created
            [pscustomobject]@{ProcessId=[int]$_.ProcessId;ParentProcessId=[int]$_.ParentProcessId;Name=[string]$_.Name;ExecutablePath=[string]$_.ExecutablePath;CommandLine=[string]$_.CommandLine;ProcessStartIdentity=[long]$start}
        })
        $stage='mysql80.process.family_validation'
        $family=Resolve-HrmsMySql80ProcessFamily ([int]$service.ProcessId) $exe $config $processes
        return [pscustomobject]@{ServiceName='MySQL80';Status=[string]$service.State;StartType=[string]$service.StartMode;ProcessId=[int]$service.ProcessId;ExecutablePath=[IO.Path]::GetFullPath($exe);ConfigPath=[IO.Path]::GetFullPath($config);ProcessFamily=@($family)}
    } catch {
        if([string]::IsNullOrWhiteSpace($script:LastMySql80IdentityDiagnostic)){$script:LastMySql80IdentityDiagnostic=$stage+'.query_or_conversion_failed'}
        throw "Restore-only blocked: MySQL80 service and verified process-family identity could not be established ($($script:LastMySql80IdentityDiagnostic))."
    }
}

function Assert-HrmsMySql80SnapshotUnchanged($Expected,$Actual) {
    if($null -eq $Expected -or $null -eq $Actual){throw 'Restore-only blocked: MySQL80 baseline or current identity is missing.'}
    if($Expected.ServiceName -cne $Actual.ServiceName -or $Expected.Status -cne $Actual.Status -or $Expected.StartType -cne $Actual.StartType -or $Expected.ProcessId -ne $Actual.ProcessId -or -not [string]::Equals($Expected.ExecutablePath,$Actual.ExecutablePath,[StringComparison]::OrdinalIgnoreCase) -or -not [string]::Equals($Expected.ConfigPath,$Actual.ConfigPath,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Restore-only blocked: MySQL80 service identity changed.'
    }
    $expectedFamily=@($Expected.ProcessFamily|ForEach-Object {"$($_.ProcessId)|$($_.ParentProcessId)|$($_.ProcessStartIdentity)|$($_.ExecutablePath)|$($_.CommandLine)"}|Sort-Object)
    $actualFamily=@($Actual.ProcessFamily|ForEach-Object {"$($_.ProcessId)|$($_.ParentProcessId)|$($_.ProcessStartIdentity)|$($_.ExecutablePath)|$($_.CommandLine)"}|Sort-Object)
    if(($expectedFamily -join "`n") -cne ($actualFamily -join "`n")){throw 'Restore-only blocked: MySQL80 process-family identity changed.'}
    return $true
}

function Assert-HrmsMySql80ServiceUnchanged($Expected) {
    $actual=Get-HrmsMySql80ServiceSnapshot
    Assert-HrmsMySql80SnapshotUnchanged $Expected $actual
}

function Assert-RestoreOnlyPortAvailable([int]$Port,[object]$ExpectedMySql80Identity,[scriptblock]$SocketInventory,[scriptblock]$BindProbe,[scriptblock]$MySqlProcessInventory) {
    if($Port -lt 1 -or $Port -gt 65535 -or $Port -eq 3306){throw 'Restore-only blocked: isolated port is invalid.'}
    try {
        if(-not $SocketInventory){$SocketInventory={param($p) Get-HrmsRestoreOnlySocketInventory $p}.GetNewClosure()}
        if(-not $BindProbe){$BindProbe={param($p)
            $tcp=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$p);$tcp.Server.ExclusiveAddressUse=$true
            try{$tcp.Start();$true}catch{$false}finally{$tcp.Stop()}
        }.GetNewClosure()}
        if(-not $MySqlProcessInventory){$MySqlProcessInventory={Get-HrmsMySql80ServiceSnapshot}.GetNewClosure()}
        if($null -eq $ExpectedMySql80Identity -or [int]$ExpectedMySql80Identity.ProcessId -le 0){throw 'Restore-only blocked: verified MySQL80 identity is required for isolated-process exclusion.'}
        $snapshot=& $SocketInventory $Port
        if($null -eq $snapshot -or -not $snapshot.NetstatAvailable){throw 'Restore-only blocked: Windows TCP listener inventory is unavailable.'}
        if($snapshot.CimAvailable){
            $cimSet=@($snapshot.CimRows | ForEach-Object {"$($_.LocalPort)|$($_.OwnerPid)|$($_.State)"} | Sort-Object)
            $netSet=@($snapshot.NetstatRows | ForEach-Object {"$($_.LocalPort)|$($_.OwnerPid)|$($_.State)"} | Sort-Object)
            if(($cimSet -join "`n") -cne ($netSet -join "`n")){throw 'Restore-only blocked: CIM and Windows TCP inventories disagree.'}
        }
        if(@($snapshot.CimRows).Count -gt 0 -or @($snapshot.NetstatRows).Count -gt 0){throw 'Restore-only blocked: isolated port has an existing TCP socket or listener.'}
        $currentMySqlIdentity=& $MySqlProcessInventory
        [void](Assert-HrmsMySql80SnapshotUnchanged $ExpectedMySql80Identity $currentMySqlIdentity)
        $bound=& $BindProbe $Port
        if(-not $bound){throw 'Restore-only blocked: isolated port bind probe failed or is inconsistent with socket inventory.'}
        return $true
    } catch {
        if($_.Exception.Message -like 'Restore-only blocked:*'){throw}
        throw 'Restore-only blocked: isolated port availability is indeterminate.'
    }
}

function Import-EncryptedDump {
    param([string]$Path,[pscustomobject]$Info,[string]$MysqlExe,[string]$LoginFile,[int]$Port)
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$MysqlExe;$psi.UseShellExecute=$false
    $psi.RedirectStandardInput=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true;$psi.CreateNoWindow=$true
    Clear-MySqlPasswordEnvironment $psi
    Add-ProcessArgument $psi "--defaults-file=$LoginFile";Add-ProcessArgument $psi "--port=$Port";Add-ProcessArgument $psi '--binary-mode'
    $p=[Diagnostics.Process]::new();$p.StartInfo=$psi;[void]$p.Start();$ot=$p.StandardOutput.ReadToEndAsync();$et=$p.StandardError.ReadToEndAsync()
    $aes=[Security.Cryptography.Aes]::Create();$aes.KeySize=256;$aes.Mode='CBC';$aes.Padding='PKCS7';$aes.Key=$Info.EncKey;$aes.IV=$Info.IV
    $dec=[Security.Cryptography.CryptoStream]::new($p.StandardInput.BaseStream,$aes.CreateDecryptor(),[Security.Cryptography.CryptoStreamMode]::Write,$true)
    $f=[IO.File]::OpenRead($Path);$f.Position=$Info.CipherOffset;$remaining=$Info.CipherLength;$buf=New-Object byte[] 65536
    try {while($remaining -gt 0){$n=$f.Read($buf,0,[Math]::Min($buf.Length,$remaining));if($n -le 0){throw 'Encrypted backup truncated during restore.'};$dec.Write($buf,0,$n);$remaining-=$n};$dec.FlushFinalBlock();$dec.Dispose();$p.StandardInput.Close();$p.WaitForExit();$null=$ot.GetAwaiter().GetResult();$null=$et.GetAwaiter().GetResult();if($p.ExitCode -ne 0){throw 'Restore import failed. Diagnostic text is withheld to avoid recording data.'}}finally{$f.Dispose();$aes.Dispose();[Array]::Clear($Info.EncKey,0,$Info.EncKey.Length);[Array]::Clear($Info.MacKey,0,$Info.MacKey.Length)}
}

function Get-FreeBytesForPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path);$root=[IO.Path]::GetPathRoot($full)
    if(-not $root){throw 'Capacity validation failed: volume could not be resolved.'}
    $drive=[IO.DriveInfo]::new($root)
    if(-not $drive.IsReady){throw 'Capacity validation failed: target volume is not ready.'}
    return [long]$drive.AvailableFreeSpace
}

function Resolve-IsolatedListenerOwnership([object[]]$CimConnections,[string[]]$NetstatLines,[int]$ExpectedPID,[int]$Port,[bool]$CimSucceeded,[bool]$NetstatSucceeded) {
    if($Port -eq 3306 -or $Port -lt 1 -or $Port -gt 65535){return [pscustomobject]@{Valid=$false;Category='restore.listener_invalid_port';OwnerPID=$null;Source='none'}}
    if(-not $CimSucceeded -and -not $NetstatSucceeded){return [pscustomobject]@{Valid=$false;Category='restore.listener_query_failed';OwnerPID=$null;Source='none'}}

    $cimListenOwners=@($CimConnections | Where-Object { [string]$_.State -eq 'Listen' } | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
    $cimUnknownOwners=@($CimConnections | Where-Object { [string]$_.State -notin @('Listen','SynSent','SynReceived','Established','FinWait1','FinWait2','CloseWait','Closing','LastAck','TimeWait','Closed') } | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
    $netstatOwners=New-Object 'System.Collections.Generic.List[int]'
    foreach($line in $NetstatLines){
        if([string]::IsNullOrWhiteSpace($line)){continue}
        $fields=$line.Trim() -split '\s+'
        if($fields.Count -lt 5 -or $fields[0] -ne 'TCP' -or $fields[3] -ne 'LISTENING'){continue}
        $localPortText=$fields[1].Substring($fields[1].LastIndexOf(':')+1).TrimEnd(']');$localPort=0;$owner=0
        if([int]::TryParse($localPortText,[ref]$localPort) -and $localPort -eq $Port -and [int]::TryParse($fields[4],[ref]$owner)){$netstatOwners.Add($owner)}
    }
    $cimListenOwners=@($cimListenOwners|Sort-Object -Unique);$netstatOwners=@($netstatOwners.ToArray()|Sort-Object -Unique)
    if($cimListenOwners.Count -gt 1 -or $netstatOwners.Count -gt 1){return [pscustomobject]@{Valid=$false;Category='restore.listener_multiple_owners';OwnerPID=$null;Source='multiple'}}

    if($CimSucceeded -and $NetstatSucceeded){
        if($cimListenOwners.Count -gt 0 -and $netstatOwners.Count -gt 0 -and $cimListenOwners[0] -ne $netstatOwners[0]){
            return [pscustomobject]@{Valid=$false;Category='restore.listener_snapshot_mismatch';OwnerPID=$null;Source='cim+netstat'}
        }
        if($cimListenOwners.Count -gt 0 -and $netstatOwners.Count -eq 0){
            return [pscustomobject]@{Valid=$false;Category='restore.listener_snapshot_mismatch';OwnerPID=$cimListenOwners[0];Source='cim'}
        }
    }

    $owners=@()
    if($netstatOwners.Count -gt 0){$owners=$netstatOwners;$source='netstat'}
    elseif($cimListenOwners.Count -gt 0){$owners=$cimListenOwners;$source='Get-NetTCPConnection'}
    else {
        if($cimUnknownOwners.Count -gt 0){return [pscustomobject]@{Valid=$false;Category='restore.listener_state_unverified';OwnerPID=$null;Source='unknown-state'}}
        return [pscustomobject]@{Valid=$false;Category='restore.listener_not_found';OwnerPID=$null;Source='none'}
    }
    if($owners.Count -ne 1){return [pscustomobject]@{Valid=$false;Category='restore.listener_multiple_owners';OwnerPID=$null;Source=$source}}
    if($owners[0] -ne $ExpectedPID){return [pscustomobject]@{Valid=$false;Category='restore.listener_owner_mismatch';OwnerPID=$owners[0];Source=$source}}
    if($cimUnknownOwners.Count -gt 0 -and @($cimUnknownOwners|Where-Object { $_ -ne $ExpectedPID }).Count -gt 0){
        return [pscustomobject]@{Valid=$false;Category='restore.listener_snapshot_mismatch';OwnerPID=$owners[0];Source=$source}
    }
    return [pscustomobject]@{Valid=$true;Category='restore.listener_owner_verified';OwnerPID=$owners[0];Source=$source}
}

function Get-VerifiedProcessIdentity([int]$ProcessId,[long]$ExpectedStartIdentity,[string]$ExpectedExe) {
    try {$proc=[Diagnostics.Process]::GetProcessById($ProcessId);$actualStart=$proc.StartTime.ToUniversalTime().Ticks}
    catch {return [pscustomobject]@{Valid=$false;Category='restore.process_not_running';ProcessId=$ProcessId;StartIdentity=$null;ExecutablePath=$null}}
    if($actualStart -ne $ExpectedStartIdentity){return [pscustomobject]@{Valid=$false;Category='restore.process_start_identity_mismatch';ProcessId=$ProcessId;StartIdentity=$actualStart;ExecutablePath=$null}}
    if(-not ('HRMS.ProcessImageQuery' -as [type])){
        Add-Type -Namespace HRMS -Name ProcessImageQuery -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)] public static extern System.IntPtr OpenProcess(uint access, bool inherit, int processId);
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)] public static extern bool QueryFullProcessImageName(System.IntPtr process, int flags, System.Text.StringBuilder path, ref uint size);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr handle);
'@ -ErrorAction Stop
    }
    $handle=[HRMS.ProcessImageQuery]::OpenProcess(0x1000,$false,$ProcessId)
    if($handle -eq [IntPtr]::Zero){return [pscustomobject]@{Valid=$false;Category='restore.executable_identity_unverified';ProcessId=$ProcessId;StartIdentity=$actualStart;ExecutablePath=$null}}
    try {
        $buffer=New-Object Text.StringBuilder 2048;$length=[uint32]$buffer.Capacity
        if(-not [HRMS.ProcessImageQuery]::QueryFullProcessImageName($handle,0,$buffer,[ref]$length)){return [pscustomobject]@{Valid=$false;Category='restore.executable_identity_unverified';ProcessId=$ProcessId;StartIdentity=$actualStart;ExecutablePath=$null}}
        $actualExe=[IO.Path]::GetFullPath($buffer.ToString());$expected=[IO.Path]::GetFullPath($ExpectedExe)
        if(-not [string]::Equals($actualExe,$expected,[StringComparison]::OrdinalIgnoreCase)){return [pscustomobject]@{Valid=$false;Category='restore.executable_identity_mismatch';ProcessId=$ProcessId;StartIdentity=$actualStart;ExecutablePath=$actualExe}}
        try{$procAfter=[Diagnostics.Process]::GetProcessById($ProcessId);$finalStart=$procAfter.StartTime.ToUniversalTime().Ticks}catch{return [pscustomobject]@{Valid=$false;Category='restore.process_not_running';ProcessId=$ProcessId;StartIdentity=$actualStart;ExecutablePath=$actualExe}}
        if($finalStart -ne $ExpectedStartIdentity){return [pscustomobject]@{Valid=$false;Category='restore.process_start_identity_mismatch';ProcessId=$ProcessId;StartIdentity=$finalStart;ExecutablePath=$actualExe}}
        return [pscustomobject]@{Valid=$true;Category='restore.process_identity_verified';ProcessId=$ProcessId;StartIdentity=$actualStart;ExecutablePath=$actualExe}
    } finally {[void][HRMS.ProcessImageQuery]::CloseHandle($handle)}
}

function Resolve-MySqlProtocolIdentityOutput([string]$Output,[string]$ExpectedDataDir,[int]$ExpectedPort,[string]$ExpectedServerUuid,[string]$ExpectedChallenge) {
    $rows=@($output -split "`r?`n" | Where-Object { $_.Length -gt 0 })
    if($rows.Count -ne 1){return [pscustomobject]@{Valid=$false;Category='restore.protocol_identity_malformed';ServerUuid=$null}}
    $fields=$rows[0].Split([char[]]@([char]9))
    if($fields.Count -ne 9){return [pscustomobject]@{Valid=$false;Category='restore.protocol_identity_malformed';ServerUuid=$null}}
    try {$actualDir=Get-CanonicalDirectoryIdentity (ConvertFrom-MySqlBatchField $fields[0])}catch{return [pscustomobject]@{Valid=$false;Category='restore.protocol_identity_malformed';ServerUuid=$null}}
    $expectedDir=Get-CanonicalDirectoryIdentity $ExpectedDataDir;$actualPort=0;$uuid=[guid]::Empty
    if(-not [int]::TryParse($fields[1],[ref]$actualPort) -or -not [guid]::TryParse($fields[2],[ref]$uuid)) {return [pscustomobject]@{Valid=$false;Category='restore.protocol_identity_malformed';ServerUuid=$null}}
    if(-not [string]::Equals($actualDir,$expectedDir,[StringComparison]::OrdinalIgnoreCase)){return [pscustomobject]@{Valid=$false;Category='restore.protocol_datadir_mismatch';ServerUuid=$fields[2]}}
    if($actualPort -ne $ExpectedPort){return [pscustomobject]@{Valid=$false;Category='restore.protocol_port_mismatch';ServerUuid=$fields[2]}}
    if($fields[3] -ne '8.0.46' -or $fields[4] -ne '127.0.0.1' -or $fields[5] -ne '0' -or $fields[6] -ne 'OFF' -or $fields[7] -ne '0' -or $fields[8] -cne $ExpectedChallenge){return [pscustomobject]@{Valid=$false;Category='restore.protocol_runtime_identity_mismatch';ServerUuid=$fields[2]}}
    if($ExpectedServerUuid -and -not [string]::Equals($fields[2],$ExpectedServerUuid,[StringComparison]::OrdinalIgnoreCase)){return [pscustomobject]@{Valid=$false;Category='restore.protocol_server_uuid_mismatch';ServerUuid=$fields[2]}}
    return [pscustomobject]@{Valid=$true;Category='restore.protocol_identity_verified';ServerUuid=$fields[2];Port=$actualPort;Version=$fields[3];BindAddress=$fields[4];SkipNetworking=$fields[5];EventScheduler=$fields[6];LogBin=$fields[7];ChallengeValid=$true}
}

function Invoke-ReadOnlyMysqlIdentityQuery([string]$MysqlExe,[int]$Port,[string]$Sql) {
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$MysqlExe;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true;Clear-MySqlPasswordEnvironment $psi
    foreach($argument in @('--no-defaults','--protocol=tcp','--host=127.0.0.1',"--port=$Port",'--user=root','--batch','--skip-column-names','--connect-timeout=3',"--execute=$Sql")){Add-ProcessArgument $psi $argument}
    $client=[Diagnostics.Process]::new();$client.StartInfo=$psi
    if(-not $client.Start()){throw 'Read-only MySQL identity client could not be started.'}
    $outTask=$client.StandardOutput.ReadToEndAsync();$errTask=$client.StandardError.ReadToEndAsync();$client.WaitForExit()
    $output=$outTask.GetAwaiter().GetResult();$null=$errTask.GetAwaiter().GetResult()
    if($client.ExitCode -ne 0){throw 'Read-only MySQL identity query failed; client output withheld.'}
    return $output
}

function Get-MySqlProtocolIdentity([string]$MysqlExe,[int]$Port,[string]$ExpectedDataDir,[string]$ExpectedServerUuid,[string]$LoginFile,[string]$User,[string]$Password) {
    $challenge=[Convert]::ToBase64String((New-CryptoRandomBytes 24))
    $sql="SELECT @@datadir,@@port,@@server_uuid,@@version,@@bind_address,@@skip_networking,@@global.event_scheduler,@@global.log_bin,'$challenge';"
    try {
        if($LoginFile){$output=Invoke-Client $MysqlExe @('--batch','--skip-column-names') $LoginFile $sql -Port $Port}
        elseif($User -eq 'root' -and [string]::IsNullOrEmpty($Password)){$output=Invoke-ReadOnlyMysqlIdentityQuery $MysqlExe $Port $sql}
        else{$output=Invoke-Client $MysqlExe @('--batch','--skip-column-names') $null $sql -User $User -Password $Password -Port $Port}
    } catch {return [pscustomobject]@{Valid=$false;Category='restore.protocol_identity_query_failed';ServerUuid=$null}}
    return Resolve-MySqlProtocolIdentityOutput $output $ExpectedDataDir $Port $ExpectedServerUuid $challenge
}

function Get-IsolatedSocketSnapshot([int]$Port) {
    $allConnections=@();$connections=@();$cimSucceeded=$false;$cimDiagnostic=$null
    try{$allConnections=@(Get-NetTCPConnection -ErrorAction Stop);$connections=@($allConnections | Where-Object { [int]$_.LocalPort -eq $Port });$cimSucceeded=$true}catch{$cimDiagnostic='{0}; HRESULT 0x{1:X8}' -f $_.Exception.GetType().FullName,$_.Exception.HResult}
    $netstatLines=@();$netstatSucceeded=$false;$netstatDiagnostic=$null
    try{$netstatExe=Join-Path $env:SystemRoot 'System32\netstat.exe';$netstatLines=@(& $netstatExe -ano -p tcp 2>$null);$netstatSucceeded=($LASTEXITCODE -eq 0);if(-not $netstatSucceeded){$netstatDiagnostic='exit_nonzero'}}catch{$netstatDiagnostic=$_.Exception.GetType().FullName}
    return [pscustomobject]@{CimConnections=$connections;ProcessConnections=$allConnections;NetstatLines=$netstatLines;CimSucceeded=$cimSucceeded;NetstatSucceeded=$netstatSucceeded;CimDiagnostic=$cimDiagnostic;NetstatDiagnostic=$netstatDiagnostic}
}

function Resolve-ProcessMetadataAvailability([bool]$NativeIdentityValid,[bool]$CimQuerySucceeded,[string]$CimExceptionType,[string]$CimHResult) {
    if(-not $NativeIdentityValid){return [pscustomobject]@{Valid=$false;Category='diagnostic.process_identity_unverified';Metadata='unavailable';Diagnostic=$null}}
    if($CimQuerySucceeded){return [pscustomobject]@{Valid=$true;Category='diagnostic.process_metadata_complete';Metadata='available';Diagnostic=$null}}
    $safeDiagnostic=if($CimExceptionType -and $CimHResult){"$CimExceptionType; HRESULT $CimHResult"}else{'CIM process metadata unavailable'}
    return [pscustomobject]@{Valid=$true;Category='diagnostic.process_metadata_partial';Metadata='partial';Diagnostic=$safeDiagnostic}
}

function Resolve-IsolatedSocketIdentityCorrelation($Before,$After,[int]$ExpectedPID,[int]$Port,[bool]$ProcessIdentityValid,[bool]$ProtocolIdentityValid) {
    if(-not $ProcessIdentityValid){return [pscustomobject]@{Valid=$false;Category='restore.process_identity_unverified';Source='none'}}
    if(-not $ProtocolIdentityValid){return [pscustomobject]@{Valid=$false;Category='restore.protocol_identity_unverified';Source='none'}}
    foreach($snapshot in @($Before,$After)){
        $processConnections=@();if($snapshot.PSObject.Properties['ProcessConnections']){$processConnections=@($snapshot.ProcessConnections | Where-Object {[int]$_.OwningProcess -eq $ExpectedPID -and [int]$_.LocalPort -ne $Port -and ([string]$_.State -eq 'Listen' -or ([string]$_.State -in @('','0','Unknown') -and [int]$_.RemotePort -eq 0))})}
        if($processConnections.Count -gt 0){return [pscustomobject]@{Valid=$false;Category='restore.unexpected_listener_port';Source='CIM-process-sockets'}}
        foreach($line in $snapshot.NetstatLines){if($line -match '^\s*TCP\s+\S*:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$' -and [int]$Matches[1] -ne $Port -and [int]$Matches[2] -eq $ExpectedPID){return [pscustomobject]@{Valid=$false;Category='restore.unexpected_listener_port';Source='netstat'}}}
    }
    $fallbackCount=0;$standardEvidence=$null
    foreach($snapshot in @($Before,$After)){
        if(-not $snapshot.CimSucceeded -and -not $snapshot.NetstatSucceeded){return [pscustomobject]@{Valid=$false;Category='restore.listener_query_failed';Source='none'}}
        $standard=Resolve-IsolatedListenerOwnership $snapshot.CimConnections $snapshot.NetstatLines $ExpectedPID $Port $snapshot.CimSucceeded $snapshot.NetstatSucceeded
        if($standard.Valid){$standardEvidence=$standard;continue}
        $fallbackCount++
        if($standard.Category -in @('restore.listener_multiple_owners','restore.listener_owner_mismatch','restore.listener_snapshot_mismatch','restore.listener_invalid_port')){return $standard}
        if(-not $snapshot.CimSucceeded -or -not $snapshot.NetstatSucceeded){return [pscustomobject]@{Valid=$false;Category='restore.listener_state_unverified';Source='incomplete-snapshot'}}
        $portRows=@($snapshot.CimConnections | Where-Object { [int]$_.LocalPort -eq $Port })
        $owners=@($portRows | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
        $listeners=@($snapshot.NetstatLines | ForEach-Object {
            if($_ -match '^\s*TCP\s+\S*:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$' -and [int]$Matches[1] -eq $Port){[int]$Matches[2]}
        } | Sort-Object -Unique)
        $foreignOwner=$false
        if($owners.Count -eq 1 -and $owners[0] -ne $ExpectedPID){$foreignOwner=$true}
        if($listeners.Count -eq 1 -and $listeners[0] -ne $ExpectedPID){$foreignOwner=$true}
        $expectedOwnerOnly=($owners.Count -eq 1)
        if($expectedOwnerOnly){$expectedOwnerOnly=($owners[0] -eq $ExpectedPID)}
        if(-not $expectedOwnerOnly -or $listeners.Count -gt 0){
            $category='restore.listener_state_unverified'
            if($owners.Count -gt 1 -or $listeners.Count -gt 1){$category='restore.listener_multiple_owners'}
            elseif($foreignOwner){$category='restore.listener_owner_mismatch'}
            return [pscustomobject]@{Valid=$false;Category=$category;Source='socket-owner-conflict'}
        }
        if($portRows.Count -ne 1){return [pscustomobject]@{Valid=$false;Category='restore.listener_state_unverified';Source='socket-record-count'}}
        $row=$portRows[0];$state=[string]$row.State;$remotePort=0
        if($state -notin @('','0','Unknown') -or -not [int]::TryParse([string]$row.RemotePort,[ref]$remotePort) -or $remotePort -ne 0 -or [string]$row.RemoteAddress -notin @('','0.0.0.0','::','*')){return [pscustomobject]@{Valid=$false;Category='restore.listener_state_unverified';Source='socket-shape-unverified'}}
    }
    if($fallbackCount -eq 0){return [pscustomobject]@{Valid=$true;Category='restore.listener_owner_verified';OwnerPID=$ExpectedPID;Source=$standardEvidence.Source}}
    if($fallbackCount -ne 2){return [pscustomobject]@{Valid=$false;Category='restore.listener_snapshot_mismatch';Source='mixed-standard-and-unknown'}}
    $beforeRow=@($Before.CimConnections | Where-Object { [int]$_.LocalPort -eq $Port })[0]
    $afterRow=@($After.CimConnections | Where-Object { [int]$_.LocalPort -eq $Port })[0]
    if([int]$beforeRow.OwningProcess -ne [int]$afterRow.OwningProcess -or [string]$beforeRow.State -ne [string]$afterRow.State -or [string]$beforeRow.RemoteAddress -ne [string]$afterRow.RemoteAddress -or [int]$beforeRow.RemotePort -ne [int]$afterRow.RemotePort){return [pscustomobject]@{Valid=$false;Category='restore.listener_snapshot_mismatch';Source='before+after'}}
    return [pscustomobject]@{Valid=$true;Category='restore.socket_owner_protocol_identity_verified';OwnerPID=$ExpectedPID;Source='CIM-owner+MySQL-identity+stable-snapshots'}
}

function Test-IsolatedProcessOwnership([Diagnostics.Process]$Process,[string]$ExpectedExe,[string]$ExpectedDataDir,[int]$ExpectedPort,[long]$ExpectedStartIdentity,[string]$MysqlExe,[string]$ExpectedServerUuid,[string]$LoginFile,[string]$User='root',[string]$Password='') {
    $script:LastRestoreDiagnostic=$null
    if(-not $Process -or $Process.HasExited){$script:LastRestoreDiagnostic='restore.process_not_running';throw 'Isolated restore process ownership failed: child process is not running.'}
    $processIdentity=Get-VerifiedProcessIdentity $Process.Id $ExpectedStartIdentity $ExpectedExe
    if(-not $processIdentity.Valid){$script:LastRestoreDiagnostic=$processIdentity.Category;throw 'Isolated restore process ownership failed: PID, start identity, or executable could not be verified.'}
    $before=Get-IsolatedSocketSnapshot $ExpectedPort
    $protocolIdentity=Get-MySqlProtocolIdentity $MysqlExe $ExpectedPort $ExpectedDataDir $ExpectedServerUuid $LoginFile $User $Password
    if(-not $protocolIdentity.Valid){$script:LastRestoreDiagnostic=$protocolIdentity.Category;throw 'Isolated restore process ownership failed: read-only MySQL identity verification failed.'}
    $after=Get-IsolatedSocketSnapshot $ExpectedPort
    $processAfter=Get-VerifiedProcessIdentity $Process.Id $ExpectedStartIdentity $ExpectedExe
    if(-not $processAfter.Valid){$script:LastRestoreDiagnostic=$processAfter.Category;throw 'Isolated restore process ownership failed: process identity changed during protocol attestation.'}
    $ownership=Resolve-IsolatedSocketIdentityCorrelation $before $after $Process.Id $ExpectedPort $processAfter.Valid $protocolIdentity.Valid
    if(-not $ownership.Valid){$script:LastRestoreDiagnostic=$ownership.Category;throw 'Isolated restore process ownership failed: process, socket owner, and protocol identity did not agree.'}
    return $protocolIdentity
}

function Get-SanitizedIsolatedProcessDiagnostic([int]$TargetPid,[long]$ExpectedStartIdentity,[string]$ExpectedDataDir,[int]$ExpectedPort,[string]$ExpectedExe,[string]$MysqlExe,[string]$ExpectedServerUuid) {
    $parsedExpectedUuid=[guid]::Empty
    if($TargetPid -le 0 -or $ExpectedStartIdentity -le 0 -or [string]::IsNullOrWhiteSpace($ExpectedDataDir) -or $ExpectedPort -lt 1 -or $ExpectedPort -gt 65535 -or $ExpectedPort -eq 3306 -or -not [guid]::TryParse($ExpectedServerUuid,[ref]$parsedExpectedUuid)){
        return [pscustomobject]@{Result='BLOCKED';Category='diagnostic.arguments_invalid';ProcessIdentityVerified=$false;ListenerCategory='not_checked';Sockets=@()}
    }
    $processIdentity=Get-VerifiedProcessIdentity $TargetPid $ExpectedStartIdentity $ExpectedExe
    if(-not $processIdentity.Valid){return [pscustomobject]@{Result='BLOCKED';Category=$processIdentity.Category;ProcessId=$TargetPid;ProcessStartIdentity=$processIdentity.StartIdentity;ExecutablePathVerified=($null -ne $processIdentity.ExecutablePath);ProcessIdentityVerified=$false;ListenerCategory='not_checked';Sockets=@()}}
    $cimMetadata=$null;$cimProcessDiagnostic=$null
    try{$cimMetadata=Get-CimInstance Win32_Process -Filter "ProcessId=$TargetPid" -ErrorAction Stop}catch{$cimProcessDiagnostic='{0}; HRESULT 0x{1:X8}' -f $_.Exception.GetType().FullName,$_.Exception.HResult}
    $cimCommandLine=if($cimMetadata){[string]$cimMetadata.CommandLine}else{''}
    $cimExecutable=if($cimMetadata){[string]$cimMetadata.ExecutablePath}else{''}
    $cimComplete=($cimMetadata -and -not [string]::IsNullOrWhiteSpace($cimCommandLine) -and -not [string]::IsNullOrWhiteSpace($cimExecutable))
    if($cimMetadata -and -not $cimComplete -and -not $cimProcessDiagnostic){$cimProcessDiagnostic='CIM returned incomplete process fields'}
    $cimType=if($cimProcessDiagnostic){($cimProcessDiagnostic -split ';')[0]}else{$null}
    $cimHresult=if($cimProcessDiagnostic -match 'HRESULT (0x[0-9A-Fa-f]+)'){$Matches[1]}else{$null}
    $metadataStatus=Resolve-ProcessMetadataAvailability $processIdentity.Valid $cimComplete $cimType $cimHresult
    $before=Get-IsolatedSocketSnapshot $ExpectedPort
    $protocol=Get-MySqlProtocolIdentity $MysqlExe $ExpectedPort $ExpectedDataDir $ExpectedServerUuid $null 'root' ''
    $after=Get-IsolatedSocketSnapshot $ExpectedPort
    $processAfter=Get-VerifiedProcessIdentity $TargetPid $ExpectedStartIdentity $ExpectedExe
    $correlation=Resolve-IsolatedSocketIdentityCorrelation $before $after $TargetPid $ExpectedPort $processAfter.Valid $protocol.Valid
    $sockets=@()
    foreach($c in @($before.CimConnections)+@($after.CimConnections)){
        if([int]$c.LocalPort -eq $ExpectedPort){$sockets+= [pscustomobject]@{Port=[int]$c.LocalPort;State=[string]$c.State;OwnerPid=[int]$c.OwningProcess;RemotePort=[int]$c.RemotePort;Source='Get-NetTCPConnection'}}
    }
    $listenerLines=@($before.NetstatLines)+@($after.NetstatLines)
    foreach($line in $listenerLines){if($line -match '^\s*TCP\s+\S*:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$' -and [int]$Matches[1] -eq $ExpectedPort){$sockets+=[pscustomobject]@{Port=$ExpectedPort;State='LISTENING';OwnerPid=[int]$Matches[2];Source='netstat'}}}
    $otherListeningPorts=@()
    foreach($snapshot in @($before,$after)){
        if($snapshot.PSObject.Properties['ProcessConnections']){$otherListeningPorts+=@($snapshot.ProcessConnections|Where-Object {[int]$_.OwningProcess -eq $TargetPid -and [int]$_.LocalPort -ne $ExpectedPort -and [string]$_.State -eq 'Listen'}|Select-Object -ExpandProperty LocalPort)}
        foreach($line in $snapshot.NetstatLines){if($line -match '^\s*TCP\s+\S*:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$' -and [int]$Matches[1] -ne $ExpectedPort -and [int]$Matches[2] -eq $TargetPid){$otherListeningPorts+=[int]$Matches[1]}}
    }
    $parentId=if($cimMetadata){[int]$cimMetadata.ParentProcessId}else{$null}
    return [pscustomobject]@{Result=$(if($correlation.Valid){'PASS'}else{'BLOCKED'});Category=$(if($correlation.Valid){'diagnostic.identity_correlation_verified'}else{$correlation.Category});ProcessId=$TargetPid;ProcessStartIdentity=$processIdentity.StartIdentity;ExecutablePathVerified=$true;ExecutablePath=$processIdentity.ExecutablePath;ParentProcessId=$parentId;CimProcessMetadata=$metadataStatus.Metadata;CimProcessDiagnostic=$metadataStatus.Diagnostic;CommandLineMetadataAvailable=(-not [string]::IsNullOrWhiteSpace($cimCommandLine));DataDirArgumentMatches=$(if($cimCommandLine){$cimCommandLine -like "*--datadir=$ExpectedDataDir*"}else{$null});PortArgumentMatches=$(if($cimCommandLine){$cimCommandLine -like "*--port=$ExpectedPort*"}else{$null});NoDefaultsArgumentMatches=$(if($cimCommandLine){$cimCommandLine -like '*--no-defaults*'}else{$null});NoMonitorArgumentMatches=$(if($cimCommandLine){$cimCommandLine -like '*--no-monitor*'}else{$null});ProcessIdentityVerified=$processAfter.Valid;ExpectedDataDir=$ExpectedDataDir;ExpectedPort=$ExpectedPort;ExpectedServerUuid=$ExpectedServerUuid;ProtocolIdentityCategory=$protocol.Category;ProtocolIdentityVerified=$protocol.Valid;ListenerCategory=$correlation.Category;CimSocketQueriesSucceeded=($before.CimSucceeded -and $after.CimSucceeded);CimSocketDiagnostic=$(if(-not $before.CimSucceeded){$before.CimDiagnostic}else{$after.CimDiagnostic});NetstatQueriesSucceeded=($before.NetstatSucceeded -and $after.NetstatSucceeded);Sockets=@($sockets|Sort-Object Port,OwnerPid,Source -Unique);OtherListeningPorts=@($otherListeningPorts|Sort-Object -Unique)}
}

function Get-IsolatedServerArguments([string]$BaseDir,[string]$DataDir,[int]$Port,[string]$LogPath) {
    if($Port -eq 3306 -or $Port -lt 1 -or $Port -gt 65535){throw 'Restore isolation validation failed: isolated port is invalid.'}
    return @('--no-defaults','--no-monitor',"--basedir=$BaseDir","--datadir=$DataDir","--port=$Port",'--bind-address=127.0.0.1','--mysqlx=0','--skip-log-bin','--event-scheduler=OFF',"--log-error=$LogPath")
}

function Stop-IsolatedMysqlGracefully([Diagnostics.Process]$Process,[string]$ExpectedExe,[string]$ExpectedDataDir,[int]$Port,[string]$MysqlAdmin,[string]$LoginFile,[long]$ExpectedStartIdentity,[string]$MysqlExe,[string]$ExpectedServerUuid) {
    [void](Test-IsolatedProcessOwnership $Process $ExpectedExe $ExpectedDataDir $Port $ExpectedStartIdentity $MysqlExe $ExpectedServerUuid $LoginFile)
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$MysqlAdmin;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    $psi.RedirectStandardError=$true;$psi.RedirectStandardOutput=$true
    Clear-MySqlPasswordEnvironment $psi
    Add-ProcessArgument $psi "--defaults-file=$LoginFile";Add-ProcessArgument $psi '--host=127.0.0.1';Add-ProcessArgument $psi '--protocol=tcp';Add-ProcessArgument $psi "--port=$Port";Add-ProcessArgument $psi 'shutdown'
    $admin=[Diagnostics.Process]::new();$admin.StartInfo=$psi
    if(-not $admin.Start()){throw 'Graceful isolated MySQL shutdown could not be started.'}
    $errTask=$admin.StandardError.ReadToEndAsync();$outTask=$admin.StandardOutput.ReadToEndAsync()
    if(-not $admin.WaitForExit(30000)){throw 'Graceful isolated MySQL shutdown timed out; process was left running.'}
    $null=$errTask.GetAwaiter().GetResult();$null=$outTask.GetAwaiter().GetResult()
    if($admin.ExitCode -ne 0){throw 'Graceful isolated MySQL shutdown returned a failure; process was left untouched.'}
    if(-not $Process.WaitForExit(30000)){throw 'Graceful isolated MySQL shutdown was not confirmed; process was left running.'}
    $remaining=@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if($remaining.Count -gt 0){throw 'Graceful isolated MySQL shutdown failed: isolated port remains in LISTEN state.'}
    return $true
}

# Shared restore-only lifecycle state machine. All effects are supplied by Ops so
# the exact ordering and fail-closed behavior can be exercised without processes.
function Invoke-HrmsRestoreOnlyLifecycle {
    param([hashtable]$Ops,[string]$ArtifactPath,[string]$ExpectedArtifactPath,[string]$ExpectedSha256,[string]$EvidenceRoot,[switch]$PreflightOnly)
    $state=@{ArtifactPath=$ArtifactPath;Run=$null;Verified=$null;Process=$null;Login=$null;RestoreSucceeded=$false;Cleanup='NOT_ATTEMPTED';FailureCategory=$null;Item=$null;Hash=$null;DataDir=$null;Port=34060;ServerUuid=$null;ProcessStartIdentity=$null;RestorePassword=$null;Inventory=$null;RestoredRowCounts=@();MySql80Identity=$null;MySql80PostCheck='NOT_VERIFIED'}
    foreach($name in @('ResolvePath','GetItem','GetHash','ReadPassphrase','CreateRun','ProtectDirectory','VerifyHmac','CheckTools','CaptureMySql80Identity','VerifyMySql80Identity','CheckPort','CreateDataDirectory','InitializeServer','StartServer','WaitReady','VerifyProtocolIdentity','VerifyProcessIdentity','SetRestoreCredential','CreateLogin','CheckEmptyTarget','ImportArtifact','VerifyRestoredData','StopServer','WriteManifest','CleanupLogin','ClearKeys')) {
        if(-not $Ops.ContainsKey($name) -or $Ops[$name] -isnot [scriptblock]) { throw "Restore-only operation adapter is missing: $name" }
    }
    $selected=& $Ops.ResolvePath $ArtifactPath
    $expected=& $Ops.ResolvePath $ExpectedArtifactPath
    if(-not [string]::Equals($selected,$expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Restore-only blocked: selected artifact path does not match the approved artifact.'}
    $item=& $Ops.GetItem $selected
    $state.Item=$item
    if($item.PSIsContainer){throw 'Restore-only blocked: selected artifact is not a file.'}
    $hash=& $Ops.GetHash $selected
    $state.Hash=$hash
    if($hash -cne $ExpectedSha256){throw 'Restore-only blocked: artifact SHA-256 mismatch.'}
    $pass=& $Ops.ReadPassphrase
    if([string]::IsNullOrEmpty($pass)){throw 'Restore-only blocked: decryption input is missing.'}
    if($PreflightOnly){
        $results=New-Object 'System.Collections.Generic.List[object]'
        $currentPreflightStage='selected path and pinned SHA-256'
        $results.Add([pscustomobject]@{Stage='selected path and pinned SHA-256';Result='PASS'})
        try {
            $currentPreflightStage='temporary preflight directory creation and protection'
            $state.Run=& $Ops.CreatePreflightRun
            & $Ops.ProtectDirectory $state.Run
            $currentPreflightStage='fresh encrypted-backup HMAC and passphrase'
            try {$state.Verified=& $Ops.VerifyHmac $selected $pass;$results.Add([pscustomobject]@{Stage='fresh encrypted-backup HMAC and passphrase';Result='PASS'})}
            catch {$script:HrmsHmacFailureCategory=$_.Exception.Data['HrmsHmacFailureCategory'];if(-not $script:HrmsHmacFailureCategory){$script:HrmsHmacFailureCategory='encrypted_artifact.authentication_failed'};$results.Add([pscustomobject]@{Stage='fresh encrypted-backup HMAC and passphrase';Result='BLOCKED'});throw 'Restore-only preflight BLOCKED: fresh artifact authentication failed.'}
            $currentPreflightStage='MySQL80 service identity and process family'
            $state.MySql80Identity=& $Ops.CaptureMySql80Identity
            $results.Add([pscustomobject]@{Stage='MySQL80 service identity and process family';Result='PASS'})
            & $Ops.CheckTools
            $currentPreflightStage='port 34060 availability'
            & $Ops.CheckPort 34060 $state.MySql80Identity
            $results.Add([pscustomobject]@{Stage='port 34060 availability';Result='PASS'})
            $currentPreflightStage='credential-file ACL round-trip'
            & $Ops.PreflightAclRoundTrip
            $results.Add([pscustomobject]@{Stage='credential-file ACL round-trip';Result='PASS'})
            $currentPreflightStage='isolated restore configuration (no startup)'
            & $Ops.ValidateIsolatedConfiguration $state
            $results.Add([pscustomobject]@{Stage='isolated restore configuration (no startup)';Result='PASS'})
            $currentPreflightStage='MySQL80 stability recheck'
            & $Ops.VerifyMySql80Identity $state.MySql80Identity
            $results.Add([pscustomobject]@{Stage='MySQL80 stability recheck';Result='PASS'})
            $currentPreflightStage='temporary preflight artifact cleanup'
            & $Ops.CleanupPreflightArtifacts $state.Run
            $state.Run=$null
            $state.Cleanup='SUCCEEDED'
            $results.Add([pscustomobject]@{Stage='temporary preflight artifact cleanup';Result='PASS'})
            $report=[pscustomobject]@{Result='PASS';Mode='RestoreOnlyPreflight';FailureStage=$null;CleanupStatus='SUCCEEDED';Stages=@($results.ToArray());RuntimeOnly=@('isolated server startup and protocol identity','encrypted dump import and restored-data verification','graceful isolated-server shutdown') } | ConvertTo-Json -Depth 4 -Compress
            $script:HrmsPreflightReportEmitted=$true
            [Console]::Out.WriteLine($report);[Console]::Out.Flush()
            return $state
        } catch {
            $cleanupFailed=$false;$cleanupAttempted=$false
            if($state.Run){$cleanupAttempted=$true;try{& $Ops.CleanupPreflightArtifacts $state.Run;$state.Run=$null}catch{$cleanupFailed=$true}}
            if(@($results | Where-Object {$_.Stage -eq $currentPreflightStage}).Count -eq 0){$results.Add([pscustomobject]@{Stage=$currentPreflightStage;Result='BLOCKED'})}
            $aclDiagnostic=$null
            if($currentPreflightStage -match 'ACL|credential-file|directory creation and protection'){$aclDiagnostic=$script:CredentialAclFailureDetails}
            $hmacCategory=if($currentPreflightStage -eq 'fresh encrypted-backup HMAC and passphrase'){$script:HrmsHmacFailureCategory}else{$null}
            $cleanupStatus=if($cleanupFailed){'BLOCKED'}elseif($cleanupAttempted){'SUCCEEDED'}elseif($aclDiagnostic -and $aclDiagnostic.CleanupStatus){$aclDiagnostic.CleanupStatus}else{'NOT_REQUIRED'}
            if($cleanupStatus -eq 'BLOCKED'){$results.Add([pscustomobject]@{Stage='temporary preflight artifact cleanup';Result='BLOCKED'})}
            $report=[pscustomobject]@{Result='BLOCKED';Mode='RestoreOnlyPreflight';FailureStage=$currentPreflightStage;FailureCategory=if($aclDiagnostic){$aclDiagnostic.Category}elseif($hmacCategory){$hmacCategory}else{$null};OperationStage=if($aclDiagnostic){$aclDiagnostic.Stage}else{$null};ExceptionType=if($aclDiagnostic){$aclDiagnostic.ExceptionType}else{$null};HResult=if($aclDiagnostic){$aclDiagnostic.HResult}else{$null};Win32ErrorCode=if($aclDiagnostic){$aclDiagnostic.Win32ErrorCode}else{$null};CleanupStatus=$cleanupStatus;Stages=@($results.ToArray());ErrorTextWithheld=$true;RuntimeOnly=@('isolated server startup and protocol identity','encrypted dump import and restored-data verification','graceful isolated-server shutdown')} | ConvertTo-Json -Depth 4 -Compress
            $script:HrmsPreflightReportEmitted=$true
            [Console]::Out.WriteLine($report);[Console]::Out.Flush()
            throw 'Restore-only preflight BLOCKED.'
        } finally {
            if($state.Verified){& $Ops.ClearKeys $state}
            $pass=$null
        }
    }
    $state.Run=& $Ops.CreateRun $EvidenceRoot
    & $Ops.ProtectDirectory $state.Run
    try {$state.Verified=& $Ops.VerifyHmac $selected $pass} catch {
        $state.FailureCategory='artifact.hmac_validation_failed'
        & $Ops.WriteManifest $state 'BLOCKED'
        throw 'Restore-only blocked: fresh artifact authentication failed.'
    } finally {$pass=$null}
    try {
        $state.MySql80Identity=& $Ops.CaptureMySql80Identity
        & $Ops.CheckTools
        & $Ops.CheckPort 34060 $state.MySql80Identity
        $state.DataDir=& $Ops.CreateDataDirectory $state.Run
        & $Ops.InitializeServer $state
        $state.Process=& $Ops.StartServer $state
        & $Ops.WaitReady $state
        & $Ops.VerifyProtocolIdentity $state
        & $Ops.VerifyProcessIdentity $state
        & $Ops.SetRestoreCredential $state
        $state.Login=& $Ops.CreateLogin $state
        & $Ops.VerifyProcessIdentity $state
        & $Ops.CheckEmptyTarget $state
        & $Ops.ImportArtifact $state
        & $Ops.VerifyProcessIdentity $state
        & $Ops.VerifyRestoredData $state
        $state.RestoreSucceeded=$true
    } catch {
        $state.FailureCategory='restore.stage_failed'
        throw
    } finally {
        if($state.Process) {
            try { & $Ops.VerifyProcessIdentity $state; & $Ops.StopServer $state; $state.Cleanup='SUCCEEDED' }
            catch { $state.Cleanup='BLOCKED' }
        } else { $state.Cleanup='NOT_REQUIRED' }
        if($state.MySql80Identity){
            try { & $Ops.VerifyMySql80Identity $state.MySql80Identity; $state.MySql80PostCheck='PASS' }
            catch { $state.MySql80PostCheck='BLOCKED';$state.Cleanup='BLOCKED';$state.FailureCategory='protected.mysql80_identity_changed_or_unverified' }
        }
        & $Ops.WriteManifest $state $(if($state.RestoreSucceeded -and $state.Cleanup -eq 'SUCCEEDED'){'PASS'}else{'BLOCKED'})
        & $Ops.ClearKeys $state
        if($state.Login){& $Ops.CleanupLogin $state.Login}
    }
    return $state
}

function New-HrmsRestoreOnlyProductionOperations([string]$MysqlBin) {
    $mysql=Join-Path $MysqlBin 'mysql.exe';$server=Join-Path $MysqlBin 'mysqld.exe';$mysqlAdmin=Join-Path $MysqlBin 'mysqladmin.exe'
    $ops=@{}
    $ops.ResolvePath={param($p) [IO.Path]::GetFullPath($p)}.GetNewClosure()
    $ops.GetItem={param($p) Get-Item -LiteralPath $p -ErrorAction Stop}.GetNewClosure()
    $ops.GetHash={param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}.GetNewClosure()
    $ops.ReadPassphrase={Get-SecureText (Read-Host 'Enter the existing backup encryption passphrase' -AsSecureString)}.GetNewClosure()
    $ops.CreateRun={param($root) $full=[IO.Path]::GetFullPath($root);[void](New-Item -ItemType Directory -Path $full -Force);$run=Join-Path $full ('restore-only-'+(Get-Date -Format 'yyyyMMdd-HHmmss'));[void](New-Item -ItemType Directory -Path $run -ErrorAction Stop);$run}.GetNewClosure()
    $ops.ProtectDirectory={param($run) Protect-Directory $run}.GetNewClosure()
    $ops.VerifyHmac={param($path,$pass) Test-EncryptedDump $path $pass}.GetNewClosure()
    $ops.CheckTools={foreach($tool in @($mysql,$server,$mysqlAdmin)){if(-not(Test-Path -LiteralPath $tool)){throw 'Restore-only blocked: required isolated MySQL tool is unavailable.'}}}.GetNewClosure()
    $ops.CaptureMySql80Identity={Get-HrmsMySql80ServiceSnapshot}.GetNewClosure()
    $ops.VerifyMySql80Identity={param($baseline) [void](Assert-HrmsMySql80ServiceUnchanged $baseline)}.GetNewClosure()
    $ops.CheckPort={param($port,$serviceIdentity) [void](Assert-RestoreOnlyPortAvailable $port $serviceIdentity)}.GetNewClosure()
    $ops.PreflightAclRoundTrip={
        $probe=New-MySqlLoginFile 'hrms_preflight_probe' ([Guid]::NewGuid().ToString('N')) 34060 $false
        try {
            [void](Read-VerifiedAcl $probe.Directory ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) $true)
            [void](Read-VerifiedAcl $probe.Path ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) $false)
        } finally { Remove-MySqlLoginFile $probe }
    }.GetNewClosure()
    $ops.CreatePreflightRun={
        $path=Join-Path ([IO.Path]::GetTempPath()) ('hrms-restore-preflight-'+[Guid]::NewGuid().ToString('N'))
        $script:CredentialAclFailureStage='directory creation'
        try {[void](New-Item -ItemType Directory -Path $path -ErrorAction Stop);return $path}
        catch {$e=$_.Exception;$code=if($e.HResult){'0x{0:X8}' -f $e.HResult}else{$null};$partial=Test-Path -LiteralPath $path -PathType Container;$cleanup='NOT_REQUIRED';if($partial){try{Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop;if(Test-Path -LiteralPath $path -PathType Container){throw 'cleanup unconfirmed'};$cleanup='SUCCEEDED'}catch{$cleanup='BLOCKED'}};$script:CredentialAclFailureDetails=[pscustomobject]@{Stage='directory creation';Category='credential_acl.directory_create_failed';ExceptionType=$e.GetType().FullName;HResult=$code;Win32ErrorCode=(Get-SafeWin32ErrorCode $e);CleanupStatus=$cleanup};throw 'Restore-only preflight BLOCKED: temporary directory creation failed.'}
    }.GetNewClosure()
    $ops.ValidateIsolatedConfiguration={param($state)
        $data=Join-Path $state.Run 'isolated-mysql-data';$log=Join-Path $state.Run 'restore-mysqld.log'
        $args=@(Get-IsolatedServerArguments ([IO.Path]::GetFullPath((Join-Path $MysqlBin '..'))) $data 34060 $log)
        if($args -notcontains '--no-defaults' -or $args -notcontains '--port=34060' -or $args -notcontains '--bind-address=127.0.0.1' -or $args -notcontains "--datadir=$data" -or $args -contains '--port=3306' -or (Test-Path -LiteralPath $data)) { throw 'Restore-only preflight BLOCKED: isolated server configuration failed validation.' }
        $state.Port=34060
    }.GetNewClosure()
    $ops.CleanupPreflightArtifacts={param($path)
        $full=[IO.Path]::GetFullPath($path);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if(-not $full.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $full -Leaf) -notmatch '^hrms-restore-preflight-[0-9a-f]{32}$'){throw 'Restore-only preflight BLOCKED: temporary cleanup ownership validation failed.'}
        Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
        if(Test-Path -LiteralPath $full){throw 'Restore-only preflight BLOCKED: temporary cleanup could not be confirmed.'}
    }.GetNewClosure()
    $ops.CreateDataDirectory={param($run) $data=Join-Path $run 'isolated-mysql-data';if(Test-Path -LiteralPath $data){throw 'Restore-only blocked: isolated working directory already exists.'};[void](New-Item -ItemType Directory -Path $data);$data}.GetNewClosure()
    $ops.InitializeServer={param($state)
        $state.DataDir=Join-Path $state.Run 'isolated-mysql-data';$state.ServerLog=Join-Path $state.Run 'restore-mysqld.log';$state.Port=34060
        $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$server;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true;Clear-MySqlPasswordEnvironment $psi
        foreach($a in @('--no-defaults','--initialize-insecure',"--basedir=$([IO.Path]::GetFullPath((Join-Path $MysqlBin '..'))) ","--datadir=$($state.DataDir)")){Add-ProcessArgument $psi $a.Trim()}
        $p=[Diagnostics.Process]::new();$p.StartInfo=$psi;[void]$p.Start();$null=$p.StandardOutput.ReadToEndAsync();$null=$p.StandardError.ReadToEndAsync();$p.WaitForExit();if($p.ExitCode -ne 0){throw 'Restore-only blocked: isolated MySQL initialization failed.'}
    }.GetNewClosure()
    $ops.StartServer={param($state)
        $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$server;$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;Clear-MySqlPasswordEnvironment $psi
        foreach($a in (Get-IsolatedServerArguments ([IO.Path]::GetFullPath((Join-Path $MysqlBin '..'))) $state.DataDir $state.Port $state.ServerLog)){Add-ProcessArgument $psi $a}
        $p=[Diagnostics.Process]::new();$p.StartInfo=$psi;[void]$p.Start();$state.ProcessStartIdentity=$p.StartTime.ToUniversalTime().Ticks;$p
    }.GetNewClosure()
    $ops.WaitReady={param($state)
        $ready=$false;for($i=0;$i -lt 60;$i++){if($state.Process.HasExited){throw 'Restore-only blocked: isolated server exited during startup.'};try{$null=Invoke-Client $mysql @('--batch','--skip-column-names') $null 'SELECT @@datadir,@@port;' -User root -Password '' -Port $state.Port;$ready=$true;break}catch{Start-Sleep -Seconds 1}}
        if(-not $ready){throw 'Restore-only blocked: isolated server did not become ready on port 34060.'}
    }.GetNewClosure()
    $ops.VerifyProtocolIdentity={param($state) $id=Get-MySqlProtocolIdentity $mysql $state.Port $state.DataDir $null $null root '';if(-not $id.Valid){throw 'Restore-only blocked: isolated protocol identity failed.'};$state.ServerUuid=$id.ServerUuid}.GetNewClosure()
    $ops.VerifyProcessIdentity={param($state) if(-not $state.Process){return};$loginPath=if($state.Login){$state.Login.Path}else{$null};[void](Test-IsolatedProcessOwnership $state.Process $server $state.DataDir $state.Port $state.ProcessStartIdentity $mysql $state.ServerUuid $loginPath)}.GetNewClosure()
    $ops.SetRestoreCredential={param($state) $state.RestorePassword=[Convert]::ToBase64String((New-CryptoRandomBytes 32));[void](Invoke-Client $mysql @('--batch','--skip-column-names') $null "ALTER USER 'root'@'localhost' IDENTIFIED BY '$($state.RestorePassword)';" -User root -Password '' -Port $state.Port -SqlOnStandardInput)}.GetNewClosure()
    $ops.CreateLogin={param($state) New-MySqlLoginFile root $state.RestorePassword $state.Port $false}.GetNewClosure()
    $ops.CheckEmptyTarget={param($state) $empty=Invoke-Client $mysql @('--batch','--skip-column-names') $state.Login.Path "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='hrms_web1';" -Port $state.Port;if($empty.Trim() -ne '0'){throw 'Restore-only blocked: target schema already exists.'}}.GetNewClosure()
    $ops.ImportArtifact={param($state) Import-EncryptedDump $state.ArtifactPath $state.Verified $mysql $state.Login.Path $state.Port}.GetNewClosure()
    $ops.VerifyRestoredData={param($state)
        $schema=Invoke-Client $mysql @('--batch','--skip-column-names') $state.Login.Path "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='hrms_web1';" -Port $state.Port
        $inventory=Invoke-Client $mysql @('--batch','--skip-column-names') $state.Login.Path "SELECT TABLE_NAME,COALESCE(ENGINE,'VIEW') FROM information_schema.TABLES WHERE TABLE_SCHEMA='hrms_web1' ORDER BY TABLE_NAME;" -Port $state.Port
        if($schema.Trim() -ne '1' -or [string]::IsNullOrWhiteSpace($inventory)){throw 'Restore-only verification failed: restored schema or table inventory is empty.'}
        $tableNames=@($inventory.Trim() -split "`r?`n" | ForEach-Object {$fields=$_ -split "`t";if($fields[-1] -ne 'VIEW'){$fields[0]}})
        $rowCounts=@();foreach($table in $tableNames){$quoted=$table.Replace('`','``');$count=Invoke-Client $mysql @('--batch','--skip-column-names') $state.Login.Path ('SELECT COUNT(*) FROM `hrms_web1`.`'+$quoted+'`;') -Port $state.Port;$rowCounts+=$count.Trim()}
        Assert-RestoredRowCounts $tableNames $rowCounts
        $state.Inventory=$inventory;$state.RestoredRowCounts=@(for($i=0;$i -lt $tableNames.Count;$i++){[pscustomobject]@{Table=$tableNames[$i];Rows=[long]$rowCounts[$i]}})
    }.GetNewClosure()
    $ops.StopServer={param($state) [void](Stop-IsolatedMysqlGracefully $state.Process $server $state.DataDir $state.Port $mysqlAdmin $state.Login.Path $state.ProcessStartIdentity $mysql $state.ServerUuid)}.GetNewClosure()
    $ops.WriteManifest={param($state,$result)
        $manifest=[ordered]@{Result=$result;Mode='RestoreOnly';TimestampUtc=[DateTime]::UtcNow.ToString('o');ArtifactIntegrity=$(if($state.Verified){'PASS'}else{'FAIL_OR_NOT_VERIFIED'});Restore=$(if($state.RestoreSucceeded){'PASS'}else{'FAIL'});Cleanup=$state.Cleanup;FailureCategory=$state.FailureCategory;OriginalArtifactPath=$state.ArtifactPath;OriginalArtifactBytes=$state.Item.Length;OriginalArtifactSHA256=$state.Hash;Hmac='Fresh HMAC-SHA256 verified before decryption';HistoricalBackupVerifiedFlagUsed=$false;RestorePort=$state.Port;IsolatedProcessStillRunning=($state.Process -and -not $state.Process.HasExited);ArtifactsPreserved=$true;WorkingDirectory=$state.Run;MySQL80IdentityVerified=($state.MySql80PostCheck -eq 'PASS');MySQL80ProcessId=if($state.MySql80Identity){$state.MySql80Identity.ProcessId}else{$null};MySQL80PostCheck=$state.MySql80PostCheck}
        if($state.Inventory){$manifest.RestoredSchema='hrms_web1';$manifest.TableInventory=@($state.Inventory.Trim() -split "`r?`n" | ForEach-Object {$f=$_ -split "`t";[pscustomobject]@{Name=$f[0];Engine=$f[-1]}});$manifest.RestoredRowCounts=@($state.RestoredRowCounts);$manifest.IsolatedProcessIdentityVerified=$true;$manifest.SocketOwnerCorrelationVerified=$true}
        $manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $state.Run 'restore-only-verification.json') -Encoding utf8
    }.GetNewClosure()
    $ops.CleanupLogin={param($login) if($login){Remove-MySqlLoginFile $login}}.GetNewClosure()
    $ops.ClearKeys={param($state) if($state.Verified){if($state.Verified.EncKey){[Array]::Clear($state.Verified.EncKey,0,$state.Verified.EncKey.Length)};if($state.Verified.MacKey){[Array]::Clear($state.Verified.MacKey,0,$state.Verified.MacKey.Length)}};$state.RestorePassword=$null}.GetNewClosure()
    return $ops
}

if($env:HRMS_RESTORE_ONLY_TEST_LIBRARY -eq '1'){return}

if($InspectIsolatedInstance -and $ShutdownIsolatedInstance){throw 'Select only one isolated-instance diagnostic action.'}

if($ShutdownIsolatedInstance){
    $expectedExe=Join-Path $MysqlBin 'mysqld.exe';$mysqlExe=Join-Path $MysqlBin 'mysql.exe';$mysqlAdmin=Join-Path $MysqlBin 'mysqladmin.exe'
    $identity=Get-SanitizedIsolatedProcessDiagnostic $TargetPid $ExpectedProcessStartIdentity $ExpectedDataDir $ExpectedPort $expectedExe $mysqlExe $ExpectedServerUuid
    if($identity.Result -ne 'PASS'){$identity|ConvertTo-Json -Depth 5;return}
    $normalBefore=@();$normalBeforeValid=$true
    foreach($protectedPid in @(7380,8724)){
        try{$protected=Get-Process -Id $protectedPid -ErrorAction Stop;$normalBefore+= [pscustomobject]@{PID=$protectedPid;Present=$true;Name=$protected.ProcessName}}
        catch{$normalBefore+= [pscustomobject]@{PID=$protectedPid;Present=$false;Name=$null};$normalBeforeValid=$false}
    }
    try{$serviceBefore=Get-Service -Name 'MySQL80' -ErrorAction Stop;$serviceBeforeRunning=($serviceBefore.Status -eq 'Running')}catch{$serviceBeforeRunning=$false}
    if(-not $normalBeforeValid -or -not $serviceBeforeRunning){
        [pscustomobject]@{Result='BLOCKED';Category='shutdown.protected_mysql_precheck_failed';IsolatedDiagnostic=$identity.Category;ProtectedProcesses=$normalBefore;MySQL80Running=$serviceBeforeRunning;ShutdownRequested=$false}|ConvertTo-Json -Depth 4
        return
    }
    $login=$null;$shutdownStage='protected login file creation'
    try {
        $login=New-MySqlLoginFile 'root' '' $ExpectedPort $false
        $shutdownStage='identity-revalidated graceful shutdown request'
        $process=[Diagnostics.Process]::GetProcessById($TargetPid)
        [void](Stop-IsolatedMysqlGracefully $process $expectedExe $ExpectedDataDir $ExpectedPort $mysqlAdmin $login.Path $ExpectedProcessStartIdentity $mysqlExe $ExpectedServerUuid)
        $shutdownStage='post-shutdown process and port verification'
        $isolatedStillRunning=$true
        try{$null=Get-Process -Id $TargetPid -ErrorAction Stop}catch{
            if($_.CategoryInfo.Category -eq 'ObjectNotFound' -or $_.FullyQualifiedErrorId -match 'NoProcessFound'){$isolatedStillRunning=$false}
            else{throw 'Post-shutdown process query failed; process exit could not be verified.'}
        }
        $tcpAfter=@();$tcpQuerySucceeded=$false;$tcpQueryDiagnostic=$null
        try{$tcpAfter=@(Get-NetTCPConnection -LocalPort $ExpectedPort -ErrorAction Stop);$tcpQuerySucceeded=$true}catch{
            if($_.FullyQualifiedErrorId -match 'CmdletizationQuery_NotFound'){$tcpAfter=@();$tcpQuerySucceeded=$true}
            else{$tcpQueryDiagnostic='{0}; HRESULT 0x{1:X8}' -f $_.Exception.GetType().FullName,$_.Exception.HResult}
        }
        $netstatAfter=@();$netstatQuerySucceeded=$false
        try{$netstatExe=Join-Path $env:SystemRoot 'System32\netstat.exe';$allNetstat=@(& $netstatExe -ano -p tcp 2>$null);$netstatQuerySucceeded=($LASTEXITCODE -eq 0)
            if($netstatQuerySucceeded){$netstatAfter=@($allNetstat | Where-Object {if($_ -match '^\s*TCP\s+(\S+)\s+\S+\s+(\S+)\s+(\S+)\s+(\d+)\s*$'){$localText=$Matches[1];$stateText=$Matches[3];$portText=$localText.Substring($localText.LastIndexOf(':')+1).TrimEnd(']');$portValue=0;[int]::TryParse($portText,[ref]$portValue) -and $portValue -eq $ExpectedPort}else{$false}})}
        }catch{}
        $normalAfter=@();$normalAfterValid=$true
        foreach($protectedPid in @(7380,8724)){
            try{$protected=Get-Process -Id $protectedPid -ErrorAction Stop;$normalAfter+= [pscustomobject]@{PID=$protectedPid;Present=$true;Name=$protected.ProcessName}}
            catch{$normalAfter+= [pscustomobject]@{PID=$protectedPid;Present=$false;Name=$null};$normalAfterValid=$false}
        }
        try{$serviceAfter=Get-Service -Name 'MySQL80' -ErrorAction Stop;$serviceAfterRunning=($serviceAfter.Status -eq 'Running')}catch{$serviceAfterRunning=$false}
        $portFree=($tcpQuerySucceeded -and $tcpAfter.Count -eq 0 -and $netstatQuerySucceeded -and $netstatAfter.Count -eq 0)
        if($isolatedStillRunning -or -not $portFree -or -not $normalAfterValid -or -not $serviceAfterRunning){
            [pscustomobject]@{Result='BLOCKED';Category='shutdown.postcondition_failed';Stage=$shutdownStage;IsolatedPIDPresent=$isolatedStillRunning;Port=$ExpectedPort;PortFree=$portFree;TcpQueryDiagnostic=$tcpQueryDiagnostic;NetstatQuerySucceeded=$netstatQuerySucceeded;ProtectedProcesses=$normalAfter;MySQL80Running=$serviceAfterRunning;GracefulShutdownRequested=$true;ForceTerminationAttempted=$false}|ConvertTo-Json -Depth 4
            return
        }
        [pscustomobject]@{Result='PASS';Category='shutdown.graceful_shutdown_confirmed';PID=$TargetPid;ProcessStartIdentity=$ExpectedProcessStartIdentity;Endpoint='127.0.0.1:34060';Datadir=$ExpectedDataDir;ServerUuid=$ExpectedServerUuid;IsolatedPIDPresent=$false;PortFree=$true;ProtectedProcesses=$normalAfter;MySQL80Running=$true;GracefulShutdownRequested=$true;ForceTerminationAttempted=$false;ArtifactsPreserved=$true}|ConvertTo-Json -Depth 4
    } catch {
        $failureCategory=if($script:LastRestoreDiagnostic){$script:LastRestoreDiagnostic}else{'shutdown.graceful_shutdown_failed'}
        [pscustomobject]@{Result='BLOCKED';Category=$failureCategory;Stage=$shutdownStage;IsolatedPIDPresent=$true;Port=$ExpectedPort;GracefulShutdownRequested=($shutdownStage -eq 'post-shutdown process and port verification');ForceTerminationAttempted=$false;ArtifactsPreserved=$true;ExceptionType=$_.Exception.GetType().FullName}|ConvertTo-Json -Depth 4
    } finally {if($login){Remove-MySqlLoginFile $login}}
    return
}

if($InspectIsolatedInstance){
    $expectedExe=Join-Path $MysqlBin 'mysqld.exe'
    $mysqlExe=Join-Path $MysqlBin 'mysql.exe'
    $inspection=Get-SanitizedIsolatedProcessDiagnostic $TargetPid $ExpectedProcessStartIdentity $ExpectedDataDir $ExpectedPort $expectedExe $mysqlExe $ExpectedServerUuid
    $inspection | ConvertTo-Json -Depth 5
    return
}

if($SelfTest){
    $passed=0
    Reset-HrmsMySqlInvocationState
    if($null -ne (Get-HrmsMySqlFailureState) -or $script:LastMySqlArgumentNames.Count -ne 0 -or $null -ne $script:LastMySqlLaunchMetadata){throw 'no-client-invocation diagnostic state test failed'};$passed++
    Reset-HrmsMySqlInvocationState;$script:LastMySqlLaunchMetadata=[pscustomobject]@{CredentialOptionFileUsed=$true;DefaultsOptionFirst=$true;ArgumentNames=@('--defaults-file','--ssl-mode')}
    if($null -ne (Get-HrmsMySqlFailureState)){throw 'successful-client invocation must clear prior failure state'};$passed++
    Reset-HrmsMySqlInvocationState;$script:LastMySqlFailure=Resolve-SanitizedMySqlClientFailure 1 'ERROR 1045 (28000): Access denied for user'
    if((Get-HrmsMySqlFailureState).ErrorNumber -ne 1045){throw 'failed-client invocation diagnostic state test failed'};$passed++
    Reset-HrmsMySqlInvocationState;$script:LastBackupDiagnosticCategory=$null;$script:CurrentCatalogStage=$null
    if((Resolve-HrmsMySqlFailureCategory 'catalog route verification') -ne 'source.catalog_connection_failed' -or $null -ne (Get-HrmsMySqlFailureState)){throw 'early catalog failure stale-diagnostic test failed'};$passed++
    Remove-Variable -Name LastMySqlFailure -Scope Script -Force
    try{throw 'synthetic early exception'}catch{if($null -ne (Get-HrmsMySqlFailureState)){throw 'exception before diagnostic initialization test failed'}};$passed++
    Reset-HrmsMySqlInvocationState;$script:LastMySqlFailure=Resolve-SanitizedMySqlClientFailure 1 'ERROR 1045 (28000): Access denied for user';Reset-HrmsMySqlInvocationState
    if($null -ne (Get-HrmsMySqlFailureState)){throw 'sequential success did not clear previous failure'}
    $script:LastMySqlFailure=Resolve-SanitizedMySqlClientFailure 1 'ERROR 2013 (HY000): Lost connection';if((Get-HrmsMySqlFailureState).ErrorNumber -ne 2013 -or (Get-HrmsMySqlFailureState).Category -ne 'mysql.transport_failed'){throw 'sequential client failure diagnostics leaked or were stale'};$passed++
    Reset-HrmsMySqlInvocationState
    Assert-CatalogRoute "WEB1`tweb1.localhost`tweb1`tMySql`t1" | Out-Null;$passed++
    try{Assert-CatalogRoute "OTHER`tweb1.localhost`tother`tMySql`t1"|Out-Null;throw 'wrong-tenant test failed'}catch{if($_.Exception.Message -eq 'wrong-tenant test failed'){throw};$passed++}
    Assert-InnoDbOnly "Employees`tInnoDB`nTenants`tInnoDB`nSomeView`tVIEW"|Out-Null;$passed++
    try{Assert-InnoDbOnly "Employees`tInnoDB`nLegacy`tMyISAM"|Out-Null;throw 'non-InnoDB test failed'}catch{if($_.Exception.Message -eq 'non-InnoDB test failed'){throw};$passed++}
    Assert-BackupCapacity 25800000 1073741824 1073741824|Out-Null;$passed++
    try{Assert-BackupCapacity 25800000 1 1|Out-Null;throw 'capacity test failed'}catch{if($_.Exception.Message -eq 'capacity test failed'){throw};$passed++}
    [void](Test-RestoreServerIdentity "D:\\isolated`t34061" 'D:\isolated' 34061);$passed++
    try{[void](Test-RestoreServerIdentity "D:\\isolated`t3306" 'D:\isolated' 3306);throw 'live-port restore test failed'}catch{if($_.Exception.Message -eq 'live-port restore test failed'){throw};$passed++}
    $serverArgs=Get-IsolatedServerArguments 'C:\mysql' 'D:\restore' 34060 'D:\restore\restore.log'
    if($serverArgs -notcontains '--no-monitor' -or $serverArgs -notcontains '--no-defaults' -or $serverArgs -notcontains '--port=34060' -or $serverArgs -contains '--port=3306'){throw 'isolated server launch arguments test failed'};$passed++
    $cimListen=@([pscustomobject]@{State='Listen';OwningProcess=49908})
    $netListen=@('  TCP    127.0.0.1:34060    0.0.0.0:0    LISTENING    49908')
    $resolved=Resolve-IsolatedListenerOwnership $cimListen $netListen 49908 34060 $true $true
    if(-not $resolved.Valid -or $resolved.OwnerPID -ne 49908){throw 'dual-source listener ownership test failed'};$passed++
    $unknownState=@([pscustomobject]@{State='0';OwningProcess=49908})
    $resolved=Resolve-IsolatedListenerOwnership $unknownState $netListen 49908 34060 $true $true
    if(-not $resolved.Valid -or $resolved.Source -ne 'netstat'){throw 'netstat fallback for unknown CIM state test failed'};$passed++
    $resolved=Resolve-IsolatedListenerOwnership $unknownState @('  TCP    127.0.0.1:53248    127.0.0.1:34060    TIME_WAIT    0') 49908 34060 $true $true
    if($resolved.Valid -or $resolved.Category -ne 'restore.listener_state_unverified'){throw 'unknown socket state fail-closed test failed'};$passed++
    $resolved=Resolve-IsolatedListenerOwnership @() @('  TCP    127.0.0.1:34060    0.0.0.0:0    LISTENING    51000') 49908 34060 $true $true
    if($resolved.Valid -or $resolved.Category -ne 'restore.listener_owner_mismatch'){throw 'wrong listener owner test failed'};$passed++
    $twoOwners=@('  TCP    127.0.0.1:34060    0.0.0.0:0    LISTENING    49908','  TCP    127.0.0.1:34060    0.0.0.0:0    LISTENING    51000')
    $resolved=Resolve-IsolatedListenerOwnership @() $twoOwners 49908 34060 $true $true
    if($resolved.Valid -or $resolved.Category -ne 'restore.listener_multiple_owners'){throw 'multiple listener owners test failed'};$passed++
    $resolved=Resolve-IsolatedListenerOwnership @([pscustomobject]@{State='Listen';OwningProcess=49908}) @('  TCP    127.0.0.1:34060    0.0.0.0:0    LISTENING    51000') 49908 34060 $true $true
    if($resolved.Valid -or $resolved.Category -ne 'restore.listener_snapshot_mismatch'){throw 'conflicting listener snapshots test failed'};$passed++
    $resolved=Resolve-IsolatedListenerOwnership @() @() 49908 34060 $true $true
    if($resolved.Valid -or $resolved.Category -ne 'restore.listener_not_found'){throw 'no listener test failed'};$passed++
    $resolved=Resolve-IsolatedListenerOwnership @() @() 49908 34060 $false $false
    if($resolved.Valid -or $resolved.Category -ne 'restore.listener_query_failed'){throw 'listener query failure test failed'};$passed++
    $challenge='Q0hBTExFTkdF';$serverUuid='123e4567-e89b-42d3-a456-426614174000'
    $validOutput="D:\\restore`t34060`t$serverUuid`t8.0.46`t127.0.0.1`t0`tOFF`t0`t$challenge"
    $identity=Resolve-MySqlProtocolIdentityOutput $validOutput 'D:\restore' 34060 $serverUuid $challenge
    if(-not $identity.Valid -or -not $identity.ChallengeValid){throw 'valid MySQL protocol identity test failed'};$passed++
    if((Resolve-MySqlProtocolIdentityOutput $validOutput 'D:\other' 34060 $serverUuid $challenge).Category -ne 'restore.protocol_datadir_mismatch'){throw 'mismatched datadir test failed'};$passed++
    $wrongPort=$validOutput.Replace("`t34060`t","`t34061`t")
    if((Resolve-MySqlProtocolIdentityOutput $wrongPort 'D:\restore' 34060 $serverUuid $challenge).Category -ne 'restore.protocol_port_mismatch'){throw 'mismatched protocol port test failed'};$passed++
    if((Resolve-MySqlProtocolIdentityOutput $validOutput 'D:\restore' 34060 '123e4567-e89b-42d3-a456-426614174001' $challenge).Category -ne 'restore.protocol_server_uuid_mismatch'){throw 'mismatched UUID test failed'};$passed++
    if((Resolve-MySqlProtocolIdentityOutput $validOutput 'D:\restore' 34060 $serverUuid 'OTHER').Category -ne 'restore.protocol_runtime_identity_mismatch'){throw 'protocol challenge mismatch test failed'};$passed++
    $wrongVersion=$validOutput.Replace('8.0.46','8.0.45')
    if((Resolve-MySqlProtocolIdentityOutput $wrongVersion 'D:\restore' 34060 $serverUuid $challenge).Category -ne 'restore.protocol_runtime_identity_mismatch'){throw 'mismatched MySQL version test failed'};$passed++
    $unknownSocket=[pscustomobject]@{LocalPort=34060;State='';OwningProcess=49908;RemoteAddress='0.0.0.0';RemotePort=0}
    $unknownSnapshot=[pscustomobject]@{CimConnections=@($unknownSocket);NetstatLines=@();CimSucceeded=$true;NetstatSucceeded=$true}
    $correlation=Resolve-IsolatedSocketIdentityCorrelation $unknownSnapshot $unknownSnapshot 49908 34060 $true $true
    if(-not $correlation.Valid -or $correlation.Category -ne 'restore.socket_owner_protocol_identity_verified'){throw 'unknown-state composite identity correlation test failed'};$passed++
    $establishedShape=[pscustomobject]@{LocalPort=34060;State='';OwningProcess=49908;RemoteAddress='127.0.0.1';RemotePort=44000}
    $establishedSnapshot=[pscustomobject]@{CimConnections=@($establishedShape);NetstatLines=@();CimSucceeded=$true;NetstatSucceeded=$true}
    if((Resolve-IsolatedSocketIdentityCorrelation $establishedSnapshot $establishedSnapshot 49908 34060 $true $true).Category -ne 'restore.listener_state_unverified'){throw 'unknown established-socket shape must not pass'};$passed++
    $wrongOwnerSnapshot=[pscustomobject]@{CimConnections=@([pscustomobject]@{LocalPort=34060;State='';OwningProcess=51000;RemoteAddress='0.0.0.0';RemotePort=0});NetstatLines=@();CimSucceeded=$true;NetstatSucceeded=$true}
    if((Resolve-IsolatedSocketIdentityCorrelation $wrongOwnerSnapshot $wrongOwnerSnapshot 49908 34060 $true $true).Category -ne 'restore.listener_owner_mismatch'){throw 'unknown socket wrong-owner test failed'};$passed++
    if((Resolve-IsolatedSocketIdentityCorrelation $unknownSnapshot $unknownSnapshot 49908 34060 $true $false).Category -ne 'restore.protocol_identity_unverified'){throw 'missing protocol identity fail-closed test failed'};$passed++
    if((Resolve-IsolatedSocketIdentityCorrelation $unknownSnapshot $unknownSnapshot 49908 34060 $false $true).Category -ne 'restore.process_identity_unverified'){throw 'process-query failure fail-closed test failed'};$passed++
    $partialMetadata=Resolve-ProcessMetadataAvailability $true $false 'Microsoft.Management.Infrastructure.CimException' '0x80041003'
    if(-not $partialMetadata.Valid -or $partialMetadata.Metadata -ne 'partial' -or $partialMetadata.Category -ne 'diagnostic.process_metadata_partial' -or $partialMetadata.Diagnostic -notmatch '0x80041003'){throw 'sanitized partial process metadata test failed'};$passed++
    if((Resolve-ProcessMetadataAvailability $false $false 'Microsoft.Management.Infrastructure.CimException' '0x80041003').Valid){throw 'unverified process identity must not pass metadata fallback'};$passed++
    $competingSnapshot=[pscustomobject]@{CimConnections=@($unknownSocket,[pscustomobject]@{LocalPort=34060;State='';OwningProcess=51000;RemoteAddress='0.0.0.0';RemotePort=0});NetstatLines=@();CimSucceeded=$true;NetstatSucceeded=$true}
    if((Resolve-IsolatedSocketIdentityCorrelation $competingSnapshot $competingSnapshot 49908 34060 $true $true).Category -ne 'restore.listener_multiple_owners'){throw 'competing socket owner fail-closed test failed'};$passed++
    $changedSnapshot=[pscustomobject]@{CimConnections=@([pscustomobject]@{LocalPort=34060;State='0';OwningProcess=49908;RemoteAddress='0.0.0.0';RemotePort=0});NetstatLines=@();CimSucceeded=$true;NetstatSucceeded=$true}
    if((Resolve-IsolatedSocketIdentityCorrelation $unknownSnapshot $changedSnapshot 49908 34060 $true $true).Category -ne 'restore.listener_snapshot_mismatch'){throw 'socket snapshot identity change test failed'};$passed++
    $unexpectedPortSnapshot=[pscustomobject]@{CimConnections=@($unknownSocket);ProcessConnections=@($unknownSocket,[pscustomobject]@{LocalPort=34061;State='Listen';OwningProcess=49908;RemoteAddress='0.0.0.0';RemotePort=0});NetstatLines=@();CimSucceeded=$true;NetstatSucceeded=$true}
    if((Resolve-IsolatedSocketIdentityCorrelation $unexpectedPortSnapshot $unexpectedPortSnapshot 49908 34060 $true $true).Category -ne 'restore.unexpected_listener_port'){throw 'unexpected process listener port test failed'};$passed++
    if((Get-SanitizedIsolatedProcessDiagnostic 0 1 'D:\restore' 34060 'C:\mysql\mysqld.exe' 'C:\mysql\mysql.exe' $serverUuid).Category -ne 'diagnostic.arguments_invalid'){throw 'read-only diagnostic argument validation test failed'};$passed++
    if((Get-SanitizedIsolatedProcessDiagnostic 2147483647 1 'D:\restore' 34060 'C:\mysql\mysqld.exe' 'C:\mysql\mysql.exe' $serverUuid).Category -ne 'restore.process_not_running'){throw 'read-only diagnostic missing-process test failed'};$passed++
    if((Get-SafeMySqlErrorSummary 'ERROR 1045 (28000): Access denied for user').Category -ne 'authentication rejected'){throw 'invalid-credential classification test failed'};$passed++
    $fakeLogin=$null
    $credentialFixtureSkipped=$false
    try {$fakeLogin=New-MySqlLoginFile 'selftest-user' 'selftest-password-value' 3306 $true $TlsCaPath}
    catch {$credentialFixtureSkipped=$true;$credentialFixtureFailureType=$_.Exception.GetType().FullName;$credentialFixtureFailureStage=$script:CredentialAclFailureStage;$credentialFixtureDetails=$script:CredentialAclFailureDetails}
    if($fakeLogin){try {
        $fakeText=[IO.File]::ReadAllText($fakeLogin.Path)
        if($fakeText -notmatch 'password="selftest-password-value"' -or $fakeText -notmatch 'ssl-mode=VERIFY_IDENTITY' -or $fakeText -notmatch 'ssl-ca='){throw 'protected credential file test failed'}
        $fakePsi=[Diagnostics.ProcessStartInfo]::new();Add-ProcessArgument $fakePsi "--defaults-file=$($fakeLogin.Path)"
        if($fakePsi.Arguments -match 'selftest-password-value'){throw 'credential command-line exposure test failed'}
        $passed++
    } finally {Remove-MySqlLoginFile $fakeLogin}}
    $ownerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $positiveAcl=New-RestrictedAcl $true $ownerSid;$positiveAcl.SetOwner([Security.Principal.SecurityIdentifier]::new($ownerSid));Assert-RestrictiveAcl $positiveAcl $ownerSid $true;$passed++
    $inheritedAcl=New-RestrictedAcl $true $ownerSid;$inheritedAcl.SetOwner([Security.Principal.SecurityIdentifier]::new($ownerSid));$inheritedAcl.SetAccessRuleProtection($false,$true)
    try{Assert-RestrictiveAcl $inheritedAcl $ownerSid $true;throw 'inherited ACL negative test failed'}catch{if($_.Exception.Message -eq 'inherited ACL negative test failed'){throw};$passed++}
    $broadAcl=New-RestrictedAcl $false $ownerSid;$broadAcl.SetOwner([Security.Principal.SecurityIdentifier]::new($ownerSid))
    $broadRule=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-1-0'),[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow)
    [void]$broadAcl.AddAccessRule($broadRule)
    try{Assert-RestrictiveAcl $broadAcl $ownerSid $false;throw 'broad ACL negative test failed'}catch{if($_.Exception.Message -eq 'broad ACL negative test failed'){throw};$passed++}
    $unexpectedAcl=New-RestrictedAcl $false $ownerSid;$unexpectedAcl.SetOwner([Security.Principal.SecurityIdentifier]::new($ownerSid))
    $unexpectedRule=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow)
    [void]$unexpectedAcl.AddAccessRule($unexpectedRule)
    try{Assert-RestrictiveAcl $unexpectedAcl $ownerSid $false;throw 'unexpected principal ACL negative test failed'}catch{if($_.Exception.Message -eq 'unexpected principal ACL negative test failed'){throw};$passed++}
    $denyAcl=New-RestrictedAcl $false $ownerSid;$denyAcl.SetOwner([Security.Principal.SecurityIdentifier]::new($ownerSid))
    [void]$denyAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($ownerSid),[Security.AccessControl.FileSystemRights]::Read,[Security.AccessControl.AccessControlType]::Deny))
    try{Assert-RestrictiveAcl $denyAcl $ownerSid $false;throw 'deny ACL negative test failed'}catch{if($_.Exception.Message -eq 'deny ACL negative test failed'){throw};$passed++}
    try{Assert-RestrictiveAcl $null $ownerSid $false;throw 'ACL inspection failure test failed'}catch{if($_.Exception.Message -eq 'ACL inspection failure test failed'){throw};$passed++}
    $missingAclPath=Join-Path ([IO.Path]::GetTempPath()) ('hrms-acl-missing-'+[guid]::NewGuid().ToString('N'))
    try{[void](Read-VerifiedAcl $missingAclPath $ownerSid $false);throw 'ACL readback failure test failed'}catch{if($_.Exception.Message -eq 'ACL readback failure test failed'){throw};$passed++}
    try{Assert-SnapshotRowCounts @('10','20') @('10','21');throw 'snapshot mismatch test failed'}catch{if($_.Exception.Message -eq 'snapshot mismatch test failed'){throw};$passed++}
    try{Assert-SnapshotRowCounts @('10') @('10','11');throw 'snapshot manifest shape test failed'}catch{if($_.Exception.Message -eq 'snapshot manifest shape test failed'){throw};$passed++}
    Assert-SnapshotRowCounts @('10','20') @('10','20');$passed++
    Assert-RestoredRowCounts @('Employees','Departments') @('100','4');$passed++
    try{Assert-RestoredRowCounts @('Employees','Departments') @('100');throw 'restore row-count inventory mismatch test failed'}catch{if($_.Exception.Message -eq 'restore row-count inventory mismatch test failed'){throw};$passed++}
    try{Assert-RestoredRowCounts @('Employees') @('invalid');throw 'restore row-count parse failure test failed'}catch{if($_.Exception.Message -eq 'restore row-count parse failure test failed'){throw};$passed++}
    try{Assert-SnapshotRowCounts @('100','4') @('100','5');throw 'snapshot row-count mismatch test failed'}catch{if($_.Exception.Message -eq 'snapshot row-count mismatch test failed'){throw};$passed++}
    if($credentialFixtureSkipped){$reason=if($credentialFixtureDetails -and $credentialFixtureDetails.PSObject.Properties['Reason']){$credentialFixtureDetails.Reason}else{$null};$detail=if($credentialFixtureDetails){"$($credentialFixtureDetails.ExceptionType); $($credentialFixtureDetails.ErrorId); HRESULT $($credentialFixtureDetails.HResult); $($credentialFixtureDetails.Category); $reason"}else{$credentialFixtureFailureType};throw "Credential ACL security fixture could not be created and verified ($credentialFixtureFailureStage; $detail); $passed non-filesystem helper assertions completed; security tests are BLOCKED."}
    try{Assert-SnapshotRowCounts @('100') @('100','4');throw 'snapshot manifest shape failure test failed'}catch{if($_.Exception.Message -eq 'snapshot manifest shape failure test failed'){throw};$passed++}
    $fixtureDb=ConvertFrom-HrmsMySqlConnectionString 'Server=127.0.0.1;Port=3306;Database=hrms_catalog;User ID=probe;Password=fixture' 'fixture'
    if($fixtureDb.Database -ne 'hrms_catalog' -or $fixtureDb.User -ne 'probe'){throw 'connection-string parser test failed'};$passed++
    try{$null=ConvertFrom-HrmsMySqlConnectionString 'Server=other;Port=3306;Database=hrms_catalog;User ID=probe;Password=fixture' 'fixture';throw 'unexpected endpoint test failed'}catch{if($_.Exception.Message -eq 'unexpected endpoint test failed'){throw};$passed++}
    $authFailure=Resolve-SanitizedMySqlClientFailure 1 "ERROR 1045 (28000): Access denied for user 'probe' (using password: YES)"
    if($authFailure.Category -ne 'mysql.authentication_rejected' -or $authFailure.ErrorNumber -ne 1045 -or -not $authFailure.ErrorTextWithheld){throw 'sanitized authentication diagnostic test failed'};$passed++
    $tlsFailure=Resolve-SanitizedMySqlClientFailure 1 'ERROR 2026 (HY000): SSL connection error'
    if($tlsFailure.Category -ne 'mysql.tls_failed' -or $tlsFailure.ErrorNumber -ne 2026){throw 'sanitized TLS diagnostic test failed'};$passed++
    $optionPsi=[Diagnostics.ProcessStartInfo]::new();Add-ProcessArgument $optionPsi '--defaults-file=C:\temp path\client.cnf';Add-ProcessArgument $optionPsi '--ssl-mode=VERIFY_IDENTITY'
    $argumentListProperty=$optionPsi.GetType().GetProperty('ArgumentList')
    if($argumentListProperty){$testArgumentList=@($optionPsi.ArgumentList);$firstArgument=$testArgumentList[0];$joinedArguments=$testArgumentList -join ' '}else{$firstArgument=($optionPsi.Arguments -replace '^"','') -split '\s+',2 | Select-Object -First 1;$joinedArguments=$optionPsi.Arguments}
    if(-not $firstArgument.StartsWith('--defaults-file=',[StringComparison]::Ordinal) -or $joinedArguments.IndexOf('--ssl-mode=VERIFY_IDENTITY',[StringComparison]::Ordinal) -lt 0){throw 'MySQL defaults-file argument ordering test failed'};$passed++
    Write-Output "PASS: $passed focused fail-closed helper checks. No database or process access performed."
    return
}
if($ReadOnlyConfigurationCheck){
    $catalogLogin=$null;$tenantLogin=$null;$stage='configuration resolution';$resolutionStage='configuration loading';$configurationSource=$null
    try{
        $databaseConfiguration=Get-HrmsDevelopmentDatabaseConfiguration
        $resolutionStage='TLS CA validation'
        $script:ApprovedTlsCaPath=Resolve-HrmsTlsCaPath $TlsCaPath
        $resolutionStage='catalog credential protection'
        $catalogLogin=New-MySqlLoginFile $databaseConfiguration.Catalog.User $databaseConfiguration.Catalog.Password $databaseConfiguration.Catalog.Port $true
        $resolutionStage='tenant credential protection'
        $tenantLogin=New-MySqlLoginFile $databaseConfiguration.Tenant.User $databaseConfiguration.Tenant.Password $databaseConfiguration.Tenant.Port $true
        $configurationSource=$databaseConfiguration.Source;$databaseConfiguration=$null
        $resolutionStage='MySQL client dependency'
        $mysql=Join-Path $MysqlBin 'mysql.exe';if(-not(Test-Path -LiteralPath $mysql)){$script:LastBackupDiagnosticCategory='configuration.mysql_client_missing';throw 'Configured MySQL client is unavailable.'}
        $resolutionStage=$null
        $catalogCheck=Invoke-HrmsCatalogRouteCheck $mysql $catalogLogin.Path
        $catalogIdentityParts=$catalogCheck.Identity
        $catalogLaunchMetadata=$script:LastMySqlLaunchMetadata
        $stage='tenant authentication and identity'
        $identity=Invoke-Client $mysql @('--batch','--skip-column-names') $tenantLogin.Path 'SELECT DATABASE(),@@port,@@version,@@server_uuid;' -Database 'hrms_web1' -RequireTls
        $tenantIdentity=ConvertFrom-SourceIdentity $identity 'hrms_web1' '3306'
        if($tenantIdentity[3] -ne $catalogIdentityParts[3]){$script:LastBackupDiagnosticCategory='source.catalog_tenant_instance_mismatch';throw 'Catalog and WEB1 tenant resolved to different MySQL instances.'}
        $tls=Invoke-Client $mysql @('--batch','--skip-column-names') $tenantLogin.Path "SHOW SESSION STATUS LIKE 'Ssl_cipher';" -Database 'hrms_web1' -RequireTls
        $tlsParts=$tls.Trim() -split "`t"
        if($tlsParts.Count -lt 2 -or $tlsParts[0] -ne 'Ssl_cipher' -or [string]::IsNullOrWhiteSpace($tlsParts[1])){$script:LastBackupDiagnosticCategory='source.tls_not_verified';throw 'TLS verification failed for WEB1 tenant connection.'}
        [pscustomobject]@{Result='PASS';Category='source.read_only_configuration_verified';ConfigurationSource=$configurationSource;TenantCode='WEB1';Host='web1.localhost';Shard='web1';Provider='MySql';Catalog='hrms_catalog';TenantDatabase='hrms_web1';Port=3306;ServerVersion=$tenantIdentity[2];CatalogAndTenantSameInstance=$true;TlsVerified=$true;CatalogCredentialOptionFileUsed=$catalogLaunchMetadata.CredentialOptionFileUsed;CatalogDefaultsOptionFirst=$catalogLaunchMetadata.DefaultsOptionFirst;DatabaseWritesPerformed=$false}|ConvertTo-Json -Compress
    }catch{
        Initialize-HrmsDiagnosticState
        $mySqlFailure=Get-HrmsMySqlFailureState
        $category=$script:LastBackupDiagnosticCategory
        if(-not $category -and $resolutionStage){
            if($script:CredentialAclFailureDetails){$category='configuration.credential_protection_failed'}
            elseif($resolutionStage -eq 'MySQL client dependency'){$category='configuration.runtime_dependency_failed'}
            elseif($resolutionStage -eq 'configuration loading'){
                $category=if($_.Exception -is [System.Management.Automation.ParameterBindingException] -or $_.Exception -is [System.ArgumentException]){'configuration.invalid'}else{'configuration.resolution_failed'}
            }else{$category='configuration.credential_protection_failed'}
        }
        if(-not $category){$category=Resolve-HrmsMySqlFailureCategory $stage}
        if($script:CurrentCatalogStage){$stage=$script:CurrentCatalogStage}
        $aclDiagnostic=$script:CredentialAclFailureDetails
        [pscustomobject]@{Result='BLOCKED';Category=$category;Stage=if($aclDiagnostic){$aclDiagnostic.Stage}elseif($resolutionStage){$resolutionStage}else{$stage};CredentialProtectionDiagnosticVersion='acl-substage-v2-owner-sid';CredentialFileOwnerIdentity=$script:CredentialFileOwnerIdentity;CredentialCleanupStatus=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['CleanupStatus']){$aclDiagnostic.CleanupStatus}else{$null};ExceptionType=if($aclDiagnostic){$aclDiagnostic.ExceptionType}else{$_.Exception.GetType().FullName};InnerExceptionType=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['InnerExceptionType']){$aclDiagnostic.InnerExceptionType}else{$null};ErrorId=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['ErrorId']){$aclDiagnostic.ErrorId}else{$null};FailureScriptLine=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['ScriptLineNumber']){$aclDiagnostic.ScriptLineNumber}else{$null};HResult=if($aclDiagnostic){$aclDiagnostic.HResult}else{('0x{0:X8}' -f $_.Exception.HResult)};NativeStatus=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['NativeStatus']){$aclDiagnostic.NativeStatus}else{$null};InnerHResult=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['InnerHResult']){$aclDiagnostic.InnerHResult}else{$null};Win32ErrorCode=if($aclDiagnostic -and $aclDiagnostic.PSObject.Properties['Win32ErrorCode']){$aclDiagnostic.Win32ErrorCode}elseif($aclDiagnostic){$null}else{(Get-SafeWin32ErrorCode $_.Exception)};CredentialProtectionCategory=if($aclDiagnostic){$aclDiagnostic.Category}else{$null};ClientExitCode=if($mySqlFailure){$mySqlFailure.ExitCode}else{$null};MySqlErrorNumber=if($mySqlFailure){$mySqlFailure.ErrorNumber}else{$null};ClientArgumentNames=if($script:LastMySqlLaunchMetadata){$script:LastMySqlLaunchMetadata.ArgumentNames}else{@()};ProtectedOptionFileUsed=if($script:LastMySqlLaunchMetadata){$script:LastMySqlLaunchMetadata.CredentialOptionFileUsed}else{$false};DefaultsOptionFirst=if($script:LastMySqlLaunchMetadata){$script:LastMySqlLaunchMetadata.DefaultsOptionFirst}else{$false};ErrorTextWithheld=$true;DatabaseWritesPerformed=$false}|ConvertTo-Json -Compress
    }finally{if($catalogLogin){Remove-MySqlLoginFile $catalogLogin};if($tenantLogin){Remove-MySqlLoginFile $tenantLogin}}
    return
}
if($SelfTestInvalidCredentials){
    $script:ApprovedTlsCaPath=Resolve-HrmsTlsCaPath $TlsCaPath
    $mysqlTest=Join-Path $MysqlBin 'mysql.exe'
    if(-not(Test-Path -LiteralPath $mysqlTest)){throw 'Invalid-credential test blocked: MySQL client is unavailable.'}
    $fakeUser='hrms_invalid_probe_'+[Guid]::NewGuid().ToString('N')
    try {
        [void](Invoke-Client $mysqlTest @('--batch','--skip-column-names') $null 'SELECT 1;' -User $fakeUser -Password ([Guid]::NewGuid().ToString('N')) -Database 'hrms_catalog' -RequireTls)
        throw 'Invalid-credential test failed: unrecognized account authenticated.'
    } catch {
        if($script:LastMySqlDiagnostic -notmatch 'Authentication rejected'){throw 'Invalid-credential test blocked: rejection could not be safely classified.'}
    }
    Write-Output 'PASS: invalid credentials rejected before SQL execution; no data or schema was modified.'
    return
}

if($BackupOnly -and ($RestoreOnly -or $RestoreOnlyPreflight)){throw 'Select -BackupOnly separately from restore-only modes.'}
if($RestoreOnly -or $RestoreOnlyPreflight){
    if($RestoreOnly -and $RestoreOnlyPreflight){throw 'Select only one of -RestoreOnly or -RestoreOnlyPreflight.'}
    if([string]::IsNullOrWhiteSpace($EncryptedBackupPath)){throw 'Restore-only requires -EncryptedBackupPath.'}
    $ops=if($RestoreOnlyOperations){$RestoreOnlyOperations}else{New-HrmsRestoreOnlyProductionOperations $MysqlBin}
    if($RestoreOnlyPreflight){
        $script:HrmsPreflightReportEmitted=$false
        try {
            [void](Invoke-HrmsRestoreOnlyLifecycle -Ops $ops -ArtifactPath $EncryptedBackupPath -ExpectedArtifactPath 'D:\HRMS\.db-backups\web1-recovery\web1-20261102-213800\hrms_web1-20261102-213800.sql.aes' -ExpectedSha256 '005E067878085D211FAE02BA1B4993BF7F0F145A192EC7D97DF943A9A9AAA570' -EvidenceRoot $EvidenceRoot -PreflightOnly)
        } catch {
            if(-not $script:HrmsPreflightReportEmitted){
                $report=[pscustomobject]@{Result='BLOCKED';Mode='RestoreOnlyPreflight';FailureStage='artifact selection, pinned SHA-256, or secure passphrase validation';CleanupStatus='NOT_REQUIRED';Stages=@([pscustomobject]@{Stage='artifact selection, pinned SHA-256, or secure passphrase validation';Result='BLOCKED'});ErrorTextWithheld=$true;RuntimeOnly=@('isolated server startup and protocol identity','encrypted dump import and restored-data verification','graceful isolated-server shutdown')} | ConvertTo-Json -Depth 4 -Compress
                [Console]::Out.WriteLine($report);[Console]::Out.Flush()
            }
            exit 1
        }
        return
    }
    $state=Invoke-HrmsRestoreOnlyLifecycle -Ops $ops -ArtifactPath $EncryptedBackupPath -ExpectedArtifactPath 'D:\HRMS\.db-backups\web1-recovery\web1-20261102-213800\hrms_web1-20261102-213800.sql.aes' -ExpectedSha256 '005E067878085D211FAE02BA1B4993BF7F0F145A192EC7D97DF943A9A9AAA570' -EvidenceRoot $EvidenceRoot
    if($state.Cleanup -ne 'SUCCEEDED' -and $state.Process){throw 'Restore-only BLOCKED: identity-verified graceful cleanup was not confirmed; process and evidence were preserved.'}
    if($state.RestoreSucceeded){Write-Output "PASS: restore-only verification manifest: $($state.Run)"}
    return
}
# Safety gates run before the script creates evidence or starts an isolated server.
$mysql=Join-Path $MysqlBin 'mysql.exe';$dump=Join-Path $MysqlBin 'mysqldump.exe';$server=Join-Path $MysqlBin 'mysqld.exe';$mysqlAdmin=Join-Path $MysqlBin 'mysqladmin.exe'
$requiredTools=if($BackupOnly){@($mysql,$dump)}else{@($mysql,$dump,$server,$mysqlAdmin)}
foreach($tool in $requiredTools){if(-not(Test-Path -LiteralPath $tool)){throw "Required MySQL 8.0 tool is missing: $tool"}}
$script:ApprovedTlsCaPath=Resolve-HrmsTlsCaPath $TlsCaPath
$root=[IO.Path]::GetFullPath($EvidenceRoot);$restoreVolumePath=Join-Path $root 'isolated-restore-capacity-probe'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss';$run=Join-Path $root "web1-$stamp"
if(Test-Path -LiteralPath $run){throw 'Evidence path already exists; refusing overwrite.'}
$restoreData=Join-Path $run 'isolated-mysql-data';$backup=Join-Path $run "hrms_web1-$stamp.sql.aes";$serverLog=Join-Path $run 'restore-mysqld.log'
$mysqlProc=$null;$backupPass=$null;$restorePass=$null;$isolatedServerUuid=$null;$engineInfo=$null;$stage='catalog route verification';$script:LastMySqlDiagnostic=$null
$catalogLogin=$null;$sourceLogin=$null;$restoreLogin=$null;$isolatedPort=$null;$backupVerified=$false;$restoreVerified=$false;$script:LastRestoreDiagnostic=$null;$script:LastBackupDiagnosticCategory=$null
$apiBefore=Get-ApiProcessSnapshot
try {
    $databaseConfiguration=Get-HrmsDevelopmentDatabaseConfiguration
    $catalogLogin=New-MySqlLoginFile $databaseConfiguration.Catalog.User $databaseConfiguration.Catalog.Password $databaseConfiguration.Catalog.Port $true
    $sourceLogin=New-MySqlLoginFile $databaseConfiguration.Tenant.User $databaseConfiguration.Tenant.Password $databaseConfiguration.Tenant.Port $true
    $databaseConfiguration=$null

    # Route is checked through the same function as read-only diagnostic mode,
    # before opening hrms_web1.
    $catalogCheck=Invoke-HrmsCatalogRouteCheck $mysql $catalogLogin.Path
    $catalogIdentityParts=$catalogCheck.Identity

    $stage='tenant database identity verification'
    $identity=Invoke-Client $mysql @('--batch','--skip-column-names') $sourceLogin.Path 'SELECT DATABASE(),@@port,@@version,@@server_uuid;' -Database 'hrms_web1' -RequireTls
    $identityParts=ConvertFrom-SourceIdentity $identity 'hrms_web1' '3306'
    if($catalogIdentityParts[3] -ne $identityParts[3]){throw 'Source validation failed: catalog and tenant database resolved to different MySQL instances.'}
    $tlsStatus=Invoke-Client $mysql @('--batch','--skip-column-names') $sourceLogin.Path "SHOW SESSION STATUS LIKE 'Ssl_cipher';" -Database 'hrms_web1' -RequireTls
    $tlsParts=$tlsStatus.Trim() -split "`t"
    if($tlsParts.Count -lt 2 -or $tlsParts[0] -ne 'Ssl_cipher' -or [string]::IsNullOrWhiteSpace($tlsParts[1])){throw 'Source validation failed: TLS was not verified.'}

    $stage='storage engine and capacity validation'
    $engineInfo=Invoke-Client $mysql @('--batch','--skip-column-names') $sourceLogin.Path "SELECT TABLE_NAME,COALESCE(ENGINE,'VIEW') FROM information_schema.TABLES WHERE TABLE_SCHEMA='hrms_web1' ORDER BY TABLE_NAME;" -Database 'hrms_web1' -RequireTls
    $tableCount=Assert-InnoDbOnly $engineInfo
    if($tableCount -ne 299){throw 'Source validation failed: base-table count differs from the approved 299-table inventory.'}
    $binlogOutput=Invoke-Client $mysql @('--batch','--skip-column-names') $sourceLogin.Path 'SELECT @@GLOBAL.log_bin,@@GLOBAL.binlog_expire_logs_seconds;' -Database 'hrms_web1' -RequireTls
    $binlogFields=$binlogOutput.Trim() -split "`t"
    if($binlogFields.Count -ne 2 -or $binlogFields[0] -ne '1' -or [long]$binlogFields[1] -lt 2592000){throw 'PITR metadata validation failed: binary logging or 30-day retention is not available.'}
    $binaryLogs=Invoke-Client $mysql @('--batch','--skip-column-names') $sourceLogin.Path 'SHOW BINARY LOGS;' -Database 'hrms_web1' -RequireTls
    $binaryLogRows=@($binaryLogs.Trim() -split "`r?`n" | Where-Object { $_.Length -gt 0 })
    if($binaryLogRows.Count -eq 0){throw 'PITR metadata validation failed: no binary log files are visible to the backup account.'}
    $binaryLogBytes=0L
    foreach($row in $binaryLogRows){$fields=$row -split "`t";$length=0L;if($fields.Count -lt 2 -or -not [long]::TryParse($fields[1],[ref]$length)){throw 'PITR metadata validation failed: binary log inventory was malformed.'};$binaryLogBytes+=$length}
    $sizeOutput=Invoke-Client $mysql @('--batch','--skip-column-names') $sourceLogin.Path "SELECT COUNT(*),COALESCE(SUM(DATA_LENGTH+INDEX_LENGTH),0) FROM information_schema.TABLES WHERE TABLE_SCHEMA='hrms_web1' AND TABLE_TYPE='BASE TABLE';" -Database 'hrms_web1' -RequireTls
    $sizeFields=$sizeOutput.Trim() -split "`t"
    if($sizeFields.Count -ne 2 -or [int]$sizeFields[0] -ne $tableCount){throw 'Source validation failed: database size inventory did not match the base-table inventory.'}
    $databaseBytes=[long]$sizeFields[1]
    $backupFree=Get-FreeBytesForPath $root
    if($BackupOnly){$restoreFree=$null;$requiredBytes=Assert-BackupCapacity $databaseBytes $backupFree $backupFree}
    else{$restoreFree=Get-FreeBytesForPath $restoreVolumePath;$requiredBytes=Assert-BackupCapacity $databaseBytes $backupFree $restoreFree}

    $stage='backup setup'
    [void](New-Item -ItemType Directory -Path $root -Force)
    [void](New-Item -ItemType Directory -Path $run);Protect-Directory $run
    $restoreData=Join-Path $run 'isolated-mysql-data';$backup=Join-Path $run "hrms_web1-$stamp.sql.aes";$serverLog=Join-Path $run 'restore-mysqld.log'
    $noDdl=Read-Host 'Confirm no schema DDL or migrations will run during the dump by entering NO DDL DURING WEB1 SNAPSHOT'
    if($noDdl -cne 'NO DDL DURING WEB1 SNAPSHOT'){throw 'Backup blocked: DDL-free snapshot window was not confirmed.'}
    $backupSecure=Read-Host 'Enter a new backup encryption passphrase (minimum 16 characters)' -AsSecureString;$backupPass=Get-SecureText $backupSecure
    if($backupPass.Length -lt 16){throw 'Backup encryption validation failed: passphrase length is insufficient.'}
    $stage='encrypted backup'
    Write-EncryptedDump $dump $sourceLogin.Path $backup $backupPass
    $verified=Test-EncryptedDump $backup $backupPass;$backupVerified=$true
    $backupHash=(Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash

    if($BackupOnly){
        $apiAfter=Get-ApiProcessSnapshot
        if(($apiBefore -join "`n") -ne ($apiAfter -join "`n")){throw 'Process safety verification failed: observed API process inventory changed during backup creation.'}
        $evidence=[ordered]@{Result='PASS';Mode='BackupOnly';CompletionStatus='BACKUP_ONLY_COMPLETE';BackupOnly=$true;TenantCode='WEB1';Host='web1.localhost';Shard='web1';Database='hrms_web1';ServerVersion=$identityParts[2];ServerPort=$identityParts[1];CatalogAndTenantSameInstance=$true;Tls='VERIFY_IDENTITY with approved CA';Consistency='InnoDB --single-transaction';DdlFreezeConfirmed=$true;DatabaseBytes=$databaseBytes;CapacityRequiredBytes=$requiredBytes;BackupVolumeFreeBytes=$backupFree;TimestampUtc=[DateTime]::UtcNow.ToString('o');BackupFile=(Split-Path $backup -Leaf);BackupBytes=(Get-Item -LiteralPath $backup).Length;SHA256=$backupHash;Encrypted=$true;Integrity='Fresh HMAC-SHA256 verified';BackupVerified=$true;RestoreVerified=$false;Restore='NOT_RUN';BaseTableCount=$tableCount;AllBaseTablesInnoDB=$true;BinaryLoggingEnabled=$true;BinlogRetentionSeconds=[long]$binlogFields[1];BinaryLogFileCount=$binaryLogRows.Count;BinaryLogBytes=$binaryLogBytes;Pitr='Available subject to retaining the referenced binlogs; full-tenant restore is the approved rollback model';ApiProcessSnapshotUnchanged=$true}
        $evidence|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
        Write-Output "PASS. BackupOnly complete; encrypted backup evidence: $run"
        return
    }

    # A separate empty datadir, loopback-only bind, and non-live port are mandatory.
    $stage='isolated server initialization and restore'
    if(Test-Path -LiteralPath $restoreData){throw 'Isolated restore datadir already exists; refusing reuse.'}
    [void](New-Item -ItemType Directory -Path $restoreData)
    $initializePsi=[Diagnostics.ProcessStartInfo]::new();$initializePsi.FileName=$server;$initializePsi.UseShellExecute=$false;$initializePsi.CreateNoWindow=$true
    $initializePsi.RedirectStandardOutput=$true;$initializePsi.RedirectStandardError=$true;Clear-MySqlPasswordEnvironment $initializePsi
    Add-ProcessArgument $initializePsi '--no-defaults';Add-ProcessArgument $initializePsi '--initialize-insecure';Add-ProcessArgument $initializePsi "--basedir=$MysqlBin\..";Add-ProcessArgument $initializePsi "--datadir=$restoreData"
    $initializer=[Diagnostics.Process]::new();$initializer.StartInfo=$initializePsi
    if(-not $initializer.Start()){throw 'Isolated MySQL initialization could not be started.'}
    $initOut=$initializer.StandardOutput.ReadToEndAsync();$initErr=$initializer.StandardError.ReadToEndAsync();$initializer.WaitForExit()
    $null=$initOut.GetAwaiter().GetResult();$null=$initErr.GetAwaiter().GetResult()
    if($initializer.ExitCode -ne 0){throw 'Isolated MySQL initialization failed; sanitized stage recorded.'}
    $port=34060;while($port -lt 34160){$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port);try{$listener.Start();$listener.Stop();break}catch{$listener.Stop();$port++}}
    if($port -ge 34160 -or $port -eq 3306){throw 'No safe isolated localhost port found.'};$isolatedPort=$port
    $baseDir=[IO.Path]::GetFullPath((Join-Path $MysqlBin '..'))
    $serverPsi=[Diagnostics.ProcessStartInfo]::new();$serverPsi.FileName=$server;$serverPsi.UseShellExecute=$false;$serverPsi.CreateNoWindow=$true;Clear-MySqlPasswordEnvironment $serverPsi
    foreach($a in (Get-IsolatedServerArguments $baseDir $restoreData $port $serverLog)){Add-ProcessArgument $serverPsi $a}
    $mysqlProc=[Diagnostics.Process]::new();$mysqlProc.StartInfo=$serverPsi;[void]$mysqlProc.Start();$processStartIdentity=$mysqlProc.StartTime.ToUniversalTime().Ticks
    $restorePass=[Convert]::ToBase64String((New-CryptoRandomBytes 32))
    $ready=$false;for($i=0;$i -lt 60;$i++){if($mysqlProc.HasExited){throw 'Isolated MySQL server exited during startup.'};try{$v=Invoke-Client $mysql @('--batch','--skip-column-names') $null 'SELECT @@datadir,@@port;' -User 'root' -Password '' -Port $port;if($v){$ready=$true;break}}catch{Start-Sleep -Seconds 1}}
    if(-not $ready){throw 'Isolated restore server did not become ready.'}
    $initialProtocolIdentity=Get-MySqlProtocolIdentity $mysql $port $restoreData $null $null 'root' ''
    if(-not $initialProtocolIdentity.Valid){$script:LastRestoreDiagnostic=$initialProtocolIdentity.Category;throw 'Isolated restore protocol identity validation failed.'}
    $isolatedServerUuid=$initialProtocolIdentity.ServerUuid
    if(-not $isolatedServerUuid -or $isolatedServerUuid -eq $identityParts[3]){throw 'Restore isolation validation failed: isolated server identity was not distinct.'}
    [void](Test-IsolatedProcessOwnership $mysqlProc $server $restoreData $port $processStartIdentity $mysql $isolatedServerUuid $null 'root' '')
    $setRootPassword="ALTER USER 'root'@'localhost' IDENTIFIED BY '$restorePass';"
    [void](Invoke-Client $mysql @('--batch','--skip-column-names') $null $setRootPassword -User 'root' -Password '' -Port $port -SqlOnStandardInput)
    $restoreLogin=New-MySqlLoginFile 'root' $restorePass $port $false
    $passwordCheck=Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path 'SELECT 1;' -Port $port
    if($passwordCheck.Trim() -ne '1'){throw 'Isolated restore credential verification failed.'}
    $serverIdentity=Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path 'SELECT @@datadir,@@port;' -Port $port
    [void](Test-RestoreServerIdentity $serverIdentity $restoreData $port)
    [void](Test-IsolatedProcessOwnership $mysqlProc $server $restoreData $port $processStartIdentity $mysql $isolatedServerUuid $restoreLogin.Path)
    $empty=Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME IN ('hrms_web1','hrms_catalog');" -Port $port
    if($empty.Trim() -ne '0'){throw 'Restore isolation validation failed: protected schemas unexpectedly exist before import.'}
    $unexpected=Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME NOT IN ('information_schema','mysql','performance_schema','sys');" -Port $port
    if($unexpected.Trim() -ne '0'){throw 'Restore isolation validation failed: fresh isolated instance was not empty.'}
    [void](Test-IsolatedProcessOwnership $mysqlProc $server $restoreData $port $processStartIdentity $mysql $isolatedServerUuid $restoreLogin.Path)
    Import-EncryptedDump $backup $verified $mysql $restoreLogin.Path $port
    $targetDb=Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='hrms_web1';" -Port $port
    if($targetDb.Trim() -ne 'hrms_web1'){throw 'Restore identity validation failed: expected database was not restored.'}
    $targetInv=Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path "SELECT TABLE_NAME,COALESCE(ENGINE,'VIEW') FROM information_schema.TABLES WHERE TABLE_SCHEMA='hrms_web1' ORDER BY TABLE_NAME;" -Port $port
    if($targetInv.Trim() -ne $engineInfo.Trim()){throw 'Restore verification failed: schema/table inventory differs.'}
    $tableNames=@(($engineInfo.Trim() -split "`r?`n") | Where-Object { ($_ -split "`t")[-1] -ne 'VIEW' } | ForEach-Object { ($_ -split "`t")[0] })
    $dstRows=@()
    foreach($table in $tableNames){
        $quoted=$table.Replace('`','``')
        $q='SELECT COUNT(*) FROM `hrms_web1`.`'+$quoted+'`;'
        $dstRows+=((Invoke-Client $mysql @('--batch','--skip-column-names') $restoreLogin.Path $q -Port $port).Trim())
    }
    Assert-RestoredRowCounts $tableNames $dstRows
    $restoreVerified=$true
    $stage='graceful isolated server shutdown'
    [void](Stop-IsolatedMysqlGracefully $mysqlProc $server $restoreData $port $mysqlAdmin $restoreLogin.Path $processStartIdentity $mysql $isolatedServerUuid)
    Remove-MySqlLoginFile $restoreLogin;$restoreLogin=$null
    if(Test-Path -LiteralPath $restoreData){
        $fullRestore=[IO.Path]::GetFullPath($restoreData);$fullRun=[IO.Path]::GetFullPath($run)
        if(-not $fullRestore.StartsWith($fullRun + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup refused: isolated datadir was outside the owned run directory.'}
        Remove-Item -LiteralPath $fullRestore -Recurse -Force
    }
    $apiAfter=Get-ApiProcessSnapshot
    if(($apiBefore -join "`n") -ne ($apiAfter -join "`n")){throw 'Process safety verification failed: observed API process inventory changed during backup verification.'}
    $hash=Get-FileHash -LiteralPath $backup -Algorithm SHA256
    $inventoryManifest=@($engineInfo.Trim() -split "`r?`n" | ForEach-Object { $f=$_ -split "`t";[pscustomobject]@{Name=$f[0];Engine=$f[-1]} })
    $evidence=[ordered]@{Result='PASS';TenantCode='WEB1';Host='web1.localhost';Shard='web1';Database='hrms_web1';ServerVersion=$identityParts[2];ServerPort=$identityParts[1];CatalogAndTenantSameInstance=$true;Tls='VERIFY_IDENTITY with approved CA';Consistency='InnoDB --single-transaction';DdlFreezeConfirmed=$true;DatabaseBytes=$databaseBytes;CapacityRequiredBytes=$requiredBytes;BackupVolumeFreeBytes=$backupFree;RestoreVolumeFreeBytes=$restoreFree;TimestampUtc=[DateTime]::UtcNow.ToString('o');BackupFile=(Split-Path $backup -Leaf);BackupBytes=(Get-Item $backup).Length;SHA256=$hash.Hash;Encrypted=$true;Integrity='HMAC verified';Restore='PASS';RestoreInstance='isolated loopback-only instance; isolated data directory removed after verified graceful shutdown';RestorePort=$port;TableInventory=$inventoryManifest;RestoredRowCountsCaptured=$true;SnapshotConsistentRowCounts='UNAVAILABLE: mysqldump transaction snapshot cannot be shared with separate COUNT queries';RowByRowEquality='NOT CLAIMED; HMAC-verified dump replay and successful client exit are the data-integrity evidence';BaseTableCount=$tableCount;AllBaseTablesInnoDB=$true;BinaryLoggingEnabled=$true;BinlogRetentionSeconds=[long]$binlogFields[1];BinaryLogFileCount=$binaryLogRows.Count;BinaryLogBytes=$binaryLogBytes;Pitr='Available subject to retaining the referenced binlogs; full-tenant restore is the approved rollback model';ApiProcessSnapshotUnchanged=$true}
    $evidence|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
    Write-Output "PASS. Encrypted backup and restore evidence: $run"
} catch {
    Initialize-HrmsDiagnosticState
    $diagnostic=$script:LastMySqlDiagnostic
    $diagnosticCategory=$script:LastBackupDiagnosticCategory
    if($script:LastRestoreDiagnostic){$diagnosticCategory=$script:LastRestoreDiagnostic;$diagnostic=$diagnosticCategory}
    $mySqlFailure=Get-HrmsMySqlFailureState
    if(-not $diagnosticCategory){$diagnosticCategory=Resolve-HrmsMySqlFailureCategory $stage}
    $exceptionCategory=$_.Exception.GetType().FullName
    if(-not $diagnostic){if(-not $diagnosticCategory){$diagnosticCategory='restore.stage_failed'};$diagnostic=$diagnosticCategory}
    if($run -and (Test-Path -LiteralPath $run)){
        $failure=[ordered]@{Result='BLOCKED';TimestampUtc=[DateTime]::UtcNow.ToString('o');Stage=$stage;Diagnostic=$diagnostic;DiagnosticCategory=$diagnosticCategory;ExceptionCategory=$exceptionCategory;BackupPresent=(Test-Path -LiteralPath $backup);BackupVerified=$backupVerified;RestoreVerified=$restoreVerified;IsolatedProcessStillRunning=($mysqlProc -and -not $mysqlProc.HasExited);TemporaryDataRetained=($mysqlProc -and -not $mysqlProc.HasExited -and (Test-Path -LiteralPath $restoreData))}
        $failure|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'failure.json') -Encoding utf8
    }
    throw "Backup/restore verification BLOCKED at '$stage'. $diagnostic"
} finally {
    if($mysqlProc -and -not $mysqlProc.HasExited -and $isolatedPort){
        $cleanupLogin=$restoreLogin;$ownedCleanupLogin=$false
        try {
            if(-not $cleanupLogin){$cleanupLogin=New-MySqlLoginFile 'root' '' $isolatedPort $false;$ownedCleanupLogin=$true}
            [void](Stop-IsolatedMysqlGracefully $mysqlProc $server $restoreData $isolatedPort $mysqlAdmin $cleanupLogin.Path $processStartIdentity $mysql $isolatedServerUuid)
        } catch {
            Write-Warning 'Isolated MySQL graceful cleanup could not be confirmed; the process and owned datadir were left untouched for operator recovery. No force termination was attempted.'
        } finally {
            if($ownedCleanupLogin -and $cleanupLogin){Remove-MySqlLoginFile $cleanupLogin}
        }
    }
    if($catalogLogin){Remove-MySqlLoginFile $catalogLogin}
    if($sourceLogin){Remove-MySqlLoginFile $sourceLogin}
    if($restoreLogin){Remove-MySqlLoginFile $restoreLogin}
    if($backupPass){$backupPass=$null};if($restorePass){$restorePass=$null}
}
