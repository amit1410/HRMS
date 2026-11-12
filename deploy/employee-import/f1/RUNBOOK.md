# Stage F1 disposable local acceptance environment

This folder prepares a local MySQL and MinIO test stack. It does not start containers, create databases, apply migrations, enable the HRMS worker, or connect to any existing HRMS environment.

## Current machine findings

Current F1 Docker configuration verification (2026-10-28):

- The operator reports Docker Engine 29.9.2, Docker Compose v5.6.1, and a Linux x86_64 engine are operational.
- `docker compose ... config -q` passed with a disposable run ID and non-secret placeholder paths. This validates only Compose interpolation/model; it starts no containers and creates no volumes.
- Metadata resolved for `mysql:8.4.13` and `amazon/aws-cli:2.38.6`.
- Anonymous manifest inspection for the original Quay MinIO image returned HTTP 401. By explicit operator approval for run `test20261028a`, Compose now references the digest-pinned Coollabs image shown below, only for isolated disposable functional tests with synthetic data. This approval does not authorize image pull, container startup, provider tests, or production/security use.
- No secrets, databases, migrations, or services were created or used during configuration verification.

## MinIO image review and run-specific restriction

The original `quay.io/minio/minio:RELEASE.2025-09-06T17-38-46Z` returned HTTP 401 to anonymous manifest inspection. For the explicitly approved run `test20261028a`, the Compose file now uses this digest-pinned third-party image:

Candidate for isolated disposable functional testing only:

```yaml
image: ghcr.io/coollabsio/minio:RELEASE.2025-10-15T17-29-55Z@sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9
```

This is a third-party Coollabs build. Its public source repository describes builds from official MinIO release source; its Dockerfile checks out a MinIO release, compiles it, uses the upstream Docker entrypoint, and publishes AGPL-3.0 metadata. The exact digest is published in GHCR metadata. The build recipe uses mutable builder/base tags, no image vulnerability report or signed build provenance was verified here, and the Dockerfile's broad `/usr/bin` permissions are a hardening concern. Treat the digest as content-pinned but not as proof of trusted provenance.

MinIO's October 2025 release fixed CVE-2025-62506. Later MinIO advisories identify high-severity authentication-bypass/object-write issues affecting open-source releases, including releases through the last open-source release; the documented fixes are in later AIStor releases. Therefore this candidate and the older pinned image are **not security-qualified**. The candidate may be used only for an explicitly approved, local, isolated, disposable **functional** acceptance run with synthetic data. Do not use it for production, security qualification, real employee information, or any environment reachable from untrusted networks. Its liveness endpoint is `/minio/health/live`; the F1 Compose service currently has no MinIO healthcheck, and bucket bootstrap performs bounded S3 readiness polling.

The source-level runtime contract is compatible with the Compose command `server /data --console-address ":9001"`, `/data` volume, 9000/9001 service ports, and `MINIO_ROOT_USER_FILE` / `MINIO_ROOT_PASSWORD_FILE`. The S3 API supports the bucket bootstrap and HRMS S3 client flows. HRMS AES-GCM encryption and tamper verification are application-level behaviors; testing them against this server does not establish the server's security.

The operator approved this image for configuration preparation only. This is not approval to pull it, start containers, execute provider tests, or use it for production or security qualification. Separate approvals are required. After the operator approves pull and startup, the image commands are:

```powershell
docker compose -p $env:HRMS_F1_PROJECT -f deploy/employee-import/f1/docker-compose.f1.yml pull minio
docker compose -p $env:HRMS_F1_PROJECT -f deploy/employee-import/f1/docker-compose.f1.yml up -d mysql minio
```

Before using those commands, apply the approved digest-pinned line, verify the process-local run/project/secret-path configuration, and review the resolved Compose model. Pulling and startup require separate explicit operator approval. Do not run the bootstrap profile, create databases, apply migrations, or enable the worker as part of these image steps.

Docker installation and WSL setup are no longer prerequisites according to the operator's report. Pulling images, creating local secret files, starting containers, creating databases, and applying migrations remain separately approved actions. Do not replace the MinIO image automatically.

## Repository harness contract

The repository has two separate gated entry points. `MySql_WelcomeInvitation_acceptance` is the legacy WelcomeInvitation runner and is not the F1 employee-import acceptance suite. The dedicated `MySql_schema_and_minio_provider_acceptance` runner and `MySql_cleanup_current_owned_resources` cleanup runner use the three-database Employee Bulk Import ownership model documented below. Do not set legacy `HRMS_PHASE1E_*` values to run F1.

