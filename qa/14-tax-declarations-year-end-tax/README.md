# Module 14 — Tax Declarations & Year-End Tax (Declaration Cycles/Categories/Items, Employee
Declaration Lifecycle, Proofs & Submission Cut-Offs, Review/Approval/Lock/Reopen, Declaration
Audit & the Approved-Input Resolver, Year-End Tax Run Lifecycle, the Annual Calculation Engine,
Previous-Employer Inputs, TDS/Payroll Integration & Adjustment Handoff, Statements/History/Reports,
Authorization/Scope, Tenant Isolation, Concurrency/Idempotency, SQL Server/MySQL Provider Parity,
Large Data/Paging, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 14-tax-declarations-year-end-tax` regenerates the module's CSVs/coverage stats, and a full
run across all fourteen modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation
implemented yet (Playwright is Phase 8+, on explicit approval). No product source was changed to
produce this module.

## Scope decision — read this before extending this module

This module is exactly the surface `qa/13-statutory-compliance` explicitly scoped OUT: "the full
annual income-tax/TDS computation engine — regime selection, investment declarations and proof
verification, Form 16, year-end reconciliation (`YearEndTaxService.cs`, `TaxDeclarations.cs`,
phases 7U/7W)." It covers two phases read together as one coherent unit because they share a data
and workflow backbone (approved `EmployeeTaxDeclaration`/`EmployeeTaxDeclarationLine` amounts feed
`YearEndTaxService.CalculateAsync`'s own reconciliation): phase 7U (`TaxDeclarationCycle`/
`TaxDeclarationCategory`/`TaxDeclarationItem`/`EmployeeTaxDeclaration`/`EmployeeTaxDeclarationLine`/
`TaxDeclarationProof`/`TaxDeclarationAuditEvent` — the declaration/proof/review workflow) and
phase 7W (`YearEndTaxRun`/`YearEndTaxEmployee`/`YearEndTaxPreviousEmployerInput`/
`YearEndTaxAdjustment`/`YearEndTaxStatement`/`YearEndTaxHistory` — the annual reconciliation engine
and its `PayrollAdjustment` handoff boundary).

**Explicitly out of scope for this module** (present and real elsewhere, not covered here, and not
claimed as covered): the monthly `StatutoryType.IncomeTax` slab-based withholding engine inside
`StatutoryPayrollService`/`PayrollCalculationEngine` — this is `qa/13-statutory-compliance`'s own
surface (its F-STAT-050 documents the two engines never call each other in either direction; this
module independently re-confirms that same boundary from its own side at F-TAXDCL-101). Also out of
scope: the general `PayrollAdjustment` approval/application workflow that a year-end handoff's Draft
adjustment must separately pass through before it reaches a real payroll run (an existing, unrelated
Phase 7R concern — tested here only at the boundary, F-TAXDCL-104/105). Separation/full-and-final
settlement processing is likewise out of scope — a full grep of `SeparationService.cs` for
`TaxDeclaration`/`YearEndTax` returns zero matches, so unlike the shape of some prior modules'
scope notes, this is not a deliberate carve-out of a real integration but confirmation that **no
code-level integration currently exists at all** between year-end tax and separation settlement.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (156 functionalities: F-TAXDCL-001…156). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-declaration-cycles-categories-items.yaml` … `cases/16-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-239…262 QA-risk register for this module (continuing after `qa/13-statutory-compliance`'s CR-238). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 14-tax-declarations-year-end-tax`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–13, no case in this module cites an `ex:` existing-test reference (see below for why).

