param([Parameter(Mandatory=$true)][string]$StatePath)
$ErrorActionPreference = 'Stop'
$state = [IO.Path]::GetFullPath($StatePath)
$processState = [IO.Path]::ChangeExtension($state, '.processes.json')
if (-not (Test-Path -LiteralPath $processState)) { throw "Process manifest not found: $processState" }
$p = Get-Content -LiteralPath $processState -Raw | ConvertFrom-Json
foreach ($id in @($p.apiPid, $p.frontendPid)) {
    $owned = Get-Process -Id $id -ErrorAction SilentlyContinue
    $expected = if ($id -eq $p.apiPid) { [DateTime]::Parse($p.apiStartTime).ToUniversalTime() } else { [DateTime]::Parse($p.frontendStartTime).ToUniversalTime() }
    if ($owned -and [Math]::Abs(($owned.StartTime.ToUniversalTime() - $expected).TotalSeconds) -lt 2) { Stop-Process -Id $id -ErrorAction Stop }
    elseif ($owned) { throw "Refusing to stop PID $id because its start time does not match the run manifest." }
}
dotnet run --project (Join-Path $PSScriptRoot 'Phase3BBrowserAcceptance.csproj') -- cleanup $state
Remove-Item -LiteralPath $processState, $state -Force
Write-Output 'Browser processes stopped and exact run-owned databases removed.'
