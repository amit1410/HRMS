# HRMS — Phase 1 Repository QA Assessment

Status: **DRAFT for review** · Baseline: `master` @ `7293569` · Scope of this document: Phases 1–4 *planning* only.
No product source was modified. No tests were executed in this session. No automation exists yet.

Companion file: [`endpoint-inventory.csv`](endpoint-inventory.csv) — 761 HTTP endpoints extracted from the controllers
(module, sub-module, verb, route, required permission, auth class). It is generated from source and should be regenerated, not hand-edited.

---

## 0. Method and confidence

| Verified directly in source | Inferred / not yet verified |
|---|---|
| All 71 controllers → routes, verbs, `[HasPermission]`, `[AllowAnonymous]`, rate-limit attributes | Field-level DTO/validator rules (done per module at module start) |
| `Permissions.cs` (281 permissions), `RoleNames`, seeded role→permission map in `SeedData.cs` | EF configuration / unique indexes / composite FKs per table |
| Frontend route table (`App.tsx`), page folders, API-client folders | Whether a page *calls* every endpoint it could (URL builders are dynamic; static grep was too noisy to trust) |
| Test inventory: xUnit attribute counts per file, Vitest case counts per folder | Whether existing tests actually pass on this machine (**not run**) |
| Auth internals: token rotation/replay handling, rate-limit partitioning, login DTO/validator | Provider-specific behavior on SQL Server (no SQL Server instance checked) |
| Hosted workers (3), enums for key state machines | Calculation correctness in payroll/leave engines (needs independent expected values — Phase 4) |

"Backend-only" claims below were confirmed with targeted greps of `Frontend/HRMS.Web/src` (not the noisy bulk heuristic).

---

## 1. Repository QA assessment

### 1.1 Architecture (as implemented)

- `API → Infrastructure → Application → Domain`, .NET 10, EF Core 10, FluentValidation (53 validators), Serilog.
- **Multi-tenant by host**: request host → catalog DB → tenant → shard (`ShardKey`/`DatabaseProvider`) → per-tenant DB. Tenant id then also enforced by EF global query filters + SaveChanges guard + composite `(TenantId, …)` FKs. Tenant id comes from host / JWT `tid` only.
- **Two identity domains**: tenant users (`/api/*`, JWT) and platform users (`/api/platform/*`, separate JWT, `PlatformBearer` + `PlatformPermission`). Platform routing runs before tenant resolution.
- **Providers**: MySQL (Development default, MySQL-only runtime), SQL Server, SQLite (tests). Three independent migration chains.
- **Authorization**: 281 permission policies, fallback-deny. Frontend gating is cosmetic (`RequirePermission`, `navigation.ts`).
- **Background workers**: `LeaveAccrualWorker`, `LeaveApprovalReminderWorker` (off by default), `AttendanceDevicePullWorker`.
- **Auth config**: tenant access token 60 min / refresh 7 d; platform access 15 min / refresh 14 d; auth rate limit 20 req / 60 s **partitioned by (tenant, IP)**; refresh tokens single-use with whole-session revocation on replay.
- **Frontend**: React 19 / Vite / Axios / React Router 7, ~185 page files, 109 route entries, host-aware API origin (`demo01.localhost:5173 → demo01.localhost:5080`, `platform.localhost` = platform UI, apex = workspace picker).

### 1.2 Size

| Item | Count |
|---|---|
| Controllers / HTTP endpoints | 71 / **761** |
| Declared permissions | 281 (+3 platform) |
| Seeded tenant roles | 14 (SuperAdmin, TenantAdmin, HRAdmin, HRManager, Manager, Employee, AccountLinkAdministrator, AccountLinkAuditor, EmployeeRelationshipOfficer, HRBP, TimeManager, IT, Accounts, SuperHR) |
| Domain entity files | ~95 |
| Backend test files / declared xUnit tests | 380 files / ~1,830 `[Fact]/[Theory]` + ~80 provider-gated custom facts |
| Frontend test files / Vitest cases | 99 files / ~690 cases |
| Docs | 61 design/runbook docs (phases 2–8, provider, platform) |

Endpoints per module (from `endpoint-inventory.csv`): Payroll 333 · Attendance 105 · Separation 84 · Leave 76 · Employee Mgmt 61 · Masters 49 · RBAC/Access 23 · Platform Admin 18 · Auth 12.

### 1.3 Existing automated coverage (approximate, by file naming)