**Build note:** `build_test_catalogue.py` overwrites `qa/HRMS_Test_Cases.xlsx` in full on every run —
it is not additive. Running it with `--module 14-tax-declarations-year-end-tax` alone regenerates
*this module's own* per-module CSVs/coverage-stats.json correctly, but if that single-module
invocation is the last one run, the saved workbook will contain **only** this module's sheets.
Always follow a single-module run with a full, no-argument run (`python qa/tools/build_test_catalogue.py`)
before treating `HRMS_Test_Cases.xlsx` as current — this is exactly what was done to produce the
workbook shipped with this module (all fourteen modules' sheets are present).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading, in full: `TaxDeclarationsController.cs` (both `MyTaxDeclarationsController`
under `api/me/tax-declarations` and `TaxDeclarationsController` under `api/payroll/tax-declarations`,
every route) and `YearEndTaxController.cs` (every route under `api/payroll/year-end-tax`);
`TaxDeclarationService.cs` and `YearEndTaxService.cs` (read in full); `ITaxDeclarationService.cs`/
`IYearEndTaxService.cs`; `TaxDeclarationDtos.cs`/`YearEndTaxDtos.cs`; the 7-entity
`TaxDeclarations.cs` and 5-entity `YearEndTaxProcessing.cs`; `TaxDeclarationEnums.cs`/
`YearEndTaxEnums.cs`; `Permissions.cs`'s 15-permission `Payroll.TaxDeclaration*`/`Payroll.YearEndTax*`
block; `SeedData.cs`'s `RolePermissionMap`, grep-verified for every one of those 15 constants across
every seeded role; `TaxDeclarationConfiguration.cs`/`YearEndTaxConfiguration.cs` (all indexes and
concurrency-token setup); migration pairs `AddPayrollTaxDeclarationsPhase7U` and
`AddYearEndTaxProcessingPhase7W` confirmed present in both the SQL Server and MySQL tenant chains;
`MyTaxDeclarationsPage.tsx` (+ its `.test.tsx`), `YearEndTaxPage.tsx` (no test file exists for it),
`src/api/taxDeclarations.ts`, `src/api/yearEndTax.ts` (all read in full); `App.tsx`/`navigation.ts`
route/nav-entry grep; and the existing 11-file xUnit inventory (`TaxDeclarationWorkflowTests`,
`TaxDeclarationConcurrencyTests`, `TaxDeclarationLargeDataTests`, `TaxDeclarationDedicatedWorkflowTests`,
`SqlServerPayrollTaxDeclarationsIntegrationTests`, `MySqlPayrollTaxDeclarationsIntegrationTests`,
`PayrollTaxDeclarationsProviderAcceptance`, `YearEndTaxServiceTests`,
`PayrollYearEndConcurrencyRetryTests`, `PayrollYearEndLargeDataTests`, `PayrollYearEndProviderAcceptance`
— referenced narratively per the `qa/8-13` precedent, not individually classified).
`docs/phase-7u-employee-tax-declarations-investment-proofs.md` and
`docs/phase-7w-year-end-tax-processing.md` were read for scope framing only; current code wins
wherever a doc and the code diverge, per `CLAUDE.md` — see CR-247 for one confirmed divergence.

