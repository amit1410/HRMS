# Module 12 — Bonus, Incentives & Variable Pay (Variable-Pay Plans and Effective-Dated Published Versions,
Eligibility/Salary-Basis/Proration-Driven Award Calculation and Generation, Tenant/Year Award Numbering,
Submit/Approve/Reject Lifecycle, Manual Override, Payroll/Final-Settlement Settlement Recording, the
Paginated Award Register, the Payroll Reports Variable-Pay Export, Authorization/Scope, Tenant Isolation,
Audit/History, SQL Server/MySQL Provider Parity, Large Data/Paging, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 12-bonus-variable-pay` regenerates the module's CSVs/coverage stats, and a full run across all
twelve modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation implemented yet
(Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## Scope decision — read this before extending this module

This module covers the phase 7Q surface: `VariablePayPlan`/`VariablePayPlanVersion` configuration,
eligibility/salary-basis/proration-driven award calculation (Preview and Generate, including
`GenerateBulkAsync`), tenant/year award numbering, the Submit/Approve/Reject lifecycle (including the
Submit-skipping shape and the narrow self-approval guard), Manual Override, Payroll/Final-Settlement
settlement recording (`VariablePaySettlement`), Cancellation, the paginated award register
(`GetAwardsAsync`), the dedicated Payroll Reports variable-pay export (settlement-level, distinct from the
award register), Authorization/Scope, Tenant Isolation, Audit/History (`VariablePayAwardHistory`), SQL
Server/MySQL Provider Parity, Large Data/Paging, and Frontend (`VariablePayPage`/`MyVariablePayPage`).

**Explicitly out of scope for this module** (present and real in the codebase, not covered here, and not
claimed as covered): Variable-Pay GL/accounting journal generation. Unlike Loans (which at least has
dedicated `PayrollGLMappingType.Loan*` mapping types), a full read of `PayrollAccountingService.cs`'s
`supportedMapping` switch shows **no `PayrollGLMappingType` exists for variable pay at all** — a
variable-pay Final Settlement line only ever surfaces generically as `FinalSettlementLineType.Bonus`, with
no dedicated GL account mapping type anywhere in the codebase. This module verifies that a variable-pay
settlement correctly produces a `VariablePaySettlement` row and updates `VariablePayAward` state (the
business outcome), not that a balanced GL journal is subsequently posted from it (the accounting outcome —
there is nothing to test, since no such mapping type exists). Full Performance Management/KPI/OKR
calculation, sales commission engines, ESOP/RSU/equity compensation, and arbitrary formula scripting are
explicitly deferred per `docs/phase-7q-bonus-incentives-variable-pay.md` and are not testable, not a gap in
this module's own execution.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (131 functionalities: F-VPAY-001…131). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-plans-configuration.yaml` … `cases/12-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-201…218 QA-risk register for this module. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 12-bonus-variable-pay`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–11, no case in this module cites an `ex:` existing-test reference (see below for why).

