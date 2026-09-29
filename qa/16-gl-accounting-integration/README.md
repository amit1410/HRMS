# Module 16 — GL / Accounting Integration (Payroll-to-GL Posting, Account/Code Mapping, Cost-Center
Dimensions, Earnings/Deduction & Employer-Contribution Mapping, Journal Generation, Debit/Credit
Balancing, Posting Lifecycle/Status, Reversal/Regeneration, Duplicate Prevention/Idempotency,
Final-Settlement Accounting, Payroll-Analytics Reconciliation Integration, Authorization/Scope,
Tenant Isolation, Audit & History, Reports/Export, Concurrency, SQL Server/MySQL Provider Parity,
Large-Data/Paging, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 16-gl-accounting-integration` regenerates the module's CSVs/coverage stats, and a full run
across all sixteen modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation
implemented yet (Playwright is Phase 8+, on explicit approval). No product source was changed to
produce this module.

## Scope decision — read this before extending this module

This module covers exactly `Backend/HRMS.Application/Services/PayrollAccountingService.cs`
(interface `IPayrollAccountingService`) and its controller (`PayrollAccountingController` in
`PayrollOutputsController.cs`, route prefix `api/payroll/accounting`), plus the 6 GL-specific
reconciliation findings that `PayrollAnalyticsService.cs` independently re-derives about it (tested
here only as an integration-boundary check, not as full coverage of the analytics/reconciliation
engine itself — that engine, together with the general
`PayrollReconciliation`/`PayrollVarianceControls`/`PayrollAnomalyFlags` surface and the non-GL
control-totals/dimension-summary endpoints on `PayrollAnalyticsService`, has no dedicated qa module
yet and is explicitly out of scope here, following the `qa/15` precedent for BankAdvice's own
reconciliation-boundary scoping).

**Explicitly out of scope for this module** (present and real elsewhere, not covered here, and not
claimed as covered): payroll-run creation/calculation/approval itself (`qa/10-payroll`'s own
surface — this module only tests the Approved/Finalized status *gate* that Generate reads);
final-settlement case creation/calculation/approval/finalization itself (owned by
`PayrollRetroSettlementController`/`IPayrollRetroSettlementService` — this module only tests the
Finalized status *gate* that `GenerateFinalSettlementAsync` reads, and reads the already-persisted
loan/reimbursement/gratuity/leave-encashment/notice-settlement rows those other modules produce,
purely as source data); bank advice / salary disbursement (`BankAdviceService`/`BankAdviceController`
— a separate, already-distinct service with no `PayrollAccounting`/`Journal` reference of any kind,
confirmed by `qa/15`'s own scope note); and loan/reimbursement/gratuity/leave-encashment/
notice-settlement *calculation* itself (owned by their respective services — this module only tests
that GL mapping and journal generation correctly consumes their already-finalized output rows).
Variable-pay/bonus award and settlement *calculation* itself (owned by `VariablePayService`,
`qa/12-bonus-variable-pay`'s own surface) remains out of scope the same way — but this module DOES
test, as a cross-module integration-boundary check (CR-289), the fact that `GenerateFinalSettlementAsync`
never consumes `VariablePaySettlements` at all, unlike the 5 sources it was actually built to read. No
docs/phase-\*.md file specifically describing GL accounting exists in this repository — current code
is the sole source of truth here, per `CLAUDE.md`.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (140 functionalities: F-GLACC-001…140). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-gl-account-master.yaml` … `cases/16-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-274…289 QA-risk register for this module (continuing after `qa/15-bank-advice-salary-disbursement`'s CR-273). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 16-gl-accounting-integration`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–15, no case in this module cites an `ex:` existing-test reference (see below for why).

