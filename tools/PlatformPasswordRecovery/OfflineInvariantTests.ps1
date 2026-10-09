[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$programPath = Join-Path $toolRoot 'Program.cs'
$projectPath = Join-Path $toolRoot 'PlatformPasswordRecovery.csproj'
$designPath = Join-Path (Split-Path -Parent (Split-Path -Parent $toolRoot)) 'docs/platform-password-recovery-activation-design.md'

$program = Get-Content -Raw -LiteralPath $programPath
$project = Get-Content -Raw -LiteralPath $projectPath
$design = Get-Content -Raw -LiteralPath $designPath

function Assert-Contains([string] $text, [string] $needle, [string] $message) {
    if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "FAIL: $message" }
}

function Assert-NotContains([string] $text, [string] $needle, [string] $message) {
    if ($text.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "FAIL: $message" }
}

Assert-Contains $program 'ReadHidden' 'the dry-run must use hidden interactive password input'
Assert-Contains $program 'Password confirmation failed.' 'the dry-run must compare both entries'
Assert-Contains $program 'IdentityPasswordHasher' 'the dry-run must exercise the existing hasher'
Assert-Contains $program 'No database write was attempted.' 'the dry-run must state that it did not write'
Assert-NotContains $program 'SaveChanges' 'the offline utility must not contain a save operation'
Assert-NotContains $program 'ExecuteUpdate' 'the offline utility must not contain an EF update operation'
Assert-NotContains $program 'BeginTransaction' 'the offline utility must not open a transaction'
Assert-NotContains $program 'SmtpEmailSender' 'the offline utility must not contain an SMTP sender'
Assert-NotContains $program 'PlatformUsers' 'the offline utility must not contain catalog reads'
Assert-NotContains $program 'PasswordHash =' 'the dry-run must not assign a password hash'
Assert-NotContains $program 'request.NewPassword' 'the dry-run must not expose an API reset request'
Assert-NotContains $project '<ProjectReference Include="../../Backend/HRMS.API' 'the utility must not reference the API'

foreach ($required in @(
    'overall active `PlatformSuperAdmin` population',
    'backupEvidenceDigest',
    'Two approvals',
    'One-time execution authorization',
    'SecurityRevision = original + 1',
    'No such schema is assumed to exist',
    'migration-history reconciliation'
)) { Assert-Contains $design $required "design must cover: $required" }

Write-Output 'PASS: offline PlatformPasswordRecovery invariants (source/design inspection only).'