| Area | Backend files | Backend tests | Frontend (Vitest) | Notes |
|---|---:|---:|---|---|
| Auth / security / tenant / platform / provider infra | 44 | ~310 | auth 31, api 58, lib 71, App 8 | Strong: refresh replay, CORS, forwarded headers, host routing, rate limit, shard resolution |
| Masters / org | 4 | ~47 | lookups 34 | Thin relative to 49 endpoints and 15+ hierarchical masters |
| Employee / linking / roles / page access | 22 | ~177 | employees 58, administration 15 | Reasonable service + HTTP tests |
| Leave | 30 (+ MySql/SqlServer concurrency files) | ~198 | leave 117 | Strong on submission/approval/cancel/withdraw accounting; several SQL Server concurrency tests provider-gated |
| Attendance / OT / Comp-Off | 89 | ~588 | attendance 138 | Heaviest covered area (incl. large-data, failure injection, concurrency) |
| Payroll (all sub-modules) | 118 | ~295 | payroll **30** (30 pages) | **Thin relative to 333 endpoints**; many are provider-gated single "acceptance" facts; only 5 facts in `PayrollCalculationTests` |
| Separation | 41 | ~153 | separation **11** (11 pages) | Good backend workflow/concurrency, minimal UI |

Layers actually present: unit (services/resolvers), integration (SQLite in-memory + `WebApplicationFactory`), provider-integration (MySQL/SQL Server, opt-in, **silently skipped** without env vars), jsdom component tests. **No live-browser or live-stack E2E layer exists.**

### 1.4 Current QA assets — and what's absent

Present: xUnit suite, Vitest suite, `scripts/test-*.ps1`, `tools/*` provider/mixed-provider validation runners, `docs/phase-3b-browser-acceptance-checklist.md`, `docs/payroll-*-checklist/runbook.md` (readiness, security hardening, UAT).

Absent: Excel test catalogue, traceability matrix, live UI/E2E suite, API contract/schema tests against a running stack, defect log, execution reports, test data seeding for a live environment, coverage reporting wired into CI (`test:coverage` exists but unused), any CI pipeline definition in the repo.

**Notable:** `phase-3b-browser-acceptance-checklist.md` records live browser acceptance as **BLOCKED / never completed** (API exited at startup; root cause not captured). The Playwright layer will therefore be the *first* live-stack UI validation this product has had. First job of any live run is confirming the stack boots.

### 1.5 Environment facts relevant to test design

- Local MySQL is listening on 3306 (observed). API (5080) and Vite (5173) were not running.
- Development is MySQL-only, **fails closed without secrets** (`Jwt:SecretKey`, `PlatformJwt:SecretKey`, `ConnectionStrings:Catalog`, `Sharding:MySqlConnectionStringTemplate`). DEMO01/DEMO02 are **not** seeded → tenants must be onboarded via the Platform API/UI, which itself needs a bootstrapped platform admin (`tools/PlatformAdminBootstrap`, interactive password).
- Consequence: **the test harness must create its own tenants** (A and B) through the platform API. That is a prerequisite for every cross-tenant test.
- Auth rate limit is 20/min per (tenant, IP). A naive suite that logs in per test will self-throttle (HTTP 429). Automation must log in once per role and reuse tokens / storage state; login-throttle tests need a dedicated tenant.
- `*.localhost` subdomains are needed for host-based tenancy (Chromium resolves them; verify Firefox/WebKit on Windows).
- Selector readiness: only **7 `data-testid`** in the frontend but **242 `aria-label`** uses → role/label-based Playwright locators are viable; recommend a small set of `data-testid` additions (Phase 8 list) rather than fragile CSS.

### 1.6 Seeded roles vs. permissions — verified observations

From `SeedData.RolePermissionMap` (the only writer of role grants, via `DatabaseSeeder`):

- `SuperAdmin`/`TenantAdmin` = every permission except `AccountEmployeeLink.*`.
- **`Employee`** is seeded with: Geography.View, Attendance Monthly-self / ExceptionView-self / CompOff-self, Leave request-create/own/withdraw/cancel/balance/type-available, Payroll AdjustmentsView(+History), TaxDeclaration own view/manage, Separation self flows. It is **not** seeded with `Attendance.View`, `Attendance.RegularizationRequest`, `Attendance.OnDutyRequest`, `Attendance.OvertimeRequest`, `Payroll.PayslipViewOwn`, `Payroll.LoansRequest`, `Payroll.ReimbursementsRequest`, `Payroll.VariablePayView` — yet the UI exposes routes guarded by exactly those permissions (my attendance, requests, my payslips, my loans, my reimbursements, my variable pay).
- No seeded role other than TenantAdmin/SuperAdmin/SuperHR/HRAdmin carries payroll permissions; `Accounts` role = `Employee.View` only; `IT` = user view/edit + link view.
- `HRBP`, `TimeManager`, `EmployeeRelationshipOfficer`, `IT`, `Accounts`, `SuperHR` have narrow grants; the workbook's "role × permission" matrix must be generated from the seed map, not from role names.

