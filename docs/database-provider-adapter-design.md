# Database Provider Adapter Design — Final Implementation Record

Status: Phases 1B–3 implementation is recorded here. Provider/runtime baselines were previously validated; Phase 3 connection-template and real-MySQL collection-contract checks remain subject to the focused commands and disposable credentials documented in the final readiness record. The opening Phase 1A sections below are retained as historical design context; the current implementation and readiness status are recorded in the final sections and supersede the original proposal where they differ.

## Executive summary

The current target supports SQL Server and MySQL for both catalog and tenant databases. SQLite remains an explicit development/test fallback. Catalog selection uses trusted `Database:CatalogProvider` configuration; tenant selection uses trusted catalog `Tenant.DatabaseProvider` metadata. The four catalog/tenant combinations have been validated by focused provider-matrix tests, and the final backend baseline is 819/819 passing.

The recommended target is a staged Option C:

1. Keep the catalog SQL Server during the first adapter increments, while making the tenant-database provider selectable from trusted catalog metadata.
2. Introduce a small Infrastructure adapter surface for provider configuration, employee serialization locking, and transient-error classification.
3. Add an independently generated MySQL tenant migration chain and provider contract tests.
4. Later make the catalog provider-independent (or move routing metadata to a provider-neutral control plane) if the operational goal is that no deployment requires SQL Server at all.

This is a feasible design, but MySQL implementation should not begin until the catalog strategy, rowversion replacement, collation policy, and attachment/document storage policy are approved.

## Historical Phase 1A design context

The sections through the Phase 1E-CI record below preserve the decisions and
intermediate audit notes made during implementation. They are not the current
provider-status contract; the final implementation and Phase 3 sections at the
end of this document supersede statements about provider support or validation
that were true only at an earlier checkpoint.

## 1. Historical architecture and resolution flow

Relevant source:

- `Backend/HRMS.Infrastructure/Persistence/HrmsCatalogDbContext.cs` — catalog context containing `Tenants` and `TenantBranding`.
- `Backend/HRMS.Infrastructure/Persistence/HrmsDbContext.cs` — tenant/shard context and tenant query filters.
- `Backend/HRMS.Infrastructure/DependencyInjection.cs` — registrations and the private `UseProvider` method.
- `Backend/HRMS.API/Program.cs` — middleware ordering, initialization, and infrastructure registration.
- `Backend/HRMS.Infrastructure/Sharding/TenantShardResolver.cs` — host lookup in the catalog.
- `Backend/HRMS.Infrastructure/Sharding/ShardContext.cs` — scoped, write-once resolved shard.
- `Backend/HRMS.Infrastructure/Sharding/ShardConnectionStringFactory.cs` — shard-key-to-connection-string resolution.
- `Backend/HRMS.Infrastructure/Sharding/ShardingOptions.cs` — connection-string templates and cache settings.
- `Backend/HRMS.Infrastructure/Persistence/ConfiguredProvider.cs` — currently recognizes only the SQLite fallback; all non-SQLite paths select SQL Server.

The request path is:

`Host -> TenantShardResolutionMiddleware (registered in Program.cs) -> TenantShardResolver.ResolveByHostAsync -> catalog Tenants row -> ShardDescriptor(TenantId, TenantCode, Host, ShardKey, Status) -> ShardContext.Use -> ShardConnectionStringFactory.For -> scoped HrmsDbContext options -> application service`.

`HrmsCatalogDbContext` is registered once with the `Catalog` connection string. `HrmsDbContext` is registered scoped and reads the resolved `ShardDescriptor` while its options are built. With a template, `{shardKey}` is substituted; without a template all tenants use the configured shared SQL Server database and rely on tenant filters. `TenantId` for application data comes from the server-resolved tenant/authentication context; it is not selected by client input.

Both contexts currently use SQL Server unless `Database:Provider=Sqlite`, in which case the application selects SQLite and uses the SQLite connection settings. The design-time factories `HrmsDbContextFactory` and `HrmsCatalogDbContextFactory` explicitly use SQL Server and the current migrations assembly.

There is no `DatabaseProvider`, provider enum, provider name, or provider-specific connection-string reference in `Tenant`. Its routing fields are `TenantCode`, `Host`, `ShardKey`, `TenantName`, contact fields, and `Status`. `ShardKey` is a safe database-name key, not a secret or connection string.

## 2. Catalog versus tenant database

### Catalog

The catalog is currently a shared SQL Server database. It is opened before a tenant is known, and it contains only routing/branding data. Its model is deliberately separate and uses the catalog migration history table `__EFMigrationsHistoryCatalog`.

### Tenant database

The tenant context contains the business model and already has global tenant filters plus SaveChanges tenant stamping. A tenant provider choice can be added at the shard descriptor/factory boundary without duplicating Application services.

### Recommendation

Use Option C, staged independence. In the first implementation slice keep the catalog SQL Server so routing remains operationally stable, and add a trusted provider discriminator to catalog routing metadata. A tenant row must resolve both `Provider` and a non-secret `ConnectionStringReference`/shard key; the API must never accept either from a caller. Later, support a provider-neutral catalog (or an external control plane) once the catalog migration/bootstrapping problem has its own tested solution. Option B immediately is possible but doubles the bootstrap and migration risks before tenant-database compatibility is proven. Option A alone does not meet the eventual “no SQL Server required anywhere” goal.