**Build note:** `build_test_catalogue.py` overwrites `qa/HRMS_Test_Cases.xlsx` in full on every run — it is
not additive. Running it with `--module 12-bonus-variable-pay` alone regenerates *this module's own*
per-module CSVs/coverage-stats.json correctly, but if that single-module invocation is the last one run, the
saved workbook will contain **only** this module's sheets. Always follow a single-module run with a full,
no-argument run (`python qa/tools/build_test_catalogue.py`) before treating `HRMS_Test_Cases.xlsx` as
current — this is exactly what was done to produce the workbook shipped with this module.

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading `VariablePayController.cs` (the sole controller, also hosting every
`/api/me/variable-pay*` ESS route) in full; `VariablePayService.cs` in full (227 lines); `IVariablePayService.cs`;
`VariablePayDtos.cs` in full; `VariablePay.cs` (`VariablePayPlan`, `VariablePayPlanVersion`,
`VariablePayAward`, `VariablePaySettlement`, `VariablePayAwardHistory`, `VariablePayNumberSequence`) and
`VariablePayEnums.cs` in full; `VariablePayConfiguration.cs` (all five entity configurations, unique indexes,
and decimal-precision setup) in full; the full `Permissions.Payroll.VariablePay*` block (ids 169-177) in
`Permissions.cs`; every seeded role's grant list in `SeedData.cs` (grep-verified for every VariablePay
permission constant); the Final Settlement variable-pay line generation and finalization block in
`PayrollRetroSettlementService.cs`; a full grep of `PayrollCalculationEngine.cs` for "VariablePay" (zero
matches — see the headline CR-205 finding below); `PayrollReportsService.cs`/`IPayrollReportsService.cs`
(`GetVariablePayAsync`, CSV export) and `PayrollReportsController.cs`; `PayrollAnalyticsService.cs`'s
variable-pay reconciliation-source findings; `VariablePayPage.tsx` (`VariablePayPage` + `MyVariablePayPage`)
and its Vitest file, plus `src/api/variablePay.ts`, all read in full; and the existing xUnit inventory
(`VariablePayFoundationTests`, `VariablePayConcurrencyTests`, `VariablePayLargeDataTests`,
`PayrollVariablePayProviderAcceptance`, `SqlServerPayrollVariablePayIntegrationTests`,
`MySqlPayrollVariablePayIntegrationTests` — all six files read in full, 139 combined lines; not individually
classified, per the `qa/10-payroll`/`qa/11-loans-advances` precedent explained below).
`docs/phase-7q-bonus-incentives-variable-pay.md` was read in full; current code wins wherever it and the
doc diverge, per `CLAUDE.md`.

**HEADLINE FINDING — a confirmed critical no-op that fabricates a success response (CR-201).**
`VariablePayService.OverrideAsync` fetches the target award via `db.VariablePayAwards.AsNoTracking()`,
mutates the **detached** in-memory instance (amount, wiped approval/tax fields, `Status=Calculated`,
`Reason`, `ConcurrencyVersion++`), and appends a history entry only to the entity's own in-memory `History`
collection via its `AddHistory` helper — never to `db.VariablePayAwardHistories.Add(...)`. The subsequent
`await db.SaveChangesAsync(ct)` therefore has nothing pending in the change tracker for either the award or
its history row, and persists **nothing at all**. The method still returns `Result.Success` with a DTO
reflecting the overridden values, so the caller receives a completely fabricated success response for a
mutating financial-amount action. A direct `GET` of the same award immediately afterward returns the
original, pre-override values, and its history shows no `AwardOverridden` row. Contrast with
`ApproveAsync`/`TransitionAsync`/`SettleAsync`, all three of which correctly use `ExecuteUpdateAsync` plus an
explicit `db.VariablePayAwardHistories.Add(...)` — `OverrideAsync` is the sole outlier in the file, and this
is materially more severe than `qa/11-loans-advances`' CR-184 (a genuine but partial field-update omission):
here the entire mutation, across two tables, never happens. **No existing xUnit test ever calls
`OverrideAsync`** (grep-verified across all six existing VariablePay test files), so this defect has never
been exercised by the automated suite despite two of those files explicitly setting `AllowManualOverride =
true` on their test plan versions.

**A second confirmed critical defect, on the frontend (CR-203).** `src/api/variablePay.ts`'s
`listMyVariablePay` is typed and requested as `VariablePayAward[]`, but the real backend route (`GET
/api/me/variable-pay`) returns `ApiResponse<PagedResult<VariablePayAward>>` — the identical paged envelope
used by the admin register. `MyVariablePayPage` calls `awards.data?.map(...)` directly, which throws at
render time against the real API response (`.map` is not a function on a `{items,page,pageSize,totalCount}`
object). The existing Vitest coverage (`VariablePayPage.test.tsx`) mocks `listMyVariablePay` to resolve a
raw array — matching the frontend's own incorrect assumed shape rather than the real backend's — so the
existing test suite passes while completely masking this crash. Every employee with a linked identity and at
least one award who visits the ESS Variable Pay screen encounters a crashed page, not their award list.

**A materially larger permission gap, the identical shape to `qa/11-loans-advances`' CR-183 (CR-202).** A
full grep of `SeedData.cs`'s `RolePermissionMap` for every `Payroll.VariablePay.*` permission constant
returns matches only inside the id-numbering dictionary and the `Permissions.All` catalogue — **zero
matches inside any individual role's grant array**. SuperAdmin and TenantAdmin both receive the module only
via `DomainPermissions.All`; HRAdmin, HRManager, Manager, Employee, and SuperHR are all granted precisely
zero VariablePay permissions, native or otherwise — including Employee's own `View`, meaning a freshly
seeded Employee cannot even list their own awards through `/api/me/variable-pay` out of the box.

