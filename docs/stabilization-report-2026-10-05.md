# HRMS stabilization review — 5 October 2026

## Assessment

The working tree builds, but it is not yet a reliable release baseline. Confirmation/probation has substantial implementation and tests, alongside incomplete review-round integration, missing dashboard registration, validation defects, and schema discrepancies. Credential-bearing configuration and destructive employment migrations must be resolved before committing or deploying.

This is a source and automated-test review. It does not certify a live employee journey, production startup, or safe upgrade of an existing customer database. No application source was changed, no existing work was discarded or committed, and no application database migration was run manually. Tests use the repository's test fixtures; provider tests can create and clean up their own test databases.

## Repository state

Initial inventory: **36 modified tracked files, 439 untracked files, no staged changes, no deleted files**. Git reported permission warnings for `qa/.pytest_cache/` and `qa/automation/.pytest_cache/`; those directories could not be inventoried fully. Full path inventory is in the ignored `artifacts/stabilization-inventory.txt`.

| Area | Changes and assessment |
|---|---|
| Confirmation/probation | New controllers, contracts, entities, services, validators, configuration mappings, reminder worker, frontend pages/API clients, tests and QA cases. Policy/master/case/decision foundations look substantially implemented; dashboard wiring, review rounds and some workflow controls are incomplete. |
| Employment | Removes duplicate joining/contractual fields, keeps joining date on Employee, makes confirmation date policy-managed when resolution succeeds. Backend/frontend contracts and separation use were adjusted together. Migration safety remains a blocker. |
| Security and integration | New confirmation permissions, role seed grants, routes/navigation, tenant query filters and DbSets. Existing workspaces must actually receive the new grants. |
| Bank master | Separate UI redesign: counts, pagination, modals, import presentation, bank-type filter and CSS. Looks implemented and existing frontend tests pass, but bank-type filtering has a pagination defect. |
| Test/QA updates | Employment and separation fixtures updated, new confirmation tests and QA scripts/cases. A leave-request test has a small fixture change. Historical verification reports are not evidence that today's tree passes. |
| Generated/scratch | 292 files grouped under generated QA report/cache paths. Also `TempInspect/`, `Backend/HRMS.Infrastructure.Persistence/`, `Claude-History/`, `VERIFICATION_RESULTS.md`, `verification_report.txt`, `.claude-nvidia/`, `litellm_config.yaml`, and `Set-MySqlEnv.ps1` require explicit disposition. |

Snapshot comparison against HEAD found 24 added entity types per provider, all confirmation-related, and no removed entity types. The only removed properties on existing entities are the five employment fields listed in the migration findings. This does not certify migration execution or all index/constraint changes.

### Files unsafe to commit without remediation/review

- `Backend/HRMS.API/appsettings.json` and `appsettings.Development.json`: populated database connection strings containing password fields. Previously the corresponding checked-in MySQL/catalog values were empty.
- `Set-MySqlEnv.ps1` and `Claude-History/migration-*.md`: credential-bearing examples/captures. Values were withheld during review.
- `qa/reports/`, `qa/automation/tests/api/reports/`, and `qa/tools/__pycache__/`: generated evidence/cache output. Evidence may contain sensitive request or employee data; do not bulk-stage it.
- `TempInspect/`, the orphan Infrastructure.Persistence directory, local model-proxy configuration and historical verification output: classify before staging. The proxy YAML files use environment references for API keys; no literal key was identified there.
- Existing `.gitignore` covers build output, logs, `.env` variants, database files and many runtime artifacts, but does not cover all newly generated QA report/cache locations or the new credential-bearing scratch files.

Pattern-based inspection is not a guarantee that every secret or personal-data format was detected. No secret values are reproduced in this report.

## Build and test status

Commands were selected from `AGENTS.md`, `HRMS.slnx`, project files, `package.json` and `vite.config.ts`. Installed dependencies were sufficient; successful builds did not require dependency reinstall or restore.

