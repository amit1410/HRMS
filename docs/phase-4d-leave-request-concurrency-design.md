# Phase 4D.3G — Leave Request SQL Server Concurrency Design

Status: DESIGN ONLY. This document complements [Phase 4D.3F](phase-4d-leave-request-submission-design.md). It does not authorize persistence, migrations, database access, balance reservation, or approval implementation.

## 1. Current evidence

`LeaveRequest` has a unique `(TenantId, EmployeeId, IdempotencyKey)` index and a tenant-aware rowversion. `LeaveRequestDay` has a unique `(TenantId, LeaveRequestId, Date)` index, which prevents duplicate dates inside one request but cannot prevent two requests from claiming the same employee/date. `LeaveRequest` also has `(TenantId, EmployeeId, StartDate, EndDate, Status)`. Request and day relationships are tenant-aware and restrictive. `LeaveRequestEvent` is immutable through the existing `HrmsDbContext` SaveChanges guard.

The current `IHrmsDbContext.BeginTransactionAsync` has no isolation-level overload. Infrastructure config uses SQL Server through `UseSqlServer` without an explicit `EnableRetryOnFailure` execution strategy. No existing `UPDLOCK`, `HOLDLOCK`, `sp_getapplock`, deadlock classifier, or Leave submission conflict handler was found. Existing services generally catch broad `DbUpdateException` values for ordinary CRUD conflicts; submission must use narrower provider/constraint classification.

## 2. Recommended strategy

Use one SQL Server transaction and serialize all request-affecting operations for one `(TenantId, EmployeeId)` by locking the already-existing Employee row with `UPDLOCK, HOLDLOCK` (or the equivalent repository-approved SQL Server locking adapter). The Employee alternate key/index `(TenantId, Id)` gives SQL Server a single stable row to lock, so different employees remain concurrent. All future Leave submission and status-transition paths that can create or release an active request claim must acquire this same employee scope lock before their persistence-sensitive checks.

This is preferable to a permanent active-date unique index because historical Rejected, Withdrawn, and Cancelled requests must remain insertable and queryable. It is also preferable to a guard table for the first slice because it introduces no lifecycle rows, cleanup problem, or new migration. It is intentionally SQL Server-specific; the application should hide the lock operation behind a persistence/concurrency abstraction so another provider can supply its own equivalent or explicitly declare unsupported concurrency semantics.

Serializable isolation alone is not sufficient as a vague instruction: the exact query shape and supporting indexes determine whether SQL Server takes useful key-range locks. The Employee-row lock provides a deterministic logical mutex independent of RequestDay query-plan details. Under this design, the lock is the correctness mechanism and the request indexes are lookup/performance aids.

## 3. Idempotency race

The transaction order is:

1. Resolve authenticated tenant and linked Employee outside the transaction as ordinary read-only preparation.
2. Begin the SQL Server transaction.
3. Acquire the Employee-row scope lock.
4. Read `LeaveRequests` by `(TenantId, EmployeeId, IdempotencyKey)`.
5. If found, compare the stored PayloadFingerprint before any overlap/limit check. Same fingerprint returns the existing request; different fingerprint returns HTTP 409 `IdempotencyConflict`.
6. If absent, rerun validation and perform persistence-sensitive overlap/limit checks while the scope lock is held.
7. Insert one LeaveRequest, its RequestDays, and one Submitted event.
8. Save and commit atomically.

The unique database index remains the final arbiter, including races from any path that misses the pre-read. If two identical requests both reach insertion, one wins. The loser rolls back, reloads the winner by the exact tenant/employee/key, compares fingerprints, and returns the same logical success. A same-key different-payload loser returns deterministic 409. The loser never creates duplicate days or an event. Unique violations must be classified by SQL Server error 2601/2627 and the known idempotency index/constraint name; unrelated unique violations remain ordinary conflicts.

## 4. Overlap race and status lifecycle

After the Employee-row lock is held, query RequestDays for the proposed employee/date set joined to their parent LeaveRequest and accept only parent statuses `PendingApproval` and `Approved`. If any proposed date conflicts, return `Overlap`/HTTP 409. Rejected, Withdrawn, and Cancelled parents do not block. A retry identified as an idempotent replay exits before this check, so a request never conflicts with its own days.

The same Employee-row lock must be acquired by future transitions that change active-request status, especially PendingApproval→Rejected/Withdrawn and Approved→Cancelled. The request and status update then retain history while the next submission observes the committed active/non-active state. A transition that does not claim a new active date cannot create an overlap; serializing it nevertheless gives deterministic visibility and avoids inconsistent limit reads. No status transition may delete historical RequestDays or Events.

