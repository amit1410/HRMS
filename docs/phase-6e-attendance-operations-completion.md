# Phase 6E — Attendance Operations Completion

Phase 6E adds an operational read/workbench layer over the existing Attendance
engine. It does not calculate a second attendance result. Daily attendance,
raw punches, Leave, On Duty, Regularization, monthly finalization, and Payroll
attendance snapshots remain the authoritative workflows.

## Implemented boundary

The new operational service derives exception rows from `EmployeeAttendanceDay`
and its effective employee/period data. Queries apply tenant scope,
authorization scope, filters, ordering, count, and paging before materializing
the page. Employee self-service is exposed separately from the operational
route, so a self query cannot be used to browse another employee.

The first operational exception categories are the existing missing/incomplete
and processing conditions plus late arrival, early departure, and absence.
Late and early values remain calculated Attendance facts; the workbench does
not overwrite them in the client.

## Operations API

- `GET /api/attendance/me/exceptions` — self-scoped exception inbox.
- `GET /api/attendance/operations/exceptions` — authorized manager/HR/Time
  Manager operational query with server-side paging and filters.
- `GET /api/attendance/operations/dashboard` — scoped summary counts.
- `POST /api/attendance/operations/bulk` — bounded, version-checked review of
  existing Regularization and On Duty requests.
- `GET /api/attendance/operations/exceptions/export` — scoped CSV export.
- `POST /api/attendance/operations/manual` — submits an operator manual
  Attendance request into the existing Regularization maker-checker workflow.

Bulk actions reuse the existing approval services' entities and reprocess the
affected day/range through `IAttendanceDayProcessor`. The request is bounded
to 100 items and each item carries an expected attendance version. Stale or
unauthorized items return an item-level failure rather than being silently
applied.

Manual Attendance requests validate employee scope, an expected Attendance
period version, open-period status, correction type, reason, and punch ordering.
They create a pending `AttendanceRegularizationRequest` plus a submitted event;
they do not mutate `EmployeeAttendanceDay` until the existing approval path
creates an `AttendanceAdjustment` and reprocesses the day. The submitting maker
cannot approve the request through the existing workflow.

## Finalization and history

Phase 6E does not mutate raw punches. Existing append-only attendance
corrections and period lock/reopen services remain the correction boundary.
Closed/finalized periods are protected by those services; reopening requires a
reason and preserves the prior period/snapshot history. Re-finalization creates
the next version under the existing monthly processor. Payroll snapshots keep
their original historical link until an explicit Payroll correction workflow is
requested.

## Frontend

The Employee `My Attendance Exceptions` page uses the self endpoint. The
Attendance Operations page uses the scoped operational endpoint and displays
server-paged rows plus dashboard counts. No balance, exception, or attendance
calculation is performed in React.

## Verification status

## Functional gap matrix

| Capability | Status |
|---|---|
| Exception workbench, employee inbox, scoped paging | Implemented |
| Missed IN/OUT, late, early, absence derivation | Implemented as derived exception facts |
| Manual Attendance request | Partial — submission is implemented; full operator UI/E2E remains |
| Maker-checker approval/rejection | Partial — existing Regularization workflow is reused |
| Manager and Time Manager/HR workbench | Partial — scoped query exists; action UI remains |
| Bulk approval/rejection | Implemented for existing Regularization/On Duty requests |
| Bulk Attendance correction | Missing |
| Dashboard | Implemented as scoped summary API; drill-down UI remains |
| Finalize/lock, controlled reopen, re-finalization | Reused existing Phase 6B services; Phase 6E operational acceptance remains |
| Audit/history and version traceability | Partial — existing correction/workflow history is retained |
| Payroll snapshot protection | Existing finalization/reopen boundary retained; dedicated Phase 6E E2E remains |
| Automatic exception resolution | Derived from current authoritative Attendance; dedicated acceptance remains |
| CSV export | Implemented and scope-filtered |
| Concurrency/failure/large-data acceptance | Missing in this pass |

The full Phase 6E acceptance matrix, large-data run, provider wrappers, and
provider executions remain to be completed and must not be described as passed
until executed. Phase 6F is deferred.

## Functional lifecycle evidence

- `Missed_punch_correction_reprocesses_attendance_and_resolves_exception` — PASS.
- `Manual_attendance_maker_checker_reprocesses_authoritative_day` — PASS.
- `Finalized_period_requires_reopen_and_preserves_payroll_snapshot_history` — PASS.
- Duplicate approval attempt — PASS; the second transition returns Conflict and
  does not create another approval event or adjustment.

These tests confirm that raw punch evidence is not rewritten, approved changes
reprocess through the authoritative Attendance processor, and refinalization
creates a new current snapshot while preserving the historical version.

