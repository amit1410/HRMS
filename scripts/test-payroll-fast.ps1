$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exitCode = 0

Write-Host '=== HRMS focused Payroll regression ==='

Push-Location $repoRoot
try {
    & dotnet test 'Backend\HRMS.Tests\HRMS.Tests.csproj' `
        --filter 'FullyQualifiedName~HRMS.Tests.SalaryComponentMasterTests|FullyQualifiedName~HRMS.Tests.SalaryStructureMasterTests|FullyQualifiedName~HRMS.Tests.EmployeeSalaryAssignmentTests|FullyQualifiedName~HRMS.Tests.PayrollPeriodRunTests|FullyQualifiedName~HRMS.Tests.PayrollControlsTests|FullyQualifiedName~HRMS.Tests.PayrollCalculationTests|FullyQualifiedName~HRMS.Tests.StatutoryCalculationTests|FullyQualifiedName~HRMS.Tests.PayrollOutputTests|FullyQualifiedName~HRMS.Tests.BankAdviceTests|FullyQualifiedName~HRMS.Tests.PayrollAccountingTests|FullyQualifiedName~HRMS.Tests.PayrollFinalSettlementAccountingTests|FullyQualifiedName~HRMS.Tests.PayrollRetroSettlementTests|FullyQualifiedName~HRMS.Tests.PayrollStatutoryComplianceTests|FullyQualifiedName~HRMS.Tests.PayrollConcurrencyMatrixTests|FullyQualifiedName~HRMS.Tests.PayrollLoansConcurrencyTests|FullyQualifiedName~HRMS.Tests.PayrollEndToEndUatTests|FullyQualifiedName~HRMS.Tests.PayrollLoansFoundationTests|FullyQualifiedName~HRMS.Tests.Reimbursement|FullyQualifiedName~HRMS.Tests.SeparationBenefitsFoundationTests|FullyQualifiedName~HRMS.Tests.SeparationBenefitsConcurrencyTests|FullyQualifiedName~HRMS.Tests.VariablePay|FullyQualifiedName~HRMS.Tests.PayrollAdjustment|FullyQualifiedName~HRMS.Tests.PayrollAnalytics' `
        --no-build `
        --no-restore `
        --logger 'console;verbosity=minimal'
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

exit $exitCode