The existing RequestDay unique key remains aggregate-local. It is not being reinterpreted as cross-request overlap protection.

## 5. Request-count and quantity limits

Inside the same locked Employee transaction, evaluate only the configured rule for the resolved LeaveType and context. Do not aggregate across LeaveTypes unless the configured rule explicitly defines that scope. Count `PendingApproval` and `Approved`; ignore Rejected, Withdrawn, and Cancelled. Month is the Gregorian `YYYY-MM` of RequestDay.Date, with no timezone conversion. LeavePeriod limits use the independently resolved LeavePeriodId.

Use RequestDays as the authoritative source and sum ChargeableQuantity for quantity limits. A count/quantity check followed by insert is race-prone under ReadCommitted; the Employee-row lock makes all compliant Leave submission paths for that employee serialize the check and insert. Thus a remaining one-request or one-quantity slot can be consumed by only one winner. Different employees may proceed concurrently.

The current schema can support these predicates. Recommended non-unique lookup indexes for scale are `(TenantId, EmployeeId, Status, StartDate, EndDate)` on LeaveRequests and `(TenantId, Date, LeaveRequestId)` on LeaveRequestDays; these are performance recommendations, not a correctness claim or a migration generated in this phase. If query-plan evidence later shows a different shape, it must be reviewed with the locking implementation.

## 6. Isolation, ordering, rollback, and deadlocks

The correctness boundary is one SQL Server transaction with the Employee-row lock acquired before any persistence-sensitive overlap or limit read. The transaction should use the repository's explicit SQL Server lock adapter; `HOLDLOCK` on the Employee key gives serializable semantics for that logical employee scope without locking unrelated employees. Do not mix lock acquisition orders: every submission and relevant transition acquires tenant/employee scope first, then idempotency read, overlap/limit reads, Request insert, Day insert, Event insert, SaveChanges, and commit.

Any validation failure, overlap/limit conflict, unique conflict, day failure, event failure, or commit failure rolls back the transaction. No Request, RequestDays, Submitted event, or future guard row may remain partially persisted.

SQL Server deadlock 1205 is retryable only for the entire transaction, after rollback/disposal. Use a bounded maximum of three attempts with short bounded backoff/jitter. Each attempt reruns the idempotency read and all authoritative validation/persistence checks; no partially tracked graph is reused. If all attempts fail, return the repository's stable `ConcurrencyConflict`/HTTP 409 or 503-equivalent policy rather than exposing SQL text. Idempotency makes a post-commit response-loss retry safe; it does not make an unbounded server retry safe.

The current SQL Server configuration has no explicit EF execution strategy. If `EnableRetryOnFailure` is introduced later, the entire user transaction must run inside `ExecutionStrategy.ExecuteAsync`; manually opening a transaction outside that delegate is invalid for retrying execution strategies. Deadlock retry and execution-strategy retry must be bounded and coordinated, not nested without a total-attempt limit.

## 7. Alternatives considered

### Serializable request-table range locks

Pure `Serializable` queries over RequestDays and parent statuses can protect ranges only when every writer uses the same predicates, indexes, and transaction behavior. Parent-status filtering across a join is plan-sensitive and may lock broad ranges or fail to protect a not-yet-existing date row as intended. It remains useful as a transaction isolation property, but is not selected as the sole invariant mechanism.

### `sp_getapplock`

Not recommended for the first implementation. It can provide a clear `Tenant+Employee` or date/limit lock and avoids row-table coupling, but is SQL Server-specific, requires exact lock-name and transaction-owner discipline, complicates provider tests and diagnostics, and still requires every writer to honor the convention. The existing Employee row is already a tenant-aware stable mutex.

### Active-date guard table

Not required for the first slice. A table such as `(TenantId, EmployeeId, Date, LeaveRequestId)` with an active-only uniqueness strategy would enforce overlap efficiently, but status changes would need atomic insert/delete/update lifecycle handling, historical release correctness, orphan cleanup, and a migration. A simple permanent unique key is invalid because it would block historical non-counting requests. Reconsider a guard table only if employee-wide serialization becomes a throughput bottleneck or a future database-native active-claim invariant is explicitly approved.

## 8. Validation-service boundary

`LeaveRequestValidationService` remains pure and side-effect free. It resolves current authoritative context and builds the fingerprint/RequestDays. A separate submission persistence/concurrency service consumes that result and performs the Employee lock, idempotency replay, persistence-sensitive overlap/limit rechecks, aggregate transaction, and provider-specific conflict handling. Do not add SQL locking to the validator or duplicate policy/rule resolution there.