**A genuine, low-friction self-approval bypass (CR-204).** `ApproveAsync`'s only self-approval protection
checks `award.SubmittedByUserId`, which is `null` unless `Submit` was called — and `Approve` accepts a
still-`Calculated` award directly (Submit is optional, not a mandatory gate). `VariablePayService.cs` has
zero references to `PayrollApprovalGuard`/`PayrollControlConfiguration` anywhere (full grep) — unlike
Loans/Reimbursements, there is no tenant-configurable maker-checker policy integration at all for this
module. A single user holding both `CreateAward` and `Approve` can generate an award and approve it
themselves in two calls, simply by never calling `Submit`, with the guard never firing.

**A confirmed, structural scope gap relative to Loans' own payroll integration (CR-205).**
`PayrollCalculationEngine.cs` has **zero references to `VariablePay`** (full grep). Unlike Loans' automatic
payroll-deduction recovery integration, an Approved Variable Pay award is never automatically picked up by a
regular payroll run — the only way a `Payroll`-type `VariablePaySettlement` is ever created is a manual
`POST .../settle` call. Consequently the payroll-run-level `VariablePayTotal`/`VariablePay` reporting columns
(sourced from `PayrollResultComponents` tagged `CalculationSource='VariablePay'`) are structurally always
zero, regardless of how many awards were actually settled that period.

**Two confirmed, severe frontend gaps that leave the admin page largely non-functional (CR-206, CR-207).**
`CreatePlanAsync` does not auto-generate an initial version (unlike Loans' `CreateProductAsync`), and
`VariablePayPage.tsx` has **no "Add version" control anywhere** — a plan created through the UI can never be
made award-eligible through the UI. Separately, the admin page has **zero award-lifecycle action controls of
any kind** — no Generate/Preview/Submit/Approve/Reject/Override/Cancel/Settle button exists anywhere, and
the corresponding client functions do not even exist in `src/api/variablePay.ts`. This is structurally
larger than `qa/11-loans-advances`' CR-194/CR-195 (Loans at least has every lifecycle button present, with a
narrower hardcoded-amount/wrong-index defect) — here, none of the seven mutating award actions has *any* UI
entry point at all, making the shipped admin page effectively a read-only dashboard.

