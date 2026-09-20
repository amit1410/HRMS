# Phase 7I — Payroll Accounting / GL Posting

## Scope

Phase 7I adds an internal, tenant-scoped payroll accounting foundation. It consumes persisted, approved or finalized payroll results and creates balanced debit/credit journal batches. It never recalculates salary, statutory values, or net pay.

## Architecture

- `PayrollGLAccount` is a minimal tenant-scoped account reference.
- `PayrollAccountingConfigurationVersion` is effective-dated and owns component/statutory mappings.
- `PayrollJournalBatch` and `PayrollJournalLine` persist immutable source snapshots and journal totals.
- `PayrollJournalLineSource` preserves traceability from aggregated lines to payroll result/component IDs.
- `PayrollJournalHistory` records generation, validation, approval, and internal posting events.
- Accounting configuration APIs manage tenant-owned GL accounts, configuration metadata, effective-dated versions, and component/statutory mappings. The configuration UI is available under Payroll > Accounting Configuration and requires `Payroll.Accounting.ManageConfiguration`.

Journal generation uses `PayrollResult` and `PayrollResultComponent` as the monetary source of truth. Account codes and names are snapshotted onto lines. Amounts use `decimal` and two-place `AwayFromZero` rounding.

## Lifecycle and controls

The internal lifecycle is `Generated -> Validated -> Approved -> Posted`. Journals must balance before approval or posting. Missing mappings, inactive accounts, invalid source payroll state, tenant mismatches, and duplicate active journals are rejected. “Posted” means posted inside HRMS; no external ERP or finance-system call is made.

CSV export is provider-neutral and escapes text values. External ERP exporters, live GL posting, reversal workflows, statutory returns, arrears/retro, and final settlement remain deferred.

## Persistence

Provider migrations were generated separately for SQL Server and MySQL. All accounting foreign keys are tenant-aware, restrictive for source/reference records, and cascade only journal-owned lines, sources, and history to avoid SQL Server multiple-cascade paths.

## Verification

The focused `PayrollAccountingTests` cover GL account CRUD and tenant-scoped uniqueness, accounting configuration CRUD, effective-dated version creation/date and overlap validation, and component/statutory/net-pay mapping validation including inactive and cross-tenant account rejection. The configuration UI has a focused route/render test. Payroll fast regression passes with 34 tests. `MySqlPayrollAccountingIntegrationTests` executes the migration and behavioral acceptance path: tenant-owned accounts/configuration, payroll-source journal generation, balancing, validation, approval, internal posting, CSV export, and cross-tenant read denial. SQL Server operator verification passed on the first run (1/1, 15.9s) and on repeatability against the same database (1/1, 6.1s), covering the equivalent provider-neutral acceptance path.

The current accounting provider acceptance fixture uses unique test-owned identifiers, disposable MySQL databases, FK-safe transaction rollback, and no development seed data. Provider migrations are model-synchronized; no Phase 7A–7H migration is modified.

## Deferred

SAP, Oracle, Dynamics, Tally, QuickBooks, external GL APIs, accounting credentials, live finance posting, statutory returns, arrears/retro, and final settlement are not implemented.
