# Phase 7L — Payroll Controls, Locking & Production Readiness

Phase 7L establishes the first production-controls layer for payroll without changing payroll monetary calculation semantics.

## Implemented

- Payroll periods record lock/unlock timestamps, actors, and mandatory reasons.
- Locking is explicit and auditable through the existing period history.
- A locked period cannot be unlocked without the dedicated unlock permission and a reason.
- Unlocking is rejected when the period contains approved or finalized payroll runs.
- Payroll control configuration is tenant-scoped and stores maker-checker, self-approval, reopen-reason, and cancellation-reason policy flags for future control workflows.
- Payroll run readiness is available at `GET /api/payroll/runs/{runId}/readiness` and reports blocking errors and warnings for period, eligible population, salary assignment, and salary-structure readiness.
- Tenant control policy can be read and updated through `GET/PUT /api/payroll/controls` with `Payroll.Controls.View` and `Payroll.Controls.Manage`.
- `IPayrollApprovalGuard` is the shared maker-checker/self-approval hook used by bank-advice approval, accounting approval/posting, and statutory approval/filed transitions.
- Provider acceptance classes cover control persistence, period lock lifecycle, self-approval policy, readiness, idempotency, lifecycle guards, tenant isolation, and cleanup for SQL Server and MySQL. Both providers have completed first-run and repeatability verification.
- Fixed permission IDs were added for the existing Phase 7G–7K payroll permission catalog so tenant seeding remains deterministic.
- The frontend period controls require a reason for locking and unlocking and expose unlock only with `Payroll.Period.Unlock`.
- Configuration Health is available at `GET /api/payroll/configuration-health` and reports tenant-scoped Salary, Statutory, Payment, Accounting, Controls, and EmployeeReadiness categories.
- Payroll Operations is available at `GET /api/payroll/dashboard/operations` and uses aggregate lifecycle counts for periods, runs, payslips, bank advice, accounting, statutory returns, retro, and final settlement.
- Focused UI pages and tests cover both operational views; the frontend suite passes 590 tests after these additions.
- The centralized approval guard now also protects retro application and final-settlement approval/finalization. Final-settlement creation persists `CreatedByUserId` from the authenticated tenant user; approval compares that authoritative creator identity, while finalization compares the recorded approver.
- Bank Advice cancellation now accepts an optional reason and applies the tenant cancellation-reason policy; the reason is retained in the Bank Advice history message.
- The SQL Server and MySQL migrations add the control configuration table and period lock metadata. Phase 7A–7K migrations remain unchanged.

## Guardrails

Readiness is informational until individual payroll commands consume it as a blocking precondition. Existing finalized payroll artifacts remain governed by their phase-specific immutable workflows. No payroll calculation, statutory formula, banking, accounting, or compliance calculation was introduced here.

Controls remain an incremental hardening layer. Readiness and health are reporting projections and do not silently recalculate payroll. The operations dashboard currently reports lifecycle queues; persisted readiness-error counts remain zero because readiness results are not stored.

Idempotency policy is source-uniqueness and lifecycle based: repeated protected transitions return a conflict or the existing artifact, while generation services retain their existing aggregate/source uniqueness guards. Concurrency is covered by focused independent-context tests for the complete matrix, including statutory source generation and non-zero retro application.

## Verification

- Focused period/control tests cover mandatory lock and unlock reasons, persisted audit metadata, unsafe unlock rejection, lifecycle behavior, and tenant scoping.
- Focused controls tests cover self-approval blocking, Final Settlement creator persistence, readiness failure reporting, configuration health, operations counts, and the concurrency matrix; Payroll fast passes with 55 tests.
- Focused frontend controls tests pass 2/2; full frontend passes 590/590; TypeScript and production build pass.
- Backend test-project build passes with existing repository warnings.
- The Phase 7L migration pair is generated independently for SQL Server and MySQL.

## Idempotency matrix

| Operation | Retry behavior | Protection |
| --- | --- | --- |
| Payroll calculation | Existing lifecycle/owned result guards reject an unsafe repeat | Run lifecycle and source ownership |
| Payslip generation | Existing payslip source/version rules govern regeneration | Payroll result/published immutability |
| Bank advice | Existing payroll-result duplicate protection rejects active duplicates | Tenant/source uniqueness and lifecycle |
| Accounting journal | Existing run/configuration duplicate guard rejects unsafe duplicates | Tenant/run/configuration uniqueness |
| Statutory return | Existing statutory-source duplicate guard rejects active duplicates | Tenant/source uniqueness |
| Retro apply | Applied case cannot be applied again | Retro status and adjustment source |
| Final settlement calculation | Repeated calculation reuses the calculated lifecycle and does not add lines | Settlement lifecycle and explicit line source |
| Final settlement finalization | Finalized case cannot be finalized again | Settlement lifecycle and approval guard |

