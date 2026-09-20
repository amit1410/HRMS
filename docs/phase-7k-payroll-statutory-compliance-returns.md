# Phase 7K — Payroll Statutory Compliance & Returns Foundation

Phase 7K prepares internal, filing-ready statutory registers from persisted payroll data. It does not recalculate PF, ESI, Professional Tax, or Income Tax/TDS.

## Source of truth

Return generation reads finalized/approved payroll runs and persisted `PayrollStatutoryResult` rows. Employee identifiers and names are snapshotted into return rows, while each row retains links to the originating payroll run, payroll result, and statutory result. Header totals are reconciled from the detail rows.

Supported configuration-driven types are PF, ESI, Professional Tax, and Income Tax/TDS, with jurisdiction recorded on each period and batch. Missing statutory sources produce an explicit generation error rather than an empty return.

## Lifecycle and validation

Compliance periods are tenant-scoped and date-validated. A return moves through Generated, Validated, Approved, and manually Filed states; cancelled periods/returns are not reused. Validation recomputes employee count and contribution totals and blocks approval when reconciliation fails. Filed means that an operator recorded filing; no government portal submission is performed.

The model includes source links, immutable history events, challan/payment-summary storage, tenant-scoped indexes, and restrictive tenant/reference foreign keys. Generic CSV export and export audit can be extended without introducing another statutory calculation engine.

## Security and providers

All periods, batches, rows, sources, history, and challans are tenant-scoped. Cross-tenant reads are denied by tenant context and global filters. SQL Server and MySQL have separate Phase 7K migrations and synchronized model snapshots; provider acceptance tests use the same behavioral path and isolated test data.

The frontend provides the initial Statutory Compliance route for opening periods and generating returns, with statutory permissions enforced by the existing authorization architecture.

## Verification status

The focused SQLite compliance tests pass, including invalid-period/duplicate-period validation and the rule that generation requires persisted statutory source data. The provider-neutral MySQL acceptance test passes twice after applying the Phase 7K migration and exercises PF, ESI, Professional Tax, and Income Tax/TDS source-linked generation, reconciliation, generic CSV export, manual Filed transition, duplicate-source protection, and tenant isolation. Updated SQL Server operator acceptance also passes twice (1/1 each) against the same integration database with the same four statutory types, proving provider parity and repeatability.

The frontend route has focused coverage. The reported Employee form failure passed when its file was rerun alone (15/15), and a subsequent full run passed all 588 tests. During diagnosis, the full suite also exposed and the Phase 7K change corrected a frontend permission-mirror ordering mismatch; no Employee production code or Employee test was changed.

Final verification records 40/40 focused Payroll tests, 588/588 frontend tests, passing TypeScript, production build, backend and solution builds, and passing `git diff --check`. Statutory values are copied from persisted `PayrollStatutoryResult` records; no PF, ESI, PT, or TDS recalculation is performed. `Filed` is a manual HRMS status only. Generic CSV files are internal registers and are not claimed to be official government upload formats.

## Deliberate limitations

The CSV outputs are internal compliance/export registers and are not guaranteed official government upload formats. Live EPFO/ESIC portal integration, government login automation, CAPTCHA/DSC, Form 16, official 24Q submission, direct challan payment, and statutory calculation changes remain deferred.