The harness requires all of these environment variables for MySQL:

| Variable | Required value/meaning |
|---|---|
| `HRMS_PHASE1E_EXECUTE` | Exactly `true`; activates the gated provider entry point. |
| `HRMS_PHASE1E_PROVIDER` | Exactly `MySql`. |
| `HRMS_PHASE1E_SQLSERVER_CONNECTION` | SQL Server counterpart used by the SQL Server entry point; leave unset for this MySQL-only run. |
| `HRMS_PHASE1E_MYSQL_CONNECTION` | Test-only MySQL admin connection string. The runner reads it directly; never echo it or place it in source control. |
| `HRMS_PHASE1E_SERVER_IDENTITY` | Exact expected result of `SELECT @@hostname`; set by the disposable MySQL container hostname. |
| `HRMS_PHASE1E_INSTANCE_OR_PORT` | Exact expected `@@port`, which is `3306` inside this container. The host-mapped port is not this value. |
| `HRMS_PHASE1E_DATABASE_NAMES` | Exactly two comma-separated, unique names containing the run ID. Names cannot be protected names and cannot contain `catalog` or `tenant`. |
| `HRMS_PHASE1E_RUN_ID` | Unique run ID, at least 12 characters, no spaces. Use lowercase letters, digits and hyphens. |
| `HRMS_PHASE1E_MANIFEST_PATH` | Absolute path to a new run-specific manifest file in a private artifacts directory. It must not already exist. The runner derives acceptance and migration-down manifests beside it. |
| `HRMS_PHASE1E_ENVIRONMENT` | Exactly `isolated-provider-acceptance`. |
| `HRMS_PHASE1E_TEST_ONLY_CONFIGURATION` | Non-empty test-only label; it must not contain `Password=` or `Pwd=`. |
| `HRMS_PHASE1E_APPROVE_CONNECTION` | Must be `true` only after explicit operator authorization to connect to the disposable server. |
| `HRMS_PHASE1E_APPROVE_CREATE` | Must be `true` only after explicit authorization to create the exact disposable databases. |
| `HRMS_PHASE1E_APPROVE_MIGRATION` | Must be `true` only after explicit authorization to apply and roll back migrations on those exact databases. |
| `HRMS_PHASE1E_APPROVE_DELETE` | Must be `true` only after explicit authorization to delete those exact disposable databases after the ownership checks pass. |
| `HRMS_PHASE1E_RESULT_PATH` | Absolute path to a private run-specific result JSON path. |

The harness refuses existing databases, verifies exact server identity and port before creation, checks run/provider/database/manifest/environment allowlists, and records both a durable JSON manifest and a database-side ownership marker. It rejects the protected names `BAD1`, `MH01`, `HRMS_Phase4_Leave_MySql_Test`, `hrms_tenant`, `master`, `model`, `msdb`, `tempdb`, and `HRMS`; it also rejects any database name containing `catalog` or `tenant`. Each allowed name must contain the unique run ID and be a valid MySQL identifier no longer than 64 characters. The marker has the form `HRMS-DISPOSABLE|MySql|<server identity>|<run ID>|<database name>`.

Cleanup requires explicit delete approval, exact manifest equality, exact server/port verification, a successful migration manifest, an exact approved database name, and a matching database ownership marker. The manifest records provider, run ID, server, database name, creation time, ownership marker, creation/migration success, and cleanup eligibility. A failed or incomplete migration is not automatically cleanup-eligible. Do not manually drop a database or volume to bypass a failed guard.

The legacy `HRMS_PHASE1E_*` harness remains documented above for its WelcomeInvitation test only. Employee Bulk Import acceptance uses the separate `HRMS_EMPLOYEE_IMPORT_F1_*` gate and three-database ownership model below.

## Prerequisites (operator action; not performed here)

1. Confirm Docker Desktop is using its local Linux container engine and no remote Docker context is active. The Compose network is internal and service ports bind only to loopback.
2. Review image provenance and security. MySQL `mysql:8.4.13` and AWS CLI `amazon/aws-cli:2.38.6` metadata resolved in static registry checks. The MinIO image remains `quay.io/minio/minio:RELEASE.2025-09-06T17-38-46Z`, but anonymous manifest inspection returned HTTP 401. **This exact image is not verified as accessible.** A matching server-binary release does not prove that a container image exists. The GHCR reference above is only a review candidate, not a trusted automatic replacement. Do not replace with `latest`; stop before startup until an operator approves an image source and immutable digest.
3. Choose a unique run ID, project name, MySQL container hostname and bucket name. Confirm the chosen host ports are free before startup.

