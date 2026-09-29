# Module 17 — Payroll Analytics / Reconciliation / Variance / Anomaly Controls (Control Totals,
Variance Analysis, Payroll-Run Comparisons, Anomaly Flags & Exceptions Register, Pre/Post-Payroll
Reconciliation, Findings Lifecycle, Effective-Dated Controls Administration, Department/Cost-Center
Dimension Summaries, Drill-Down, Filters, Reports/Export, Authorization/Scope, Tenant Isolation,
Concurrency/Idempotency, SQL Server/MySQL Provider Parity, Large-Data/Paging, Frontend, Integration
with Payroll/Statutory/Bank-Advice/GL)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 17-payroll-analytics-reconciliation` regenerates the module's CSVs/coverage stats, and a
full run across all seventeen modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No
automation implemented yet (Playwright is Phase 8+, on explicit approval). No product source was
changed to produce this module.

## Scope decision — read this before extending this module

This module covers exactly `Backend/HRMS.Application/Services/PayrollAnalyticsService.cs` (interface
`IPayrollAnalyticsService`, ~30 methods) and its four controllers, all declared in
`Backend/HRMS.API/Controllers/PayrollAnalyticsController.cs`: `PayrollAnalyticsController` (route
`api/payroll/analytics` — overview/summary/control-totals/department/cost-center/variance/
components/findings + 4 CSV exports), `PayrollExceptionsController` (route `api/payroll/exceptions`
— list + export.csv), `PayrollReconciliationController` (route `api/payroll/reconciliation` —
pre/post generate, get, acknowledge/resolve/accept-exception), and
`PayrollAnalyticsControlsController` (route `api/payroll/analytics-controls` — list/create/update of
`PayrollVarianceControl` rows). This is exactly the surface that `qa/16-gl-accounting-integration`'s
own scope note flagged as "no dedicated qa module yet" — this module fills that gap.

**Explicitly out of scope for this module** (present and real elsewhere, not covered here, and not
claimed as covered): payroll-run creation/calculation/approval itself (`qa/10-payroll`'s own surface
— Analytics only reads a run's `Status`/`PayrollPeriod` and persisted `PayrollResults`); bank advice
generation/approval/export itself (`qa/15`'s own surface — Analytics only reads
`BankAdviceBatches`/`BankAdvicePayments` as source evidence); GL journal generation/lifecycle itself
(`qa/16`'s own surface — Analytics only reads `PayrollJournalBatches`/`Lines`/`LineSources` as source
evidence, mirroring `qa/16`'s own reconciliation-integration-boundary scope note in reverse);
statutory return generation itself (`qa/13`'s own surface — Analytics only reads
`PayrollStatutoryResults`/`PayrollStatutoryReturnSources` as source evidence);
reimbursement/loan/variable-pay/adjustment/final-settlement *calculation* itself (`qa/11`, `qa/12`,
and the not-yet-numbered reimbursement/gratuity/leave-encashment/notice-settlement/final-settlement
modules — Analytics only reads their already-persisted, already-finalized rows as source evidence).
`docs/phase-7s-payroll-analytics-reconciliation-controls.md` was read in full and is the only phase
doc in this repository whose own body ends with a "COMPLETE" verification-log entry after several
earlier "NOT COMPLETE" entries in the same file — this module treats the feature as shipped and tests
the code as found, not the doc's intermediate history.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (145 functionalities: F-PYA-001…145, non-contiguous by design — F-PYA-144/145 were added after the initial authoring pass once 2 gaps were found). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-overview-summary-control-totals.yaml` … `cases/19-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-290…305 QA-risk register for this module (continuing after `qa/16-gl-accounting-integration`'s CR-289). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 17-payroll-analytics-reconciliation`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–16, no case in this module cites an `ex:` existing-test reference (see below for why).

