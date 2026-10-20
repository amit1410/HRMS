[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ApprovedCertificatePath
)

$ErrorActionPreference = 'Stop'

function Add-Result {
    param([string]$Name, [ValidateSet('PASS', 'FAIL', 'BLOCKED')][string]$Status, [string]$Detail)
    [pscustomobject]@{ Check = $Name; Status = $Status; Detail = $Detail }
}

function Get-JsonObject {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json) }
    catch { return $null }
}

function Get-ConfigValue {
    param($Object, [string]$ColonKey)
    $current = $Object
    foreach ($part in $ColonKey.Split(':')) {
        if ($null -eq $current) { return $null }
        if ($current -is [System.Collections.IDictionary]) {
            if (-not $current.Contains($part)) { return $null }
            $current = $current[$part]
        } else {
            $property = $current.PSObject.Properties[$part]
            if ($null -eq $property) { return $null }
            $current = $property.Value
        }
    }
    return $current
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$project = Join-Path $root 'Backend/HRMS.MigrationHost/HRMS.MigrationHost.csproj'
$apiConfig = Join-Path $root 'Backend/HRMS.API/appsettings.json'
$environmentName = [Environment]::GetEnvironmentVariable('DOTNET_ENVIRONMENT')
if ([string]::IsNullOrWhiteSpace($environmentName)) { $environmentName = 'Production' }
$environmentConfig = Join-Path $root "Backend/HRMS.API/appsettings.$environmentName.json"
$results = [System.Collections.Generic.List[object]]::new()

$cwd = [IO.Path]::GetFullPath((Get-Location).Path)
$expectedConfigPath = Join-Path $cwd 'Backend/HRMS.API/appsettings.json'
if ($cwd -eq $root -and (Test-Path -LiteralPath $expectedConfigPath -PathType Leaf)) {
    $results.Add((Add-Result 'MigrationHost working directory' PASS 'Current directory is the repository root; repository appsettings discovery resolves as expected.'))
} else {
    $results.Add((Add-Result 'MigrationHost working directory' FAIL 'Current directory does not resolve the repository appsettings path used by MigrationHost.'))
}

if ((Test-Path -LiteralPath $apiConfig -PathType Leaf) -and (Test-Path -LiteralPath $project -PathType Leaf)) {
    $results.Add((Add-Result 'API configuration discovery' PASS 'Base API appsettings file and MigrationHost project metadata are present.'))
} else {
    $results.Add((Add-Result 'API configuration discovery' FAIL 'Expected API appsettings or MigrationHost project file is absent.'))
}

$base = Get-JsonObject $apiConfig
$envConfig = Get-JsonObject $environmentConfig
if ((Test-Path -LiteralPath $environmentConfig -PathType Leaf) -and $null -eq $envConfig) {
    $results.Add((Add-Result 'Environment configuration JSON' FAIL 'The selected environment appsettings file exists but is not valid JSON.'))
} elseif (Test-Path -LiteralPath $environmentConfig -PathType Leaf) {
    $results.Add((Add-Result 'Environment configuration JSON' PASS 'Selected environment appsettings file is readable JSON.'))
} else {
    $results.Add((Add-Result 'Environment configuration JSON' PASS 'No environment-specific file is present; MigrationHost treats this source as optional.'))
}

$required = @('Database:Provider', 'ConnectionStrings:Catalog', 'Sharding:MySqlConnectionStringTemplate', 'MigrationHost:AllowedCatalogs')
$missing = [System.Collections.Generic.List[string]]::new()
$deploymentUnknown = $false
foreach ($key in $required) {
    $envKey = $key.Replace(':', '__')
    $value = [Environment]::GetEnvironmentVariable($envKey)
    if ([string]::IsNullOrWhiteSpace([string]$value)) { $value = Get-ConfigValue $envConfig $key }
    if ([string]::IsNullOrWhiteSpace([string]$value)) { $value = Get-ConfigValue $base $key }
    if ($key -eq 'MigrationHost:AllowedCatalogs' -and $null -ne $value -and $value.Count -eq 0) { $value = $null }
    if ([string]::IsNullOrWhiteSpace([string]$value)) { $missing.Add($key) }
}
if ($missing.Count -eq 0) {
    $results.Add((Add-Result 'Required configuration key presence' PASS 'Required keys are present in the local configuration sources; values were not displayed.'))
} else {
    $deploymentUnknown = $true
    $results.Add((Add-Result 'Required configuration key presence' BLOCKED ("Missing or empty in this shell and repository files: " + ($missing -join ', ') + '. This does not establish deployment configuration state.')))
}
$results.Add((Add-Result 'Deployment configuration visibility' BLOCKED 'Offline inspection cannot determine environment variables or mounted files in the actual deployment.'))

$provider = Get-ConfigValue $envConfig 'Database:Provider'
if ([string]::IsNullOrWhiteSpace([string]$provider)) { $provider = Get-ConfigValue $base 'Database:Provider' }
if ($provider -eq 'MySql') {
    $results.Add((Add-Result 'Configured provider' PASS 'Local API configuration selects MySql.'))
} else {
    $results.Add((Add-Result 'Configured provider' BLOCKED 'Local configuration does not establish MySql as the provider; deployment overrides may differ.'))
}

try {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $package = $projectXml.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'MySql.EntityFrameworkCore' } | Select-Object -First 1
    if ($package -and $package.Version) {
        $results.Add((Add-Result 'MySQL EF provider version' PASS ("Project metadata pins MySql.EntityFrameworkCore version " + [string]$package.Version + '.')))
    } else {
        $results.Add((Add-Result 'MySQL EF provider version' BLOCKED 'A direct version was not found in the MigrationHost project metadata.'))
    }
} catch {
    $results.Add((Add-Result 'MySQL EF provider version' FAIL 'MigrationHost project metadata could not be parsed.'))
}

