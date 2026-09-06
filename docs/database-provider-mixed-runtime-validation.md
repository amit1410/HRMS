# Controlled Mixed-Provider End-to-End Validation Plan

## Scope and safety boundary

This plan is for a future, explicitly approved validation run. It was prepared
without connecting to or modifying any database.

The existing catalog tenants `DEMO01` and `DEMO02` are out of scope and must
not be changed. The retained databases
`HRMS_MySqlAdapter_Test_20260905` and
`HRMS_MySqlCatalogAdapter_Test_20260906` must not be dropped or recreated.

Every API process used for this validation must run with:

```powershell
$env:Database__SkipInitialization = "true"
```

This prevents catalog migration, tenant migration, seeding, and schema
creation during runtime validation. Schemas must be prepared separately before
the API starts.

## A. Exact runtime routing flow

The production path is:

```text
HTTP Host
  -> TenantShardResolutionMiddleware
  -> ITenantShardResolver / TenantShardResolver
  -> HrmsCatalogDbContext.Tenants
  -> ShardDescriptor(DatabaseProvider, ShardKey, TenantId, Status)
  -> IShardContext / ShardContext
  -> ShardConnectionStringFactory.For
  -> provider-specific connection string
  -> AddDbContext<HrmsDbContext> options factory
  -> EF tenant provider and tenant database
```

Relevant source files and types:

- `Backend/HRMS.API/Middleware/TenantShardResolutionMiddleware.cs` — reads the request host and records the resolved shard.
- `Backend/HRMS.Infrastructure/Sharding/TenantShardResolver.cs` — exact host lookup in the catalog and projection to `ShardDescriptor`.
- `Backend/HRMS.Application/Abstractions/ShardDescriptor.cs` — carries `TenantId`, `ShardKey`, status, and trusted `DatabaseProvider` metadata without credentials.
- `Backend/HRMS.Infrastructure/Sharding/ShardContext.cs` — scoped resolved-shard holder.
- `Backend/HRMS.Infrastructure/Sharding/ShardConnectionStringFactory.cs` — selects the SQL Server or MySQL template and substitutes the validated shard key.
- `Backend/HRMS.Infrastructure/DependencyInjection.cs` — creates scoped tenant `HrmsDbContext` options and selects `UseSqlServer` or `UseMySQL`.
- `Backend/HRMS.Infrastructure/Persistence/Catalog/HrmsCatalogDbContext.cs` — catalog model and routing metadata.

Provider-specific connection resolution is:

```text
SqlServer -> Sharding:SqlServerConnectionStringTemplate
             or legacy Sharding:ConnectionStringTemplate
MySql    -> Sharding:MySqlConnectionStringTemplate only
SQLite   -> existing explicit development/test path
```

MySQL has no SQL Server/generic-template fallback. Missing provider-specific
configuration fails closed.

## B. Current provider matrix

The current implementation and focused `ProviderCombinationRoutingTests`
cover all four combinations:

| Catalog | Tenant | Current status |
|---|---|---|
| SQL Server | SQL Server | Focused matrix coverage passed |
| SQL Server | MySQL | Focused matrix coverage passed |
| MySQL | SQL Server | Focused matrix coverage passed |
| MySQL | MySQL | Focused matrix coverage passed |

The matrix tests exercise a real catalog context, the real shard resolver,
the real scoped shard context, the real tenant context registration, and the
actual EF provider names. They are not enum-only tests.

The remaining gap is live HTTP validation in one running API process against a
real SQL Server catalog containing two disposable mixed-provider tenants.

## C. Disposable tenant records

Do not use or edit `DEMO01` or `DEMO02`. Under a separately approved data
preparation step, create two synthetic catalog rows with new GUIDs:

| Field | SQL Server validation tenant | MySQL validation tenant |
|---|---|---|
| TenantCode | `ADAPTERSQL` | `ADAPTERMYSQL` |
| Host | `adaptersql.localhost` | `adaptermysql.localhost` |
| ShardKey | `adapter-sql` | `adapter-mysql` |
| Status | `Active` | `Active` |
| DatabaseProvider | `SqlServer` | `MySql` |

The actual GUIDs must be generated for that run and recorded in a sanitized
run manifest. No customer data is copied.

