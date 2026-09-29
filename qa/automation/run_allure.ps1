<#
.SYNOPSIS
Wrapper for the portable Allure commandline tool downloaded into .tools/ (see README.md ->
"Allure report"). Avoids needing a system-wide Java/Allure install or admin rights.

.EXAMPLE
.\run_allure.ps1 generate reports/allure-results -o reports/allure-report --clean
.\run_allure.ps1 open reports/allure-report
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$jreDir = Get-ChildItem (Join-Path $root '.tools') -Directory -Filter 'jdk-*jre' -ErrorAction SilentlyContinue | Select-Object -First 1
$allureBat = Get-ChildItem (Join-Path $root '.tools') -Filter 'allure.bat' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $jreDir -or -not $allureBat) {
    Write-Error @"
Portable Allure CLI / JRE not found under qa/automation/.tools/.
Either re-run the download steps in README.md ("Allure report" section), or, if you have a
system-wide `allure` command available (e.g. via `npm install -g allure-commandline`), just call
`allure` directly instead of this wrapper.
"@
    exit 1
}

$env:JAVA_HOME = $jreDir.FullName
& $allureBat.FullName @args
exit $LASTEXITCODE
