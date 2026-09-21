# Phase 7N — Loans & Salary Advances

## Current implementation status

Phase 7N currently contains the initial tenant-scoped loan foundation:

- loan product and salary-advance product types
- effective-dated product version storage
- employee loan requests and lifecycle states
- approval guard integration for loan approval
- manual disbursement recording semantics (no bank transfer)
- zero-interest, flat-interest, and reducing-balance schedule calculation foundation
- installment snapshots and immutable loan event history
- manual repayment, partial-prepayment, and early-closure service operations with repayment history
- final-settlement calculation/finalization recovery lines for active loans
- payroll recovery resolver for due active installments
- explicit `LoanRecovery` deduction components with loan/installment traceability
- payroll-sourced repayment persistence and outstanding-balance updates after successful calculation
- linked-employee ESS loan API endpoints under `/api/me/loans` and `/api/me/loan-products`
- payroll accounting mapping types for loan disbursement/recovery/final-settlement sources using the existing GL engine
- tenant-scoped paginated loan register endpoint at `/api/payroll/loans/register`
- product update and effective-dated version endpoints for the product-management workflow
- initial administrative loan/product and linked-employee loan views
- tenant-scoped permissions and initial administrative APIs
- shared provider acceptance infrastructure for the persisted loan lifecycle, including MySQL and SQL Server test classes

Loan numbers and all monetary values are tenant-scoped and use `decimal`. Historical schedules are persisted snapshots; they are not silently recomputed after activation.

## Historical deferred scope

The following Phase 7N work remains before the phase can close:

- complete insufficient-net-pay policy coverage and dedicated regression tests
- provider/runtime tests for the persisted loan lifecycle, including MySQL disbursement concurrency coverage
- broader repayment/prepayment/closure and final-settlement integration tests
- Final Settlement loan accounting now generates a balanced journal from finalized `LoanRepayment` rows through `LoanFinalSettlementRecovery`, with optional `LoanInterestRecovery`, tenant-scoped mappings, duplicate protection, and settlement/loan source traceability
- complete admin/product/ESS UI workflows beyond the initial views
- register CSV export and broader reporting coverage
- complete product version management UI and overlap-validation presentation
- SQL Server/MySQL provider acceptance and runtime verification (completed; final evidence below)
- lifecycle, schedule, recovery, security, and concurrency tests
- SQL Server operator runtime verification (completed; final evidence below)
- completion of the remaining broad non-provider regression group; bounded Group F evidence is now complete below

The current disbursement operation records a manually confirmed disbursement/activation event only. It does not submit a payment or call a bank API.

## Accounting integration

Loan recovery payroll components use the existing `PayrollGLMapping` configuration with these mapping types:

- `LoanDisbursementReceivable`
- `SalaryAdvanceDisbursementReceivable`
- `LoanPayrollRecovery`
- `LoanInterestRecovery`
- `LoanFinalSettlementRecovery`

Payroll loan recovery journals create balanced debit/credit lines with `LoanRepayment` source classification and installment/loan source identifiers. The focused payroll-recovery accounting test now passes, including balance and source traceability. No separate accounting engine or hardcoded GL account is introduced. Manual disbursement and final-settlement journal generation still require their corresponding configured event path and dedicated acceptance coverage.

The MySQL provider initially exposed a disbursement failure: newly generated `LoanInstallment` rows were tracked as modified, causing MySQL to issue zero-row updates for new keys and surface a misleading `DbUpdateConcurrencyException`. `RecordDisbursementAsync` now explicitly adds a newly generated schedule before the same atomic save. The `EmployeeLoan.ConcurrencyVersion` predicate remains enabled and the stale-row protection is unchanged. MySQL provider acceptance passed on the first run and on repeatability.

## Verification recorded for this pass

- solution build: PASS
- Payroll-focused backend regression: 62 passed, 0 failed, 0 skipped
- Payroll fast regression: 69 passed, 0 failed, 0 skipped
- six-scenario concurrency suite: 6 passed, 0 failed, 0 skipped, approximately 5 seconds
- SQLite large-data register acceptance: 100 loans, two pages of 50, reconciliation and tenant isolation PASS
- frontend production build: PASS
- focused Phase 7N frontend suite: 5 passed, 0 failed, 0 skipped
- latest full frontend suite: 80 files, 596 passed, 0 failed, 0 skipped
- backend Phase 7N focused baseline: 69 passed, 0 failed, 0 skipped
- Admin lifecycle, ESS lifecycle, and Loan Register workflows are implemented and covered by the focused frontend suite
- SQL Server model pending-change check: PASS
- MySQL model pending-change check: PASS
- `git diff --check`: PASS

Provider runtime acceptance passes for MySQL and SQL Server first-run and repeatability. Both providers execute the shared `PayrollLoansProviderAcceptance.RunAsync` path. The Final Settlement accounting path and six-scenario concurrency suite are green. Admin/ESS list, detail, filter, pagination, lifecycle visibility, and schedule workflows are wired through existing APIs.

## Deferred scope

## Final Settlement accounting

`POST /api/payroll/final-settlements/{id}/accounting/generate` uses the existing payroll accounting engine. It accepts only finalized settlements with persisted Final Settlement loan repayments, resolves tenant-scoped effective mappings, posts settlement clearing as the debit, loan receivable as the principal credit, and the configured interest credit when present. Missing mappings, missing accounts, wrong-tenant configuration, non-finalized settlements, and duplicate active journals fail before persistence. Journal batches link to the settlement and lines retain `FinalSettlement` and `EmployeeLoan` source records. Focused tests cover the balanced principal/interest path, missing-mapping rollback, duplicate protection, and tenant isolation.

