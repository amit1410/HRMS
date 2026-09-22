# Phase 7T - Payroll Reports & Management Insights

## Scope

Phase 7T adds read-only management reports over persisted Payroll evidence. It does not calculate Payroll, alter finalized results, or replace the Phase 7S analytics and reconciliation lifecycle.

## Discovery and reuse

- Reuses `PayrollResult`, `PayrollResultComponent`, `PayrollStatutoryResult`, Bank Advice, accounting journals, loan, reimbursement, variable-pay, adjustment, Final Settlement, and off-cycle persistence.
- Reuses immutable Phase 7S `PayrollRunEmployee.DepartmentId` and `CostCenterId` snapshots. Null historical dimensions are reported as `UNCLASSIFIED` and are never resolved through current Employee data.
- Reuses `PagedResult`, `PagedQuery`, tenant-scoped `IHrmsDbContext`, the canonical `Payroll.Analytics.View` authorization boundary, and centralized `CsvBuilder`.
- No XLSX dependency was added; CSV is the supported export format for this increment.

## API and export catalog

`PayrollReportsController` exposes read-only endpoints under `/api/payroll/reports` for the Payroll register, earnings, deductions, employer contributions, Department and Cost Center summaries, components, statutory summary, bank payments, accounting summary, loan recoveries, reimbursements, variable pay, adjustments, Final Settlements, off-cycle Payroll, dashboard, and trends.

CSV exports use the same report service/query path for Payroll Register, earnings, deductions, employer contributions, Department, Cost Center, components, statutory summary, bank payments, accounting summary, loans, reimbursements, variable pay, adjustments, Final Settlements, and off-cycle Payroll.

All report queries are tenant-scoped, use persisted Payroll values, support server-side filtering/paging where the result can grow, and use deterministic ordering. Cross-module report rows reference persisted source records and do not mutate them.

## Verification

- Focused Phase 7T report tests: 3 passed / 0 failed / 0 skipped.
- CSV escaping test: 1 passed / 0 failed / 0 skipped.
- Large data: PASS - 1,000 employees, 3 Payroll runs, 3,000 Payroll results, historical dimensions, paging, component aggregation, and dashboard totals.
- MySQL report provider acceptance: first 1 passed / 0 failed / 0 skipped; repeat 1 passed / 0 failed / 0 skipped.
- MySQL durations: first 51.8s; repeat 34.3s.
- SQL Server report provider acceptance: first 1 passed / 0 failed / 0 skipped (18.2724s test time; 18.5s overall); same-database repeat 1 passed / 0 failed / 0 skipped (7.3828s test time; 7.6s overall).
- Payroll fast: 138 passed / 0 failed / 0 skipped.
- Non-provider backend: 1,214 passed / 0 failed / 0 skipped.
- Frontend: 605 passed; production build passed.
- Backend/solution build: PASS; `git diff --check`: PASS.
- SQL Server final operator gate: PASS.

`PayrollReportsProviderAcceptance.RunAsync` is shared by the SQL Server and MySQL wrappers. It covers persisted Payroll/report data, historical dimensions, all report families, CSV generation, filtering, paging, tenant isolation, and repeatability. No provider-specific business workaround is used.

## Security and privacy

The report surface reuses `Payroll.Analytics.View`, preserving the Phase 7S authorization boundary while avoiding a new permission or migration. Report APIs and exports are tenant-scoped and do not expose secrets or configuration values.

## Migration and performance status

No Phase 7A-7S migration was changed and no Phase 7T migration is required for the current read-model implementation. Reports use projections, server-side filters/paging, deterministic ordering, and persisted historical dimensions. Full history is not intentionally loaded by the register/component report queries; the Department/Cost Center and dashboard paths remain bounded by the requested run/filter scope.

## Deferred scope

XLSX without an existing approved dependency, predictive/ML analytics, fraud scoring, forecasting, BI warehouse integration, custom SQL/report designer, and regulatory filing workflows remain deferred.

SQL Server status: FINAL OPERATOR REVERIFICATION PASSED
