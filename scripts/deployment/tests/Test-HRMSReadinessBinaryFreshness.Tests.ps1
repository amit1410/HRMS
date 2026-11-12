Describe 'Test-HRMSReadinessBinaryFreshness' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '..\Test-HRMSReadinessBinaryFreshness.ps1')
        function Assert-Equal {
            param($Actual, $Expected)
            if (-not [object]::Equals($Actual, $Expected)) {
                throw "Expected '$Expected'; received '$Actual'."
            }
        }
    }

    BeforeEach {
        $script:sourceRoot = Join-Path $TestDrive 'source'
        $script:binaryPath = Join-Path $TestDrive 'HRMS.EmployeeImportPreflight.dll'
        New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $sourceRoot 'Program.cs') -Value '// source'
        Set-Content -LiteralPath $binaryPath -Value 'binary'
    }

    It 'rejects a binary older than its readiness source' {
        $now = [DateTime]::UtcNow
        [IO.File]::SetLastWriteTimeUtc($binaryPath, $now.AddMinutes(-2))
        [IO.File]::SetLastWriteTimeUtc((Join-Path $sourceRoot 'Program.cs'), $now.AddMinutes(-1))

        $result = Test-HRMSReadinessBinaryFreshness -BinaryPath $binaryPath -SourceRoot $sourceRoot

        Assert-Equal $result.IsFresh $false
        Assert-Equal $result.Category 'binary.stale'
    }

    It 'accepts a binary at least as new as its readiness source' {
        $now = [DateTime]::UtcNow
        [IO.File]::SetLastWriteTimeUtc((Join-Path $sourceRoot 'Program.cs'), $now.AddMinutes(-1))
        [IO.File]::SetLastWriteTimeUtc($binaryPath, $now)

        $result = Test-HRMSReadinessBinaryFreshness -BinaryPath $binaryPath -SourceRoot $sourceRoot

        Assert-Equal $result.IsFresh $true
        Assert-Equal $result.Category 'binary.current'
    }

    It 'fails closed when the binary is missing' {
        Remove-Item -LiteralPath $binaryPath

        $result = Test-HRMSReadinessBinaryFreshness -BinaryPath $binaryPath -SourceRoot $sourceRoot

        Assert-Equal $result.IsFresh $false
        Assert-Equal $result.Category 'binary.missing'
    }

    It 'rejects a diagnostic binary whose reviewed hash does not match' {
        $now = [DateTime]::UtcNow
        [IO.File]::SetLastWriteTimeUtc((Join-Path $sourceRoot 'Program.cs'), $now.AddMinutes(-1))
        [IO.File]::SetLastWriteTimeUtc($binaryPath, $now)

        $result = Test-HRMSReadinessBinaryFreshness -BinaryPath $binaryPath `
            -SourceRoot $sourceRoot -ExpectedSHA256 ('0' * 64)

        Assert-Equal $result.IsFresh $false
        Assert-Equal $result.Category 'binary.hash_mismatch'
    }

    It 'accepts a diagnostic binary with the reviewed hash' {
        $now = [DateTime]::UtcNow
        [IO.File]::SetLastWriteTimeUtc((Join-Path $sourceRoot 'Program.cs'), $now.AddMinutes(-1))
        [IO.File]::SetLastWriteTimeUtc($binaryPath, $now)
        $hash = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash

        $result = Test-HRMSReadinessBinaryFreshness -BinaryPath $binaryPath `
            -SourceRoot $sourceRoot -ExpectedSHA256 $hash

        Assert-Equal $result.IsFresh $true
        Assert-Equal $result.Category 'binary.current'
    }

    It 'restores absent candidate environment overrides as absent' {
        $keys = @(
            'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT', 'EmployeeBulkImportWorker__Enabled',
            'WelcomeInvitations__Enabled', 'LeaveReminders__Enabled', 'ConfirmationReminders__Enabled',
            'LeaveAccrual__Enabled', 'AttendanceDeviceWorker__Enabled', 'Database__StartupMode'
        )
        $before = @{}
        foreach ($key in $keys) {
            $before[$key] = [pscustomobject]@{
                Present = [Environment]::GetEnvironmentVariables('Process').Contains($key)
                Value = [Environment]::GetEnvironmentVariable($key, 'Process')
            }
        }

        & (Join-Path $PSScriptRoot '..\Test-HRMSApiCandidateConfiguration.ps1') `
            -ContentRoot (Join-Path $PSScriptRoot '..\..\..\Backend\HRMS.API') | Out-Null

        foreach ($key in $keys) {
            $present = [Environment]::GetEnvironmentVariables('Process').Contains($key)
            $value = [Environment]::GetEnvironmentVariable($key, 'Process')
            Assert-Equal $present $before[$key].Present
            Assert-Equal $value $before[$key].Value
        }
    }
}