**Build note:** `build_test_catalogue.py` overwrites `qa/HRMS_Test_Cases.xlsx` in full on every run —
it is not additive. Running it with `--module 17-payroll-analytics-reconciliation` alone regenerates
*this module's own* per-module CSVs/coverage-stats.json correctly, but if that single-module
invocation is the last one run, the saved workbook will contain **only** this module's sheets. Always
follow a single-module run with a full, no-argument run (`python qa/tools/build_test_catalogue.py`)
before treating `HRMS_Test_Cases.xlsx` as current — this is exactly what was done to produce the
workbook shipped with this module (all seventeen modules' sheets are present, confirmed by the
build's own per-module print output).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading, in full: `PayrollAnalyticsController.cs` (all 4 controller classes, every
action); `IPayrollAnalyticsService.cs` (21 interface methods) and `PayrollAnalyticsService.cs` (the
full ~400-line implementation, across two read passes); `PayrollAnalyticsDtos.cs` (11 DTOs); the
5-entity `PayrollAnalytics.cs` (`PayrollVarianceControl`/`PayrollAnalyticsSnapshot`/
`PayrollReconciliation`/`PayrollReconciliationFinding`/`PayrollAnomalyFlag`);
`PayrollAnalyticsConfiguration.cs` (all 4 EF configurations, explicitly checked for
`.IsConcurrencyToken()` on every `ConcurrencyVersion` property); `PayrollEnums.cs`'s
`PayrollAnalyticsSnapshotType`/`PayrollControlScope` (8 members)/`PayrollControlMetric` (9 members)/
`PayrollFindingSeverity`/`PayrollControlAction` (4 members)/`PayrollReconciliationType`/
`PayrollReconciliationStatus` (3 members)/`PayrollFindingStatus`/`PayrollAnomalyType` (14 members) —
every enum member cross-checked against actual service code for live-vs-dead usage; migration pairs
`AddPayrollAnalyticsPhase7S` and `AddHistoricalPayrollDimensionsPhase7S` confirmed present in both the
SQL Server and MySQL tenant chains; `Permissions.cs`'s 8-permission
`Payroll.Analytics*/Reconciliation*/Exceptions*` block; `SeedData.cs`'s `RolePermissionMap`,
grep-verified for every one of those 8 constants across every seeded role; `PayrollRunService.cs`/
`PayrollApprovalGuard.cs` (full grep for any control/action consumption — zero matches);
`PayrollAnalyticsPage.tsx` (+ its `.test.tsx`, read in full), `src/api/payrollAnalytics.ts` (read in
full), `App.tsx`/`layout/navigation.ts` route/nav-entry grep, and a full-repo grep for the literal
route path; and the existing 8-file xUnit inventory (`PayrollAnalyticsTests`,
`PayrollAnalyticsConcurrencyTests`, `PayrollAnalyticsLargeDataTests`,
`PayrollAnalyticsProviderAcceptance`, `PayrollAnalyticsReportingTests`,
`PayrollAnalyticsSourceReconciliationTests` [536 lines, the full 8-source matrix],
`MySqlPayrollAnalyticsIntegrationTests`, `SqlServerPayrollAnalyticsIntegrationTests` — referenced
narratively per the `qa/10-16` precedent, not individually classified).

**HEADLINE FINDING #1 — `ConcurrencyVersion` on `PayrollVarianceControl`/`PayrollReconciliation` is a
decorative, never-enforced, never-incremented integer, and `PayrollReconciliationFinding` has no
concurrency protection at all (CR-290).** This breaks with the `.IsConcurrencyToken()` convention used
throughout the rest of this codebase (AttendanceMonthly/AttendanceWorkflow/BankAdvice/CompOff, etc.).
Two concurrent writers to the same control or the same finding silently last-write-wins with no
conflict ever raised — the existing concurrency tests' own `catch(DbUpdateConcurrencyException)`
branches are unreachable dead code.

**HEADLINE FINDING #2 — no seeded role except SuperAdmin/TenantAdmin holds any of the 8
Analytics/Reconciliation/Exceptions permissions, out of the box (CR-291).** Identical shape to
`qa/15`'s CR-263 and `qa/16`'s CR-274 — now the THIRD consecutive Payroll-output module with this gap.
HRAdmin, HRManager, SuperHR, and the role literally named "Accounts" all hold zero.

