# PowerShell 5.1-compatible, side-effect-free selection helpers for Stage 4.

function Get-LeavePolicyVersionItems {
    param([AllowNull()][object]$Data)

    if ($null -eq $Data) { throw 'STOP: deterministic leave policy version response was null; no version was changed.' }
    if ($null -ne $Data.items) { return @($Data.items) }
    if ($Data -is [System.Collections.IEnumerable] -and $Data -isnot [string]) { return @($Data) }
    return @($Data)
}

function Get-UniqueLeavePolicyVersions {
    param([AllowNull()][object]$Data)

    $raw = @(Get-LeavePolicyVersionItems $Data)
    $byId = @{}
    $missing = @()
    foreach ($item in $raw) {
        $id = [string]$item.id
        if ([string]::IsNullOrWhiteSpace($id)) { $missing += $item; continue }
        if (-not $byId.ContainsKey($id.ToLowerInvariant())) { $byId[$id.ToLowerInvariant()] = $item }
    }
    [pscustomobject]@{
        Raw = $raw
        Items = @($byId.Values)
        DuplicateCount = $raw.Count - $missing.Count - $byId.Count
        MissingIdentityCount = $missing.Count
    }
}

function Get-LeavePolicyVersionInventory {
    param([AllowNull()][object]$Data)

    $normalized = Get-UniqueLeavePolicyVersions $Data
    $versions = @($normalized.Items)
    $drafts = @($versions | Where-Object { [string]$_.status -ceq 'Draft' })
    $published = @($versions | Where-Object { [string]$_.status -ceq 'Published' })
    $discarded = @($versions | Where-Object { [string]$_.status -ceq 'Discarded' })
    $unknown = @($versions | Where-Object { [string]$_.status -notin @('Draft', 'Published', 'Retired', 'Discarded') })
    $publishedNumbers = @($published | Group-Object versionNumber | Where-Object { $_.Count -gt 1 })
    [pscustomobject]@{
        Raw = $normalized.Raw
        Items = $versions
        RawCount = $normalized.Raw.Count
        DistinctCount = $versions.Count
        DuplicateCount = $normalized.DuplicateCount
        MissingIdentityCount = $normalized.MissingIdentityCount
        DraftCount = $drafts.Count
        PublishedCount = $published.Count
        DiscardedCount = $discarded.Count
        UnknownStatusCount = $unknown.Count
        ConflictingPublishedVersionNumberCount = $publishedNumbers.Count
        Classification = if ($unknown.Count -gt 0) { 'UNKNOWN STATUS' } elseif ($drafts.Count -gt 1) { 'COMPETING DRAFTS' } elseif ($drafts.Count -eq 1) { 'ONE DRAFT' } elseif ($published.Count -gt 0) { 'PUBLISHED HISTORY' } elseif ($discarded.Count -gt 0) { 'DISCARDED HISTORY' } else { 'NO VERSIONS' }
    }
}

function Format-LeavePolicyVersionDiagnostics {
    param([object[]]$Versions)

    if ($null -eq $Versions -or $Versions.Count -eq 0) { return 'none' }
    return (@($Versions | Sort-Object @{ Expression = { [int]$_.versionNumber }; Descending = $true } | ForEach-Object {
        "$($_.id) v$($_.versionNumber) $($_.status) [$($_.effectiveFrom)..$($_.effectiveTo)]"
    }) -join ', ')
}

function Resolve-DeterministicLeavePolicyVersion {
    param([AllowNull()][object]$VersionsData)

    $normalized = Get-UniqueLeavePolicyVersions $VersionsData
    if ($normalized.MissingIdentityCount -gt 0) {
        throw "STOP: deterministic leave policy version response contained $($normalized.MissingIdentityCount) item(s) without a persisted version id; no version was changed."
    }
    $versions = @($normalized.Items)
    if ($versions.Count -eq 0) {
        return [pscustomobject]@{ Version = $null; Kind = 'None'; RawCount = 0; DistinctCount = 0; DuplicateCount = $normalized.DuplicateCount; Diagnostics = 'none' }
    }

    $drafts = @($versions | Where-Object { [string]$_.status -ceq 'Draft' })
    $published = @($versions | Where-Object { [string]$_.status -ceq 'Published' })
    if (@($versions | Where-Object { [string]$_.status -notin @('Draft', 'Published', 'Retired', 'Discarded') }).Count -gt 0) {
        throw "STOP: deterministic leave policy returned an unknown version status; versions: $(Format-LeavePolicyVersionDiagnostics $versions)"
    }
    if ($drafts.Count -gt 1) {
        throw "STOP: deterministic leave policy has multiple competing Draft versions; no version was changed. Versions: $(Format-LeavePolicyVersionDiagnostics $drafts)"
    }

    # A published history is normal. The API contract orders by VersionNumber, but
    # selection remains explicit and refuses an ambiguous current version.
    $latestPublished = $null
    if ($published.Count -gt 0) {
        $maxNumber = ($published | Measure-Object -Property versionNumber -Maximum).Maximum
        $latest = @($published | Where-Object { [int]$_.versionNumber -eq [int]$maxNumber })
        if ($latest.Count -ne 1) {
            throw "STOP: deterministic leave policy has conflicting current Published versions (same version number); no version was changed. Versions: $(Format-LeavePolicyVersionDiagnostics $latest)"
        }
        $latestPublished = $latest[0]
    }

    if ($drafts.Count -eq 1) {
        return [pscustomobject]@{ Version = $drafts[0]; Kind = 'Draft'; RawCount = $normalized.Raw.Count; DistinctCount = $versions.Count; DuplicateCount = $normalized.DuplicateCount; PublishedHistory = $published; Diagnostics = (Format-LeavePolicyVersionDiagnostics $versions) }
    }
    if ($published.Count -eq 0 -and @($versions | Where-Object { [string]$_.status -ceq 'Discarded' }).Count -gt 0) {
        throw "STOP: deterministic leave policy has only Discarded history; no new version was created. An explicit production decision is required. Versions: $(Format-LeavePolicyVersionDiagnostics $versions)"
    }
    return [pscustomobject]@{ Version = $latestPublished; Kind = 'Published'; RawCount = $normalized.Raw.Count; DistinctCount = $versions.Count; DuplicateCount = $normalized.DuplicateCount; PublishedHistory = @($published); Diagnostics = (Format-LeavePolicyVersionDiagnostics $versions) }
}