## 9. First persistence slice

After this design, Unlimited and NoBalanceRequired are safe candidates for implementation using the current schema, provided every submission/status writer follows the Employee-row lock protocol and SQL Server integration tests pass. The first slice must reject Allocated before inserting anything: balance reservation, ledger posting, projection concurrency, and deadlock validation remain outside this phase. No fake availability check or reservation is permitted.

The current schema is sufficient for the first basic persistence slice; no concurrency-hardening migration is required before it can begin. The recommended lookup indexes above may be added later through a separately reviewed migration if performance evidence warrants them. Current schema is not a claim that overlap/limit safety exists automatically; it depends on the required transaction protocol.

## 10. SQL Server integration test plan

Real SQL Server tests are required for:

1. identical concurrent submissions produce one Request, one Day set, and one Submitted event;
2. different payloads sharing a key produce one success and one IdempotencyConflict;
3. same-day overlap races produce one success;
4. PendingApproval and Approved block while Rejected, Withdrawn, and Cancelled do not;
5. status transition and new submission serialize correctly;
6. request-count, monthly quantity, and LeavePeriod quantity last-slot races admit only one winner;
7. day/event failure rolls back the complete aggregate;
8. response-loss retry returns the existing request;
9. bounded deadlock retry behavior;
10. tenant isolation, same-date different-employee concurrency, and independent tenants;
11. rowversion behavior and unique violation classification.

EF InMemory cannot reliably test SQL Server key-range/row locks, lock hints, isolation, deadlocks, SQL error numbers, rowversion, execution strategies, or actual unique-index races. SQLite tests can cover basic transaction composition but cannot substitute for SQL Server concurrency acceptance.

No request persistence, migration, database access, balance reservation, approval, or frontend change is included in Phase 4D.3G.

## Phase 4D.3I SQL Server Validation Results

Status: BLOCKED in this run. The focused SQL Server test class and disposable one-database fixture are present, but no SQL Server run was executed because the required `HRMS_SQLSERVER_TEST_SERVER` configuration was absent. The tests do not fall back to SQLite for concurrency claims.

The fixture uses the existing Phase 3B convention: `HRMS_SQLSERVER_TEST_SERVER` plus `HRMS_SQLSERVER_TEST_AUTH=Integrated`. It creates a uniquely named `HRMS_LeaveRequestConcurrency_<UTC timestamp>_<suffix>` database, applies current migrations, seeds only synthetic tenant-scoped rows, and drops that database during fixture cleanup. It refuses protected/non-owned names and never uses an existing HRMS database.

| Scenario | Result | Evidence / note |
|---|---|---|
| Same key / same payload | BLOCKED | Focused real-SQL test added; SQL Server not configured. |
| Same key / different payload | NOT IMPLEMENTED | Requires configured SQL Server run. |
| Overlapping request race | BLOCKED | Focused real-SQL test added; SQL Server not configured. |
| Different employees remain concurrent | BLOCKED | Deterministic lock test added; SQL Server not configured. |
| Different tenants | NOT IMPLEMENTED | Seed graph is present; submission scenario still requires a configured run. |
| PendingApproval blocks | NOT IMPLEMENTED | No status-transition path exists to seed/operate this scenario in the focused slice. |
| Approved blocks | NOT IMPLEMENTED | No status-transition path exists to seed/operate this scenario in the focused slice. |
| Rejected / Withdrawn / Cancelled do not block | NOT IMPLEMENTED | No status-transition implementation exists. |
| Replay before overlap | NOT IMPLEMENTED | Requires configured SQL Server run. |
| Allocated persists nothing | BLOCKED | Foundation test covers the side-effect-free gate; SQL Server proof requires configured run. |
| Atomic rollback | NOT IMPLEMENTED | No safe existing failure-injection seam was found. |
| SQL Server idempotency unique classification | BLOCKED | Classification code statically targets the named index and SQL Server 2601/2627; real race requires configured run. |
| Same-employee lock blocking | BLOCKED | Focused bounded-time lock test added; SQL Server not configured. |
| Different-employee lock acquisition | BLOCKED | Covered by the same deterministic lock test; SQL Server not configured. |
| Request-limit concurrency | NOT IMPLEMENTED | Current validator rejects unsupported request-limit configurations; no concurrency claim is made. |
| Deadlock 1205 | NOT IMPLEMENTED | 4D.3H retry hardening is not implemented and no deterministic deadlock test was run. |
| Rowversion | NOT IMPLEMENTED | Schema uses SQL Server `rowversion`; status-transition tests remain future work. |

