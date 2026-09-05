# Phase 4D.5A — Allocated Leave Balance Reservation and Restoration Design

## Status

Design and source review only. No production code, tests, migration, frontend,
package, environment, or database was changed or accessed in this phase.

## 1. Existing implementation inventory

### Balance projection

`EmployeeLeaveBalance` is tenant-scoped by:

- `TenantId`;
- `EmployeeId`;
- `LeaveTypeId`;
- `LeavePeriodId`.

It stores `GrantedQuantity`, `ReservedQuantity`, `ConsumedQuantity`, and an EF
SQL Server `rowversion`. `AvailableQuantity` is not persisted; the entity
derives it as:

```text
Available = GrantedQuantity - ReservedQuantity - ConsumedQuantity
```

The configuration also enforces non-negative granted, reserved, and consumed
quantities and requires `ReservedQuantity + ConsumedQuantity <= GrantedQuantity`.
The four-key balance identity has a unique tenant-scoped index.

### Ledger

`LeaveBalanceTransaction` is append-only and currently contains:

`TenantId`, `EmployeeLeaveBalanceId`, `EmployeeId`, `LeaveTypeId`,
`LeavePeriodId`, `TransactionType`, positive `Quantity`, `EffectiveDate`,
`OccurredAtUtc`, optional `LeavePolicyVersionId` and `LeavePolicyRuleId`,
`SourceType`, `SourceReference`, actor fields, `CorrelationId`,
`IdempotencyKey`, and `PayloadFingerprint`.

The database requires `Quantity > 0`, stores enum values as integers, and has a
unique `(TenantId, IdempotencyKey)` index. Existing transaction values are:

```text
Opening = 0
Accrual = 1
ExternalGrant = 2
```

There is no `LeaveRequestId` on the ledger today. Existing foreign keys are
tenant-aware and restrictive. The current poster supports only Opening,
Accrual, and ExternalGrant credits. It can create a missing balance row for a
credit, but that behavior is not safe for request reservation.

### Request and policy context

`LeaveRequest` captures `LeaveTypeId`, `LeavePeriodId`,
`LeavePolicyVersionId`, `LeavePolicyRuleId`, quantities, dates, status, and
immutable request days/events. It does not store `EntitlementMode` directly.
The captured `LeavePolicyRule` owns an `EntitlementRule`, whose current fields
include `EntitlementMode`, `EntitlementSource`, optional entitlement quantity,
and accrual settings. The request's captured rule and period, not a later
current-policy resolution, must control later accounting.

`ILeavePeriodResolver` resolves an active period for a date during current
validation. Later lifecycle accounting must use the request's persisted
`LeavePeriodId`; it must not resolve a replacement current period.

### Current lifecycle and concurrency

`LeaveRequestSubmissionService` validates twice, acquires the tenant-plus-
Employee lock, checks blocking overlap, then atomically persists the request,
days, and Submitted event. Allocated entitlement currently exits through
`AllocatedBalanceReservationNotReady` before request persistence.

`LeaveRequestApprovalService` changes `PendingApproval` to `Approved`, or to
`Rejected`, under the same Employee lock and transaction. It currently writes
only the request event; it does not reserve, consume, or release balance.

`LeaveRequestWithdrawalService` changes `PendingApproval` to `Withdrawn` under
the same lock and transaction, without balance accounting. The cancellation
service changes `Approved` to `Cancelled`, but currently rejects Allocated with
`AllocatedCancellationBalanceReleaseNotReady` before status/event mutation.

The shared retry policy retries only causal SQL Server error 1205, for at most
three total attempts, with a fresh transaction and change-tracker reset on
retry paths. `HrmsDbContext` exposes `BeginTransactionAsync` and
`ClearChangeTracker`; tenant query filters apply to balances, ledger rows,
requests, and policy entities.

## 2. Current blockers

Allocated submission is blocked in `ValidatePersistableEntitlement` in
`LeaveRequestSubmissionService`. The block is intentionally before request,
day, event, and transaction persistence.

Allocated cancellation is blocked in `LeaveRequestCancellationService` after
captured policy/entitlement lookup and before status/event persistence. No
balance mutation or ledger reversal exists.

