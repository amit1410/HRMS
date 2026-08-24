# HRMS — Multi-Tenant Human Resource Management System

A multi-tenant HRMS built as a real-world enterprise product: **ASP.NET Core Web API (.NET 10) + EF Core (Code-First) + SQL Server**, with a **React + TypeScript** frontend.

Multi-tenancy uses a **shared database / shared schema** model: every tenant-scoped row carries a `TenantId`, isolation is enforced server-side by EF Core **global query filters**, and the `TenantId` is resolved **only** from the authenticated server-side context — never trusted from the client.

---

## Solution layout

```
HRMS.slnx                     .NET 10 solution (XML format)
Backend/
  HRMS.Domain/                Entities, enums, authorization constants. No dependencies.
  HRMS.Application/           DTOs/contracts, abstractions (ITenantContext, IPasswordHasher, IAuthService,
                              IDepartmentService, IDesignationService, IEmployeeService), the services
                              themselves, JwtSettings, validation, ApiResponse/Result/PagedResult, CsvBuilder.
  HRMS.Infrastructure/        EF Core DbContext, entity configs, migrations, seeding, password hasher,
                              JWT token service.
  HRMS.API/                   ASP.NET Core host: controllers, middleware, JWT-claim tenant context,
                              authentication/authorization + rate-limiting wiring, Program.cs.
  HRMS.Tests/                 xUnit tests (SQLite in-memory + in-process HTTP integration tests).
Frontend/
  HRMS.Web/                   React 19 + TypeScript (Vite): typed API client, session/auth provider,
                              route guards, app shell, dashboard, employee/department/designation
                              screens. Vitest + Testing Library.
```

Dependency direction: `API → Infrastructure → Application → Domain`. The Domain project has no external dependencies; controllers contain no business logic and never expose EF entities directly.

---

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` should report `10.x`).
- **Node.js 20.19+ / 22.12+** (for the frontend; `npm` ships with it).
- A database provider — either:
  - **SQL Server** (the default and the production target), or
  - **SQLite** (a zero-install dev fallback; see note below).

---

## Database provider (important)

The provider is **config-driven** via the `Database:Provider` setting (`SqlServer` or `Sqlite`).

| Environment | Provider | Where set | Schema created by |
|-------------|----------|-----------|-------------------|
| Production / default | `SqlServer` | `appsettings.json` | EF Core **migrations** (`InitialCreate`, `AddRefreshTokens`, `AddOrganizationAndEmployees`) |
| Development | `Sqlite` | `appsettings.Development.json` | `EnsureCreated` (from the model) |

> `EnsureCreated` does nothing to a database that already exists, so it cannot add tables the model has gained since. The SQLite dev path therefore compares the model's tables against the file and **recreates the database** (rebuilding the seed) if any are missing — a dev database left over from an earlier phase would otherwise fail at the first write. SQL Server uses migrations and is unaffected.

> **Why a SQLite fallback exists.** The SQL Server **LocalDB** engine on this machine is currently broken — `sqlservr.exe` crashes on startup inside `sqllang.dll`, so `(localdb)\MSSQLLocalDB` cannot start an instance. Rather than block development, the app can run against SQLite for local dev while **SQL Server remains the mandated default** and the real **SQL Server migration is committed** (`Backend/HRMS.Infrastructure/Persistence/Migrations`). Nothing about the domain model or the migration is SQLite-specific.

### Running against real SQL Server

1. Point the connection string at a working instance in `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "SqlServer": "Server=YOUR_SERVER;Database=HRMS;Trusted_Connection=True;TrustServerCertificate=True"
   }
   ```
2. Ensure the environment resolves to the `SqlServer` provider (default in `appsettings.json`; note that `appsettings.Development.json` overrides it to `Sqlite` — set `Database:Provider` to `SqlServer` there, or run in a non-Development environment).
3. Start the app — it runs `Migrate` automatically and applies the committed migrations, then seeds.

### Restoring SQL Server LocalDB (if you want the default to work locally)

LocalDB failing to start is an engine-level fault, not a project issue. To repair:

```bash
sqllocaldb stop MSSQLLocalDB
sqllocaldb delete MSSQLLocalDB
sqllocaldb create MSSQLLocalDB
sqllocaldb start MSSQLLocalDB
```

If it still crashes (check **Event Viewer → Windows Logs → Application** for a `sqlservr.exe` fault in `sqllang.dll`), repair or reinstall the **SQL Server Express LocalDB** component via the SQL Server installer. Until then, use the SQLite dev fallback.

---

## Run the API

Development (SQLite, no database install required):

```bash
dotnet run --project Backend/HRMS.API
```

The API listens on `http://localhost:5080` (and `https://localhost:5081` via the `https` launch profile). On startup it creates/migrates the database and seeds reference data + demo tenants.