## Maker-checker matrix

| Workflow | Maker source | Protected action | Self-approval | Permission/audit |
| --- | --- | --- | --- | --- |
| Payroll Run | `StartedByUserId` | Approved/Finalized transition | Guarded | Run permission and run history |
| Bank Advice | `GeneratedByUserId` | Approval | Guarded | Bank Advice permission and history |
| Accounting | `GeneratedByUserId` | Approval/Posting | Guarded | Accounting permission and history |
| Statutory Return | `GeneratedByUserId` | Approval/Filed | Guarded | Compliance permission and history |
| Retro | `DetectedByUserId` | Approval/Application | Guarded | Retro permission and history |
| Final Settlement approval | `CreatedByUserId` | Approval | Guarded | Final Settlement permission and history |
| Final Settlement finalization | `ApprovedByUserId` | Finalization | Guarded against approver | Final Settlement permission and history |
| Period | Existing lock/unlock actor and policy | Unlock/reopen | Policy/permission guarded | Period history |

## Cancellation and immutability

Bank Advice is the currently supported payroll cancellation endpoint with configured reason enforcement and history retention. Other aggregates expose only the lifecycle actions implemented by their phase-specific services; no new cancellation routes were invented in this pass.

Protected terminal states remain service-controlled: published payslips, posted journals, filed returns, applied retro cases, and finalized settlements cannot be directly edited. Payroll results and statutory results remain historical records. Period unlock rejects downstream approved/finalized payroll runs.

## Failure recovery review

Existing generation services persist their aggregate and dependent rows in the same `SaveChangesAsync` unit where the repository service owns both writes. Source-consumption operations use lifecycle/source guards before persistence. No demonstrated partial-commit defect was found in this pass, so no transaction refactor or new migration was introduced.

## Final provider verification

- SQL Server `SqlServerPayrollProductionControlsIntegrationTests`: first run PASS, 1/1; second run against the same database PASS, 1/1. Operator durations were not included in the supplied evidence.
- SQL Server verified migration `20260920063113_AddPayrollControlsProductionReadiness`, controls persistence, lock/unlock, maker-checker, PreventSelfApproval, readiness, idempotency/duplicate protection, lifecycle guards, tenant isolation, cross-tenant denial, cleanup, and repeatability with no provider issues.
- MySQL `MySqlPayrollProductionControlsIntegrationTests`: first run PASS, 1/1; repeatability PASS, 1/1, with no provider issues.
- Final Settlement creator identity and approval self-check are now implemented and covered by focused tests.
- Final Settlement, Payroll Period, Payroll Run, and Statutory Return lifecycle versions are concurrency tokens; competing-finalization coverage has one winner and one rejected transition, while repeated statutory validation is idempotent.
- The completed non-provider backend regression passes 1,125 tests with 0 failures and 0 skips in 2.769 minutes.
- The bounded large-data acceptance creates 100 employees and 100 payroll results, verifies two pages of 50, reconciles gross totals to ₹1,000,000, and confirms tenant isolation.
- The final concurrency matrix covers period lock, Payroll Run, Bank Advice, accounting generation, statutory generation, non-zero ₹100 retro apply, and Final Settlement finalization.

## Production checklist

Before a real payroll, validate tenant configuration, active salary structures and assignments, statutory and accounting configuration, employee identifiers and bank data, numbering, roles and permissions, maker-checker policy, a test payroll, reconciliation reports, backups, and applied database migrations.

## Reliability verification

- The bounded large-data acceptance creates 100 employees with active assignments, runs the existing calculation engine, verifies 100 calculated results, retrieves two 50-row register pages, reconciles gross totals to 1,000,000 INR, and confirms another tenant receives no rows.
- Final Settlement competing finalization uses independent DbContexts and the concurrency token; one transition succeeds and exactly one finalized history event remains.
- The concurrency matrix has 6 new passing tests, including true statutory generation and non-zero retro application; the existing Final Settlement concurrency test is also passing. Payroll fast passes 55 tests.
- The completed non-provider backend regression passes 1,125 tests with no failures or skips in 2.769 minutes.

### Provider parity

SQL Server and MySQL are aligned for control configuration persistence, lock/unlock state, maker-checker and self-approval policy, readiness, duplicate/idempotency guards, lifecycle protection, tenant isolation, cross-tenant denial, cleanup, and repeatability.

### Performance sanity review

Readiness, configuration health, and the operations dashboard use scoped projections/aggregate counts; payroll register, payslips, Bank Advice, accounting journals, statutory returns, retro cases, and final settlements use paged or source-scoped queries in their existing services. No new N+1 issue was identified in the Phase 7L paths. The large-data test validates correctness and pagination without a timing threshold.

## Deferred

This phase does not add external ERP, live banking, statutory portal submission, loan or reimbursement modules, gratuity calculation, or new payroll calculation functionality.