Approval, rejection, and withdrawal have no balance integration, so enabling
Allocated submission without implementing their accounting would leave a
reservation with no deterministic terminal handling. The existing balance
poster is credit-only and does not provide the required reservation, balance
locking, request traceability, or lifecycle idempotency.

## 3. Proposed authoritative accounting model

Keep the existing three projection quantities and positive ledger quantities.
Do not add a fourth balance amount merely to duplicate a derivable value.

```text
Granted    = entitlement credited by Opening, Accrual, or ExternalGrant
Reserved   = chargeable quantity held by PendingApproval requests
Consumed   = chargeable quantity held by Approved requests
Available  = Granted - Reserved - Consumed
```

All quantities remain non-negative and use the existing decimal(9,3) scale.
The projection is a rebuildable current view; the immutable ledger is the
audit source. Every projection change and its ledger row must be written in
the same transaction.

Approval converts the reservation into consumption, so Available does not
change at approval:

```text
Reserved -= Q
Consumed += Q
```

Rejection or withdrawal releases the reservation:

```text
Reserved -= Q
```

Cancellation restores approved consumption:

```text
Consumed -= Q
```

No operation may make any component negative. A balance check and projection
update must be performed against a locked balance row, not a stale read.

## 4. State-transition/accounting matrix

Let `Q` be the request's persisted `ChargeableQuantity`.

| Transition | Balance effect | Ledger effect |
|---|---|---|
| submit: new -> PendingApproval | `Reserved += Q` | one `Reservation(Q)` |
| PendingApproval -> Approved | `Reserved -= Q`, `Consumed += Q` | one `Consumption(Q)` |
| PendingApproval -> Rejected | `Reserved -= Q` | one `ReservationRelease(Q)` |
| PendingApproval -> Withdrawn | `Reserved -= Q` | one `ReservationRelease(Q)` |
| Approved -> Cancelled | `Consumed -= Q` | one `CancellationRestore(Q)` |

The request, RequestDays, status event, projection update, and ledger entry
for each operation are atomic. A failed validation, insufficient balance,
duplicate operation, deadlock exhaustion, or persistence failure leaves all of
these unchanged.

Only the first slice's full request quantities are supported. Partial-day or
selected-day accounting is deferred.

## 5. Submission semantics

For an Allocated validated request, submission must acquire the existing
tenant-plus-Employee serialization boundary before overlap and balance reads.
Within one transaction it must:

1. perform the existing identity and validation flow;
2. acquire the Employee lock;
3. re-run persistence-sensitive validation while the lock is held;
4. resolve the captured period, policy rule, and `EntitlementMode.Allocated`;
5. locate the exact balance row for tenant, employee, leave type, and captured
   LeavePeriod;
6. fail with `BalanceNotInitialized` if the row is absent;
7. require `Q <= Available`; otherwise fail with `InsufficientLeaveBalance`;
8. lock/re-read the balance row and increment `ReservedQuantity` by `Q`;
9. add the request, RequestDays, Submitted event, and request-linked
   Reservation ledger row;
10. save and commit everything atomically.

There must be no path that commits a PendingApproval Allocated request without
its Reservation row and projection increment. A missing balance row is not an
implicit zero row and must not be created by submission.

## 6. Approval, rejection, withdrawal, and cancellation

### Approval

Approval should convert the reservation into consumption rather than deducting
again. Under the Employee lock and the balance-row lock it must verify the
request is PendingApproval and that exactly one request-linked Reservation
exists. It then decrements Reserved, increments Consumed, appends one
Consumption row, changes status, appends Approved, and commits atomically.

### Rejection

Rejection must release the reservation exactly once. It verifies the request's
reservation, decrements Reserved, appends one ReservationRelease row, changes
status, appends Rejected, and commits atomically. No current policy or period
resolution is used.

### Withdrawal

Withdrawal follows the same release operation as rejection, with a Withdrawn
status/event. Concurrent approval, rejection, withdrawal, and submission are
serialized by the existing Employee lock. Only the first terminal transition
can consume or release the reservation.

### Cancellation

Cancellation remains employee-owner `Approved -> Cancelled`. For Allocated it
must require a prior request-linked Consumption row, decrement Consumed by Q,
append one CancellationRestore row, change status, append Cancelled, and commit
atomically. It must not delete or edit the original Consumption row.

