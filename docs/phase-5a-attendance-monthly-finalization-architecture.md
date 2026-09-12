# Phase 5A — Attendance Monthly Finalization Architecture

Status: design only. This document proposes the Phase 5 persistence and application boundaries; it does not add runtime code or migrations.

## 1. Purpose

Phase 5 turns the existing effective daily Attendance read model into a controlled monthly processing and finalization workflow. The design covers calendar-month processing, employee summaries, blocking exceptions, close/lock, reopen, administrator corrections, auditability, reporting, and provider parity.

The invariant is:

```text
AttendancePunch (immutable evidence)
    -> AttendanceDayProcessor
    -> EmployeeAttendanceDay (effective daily truth)
    -> monthly processing
    -> EmployeeAttendanceMonthlySummary
    -> close / report / export
```

Monthly processing must never derive truth directly from raw punches, and it must never rewrite an `AttendancePunch`.

## 2. Existing Architecture Reused

The following repository components are existing and are the source of truth for Phase 5:

| Existing component | Phase 5 use | Status |
| --- | --- | --- |
| `Shift`, `ShiftPattern`, `ShiftApplicabilityRule` | Resolve the effective shift and roster context for a date | Reuse |
| `EmployeeRosterDay` and roster/calendar resolvers | Resolve working, holiday, weekly-off, and override context | Reuse |
| `AttendancePunch` | Immutable source evidence only | Reuse, never mutate |
| `EmployeeAttendanceDay` | Source for monthly status, effective times, minutes, and variance flags | Reuse |
| `AttendanceDayProcessor` | Existing daily effective-state projection; remains authoritative | Reuse |
| `AttendanceReadService` and `AttendanceCalendarDayDto` | Existing calendar/detail presentation; monthly APIs should use analogous DTO conventions | Reuse pattern |
| `AttendanceRegularizationRequest` / `AttendanceAdjustment` | Approved effective correction; pending workflow is a close blocker | Reuse |
| `AttendanceOnDutyRequest` | Approved On Duty changes the effective daily status; pending workflow is a close blocker | Reuse |
| `EmployeeEmploymentHistory` | Date-effective employment population and historical manager relationship | Reuse |
| `EmployeeManagerResolver` | Date-specific manager authorization and historical scoping | Reuse |
| `ITenantContext` and `HrmsDbContext` global filters | Tenant routing, tenant predicates, and tenant stamping | Reuse |
| `Result<T>`, `PagedResult<T>`, `ToActionResult` | Service/API outcome and paging contract | Reuse |
| `ConcurrencyVersion` on Phase 4 workflow rows | Workflow mutation concurrency; period operations need a comparable token | Extend pattern |
| `BeginTransactionAsync` and provider-specific EF configuration | Atomic processing and close boundaries | Reuse pattern |
| `AttendanceBusinessTimeZoneProvider` | Business-date/timezone decisions | Reuse and later make tenant-configured |

Current read-model facts that constrain the design:

* `AttendanceCalendarDayDto` exposes `AttendanceStatus`, effective `FirstPunchAtUtc`/`LastPunchAtUtc`, worked/expected minutes, roster day type, and variance flags. It does not currently expose a separate adjustment-origin field; Phase 5 must not invent one in the UI without a backend contract.
* `EmployeeAttendanceDay.Status` includes `Present`, `Absent`, `OnLeave`, `OnDuty`, `Holiday`, `WeeklyOff`, `Incomplete`, and `NotProcessed`.
* Approved Regularization replaces the effective first/last values during processing while raw punches remain visible in detail. Approved On Duty projects `OnDuty` while preserving the underlying roster day type.
* `EmployeeEmploymentHistory` uses inclusive `EffectiveFrom`/`EffectiveTo` and stores `ManagerId`; `EmployeeManagerResolver` rejects overlapping records and checks the manager's active employment.
* `Tenant` is catalog-routed by host and contains a provider/shard key. Tenant databases use composite tenant keys and global tenant filters.
* The current timezone implementation returns UTC. Phase 5 should call the provider, not use server-local time; tenant timezone configuration is an extension required before non-UTC deployments.

## 3. Scope and Non-Scope

### Phase 5 scope

