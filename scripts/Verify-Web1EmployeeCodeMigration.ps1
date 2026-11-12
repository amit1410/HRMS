[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$expectedCatalog = 'hrms_catalog'
$expectedCatalogAccount = 'root'
$expectedCatalogPort = 3306
$expectedTenantCode = 'WEB1'
$expectedHost = 'web1.localhost'
$expectedShard = 'web1'
$expectedProvider = 'MySql'
$expectedTenantDatabase = 'hrms_web1'
$expectedTenantId = [guid]'16313e68-2e72-4a6a-b689-79b271cec295'
$migrationId = '20261030034652_AddEmployeeCodeConfigurationAudit'

$result = [ordered]@{
    CatalogVerification = 'FAIL'
    TenantRouting = 'FAIL'
    TenantDatabaseIdentity = 'FAIL'
    MigrationStatus = 'UNKNOWN'
    AuditTable = 'UNKNOWN'
    OverallReadiness = 'FAIL'
}
$stage = 'configuration'
$failureReason = $null
$failureCode = $null
$catalogConnection = $null
$tenantConnection = $null
$catalogCommand = $null
$tenantCommand = $null
$catalogReader = $null
$tenantReader = $null
$assemblyResolver = $null

function Stop-Verification {
    param(
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][string]$Reason
    )
    $script:failureCode = $Code
    $script:failureReason = $Reason
    throw [InvalidOperationException]::new('Read-only verification stopped.')
}