The existing `AllocatedCancellationBalanceReleaseNotReady` gate remains until
this complete restoration path is implemented.

## 7. Ledger model and transaction types

Preserve values 0–2. Add only the minimum explicit values in a later
implementation:

```text
Reservation          = 3   // Q held for PendingApproval
ReservationRelease   = 4   // Q released by Rejected or Withdrawn
Consumption          = 5   // Q converted from Reserved to Consumed
CancellationRestore  = 6   // Q restored from Consumed by Cancelled
```

`Quantity` remains positive. The transaction type determines which projection
components change; negative ledger quantities are prohibited by the existing
database check constraint.

Each request-linked row should include:

- the exact TenantId, EmployeeId, LeaveTypeId, and LeavePeriodId;
- the captured LeavePolicyVersionId and LeavePolicyRuleId;
- the request's effective date (using the existing field convention);
- `SourceType.Policy`;
- an explicit request source reference and correlation ID;
- the authenticated actor for employee/manager actions, or System where the
  operation is explicitly system-driven.

The source meaning is: Reservation is a hold, ReservationRelease is a
compensating release of that hold, Consumption is the approval conversion,
and CancellationRestore is a compensating restoration of consumed balance.
No prior row is edited.

## 8. Request-to-ledger traceability

`LeaveBalanceTransaction` currently has no `LeaveRequestId`; SourceReference
alone is not a relational audit guarantee. Add a nullable tenant-aware
`LeaveRequestId` foreign key with restrictive delete behavior. It stays nullable
for existing Opening, Accrual, and ExternalGrant rows.

Add an index on `(TenantId, LeaveRequestId, TransactionType)` and a filtered
unique index for non-null request-linked lifecycle rows. The filtered unique
constraint prevents more than one Reservation, ReservationRelease,
Consumption, or CancellationRestore for a request in the first full-request
slice. The request cannot be deleted while ledger history references it.

This enables an operator to trace every lifecycle accounting row to the exact
leave request while retaining tenant isolation and immutable financial history.

## 9. Exactly-once and idempotency

Use deterministic operation keys derived server-side:

```text
{TenantId:D}:leave-request:{RequestId:D}:reservation
{TenantId:D}:leave-request:{RequestId:D}:reservation-release
{TenantId:D}:leave-request:{RequestId:D}:consumption
{TenantId:D}:leave-request:{RequestId:D}:cancellation-restore
```

The existing unique `(TenantId, IdempotencyKey)` index is the final duplicate
arbiter. A retry with the same key and same fingerprint may resolve the
already-completed operation; the same key with different accounting data must
return a conflict. A retry must never create a second projection effect.

The filtered request/type unique index is a second invariant against malformed
keys or application defects. Status transition checks under the Employee lock
ensure that HTTP retries and two concurrent lifecycle commands cannot both
advance the same request.

The request submission idempotency key continues to identify the submission
request. It must not be reused as the sole key for every lifecycle operation;
the lifecycle suffix makes each accounting operation independently unique.

## 10. Lock order and transactions

All operations that affect request overlap, request status, or allocated
balance must use this order:

1. tenant-scoped Employee lock `(TenantId, EmployeeId)` using the existing
   `UPDLOCK, HOLDLOCK` mechanism;
2. exact `EmployeeLeaveBalance` row lock for
   `(TenantId, EmployeeId, LeaveTypeId, LeavePeriodId)`;
3. request re-read and status/ownership validation;
4. projection update, request status/event, and ledger append;
5. one save and commit.

The balance row must be created only by an explicitly authorized credit/setup
operation, never by request reservation. If a future operation spans multiple
balance rows, it must acquire them in deterministic key order after the
Employee lock.

Submission, approval, rejection, withdrawal, cancellation, and any
employee-scoped accrual/grant mutation that can contend with them must use the
same order. Do not introduce `sp_getapplock`, a guard table, or global
serializable isolation.

Reuse the existing whole-operation retry policy: SQL Server 1205 only, three
total attempts maximum, fresh transaction, Employee lock reacquisition, and
change-tracker cleanup. Non-deadlock errors are not retried. A retry must
re-read request, balance, and operation rows rather than replaying tracked
mutations.