## Generate test-only secrets locally

Do this only after approval to create local test secrets. Keep the directory outside the repository, protect it to the current Windows user, and do not paste secret contents into chat, terminal output, shell history or logs. These commands create new files but are deliberately not run by this preparation task.

```powershell
$runId = 'f1-<unique-lowercase-run-id>'
$secretDir = Join-Path $env:USERPROFILE "hrms-f1-secrets\$runId"
New-Item -ItemType Directory -Path $secretDir -Force | Out-Null
icacls $secretDir /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" | Out-Null

function Write-RandomBase64Secret([string]$Path, [int]$ByteCount) {
  $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes($ByteCount)
  try { [System.IO.File]::WriteAllText($Path, [Convert]::ToBase64String($bytes), [System.Text.UTF8Encoding]::new($false)) }
  finally { [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes) }
}

function Write-RandomHexSecret([string]$Path, [int]$ByteCount) {
  $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes($ByteCount)
  try { [System.IO.File]::WriteAllText($Path, [Convert]::ToHexString($bytes).ToLowerInvariant(), [System.Text.UTF8Encoding]::new($false)) }
  finally { [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes) }
}

Write-RandomBase64Secret (Join-Path $secretDir 'mysql-root-password') 48
Write-RandomHexSecret (Join-Path $secretDir 'minio-root-user') 20
Write-RandomBase64Secret (Join-Path $secretDir 'minio-root-password') 48
Write-RandomBase64Secret (Join-Path $secretDir 'employee-import-key-active') 32
Write-RandomBase64Secret (Join-Path $secretDir 'employee-import-key-previous') 32
```

The two distinct 32-byte encryption keys must remain outside Docker logs and the repository. The isolated HRMS test host needs a private configuration file containing both Base64 values under `EmployeeImportStorage:Encryption:Keys:<current-key-id>` and `...:Keys:<previous-key-id>`, with `ActiveKeyId` set to the current ID. Use S3 provider, the run-specific bucket, `http://127.0.0.1:<MinIO API host port>`, region `us-east-1`, `UseTls=false`, `AllowInsecureDevelopmentEndpoint=true`, `Credentials:Mode=Static`, and the two test-only MinIO credentials. This HTTP exception is only for Development tests against loopback. Never use these settings in production.

## F1 storage configuration (operator example; not executed)

The gate variable `HRMS_EMPLOYEE_IMPORT_F1_STORAGE_CONFIGURATION` is an **absolute path** to a private JSON configuration file. The runner loads this file with `AddJsonFile`; it is not a JSON-valued environment variable. Required JSON settings are:

| JSON path | Required value/source |
|---|---|
| `EmployeeImportStorage:Provider` | `S3` |
| `EmployeeImportStorage:RetentionDays` | Positive integer; use `30` for this disposable run. |
| `EmployeeImportStorage:S3:Bucket` | Exactly `hrms-employee-import-test20261028a`, matching run ID `test20261028a`. |
| `EmployeeImportStorage:S3:Endpoint` | `http://127.0.0.1:19000` |
| `EmployeeImportStorage:S3:Region` | `us-east-1` (must be non-empty; also matches the bucket-init AWS region). |
| `EmployeeImportStorage:S3:UseTls` | `false` |
| `EmployeeImportStorage:S3:AllowInsecureDevelopmentEndpoint` | `true`; allowed only because the isolated runner uses Development and loopback. |
| `EmployeeImportStorage:S3:Credentials:Mode` | `Static` |
| `EmployeeImportStorage:S3:Credentials:AccessKey` | Contents of `minio-root-user` (read by the script, never displayed). |
| `EmployeeImportStorage:S3:Credentials:SecretKey` | Contents of `minio-root-password` (read by the script, never displayed). |
| `EmployeeImportStorage:Encryption:ActiveKeyId` | A configured key ID, for example `test20261028a-active`. |
| `EmployeeImportStorage:Encryption:Keys:<active-id>` | Base64 contents of `employee-import-key-active`. |
| `EmployeeImportStorage:Encryption:Keys:<previous-id>` | Base64 contents of `employee-import-key-previous`, with a different key ID. |

