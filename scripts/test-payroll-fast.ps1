$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exitCode = 0

Write-Host '=== HRMS focused Payroll regression ==='

Push-Location $repoRoot
try {
    & dotnet test 'Backend\HRMS.Tests\HRMS.Tests.csproj' `
        --filter 'FullyQualifiedName~HRMS.Tests.SalaryComponentMasterTests|FullyQualifiedName~HRMS.Tests.SalaryStructureMasterTests|FullyQualifiedName~HRMS.Tests.EmployeeSalaryAssignmentTests|FullyQualifiedName~HRMS.Tests.PayrollPeriodRunTests|FullyQualifiedName~HRMS.Tests.PayrollCalculationTests|FullyQualifiedName~HRMS.Tests.StatutoryCalculationTests' `
        --no-build `
        --no-restore `
        --logger 'console;verbosity=minimal'
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

exit $exitCode