→ See CR-01. Test-data strategy must decide whether QA roles are the seeded defaults or are customised through Page Access Management (`PUT /api/page-access/roles/{id}`).

### 1.7 High-risk functional areas (ranked)

1. **Tenant isolation** (host, JWT `tid`, shard mismatch, composite FKs, query filters) — every module, every ID-taking endpoint (463 of 761 routes take a `{guid}`; 402 are POST/PUT/DELETE).
2. **Authentication lifecycle**: refresh rotation/replay, concurrent refresh, set-password/invitation, OTP forgot-password (email + SMS channels), login identifier modes, platform vs tenant token confusion, host↔token agreement.
3. **Payroll calculation & state machines** (run: Draft→Prepared→Processing→Calculated→Approved→Finalized/Cancelled; period: Draft→Open→Closed→Locked; formula/percentage/manual components, rounding, proration, statutory, retro, final settlement, year-end tax, reversals). Money correctness; thin existing calc tests.
4. **Object-level authorization ("me" and scoped endpoints)**: manager/HRBP/team scopes, `me/*` endpoints guarded by broad view permissions (see CR-03), sensitive-data endpoints (`EmployeeSensitive.*`, bank details).
5. **Leave balance accounting & concurrency** (reserve/consume/release, allocated balances, period close, accrual worker replay).
6. **Effective-dated employment / manager resolution** (employment history, supervisor, `EffectiveEmploymentResolver`, manager auto-provisioning) feeding leave, attendance, payroll, separation scopes.
7. **Attendance processing**: punch → day → month finalization, period locks, device sync/leases, timezone/business-date, overtime and comp-off payroll/leave integration.
8. **Imports/bulk**: employee import, master import, leave balance import, roster upload, payroll input batches (atomicity, duplicate handling, size limits).
9. **Cross-module state machines**: separation → clearance → settlement → payroll final settlement → exit execution (deactivates account? updates leave/attendance?).
10. **Provider parity**: rowversion vs `binary(16)` tokens, locks, filtered unique indexes, deadlock/lock-timeout classification.

### 1.8 Clarification / QA-Risk register (NOT defects — none confirmed yet)

| ID | Observation (source) | Why it matters | Needed to resolve |
|---|---|---|---|
| CR-01 | Seeded `Employee` role lacks `Attendance.View`, regularization/on-duty/overtime request, payslip-own, loans/reimbursements request, variable-pay view (`SeedData.cs`), while UI routes/pages exist for them | Either intentional (tenant configures via Page Access) or a provisioning gap; determines every self-service test's preconditions | Product owner confirmation |
| CR-02 | `GET /api/attendance/comp-off/operations` has only `[Authorize]`, no `[HasPermission]`; authorization is enforced inside `CompOffService.BuildOperationalScopeAsync` (tries ViewAll → ViewTeam → …) | Inconsistent with "permissions guard endpoints"; must verify a user with *no* CompOff view permission gets 403, not an empty 200 | Test (low effort) |
| CR-03 | `me/*` endpoints for adjustments, variable pay, separation-benefits are guarded by the *general* view permission (`Payroll.AdjustmentsView`, `VariablePayView`, `SeparationBenefitsView`), which also guards HR list endpoints; `Employee` role holds `AdjustmentsView` | IDOR/scope leak risk: does `GET /api/payroll/adjustments` (and `/{id}`, `/reasons`) scope an Employee to own records? | Test with Employee token against HR endpoints |
| CR-04 | All 20 payroll report endpoints **and their CSV exports** require only `Payroll.AnalyticsView` (no separate export permission, unlike attendance/leave/employee) | Export not separately controllable; salary data exfiltration by any analytics viewer | Product decision |
| CR-05 | `DELETE /api/masters/{kind}/{id}` soft-deactivates when unreferenced, but returns **409** when referenced ("Deactivate it instead") — although the delete *is* a deactivate | Behaviour contradicts message; unclear whether referenced masters can ever be deactivated via this API | Clarify intended semantics |
| CR-06 | `PUT /api/employee-code-configuration/rules/{id:guid?}` — optional route id on PUT | Ambiguous with `POST .../rules`; behaviour when id omitted? | Test |
| CR-07 | Generic `POST /api/payroll/periods/{id}/{actionName}` and `.../employee-salary-assignments/{id}/{actionName}`; run transition uses `?target=` enum in query string | Free-text action names → need full enumeration of accepted/rejected actions, case-sensitivity, `unlock` bypass (controller returns Forbid for `unlock` on the generic route) | Test matrix |
| CR-08 | "Remember me" is client-side only (`session.ts`: refresh token in localStorage vs memory); no server-side session-lifetime difference | Cannot test as a server rule; refresh token in localStorage is an XSS-exposure design choice | Note; confirm intent |
| CR-09 | No account lockout / failed-attempt counter found in `AuthService`; only IP+tenant rate limiting | Brute-force resistance rests on rate limiter alone | Confirm intent |
| CR-10 | `SystemController` `CurrentPhase` constant reads "Phase 7 - whitelabel host resolution…" although phases 7x/8x payroll/separation shipped | Cosmetic/stale metadata | Low |
| CR-11 | Tax-declaration **HR-side** endpoints (cycles, categories, items config, review, approve, lock, reopen, audit) — frontend API client only has `my*` functions | Backend-only capability → API tests only, no UI journey | Confirm UI is planned |
| CR-12 | `MasterLookup` endpoints for banks, grades, cost centres, etc. all use `Department.View` | A user with department view sees all org masters — likely intentional; document | Low |
| CR-13 | Repo root contains many scratch artefacts (`token.txt`, `login*.json`, `e2e-token.txt`, logs, `tools/phase3b-browser-state.credentials.txt`). `git ls-files` shows none are tracked | Hygiene only; QA must not read/copy them | None |