| Check | Command | Result |
|---|---|---|
| Backend build | `dotnet build HRMS.slnx --no-restore` | Pass, 103 warnings, 0 errors. Initial sandbox run failed with no compiler diagnostics; elevated rerun passed. |
| Backend tests | `dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-restore --logger "trx;LogFileName=stabilization.trx"` | Still running at 19:40 local time, approximately 37 minutes after testhost startup. Nine failures observed (listed below); final totals and TRX are not available. The full-scale attendance test remains active. |
| Focused confirmation tests | `dotnet test Backend/HRMS.Tests/HRMS.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~Confirmation"` | 94 passed, 0 failed, 0 skipped. These tests do not cover every finding below. |
| Attendance scale diagnosis | Existing `HRMS_PHASE6F_LARGE_DATA_SCALE=small`, with `--filter "FullyQualifiedName~AttendanceDeviceLargeDataTests" --no-build --no-restore` | 1 passed in 45 seconds. This is a separate diagnostic, not a replacement for the unchanged full-scale suite. |
| Frontend build/type check | `npm run build` | Pass after sandbox retry. This executes `tsc -b` and Vite. |
| Frontend tests | `npm run test:run` | 108 files: 103 passed, 5 failed. 736 tests: 728 passed, 8 failed. |
| Frontend failure recheck | Same five files, `--maxWorkers=1` | 41 tests: 38 passed, 3 failed. App and platform test files passed. |
| Employee timeout diagnosis | EmployeeFormPage test file, `--maxWorkers=1 --testTimeout=20000` | All 16 tests passed. No permanent timeout configuration was changed. |
| Frontend lint | `npm run lint` | 70 warnings. No lint error reported. |
| Diff whitespace | `git diff --check` | Pass; Git warns that three changed files will normalize LF to CRLF. |

Initial Vite build/test startup errors were `spawn EPERM`: an execution environment restriction, not a TypeScript/compiler failure.

### Warning inventory

Full backend build diagnostics and frontend build/lint diagnostics are preserved under ignored `artifacts/stabilization-*-diagnostics.txt`. These retain every emitted warning rather than only representative examples.

- Backend: CS0618 (9), CS9113 (10), CS8602 (28), CS8604 (8), CS9124 (1), xUnit1030 (1), xUnit2013 (9), xUnit2031 (12), xUnit1031 (17), xUnit2002 (1), xUnit2029 (1), xUnit2012 (3), EF1002 (3). Counts are deduplicated because MSBuild repeats warning lines in its summary. EF1002 findings are in test code, not proof of application SQL injection.
- Frontend lint: exhaustive-deps (13), set-state-in-effect (49), only-export-components (3), no-empty-pattern (2), preserve-manual-memoization (2), refs-during-render (1). Most affected source files are unchanged from HEAD; some warnings occur in the new confirmation editor and modified employment form.
- Vite: ineffective dynamic import for `src/api/separationDocuments.ts` (also statically imported), and a chunk exceeding 500 kB after minification. App.tsx statically imports the new confirmation pages.
- Test stderr includes jsdom's `Not implemented: navigation to another Document`; this is a test environment limitation, not evidence of failed browser navigation in production.

The full backend run exceeded 30 minutes. A read-only managed stack snapshot identified `AttendanceDeviceLargeDataTests.Phase6f_device_integration_large_data_acceptance` executing `AttendanceDeviceIntegrationService.SaveIssueAsync` -> `HrmsDbContext.SaveChangesAsync` -> `ApplyAuditAndTenantStamps` -> EF change detection. This is observed CPU work, not a diagnosed deadlock. Per-event saves combine with repeated ChangeTracker.Entries scans; performance requires profiling before changing durability, audit or tenant semantics. The diagnostic tool was installed only under ignored `artifacts/diagnostics-tools`.

### Frontend failures and classification

| Failure | Evidence / diagnosis | Classification |
|---|---|---|
| App anonymous-login/return test | 5-second timeout in full suite; passed with one worker. | Environment/resource-related flakiness; not a reproduced functional defect. |
| EmployeeFormPage personal-details edit | 5-second timeout both initially and with one worker; passed with diagnostic 20-second limit. | Slow test / requires timing investigation. Page and test unchanged from HEAD. |
| EmployeeFormPage add/address save | Initial 5-second timeout; passed with one worker. | Environment/resource-related flakiness. |
| SupervisorSectionForm scheduled L1 display | Expected `Scheduled L1: EMP-003`; fixed fixture effective date is 2026-10-01, which is already past on review date. | Pre-existing date-dependent test defect; page and test unchanged from HEAD. |
| PlatformTenantsPage editable metadata | Initial timeout; passed with one worker. | Environment/resource-related flakiness. |
| PlatformTenantsPage database preview | Initial timeout; passed with one worker. | Environment/resource-related flakiness. |
| PlatformTenantsPage search/status | Initial missing expected row; passed with one worker. | Non-reproduced timing issue. |
| TeamLeaveCalendarPage event button | Fixture absences are September 2026; component opens current month, October 2026. Name appears in side rail but no day-event button exists in October. | Pre-existing date-dependent test defect; page and test unchanged from HEAD. |