## 3. Provider-specific inventory

### Provider registration

`DependencyInjection.UseProvider` calls `UseSqlServer` for every non-SQLite configuration and sets the migrations assembly. There is no `UseMySql` path. `ConfiguredProvider` is therefore a global provider switch, not a per-tenant provider factory. Both design-time factories hard-code SQL Server intentionally.

### Raw SQL and provider APIs

The non-migration production inventory is small:

| File | Use | SQL Server behavior | MySQL risk/adapter need |
|---|---|---|---|
| `Persistence/SqlServerLeaveRequestSubmissionLock.cs` | `DbConnection`/`DbCommand` with `SELECT 1 FROM [Employees] WITH (UPDLOCK, HOLDLOCK)` | Takes an update/range lock in the current transaction | SQL syntax and lock semantics differ; replace the concrete registration with a provider-neutral lock contract and SQL Server/MySQL implementations |
| `Persistence/SqlServerLeaveRequestSubmissionDeadlockClassifier.cs` | `Microsoft.Data.SqlClient.SqlException.Number == 1205` | Identifies SQL Server deadlock | MySQL uses a different exception/provider and distinguishes deadlock (commonly 1213) from lock wait timeout (commonly 1205); classify through an adapter |
| `Persistence/SchemaPreparer.cs` | `Database.SqlQuery<string>` querying `sqlite_master` and `pragma_table_xinfo` | SQLite-only schema comparison used by the SQLite development initializer | Must not run against MySQL/SQL Server; use provider-specific schema inspection or migrations for those providers |

No `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSqlRaw`, `ExecuteSqlInterpolated`, `SqlConnection`, `SqlCommand`, `NOLOCK`, `ROWLOCK`, `MERGE`, `OUTPUT`, `SYSUTCDATETIME`, or application-layer `SqlClient` usage was found outside generated migrations/tests. Ordinary Application code uses EF Core LINQ and `ExecuteUpdateAsync`; those are provider-translated and should remain shared, subject to provider contract tests.

### EF model configuration

The main provider-sensitive configurations are:

- `LeaveRequestFoundationConfigurations.cs`: `date`, decimal precision `(9,3)`, and `RowVersion` as SQL Server rowversion/concurrency token.
- `LeaveBalanceFoundationConfigurations.cs`: decimal `(9,3)`, `date`, rowversion/concurrency token, balance check constraint, and unique lifecycle index filtered with `[LeaveRequestId] IS NOT NULL`.
- `EmployeeCodeSequenceConfiguration.cs`: `RowVersion` as SQL Server rowversion.
- `LeavePolicyFoundationConfigurations.cs`: `date` policy ranges, decimal `(9,3)`, and `LeavePolicyClubbingRule.NormalizedPairKey` as a stored computed column using SQL Server `CONVERT(varchar(36), ...)`, `CASE`, and `+` concatenation. The index explicitly uses no filter.
- Enum properties use integer conversions throughout; this is portable but should remain explicitly tested.
- Numerous configurations use lengths, unique indexes, composite alternate keys, restrictive/cascade deletes, `HasDefaultValue(true)`, and precision. These are portable concepts, but generated DDL, collation, and index rules must be reviewed per provider.

There are no configured SQL Server sequences. Employee code allocation is row-based: `EmployeeCodeSequenceService` reads a sequence row and advances it with an optimistic `ExecuteUpdateAsync` compare-and-update, retrying bounded `DbUpdateException`s. `EmployeeService` also advances configuration values using compare-and-update logic. MySQL must preserve the unique indexes and affected-row semantics; this does not require a provider-specific business service.

## 4. Leave concurrency and transient errors

The shared Application contract is `ILeaveRequestSubmissionLock`, registered in `DependencyInjection.cs` to `SqlServerLeaveRequestSubmissionLock`. Approval, submission, withdrawal, and cancellation services request this abstraction. The SQL Server implementation:

1. returns immediately for SQLite tests;
2. requires SQL Server otherwise;
3. opens the context connection if needed;
4. executes `SELECT 1 FROM [Employees] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = @tenantId AND [Id] = @employeeId`;
5. attaches the current EF transaction to the command; and
6. closes the connection only if it opened it.

The lock is therefore held by the surrounding transaction until commit/rollback. Existing services acquire the Employee lock before dependent balance/request/event/ledger mutations. The retry policy is `LeaveRequestSubmissionRetryPolicy`, with three whole-operation attempts and a short bounded delay. Its classifier is Infrastructure-provided and currently recognizes only SQL Server error 1205. The service retry boundary and ChangeTracker cleanup behavior must remain unchanged when adapters are introduced.

### Recommended lock adapter

Retain the existing Application abstraction initially, or rename it only in a focused refactor to `ILeaveRequestSerializationLock`. Register:

- `SqlServerLeaveRequestSubmissionLock`: parameterized employee-row `UPDLOCK, HOLDLOCK` command in the caller transaction.
- `MySqlLeaveRequestSubmissionLock`: parameterized employee-row `SELECT ... FOR UPDATE` in the caller transaction, with an indexed `(TenantId, Id)` lookup.

