# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`AGENTS.md` covers repo layout, coding style, and PR conventions — read it too. This file adds the commands and cross-file architecture that AGENTS.md doesn't. `README.md` is largely stale (it describes SQL Server + SQLite dev and a 2-phase feature set); prefer `docs/mysql-development.md`, `docs/configuration-and-secrets.md`, and the per-phase design docs in `docs/`.

## Commands

Backend (repo root):

```powershell
dotnet build HRMS.slnx
dotnet run --project Backend/HRMS.API            # http://localhost:5080, Swagger at /swagger in Development
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --filter "FullyQualifiedName~AttendanceMonthlyProcessorTests"   # one class/test
dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --filter "FullyQualifiedName~ClassName.MethodName"
```

`scripts/test-backend-full.ps1` is the full run; `scripts/test-payroll-fast.ps1` is a filtered payroll subset; `scripts/test-payroll-providers.ps1` runs the SQL Server / MySQL provider suites.

Frontend (`Frontend/HRMS.Web`): `npm run dev` (port 5173, `strictPort` — CORS allow-lists exactly that origin), `npm run build` (tsc -b + vite), `npm run typecheck`, `npm run lint` (oxlint), `npm run test:run`. One test file: `npx vitest run src/pages/LoginPage.test.tsx`; one test: add `-t "name"`.

EF tooling is a local tool (`dotnet tool restore`, dotnet-ef 10.0.11). Provider and connection for design time come from env vars, never inferred:

```powershell
# SQL Server chain (Backend/HRMS.Infrastructure/Persistence/Migrations)
dotnet ef migrations add <Name> --context HrmsDbContext --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API --output-dir Persistence/Migrations
# MySQL chain: set $env:ConnectionStrings__MySql to a NON-production design-time DB, then target
#   Backend/HRMS.Infrastructure.MySqlMigrations (tenant) or Backend/HRMS.Infrastructure.MySqlCatalogMigrations (catalog)
#   which have their own IDesignTimeDbContextFactory.
```

## Architecture

**Layering**: `API → Infrastructure → Application → Domain`. Controllers are thin; business rules live in `Application/Services` behind `I*Service` interfaces in `Application/Abstractions`. Application never references provider types — anything provider-specific (locks, transient-error classification, employee-code sequence updates, concurrency tokens) is an Application interface implemented per provider in `Infrastructure/Persistence` (`SqlServer*`, `MySql*`).

**Two databases, per-tenant sharding**: A *catalog* DB (`HrmsCatalogDbContext`: `Tenants`, `TenantBranding`, `Platform*` identity tables) maps a request host → tenant → `ShardKey`/`DatabaseProvider`. Request flow: `TenantShardResolutionMiddleware` → `TenantShardResolver` → scoped `ShardContext` (write-once) → `ShardConnectionStringFactory` fills `{shardKey}` into a provider-specific connection-string template → scoped `HrmsDbContext`. Tenant data lives in that shard DB but is *also* filtered by `TenantId` (EF global query filters in `HrmsDbContext` + a SaveChanges guard that stamps the server-resolved tenant). Tenant id comes only from the resolved host / validated JWT `tid` claim, never from client input. Tenant-owned FKs are composite `(TenantId, …)`. Platform routes (`/api/platform/*`, `PlatformBearer`, `PlatformPermission`) are a separate identity domain handled before tenant resolution — a tenant `SuperAdmin` cannot authorize them.

**Multi-provider persistence**: Catalog provider comes from `Database:CatalogProvider`; each tenant's provider comes from the catalog row (`Tenant.DatabaseProvider`) — never from a request. Supported: SQL Server, MySQL (Oracle `MySql.EntityFrameworkCore`), SQLite (dev/test only). There are **three independent migration chains**: `HRMS.Infrastructure/Persistence/Migrations` (SQL Server tenant + `Persistence/Catalog/Migrations`), `HRMS.Infrastructure.MySqlMigrations`, `HRMS.Infrastructure.MySqlCatalogMigrations`. A schema change usually needs a migration in *each* tenant chain, and the model snapshot in each. Provider divergences to remember: SQL Server uses native `rowversion`, MySQL uses app-managed `binary(16)` tokens (interceptor); MySQL lock is `SELECT … FOR UPDATE`, SQL Server is `UPDLOCK, HOLDLOCK`; MySQL deadlock 1213 is retryable, lock-timeout 1205 deliberately isn't.

