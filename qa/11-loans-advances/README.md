# Module 11 — Loans & Advances (Loan/Advance Product Configuration and Versions, Eligibility and Employee
Loan Requests, Submission/Approval/Rejection, Disbursement and EMI Schedule Generation, Payroll Deduction
Integration, Manual Repayment/Partial Prepayment/Early Closure, Cancellation, Final Settlement Integration,
Loan Register and Reports, Authorization/Scope, Tenant Isolation, Audit/History, SQL Server/MySQL Provider
Parity, Large Data/Paging, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 11-loans-advances` regenerates the module's CSVs/coverage stats, and a full run across all
eleven modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation implemented yet
(Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## Scope decision — read this before extending this module

This module covers exactly the surface `qa/10-payroll`'s own README named as an explicit out-of-scope
carry-forward for a future module: **"Loans/Salary Advances (phase 7n)"**. Everything phase 7N actually
implements is in scope: Loan Product and Salary Advance product configuration and effective-dated
versions, employee loan/advance eligibility and requests (including the linked-employee ESS routes under
`/api/me/loans*`), the Submit/Approve/Reject lifecycle and its maker-checker integration, manual
disbursement recording and EMI schedule generation (None/Flat/ReducingBalance), the Calculation Engine's
loan-recovery (payroll deduction) integration, manual repayment/partial prepayment/early closure and
outstanding-balance maintenance, cancellation, Final Settlement's loan-recovery-line generation and
finalization, the paginated Loan Register, Authorization/Scope, Tenant Isolation, Audit/History, SQL
Server/MySQL Provider Parity, Large Data/Paging, and Frontend (`PayrollLoansPage`/`MyLoansPage`).

**Explicitly out of scope for this module** (present and real in the codebase, not covered here, and not
claimed as covered): Loan/Final-Settlement **GL/accounting journal generation** —
`PayrollAccountingService`'s `LoanPayrollRecovery`/`LoanFinalSettlementRecovery`/`LoanInterestRecovery`
`PayrollGLMapping` mapping types, i.e. the actual balanced debit/credit journal lines and their own
configuration surface — for the identical reason `qa/10-payroll` excluded all of Payroll Accounting/GL
Posting (phase 7i): it is a separate, self-contained ledger-posting concern. This module verifies that a
loan recovery correctly produces a `LoanRepayment` row and updates `EmployeeLoan`/`LoanInstallment` state
(the business outcome), not that a balanced GL journal is subsequently posted from it (the accounting
outcome). A dedicated register CSV/export endpoint is likewise absent from the product itself (the design
doc lists it as historically deferred scope) and is therefore not testable, not a gap in this module's own
execution.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (112 functionalities: F-LOAN-001…112). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-loan-products-configuration.yaml` … `cases/13-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. |
| `clarifications.yaml` | The CR-183…200 QA-risk register for this module. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 11-loans-advances`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Modules 8–10, no case in this module cites an `ex:` existing-test reference (see below for why).

## The real implementation (source-verified by direct code reading, not assumed from any design doc)

Discovered by reading `PayrollLoansController.cs` (the sole controller, also hosting every `/api/me/loans*`
ESS route) in full; `PayrollLoanService.cs` and `LoanPayrollRecoveryResolver.cs` in full;
`IPayrollLoanService.cs`/`ILoanPayrollRecoveryResolver.cs`; `PayrollLoanDtos.cs`/`PayrollLoanRecoveryDtos.cs`;
`PayrollLoans.cs` (`LoanProduct`, `LoanProductVersion`, `EmployeeLoan`, `LoanInstallment`, `LoanRepayment`,
`LoanHistory`) and `LoanEnums.cs`; `PayrollLoansConfiguration.cs`; the full `Permissions.Payroll.Loans*`
block (ids 145-154) in `Permissions.cs`; every seeded role's grant list in `SeedData.cs` (grep-verified for
every Loans permission constant); the loan-recovery integration block and `PersistLoanRecoveriesAsync` in
`PayrollCalculationEngine.cs`; the Final Settlement loan-recovery-line generation and finalization in
`PayrollRetroSettlementService.cs`; `PayrollApprovalGuard.cs` (the maker-checker consumed by
Approve/Cancel); `PayrollLoansPage.tsx` (`PayrollLoansPage` + `MyLoansPage`) and its Vitest file; and the
existing xUnit inventory (`PayrollLoansFoundationTests`, `PayrollLoansConcurrencyTests`,
`PayrollLoansProviderAcceptance`, `MySqlPayrollLoansIntegrationTests`, `SqlServerPayrollLoansIntegrationTests`
— not individually classified, per the `qa/10-payroll` precedent explained below). `docs/phase-7n-loans-
salary-advances.md` was read in full; current code wins wherever it and the doc diverge, per `CLAUDE.md`.