**Build note:** `build_test_catalogue.py` overwrites `qa/HRMS_Test_Cases.xlsx` in full on every run —
it is not additive. Running it with `--module 16-gl-accounting-integration` alone regenerates *this
module's own* per-module CSVs/coverage-stats.json correctly, but if that single-module invocation is
the last one run, the saved workbook will contain **only** this module's sheets. Always follow a
single-module run with a full, no-argument run (`python qa/tools/build_test_catalogue.py`) before
treating `HRMS_Test_Cases.xlsx` as current — this is exactly what was done to produce the workbook
shipped with this module (all sixteen modules' sheets are present, confirmed by the build's own
per-module print output).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading, in full: `PayrollOutputsController.cs` (`PayrollAccountingController`, every
route under `api/payroll/accounting` plus the two `/accounting/generate` routes nested under
`api/payroll/runs/{runId}` and `api/payroll/final-settlements/{id}`); `IPayrollAccountingService.cs`
(17 methods) and `PayrollAccountingService.cs` (the full implementation); `PayrollAccountingDtos.cs`;
the 8-entity `PayrollAccounting.cs` (`PayrollGLAccount`/`PayrollAccountingConfiguration`/
`PayrollAccountingConfigurationVersion`/`PayrollGLMapping`/`PayrollJournalBatch`/`PayrollJournalLine`/
`PayrollJournalLineSource`/`PayrollJournalHistory`); `PayrollAccountingConfiguration.cs` (all 8 EF
configurations, every index and FK); `PayrollEnums.cs`'s `PayrollGLAccountType`/
`PayrollAccountingConfigurationVersionStatus`/`PayrollGLMappingType` (23 members)/
`PayrollJournalAggregationMode`/`PayrollJournalStatus`/`PayrollJournalHistoryChangeType`/
`PayrollControlScope.Accounting`/`PayrollControlMetric.GLTotal`; `PayrollApprovalGuard.cs` (the
shared opt-in self-approval guard, used here by Approve and Post); migration pairs
`AddPayrollAccountingGLPosting` and `AddFinalSettlementAccountingJournalSql`/`MySql` confirmed present
in both the SQL Server and MySQL tenant chains; `Permissions.cs`'s 8-permission `Payroll.Accounting*`
block; `SeedData.cs`'s `RolePermissionMap`, grep-verified for every one of those 8 constants across
every seeded role (HRAdmin, HRManager, SuperHR, Accounts, IT, Manager, Employee, SuperAdmin,
TenantAdmin); `PayrollAnalyticsService.cs`'s `AddSourceFindingsAsync` GL-specific block (6 findings)
and its `GetOverviewAsync`/`GetRunSummaryAsync` accounting-total wiring; a full grep of
`SeparationService.cs` for `Accounting`/`Journal`/`GL` (zero matches); `PayrollAccountingPage.tsx`
(no `.test.tsx` exists for it — confirmed by directory listing), `PayrollAccountingConfigurationPage.tsx`
(+ its `.test.tsx`, read in full), `src/api/payroll.ts`'s Accounting section, `src/auth/permissions.ts`'s
mirror (all read in full); `App.tsx`/`navigation.ts` route/nav-entry grep; and the existing 6-file
xUnit inventory (`PayrollAccountingTests`, `SqlServerPayrollAccountingIntegrationTests`,
`MySqlPayrollAccountingIntegrationTests`, `PayrollAccountingProviderAcceptance`,
`PayrollFinalSettlementAccountingTests`, `ReimbursementAccountingTests` — referenced narratively per
the `qa/8-15` precedent, not individually classified).

**HEADLINE FINDING #1 — no seeded role except SuperAdmin/TenantAdmin holds any of the 8 Accounting
permissions, out of the box (CR-274).** Identical shape to `qa/15`'s CR-263 for BankAdvice: a full
grep of `SeedData.cs`'s `RolePermissionMap` for all 8 `Payroll.Accounting*` constants returns matches
only in the permission-id-assignment dictionary, never in any role array. HRAdmin, HRManager,
SuperHR, and the role literally named "Accounts" all hold zero.