* Calendar-month Attendance periods.
* Historical employee population and effective daily coverage.
* Idempotent monthly summary processing.
* Derived unresolved exception read model.
* Close preview, close, reopen, and audit history.
* Authorized admin corrections represented separately from employee Regularization.
* Server-side reports and streamed CSV export.

### Explicitly out of scope

* Payroll, salary, payslip, or statutory calculation.
* Overtime payment, comp-off generation, or payroll posting.
* Billing timesheets.
* Biometric-device/vendor adapters.
* Leave approval, balance, entitlement, or period closing.
* New attendance status semantics such as payroll half-days.

## 4. Core Domain Model

### 4.1 `AttendancePeriod` (new)

One tenant-scoped row identifies one calendar month:

| Field | Type/meaning |
| --- | --- |
| `Id` | `Guid` primary key |
| `TenantId` | tenant scope; composite foreign-key component |
| `Year`, `Month` | canonical identity; `Month` 1–12 |
| `StartDate`, `EndDate` | derived inclusive calendar boundaries, stored for queryability |
| `Status` | `Open`, `Processing`, `ReadyToClose`, or `Closed` |
| `Version` | optimistic concurrency token, integer for provider parity |
| `DataVersion` | monotonically increasing effective-attendance version |
| `ProcessedDataVersion` | source version represented by the latest successful summary build |
| `ProcessedAtUtc` | latest successful processing timestamp |
| `ProcessingRunId` | nullable run identifier for diagnostics/idempotency |
| `CreatedDate`, `ModifiedDate` | existing audit timestamps |
| `CreatedByUserId` | authenticated actor, nullable only for controlled system creation |
| `ClosedAtUtc`, `ClosedByUserId` | last close metadata |
| `ReopenedAtUtc`, `ReopenedByUserId`, `ReopenReason` | last reopen metadata; history remains in events |

Canonical identity is `TenantId + Year + Month`. `StartDate` must be the first day of that month and `EndDate` the last day. A unique index enforces one period per tenant/month.

`Reopened` is not a durable state. Reopen changes `Closed` to `Open` and the event history records every reopen/close cycle.

### 4.2 `AttendancePeriodEvent` (new)

Append-only audit rows:

* `Id`, `TenantId`, `AttendancePeriodId`.
* `EventType`: `Created`, `ProcessingStarted`, `ProcessingCompleted`, `ProcessingFailed`, `ReadyToClose`, `Closed`, `Reopened`.
* `ActorUserId` nullable for a system processor, but never supplied by a client body.
* `OccurredAtUtc`, `ReasonOrComment`, `ProcessingRunId`.
* `PeriodVersion`/`DataVersion` snapshot for diagnosis.

Events are immutable: no update or delete path. The period row is a current-state projection; events are the audit record.

### 4.3 `EmployeeAttendanceMonthlySummary` (new)

One tenant/employee/period row stores the deterministic result of processing:

* identity: `Id`, `TenantId`, `AttendancePeriodId`, `EmployeeId`;
* historical display snapshots: `EmployeeCode`, `EmployeeName` at processing time (IDs remain authoritative);
* `SourceDataVersion`, `ProcessedAtUtc`, `ProcessingRunId`;
* day counts: `CalendarDays`, `WorkingDays`, `PresentDays`, `AbsentDays`, `OnLeaveDays`, `OnDutyDays`, `HolidayDays`, `WeeklyOffDays`, `IncompleteDays`, `NotProcessedDays`;
* workflow/effective counts: `RegularizedDays`, `ApprovedOnDutyDays`;
* variance counts: `LateInCount`, `EarlyOutCount`, `GraceAppliedCount`, `MissingInCount`, `MissingOutCount`, `LeaveConflictCount`, `ExceptionCount`;
* duration measures: `ExpectedWorkMinutes`, `ActualWorkMinutes`;
* `SummaryStatus`/diagnostic message only if processing needs to distinguish a complete row from a failed row.

The unique key is `TenantId + AttendancePeriodId + EmployeeId`. Minutes are attendance measures, not payroll amounts. No half-day, salary, overtime, or comp-off quantity is introduced.

The employee/name snapshots make historical exports stable after later HR changes. IDs remain available for authorization and joins; corrections create a new processed version rather than rewriting history silently.

## 5. Population and Daily Mapping