### 1.9 Implementation-status classification (verified by targeted grep)

| Class | Items |
|---|---|
| **Implemented (backend + frontend)** | Tenant auth, platform login/tenants, role assignment, page access, account linking, masters (generic + lookups + import), employee CRUD/sub-sections/portal account/export/import, employee code config, leave (types, policies, periods, working-day calendar, requests, approvals, balances, import, reports, dashboard, calendar), attendance (foundation, roster, workflow, monthly, operations, devices, reports, OT, comp-off), payroll core (components, structures, assignment, periods, runs, payslips, bank advice, accounting, retro, final settlement, statutory, loans, reimbursements, variable pay, adjustments, inputs, off-cycle, analytics, reports, year-end tax, filings), separation (request, approvals, notice, clearance, exit interview, documents, settlement, exit) |
| **Backend only (no UI caller found)** | `POST/…/api/payroll/reversals*` (3), `GET/PUT /api/payroll/controls` (page for *health* exists, not controls edit), `/api/attendance/admin-corrections*` (3; the *operations* manual/bulk correction endpoints do have UI), `POST /api/attendance/comp-off/policies`, `POST /api/attendance/overtime/policies`, tax-declaration cycles/categories/items/review/approve/lock/reopen/audit (CR-11), `GET /api/system/info` |
| **Partially implemented (per design docs)** | Leave: delegation, leave pool, quarter/hour units, partial cancellation, rejection/cancellation reasons, approval-based cancellation, request modify, notice bands — all documented as deferred. Attendance: earlier phase docs defer vendor/biometric adapters; phases 6f/6g later add a device-integration foundation — scope of real vendor connectors to be confirmed at Attendance start |
| **Needs clarification** | CR-01, CR-04, CR-05, CR-09 |
| **Missing vs. brief** | "Remember me" as a server feature (CR-08); account lockout (CR-09). Other brief items (Work Location, Cost Center, Bank masters) exist as generic-master kinds — confirm UI parity at Masters start |
| **Deprecated/legacy** | `LegacyMasterRedirect` (`/departments/*`, `/designations/*` redirect to `/masters/:kind`); legacy login field `Email` beside `Identifier` in `LoginRequest` |

---

## 2. Module inventory

Legend — Backend: controllers · Frontend: page area · Permission: representative · Tests: existing coverage (xUnit / Vitest, approximate).