**HEADLINE FINDING #2 — there is no way to cancel or reverse a journal through any exposed API, ever
(CR-275).** `IPayrollAccountingService`'s full 17-method interface has no Cancel/Reverse method, yet
both Generate methods' own active-journal guards explicitly exclude the unreachable `Cancelled`
status — implying a cancel-then-regenerate design that was never actually built. A journal, once
generated, can never be corrected by regenerating.

**HEADLINE FINDING #3 — `PayrollJournalStatus.Exported`/`.Reversed` are both permanently unreachable
dead states, and history is captured but completely invisible through any API (CR-276, CR-277,
CR-278).** Export is a pure stateless read-only CSV download despite the schema implying a stateful
export step; `Reversed` and the reconciliation engine's own `BrokenAccountingReversalLinkage`/
`BrokenAccountingAdjustmentLinkage` findings are dead code by construction; and `PayrollJournalDto`
carries no History field at all, so `Payroll.Accounting.ViewHistory` gates nothing observable for
anyone.

**HEADLINE FINDING #4 — `AggregationMode` and `EmployerContributionAccountId` are both dead
configuration fields (CR-283), and cost-center/department/location GL dimensions have no
representation anywhere beyond one unused column (CR-287).** Every journal is generated at the same
fixed granularity regardless of the configured aggregation mode; a designated employer-contribution
account has zero effect on which account a line posts to; and `PayrollJournalLine.CostCenterId` is
declared with no FK configuration and is never set by `AddLine`.

**HEADLINE FINDING #5 — no UI path exists to generate any journal, create a configuration version, or
create/update/deactivate a mapping (CR-284, frontend).** `PayrollAccountingConfigurationPage.tsx`
only wires Create-account and Create-configuration; `PayrollAccountingPage.tsx` only wires
Validate/Approve/Post/Export on an already-existing journal. The entire posting and mapping-
configuration workflow — the core purpose of this feature — has no UI path of any kind, despite all
of the underlying endpoints working correctly.

