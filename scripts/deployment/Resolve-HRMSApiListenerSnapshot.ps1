function Get-HRMSCimListenerQueryStatus {
    [CmdletBinding()]
    param([string]$FullyQualifiedErrorId)

    if ($FullyQualifiedErrorId -like 'CmdletizationQuery_NotFound*') { return 'no_match' }
    return 'query_failed'
}

function ConvertFrom-HRMSNetstatListenerLines {
    [CmdletBinding()]
    param([string[]]$Lines)

    foreach ($line in $Lines) {
        if ($line -match '^\s*TCP\s+\S+:(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$') {
            [pscustomobject]@{ LocalPort = [int]$Matches[1]; OwningProcess = [int]$Matches[2] }
        }
    }
}

function Resolve-HRMSApiListenerSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [ValidateSet('success', 'no_match', 'query_failed')]
        [string]$CimStatus,
        [object[]]$CimRecords = @(),
        [bool]$NetstatSucceeded,
        [object[]]$NetstatRecords = @()
    )

    $cimOwners = @($CimRecords | Where-Object { [int]$_.LocalPort -eq $Port } |
        Select-Object -ExpandProperty OwningProcess -Unique)
    $netstatOwners = @($NetstatRecords | Where-Object { [int]$_.LocalPort -eq $Port } |
        Select-Object -ExpandProperty OwningProcess -Unique)

    if ($cimOwners.Count -gt 1 -or $netstatOwners.Count -gt 1) {
        return [pscustomobject]@{ Category = 'listener.owner_ambiguous'; ProcessId = $null }
    }

    if ($CimStatus -eq 'query_failed') {
        if ($NetstatSucceeded -and $netstatOwners.Count -eq 1) {
            return [pscustomobject]@{ Category = 'listener.netstat_fallback'; ProcessId = [int]$netstatOwners[0] }
        }
        if ($NetstatSucceeded -and $netstatOwners.Count -eq 0) {
            return [pscustomobject]@{ Category = 'listener.no_ports_found'; ProcessId = $null }
        }
        return [pscustomobject]@{ Category = 'listener.query_failed'; ProcessId = $null }
    }

    if (-not $NetstatSucceeded) {
        if ($cimOwners.Count -eq 1) {
            return [pscustomobject]@{ Category = 'listener.netstat_query_failed'; ProcessId = [int]$cimOwners[0] }
        }
        if ($CimStatus -eq 'no_match') {
            return [pscustomobject]@{ Category = 'listener.no_ports_found'; ProcessId = $null }
        }
        return [pscustomobject]@{ Category = 'listener.query_failed'; ProcessId = $null }
    }

    if ($cimOwners.Count -eq 0 -and $netstatOwners.Count -eq 0) {
        return [pscustomobject]@{ Category = 'listener.no_ports_found'; ProcessId = $null }
    }
    if ($cimOwners.Count -eq 1 -and $netstatOwners.Count -eq 1 -and $cimOwners[0] -eq $netstatOwners[0]) {
        return [pscustomobject]@{ Category = 'listener.verified'; ProcessId = [int]$cimOwners[0] }
    }
    if ($cimOwners.Count -eq 1 -and $netstatOwners.Count -eq 0) {
        return [pscustomobject]@{ Category = 'listener.snapshot_inconsistent'; ProcessId = [int]$cimOwners[0] }
    }
    if ($cimOwners.Count -eq 0 -and $netstatOwners.Count -eq 1) {
        return [pscustomobject]@{ Category = 'listener.snapshot_inconsistent'; ProcessId = [int]$netstatOwners[0] }
    }
    return [pscustomobject]@{ Category = 'listener.snapshot_inconsistent'; ProcessId = $null }
}