**A materially larger permission gap than any single-role finding in `qa/10-payroll`.** A full grep of
`SeedData.cs`'s `RolePermissionMap` for every `Payroll.Loans.*` permission constant returns matches only
inside the id-numbering dictionary and the `Permissions.All` catalogue — **zero matches inside any
individual role's grant array**. SuperAdmin and TenantAdmin both receive the module only via
`DomainPermissions.All`; HRAdmin (which natively holds the entire Salary/Structure/Period/Run/Controls/
TaxDeclaration/YearEndTax grant list in `qa/10-payroll`), SuperHR (equivalent native Payroll footprint
there), HRManager, Manager, and Employee are **all** granted precisely zero Loans permissions, native or
otherwise — including Employee's own `Payroll.Loans.Request`, meaning a freshly seeded Employee cannot
submit a loan/advance request for themselves through `/api/me/loans` out of the box (CR-183).

**A confirmed source-level defect was found in the payroll-recovery/loan-closure interaction (CR-184).**
`PayrollCalculationEngine.PersistLoanRecoveriesAsync` correctly decrements `EmployeeLoan.OutstandingPrincipal
/OutstandingInterest/OutstandingTotal` on every successful payroll-sourced recovery, but never checks
whether the resulting `OutstandingTotal` has reached zero and never transitions the loan to `Closed` — a
structural asymmetry with `PayrollLoanService.RecordRepaymentAsync` (the manual-repayment path), which
explicitly closes the loan the instant `OutstandingTotal` hits zero. **A loan fully recovered purely through
successive payroll cycles — the product's own primary intended repayment mechanism — remains `Active`
forever.** The same gap recurs, independently, in the explicitly product-gated Partial Prepayment action
(CR-187): `PartialPrepayAsync` has no equivalent zero-balance auto-close check either.

**CR-66/CR-77's recurring shape is present again, in narrower form.** SuperHR holds zero native
`Payroll.Loans.*` permissions but can self-grant the entire family (`View/Request/Approve/Disburse/Recover/
Close/Cancel/ManageProducts`) via one `PageAccess.Manage` call — the same self-escalation vector documented
since `qa/06-rbac-account-linking`'s CR-66 and re-verified in every module since.

**Two more dead permissions, plus the absence of a history endpoint entirely (CR-185).**
`Payroll.Loans.Manage` (id 147) and `Payroll.Loans.ViewHistory` (id 153) are declared and seeded but checked
by zero `[HasPermission]` attributes anywhere in the codebase — the same shape as `qa/10-payroll`'s
CR-167/173/175. Unlike every history-bearing entity surveyed in `qa/10-payroll` (all of which expose a
working `GET .../history` route), Loans has **no history endpoint at all** despite maintaining a fully
populated, append-only `LoanHistory` table across the entire lifecycle.

**Declared-but-unenforced configuration surface (CR-186, CR-193, CR-200).** `LoanProduct.MaxConcurrentLoans`
is stored and round-tripped but never read or enforced in `CreateLoanAsync` — an employee may hold
unlimited concurrent loans against a product declaring a cap. The product header's own eligibility-bound
fields (used by `CreateLoanAsync`) and each `LoanProductVersion`'s independently-set bound fields are two
separate, never-reconciled sources of truth. `LoanProductVersionStatus.Retired` is a declared lifecycle
state no service method ever assigns (defensive, not reachable — the `qa/10-payroll` CR-178 pattern).

**No client-facing optimistic-concurrency contract exists for Loans at all (CR-190).**
`EmployeeLoan.ConcurrencyVersion` is a genuine EF `.IsConcurrencyToken()` column, so a true SaveChanges-level
race does throw `DbUpdateConcurrencyException` — but no Loan mutation route accepts a client-supplied
`expectedConcurrencyVersion`, unlike SalaryComponent/SalaryStructure/EmployeeSalaryAssignment's activate/
deactivate/update actions, all of which pre-check an explicit version and return a clean `409 Conflict`.
Because `ExceptionHandlingMiddleware` has no special handling for `DbUpdateConcurrencyException`, an
*ordinary* stale-form conflict on a loan (not only a genuine race) has no path to a clean `409` in this
module — it surfaces as a generic, unhandled `500`.

