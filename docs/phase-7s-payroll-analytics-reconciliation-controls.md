# Phase 7S — Payroll Analytics, Reconciliation & Operational Controls

## Architecture

Phase 7S is an evidence layer over persisted `PayrollRun`, `PayrollResult`, result-component, bank-advice, and accounting records. It does not recalculate payroll, change PayrollResult values, rewrite payslips, mutate bank advice, edit journals, or perform automatic corrections.

`PayrollAnalyticsService` provides run overview and summary, persisted control totals, run-to-run employee/component variance, effective-dated variance controls, pre/post reconciliation generation, immutable finding evidence, acknowledgement/resolution actions, a tenant-scoped exception register, tenant-scoped control administration, paginated findings, and read-only summary/control-total/variance/finding/exception CSV exports.

## Snapshots and reconciliation

`PayrollAnalyticsSnapshot` captures the persisted run totals and source version used for an analysis. `PayrollReconciliation` is versioned per run and type; regeneration creates a new version rather than rewriting earlier evidence. Findings retain control code, severity, configured action, metric, values, difference, actor actions, and resolution references.

Variance percentages use decimal arithmetic. A zero prior value is represented as `NewValue` rather than an invalid percentage. Controls are tenant-scoped, effective-dated, optionally run-type scoped, and support absolute or percentage thresholds. Severity and action are configuration values; analytics does not automatically block payroll unless the existing lifecycle explicitly consumes such a control.

The initial reconciliation checks include persisted-result availability, the existing gross-minus-deductions-equals-net control equation, negative net pay, zero-net warnings, and configured run-level thresholds. Bank, accounting, statutory, reimbursement, loan, variable-pay, adjustment, and Final Settlement checks remain source-specific extensions over the same persisted evidence model; they never become alternate calculation engines.

## UI and API

The analytics overview is available at `Payroll > Analytics & Controls` and is permission protected. API surfaces provide run overview, employee/component variance, pre/post reconciliation generation, finding acknowledgement/resolution/exception acceptance, and effective-dated control management. The dashboard is read-only with respect to financial payroll data.

## Provider and regression plan

SQL Server and MySQL use `PayrollAnalyticsProviderAcceptance.RunAsync`, covering the migration model, control, snapshot, pre/post reconciliation, versioned regeneration, paginated finding query, CSV export, tenant isolation, and cleanup. SQLite focused tests verify tenant scoping, payroll immutability, CSV escaping/header behavior, and representative bank/accounting mismatch evidence. Provider runtime and the complete Phase 7S regression remain the final verification gates.

## Verification evidence

- Phase 7S focused backend before the final bounded additions: 1 passed, 0 failed, 0 skipped.
- Final analytics/reporting/source/concurrency bounded suite: 10 passed, 0 failed, 0 skipped.
- Payroll fast after the additions: 118 passed, 0 failed, 0 skipped.
- MySQL provider acceptance: 1 passed, 0 failed, 0 skipped; the acceptance test executes first-run and same-database repeatability.
- Non-provider backend regression prior to the final bounded additions: 1,172 passed, 0 failed, 0 skipped, excluding provider-runtime tests and the documented slow `AttendanceReportTests` runner. A subsequent broad command was terminated because its exclusion filter still selected SQL Server provider tests; it is not treated as passing evidence.
- Frontend: 605 passed, 0 failed, 0 skipped; TypeScript and production build passed; lint passed with existing repository warnings.
- Backend and solution builds passed; EF reported no pending model changes for SQL Server or MySQL; `git diff --check` passed.
- SQL Server operator runtime was intentionally not rerun in this pass and remains required because the read-only source reconciliation service and shared acceptance helper changed.

The provider acceptance regression fixed during verification is versioned analytics snapshot identity: each reconciliation generation captures its own reconciliation version, preserving prior evidence while making retries deterministic. New Phase 7S permission seed IDs are also included so existing seeded-tenant regressions remain compatible.

## Final operator evidence

