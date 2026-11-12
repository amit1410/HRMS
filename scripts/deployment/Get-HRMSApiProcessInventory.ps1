[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$api = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'HRMS.API.exe' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '(?i)HRMS\.API\.dll')
})
$services = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessId -in @($api.ProcessId) })

$inventory = foreach ($process in $api) {
    $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.ParentProcessId)" -ErrorAction SilentlyContinue
    $ports = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $process.ProcessId } |
        Select-Object -ExpandProperty LocalPort -Unique)
    [pscustomobject]@{
        PID = $process.ProcessId
        Name = $process.Name
        ParentPID = $process.ParentProcessId
        ParentPresent = ($null -ne $parent)
        Started = $process.CreationDate
        RegisteredService = ($null -ne ($services | Where-Object { $_.ProcessId -eq $process.ProcessId }))
        ListeningPorts = ($ports -join ',')
        WorkerState = 'UNKNOWN'
    }
}

if ($inventory) { $inventory | Format-Table -AutoSize }
else { 'No HRMS.API process was found by executable or HRMS.API.dll host match.' }

Get-NetTCPConnection -State Listen -LocalPort 5080,5090 -ErrorAction SilentlyContinue |
    Select-Object OwningProcess, LocalAddress, LocalPort |
    Format-Table -AutoSize
