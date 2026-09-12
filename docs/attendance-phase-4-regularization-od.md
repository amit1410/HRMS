# Attendance Phase 4: Regularization and On Duty

Phase 4 adds employee-request workflows without changing historical punch data.

## Regularization

An employee submits a single-business-date request through the account-to-employee link. A request is
Pending until an effective manager for that date approves or rejects it. Approval creates an immutable
`AttendanceAdjustment`, appends an approval event, and reprocesses the existing
`EmployeeAttendanceDay`. Reprocessing combines raw punches with the approved effective in/out values;
raw `AttendancePunch` rows, counts, and source history remain unchanged. Rejected and cancelled requests
are ignored by processing, and only one Pending request is allowed for an employee/date.

Future dates and clean Present days are not eligible under the initial conservative policy. No arbitrary
age cutoff is applied; a configurable policy can be added later.

## On Duty

On Duty is a separate request type and table. The initial workflow supports full-day ranges. Approval
creates an append-only approval event and reprocesses every affected business date, projecting
`OnDuty` while preserving punches and the underlying roster day type. Approved Leave overlap is rejected
without changing Leave. Pending, rejected, and cancelled requests have no attendance effect.

Both workflows use effective-date manager authorization, tenant-scoped queries, append-only lifecycle
events, and safe already-processed conflicts. Leave is read-only to Attendance.

## Deferred

Multi-level approvals, attachments, policy cutoffs, partial-day OD, Regularization UI, OD UI,
Regularization supersession, payroll, overtime, comp-off, biometric adapters, and monthly close remain
out of scope for this phase.