| # | Module | Sub-module | Backend controller(s) | Frontend | Key permission(s) | Existing tests |
|---|---|---|---|---|---|---|
| 1 | Platform Admin | Login/session | PlatformAuth | `/platform/login` | (platform JWT) | PlatformTenantAuthorization, PlatformRequestRouting, PlatformIdentityCatalogModel; Vitest platform 21 |
| | | Tenant onboarding / list / update / activate / deactivate / retry / reset admin password | PlatformTenants (8) | `PlatformTenantsPage` | PlatformTenant.View/Create/UpdateStatus | TenantProvisioning, PlatformTenantPasswordReset, Recovery/RequestValidator |
| | | Branding & login/recovery settings | Tenants (5) | `TenantLoginSettingsPage`, `TenantPasswordRecoverySettingsPage` | `User.Edit` (branding anonymous) | TenantBranding* |
| | | System info (anonymous) | System | — | none | SystemControllerTests |
| 2 | Authentication & Security | Login / refresh / logout / me / set-password / forgot-password (OTP send/resend/verify/reset) / change-password / my profile | Auth (10), Me (2) | Login, SetPassword, ForgotPassword, ChangePassword, MyProfile | anonymous / authenticated | AuthEndpoints 22, AuthServiceLogin 19, Refresh 15, Jwt 10, Password recovery ~25, TokenHostAgreement, Cors ~20, ForwardedHeaders, RateLimit |
| | | Tenant resolution / shard / host | middleware | WorkspacePicker | — | TenantShardResolver, TenantHostRouting, ShardContext, HttpTenantContext, TenantIsolation |
| 3 | RBAC & Access | Role assignments (assign/revoke/history/candidates) | RoleAssignments (8) | RoleManagementPage | RoleManagement.* | RoleAssignmentService 11, Concurrency 7 |
| | | Page access matrix + navigation | PageAccess (7) | PageAccessManagementPage | PageAccess.View/Manage | MySqlPageAccessAuthorization; AuthorizationMatrix |
| | | Account ↔ Employee linking | AccountEmployeeLinks (8) | AccountEmployeeLinksPage | AccountEmployeeLink.* | AccountEmployeeLink* ~13; browser acceptance **never completed** |
| 4 | Masters | Geography (country/state/city), Department, Designation | 5 controllers (25) | lookups + `MasterManagementPage` | Geography.*, Department.*, Designation.* | Organization* 27, Department 18, Designation 10, TenantGeographySecurity |
| | | Generic hierarchical masters (HoldingCo, LoB, Organisation, Sub-dept, Section, Sub-section, Function, Sub-function, Grade, Employee Type, Work Location, Cost Center, Position Change Reason, Bank) + lookups + import | MasterManagement (5), MasterLookup (16), MasterImport (3) | `MasterManagementPage` | Geography.*, Department.View | MasterManagementTests 2 (**thin**) |
| 5 | Employee Management | Employee core (list/search/filter/sort/page, create, update, delete, personal details, sensitive details, export) | Employees (13) | EmployeesPage, EmployeeFormPage, EmployeeDetailPage | Employee.*, EmployeeSensitive.* | EmployeeService 29, Export 14, PersonalDetails 11, Sensitive |
| | | Sub-resources: contact, addresses, family, education, previous employment, bank details, supervisor, additional info, documents, audit log | EmployeeSubResources (36) | section forms | Employee.View/Edit, EmployeeSensitive.* | EmployeeBankDetails 13, ContactSync 5, … |
| | | Employment / effective-dated history / manager | EmployeeSubResources (7) | EmploymentSectionForm | EmploymentHistory.View/Change | EmployeeEmploymentHardening 22, Endpoints 9, E2E 10 |
| | | Portal account (create/resend/revoke) | Employees (4) | PortalAccessCard | User.View/Create | EmployeePortalAccountService |
| | | Employee import batches | ImportBatch (4) | (verify) | Employee.Import | ImportBatch (verify) |
| | | Employee code configuration (Manual/Auto, Simple/Rule-based, rules, conditions, preview, sequences) | EmployeeCodeConfiguration (8) | EmployeeCodeConfigurationPage | EmployeeCodeConfiguration.* | EmployeeCodeRuleMatcher, EmployeeCodeEmploymentFlow 13 |
| 6 | Leave | Types, Policies (versions, eligibility, entitlement, request rules, calendar, attachments, clubbing, cancellation, publish/retire), Periods, Holidays/weekly-off | 6 controllers (~50) | leave/* pages | Leave.Type/Policy/Period* | LeavePolicyFoundation 17, RuntimeEvaluator, WorkingDay, PeriodResolver |
| | | Requests (preview/submit/approve/reject/withdraw/cancel), approvals inbox, balances, calendar, options, reminders, accrual, period close | LeaveRequests, LeaveApprovals, LeaveBalanceSummary, LeaveCalendar, LeaveRequestOptions + workers | Apply / MyRequests / Approvals / TeamCalendar | Leave.Request*, Leave.Approve | ~90 tests incl. SQL Server concurrency (gated) |
| | | Balance import, reports (8 + CSV), HR dashboard | LeaveBalanceImports, LeaveReports, HrLeaveDashboard | Import / Reports / Dashboard | Leave.Balance*, Leave.Reports* | LeaveBalanceImport 5 |
| 7 | Attendance | Shifts, patterns, applicability, roster + roster upload | AttendanceFoundation (17) | AttendanceFoundationPage, ShiftPatterns | Attendance.ShiftManage… | Foundation, Applicability, RosterQuery/Calendar 30+ |
| | | Employee/manager views, regularization, on-duty | AttendanceRead, AttendanceWorkflow | MyAttendance, Requests, ManagerAttendance | Attendance.View, …Request/Approve | AttendanceWorkflow 24, HTTP harnesses |
| | | Punch processing, monthly close/reopen, exceptions/operations, admin corrections, period locks | AttendanceMonthly, AttendanceOperations, AttendanceAdminCorrections | Monthly/Operations | Attendance.Monthly*, Exception*, AdminCorrectionManage | MonthlyFinalization 32, Operations ~60 |
| | | Devices (register/map/sync/import/health/issues) + pull worker | AttendanceDevices (18) | AttendanceDevicesPage | Attendance.Device* | DeviceLease/Concurrency/Recovery ~60 |
| | | Reports (daily/monthly/exceptions + export) | AttendanceReport (6) | AttendanceReports | Attendance.ReportView/Export | AttendanceReport 11 |
| | | Overtime, Comp-Off | Overtime (9), CompOff (13) | OvertimePage, CompOff pages | Attendance.Overtime*, CompOff* | Overtime ~60, CompOff ~55 |
| 8 | Payroll | Salary components / structures / employee assignment | 3 controllers (28) | 3 pages | Payroll.SalaryComponent*/Structure*/EmployeeSalary* | 13 tests |
| | | Periods, runs, calculation engine, readiness, controls | PayrollPeriods, PayrollRuns, PayrollControls, PayrollOperations | PeriodsRuns, ConfigHealth, Ops, ProdHealth | Payroll.Period*/Run* | PayrollPeriodRun 2, PayrollCalculation 5, Controls 7 |
| | | Inputs & bulk, adjustments, reversals, off-cycle, loans | 5 controllers (65) | pages (reversals: **no UI**) | Payroll.Input*/Adjustments*/OffCycle*/Loans* | ~70 |
| | | Payslips, register, bank advice, accounting/GL, retro, final settlement, statutory returns | PayrollOutputs (51) | BankAdvice, Accounting, Retro, FinalSettlement, StatutoryCompliance | Payroll.Payslip*/BankAdvice*/Accounting*/Retro*/FinalSettlement*/StatutoryCompliance* | ~50 |
| | | Statutory config & employee profile, statutory filings (external connectors) | StatutoryPayroll (7), StatutoryFilings (19) | StatutoryConfigurations, StatutoryFilings | Payroll.Statutory* | ~20 |
| | | Tax declarations, year-end tax | TaxDeclarations (21), YearEndTax (15) | MyTaxDeclarations, YearEndTax (HR review UI **absent**) | Payroll.TaxDeclaration*, YearEndTax* | ~35 |
| | | Variable pay, reimbursements/claims, gratuity/separation benefits | 3 controllers (58) | VariablePay, Reimbursements, SeparationBenefits (+My*) | Payroll.VariablePay*/Reimbursements*/SeparationBenefits* | ~50 |
| | | Analytics/reconciliation, reports (20) | PayrollAnalytics (23), PayrollReports (20) | PayrollAnalytics, PayrollReports | Payroll.AnalyticsView (+ Reconciliation*) | ~30 |
| 9 | Separation | Core: reasons, self/initiated requests, submit/withdraw, manager & HR review, LWD revision, notice/waiver | Separation (24) | MySeparation, SeparationInbox | Separation.* | ~50 |
| | | Clearance / no-dues / assets | SeparationClearance (15) | ClearanceOperations | Separation.Clearance* | ~20 |
| | | Exit interview (templates, self-response, HR) | SeparationExitInterview (16) | MyExitInterview, ExitInterviewHr* | Separation.HrReview/ClearanceConfigure | ~25 |
| | | Documents (templates, generate, approve, issue, supersede) | SeparationDocument (17) | SeparationDocuments, templates | Separation.Manage/ViewAll | ~25 |
| | | Settlement orchestration + Exit execution | SeparationSettlement (6), SeparationExit (6) | SettlementDashboard, ExitClosure | Separation.Manage | ~35 |
| 10 | Cross-cutting | Imports (employee, master, leave balance, roster, payroll input) | 5 controllers | 5 pages | various | partial |
| | | Reports (leave, attendance, payroll, analytics) | 4 controllers (~55) | report pages | *.ReportView / AnalyticsView | partial |
| | | Audit/history (employee audit log, role events, page-access history, per-module histories) | various | history panels | *.ViewHistory | partial |