## 11. Missing and insufficient balance

If the captured entitlement is Allocated and the exact balance row is absent,
return stable `BalanceNotInitialized`. Persist no request, status, event,
ledger, or projection mutation. A missing row is distinct from a row whose
available amount is zero.

If `Available < Q`, return stable `InsufficientLeaveBalance`. The response may
include safe requested and available quantities, but must not expose ledger IDs,
internal row versions, or other employees' data. The check is authoritative
only while the balance row is locked in the submission transaction.

Negative leave is not supported. Accrual and ExternalGrant may increase
GrantedQuantity while reservations exist; they do not erase reservations and
do not require Available to be positive after a valid prior reservation.
The existing projection check remains the invariant.

## 12. Period and policy authority

Balance operations use the request's captured `LeavePeriodId`, `LeaveTypeId`,
`LeavePolicyVersionId`, and `LeavePolicyRuleId`. They do not call
`ILeavePeriodResolver` or the current policy resolver during approval,
rejection, withdrawal, or cancellation.

The captured entitlement rule is authoritative for determining that a request
is Allocated. Historical policy versions must remain queryable and tenant-
scoped even if retired or no longer current. If the captured rule or
entitlement context is missing or inconsistent, fail with
`UnsupportedConfiguration` and write nothing.

## 13. Accrual, period close, and historical data

An Accrual or ExternalGrant of `G` changes GrantedQuantity by `G`. With a
balance of 5 and a reservation of 3, an accrual of 2 produces Granted 7,
Reserved 3, Consumed 0, Available 4. It must not release or consume a request
reservation.

Carry-forward, lapse, encashment, period close, retroactive accrual, and
attendance consumption are outside this first Allocated request slice. Any
future operation that changes Consumed or affects request eligibility must use
the same lock order and request-linked ledger rules.

Existing rows must not be reverse-inferred from historical Approved requests.
If historical Allocated Approved requests exist before the feature is enabled,
implementation must perform an explicit reconciliation/import decision. It
must not invent reservations or consumption from request history silently.

## 14. Minimum proposed schema changes

No migration is created in this phase. The implementation phase will require,
at minimum:

1. a nullable `LeaveRequestId uniqueidentifier` column on
   `LeaveBalanceTransactions`;
2. a tenant-aware restrictive FK from `(TenantId, LeaveRequestId)` to the
   request alternate key `(TenantId, Id)`;
3. an index on `(TenantId, LeaveRequestId, TransactionType)`;
4. a filtered unique index on request-linked lifecycle rows, scoped to
   non-null `LeaveRequestId` and the four new lifecycle transaction values;
5. explicit code enum values 3–6. The existing transaction type column is an
   unrestricted SQL Server `int`, so enum values alone need no migration.

No new Reserved or Consumed columns are required: both already exist. No
negative-quantity support, balance transaction type for a duplicate concept,
reason field, or request deletion cascade is proposed.

Before migration, verify the actual model snapshot and deployed schema for
the existing unique/check constraints. Existing Opening, Accrual, and
ExternalGrant rows receive `LeaveRequestId = NULL`; no historical accounting
amount is inferred. A data-reconciliation gate is required before enabling
Allocated requests in a tenant with historical Allocated Approved data.

## 15. Error contract

The first Allocated implementation should expose stable categories:

| Condition | Error |
|---|---|
| missing balance row | `BalanceNotInitialized` |
| available less than Q | `InsufficientLeaveBalance` |
| captured policy/entitlement missing or malformed | `UnsupportedConfiguration` |
| duplicate/different operation payload | existing idempotency conflict |
| stale or already-terminal request | `InvalidStatusTransition` |
| exhausted SQL deadlock or EF concurrency | `ConcurrencyConflict` |

Errors must not reveal another tenant's balance or request existence.

## 16. SQL Server concurrency test plan

The implementation phase must use only disposable
`HRMS_LeaveRequestConcurrency_*` databases and fresh-context persisted-state
assertions. Required scenarios are:

- two concurrent Allocated submissions with balance sufficient for one:
  exactly one request and Reservation succeeds;