**Several declared-but-unenforced/dead configuration paths, the same shape as prior modules' CR-178/CR-186/
CR-193/CR-200.** `RequiresApproval` (CR-208) and `ApplicabilityJson` (CR-209) are persisted but never read
by the service. `ProrationMethod.CalendarDays`/`ServiceDays`/`EligibleDaysInPeriod` share one identical
day-ratio implementation despite being three distinct declared values (CR-210). `SalaryBasisType.Basic` has
no implementation and falls through to a misleading error message copy-pasted from the
`SelectedSalaryComponents` path (CR-211). `GenerateBulkAsync` has no per-employee override fields, making it
structurally incompatible with `ManualAmount`/`PerformanceRatingRequired` plans (CR-212).
`FinalSettlementTreatment.Exclude` and `.CancelOutstanding` behave identically — `CancelOutstanding` never
actually cancels the underlying award (CR-213). `VariablePayPlanVersionStatus.Inactive`/`Superseded` are
never assigned by any method (defensive, not reachable — CR-214, the `qa/11-loans-advances` CR-200 pattern).
`EligibilityMethod.ConfiguredApplicability`/`ManualReview` are not independently implemented (CR-216).

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Plans and Plan Versions | Full CRUD + validation matrix, overlap (Published-only), version-count/applicability gaps | Currency normalisation, PlanType/CalculationMethod drift | No auto-version on create (CR-206); RequiresApproval dead (CR-208) |
| Eligibility, Salary Basis and Award Generation | Eligibility gates, all 4 salary-basis types, all 4 calculation methods, proration matrix, idempotency, award numbering | Bulk generation, ApplicabilityJson, ConfiguredApplicability/ManualReview | Basic-basis always fails (CR-211); CalendarDays≡ServiceDays≡EligibleDaysInPeriod (CR-210); bulk unusable for ManualAmount (CR-212) |
| Submission, Approval and Rejection | Full state machine, Submit-skipping approve path, self-approval guard (both firing and non-firing) | Concurrency race | **Self-approval bypass when Submit is skipped (CR-204, headline security finding)** |
| Manual Override | Validation, missing-status-guard proof, the full no-op proof (response vs. GET vs. history) | Cross-tenant, permission separation | **Silent no-op fabricating success (CR-201, headline defect)** |
| Settlement (Payroll/Manual) | Status guard, amount bounds, duplicate prevention, tax-split apportionment, Paid/PartiallyPaid transitions | Settlement-date default | **No Calculation Engine integration at all (CR-205)** |
| Final Settlement Integration | Line generation/finalization, min(line,outstanding), Exclude/CancelOutstanding equivalence, stale-line residual | Orphaned-line skip | CancelOutstanding doesn't cancel (CR-213) |
| Cancellation | State-guard matrix (no idempotency gap, unlike Loans' CR-188), reason requirement | Draft-unreachable state | No maker-checker on Cancel (consistent with CR-204's absence pattern) |
| Award Register, Reports and Analytics | Full paged filter matrix, inline clamp proof, settlement-vs-award-level report distinction, CSV columns | DuplicateVariablePaySettlement finding | **Register paging is inline-clamped, unlike Loans' CR-191**; VariablePayTotal always 0 (CR-205) |
| Authorization and Scope | Full seeded-role absence proof, CR-66 self-escalation re-verification, ESS ownership | Calculate/CreateAward independent gating | **No role but SuperAdmin/TenantAdmin holds any VariablePay permission (CR-202)** |
| Audit and History | Append-only proof across the full genuinely-persisted lifecycle | Actor attribution, amount trail | Override's history entry is silently absent (CR-201) |
| Provider Parity and Large Data | Status-keyed and SettledAmount-keyed compare-and-swap proofs, unique-index-backed idempotency, 100-award scale/tenant-isolation proof | Provider-acceptance coverage-gap audit | Provider acceptance never exercises Override/Reject/Cancel/GenerateBulk/register/reports |
| Frontend | Missing version/lifecycle UI proof, MyVariablePayPage crash reproduction, navigation gap | Route-level permission gating, plan-form field gaps | **Zero lifecycle actions (CR-207) + no version UI (CR-206) + ESS crash (CR-203)** |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| VPay_Plans | 20 | Plan/version CRUD, validation matrix, Published-only overlap, no-auto-version gap |
| VPay_Eligibility_Generation | 27 | Eligibility gates, salary-basis resolution, proration matrix, calculation formulas, numbering, idempotency, bulk generation |
| VPay_Approval_Rejection | 13 | Submit/Approve/Reject state machine, Submit-skipping approve, self-approval guard (CR-204) |
| VPay_Override | 8 | Validation, missing status guard, the full silent-no-op proof (CR-201) |
| VPay_Settlement | 10 | Status/amount guards, duplicate prevention, tax split, missing Calculation Engine integration (CR-205) |
| VPay_FinalSettlement | 8 | Line generation/finalization, min(line,outstanding), Exclude/CancelOutstanding equivalence |
| VPay_Cancellation | 7 | Cancel state guard, reason requirement, no maker-checker |
| VPay_Register_Reports | 11 | Paging contract, filters, settlement-level report, CSV export, always-zero VariablePayTotal |
| VPay_Scope_Security | 12 | Seeded-role permission matrix (headline finding), ESS ownership, tenant isolation |
| VPay_Audit_History | 6 | Append-only proof, actor attribution, Override's missing history entry |
| VPay_Concurrency_Provider | 8 | Compare-and-swap races, unique-index idempotency, provider-acceptance gap audit, 100-award scale |
| VPay_Frontend | 9 | Missing version/lifecycle UI, MyVariablePayPage crash, navigation, permission-gating |