### 5.1 Historical population

An employee is in a period's population when the employee's effective employment interval intersects the period:

```text
DateOfJoining <= period.EndDate
and (DateOfLeaving is null or DateOfLeaving >= period.StartDate)
```

The implementation must resolve employment history as of each business date, not rely only on today's `Employee.Status`. A person who leaves during the month remains in the historical summary. Dates before joining or after leaving are excluded from `WorkingDays`; they are not `Absent`.

If employment-history records overlap, processing records a blocking `ConfigurationAmbiguity`/`ProcessingError` exception and does not claim the period is ready to close.

### 5.2 Daily source and mapping

The monthly processor reads `EmployeeAttendanceDay` after ensuring the date has been processed through the existing foundation/processor. It reuses resolved `RosterDayType`; it does not recalculate Holiday or WeeklyOff independently.

| `EmployeeAttendanceDay.Status` | Summary counter |
| --- | --- |
| `Present` | `PresentDays` and `WorkingDays` |
| `Absent` | `AbsentDays` and `WorkingDays` |
| `OnLeave` | `OnLeaveDays`; working-day treatment follows the existing day/read-model contract, and is fixed explicitly in Phase 5B tests |
| `OnDuty` | `OnDutyDays` and `ApprovedOnDutyDays` |
| `Holiday` | `HolidayDays` |
| `WeeklyOff` | `WeeklyOffDays` |
| `Incomplete` | `IncompleteDays`, `WorkingDays`, and a blocking exception |
| `NotProcessed` | `NotProcessedDays`, and a blocking exception |

Variance flags (`IsLateIn`, `IsEarlyOut`, `IsGraceApplied`, missing-punch flags, and `LeaveConflict`) increment independent counters. They never replace the primary status.

For `OnLeave`, Phase 5B must codify whether approved leave is included in the organization's working-day denominator. The safe design is to preserve the existing daily/read-model convention and make the denominator a named, tested policy rather than infer it in reports.

## 6. Exception Model

Phase 5 initial exceptions are a derived, server-side read model, not a new lifecycle table. Derivation avoids duplicating facts already held by `EmployeeAttendanceDay` and Phase 4 workflows.

Initial categories:

* `MissingInPunch`, `MissingOutPunch`, `Incomplete` from daily flags/status.
* `NotProcessed` from status/processing outcome.
* `LeaveConflict` from the daily flag.
* `RosterMissing`, `ShiftMissing`, or `ProcessingError` from processor diagnostics.
* `PendingRegularization` for a pending request whose date is in the period.
* `PendingOnDuty` for a pending request whose date range intersects the period.
* `ConfigurationAmbiguity` for overlapping employment or other unresolved effective configuration.

Each exception DTO has a stable derived key such as `(EmployeeId, BusinessDate, ExceptionType)`, source IDs where applicable, blocking severity, and a current message. Arbitrary free text is diagnostic detail, not the category.

Do not persist exceptions until assignment, acknowledgement, escalation, or independent lifecycle is required. If that requirement appears, add `AttendanceException` with immutable occurrence/history and a current projection; do not retrofit arbitrary strings into the summary.

## 7. Monthly Processing

Introduce an application orchestration boundary, proposed as `IAttendanceMonthlyProcessor` and `AttendanceMonthlyProcessor`.

Responsibilities:

1. Get or create the tenant's calendar `AttendancePeriod`.
2. Resolve the historical employee population.
3. Resolve date-effective roster/shift and ensure daily coverage exists.
4. Reuse `EmployeeAttendanceDay` as effective input, including approved Regularization and On Duty.
5. Derive exceptions and classify blocking/non-blocking items.
6. Build deterministic employee summaries and tenant period totals.
7. Upsert summaries transactionally and record a processing event.
8. Set `ProcessedDataVersion` and `Status = ReadyToClose` only when processing completed and the close blockers are known.

Processing is allowed for `Open` and may be rerun for `ReadyToClose`. Processing a `Closed` period requires reopen first. A failed run leaves the period non-closable and records `ProcessingFailed`; it must not report a partially successful final result.

### Idempotency and scale

Use a `ProcessingRunId`, deterministic upsert keyed by tenant/period/employee, and `SourceDataVersion`. Repeating a successful run with unchanged source data produces the same summaries and no duplicate business rows. Repeating `Close` or `Reopen` is a stable conflict/result, not a duplicate event.