**Two confirmed frontend implementation gaps (CR-194, CR-195), on top of the coarse permission-gating and
navigation-discoverability pattern already documented across `qa/08-attendance`/`qa/09-separation`/
`qa/10-payroll` (CR-197, CR-198).** `PayrollLoansPage`'s admin "Manual repayment" and "Partial prepayment"
buttons hardcode `amount: 1` with no input field — every click submits a trivial 1-currency-unit
transaction regardless of intent, making both actions functionally unusable for their real purpose in the
shipped UI. The "Edit first product" control always edits `products.data[0]`, so with more than one
product configured every product after the first can never be edited through the page. Separately
(CR-196), the product form exposes no controls for `AllowPartialPrepayment`/`AllowEarlyClosure`/
`MaxConcurrentLoans`, so every UI-created product is silently fixed at `AllowPartialPrepayment=false` and
`AllowEarlyClosure=false` — making two fully-implemented, correctly product-gated backend features
permanently unreachable for any UI-created product.

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Loan Products and Configuration | Full CRUD + validation matrix, version overlap, auto-generated initial version | Header/version bound drift, dead Retired enum state | Header/version dual-source-of-truth (CR-193) |
| Eligibility and Loan Requests | Amount/tenure limits, product/employee existence, ESS EmployeeId spoofing defense | LoanNumber format, RequiresApproval=false auto-approve path | MaxConcurrentLoans never enforced (CR-186); no employee-status gate (CR-199) |
| Submission, Approval and Rejection | Full state machine, maker-checker self-approval block, overrides | Reject's Draft-eligibility, history proof | Reject bypasses the approval guard entirely (CR-189) |
| Disbursement and EMI Schedule | Partial/full disbursement, all three interest methods' schedule math | Schedule idempotency, outstanding-interest recompute | — |
| Payroll Deduction Integration | RecoverFullOrFail/PartialRecovery/DeferInstallment matrix, priority ordering, idempotent persistence | Installment status transitions, outstanding decrement | **Never-closes-on-full-payroll-recovery (CR-184, Critical)** |
| Repayment, Prepayment, Closure | Interest-then-principal split, auto-close-at-zero, early-closure installment cancellation | Concurrency-token contract | PartialPrepay never auto-closes (CR-187); no expectedConcurrencyVersion anywhere (CR-190) |
| Cancellation | State-guard matrix, maker-checker reason/self-approval policy | — | Double-cancel idempotency gap (CR-188) |
| Final Settlement Integration | Line auto-generation, idempotent finalization, min(line,outstanding) recovery, auto-close | Residual-outstanding and already-non-Active skip paths | Stale line amount is not refreshed on recalculation (CR-193-adjacent) |
| Loan Register and Reports | Full paged filter matrix, live-computed recovered totals | 100+ row scale/tenant-isolation proof | No register validator (CR-191); admin/ESS listing hard-capped at 500 (CR-192) |
| Authorization and Scope | Full seeded-role absence proof, CR-66 self-escalation re-verification, ESS ownership | Dead-permission proofs, unlinked-user handling | **No role but SuperAdmin/TenantAdmin holds any Loans permission (CR-183)** |
| Tenant Isolation | Cross-tenant sweep across every read/mutation route | Register cross-tenant proof | — |
| Audit and History | Append-only proof across the full lifecycle | Actor/source attribution, absence of a history route | No history endpoint despite a fully populated table (CR-185) |
| Provider Parity and Large Data | Five concurrency-race scenarios, SQL Server/MySQL shared acceptance path | 100+ loan register scale proof | No dedicated `*LargeDataTests.cs` class of its own |
| Frontend | Permission-gating and navigation-discoverability audit, hardcoded-amount/edit-index defects | ESS eligibility pre-check, state-dependent action rendering | Hardcoded `amount:1` (CR-194, Critical); "Edit first product" bug (CR-195) |

## Sheets produced for this module

| Sheet | Cases | Focus |
|---|---:|---|
| Loans_Products | 14 | Product/version CRUD, validation matrix, overlap, header/version drift |
| Loans_Eligibility_Requests | 15 | Eligibility bounds, ESS spoofing defense, auto-approve path |
| Loans_Approval_Rejection | 14 | Submit/Approve/Reject state machine, maker-checker |
| Loans_Disbursement_Schedule | 13 | Disbursement validation, None/Flat/ReducingBalance schedule math |
| Loans_Payroll_Recovery | 13 | Calculation-engine integration, recovery policies, CR-184 |
| Loans_Repayment_Closure | 13 | Manual repayment/prepayment/closure, outstanding maintenance, CR-187/190 |
| Loans_Cancellation | 7 | Cancel state guard, idempotency gap, maker-checker reason policy |
| Loans_FinalSettlement | 9 | Line generation/finalization, min(line,outstanding), auto-close |
| Loans_Register_Reports | 8 | Paging contract, filters, unpaginated listing gap |
| Loans_Scope_Security | 12 | Seeded-role permission matrix (headline finding), ESS ownership, tenant isolation |
| Loans_Audit_History | 5 | Append-only proof, actor attribution, missing history route |
| Loans_Provider_Scale | 10 | Concurrency races, SQL Server/MySQL parity, register scale |
| Loans_Frontend | 9 | Permission-gating, navigation, hardcoded-amount and edit-index defects |