---

## 3. Functionality groups (Phase 3 seed — expand per module at module start)

Each functionality will be mapped to: UI route · API endpoint(s) · service · permission · DB tables · existing tests, in the Traceability sheet.

- **Authentication**: F1 Tenant login (identifier modes: EmailOnly / EmployeeCodeOnly / EmailOrEmployeeCode; TenantAdmin exemption) · F2 Refresh rotation · F3 Replay / concurrent refresh → session revocation · F4 Logout idempotency · F5 `me` / navigation · F6 Set password (invitation, expiry 24 h) · F7 Forgot password OTP (channels, resend, verify, reset, rate limits) · F8 Change password · F9 Host↔token agreement / shard mismatch · F10 CORS & forwarded headers · F11 Rate limiting · F12 Platform login/refresh/logout · F13 Platform vs tenant token isolation.
- **Platform**: tenant create (validation, provisioning, provider/shard selection) · list/search · update (inactive only) · activate/deactivate (effect on login) · retry provisioning · reset admin password · branding · anonymous branding endpoint.
- **Masters**: per-kind list/get/create/update/deactivate · parent/child dependency · uniqueness (code/name, per tenant) · paging (max 200) · sorting · import (template/validate/confirm) · lookup endpoints.
- **Employee**: list/search/filter/sort/paging · create (code Auto/Manual) · personal · contact · addresses (same-as-current) · family · education · previous employment · bank (sensitive) · statutory identifiers · supervisor · additional info · employment change (effective-dated) · history/current · documents · audit · status (Active/Resigned/Terminated) · delete · export · import · portal account.
- **Employee code**: config (mode, method, prefix/sep/padding/next number, reset period, sequence scope) · rules & conditions (Equals on 16 master fields) · preview · effective dates · Draft/Active/Inactive · concurrent generation.
- **RBAC/linking**: (see §2).
- **Leave / Attendance / Payroll / Separation**: see §2 rows; each row becomes ≥1 functionality group; payroll splits into the 12 workbook sheets proposed in the brief.