Cleanup is exact-ID cleanup only, after the API is stopped and all contexts
are disposed. Remove dependent branding first if any was created. Never use a
pattern delete, truncate, broad host predicate, or cleanup against `DEMO01` or
`DEMO02`.

## D. Disposable database strategy

### SQL Server tenant

Create a new SQL Server tenant database under the repository’s existing
disposable SQL Server acceptance-run ownership convention. Apply only the
current SQL Server tenant migrations to that new database. Do not point the
test at `HRMS`, `HRMS_Catalog`, `DEMO01`, `DEMO02`, or any business tenant.

The database name must be deterministically produced from the shard key or the
connection template so `adapter-sql` resolves to that exact database.

### MySQL tenant

Prefer `HRMS_MySqlAdapter_Test_20260905` only after a read-only schema/data
inspection proves it has the current tenant schema and contains no conflicting
test ownership. Because it is retained, do not reset, migrate destructively,
or drop it.

If the existing database cannot safely represent the validation tenant, create
a new disposable MySQL database and apply the dedicated MySQL tenant migration
project. That is a separate approved preparation step, not part of API startup.

## E. Process-scoped configuration

The future run must use process-scoped variables only. Do not edit appsettings
or user/machine environment variables.

```powershell
$env:Database__CatalogProvider = "SqlServer"
$env:Database__SkipInitialization = "true"
$env:ConnectionStrings__Catalog = $catalogConnectionString
$env:Sharding__SqlServerConnectionStringTemplate = "Server=lpc:.;Database=HRMS_Adapter_{shardKey};Integrated Security=True;TrustServerCertificate=True;"
$env:Sharding__MySqlConnectionStringTemplate = "Server=127.0.0.1;Port=3306;Database=HRMS_{shardKey};User ID=root;Password=$plainPassword;"
```

The actual SQL Server database naming and MySQL database name must match the
prepared disposable databases. The MySQL password must be entered securely and
must never be printed, logged, or placed in source control. Preserve and restore
all original process variables in a `finally` block.

## F. Schema and data prerequisites

Before starting the API, verify separately and read-only where possible:

1. SQL Server disposable tenant has the current SQL Server tenant migration chain.
2. MySQL disposable tenant has the current MySQL tenant migration chain.
3. Both databases contain the minimum read-only data needed by the selected GET endpoints.
4. The catalog rows point to the exact disposable shard keys and providers.
5. Both disposable tenants are `Active`.
6. No catalog migration or tenant migration is pending for the runtime process.
7. `Database__SkipInitialization=true` is set before the process starts.

Prefer existing migrated test data. Do not add employees, users, or tokens
unless an approved endpoint smoke test genuinely requires them. If login is
needed, use only an already-authorized disposable test account and keep its
credentials outside output.

## G. Controlled runtime sequence

1. Record current process environment values.
2. Confirm disposable database ownership and schema state.
3. Add the two synthetic catalog rows through a separately approved, exact-row data preparation step.
4. Set the process-scoped configuration above, including `Database__SkipInitialization=true`.
5. Start one API process from the current build.
6. Confirm startup logs explicitly report skipped database initialization.
7. Call `/health` without a tenant host and require HTTP 200.
8. Exercise the SQL Server disposable host with read-only requests.
9. Exercise the MySQL disposable host with read-only requests.
10. Verify `DEMO01` still resolves as SQL Server.
11. Verify `DEMO02` remains suspended and returns the existing unavailable-workspace behavior.
12. Stop the API and dispose all clients/resources.
13. Remove only the two synthetic catalog rows if cleanup was separately approved.
14. Restore all process-scoped variables.

The API must never run migration, schema creation, seed, or provisioning work
during this sequence.

## H. Evidence proving SQL Server routing

For `adaptersql.localhost`, capture sanitized evidence showing:

- host lookup returns `ADAPTERSQL` and the expected generated tenant ID;
- the descriptor provider is `SqlServer` and shard key is `adapter-sql`;
- the request reaches the disposable SQL Server tenant;
- the tenant `DbContext.Database.ProviderName` is `Microsoft.EntityFrameworkCore.SqlServer`;
- a read-only tenant query succeeds against the SQL Server database.

The current public API does not expose `DbContext.Database.ProviderName`.
Therefore provider-name evidence must come from an approved test-only
diagnostic harness or controlled server-side diagnostic logging; do not add an
unprotected production endpoint merely for this run.

