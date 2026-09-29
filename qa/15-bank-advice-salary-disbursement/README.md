# Module 15 — Bank Advice & Salary Disbursement (Bank Advice Generation & Payroll-Run Eligibility,
Employee Bank-Account Resolution & Net-Pay Amount Selection, Payment Validation Rules, Batch
Lifecycle [Validate/Prepare/Approve], Export & Bank/Export Formats, Cancellation & Regeneration,
Concurrency & Idempotency, Payroll-Result Integration/Reconciliation/Final-Settlement Scope, Audit &
History, Authorization/Scope, Tenant Isolation, Reports/Listing/Large Data/Paging, SQL Server/MySQL
Provider Parity, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 15-bank-advice-salary-disbursement` regenerates the module's CSVs/coverage stats, and a full
run across all fifteen modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation
implemented yet (Playwright is Phase 8+, on explicit approval). No product source was changed to
produce this module.

## Scope decision — read this before extending this module

This module covers exactly `Backend/HRMS.Application/Services/BankAdviceService.cs` (phase 7H "Bank
Advice & Payment Processing") and its controller (`BankAdviceController` in
`PayrollOutputsController.cs`), plus the BankAdvice-specific reconciliation findings that
`PayrollAnalyticsService.cs` independently re-derives about it (tested here only as an
integration-boundary check, not as full coverage of the analytics/reconciliation engine itself — that
engine, together with `PayrollReconciliation`/`PayrollVarianceControls`/`PayrollAnomalyFlags`, has no
dedicated qa module yet and is explicitly out of scope here).

**Explicitly out of scope for this module** (present and real elsewhere, not covered here, and not
claimed as covered): payroll-run creation/calculation/approval itself (`qa/10-payroll`'s own surface —
this module only tests the Approved/Finalized status *gate* that Generate reads); employee
bank-account CREATE/UPDATE/deactivate (owned by `EmployeeSubResourcesController` and employee
master-data modules — this module tests only the READ-SIDE resolution query `BankAdviceService`
itself runs); payroll accounting/GL posting (`PayrollAccountingController`/`IPayrollAccountingService`
— a separate, already-distinct service with no `BankAdvice` reference of any kind, grep-confirmed);
and final settlement/separation payouts — a full grep of `SeparationService.cs` for `BankAdvice`
returns zero matches, and `docs/phase-7h-bank-advice-payment-processing.md`'s own "Deferred" section
explicitly lists "final settlement" alongside GL posting, statutory returns, and arrears/retro. No
live bank API, payment submission, NEFT/RTGS/IMPS, SFTP, or bank-specific signed/encrypted export
format is implemented anywhere in this codebase — the single CSV export tested here (`F-BANKADV-046`
through `-052`) is the only real export format that exists.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (108 functionalities: F-BANKADV-001…108). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-generation-eligibility.yaml` … `cases/14-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-263…273 QA-risk register for this module (continuing after `qa/14-tax-declarations-year-end-tax`'s CR-262). |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 15-bank-advice-salary-disbursement`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–14, no case in this module cites an `ex:` existing-test reference (see below for why).