Classification is based on source comparison and targeted reruns. A complete separate HEAD checkout/test run was not performed, so unchanged files alone do not rule out all indirect effects of current changes.

### Backend failures observed in the full run

| Test | Exact failure / evidence | Classification |
|---|---|---|
| SqlServerCompOffIntegrationTests.SqlServer_comp_off_provider_acceptance | `$XunitDynamicSkip$` exception: HRMS_SQLSERVER_TEST_CONNECTION absent; reported Failed rather than Skipped. | Environment issue plus pre-existing skip-gate defect. |
| SqlServerAttendanceDeviceSyncRecoveryProviderTests.SqlServer_phase6g_attendance_device_sync_recovery_provider_acceptance | `$XunitDynamicSkip$` exception: missing SQL Server provider connection; reported Failed. | Environment issue plus pre-existing skip-gate defect. |
| SqlServerOvertimeIntegrationTests.SqlServer_overtime_provider_acceptance_preserves_versioned_snapshot_semantics | `$XunitDynamicSkip$` exception: missing SQL Server provider connection; reported Failed. | Environment issue plus pre-existing skip-gate defect. |
| SqlServerAttendanceOperationsIntegrationTests.SqlServer_phase6e_attendance_operations_provider_acceptance | `$XunitDynamicSkip$` exception: missing SQL Server provider connection; reported Failed. | Environment issue plus pre-existing skip-gate defect. |
| SqlServerPayrollReportsIntegrationTests.SqlServer_payroll_reports_provider_acceptance_is_repeatable | `InvalidOperationException: SQL Server integration test connection is not configured.` | Environment issue plus pre-existing missing gate. |
| SqlServerPayrollTaxDeclarationsIntegrationTests.SqlServer_payroll_tax_declarations_provider_acceptance_is_repeatable | Same missing SQL Server connection exception. | Environment issue plus pre-existing missing gate. |
| SqlServerPayrollAnalyticsIntegrationTests.SqlServer_payroll_analytics_provider_acceptance_is_repeatable | Same missing SQL Server connection exception. | Environment issue plus pre-existing missing gate. |
| MySqlSeparationSettlementIntegrationTests.MySql_separation_settlement_provider_acceptance | `TaskCanceledException: A task was canceled.` after 3 minutes 12 seconds, inside MySQL migrations/connection acquisition. TCP endpoint probe succeeded. | Requires further investigation: provider/connection/load issue; no demonstrated schema defect from this exception. |
| DesignTimeProviderFactoryTests.Explicit_mysql_without_connection_fails_clearly | `Assert.Throws() Failure: No exception was thrown`; expected InvalidOperationException. Test clears environment connection but factory also reads appsettings, now populated. | Likely caused by current uncommitted configuration; test also depends on repository configuration instead of isolated input. |

The repository already uses conditional Fact attributes with `Skip` set before execution in other provider tests; reuse that established pattern for absent operator configuration. Configure and run real provider acceptance on dedicated test stores rather than declaring skipped coverage verified.

## Confirmation/probation audit

### Substantially implemented

- Policy CRUD, draft/publish versions, effective ranges, organization applicability, priority/specificity selection and ambiguity detection.
- Rating scales and review questions, including snapshotted question text/options/rating metadata for historical case stability.
- Idempotent case creation, manager/HRBP resolution, draft/submitted/superseded review versions, manager-then-HRBP ordering and approval requirements.
- Decision calculation/history and transactional confirm, extension, not-confirmed and additional-review paths. Terminal decisions use a conditional status update to choose a winner.
- Extension cycles, employment confirmation-date integration, reminder leases/retries/deduplication, tenant scheduler settings and per-tenant worker scopes.
- Permission-guarded controllers and frontend routes; global tenant query filters and many composite tenant foreign keys.

These are implementation observations, not a claim that all paths are correct or production verified.

### Incomplete or inconsistent

