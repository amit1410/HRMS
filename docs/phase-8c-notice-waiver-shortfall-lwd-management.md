# Phase 8C — Notice Period, Waiver, Shortfall & LWD Management

Phase 8C extends the Phase 8A/8B separation case without introducing a second notice model or any settlement calculation. `EmployeeSeparation` owns the lifecycle snapshot while the effective `EmployeeEmployment` record remains the runtime notice contract consumed by Leave.

## Rules

- Required notice days are snapshotted from the effective employment record when HR approves the separation. The supported policy unit is the existing day-based employment notice configuration; missing or unsupported configuration is rejected.
- Notice dates use inclusive calendar days. Expected end is `NoticeStartDate + RequiredNoticeDays - 1`; served days are `ApprovedLastWorkingDate - NoticeStartDate + 1`, bounded at zero.
- Shortfall is `max(0, required - served - waived)`. Extension is `max(0, served - required)`. These are quantities only.
- Waiver is HR-authorized, reason-required, additive, append-only, and cannot exceed the remaining shortfall. No salary, recovery, tax, deduction, buyout, or accounting amount is calculated.
- Post-approval LWD revision is an explicit command. It supports extension and early release, preserves the prior value in event history, recalculates quantities, and atomically synchronizes `EmployeeEmployment.NoticeEndDate`.
- Approved, exited, cancelled, rejected, and withdrawn lifecycle rules are server-enforced; approved cases remain notice-active until a later exit-completion phase.

## API and audit surface

The notice summary is available at `GET /api/separation/{id}/notice`; notice history uses `/notice/history`. HR actions are `POST /notice/waive` and `POST /revise-approved-lwd`, protected by `Separation.NoticeWaive` and `Separation.ReviseApprovedLastWorkingDate`. New fields are `ExpectedNoticeEndDate`, `WaivedNoticeDays`, `NoticeExtensionDays`, `NoticeDisposition`, and `LastNoticeRevisionAtUtc`.

Notice requirement snapshots, waivers, approved-LWD extensions/reductions, and recalculation facts are append-only separation events. The transaction updates the separation, employment notice dates, event, and concurrency version together; deterministic save failure tests prove rollback from a fresh context.

## Boundaries

Leave continues to read the existing effective employment notice fields. Phase 8C does not mutate Leave balances or policy logic. Payroll remains isolated: no Final Settlement, gratuity, notice recovery money, PayrollAdjustment, PayrollResult, bank advice, GL, loan settlement, reimbursement settlement, or monetary buyout is created. Future settlement orchestration may consume the approved LWD, required days, waived days, and shortfall quantity.

## Verification evidence

- Focused Phase 8C: 13/13 PASS.
- Notice concurrency: 6/6 PASS using independent contexts and a file-backed SQLite test database so the concurrent transactions are genuine.
- Failure injection: 3/3 PASS, including LWD synchronization rollback, waiver rollback, and durable-success replay.
- Phase 8B regression: 9/9 PASS.
- Phase 8A regression: 6/6 PASS.
- MySQL provider first/repeat: 1/1 PASS — 39s / 38s after the verified test build (`--no-build` provider invocations). Shared provider acceptance covers snapshot, shortfall, waiver, LWD extension, employment synchronization, and history.
- SQL Server wrapper: `SqlServerSeparationNoticeIntegrationTests.SqlServer_separation_notice_provider_acceptance` is discoverable and uses `HRMS_SQLSERVER_TEST_CONNECTION`; operator execution remains pending when that environment variable is unavailable.
- SQL Server and MySQL pending-model checks: no changes since the latest migration.
- Frontend: 610/610 PASS; TypeScript and production build PASS; lint PASS with existing warnings.
- Infrastructure, API, and solution builds PASS; `git diff --check` PASS; no secrets or production failure switches added.

## Deferred

Clearance, exit interview, final settlement orchestration, gratuity, leave encashment, notice recovery money, relieving/experience letters, account disablement, rehire, and alumni access remain outside Phase 8C.