function Get-Scalar {
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$Sql,
        [string]$ParameterName,
        [object]$ParameterValue
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 10
    if ($ParameterName) {
        $parameter = $command.CreateParameter()
        $parameter.ParameterName = $ParameterName
        $parameter.Value = $ParameterValue
        [void]$command.Parameters.Add($parameter)
    }

    try {
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}

function Get-UnsupportedMySqlOptionNames {
    param([Parameter(Mandatory)][string]$ConnectionString)

    $parsedOptions = [System.Data.Common.DbConnectionStringBuilder]::new()
    try {
        $builderType = [System.Data.Common.DbConnectionStringBuilder]
        $builderType.GetProperty('ConnectionString').SetValue($parsedOptions, $ConnectionString)
    }
    catch {
        return @()
    }

    $unsupportedNames = [System.Collections.Generic.List[string]]::new()
    foreach ($optionName in $parsedOptions.Keys) {
        $optionProbe = [MySql.Data.MySqlClient.MySqlConnectionStringBuilder]::new()
        try {
            $optionProbe[[string]$optionName] = $parsedOptions[$optionName]
        }
        catch {
            $probeError = $_.Exception
            while ($probeError.InnerException) {
                $probeError = $probeError.InnerException
            }
            if ([string]::Equals([string]$probeError.ParamName, [string]$optionName, [StringComparison]::OrdinalIgnoreCase)) {
                $unsupportedNames.Add([string]$optionName)
            }
        }
    }

    return $unsupportedNames.ToArray()
}

function Get-MySqlFailureCategory {
    param([Parameter(Mandatory)][System.Management.Automation.ErrorRecord]$ErrorRecord)

    $exception = $ErrorRecord.Exception
    while ($null -ne $exception) {
        $typeName = $exception.GetType().FullName
        if ($typeName -in @(
            'System.IO.FileNotFoundException',
            'System.IO.FileLoadException',
            'System.DllNotFoundException',
            'System.BadImageFormatException',
            'System.TypeLoadException'
        )) {
            return 'MYSQL_RUNTIME_DEPENDENCY_MISSING'
        }

        if ($typeName -eq 'MySql.Data.MySqlClient.MySqlException') {
            $numberProperty = $exception.GetType().GetProperty('Number')
            $stateProperty = $exception.GetType().GetProperty('SqlState')
            $number = if ($numberProperty) { [int]$numberProperty.GetValue($exception) } else { -1 }
            $sqlState = if ($stateProperty) { [string]$stateProperty.GetValue($exception) } else { '' }
            if ($number -in @(1044, 1045) -or $sqlState -eq '28000') {
                return 'MYSQL_AUTHENTICATION_FAILURE'
            }
            if ($number -in @(1042, 2002, 2003, 2005)) {
                return 'MYSQL_NETWORK_FAILURE'
            }
        }

        if ($exception -is [System.Security.Authentication.AuthenticationException]) {
            return 'MYSQL_TLS_NEGOTIATION_FAILURE'
        }
        if ($exception -is [System.TimeoutException]) {
            return 'MYSQL_NETWORK_TIMEOUT'
        }
        if ($exception -is [System.Net.Sockets.SocketException]) {
            if ($exception.SocketErrorCode -eq [System.Net.Sockets.SocketError]::TimedOut) {
                return 'MYSQL_NETWORK_TIMEOUT'
            }
            return 'MYSQL_NETWORK_FAILURE'
        }
        if ($exception -is [System.IO.IOException]) {
            return 'MYSQL_NETWORK_FAILURE'
        }

        $exception = $exception.InnerException
    }

    return 'MYSQL_CLIENT_FAILURE'
}

try {
    $stage = 'CFG.ENV.CATALOG.READ'
    $catalogValue = [Environment]::GetEnvironmentVariable('ConnectionStrings__Catalog', 'Process')
    if ([string]::IsNullOrWhiteSpace($catalogValue)) {
        Stop-Verification 'CFG_ENV_CATALOG_MISSING' 'ConnectionStrings__Catalog is missing from this PowerShell process.'
    }

    $stage = 'CFG.ENV.SHARD_TEMPLATE.READ'
    $templateValue = [Environment]::GetEnvironmentVariable('Sharding__MySqlConnectionStringTemplate', 'Process')
    if ([string]::IsNullOrWhiteSpace($templateValue)) {
        Stop-Verification 'CFG_SHARD_TEMPLATE_MISSING' 'Sharding__MySqlConnectionStringTemplate is missing from this PowerShell process.'
    }

    $stage = 'CFG.RUNTIME.CHECK'
    if ($PSVersionTable.PSEdition -ne 'Core' -or [Environment]::Version.Major -lt 10) {
        Stop-Verification 'CFG_RUNTIME_UNSUPPORTED' 'Run this script in PowerShell 7 on .NET 10 or later to use the repository MySQL client dependency.'
    }

    $stage = 'CFG.MYSQL_ASSEMBLY.PATH'
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $dependencyDirectory = Join-Path $repositoryRoot 'Backend\HRMS.API\bin\Debug\net10.0'
    $mysqlAssemblyPath = Join-Path $dependencyDirectory 'MySql.Data.dll'
    if (-not (Test-Path -LiteralPath $mysqlAssemblyPath -PathType Leaf)) {
        Stop-Verification 'CFG_MYSQL_ASSEMBLY_MISSING' 'The MySql.Data client assembly is not present in the API build output. Build the API project, then rerun this script.'
    }

    $stage = 'CFG.MYSQL_ASSEMBLY.RESOLVER_REGISTER'
    $assemblyResolver = [System.Func[System.Runtime.Loader.AssemblyLoadContext, System.Reflection.AssemblyName, System.Reflection.Assembly]] {
        param($loadContext, $assemblyName)
        $candidate = Join-Path $dependencyDirectory ($assemblyName.Name + '.dll')
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $loadContext.LoadFromAssemblyPath($candidate)
        }
        return $null
    }
    [System.Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving($assemblyResolver)

    $stage = 'CFG.MYSQL_ASSEMBLY.LOAD'
    $mysqlAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($mysqlAssemblyPath)
    $loadedMysqlAssembly = [System.Runtime.Loader.AssemblyLoadContext]::Default.Assemblies |
        Where-Object { $_.GetName().FullName -eq $mysqlAssemblyName.FullName } |
        Select-Object -First 1
    if ($null -eq $loadedMysqlAssembly) {
        [void][System.Reflection.Assembly]::LoadFrom($mysqlAssemblyPath)
    }

    $stage = 'CFG.CATALOG.CONNECTION_STRING.PARSE'
    try {
        $catalogBuilder = [MySql.Data.MySqlClient.MySqlConnectionStringBuilder]::new($catalogValue)
    }
    catch {
        $unsupportedNames = @(Get-UnsupportedMySqlOptionNames -ConnectionString $catalogValue)
        if ($unsupportedNames.Count -gt 0) {
            Stop-Verification 'CFG_CATALOG_CONNECTION_OPTION_UNSUPPORTED' "Unsupported catalog connection-string option name(s): $($unsupportedNames -join ', '). Option values are suppressed."
        }
        throw
    }

    $stage = 'CFG.CATALOG.DATABASE.GUARD'
    if (-not [string]::Equals($catalogBuilder.Database, $expectedCatalog, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Verification 'CFG_CATALOG_DATABASE_MISMATCH' 'Catalog connection configuration does not target hrms_catalog; no connection was opened.'
    }

    $catalogHost = [string]$catalogBuilder.Server
    if (-not [string]::Equals($catalogHost, 'localhost', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($catalogHost, '127.0.0.1', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($catalogHost, '::1', [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Verification 'CFG_CATALOG_ENDPOINT_MISMATCH' 'Catalog connection target is not the expected localhost MySQL endpoint; configured endpoint details are suppressed.'
    }
    if ([int]$catalogBuilder.Port -ne $expectedCatalogPort) {
        Stop-Verification 'CFG_CATALOG_ENDPOINT_MISMATCH' 'Catalog connection target does not use the expected local MySQL port; configured endpoint details are suppressed.'
    }
    if (-not [string]::Equals([string]$catalogBuilder.UserID, $expectedCatalogAccount, [StringComparison]::Ordinal)) {
        Stop-Verification 'CFG_CATALOG_ACCOUNT_MISMATCH' 'Catalog connection account does not match the expected local MySQL account; configured username is suppressed.'
    }

    $stage = 'CFG.CATALOG.TIMEOUT.SET'
    $catalogBuilder.ConnectionTimeout = 5

    $stage = 'CFG.SHARD_TEMPLATE.CONNECTION_STRING.PARSE'
    try {
        $templateBuilder = [MySql.Data.MySqlClient.MySqlConnectionStringBuilder]::new($templateValue)
    }
    catch {
        $unsupportedNames = @(Get-UnsupportedMySqlOptionNames -ConnectionString $templateValue)
        if ($unsupportedNames.Count -gt 0) {
            Stop-Verification 'CFG_SHARD_TEMPLATE_OPTION_UNSUPPORTED' "Unsupported shard-template option name(s): $($unsupportedNames -join ', '). Option values are suppressed."
        }
        throw
    }

    $stage = 'CFG.SHARD_TEMPLATE.PLACEHOLDER.CHECK'
    if (-not $templateBuilder.Database.Contains('{shardKey}', [StringComparison]::Ordinal)) {
        Stop-Verification 'CFG_SHARD_PLACEHOLDER_MISSING' 'The configured MySQL database template does not contain the required {shardKey} placeholder.'
    }

    $stage = 'catalog connection'
    $catalogConnection = [MySql.Data.MySqlClient.MySqlConnection]::new($catalogBuilder.ConnectionString)
    $catalogConnection.Open()
    $catalogDatabase = [string](Get-Scalar -Connection $catalogConnection -Sql 'SELECT DATABASE()')
    if (-not [string]::Equals($catalogDatabase, $expectedCatalog, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Verification 'CATALOG_DATABASE_IDENTITY_MISMATCH' 'Connected catalog identity is not hrms_catalog; tenant routing was not queried.'
    }
    $result.CatalogVerification = 'PASS'

    $stage = 'catalog tenant route'
    $catalogCommand = $catalogConnection.CreateCommand()
    $catalogCommand.CommandText = 'SELECT Id, TenantCode, Host, ShardKey, DatabaseProvider, Status FROM Tenants WHERE Host = @host'
    $catalogCommand.CommandTimeout = 10
    $hostParameter = $catalogCommand.CreateParameter()
    $hostParameter.ParameterName = '@host'
    $hostParameter.Value = $expectedHost
    [void]$catalogCommand.Parameters.Add($hostParameter)
    $catalogReader = $catalogCommand.ExecuteReader()

    if (-not $catalogReader.Read()) {
        Stop-Verification 'WEB1_ROUTE_NOT_FOUND' 'No catalog tenant route was found for web1.localhost; tenant database was not opened.'
    }
    $routeId = [guid]::Parse([string]$catalogReader.GetValue(0))
    $routeCode = [string]$catalogReader.GetValue(1)
    $routeHost = ([string]$catalogReader.GetValue(2)).Trim().TrimEnd('.').ToLowerInvariant()
    $routeShard = [string]$catalogReader.GetValue(3)
    $routeProvider = [string]$catalogReader.GetValue(4)
    $routeStatus = [int]$catalogReader.GetValue(5)
    $hasSecondRoute = $catalogReader.Read()
    $catalogReader.Dispose()
    $catalogReader = $null
    $catalogCommand.Dispose()
    $catalogCommand = $null

    if ($hasSecondRoute -or
        $routeId -ne $expectedTenantId -or
        $routeCode -cne $expectedTenantCode -or
        $routeHost -cne $expectedHost -or
        $routeShard -cne $expectedShard -or
        -not [string]::Equals($routeProvider, $expectedProvider, [StringComparison]::OrdinalIgnoreCase) -or
        $routeStatus -ne 1) {
        Stop-Verification 'WEB1_ROUTE_MISMATCH' 'Catalog route does not exactly match the active WEB1 tenant, host, shard, and MySQL provider; tenant database was not opened.'
    }
    $result.TenantRouting = 'PASS'

    $resolvedDatabase = $templateBuilder.Database.Replace('{shardKey}', $routeShard, [StringComparison]::Ordinal)
    if (-not [string]::Equals($resolvedDatabase, $expectedTenantDatabase, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Verification 'WEB1_SHARD_DATABASE_MISMATCH' 'The repository shard template resolves WEB1 to an unexpected database; no tenant connection was opened.'
    }
    $templateBuilder.Database = $resolvedDatabase
    $templateBuilder.ConnectionTimeout = 5

    $stage = 'tenant connection'
    $tenantConnection = [MySql.Data.MySqlClient.MySqlConnection]::new($templateBuilder.ConnectionString)
    $tenantConnection.Open()
    $tenantDatabase = [string](Get-Scalar -Connection $tenantConnection -Sql 'SELECT DATABASE()')
    if (-not [string]::Equals($tenantDatabase, $expectedTenantDatabase, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Verification 'WEB1_DATABASE_IDENTITY_MISMATCH' 'Connected tenant database identity is not hrms_web1.'
    }
    $result.TenantDatabaseIdentity = 'PASS'

    $stage = 'tenant migration history'
    $historyTableCount = [int](Get-Scalar -Connection $tenantConnection -Sql "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '__EFMigrationsHistory'")
    if ($historyTableCount -eq 0) {
        $result.MigrationStatus = 'UNKNOWN'
    }
    else {
        $migrationCount = [int](Get-Scalar -Connection $tenantConnection -Sql 'SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE MigrationId = @migrationId' -ParameterName '@migrationId' -ParameterValue $migrationId)
        $result.MigrationStatus = if ($migrationCount -gt 0) { 'APPLIED' } else { 'PENDING' }
    }

    $stage = 'audit table check'
    $auditTableCount = [int](Get-Scalar -Connection $tenantConnection -Sql 'SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName' -ParameterName '@tableName' -ParameterValue 'EmployeeCodeConfigurationAuditEvents')
    $result.AuditTable = if ($auditTableCount -gt 0) { 'EXISTS' } else { 'MISSING' }

    $stage = 'RESULT.READINESS'
    if ($result.MigrationStatus -eq 'APPLIED' -and $result.AuditTable -eq 'EXISTS') {
        $result.OverallReadiness = 'PASS'
    }
    else {
        $failureCode = 'WEB1_AUDIT_MIGRATION_NOT_READY'
        $failureReason = 'The verified WEB1 database does not yet have both the audit migration history entry and audit table.'
    }
}
catch {
    if (-not $failureReason) {
        if ($stage -in @('catalog connection', 'tenant connection')) {
            $failureCode = Get-MySqlFailureCategory -ErrorRecord $_
            $failureReason = "MySQL connection failed at stage $stage with category $failureCode; exception details are suppressed."
        }
        else {
            $failureCode = 'VERIFY_' + ($stage -replace '[^A-Za-z0-9]+', '_').Trim('_').ToUpperInvariant()
            $failureReason = "Operation failed at stage $stage; exception details are suppressed."
        }
    }
}
finally {
    foreach ($resource in @($catalogReader, $tenantReader, $catalogCommand, $tenantCommand, $catalogConnection, $tenantConnection)) {
        if ($null -ne $resource) {
            try { $resource.Dispose() } catch { }
        }
    }
    if ($assemblyResolver) {
        try { [System.Runtime.Loader.AssemblyLoadContext]::Default.remove_Resolving($assemblyResolver) } catch { }
    }
}

Write-Output "Catalog verification: $($result.CatalogVerification)"
Write-Output "Tenant routing: $($result.TenantRouting)"
Write-Output "Tenant database identity: $($result.TenantDatabaseIdentity)"
Write-Output "Migration status: $($result.MigrationStatus)"
Write-Output "Audit table: $($result.AuditTable)"
Write-Output "Overall readiness: $($result.OverallReadiness)"
if ($failureReason) {
    Write-Output "Failure code: $failureCode"
    Write-Output "Failure stage: $stage"
    Write-Output "Blocker: $failureReason"
}