The cipher decodes each configured key with `Convert.FromBase64String` and requires exactly 32 decoded bytes. The existing secret generator writes Base64 of 32 random bytes, so the expected representation matches these files. The runner requires at least two keys and rotates to the other configured key during acceptance. Do not decode/re-encode, print, or regenerate the keys.

This example reads existing test-only files, creates a **new** secret-bearing JSON file outside the repository, and sets only the configuration-path variable in the current PowerShell process. It refuses to overwrite an existing config. Review the target path and ACL before running it; the JSON contains credential/key values in plaintext form and must remain protected as a secret. The example is intentionally not executed by this task:

```powershell
$runId = 'test20261028a'
$secretDir = 'C:\Users\amit\hrms-f1-secrets\test20261028a'
$configPath = Join-Path $secretDir 'employee-import-storage-test20261028a.json'
if (Test-Path -LiteralPath $configPath) { throw 'Storage config already exists; refusing to overwrite it.' }

$activeKeyId = "$runId-active"
$previousKeyId = "$runId-previous"
$settings = @{
  EmployeeImportStorage = @{
    Provider = 'S3'
    RetentionDays = 30
    S3 = @{
      Bucket = "hrms-employee-import-$runId"
      Endpoint = 'http://127.0.0.1:19000'
      Region = 'us-east-1'
      UseTls = $false
      AllowInsecureDevelopmentEndpoint = $true
      Credentials = @{
        Mode = 'Static'
        AccessKey = [IO.File]::ReadAllText((Join-Path $secretDir 'minio-root-user')).Trim()
        SecretKey = [IO.File]::ReadAllText((Join-Path $secretDir 'minio-root-password')).Trim()
      }
    }
    Encryption = @{
      ActiveKeyId = $activeKeyId
      Keys = @{
        $activeKeyId = [IO.File]::ReadAllText((Join-Path $secretDir 'employee-import-key-active')).Trim()
        $previousKeyId = [IO.File]::ReadAllText((Join-Path $secretDir 'employee-import-key-previous')).Trim()
      }
    }
  }
}
$json = $settings | ConvertTo-Json -Depth 8
$utf8NoBom = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($configPath, $json, $utf8NoBom)
icacls $configPath /inheritance:r /grant:r "$($env:USERNAME):(F)" | Out-Null
$env:HRMS_EMPLOYEE_IMPORT_F1_STORAGE_CONFIGURATION = $configPath
Remove-Variable settings,json,activeKeyId,previousKeyId,utf8NoBom
```

When the config is prepared, separately verify that `EmployeeImportStorage:S3:Bucket`, endpoint port, and key ring match the F1 run ID, `HRMS_EMPLOYEE_IMPORT_F1_MINIO_PORT`, and the two existing key files. Do not echo the expanded configuration or include it in logs.

## Configure and statically inspect Compose

Set only non-secret identifiers and secret file paths in the PowerShell process that will run Compose. Do not create a committed `.env` file. All file path variables must be absolute. This unique lowercase example is for static configuration validation; the secret paths point at this non-secret runbook, so it does not create secret files. Use a fresh ID for an approved real run:

```powershell
$runId = 'f1-20261028-configcheck-7c1e9a2b'
$env:HRMS_F1_PROJECT = "hrms-f1-$runId"
$env:HRMS_F1_RUN_ID = $runId
$env:HRMS_F1_MYSQL_HOSTNAME = 'hrmsf1mysqlf120261028configcheck7c1e9a2b'
$env:HRMS_F1_BUCKET = 'hrms-employee-import-f1-20261028-configcheck-7c1e9a2b'
$nonSecretPlaceholder = (Resolve-Path (Join-Path $PWD 'RUNBOOK.md')).Path
$env:HRMS_F1_MYSQL_PASSWORD_FILE = $nonSecretPlaceholder
$env:HRMS_F1_MINIO_USER_FILE = $nonSecretPlaceholder
$env:HRMS_F1_MINIO_PASSWORD_FILE = $nonSecretPlaceholder
$env:HRMS_F1_MYSQL_HOST_PORT = '13307'
$env:HRMS_F1_MINIO_API_HOST_PORT = '19000'
$env:HRMS_F1_MINIO_CONSOLE_HOST_PORT = '19001'
```