The MySQL adapter must use the same transaction and lock lifetime. It must not introduce `SERIALIZABLE`, `GET_LOCK`, `sp_getapplock`, a guard table, or a global lock. `FOR UPDATE` is effective only inside a transaction, so registration and transaction ownership need integration tests. SQL Server and MySQL may differ in gap/range locking; the exact indexed access path and isolation behavior must be validated with real provider tests.

### Recommended transient-error adapter

Keep `ILeaveRequestSubmissionDeadlockClassifier` as the smallest compatible seam, or generalize its name to `IDatabaseTransientErrorClassifier` only with a narrow migration of callers. Provider implementations should classify their own exception types/numbers. SQL Server: `SqlException.Number == 1205`. MySQL: provider-specific deadlock code (normally 1213) and an explicit policy decision on lock wait timeout (normally 1205); do not classify every timeout as retryable. Application retry count, transaction recreation, and ChangeTracker cleanup stay shared.

## 5. Rowversion and indexes

### Rowversion entities

`EmployeeCodeSequence.RowVersion`, `EmployeeLeaveBalance.RowVersion`, and `LeaveRequest.RowVersion` are `byte[]` values configured with `IsRowVersion()`; the latter two also use `IsConcurrencyToken()`. SQL Server generates them automatically. SQLite has explicit model overrides because it has no rowversion type.

Recommended cross-provider replacement: an application-managed non-null `long Version`/`bigint` concurrency token incremented on every update, with the old token included in EF concurrency predicates. This has clear MySQL and SQL Server mappings and preserves 409 behavior without relying on wall-clock precision. It requires a deliberate API/serialization compatibility decision because the current token is byte-array-shaped. A GUID token is also portable, but a numeric version is easier to inspect and test. Do not silently map SQL Server rowversion to a timestamp or `DateTime`.

### Filtered indexes

Two relevant filters exist:

- historical generated snapshots contain `[EmployeeCode] IS NOT NULL` for the pending-code uniqueness index;
- `LeaveBalanceTransactionConfiguration` defines unique `(TenantId, LeaveRequestId, TransactionType)` with `[LeaveRequestId] IS NOT NULL`.

The clubbing index calls `HasFilter(null)`, so it is not filtered. MySQL/InnoDB unique indexes allow multiple NULL values, so the lifecycle index can likely become an ordinary unique index with equivalent NULL behavior; this must be proven with the target provider. The nullable EmployeeCode index may likewise use a normal unique index if the intended NULL semantics are preserved. If MySQL version/provider behavior or a future soft-delete rule makes NULL semantics unsafe, use a generated non-null discriminator/key or a provider-specific index definition. Do not add a second uniqueness rule.

### Computed column

`LeavePolicyClubbingRule.NormalizedPairKey` is stored and computed by SQL Server as an unordered GUID pair: compare `CONVERT(varchar(36), Lower...)` with `Higher...`, then concatenate the lower textual GUID, `:`, and the higher textual GUID. MySQL should use a stored generated column with `CASE`, `CAST(... AS CHAR(36))`, and `CONCAT`, or a provider-neutral application-maintained normalized key if generated-column portability becomes too costly. The existing invariant that participants differ and the unique `(TenantId, LeavePolicyVersionId, NormalizedPairKey)` index must remain. This is a clear provider-specific model/migration override, not an Application concern.

## 6. Scalar, collation, and relationship compatibility

- `Guid`: SQL Server `uniqueidentifier`; MySQL should prefer `BINARY(16)` if the selected EF provider supports stable conversion, otherwise `CHAR(36)`. Binary is smaller and better for indexes but is a migration/API compatibility choice. All composite FK columns must use the identical representation and length.
- `bool`: SQL Server `bit`; MySQL commonly uses `tinyint(1)`. EF conversion is normally safe, but defaults and generated DDL must be provider-tested.
- `DateOnly`/`date`: SQL Server `date` and MySQL `DATE` both represent calendar dates. Keep leave/policy/day logic date-based; do not convert to timestamps.
- `DateTime`: the repository applies `UtcDateTimeConverter`; SQL Server `datetime2` and MySQL `datetime(fractional precision)` do not carry timezone identity. Continue enforcing UTC at the application boundary and test precision/truncation.
- `decimal`: current business quantities use precision `(9,3)`. MySQL `DECIMAL(9,3)` is suitable, but strict SQL mode, rounding, and check enforcement must be tested with the real provider. Do not weaken the balance check.
- strings: SQL Server migrations use `nvarchar`; MySQL should use `utf8mb4` with explicit lengths. Case-insensitive unique-code behavior must be made explicit rather than inherited from a server default. Host normalization already lowercases before lookup.
- enums: integer conversions are provider-neutral; preserve numeric stability in migrations.
- string index length: MySQL byte limits make long UTF-8 composite indexes more sensitive than SQL Server indexes. Review every composite unique index containing strings, especially code/name/key fields.
- composite tenant-aware FKs and `Restrict`/`Cascade` delete behavior are supported concepts in MySQL/InnoDB, provided principal/foreign column order, type, collation, and indexes match exactly. The existing tenant-scoped relationship design should remain shared.

