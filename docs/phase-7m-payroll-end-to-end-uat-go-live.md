# Phase 7M — Payroll End-to-End UAT & Go-Live Readiness

Phase 7M composes the provider-neutral Payroll acceptance paths through the complete persisted payroll lifecycle. It does not add calculation, banking, ERP, or statutory-portal functionality.

## UAT scenario

The shared `PayrollEndToEndUatAcceptance` path runs the established tenant-scoped acceptance flows for:

- persisted payroll output and payslip publication/self-service
- Bank Advice generation, validation, approval, masking, and CSV export
- balanced accounting journal generation, approval, posting, and export
- PF/ESI/PT/TDS compliance return generation, validation, approval, export, and manual Filed status
- retro adjustment and final settlement lifecycle, including immutability and tenant isolation

Each component acceptance uses unique tenant data and provider-neutral services. The SQLite UAT test completed successfully; the SQL Server and MySQL integration classes execute the same shared path.

## Configuration and readiness

The existing phase-specific acceptance fixtures verify salary structures/assignments, persisted payroll results, statutory configuration and results, bank details, GL mappings, controls, and tenant boundaries. Phase 7L Configuration Health and Payroll Readiness remain the pre-execution operational gates.

Configuration Health must report no blocking Salary, Statutory, Payment, Accounting, or Controls errors. Payroll Readiness must report a ready run or only explicitly non-blocking warnings.

## Reconciliation

| Metric | UAT assertion |
| --- | --- |
| Payroll result values | Persisted result is the monetary source of truth |
| Payslip values | Match PayrollResult and statutory snapshot lines |
| Bank Advice | Payment rows and totals derive from finalized payroll results |
| Accounting | Total Debit equals Total Credit |
| Statutory returns | PF, ESI, PT, and TDS rows/source totals reconcile |
| Retro | Historical payroll remains unchanged; adjustment source is traceable |
| Final Settlement | Gross minus deductions equals NetSettlement |

No unexplained monetary mismatch was introduced by the composed UAT acceptance.

## Controls and security

The UAT coverage retains Phase 7L controls: period locking, readiness, maker-checker, PreventSelfApproval, lifecycle guards, idempotency, concurrency, audit/history, and tenant isolation. Employee self-service is limited to the employee’s own published payslips; it does not expose Bank Advice, accounting, or statutory administration.

## Provider UAT

- `SqlServerPayrollEndToEndUatIntegrationTests` uses the same shared acceptance and a disposable SQL Server database.
- `MySqlPayrollEndToEndUatIntegrationTests` migrates a unique disposable MySQL database and uses the same shared acceptance.
- Provider-specific business semantics are not introduced.
- Cleanup is performed by the provider harness/database lifecycle.

Provider results: SQL Server UAT first run PASS (1/1) and repeatability PASS (1/1); MySQL UAT first run PASS (1/1) and repeatability PASS (1/1). Both providers execute the same shared acceptance path and report no provider issues.

## Regression and go-live checklist

Before production payroll:

- tenant active and timezone reviewed
- payroll controls and maker-checker policy configured
- employees active and salary assignments complete
- salary structures/components effective
- statutory configurations and identifiers validated
- salary bank accounts validated
- accounting mappings complete and balanced
- numbering configuration checked
- Configuration Health has no blocking issues
- Payroll Readiness is clean
- test payroll reconciled
- payslips reconciled and publication reviewed
- Bank Advice reconciled and export reviewed
- accounting journal balanced and internally posted
- compliance totals reconciled; Filed remains manual HRMS status
- database backup and restore plan verified
- migrations verified in the deployment environment
- monitoring/logging enabled without sensitive payroll data
- first-payroll support owner identified

## Known deferred items

Loans and advances, reimbursements, advanced gratuity, Form 16, official 24Q submission, live EPFO/ESIC integration, live bank submission, ERP/SAP/Tally integration, and advanced segmented retro remain deferred. Generic CSV exports are internal files and are not represented as official government upload formats. No external payment or portal filing occurs in Phase 7M.

## Verification record

- SQLite composed UAT: PASS — 1/1.
- Payroll fast: PASS — 56 passed, 0 failed, 0 skipped.
- MySQL UAT: PASS — first run 1/1; repeatability 1/1.
- SQL Server UAT: PASS — first run 1/1; repeatability 1/1. Operator durations were not included in the supplied evidence.
- Non-provider backend regression: PASS — 1,126 passed, 0 failed, 0 skipped, 2m08s.
- Frontend Phase 7M verification: focused 3 passed, full 591 passed, TypeScript and production build passed, lint passed with existing warnings.

Final UAT status: SQL Server and MySQL provider parity is green. Configuration, readiness, calculation-output hand-offs, payslips, Bank Advice, accounting, PF/ESI/PT/TDS compliance, retro/final settlement, tenant isolation, cleanup, reconciliation, locking, maker-checker, self-approval, idempotency, and concurrency controls remain green. No Phase 7M migration or previous Payroll migration was created or modified.