**HEADLINE FINDING #1 — SubmitOwnAsync crashes with an unhandled exception, not a graceful error
(CR-239).** `SubmitOwnAsync`'s own declaration lookup is `SingleOrDefaultAsync(x => x.TenantId==...
&& x.EmployeeId==...)` with no cycle or status filter. Nothing prevents an employee from holding two
simultaneous declarations across two concurrently-open cycles (CR-240 — `CreateCycleAsync` has no
overlap check of any kind beyond `Code` uniqueness), so this is reachable through entirely normal use,
not a contrived edge case.

**HEADLINE FINDING #2 — the one frontend page that reads a declaration calls a route that does not
exist (CR-241).** `getMyTaxDeclaration()` calls `GET /api/me/tax-declarations/current`; the real (and
only) route is `GET /api/me/tax-declarations`, no suffix. Every real page load 404s. The page's own
unit test always mocks this function, so the mismatch has never been caught.

**HEADLINE FINDING #3 — the employee self-service UI cannot start a declaration at all (CR-242), and
the entire reviewer/admin UI is missing (CR-253).** `src/api/taxDeclarations.ts` has no client
function for Create/AddLine/AddProof; `MyTaxDeclarationsPage` only edits what already exists. Separately,
10 of `TaxDeclarationsController`'s 12 actions (cycles, categories, items, review queue, ReviewLine,
ReviewProof, Approve, RequestResubmission, Lock, Reopen, Audit) have zero frontend surface anywhere.

**HEADLINE FINDING #4 — previous-employer inputs can never actually be used by a calculation (CR-250),
and the one UI surface for them is a non-functional stub (CR-254).** No endpoint ever advances a
`YearEndTaxPreviousEmployerInput` to `Status=Approved`; `CalculateAsync`'s own source query requires
exactly that status. Separately, `YearEndTaxPage`'s "Previous Employer Inputs" tab renders static text
only — the working `addPreviousEmployerInput` client function is never called from it.

**A confirmed calculation-field defect (CR-248).** `YearEndTaxEmployee.ProjectedRemainingTaxableIncome`
is a declared, mapped, DTO-exposed field that `CalculateAsync` never assigns — permanently 0 for every
row this service has ever produced.

**A confirmed line-review regression (CR-245).** `ReviewLineAsync`'s blocked-status set is only
`Draft`/`Locked` — reviewing a single line on an already-`Approved` declaration silently reverts the
whole declaration's `Status` back to `UnderReview`, with no explicit re-approval trigger required to
cause it and no distinguishable audit action for the regression itself.

**Several declared-but-unenforced/dead paths, the same shape as prior modules' CR-178/186/193/200/214
(and `qa/13-statutory-compliance`'s CR-227-family).** No update/status-transition endpoint exists for
`TaxDeclarationCycle`/`Category`/`Item` after creation (CR-243). The cycle's own proof-submission
cut-off dates are validated only for internal ordering and never compared against "today" anywhere
(CR-244). `ApproveAsync` never verifies every line was individually reviewed before approving the
whole declaration (CR-246). `ResolveApprovedAsync` is dead code in production, diverging from its own
design doc's claim that YearEndTax reads through it (CR-247). `EmployeeTaxDeclarationLineStatus.
ProofRequired`, `YearEndTaxAdjustmentStatus.Resolved`/`.Cancelled`, and `YearEndTaxHistoryEvent.
Recalculated` are all declared enum members no method ever assigns (CR-259, CR-260). No GET route
exists for a persisted `YearEndTaxStatement`; the "statements" CSV export is actually an alias of the
same rows as "employees" (CR-261). Three separate app-level-only uniqueness/overlap guards
(declaration creation, previous-employer-input creation, year-end-run date-overlap) share an
uncaught-`DbUpdateException` race-loser shape unlike the rest of the module's correctly-caught
concurrency handling (CR-262).

**A positive finding, the first of its kind in this QA programme so far (F-TAXDCL-117).** Unlike every
payroll sub-module surveyed in `qa/10` through `qa/13` (loans, variable pay, statutory compliance —
each finding "only SuperAdmin/TenantAdmin holds anything out of the box"), **HRAdmin is seeded with
all 15 permissions, HRManager gets a genuinely scoped view/review-only subset, and Employee gets
exactly the two self-service permissions it needs** — all out of the box. The one gap in this
otherwise good pattern is `SuperHR`, which holds zero of the 15 despite an otherwise broad seeded
Payroll grant (CR-251).

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Declaration Cycles, Categories & Items | Create validation, code uniqueness (tenant-wide for cycles/categories, per-category for items), trimming | Cross-tenant category id | No update endpoint for any of the three (CR-243); no cycle overlap check (CR-240) |
| Employee Declaration Lifecycle | Create gating (cycle existence/status/uniqueness), line add/update/delete editability matrix, submit validation, resubmit scoping | Multi-cycle GetOwnAsync selection | **CR-239 headline Submit crash, proven end-to-end** |
| Proofs, Proof Review & Cut-Offs | Add/replace gating by declaration status, replace-only-if-Rejected rule, review decision restriction, non-destructive replacement | Line-vs-declaration status interplay on rejection | Cut-off dates never enforced (CR-244); ProofRequired dead enum (CR-259) |
| Review, Approval, Lock & Reopen | Full queue/ReviewLine/Approve/RequestResubmission/Lock/Reopen state machine, amount bounds, permission separation | Concurrency races | **CR-245 (Approved regression) and CR-246 (unreviewed-line approval) headline findings** |
| Declaration Audit & Approved-Input Resolution | Append-only proof, chronological order, resolver filter logic | Cross-tenant resolver behavior | **CR-247, resolver is dead code, diverges from its own design doc** |
| Year-End Tax Run Lifecycle | Full Draft→Calculated→Submitted→Approved→Closed(/Cancelled) state machine, overlap/uniqueness guards, destructive-rebuild recalculation, self-approval opt-in proof | Concurrency races | Self-approval opt-in-only (CR-252's YearEndTax-side counterpart, same shape as `qa/13`'s CR-233) |
| Year-End Calculation Engine | YTD sourcing scope, taxable-income netting, monthly-engine read-only relationship, config/slab matching, ambiguity/missing-config/missing-slab as per-employee blocking issues, tax formula/rounding, due/excess mutual exclusivity, auto-adjustment creation | Ceiling/edge amounts | **CR-248 (ProjectedRemainingTaxableIncome always 0) and CR-249 (projected==final always) headline findings** |
| Previous Employer Inputs | Full validation/uniqueness/status-reset/concurrency matrix | — | **CR-250, inputs can never be picked up by any calculation** |
| TDS / Payroll Integration & Adjustment Handoff | Monthly/annual engine independence proof, read-only-payroll proof, handoff idempotency/race handling, Draft-status handoff boundary proof | — | Confirms `qa/13`'s F-STAT-050 from this module's own side |
| Statements, History & Reports | Full append-only history proof, all 4 export kinds, exceptions filter, money formatting | — | **CR-261, no real GET route for a statement; "statements" export is an alias of "employees"** |
| Authorization and Scope | Full role-seed audit (positive and negative), unroutable-permission-adjacent checks, self-approval proofs for both TaxDeclaration and YearEndTax sides | — | **F-TAXDCL-117 positive finding; CR-251 (SuperHR gap) and CR-252 (no TaxDeclaration self-approval guard at all)** |
| Tenant Isolation | Cross-tenant proof across every service, composite-key proof, self-service identity-resolution proof | — | — |
| Concurrency and Idempotency | Caught-vs-uncaught concurrency-exception contrast across both services, destructive-rebuild idempotency, handoff idempotency | — | **CR-262, three uncaught-race creation paths** |
| SQL Server/MySQL Provider Parity | Migration-pair proof, existing provider-fixture inventory (including an organizational inconsistency) | — | CR-258 (informational, not a coverage gap) |
| Large Data and Paging | Unpaginated-endpoint inventory, GetEmployeesAsync filter/paging composition, existing scale-fixture inventory | Export materialization behavior | — |
| Frontend | Missing-create-UI proof, missing-admin-UI proof, missing-previous-employer-UI proof, missing-client-function inventory, nav-gap proof, permission-gating contrast | — | **CR-241/242/253/254, four of this module's most severe findings are all frontend** |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| TaxDcl_Cycles_Categories_Items | 14 | Cycle/category/item CRUD-minus-Update, code uniqueness scoping, no-overlap gap |
| TaxDcl_Employee_Lifecycle | 17 | Create/line/submit/resubmit lifecycle, the CR-239 Submit crash |
| TaxDcl_Proofs_CutOffs | 9 | Proof add/replace/review, non-enforced cut-off dates, dead ProofRequired status |
| TaxDcl_Review_Approval | 14 | Full review/approve/lock/reopen state machine, the CR-245/246 defects |
| TaxDcl_Audit_Resolver | 8 | Append-only audit, the CR-247 dead resolver and doc divergence |
| TaxDcl_YearEnd_Run | 14 | Full run lifecycle, overlap/uniqueness guards, destructive recalculation |
| TaxDcl_YearEnd_Calc | 16 | YTD sourcing, taxable-income math, slab resolution, the CR-248/249 defects |
| TaxDcl_PreviousEmployer | 8 | Full CRUD/status matrix, the CR-250 unreachable-Approved-status defect |
| TaxDcl_TDS_PayrollIntegration | 8 | Monthly/annual engine independence, handoff boundary and idempotency |
| TaxDcl_Statements_History_Reports | 8 | Append-only history, all 4 exports, the CR-261 statement-export alias finding |
| TaxDcl_Authz_Scope | 8 | Positive role-seed finding, the CR-251 SuperHR gap, the CR-252 self-approval gap |
| TaxDcl_Tenant_Isolation | 6 | Cross-tenant proof across both services |
| TaxDcl_Concurrency_Idempotency | 8 | Caught vs. uncaught concurrency handling, the CR-262 race-loser finding |
| TaxDcl_Provider_Parity | 4 | Migration pairing, fixture organization inconsistency |
| TaxDcl_Large_Data_Paging | 4 | Unpaginated-endpoint inventory, filter/paging composition |
| TaxDcl_Frontend | 10 | The 4 headline frontend defects, client-function inventory, nav gap |

**Total: 156 test cases, 156 functionalities** (`python qa/tools/build_test_catalogue.py --module
14-tax-declarations-year-end-tax` regenerates and reports the authoritative counts; treat
`coverage-stats.json` as authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-239**: **Confirmed defect (headline)** — `SubmitOwnAsync` throws an unhandled exception instead of a graceful error when an employee has more than one declaration.
- **CR-240**: Nothing prevents two simultaneously-open declaration cycles — the precondition for CR-239.
- **CR-241**: **Confirmed defect (headline, frontend)** — `getMyTaxDeclaration()` calls a route that does not exist.
- **CR-242**: **Confirmed defect (frontend)** — no UI path to create a declaration, add a line, or add a proof.
- **CR-243**: No update/status-transition endpoint exists for cycle/category/item.
- **CR-244**: Proof submission cut-off dates are validated for ordering only, never enforced against "today".
- **CR-245**: **Confirmed defect** — reviewing a line on an Approved declaration silently regresses it to UnderReview.
- **CR-246**: `ApproveAsync` doesn't verify every line was individually reviewed first.
- **CR-247**: `ResolveApprovedAsync` is dead code in production and diverges from its own design doc.
- **CR-248**: **Confirmed defect** — `ProjectedRemainingTaxableIncome` is permanently 0.
- **CR-249**: Projected and Final fields are always numerically identical within one calculation pass.
- **CR-250**: **Confirmed defect** — previous-employer inputs can never actually be picked up by a calculation.
- **CR-251**: SuperHR holds zero of the 15 module permissions despite a broad seeded Payroll grant otherwise.
- **CR-252**: TaxDeclaration's own Approve has no self-approval guard of any kind (not even opt-in).
- **CR-253**: **Confirmed defect (frontend, headline)** — the entire reviewer/admin side has zero UI anywhere.
- **CR-254**: **Confirmed defect (frontend, headline)** — the Previous Employer Inputs tab is a non-functional stub.
- **CR-255**: `yearEndTax.ts` is missing client functions for 6 of 14 backend routes.
- **CR-256**: Neither page has a `navigation.ts` entry.
- **CR-257**: `MyTaxDeclarationsPage` has no permission gating on any button.
- **CR-258**: (informational) YearEndTax's provider-acceptance tests are organized differently than TaxDeclarations'.
- **CR-259**: `EmployeeTaxDeclarationLineStatus.ProofRequired` is a dead enum member.
- **CR-260**: `YearEndTaxAdjustmentStatus.Resolved`/`.Cancelled` and `YearEndTaxHistoryEvent.Recalculated` are dead enum members.
- **CR-261**: No GET route exists for a persisted `YearEndTaxStatement`; the "statements" export is an alias of "employees".
- **CR-262**: Three creation paths share an uncaught-`DbUpdateException` race-loser shape.

Only CR-239, CR-241, CR-242, CR-245, CR-248, CR-250, CR-253, and CR-254 are asserted as confirmed
defects; every other CR is classified `Open - awaiting decision` per the classification column in
`clarifications.yaml` — each is exposed by specific test cases and awaits engineering/product action.

## Existing automated coverage referenced by this module

Tax Declarations & Year-End Tax has an 11-file existing xUnit inventory: 4 dedicated TaxDeclarations
workflow/concurrency/large-data files, a SQL Server/MySQL integration pair plus a provider-agnostic
acceptance class for TaxDeclarations, and for YearEndTax a service-level test class, a 14-scenario
concurrency/retry-safety class, a 10,000-employee large-data class, and one combined SQL
Server+MySQL provider-acceptance class. Following the identical precedent set by Modules 8–13,
**no case in this module cites an `ex:` existing-test reference.** Every existing test class
discovered during research is instead documented narratively inside the relevant `src:` fields, the
coverage matrix above, and `clarifications.yaml`; `qa/tools/existing_test_layers.yaml` was **not**
extended for this module.

A green run of `SqlServerPayrollTaxDeclarationsIntegrationTests`/`MySqlPayrollTaxDeclarationsIntegrationTests`
is evidence of provider parity for the TaxDeclarations area. `PayrollYearEndProviderAcceptance`'s two
methods are evidence of provider parity for the YearEndTax area, organized as one shared class rather
than two dedicated ones (CR-258, informational only — both provider paths are still genuinely
exercised). Both areas' provider fixtures are opt-in and skip silently when
`HRMS_SQLSERVER_TEST_CONNECTION`/`HRMS_MYSQL_TEST_CONNECTION` are absent, per the repository-wide
convention in `CLAUDE.md` — a green default-suite run is not evidence of either provider's parity on
its own.