Authentication/account linking uses ordinary EF entities, composite tenant-aware FKs, and unique indexes; no Application-layer provider leak was found. The main compatibility risks are collation, GUID representation, index byte limits, and concurrent unique-key handling—not duplicated authentication logic.

## 7. Migrations and model configuration

The current migration chain is one SQL Server-oriented folder under `Backend/HRMS.Infrastructure/Persistence/Migrations`, with separate catalog and tenant history tables but the same assembly. Existing migrations cannot run unchanged on MySQL. Examples include `uniqueidentifier`, `nvarchar`, `datetime2`, `bit`, SQL Server `rowversion`, filtered-index syntax, the computed clubbing expression, `CONVERT(date, GETUTCDATE())`, and `migrationBuilder.Sql` batches using `NEWID`, `GETUTCDATE`, `UPDATE ... FROM`, and SQL Server join syntax.

Recommended architecture:

- retain shared entity/configuration intent;
- split provider-specific migrations into separate assemblies (preferred for tooling and accidental cross-application prevention), such as `HRMS.Infrastructure.Migrations.SqlServer` and `HRMS.Infrastructure.Migrations.MySql`;
- keep separate catalog and tenant migration histories in each provider assembly;
- select provider, context, and migration assembly together from a trusted provider descriptor;
- keep design-time factories provider-explicit and never infer a provider from an arbitrary connection string.

Same-assembly provider folders are possible, but separate assemblies give safer tooling boundaries and make it harder to apply a SQL Server migration chain to MySQL. Shared configurations should express portable constraints/indexes; provider overrides should handle rowversion, generated columns, filters, native types, collations, and defaults. Avoid duplicating whole entity configurations.

## 8. Tenant provider metadata and connection security

Add a future catalog-level `DatabaseProviderType` discriminator with at least `SqlServer` and `MySql`, preferably on a routing/shard record rather than embedding provider concerns in the domain `Tenant` aggregate. The record should also carry a non-secret `ConnectionStringReference` or validated shard key. Resolve it only after host lookup; never accept provider/connection selection from JWT/client payloads, and never expose connection strings through API DTOs.

Credentials remain in a secret manager or deployment configuration keyed by the trusted reference. The connection factory should select an allow-listed provider configurator and secret reference, validate that the resolved tenant owns that route, and reject unknown providers. Cache entries must include provider metadata and be invalidated when routing changes; a stale provider cache is a correctness/security risk.

## 9. Provider factory and minimal adapter surface

Recommended Infrastructure contracts:

1. `IDatabaseProviderConfigurator` — provider name, EF options registration, migration assembly/history configuration, and provider-specific model hooks.
2. Existing `ILeaveRequestSubmissionLock` (or narrowly renamed `ILeaveRequestSerializationLock`) — acquire the tenant+employee serialization boundary using the caller transaction.
3. Existing `ILeaveRequestSubmissionDeadlockClassifier` (or generalized `IDatabaseTransientErrorClassifier`) — classify provider exceptions without exposing provider types to Application.
4. A provider-aware migration/bootstrap coordinator — chooses the correct context/migration assembly for a new database; it is not needed by ordinary request services.

Do not create provider-specific Employee, Leave, Organization, Authentication, or accounting services. Entities, validators, controllers, DTOs, authorization, policies, and React remain shared. Only provider registration, lock SQL, transient classification, migration DDL, and narrowly scoped EF model overrides may differ.

## 10. Attachment/document relevance

The existing `EmployeeDocument` is metadata with `FilePath`, size, and content type; no provider-neutral blob/document storage abstraction or secure document download contract was found in the audited backend. It is not a suitable basis for provider selection. A future leave-attachment slice should define storage independently of SQL provider and avoid putting binary data or physical paths into tenant routing metadata.

## 11. MySQL provider choice

The repository targets `net10.0` and EF Core `10.0.11`. No MySQL package is currently referenced. `Pomelo.EntityFrameworkCore.MySql` is the practical first candidate because it is widely used with EF Core and offers good MySQL/MariaDB translation coverage; however, a package release explicitly compatible with EF Core 10 must be confirmed before selection. Oracle’s MySQL EF Core provider is the alternative when its matching EF Core/.NET support and operational/licensing requirements are preferable. No package version is recommended or installed in this phase.

Selection gates: exact EF Core 10 compatibility, generated-column/index support, DateOnly/decimal behavior, migrations tooling, transaction/`FOR UPDATE` behavior, and production support policy. If neither provider has a supported EF Core 10 release, the project must either wait, target a supported EF Core version, or accept a provider/version architecture decision; do not force an incompatible package into the solution.

## 12. Testing strategy

