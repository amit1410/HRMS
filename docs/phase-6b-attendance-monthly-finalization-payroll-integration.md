# Phase 6B — Attendance Monthly Finalization & Payroll Integration

## Contract

Payroll consumes `PayrollAttendanceSnapshot` only. The source path is:

```text
AttendancePunch -> EmployeeAttendanceDay -> AttendancePeriod processing
-> EmployeeAttendanceMonthlySummary -> immutable PayrollAttendanceSnapshot
-> PayrollCalculationEngine
```

Raw punches and open daily/monthly Attendance data are never queried by Payroll.

## Finalization and versioning

`AttendancePeriod` remains the existing tenant-scoped calendar-month lifecycle. A
period must be processed and pass the existing blocking-exception preview before
it can be finalized. Finalization creates one current snapshot per employee in
the same transaction as closing the period. The database uniqueness constraint
on tenant, period, employee, and current flag prevents two current snapshots.

Reopen/refinalization supersedes the current snapshot and creates version N+1;
prior versions remain queryable and are never edited. Finalized Payroll blocks
Attendance reopen/close correction until the existing Payroll correction path is
used. Payroll results record snapshot ID, version, eligible days, payable days,
and LOP days for traceability.

## Semantics

Employment eligibility is the inclusive intersection of the employee's effective
employment interval and the calendar period. Days before joining or after leaving
are excluded, not marked absent or LOP. Existing resolved daily status remains
authoritative; no universal Saturday/Sunday rule is introduced. Current Phase 6A
daily statuses are whole-day, but all payroll-facing quantities use decimal
columns so fractional-day semantics can be added without changing the contract.

The current implementation treats resolved `Absent` as LOP and resolved
`OnLeave` as paid leave. The existing daily processor remains responsible for
approved Leave, On Duty, and regularization precedence. Unresolved/pending
regularization and On Duty remain close blockers. Existing employment-date
proration remains the single salary-proration path; Attendance payable/LOP facts
are carried as authoritative inputs and are not independently re-prorated by the
generic Payroll component engine.

## Failure and provider safety

Snapshot writes are in the finalization transaction. A failed write cannot leave
the period finalized. Payroll returns structured Attendance errors when a matching
Attendance period is open or has no current snapshot. Existing legacy Payroll
periods without an Attendance period remain compatible until they are migrated
to the Attendance contract.

SQL Server and MySQL migrations are separate and do not modify historical
Attendance, Payroll, Separation, or protected Phase 7U/7V migrations.
