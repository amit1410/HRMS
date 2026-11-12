function Test-HRMSReadinessBinaryFreshness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BinaryPath,

        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,

        [string]$ExpectedSHA256
    )

    if (-not (Test-Path -LiteralPath $BinaryPath -PathType Leaf)) {
        return [pscustomobject]@{ IsFresh = $false; Category = 'binary.missing' }
    }

    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
        return [pscustomobject]@{ IsFresh = $false; Category = 'source.unavailable' }
    }

    $sources = @(Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -ErrorAction Stop |
        Where-Object {
            $_.Extension -in @('.cs', '.csproj', '.props', '.targets') -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        })
    if ($sources.Count -eq 0) {
        return [pscustomobject]@{ IsFresh = $false; Category = 'source.unavailable' }
    }

    $binary = Get-Item -LiteralPath $BinaryPath -ErrorAction Stop
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSHA256)) {
        if ($ExpectedSHA256 -notmatch '^[A-Fa-f0-9]{64}$') {
            return [pscustomobject]@{ IsFresh = $false; Category = 'binary.expected_hash_invalid' }
        }

        $actualHash = (Get-FileHash -LiteralPath $binary.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
        if (-not [string]::Equals($actualHash, $ExpectedSHA256, [StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ IsFresh = $false; Category = 'binary.hash_mismatch' }
        }
    }

    $latestSourceUtc = ($sources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
    if ($binary.LastWriteTimeUtc -lt $latestSourceUtc) {
        return [pscustomobject]@{ IsFresh = $false; Category = 'binary.stale' }
    }

    return [pscustomobject]@{ IsFresh = $true; Category = 'binary.current' }
}