For current tenant sizes, one transaction may cover summary replacement. The contract must permit batching: keep the period `Processing`, write a run ID, process employees in bounded batches, and set `ProcessedDataVersion` only after every batch succeeds. A future high-volume implementation can use a staging/run table without changing the public state machine.

## 8. Ready-to-Close and Close/Lock

### 8.1 Blocking rules

`ReadyToClose` requires:

* every applicable employee/date has an effective daily result or an explicitly excluded non-employment date;
* no `NotProcessed`, unresolved processing/configuration error, or blocking incomplete day;
* no Pending Regularization affecting the period;
* no Pending On Duty date intersecting the period;
* all summaries for the current `DataVersion` exist;
* the final blocker check and summary version are from the same transaction/concurrency window.

Blocking exceptions: `NotProcessed`, unresolved `Incomplete`/missing required punch, pending workflows, processing errors, and effective-configuration ambiguity.

Non-blocking exceptions: `LateIn`, `EarlyOut`, `GraceApplied`, and approved/rejected/cancelled workflow history. A business decision may later make some incomplete cases non-blocking, but that is a product policy change, not an implementation shortcut.

### 8.2 Close operation

`POST close` is authoritative and re-runs the blocker check; a preview is advisory only. Close atomically verifies:

1. period is not already closed;
2. period is not processing;
3. summaries match the current `DataVersion`;
4. no blocker exists;
5. status changes to `Closed`;
6. a `Closed` event is appended with actor, timestamp, version, and optional comment.

After close, any operation that could change effective attendance for a date in the period is rejected with a domain conflict until reopen: roster changes affecting the date, reprocessing, Regularization submission, OD submission/approval, and admin correction. The policy is enforced in application services, not only by frontend disabling.

### 8.3 Close preview

Provide a read-only `close-preview` result containing `CanClose`, period status, source/summary versions, blocking and non-blocking exception counts, pending workflow IDs, unprocessed dates, and a bounded list of blockers. `POST close` must repeat the authoritative validation because the preview can become stale.

## 9. Late Punches and Workflow Interaction

Raw punches remain ingestible and immutable after close so evidence is preserved. A late punch must not automatically reprocess or alter a closed period. Ingestion/reprocessing records a period-dirty condition or late-evidence exception. Reopen is required before the punch can affect effective daily attendance and summaries.

Regularization submission for a closed period is rejected; it never silently reopens. An OD request is rejected atomically if any affected date belongs to a closed period. For an OD spanning January 30–February 2, both January and February are blockers while pending, and both must be open before approval can change any date. There is no partial application across an open/closed boundary.

Approved Phase 4 workflow results already reflected in `EmployeeAttendanceDay` are consumed normally. Rejected/cancelled requests do not create effective attendance. Phase 5 does not alter Phase 4 workflow rules.

## 10. Reopen and Reclose

`POST reopen` requires `Attendance.Monthly.Reopen`, a non-empty reason, and an optimistic version. It atomically:

* verifies the period is `Closed`;
* changes status to `Open`;
* increments `Version` and invalidates `ProcessedDataVersion`/summary freshness;
* appends an immutable `Reopened` event with authenticated actor and reason.

Reopen does not delete summaries or events. They become stale and are replaced by the next successful processing run. Corrections then follow: reopen → correction/reprocess → process → clear blockers → close. A second close appends another `Closed` event, preserving every close cycle.

## 11. Admin Corrections

Do not edit raw punches and do not make an admin change look like an employee Regularization. Recommend a separate `AttendanceAdminAdjustment` entity (or a carefully versioned extension only if later code proves the existing `AttendanceAdjustment` can carry origin without ambiguity).

Recommended fields:

* `Id`, `TenantId`, `EmployeeId`, `BusinessDate`;
* nullable effective In/Out values and optional corrected primary status;
* `Reason`, `CreatedByUserId`, `CreatedAtUtc`;
* `Source = AdminCorrection`, immutable sequence/version, and supersession metadata if needed.