Before any startup, from this directory:

```powershell
./verify-static.ps1
docker compose -f docker-compose.f1.yml config -q
```

Required Compose variables and constraints:

| Variable | Constraint |
|---|---|
| `HRMS_F1_PROJECT` | Required, unique lowercase Compose project identifier. Use `hrms-f1-<run-id>`; project-derived volume/network names inherit it. |
| `HRMS_F1_RUN_ID` | Required unique lowercase run ID; use ASCII `a-z`, digits and hyphens and match the acceptance-gate run ID. |
| `HRMS_F1_MYSQL_HOSTNAME` | Required unique DNS-safe hostname. For F1 use exactly `hrmsf1mysql` plus the run ID with hyphens removed. |
| `HRMS_F1_BUCKET` | Required unique S3 DNS bucket name: lowercase letters, digits and hyphens; starts/ends alphanumeric; max 63 characters. |
| `HRMS_F1_MYSQL_PASSWORD_FILE` | Required absolute path to the disposable MySQL password file. |
| `HRMS_F1_MINIO_USER_FILE` | Required absolute path to the disposable MinIO root-user file. |
| `HRMS_F1_MINIO_PASSWORD_FILE` | Required absolute path to the disposable MinIO root-password file. |
| `HRMS_F1_MYSQL_HOST_PORT` | Optional, default `13307`; free TCP port 1024-65535, published on `127.0.0.1` only. |
| `HRMS_F1_MINIO_API_HOST_PORT` | Optional, default `19000`; free TCP port 1024-65535, loopback only. |
| `HRMS_F1_MINIO_CONSOLE_HOST_PORT` | Optional, default `19001`; free TCP port 1024-65535, loopback only. |

Only the three host-port variables have defaults. Compose requires all other variables above. Use unique names and never reuse prior run IDs, projects, or volumes. `config -q` validates the model only; it does not prove that images can be pulled.

The Compose project and named volumes include the unique project/run identity. Both persistent volumes and services carry `com.hrms.disposable`, owner, and run-ID labels. MySQL and MinIO are the only base services. There is no HRMS API, catalog, tenant-routing template, migration host, or worker in this Compose file. The Docker network is internal; only loopback host ports are published. The acceptance harness will target only the loopback MySQL endpoint and must still verify hostname/port and run-specific database names.

## Startup and isolation verification (requires explicit approval)

Do not run these until the operator approves image pulls and starting the named disposable services/volumes.

1. Inspect the current context and confirm it is the local Docker Desktop Engine, then verify the run ID, project, secret file paths, bucket name and free loopback ports. Do not run against a remote context.
2. Review `docker compose -f docker-compose.f1.yml config -q` success and inspect `docker compose -p $env:HRMS_F1_PROJECT -f docker-compose.f1.yml config` locally. The expanded output contains secret **paths**, not values; do not redirect it into shared logs.
3. Start only MySQL and MinIO: `docker compose -p $env:HRMS_F1_PROJECT -f docker-compose.f1.yml up -d mysql minio`.
4. Verify container labels, project name, internal network, loopback port bindings, and volumes before continuing. Verify `mysql` reports the configured unique hostname and internal port `3306`. Never use an HRMS development or tenant connection string.
5. Create/verify the private run-specific bucket only after approval: `docker compose -p $env:HRMS_F1_PROJECT -f docker-compose.f1.yml --profile bootstrap run --rm bucket-init`. It uses test-only MinIO root credentials from file-backed Compose secrets, creates no public policy, and fails on public ACL grants or an existing bucket policy. Do not enable public access.
6. Verify the bucket using authenticated S3 API calls and confirm anonymous access is denied. Configure the isolated test host with the private test configuration; do not start the ordinary HRMS API or enable the ordinary background worker.

## Database acceptance configuration (separate explicit approval required)

The MySQL harness creates and migrates databases and later migrates down/deletes them. This is an explicit destructive test action. Do not set approval flags or run it until approval identifies the exact local server, internal hostname, host port, two exact run-specific database names, manifest paths, and result path.

Only after that separate approval, configure the variables in the table above. Build the MySQL connection string in the PowerShell process from the secret file without printing it; set `HRMS_PHASE1E_MYSQL_CONNECTION` only in that process. Use host `127.0.0.1`, the selected loopback MySQL host port, user `root`, and the disposable password file. The harness replaces the database name when opening admin and target connections. Do not put the password in a command literal, `.env`, manifest, result file, or checked-in configuration.