**HEADLINE FINDING #3 — three of the four dimensions this module lets an administrator configure
(control `Scope`, control `Metric`, and `PayrollAnomalyType`) are each mostly dead (CR-293, CR-294,
CR-298).** 7 of 8 `PayrollControlScope` values are accepted and saved but never evaluated (only
`PayrollRun`-scoped controls are ever read); 3 of 9 `PayrollControlMetric` values always evaluate
against a hard-coded `0`, not the real metric; and 12 of 14 `PayrollAnomalyType` members are declared
but never constructed anywhere — only `NegativeNetPay`/`ZeroNetPay` ever populate the exceptions
register.

**HEADLINE FINDING #4 — the frontend wires 2 of this module's 22 backend endpoints and has no
navigation entry to its own single page (CR-296).** `PayrollAnalyticsPage.tsx` renders a 7-tile
overview summary and nothing else; control totals, dimension summaries, component variance, findings,
all 4 analytics CSV exports, the exceptions register, reconciliation generate/acknowledge/resolve, and
analytics-control CRUD have zero UI. `layout/navigation.ts` has no sidebar entry for it, and a
full-repo grep confirms no other page links to the route either.

**HEADLINE FINDING #5 — `PayrollReconciliation.Status` can never leave `Generated`, and
`PayrollVarianceRowDto.Flags`/`Classification` never reflect variance magnitude (CR-297, CR-299).**
`Acknowledged`/`Superseded` are declared and unreachable; every reconciliation row reports `Generated`
forever, even after every finding is Resolved or a newer version supersedes it. Separately, a 1000%
gross-pay swing and a 0.01% swing both classify identically as `"NormalComparable"` with an always-
empty `Flags` array — the DTO's own severity-classification fields carry no signal at all.

