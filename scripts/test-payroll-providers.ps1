$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sqlServerStatus = 'BLOCKED'
$mySqlStatus = 'BLOCKED'

function Invoke-PayrollProviderTest {
    param(
        [Parameter(Mandatory)]
        [string]$Provider,

        [Parameter(Mandatory)]
        [string]$Filter
    )

    Write-Host "=== HRMS $Provider Payroll provider tests ==="

    & dotnet test 'Backend\HRMS.Tests\HRMS.Tests.csproj' `
        --filter $Filter `
        --no-restore `
        --logger 'console;verbosity=minimal' |
        Out-Host

    $code = $LASTEXITCODE

    if ($code -eq 0) {
        Write-Host "=== $Provider Payroll provider tests PASSED ==="
    }
    else {
        Write-Host "=== $Provider Payroll provider tests FAILED (exit code $code) ==="
    }

    return [int]$code
}

Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($env:HRMS_SQLSERVER_TEST_CONNECTION)) {
        Write-Host 'SQL Server payroll provider tests cannot run because HRMS_SQLSERVER_TEST_CONNECTION is not configured.'
    }
    else {
        $sqlServerExitCode = Invoke-PayrollProviderTest `
            -Provider 'SQL Server' `
            -Filter 'FullyQualifiedName~HRMS.Tests.SqlServerSalaryComponentIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerSalaryStructureIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerEmployeeSalaryAssignmentIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollPeriodRunIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollCalculationIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerStatutoryPayrollIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollOutputIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerBankAdviceIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollAccountingIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollRetroSettlementIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollStatutoryComplianceIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollProductionControlsIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollEndToEndUatIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollLoansIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollReimbursementsIntegrationTests|FullyQualifiedName~HRMS.Tests.SqlServerPayrollSeparationBenefitsIntegrationTests'
        $sqlServerStatus = if ($sqlServerExitCode -eq 0) { 'PASS' } else { 'FAIL' }
    }

    if ([string]::IsNullOrWhiteSpace($env:HRMS_MYSQL_TEST_CONNECTION)) {
        Write-Host 'MySQL payroll provider tests cannot run because HRMS_MYSQL_TEST_CONNECTION is not configured.'
    }
    else {
        $mySqlExitCode = Invoke-PayrollProviderTest `
            -Provider 'MySQL' `
            -Filter 'FullyQualifiedName~HRMS.Tests.MySqlSalaryComponentIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlSalaryStructureIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlEmployeeSalaryAssignmentIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollPeriodRunIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollCalculationIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlStatutoryPayrollIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollOutputIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlBankAdviceIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollAccountingIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollRetroSettlementIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollStatutoryComplianceIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollProductionControlsIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollEndToEndUatIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollLoansIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollReimbursementsIntegrationTests|FullyQualifiedName~HRMS.Tests.MySqlPayrollSeparationBenefitsIntegrationTests'
        $mySqlStatus = if ($mySqlExitCode -eq 0) { 'PASS' } else { 'FAIL' }
    }
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host '=== PAYROLL PROVIDER SUMMARY ==='
Write-Host "SQL Server: $sqlServerStatus"
Write-Host "MySQL: $mySqlStatus"

if ($sqlServerStatus -eq 'PASS' -and $mySqlStatus -eq 'PASS') {
    Write-Host 'Overall: PASS'
    Write-Host '=== ALL PAYROLL PROVIDER TESTS PASSED ==='
    exit 0
}

Write-Host 'Overall: FAIL'
exit 1
