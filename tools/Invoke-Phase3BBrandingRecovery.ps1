[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Check', 'Repair')]
    [string] $Mode,
    [AllowNull()]
    [string] $StatePath = $null,
    [switch] $AllowUntrustedServerCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath 'Phase3BRecoveryMetadata.ps1')
$StatePath = Resolve-Phase3BStatePath -StatePath $StatePath -ScriptRoot $PSScriptRoot

$ExpectedServer = 'localhost,1433'
$ExpectedRunId = '20260903T180116Z_150524'
$ExpectedCatalog = 'HRMS_Phase3B_Integration_20260903T180116Z_150524_catalog'
$ExpectedTenants = @(
    'HRMS_Phase3B_Integration_20260903T180116Z_150524_tenanta',
    'HRMS_Phase3B_Integration_20260903T180116Z_150524_tenantb'
)
$Fixture = @(
    [pscustomobject]@{ Id = 'a5000000-0000-0000-0000-000000000001'; Host = 'tenant-a.localhost'; DisplayName = 'Phase 3B Tenant A'; Color = '#0F766E'; Welcome = 'Sign in to the Phase 3B Tenant A workspace.'; Support = 'support-a@phase3b.test' },
    [pscustomobject]@{ Id = 'b5000000-0000-0000-0000-000000000001'; Host = 'tenant-b.localhost'; DisplayName = 'Phase 3B Tenant B'; Color = '#7C3AED'; Welcome = 'Sign in to the Phase 3B Tenant B workspace.'; Support = 'support-b@phase3b.test' }
)

function Fail([string] $Message) { throw "Branding recovery stopped: $Message" }

if ($env:HRMS_SQLSERVER_TEST_SERVER -cne $ExpectedServer) { Fail 'HRMS_SQLSERVER_TEST_SERVER is not localhost,1433.' }
if ($env:HRMS_SQLSERVER_TEST_AUTH -cne 'Integrated') { Fail 'HRMS_SQLSERVER_TEST_AUTH is not Integrated.' }
try {
    $ownership = Assert-Phase3BOwnership -StatePath $StatePath -ExpectedServer $ExpectedServer -ExpectedRunId $ExpectedRunId -ExpectedCatalog $ExpectedCatalog -ExpectedTenants $ExpectedTenants
}
catch { Fail $_.Exception.Message }
$state = $ownership.State
$manifestPath = $ownership.ManifestPath
$manifest = $ownership.Manifest

if ($AllowUntrustedServerCertificate -and
    ($ExpectedServer -cne 'localhost,1433' -or $ExpectedCatalog -cne 'HRMS_Phase3B_Integration_20260903T180116Z_150524_catalog')) {
    Fail 'The certificate-validation exception is restricted to localhost,1433 and the authorized Phase 3B catalog.'
}

$sqlcmdCommand = Get-Command 'sqlcmd.exe' -ErrorAction SilentlyContinue
if (-not $sqlcmdCommand) { Fail 'sqlcmd.exe is not available.' }
$Sqlcmd = $sqlcmdCommand.Source

$fixtureRows = ($Fixture | ForEach-Object {
    "('$($_.Id)', '$($_.Host)', '$($_.DisplayName.Replace("'", "''"))', '$($_.Color)', '$($_.Welcome.Replace("'", "''"))', '$($_.Support)')"
}) -join ",`n    "

$readSql = @"
SET NOCOUNT ON;
SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS ServerIdentity, DB_NAME() AS DatabaseIdentity;
SELECT CONVERT(varchar(36), t.Id) AS TenantId, t.Host, t.TenantName,
       CASE WHEN b.TenantId IS NULL THEN 'Missing' ELSE 'Present' END AS BrandingState,
       b.IsPublic, b.DisplayName, b.PrimaryColor, b.WelcomeMessage, b.SupportEmail,
       b.SsoEnabled, b.LogoUrl, b.SsoProviderName
FROM dbo.Tenants AS t
LEFT JOIN dbo.TenantBranding AS b ON b.TenantId = t.Id
WHERE t.Id IN ('a5000000-0000-0000-0000-000000000001', 'b5000000-0000-0000-0000-000000000001')
ORDER BY t.Id;
"@

$repairSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Inserted int = 0;
DECLARE @Expected TABLE (TenantId uniqueidentifier NOT NULL PRIMARY KEY, Host nvarchar(256) NOT NULL, DisplayName nvarchar(100) NOT NULL, PrimaryColor nvarchar(7) NOT NULL, WelcomeMessage nvarchar(160) NOT NULL, SupportEmail nvarchar(256) NOT NULL);
INSERT @Expected (TenantId, Host, DisplayName, PrimaryColor, WelcomeMessage, SupportEmail)
VALUES
    $fixtureRows;

BEGIN TRY
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Expected e
        LEFT JOIN dbo.Tenants t WITH (UPDLOCK, HOLDLOCK) ON t.Id = e.TenantId
        WHERE t.Id IS NULL OR t.Host <> e.Host)
        THROW 51001, 'Synthetic tenant identity conflict.', 1;

    DECLARE @TenantId uniqueidentifier, @Host nvarchar(256), @DisplayName nvarchar(100), @Color nvarchar(7), @Welcome nvarchar(160), @Support nvarchar(256);
    DECLARE expected_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TenantId, Host, DisplayName, PrimaryColor, WelcomeMessage, SupportEmail FROM @Expected ORDER BY TenantId;
    OPEN expected_cursor;
    FETCH NEXT FROM expected_cursor INTO @TenantId, @Host, @DisplayName, @Color, @Welcome, @Support;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.TenantBranding WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @TenantId)
        BEGIN
            IF EXISTS (
                SELECT 1 FROM dbo.TenantBranding b
                WHERE b.TenantId = @TenantId AND NOT (
                    b.IsPublic = 1 AND b.DisplayName = @DisplayName AND b.PrimaryColor = @Color AND
                    b.WelcomeMessage = @Welcome AND b.SupportEmail = @Support AND b.SsoEnabled = 0 AND
                    b.LogoUrl IS NULL AND b.SsoProviderName IS NULL))
                THROW 51002, 'Synthetic branding conflict.', 1;
        END
        ELSE
        BEGIN
            INSERT dbo.TenantBranding (TenantId, IsPublic, DisplayName, LogoUrl, PrimaryColor, WelcomeMessage, SupportEmail, SsoEnabled, SsoProviderName)
            VALUES (@TenantId, 1, @DisplayName, NULL, @Color, @Welcome, @Support, 0, NULL);
            SET @Inserted += 1;
        END;
        FETCH NEXT FROM expected_cursor INTO @TenantId, @Host, @DisplayName, @Color, @Welcome, @Support;
    END
    CLOSE expected_cursor;
    DEALLOCATE expected_cursor;
    COMMIT TRANSACTION;
    SELECT @Inserted AS InsertedRows;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local', 'expected_cursor') >= -1 BEGIN CLOSE expected_cursor; DEALLOCATE expected_cursor; END;
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@

function Invoke-Sql([string] $Query) {
    if ($AllowUntrustedServerCertificate) {
        # Explicitly approved local exception: encryption remains mandatory (-N m),
        # while -C trusts the server certificate for this exact connection only.
        $encryptionArguments = @('-Nm', '-C')
    }
    else {
        # Strict encryption with normal certificate validation. Do not add -C here.
        $encryptionArguments = @('-Ns')
    }
    $output = & $Sqlcmd -S $ExpectedServer -d $ExpectedCatalog -E @encryptionArguments -l 10 -b -W -s ' | ' -Q $Query 2>&1
    if ($LASTEXITCODE -ne 0) { Fail (($output | Out-String).Trim()) }
    $output
}

Write-Output "Ownership validated: run $ExpectedRunId; authorized catalog only."
if ($Mode -eq 'Check') {
    Write-Output 'Check mode is read-only.'
    Invoke-Sql $readSql
    exit 0
}

Write-Output 'Repair mode requires explicit invocation and reuses the validated ownership checks.'
Invoke-Sql $repairSql
Write-Output 'Repair committed. Endpoint verification must follow.'

$apiPort = Get-RequiredPhase3BApiPort $ownership.ProcessState
foreach ($item in $Fixture) {
    $headers = @{ Host = $item.Host }
    $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 -Uri "http://127.0.0.1:$apiPort/api/tenants/current/branding" -Headers $headers
    if ($response.StatusCode -ne 200) { Fail "Branding endpoint failed for $($item.Host): HTTP $($response.StatusCode)." }
    $payload = $response.Content | ConvertFrom-Json
    if (-not $payload.success -or [string] $payload.data.displayName -cne $item.DisplayName) { Fail "Branding endpoint display-name verification failed for $($item.Host)." }
    Write-Output "Endpoint verified: $($item.Host) -> $($item.DisplayName)"
}
