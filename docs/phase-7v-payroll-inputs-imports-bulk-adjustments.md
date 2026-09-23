# Phase 7V — Payroll Inputs, Imports & Bulk Adjustments

## Scope and architecture

Phase 7V stages payroll inputs in tenant-scoped `PayrollInputBatch` and `PayrollInputLine` records. It supports manual rows and CSV staging, structured validation issues, preview, maker-checker approval, posting, cancellation, and immutable batch history. It does not calculate payroll, mutate `PayrollResult`, or create a second correction engine.

Approved rows post into the Phase 7R `PayrollAdjustment` model with `SourceType = PayrollInputBatch` and `SourceId = PayrollInputLine.Id`. Existing adjustment application records provide exactly-once payroll consumption. Finalized payroll remains immutable; historical corrections continue through Phase 7R.

## Lifecycle and validation

The supported atomic lifecycle is `Draft -> Validated -> Submitted -> Approved -> Posted`. Validation failures produce `ValidationFailed`; rejection produces `Rejected`; cancellation is available before posting. Any error blocks submission/posting. Warnings are retained and may proceed. Stable validation codes include `EMPLOYEE_NOT_FOUND`, `COMPONENT_NOT_FOUND`, `COMPONENT_NOT_EFFECTIVE`, `INVALID_COMPONENT_TYPE`, `INVALID_AMOUNT`, `DUPLICATE_ROW`, and `PERIOD_FINALIZED`.

The service resolves employees and salary components only inside the current tenant. Components are referenced by existing master code/ID and their active/effective date and earning/deduction behavior are checked. The preview is server-side and reports input arithmetic only (`Earnings - Deductions`), never final net pay.

## CSV and templates

CSV is the initial supported file format. Required canonical headers are `EmployeeCode`, `ComponentCode`, `Amount`, and `EffectiveDate`; optional headers are `InputType`, `ReferenceNumber`, and `Remarks`. The parser handles quoted commas, escaped quotes, LF/CRLF, UTF-8/BOM, blank values, and streams rows without logging content. Duplicate headers, missing headers, invalid UTF-8, empty files, path-like names, oversized files, and repeated SHA-256 content are rejected. XLSX, executable mappings, SQL mappings, OCR, SFTP, and ERP connectors are deferred.

Templates are tenant-scoped, versioned, and limited to safe column metadata and named transforms. No arbitrary scripts or SQL are accepted.

## Authorization, isolation, audit, and operations

The API exposes template creation/update plus batch creation, upload, validation, preview, paged lines/issues, staged line update/delete, issue/preview/result CSV exports, submit, approve, reject, post, cancel, and history routes under `/api/payroll/input-*`. New `PayrollInput.*` permissions are registered through the canonical permission catalog. Approval uses the existing `IPayrollApprovalGuard`, so maker-checker rules are shared with Payroll. The frontend exposes Batches, New Import, Manual Input, and Templates tabs; actions are permission-gated in addition to backend authorization.

Every entity has tenant filters and composite tenant foreign keys. Batch status changes and upload/validation/post/cancel actions write immutable history. Posted authoritative adjustments are never deleted. Pre-consumption cancellation is retained as batch history; post-consumption historical correction must use Phase 7R.

All lists are deterministically ordered and paged at the API boundary. Posting uses a serializable database transaction plus the unique `(TenantId, SourceType, SourceId)` adjustment identity, so simultaneous posting cannot create duplicate authoritative adjustments; a concurrent loser receives a safe retry/conflict result. Template edits create a new version once a non-draft batch has used the prior version. Line correction clears stale issues, resets the batch to draft, and retains correction history. A posted input that has already affected finalized payroll is not rewritten; Phase 7R remains the correction/reversal handoff.

## Verification status and environment classification

The workload resolver was repaired with elevated `dotnet workload restore HRMS.slnx`; no repository resolver workaround or package change was made. Infrastructure, API, solution, and test-project builds pass with zero errors. SQL Server and MySQL EF pending-model checks both report: `No changes have been made to the model since the last migration.` The synchronized Phase 7V migrations are `20260923070000_AddPayrollInputsPhase7V` in both provider projects, with generated designers and snapshots. Phase 7U migration content was not changed.

Evidence completed in the current environment:

- Focused Phase 7V service tests: 4 passed, 0 failed.
- Payroll fast regression: 138 passed, 0 failed, 0 skipped, 28 seconds.
- Bounded non-provider backend regression (`FullyQualifiedName!~SqlServer&FullyQualifiedName!~MySql`): 1,232 passed, 0 failed, 0 skipped, 2 minutes 52 seconds.
- Frontend TypeScript: passed.
- Frontend Vitest outside the sandbox: 87 files and 609 tests passed in 24.14 seconds.
- Frontend production build outside the sandbox: passed; Vite emitted only the existing large-chunk warning.
- Frontend lint: passed with existing repository warnings.
- Shared provider acceptance on SQLite: 2 passed, including sequential post retry and a 10,000-row pipeline.
- MySQL provider acceptance first run: 1 passed, 0 failed, 0 skipped, 34 seconds.
- MySQL provider acceptance repeat run: 1 passed, 0 failed, 0 skipped, 33 seconds.
- Large-data acceptance: 10,000 rows, 1,000 employees, 1 component, 9,000 valid, 500 invalid, 500 warnings, server-paged lines and filtered issues passed in 10 seconds.
- Independent-context post proof: 2 passed; simultaneous post produced exactly one `PayrollAdjustment` and one post-history event, with total amount preserved at 100. Sequential post retry also remained exactly once.