- SQL Server migration: `20260922050332_AddPayrollAnalyticsPhase7S`; MySQL migration: `20260922050406_AddPayrollAnalyticsPhase7S`. Both provider snapshots correspond and EF reports no pending model changes.
- SQL Server `SqlServerPayrollAnalyticsIntegrationTests`: first run passed 1/1, 0 failed, 0 skipped; test time 17.0902 seconds and overall execution approximately 17.3 seconds.
- SQL Server repeatability against the same disposable database passed 1/1, 0 failed, 0 skipped; test time 6.6835 seconds and overall execution approximately 6.9 seconds.
- MySQL provider execution passed 1/1, 0 failed, 0 skipped on both final bounded reruns (35 seconds and 34 seconds reported by the test runner); the test itself exercises the shared acceptance twice against its disposable database. Provider issues were none.
- Both provider classes call `PayrollAnalyticsProviderAcceptance.RunAsync`; no provider-specific business workaround exists.
- SQL Server and MySQL migrations are synchronized and both EF model checks report no pending model changes.
- Phase 7S focused backend: 1 passed, 0 failed, 0 skipped.
- Dedicated Phase 7S bounded suite: 10 passed, 0 failed, 0 skipped before the latest large-data assertion refinement; the latest dedicated large-data test passes independently. It covers concurrent generation/finding actions, regeneration/control races, CSV escaping, representative bank/accounting mismatch evidence, and a 500-employee, two-run, 1,000-result paging/variance acceptance with component variance and threshold finding checks.
- Payroll fast: 118 passed, 0 failed, 0 skipped.
- Non-provider backend: 1,172 passed, 0 failed, 0 skipped, excluding provider-runtime tests and the documented slow `AttendanceReportTests` runner.
- Focused frontend: 1 passed, 0 failed, 0 skipped; full frontend: 605 passed, 0 failed, 0 skipped.
- TypeScript and production build passed; lint passed with existing warnings; backend and solution builds passed; `git diff --check` passed.
- Dedicated Payroll-focused coverage is represented by Payroll fast; no separate run total was recorded.

The prior SQL Server operator gate was green for the older acceptance path. Because the shared helper now also asserts paginated findings and CSV exports, and the service contains post-payroll source checks, that evidence is stale and requires operator reverification.

## Scope boundary and closure status

### Latest pre-operator verification

Historical Payroll dimensions are now persisted on `PayrollRunEmployee` as nullable `DepartmentId` and `CostCenterId`. `PayrollRunService.PrepareAsync` captures the effective employment record at the Payroll period end; later transfers do not rewrite prior snapshots. Missing historical dimensions remain explicitly `UNCLASSIFIED` / `Historical dimension unavailable` and are never resolved from current employee data.

Dedicated historical-dimension tests pass 2/2. They cover period-end selection, later transfer immutability, later-run capture, snapshot-based Department/Cost Center grouping, null-dimension grouping, stable ordering, and tenant isolation. The extended large-data test passes 1/1 with 500 employees, two runs, 1,000 results, two Departments, two Cost Centers, run/control totals, Department and Cost Center aggregate reconciliation, component variance, two 50-row pages, and tenant isolation.

The current Phase 7S migration sequence is:

- SQL Server: `20260922050332_AddPayrollAnalyticsPhase7S`, `20260922081107_AddHistoricalPayrollDimensionsPhase7S`.
- MySQL: `20260922050406_AddPayrollAnalyticsPhase7S`, `20260922081130_AddHistoricalPayrollDimensionsPhase7S`.

The follow-up migrations contain the two nullable columns and their run/dimension indexes. Both provider model checks now report no pending model changes. The MySQL provider acceptance was rerun twice after this migration correction: 1/1 each run, with the shared acceptance itself executing its same-database repeatability path; observed outer durations were approximately 41 seconds and 33 seconds.

The expanded shared acceptance now persists and verifies a Payroll run/result, historical Department and Cost Center snapshots, run summary, control totals, Department/Cost Center summaries, representative finding acknowledgement/resolution, and the new CSV surfaces for both provider classes. It remains intentionally representative rather than a replacement for the focused source matrix.

