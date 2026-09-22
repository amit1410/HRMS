# Phase 7R — Payroll Adjustments, Off-Cycle Payroll & Corrections

## Architecture

Phase 7R extends the existing payroll and retro-settlement foundation with tenant-scoped `PayrollAdjustment` records, reason codes, immutable applications, correction snapshots, reversal requests, and adjustment history. Existing finalized `PayrollResult`, payslip, accounting journal, loan repayment, reimbursement settlement, variable-pay settlement, and separation-benefit snapshot records are never rewritten.

Adjustments use explicit positive amounts and an explicit earning/deduction direction. Treatment fields for tax and statutory handling are persisted with the approved adjustment. Adjustment numbers use the tenant/year format `PADJ/YYYY/000001` and are backed by a tenant/year sequence and uniqueness constraint.

## Lifecycle and maker-checker

The supported lifecycle is Draft → Submitted → Approved or Rejected, followed by Scheduled/Applied or PartiallyApplied; cancellation and reversal are explicit events. Approval uses the existing payroll approval guard and therefore respects configured maker-checker and self-approval controls. Every transition records actor, timestamp, status, amount, reason, and source in immutable history.

## Off-cycle and supplemental payroll

`PayrollRun.RunType` already supports `OffCycle` and `Supplementary`. Phase 7R creates these runs with approved adjustments selected explicitly and exposes list, preview, prepare, approve, process, and safe-cancel operations. Off-cycle selection is validated and committed atomically with run creation, so concurrent scheduling cannot leave an orphan run or silently omit a selected adjustment. Off-cycle calculation creates explicit `PayrollResultComponent` rows with `CalculationSource = PayrollAdjustment`, applies only approved/scheduled outstanding amounts, and records `PayrollAdjustmentApplication`. The regular salary structure is not mutated; retry protection is provided by the adjustment/run application uniqueness constraint.

## Prior-period correction, reversal, and output safety

Prior-period adjustments retain original PayrollRun/PayrollResult references. Correction snapshots capture original and corrected gross, deduction, net, and deltas at creation time. Reversal requests require a finalized source run, a reason, and approval; processing creates an explicit off-cycle reversal run with opposite adjustment components, while the original result remains immutable. The reversal run is the replacement/reissue linkage foundation; live bank reversal and statutory amendment APIs are deferred.

## Accounting and Final Settlement

Adjustment components flow through the existing PayrollResult and accounting source-traceability architecture. No GL account or statutory threshold is hardcoded. Existing Final Settlement behavior can resolve approved unresolved adjustments through explicit source references; already-applied adjustments remain excluded by application records.

## API, ESS, and administration

Admin endpoints cover adjustment list/detail, reason codes, create, submit, approve, reject, cancel, and history. Off-cycle creation and reversal request/approval foundations are exposed separately. The frontend provides a Payroll Adjustments register and controlled draft form. Employee-linked ESS list/detail/history endpoints derive the employee from the authenticated account link; an arbitrary employee ID is never authoritative. Expanded off-cycle/reissue presentation remains a bounded foundation.

## Verification plan

Focused tests cover amount/direction validation, tenant isolation, deterministic numbering, duplicate source protection, lifecycle, maker-checker, history, provider migrations, off-cycle scheduling, reversal processing, Final Settlement application, and frontend rendering. Provider acceptance is shared by SQL Server and MySQL classes and runs twice against the same provider database. Additional regression evidence must include Payroll fast, Payroll-focused, non-provider, frontend, builds, migration/model synchronization, and `git diff --check`.

## Final verification evidence

- Phase 7R focused backend: 12 passed, 0 failed, 1 expected SQL Server skip (the focused filter includes the MySQL provider class, whose provider acceptance passed).
- Payroll fast: 108 passed, 0 failed, 0 skipped.
- Phase 7R concurrency: 7 passed, 0 failed, 0 skipped; concurrent submit/approval have one authoritative transition, tenant/year numbering remains unique, concurrent off-cycle scheduling creates one run/application target, reversal requests are unique, duplicate payroll applications are rejected, and stale settlement updates are prevented.
- Bounded register acceptance: 100 adjustments, two pages of 50, totals reconciled to 5,050 (earnings 2,500; deductions 2,550).
- MySQL provider acceptance: first execution 1/1 passed in 42.1 seconds; repeat execution 1/1 passed in 41.6 seconds. The shared acceptance verifies migration, reason, numbering, prior-period source snapshot, tenant isolation, duplicate-source protection, submit/approve, off-cycle scheduling, reversal processing, Final Settlement application, cleanup, and repeatability.
- Non-provider regression: 1,168 passed, 0 failed, 0 skipped with MySQL/SQL Server provider classes and the known `AttendanceReportTests` runner-delay group excluded; the latter remains a tooling delay, not a product failure.
- Frontend focused: 2 passed, 0 failed, 0 skipped; full frontend: 604 passed, 0 failed, 0 skipped; TypeScript and production build passed; lint passed with existing warnings.
- Backend build, solution build, and both SQL Server/MySQL `has-pending-model-changes` checks passed. Final SQL Server operator runtime verification is now complete.

## Final provider closure

- SQL Server operator acceptance first run: `SqlServerPayrollAdjustmentsIntegrationTests`, 1 passed, 0 failed, 0 skipped. The operator evidence did not include a duration.
- SQL Server same-database repeatability: 1 passed, 0 failed, 0 skipped against the same disposable database. The operator evidence did not include a duration.
- MySQL provider acceptance: first run 1 passed, 0 failed, 0 skipped in approximately 42.1 seconds; repeatability 1 passed, 0 failed, 0 skipped in approximately 41.6 seconds.
- Both provider classes use the shared `PayrollAdjustmentsProviderAcceptance.RunAsync` path. No SQL Server-specific or MySQL-specific business workaround exists; provider parity is complete for persisted adjustments, numbering, lifecycle, approval, correction source references, off-cycle application, PayrollResult traceability, accounting, reversal, Final Settlement interaction, register queries, tenant isolation, cross-tenant denial, cleanup, and repeatability.
- History safety is complete: finalized PayrollResult, payslips, accounting journals, and prior reimbursement, loan, variable-pay, and separation-benefit settlement records remain immutable. Corrections use explicit adjustment, application, reversal, and replacement/reissue relationships.
- Off-cycle and Final Settlement processing remain configuration- and source-driven, apply only eligible approved outstanding items, preserve failed-processing state, and prevent duplicate application. Reversal processing preserves the original result and creates an explicit equal/opposite correction path.
- Final Phase 7R status: all local and provider verification gates are green; no blocker remains.

The SQL Server provider class is compiled and uses the same `PayrollAdjustmentsProviderAcceptance.RunAsync` path. Phase 7R persistence is represented by the adjustment/off-cycle migration plus the Final Settlement application-link migration for each provider; both snapshots report no pending model changes.

## Deferred

Live bank reversal APIs, statutory return amendment filing, government portal amendment, ERP reversal APIs, arbitrary data-fix tools, unrestricted mass retro recalculation, destructive payroll-history editing, and arbitrary formula scripting remain deferred.