### Endpoints

| Endpoint | Auth | Purpose |
|----------|------|---------|
| `POST /api/auth/login` | anonymous, rate-limited | Sign in with organization code + email + password; returns an access/refresh pair and the user's profile, roles and permissions. |
| `POST /api/auth/refresh` | anonymous, rate-limited | Exchange a refresh token for a new pair. Single-use — the presented token is revoked. |
| `POST /api/auth/logout` | bearer token | Revoke a refresh token. Idempotent. |
| `GET /api/auth/me` | bearer token | The signed-in user's profile, roles and effective permissions. |
| `GET /api/departments` | `Department.View` | Paged, searchable, sortable list with a live employee count per department. |
| `GET /api/departments/{id}` | `Department.View` | One department. |
| `POST /api/departments` | `Department.Create` | Create. Code and name are unique per tenant (case-insensitive). |
| `PUT /api/departments/{id}` | `Department.Edit` | Update. |
| `DELETE /api/departments/{id}` | `Department.Delete` | Delete — refused with 409 while employees are still assigned. |
| `GET /api/designations` | `Designation.View` | Paged list with a holder count per designation. |
| `GET /api/designations/{id}` | `Designation.View` | One designation. |
| `POST /api/designations` | `Designation.Create` | Create. Code and name are unique per tenant. |
| `PUT /api/designations/{id}` | `Designation.Edit` | Update. |
| `DELETE /api/designations/{id}` | `Designation.Delete` | Delete — refused with 409 while the title is held. |
| `GET /api/employees` | `Employee.View` | Paged list. Filters: `search`, `departmentId`, `designationId`, `status`, `reportingManagerId`. |
| `GET /api/employees/{id}` | `Employee.View` | One employee, with department, designation and manager names resolved. |
| `POST /api/employees` | `Employee.Create` | Hire. Employee code and email are unique per tenant. |
| `PUT /api/employees/{id}` | `Employee.Edit` | Update. |
| `DELETE /api/employees/{id}` | `Employee.Delete` | Delete — refused with 409 while the employee still has direct reports. |
| `GET /api/employees/export` | `Employee.Export` | CSV download of the **filtered** list (paging ignored, capped at 10,000 rows). |
| `GET /health` | anonymous | Liveness probe. |
| `GET /api/system/info` | anonymous | Confirms the API is up. In **Development** it also reports the seeded state (counts + per-tenant summary); outside Development it reports nothing else, since an anonymous caller has no business learning the platform's size, provider or customer list. |