Example process-local setup (run only after approval; it deliberately emits no value):

```powershell
$mysqlPassword = [System.IO.File]::ReadAllText($env:HRMS_F1_MYSQL_PASSWORD_FILE).Trim()
$env:HRMS_PHASE1E_MYSQL_CONNECTION = "Server=127.0.0.1;Port=$($env:HRMS_F1_MYSQL_HOST_PORT);Database=mysql;User ID=root;Password=$mysqlPassword;SslMode=None"
Remove-Variable mysqlPassword
```

Set `HRMS_PHASE1E_EXECUTE=true`, `HRMS_PHASE1E_PROVIDER=MySql`, exact observed `HRMS_PHASE1E_SERVER_IDENTITY`, `HRMS_PHASE1E_INSTANCE_OR_PORT=3306`, exactly two unique `HRMS_PHASE1E_DATABASE_NAMES` containing the run ID, `HRMS_PHASE1E_RUN_ID`, absolute new manifest/result paths, `HRMS_PHASE1E_ENVIRONMENT=isolated-provider-acceptance`, and a non-secret test-only configuration label. Set each approval flag to `true` only when the operator has explicitly approved that exact class of action. The narrowly filtered existing harness command is:

```powershell
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --filter FullyQualifiedName~MySql_WelcomeInvitation_acceptance --logger "console;verbosity=minimal" -m:1
```

This command exercises only the existing MySQL welcome-invitation migration acceptance; it is **not** the F1 employee-import suite. Never run a broad test filter with provider execution enabled.

The dedicated F1 provider entry point is documented below. Keep its exact allowlist, approvals and run ownership isolated from the legacy WelcomeInvitation runner.

## Safe cleanup (requires explicit approval)

1. First preserve the acceptance result and manifests. Verify their run ID, provider, exact database names, server identity, cleanup eligibility and database-side ownership markers.
2. Let the harness perform database cleanup only after its checks pass. If setup/migration failed and the manifest is not cleanup-eligible, stop; do not manually drop a schema.
3. Verify each exact disposable database is absent. Do not use wildcard database deletion.
4. Only after separately approving deletion of this run’s stack, verify project name and all Compose labels match this run ID, then run `docker compose -p $env:HRMS_F1_PROJECT -f docker-compose.f1.yml down -v`. The `-v` command deletes both named data volumes; do not run it without the owner/run-ID verification.
5. Verify the exact containers, network, and volumes for this project are absent. Retain manifests/results until review is complete. Securely remove local secret files only after no test artifacts need them and the operator authorizes their deletion.

## Static checks and remaining blockers

`verify-static.ps1` checks the Compose file for required unique names, labeled named volumes, an internal network, loopback-only published ports, file-backed secret configuration, opt-in bucket bootstrap, and absence of shared database names/connection strings/literal secrets. It does not parse Compose YAML or contact Docker. Run Compose's own `config -q` after Docker Compose is installed and before startup.

Remaining blockers: MinIO image access/provenance is unresolved; test-only secrets and storage configuration do not exist; and no database/container actions have been approved. No F1 acceptance test has been run.

## F1-ENV-2 dedicated employee-import harness contract

### Database modes and Phase A adoption boundary

`HRMS_EMPLOYEE_IMPORT_F1_DATABASE_MODE` accepts exactly `CreateNew` (default) or `AdoptExistingEmpty`. `CreateNew` retains the existing refusal to use any target database that already exists. `AdoptExistingEmpty` additionally requires `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_ADOPTION=true`; database existence never implies approval.

Phase A defines read-only Docker identity and MySQL metadata preflight contracts and a versioned `AdoptedExistingEmpty` manifest (`SchemaVersion: 1`, `LifecycleState: PreflightOnly`). The three exact approved databases must all exist and contain no tables/views (including EF migration history), routines, triggers, or events. Incomplete metadata or any identity mismatch fails closed. An adoption manifest does not authorize deletion.

**Adoption execution is disabled in Phase A.** Even with the mode and approval set, the provider runner returns `NotImplementedException` before validating resources, writing a manifest, connecting to MySQL, or performing any mutation. Do not use adoption for acceptance until Phase B is separately implemented and approved. The Docker verifier and metadata inspector are read-only contracts; Phase A does not start Docker, connect to MySQL, or run provider acceptance.