- Shared unit tests: domain rules, validators, fingerprinting, authorization, and status behavior.
- SQLite tests: fast Application behavior only; do not treat SQLite as evidence for rowversion, filtered indexes, decimal checks, or locking.
- SQL Server disposable integration tests: current lifecycle, real decimal/check/index behavior, locks, deadlocks, and 409 concurrency semantics.
- MySQL disposable integration tests: same provider contract suite against a clean MySQL database.
- Provider contract tests: parameterize the same test scenarios by context/provider, including tenant isolation, composite FKs, unique lifecycle rows, generated clubbing key, UTC/date/decimal round trips, sequence compare-and-update, and `FOR UPDATE` concurrency.
- Deployment tests: apply each provider’s catalog and tenant migration chain to empty databases and verify migration history separation.

No database was opened and no test or disposable database was created during this review.

## 13. Implementation sequence and effort

| Slice | Scope | Best | Likely | High-risk |
|---|---|---:|---:|---:|
| DB Adapter 1B | Trusted provider metadata, provider selection contracts, secret-reference plumbing, SQL Server behavior unchanged | 8h | 14h | 24h |
| DB Adapter 1C | Extract SQL Server lock/deadlock/model hooks without behavior change | 12h | 20h | 32h |
| DB Adapter 1D | Add a compatible MySQL provider, tenant registration, scalar/index/concurrency mapping | 16h | 30h | 55h |
| DB Adapter 1E | Separate catalog/tenant MySQL migration chain and bootstrap tooling | 20h | 40h | 75h |
| DB Adapter 1F | Shared/provider contract tests and disposable MySQL fixtures | 20h | 36h | 60h |
| DB Adapter 1G | Real MySQL locking, deadlock, retry, and lifecycle validation | 16h | 32h | 65h |
| DB Adapter 1H | Optional provider-independent catalog/control plane | 16h | 32h | 70h |

For the required tenant-provider capability through 1G, estimate 92–172 focused engineering hours: best case 92–110, likely 150–175, high-risk 250+ if rowversion/API compatibility, generated columns, or MySQL locking behavior require redesign. The catalog-independence slice is separate and should not be hidden inside the first MySQL implementation.

## 14. Recommended decisions before implementation

1. Approve staged Option C and whether the first release may keep a SQL Server catalog.
2. Choose the public/serialized representation of the current `byte[]` rowversion replacement.
3. Set explicit UTF-8/case-sensitivity rules for tenant codes, employee codes, emails, hosts, and policy codes.
4. Confirm whether MySQL means Oracle MySQL only or MySQL-compatible MariaDB as well.
5. Confirm provider package support for EF Core 10 before adding it.
6. Define whether lock-wait timeout is retryable and how MySQL deadlocks map to the existing 409/retry contract.
7. Approve separate migration assemblies and an empty-database migration rehearsal before any existing database is considered.

## Conclusion

The Application and API architecture is suitable for provider-neutral business logic, but the current persistence path is SQL Server-specific at registration and at several critical infrastructure/model points. The adapter surface is small enough for staged implementation. MySQL compatibility is not a simple connection-string switch: rowversion, locking, generated columns, filtered-index semantics, SQL Server migration batches, GUID/index representation, and collation must be handled explicitly and tested against real MySQL. No production code, tests, packages, migrations, or databases were changed in Phase 1A.

## Phase 1B implementation update

Phase 1B added provider metadata plumbing without enabling MySQL execution:

- `HRMS.Domain.Enums.DatabaseProviderType` contains the stable values `SqlServer = 1` and `MySql = 2`. SQLite is not a tenant routing value; it remains selected only through the explicit `Database:Provider=Sqlite` development/test path.
- `Tenant.DatabaseProvider` is stored on the catalog/shard routing record. EF converts it to a readable required string column (`nvarchar(32)` on the current SQL Server migration). The CLR default and migration default are `SqlServer`, so existing catalog rows remain compatible.
- `ShardDescriptor` carries `DatabaseProvider` from `TenantShardResolver` through `ShardContext`, and startup provisioning/system projections preserve it.
- Tenant `HrmsDbContext` registration now rejects MySQL metadata with `DatabaseProviderNotSupported` before configuring a provider. It does not fall back to SQL Server or SQLite. SQL Server behavior and the explicit SQLite fallback remain unchanged.
- Catalog selection is controlled by trusted `Database:CatalogProvider` configuration; SQL Server, MySQL, and the explicit SQLite development/test fallback are supported.
- The local `HRMS_Catalog` inspected on 2026-09-06 already contains `20260905142921_AddTenantDatabaseProvider`, and its two current tenants resolve to `SqlServer`. This records local environment state only; it does not claim when or by what process the migration was applied.
- Focused routing tests cover SQL Server defaults/explicit values, MySQL metadata propagation, fail-closed MySQL execution, unknown values, tenant isolation, and write-once provider protection. No application-layer provider resolver was added because `ShardDescriptor`/`IShardContext` already form the trusted resolution boundary; a provider configurator remains a Phase 1C concern.

Phase 1C should extract SQL Server lock/deadlock behavior behind provider adapters while preserving the existing Application contracts, before adding a MySQL EF provider.

## Phase 1C implementation update

The provider-neutral Application contracts are now explicit:

- `IEmployeeSerializationLock` is the canonical employee-scoped serialization contract. The prior `ILeaveRequestSubmissionLock` name remains as a compatibility interface for existing callers and tests.
- `IDatabaseTransientErrorClassifier` is the canonical provider-neutral transient/deadlock contract. The prior `ILeaveRequestSubmissionDeadlockClassifier` name remains as a compatibility interface.
- The existing `SqlServerLeaveRequestSubmissionLock` remains the SQL Server implementation and still executes the same parameterized `SELECT 1 FROM [Employees] WITH (UPDLOCK, HOLDLOCK)` on the active transaction/connection. SQLite still returns without a database lock; non-SQL Server providers fail closed.
- The existing SQL Server classifier still recognizes only `Microsoft.Data.SqlClient.SqlException.Number == 1205`. No MySQL exception type or retry behavior was introduced.
- Application leave services and the retry policy now depend on the provider-neutral contracts. DI registers the SQL Server implementations for both canonical and compatibility interfaces without changing retry counts, transactions, lock order, or business behavior.

Phase 1D remains responsible for the actual MySQL EF provider, `FOR UPDATE` lock adapter, MySQL transient error classification, and provider-specific model/migration configuration.

## Phase 1D implementation update

The tenant context now has an actual MySQL provider path. The repository uses .NET 10 and EF Core 10.0.11 (`Microsoft.EntityFrameworkCore`, `Relational`, `SqlServer`, and `Design`). The selected provider is Oracle's stable `MySql.EntityFrameworkCore` version `10.0.9`; its published `net10.0` asset requires EF Core 10.0.9 or later and is compatible with this graph. Pomelo was evaluated as an alternative, but the Oracle provider was selected for this phase because it had a published stable EF Core 10/net10.0 package at implementation time. No EF or .NET downgrade was made.

`DependencyInjection.UseProvider` now selects `UseMySQL` for trusted tenant `DatabaseProviderType.MySql` metadata and keeps the existing SQL Server and explicit SQLite branches unchanged. The provider is not selected from HTTP data. Server-version discovery is not performed during DI; the provider receives the configured connection string and later validation must target the approved MySQL 8.x production baseline.

`MySqlLeaveRequestSubmissionLock` implements the existing employee serialization contracts. It uses the same context connection and caller transaction, parameterized tenant/employee values, and `SELECT 1 FROM \`Employees\` ... FOR UPDATE`. It refuses to run against a non-MySQL context and refuses an absent transaction. It does not use a global lock. `MySqlTransientErrorClassifier` inspects `MySql.Data.MySqlClient.MySqlException.Number` and classifies only error 1213 (deadlock). Error 1205 (lock wait timeout) is deliberately not retried until a provider-specific timeout policy is approved; authentication, connection, schema, and other errors are not classified as deadlocks.

At the historical Phase 1D checkpoint, the MySQL model override replaced the SQL Server computed clubbing expression with a stored generated-column expression using `CAST(... AS CHAR(36))`, `CASE`, and `CONCAT`, and removed the SQL Server filter expression from the lifecycle unique index. Subsequent Phase 1E work generated and validated the dedicated MySQL migrations and real MySQL concurrency behavior.

At that historical checkpoint, the focused routing test did not claim live MySQL locking/concurrency validation. Later sections record the completed tenant and catalog runtime validation.

## Phase 1B model-drift correction

`TenantMapping.ApplyColumns` is shared because the catalog and shard copies of `Tenant` must agree on their common columns. Provider selection is the exception: it is control-plane metadata owned by the catalog. `TenantConfiguration` therefore explicitly ignores `Tenant.DatabaseProvider` after applying the shared columns. `CatalogTenantConfiguration` continues to map and persist it.

This keeps `HrmsDbContext` aligned with its existing tenant migration snapshot while the catalog migration `20260905142921_AddTenantDatabaseProvider` remains responsible for the catalog column. No tenant migration is required, and `PendingModelChangesWarning` is not suppressed.

## Phase 1E-C concurrency-token design decision

The three current tokens are:

| Entity | Property and CLR type | Current mapping | Usage |
|---|---|---|---|
| `LeaveRequest` | `RowVersion : byte[]` | `IsRowVersion().IsConcurrencyToken().IsRequired()` | Status changes in submission, approval/rejection, withdrawal, and cancellation are tracked and persisted through `SaveChangesAsync`. |
| `EmployeeLeaveBalance` | `RowVersion : byte[]` | `IsRowVersion().IsConcurrencyToken().IsRequired()` | Opening/accrual/external-grant posting and allocated reserve/consume/release/restore mutate the tracked balance and call `SaveChangesAsync`. The token is returned by `LeaveBalanceSnapshot`. |
| `EmployeeCodeSequence` | `RowVersion : byte[]` | `IsRowVersion()` | Insert uses tracked `SaveChangesAsync`; allocation updates `NextNumber` with an `ExecuteUpdateAsync` compare-and-swap on the old `NextNumber`, so that update does not currently use the rowversion token. |

`LeaveBalanceSnapshot.RowVersion` is therefore an existing public `byte[]` contract. ASP.NET Core's JSON serialization represents it as a Base64 string. No client currently sends this balance token back for a balance mutation; leave configuration concurrency tokens are separate string DTO tokens. The leave request and balance lifecycle services use EF tracked updates, `DbUpdateConcurrencyException`, and existing conflict mappings. No raw SQL update was found for these three token properties. `EmployeeCodeSequenceService` is the deliberate exception: its conditional `ExecuteUpdateAsync` and the unique generated-employee-code constraint form its allocation safety chain.