- Dashboard controller depends on `IHrConfirmationDashboardService`, which is not registered in Application, Infrastructure or Program. An authorized dashboard request cannot construct the controller.
- ReviewRound exists on cases/decisions/calculations, but not reviewer reviews/responses. Submission and evaluation queries accept old submitted reviews across rounds. Additional Review records its decision/calculation against the incremented round, although the evidence came from the prior round.
- SQL Server snapshot lacks all three ReviewRound properties; no matching SQL Server migration exists. MySQL adds the fields with default 0, while new CLR entities start at 1. Existing records are not backfilled to round 1.
- Proposed default-1 mappings are outside the actual project. They duplicate existing configuration class names; one imports a non-existent EF metadata namespace. Do not move these files wholesale into the project.
- Rating response validation is present and its existing regression test passes. Duplicate question inputs are collapsed during validation but are all persisted by ReplaceResponsesAsync, potentially inflating a score or introducing an earlier unvalidated value for the same question.
- Reviewer writes enforce employee scope but do not ensure the caller is the assigned reviewer. The saved ReviewerEmployeeId can be the assigned employee while SubmittedBy is a different authorized user. This may represent intended HR delegation, but no explicit delegation permission/reason is enforced.
- The frontend chooses manager whenever the user holds both review permissions; there is no HRBP selector. Decision DTO additions for reviewRound are absent from the frontend types and requests, and the case-list/detail DTO does not expose the round at all.
- Reminder emails point to `/confirmation/employees/{employeeId}`, which has no App route. HRBP recipients are all effective tenant HRBPs, including those whose organization scope does not cover the employee.
- Scheduler Take(BatchSize) is applied before policy/window eligibility and uses the same oldest-employee ordering every scan. Older unresolved/overdue employees can permanently starve later eligible employees. Global defaults permit worker interval 15 minutes versus default frequency 60 minutes, but tenants without a settings row do not persist LastRunAtUtc and run every worker tick.
- Automatic-confirmation and confirmation-threshold configuration is saved/displayed but not consumed by the review/decision execution paths. Treat this as an incomplete existing contract, not authorization to invent new automatic behavior.
- Policy resolution ambiguity and missing effective employment are treated as manual fallback in employment/status integration, rather than distinguishing them from NoPolicy. A configuration fault can therefore re-enable manual confirmation edits.

### Security and data limits

No confirmed cross-tenant leak was found in the reviewed confirmation queries: the new entities have query filters, resolvers use tenant checks, and mapped relationships largely use tenant-composite keys. Remaining concerns are reviewer attribution, HRBP notification scope, bulk update/concurrency behavior, and missing constraints for some stored references such as a case's policy-version identity. Database-backed hostile-input and concurrency coverage must be expanded before calling these guarantees complete.

Not Confirmed intentionally changes JobStatus only; it does not terminate the employee. Termination continues through Separation. This should be communicated in the workflow rather than silently treating rejection as termination.

## Broader employee journey

| Stage | Existing support | Verification prerequisite / blocker |
|---|---|---|
| Create employee | Employee code generation, personal details, employment hierarchy and validation exist. | Tenant provisioning, usable masters/code rules, safe schema upgrade and access permissions. |
| Assign access | Account links, role assignments/scopes, permissions and portal provisioning exist. | Linked identity and current role grants; reviewer identity rules require clarification/enforcement. |
| Leave/attendance | Published policies, balances, calendars, shifts/rosters, punches, approvals and period finalization exist. | Effective employment history, policy/balance setup and finalized attendance period. Date-dependent frontend tests prevent a clean baseline. |
| Payroll | Structures/versions, salary assignments, prepared run snapshots, calculations, statutory/loan/reimbursement integrations, approval/output flows exist. | Salary assignment/version and finalized attendance/overtime snapshots; provider acceptance must pass. |
| Separation | Approval, notice, clearance, exit survey/documents and exit execution exist. | Employment record and supported notice settings. Current approval consumes joining date from Employee consistently. |
| Final settlement | Readiness, payroll settlement initiation, idempotent links and closure checks exist. | Approved last-working date, completed required clearance and finalized settlement. MySQL settlement provider test timed out during migrations in this run. |

The entire journey was not executed against a live disposable workspace. Source paths exist; build success and individual tests do not establish full end-to-end success. Missing setup is not automatically a missing feature. QA/browser execution requires a known disposable tenant, suitable actor accounts and verified provider schema, without using real employee records.