**Build note:** `build_test_catalogue.py` overwrites `qa/HRMS_Test_Cases.xlsx` in full on every run —
it is not additive. Running it with `--module 15-bank-advice-salary-disbursement` alone regenerates
*this module's own* per-module CSVs/coverage-stats.json correctly, but if that single-module
invocation is the last one run, the saved workbook will contain **only** this module's sheets.
Always follow a single-module run with a full, no-argument run (`python qa/tools/build_test_catalogue.py`)
before treating `HRMS_Test_Cases.xlsx` as current — this is exactly what was done to produce the
workbook shipped with this module (all fifteen modules' sheets are present, confirmed by the build's
own per-module print output).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading, in full: `PayrollOutputsController.cs` (`BankAdviceController`, every route
under `api/payroll/bank-advice` plus the `runs/{runId}/bank-advice` generate route);
`BankAdviceService.cs` (all 137 lines) and `IBankAdviceService.cs`; `BankAdviceDtos.cs`; the 3-entity
`BankAdvice.cs` (`BankAdviceBatch`/`BankAdvicePayment`/`BankAdviceHistory`); `BankAdviceConfiguration.cs`
(all 3 EF configurations, every index and composite FK); `PayrollEnums.cs`'s
`BankAdviceStatus`/`BankAdvicePaymentStatus`/`BankAdviceValidationStatus`/`BankAdviceHistoryChangeType`
plus `PayrollControlScope.BankAdvice`/`PayrollControlMetric.BankAdviceTotal`; `EmployeeBankDetail.cs`/
`Bank.cs`/`AccountPurpose.cs`/`BankAccountStatus.cs`; `PayrollApprovalGuard.cs` (the shared opt-in
self-approval/reason-required guard); migration pair `AddBankAdvicePaymentProcessing` confirmed present
in both the SQL Server and MySQL tenant chains; `Permissions.cs`'s 7-permission `Payroll.BankAdvice*`
block; `SeedData.cs`'s `RolePermissionMap`, grep-verified for every one of those 7 constants across
every seeded role (HRAdmin, HRManager, SuperHR, Accounts, IT, Manager, Employee, SuperAdmin,
TenantAdmin); `PayrollAnalyticsService.cs`'s `AddSourceFindingsAsync` BankAdvice-specific block; a full
grep of `SeparationService.cs` for `BankAdvice` (zero matches); `BankAdvicePage.tsx` (+ its
`.test.tsx`), `src/api/payroll.ts`'s BankAdvice section, `src/auth/permissions.ts`'s mirror (all read
in full); `App.tsx`/`navigation.ts` route/nav-entry grep; and the existing 4-file xUnit inventory
(`BankAdviceTests`, `SqlServerBankAdviceIntegrationTests`, `MySqlBankAdviceIntegrationTests`,
`BankAdviceProviderAcceptance` — referenced narratively per the `qa/8-14` precedent, not individually
classified). `docs/phase-7h-bank-advice-payment-processing.md` was read for scope framing only; current
code wins wherever a doc and the code diverge, per `CLAUDE.md` — see CR-266 for one confirmed
divergence from the doc's own "acceptance complete" framing.

**HEADLINE FINDING #1 — no seeded role except SuperAdmin/TenantAdmin holds any of the 7 BankAdvice
permissions, out of the box (CR-263).** A full grep of `SeedData.cs`'s `RolePermissionMap` for
`BankAdviceView`/`Generate`/`Validate`/`Approve`/`Export`/`Cancel`/`ViewHistory` returns matches only in
the permission-id-assignment dictionary, never in any role array. HRAdmin (which otherwise holds the
full `Payroll.SalaryComponent*`/`SalaryStructure*`/`EmployeeSalary*`/`Period*`/`Controls*`/`Run*`/
`TaxDeclaration*`/`YearEndTax*` surface), HRManager, SuperHR (which otherwise holds a broad
`SalaryComponent*`/`SalaryStructure*`/`EmployeeSalary*`/`Period*`/`Controls*`/`Run*` Payroll grant — the
same set `qa/14`'s CR-251 noted), and the role literally named "Accounts" (whose entire seeded grant is
`Employee.View` — nothing else) all hold zero.

**HEADLINE FINDING #2 — `Payroll.BankAdvice.ViewHistory` is a declared, seeded permission that gates
nothing (CR-264).** No controller route or service method anywhere checks it; history is returned
unconditionally wherever `BankAdvice.View` already grants access.

**HEADLINE FINDING #3 — cancelling a batch never updates its child payment rows (CR-265).**
`CancelAsync`'s own `ExecuteUpdateAsync` is scoped entirely to `BankAdviceBatches` — no statement
anywhere ever sets a `BankAdvicePayment.PaymentStatus` to `Cancelled`. That enum member, and
`.Processed`, are both confirmed dead via full grep.