## I. Evidence proving MySQL routing

For `adaptermysql.localhost`, capture the same evidence:

- host lookup returns `ADAPTERMYSQL` and the expected generated tenant ID;
- the descriptor provider is `MySql` and shard key is `adapter-mysql`;
- the request reaches the disposable MySQL tenant;
- the tenant `DbContext.Database.ProviderName` is `MySql.EntityFrameworkCore`;
- a read-only tenant query succeeds against the MySQL database.

No connection string, password, token, or secret may appear in evidence.

## J. Existing-tenant and isolation validation

Read-only checks must also prove:

- `demo01.localhost` resolves to `DEMO01`, provider `SqlServer`, and its expected shard.
- `demo02.localhost` remains `Suspended` and is rejected before tenant data access.
- an SQL Server tenant request cannot read the MySQL tenant’s rows;
- a MySQL tenant request cannot read the SQL Server tenant’s rows;
- a token issued for one tenant is rejected when presented on another tenant host;
- unknown hosts do not select a provider or database;
- the catalog provider remains SQL Server regardless of either tenant’s metadata.

Use read-only GET endpoints and existing authorization/tenant-match behavior.
Do not create or modify business rows.

## K. Failure-case validation

Use isolated disposable configuration or unit-level harnesses for failure
cases. Do not damage the real catalog or retained MySQL database.

- unsupported `DatabaseProvider` value: fail closed;
- absent MySQL template: clear configuration failure without secrets;
- absent SQL Server template and absent legacy fallback: clear configuration failure;
- unsafe shard key: rejected before connection creation;
- unavailable MySQL database: only that tenant fails, with no fallback to SQL Server;
- unavailable SQL Server database: only that tenant fails, with no provider substitution.

## L. Existing automated coverage and remaining gap

Existing coverage includes:

- `CatalogProviderSelectionTests` — trusted catalog provider selection and legacy compatibility;
- `ShardConnectionStringTests` — provider-specific templates, legacy SQL Server fallback, MySQL fail-closed behavior, and shard-key validation;
- `ProviderCombinationRoutingTests` — all four catalog/tenant provider combinations through real DI and EF provider names;
- `MySqlTenantRuntimeIntegrationTests` — real MySQL tenant runtime, concurrency, locking, DateOnly, authorization, leave, and policy paths;
- `MySqlCatalogRuntimeIntegrationTests` — real MySQL catalog provider, routing metadata, branding, and resolver behavior;
- SQL Server acceptance infrastructure — disposable SQL Server migration/runtime support.

The remaining end-to-end gap is one running API process using the already
migrated SQL Server catalog with two disposable catalog rows that route to two
different real tenant providers. Provider-name evidence also needs a safe
server-side/test-only observation point because the public API intentionally
does not expose provider internals.

## M. Cleanup procedure

After the run:

1. Stop the API process.
2. Dispose HTTP clients, scopes, contexts, and connection pools.
3. Remove only the two generated catalog rows by exact GUID, with branding children first.
4. Retain `HRMS_MySqlAdapter_Test_20260905` and `HRMS_MySqlCatalogAdapter_Test_20260906`.
5. Do not touch `DEMO01`, `DEMO02`, `HRMS_Catalog`, or any production database.
6. Restore all process environment variables in `finally`.

## N. Commands for the later normal-PowerShell run

These are execution templates for a separately approved run. They are not run
as part of this plan.