### Options considered

- **Long revision:** portable and easy to index, but changes the public balance snapshot from Base64 `byte[]` to a numeric wire value. It requires a coordinated SQL Server conversion/backfill and every update path—including the sequence compare-and-swap—to participate in the revision protocol.
- **Application-managed `byte[]`:** preserves the existing public type and Base64 wire shape. A fixed 16-byte token can be generated on insert and replaced on each tracked update while the original value remains in EF's concurrency predicate. It still requires a SQL Server schema conversion away from native `rowversion` if applied globally.
- **Provider-specific token mechanics with a shared `byte[]` contract (recommended):** retain native SQL Server `rowversion` unchanged, and map MySQL tokens as required, application-managed fixed-length bytes. The Domain/Application/API contract remains `byte[]`; only Infrastructure mapping/token generation differs. This avoids rewriting existing SQL Server databases while giving MySQL behavior that does not depend on timestamp precision or `ON UPDATE` semantics.

### Decision

Recommend **provider-specific token mechanics with the shared `byte[]` contract**. This ranks highest for correctness and preservation of the existing API and SQL Server migration history. It also keeps provider-specific behavior in Infrastructure rather than duplicating business services. MySQL must not be considered equivalent merely because EF reports `IsConcurrencyToken` or `ValueGenerated.OnAddOrUpdate`; the provider must use an explicitly application-managed token strategy and verify affected-row/concurrency behavior against a real MySQL schema.

The eventual implementation should use a narrowly scoped `SaveChangesInterceptor` (or equivalent `HrmsDbContext` save boundary) for `LeaveRequest` and `EmployeeLeaveBalance`: assign a new nonzero 16-byte token for Added entities and a different token for Modified entities, without replacing EF's original-value snapshot. SQL Server must bypass this generation and retain native rowversion. The interceptor must run before each save attempt, and retry handling must clear/rebuild tracking as it does today so a rolled-back token is never reused accidentally. `EmployeeCodeSequence` needs a separate decision: either keep its existing atomic `NextNumber` compare-and-swap as the concurrency mechanism or change the update path to a tracked/version-aware update. The first is smaller and preserves the currently validated duplicate-code safety chain, but it should not be described as rowversion-based optimistic concurrency.

### Phase 1E-CI implementation

The provider-specific mechanics are now implemented without changing the shared `byte[] RowVersion` contract:

- MySQL maps the three tokens as required `binary(16)`, `IsConcurrencyToken()`, and `ValueGeneratedNever()`. SQL Server continues to use the existing `IsRowVersion()` mappings; SQLite's existing test fallback is unchanged.
- `MySqlConcurrencyTokenGenerator` creates cryptographically random 16-byte values. `MySqlConcurrencyTokenInterceptor` runs only for the Oracle MySQL provider and only for the three targeted entities. Added entries receive an initial token; Modified entries receive a new current token while EF's original value remains intact.
- `IEmployeeCodeSequenceUpdater` keeps provider-specific sequence SQL out of the Application service. The SQL Server updater preserves the existing `NextNumber` compare-and-swap and does not assign `RowVersion`. The MySQL updater performs the same predicate and sets `NextNumber` and a newly generated `RowVersion` in one `ExecuteUpdateAsync` operation.
- The existing DI provider selection registers the MySQL interceptor and updater only for trusted MySQL shard metadata. No catalog interceptor is registered, and no SQL Server lock, retry count, transaction boundary, or migration was changed.
- `ConcurrencyTokenMechanicsTests` covers model metadata, token assignment/rotation, original-value preservation, SQL Server non-assignment, token length/distinctness, and Base64 serialization of `LeaveBalanceSnapshot`. Real MySQL stale-update and CAS behavior still require a disposable MySQL database.

### Migration and legacy-data impact

For SQL Server, the recommended split requires no conversion of existing `rowversion` columns: native tokens and current API behavior remain unchanged. For MySQL, the initial empty-database migration can create a fixed-length binary token column with application-managed values. If a single provider-neutral schema is later required, the safe approach is additive: add a new nullable token column, backfill every existing row with unique nonzero values, make it required/concurrency-enabled, deploy code that understands both versions, then remove the old column only after all readers and writers are upgraded. A direct `rowversion`-to-`bigint` replacement is not a safe in-place assumption.

### Required implementation validation

The next implementation slice must add model tests for both provider mappings; tracked stale-update tests for LeaveRequest and EmployeeLeaveBalance; API serialization tests proving the existing Base64 balance token; retry tests covering token regeneration after rollback/`ChangeTracker.Clear`; and sequence allocation tests proving the existing compare-and-swap plus unique-code constraint remains safe. SQL Server disposable tests must prove the current native rowversion behavior is unchanged. MySQL empty-schema and real concurrent stale-update tests are required before declaring equivalence. No migration or token code was changed during this design review.

## Final implementation and production-readiness record

### Provider selection and connection resolution

Catalog provider selection is controlled only by trusted server configuration:

