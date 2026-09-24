# Phase 8F — Final Settlement Orchestration & Separation Closure Readiness

Phase 8F is a non-monetary handoff from Separation to the existing Payroll
Final Settlement domain. `SeparationSettlementOrchestration` owns only
readiness, lifecycle linkage, immutable handoff facts, retry/reconciliation
state, and append-only orchestration events.

## Readiness

Readiness is evaluated from current authoritative state at every read and
initiation. It requires an approved LWD, finalized notice facts, completed
clearance, no pending asset return, a completed/authorized non-participating
exit interview, and an authoritative employee identity. Recovery-required
assets contribute references/counts only; Separation never invents a recovery
amount.

The initiation snapshot records LWD, notice quantities, clearance and exit
interview references, and pending recovery count. It contains no money.

## Payroll ownership

Initiation calls the existing `IPayrollRetroSettlementService` and persists
the canonical `FinalSettlementCase` ID. Payroll remains responsible for
salary, notice recovery, leave encashment, gratuity/separation benefits, tax,
loans, reimbursements, adjustments, calculations, maker-checker, and final
settlement immutability. No second settlement, gratuity, loan, reimbursement,
or asset-recovery engine exists in Separation.

Existing Payroll status is projected by the status endpoint. A finalized
Payroll settlement is acknowledged as `Completed` by orchestration and makes
the separation eligible for derived `ReadyForFinalExitClosure` when current
readiness has no blockers.

## Idempotency and failure handling

One orchestration is unique per tenant/separation and the Payroll settlement
reference is unique where present. Retries first reconcile an existing Payroll
settlement reference and never recreate a finalized settlement. Orchestration
events are append-only. Corrections after Payroll finalization belong to the
existing Payroll correction mechanisms.

Clearance and LWD changes are re-evaluated before initiation. Exit interview
reopen remains operational and does not silently mutate a finalized Payroll
result. Employment termination, account disablement, DateOfLeaving, letters,
and alumni/rehire processing remain deferred.

## Provider parity and UI

SQL Server and MySQL receive separate Phase 8F migrations containing only the
two orchestration tables. The HR dashboard is server-paged and exposes
readiness, blockers, Payroll status, initiate, and retry actions. It does not
provide a duplicate Payroll settlement editor or financial amount inputs.
