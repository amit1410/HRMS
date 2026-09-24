# Phase 8H — Separation Production Closure & Exit Execution

Phase 8H is the terminal Separation execution phase. It consumes the authoritative Separation state produced by Phases 8A–8G and performs the employment and tenant-access exit without deleting historical data.

## Readiness and authoritative date

`SeparationExitService` re-reads the tenant-scoped Separation, clearance, exit-interview, settlement, employee, direct-report, and account-link state before execution. The authoritative exit date is `EmployeeSeparation.ApprovedLastWorkingDate`; a client cannot supply or override `DateOfLeaving`. Execution is blocked before the repository business date reaches that LWD.

Readiness returns structured blockers, including incomplete notice, clearance, assets, exit interview, settlement, inactive employment, and unresolved direct reports. The settlement orchestration and Phase 8F final settlement remain authoritative; Phase 8H only reads their completion state.

## Durable execution and recovery

`SeparationExitExecutions` is a tenant-scoped, unique execution record. `SeparationExitExecutionEvents` is append-only with one canonical event per execution step. The execution stores an immutable non-monetary snapshot of the employee, Separation, LWD, manager, settlement, and account references.

Execution checkpoints are persisted in order: employment exit, tenant account deactivation, refresh-token revocation, role reconciliation, manager reconciliation, and final Separation closure. Retries resume from completed checkpoints and do not create a second terminal employment transition, token material, role-history event, or `SeparationClosed` event. Partial failures remain retryable and are not presented as Closed.

## Employment and access semantics

The employee remains queryable. Existing employment history is retained; the current employee receives the repository-standard terminated/separated status and the authoritative `DateOfLeaving`. The linked tenant user is deactivated, active refresh tokens are revoked, and current tenant role assignments are ended with append-only assignment history. Account-to-employee link history is retained. No global account belonging to another tenant is disabled by a tenant-scoped exit.

Direct reports block closure until an effective reassignment exists. No manager, HR, or operational scope is invented by this phase. Existing authorization and authentication services remain the source of access semantics.

## Closed-state boundary

After completion, Separation is `Closed`. Normal LWD revision, clearance reopen, exit-interview reopen, settlement initiation, and ordinary execution retry cannot reopen or reverse closure. Historical reads remain available to authorized users. Correction requires an explicit audited correction process; rehire, account restoration, alumni access, and financial rollback are outside this phase.

## Cross-module boundaries

Phase 8H does not recalculate or mutate Final Settlement, PayrollResult, gratuity, adjustments, loans, reimbursements, GL, or bank advice. It does not rewrite Phase 8G document bytes, snapshots, hashes, or numbers. Leave, attendance, payroll, role, account-link, employment, document, and Separation records remain historically traceable after inactivation.

## Provider and acceptance coverage

SQL Server and MySQL receive separate Phase 8H migrations for the execution aggregate and append-only events. Provider wrappers validate model visibility and migration connectivity. Acceptance coverage includes readiness, authoritative LWD execution, employee inactivation, tenant access deprovisioning, refresh-token revocation, role history, idempotent retry, closed-state protection, and tenant isolation. Secrets and token values are never written to the snapshot, audit events, logs, or documentation.

## Deferred scope

Phase 8H does not implement Phase 8I, rehire, alumni access, generic IAM, employment reactivation, or automatic reversal of a completed exit.