- `Database:CatalogProvider=SqlServer` selects SQL Server.
- `Database:CatalogProvider=MySql` selects Oracle `MySql.EntityFrameworkCore`.
- `Database:CatalogProvider=Sqlite` selects the explicit development/test fallback.
- When absent, legacy `Database:Provider=Sqlite` preserves SQLite; otherwise SQL Server is the default.
- Invalid explicit catalog values fail closed.

Tenant provider selection is independent and comes only from `Tenant.DatabaseProvider` in the catalog. Requests, headers, JWTs, DTOs, and frontend values cannot choose a provider.

Tenant connection templates are provider-specific:

- `Sharding:SqlServerConnectionStringTemplate` is preferred for SQL Server tenants.
- Legacy `Sharding:ConnectionStringTemplate` is retained as the SQL Server fallback.
- `Sharding:MySqlConnectionStringTemplate` is required for MySQL tenants; MySQL never falls back to the legacy SQL Server template or shared SQL Server connection.
- `Sharding:SqliteConnectionStringTemplate` remains the explicit SQLite development/test path.

All templates substitute only a validated catalog `ShardKey`. Credentials remain deployment-time configuration or secret-manager values and are not logged or exposed through APIs.

### Provider-specific runtime behavior

- SQL Server keeps native `rowversion` for `LeaveRequest`, `EmployeeLeaveBalance`, and `EmployeeCodeSequence`.
- MySQL uses application-managed required `binary(16)` concurrency tokens for those entities while retaining the shared `byte[]` API/Base64 contract.
- SQL Server employee serialization remains `UPDLOCK, HOLDLOCK`.
- MySQL employee serialization uses parameterized `SELECT ... FOR UPDATE` in the caller transaction.
- SQL Server deadlock 1205 is retryable.
- MySQL deadlock 1213 is retryable.
- MySQL lock timeout 1205 is intentionally not classified as a deadlock/retryable error.
- Employee-code CAS remains atomic; MySQL rotates `RowVersion` in the same `ExecuteUpdate` statement.

### Migration inventory and rollout boundary

Separate migration chains exist:

- SQL Server catalog: `20260905142921_AddTenantDatabaseProvider`; existing rows receive `SqlServer` through the non-null default. The local `HRMS_Catalog` state was verified as migrated on 2026-09-06; other environments remain subject to the controlled rollout runbook.
- MySQL tenant: `20260905172008_InitialMySqlTenantSchema` in `HRMS.Infrastructure.MySqlMigrations`.
- MySQL catalog: `20260905181639_InitialMySqlCatalogSchema` in `HRMS.Infrastructure.MySqlCatalogMigrations`.

No migration is applied automatically as part of provider selection. Production migration rollout, backup, verification, rollback, and first-MySQL-tenant activation remain separately controlled operational activities.

### Oracle MySQL collection-parameter risk

Real tests previously exposed provider binding problems with captured collection predicates. Production review found collection-based EF predicates in `LeaveApprovalReadService`, `LeavePolicyResolver`, `LeavePolicyFoundationService`, `LeaveConfigurationService`, and the authorization role/permission loading in `AuthService`. These remain provider-contract test targets; they must not be globally rewritten or moved client-side. Any query that fails against real MySQL must be fixed narrowly while preserving tenant, authorization, ordering, and null semantics.

Phase 3 adds real-provider contract coverage to `MySqlTenantRuntimeIntegrationTests` for authorization role/permission loading, the leave-policy resolver, leave-policy foundation validation, leave configuration rule/type lookup, and the leave-approval inbox predicate. The tests exercise one-, multi-, and empty-collection paths where the service contract makes an empty path meaningful. They require `HRMS_MYSQL_TEST_CONNECTION`; no production query was changed without an observed provider failure. Execution remains an environment-dependent validation step when the disposable MySQL credentials are unavailable.

### Validation status and outstanding scope

Validated baseline: 819/819 backend tests, 10/10 catalog provider tests, 6/6 real MySQL tenant runtime tests, 4/4 real MySQL catalog runtime tests, and 4/4 provider-combination tests. Browser acceptance remains deferred. Existing disposable MySQL databases are not production evidence. Production readiness still requires migration rehearsal, backups, monitoring, provider-specific secret/connection configuration, and controlled rollout approval.

## Final local acceptance closeout

The complete local/development database-provider adapter validation is now
accepted. This closeout supersedes earlier pending-status language for the
local environment only.

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

The current local SQL Server configuration intentionally uses the shared
fallback `ConnectionStrings:SqlServer -> HRMS` with no
`Sharding:SqlServerConnectionStringTemplate`. The shared-fallback warning is
expected for this local configuration.

The warning that `Tenant.DatabaseProvider` was first mapped explicitly and
then ignored is recorded as a **NON-BLOCKING CODE-CLEANUP FOLLOW-UP**. It was
not changed during closeout because both SQL Server and MySQL runtime paths
passed.

**DATABASE PROVIDER ADAPTER ACCEPTANCE COMPLETE FOR LOCAL/DEVELOPMENT VALIDATION**

This does not claim production rollout or browser acceptance, and does not
claim that the Leave module is complete.
