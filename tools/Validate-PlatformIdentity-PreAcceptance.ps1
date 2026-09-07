[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$frontendRoot = Join-Path $repoRoot 'Frontend\HRMS.Web'

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Phase,
        [string]$WorkingDirectory = $repoRoot
    )

    Write-Host ""
    Write-Host "[$Phase]"
    Write-Host ("{0} {1}" -f $Executable, ($Arguments -join ' '))
    Push-Location $WorkingDirectory
    try {
        & $Executable @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($null -eq $exitCode) {
        throw "$Phase failed because the process did not provide an exit code."
    }
    Write-Host "Exit code: $exitCode"
    if ($exitCode -ne 0) {
        throw "$Phase failed with exit code $exitCode."
    }
    Write-Host "${Phase}: PASS"
}

function Invoke-Phase {
    param(
        [Parameter(Mandatory)][int]$Number,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    try {
        & $Action
    }
    catch {
        throw "[$Number] $Name failed: $($_.Exception.Message)"
    }
}

try {
    Invoke-Phase 1 'Repository preflight' {
        if (-not (Test-Path (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
            throw 'AGENTS.md was not found.'
        }
        if (-not (Test-Path (Join-Path $repoRoot 'HRMS.slnx') -PathType Leaf)) {
            throw 'HRMS.slnx was not found.'
        }
        if (-not (Test-Path (Join-Path $frontendRoot 'node_modules') -PathType Container)) {
            throw 'Frontend node_modules is missing; run npm ci separately after review.'
        }
        Write-Host 'Repository preflight: PASS'
    }

    Invoke-Phase 2 'Backend restore/build' {
        Invoke-CheckedProcess 'dotnet' @('restore', 'HRMS.slnx', '-p:MSBuildEnableWorkloadResolver=false') 'Backend restore'
        Invoke-CheckedProcess 'dotnet' @('build', 'HRMS.slnx', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'Backend build'
    }

    Invoke-Phase 3 'Backend focused tests' {
        Invoke-CheckedProcess 'dotnet' @('test', 'Backend\HRMS.Tests\HRMS.Tests.csproj', '--no-restore', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--filter', 'FullyQualifiedName~Platform') 'Platform tests'
    }

    Invoke-Phase 4 'Catalog migration project builds' {
        Invoke-CheckedProcess 'dotnet' @('build', 'Backend\HRMS.Infrastructure.MySqlCatalogMigrations\HRMS.Infrastructure.MySqlCatalogMigrations.csproj', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'MySQL catalog migration build'
        Invoke-CheckedProcess 'dotnet' @('build', 'Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'SQL Server catalog migration build'
    }

    Invoke-Phase 5 'Platform bootstrap project build' {
        Invoke-CheckedProcess 'dotnet' @('restore', 'tools\PlatformAdminBootstrap\PlatformAdminBootstrap.csproj', '-p:MSBuildEnableWorkloadResolver=false') 'Platform bootstrap restore'
        Invoke-CheckedProcess 'dotnet' @('build', 'tools\PlatformAdminBootstrap\PlatformAdminBootstrap.csproj', '-p:UseAppHost=false', '-p:MSBuildEnableWorkloadResolver=false', '--no-restore') 'Platform bootstrap build'
    }

    Invoke-Phase 6 'Frontend typecheck' {
        Invoke-CheckedProcess 'npm.cmd' @('run', 'typecheck') 'Frontend typecheck' $frontendRoot
    }

    Invoke-Phase 7 'Frontend lint' {
        Invoke-CheckedProcess 'npm.cmd' @('run', 'lint') 'Frontend lint' $frontendRoot
    }

    Invoke-Phase 8 'Frontend tests' {
        Invoke-CheckedProcess 'npm.cmd' @('run', 'test:run') 'Frontend tests' $frontendRoot
    }

    Invoke-Phase 9 'Frontend production build' {
        Invoke-CheckedProcess 'npm.cmd' @('run', 'build') 'Frontend production build' $frontendRoot
    }

    Invoke-Phase 10 'git diff --check' {
        Invoke-CheckedProcess 'git' @('diff', '--check') 'git diff --check'
    }

    Write-Host ''
    Write-Host 'PLATFORM IDENTITY PRE-ACCEPTANCE VALIDATION PASS'
    Write-Host 'SECURITY IMPLEMENTATION READY FOR MIGRATION REHEARSAL'
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
