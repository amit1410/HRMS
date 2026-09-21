# Phase 7O - Reimbursements & Claims Settlement

## Status

Phase 7O is complete. SQL Server operator acceptance and same-database repeatability are both reported PASS. The module is tenant-scoped and keeps reimbursement amounts as explicit persisted claim, line, settlement, and history records.

## Architecture

- `ReimbursementCategory` is the tenant-scoped master and does not hardcode business categories.
- `ReimbursementPolicyVersion` is effective-dated and rejects overlapping active versions for a category.
- Claims contain multiple lines. Each line captures the resolved policy, claimed/eligible/approved amounts, receipt state, and taxable/non-taxable split.
- Claim numbers use a tenant-scoped, year-resetting `CLM/YYYY/SEQ` format allocated under a serializable transaction.
- Receipt/file support is metadata-only through `ReimbursementAttachment`; binary storage and OCR are intentionally outside this phase.
- `ReimbursementSettlement` is immutable settlement history for Payroll, Manual, and Final Settlement flows.
- `ReimbursementHistory` records lifecycle and settlement events with actor and source information.
- ESS claim reads, updates, submission, history, and attachment metadata are constrained to the authenticated linked employee identity; EmployeeId is never authoritative input for ESS.

## Lifecycle and controls

Claims move through Draft, Submitted, Approved or PartiallyApproved, ReadyForSettlement, Settled, Rejected, and Cancelled states. Approval is maker-checker protected through the shared payroll approval guard. Tenant query filters, linked ESS employee identity, concurrency versions, effective policy resolution, receipt requirements, category limits, and duplicate settlement checks protect financial effects.

## Payroll and Final Settlement

Payroll resolves approved Payroll-settlement lines into explicit `TaxableReimbursement` and `NonTaxableReimbursement` result components, retaining claim and line identifiers. Settlement persistence is idempotent per payroll result. Final Settlement generates eligible FinalSettlement reimbursement lines and persists a corresponding settlement exactly once.

## Accounting and reporting

Claim totals reconcile from lines, and the register exposes claimed, eligible, approved, taxable, non-taxable, settled, and outstanding values with tenant-safe filtering. Accounting mappings remain configuration-driven; no statutory exemption or live payment behavior is embedded in reimbursement logic.

## Provider and regression verification

SQL Server and MySQL migrations were generated as separate Phase 7O artifacts; model snapshots are synchronized and previous Phase 7A-7N migrations are unchanged. The shared `PayrollReimbursementsProviderAcceptance` path is used by both provider classes, with no provider-specific business workaround. MySQL acceptance completed twice against the same disposable database with 1 passed / 0 failed / 0 skipped for each invocation (29.5636s and 30.5398s). SQL Server operator evidence reports `SqlServerPayrollReimbursementsIntegrationTests` PASS on the first run (1 passed / 0 failed / 0 skipped) and PASS on the second run against the same database (1 passed / 0 failed / 0 skipped); exact durations were not supplied in the operator handoff.

The SQLite reimbursement foundation, concurrency/idempotency, receipt, numbering, partial-settlement, security, accounting, and bounded large-data slices are green. The bounded acceptance creates 100 claims and 250 lines, reconciles 2 x 50 register pages, and verifies claimed 2,500, eligible 2,500, approved 2,500, taxable 0, non-taxable 2,500, settled 1,500, and outstanding 1,000, with tenant isolation. The reimbursement-focused backend completed at 20 passed / 0 failed / 1 expected SQL Server skip; the dedicated concurrency suite completed at 13 passed / 0 failed / 0 skipped; and Payroll fast completed at 88 passed / 0 failed / 0 skipped. The explicit provider-name-excluded non-provider backend regression completed at 1,158 passed / 0 failed / 0 skipped. The focused frontend reimbursement suite completed at 2 passed / 0 failed / 0 skipped; the full frontend suite completed at 598 passed / 0 failed / 0 skipped; TypeScript, production build, and lint are green with existing warnings. A single unrelated Leave Periods pagination assertion was transient; its bounded rerun completed 14/14.

Settlement integrity was rechecked after the final implementation pass: manual and payroll settlement mark each fully settled line as settled, payroll recovery returns only the approved outstanding amount after prior partial settlements, and Final Settlement cannot double-settle a Payroll or already settled claim. A dedicated accounting test proves configured reimbursement mapping, balanced payroll journal generation, and persisted component source traceability. The shared provider acceptance covers category/policy, multi-line claims, attachments, submission, maker-checker approval, partial approval, tax split, Payroll and Manual settlement, Final Settlement persistence, register, tenant isolation, cross-tenant denial, cleanup, and repeatability. No provider-specific business workaround was introduced.

## Final closure

Provider parity is complete. Representative persisted coverage includes categories, effective-dated policies, tenant/year claim numbering, claim headers and lines, attachment metadata, submission, maker-checker approval, partial approval, taxable/non-taxable treatment, Payroll and Manual settlement, `ReimbursementSettlement`, Final Settlement integration, representative accounting, register reporting, ESS/admin authorization, tenant isolation, cross-tenant denial, cleanup, and repeatability. SQL Server operator verification is complete.

## Deferred

OCR/AI receipt parsing, corporate cards, travel booking, live reimbursement banking, external expense platforms, ERP posting, GST filing, mileage GPS, fraud scoring, and a generic multi-level workflow engine remain explicitly deferred.
