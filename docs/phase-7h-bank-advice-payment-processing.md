# Phase 7H — Bank Advice & Payment Processing

## Scope

Phase 7H prepares controlled, auditable payment instructions from approved or finalized payroll results. It does not recalculate payroll or submit funds to a bank.

## Architecture

`BankAdviceBatch` is tenant-scoped and linked to one `PayrollRun`, `PayrollPeriod`, currency, and pay date. `BankAdvicePayment` rows snapshot the payroll result, employee identity, masked salary-account details, validation outcome, amount, sequence, and deterministic payment reference. `BankAdviceHistory` records immutable lifecycle events.

Payroll results remain authoritative. The service never resolves salary structures or recalculates amounts. Bank account resolution uses the existing `EmployeeBankDetail` source and selects an active Salary-purpose account effective on the payroll pay date. Missing, ambiguous, incomplete, or non-positive payment inputs remain explicit validation failures.

## Lifecycle and controls

The lifecycle is `Draft -> Prepared -> Approved -> Exported`, with controlled cancellation before export. Generation is restricted to `Approved` or `Finalized` payroll runs. Approval requires every payment instruction to be valid and the batch total to reconcile to its instruction rows. Concurrency-version guarded updates prevent duplicate lifecycle transitions.

Batch and payment numbers are tenant-scoped and deterministic. A batch cannot be generated twice for the same active payroll run, and the database enforces unique batch numbers, payment references, and payroll-result membership within a batch. Account numbers are stored in snapshots only in masked form.

## Export and security

The provider-neutral export is escaped CSV. Export records the actor, time, status transition, payment status, and immutable history event. No live bank API, payment submission, bank-specific signed/encrypted format, or settlement confirmation is implemented. Bank Advice permissions are separate for view, generate, validate, approve, export, cancel, and history. Tenant filters and composite tenant foreign keys protect every read and write.

## APIs and frontend

The API supports generation from a payroll run, list/detail, validation, preparation, approval, cancellation, and CSV export. The Payroll navigation includes a Bank Advice page with batch selection, masked payment rows, validation messages, lifecycle actions, and export.

## Persistence

Provider migrations add `BankAdviceBatches`, `BankAdvicePayments`, and `BankAdviceHistories`:

- SQL Server: `20260919202210_AddBankAdvicePaymentProcessing`
- MySQL: `20260919202237_AddBankAdvicePaymentProcessing`

Payroll source relationships are restrictive to preserve payroll history; payment and history rows are owned by their batch. Phase 7A–7G migrations are unchanged.

## Verification

- SQLite focused Bank Advice tests: 2 passed, covering valid generation/validation/approval/export, masking, and rejection of unapproved runs.
- Frontend focused Bank Advice tests: 2 passed, covering batch display/payment validation state and empty state.
- Backend and solution builds compile the new model and APIs successfully; existing repository warnings remain outside this phase.
- SQL Server provider acceptance: PASS, `SqlServerBankAdviceIntegrationTests.SqlServer_bank_advice_provider_behavior_preserves_provider_neutral_semantics`, 1 passed, 0 failed, 0 skipped, first run 6.0 seconds.
- SQL Server repeatability: PASS against the same integration database, 1 passed, 0 failed, 0 skipped, 8.6 seconds. Migration application, batch/payment persistence, bank-account resolution, masked snapshots, validation, numbering, lifecycle, reconciliation, CSV export, duplicate PayrollResult protection, tenant isolation, cross-tenant denial, and FK-safe cleanup all passed through the shared provider-neutral acceptance path.
- MySQL provider acceptance: PASS with behavioral coverage and repeatability, with no provider issues. SQL Server and MySQL now have equivalent persistence, totals, numbering, lifecycle, export, and tenant-isolation behavior.
- Security verification: full account numbers are not exposed by the Bank Advice DTO/UI; employee self-service has no Bank Advice permission or route. Payment statuses remain internal processing states; no live bank submission exists.
- Phase 7H provider blocker: RESOLVED. No unresolved Phase 7H product or provider blocker remains.

## Deferred

Live bank APIs, payment submission, NEFT/RTGS/IMPS, SFTP or bank-specific payment files, fund-transfer confirmation, accounting/GL posting, statutory returns, arrears/retro, and final settlement remain deferred.

## Final status

Phase 7H acceptance is complete. Bank Advice prepares auditable internal payment instructions from approved/finalized payroll results; it does not submit payments or claim external settlement.
