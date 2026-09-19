# Phase 7E - Payroll Calculation Engine

## Scope

Phase 7E calculates generic payroll earnings, deductions, gross earnings, total deductions, and net pay for prepared Payroll Runs. It uses the immutable population snapshot created in Phase 7D and persists historical result/component snapshots.

Country-specific statutory engines, payslips, arrears, retro pay, bank advice, and accounting posting are deferred.

## Architecture

`PayrollCalculationEngine` owns orchestration and `PayrollCalculationService` exposes tenant-safe result/error queries. The engine validates run state, transitions `Prepared -> Processing -> Calculated` only when all eligible employees calculate successfully, and leaves the run `Prepared` with explicit errors when any employee fails.

Results are tenant-scoped and keyed by run, employee, and calculation attempt. Each result retains the exact EmployeeSalaryAssignment and SalaryStructureVersion references from the prepared snapshot, the employment snapshot date, calendar/eligible days, proration factor, currency, totals, calculation version, and current-attempt marker. Result components copy component code/name, type, calculation type, base/rate, unprorated amount, applied proration, formula snapshot, final amount, ordering, source, and metadata so historical rendering does not depend on mutable master names.

## Calculation strategies

- Fixed Amount: configured or permitted employee override value.
- Percentage: configured/override rate applied to a previously calculated base Salary Component.
- Manual: configured or permitted employee-specific value.
- Formula: controlled arithmetic expressions containing numeric literals, component codes, parentheses, and `+ - * /`; no dynamic code, JavaScript, C#, or unrestricted evaluation.

Components are evaluated by sequence. Missing dependencies, unsupported formulas, invalid rates, and negative generic values produce explicit calculation errors rather than zero values.

## Effective dates and proration

The prepared PayrollRunEmployee snapshot remains authoritative. The exact assignment and SalaryStructureVersion IDs are persisted into each result. PayrollPeriod dates are used for the calculation window. Proratable components use calendar-day proration based on assignment and employment boundaries; percentage/formula components referencing an already prorated component are not prorated twice.

Overlapping or ambiguous mid-period assignments are rejected with an explicit calculation error. Segmented mid-period assignment calculation is deferred to a future Retro/Segmented Payroll phase; the engine does not silently choose or aggregate an assignment.

## Rounding and currency

All monetary values use `decimal` and the centralized `PayrollRoundingPolicy` two-decimal `MidpointRounding.AwayFromZero` policy. Internal dependency values retain decimal precision; persisted component and aggregate amounts are rounded for the payroll result. The policy is covered at 10.005 -> 10.01 and 10.004 -> 10.00. Results use the Employee Salary Assignment currency; FX conversion is not implemented.

## Errors, history, and recalculation

`PayrollCalculationError` records employee/component failures such as missing assignments, missing versions, missing dependencies, invalid formulas, invalid overrides, currency issues, and negative net pay. `PayrollCalculationHistory` records calculation started, employee calculated/failed, completed, recalculation requested/completed, and result reset events, each tied to the calculation attempt.

Recalculation is allowed before approval/finalization. Each attempt receives a unique ID and incremented calculation version. Prior results and history remain immutable and queryable, while only the successful latest result is marked current; stale current errors are retired before the next attempt. Approved and finalized runs cannot be recalculated. Recalculation is idempotent for unchanged inputs. Failed employees remain uncalculated and the run does not transition to `Calculated` while blocking errors remain.

## APIs and authorization

Added endpoints:

- `POST /api/payroll/runs/{id}/calculate`
- `POST /api/payroll/runs/{id}/recalculate`
- `GET /api/payroll/runs/{id}/results`
- `GET /api/payroll/runs/{id}/results/{employeeId}`
- `GET /api/payroll/runs/{id}/calculation-errors`

Permissions: `Payroll.Run.Calculate`, `Payroll.Run.Recalculate`, and `Payroll.Run.ViewResults`, following existing seeding and policy conventions.

## Frontend

Prepared Payroll Runs expose a permission-aware Calculate action. Calculation and result/error API clients are available for the run detail/result UI; statutory and payslip screens are intentionally not included.

## Persistence

SQL Server and MySQL migrations named `AddPayrollCalculationEngine` and the additive `AddPayrollCalculationTraceability` migration create the four calculation tables with tenant-aware indexes, foreign keys, concurrency, attempt/current markers, and query filters. Phase 7A-7D migrations are unchanged.

## Verification

- Payroll calculation tests: 6 passed, 0 failed, 0 skipped, covering fixed/percentage/formula calculation, gross/deduction/net totals, proration, tenant isolation, explicit formula errors, recalculation retention/idempotency, and rounding boundaries.
- Payroll fast regression: 21 passed, 0 failed, 0 skipped.
- MySQL provider runtime: 1 passed, 0 failed, 0 skipped (20 seconds), including migration, calculation persistence, and cleanup.
- Frontend focused Payroll API tests: 6 passed.
- Full frontend suite: 579 passed.
- Test-project/backend compilation passed with the existing warnings; the solution build `dotnet build HRMS.slnx --no-restore /m:1 /p:UseSharedCompilation=false` also passed with 0 warnings and 0 errors.
- Frontend focused Payroll tests: 6 passed; TypeScript passed; full frontend suite: 579 passed; production build passed; lint passed with existing warnings; `git diff --check` passed.
- SQL Server provider runtime: PASS. The dedicated test `SqlServerPayrollCalculationIntegrationTests.SqlServer_payroll_calculation_persists_provider_neutral_run_state` executed successfully: 1 passed, 0 failed, 0 skipped, duration 13.1 seconds.
- `SqlServerIntegrationTestHarness` requires an existing dedicated database whose name contains `Test` or `Integration`, refuses protected databases, applies the complete migration chain, and the Phase 7E test creates a unique tenant with scoped FK-safe cleanup. The test does not create the database.
- SQL Server migration chain, including the Phase 7E calculation and traceability migrations, applied successfully. Calculation execution, run-state persistence, provider-neutral schema behavior, and repeatable FK-safe cleanup passed. The SQL Server blocker is RESOLVED.
- SQL Server verification confirmed result/component persistence, snapshot traceability, recalculation/history/current-attempt semantics, proration/rounding behavior, tenant isolation, cross-tenant denial, and provider parity for the covered integration scenario.
- Full backend regression with hang diagnostics completed: 1,284 passed, 0 failed, 70 skipped, total duration 2.77 minutes. The prior apparent hang was long-running MySQL/provider execution rather than a Phase 7E failure; `--blame-hang` completed without identifying a hung test.
- Final Phase 7E acceptance: focused Payroll regression 21 passed, 0 failed, 0 skipped; MySQL provider 1 passed, 0 failed, 0 skipped; SQL Server provider 1 passed, 0 failed, 0 skipped; solution build passed with 0 errors; frontend and TypeScript checks remained green.

## Deferred scope

PF, ESI, TDS, Professional Tax, Labour Welfare Fund, country-specific statutory engines, gratuity, arrears, retro payroll, bonus, loans, reimbursements, payslips, bank advice, accounting posting, GL integration, segmented mid-period payroll, and Phase 7F review/approval workflow are not started.

## Closure status

All mandatory Phase 7E implementation and verification checks are complete. No production secrets were written to the repository, and no commit or push was performed as part of closure.

PHASE 7E PAYROLL CALCULATION ENGINE: COMPLETE
