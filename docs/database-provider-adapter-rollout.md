# Database Provider Adapter: SQL Server Catalog Rollout

## Scope

This runbook is for the controlled production application of:

`20260905142921_AddTenantDatabaseProvider`

It does not apply the migration, change a database, or migrate any tenant to
MySQL. The migration adds catalog routing metadata only.

## Local environment state

The local `HRMS_Catalog` independently inspected on 2026-09-06 already
contains `20260905142921_AddTenantDatabaseProvider`. Its two current tenants
(`DEMO01` and `DEMO02`) both resolve to `SqlServer`; tenant counts, branding
relationships, uniqueness checks, and routing-field validation were also
verified. This is an observation of the local environment only. It does not
state when or by what process the migration was applied, and it does not
replace the controlled rollout procedure for another environment.

## A. Current catalog deployment architecture

The application registers `HrmsCatalogDbContext` in
`Backend/HRMS.Infrastructure/DependencyInjection.cs`.

- Catalog context: `HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext`
- Migration project/assembly: `Backend/HRMS.Infrastructure`
- Startup project: `Backend/HRMS.API`
- Design-time factory: `HrmsCatalogDbContextFactory`
- SQL Server history table: `__EFMigrationsHistoryCatalog`
- Catalog connection key: `ConnectionStrings:Catalog`
- Explicit catalog provider key: `Database:CatalogProvider`
- SQL Server catalog value: `SqlServer`

The current application startup path is:

`Program.cs` -> `DatabaseInitializer.InitializeIfEnabledAsync` ->
`DatabaseInitializer.InitializeCatalogAsync` -> `SchemaPreparer.PrepareAsync`.

`SchemaPreparer` calls `Database.MigrateAsync()` for SQL Server and MySQL. The
development SQLite path uses `EnsureCreated`; it is not part of this rollout.

The migration can also be applied separately with the EF command shown below.
That is the preferred controlled sequence so backup and preflight checks happen
before new application binaries are started.

## B. Preconditions

Do not begin until all of the following are true:

- A change ticket and maintenance window are approved.
- The catalog owner and application owner are available.
- A tested full backup has completed successfully.
- The deployment artifact contains the current catalog model and migration.
- The target migration is not already present in `__EFMigrationsHistoryCatalog`.
- The current catalog is confirmed to be SQL Server.
- Existing tenant routing is known to be SQL Server.
- No first-MySQL-tenant change is bundled with this migration.
- The rollback decision maker is identified.

## C. Backup plan

Before any schema change:

1. Take a full SQL Server backup of the existing catalog database.
2. Use the approved backup location, encryption policy, and retention policy.
3. Name the backup with the database name, UTC timestamp, release/change ID,
   and backup type.
4. Verify the backup completed successfully and is restorable according to the
   normal SQL Server backup verification process.
5. Record the backup path, checksum or backup identifier, completion time, and
   operator in the change ticket.
6. Retain the backup through the release rollback window and the organization’s
   normal disaster-recovery retention period.
7. Prefer a restore test to an isolated SQL Server instance before the change
   if the catalog is business-critical or the backup chain has not recently
   been tested.

No backup is performed by this document.

## D. Read-only preflight checklist

Run these checks against the intended catalog only. Do not print credentials or
full connection strings.

Connection and identity:

- Confirm the database name and SQL Server instance.
- Confirm the authenticated principal has the approved deployment rights.
- Confirm the database is online and writable after the maintenance gate.
- Confirm adequate data/log free space.
- Review active sessions, open transactions, blocking, and locks before the
  change.

Migration state:

- Confirm `__EFMigrationsHistoryCatalog` exists.
- Confirm the target migration state for the deployment environment. If it is
  already present, stop and review rather than applying it again.
- Confirm the expected prior catalog migration is present.
- Confirm the deployment artifact and database migration chain match.

Schema and data:

- Confirm `dbo.Tenants` exists.
- Confirm `dbo.TenantBranding` exists.
- Confirm `dbo.Tenants.DatabaseProvider` is absent before the change.
- Record the total tenant count.
- Record the total branding count.
- Snapshot `Id`, `TenantCode`, `Host`, `ShardKey`, and `Status` for every
  tenant, preferably into a change-controlled output or verification record.
- Check duplicate `TenantCode` values.
- Check duplicate `Host` values.
- Check duplicate `ShardKey` values.
- Check orphan `TenantBranding.TenantId` values.
- Check null or invalid legacy values in required routing columns.
- Record the tenant status distribution.

The preflight must be read-only. Any unexpected duplicate, orphan, null, or
schema state stops the rollout for review.

## E. Exact rollout sequence