Phase 7S remains **NOT COMPLETE**. The focused analytics/regression set currently passes 14/14, and the historical/large-data additions pass, but the complete source-specific reconciliation matrix (including exact, missing, duplicate, and Final Settlement paths) and final affected Payroll-fast/non-provider regression evidence are not yet closed. SQL Server operator verification has not been run.

### Final bounded reconciliation verification

The read-only reconciliation service now records missing active bank advice, missing active payroll journals, missing statutory return sources, Final Settlement line-total mismatches, and duplicate Final Settlement source identifiers. It continues to retain source records unchanged.

Focused source reconciliation evidence is 3/3. It covers exact bank/accounting parity, missing bank/journal sources, Final Settlement mismatch detection, and PayrollResult/source immutability. The complete eight-source matrix remains open: dedicated exact/missing/duplicate coverage for statutory PF/ESI/PT/TDS, reimbursements, loans, variable pay, payroll adjustments, and the full Final Settlement source taxonomy still requires closure.

Final bounded verification after the reconciliation changes:

- Analytics, concurrency, large-data, reporting, historical-dimension, and source tests: **16 passed / 0 failed / 0 skipped**, approximately 7 seconds.
- Payroll fast: **122 passed / 0 failed / 0 skipped**, approximately 25 seconds.
- Non-provider backend filter (`SqlServer`/`MySql`/`AttendanceReport` excluded): **1,177 passed / 0 failed / 0 skipped**, approximately 2 minutes 30 seconds. The known AttendanceReport runner delay was excluded by the documented bounded filter and is not claimed as product evidence.
- MySQL provider acceptance after reconciliation changes: **1/1 passed** twice, approximately 33 seconds and 34 seconds. The test itself exercises shared acceptance repeatability against its disposable database.
- SQL Server operator acceptance remains intentionally unrun and is still required after the source-reconciliation implementation is final.

The delivered foundation covers persisted run overview, employee/component variance, effective-dated run controls, versioned pre/post reconciliation evidence, deterministic findings, anomaly flags for negative/zero net conditions, acknowledgement/resolution endpoints, tenant isolation, and the initial dashboard. The analytics layer is read/evidence oriented: it does not mutate PayrollResult, payslips, bank advice, accounting journals, statutory registers, or perform automatic corrections. Decimal variance arithmetic handles zero-base values explicitly and controls are configuration-driven.

Post-payroll evidence now performs read-only mismatch checks for active bank advice amount/count/duplicate references, active payroll journal balance/duplication, statutory result versus return-source totals, and reimbursement/variable-pay/loan/adjustment settlement source totals. A bounded local test verifies bank amount/count and accounting imbalance findings without mutating either source.

The current shared provider helper verifies controls, snapshots, pre/post reconciliation, versioned regeneration, paginated findings, variance/finding CSV generation, tenant isolation, cleanup, and repeatability. Added read-only surfaces include run summary, control totals, exception register, and their CSV exports. Final dedicated suite evidence remains: concurrency 7/7 passed in approximately 7 seconds, and large-data 1/1 passed in approximately 6 seconds with 500 employees, two runs, 1,000 results, two 50-row pages, component variance, threshold findings, and tenant isolation. Dedicated source coverage is still bounded: bank/accounting mismatch evidence is covered locally, while exact-match and source-specific statutory, reimbursement, loan, variable-pay, adjustment, Final Settlement, department/cost-center reports, and expanded provider parity remain follow-up work. The SQL Server operator must rerun first and repeatability against the same disposable database only after these gaps are closed.

### Latest bounded verification

The reconciliation service now also records balanced-accounting total mismatches, statutory mismatches by persisted statutory type, duplicate reimbursement claim-line settlements, duplicate variable-pay award settlements, duplicate loan-installment recoveries, and duplicate payroll-adjustment applications. These checks are read-only and preserve the source records.