For `CreateNew`, preserve all existing independent approvals for connection, creation, migration, worker execution, deletion, and MinIO restart. Do not enable those flags as a shortcut for adoption.

The new configuration gate is separate from `HRMS_PHASE1E_*` and from `MySql_WelcomeInvitation_acceptance`. It is pure configuration validation: it does not open a socket, create a database, migrate, start a worker, write a manifest, or delete anything. Unit tests exercise unsafe settings without constructing a database adapter.

The dedicated variables are:

| Variable | Required meaning |
|---|---|
| `HRMS_EMPLOYEE_IMPORT_F1_EXECUTE` | Exactly `true` to opt into the dedicated provider runner. |
| `HRMS_EMPLOYEE_IMPORT_F1_ENVIRONMENT` | Exactly `isolated-employee-import-f1`. |
| `HRMS_EMPLOYEE_IMPORT_F1_RUN_ID` | Unique 12-48 ASCII alphanumeric/hyphen ID. |
| `HRMS_EMPLOYEE_IMPORT_F1_SERVER_IDENTITY` | Exact MySQL `@@hostname`; it must equal `hrmsf1mysql` plus the run ID with hyphens removed. Set `HRMS_F1_MYSQL_HOSTNAME` in Compose to the same value. |
| `HRMS_EMPLOYEE_IMPORT_F1_HOST_PORT` | Approved host port bound to loopback. |
| `HRMS_EMPLOYEE_IMPORT_F1_SERVER_PORT` | MySQL container's internal server port; currently required to be `3306` (distinct from the host-mapped port). |
| `HRMS_EMPLOYEE_IMPORT_F1_MYSQL_CONNECTION` | Test-only administrator connection; must target `127.0.0.1`, match the approved port, use only `mysql` or no default schema, and disable pooling. Never echo it. |
| `HRMS_EMPLOYEE_IMPORT_F1_CATALOG_DATABASE` | Run-owned metadata/catalog database name. |
| `HRMS_EMPLOYEE_IMPORT_F1_TENANT_DATABASE` | Run-owned tenant-shard database name. |
| `HRMS_EMPLOYEE_IMPORT_F1_MIGRATION_DOWN_DATABASE` | Separate disposable migration-up/down database. |
| `HRMS_EMPLOYEE_IMPORT_F1_DATABASE_ALLOWLIST` | Exact comma-separated set of those three names, no extras or duplicates. |
| `HRMS_EMPLOYEE_IMPORT_F1_MANIFEST_PATH` | Absolute, new, private ownership-manifest path. Existing path is rejected. |
| `HRMS_EMPLOYEE_IMPORT_F1_RESULT_PATH` | Absolute, private result path. |
| `HRMS_EMPLOYEE_IMPORT_F1_MINIO_PORT` | Loopback-mapped MinIO S3 API host port. |
| `HRMS_EMPLOYEE_IMPORT_F1_STORAGE_CONFIGURATION` | Absolute path to a private, test-only JSON configuration file containing S3 static test credentials and a persistent encryption key ring. Do not store it in the repository or print it. |
| `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_CONNECTION` | Explicit `true` approval to connect. |
| `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_CREATE` | Explicit `true` approval to create the named databases. |
| `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_MIGRATION` | Explicit `true` approval to apply/roll back migrations. |
| `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_WORKER` | Explicit `true` approval to start isolated worker execution. |
| `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_DELETE` | Explicit `true` approval to delete only verified run-owned databases. |
| `HRMS_EMPLOYEE_IMPORT_F1_APPROVE_MINIO_RESTART` | Explicit approval to restart exactly one MinIO container after its Compose project, run ID, owner and disposable labels match. |
| `HRMS_EMPLOYEE_IMPORT_F1_COMPOSE_PROJECT` | Exact `hrms-f1-<lowercase-run-id>` project label used to find the run-owned MinIO service. |
| `HRMS_EMPLOYEE_IMPORT_F1_COMPOSE_FILE` | Absolute path to this directory's `docker-compose.f1.yml`; existence and file name are checked before provider work. |

The configuration gate rejects protected names including `hrms_catalog`, `hrms_web1`, `WEB1`, and system schemas; database names must use the lowercase run-specific prefix `hrms_ei1_<run-id-with-hyphens-replaced-by-underscores>_`, be unique, and fit MySQL identifier limits. Lowercase is required because the production shard router rejects uppercase shard keys. It refuses non-loopback hosts, mismatched ports, ambiguous default schemas, connection pooling, missing approvals, unsafe manifest paths, or an existing manifest. Validation errors never include connection-string contents.