1. Announce the maintenance window and stop catalog/tenant writes according to
   the normal application traffic procedure.
2. Drain or stop application instances that could perform provisioning or
   catalog writes.
3. Confirm the backup and restore verification record.
4. Run the read-only preflight checklist and save its output.
5. Confirm the target database name a second time immediately before applying
   the migration.
6. Apply only `20260905142921_AddTenantDatabaseProvider` using the command in
   this runbook.
7. Verify the schema, default, backfill, row preservation, branding foreign
   key, and migration history.
8. Run the current EF model/read and host-resolution smoke checks against the
   catalog.
9. Deploy the approved application binaries and configuration with
   `Database:CatalogProvider=SqlServer` and the correct catalog connection
   secret.
10. Start one application instance, verify health and catalog routing, and
    inspect logs.
11. Start or release the remaining instances using the normal rolling or
    maintenance procedure.
12. Verify existing SQL Server tenants, authentication, `/me`, tenant
    isolation, and representative read paths.
13. Reopen traffic only after the smoke checks and monitoring checks pass.

The migration is additive. An old binary generally ignores the extra catalog
column and can temporarily operate against the upgraded schema, but this is
not a substitute for an approved compatibility test. A new binary should not
be started against a catalog that has not received the migration because its
startup path may attempt to use the new model. A maintenance window is the
recommended operational choice.

## F. Exact migration command template

The following is a template for the approved deployment environment. It uses a
process-scoped connection-string environment variable and does not print it.
The command is intentionally not executed by this phase.

```powershell
$ErrorActionPreference = "Stop"
$env:Database__CatalogProvider = "SqlServer"

if ([string]::IsNullOrWhiteSpace($env:HRMS_SQLSERVER_CATALOG_CONNECTION)) {
    throw "HRMS_SQLSERVER_CATALOG_CONNECTION is required."
}

dotnet ef database update 20260905142921_AddTenantDatabaseProvider `
  --project .\Backend\HRMS.Infrastructure\HRMS.Infrastructure.csproj `
  --startup-project .\Backend\HRMS.API\HRMS.API.csproj `
  --context HRMS.Infrastructure.Persistence.Catalog.HrmsCatalogDbContext `
  --connection $env:HRMS_SQLSERVER_CATALOG_CONNECTION `
  --no-build