The focused source test class now passes **5/5**. It covers the existing exact bank/accounting path, missing bank/journal evidence, individual missing reimbursement/loan/variable-pay/adjustment source findings, exact Final Settlement line reconciliation, duplicate Final Settlement source detection, and PayrollResult immutability. This is still bounded evidence: the full statutory PF/ESI/PT/TDS persisted matrix and all exact/missing/duplicate source records requested for every source are not yet represented as separate focused cases.

After the reconciliation changes, the provider-excluded Phase 7S bounded group passed **18/18** in approximately 7 seconds, including the seven-scenario concurrency suite. Payroll fast passed **124/124** in approximately 25 seconds. The final non-provider backend filter (excluding SQL Server/MySQL provider classes and the known AttendanceReport runner) passed **1,179/1,179** in approximately 2 minutes 39 seconds. The MySQL provider evidence remains 1/1 on both final runs (approximately 33 and 34 seconds); no SQL Server database run was performed. The model checks remain clean for both providers, solution build passes, and `git diff --check` passes.

Phase 7S remains **NOT COMPLETE**. SQL Server operator reverification is still prohibited until the complete eight-source reconciliation matrix and full Final Settlement coverage are implemented and evidenced.

### Explicit source-matrix status

| Source | Exact | Missing | Mismatch | Duplicate | Tenant isolation | Evidence status |
|---|---|---|---|---|---|---|
| Bank Advice | PASS | PASS | PASS | PASS | PASS by tenant-scoped query | Focused evidence present; held/excluded semantics remain not applicable because the persisted payment lifecycle has no Held status |
| Accounting | PASS | PASS | PASS | PASS | PASS by tenant-scoped query | Reversal/adjustment linkage validation added; negative linkage fixture remains open |
| Statutory | PASS for persisted PF/ESI/PT/TDS exact fixture | PASS | Type-specific mismatch logic implemented | Not applicable to current return-source uniqueness constraint | PASS by tenant-scoped query | Separate negative PF/ESI/PT/TDS mismatch fixtures remain open |
| Reimbursement | PASS | PASS | PASS | PASS | PASS by tenant-scoped query | Real persisted exact settlement fixture added |
| Loans | PASS | PASS | PASS | PASS | PASS by tenant-scoped query | Real persisted exact repayment/installment fixture added |
| Variable Pay | PASS | PASS | PASS | PASS | PASS by tenant-scoped query | Real persisted exact award/settlement fixture added |
| Payroll Adjustment | PASS | PASS | PASS | PASS | PASS by tenant-scoped query | Real persisted exact application fixture added; reversal-linkage case remains open |
| Final Settlement | PASS including cross-source composition | PASS through empty-line mismatch | PASS | PASS | PASS by tenant-scoped query | Persisted gratuity/leave/notice/cross-source composition added; category-specific negative fixtures remain open |

The source-focused class now passes **13/13**. The explicit matrix is recorded here to prevent aggregate test totals from being mistaken for complete eight-source coverage.

The latest provider-excluded Phase 7S group passes **23/23** in approximately 8 seconds. Payroll fast passes **129/129** in approximately 27 seconds. The previously verified non-provider backend evidence remains **1,179/1,179**, because the latest changes added tests/documentation only and did not change production code after that run. SQL Server remains unrun and is still the final operator gate only after the open matrix rows above are closed.

### Latest source-matrix verification

The source-focused class now passes **13/13**. Added evidence includes real persisted exact settlement identities for reimbursement, loan repayment/installment, variable pay award/settlement, and payroll adjustment application; a persisted four-type statutory fixture for PF, ESI, PT, and TDS source aliases; and a Final Settlement composition containing gratuity, leave encashment, reimbursement, variable pay, notice pay, notice recovery, loan recovery, and adjustment lines. The service also validates accounting journal reversal/adjustment source links and maps statutory aliases without applying rates or recalculating tax.

