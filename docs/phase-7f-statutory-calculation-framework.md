# Phase 7F — Statutory Calculation Framework

## Status

PHASE 7F STATUTORY CALCULATION FRAMEWORK: COMPLETE

Phase 7F establishes a provider-neutral, tenant-scoped statutory framework; it does not claim legal compliance for any jurisdiction. Rates, ceilings, slabs, and applicability are configuration data and must be validated by the deploying organization for the applicable jurisdiction and period.

## Architecture

`IStatutoryPayrollService` resolves the employee statutory profile and the effective statutory configuration version for the payroll period. The service persists immutable configuration/profile history and `PayrollStatutoryResult` rows. The Phase 7E calculation engine invokes it after ordinary earnings and deductions are calculated, then adds employee statutory deductions to `TotalDeductions` and employer contributions to the separate employer-contribution total. Employer contributions never reduce employee `NetPay`.

The initial jurisdiction is India (`IN`) with extensible types for Provident Fund, ESI, Professional Tax, and Income Tax. The service intentionally has no hard-coded statutory rate or legal threshold.

## Configuration and effective dating

`StatutoryConfiguration` is tenant-scoped and identified by jurisdiction, optional state, statutory type, and code. `StatutoryConfigurationVersion` stores effective dates, priority, status, typed JSON rules, component-basis mappings, and professional-tax/income-tax slabs. Overlapping versions are rejected and equal-priority ambiguity is reported explicitly. Employee statutory profiles are also effective-dated and retain immutable history.

PF and ESI use configured component basis mappings, employee/employer rates, optional wage ceilings, and centralized two-decimal monetary rounding. Professional Tax and the configurable Income Tax foundation use persisted slabs. An absent profile/configuration produces no statutory output; an applicable but ambiguous configuration is an explicit failure.

## API and authorization

The API exposes statutory configuration list/detail/version creation, employee statutory profile read/write, and payroll-run employee statutory results. Permissions are `Payroll.Statutory.View`, `Payroll.Statutory.Manage`, `Payroll.Statutory.ViewHistory`, `Payroll.EmployeeStatutory.View`, and `Payroll.EmployeeStatutory.Manage`.

## Persistence and providers

The Phase 7F schema is added in new provider-specific migrations only:

- SQL Server: `AddStatutoryCalculationFramework`
- MySQL: `AddStatutoryCalculationFramework`

Tenant-aware keys, indexes, foreign keys, query filters, decimal precision, and JSON/text storage are configured in the infrastructure model. Previous Phase 7A–7E migrations are unchanged.

## Verification

The fast Payroll script includes the focused statutory test class and remains separate from provider integration classes. The provider script includes independent SQL Server and MySQL statutory integration filters. SQL Server runtime requires `HRMS_SQLSERVER_TEST_CONNECTION`; a missing variable is reported as blocked rather than treated as a pass. MySQL uses the repository’s existing integration environment.

Current verification:

- `StatutoryCalculationTests`: 4 passed, 0 failed, 0 skipped, covering configured PF, ESI, Professional Tax, and Income Tax slab calculations.
- Payroll fast regression: 25 passed, 0 failed, 0 skipped.
- MySQL statutory provider integration: 1 passed, 0 failed, 0 skipped.
- Frontend full suite: 579 passed; TypeScript and production build passed.
- SQL Server statutory provider integration, first run: 1 passed, 0 failed, 0 skipped, 17.7 seconds; build passed.
- SQL Server statutory provider integration, repeatability run against the same database: 1 passed, 0 failed, 0 skipped, 5.4 seconds; build passed.
- SQL Server migration application, configuration/profile/basis/result persistence, PF/ESI/PT/Income Tax configured behavior, payroll integration, tenant isolation, cross-tenant denial, FK-safe cleanup, and provider parity: PASS.
- Backend and solution builds: PASS; TypeScript and frontend production build: PASS; lint: PASS with existing warnings; `git diff --check`: PASS.

### SQL Server cascade-path correction

The initial SQL Server migration correctly exposed a provider-specific schema defect: `PayrollStatutoryResults` had cascading foreign keys to both `PayrollResults` and `PayrollRuns`, while the existing payroll-result graph already reaches the run. SQL Server rejected the resulting multiple cascade paths. The direct `PayrollStatutoryResults -> PayrollRuns` relationship now uses `DeleteBehavior.NoAction`; the result ownership cascade remains through `PayrollResult`, and tenant, employee, configuration, and configuration-version references remain restrictive. The Phase 7F SQL Server and MySQL migrations were regenerated from this corrected model. No Phase 7A–7E migration was changed.

The SQL Server provider test was also corrected without changing production behavior: its cleanup now uses the physical `StatutoryComponentBasis` table name, and its verification query uses `TestTenantContext(tenant)` so the intended global tenant filter can see the test-owned configuration. The operator verified both the first run and a repeatability run against the same integration database successfully. The SQL Server verification blocker is resolved.

### Final acceptance

The framework persists statutory configuration, effective versions, employee statutory profiles, component basis mappings, and payroll statutory results with configuration/version traceability. Configured employee statutory deductions flow into `TotalDeductions` and `NetPay`; employer contributions remain separate. Focused coverage is green for configuration, PF, ESI, Professional Tax, Income Tax configured slabs, and payroll statutory integration. No hard-coded legal rates or slabs were introduced, and deterministic decimal rounding is used.

## Deferred scope

This phase does not implement statutory filing or government integrations, challans, Form 16/24Q, ECR/ESI filing, a full investment-declaration workflow, arrears/retro, final settlement, payslips, bank advice, accounting/GL posting, FX conversion, or jurisdiction-specific legal compliance beyond explicitly configured sample rules.
