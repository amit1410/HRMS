# Phase 7W — Year-End Tax Processing & Annual Reconciliation

## Scope

Phase 7W provides a tenant-scoped year-end tax run over finalized payroll results, approved Phase 7U declaration/proof values, controlled previous-employer inputs, and the configured Phase 7F statutory framework. It persists an auditable reconciliation snapshot, recommendations, statements, exceptions, and lifecycle history.

It is not a payroll calculation engine. It never rewrites finalized payroll, payslips, journals, bank advice, or historical tax results. A required downstream tax effect is handed to the existing PayrollAdjustment/Phase 7R correction path.

## Model and lifecycle

The Phase 7W model contains `YearEndTaxRun`, `YearEndTaxEmployee`, `YearEndTaxPreviousEmployerInput`, `YearEndTaxAdjustment`, `YearEndTaxStatement`, and `YearEndTaxHistory`. All records are tenant-scoped and use composite tenant foreign keys where a relationship crosses an aggregate boundary.

Runs follow `Draft -> Calculated -> Submitted -> Approved -> Closed`; `Cancelled` is available before irreversible downstream use. Invalid transitions are rejected, and closed runs are immutable. Calculation is repeatable only before close and rebuilds the current run snapshot rather than mutating historical payroll.

## Sources and reconciliation

Only finalized payroll periods/results are aggregated. Draft, cancelled, unapproved, staged, or approved-but-unposted inputs are excluded. Approved/accepted Phase 7U declarations and proofs are read through the existing declaration service. Previous-employer values are validated, tenant-scoped, and lifecycle-controlled. Tax liability uses the existing configured statutory profile/version/slab framework; no legal rates or formulas are hardcoded.

The persisted employee snapshot records YTD gross, taxable income and tax deducted, approved declaration/proof values, previous-employer amounts, projected/final taxable income and liability, due/excess indicators, blocking issues, and source calculation metadata. Where reliable future projection data is unavailable, the run records an explicit configuration/insufficient-data issue rather than fabricating payroll.

## Adjustments and Phase 7R boundary

Year-end reconciliation creates a recommendation. It does not change a finalized result. Handoff creates the canonical `PayrollAdjustment` with a stable Phase 7W source identity and records the handoff history. Any correction after finalized Payroll remains a Phase 7R correction/reversal concern. Repeated handoff is protected by source identity and lifecycle rules.

## API and frontend

The API is under `/api/payroll/year-end-tax` for run listing/creation, calculation, employee paging, previous-employer inputs, submit, approve, close, cancel, adjustment handoff, and history. The frontend route is Payroll → Year-End Tax and provides run, reconciliation, previous-employer, and exception surfaces with permission-gated actions.

Employee reconciliation is server-paged. Statements and run data are tenant-filtered. CSV exports are available for summary, reconciliation/statements, and blocking exceptions through the shared `CsvBuilder`; no spreadsheet formula execution is involved.

## Authorization, tenant isolation, and audit

Permissions are `PayrollYearEnd.View`, `Manage`, `Calculate`, `Submit`, `Approve`, `Close`, `ViewHistory`, and `Export`. Maker-checker uses the existing approval architecture and prevents self-approval where enabled. Cross-tenant runs, employees, declarations, and previous-employer records are denied. History records actor, event, status transition, timestamp, and source/reason without logging proof contents or secrets.

## Provider and migration parity

Provider migrations are:

- SQL Server: `20260923100000_AddYearEndTaxProcessingPhase7W`
- MySQL: `20260923100000_AddYearEndTaxProcessingPhase7W`

The SQL Server model avoids duplicate tenant relationships and uses restrictive relationship semantics for audit/history graphs. Pending-model checks must remain `None` for both providers and snapshots must stay synchronized. Prior Phase 7A–7V migrations are not modified.

## Verification and deferred work

Focused year-end service evidence is 4/4 passing: duplicate active-run protection, lifecycle guards, tenant isolation, and the no-shadow-tenant-column model check. The shared provider flow is `PayrollYearEndProviderAcceptance.RunAsync`; both provider wrappers call it without provider-specific business behavior.

MySQL provider acceptance is green on disposable databases:

- First run: 1 passed, 0 failed, 0 skipped; 36 seconds.
- Repeat run: 1 passed, 0 failed, 0 skipped; 34 seconds.

The acceptance proves finalized-only payroll aggregation, exclusion of draft payroll, approved Phase 7U declaration/proof use, approved previous-employer inclusion, exact reconciliation values, maker self-approval denial, checker approval, adjustment handoff without rewriting finalized payroll, close immutability, history, and tenant isolation. A tracked-entity JSON cycle found during the first MySQL run was fixed by persisting a scalar statement snapshot rather than serializing navigation graphs.

The dedicated concurrency matrix is 7/7 passing: calculate versus previous-employer edit, calculate versus calculate, submit versus recalculate, approve versus cancel/reject, approve versus approve, close versus adjustment change, and close versus close. The retry-safety matrix is 8/8 passing: calculate, submit, approve, previous-employer input, adjustment generation, adjustment handoff, close, and annual statement snapshot retries. The SQLite concurrency tests use independent contexts with deterministic interleavings where SQLite writer locking would otherwise obscure the provider-neutral lifecycle assertion.

Exactly-once adjustment handoff is proven with one `YearEndTaxAdjustment` source, one downstream `PayrollAdjustment`, zero duplicate source identities, one handoff history event, and the original amount exactly once. Repeated handoff is safe and finalized Payroll remains unchanged. During this work, recalculation cleanup was corrected to remove dependent statements before employee snapshots, and statement snapshots were made deterministic by excluding volatile internal row IDs.

Large-data acceptance passes with 10,000 employees, two finalized payroll periods, 11,001 payroll results/components/statutory results, 1,000 declarations, 250 accepted proofs, 500 approved previous-employer records, 3,197 additional-tax employees/adjustments, 3,047 excess-tax employees, 3,746 no-change employees, and 10 blocking issues. Calculation duration was 10.35 seconds and total test runtime was approximately 29 seconds. The test covered calculation, server-side paging, tax-due filtering, tenant isolation, and finalized-payroll immutability; draft payroll was excluded.

SQL Server operator reverification also passes against the same database through `PayrollYearEndProviderAcceptance.RunAsync`: first run 1/1 in 10.0570 seconds (10.3 seconds summary), repeat 1/1 in 8.7558 seconds (9.0 seconds summary) with `--no-build`. No SQL Server-specific business workaround was used.

Payroll fast passes 138/138. The provider-excluded non-provider backend regression passes 1,251/1,251 with 0 failed, 0 skipped, 0 blocked, and 0 unaccounted in 3m24s. Focused year-end service evidence remains 4/4 passing and Phase 7V focused evidence remains 4/4 passing. Frontend evidence is 609/609 passing with TypeScript and production build passing; lint passes with existing warnings. Infrastructure/API/solution builds pass, SQL Server and MySQL pending-model checks remain `None`, snapshots are synchronized, and Phase 7A–7V migrations plus protected Phase 7U changes remain untouched. SQL Server status: FINAL OPERATOR REVERIFICATION PASSED.

Deferred unless already supported: government e-filing, statutory certificate generation, OCR/AI proof interpretation, arbitrary formulas/scripts, external tax providers, SFTP, generic request-idempotency infrastructure, and multi-country tax-law expansion.