The implementation adds current Phase 7N SQL Server and MySQL migrations named `AddFinalSettlementAccountingJournal*`; earlier Phase 7A–7M migrations remain unchanged.

## Closure evidence

- concurrency: 6 passed, 0 failed, 0 skipped
- focused Payroll regression: 69 passed, 0 failed, 0 skipped
- focused frontend loan workflow tests: 5 passed, 0 failed, 0 skipped
- Employee Address failure isolation: the `AddressDetailsForm.test.tsx` test `when same-as-current is set, saving sends the permanent row mirroring the current one and persists the flag` passed alone (1 passed, 4 skipped), and the complete related file passed (5 passed). The subsequent full suite passed 596/596. The earlier failure was an unrelated/flaky suite interaction, not a Phase 7N regression; no Employee production code was changed.
- MySQL provider first run and repeatability: 1 passed each
- seeded tenant regression: 41 passed after adding Phase 7N loan permission ids
- bounded non-provider evidence: Auth/Authorization 138 passed; Employee/Masters 196 passed; Leave 255 passed; Tenant/Platform 242 passed; Payroll 62 passed; Group F 372 passed. These groups intentionally overlap on a small number of shared Payroll/Master tests, so their totals are not summed as a unique overall count. Group F completed through bounded batches; the slow Attendance large-data export was isolated as runner/tooling behavior, not a product defect.
- Group F closure: 41 intended classes completed in bounded batches, 372 passed, 0 failed, 0 skipped. The inventory was 25 Attendance classes (`AttendanceAdminCorrectionHttpTests`, `AttendanceAdminCorrectionTests`, `AttendanceApplicabilityCrudTests`, `AttendanceApplicabilityEndpointTests`, `AttendanceDefaultShiftTests`, `AttendanceFoundationTests`, `AttendanceHttpEmployeeHarnessTests`, `AttendanceHttpManagerHarnessTests`, `AttendanceHttpWorkflowHarnessTests`, `AttendanceLeaveIntegrationTests`, `AttendanceMonthlyHttpTests`, `AttendanceMonthlyProcessorTests`, `AttendancePeriodLockTests`, `AttendancePhase4HttpOwnershipTenantTests`, `AttendancePunchProcessingTests`, `AttendanceReadServiceTests`, `AttendanceReportHttpTests`, `AttendanceReportTests`, `AttendanceRosterAuthorizationEndpointTests`, `AttendanceRosterCalendarOverrideTests`, `AttendanceRosterQueryTests`, `AttendanceRosterUploadClassificationTests`, `AttendanceShiftApplicabilityTests`, `AttendanceWorkflowAuthorizationHttpTests`, `AttendanceWorkflowTests`) and 16 provider-neutral support classes (`AuditTimestampTests`, `BrowserAcceptanceScriptTests`, `CatalogProviderSelectionTests`, `ConcurrencyTokenMechanicsTests`, `CorsOriginPolicyTests`, `CorsPolicyTests`, `DatabaseInitializerTests`, `DatabaseProviderRoutingTests`, `DesignTimeProviderFactoryTests`, `ForwardedHeadersTests`, `MigrationDiscoveryTests`, `Msg91SmsOtpSenderTests`, `ShardConnectionStringTests`, `ShardContextTests`, `SmtpEmailSenderTests`, `UniqueConstraintTests`). The two SQL Server/MySQL cases in `ProviderCombinationRoutingTests` were excluded as legitimate provider-routing coverage outside the non-provider scope; the class also had two provider-neutral cases, which passed.
- Group F hang isolation: the no-output batch reduced to `AttendanceReportTests.Attendance_report_export_at_row_limit_succeeds` / `Attendance_report_export_at_row_limit_succeeds`. It seeds 50,000 SQLite employee/day rows; isolated execution passed in approximately 25 seconds. The companion over-limit test passed in approximately 57 seconds. VSTest diagnostics showed normal test completion, test-host exit, and runner disposal; no fixture, DbContext, hosted-service, timer, process, WebApplicationFactory, or database-cleanup hang was observed. Classification: test-runner/tooling issue caused by slow SQLite large-data execution and delayed console output in the parallel batch, not a Phase 7N defect. No fix was required.
- Combined bounded non-provider evidence: Group A 138 + Group B 196 + Group C 255 + Group D 62 + Group E 242 + Group F 372 = 1,265 passed, 0 failed, 0 skipped across the intended bounded evidence. Group totals intentionally retain the small overlaps documented above.
- Final SQL Server provider evidence: `SqlServerPayrollLoansIntegrationTests` passed on the first run (1 passed, 0 failed, 0 skipped) and passed on repeatability against the same disposable database (1 passed, 0 failed, 0 skipped). Provider issues: none. The test uses the shared `PayrollLoansProviderAcceptance.RunAsync` path; no SQL Server-specific business workaround was required. SQL Server coverage includes product creation/update, product-version overlap validation, Employee Loan lifecycle and maker-checker approval, schedule persistence, payroll recovery, LoanRepayment and outstanding updates, manual repayment, partial prepayment, early closure, Final Settlement recovery/accounting, Loan Register, tenant isolation, cross-tenant denial, cleanup, and repeatability. SQL Server and MySQL provider parity is complete.
- frontend TypeScript: PASS; production build: PASS; lint: PASS with existing warnings
- backend build: PASS; solution build: PASS; `git diff --check`: PASS

Phase 7N does not implement external lending platforms, credit bureaus, live bank disbursement, floating benchmark-linked rates, penalty engines, collections/legal recovery, EMI direct debit, or a generalized finance subledger.