The dedicated runner creates exactly three databases after passing the F1 gate, verifies loopback MySQL identity and port, writes ownership markers and a run manifest, applies migrations, checks import mappings/indexes, and migrates the isolated third database down. It seeds a synthetic tenant whose shard key is the approved tenant database. The isolated API test host uses the production shard placeholder, points its catalog only at the approved catalog database, disables other hosted workers, and enables the employee-import worker only inside that isolated test host. It verifies HTTP 202 while that host's worker is disabled, then exercises the production `EmployeeBulkImportWorker` and `EmployeeBulkImportExecutionService` through hosted test hosts. Interceptors create a deterministic interruption before a later row and a controlled stale-worker window; the runner expires only its operation lease, proves recovery by another host, and asserts stale renewal/outcome writes are rejected. A separate synthetic workbook injects an employee-persistence failure after an earlier row committed and checks partial state and counts through HTTP. It also checks generated codes, audit association, durable row outcomes, history/status, cross-tenant operation reads, and response masking. These are executable provider scenarios, but remain unverified until the gated MySQL and MinIO acceptance run completes.

Guard tests can be run without external access:

```powershell
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --filter FullyQualifiedName~EmployeeImportMySqlAcceptanceGateTests --logger "console;verbosity=minimal" -m:1
```

Before the provider command, configure a unique Compose hostname exactly as `hrmsf1mysql` plus the run ID with hyphens removed, and use the matching value for `HRMS_EMPLOYEE_IMPORT_F1_SERVER_IDENTITY`. Set the S3 bucket to `hrms-employee-import-<lowercase-run-id>`. The private storage configuration must select S3, static test credentials, loopback endpoint, Development-only HTTP, the approved bucket, and at least two persistent 32-byte encryption keys for rotation checks. Never use the default AWS credential chain for this acceptance runner. After the separate installation, test-secret creation, endpoint verification, and per-action approval steps are completed, invoke the provider runner narrowly (do not run a broad suite with approvals enabled):

```powershell
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --filter FullyQualifiedName~MySql_schema_and_minio_provider_acceptance --logger "console;verbosity=minimal" -m:1
```

It reports `SCHEMA_PROVIDER_CHECKS_PASSED` only after MySQL migrations, hosted-worker scenarios, HTTP/status checks, synthetic employee onboarding and S3-compatible scenarios complete. It deliberately retains all three databases and their manifest for inspection. Cleanup is a separate command after reviewing the run result and re-approving deletion:

```powershell
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --filter FullyQualifiedName~MySql_cleanup_current_owned_resources --logger "console;verbosity=minimal" -m:1
```

Cleanup re-verifies the MySQL identity/ports, exact manifest database set, creation/migration flags, and each in-database ownership marker before dropping only those three exact database names. A partial setup or failed migration is intentionally not cleanup-eligible; stop and review it manually rather than bypassing the guard. Compose volumes remain a separate cleanup requiring run-ID label verification and explicit approval.

The provider test is safely skipped unless all dedicated settings and approvals are present. The runner's MinIO restart step uses Docker CLI filters and verifies exact project/run/owner/disposable/service labels before restarting the identified container. It will not restart by container name or stop unrelated containers. Compose and Docker must be available and the unique F1 project must already be running. If restart approval is unavailable, leave the flag unset and provider acceptance remains skipped. State waits are bounded to 45 seconds and Docker CLI operations to 30 seconds.

The F1 provider command is:

```powershell
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --filter FullyQualifiedName~MySql_schema_and_minio_provider_acceptance --logger "console;verbosity=minimal" -m:1
```

This gated test covers HTTP confirmation returning 202 without processing in the confirmation host, durable queue processing by the actual hosted worker, catalog tenant enumeration and production shard-template routing, interruption/restart recovery, concurrent worker claim, expired lease recovery and stale-owner fencing, partial runtime failure, status/history counts, and real encrypted MinIO persistence through a verified container restart. Use synthetic employee data only. Do not run the provider command until services, private bucket, external encryption key ring, run ownership, configuration, and each separate action approval have been verified. This preparation task must run only builds, static checks, gate tests, and SQLite/fake-storage regressions.