Static verification: `LeaveRequestSubmissionService` begins the transaction before `ILeaveRequestSubmissionLock.AcquireAsync`; `SqlServerLeaveRequestSubmissionLock` uses the same `HrmsDbContext` connection and `CurrentTransaction.GetDbTransaction()`, with parameterized tenant/employee values and `UPDLOCK, HOLDLOCK`. No existing status mutation path was found. `EnableRetryOnFailure` is not configured. Deadlock retry is not implemented.

No migration was generated. No production file, frontend file, public endpoint, balance implementation, approval implementation, or existing database was changed or accessed by this phase.

## Phase 4D.3I.1 SQL Server ConcurrencyConflict Root-Cause Diagnosis

Status: ROOT CAUSE UNKNOWN — blocked before a diagnostic SQL Server submission run could complete in this workspace.

The original focused class contains these three tests:

1. `Same_employee_lock_blocks_until_owner_commits_but_different_employee_proceeds` — the lock-only test; reported passing in the supplied fresh run.
2. `Same_key_same_payload_concurrency_persists_one_request_days_and_event` — reported failing; both calls returned generic `ConcurrencyConflict`.
3. `Overlapping_new_requests_for_one_employee_have_one_winner` — reported failing; both calls returned generic `ConcurrencyConflict`.

Added diagnostic test: `Single_unlimited_submission_persists_request_days_and_event`. It uses the same synthetic graph and fixed `EntitlementMode.Unlimited` validation, and checks one request, one day, one Submitted event, and `PendingApproval`.

The submission service’s generic result is produced by the final `catch (DbUpdateException)` after the lock, idempotency read, second validation, overlap query, aggregate construction, and either `SaveChangesAsync` or transaction commit. The diagnostic observer records the original exception before rollback without exposing it through the application result. It has not produced a SQL Server persistence exception in this workspace because the project build/test host failed before executing the new assembly.

The direct execution of the previously built assembly also could not reach the disposable database: the local SQL Server connection failed during database creation with a sanitized `Microsoft.Data.SqlClient.SqlException` stating that the instance requires encryption but the client machine cannot support it. This is a local test-harness connection failure, not evidence for the supplied `ConcurrencyConflict` root cause. No disposable database was created by that attempt.

Static findings: the lock SQL is parameterized on `TenantId` and `Id`, uses `UPDLOCK, HOLDLOCK`, and assigns the current `GetDbTransaction()` on the same context connection. The lock-only test therefore does not prove the aggregate persistence path. The event has composite tenant-aware FKs to the authenticated User, subject Employee, and LeaveRequest. LeaveRequest uses SQL Server-generated `rowversion`; no client rowversion value is assigned. The idempotency index is `IX_LeaveRequests_TenantId_EmployeeId_IdempotencyKey`. No evidence identifies an FK, rowversion, unique-key, deadlock, or lock error as the supplied failure.

No correction was made. Lock semantics and idempotency semantics are unchanged. No migration was generated. The required next action is to rerun the four-test focused class in an environment where the supplied build command can execute and the disposable SQL Server connection can be established, then capture the observer’s exception type, SQL number, inner message, and stack phase before any production fix.
## Phase 4D.3I.2 SQL Server EmployeeEmploymentHistory FK Fix

The FK configuration is correct and unchanged. `LeaveRequests(TenantId, EmployeeId, EmployeeEmploymentHistoryId)` references the alternate principal key `EmployeeEmploymentHistory(TenantId, EmployeeId, Id)` with `DeleteBehavior.Restrict`.

The disposable fixture seeds Employee A history as `TenantA`, `EmployeeA`, `EmploymentA`, `EffectiveFrom=2020-01-01`, `EffectiveTo=null`, and active employment status. The defect was in the test's fixed validation result construction: constructor arguments were supplied in the wrong order, so `LeavePeriodId` was assigned to `EmployeeEmploymentHistoryId`, while the real history ID was shifted into `LeavePolicyRuleId`. Production `LeaveRequestSubmissionService.CreateRequest` already maps `EmployeeEmploymentHistoryId = result.EmployeeEmploymentHistoryId` and uses the second, in-transaction validation result.

Correction: fixed only the test validation argument order and added assertions that the exact composite history principal exists before submission and that the persisted request references `EmploymentA`. No production mapping, FK, lock, transaction, migration, or idempotency behavior changed.

## Phase 4D.3I.3 SQL Server Test Isolation Fix

