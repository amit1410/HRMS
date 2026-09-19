# Phase 7D — Payroll Period / Payroll Run Setup

## Scope and status

Phase 7D establishes tenant-scoped payroll calendar and run orchestration without calculating payroll amounts. It is the setup boundary between Employee Salary Assignment and the future Payroll Calculation Engine.

Payroll calculation, gross/net computation, statutory deductions, arrears, retro pay, payslips, bank advice, and accounting posting are explicitly deferred.

## Architecture

The Application layer owns `PayrollPeriodService` and `PayrollRunService`; controllers are thin and use the existing tenant and permission pipeline. The Domain layer contains period, run, population snapshot, and immutable history entities. EF configurations and provider migrations support SQL Server and MySQL.

## Payroll periods

`PayrollPeriod` is tenant-scoped and contains code, name, period type, start/end dates, pay date, fiscal year/period number, status, active flag, and optimistic concurrency. Codes are unique within a tenant. Active overlapping date ranges are rejected with a provider-translatable predicate. Periods transition `Draft -> Open -> Closed -> Locked`; closed and locked periods cannot be edited.

## Payroll runs and population snapshot

`PayrollRun` belongs to one period and uses a tenant-scoped server-generated run number. The lifecycle is explicit: `Draft -> Prepared -> Processing -> Calculated -> Approved -> Finalized`, with cancellation permitted only from the pre-processing states. No amount calculation occurs in this phase.

Preparing a run snapshots every tenant employee as eligible or excluded. The snapshot records the employee, effective salary assignment, salary structure, and exact salary structure version identifiers when available. Population rebuild is allowed only before processing; later states are immutable.

## Effective-date rule

Payroll-period `EndDate` is the authoritative reference date for employee employment eligibility and Employee Salary Assignment resolution. The exact `SalaryStructureVersionId` resolved by that assignment is persisted in the run population snapshot. This avoids silently changing a prepared run when master data changes later.

## History and authorization

Period and run histories record creation, lifecycle transitions, preparation, population generation/rebuild, approval, finalization, cancellation, actor, timestamp, and relevant snapshot/reason data. Permissions are provided for period view/manage and run view/manage/prepare/approve/finalize/history and are seeded through the existing permission conventions.

## APIs and frontend

The API provides tenant-safe period list/detail/create/update/transition/history endpoints and run list/detail/create/prepare/population/transition/history endpoints under `/api/payroll/periods` and `/api/payroll/runs`.

Payroll navigation now includes Payroll Periods and Payroll Runs. The UI supports period creation and open/close/lock actions, run creation and preparation/lifecycle actions, population counts, and status display. It intentionally does not display or calculate gross/net amounts.

## Persistence

Provider-specific migrations named `AddPayrollPeriodAndRunSetup` create the period, run, population, and history tables, tenant-aware indexes, concurrency columns, and foreign keys. Existing Phase 7A, 7B, and 7C migrations are unchanged.

## Verification

- SQLite service coverage verifies tenant scoping, overlap validation, period lifecycle, population eligibility/exclusion, exact version snapshotting, invalid transitions, rebuild protection, and history.
- The fast Payroll regression uses exact non-provider classes (`SalaryComponentMasterTests`, `SalaryStructureMasterTests`, `EmployeeSalaryAssignmentTests`, and `PayrollPeriodRunTests`): 15 passed, 0 failed, 0 skipped, in 5 seconds.
- The focused SQL Server Payroll Period/Run integration test passed, and the focused MySQL Payroll Period/Run integration test passed.
- SQL Server Phase 7D provider runtime passed: `HRMS.Tests.SqlServerPayrollPeriodRunIntegrationTests.SqlServer_payroll_period_and_run_preserve_provider_neutral_semantics` — 1 passed, 0 failed, 0 skipped, 5.6 seconds.
- MySQL Phase 7D provider runtime passed: `HRMS.Tests.MySqlPayrollPeriodRunIntegrationTests.MySql_payroll_period_and_run_preserve_provider_neutral_semantics` — 1 passed, 0 failed, 0 skipped, 28.5 seconds.
- Provider verification blocker: RESOLVED. The provider tests confirmed migration/runtime parity, tenant isolation, cross-tenant visibility denial, run creation, preparation, population behavior, and history for the Phase 7D provider-neutral flow.
- Frontend focused coverage passed 5 tests; the full frontend suite passed 578 tests. Backend build, TypeScript, production frontend build, lint, and `git diff --check` passed; lint retains existing warnings.
- The full long-running backend suite remains deferred to the next major repository checkpoint under the approved targeted regression strategy.

## Deferred scope

The following are not started in Phase 7D: payroll calculation, gross/net, PF, ESI, TDS, professional tax, gratuity, bonus, arrears, retro payroll, loan recovery, reimbursement calculation, payslip, bank advice, and accounting journal posting.