**Total: 139 test cases, 131 functionalities** (`python qa/tools/build_test_catalogue.py --module
12-bonus-variable-pay` regenerates and reports the authoritative counts; treat `coverage-stats.json` as
authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-201**: **Confirmed defect (Critical, headline)** — `OverrideAsync` persists nothing while returning a fabricated Success response.
- **CR-202**: **Headline finding** — no seeded role but SuperAdmin/TenantAdmin holds any VariablePay permission.
- **CR-203**: **Confirmed defect (Critical, frontend, headline)** — `MyVariablePayPage` crashes against the real paged API response; the existing Vitest test masks it with an incorrect mock.
- **CR-204**: `ApproveAsync`'s self-approval guard never fires when Submit is skipped (which is always allowed).
- **CR-205**: No automatic Payroll Calculation Engine integration exists for approved awards; settlement is manual-only.
- **CR-206**: **Confirmed defect (frontend)** — a plan created through the UI can never have a version added through the UI.
- **CR-207**: **Confirmed defect (frontend)** — zero award-lifecycle action controls exist anywhere on the admin page.
- **CR-208**: `RequiresApproval` is persisted but never read.
- **CR-209**: `ApplicabilityJson` is persisted but never enforced.
- **CR-210**: `CalendarDays`/`ServiceDays`/`EligibleDaysInPeriod` proration methods are numerically identical.
- **CR-211**: `SalaryBasisType.Basic` has no implementation and reports a misleading error.
- **CR-212**: `GenerateBulkAsync` is structurally incompatible with `ManualAmount`/`PerformanceRatingRequired` plans.
- **CR-213**: `FinalSettlementTreatment.Exclude` and `.CancelOutstanding` behave identically; `CancelOutstanding` never cancels the award.
- **CR-214**: (defensive, not reachable) `VariablePayPlanVersionStatus.Inactive`/`Superseded` are never assigned.
- **CR-215**: `Settle` shares the `Approve` permission; no dedicated settlement permission exists.
- **CR-216**: `EligibilityMethod.ConfiguredApplicability`/`ManualReview` are not independently implemented.
- **CR-217**: Neither `VariablePayPage` nor `MyVariablePayPage` has a `navigation.ts` entry.
- **CR-218**: The plan-create form cannot set Description/IsActive/CurrencyCode.

Only CR-201, CR-203, CR-206, and CR-207 are asserted as confirmed defects; CR-214 documents a defensive,
unreachable enum state (design confirmed, no change requested). Every other CR is classified `Open -
awaiting decision`, per the classification column in `clarifications.yaml` — each is exposed by specific
test cases and awaits engineering/product action.

## Existing automated coverage referenced by this module

Variable Pay has a modest but focused existing xUnit inventory (`VariablePayFoundationTests`,
`VariablePayConcurrencyTests`, `VariablePayLargeDataTests`, `PayrollVariablePayProviderAcceptance`'s shared
`RunAsync` path, and the `SqlServerPayrollVariablePayIntegrationTests`/`MySqlPayrollVariablePayIntegrationTests`
provider mirrors — 139 combined lines) and one focused Vitest file (`VariablePayPage.test.tsx`, two cases —
one of which, per CR-203, actively masks a real defect with an incorrect mock).

Following the identical precedent set by Modules 8, 9, 10, and 11, **no case in this module cites an `ex:`
existing-test reference.** Every existing test class discovered during research is instead documented
narratively inside the relevant `src:` fields, the coverage matrix above, and `clarifications.yaml`;
`qa/tools/existing_test_layers.yaml` was **not** extended for this module, consistent with `qa/tools/
build_test_catalogue.py`'s validation (which only requires `ex:` references to resolve when they are
actually used).

This module has **no dedicated `*VariablePayLargeDataTests.cs` gap** — `VariablePayLargeDataTests.cs` exists
and was read in full; `VPay_Concurrency_Provider`'s own cases document its actual scope (100 identical
freshly-Calculated awards, not a mixed-status set) rather than treating a green run as full-scale proof.

A green run of `SqlServerPayrollVariablePayIntegrationTests`/`MySqlPayrollVariablePayIntegrationTests` is
**not** evidence of full provider parity for this module's Override/Reject/Cancel/GenerateBulk/register/
reports surfaces — `PayrollVariablePayProviderAcceptance.RunAsync` was read in full and does not exercise
any of them (`VPAY-CONC-006`), and those provider tests skip silently when their env vars are absent, per
`CLAUDE.md`'s documented convention.