**HEADLINE FINDING #4 — Validate silently commits to Prepared whenever a batch is already fully valid
(CR-266).** `ValidateAsync` and `PrepareAsync` share one implementation; the `if (valid)` branch that
transitions `Draft`→`Prepared` runs unconditionally, regardless of which of the two actions the caller
actually invoked.

**HEADLINE FINDING #5 — no UI path exists to generate a new batch or cancel an existing one (CR-267,
frontend).** `generateBankAdvice`/`cancelBankAdvice` are both fully working client functions calling
real backend routes, but neither is ever called from `BankAdvicePage.tsx` or any other page (full grep
of `Frontend/HRMS.Web/src/pages`).

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Generation & Payroll-Run Eligibility | Run-status gate (all 7 statuses), active-batch guard, IsCurrent/Calculated sourcing, mixed-currency guard, deterministic batch numbering/sequencing/payment-reference | Cross-tenant run id | Regeneration-after-cancel is the only regeneration path (F-BANKADV-012) |
| Bank-Account Resolution & Net-Pay Selection | Full account-resolution filter matrix (purpose/active/status/effective-date), 0/1/many account counts, masking including the <=4-digit short-circuit, NetPay/CurrencyCode verbatim sourcing | Employee-name composition | Bank-account CRUD itself confirmed out of scope (owned elsewhere) |
| Payment Validation Rules | All 5 validation rules individually, first-failing-rule-wins ordering | — | — |
| Batch Lifecycle (Validate/Prepare/Approve) | Full Draft/Prepared/Approved state machine, opt-in self-approval guard, concurrency compare-and-swap | Approve's defense-in-depth checks | **CR-266 headline finding, proven end-to-end** |
| Export & Bank/Export Formats | CSV header/row shape, masked-only account numbers, CSV escaping, idempotent re-export | Concurrency race on first export | Confirmed single-format-only per docs/phase-7h |
| Cancellation & Regeneration | Full cancellable-status matrix, opt-in reason requirement, regeneration versioning | — | **CR-265 headline finding, proven end-to-end**; no per-payment edit endpoint (CR-271) |
| Concurrency & Idempotency | Compare-and-swap across all 4 mutating transitions, all 3 unique indexes | Uncaught-race shape on Generate | CR-269 (same shape as prior modules' CR-262 family) |
| Payroll-Result Integration, Reconciliation & Final-Settlement Scope | All 4 BankAdvice-specific reconciliation findings, final-settlement scope-out confirmed by grep | — | Confirms zero code-level final-settlement integration exists |
| Audit & History | Full append-only history proof, ordering, snapshot content | — | **CR-264 headline finding**; ActorUserId never populated (CR-272) |
| Authorization and Scope | Full per-permission negative/positive matrix, role-seed audit | Self-service-route absence | **CR-263 headline finding**; Validate/Prepare permission-equivalence (CR-273) |
| Tenant Isolation | Cross-tenant proof across every action, composite-FK proof, colliding-id proof | Cross-tenant control-policy isolation | — |
| Reports, Listing & Large Data/Paging | Filter/paging/sort behavior, 5,000-row generation and export | — | No PayrollRunId filter (CR-270); unbounded per-batch payment list |
| SQL Server/MySQL Provider Parity | Migration-pair proof, existing 3-file provider-fixture inventory | — | — |
| Frontend | Missing-generate-UI proof, missing-cancel-UI proof, correctly-wired-lifecycle proof, nav-discoverability proof, permission-gating contrast | — | **CR-267 headline (frontend)**; CR-268 low-severity gating gap |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| BankAdv_Generation_Eligibility | 13 | Run-status gate, sourcing/currency guards, numbering, regeneration path |
| BankAdv_Account_Resolution_NetPay | 10 | Full account-resolution filter matrix, masking, NetPay sourcing |
| BankAdv_Validation_ErrorHandling | 7 | All 5 validation rules, ordering |
| BankAdv_Lifecycle_Validate_Prepare_Approve | 11 | Draft/Prepared/Approved state machine, the CR-266 defect |
| BankAdv_Export_Formats | 8 | CSV shape/escaping/masking, idempotent re-export |
| BankAdv_Cancellation_Regeneration | 6 | Cancellable-status matrix, the CR-265 defect, regeneration versioning |
| BankAdv_Concurrency_Idempotency | 6 | Compare-and-swap proof, unique indexes, the CR-269 race |
| BankAdv_PayrollIntegration_Reconciliation | 8 | 4 reconciliation findings, final-settlement scope-out |
| BankAdv_Audit_History | 5 | Append-only history, the CR-264 dead permission, CR-272 |
| BankAdv_Authz_Scope | 8 | The CR-263 headline role-seed gap, CR-273 permission overlap |
| BankAdv_Tenant_Isolation | 6 | Cross-tenant proof across every action |
| BankAdv_Reports_LargeData_Paging | 6 | Filter/paging/sort, 5,000-row scale proof, CR-270 |
| BankAdv_Provider_Parity | 2 | Migration pairing, fixture inventory |
| BankAdv_Frontend | 8 | The CR-267 headline frontend gaps, CR-268 gating gap |

**Total: 104 test cases, 108 functionalities** (`python qa/tools/build_test_catalogue.py --module
15-bank-advice-salary-disbursement` regenerates and reports the authoritative counts; treat
`coverage-stats.json` as authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-263**: No seeded role except SuperAdmin/TenantAdmin holds any of the 7 BankAdvice permissions — not even HRAdmin, SuperHR, or the role named "Accounts".
- **CR-264**: **Confirmed defect** — `Payroll.BankAdvice.ViewHistory` is declared/seeded/mirrored but gates nothing.
- **CR-265**: **Confirmed defect** — cancelling a batch never updates child payment rows; `BankAdvicePaymentStatus.Cancelled`/`.Processed` are dead enum members.
- **CR-266**: **Confirmed defect** — Validate has the same state-mutating effect as Prepare whenever the batch is already fully valid.
- **CR-267**: **Confirmed defect (frontend, headline)** — no UI path exists to generate a new batch or cancel an existing one, despite both client functions working.
- **CR-268**: (frontend, low severity) No permission gating on any of BankAdvicePage's lifecycle buttons.
- **CR-269**: GenerateAsync's active-batch pre-check has no surrounding try/catch — an uncaught-race shape shared with prior modules' CR-262 family.
- **CR-270**: The list endpoint supports only a Status filter — no PayrollRunId/date-range/BatchNumber filter exists.
- **CR-271**: No endpoint exists to edit/amend an individual payment row — only cancel-and-regenerate.
- **CR-272**: `BankAdviceHistory.ActorUserId` is never populated.
- **CR-273**: Validate and Prepare share one permission constant — there is no way to grant one without the other.

Only CR-264, CR-265, CR-266, and CR-267 are asserted as confirmed defects; every other CR is
classified `Open - awaiting decision` per the classification column in `clarifications.yaml` — each is
exposed by specific test cases and awaits engineering/product action.

## Existing automated coverage referenced by this module

Bank Advice has a 4-file existing xUnit inventory: one dedicated `BankAdviceTests` workflow class, a
SQL Server/MySQL integration pair, and one provider-agnostic `BankAdviceProviderAcceptance` acceptance
helper. Following the identical precedent set by Modules 8–14, **no case in this module cites an `ex:`
existing-test reference.** Every existing test class discovered during research is instead documented
narratively inside the relevant `src:` fields, the coverage matrix above, and `clarifications.yaml`;
`qa/tools/existing_test_layers.yaml` was **not** extended for this module.

A green run of `SqlServerBankAdviceIntegrationTests`/`MySqlBankAdviceIntegrationTests` is evidence of
provider parity for this module. Both fixtures are opt-in and skip silently when
`HRMS_SQLSERVER_TEST_CONNECTION`/`HRMS_MYSQL_TEST_CONNECTION` are absent, per the repository-wide
convention in `CLAUDE.md` — a green default-suite (SQLite in-memory) run is not evidence of either
provider's parity on its own.