**HEADLINE FINDING #6 — every CSV export action in this module returns 400 for a failure that its own
JSON sibling correctly reports as 404/401 (CR-305), and `PayrollControlTotalsDto.TaxTotal` is always a
hard-coded `0` regardless of real persisted statutory totals (CR-303).** Both are silent, easily
missed divergences between two paths a caller would otherwise expect to behave identically.

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Run Overview & Summary | Employee count/totals filtering (current+Calculated only), bank/accounting aggregation, open/critical finding counts, anomaly count | Null-vs-zero bank/accounting totals | CR-303 (TaxTotal always 0) |
| Control Totals & Dimension Summaries | Source substring matching, historical Department/CostCenter snapshot capture, UNCLASSIFIED fallback (both null and stale-id), reconciliation to run totals | Sort stability | CR-303 |
| Employee & Component Variance | Explicit/default comparison-run selection, NewValue classification, Percent() zero-base/rounding, search, paging, CSV export | Cross-tenant compareRunId | **CR-299, CR-300, CR-302** |
| Reconciliation Generation (Pre/Post) | Zero-results gate, equation/negative/zero-net checks, run-level control evaluation window/RunType matching | — | Foundation for the whole source matrix below |
| Source Matrix — Bank/Accounting/Statutory/Cross-Module/Final-Settlement | All ~30 control codes across the full 8-source matrix (missing/mismatch/duplicate/exact, both positive and negative), reversal/adjustment linkage | Category-substring fallback matching | Mirrors the backend's own 536-line source-reconciliation test class, at the API/authorization/tenant layer |
| Reconciliation Retrieval & Versioning | Get by id, version increments, derived-once check counts | — | **CR-297** |
| Findings Lifecycle | Acknowledge/resolve/accept-exception, terminal-state guard (Acknowledged is not terminal), invalid action | Permission-sharing (resolve/accept-exception) | **CR-304** |
| Anomaly Flags & Exceptions Register | Tenant-wide unscoped listing, search-only filtering, dedup on regeneration, run-level null-EmployeeId rows, CSV export | — | **CR-298, CR-301** |
| Controls Administration | Full CRUD, Code+EffectiveFrom uniqueness (Create only, not Update), Scope/Metric dead-combo acceptance, IsActive/EffectiveTo window | No delete endpoint | **CR-290, CR-293, CR-294** |
| CSV Exports & Escaping | RFC 4180 escaping, formula-injection neutralization, empty-result header-only file, permission parity | — | **CR-305** |
| Authorization & Scope | Full per-permission negative/positive matrix across all 22 endpoints, role-seed audit, platform-token rejection | — | **CR-291, CR-292, CR-304** |
| Tenant Isolation | Cross-tenant proof across every read/write action, controls uniqueness scoping, exceptions register scoping, default-comparison-run scoping | — | — |
| Concurrency & Idempotency | Unique-index-enforced version race, unenforced-token race (acknowledge vs. resolve), control-update-vs-generate race | Journal-posting-vs-reconcile race | **CR-290, headline** |
| Large Data & Paging | 500-employee/2-run variance paging, dimension-summary reconciliation at scale, component aggregation at scale, PageSize clamp, beyond-last-page | — | — |
| SQL Server/MySQL Provider Parity | Shared acceptance helper proof, migration-pair proof, provider-independent concurrency-token gap | — | Reinforces CR-290 |
| Frontend | 2-of-22-endpoint proof, no-nav-entry proof, dead-export proof, permission gate, error rendering | Existing Vitest coverage | **CR-296, headline** |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| PYA_Overview_Summary_ControlTotals | 12 | Run overview/summary/control-totals, bank/accounting aggregation, the CR-303 defect |
| PYA_Dimension_Summaries | 10 | Department/Cost Center historical snapshot grouping, UNCLASSIFIED fallback |
| PYA_Employee_Variance | 13 | Comparison-run selection, NewValue/Percent() edge cases, the CR-299/CR-300/CR-302 findings |
| PYA_Component_Variance | 6 | Component grouping/union semantics, no-paging/no-CSV gap |
| PYA_Reconciliation_Generation | 12 | Pre/post generation, equation/negative/zero-net checks, run-level control evaluation |
| PYA_Source_Matrix_Bank_Accounting | 12 | Bank advice + accounting/GL source-evidence matrix |
| PYA_Source_Matrix_Statutory_CrossModule | 9 | Statutory PF/ESI/PT/TDS + reimbursement/loan/variable-pay/adjustment matrix |
| PYA_Final_Settlement_Matrix | 9 | All 8 Final Settlement categories, cross-source composition |
| PYA_Reconciliation_Retrieval_Versioning | 5 | Get by id, versioning, the CR-297 dead-status defect |
| PYA_Findings_Lifecycle | 10 | Acknowledge/resolve/accept-exception, terminal-state guard, the CR-304 finding |
| PYA_Anomaly_Exceptions_Register | 10 | Tenant-wide register, the CR-298/CR-301 findings |
| PYA_Controls_Administration | 11 | CRUD, uniqueness, the CR-290/CR-293/CR-294 findings |
| PYA_Exports_CSV | 7 | RFC 4180 escaping, formula injection, the CR-305 finding |
| PYA_Authorization_Scope | 12 | Full permission matrix, the CR-291/CR-292 headline findings |
| PYA_Tenant_Isolation | 6 | Cross-tenant proof across every action |
| PYA_Concurrency_Idempotency | 8 | The CR-290 headline concurrency-token gap, proven end-to-end |
| PYA_Large_Data_Paging | 5 | 500-employee scale proof, paging clamp |
| PYA_Provider_Parity | 5 | Shared acceptance helper, migration-pair proof |
| PYA_Frontend | 7 | The CR-296 headline frontend gap |