---

## 4. Proposed test-case breakdown

Counts are **planning estimates, not caps** — every discovered business rule gets cases. "Cat." = case categories.

| Module | Functionalities needing cases | Cat. | Depends on | Priority | Est. cases |
|---|---|---|---|---|---:|
| Authentication & Tenant Security | F1–F13 above | +ve, −ve, validation, boundary (identifier 256 / password 128), token expiry/skew, replay/concurrency, rate limit, CORS, host manipulation, XSS-storage note | none (needs 2 tenants) | **P0** | 180–230 |
| Platform Administration | login, tenant lifecycle, provisioning failure/retry, password reset, branding, platform↔tenant isolation | +ve, −ve, authz, lifecycle, isolation | Auth | **P0** | 90–120 |
| Cross-tenant matrix (every module) | ID-swap on all `{guid}` endpoints, token/host mismatch, export/report/approval/link/assign | security/tenant | each module's data | **P0** | 250–350 |
| RBAC & Access | role assign/revoke/history, Manager auto-provisioning, effective permissions, page-access matrix, navigation, API-vs-UI gating | authz matrix (14 roles × endpoints), workflow | Auth, Employee | **P0** | 170–220 |
| Account ↔ Employee linking | candidates, link/unlink/replace, self-link, duplicate, history, future-joining/separated rules | +ve/−ve/authz/concurrency/cross-tenant | Employee, RBAC | P1 | 80–100 |
| Masters | 15+ kinds × CRUD/validation/dup/hierarchy/deactivate/reference-block/import | field-level, hierarchy, uniqueness, import | Auth | P1 | 300–380 |
| Employee Management | all §3 groups, effective dating, sensitive data, export/import, sorting/paging server-side | full matrix | Masters, Code config | **P0** | 420–520 |
| Employee code generation | modes × methods × segments × reset periods × scopes, rule matching, concurrency, tenant isolation | data-driven + concurrency | Masters | P1 | 110–140 |
| Leave | type/policy/version lifecycle, eligibility, entitlement, proration, request rules, half-day, working-day calc, balances, reserve/consume, approve/reject/withdraw/cancel, imports, reports, dashboard, calendar, accrual/reminder workers, period close, comp-off link | state machine, boundary, calc, concurrency, scope | Employee, Masters, Attendance calendar | **P0** | 600–750 |
| Attendance | shifts/roster/applicability, punch→day, regularization/OD, monthly close/reopen/locks, exceptions/bulk, admin corrections, devices/sync/leases, reports/export, overtime, comp-off | state machine, timezone, large data, concurrency, scope | Employee, Leave (calendar), Masters | P1 | 650–800 |
| Payroll (12 sub-sheets) | components, structures, assignment, periods, runs, **calculation (known-value oracle)**, inputs, adjustments, loans, statutory, tax, reports, final settlement, reimbursements, variable pay, filings, accounting, bank advice | calc oracle, state machine, rounding, proration, authz, concurrency, provider | Employee, Attendance snapshot, Leave, Separation | **P0** (calc, run, authz) / P1 (rest) | 1,300–1,700 |
| Separation | request, approvals, notice/waiver, LWD, clearance, exit interview, documents, settlement, exit execution + integrations (leave/attendance/payroll) | state machine, authz, cross-module | Employee, Payroll (settlement) | P1 | 420–520 |
| Imports / Bulk | employee, master, leave balance, roster, payroll input | atomicity, dup, errors, size | owning module | P1 | 140–180 |
| Reports | leave (8), attendance (3), payroll (20+), analytics — filters/sort/page/totals/export/scope | data-driven | owning module | P1–P2 | 200–260 |
| Provider parity matrix | row-version, locks, filtered indexes, sequences, leave reservations, payroll concurrency | provider | env vars | P1 | 40–60 (matrix rows, mostly "existing xUnit; NOT EXECUTED if env absent") |