## Prioritized blockers

| Priority | Module | Problem | Evidence | Recommended fix | Files affected |
|---|---|---|---|---|---|
| P0 | Configuration/security | Credentials in changes and local captures can be committed accidentally. | Password-bearing connection settings and history/script patterns, values withheld. | Move values to protected environment/user-secret configuration; review captures and ignore appropriate local files; rotate credentials if previously disclosed. Preserve originals until understood. | API appsettings files; Set-MySqlEnv.ps1; Claude-History/migration-*.md; .gitignore |
| P0 | Employment migrations | FirstHiredDate, GroupDateOfJoining and probation fields dropped without preserving their values. | DropJoiningContractualEmploymentFields.Up consists of DropColumn operations; Down restores empty/default fields, not historical values. | Create a reviewed archival/backfill plan, verify conflicts and backups, and test upgrades from populated copies before applying. Do not rewrite already-applied migrations blindly. | SQL Server/MySQL 20261001 DropJoiningContractualEmploymentFields migrations; EmployeeEmployment model |
| P0 | SQLite startup | Existing SQLite databases are automatically deleted/recreated when the current model adds missing tables/columns. | SchemaPreparer.RebuildIfSchemaIsStaleAsync calls EnsureDeletedAsync; it checks provider, not an explicit data-loss opt-in or host environment. New confirmation tables can trigger this existing behavior. | Require an explicit disposable-development-store opt-in; otherwise fail with a migration/configuration message and preserve data. Verify without using a valued database. | Infrastructure/Persistence/SchemaPreparer.cs; DependencyInjection.cs; schema/startup tests |
| P1 | Confirmation schema | Review-round schema differs across providers and old rows start at 0. | SQL snapshot has no ReviewRound; only MySQL AddReviewRound migration exists, defaultValue 0. | Establish migration-application history; align both provider schemas and backfill round 1 safely. | Both migration trees/snapshots; ConfirmationDecision/ConfirmationReviewCase entities/configurations |
| P1 | Confirmation dashboard | Missing service registration causes request activation failure. | Controller constructor needs IHrConfirmationDashboardService; DI lists omit it. | Register scoped service and add authorized endpoint/DI regression coverage. | Application/DependencyInjection.cs; HrConfirmationDashboardController.cs; dashboard tests |
| P1 | Confirmation rounds | Prior-round reviews count toward new-round readiness/scoring. | AdditionalReviewAsync increments only case; BuildEvaluationContextAsync and UpdateCaseStatusAfterSubmitAsync filter case/status only. | Persist round ownership for reviews, use current-round queries and concurrency predicates, preserve history; align frontend contracts. | ConfirmationDecisionService.cs; ConfirmationReviewCaseService.cs; entities/DTOs/mappings/migrations; confirmationReviewCase.ts/detail page |
| P1 | Review validation | Duplicate answers can distort decisions or bypass validation of an earlier duplicate. | Validation groups question IDs and checks only Last(), but persistence loops all original inputs. Existing single-answer rating validation passes. | Reject duplicate question answers before persisting; add hostile duplicate-input coverage without changing valid rating behavior. | ConfirmationReviewCaseService.cs; validators; ConfirmationReviewCaseTests.cs |
| P1 | Review authorization | Caller may submit under another assigned reviewer's identity. | LoadForWriteAsync checks employee scope only; ResolveReviewerEmployeeIdAsync returns assignment without caller comparison. | Define explicit assigned-reviewer/delegation rules; enforce actor identity and audit delegation, with negative endpoint tests. | ConfirmationReviewCaseService.cs; permissions; endpoint authorization tests |
| P1 | Confirmation frontend | Dual-role user cannot select HRBP submission. | reviewer = canManager ? ReportingManager : canHrbp ? Hrbp : null. | Expose authorized reviewer selection and test manager-and-HRBP policy completion. | ConfirmationReviewCaseDetailPage.tsx; frontend review tests |
| P1 | Reminder authorization | Employee details emailed to HRBPs outside their employee scope. | ResolveHrbpRecipientsAsync returns every effective tenant HRBP without scope filtering. | Resolve recipients from scoped assignment coverage; test distinct organization scopes. | ConfirmationReminderNotificationService.cs; ConfirmationHrbpResolver.cs; reminder tests |
| P1 | Scheduler | Later employees can be starved indefinitely. | Ordered Take(BatchSize) precedes policy/window filtering, with no cursor/progress. | Use complete paged candidate traversal or durable progress, and test more employees than BatchSize including old ineligible records. | ConfirmationReminderProcessor.cs; ConfirmationReminderTests.cs |
| P1 | Employment policy integration | Ambiguous configuration silently permits manual confirmation. | Every non-Resolved result becomes Managed=false. | Permit fallback only for intentional NoPolicy; surface ambiguity/missing-history errors consistently. | EmployeeEmploymentService.cs; ConfirmationPolicyService.cs; policy/employment tests |
| P1 | Baseline/provider tests | Provider failures prevent a verified lifecycle baseline. | SQL environment absent; dynamic skips are reported as failures; MySQL migration timeout; design-time missing-connection test no longer throws. | Repair skip gates/config isolation and investigate MySQL connectivity before rerunning migrations on disposable stores. | CompOffProviderAcceptance.cs; SqlServerPayrollReportsIntegrationTests.cs; SqlServerAttendanceDeviceSyncRecoveryProviderTests.cs; DesignTimeProviderFactoryTests.cs; provider fixtures |
| P1 | Attendance ingestion/performance | Full-scale ingestion substantially delays baseline verification and may be unsuitable for operational batch loads. | Full suite exceeded 30 minutes; stack identifies the full-scale device test inside SaveIssueAsync, SaveChangesAsync and repeated audit/tenant change detection. | Profile and reduce repeated change detection/per-event persistence overhead while preserving durable ingestion, checkpoints, audit stamps and tenant enforcement; verify functional and scale tests. This predates the current confirmation changes. | AttendanceDeviceIntegrationService.cs; HrmsDbContext.cs; AttendanceDeviceLargeDataTests.cs |
| P2 | Reminder integration | Email action path has no matching page. | Notification uses /confirmation/employees/...; routes expose /confirmation/review-cases/... only. | Use an existing workspace-bound review route with sufficient case context. | ConfirmationReminderNotificationService.cs; App.tsx; reminder tests |
| P2 | Test determinism | Two frontend tests depend on elapsed fixture dates. | Supervisor Oct 1 date now past; September calendar fixture viewed in October. | Freeze/inject deterministic test time or navigate to fixture month; retain behavior assertions. | SupervisorSectionForm.test.tsx; TeamLeaveCalendarPage.test.tsx |
| P2 | Bank master | Bank-type filter hides pagination and searches only loaded page. | visibleRows = rows.filter(...); pagination only rendered when !bankType. | Implement full server-side filtering or retain truthful pagination/navigation for the loaded-page filter. | BankMasterPage.tsx; banks API/query contract if extending existing filter; bank tests |
| P2 | Policy/scheduler contracts | Stored auto-confirm/threshold settings have no runtime behavior; default cadence not enforced without settings row. | Settings saved in ApplyRule but not executed; MarkTenantRunAsync does nothing without tenant setting. | Document/disable unsupported controls or complete previously intended semantics after agreement; honor configured default cadence. | ConfirmationPolicyService.cs; review/decision services; ConfirmationSchedulerSettingService.cs; ConfirmationReminderProcessor.cs |
| P3 | Cleanup/static analysis | Orphan duplicate configurations, generated reports and 173 compiler/lint warnings. | Files outside project; 103 backend + 70 lint warnings; Vite chunk warnings. | Classify scratch files, improve ignores, address focused warnings without broad refactoring. | Backend/HRMS.Infrastructure.Persistence/; QA output paths; .gitignore; affected test/frontend files |

## Recommended execution order

1. Resolve secret placement and migration data-preservation questions before committing or running startup against existing databases.
2. Reconcile provider migrations and complete a reproducible test baseline; diagnose failures rather than weakening tenant filters or suppressing checks.
3. Apply small confirmation fixes first: DI registration and validation, then reviewer/round correctness with targeted regression coverage.
4. Repair reminder targeting/traversal/routes and dual-role frontend flow.
5. Run both full suites, then perform the employee lifecycle in a disposable tenant with documented prerequisites.

Substantial implementation is intentionally deferred until this diagnostic report is reviewed. No unrelated feature or refactor was introduced.