```powershell
Set-Location D:\HRMS
$ErrorActionPreference = "Stop"

$oldCatalogProvider = $env:Database__CatalogProvider
$oldSkipInitialization = $env:Database__SkipInitialization
$oldCatalogConnection = $env:ConnectionStrings__Catalog
$oldSqlServerTemplate = $env:Sharding__SqlServerConnectionStringTemplate
$oldMySqlTemplate = $env:Sharding__MySqlConnectionStringTemplate
$oldWorkloadResolver = $env:MSBuildEnableWorkloadResolver

$securePassword = Read-Host "MySQL disposable-tenant password" -AsSecureString
$plainPassword = [System.Net.NetworkCredential]::new("", $securePassword).Password
if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw "MySQL password was empty; validation was not started." }

try {
    $env:Database__CatalogProvider = "SqlServer"
    $env:Database__SkipInitialization = "true"
    $env:MSBuildEnableWorkloadResolver = "false"
    $env:ConnectionStrings__Catalog = $catalogConnectionString
    $env:Sharding__SqlServerConnectionStringTemplate = $sqlServerTemplate
    $env:Sharding__MySqlConnectionStringTemplate = $mySqlTemplate

    dotnet build .\Backend\HRMS.API\HRMS.API.csproj -p:UseAppHost=false -p:MSBuildEnableWorkloadResolver=false --no-restore
    if ($LASTEXITCODE -ne 0) { throw "API build failed." }

    dotnet run --project .\Backend\HRMS.API\HRMS.API.csproj --no-build
} finally {
    if ($null -eq $oldCatalogProvider) { Remove-Item Env:Database__CatalogProvider -ErrorAction SilentlyContinue } else { $env:Database__CatalogProvider = $oldCatalogProvider }
    if ($null -eq $oldSkipInitialization) { Remove-Item Env:Database__SkipInitialization -ErrorAction SilentlyContinue } else { $env:Database__SkipInitialization = $oldSkipInitialization }
    if ($null -eq $oldCatalogConnection) { Remove-Item Env:ConnectionStrings__Catalog -ErrorAction SilentlyContinue } else { $env:ConnectionStrings__Catalog = $oldCatalogConnection }
    if ($null -eq $oldSqlServerTemplate) { Remove-Item Env:Sharding__SqlServerConnectionStringTemplate -ErrorAction SilentlyContinue } else { $env:Sharding__SqlServerConnectionStringTemplate = $oldSqlServerTemplate }
    if ($null -eq $oldMySqlTemplate) { Remove-Item Env:Sharding__MySqlConnectionStringTemplate -ErrorAction SilentlyContinue } else { $env:Sharding__MySqlConnectionStringTemplate = $oldMySqlTemplate }
    if ($null -eq $oldWorkloadResolver) { Remove-Item Env:MSBuildEnableWorkloadResolver -ErrorAction SilentlyContinue } else { $env:MSBuildEnableWorkloadResolver = $oldWorkloadResolver }
}
```

Before using this template, replace only the in-memory `$catalogConnectionString`,
`$sqlServerTemplate`, and `$mySqlTemplate` values with the approved disposable
configuration. Do not print them. API requests should use an HTTP client with
the `Host` header set to each validation host. Health can be checked with:

```powershell
Invoke-WebRequest -Uri "http://localhost:5000/health" -Headers @{ Host = "localhost" }
```

The exact read-only tenant endpoint and test account must be selected from the
approved local test fixture; no credentials are included in this document.

## Final readiness

The implementation and focused matrix are ready for a controlled execution.
The true mixed-provider HTTP run remains pending disposable database
preparation, exact catalog-row approval, and a safe provider-observation
mechanism.

**MIXED-PROVIDER END-TO-END VALIDATION PLAN READY - NOT EXECUTED**

## Final local HTTP E2E validation closeout

The planned local run has now completed successfully. This section supersedes
the pending execution statement above for the local/development environment.

- SQL Server catalog migration rehearsal: PASS.
- Current `HRMS_Catalog` migration-state verification: PASS.
- SQL Server catalog runtime: PASS.
- MySQL catalog runtime: PASS.
- SQL Server tenant runtime: PASS.
- MySQL tenant runtime: PASS.
- External mixed-provider provider-selection validation: PASS.
- Full HTTP host -> catalog -> provider -> tenant `DbContext` E2E: PASS.
- Temporary `ADAPTERSQL` and `ADAPTERMYSQL` catalog rows were cleaned
  successfully.
- `DEMO01` and `DEMO02` were preserved.
- Retained test databases were preserved.

The local SQL Server route intentionally uses the shared
`ConnectionStrings:SqlServer -> HRMS` fallback and has no
`Sharding:SqlServerConnectionStringTemplate`; the shared-fallback warning is
expected.

The warning that `Tenant.DatabaseProvider` was first mapped explicitly and
then ignored is a **NON-BLOCKING CODE-CLEANUP FOLLOW-UP**. It was not changed
because both SQL Server and MySQL HTTP runtime paths passed.

**DATABASE PROVIDER ADAPTER ACCEPTANCE COMPLETE FOR LOCAL/DEVELOPMENT VALIDATION**

This does not claim production rollout completion, browser acceptance, or
Leave-module completion.
