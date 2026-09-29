# Module 8 — Attendance Management (Foundation/Configuration, Self-View, Punch/Event Ingestion, Daily
Derivation, Monthly Processing, Exceptions, Regularization, On Duty, Overtime, Admin Corrections,
Manager/HR Scope, Reports, Devices/Ingestion, Leave/Calendar Integration, Concurrency/Provider Parity,
Authorization/Tenant Security, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py`
regenerates `qa/HRMS_Test_Cases.xlsx` across all eight modules with no errors). No automation implemented
yet (Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (338 functionalities: F-ATT-001…338). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-foundation-config.yaml` … `cases/17-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet (17 source files → 17 worksheets, one-to-one — Attendance did not need to share sheets across files the way Leave did). |
| `clarifications.yaml` | The CR-91…CR-128 QA-risk register for this module, plus re-verified (not merely restated) cross-references to CR-53 (Module 5), CR-57 (Module 5/6 — Attendance is the *origin*, not a downstream consumer, see below), CR-66 (Module 6), CR-77 (Module 5/6/7), and CR-82 (Module 7) that this module's own code confirms or reframes directly. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## The real implementation (source-verified across 8 parallel deep-dive research passes, not assumed from the brief's example list)

Discovered by reading all 9 Attendance-family controllers (`AttendanceFoundationController`,
`AttendanceReadController`, `AttendanceWorkflowController`, `AttendanceOperationsController`,
`AttendanceAdminCorrectionsController`, `AttendanceMonthlyController`, `AttendanceReportController`,
`AttendanceDevicesController`, `OvertimeController`); every Application-layer Attendance service (foundation/
roster resolution, punch ingestion, daily derivation, monthly processing/close/reopen, period locking, admin
corrections, payroll-snapshot resolution, regularization/on-duty workflow, overtime, operations/exceptions,
reports, device integration/lease/sync-recovery, and the Attendance-specific authorization service); every
Domain entity, EF configuration, and enum under the Attendance/Overtime/Device surface; both migration chains;
`Permissions.cs`/`SeedData.RolePermissionMap`'s full Attendance/Overtime/Device slice; every Attendance/
Overtime/Device/Comp-Off frontend page and API client; and the full existing xUnit (~150 test classes,
including nested classes discovered via targeted grep, not just top-level files) and Vitest inventory
touching this surface. `docs/attendance-phase-2-punch-processing.md`, `docs/phase-6b-attendance-monthly-
finalization-payroll-integration.md`, and `docs/phase-6g-attendance-device-production-runbook.md` were read
where they exist and current code wins wherever they diverge, per `CLAUDE.md` — Attendance Foundation/
Configuration itself has **no design doc at all**, only source and tests.

**Attendance is the origin of CR-57, not a carried-forward consumer (the single most important framing
correction this module makes).** Prior modules described `EmployeeAccessScopeService`'s self+direct-report
narrowing as an "Attendance-permission-keyed" coupling without identifying which module actually owns that
keying. This module confirms directly: `EmployeeAccessScopeService.BuildPredicateAsync`'s narrowing decision
is keyed literally off `Permissions.Attendance.MonthlyViewTeam`/`MonthlyViewAll` — Attendance's own permission
constants — and Attendance's own `AttendanceAuthorizationService.BuildEmployeePredicateAsync` is a first-class,
direct caller of `EmployeeAccessScopeService.BuildRoleScopePredicateAsync` (the sibling method), not merely an
affected downstream module. See `CR-57` in `clarifications.yaml` for the full re-framing.

**Attendance has its OWN, 5th independent "who can see/act on whose employees" mechanism (CR-77 recurrence),**
distinct from `RoleScopeResolver` (RBAC), `EmployeeAccessScopeService.BuildPredicateAsync` (Employee/CR-57
origin), and `LeaveAuthorizationService` (Leave/CR-77) — `AttendanceAuthorizationService` shares genuine code
reuse with Leave ONLY at the role-scope dimension (both call `BuildRoleScopePredicateAsync`); its
manager-relationship arm is independently reimplemented (`BuildCurrentManagerPredicate`, a textually distinct
method from Leave's own method of the same name in a different file), and it internally uses a SECOND,
different manager-check implementation (`IEmployeeManagerResolver`) inside `CanAccessEmployeeAsync` — two
different "is this my manager" implementations coexist within the one service depending on the entry point.
`AttendanceWorkflowService`'s optional-DI null fallback independently reproduces Leave's own CR-83-style gap
(zero role-scope authorization when `IAttendanceAuthorizationService` is null) — see `CR-124`.

**CR-53 has a genuinely two-part story in Attendance, and Attendance is the first module in the carry-forward
chain whose own business logic reacts to `Employee.Status` directly, not just `User.IsActive`.**
`AttendanceWorkflowService.SubmitRegularizationAsync`/`SubmitOnDutyAsync` check `employee.Status !=
EmployeeStatus.Active` directly and block submission for ANY business date the instant Status changes — a
real, working, self-contained eligibility gate, distinct from every other module's pure read-side CR-53
finding. But everything else about CR-53 still holds: `AttendanceDayProcessor`, `AttendanceMonthlyProcessor`,
history reads, and manager-queue visibility never check `Employee.Status` at all, and a manager can still
approve/reject a pending request from a since-separated employee (`CR-123`). See `CR-53` in
`clarifications.yaml` for the full evidence trail, including the confirmed side-channel catch this module
provides that Leave's own gate does not.

**The single highest-density finding cluster in this module is the seeded permission matrix (CR-111 through
CR-114).** No seeded role except SuperAdmin/TenantAdmin holds `Regularization.Request/Approve`,
`OnDuty.Request/Approve`, `AdminCorrection.Manage`, or any `Attendance.Overtime.*` permission — the entire
correction/approval/overtime workflow is operationally SuperAdmin/TenantAdmin-only in the seeded matrix.
Separately, a plain seeded `Employee` holds `MonthlyViewSelf`/`ExceptionViewSelf`/`CompOffViewSelf` but **not**
`Attendance.View` — so `GET /api/attendance/me/calendar`, `/me/days/{date}`, and `/me/exceptions` (all three
gated on plain `Attendance.View`) are unreachable by the seeded Employee role at all, and the seeded `Manager`
role has the mirror-image gap for `manager/team` reads.

**CR-82 recurs verbatim in Attendance's own controller.** `GET /api/attendance/comp-off/operations` has no
`[HasPermission]` attribute — only controller-level `[Authorize]` — the exact pattern Module 7 flagged for
Leave, confirmed present because this is literally the same shared controller action.

**One genuinely race-susceptible boundary was found in an otherwise consistently DB-constrained module
(CR-110).** Every other race-sensitive path in Attendance (Monthly summaries, Period close, Admin corrections,
Workflow transitions, Overtime finalization, Device lease/mapping) has a DB-level unique index as the ultimate
backstop. The Comp-Off/Overtime `DoubleBenefitDenied` exclusivity check is a pre-commit `AnyAsync` application
check with no backing constraint, and no dedicated concurrent-race test was found proving two simultaneous
"earn Comp-Off" and "create OT" calls against the identical attendance day cannot both slip through.

**Frontend confirms two backend gaps and adds several of its own.** The Overtime "Team Overtime" Approve/
Reject buttons render (correctly permission-gated) but have **no `onClick` handler at all** — matching the
backend finding that no team/self Overtime list endpoint exists at all (`CR-105`). Two routes
(`/attendance/overtime`, `/attendance/comp-off/operations`) are fully functional and tested but have **zero
navigation entries or in-app links** (`CR-126`). The Applicability Rules table's Edit/Delete buttons render
**unconditionally with no permission gate at all**, the clearest confirmed instance of the "full mutating UI
regardless of granted permissions" anti-pattern (CR-44/CR-88 style) found in this module (`CR-125`). The
Monthly Finalization page's periods-table pagination control is a confirmed **dead no-op**
(`onPageChange={() => undefined}`) — untested because no existing fixture seeds enough period history to even
render it (`CR-128`).

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Foundation/Config (Shift, Pattern, Applicability, Roster) | Full CRUD, uniqueness, validation ordering, effective dating, period-lock coverage inconsistency | GET-drops-conditions gap (CR-91), inactive-default-still-blocks (CR-91), roster-upload-stuck-at-Validated (CR-92) | No design doc exists for this whole area at all |
| Self-View / Manager Team | me/calendar, me/days, manager/team, manager/team/{id}/{date}, identity anti-spoofing | Extreme year/month boundaries, DI-dependent 404-vs-403 (CR-100) | Two different permissions gate the "daily vs monthly" self-view pair (CR-111) |
| Punch/Event Ingestion | Device sync/import, idempotency, source-check, batching | Overnight backdating heuristic, UTC-hardcoded timezone (CR-97) | No self-punch endpoint exists at all (CR-96) |
| Daily Derivation | Full status decision tree, punch-pairing algorithm, worked-example boundaries | Regularization/correction silencing raw-punch flags (CR-99) | Status has exactly 8 values, no Late/EarlyOut status |
| Monthly Processing | Full period lifecycle, close/reopen, payroll snapshot versioning, exceptions | Permission/scope mismatch on exceptions route (CR-102), no ProcessingFailed audit event (CR-103) | No pessimistic locking anywhere (CR-101) |
| Exceptions | Operations queue, dashboard, bulk actions, resolve, self-view split | SQL-side authorization proven at scale before paging | Self-view gated on wrong permission for seeded Employee (CR-111) |
| Regularization | Submission, approval/rejection, cancellation, effective-manager-as-of-date | No-attendance-record allowed, clean-Present-day blocked | Maker-checker shared with HR manual-correction path |
| On Duty | Multi-day submission, every-date manager authority, approval-time Leave overlap | Submission-time overlap only checks own Pending (CR-106) | No adjustment-equivalent entity exists for OD |
| Overtime | Policy, request/approve/reject/correct, finalization, Comp-Off exclusivity | ActualEligibleMinutes never recomputed (CR-108), no cancel endpoint (CR-107) | No team/self view endpoint exists at all (CR-105) |
| Admin Corrections | Append-only versioning, precedence over Regularization, closed-period block | Default-scope-date-is-today gap (CR-104) | Immutable by design, confirmed at the DB layer |
| Manager/HR Scope | Full AttendanceAuthorizationService mechanism deep-dive, role x permission matrix | Null-DI fallback fully permissive (not merely degraded) | Attendance is CR-57's origin, a 5th CR-77-style mechanism |
| Reports | Daily/Monthly/Exceptions, export permission matrix, CSV injection hardening | In-memory derivation+pagination for Exceptions report only (CR-115) | Manager lacks Report.View entirely |
| Devices/Ingestion | Full CRUD/mapping/lease/sync-recovery/worker, dual idempotency keys | No SerialNumber uniqueness (CR-120), credential can't be cleared (CR-116) | Zero HTTP-level permission tests exist (CR-119) |
| Leave/Calendar Integration | Leave push-vs-read integration, Separation walkthrough, Holiday/WeeklyOff precedence | Half-day/month-boundary leave untested combination, Holiday-ignored-by-applicability-rule (CR-121) | EmployeeSupervisor never used by Attendance's primary path |
| Concurrency/Provider | Cross-module race inventory, MySQL/SQL Server-gated cases | DoubleBenefitDenied is the one unconstrained race (CR-110) | Close-vs-Correction race lacks real-provider test coverage |
| Authz/Tenant Security | Consolidated matrix, unauthenticated sweep, forged-token rejection | Scope-vs-permission 404-vs-403 distinction proven across 4 endpoints | Overtime's cross-tenant failure is 401, not 403/404 |
| Frontend | Loading/empty/error across all 15 pages, permission-gated actions, orphan routes | 3 pages with single-smoke-test coverage flagged explicitly | Devices page is the best-practice reference; Applicability table is the worst |

## Sheets produced for this module

| Sheet | Cases | Steps | Focus |
|---|---:|---:|---|
| Attendance_Config | 79 | 163 | Shift/Pattern/Applicability CRUD, Roster assign/query/upload, Calendar overrides, Effective dating |
| Attendance_Self | 19 | 32 | Self-view calendar/day, identity resolution, Manager team read |
| Attendance_Events | 17 | 30 | Device-originated punch ingestion, idempotency, business-date/timezone resolution |
| Attendance_Daily | 27 | 61 | AttendanceDayProcessor status decision tree, punch-pairing algorithm |
| Attendance_Monthly | 39 | 78 | Period lifecycle, close/reopen, period lock, payroll snapshot |
| Attendance_Exceptions | 21 | 38 | Operations exception queue, dashboard, bulk actions, resolve |
| Attendance_Regularization | 24 | 44 | Submission, approval workflow, cancellation |
| Attendance_OnDuty | 21 | 45 | Multi-day submission, approval workflow, cancellation |
| Attendance_Overtime | 32 | 65 | Policy, request lifecycle, finalization, Comp-Off exclusivity |
| Attendance_Admin | 18 | 43 | HR/Admin corrections, append-only versioning |
| Attendance_Scope | 18 | 46 | AttendanceAuthorizationService deep dive, role x permission matrix |
| Attendance_Reports | 14 | 26 | Daily/Monthly/Exceptions reports, export permission matrix |
| Attendance_Devices | 43 | 94 | Device CRUD, mapping, ingestion/replay, lease, sync recovery, worker |
| Attendance_Integration | 28 | 70 | Leave<->Attendance, Separation<->Attendance, Holiday/WeeklyOff, CR carry-forward |
| Attendance_Concurrency | 12 | 25 | Cross-cutting races, SQL Server/MySQL provider-parity matrix |
| Attendance_Security | 8 | 34 | Consolidated authorization matrix and tenant-isolation sweep |
| Attendance_Frontend | 35 | 55 | Page-level loading/empty/error/permission-gated behavior |

**Total: 455 test cases, 949 steps, 338 functionalities** (`python qa/tools/build_test_catalogue.py`
regenerates and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they
ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-91**: Applicability rule GET responses drop 10 of ~16 supported dimension conditions.
- **CR-92**: A Roster Upload batch can get permanently stuck at `Validated` with no repair path.
- **CR-93**: Frontend permission-gating for Foundation/Shift-Pattern is inconsistent (wrong constant / entirely absent).
- **CR-94**: Deactivating a Shift doesn't invalidate existing references.
- **CR-95**: Applicability Update skips the validation Create performs.
- **CR-96**: No employee self-punch endpoint exists anywhere.
- **CR-97**: Tenant business timezone is hard-coded to UTC for every tenant.
- **CR-98**: Self-view/punch/day-processing never check employment/separation status.
- **CR-99**: Approved corrections silence raw-punch flags without touching the raw data shown.
- **CR-100**: Manager-day denial is 404 or 403 depending on DI wiring.
- **CR-101**: No pessimistic locking anywhere in Monthly/PeriodLock/AdminCorrection/PayrollSnapshot.
- **CR-102**: `/periods/{id}/exceptions` permission and internal scope-resolution permission diverge.
- **CR-103**: A failed monthly-processing attempt leaves zero audit trail.
- **CR-104**: Admin correction list's default scope date is "today," not the queried history.
- **CR-105**: No Overtime team/self view endpoint exists; frontend approve/reject buttons are dead.
- **CR-106**: On Duty submission-time overlap check is incomplete.
- **CR-107**: No Overtime cancel/withdraw endpoint exists.
- **CR-108**: Overtime's eligible minutes are never recomputed at approval time.
- **CR-109**: Overtime Category is never cross-checked against the actual resolved day type.
- **CR-110**: The one genuinely race-susceptible boundary — Comp-Off/Overtime double-benefit exclusivity.
- **CR-111**: `Attendance.View` gates self/manager routes the seeded Employee/Manager roles don't hold.
- **CR-112**: The entire correction/approval workflow is SuperAdmin/TenantAdmin-only in the seeded matrix.
- **CR-113**: SuperHR and TimeManager hold far less than their names imply.
- **CR-114**: No seeded role holds any Overtime permission.
- **CR-115**: The Exceptions report derives/paginates in memory, unlike Daily/Monthly.
- **CR-116**: A device's credential reference can never be cleared via Update.
- **CR-117**: Two independent ingestion idempotency keys (receipt vs. authoritative punch).
- **CR-118**: Manual device import bypasses the sync lease system entirely.
- **CR-119**: Zero HTTP-level permission tests exist for the 17 device routes.
- **CR-120**: Device `SerialNumber` has no uniqueness constraint.
- **CR-121**: Holiday is silently ignored once a Shift/Pattern applicability rule matches.
- **CR-122**: Approved On Duty silently overwrites a Holiday/WeeklyOff day's Status.
- **CR-123**: A manager can approve a pending request from a since-separated employee.
- **CR-124**: The Attendance Workflow legacy/null-DI fallback has zero role-scope authorization.
- **CR-125**: Applicability Rules table Edit/Delete render with no permission gate at all.
- **CR-126**: Two frontend routes (Overtime, Comp-Off Operations) have zero nav entries.
- **CR-127**: Regularization form only supports OUT-punch correction; Reject sends a hardcoded comment.
- **CR-128**: The Monthly Finalization periods-table pagination control is a dead no-op.

`clarifications.yaml` also carries re-verified (not merely restated) cross-references to **CR-53** (confirmed
two-part: a genuine write-path gate plus the same read-side exposure every other module has), **CR-57**
(Attendance confirmed as the ORIGIN, not a downstream consumer), **CR-66** (confirmed reachable for every
Attendance/Overtime/Device permission with no carve-out), **CR-77** (Attendance's own 5th independent
mechanism), and **CR-82** (the identical `CompOffController.Operations` gap, since Comp-Off Operations is a
shared controller action between the Leave and Attendance functional areas).

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a product
decision, per the classification column in `clarifications.yaml`.

## Existing automated coverage referenced by this module

Attendance has by far the largest existing xUnit inventory of any module reviewed so far in this catalogue —
roughly 150 test classes (including nested classes discovered only via targeted grep against stale `.trx`
result names, e.g. `AttendanceMonthlyFinalizationConcurrencyTests`/`AttendancePayrollCorrectionEndToEndTests`
living as nested classes inside `AttendanceMonthlyFinalizationAcceptanceTests.cs`, and
`AttendanceOperationsMissedPunchEndToEndTests` living inside `AttendanceOperationsFunctionalLifecycleTests.cs`).
Given this scale — larger than Leave's own 58+-class inventory that Module 7 already found impractical to
100%-verify per-case — this module follows the identical precedent Module 7 set: **no case in this module
cites an `ex:` existing-test reference.** Every existing test class discovered during the eight parallel
research passes is instead documented narratively, per topic area, inside each research pass's own notes and
summarized in the coverage matrix above and the completion report below; `qa/tools/existing_test_layers.yaml`
was **not** extended for this module (no `ex:` references exist that would require validating against it).
This is a deliberate scope decision, consistent with Module 7's own precedent and explicitly permitted by
`qa/tools/build_test_catalogue.py`'s validation (which only requires `ex:` references to resolve when they are
actually used).

The full class inventory discovered (not individually cited via `ex:`, but fully read/classified during
research and referenced by name inside the relevant `src:` fields and clarification entries throughout this
module) spans, by area:

- **Foundation/Roster**: `AttendanceFoundationTests`, `AttendanceApplicabilityCrudTests`,
  `AttendanceApplicabilityEndpointTests`, `AttendanceDefaultShiftTests`, `AttendanceShiftApplicabilityTests`,
  `AttendanceRosterAuthorizationEndpointTests`, `AttendanceRosterCalendarOverrideTests`,
  `AttendanceRosterQueryTests`, `AttendanceRosterUploadClassificationTests`, plus six `MySqlAttendance*`
  provider-acceptance classes.
- **Self-view/Punch/Daily**: `AttendanceReadServiceTests`, `AttendancePunchProcessingTests`,
  `AttendanceHttpEmployeeHarnessTests`, `AttendancePhase4HttpOwnershipTenantTests`,
  `MySqlEmployeeAttendanceDayIntegrationTests`, `MySqlAttendancePunchIntegrationTests`.
- **Monthly/Admin/Payroll**: `AttendanceMonthlyProcessorTests`, `AttendanceMonthlyHttpTests`,
  `AttendanceMonthlyFinalizationAcceptanceTests` (with its 4 nested test classes plus
  `AttendanceMonthlyFinalizationLargeDataTests`), `AttendanceAdminCorrectionTests`,
  `AttendanceAdminCorrectionHttpTests`, `AttendancePeriodLockTests`, `AttendancePayrollIntegrationTests`,
  `AttendancePayrollEndToEndTests`, `AttendancePayrollCorrectionEndToEndTests`,
  `MySqlAttendanceMonthlyIntegrationTests`, `MySqlAttendanceAdminCorrectionIntegrationTests`.
- **Regularization/On Duty/Overtime**: `AttendanceWorkflowTests`, `AttendanceHttpWorkflowHarnessTests`,
  `AttendanceWorkflowAuthorizationHttpTests`, `CompOffOvertimeExclusivityTests`, `OvertimeAcceptanceTests`,
  `OvertimeConcurrencyTests`, `OvertimeFailureInjectionTests`, `OvertimeLargeDataTests`,
  `OvertimePayrollCorrectionEndToEndTests`, `OvertimePayrollEndToEndTests`, `OvertimePayrollIntegrationTests`,
  `OvertimePermissionSeedTests`, `OvertimeSecurityTests`, `MySqlAttendanceWorkflowIntegrationTests`,
  `MySqlOvertimeIntegrationTests`, `SqlServerOvertimeIntegrationTests`.
- **Operations/Reports/Scope**: `AttendanceOperationsConcurrencyTests`, `AttendanceOperationsDashboardTests`,
  `AttendanceOperationsExportTests`, `AttendanceOperationsFailureInjectionTests`,
  `AttendanceOperationsFunctionalLifecycleTests` (with 3 nested classes), `AttendanceOperationsHttpTests`,
  `AttendanceOperationsLargeDataTests`, `AttendanceOperationsSecurityTests`, `AttendanceReportHttpTests`,
  `AttendanceReportTests`, `MySqlAttendanceOperationsProviderWrapper`,
  `SqlServerAttendanceOperationsIntegrationTests`, `MySqlAttendanceReportIntegrationTests`.
- **Devices/Ingestion**: `AttendanceDeviceConcurrencyTests`, `AttendanceDeviceIntegrationTests`,
  `AttendanceDeviceLargeDataTests`, `AttendanceDeviceLeaseTests`, `AttendanceDeviceOperationsTests`,
  `AttendanceDeviceRecoveryTests`, `AttendanceDeviceWorkerScaleAndE2ETests`, plus four
  `MySql*`/`SqlServer*` provider-acceptance classes covering both Phase 6F and 6G scenario sets (32 shared
  scenarios total).
- **Integration**: `AttendanceLeaveIntegrationTests`, `SeparationExitAttendanceIntegrationTests`.

Vitest coverage exists for every Attendance/Overtime/Comp-Off/Device frontend page (one `.test.tsx` per
`.tsx` page under `Frontend/HRMS.Web/src/pages/attendance/`) — the coverage *density* varies enormously and is
documented explicitly in `Attendance_Frontend`'s own cases: `AttendanceDevicesPage.test.tsx` (28 tests) is the
strongest file in the entire module, while `ShiftPatternsPage.test.tsx`, `MyAttendanceExceptionsPage.test.tsx`,
and `MyMonthlyAttendancePage.test.tsx` each contain exactly one smoke-render test (flagged explicitly as
coverage gaps, not silently assumed adequate). The API client modules (`attendance.ts`, `overtime.ts`,
`compOff.ts`, `attendanceDevices.ts`) have zero dedicated unit tests of their own.

A green run of any MySQL/SQL-Server-tagged class is **not** evidence of provider parity if its env var was
absent — they skip silently, per `CLAUDE.md`'s documented convention, and `Attendance_Concurrency`'s own cases
report `NOT EXECUTED - environment unavailable` explicitly rather than PASS when this is the case.
