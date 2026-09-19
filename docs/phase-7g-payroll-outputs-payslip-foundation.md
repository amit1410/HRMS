# Phase 7G — Payroll Outputs & Payslip Foundation

## Scope

Phase 7G adds payroll output capabilities without recalculating payroll. `PayrollResult` and its persisted component/statutory rows remain the authoritative source of truth. Payslip generation copies those values into an immutable, tenant-scoped display snapshot.

## Architecture

The output service generates `Payslip`, `PayslipLine`, and `PayslipHistory` rows from the current calculated result for a run. Employee identity data used for display is snapshotted at generation time. Current Salary Components, assignments, and employee masters are not re-resolved to render an existing payslip.

Payslips may be generated for calculated, approved, or finalized runs. Publication is restricted to approved or finalized runs. Published snapshots are not mutated; pre-publication regeneration creates a superseding snapshot/version. The current implementation provides a provider-neutral printable HTML document model; a PDF renderer abstraction can be added without moving calculation logic into presentation code.

## Lines, register, and export

Result component lines are preserved in calculation sequence. Statutory employee and employer amounts are included as identifiable statutory lines. The payroll register reads current PayrollResults with pagination and a CSV export contract that escapes commas, quotes, and line breaks safely. No dynamic salary-component SQL columns are created.

Payslip numbering is tenant-safe and deterministic for a payroll result/version (`PS/{period}/{employee}/{version}`). Database uniqueness is enforced per tenant. Output APIs include generation, publication, run payslips, printable document, payroll register, CSV export, and self-service published-payslip access.

## Security

HR/payroll output permissions are separate from employee self-service: `Payroll.Payslip.ViewAll`, `Payroll.Payslip.Generate`, `Payroll.Payslip.Publish`, `Payroll.Register.View`, `Payroll.Register.Export`, and `Payroll.Payslip.ViewOwn`. Self-service access resolves the linked Employee identity and only returns published payslips for that employee. Tenant query filters and tenant-aware foreign keys remain enforced.

## Persistence

New provider-specific migrations add `Payslips`, `PayslipLines`, and `PayslipHistories`. Payslip foreign keys use restrictive delete behavior for payroll source rows so historical output cannot disappear through a payroll-run cascade. Lines are owned by their payslip and may cascade with it. Previous Phase 7A–7F migrations are unchanged.

## Verification

- Focused SQLite output test: 1 passed, verifying payslip generation from a persisted result, line fidelity, no recalculation, publication, linked-employee self-service visibility, paginated register output, and CSV export content.
- Payroll fast regression: 26 passed, 0 failed, 0 skipped.
- Provider acceptance uses the shared `PayrollOutputProviderAcceptance` path for SQL Server and MySQL. It verifies result-backed snapshot generation, earning/deduction/statutory lines, no recalculation, tenant-scoped numbering, pre-publication regeneration, publication, self-service visibility, register pagination/totals, CSV output, cross-tenant denial, and transactional cleanup.
- MySQL output provider behavior: 1 passed on the first isolated disposable database and 1 passed on a repeat run; the complete behavioral path executed without skips.
- SQL Server output provider class: behaviorally equivalent and repeatable by design, but runtime remains externally required because `HRMS_SQLSERVER_TEST_CONNECTION` is unavailable in the Codex process.
- Migration consistency correction: the uncommitted Phase 7G SQL Server and MySQL migration designers/snapshots had retained `Payslip.ConcurrencyVersion` as a concurrency token after the final configuration removed that token. The Phase 7G migrations were cleanly regenerated as `20260919141540_AddPayrollOutputsPayslipFoundation` (SQL Server) and `20260919141712_AddPayrollOutputsPayslipFoundation` (MySQL), with synchronized snapshots. EF reports no pending model changes for either provider. No warning suppression, `EnsureCreated`, schema workaround, or Phase 7A–7F migration change was introduced.
- Backend/API compilation and serial solution build: PASS, 0 errors.
- Frontend focused payslip verification: 3 passed, covering published rendering, printable action, empty state, and API failure handling. Full frontend verification: 582 tests passed, 0 failed; TypeScript PASS, production build PASS, lint PASS with existing warnings.
- `git diff --check`: PASS.

## Deferred

Bank advice/payment files, accounting/GL posting, statutory returns and filings, Form 16/24Q, ECR/ESI filing, arrears/retro, final settlement, reimbursement settlement, loan recovery, and a concrete PDF backend remain deferred. No payslip calculation, bank payment, or accounting behavior is implemented here.

## Status

Phase 7G is not complete until the SQL Server behavioral provider test executes and passes. MySQL behavioral verification is complete; SQL Server remains the only closure blocker.