Each correction is append-only. The latest valid correction is selected deterministically by sequence/created time; prior corrections remain auditable. `AttendanceAdjustment` remains the origin-specific result of Regularization. The day processor can consume a resolved effective-adjustment abstraction in a later implementation without changing raw evidence.

Admin correction is not allowed directly against a closed period. It requires reopen, correction, reprocess, summary regeneration, and reclose. The permission is distinct from employee request and manager approval permissions.

## 12. Effective Employment and Manager Scoping

The monthly population uses employment-history intersection with the period. Each day uses the history record effective on that business date. No current-status shortcut is allowed.

For manager access, recommend date-accurate union scope: a manager may see an employee's daily/monthly data if the production `EmployeeManagerResolver` resolves that manager for at least one applicable business date in the requested period. Daily rows are authorized against the exact row date. A monthly aggregate must either:

* be presented as a period aggregate for the dates in which the relationship applied, or
* expose the full employee period only when policy explicitly permits a period snapshot.

Phase 5 initial recommendation is the first option for historical correctness. A manager change mid-month must not leak the employee's full month to both managers without a documented policy. HR/Admin permissions can view the tenant population.

## 13. Timezone and Month Boundaries

The period is a `DateOnly` calendar month in the tenant's Attendance business timezone, from day 1 through the month's last day. The processor uses `IAttendanceBusinessTimeZoneProvider` and the established business-date resolver. It never uses the server's local date or UTC date as an implicit month boundary.

The current provider returns UTC; before multi-timezone production use, add tenant timezone configuration through the existing tenant settings path and test DST transitions. Stored punch timestamps remain UTC; period and business-date comparisons remain `DateOnly`.

## 14. Freshness, Dirty Periods, and Concurrency

### 14.1 Recommended freshness strategy

Use an explicit tenant/period `DataVersion` plus `ProcessedDataVersion`. Any operation that changes effective AttendanceDay state for a period increments `DataVersion` in the same transaction: punch processing/reprocessing, approved Regularization, approved OD, roster/shift changes affecting a past day, and admin correction. A summary is current only when its `SourceDataVersion` equals the period's `DataVersion`.

Because legacy or future mutation paths may be added, close also performs a defensive reconciliation over applicable daily `ModifiedDate`/processing state and pending workflows. A mismatch marks the period dirty and prevents close. This is a correctness backstop, not a replacement for transactional version increments.

### 14.2 Concurrency matrix

Use the period `Version` as an optimistic concurrency token and revalidate inside the transaction:

| Race | Required result |
| --- | --- |
| process vs process | one run wins the version; other retries or returns a stable conflict |
| close vs process | close cannot commit stale summaries; one operation retries/fails safely |
| close vs workflow approval | approval cannot change a closed period; close rechecks pending/effective state |
| reopen vs reopen | one succeeds; the other gets already-open/concurrency conflict |
| reopen vs close | version conflict; no duplicate or contradictory event |
| correction vs close | correction is rejected while closed or close loses the version race |

Do not rely on UI disabled buttons. SQL Server rowversion is not assumed because MySQL and SQLite must behave equivalently; use an integer token with provider-safe configuration, following Phase 4's concurrency pattern.

### 14.3 Transaction boundary

For close, use one transaction for final blocker read, summary freshness check, period status/version update, and `Closed` event insert. Workflow approval/reprocessing must update the affected daily rows and period `DataVersion` in the same transaction. If database-scale processing cannot fit one transaction, use a processing run/staging strategy and allow close only after the run's final version is committed.

## 15. Tenant Isolation and Delete Behavior

Every proposed row is tenant-scoped and uses the repository's composite `(TenantId, Id)` foreign-key convention:

* `AttendancePeriods`: tenant + canonical month unique index.
* `EmployeeAttendanceMonthlySummaries`: tenant + period + employee unique index.
* `AttendancePeriodEvents`: tenant + period + occurred-time index.
* `AttendanceAdminAdjustments`: tenant + employee + business date + sequence indexes.

All queries include tenant scope through the existing context/filter and explicit predicates where security-sensitive. Foreign IDs are never accepted without the current tenant. Historical rows use restrictive deletes; deleting or changing an Employee/User must not cascade-delete finalized summaries or audit events. Employee master deletion should be prohibited or handled as an HR archival operation.

## 16. Authorization

Proposed permission names follow the existing `Resource.Action` convention:

* `Attendance.Monthly.ViewSelf`
* `Attendance.Monthly.ViewTeam`
* `Attendance.Monthly.ViewAll`
* `Attendance.Monthly.Process`
* `Attendance.Monthly.Close`
* `Attendance.Monthly.Reopen`
* `Attendance.AdminCorrection.Manage`
* `Attendance.Exception.View`
* `Attendance.Report.View`
* `Attendance.Report.Export`

Permissions are seeded through the existing permission/role infrastructure in the implementation phase. Authorization must combine permission with scope: employee self, effective direct-report relationship for the row/date, or explicit HR/Admin permission. The actor is always derived from the authenticated tenant user; no actor ID is accepted in a command body.

## 17. API Design

These are proposed route groups using the existing `api/attendance`, `Result<T>`, ProblemDetails, and `PagedResult<T>` conventions:

| Endpoint | Purpose / permission |
| --- | --- |
| `GET /api/attendance/periods` | paged period list; monthly view permission |
| `POST /api/attendance/periods/{id}/process` | idempotent processing; Process |
| `GET /api/attendance/periods/{id}` | period state/metadata; monthly view |
| `GET /api/attendance/periods/{id}/close-preview` | read-only blocker preview; Close or view permission |
| `POST /api/attendance/periods/{id}/close` | authoritative close; Close |
| `POST /api/attendance/periods/{id}/reopen` | mandatory reason; Reopen |
| `GET /api/attendance/periods/{id}/summary` | paged employee summaries; self/team/all scope |
| `GET /api/attendance/periods/{id}/exceptions` | paged derived exceptions and filters |
| `GET /api/attendance/reports/monthly` | filtered monthly summary read model |
| `GET /api/attendance/reports/daily-register` | daily effective register |
| `GET /api/attendance/reports/regularizations` | Phase 4 request/audit report |
| `GET /api/attendance/reports/on-duty` | Phase 4 On Duty report |
| `GET /api/attendance/reports/{report}/export.csv` | authorization-checked streamed CSV |
| `POST /api/attendance/admin-adjustments` | authorized correction while period is open |

`AttendanceMonthlySummaryDto` should expose employee identity snapshot, period, counts, minutes, exception count, and period status. It must not expose internal concurrency or connection details. All list/report endpoints are server-paged and use a common filter contract.

## 18. Reporting and Export

Initial reports:

1. Monthly Attendance Summary.
2. Daily Attendance Register.
3. Exception Report.
4. Late In / Early Out Report.
5. Absence Report.
6. On Duty Report.
7. Regularization Report.

Leave remains a status/read-model input; Phase 5 does not create a duplicate Leave report or change Leave data. Report filters include period/date range, employee, department, location, manager, status, and exception type where applicable. Organizational filters resolve against date-effective employment, not only current employee attributes.

CSV is the initial export format. The server reuses report authorization and filters, streams rows, enforces tenant scope, and escapes values beginning with spreadsheet formula characters (`=`, `+`, `-`, `@`) when generating CSV. Do not add an Excel dependency for Phase 5A. Large reports must not materialize the whole tenant dataset in memory.

## 19. SQL Server and MySQL Persistence

Proposed tables are provider-neutral EF Core entities:

1. `AttendancePeriods` — unique `(TenantId, Year, Month)`; indexes on `(TenantId, Status)` and `(TenantId, StartDate, EndDate)`.
2. `AttendancePeriodEvents` — `(TenantId, AttendancePeriodId, OccurredAtUtc, Id)` index; restrictive FK to period.
3. `EmployeeAttendanceMonthlySummaries` — unique `(TenantId, AttendancePeriodId, EmployeeId)`; indexes by `(TenantId, AttendancePeriodId, ExceptionCount)`, employee, and source version.
4. `AttendanceAdminAdjustments` — only when Phase 5D is implemented; unique sequence/lookup per tenant, employee, date.

Use `Guid`, `DateOnly` with the existing provider converters, `DateTime` UTC, integer counts/minutes, and nullable values where a daily measure is absent. Avoid computed columns, provider-specific SQL, filtered-index syntax, or database-specific enum behavior unless the existing migration generator has a tested equivalent. SQL Server and MySQL migrations must be generated together and validated against both model snapshots.

