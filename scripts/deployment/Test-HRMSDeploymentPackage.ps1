[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedManifestSHA256
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $package 'MANIFEST.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Package manifest is missing.' }
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if ($manifestHash -ne $ExpectedManifestSHA256.ToUpperInvariant()) { throw 'Package manifest SHA-256 mismatch.' }
$parsedManifest = ConvertFrom-Json (Get-Content -LiteralPath $manifestPath -Raw)
$entries = if ($parsedManifest -is [array]) { $parsedManifest } else { @($parsedManifest) }
if ($entries.Count -eq 0) { throw 'Package manifest is empty.' }
foreach ($entry in $entries) {
    $file = Join-Path $package ([string]$entry.Path)
    $resolved = (Resolve-Path -LiteralPath $file -ErrorAction Stop).Path
    if (-not $resolved.StartsWith($package.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Manifest contains an out-of-package path.'
    }
    if ((Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash -ne [string]$entry.SHA256) {
        throw 'Packaged file SHA-256 mismatch.'
    }
}
if (Get-ChildItem -LiteralPath $package -Filter 'appsettings*.json' -File -Recurse) {
    throw 'Package contains an appsettings file; refuse to use a package with copied configuration.'
}
if (-not (Test-Path -LiteralPath (Join-Path $package 'HRMS.API.dll') -PathType Leaf)) {
    throw 'Package has no HRMS.API.dll entry.'
}
[pscustomobject]@{
    ManifestSHA256 = $manifestHash
    FileCount = $entries.Count
    ConfigurationFilesCopied = $false
    PackageIntegrity = 'PASS'
} | Format-List
