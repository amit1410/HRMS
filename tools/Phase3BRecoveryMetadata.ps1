Set-StrictMode -Version Latest

function Resolve-Phase3BStatePath {
    param([AllowNull()][string] $StatePath, [Parameter(Mandatory)][string] $ScriptRoot)
    if ([string]::IsNullOrWhiteSpace($StatePath)) {
        return [IO.Path]::GetFullPath((Join-Path -Path $ScriptRoot -ChildPath 'phase3b-browser-state.json'))
    }
    return [IO.Path]::GetFullPath($StatePath)
}

function Get-RequiredPhase3BProperty {
    param([Parameter(Mandatory)][object] $Object, [Parameter(Mandatory)][string] $ObjectName, [Parameter(Mandatory)][string] $PropertyName)
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) { throw "Required $ObjectName property '$PropertyName' is missing." }
    if ($property.Value -is [string] -and [string]::IsNullOrWhiteSpace($property.Value)) { throw "Required $ObjectName property '$PropertyName' is empty." }
    return $property.Value
}

function Get-RequiredPhase3BApiPort {
    param([Parameter(Mandatory)][object] $ProcessState)
    $value = Get-RequiredPhase3BProperty $ProcessState 'process-state' 'apiPort'
    $port = 0
    if (-not [int]::TryParse([string]$value, [ref]$port) -or $port -lt 1024 -or $port -gt 65535) {
        throw "Required process-state property 'apiPort' must be a valid TCP port (1024-65535)."
    }
    return $port
}

function Assert-Phase3BOwnership {
    param(
        [Parameter(Mandatory)][string] $StatePath,
        [Parameter(Mandatory)][string] $ExpectedServer,
        [Parameter(Mandatory)][string] $ExpectedRunId,
        [Parameter(Mandatory)][string] $ExpectedCatalog,
        [Parameter(Mandatory)][string[]] $ExpectedTenants
    )
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) { throw "Acceptance state file is missing: $StatePath" }
    $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    $stateRunId = [string](Get-RequiredPhase3BProperty $state 'state' 'runId')
    $stateServer = [string](Get-RequiredPhase3BProperty $state 'state' 'server')
    $stateManifestValue = [string](Get-RequiredPhase3BProperty $state 'state' 'manifestPath')
    $stateCatalog = [string](Get-RequiredPhase3BProperty $state 'state' 'catalogDatabase')
    $stateTenantValue = Get-RequiredPhase3BProperty $state 'state' 'tenantDatabases'
    $stateTenants = @($stateTenantValue)
    if ($stateTenants.Count -ne $ExpectedTenants.Count) { throw "Required state property 'tenantDatabases' has an unexpected count." }

    $manifestPath = [IO.Path]::GetFullPath($stateManifestValue)
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Acceptance ownership manifest is missing: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifestRunId = [string](Get-RequiredPhase3BProperty $manifest 'manifest' 'runId')
    $manifestServer = [string](Get-RequiredPhase3BProperty $manifest 'manifest' 'server')
    $manifestCatalog = [string](Get-RequiredPhase3BProperty $manifest 'manifest' 'catalogDatabase')
    $manifestTenantValue = Get-RequiredPhase3BProperty $manifest 'manifest' 'tenantDatabases'
    $manifestTenants = @($manifestTenantValue)
    $manifestDatabaseValue = Get-RequiredPhase3BProperty $manifest 'manifest' 'databases'
    $manifestDatabases = @($manifestDatabaseValue)

    if ($stateRunId -cne $ExpectedRunId -or $manifestRunId -cne $ExpectedRunId) { throw 'Run ownership does not match the authorized run.' }
    if ($stateServer -cne $ExpectedServer -or $manifestServer -cne $ExpectedServer) { throw 'Server ownership does not match the authorized server.' }
    if ($stateCatalog -cne $ExpectedCatalog -or $manifestCatalog -cne $ExpectedCatalog) { throw 'Catalog ownership does not match the authorized catalog.' }
    if ((Compare-Object $stateTenants $ExpectedTenants) -or (Compare-Object $manifestTenants $ExpectedTenants)) { throw 'Tenant database ownership does not match the authorized set.' }
    if ((Compare-Object $manifestDatabases (@($ExpectedCatalog) + $ExpectedTenants))) { throw 'Manifest database set is not exactly the authorized set.' }

    $processStatePath = Join-Path -Path (Split-Path -Parent $StatePath) -ChildPath 'phase3b-browser-state.processes.json'
    if (-not (Test-Path -LiteralPath $processStatePath -PathType Leaf)) { throw "Acceptance process-state metadata is missing: $processStatePath" }
    $processState = Get-Content -LiteralPath $processStatePath -Raw | ConvertFrom-Json
    $processStatePathValue = [string](Get-RequiredPhase3BProperty $processState 'process-state' 'statePath')
    $processManifestValue = [string](Get-RequiredPhase3BProperty $processState 'process-state' 'manifestPath')
    if ([IO.Path]::GetFullPath($processStatePathValue) -cne [IO.Path]::GetFullPath($StatePath) -or [IO.Path]::GetFullPath($processManifestValue) -cne $manifestPath) { throw 'Process-state metadata does not belong to the validated state and manifest.' }
    [pscustomobject]@{ State = $state; Manifest = $manifest; ManifestPath = $manifestPath; ProcessState = $processState }
}
