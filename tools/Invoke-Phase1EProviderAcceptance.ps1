[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SqlServer', 'MySql')]
    [string] $Provider
)

$required = @(
    'HRMS_PHASE1E_SERVER_IDENTITY',
    'HRMS_PHASE1E_INSTANCE_OR_PORT',
    'HRMS_PHASE1E_DATABASE_NAMES',
    'HRMS_PHASE1E_RUN_ID',
    'HRMS_PHASE1E_MANIFEST_PATH',
    'HRMS_PHASE1E_ENVIRONMENT',
    'HRMS_PHASE1E_TEST_ONLY_CONFIGURATION',
    'HRMS_PHASE1E_APPROVE_CONNECTION',
    'HRMS_PHASE1E_APPROVE_CREATE',
    'HRMS_PHASE1E_APPROVE_MIGRATION',
    'HRMS_PHASE1E_APPROVE_DELETE',
    'HRMS_PHASE1E_RESULT_PATH'
)

if ($Provider -eq 'SqlServer' -and [string]::IsNullOrWhiteSpace($env:HRMS_PHASE1E_SQLSERVER_CONNECTION)) { throw 'HRMS_PHASE1E_SQLSERVER_CONNECTION is required in the process environment.' }
if ($Provider -eq 'MySql' -and [string]::IsNullOrWhiteSpace($env:HRMS_PHASE1E_MYSQL_CONNECTION)) { throw 'HRMS_PHASE1E_MYSQL_CONNECTION is required in the process environment.' }
foreach ($name in $required) { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "$name is required in the process environment." } }

$env:HRMS_PHASE1E_EXECUTE = 'true'
$env:HRMS_PHASE1E_PROVIDER = $Provider
$filter = "FullyQualifiedName~ProviderAcceptanceRunnerEntryPoints.$($Provider)_WelcomeInvitation_acceptance"
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --filter $filter --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) { throw "Phase 1E $Provider provider acceptance did not pass. Review the redacted result artifact." }
