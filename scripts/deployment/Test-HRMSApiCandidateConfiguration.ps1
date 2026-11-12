[CmdletBinding()]
param(
    [string]$ContentRoot,
    [string]$ApiProject,
    [ValidateSet('Normal', 'IsolatedRecovery')]
    [string]$Profile = 'Normal'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ContentRoot)) { $ContentRoot = Join-Path $PSScriptRoot '..\..\Backend\HRMS.API' }
if ([string]::IsNullOrWhiteSpace($ApiProject)) { $ApiProject = Join-Path $PSScriptRoot '..\..\Backend\HRMS.API\HRMS.API.csproj' }
$contentRootPath = (Resolve-Path -LiteralPath $ContentRoot).Path
if (-not (Test-Path -LiteralPath $ApiProject -PathType Leaf)) {
    throw 'API project metadata was not found.'
}

function Add-ConfigObject {
    param([hashtable]$Target, [string]$Prefix, $Value)

    if ($null -eq $Value) { return }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Value.PSObject.Properties) {
            $key = if ($Prefix) { '{0}:{1}' -f $Prefix, $property.Name } else { $property.Name }
            Add-ConfigObject -Target $Target -Prefix $key -Value $property.Value
        }
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($keyName in $Value.Keys) {
            $key = if ($Prefix) { '{0}:{1}' -f $Prefix, $keyName } else { [string]$keyName }
            Add-ConfigObject -Target $Target -Prefix $key -Value $Value[$keyName]
        }
        return
    }
    if ($Value -is [array]) {
        $Target[$Prefix] = ConvertTo-Json -InputObject $Value -Compress -Depth 32
        return
    }
    $Target[$Prefix] = [string]$Value
}

function Add-JsonLayer {
    param([hashtable]$Target, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    Add-ConfigObject -Target $Target -Prefix '' -Value $json
}

function Get-LayeredConfiguration {
    param([string]$Root, [string]$SecretsId, [string]$MountedPath)

    $values = @{}
    Add-JsonLayer -Target $values -Path (Join-Path $Root 'appsettings.json')
    Add-JsonLayer -Target $values -Path (Join-Path $Root 'appsettings.Development.json')

    if ($SecretsId) {
        $secretsPath = Join-Path $env:APPDATA (Join-Path 'Microsoft\UserSecrets' (Join-Path $SecretsId 'secrets.json'))
        Add-JsonLayer -Target $values -Path $secretsPath
    }

    foreach ($item in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $key = ([string]$item.Key).Replace('__', ':')
        $values[$key] = [string]$item.Value
    }

    if ($MountedPath) { Add-JsonLayer -Target $values -Path $MountedPath }
    return $values
}

$savedEnvironment = @{}
$workerEnvironmentKeys = @(
    'EmployeeBulkImportWorker__Enabled', 'WelcomeInvitations__Enabled', 'LeaveReminders__Enabled',
    'ConfirmationReminders__Enabled', 'LeaveAccrual__Enabled', 'AttendanceDeviceWorker__Enabled',
    'Database__StartupMode'
)
$environmentKeys = @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT') + $workerEnvironmentKeys
foreach ($key in $environmentKeys) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}