The disposable fixture is one database per xUnit collection, not one database per test method. The collection fixture seeds one shared Tenant A and Employee A, and requests remain persisted between methods. The old same-key assertion counted all `LeaveRequests` for Employee A. In a full-class run, that included the single-sanity request (`single-sanity`), the overlap winner (`overlap-a` or `overlap-b`), and the same-key request (`same-key`), producing 3. The lock test creates no LeaveRequest.

The assertion now counts exactly `(TenantId, EmployeeId, IdempotencyKey)` for `same-key`, while retaining exact `RequestId` scoping for its RequestDays and Submitted event assertions. No assertions were weakened; the logical idempotency invariant remains exactly one request for the same tenant, employee, and key. The other tests already scope their persistence assertions to exact request IDs or business results and required no rewrite. Production code, lock semantics, idempotency semantics, and migrations were unchanged.

## Phase 4D.3I.4 Remaining SQL Server Submission Validation

Added test coverage for same-key/different-payload conflicts, sequential replay-before-overlap, PendingApproval and Approved blocking, Rejected/Withdrawn/Cancelled non-blocking historical rows, different-tenant concurrent success, Allocated no-persistence, NoBalanceRequired persistence, and UnsupportedConfiguration no-persistence. Assertions are scoped to each test's tenant, employee, idempotency key, or request ID; the shared disposable database is not assumed empty.

Latest real SQL Server evidence remains the prior two successful four-test class runs. The expanded class was not executable in this workspace because MSBuild failed before test-host startup with zero diagnostics. New scenario results are therefore BLOCKED pending the user's focused rerun. Request-limit concurrency remains NOT IMPLEMENTED because the validator deliberately rejects configured request limits as UnsupportedConfiguration. Atomic rollback remains NOT IMPLEMENTED because no non-invasive failure-injection seam exists. Deadlock 1205 validation is DEFERRED to Phase 4D.3J. No status-transition paths exist; future transitions must use the existing Employee lock.

## Phase 4D.3J Deadlock Retry Hardening

Implemented a narrow submission retry boundary. `LeaveRequestSubmissionRetryPolicy` executes the complete submission attempt, including the fresh authoritative validation, transaction creation, Employee `UPDLOCK, HOLDLOCK`, idempotency lookup, overlap check, persistence, and commit. It permits three total attempts: the initial attempt plus two retries.

Only an exception chain containing `Microsoft.Data.SqlClient.SqlException.Number == 1205` is retried. Non-1205 SQL errors, unique/FK errors, validation failures, overlap, idempotency conflict, allocated rejection, cancellation, and other exceptions are not retried. After the third deadlock, the service returns the existing `ConcurrencyConflict` result with a stable exhaustion message; raw SQL text is not exposed.

Each deadlock attempt rolls back, clears the EF change tracker, and disposes its transaction before the policy starts the next attempt. The next attempt reacquires the Employee lock and repeats idempotency and in-transaction validation. This uses a clean tracker on the existing scoped context; a fresh context factory was not introduced. `ILeaveRequestSubmissionDeadlockClassifier` keeps SQL Server-specific classification in Infrastructure. Structured warning logs record attempt and exhaustion counts without request secrets.

Foundation tests cover first-attempt success, one- and two-deadlock recovery, three-attempt exhaustion, non-deadlock non-retry, cancellation, and repeated attempt invocation. A deterministic genuine SQL Server 1205/deadlock test was not added yet; submission-path deadlock validation remains deferred until a bounded, non-flaky disposable-database scenario is available. No migration, lock change, idempotency change, status transition, API, frontend, balance, or approval change was made.

Phase 4D.3J execution status: the retry-policy test filter, the existing SQL Server submission filter, the submission-foundation filter, and the full backend test command were attempted in this workspace but stopped during the pre-discovery MSBuild step with zero diagnostics. The latest user-validated baselines remain 15/15 for the SQL Server submission suite and 3/3 for the submission foundation; this retry hardening is not marked SQL Server validated until the focused tests and a genuine disposable-database 1205 scenario run successfully.

Phase 4D.3J.1 adds `SqlServerLeaveRequestSubmissionDeadlockTests`, using two independent transactions and two disposable `Employees` rows with deterministic `UPDLOCK, HOLDLOCK` lock ordering. It also verifies a real non-deadlock SQL Server error is rejected by the classifier. The focused class could not reach test discovery in this workspace because the same zero-diagnostic MSBuild failure occurred; genuine 1205 generation and classifier results remain pending the user's normal-PowerShell run. No production retry or locking behavior was changed.