Every list endpoint takes `page`, `pageSize` (default 20, max 100), `search`, `sortBy` and `sortDescending`, and answers with a paging envelope (`page`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage`). `sortBy` is checked against a per-endpoint whitelist — an unrecognized field is a 400 that names the permitted values, never a silent fallback:

| List | Sortable by |
|------|-------------|
| Departments, designations | `code`, `name`, `employeeCount`, `isActive`, `createdDate` |
| Employees | `employeeCode`, `firstName`, `lastName`, `email`, `department`, `designation`, `status`, `dateOfJoining`, `createdDate` |

Swagger UI (Development only): `http://localhost:5080/swagger` — use **Authorize** and paste the `accessToken` from `/api/auth/login`.

Sign in:

```bash
curl -X POST http://localhost:5080/api/auth/login -H "Content-Type: application/json" -d "{\"tenantCode\":\"DEMO01\",\"email\":\"admin@demo01.com\",\"password\":\"Passw0rd!\"}"
```

Then call an authenticated endpoint:

```bash
curl http://localhost:5080/api/auth/me -H "Authorization: Bearer <accessToken>"
```

---

## Authentication & authorization

**Sign-in is tenant-scoped.** Email addresses are unique *per tenant*, so login takes an organization code alongside the credentials. The code only selects which tenant's credentials are checked — the user row must match both the tenant and the password, so it can never grant access to a tenant the credentials don't belong to.

**The access token carries the tenant.** `TenantId` is stamped into the JWT at sign-in and read back from the validated token by `HttpTenantContext` on every request; the client cannot influence it. Compact claim names are used (`uid`, `tid`, `tcode`, `email`, `role`, `permission`), and inbound claim mapping is disabled so they arrive exactly as written.

**Every authenticated endpoint requires a tenant claim.** The default authorization policy demands an authenticated user *and* a `tid` claim, so a token without a tenant cannot satisfy a bare `[Authorize]`. The same requirements are installed as the **fallback policy**, so an endpoint that declares no authorization at all is refused rather than silently public — anonymous endpoints must opt out explicitly with `[AllowAnonymous]`.

**Permissions, not roles, guard endpoints.** `[HasPermission(Permissions.Employee.View)]` maps to one authorization policy per permission name, registered automatically from `Permissions.All`. Roles are simply the vehicle for granting permissions, and both are re-read from the database on every refresh — so an administrator's change takes effect on the next refresh without forcing a new sign-in.

**Refresh tokens are opaque, hashed and single-use.** A 256-bit random token is returned to the client; only its SHA-256 hash is stored. Refreshing consumes the presented token (a single conditional `UPDATE`, so two concurrent presentations cannot both win) and issues a replacement. Presenting an already-consumed token is treated as theft: **every** live session for that user is revoked, because a replay cannot be distinguished from a stolen token in use.

**Failure responses reveal nothing.** Unknown organization, unknown email and wrong password all return the same 401 message, and the "no such tenant/user" paths verify the submitted password against a throwaway hash so response latency doesn't disclose which accounts exist. Credential endpoints are rate-limited per client IP. Passwords are hashed with ASP.NET Core Identity's PBKDF2 hasher, and no password material appears in logs or responses.

### Configuration

```json
"Jwt": {
  "Issuer": "HRMS.API",
  "Audience": "HRMS.Client",
  "SecretKey": "",              // must be supplied outside development — see below
  "AccessTokenMinutes": 60,
  "RefreshTokenDays": 7,
  "ClockSkewSeconds": 30
},
"RateLimiting": {
  "Authentication": { "PermitLimit": 20, "WindowSeconds": 60 }
},
"Cors": {
  "AllowedOrigins": [ "http://localhost:5173", "https://localhost:5173" ]
}
```

`Cors:AllowedOrigins` is an explicit allow-list, not a wildcard — credentials are allowed, and `*` cannot be combined with that. Deploying the client elsewhere means adding its origin here. The policy also names `Content-Disposition` as an **exposed** header: it is not CORS-safelisted, so without that the browser hides it from JavaScript and the CSV export downloads under a generated filename instead of the one the API chose.

`Jwt:SecretKey` is deliberately **empty in `appsettings.json`** — no production signing key is committed. Development uses a throwaway key from `appsettings.Development.json`; anywhere else, supply one out of band:

```bash
dotnet user-secrets set "Jwt:SecretKey" "<at least 32 characters of random material>" --project Backend/HRMS.API
```

The `Jwt` and `RateLimiting` sections are bound with `ValidateOnStart`, so a missing or too-short key **fails the start** rather than producing tokens nobody can trust. Anything read on more than one code path goes through `IOptions<T>` rather than straight off `IConfiguration` at registration time — so the key used to *sign* tokens is always the key used to *validate* them. (An early bug here did exactly that and produced unexplained 401s: registration-time reads snapshot configuration before the host has layered in every source, so the signing key and the validation key came from different snapshots. The CORS origins are read at registration deliberately — they are consumed once, when the policy is built, so there is no second read to disagree with.)

---

## Organization structure & employees

Three entities, all tenant-scoped: **Department**, **Designation** (job title) and **Employee**. Departments and designations are independent lookups; an employee references one of each, and optionally a reporting manager.

**References carry the tenant in the key.** An employee's foreign keys are composite — `(TenantId, DepartmentId) → Departments(TenantId, Id)`, and likewise for designation and reporting manager — rather than the usual single column. A plain `DepartmentId → Id` key would happily accept another organization's department, because that row *does* exist; it just isn't ours. With the tenant inside the key the database itself refuses the write, so a future bug, bulk import or hand-written `INSERT` cannot stitch two tenants together. The service layer rejects it first with a field-level validation error; the constraint is what makes the rejection a guarantee rather than a convention.

**Nothing is deleted out from under a record.** All three relationships are `DeleteBehavior.Restrict`. Deleting a department that still has staff, a designation that is still held, or a manager who still has direct reports is refused with a 409 that says how many rows are in the way and suggests deactivating instead. Employment history outliving the org chart is the point: an employee who leaves is marked `Resigned`/`Terminated`, not removed.

**Reporting lines are validated, not trusted.** An employee cannot manage themselves, and cannot be given a manager who reports (directly or indirectly) back to them — the service walks the chain to the top before accepting the change, with a depth limit so a pre-existing cycle in the data cannot spin. A status other than `Active` requires a date of leaving, and a date of leaving cannot precede the date of joining.

**Uniqueness is per tenant, case-insensitive.** Department/designation code and name, and employee code and email, are unique within an organization and free to repeat across organizations. Duplicates are detected up front and answered with 409 plus the offending field; the unique index remains the real guarantee, and the race that slips past the check is caught and reported the same way rather than surfacing as a 500.

**Timestamps mean the same thing on both sides of the database.** `CreatedDate`/`ModifiedDate` are stamped in UTC by the save guard, but a provider hands `DateTime` back as `Unspecified` — so the value returned by a `POST` serialized with a `Z` while the very same row read a moment later serialized without one, and a client shifted it by its own offset. A model-wide value converter now marks every `DateTime` read as UTC (and converts a `Local` value on the way in rather than relabelling it). It keeps the store type, so it changes no schema and produces no migration.

### CSV export

`GET /api/employees/export` returns the same rows the list would return under the same filters — paging is deliberately ignored, since an export of "page 2 of 7" is not what anyone means — as a `text/csv` attachment named `employees-<yyyyMMdd-HHmmss>.csv`.

- **UTF-8 with a BOM and CRLF line endings**, so Excel opens non-ASCII names correctly without an import dialog.
- **Formula injection is neutralized**: a value starting with `=`, `+`, `-`, `@`, tab or carriage return is prefixed with an apostrophe, so a spreadsheet displays `=SUM(A1:A9)` as text instead of executing it. Values containing a comma, a quote or a newline are quoted with doubled quotes.
- **Capped at 10,000 rows.** A wider result set is refused with a 400 naming both numbers and suggesting a narrower filter, rather than streaming an unbounded response.
- The log line records the row count only — never the exported personal data.

---

## Seeded demo data

Seeding is deterministic (fixed ids) and idempotent (safe to run on every startup).

**Tenants**

| Code | Name | Status |
|------|------|--------|
| `DEMO01` | Demo Organization | Active |
| `DEMO02` | Sample Organization | Active |

**Users** — all share the development password **`Passw0rd!`**:

| Email | Tenant | Role |
|-------|--------|------|
| `admin@demo01.com` | DEMO01 | TenantAdmin |
| `hr@demo01.com` | DEMO01 | HRManager |
| `admin@demo02.com` | DEMO02 | TenantAdmin |
| `hr@demo02.com` | DEMO02 | HRManager |

**Roles**: SuperAdmin, TenantAdmin, HRAdmin, HRManager, Manager, Employee.
**Permissions**: `Resource.Action` grants for Employee / Department / Designation / User, mapped to roles.

**Departments**

| Tenant | Code | Name |
|--------|------|------|
| DEMO01 | `ENG` | Engineering |
| DEMO01 | `HR` | Human Resources |
| DEMO01 | `FIN` | Finance |
| DEMO02 | `OPS` | Operations |
| DEMO02 | `SLS` | Sales |

**Designations**

| Tenant | Codes |
|--------|-------|
| DEMO01 | `CTO`, `EM`, `SSE`, `SE`, `HRM`, `ACC` |
| DEMO02 | `OPSM`, `SR` |

**Employees** — DEMO01 gets a three-level reporting line so the manager relationship (and the loop check guarding it) has something real to run against; DEMO02 is deliberately smaller, with unrelated names, so a leak between the two is obvious rather than subtle.

| Tenant | Code | Name | Dept | Title | Reports to |
|--------|------|------|------|-------|------------|
| DEMO01 | `EMP-001` | Nadia Farrell | ENG | CTO | — |
| DEMO01 | `EMP-002` | Owen Brand | ENG | EM | EMP-001 |
| DEMO01 | `EMP-003` | Priya Raman | ENG | SSE | EMP-002 |
| DEMO01 | `EMP-004` | Diego Santos | ENG | SE | EMP-002 |
| DEMO01 | `EMP-005` | Mira Kovac | HR | HRM | EMP-001 |
| DEMO01 | `EMP-006` | Tomas Lind | FIN | ACC | EMP-001 |
| DEMO02 | `E-100` | Grace Okoro | OPS | OPSM | — |
| DEMO02 | `E-101` | Liam Hayes | SLS | SR | E-100 |

Seed ids are fixed, and their first digit encodes the tenant while the second encodes the entity — `10…`/`20…` departments, `11…`/`21…` designations, `12…`/`22…` employees. A cross-tenant mistake is then visible at a glance in test output and log lines.

> These are development seeds only — never ship these credentials.

---

## Frontend (React + TypeScript)

`Frontend/HRMS.Web` — Vite + React 19 + TypeScript, React Router for routing and Axios for transport.
No UI framework: the shell is plain CSS driven by design tokens, so nothing about the layout depends on
a component library's conventions.

```bash
cd Frontend/HRMS.Web
npm install
npm run dev
```

Then open <http://localhost:5173> and sign in with organization code `DEMO01`, email `hr@demo01.com`,
password `Passw0rd!` (see [Seeded demo data](#seeded-demo-data)). **The API must be running** — the
client talks to `VITE_API_BASE_URL`, which defaults to `http://localhost:5080`.

The dev server binds port 5173 with `strictPort`, deliberately: the API's CORS allow-list names exactly
that origin, so silently falling back to 5174 would turn every request into a preflight failure that
looks like a bug in the client.

| Script | What it does |
|--------|--------------|
| `npm run dev` | Dev server with HMR on <http://localhost:5173> |
| `npm run build` | `tsc -b` then `vite build` → `dist/` |
| `npm run preview` | Serve the built bundle |
| `npm run typecheck` | `tsc -b` only |
| `npm run lint` | oxlint |
| `npm run test:run` | Vitest once (what CI runs); `npm run test` watches |

### What is in it

- **Login** — organization code + email + password, matching the API's tenant-scoped sign-in. The last
  organization code is remembered locally (a convenience, not a credential) so a returning user types
  one field less. Field errors from the API land on the fields they name.
- **App shell** — sidebar navigation on wide viewports, becoming a horizontal strip above the content
  below 48rem (768px), and a header showing the signed-in user *and their organization*, because two
  tabs signed into different tenants must not be mistakable for one another.
- **Dashboard** — headcount tiles (total, active, departments, designations), recent hires newest-first,
  headcount by department, and a CSV export button.
- **Employees** — a list that pages, searches and sorts *on the server* (the client never holds more than
  one page), with department, designation and status filters. Every one of those lives in the query
  string, so a filtered view can be linked to, reloaded, or reached with the browser's Back button, and
  a save returns to the exact page and sort the user left rather than to a reset list.
- **Employee form** — one component for create and edit, mirroring the API's contract field for field.
  Its notable parts: the reporting-manager picker searches the server instead of loading every employee
  into a `<select>`, and keeps the currently-assigned manager selectable even when the search has moved
  on — otherwise saving would silently reassign them. `Date of leaving` appears only when the status is
  not Active, and is cleared rather than merely hidden when it does not apply, so a date typed and then
  reverted is never submitted. Unanswered optional fields are sent as `null`, never as `""`.
- **Departments and designations** — one list component and one form component, configured by a module
  object rather than duplicated, since the two resources differ only in wording and permissions. A
  `key` on each route element is what keeps that safe: without it React would reuse the mounted list
  when moving between the two, carrying the search box across from one resource to the other.
- **Deletes are confirmed, and refusals are explained where they happen.** The dialog says what will be
  removed and why a status change is usually the better answer. When the API refuses — a department that
  still has employees, an employee who still has reports — the reason renders *inside* the open dialog
  and the row stays, rather than as a banner somewhere the user is no longer looking.
- **Route guards** — `RequireAuth` (with the interrupted path preserved across sign-in) and
  `RequirePermission`, plus 403/404 screens inside the shell so there is always a way back.

### Token handling (and its trade-off)

**The access token lives in memory only** — a module variable, never `localStorage`, never a cookie. It
dies with the tab, so a stolen storage dump cannot contain it.

**The refresh token is written to `localStorage`** under `hrms.refreshToken.v1`, because surviving a
reload is the entire purpose of a refresh token. That is the honest cost: any script on the origin can
read it, so this one credential is exposed to XSS. The genuinely safe alternative is an
`HttpOnly; Secure; SameSite` cookie issued by the API, which no script can read — but that is a
server-side change (cookie issuance plus CSRF protection on every state-changing request), not
something the client can adopt on its own. It is the recommended hardening step before production.

Around it:

- **One shared refresh, never two.** A 401 triggers a single-flight refresh and the original request is
  replayed once. This is correctness, not tuning: refresh tokens are single-use server-side, so three
  requests refreshing independently would present a consumed token — indistinguishable from a replay,
  which the API answers by revoking every session for that user.
- **Refresh runs on a separate axios instance**, so a 401 from `/api/auth/refresh` cannot recurse into
  another refresh.
- **The user object is never read from storage.** Roles and permissions are only ever what the last
  login or refresh returned, both recalculated server-side — so a revoked permission takes effect on
  the next refresh rather than being overridden by a cached copy.
- **Signing out in one tab signs out the others**, via the `storage` event on the refresh-token key.

### Permission-aware UI, not permission-enforcing

`src/auth/permissions.ts` mirrors `Backend/HRMS.Domain/Authorization/Permissions.cs`, and a test reads
the C# file and asserts the two lists still agree — a permission added on the server cannot quietly go
missing here.

These checks are **cosmetic**: they decide what is *rendered*. Every endpoint is guarded by
`[HasPermission(...)]`, and a user who edits their own JavaScript gains nothing but a button that
returns 403. What they buy is an honest interface — a user without `Department.View` is not shown a
link that could only ever fail.

Gating decides whether a panel is **rendered**, not whether it is disabled — because for the data panels
rendering *is* the request. A dashboard card fetches on mount, so a user without `Department.View` gets no
card and no request, rather than an empty card carrying a 403. The export button is the same idea one step
along: it is rendered only for holders of `Employee.Export`, so no export request is ever issued on behalf
of someone who could not be granted one.

Both halves of that are asserted, since a missing button alone proves nothing about what was fetched:
`DashboardPage.test.tsx` checks that a user with no view permissions produces **zero** requests, and that a
Manager sees no export button *and* triggers no `GET /api/employees/export`.

**No tenant id is ever sent.** The client has no tenant parameter to send — `TenantId` comes from the
validated JWT server-side. A test walks every request the dashboard makes and asserts the word
"tenant" appears in none of them, so a future filter cannot casually reintroduce it.

---

## Migrations

Migrations live in `Backend/HRMS.Infrastructure` and target **SQL Server** (via the design-time factory). The API project is the startup project.

```bash
# Add a migration
dotnet ef migrations add <Name> --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API --output-dir Persistence/Migrations

# Apply migrations to a SQL Server database
dotnet ef database update --project Backend/HRMS.Infrastructure --startup-project Backend/HRMS.API
```

At runtime the SQL Server path applies migrations automatically; the SQLite dev path uses `EnsureCreated` and ignores migrations.

---

## Tests

### Backend

```bash
dotnet test
```

200 tests, xUnit. Two styles:

- **Unit / persistence tests** over SQLite in-memory: seed correctness and idempotency, password hashing, unique constraints (tenant code; per-tenant email; the same email allowed across tenants), **tenant isolation** (query-filter scoping, empty results when no tenant is resolved, and the `SaveChanges` guard forcing new rows onto the server-resolved tenant even when a foreign `TenantId` is supplied), JWT issuance, the claims `HttpTenantContext` reads, `AuthService` sign-in/refresh/sign-out rules, and the development database rebuild.
- **HTTP integration tests** (`WebApplicationFactory`) over the real pipeline — the actual bearer handler, authorization policies, validation filter and middleware. These are the ones that pin the security boundary: a token naming another tenant cannot read this tenant's data; tokens signed with another key, using a different algorithm, expired, or issued for another audience are refused; an access token cannot be used as a refresh token; a rotated refresh token stops working; endpoints declaring no authorization are closed by the fallback policy; and repeated sign-in attempts are throttled.

The organization modules add:

| Suite | What it holds down |
|-------|--------------------|
| `DepartmentServiceTests`, `DesignationServiceTests`, `EmployeeServiceTests` | CRUD, per-tenant case-insensitive uniqueness, 409s for in-use rows, reporting-line rules (self-management, indirect cycles, inactive managers), cross-tenant foreign keys rejected as validation failures, and cross-tenant reads answering 404 rather than 403 — a wrong-tenant id should not confirm the row exists. |
| `OrganizationSortingTests` | Every whitelisted `sortBy` value actually executes against the provider and orders as claimed, ties break on a unique key so paging neither repeats nor skips a row, `page`/`pageSize` are clamped rather than trusted, and a field that somehow reaches the service unvalidated falls back to the default order instead of throwing. This is the suite that caught a sort applied *after* projection, which EF could not translate at all. |
| `EmployeeExportTests` | Filters carried into the export, paging ignored, the row cap, BOM/CRLF, quoting, and formula-injection neutralization. |
| `TenantForeignKeyTests` | The composite `(TenantId, …)` keys at the database level: a hand-written insert pointing at another tenant's department, designation or manager fails on the constraint, not just in the service — and `Restrict` blocks deleting a department that still has staff or a manager who still has reports. |
| `OrganizationEndpointsTests` | The same rules over HTTP with real tokens — a permission per endpoint (and one resource's grant not carrying to another), 201 with a `Location` that resolves, a cross-tenant id answering 404 on *every* verb, an unsupported sort field and an out-of-range page size rejected by name, one field error per problem, 409s, the paging envelope, and the CSV download's headers (including a refused export arriving as JSON rather than as a file). |
| `AuditTimestampTests` | Timestamps come back from the database as UTC (including the nullable `ModifiedDate`), a Local value is converted rather than relabelled, and `CreatedDate` cannot be rewritten by an update. |
| `CorsPolicyTests` | The policy through the real pipeline: the dev origin is granted with credentials, an origin that is not on the allow-list gets no grant, a request with no `Origin` is untouched, and `Content-Disposition` is exposed — a browser-only contract that no ordinary API test can see, because CORS is enforced in the browser and the response looks identical from a bare HTTP client. |

### Frontend

```bash
cd Frontend/HRMS.Web
npm run test:run
```

197 tests across 18 files, Vitest + Testing Library + jsdom. Requests are not mocked at the function
level: a stub adapter replaces `axios.defaults.adapter`, so tests exercise the **real interceptor
chain** — the token header, the 401 refresh, the single-flight collapse, the envelope unwrapping — and
can then assert on the exact requests that were made.

| Suite | What it holds down |
|-------|--------------------|
| `api/client.test.ts` | A 401 refreshes once and replays the original request; concurrent 401s collapse onto a single refresh; a refused refresh ends the session; a replay that 401s again surfaces to the caller instead of looping; a 401 from login is bad credentials rather than an expired session; and signing out does not refresh first. |
| `api/session.test.ts` | The access token never reaches storage, the refresh token is read from storage on every use rather than cached, and a stored session is reported only while that token is present. |
| `api/employees.test.ts` | The CSV download: the filename parsed out of `Content-Disposition` (RFC 6266 encoded form preferred, percent-decoded, quoted form as fallback), a sensible name when the header is not exposed, the object URL revoked after use, and an error message read out of a JSON body that arrived as a `Blob` — which is how a refused export fails. |
| `auth/permissions.mirror.test.ts` | Reads `Permissions.cs` and asserts the TypeScript mirror declares exactly the same permissions, in the same order. |
| `auth/AuthProvider.test.tsx` | Restore-on-load spending exactly one single-use token, a dead stored token falling back to anonymous and clearing storage, signing out locally even when the server-side revoke fails, cross-tab sign-out, and `useAuth` throwing outside a provider rather than looking quietly anonymous. |
| `auth/guards.test.tsx` | `RequireAuth` *waiting* rather than deciding while a session is being restored, and `RequirePermission` explaining itself instead of rendering a screen full of 403s. |
| `pages/LoginPage.test.tsx` | Field errors landing under the fields the API named, the organization code remembered and prefilled, trimmed values sent with the password left untouched, no second submit while a sign-in is in flight, and the form redirecting away when the session is already good. |
| `pages/DashboardPage.test.tsx` | The counts, recent hires and headcount split rendering from the API's own shapes; the tenant named on the page; one panel failing without taking the rest down; the CSV download including its filename and a refused export explained rather than saved as an error page; no request carrying anything tenant-shaped; and permission gating asserted on *both* halves — no button **and** no request issued on that user's behalf. |
| `pages/employees/EmployeesPage.test.tsx` | Paging, search, sort and all three filters read *out of* the URL and sent to the server; a status a hand-edited URL invented dropped rather than forwarded; one search request per word rather than per keystroke; the export carrying the filters on screen but not the paging; a delete that names the person, re-reads the list, and steps back a page when it removed the last row on it; a refusal kept in the dialog in the server's words; the filters still usable when the directory itself fails to load; and no request carrying anything tenant-shaped. |
| `pages/employees/EmployeeFormPage.test.tsx` | The create body sending every unanswered optional as `null`; `0001-01-01`/empty-GUID sentinels for unset required values, so a missing date comes back as a field error on the input rather than as a deserialization failure keyed to `$.dateOfJoining`; `Date of leaving` appearing with `min` at the joining date, and being cleared rather than hidden when it no longer applies; the manager picker searching the server, debounced, while keeping the assigned manager selectable and the employee out of their own reporting line; a retired department still selected on an edit whose options list only actives; the full 14-field PUT; one save at a time; and a failed save keeping the edits and not re-reading the record. |
| `pages/lookups/LookupListPage.test.tsx` | The same list contract for both modules from one component: each asking its own endpoint sorted by name, the count column named for what it counts, a sort field the API would refuse discarded, an oversized page size clamped instead of rejected, a filter change returning to page one, each module reading *its own* permissions rather than any lookup permission, and an empty resource told apart from an empty filter. |
| `pages/lookups/LookupFormPage.test.tsx` | Create and edit for both modules: the code rule quoted before it is broken, trimmed values with an empty description as `null`, field errors under the inputs the API named and a field-less failure as a banner, the whole record sent on update so clearing the description clears the stored one, one save at a time, and a return path that does not address this module's own list ignored. |
| `components/ConfirmDialog.test.tsx` | It interrupts (`alertdialog`), names the record instead of "this item", opens with **Cancel** focused rather than the destructive button, stays open showing the server's words when a delete is refused, cannot be answered twice while the first answer is in flight, is not dismissible while it runs, and backs out on Cancel, Escape, the close button and the backdrop alike. |
| `components/Pagination.test.tsx` | Which *rows* these are rather than only which page, thousands grouped, nothing rendered when there is nothing to page through, the current page announced and not merely coloured, no step off either end, and `pageWindow` keeping the first, last and current pages with their neighbours without opening a gap where no page is skipped. |
| `App.test.tsx` | The route table end to end with the real provider inside StrictMode: anonymous → login → back to the interrupted path, a reload restoring the session without flashing the form, sign-out clearing the token, every navigation entry pointing at a route that exists, and a module the user cannot open left out of the navigation entirely. |

`auth/permissions.test.ts`, `lib/format.test.ts` and `lib/returnTo.test.ts` cover the pure helpers
underneath all of that: case-insensitive permission matching (the API's policies are case-insensitive, and
a case mismatch would hide a button the user can actually use), date formatting that keeps the calendar day
the API sent rather than shifting it into the browser's timezone, and the return path that sends a save back
to the exact list view it was opened from — while refusing one that addresses anything other than that
module's own list.

---

## Status

- **Phase 1 — Solution foundation, EF Core Code-First, DB creation & seeding.** ✅ Complete.
- **Phase 2 — Authentication & login (JWT), permission-based authorization, refresh-token rotation.** ✅ Complete.
- **Phase 3 — Organization structure & employees: department, designation and employee CRUD, paging/search/sorting, CSV export.** ✅ Complete.
- **Phase 4 — React + TypeScript frontend foundation: typed API client, session/token handling, route guards, app shell, dashboard.** ✅ Complete.
- **Phase 5 — Employee, department and designation screens: lists with server-side paging/search/sort and filters, create/edit forms, delete confirmation.** ✅ Complete.

Every module in the plan so far is built, tested and reachable from the UI; nothing beyond Phase 5 is scoped yet.

Authorization infrastructure is in place and exercised end to end (`[HasPermission]`, one policy per permission, tenant-claim requirement, fallback policy), and the three domain modules sit behind it with tenant isolation enforced on reads *and* writes. The frontend now drives that API for real — sign-in, session restore, refresh rotation, permission-aware navigation, a dashboard, the CSV export, and full create/read/update/delete for all three modules, with every list's paging, search, sort and filters carried in the URL so a view can be linked to and returned to.