try {
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    [xml]$project = Get-Content -LiteralPath $ApiProject -Raw
    $secretsId = [string]$project.Project.PropertyGroup.UserSecretsId

    # Baseline and candidate use the same Development provider chain. The normal profile only disables
    # the import worker. The isolated recovery profile additionally disables every hosted worker and
    # database startup work. The mounted JSON provider is applied last, matching API startup.
    $baseline = Get-LayeredConfiguration -Root $contentRootPath -SecretsId $secretsId -MountedPath $null
    $mountedPath = [string]$baseline['EmployeeImportStorage:ConfigurationFile']
    if ($mountedPath -and -not [IO.Path]::IsPathFullyQualified($mountedPath)) {
        throw 'The configured mounted JSON path is not absolute.'
    }
    if ($mountedPath -and -not (Test-Path -LiteralPath $mountedPath -PathType Leaf)) {
        throw 'The configured mounted JSON file is unavailable.'
    }
    if ($mountedPath) { Add-JsonLayer -Target $baseline -Path $mountedPath }

    $overriddenKeys = @('EmployeeBulkImportWorker:Enabled')
    $env:EmployeeBulkImportWorker__Enabled = 'false'
    if ($Profile -eq 'IsolatedRecovery') {
        $env:WelcomeInvitations__Enabled = 'false'
        $env:LeaveReminders__Enabled = 'false'
        $env:ConfirmationReminders__Enabled = 'false'
        $env:LeaveAccrual__Enabled = 'false'
        $env:AttendanceDeviceWorker__Enabled = 'false'
        $env:Database__StartupMode = 'Disabled'
        $overriddenKeys += @(
            'WelcomeInvitations:Enabled', 'LeaveReminders:Enabled', 'ConfirmationReminders:Enabled',
            'LeaveAccrual:Enabled', 'AttendanceDeviceWorker:Enabled', 'Database:StartupMode'
        )
    }
    $candidate = Get-LayeredConfiguration -Root $contentRootPath -SecretsId $secretsId -MountedPath $mountedPath

    $workerText = [string]$candidate['EmployeeBulkImportWorker:Enabled']
    $workerValue = $false
    $workerParsed = [bool]::TryParse($workerText, [ref]$workerValue)

    $nonWorkerEqual = $true
    $allKeys = @($baseline.Keys + $candidate.Keys | Sort-Object -Unique)
    foreach ($key in $allKeys) {
        if ($overriddenKeys -contains [string]$key) { continue }
        $leftExists = $baseline.ContainsKey($key)
        $rightExists = $candidate.ContainsKey($key)
        if ($leftExists -ne $rightExists) { $nonWorkerEqual = $false; break }
        if ($leftExists -and -not [string]::Equals([string]$baseline[$key], [string]$candidate[$key], [StringComparison]::Ordinal)) {
            $nonWorkerEqual = $false
            break
        }
    }

    $jwtSecret = [string]$candidate['Jwt:SecretKey']
    $databaseProvider = [string]$candidate['Database:CatalogProvider']
    if (-not $databaseProvider) { $databaseProvider = [string]$candidate['Database:Provider'] }
    if (-not $databaseProvider) { $databaseProvider = 'SqlServer' }
    $catalogConnection = [string]$candidate['ConnectionStrings:Catalog']
    if ($databaseProvider -eq 'Sqlite') {
        $catalogConnection = [string]$candidate['ConnectionStrings:SqliteCatalog']
        if (-not $catalogConnection) { $catalogConnection = 'Data Source=hrms_catalog_dev.db' }
    }

    $encryptionActiveId = [string]$candidate['EmployeeImportStorage:Encryption:ActiveKeyId']
    $keyPrefix = 'EmployeeImportStorage:Encryption:Keys:'
    $activeKey = [string]$candidate[($keyPrefix + $encryptionActiveId)]
    $encryptionKeyValid = $false
    if ($encryptionActiveId -and $activeKey) {
        try { $encryptionKeyValid = ([Convert]::FromBase64String($activeKey).Length -eq 32) } catch { $encryptionKeyValid = $false }
    }

    $coreSettingsAvailable =
        -not [string]::IsNullOrWhiteSpace([string]$candidate['Jwt:Issuer']) -and
        -not [string]::IsNullOrWhiteSpace([string]$candidate['Jwt:Audience']) -and
        ([Text.Encoding]::UTF8.GetByteCount($jwtSecret) -ge 32) -and
        -not [string]::IsNullOrWhiteSpace([string]$candidate['Database:Provider']) -and
        -not [string]::IsNullOrWhiteSpace($catalogConnection) -and
        -not [string]::IsNullOrWhiteSpace([string]$candidate['EmployeeImportStorage:Provider']) -and
        $encryptionKeyValid

    $workerKeys = @(
        'EmployeeBulkImportWorker:Enabled', 'WelcomeInvitations:Enabled', 'LeaveReminders:Enabled',
        'ConfirmationReminders:Enabled', 'LeaveAccrual:Enabled', 'AttendanceDeviceWorker:Enabled'
    )
    $allWorkersDisabled = $true
    $workerStates = @{}
    foreach ($key in $workerKeys) {
        $value = $false
        $parsed = [bool]::TryParse([string]$candidate[$key], [ref]$value)
        $workerStates[$key] = if ($parsed) { $value } else { $null }
        if (-not $parsed -or $value) { $allWorkersDisabled = $false }
    }
    $startupMode = [string]$candidate['Database:StartupMode']
    if (-not $startupMode) { $startupMode = 'Disabled' }
    $databaseStartupSafe = ($startupMode -eq 'Disabled')

    $passed = $workerParsed -and -not $workerValue -and $nonWorkerEqual -and $coreSettingsAvailable -and $databaseStartupSafe
    if ($Profile -eq 'IsolatedRecovery') { $passed = $passed -and $allWorkersDisabled -and $databaseStartupSafe }
    [pscustomobject]@{
        Profile = $Profile
        Environment = 'Development'
        MountedConfigurationConfigured = [bool]$mountedPath
        MountedConfigurationAvailable = (-not $mountedPath -or (Test-Path -LiteralPath $mountedPath -PathType Leaf))
        EmployeeBulkImportWorkerEnabled = if ($workerParsed) { $workerValue } else { $null }
        WelcomeInvitationsEnabled = $workerStates['WelcomeInvitations:Enabled']
        LeaveRemindersEnabled = $workerStates['LeaveReminders:Enabled']
        ConfirmationRemindersEnabled = $workerStates['ConfirmationReminders:Enabled']
        LeaveAccrualEnabled = $workerStates['LeaveAccrual:Enabled']
        AttendanceDeviceWorkerEnabled = $workerStates['AttendanceDeviceWorker:Enabled']
        AllHostedWorkersDisabled = $allWorkersDisabled
        DatabaseStartupMode = $startupMode
        DatabaseStartupWorkDisabled = $databaseStartupSafe
        OtherEffectiveConfigurationPreserved = $nonWorkerEqual
        RequiredSettingsAvailable = $coreSettingsAvailable
        DatabaseAccessed = $false
        Passed = $passed
    } | Format-List

    if (-not $passed) {
        $global:LASTEXITCODE = 2
        throw 'Normal-candidate effective configuration did not pass.'
    }
    $global:LASTEXITCODE = 0
}
finally {
    foreach ($key in $environmentKeys) {
        $value = $savedEnvironment[$key]
        if ($null -eq $value) { Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($key, $value, 'Process') }
    }
}
