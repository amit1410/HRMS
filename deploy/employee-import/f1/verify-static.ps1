$ErrorActionPreference = 'Stop'

$composePath = Join-Path $PSScriptRoot 'docker-compose.f1.yml'
$bootstrapPath = Join-Path $PSScriptRoot 'create-private-bucket.sh'
$compose = Get-Content -LiteralPath $composePath -Raw
$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw

$requiredComposePatterns = @(
    '(?m)^name:\s*\$\{HRMS_F1_PROJECT:\?'
    'image:\s*mysql:8\.4\.13'
    'image:\s*ghcr\.io/coollabsio/minio:RELEASE\.2025-10-15T17-29-55Z@sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9'
    '127\.0\.0\.1:\$\{HRMS_F1_MYSQL_HOST_PORT'
    '127\.0\.0\.1:\$\{HRMS_F1_MINIO_API_HOST_PORT'
    '127\.0\.0\.1:\$\{HRMS_F1_MINIO_CONSOLE_HOST_PORT'
    'driver:\s*bridge'
    'com\.docker\.network\.bridge\.enable_ip_masquerade:\s*"false"'
    'MYSQL_ROOT_PASSWORD_FILE:\s*/run/secrets/mysql_root_password'
    'MINIO_ROOT_USER_FILE:\s*/run/secrets/minio_root_user'
    'com\.hrms\.disposable:\s*"true"'
    'name:\s*\$\{HRMS_F1_PROJECT\}_mysql_data'
    'name:\s*\$\{HRMS_F1_PROJECT\}_minio_data'
    'profiles:\s*\["bootstrap"\]'
)

foreach ($pattern in $requiredComposePatterns) {
    if ($compose -notmatch $pattern) { throw "Static isolation check failed: expected Compose control '$pattern'." }
}

if ($compose -match '(?im)^\s*network_mode:\s*host\b|(?im)^\s*external:\s*true\b') {
    throw 'Static isolation check failed: host networking or external Docker resources are configured.'
}
if ($compose -match '(?im)^\s*internal:\s*true\b') {
    throw 'Static networking check failed: a host-published F1 service cannot use a Docker internal-only network.'
}
if ($compose -match '(?i)hrms_catalog|hrms_web1|WEB1|ConnectionStrings|Sharding:|Password\s*=|SecretKey\s*=') {
    throw 'Static isolation check failed: shared database references or literal credentials are present.'
}
if ($bootstrap -match '(?i)AWS_SECRET_ACCESS_KEY\s*=\s*[A-Za-z0-9/+=]{12,}|password\s*=\s*[^"'']+') {
    throw 'Static isolation check failed: a literal secret appears in the bootstrap script.'
}
if ($bootstrap -notmatch 'set -eu' -or $bootstrap -notmatch 'set \+x' -or
    $bootstrap -notmatch 'get-bucket-acl' -or $bootstrap -notmatch 'get-bucket-policy') {
    throw 'Static isolation check failed: bucket bootstrap must be fail-closed and verify public access settings.'
}

Write-Output 'Static checks passed: run-specific bridge network with outbound masquerading disabled, loopback-only published ports, labeled persistent volumes, file-backed secrets, optional bucket bootstrap, and no shared database references or literal secrets.'