The latest bounded Phase 7S group passes **26/26** in approximately 10 seconds. Payroll fast passes **132/132** in approximately 25 seconds. The final non-provider backend filter passes **1,187/1,187** in approximately 2 minutes 36 seconds, excluding provider-runtime tests and the known AttendanceReport runner. MySQL provider acceptance passes **1/1** on both final runs, with outer durations approximately 33.7 and 32.4 seconds. Backend and solution builds pass; `git diff --check` passes.

Remaining matrix rows are explicitly limited to separate negative statutory PF/ESI/PT/TDS mismatch fixtures, accounting reversal execution/linkage negative coverage, payroll-adjustment reversal negative coverage, and separate Final Settlement missing/mismatch/duplicate fixtures for each gratuity/leave/notice/cross-source category. These are not represented as complete separate evidence yet. SQL Server operator reverification remains deferred.

### Latest negative-path verification

Category-aware Final Settlement checks now use persisted linked `GratuityCalculation`, `LeaveEncashmentCalculation`, and `NoticeSettlementCalculation` records to detect missing or mismatched Final Settlement lines without recalculating gratuity, leave, or notice values. Accounting reversal/adjustment journal links are also checked read-only, and statutory source aliases remain configuration/persistence-driven.

Final source-focused tests pass **13/13**. The provider-excluded Phase 7S bounded suite passes **26/26**. Payroll fast passes **132/132**. Non-provider backend passes **1,187/1,187** with the documented provider/AttendanceReport exclusions. MySQL provider acceptance passes **1/1** twice after the latest service changes (approximately 34s and 33s). Backend and solution builds pass and `git diff --check` passes.

Phase 7S remains **NOT COMPLETE**: separate negative fixtures for statutory contribution/count/type mismatches, orphan/wrong accounting and adjustment reversals, and category-specific Final Settlement missing/mismatch/duplicate cases are still required before SQL Server operator reverification.

### Final Settlement negative-category verification

Category-aware Final Settlement reconciliation now resolves persisted source identities for reimbursement settlements, loan repayments, variable-pay settlements, and PayrollAdjustmentApplications. Missing source, amount mismatch, and duplicate-source fixtures are read-only and preserve the underlying source records.

- Final Settlement source class: **19 passed / 0 failed / 0 skipped**.
- Final provider-excluded Phase 7S bounded group: **32 passed / 0 failed / 0 skipped**.
- Payroll fast: **138 passed / 0 failed / 0 skipped**.
- Non-provider backend: **1,193 passed / 0 failed / 0 skipped**, excluding SQL Server, MySQL, and the known AttendanceReport runner.
- MySQL provider first/repeat: **1 passed / 0 failed / 0 skipped** each; test durations approximately 33 and 34 seconds.
- Backend/solution build: **PASS**; existing analyzer warnings only.
- `git diff --check`: **PASS**.

Final Settlement negative matrix:

| Category | Missing | Mismatch | Duplicate | Tenant/read-only |
|---|---|---|---|---|
| Reimbursement | PASS | PASS | PASS | PASS |
| Loan | PASS | PASS | PASS | PASS |
| Variable Pay | PASS | PASS | PASS | PASS |
| Payroll Adjustment | PASS | PASS | PASS | PASS |

The existing gratuity, leave-encashment, notice, and cross-source evidence remains green. Concurrency remains 7/7 and large-data remains green with 500 employees, two runs, 1,000 results, and reconciled Department/Cost Center aggregates. Both provider model checks remain clean; no Phase 7A–7R migrations were modified. No source records are mutated, no recalculation or automatic correction is performed, and SQL Server operator reverification is still pending.

### Latest negative-path verification

The negative-path implementation now includes persisted statutory PF/ESI/PT/TDS mismatch fixtures, read-only accounting reversal-link validation, read-only PayrollAdjustment reversal-link validation, and linked Final Settlement category checks for gratuity, leave encashment, and notice pay. The category checks compare persisted source records to persisted Final Settlement lines and do not recalculate gratuity, leave, or notice values.