**Total: 142 test cases, 112 functionalities** (`python qa/tools/build_test_catalogue.py --module
11-loans-advances` regenerates and reports the authoritative counts; treat `coverage-stats.json` as
authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-183**: **Headline finding** — no seeded role but SuperAdmin/TenantAdmin holds any Loans permission.
- **CR-184**: **Confirmed defect (Critical)** — a loan fully recovered via payroll deductions is never closed.
- **CR-185**: `Payroll.Loans.Manage`/`Payroll.Loans.ViewHistory` are dead permissions; no history endpoint exists.
- **CR-186**: `MaxConcurrentLoans` is stored but never enforced.
- **CR-187**: `PartialPrepayAsync` does not auto-close at a zero balance, unlike `RecordRepaymentAsync`.
- **CR-188**: `CancelAsync` has no idempotency guard against an already-Cancelled/Rejected loan.
- **CR-189**: `RejectAsync` bypasses the maker-checker guard entirely, unlike Approve/Cancel.
- **CR-190**: No Loan endpoint exposes `expectedConcurrencyVersion` — ordinary conflicts surface as 500, not 409.
- **CR-191**: `LoanRegisterQuery` has no paging validator — PageSize/Page are unbounded server-side.
- **CR-192**: The admin/ESS loan listing is hard-capped at 500 rows with no pagination or truncation signal.
- **CR-193**: Product-header vs. product-version eligibility bounds are two never-reconciled sources of truth.
- **CR-194**: **Confirmed frontend defect (Critical)** — Manual repayment/Partial prepayment hardcode `amount:1`.
- **CR-195**: **Confirmed frontend defect** — "Edit first product" always edits index 0.
- **CR-196**: The product form has no controls for `AllowPartialPrepayment`/`AllowEarlyClosure`/`MaxConcurrentLoans`.
- **CR-197**: `PayrollLoansPage`/`MyLoansPage` perform zero client-side permission checks.
- **CR-198**: Neither Loans page has a `navigation.ts` entry.
- **CR-199**: `CreateLoanAsync` does not gate on the target employee's employment Status.
- **CR-200**: (defensive, not reachable) `LoanProductVersionStatus.Retired` is never assigned by any method.

None of these are asserted as confirmed defects except CR-184, CR-194, and CR-195, which are — each is
exposed by specific test cases and awaits engineering/product action, per the classification column in
`clarifications.yaml`.

## Existing automated coverage referenced by this module

Loans has a modest but focused existing xUnit inventory (`PayrollLoansFoundationTests`,
`PayrollLoansConcurrencyTests` — five concurrency-race scenarios read and mapped directly into
`Loans_Provider_Scale` — `PayrollLoansProviderAcceptance`'s shared `RunAsync` path, and the
`MySqlPayrollLoansIntegrationTests`/`SqlServerPayrollLoansIntegrationTests` provider mirrors) and one
focused Vitest file (`PayrollLoansPage.test.tsx`, five cases).

Following the identical precedent set by Modules 8, 9, and 10, **no case in this module cites an `ex:`
existing-test reference.** Every existing test class discovered during research is instead documented
narratively inside the relevant `src:` fields, the coverage matrix above, and `clarifications.yaml`;
`qa/tools/existing_test_layers.yaml` was **not** extended for this module, consistent with `qa/tools/
build_test_catalogue.py`'s validation (which only requires `ex:` references to resolve when they are
actually used).

This module has **no dedicated `*LoansLargeDataTests.cs` class** (confirmed by a full glob of
`Backend/HRMS.Tests`) — `Loans_Provider_Scale`'s own cases supply the manual-scale proof in the interim,
consistent with `qa/10-payroll`'s own precedent for areas lacking a dedicated large-data suite.

A green run of `MySqlPayrollLoansIntegrationTests`/`SqlServerPayrollLoansIntegrationTests` is **not**
evidence of provider parity if its env var was absent — they skip silently, per `CLAUDE.md`'s documented
convention, and `Loans_Provider_Scale`'s own cases report `NOT EXECUTED - environment unavailable`
explicitly rather than PASS when this is the case.
