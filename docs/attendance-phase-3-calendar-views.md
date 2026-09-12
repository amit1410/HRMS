# Attendance Phase 3: Calendar Views

Phase 3 adds read-only attendance views over the Phase 1 roster and Phase 2 processed-day
foundations. The employee calendar is built from the authenticated account's authoritative
account-to-employee link; it never accepts an arbitrary employee identifier. Each month returns
planned roster days as well as processed `EmployeeAttendanceDay` rows, using `NotProcessed` for
future or unprocessed dates.

The daily read model keeps calendar `DayType` (`Shift`, `Holiday`, or `WeeklyOff`) separate from
attendance status and exposes punch summaries, sessions, variance flags, Leave Conflict, calendar
source, assignment source, and override state. Leave is read-only input and is not changed by these
queries.

Manager team queries are bounded to 92 days, paged server-side, and authorize each employee/date
through the existing effective-date manager resolver. Tenant filters apply to employees, roster,
punches, processed days, and Leave lookups. Employee and manager detail routes reuse the same read
model and remain read-only.

The React pages provide month navigation, status/source summaries, server-backed manager filters,
pagination, daily detail, loading/error/empty states, and semantic status/variance labels. Times
remain UTC values from the API and are formatted by the browser's locale; no timezone is hard-coded.

Regularization, OD, punch correction, manager approval, month close, overtime, comp-off, payroll,
and biometric/vendor integrations are intentionally deferred.