- two concurrent submissions with balance sufficient for both: both succeed,
  reservations equal the sum, and no overbooking occurs;
- Approve versus Reject: one terminal transition and exactly one accounting
  effect;
- Approve versus Withdraw: one terminal transition and exactly one release or
  consumption;
- Withdraw versus overlapping submission: serialized outcomes reflect whether
  the release happened before overlap evaluation;
- Cancel versus new overlapping submission: restoration and reservation are
  serialized;
- Cancel versus Cancel: one restoration and one Cancelled event;
- Accrual/ExternalGrant concurrent with reservation where those mutations
  share the balance row;
- different Employee independence;
- tenant isolation;
- genuine SQL Server deadlock/retry behavior, including no duplicate ledger
  row after a retried operation.

Every test must assert request status, balance projection, request-linked
ledger counts/types/quantities, request events, and idempotency keys. Tests
must not use timing-only winners or an existing HRMS database.

## 17. Frontend implications

Future Apply Leave may submit Allocated requests only after the backend
reservation path is validated. It may display authoritative
`BalanceNotInitialized` and `InsufficientLeaveBalance` messages and, if the
API contract is approved, safe available/requested quantities.

The frontend must not calculate authoritative availability, reserve balance,
restore balance, or rerun current policy logic. Approval, rejection,
withdrawal, and cancellation UI should continue to rely on authoritative
server responses.

## 18. Deferred items

The following remain outside this design's first implementation slices:

- partial or selected-day cancellation;
- Modify/replacement workflow;
- negative balances or overdraft policy;
- balance consumption by attendance/payroll;
- period close, lapse, carry-forward, and encashment;
- retroactive policy/accrual reconciliation;
- cancellation reasons;
- manager/HR/admin balance overrides;
- Allocated cancellation before complete reservation/consumption history is
  available.

## 19. Recommended implementation slices

1. **4D.5B — Balance ledger schema and operation foundation:** add the
   request FK, explicit transaction types, filtered uniqueness, and a
   request-linked balance operation abstraction without enabling Allocated
   submission.
2. **4D.5C — Allocated submission reservation:** integrate captured
   entitlement/period context, balance row locking, missing/insufficient
   errors, atomic request plus Reservation, and idempotent replay.
3. **4D.5D — Terminal reservation accounting:** integrate Approval/Reject/
   Withdraw with conversion/release and focused foundation tests.
4. **4D.5E — Allocated cancellation restoration:** remove the safety block only
   after consumption/reversal semantics and atomic Cancel restoration are
   implemented and tested.
5. **4D.5F — Real SQL Server Allocated lifecycle concurrency validation:** run
   the race matrix above against disposable SQL Server databases.
6. **4D.5G — Allocated frontend balance UX:** add authoritative balance
   presentation and controlled errors after backend and SQL validation.

No slice should enable Allocated submission before all terminal paths can
release or convert its reservation exactly once.

## 20. Phase 4D.5C implementation status

Allocated submission reservation is now wired through the existing Employee
serialization boundary. The service uses the fresh, locked validation result's
`ChargeableQuantity` and captured `LeaveTypeId`, `LeavePeriodId`, policy
version, and policy rule identifiers. It locates the existing balance and
performs `Reserve`, producing one request-linked `Reservation` ledger entry.

The request, request days, Submitted event, balance projection, and ledger
entry participate in the caller-owned transaction. The accounting helper does
not begin or commit a transaction. Existing submission idempotency remains the
authority for replay, so a replay does not reserve or ledger twice.

Missing balances return `BalanceNotInitialized`; insufficient availability
returns `InsufficientLeaveBalance`. Unlimited and NoBalanceRequired submission
behavior remains unchanged and does not require a balance.

Because Allocated PendingApproval requests now exist, temporary safety gates
remain in place for Approval
(`AllocatedApprovalBalanceConsumptionNotReady`), Rejection
(`AllocatedRejectionBalanceReleaseNotReady`), and Withdrawal
(`AllocatedWithdrawalBalanceReleaseNotReady`). Allocated Cancellation remains
blocked by `AllocatedCancellationBalanceReleaseNotReady`. No historical
requests are backfilled and no new migration was created or applied.

