# Phase 6C — Overtime Management & Payroll Integration

Phase 6C adds overtime units on top of the completed Attendance 6B contract:

`Daily Attendance → approved OT request → OT finalization → immutable PayrollOvertimeSnapshot → Payroll`

The OT module never reads raw punches for payroll and never persists an OT monetary amount. Raw punches may contribute to `EmployeeAttendanceDay`; OT derives actual eligible minutes from that resolved Attendance row. Payroll remains the owner of monetary calculation and monetary rounding.

## Policy and request lifecycle

`OvertimePolicy` is tenant-scoped and effective-dated. Resolution uses the OT work date, not the current date. Thresholds, rounding mode, daily/monthly caps, category switches, and normal/week-off/holiday multipliers are policy data. `EligibilityMode = None` is an explicit ineligible configuration; the initial implementation keeps the eligibility extension point small and does not introduce a second rule engine.

Requests retain `RequestedMinutes`, `ActualEligibleMinutes`, and `ApprovedMinutes` separately. Actual minutes come from resolved Attendance: normal-day extra time is worked minutes minus expected work minutes; week-off and holiday time use resolved worked minutes. Threshold, rounding, and daily cap are applied before approval. One active request per employee/work date prevents duplicate approval sources.

## Finalization and versioning

OT finalization requires a closed Attendance period and the current Attendance snapshot. It creates a zero-capable Payroll OT snapshot for every employee represented by the Attendance finalization, records Attendance snapshot/version and policy multipliers, and preserves prior versions. Re-finalization marks the previous current version non-current and creates a new version. Database indexes enforce unique `(tenant, period, employee, version)` and the repository’s current-version uniqueness convention.

Payroll resolves only a finalized current `PayrollOvertimeSnapshot`; it does not query requests or raw Attendance. The snapshot contains minutes and policy multipliers, not money. A later Payroll integration consumes those units through the existing Payroll calculation/component boundary. No Comp-Off lifecycle is implemented in Phase 6C; it is deferred to Phase 6D.

## Boundaries and limitations

The existing Attendance business-date semantics remain authoritative, including overnight-shift handling once the daily resolver has produced a business-date row. The first eligibility mode is deliberately conservative; additional employee-master criteria can be added as a later policy extension without changing the snapshot contract. Finalized Payroll remains protected by the existing Payroll correction boundary.

Provider migrations are separate Phase 6C migrations for SQL Server and MySQL. Phase 6A/6B, Payroll historical, Phase 8, and protected Phase 7U/7V migrations are not regenerated or modified.