Order-of-magnitude total ≈ **5,000–6,000** manual cases across ~35 worksheets; roughly 35–45 % will be API-automatable, 10–15 % UI, remainder existing xUnit/Vitest or manual.

---

## 5. QA folder / artifact proposal

```
D:\HRMS\
├─ qa\                                   # new; QA-owned, versioned in git except results
│  ├─ 00-analysis\                       # this folder (assessment, endpoint inventory)
│  ├─ 01-test-cases\
│  │   ├─ HRMS_Test_Cases.xlsx           # master workbook (sheets per module; see brief)
│  │   ├─ HRMS_Defect_Log.xlsx           # or Defects sheet inside the workbook
│  │   └─ traceability\                  # generated CSV/xlsx for review diffs
│  ├─ 02-test-data\                      # data *specifications* only (no secrets, no real data)
│  ├─ 03-reports\                        # execution summaries (md/xlsx) — gitignored except templates
│  └─ tools\                             # workbook builder / coverage-review scripts (python)
├─ automation\                           # Playwright-Python suite (Phase 8+, per brief structure)
│  └─ reports\ {allure-results, screenshots, traces, videos, logs}   # gitignored
```

Rules: `.env` from `.env.example` only; secrets never in workbook or repo; results/traces/videos/screens **gitignored**; workbook and Python source **committed** (only when you say so); add `qa/03-reports/*`, `automation/reports/`, `automation/.env` to `.gitignore` when the folder is adopted.

Workbook engineering note: a large multi-thousand-row xlsx diffs poorly. Recommend generating it from small, reviewable **per-module source files** (CSV or Python data) via a builder script in `qa/tools`, so reviews happen on text diffs and the .xlsx is a build artefact. I will do that only if you agree.

---

## 6. Phase-1 recommendation

**Start with Authentication & Tenant Security (incl. Platform login and the tenant-host/JWT rules), in this order:**

1. Tenant auth lifecycle (login modes, refresh rotation/replay, logout, set-password, forgot-password, change-password).
2. Tenant resolution & isolation primitives (host, `tid`, shard mismatch, CORS, forwarded headers, rate limit).
3. Platform login + tenant onboarding (needed to *create* tenants A/B for every later module).

Justification from the repo, not preference: (a) every other module's tests need authenticated roles and two isolated tenants; (b) the cross-tenant framework built here is reused verbatim by every later module; (c) it is small (≈12 auth endpoints + 12 platform endpoints) with rich existing xUnit coverage, so gaps are easy to see; (d) it forces resolution of environment blockers (secrets, platform-admin bootstrap, `*.localhost`, rate-limit design) that otherwise stall Phase 6/8; (e) live browser acceptance has never been completed (§1.4) — this is where we discover whether the stack boots reliably.

Second module: **Masters → Employee code config → Employee** (they are the data foundation), then RBAC.

## 7. Questions I need answered before Module 1 finishes (none block starting test-case design)

1. Environment: may I start MySQL-backed API+Vite locally for exploratory verification, and is there a disposable catalog/tenant DB pair (never `hrms_catalog`)? Who supplies the platform-admin bootstrap password?
2. CR-01: are the seeded `Employee` grants intentional, or should QA roles be customised via Page Access?
3. Is SQL Server available for provider runs? (Otherwise every SQL Server row will be reported `NOT EXECUTED`.)
4. Workbook approach: hand-maintained `.xlsx`, or generated from per-module source files (recommended)?
5. Confirm `qa/` + `automation/` as top-level folders (or another location).
