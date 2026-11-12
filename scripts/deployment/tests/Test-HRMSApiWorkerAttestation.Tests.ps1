Describe 'HRMS API listener snapshot classification' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '..\Resolve-HRMSApiListenerSnapshot.ps1')
        function Assert-Equal {
            param($Actual, $Expected)
            if (-not [object]::Equals($Actual, $Expected)) {
                throw "Expected '$Expected'; received '$Actual'."
            }
        }
    }

    It 'classifies CmdletizationQuery_NotFound as a genuine empty query' {
        Assert-Equal (Get-HRMSCimListenerQueryStatus 'CmdletizationQuery_NotFound,Get-NetTCPConnection') 'no_match'
        $result = Resolve-HRMSApiListenerSnapshot -Port 5080 -CimStatus no_match -NetstatSucceeded $true
        Assert-Equal $result.Category 'listener.no_ports_found'
        Assert-Equal $result.ProcessId $null
    }

    It 'uses one netstat owner as a fallback when CIM listener discovery fails' {
        Assert-Equal (Get-HRMSCimListenerQueryStatus 'CimJob_BrokenCimSession,Get-NetTCPConnection') 'query_failed'
        $netstat = @([pscustomobject]@{ LocalPort = 5080; OwningProcess = 25972 })
        $result = Resolve-HRMSApiListenerSnapshot -Port 5080 -CimStatus query_failed -NetstatSucceeded $true -NetstatRecords $netstat
        Assert-Equal $result.Category 'listener.netstat_fallback'
        Assert-Equal $result.ProcessId 25972
    }

    It 'fails closed when Windows CIM access is denied and netstat finds no API listener' {
        Assert-Equal (Get-HRMSCimListenerQueryStatus 'HRESULT 0x80041003,Get-NetTCPConnection') 'query_failed'
        $result = Resolve-HRMSApiListenerSnapshot -Port 5080 -CimStatus query_failed -NetstatSucceeded $true
        Assert-Equal $result.Category 'listener.no_ports_found'
        Assert-Equal $result.ProcessId $null
    }

    It 'verifies one owner when multiple listener records belong to that PID' {
        $cim = @(
            [pscustomobject]@{ LocalPort = 5080; OwningProcess = 25972 },
            [pscustomobject]@{ LocalPort = 5080; OwningProcess = 25972 }
        )
        $netstat = @([pscustomobject]@{ LocalPort = 5080; OwningProcess = 25972 })
        $result = Resolve-HRMSApiListenerSnapshot -Port 5080 -CimStatus success -CimRecords $cim -NetstatSucceeded $true -NetstatRecords $netstat
        Assert-Equal $result.Category 'listener.verified'
        Assert-Equal $result.ProcessId 25972
    }

    It 'rejects multiple distinct listener owners' {
        $cim = @(
            [pscustomobject]@{ LocalPort = 5080; OwningProcess = 25972 },
            [pscustomobject]@{ LocalPort = 5080; OwningProcess = 30000 }
        )
        $result = Resolve-HRMSApiListenerSnapshot -Port 5080 -CimStatus success -CimRecords $cim -NetstatSucceeded $true
        Assert-Equal $result.Category 'listener.owner_ambiguous'
        Assert-Equal $result.ProcessId $null
    }

    It 'flags intermittent snapshots when only one source sees the listener' {
        $netstat = @([pscustomobject]@{ LocalPort = 5080; OwningProcess = 25972 })
        $result = Resolve-HRMSApiListenerSnapshot -Port 5080 -CimStatus no_match -NetstatSucceeded $true -NetstatRecords $netstat
        Assert-Equal $result.Category 'listener.snapshot_inconsistent'
        Assert-Equal $result.ProcessId 25972
    }

    It 'parses IPv4 and IPv6 LISTENING rows and ignores non-listening states' {
        $records = @(ConvertFrom-HRMSNetstatListenerLines -Lines @(
            '  TCP    0.0.0.0:5080       0.0.0.0:0       LISTENING       25972',
            '  TCP    [::]:5080          [::]:0          LISTENING       25972',
            '  TCP    127.0.0.1:5080     127.0.0.1:50000 ESTABLISHED    25972',
            '  TCP    0.0.0.0:5080       0.0.0.0:0       CLOSED          25972'
        ))
        Assert-Equal $records.Count 2
        Assert-Equal $records[0].LocalPort 5080
        Assert-Equal $records[1].LocalPort 5080
        Assert-Equal $records[0].OwningProcess 25972
        Assert-Equal $records[1].OwningProcess 25972
    }
}