**HEADLINE FINDING #6 — Variable Pay/Bonus settled against a final settlement is silently omitted
from GL journal generation (CR-289, cross-module).** `VariablePayService.SettleAsync` supports
`SettlementType.FinalSettlement` with a `FinalSettlementId`, exactly like `ReimbursementSettlement`'s
own final-settlement rows (which `GenerateFinalSettlementAsync` DOES read). But
`GenerateFinalSettlementAsync`'s complete source list is limited to
LoanRepayments/ReimbursementSettlements/GratuityCalculations/LeaveEncashmentCalculations/
NoticeSettlementCalculations — it never queries `VariablePaySettlements` at all, and
`PayrollGLMappingType`'s full 23-member enum has no Variable Pay/Bonus mapping type either. A settled
bonus amount is excluded from the journal with no `MissingGLMapping` error (unlike every one of the 5
supported sources) and, when it is the case's only eligible source, the case is wrongly rejected as
having "no finalized separation benefit or recovery" despite the settled amount existing.

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| GL Account Master Data | Full CRUD, tenant-unique code, cross-tenant code reuse | Oversized pageSize clamp | No delete/reference-check on deactivation (CR-281) |
| Accounting Configuration & Versioning | Config/version CRUD, overlap prevention, version resolution by effective date | Retire/deactivate absence | CR-283 (AggregationMode dead) |
| GL Mapping Rules | Full discriminator/duplicate/account-active validation matrix, priority ordering, deactivate-only removal | All 23 mapping types | CR-283 (EmployerContributionAccountId dead), CR-287 (no dimension fields) |
| Journal Generation from Payroll Run | Full eligibility gate, mapping resolution by CalculationSource, balance check, deterministic numbering | Read-only-source proof | **CR-282 uncaught race, non-versioned number** |
| Final Settlement Accounting | All 5 source types individually, per-source mapping gaps, interest capping, balance check | Cross-tenant source-row exclusion | Confirms this module IS the final-settlement/GL integration point; **CR-289** (Variable Pay/Bonus silently omitted) |
| Journal Lifecycle | Full Generated→Validated→Approved→Posted state machine, self-approval guard, concurrency CAS | ApprovedByUserId/PostedByUserId gap | **CR-280, CR-288** |
| Reversal, Cancellation & Regeneration | Full absence proven across interface, routes, and reachable states | — | **CR-275, CR-276, CR-277 headline findings, proven end-to-end** |
| Concurrency & Idempotency | Unique-index proof, uncaught-race proof (generate, account, mapping, version) | Atomic multi-table persistence | CR-282 |
| Export | Full status-gate/CSV-shape/escaping/idempotency matrix | 5,000-line export | Zero side effects on any call, unlike qa/15 BankAdvice |
| Audit & History | Append-only proof, ActorUserId population proof | — | **CR-278 headline finding, proven end-to-end** |
| Payroll-Analytics Reconciliation Integration | All 6 GL-specific findings, 2 dead-linkage findings | Overview-total wiring | CR-277 |
| Authorization and Scope | Full per-permission negative/positive matrix, role-seed audit | Approve/Post permission separation | **CR-274 headline finding** |
| Tenant Isolation | Cross-tenant proof across every action, composite-FK proof, colliding-reference proof | Cross-tenant control-policy isolation | — |
| Reports, Listing & Large Data/Paging | Filter absence, paging clamp, 3,000/5,000-row scale proof | — | No filter at all on journal list (narrower than qa/15's own CR-270) |
| SQL Server/MySQL Provider Parity | Migration-pair proof, existing 6-file provider-fixture inventory | — | — |
| Frontend | Missing-generate/mapping-UI proof, correctly-wired-lifecycle proof, nav-discoverability proof, zero test coverage proof | Permission-gating contrast | **CR-284 headline (frontend)**; CR-285/CR-286 |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| GLAcc_Account_Master | 8 | GL account CRUD, tenant-unique code, cross-tenant reuse |
| GLAcc_Configuration_Versioning | 10 | Configuration/version CRUD, overlap prevention, version resolution, the CR-283 defect |
| GLAcc_Mapping_Rules | 12 | Discriminator/duplicate/account validation, priority ordering, the CR-283/CR-287 defects |
| GLAcc_Journal_Generation | 16 | Payroll-run eligibility, mapping resolution, balance check, deterministic numbering |
| GLAcc_FinalSettlement_Accounting | 17 | All 5 final-settlement source types, per-source mapping gaps, interest capping, the CR-289 cross-module gap |
| GLAcc_Journal_Lifecycle | 12 | Generated→Validated→Approved→Posted state machine, the CR-280/CR-288 defects |
| GLAcc_Reversal_Cancellation | 6 | The CR-275/CR-276/CR-277 headline absence findings, proven end-to-end |
| GLAcc_Concurrency_Idempotency | 8 | Unique-index proof, the CR-282 uncaught race, atomic persistence |
| GLAcc_Export | 6 | CSV shape/escaping/idempotency, zero side effects |
| GLAcc_Audit_History | 5 | Append-only history, the CR-278 headline dead-history-API finding |
| GLAcc_Analytics_Reconciliation | 8 | 6 GL-specific reconciliation findings, the CR-277 dead-linkage findings |
| GLAcc_Authz_Scope | 8 | The CR-274 headline role-seed gap, permission-sharing analysis |
| GLAcc_Tenant_Isolation | 7 | Cross-tenant proof across every action |
| GLAcc_Reports_LargeData_Paging | 6 | Filter absence, paging clamp, large-journal scale proof |
| GLAcc_Provider_Parity | 2 | Migration pairing, fixture inventory |
| GLAcc_Frontend | 9 | The CR-284 headline frontend gap, CR-285/CR-286 |

**Total: 140 test cases, 140 functionalities** (`python qa/tools/build_test_catalogue.py --module
16-gl-accounting-integration` regenerates and reports the authoritative counts; treat
`coverage-stats.json` as authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-274**: No seeded role except SuperAdmin/TenantAdmin holds any of the 8 Accounting permissions — not even HRAdmin, SuperHR, or the role named "Accounts".
- **CR-275**: **Confirmed defect (headline)** — there is no way to cancel or reverse a journal through any exposed API, ever; regeneration is permanently blocked once a journal exists.
- **CR-276**: **Confirmed defect** — `PayrollJournalStatus.Exported` is permanently unreachable; Export is a stateless read-only operation.
- **CR-277**: **Confirmed defect** — `Reversed` is dead, and the reconciliation engine's own reversal/adjustment-linkage findings can never fire.
- **CR-278**: **Confirmed defect (headline)** — journal history is captured correctly but is completely unreachable through any API; `ViewHistory` gates nothing.
- **CR-279**: `PayrollJournalStatus.Draft` is never actually observable via the API (low severity).
- **CR-280**: Validate performs no independent balance/mapping re-check of its own beyond the status gate.
- **CR-281**: A GL account can be deactivated after being referenced by an already-Generated journal with no re-check at any later stage.
- **CR-282**: The uncaught concurrent-Generate race is worse here than `qa/15`'s CR-269, since JournalNumber has no version entropy at all.
- **CR-283**: **Confirmed defect** — `AggregationMode` and `EmployerContributionAccountId` are both dead configuration fields with zero effect on generated journals.
- **CR-284**: **Confirmed defect (frontend, headline)** — no UI path exists to generate any journal, create a version, or create/update/deactivate a mapping.
- **CR-285**: (frontend, low severity) No permission gating on any control on either accounting page.
- **CR-286**: PayrollAccountingPage has zero automated test coverage.
- **CR-287**: **Confirmed defect** — cost-center/department/location GL dimensions have no functional representation anywhere in the model.
- **CR-288**: `ApprovedByUserId`/`PostedByUserId` on the journal batch are declared but never populated (the equivalent history-level `ActorUserId` IS populated correctly).
- **CR-289**: **Confirmed defect (cross-module, headline)** — a Variable Pay/Bonus settlement recorded against a final settlement is silently omitted from GL journal generation; `GenerateFinalSettlementAsync` never queries `VariablePaySettlements`, `PayrollGLMappingType` has no Bonus/Variable-Pay mapping type, and no missing-mapping/error signal is ever raised for it.

Only CR-275, CR-276, CR-277, CR-278, CR-283, CR-284, CR-287, and CR-289 are asserted as confirmed
defects; every other CR is classified `Open - awaiting decision` per the classification column in
`clarifications.yaml` — each is exposed by specific test cases and awaits engineering/product action.

## Existing automated coverage referenced by this module

GL Accounting has a 6-file existing xUnit inventory: a general `PayrollAccountingTests` workflow
class, a SQL Server/MySQL integration pair, one provider-agnostic `PayrollAccountingProviderAcceptance`
acceptance helper, and two source-specific classes (`PayrollFinalSettlementAccountingTests`,
`ReimbursementAccountingTests`). Following the identical precedent set by Modules 8–15, **no case in
this module cites an `ex:` existing-test reference.** Every existing test class discovered during
research is instead documented narratively inside the relevant `src:` fields, the coverage matrix
above, and `clarifications.yaml`; `qa/tools/existing_test_layers.yaml` was **not** extended for this
module.

A green run of `SqlServerPayrollAccountingIntegrationTests`/`MySqlPayrollAccountingIntegrationTests`
is evidence of provider parity for this module. Both fixtures are opt-in and skip silently when
`HRMS_SQLSERVER_TEST_CONNECTION`/`HRMS_MYSQL_TEST_CONNECTION` are absent, per the repository-wide
convention in `CLAUDE.md` — a green default-suite (SQLite in-memory) run is not evidence of either
provider's parity on its own.
