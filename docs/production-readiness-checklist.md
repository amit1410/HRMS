# HRMS production-readiness checklist

This checklist is a controlled deployment aid. It contains placeholders only;
credentials and private infrastructure details belong in the approved secret
manager or protected environment configuration, never in Git.

## Required configuration

- `ASPNETCORE_ENVIRONMENT=Production`
- `Database__Provider=SqlServer` or `MySql`
- `Database__CatalogProvider=SqlServer` or `MySql`
- `ConnectionStrings__Catalog`
- Provider-specific tenant template:
  - `Sharding__SqlServerConnectionStringTemplate`, or
  - `Sharding__MySqlConnectionStringTemplate`
- `Jwt__SecretKey`, issuer, audience, and token lifetimes
- `PlatformJwt__SecretKey` and platform token settings
- Email/SMS provider settings when those providers are enabled
- `ForwardedHeaders__KnownProxies` or `KnownNetworks` for the trusted reverse
  proxy, with `ForwardLimit` set to the actual proxy chain length
- Production CORS origins and workspace origin templates

Never place a password, token, connection string, or API key in this document,
`appsettings*.json`, the frontend bundle, or a checked-in `.env` file.

## Pre-deployment database checks

1. Confirm the target catalog and tenant databases are the intended environment.
2. Confirm the provider metadata and connection templates match each tenant.
3. Confirm the database is reachable and writable by the migration principal.
4. Record pending migrations using the approved deployment identity.
5. Take and verify a full backup before any schema change.
6. Record tenant, employee, account-link, leave, balance, and attendance counts
   for the change record.

Apply migrations as an explicit deployment step with the approved release
artifact. Do not use a broad or destructive repair command in production.

## Application deployment

```powershell
dotnet publish .\Backend\HRMS.API\HRMS.API.csproj -c Release -o .\artifacts\hrms-api
```

Verify the publish output contains no local database files, test data, secrets,
or development-only configuration. Deploy the artifact through the normal
service manager and retain the previous artifact for rollback.

The API exposes two anonymous probes:

- `/health` is liveness-only and does not access a database.
- `/ready` checks catalog connectivity and returns `503 NotReady` without
  connection details when the catalog cannot be reached. It does not scan
  tenant databases.

Traffic may be released only after both probes pass and a tenant-host smoke
test succeeds.

## Smoke test

- `/health` returns HTTP 200.
- `/ready` returns HTTP 200.
- Login succeeds for a disposable authorized test account.
- `/api/auth/me` returns the expected tenant and permissions.
- Employee, Leave Dashboard, My Attendance, Team Leave Calendar, and Attendance
  Reports behave within the account's authorization scope.
- A cross-tenant host/token check is rejected.
- Logs contain no migration, provider-selection, connection, token, password,
  or SQL error details in client responses.

## Hosting

For systemd, use a dedicated non-root service account, an environment file with
mode `0600`, the correct working directory, an absolute `ExecStart`, and an
automatic restart policy. Keep the environment file outside the repository.

For Nginx or another reverse proxy, forward only the headers from configured
trusted proxies. Preserve the original host and scheme because host-based
tenant routing depends on them. HTTPS termination must be paired with the
configured forwarded-header trust boundary.

## Backup and restore

### SQL Server

- Back up the catalog and every tenant database with the approved encrypted
  SQL Server backup procedure.
- Include database name, UTC timestamp, release/change identifier, and backup
  type in the backup record.
- Verify the backup with a restore to a disposable database before a risky
  migration or when restore capability has not recently been tested.

### MySQL

- Use the approved `mysqldump`/physical-backup procedure separately for the
  catalog and each tenant database.
- Restore only to a disposable target during validation.
- Preserve character set, collation, routines, and migration history.

Do not build an in-application backup endpoint and never restore over a
production database without an approved change and verified backup.

## Rollback

- If migration has not started, keep the previous application running.
- If the additive migration succeeded but the application is unhealthy, roll
  back the application artifact/configuration first; do not automatically run
  `Down()` migrations.
- If data or schema corruption is suspected, stop traffic and use the approved
  restore/forward-fix decision with the database owner.
- After rollback, verify `/health`, `/ready`, tenant routing, authentication,
  tenant isolation, and representative Leave/Attendance reads.

## Troubleshooting evidence

Record release version, migration state, provider, sanitized tenant code,
correlation/request ID, timestamps, and status codes. Never record connection
strings, passwords, bearer tokens, OTPs, or raw authorization headers.
