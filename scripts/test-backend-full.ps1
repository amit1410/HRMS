$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exitCode = 0

Write-Host '=== HRMS full backend regression ==='

Push-Location $repoRoot
try {
    & dotnet test 'Backend\HRMS.Tests\HRMS.Tests.csproj' `
        --no-restore `
        --logger 'console;verbosity=minimal'
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

exit $exitCode
