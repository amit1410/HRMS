# Module 10 — Payroll (Salary Component Master, Salary Structures/Versions, Employee Salary Assignment,
Payroll Periods/Runs, Calculation Engine, Recalculation/Off-Cycle/Adjustments/Reversals, Payroll
Results/Errors/Outputs, Reimbursements/Claims, Payroll Settlement, Payroll Inputs, Authorization/Scope,
Tenant Isolation/Audit/Reports, SQL Server/MySQL Provider Parity, Large Data, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 10-payroll` regenerates the module's CSVs/coverage stats, and a full run across all ten
modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation implemented yet
(Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## Scope decision — read this before extending this module

Payroll is the largest single domain in the product: 24 design docs (`docs/phase-7a.md` through
`docs/phase-7y.md`), 20 controllers, 30+ services, 90+ existing xUnit classes. Matching every prior
module's practice of covering *everything implemented* in the area would mean re-covering roughly half
the product's total surface in one module. This module instead covers exactly what the brief asked for
by name, plus what is inseparable from it:

**In scope:** Salary Component Master, Salary Structures and Versions, Employee Salary Assignment,
Payroll Periods and Runs (incl. the Operations dashboard and Configuration/Production Health), the
Calculation Engine (earnings/deductions/proration, statutory-deduction *integration* into the engine),
Recalculation/Off-Cycle Runs/Adjustments/Reversals, Payroll Results/Errors/Outputs (payslips, register),
Reimbursements and Claims, Payroll Settlement (Retro/Arrears cases and Final Settlement), Payroll Inputs
(bulk CSV correction imports — included because it is the direct upstream trigger for recalculation),
Authorization/Role Scope, Tenant Isolation, Audit/History, Reports and Queues (Payroll Reports, Payroll
Analytics, Exceptions, Reconciliation, Analytics Controls), SQL Server/MySQL Provider Parity, Large Data,
and Frontend.

**Explicitly out of scope for this module** (present and real in the codebase, not covered here, and not
claimed as covered): Loans/Salary Advances (phase 7n), Bonus/Variable Pay (7q), Statutory Calculation
*Configuration/Compliance-Returns/Filing* (7f/7k/7x — `StatutoryPayrollController`'s own configuration
CRUD and `PayrollStatutoryComplianceController`/`StatutoryFilingsController` are excluded; only
`StatutoryPayrollService.CalculateAsync`'s *integration point inside the Calculation Engine* is covered,
since it directly determines earnings/deductions), Bank Advice/Payment Processing (7h), Payroll
Accounting/GL Posting (7i), Payroll Controls/Production-Readiness/UAT/Production-Closure (7l/7m/7y —
`PayrollControlService`'s maker-checker *configuration* is touched only insofar as `PayrollApprovalGuard`
consumes it across every in-scope approval action), Employee Tax Declarations (7u), and Year-End Tax
Processing (7w). Gratuity/Separation Benefits (7p) is already fully covered by `qa/09-separation`
(`Separation_Benefits` sheet) and is not duplicated here. These are natural candidates for a future
Module 11 (or split further), not gaps in this module's own execution.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (257 functionalities: F-PAY-001…257). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-salary-component-master.yaml` … `cases/14-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-165…182 QA-risk register for this module, plus re-verified (not merely restated) cross-references to CR-66 and CR-77 from earlier modules. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 10-payroll`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr`
field meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical
to [Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8 and 9, no case in this module cites an `ex:` existing-test reference (see below for why).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading every in-scope controller (`SalaryComponentsController`, `SalaryStructuresController`,
`EmployeeSalaryAssignmentsController`, `PayrollPeriodsController`, `PayrollRunsController`,
`PayrollOperationsController`, `PayrollOffCycleController`, `PayrollAdjustmentsController`,
`PayrollReversalsController`, `PayrollOutputsController`/`MyPayslipsController`, `ReimbursementsController`,
`PayrollInputsController`/`PayrollInputTemplatesController`, `PayrollAnalyticsController`/
`PayrollExceptionsController`/`PayrollReconciliationController`/`PayrollAnalyticsControlsController`,
`PayrollReportsController`, `StatutoryPayrollController`, `PayrollControlsController`, and
`PayrollRetroSettlementController`); every in-scope Application-layer service (`SalaryComponentService`,
`SalaryStructureService`, `EmployeeSalaryAssignmentService`, `PayrollPeriodService`, `PayrollRunService`,
`PayrollCalculationEngine`/`PayrollCalculationService`, `PayrollRoundingPolicy`, `PayrollApprovalGuard`,
`PayrollReadinessService`, `PayrollAdjustmentService`, `PayrollOutputService`, `ReimbursementService`,
`ReimbursementPolicyResolver`/`ReimbursementEligibilityService`, `PayrollRetroSettlementService`,
`PayrollInputService`, `PayrollOperationsService`, `PayrollControlService`, `StatutoryPayrollService`,
`AttendancePayrollSnapshotResolver`, `LoanPayrollRecoveryResolver`, `ReimbursementPayrollResolver`,
`PayrollReportsService`); the full `Permissions.Payroll` constant inventory (~160 entries) and every
seeded role's grant list in `SeedData.cs`; the in-scope frontend pages, `src/api/payroll*.ts`,
`src/layout/navigation.ts`, and `src/App.tsx`'s route table; and the 90+ existing xUnit classes and
Vitest files touching this surface (not individually classified — see below).

**CR-66 recurs with a new twist.** Unlike every prior module where the self-escalating role (SuperHR)
started from zero native permissions in the family, here SuperHR already holds a *partial native*
Payroll footprint — full Salary/Period/Run management including `Run.Approve`/`Run.Finalize`/
`Run.Calculate`/`Run.Recalculate` and `Controls.Manage` — baked in by direct seed, before any
self-escalation is even considered. `PageAccess.Manage` only needs to close the remaining gap
(Adjustments/Reimbursements/OffCycle/Analytics/Retro/FinalSettlement/`PayrollInput.*`).

**CR-77 does NOT recur — the first module surveyed where the mechanism is entirely absent, not another
inconsistent variant of it.** A full grep of `Permissions.Payroll` finds zero `ViewTeam`-style constants;
there is no "manager sees their team's payroll" concept anywhere in this module, only self (`ViewOwn`)
vs. tenant-wide (`View`/`Manage`). Consistent with this, `Manager` holds **zero** Payroll permissions of
any kind (CR-168), and `HRManager` holds **none of the module's core permissions** at all (CR-165) — a
narrower gap than even `qa/09-separation`'s own HRManager finding (CR-135).

**A confirmed source-level defect, not a design ambiguity, was found in the Retro/Arrears feature
(CR-181).** `PayrollRetroSettlementService.EvaluateRetroAsync` hardcodes every snapshot's corrected
amounts equal to the original with every difference field fixed at 0 — it never actually recalculates
anything. Because the case's resulting status can only become `Evaluated` when at least one snapshot has
a nonzero `NetDifference`, and every snapshot's `NetDifference` is unconditionally 0, **a retro case can
never reach `Evaluated`, ever, through this method — it always lands on `NoImpact`.** `ApproveRetroAsync`
and `ApplyRetroAsync` both require preconditions built on top of `Evaluated`/`Approved`, so the entire
Retro correction pipeline is unreachable through ordinary use of the exposed API. This is flagged for
engineering follow-up, not merely documented as expected behavior.

**A second confirmed source-level bug was found in the Run state machine (CR-172).**
`PayrollRunService.TransitionAsync` has an unbraced `if` statement: `if (target is Finalized or
Cancelled) row.CompletedAtUtc = ...; row.CompletedByUserId = tenant.UserId;` — only the first statement
is conditional in C#; `CompletedByUserId` is stamped on **every** transition (Approved, Processing,
etc.), long before the run is actually complete.

**Four more dead/orphan permissions were found, on top of the pattern already seen in Modules 7–9:**
`Payroll.Run.Approve`/`Payroll.Run.Finalize` (CR-167 — the real transition action is gated by the
coarser `Run.Manage`), `Payroll.Payslip.ViewHistory` (CR-173 — no payslip-history endpoint exists at
all, unlike every sibling entity), `Payroll.Retro.Cancel` (CR-174 — no cancel action exists for a retro
case, the sibling of `qa/09-separation`'s already-known `FinalSettlementCancel` orphan), and
`Payroll.Exceptions.Manage` (CR-175 — both exception routes check `Exceptions.View` only).

**Frontend confirms and sharpens the coarse permission-gating pattern already seen in Modules 8–9.** Only
8 of ~29 Payroll pages — exactly the master-configuration screens — perform any client-side permission
check (CR-170); ~19 of ~27 registered routes have no navigation entry, including the in-scope
Reimbursements, Adjustments, Off-Cycle, Inputs, Analytics, and Reports screens (CR-169). On the positive
side, the three master pages that DO manage optimistic concurrency (`SalaryComponentsPage`,
`SalaryStructuresPage`, `EmployeeSalaryAssignmentsPage`) all correctly thread the real row's
`concurrencyVersion` rather than hardcoding it — no `CR-161`-style bug was found here.

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Salary Component Master | Full CRUD + validation matrix, idempotent activate/deactivate, history | Concurrent duplicate-code race, tenant isolation | — |
| Salary Structures/Versions | Header+component validation matrix, versioning-on-update, reconciliation | Version-window fallback resolution, soft-delete components | — |
| Employee Salary Assignment | CTC binding, structure-version resolution, overlap guard, overrides | GetEffective's 0/1/many resolution | Non-idempotent SetActiveAsync (CR-171) |
| Payroll Periods and Runs | Lock/Unlock state machine, population Prepare/rebuild, run transitions, readiness | Config/Production Health, Integrity checks | Unbraced-if CompletedByUserId bug (CR-172) |
| Calculation Engine | FixedAmount/Percentage/Formula/Manual matrix, proration, rounding, statutory/loan/reimbursement/adjustment integration | Overtime add-on, formula parser edge cases | Unconditional negative-value rejection (CR-176) |
| Recalc/Off-Cycle/Adjustments/Reversals | Full adjustment lifecycle, off-cycle orchestration, reversal generation | Correction-snapshot immutability, self-service scoping | One-reversal-per-run, not per-employee (CR-180) |
| Payroll Results/Outputs | Payslip generation/regeneration/publish, register, self-service | HTML-encoding safety, CSV escaping | Payslip.ViewHistory is a dead permission (CR-173) |
| Reimbursements and Claims | Category/policy versioning, claim lifecycle, approval limits, settlement | Payroll-recovery idempotency, eligibility advisory service | — |
| Payroll Settlement | Final Settlement calculate/approve/finalize, sub-ledger posting-at-finalize | — | Retro Evaluate can never leave NoImpact (CR-181, Critical) |
| Payroll Inputs | Template versioning, upload/validate/correct, submit/approve/post/cancel | File-hash dedup, Serializable-transaction posting | — |
| Authorization/Scope | Full seeded-role matrix (SuperAdmin→Employee), CR-66/CR-77 re-verification | Custom-role dead-permission proofs | HRManager/Manager hold none of the module (CR-165/168) |
| Tenant Isolation | Cross-tenant sweep across every in-scope service, number-sequence isolation | — | — |
| Audit/History | Append-only proof across all 12 history tables | ActorUserId attribution | — |
| Reports and Queues | Every report/analytics/reconciliation/exceptions endpoint's filter+paging contract | Dashboard's substring-based classification | Substring-fragility in Dashboard totals (CR-182) |
| Provider Parity | Concurrency-token contract across 7 entities, sequence-allocation races | 1000-employee scale proof | Only 2 of the in-scope areas have a dedicated large-data suite |
| Frontend | Permission-gating and navigation-discoverability audit across ~29 pages | Concurrency-version threading proof | Only 8/29 pages gate client-side (CR-170); ~19/27 routes un-navigated (CR-169) |

## Sheets produced for this module

| Sheet | Cases (approx.) | Focus |
|---|---:|---|
| Payroll_SalaryComponents | 16 | Master CRUD, validation matrix, idempotent activation, history |
| Payroll_SalaryStructures | 21 | Header/component validation matrix, versioning, reconciliation |
| Payroll_EmployeeSalary | 17 | CTC binding, structure-version resolution, overlap guard, overrides |
| Payroll_PeriodsRuns | 28 | Lock/Unlock state machine, population prepare, transitions, readiness, ops/config/production health |
| Payroll_Calculation | 30 | Earnings/deductions matrix, proration, statutory/loan/reimbursement/adjustment integration |
| Payroll_Adjustments | 23 | Adjustment lifecycle, off-cycle orchestration, reversals, self-service scoping |
| Payroll_Outputs | 13 | Payslip generation/publication, register, self-service, dead permission |
| Payroll_Reimbursements | 24 | Category/policy, claim lifecycle, approval limits, settlement, self-service |
| Payroll_Settlement | 18 | Retro cases (incl. CR-181), Final Settlement calculate/approve/finalize |
| Payroll_Inputs | 21 | Template versioning, upload/validate/correct, submit/approve/post/cancel |
| Payroll_Scope | 12 | Seeded role matrix, CR-66/CR-77 re-verification, dead permissions, tenant isolation |
| Payroll_Audit_Reports | 16 | Append-only audit proof, Reports/Analytics/Exceptions/Reconciliation |
| Payroll_Provider_Scale | 8 | Concurrency-token/sequence-race contract across providers, large-data scale |
| Payroll_Frontend | 10 | Permission-gating and navigation audit, concurrency-version threading |

**Total: 256 test cases, 257 functionalities** (`python qa/tools/build_test_catalogue.py --module
10-payroll` regenerates and reports the authoritative counts; treat `coverage-stats.json` as
authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-165**: HRManager holds none of Payroll's core module permissions.
- **CR-166**: SuperHR's native footprint already reaches irreversible Run.Finalize before any self-escalation.
- **CR-167**: `Payroll.Run.Approve`/`Run.Finalize` are declared/seeded but checked by zero endpoints.
- **CR-168**: Manager holds zero Payroll permissions of any kind.
- **CR-169**: ~19 of ~27 Payroll routes have no navigation entry.
- **CR-170**: Only 8 of ~29 Payroll pages perform any client-side permission check.
- **CR-171**: EmployeeSalaryAssignment's SetActiveAsync always writes history, even on a no-op.
- **CR-172**: Confirmed bug — CompletedByUserId is stamped on every run transition, not just terminal ones.
- **CR-173**: `Payroll.Payslip.ViewHistory` is a dead permission — no payslip-history feature exists at all.
- **CR-174**: `Payroll.Retro.Cancel` is an orphan permission — no cancel action exists for retro cases.
- **CR-175**: `Payroll.Exceptions.Manage` is a dead permission.
- **CR-176**: The calculation engine rejects any negative computed component value unconditionally.
- **CR-177**: (positive contrast) Statutory config ambiguity is an explicit Conflict, unlike gratuity's silent pick.
- **CR-178**: (defensive, not a bug) GetEffective's "multiple assignments" Conflict is unreachable via the API.
- **CR-179**: Employee role can't view its own payslip or file a reimbursement — both self-service permissions are unseeded.
- **CR-180**: A reversal is one-per-run, not one-per-employee-result, despite the DTO implying otherwise.
- **CR-181**: **Confirmed defect (Critical)** — Retro Evaluate can never produce a real correction; the whole Approve/Apply pipeline is unreachable.
- **CR-182**: The Payroll Dashboard classifies sub-totals by fragile substring matching on a free-text field.

`clarifications.yaml` also carries re-verified (not merely restated) cross-references to **CR-66**
(recurs with a new twist — SuperHR's native footprint plus escalation) and **CR-77** (does NOT recur —
the first module with no manager/team-scope concept at all).

None of these are asserted as confirmed defects except CR-172 and CR-181, which are — each is exposed by
specific test cases and awaits engineering/product action, per the classification column in
`clarifications.yaml`.

## Existing automated coverage referenced by this module

Payroll has the largest existing xUnit inventory of any module surveyed so far (90+ classes touching this
surface, spanning foundation/master tests, period/run tests, calculation tests, adjustment/concurrency
tests, output tests, reimbursement provider-acceptance, retro-settlement tests, input-service/acceptance/
concurrency/retry-safety tests, and the full analytics/reports test families) and Vitest coverage across
its in-scope frontend pages and API-client modules.

Given this scale — consistent with Modules 7, 8, and 9's own precedent, all of which found
100%-per-case verification against the existing suite impractical at this size — this module follows the
identical precedent: **no case in this module cites an `ex:` existing-test reference.** Every existing
test class discovered during research is instead documented narratively, per topic area, inside the
relevant `src:` fields, the coverage matrix above, and `clarifications.yaml`; `qa/tools/
existing_test_layers.yaml` was **not** extended for this module, consistent with `qa/tools/
build_test_catalogue.py`'s validation (which only requires `ex:` references to resolve when they are
actually used).

Two areas are flagged explicitly as having **no dedicated large-data automated suite** despite being
scale-sensitive: Salary Master/Periods/Runs/Calculation/Adjustments/Reimbursements/Settlement/Inputs have
no `*LargeDataTests.cs` class of their own (only `PayrollAnalyticsLargeDataTests.cs` and
`PayrollReportsLargeDataTests.cs` exist among the in-scope areas) — see `Payroll_Provider_Scale`'s own
cases for the manual-scale proof this module supplies in the interim.

A green run of any MySQL/SQL-Server-tagged provider-acceptance class is **not** evidence of provider
parity if its env var was absent — they skip silently, per `CLAUDE.md`'s documented convention, and
`Payroll_Provider_Scale`'s own cases report `NOT EXECUTED - environment unavailable` explicitly rather
than PASS when this is the case.
