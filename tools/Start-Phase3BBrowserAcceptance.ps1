param(
    [string]$StatePath = (Join-Path $PSScriptRoot 'phase3b-browser-state.json')
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:HRMS_SQLSERVER_TEST_SERVER)) { throw 'Set HRMS_SQLSERVER_TEST_SERVER to a server/instance name first.' }
$env:HRMS_SQLSERVER_TEST_AUTH = 'Integrated'
$state = [IO.Path]::GetFullPath($StatePath)
if (Test-Path -LiteralPath $state) { throw "Refusing existing browser state: $state" }

function Get-FreePort {
    do { $port = Get-Random -Minimum 20000 -Maximum 60000 }
    while (Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue)
    return $port
}

function Wait-ForBranding {
    param(
        [int]$ApiPort,
        [string]$Workspace,
        [string]$ExpectedDisplayName
    )

    $uri = "http://$Workspace.localhost:$ApiPort/api/tenants/current/branding"
    $deadline = (Get-Date).AddSeconds(20)
    do {
        try {
            $response = Invoke-WebRequest -Uri $uri -Method Get -TimeoutSec 3 -UseBasicParsing
            $body = $response.Content | ConvertFrom-Json
            if ($response.StatusCode -eq 200 -and $body.success -eq $true -and
                $body.data.displayName -eq $ExpectedDisplayName) {
                return
            }
        }
        catch {
            # The API may still be binding or opening its already-migrated database.
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Acceptance branding readiness failed for $Workspace.localhost."
}

$apiPort = Get-FreePort
do { $webPort = Get-FreePort } while ($webPort -eq $apiPort)
$manifest = [IO.Path]::ChangeExtension($state, '.manifest.json')
$env:HRMS_PHASE3B_MANIFEST_PATH = $manifest

$projectPath = Join-Path $PSScriptRoot 'Phase3BBrowserAcceptance\Phase3BBrowserAcceptance.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw "Browser acceptance setup project was not found: $projectPath" }
dotnet run --project $projectPath -- setup $state
$setupExitCode = $LASTEXITCODE
if ($setupExitCode -ne 0) { throw "Browser acceptance setup failed with exit code $setupExitCode." }
if (-not (Test-Path -LiteralPath $state -PathType Leaf)) { throw "Browser acceptance setup succeeded but did not create expected state file: $state" }
$run = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
$template = "Server=$($run.server);Database=HRMS_Phase3B_Integration_$($run.runId)_{shardKey};Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$catalog = "Server=$($run.server);Database=$($run.catalogDatabase);Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$apiOrigin = "http://localhost:$apiPort"
$workspaceApi = "http://{workspace}.localhost:$apiPort"
$acceptanceApiOrigins = @("http://tenant-a.localhost:$apiPort", "http://tenant-b.localhost:$apiPort")
$workspaceWeb = "http://{workspace}.localhost:$webPort"

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $apiOrigin
$env:Database__Provider = 'SqlServer'
$env:Database__SkipInitialization = 'true'
$env:ConnectionStrings__Catalog = $catalog
$env:Sharding__ConnectionStringTemplate = $template
$env:Cors__AllowedOrigins__0 = "http://localhost:$webPort"
$env:Cors__WorkspaceOriginTemplates__0 = "http://{workspace}.localhost:$webPort"
$env:Jwt__SecretKey = 'Phase3B-local-only-do-not-reuse-32-bytes-minimum'
$api = $null
$web = $null
try {
    $api = Start-Process dotnet -ArgumentList @('run','--project','Backend/HRMS.API/HRMS.API.csproj','--no-launch-profile') -PassThru -WindowStyle Hidden

    Wait-ForBranding -ApiPort $apiPort -Workspace 'tenant-a' -ExpectedDisplayName $run.tenantBranding.tenantA
    Wait-ForBranding -ApiPort $apiPort -Workspace 'tenant-b' -ExpectedDisplayName $run.tenantBranding.tenantB

$env:VITE_API_BASE_URL = $apiOrigin
$env:VITE_API_ORIGIN_TEMPLATE = $workspaceApi
$env:VITE_API_CSP_CONNECT_SRC = $acceptanceApiOrigins -join ' '
$web = Start-Process npm.cmd -WorkingDirectory (Join-Path $PSScriptRoot '..\Frontend\HRMS.Web') -ArgumentList @('run','dev','--','--host','127.0.0.1','--port',$webPort) -PassThru -WindowStyle Hidden

$browserState = [ordered]@{ statePath=$state; manifestPath=$manifest; apiPort=$apiPort; frontendPort=$webPort; apiPid=$api.Id; apiStartTime=$api.StartTime.ToUniversalTime().ToString('O'); frontendPid=$web.Id; frontendStartTime=$web.StartTime.ToUniversalTime().ToString('O'); workspaceUrls=@("http://tenant-a.localhost:$webPort", "http://tenant-b.localhost:$webPort"); apiUrls=@("http://tenant-a.localhost:$apiPort", "http://tenant-b.localhost:$apiPort") }
$browserState | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($state, '.processes.json'))
$credentials = [IO.Path]::ChangeExtension($state, '.credentials.txt')
@("Tenant A", "actor-a@p3b0.test", "actor-b@p3b0.test", "view-only@p3b0.test", "history@p3b0.test", "manage-only@p3b0.test", "Tenant B", "actor-a@p3b1.test", "actor-b@p3b1.test", "view-only@p3b1.test", "history@p3b1.test", "manage-only@p3b1.test", "Password: $($run.syntheticPassword)") | Set-Content -LiteralPath $credentials
icacls $state /inheritance:r /grant:r "$env:USERNAME:(R,W)" | Out-Null
icacls $credentials /inheritance:r /grant:r "$env:USERNAME:(R,W)" | Out-Null
}
catch {
    foreach ($process in @($api, $web)) { if ($process -and (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue } }
    dotnet run --project $projectPath -- cleanup $state
    throw
}
@"
Phase 3B browser environment is running.
Tenant A: http://tenant-a.localhost:$webPort
Tenant B: http://tenant-b.localhost:$webPort
State: $state
Credentials: derive locally from the synthetic run state; do not paste them into reports.
Run .\Stop-Phase3BBrowserAcceptance.ps1 -StatePath '$state' when finished.
"@
