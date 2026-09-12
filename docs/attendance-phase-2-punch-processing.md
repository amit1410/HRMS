# Attendance Phase 2: punch processing foundation

Phase 2 keeps raw capture separate from calculated daily attendance.

## Processing flow

`AttendancePunch` is an immutable, tenant-scoped source record. Ingestion validates the employee, resolves the effective Shift through the existing roster service, calculates the business date, enforces the Shift's allowed capture sources, and applies the source idempotency key. The current implementation is synchronous; vendor adapters and queues are intentionally deferred.

`AttendanceDayProcessor` resolves the same effective roster, reads approved Leave without changing Leave state, pairs deterministic In/Out sessions, subtracts unpaid fixed breaks, calculates schedule variances, and upserts one `EmployeeAttendanceDay` per tenant/employee/business date.

## Rules captured

- Business dates and overnight shifts are resolved in the tenant time-zone abstraction. The default provider is UTC until a tenant-specific setting exists; IST is not hard-coded.
- Raw punches are never edited or deleted by this foundation. Duplicate external identifiers are safe to replay within a tenant and source.
- Primary daily status is normalized (`Present`, `Incomplete`, `OnLeave`, `Holiday`, `WeeklyOff`, `Absent`, or `NotProcessed`). Late, grace, early-out, single-punch, invalid-sequence, missing-punch, Leave-conflict, and mark-out-approval conditions remain independent flags.
- Holiday and WeeklyOff days remain non-working unless the effective roster contains an explicit working override. Punches are preserved in either case.
- Approved Leave is read-only. A no-punch day is `OnLeave`; qualifying punches produce attendance with a Leave conflict flag.
- Processing is restart-safe through the daily unique key and reprocessing updates the existing daily aggregate.

## Persistence and scope

SQL Server and MySQL migrations create `AttendancePunches` and `EmployeeAttendanceDays` with tenant-aware foreign keys, query indexes, the external-punch uniqueness constraint, and the daily uniqueness constraint.

Deferred: biometric/vendor adapters, device management, portal punch UI, OD, Regularization, Attendance Calendar UI, month close, overtime, comp-off, payroll, sandwich/variable-minute policy, shift swap, geolocation, face recognition, and mobile attendance.