if ($LASTEXITCODE -ne 0) {
    throw "Catalog migration failed with exit code $LASTEXITCODE."
}
```

The supplied connection must target the existing production catalog and must
be provided by the approved secret/configuration mechanism. Do not place it in
source, tickets, logs, or this document.

Before use, confirm the release’s design-time factory and EF tool version are
the same versions validated in the rehearsal.

## G. Post-migration database verification

All checks are against the intended catalog only.

Schema:

- `dbo.Tenants.DatabaseProvider` exists.
- Its SQL Server type is `nvarchar(32)`.
- It is `NOT NULL`.
- Its default constraint represents `SqlServer`.
- Existing tenant primary key, unique indexes, and the
  `TenantBranding` foreign key are unchanged.

Backfill:

- `SqlServer` count equals the pre-migration tenant count.
- `MySql` count is zero unless a separately approved routing change already
  exists.
- `NULL` count is zero.
- `Other` count is zero.

Preservation:

- Tenant count is unchanged.
- Branding count is unchanged.
- Every `Id` is unchanged.
- Every `TenantCode` is unchanged.
- Every `Host` is unchanged.
- Every `ShardKey` is unchanged.
- Every `Status` is unchanged.
- Every `TenantBranding.TenantId` still references the same tenant.
- `__EFMigrationsHistoryCatalog` contains
  `20260905142921_AddTenantDatabaseProvider` after the prior migration.

Do not treat a successful migration command alone as sufficient evidence.
Persist the before/after comparison with the change record.

## H. Application smoke tests

After the new binaries are deployed, perform non-destructive checks first:

- Health endpoint returns healthy.
- An existing tenant host resolves to the expected tenant ID and shard key.
- The resolved provider is `SqlServer`.
- An existing tenant can log in.
- `/me` returns the authenticated user for the correct tenant.
- Tenant isolation checks show no cross-tenant data.
- One employee read succeeds.
- Configuration/master-data read succeeds.
- Existing SQL Server tenant routing remains stable.
- No tenant is unexpectedly routed to MySQL.
- Application logs contain no migration, connection, or provider-selection
  errors.

Avoid writes during the first smoke pass. Any approved write test must use the
normal test account/data-preservation procedure.

## I. Rollback plan

If deployment fails before migration application:

- Keep the old application state.
- Do not apply the migration.
- Correct the deployment issue and repeat preflight.

If the migration command fails before committing:

- Stop the rollout.
- Preserve command output and SQL Server error details.
- Do not retry blindly if the failure indicates locks, permissions, space, or
  schema drift.
- Resolve the cause and re-run read-only preflight.

If the migration succeeds but the application fails:

- Keep the additive `DatabaseProvider` column.
- Roll back application binaries/configuration to the last compatible release
  if that release is approved to operate with the additive schema.
- Do not run the migration `Down()` as an automatic response.

If existing SQL Server tenant routing fails:

- Stop traffic or isolate the affected instances.
- Confirm catalog values and connection configuration.
- Roll back application binaries if appropriate.
- Restore the catalog backup only if data/schema corruption occurred and the
  restore decision is approved.

Once any tenant has been intentionally assigned `MySql`, dropping the
`DatabaseProvider` column destroys routing metadata. Therefore `Down()` is not
the normal production rollback strategy. Prefer application rollback while
retaining the additive schema, or a reviewed forward-fix migration.

## J. First MySQL tenant guardrails

Applying this migration does not migrate any tenant database to MySQL.

Immediately after this migration:

- All existing tenants should remain `DatabaseProvider=SqlServer`.
- No MySQL tenant database should be provisioned automatically.
- No tenant routing metadata should be changed as part of this rollout.

A first MySQL tenant requires a separate approved change covering MySQL tenant
database provisioning, the dedicated MySQL tenant migration, provider-specific
connection configuration, data migration or onboarding, host resolution,
authentication, employee/leave behavior, concurrency, and tenant isolation.

## K. Maintenance-window recommendation

Classification: **MAINTENANCE WINDOW RECOMMENDED**.

The migration is a small additive catalog change and is expected to be low
risk, but the catalog is a routing dependency for every tenant. The operation
must account for schema locks, concurrent catalog writes, application startup
initialization, backup verification, and the possibility of inconsistent old
and new application binaries.

Do not promise zero downtime without an environment-specific rehearsal using
the production SQL Server version, catalog size, deployment topology, and lock
profile. A short write pause with controlled application restart is the safer
default.

## L. Remaining production risks

- Production backup restore capability must be verified independently.
- The rehearsal did not preserve original migration elapsed time.
- Production catalog size and lock duration may differ from the rehearsal.
- Startup initialization can apply migrations if a new application instance is
  started before the controlled migration step.
- Connection-string and secret-manager configuration must be validated without
  exposing secrets.
- A future MySQL tenant is a separate data and operational migration.
- Browser acceptance remains outside this rollout plan.

## GO / NO-GO criteria

Go only when:

- backup and restore evidence is recorded;
- preflight checks pass;
- target migration is absent before rollout;
- maintenance approval is active;
- migration command targets the intended catalog;
- post-migration counts, values, schema, and history pass;
- application smoke tests pass; and
- rollback ownership and monitoring are active.

No-go on any unexpected tenant count/value change, duplicate/orphan data,
schema drift, migration-history mismatch, lock/space/permission failure,
provider misconfiguration, or failed tenant-isolation check.

## Status

This document is a read-only rollout plan. No migration was applied and no
database was accessed while preparing it.

## Local validation closeout

The statements above describe preparation of a controlled rollout and remain
applicable to other environments. Separately, the local `HRMS_Catalog` and
disposable validation environment have completed the full adapter validation:

- SQL Server catalog migration rehearsal: PASS.
- Current `HRMS_Catalog` migration-state verification: PASS.
- SQL Server catalog runtime: PASS.
- MySQL catalog runtime: PASS.
- SQL Server tenant runtime: PASS.
- MySQL tenant runtime: PASS.
- External mixed-provider provider-selection validation: PASS.
- Full HTTP host -> catalog -> provider -> tenant `DbContext` E2E: PASS.
- Temporary catalog rows were cleaned successfully.
- `DEMO01` and `DEMO02` were preserved.
- Retained test databases were preserved.

The local SQL Server tenant architecture intentionally uses the shared
`ConnectionStrings:SqlServer` connection to `HRMS`; no
`Sharding:SqlServerConnectionStringTemplate` is configured. The resulting
shared SQL fallback warning is expected locally.

The warning that `Tenant.DatabaseProvider` was first mapped explicitly and
then ignored is a **NON-BLOCKING CODE-CLEANUP FOLLOW-UP**. No production code
was changed for it because both provider runtime paths passed.

**DATABASE PROVIDER ADAPTER ACCEPTANCE COMPLETE FOR LOCAL/DEVELOPMENT VALIDATION**

This local closeout is not production rollout completion, browser acceptance,
or Leave-module completion. Production rollout remains governed by the
controlled plan above.