**Total: 169 test cases, 145 functionalities** (`python qa/tools/build_test_catalogue.py --module
17-payroll-analytics-reconciliation` regenerates and reports the authoritative counts; treat
`coverage-stats.json` as authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-290**: **Confirmed defect (headline)** — `ConcurrencyVersion` on `PayrollVarianceControl`/`PayrollReconciliation` is never wired as an EF concurrency token and never incremented; `PayrollReconciliationFinding` has no token at all.
- **CR-291**: No seeded role except SuperAdmin/TenantAdmin holds any of the 8 Analytics/Reconciliation/Exceptions permissions — the third consecutive Payroll-output module with this gap.
- **CR-292**: **Confirmed defect** — `Payroll.Exceptions.Manage` is declared and seeded but gates zero endpoints; no code path ever mutates a `PayrollAnomalyFlag`.
- **CR-293**: **Confirmed defect** — 7 of 8 `PayrollControlScope` values can be saved but are never evaluated.
- **CR-294**: **Confirmed defect** — a `PayrollRun`-scoped control using `ComponentAmount`/`BankAdviceTotal`/`GLTotal` always compares against a hard-coded 0.
- **CR-295**: `PayrollControlAction.BlockApproval`/`BlockFinalization` are stored/returned but enforce nothing anywhere in the payroll lifecycle.
- **CR-296**: **Confirmed defect (frontend, headline)** — the frontend wires 2 of 22 backend endpoints and has no navigation entry to its own page.
- **CR-297**: **Confirmed defect** — `PayrollReconciliation.Status` can never leave `Generated`; `Acknowledged`/`Superseded` are unreachable.
- **CR-298**: 12 of `PayrollAnomalyType`'s 14 members have zero corresponding functionality; only `NegativeNetPay`/`ZeroNetPay` are ever constructed.
- **CR-299**: **Confirmed defect** — `PayrollVarianceRowDto.Flags` is always empty and `Classification` is always one of exactly 2 fixed strings, regardless of variance magnitude.
- **CR-300**: **Confirmed defect** — `SortBy`/`SortDescending` are accepted and silently ignored by every list endpoint in this module, contrary to the documented shared-contract behavior.
- **CR-301**: The Exceptions register has no filter by type/severity/status/run/date — only free-text search on EmployeeCode/Message.
- **CR-302**: The default comparison-run selection for variance/component-variance is picked by recency alone, not by completion status.
- **CR-303**: **Confirmed defect** — `PayrollControlTotalsDto.TaxTotal` is always a hard-coded 0, never the run's real persisted statutory tax total.
- **CR-304**: `accept-exception` is gated by the same permission as `resolve` — there is no separate accept-exception permission.
- **CR-305**: **Confirmed defect** — every CSV export action returns 400 for a failure its JSON sibling would correctly report as 404/401.

Only CR-290, CR-292, CR-293, CR-294, CR-296, CR-297, CR-299, CR-300, CR-303, and CR-305 are asserted
as confirmed defects; every other CR is classified `Open - awaiting decision` per the classification
column in `clarifications.yaml` — each is exposed by specific test cases and awaits
engineering/product action.

## Existing automated coverage referenced by this module

Payroll Analytics has an 8-file existing xUnit inventory: a general `PayrollAnalyticsTests` workflow
class, a concurrency class, a large-data class, one provider-agnostic
`PayrollAnalyticsProviderAcceptance` helper, a CSV-reporting class, the 536-line
`PayrollAnalyticsSourceReconciliationTests` (the full 8-source matrix), and a SQL Server/MySQL
integration pair. Following the identical precedent set by Modules 8–16, **no case in this module
cites an `ex:` existing-test reference.** Every existing test class discovered during research is
instead documented narratively inside the relevant `src:` fields, the coverage matrix above, and
`clarifications.yaml`; `qa/tools/existing_test_layers.yaml` was **not** extended for this module.

A green run of `SqlServerPayrollAnalyticsIntegrationTests`/`MySqlPayrollAnalyticsIntegrationTests` is
evidence of provider parity for this module. Both fixtures are opt-in and skip silently when
`HRMS_SQLSERVER_TEST_CONNECTION`/`HRMS_MYSQL_TEST_CONNECTION` are absent, per the repository-wide
convention in `CLAUDE.md` — a green default-suite (SQLite in-memory) run is not evidence of either
provider's parity on its own.