Focused service/gate validation and the API build are the next validation
commands. Real SQL Server lifecycle concurrency validation is deferred to
4D.5F. 4D.5C must not be released as a complete Allocated workflow until
4D.5D implements and validates terminal reservation accounting.

## 21. Phase 4D.5D implementation status

Allocated terminal reservation accounting is wired to the existing captured
request context and Employee-scoped transaction boundary. Allocated approval
consumes the request's persisted `ChargeableQuantity` through
`ConsumeReservation`; Allocated rejection and owner withdrawal release that
reservation through `ReleaseReservation`. No current policy or LeavePeriod is
resolved during these transitions.

The accounting helper remains caller-transactional and supplies the request
linked `Consumption` or `ReservationRelease` ledger entry. Status and event
changes remain in the same transaction. A missing or insufficient reservation
fails with `AllocatedReservationNotFound` and does not authorize the terminal
transition. Unlimited and NoBalanceRequired paths remain unchanged.

The temporary Allocated approval, rejection, and withdrawal gates have been
removed in favor of these primitives. Allocated cancellation remains blocked
by `AllocatedCancellationBalanceReleaseNotReady`; no CancellationRestore,
historical backfill, migration, or frontend change is included. Real SQL
Server lifecycle race validation remains deferred to the later concurrency
phase.

## Phase 4D.5E implementation status

Allocated cancellation now restores the persisted request's chargeable
quantity through `RestoreConsumption` for captured Allocated Approved requests.
The operation uses the captured tenant, employee, leave type, leave period,
policy version, and policy rule context; it does not resolve current policy or
mutate ReservedQuantity. A positive `CancellationRestore` ledger entry is
written with the request and actor identity, and the balance mutation, status,
event, and ledger remain in the caller-owned Employee-serialized transaction.

Insufficient consumed quantity returns `AllocatedConsumptionNotFound` without
changing the request, balance, or event history. The existing request-linked
uniqueness protects repeat accounting, and a second cancellation is rejected
by the existing status transition. Cancellation policy denial occurs before
accounting. Unlimited and NoBalanceRequired cancellation remain balance-free,
and historical consumption is never inferred or backfilled. No migration was
created or applied. Focused SQLite restoration tests cover exact consumption,
policy denial, double cancellation, and non-Allocated regression; real SQL
Server lifecycle race validation remains pending.

## Phase 4D.5G frontend Allocated leave UX

The Apply Leave preview uses the authoritative `BalanceReservationRequired` and
`ChargeableQuantity` values to explain when submission will reserve leave
balance. Allocated submission keeps the existing Pending Approval flow and
uses reservation wording after success; Unlimited and NoBalanceRequired keep
the generic success message.

The frontend presents controlled messages for `InsufficientLeaveBalance`,
`BalanceNotInitialized`, `AllocatedReservationNotFound`, and
`AllocatedConsumptionNotFound`, while preserving existing idempotency,
concurrency, status, authorization, and cancellation-policy handling. The
former `AllocatedCancellationBalanceReleaseNotReady` message is no longer a
current Allocated cancellation path.

Approve, reject, withdraw, and cancel continue to refresh authoritative request
state and never issue client-side accounting operations. The frontend does not
calculate, infer, or mutate Granted, Reserved, or Consumed quantities; the
backend remains authoritative. No new balance endpoint, migration, or browser
E2E coverage is introduced in this phase.

## Phase 4D.6A request limits

The runtime request validator evaluates the existing policy fields for minimum
request quantity, maximum request quantity, maximum consecutive quantity,
maximum requests per period, and maximum quantity per period. Quantities use
authoritative chargeable RequestDays. Period limits use the captured
LeavePeriod or Gregorian request-day month. Historical counts include only
PendingApproval and Approved requests; Rejected, Withdrawn, and Cancelled
requests are excluded. A matching idempotency key is excluded from history so
replay remains authoritative.

Preview remains side-effect free, and submission revalidates limits during the
existing employee-serialized transaction before Allocated reservation. Limit
failures therefore create no request, event, ledger row, or balance mutation.
No client-side counters or quantity authority are introduced. Advance notice,
backdated rules, attachments, calendars, and other deferred RequestRule
capabilities remain unsupported. No migration was created or applied.