The sandbox-only Vitest/build `spawn EPERM` is therefore classified as environment-specific. The supported concurrency and retry-safety evidence is complete; SQL Server provider acceptance was not run and remains the final operator gate.

Retry-safety evidence is now complete: `PayrollInputsRetrySafetyTests` discovers exactly eight tests and passes 8/8 in 11.780 seconds. Upload replay is safe through same-batch SHA-256 recognition without restaging or duplicate upload history; lifecycle retries use state guards; posting retains one source-line adjustment and one post-history event; cancellation is stable; and repeated cancellation after consumption returns the Phase 7R-required conflict without rewriting finalized Payroll. The generic request-idempotency-key subsystem remains out of scope. The supported concurrency matrix remains 7/7, with its latest rerun passing 7/7 in 8 seconds after the upload replay fix. SQL Server provider acceptance remains unexecuted and is the final operator gate.

Post-retry verification after the upload replay fix: Phase 7V acceptance passed 2/2 in 29 seconds; MySQL provider acceptance passed first run 1/1 in 1 minute 1 second and repeat run 1/1 in 1 minute 1 second; Payroll fast passed 138/138 in 1 minute 19 seconds; bounded non-provider backend regression passed 1,249/1,249 in 4 minutes 2 seconds; Infrastructure, API, and solution builds passed; and `git diff --check` passed. No persistence model or migration change was made for retry safety. SQL Server provider acceptance remains the only unexecuted gate.

SQL Server migration initialization exposed a Phase 7V multiple-cascade-path error: `PayrollInputBatch -> PayrollInputValidationIssue` cascaded directly while `PayrollInputBatch -> PayrollInputLine -> PayrollInputValidationIssue` also cascaded through the issue's line relationship. The provider-neutral `PayrollInputValidationIssue -> PayrollInputLine` relationship now uses `DeleteBehavior.NoAction`; the current SQL Server and MySQL Phase 7V migrations, designers, and snapshots use the matching `NoAction`/`ReferentialAction.NoAction` semantics. Tenant composite keys remain unchanged. SQL Server and MySQL pending-model checks both report no changes. MySQL first/repeat passed 1/1 each (36s and 37s); concurrency passed 7/7, retry-safety passed 8/8, focused Phase 7V passed 2/2, Payroll fast passed 138/138, and bounded non-provider backend passed 1,249/1,249. SQL Server provider acceptance was not executed and remains final operator reverification.

## Deferred scope

Arbitrary Excel formulas, executable or SQL mappings, AI mapping, OCR, SFTP, scheduled inbound integrations, third-party ERP connectors, predictive validation, and automatic rule generation remain deferred.

## Final closure evidence

Final Phase 7V closure evidence is complete. The implementation includes tenant-scoped batches, lines, templates and template columns, validation issues, immutable history, CSV staging, preview and exports, manual line add/edit/delete, maker-checker, lifecycle controls, cancellation, Phase 7R correction boundaries, duplicate-file protection, and canonical PayrollAdjustment posting.

The architectural boundary remains enforced: Phase 7V does not calculate Payroll or mutate finalized Payroll. Approved-but-unposted inputs are ignored by Payroll; posted inputs flow through the existing Phase 7R PayrollAdjustment/input path; finalized historical Payroll remains immutable.

Concurrency evidence: 7/7 passed (validate/edit, submit/edit, approve/reject, approve/approve, post/post, post/cancel, and duplicate-file race). Retry-safety evidence: 8/8 passed (upload, validate, submit, approve, reject, post, cancel, and Phase 7R handoff). Independent-context simultaneous posting proved exactly one PayrollAdjustment, one post-history event, and the correct amount once. Generic request-idempotency keys are not implemented or required for Phase 7V; operation-specific protections use file hashes, lifecycle guards, EF concurrency tokens, source-line uniqueness, transaction boundaries, and the Phase 7R boundary.

Large-data acceptance passed with 10,000 rows, 1,000 employees, 1 component, 9,000 valid rows, 500 invalid rows, 500 warnings, and a 12-second duration. MySQL shared provider acceptance passed first run 1/1 in 36 seconds and repeat 1/1 in 37 seconds. SQL Server shared provider acceptance passed operator reverification on the same database: first run 1/1 (8.8065-second test time, 9.0-second overall) and repeat 1/1 (7.6401-second test time, 7.8-second overall) using `--no-build`. No provider-specific business workaround was used.

The SQL Server multiple-cascade-path issue was resolved by changing `PayrollInputValidationIssue -> PayrollInputLine` from `Cascade` to `NoAction`, while preserving tenant composite-key integrity. SQL Server and MySQL pending-model checks report none, and snapshots are synchronized. Payroll fast passed 138/138; the bounded non-provider backend regression passed 1,249/1,249 with zero failures and zero unaccounted tests; frontend verification passed 609/609, TypeScript and production build passed, and lint passed with existing warnings.

SQL Server status: **FINAL OPERATOR REVERIFICATION PASSED**