Do not add a migration in Phase 5A. Migration ordering later should be: period/event tables, summary tables, permissions/seeds, then admin-adjustment tables only when that feature is implemented.

## 20. Error Model

Map domain outcomes through the existing `Result<T>`/ProblemDetails convention:

* `PeriodNotFound`
* `PeriodAlreadyClosed`
* `PeriodNotOpen`
* `PeriodProcessing`
* `BlockingExceptionsExist`
* `PendingWorkflowExists`
* `SummaryStale`
* `ConfigurationAmbiguity`
* `ConcurrencyConflict`
* `UnauthorizedReopen`
* `InvalidReopenReason`
* `EffectiveDateOutsideEmployment`
* `ClosedPeriodMutation`
* `ProcessingFailed`

Responses must not reveal another tenant's employee/request existence. A foreign identifier may safely produce the repository's 403/404 convention. Validation errors remain field-specific where useful; expected domain conflicts are not 500s.

## 21. Integration Boundaries

### Phase 1–2

Monthly processing consumes resolved roster/shift context and the processed `EmployeeAttendanceDay`. Punch ingestion and daily processing remain separate responsibilities. A raw punch can make a period dirty only through the normal daily processing/version path.

### Phase 3

Calendar and manager views remain daily read models. Phase 5 reports reuse the effective daily result and must preserve roster Holiday/WeeklyOff and attendance status precedence.

### Phase 4

Approved Regularization and On Duty effects are inputs. Pending requests block close; rejected/cancelled requests do not. Phase 5 must add closed-period guards at the command boundary without changing Phase 4 authorization semantics.

### Leave

Attendance reads approved Leave-derived daily state. It does not modify Leave requests, balances, entitlements, approvals, or close state. If Leave changes before Attendance close, the existing integration reprocesses the affected day and increments the period data version.

### Future Payroll/Overtime/Comp-Off

Monthly counts/minutes are attendance facts only. Payroll may consume a finalized export/read model later, but Phase 5 does not calculate pay, overtime eligibility/payment, comp-off, or posting transactions. No payroll identifiers or monetary fields belong in these tables.

## 22. Test Strategy

Future implementation must include:

* unit tests for month boundaries, daily mapping, population intersection, exception severity, CSV escaping, and idempotent summary calculation;
* service tests for processing, close preview, authoritative close, reopen, reclose, stale summary, pending Regularization/OD, cross-month OD, late punch, and admin correction rules;
* HTTP tests for authentication-derived actors, permission/scope, employee self access, effective manager changes, tenant isolation, foreign IDs, pagination, ProblemDetails, close/reopen, and zero side effects on rejected commands;
* SQL Server and MySQL integration tests for migrations, DateOnly, unique keys, transactions, concurrency tokens, and provider parity;
* concurrency tests for process/process, process/close, workflow approval/close, reopen/reopen, correction/close, and retry after transient failure;
* historical tests for join/leave mid-month, manager changes, roster overrides, Holiday/WeeklyOff, Leave read-only behavior, and DST/business timezone boundaries;
* summary idempotency and stale-version tests, including a change after processing but before close;
* report/export tests for tenant scope, effective manager scope, server paging, stable snapshots, formula injection, and streaming behavior.

Failed processing/close must leave no false `Closed` status, no duplicate event, and no partially accepted final summary. Denied commands must have zero side effects.

## 23. Implementation Sequence

### Phase 5B — Period, summary, and derived exceptions

Add the period/event and monthly-summary entities, configurations, migrations for SQL Server/MySQL, permissions, processor service, population/mapping rules, period data-version hooks, and read-only summary/exception APIs. No close/reopen mutation yet beyond internal state needed by processing.

### Phase 5C — Close, lock, and reopen

Add close preview, authoritative close transaction, closed-period command guards, reopen with mandatory reason, event history, concurrency tests, and recclose behavior.

### Phase 5D — Admin corrections and reprocessing

Add append-only admin corrections, effective-value resolution, reopen-required workflow, reprocessing/version invalidation, and audit/read APIs.

### Phase 5E — Reports and CSV export

Add paged report queries, effective manager filters, HR/Admin scope, streamed CSV, formula escaping, and report authorization.

### Phase 5F — Frontend and final regression

