$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exitCode = 0

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
        --logger 'console;verbosity=minimal'

    return $LASTEXITCODE
}

Push-Location $repoRoot
try {
    $sqlServerExitCode = Invoke-PayrollProviderTest `
        -Provider 'SQL Server' `
        -Filter 'FullyQualifiedName~SqlServerSalaryComponent|FullyQualifiedName~SqlServerSalaryStructure|FullyQualifiedName~SqlServerEmployeeSalaryAssignment'

    if ($sqlServerExitCode -ne 0) {
        $exitCode = $sqlServerExitCode
    }
    else {
        $exitCode = Invoke-PayrollProviderTest `
            -Provider 'MySQL' `
            -Filter 'FullyQualifiedName~MySqlSalaryComponent|FullyQualifiedName~MySqlSalaryStructure|FullyQualifiedName~MySqlEmployeeSalaryAssignment'
    }
}
finally {
    Pop-Location
}

exit $exitCode