- Source reconciliation tests: **16 passed / 0 failed / 0 skipped**.
- Provider-excluded Phase 7S bounded group: **29 passed / 0 failed / 0 skipped**.
- Payroll fast: **135 passed / 0 failed / 0 skipped**, approximately 41 seconds.
- MySQL provider repeatability after the production service change: **1 passed / 0 failed / 0 skipped**, approximately 32 seconds; the preceding first run also passed 1/1.
- Backend and solution build: **PASS** with 0 warnings and 0 errors.
- `git diff --check`: **PASS** (existing line-ending warnings only).

Negative matrix evidence now includes:

| Area | Negative evidence | Status |
|---|---|---|
| PF / ESI / PT / TDS | Persisted per-type amount mismatch findings | PASS |
| Accounting reversal | Orphan reversal journal source link | PASS |
| Payroll adjustment reversal | Reversal adjustment without valid persisted reversal linkage | PASS |
| Final Settlement gratuity | Linked source amount mismatch | PASS |
| Final Settlement leave encashment | Linked source missing from settlement lines | PASS |
| Final Settlement notice pay | Linked source missing from settlement lines | PASS |
| Final Settlement reimbursement/loan/variable-pay/adjustment categories | Existing cross-module exact/missing/duplicate evidence remains; category-specific negative fixtures are not yet independently complete | OPEN |

The isolated final non-provider backend rerun completed with **1,190 passed / 0 failed / 0 skipped** in approximately 2 minutes 19 seconds. SQL Server operator reverification remains prohibited while the remaining category-specific cross-module negative matrix is open. Phase 7S therefore remains **NOT COMPLETE**.

### Final Phase 7S operator closure

SQL Server operator reverification was completed against the same disposable database:

- First run: **1 passed / 0 failed / 0 skipped**; test time **20.6689 seconds**, overall **20.9 seconds**.
- Same-database repeatability with `--no-build`: **1 passed / 0 failed / 0 skipped**; test time **12.1994 seconds**, overall **12.5 seconds**.
- Test: `HRMS.Tests.SqlServerPayrollAnalyticsIntegrationTests.SqlServer_payroll_analytics_provider_acceptance_is_repeatable`.

Final retained evidence is: source reconciliation **19/19**, Phase 7S bounded **32/32**, Payroll fast **138/138**, non-provider backend **1,193/1,193**, MySQL provider first/repeat **1/1 and 1/1**, SQL Server provider first/repeat **1/1 and 1/1**, backend/solution build PASS, and `git diff --check` PASS. Concurrency remains **7/7** and large-data remains PASS for 500 employees, two runs, 1,000 results, and reconciled Department/Cost Center aggregates.

The Phase 7S migration sequence remains SQL Server `20260922050332_AddPayrollAnalyticsPhase7S` followed by `20260922081107_AddHistoricalPayrollDimensionsPhase7S`, and MySQL `20260922050406_AddPayrollAnalyticsPhase7S` followed by `20260922081130_AddHistoricalPayrollDimensionsPhase7S`. Provider snapshots are synchronized, pending model changes are None, and Phase 7A–7R migrations are unchanged. The shared provider helper has no provider-specific business workaround.

Analytics and reconciliation remain read-only: no PayrollResult, payslip, bank advice, accounting journal, statutory register, reimbursement settlement, loan repayment, Variable Pay settlement, PayrollAdjustmentApplication, or Final Settlement source is mutated; no gratuity/leave/notice recalculation, automatic correction, or alternate Payroll engine exists. The repository scan found no SQL operator password added to the Phase 7S changes; the externally used SQL credential must be rotated outside the repository.

SQL Server status: **FINAL OPERATOR REVERIFICATION PASSED**.

Phase 7S status: **COMPLETE**.

## Deferred

Machine-learning anomaly detection, fraud scoring, predictive payroll, workforce forecasting, a data warehouse, external BI, streaming analytics, ERP/bank-statement/regulatory reconciliation, automatic correction, and arbitrary analytics scripting are deferred.