$assetsPath = Join-Path $root 'Backend/HRMS.MigrationHost/obj/project.assets.json'
$connectorVersion = $null
if (Test-Path -LiteralPath $assetsPath -PathType Leaf) {
    try {
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        $connector = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^MySqlConnector/' } | Select-Object -First 1
        if ($connector) { $connectorVersion = $connector.Split('/')[1] }
    } catch { }
}
if ($connectorVersion) {
    $results.Add((Add-Result 'Resolved MySQL connector metadata' PASS ("Local NuGet assets identify MySqlConnector version $connectorVersion.")))
} else {
    $results.Add((Add-Result 'Resolved MySQL connector metadata' BLOCKED 'Local project.assets.json does not establish a resolved MySqlConnector version.'))
}

if ([string]::IsNullOrWhiteSpace($ApprovedCertificatePath)) {
    $results.Add((Add-Result 'Public certificate metadata' BLOCKED 'No explicitly approved certificate path was supplied; no certificate files were inspected.'))
} elseif (-not (Test-Path -LiteralPath $ApprovedCertificatePath -PathType Leaf)) {
    $results.Add((Add-Result 'Public certificate metadata' FAIL 'The explicitly approved certificate path is not an accessible file.'))
} else {
    try {
        if ([IO.Path]::GetExtension($ApprovedCertificatePath) -ine '.pem') { throw 'Only PEM public certificate files are accepted.' }
        $insideCertificate = $false
        $certificateLines = [System.Collections.Generic.List[string]]::new()
        foreach ($line in [IO.File]::ReadLines($ApprovedCertificatePath)) {
            if ($line -eq '-----BEGIN CERTIFICATE-----') { $insideCertificate = $true; continue }
            if ($line -eq '-----END CERTIFICATE-----') { break }
            if ($insideCertificate) { $certificateLines.Add($line.Trim()) }
        }
        if ($certificateLines.Count -eq 0) { throw 'No public certificate PEM block was found.' }
        $rawCertificate = [Convert]::FromBase64String(($certificateLines -join ''))
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($rawCertificate)
        $hasPrivateKey = $cert.HasPrivateKey
        $subject = $cert.Subject
        $notAfter = $cert.NotAfter.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        $cert.Dispose()
        if ($hasPrivateKey) {
            $results.Add((Add-Result 'Public certificate metadata' FAIL 'The supplied certificate includes a private key; certificate material was not output.'))
        } else {
            $results.Add((Add-Result 'Public certificate metadata' PASS ("Public certificate metadata read only (subject: $subject; expires UTC: $notAfter).")))
        }
    } catch {
        $results.Add((Add-Result 'Public certificate metadata' FAIL 'The approved path could not be parsed as a public certificate. No contents were output.'))
    }
}

$results.Add((Add-Result 'TLS behavior equivalence' BLOCKED 'No failure is inferred from API Development TLS normalization differences; deployment requirements for identical behavior are not established.'))

$results | Format-Table -AutoSize -Wrap
if ($results.Status -contains 'FAIL') { exit 1 }
if ($results.Status -contains 'BLOCKED') { exit 2 }
exit 0