Add period dashboard, exception/close preview, close/reopen/correction UX, report/export UI, frontend error states, SQL Server/MySQL regression, and full acceptance audit.

## 24. ADR-Style Decisions

| Decision | Chosen approach | Alternatives considered | Reason |
| --- | --- | --- | --- |
| Month identity | Calendar month, tenant + year + month | Payroll cycles; date-range identity | Required Phase 5 scope and simple canonical uniqueness |
| State model | `Open -> Processing -> ReadyToClose -> Closed`; reopen returns to Open | Durable `Reopened`; no Processing state | Small state machine with explicit operational state and append-only history |
| Summary storage | Persist one deterministic row per employee/period | Recompute every report; raw daily aggregation at query time | Stable historical export, bounded report cost, clear freshness contract |
| Exceptions | Derived initially | Persist every exception now | Existing daily/workflow facts already provide source; avoid unnecessary lifecycle table |
| Close semantics | Lock all effective-attendance mutations until privileged reopen | Silent post-close changes; auto-reopen | Protects finalized truth and makes corrections auditable |
| Reopen | Permission + mandatory reason + event + optimistic concurrency | Admin flag toggle | Prevents silent historical changes |
| Late punches | Preserve/ingest raw evidence; no closed-period reprocessing | Reject punch; silently alter close | Preserves evidence without changing finalized results |
| Admin correction | Separate append-only origin from Regularization | Edit raw punch; overload employee adjustment | Maintains provenance and raw evidence |
| Population | Employment interval intersects period; resolve each date | Current active employees only | Historical correctness for joiners/leavers |
| Manager scope | Date-effective union with row-date authorization | Current manager; period-end snapshot | Avoids historical leakage across manager changes |
| Freshness | Period `DataVersion` + summary source version plus close reconciliation | Timestamp only; manual dirty flag | Deterministic and race-resistant across providers |
| Concurrency | Integer optimistic token and transactional revalidation | UI-only locks; SQL Server-only rowversion | Provider parity and server-side safety |
| Export | Authorized streamed CSV first | Excel/PDF; client-side export | Small dependency surface and scalable output |
| Holiday/weekly off | Consume effective daily model | Recalculate in monthly service | Keeps one roster/calendar authority |
| Leave | Read-only daily status integration | Duplicate Leave calculations | Preserves ownership and avoids balance side effects |

## 25. Open Product Decisions

These are the only decisions that should be confirmed before implementation; the technical architecture does not depend on inventing additional rules.

1. **Are approved Leave days included in `WorkingDays`?** Recommended default: exclude them from the working-day denominator while counting them separately as `OnLeaveDays`, matching common attendance reporting. Impact: one mapping/policy test and report labels.
2. **Should an incomplete day always block close?** Recommended default: yes when required punches are missing; allow an explicit HR/Admin override only as a separately audited future policy. Impact: blocker severity configuration and close preview wording.
3. **What should a manager see for an employee whose manager changes mid-month?** Recommended default: date-effective daily rows and an aggregate limited to dates of the relationship. Impact: summary query shape and UI explanation.
4. **What tenant timezone settings are required for first Phase 5 production rollout?** Recommended default: retain UTC until an existing tenant setting is approved, then configure the existing timezone provider before enabling local-time month boundaries. Impact: tenant settings migration and DST tests.
5. **Are correction approvals required beyond the authorized admin actor?** Recommended default: no second approval in initial Phase 5; every correction requires explicit permission, reason, and audit event. Impact: if changed, add a correction workflow rather than altering period semantics.

## 26. Design Review Checklist

* Existing daily Attendance remains authoritative: yes.
* Raw `AttendancePunch` remains immutable: yes.
* Phase 4 Regularization/On Duty are consumed, not duplicated: yes.
* Leave is read-only: yes.
* Tenant scope and authenticated actor are mandatory: yes.
* SQL Server and MySQL use provider-neutral persistence: yes.
* Close is authoritative and revalidates blockers: yes.
* Reopen is explicit, permissioned, reasoned, and audited: yes.
* Processing is repeatable and stale summaries cannot close a period: yes.
* Payroll/overtime/comp-off are excluded: yes.
* Phase 5A adds no production code, frontend code, or migration: yes.