**Startup**: `Program.cs` → `DatabaseInitializer` migrates + seeds the catalog first (fatal on failure), then enumerates tenants and provisions each in its own scope (a single tenant failure is logged and skipped). `Database:SkipInitialization` (Development only) bypasses this. Middleware order in `Program.cs` matters (exception handler → forwarded headers → CORS → platform routing → tenant shard resolution → rate limiter → authn → authz).

**Authorization**: Permissions, not roles, guard endpoints — `[HasPermission(Permissions.X.Y)]` maps to one policy per name, registered from `Permissions.All`. The fallback policy denies endpoints with no declared authorization (use `[AllowAnonymous]` explicitly). Adding a permission touches: `Domain/Authorization/Permissions.cs` → role mappings in `Infrastructure/Persistence/Seed/SeedData.cs` → the frontend mirror `Frontend/HRMS.Web/src/auth/permissions.ts` (a test reads the C# file and fails if the lists diverge; frontend checks are cosmetic only).

**Frontend**: Vite/React 19, Axios client in `src/api` with single-flight 401 refresh (refresh tokens are single-use server-side — concurrent refreshes would trigger session revocation). Access token is in-memory only. API origin is host-aware: `demo01.localhost:5173` talks to `demo01.localhost:5080` (`VITE_API_ORIGIN_TEMPLATE`); the apex host shows a workspace picker and `platform.localhost` is the platform admin UI. Navigation is permission-driven from `src/layout/navigation.ts`.

## Configuration and local setup

Development is MySQL-only and fails closed when connection strings are missing (committed JSON has them empty). Secrets go in .NET user-secrets (`HRMS.API` has a `UserSecretsId`) or env vars (`__` = `:`): `Jwt:SecretKey`, `PlatformJwt:SecretKey` (≥32 bytes each), `ConnectionStrings:Catalog`, `Sharding:MySqlConnectionStringTemplate` (contains `{shardKey}`), `Email:Smtp:Password`. Setup details: `docs/configuration-and-secrets.md` and `docs/mysql-development.md`. First platform admin: `dotnet run --project tools/PlatformAdminBootstrap -- --confirm-platform-bootstrap` (interactive password). Demo tenants (DEMO01/DEMO02) are **not** seeded in the MySQL Development runtime; onboard tenants through the platform UI.

## Testing notes

- Default suite runs on SQLite in-memory (`TestSupport/SqliteInMemoryDatabase`, `HrmsApiFactory` for HTTP tests). SQLite passing is **not** evidence of SQL Server/MySQL behavior (rowversion, locking, filtered indexes, collection-parameter binding).
- `*ProviderAcceptance.cs` and MySQL/SQL Server fixtures are opt-in and **skip silently when env vars are absent**: `HRMS_MYSQL_TEST_CONNECTION`, `HRMS_MYSQL_CATALOG_TEST_CONNECTION`, `HRMS_SQLSERVER_TEST_CONNECTION` (+ `_SERVER`, `_AUTH`). Point them only at disposable databases — never `hrms_catalog`, `HRMS`, `HRMS_Catalog`, or a real tenant. A green run with skips is not provider parity.
- Feature work follows numbered phases (attendance 6x, payroll 7x, separation 8x); each ships a design doc in `docs/phase-*.md` and tests named after the phase. Check the relevant doc before changing behavior in those modules.

## Repo hygiene

The repo root has a lot of scratch output (`.*-build*/`, `*.log`, `login*.json`, `token*.txt`, `hrms*.db`). These are throwaway artifacts, not source — don't read them for context, don't commit them, and don't copy tokens/credentials from them.