## Versioned exception resolutions

The generic `EmployeeAuditLog` was not a suitable operational resolution
source: the Attendance version was encoded in free-form `Source`, while
exception type and action were encoded in `FieldName`; it had no structured
versioned resolution key. Phase 6E now stores resolution state in
`AttendanceExceptionResolutions`, keyed uniquely by tenant, Attendance day,
Attendance version, and exception type, with employee validation, terminal
action, actor, timestamp, and reason. `EmployeeAuditLog` remains the history
record. The operational query applies a correlated database-side existence
predicate before count and paging. `Acknowledge` and `Waive` are terminal for
that exact version and exception only; newer Attendance versions are evaluated
independently. The dedicated SQL Server and MySQL Phase 6E migrations were
scaffolded and both pending-model checks report no changes.

The same resolution endpoint and exact versioned query rule cover both Late
Arrival and Early Departure; neither operation changes calculated minutes or
punch evidence. The employee inbox and operational manager/HR workbench share
the same filtered query, so an explicitly resolved item is absent from active
lists while its audit history remains available. Prior verification: Late
resolution/version tests 3/3, Early resolution/version tests 3/3, and the
combined manager-and-employee resolved-inbox test 1/1. This pass adds approval
propagation and scoped Time Manager evidence described below.

Leave approval now invokes the existing Attendance day processor for each
approved request date inside the Leave approval transaction, after the
authoritative Leave status is saved and before approval commits. A processor
failure rolls back the approval. This is not an AttendanceExceptionResolution:
the Absent exception disappears because the Attendance processor reflects
approved Leave. On Duty approval already used this same processor pattern.
Focused real-service tests verify Leave and On Duty remove Absence from the
active exception query without synthetic resolution rows. A scoped Time
Manager acceptance exercises the existing Phase 6A department-role scope for
query, resolution and history, including denied out-of-scope access.
The resulting focused propagation/inbox slice passed 17/17; the additional
Leave, Leave-approval, and On Duty focused workflow group passed 16/16.

## Functional closure additions

Bulk Attendance correction is bounded to 100 items and returns one explicit
result per input. Identical repeated items within the same request are
idempotently mapped to the first outcome and request ID; they do not create a
second Regularization request, event, or audit row. Each unique item still
passes through the existing scope, open-period, expected-version, and
maker-checker request path. A successful request writes its submission event
and Attendance audit entry in the same EF save operation. Focused acceptance
for duplicate idempotency and successful-item history passed 2/2, in addition
to the existing valid, stale, locked, scope, per-item, and bounded-input tests.
Bulk Regularization approval/rejection acceptance also verifies per-item
decision events, authoritative reprocessing on approval, and no Attendance
adjustment on rejection.

The dashboard's Late, Early, Absent, and missing-punch counts use the same
scoped operational exception query and version-specific resolution exclusion
as workbench drill-down. Missing IN and Missing OUT are exposed as separate
drill-down controls; their aggregate is the sum of those disjoint predicates.
Pending Corrections is the scoped pending Regularization count and its action
scrolls to the existing manager correction inbox. The dashboard parity
acceptance compares each displayed count to the corresponding filtered query
over the identical date range.

Operational history now merges scoped Attendance audit records with existing
Regularization workflow events; it does not replace either source of truth.
Manual request creation writes an audit record linked to its request and
expected Attendance version. Regularization approval/rejection also writes an
audit event in the same workflow transaction, retaining old/new Attendance
status and period data versions alongside the request reference. History
returns both version endpoints when recorded; workflow event records retain
maker/checker actors, timestamps, decisions, comments/reasons, and references.
History remains tenant/scope/date constrained and is paged from bounded
source queries. Period reopen/refinalization evidence remains in the existing
Phase 6B AttendancePeriodEvent history. Payroll Attendance snapshot versions
remain immutable and the existing Phase 6B correction acceptance verifies the
historical Payroll result continues to reference its original snapshot.
The focused operational backend slice (AttendanceOperationsHttpTests,
AttendanceOperationsFunctionalLifecycleTests, and AttendanceWorkflowTests)
passed 54/54 after the latest workflow query and dashboard scope changes. It
includes Missing-IN derivation, manual-request rejection, bulk correction
validation/idempotency/audit, bulk approval/rejection, exception-count and
pending-correction drill-down parity, bounded filtered export and export
tenant isolation, same-tenant manager team exclusion, scoped roles,
and the three Phase 6E lifecycle E2Es. The focused Attendance Operations
frontend page tests passed 3/3; TypeScript and the production build passed.
Lint passed with the repository's existing warnings. No full frontend suite,
large-data acceptance, provider acceptance, or final broad regressions were
run in this functional pass.
