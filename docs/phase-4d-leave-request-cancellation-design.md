# Phase 4D.4K — Approved Leave Request Cancellation Workflow Design Review

Status: DESIGN ONLY. This document authorizes no API, entity, migration,
frontend, balance, or database change.

## 1. Validated baseline and source findings

The current validated request lifecycle is:

```text
new request -> PendingApproval -> Approved
                           \-> Rejected
PendingApproval -> Withdrawn
```

`PendingApproval` and `Approved` are blocking statuses for overlap and request
limits. `Rejected`, `Withdrawn`, and `Cancelled` are non-blocking. Submission,
approval, and withdrawal use the same tenant-plus-Employee SQL Server
`UPDLOCK, HOLDLOCK` serialization boundary, in the same transaction and
connection, with the whole-operation retry policy that retries causal SQL
Server deadlock 1205 up to three total attempts.

The current code was inspected directly. The relevant sources are:

- `Backend/HRMS.Domain/Entities/LeavePolicyFoundation.cs`;
- `Backend/HRMS.Domain/Entities/LeaveRequestFoundation.cs`;
- `Backend/HRMS.Domain/Entities/LeaveBalanceFoundation.cs`;
- `Backend/HRMS.Domain/Enums/LeavePolicyEnums.cs`;
- `Backend/HRMS.Domain/Enums/LeaveRequestEnums.cs`;
- `Backend/HRMS.Domain/Enums/LeaveBalanceEnums.cs`;
- `Backend/HRMS.Application/Services/LeaveConfigurationService.cs`;
- `Backend/HRMS.Application/Services/LeavePolicyResolver.cs`;
- `Backend/HRMS.Application/Services/LeaveRequestValidationService.cs`;
- `Backend/HRMS.Application/Services/LeaveRequestSubmissionService.cs`;
- `Backend/HRMS.Application/Services/LeaveRequestApprovalService.cs`;
- `Backend/HRMS.Application/Services/LeaveRequestWithdrawalService.cs`;
- `Backend/HRMS.Application/Services/LeaveRequestReadService.cs`;
- `Frontend/HRMS.Web/src/pages/leave/LeavePolicyCancellationSection.tsx`.

## 2. Current CancellationRule model

The actual entity is `LeavePolicyCancellationRule`, a tenant-scoped,
one-to-one child of `LeavePolicyRule`:

| Field | Current meaning |
|---|---|
| `Id` | Base entity identifier |
| `TenantId` | Tenant scope |
| `LeavePolicyRuleId` | Owning LeaveType rule |
| `WithdrawAllowed` | Capability flag for withdrawal configuration |
| `CancelAllowed` | Capability flag for cancellation configuration |
| `ModifyAllowed` | Capability flag for modification configuration |
| `CreatedDate`, `ModifiedDate` | Base audit fields used for the opaque concurrency token |

The DTO and request contain the same three booleans plus the DTO identity and
the request/DTO `ConcurrencyToken`. The nested configuration API is:

```text
GET /api/leave-policies/{policyId}/versions/{versionId}/leave-types/{leaveTypeId}/cancellation
PUT /api/leave-policies/{policyId}/versions/{versionId}/leave-types/{leaveTypeId}/cancellation
```

GET requires `Leave.PolicyView`; PUT requires `Leave.PolicyManage` and only
Draft policy versions can be changed. Saving all three flags as false removes
the child row and returns a successful null value. The editor exposes three
checkboxes: Allow Withdraw, Allow Cancel, and Allow Modify. It explicitly
describes these as configuration for future runtime behavior.

There is no current field for:

- direct cancellation versus cancellation approval;
- cancellation cutoff or number of days;
- before-start, after-start, after-end, same-day, or other date restrictions;
- quantity or selected-day restrictions;
- cancellation reason/comment;
- previous-period behavior;
- balance release behavior.

The EF mapping is a tenant-aware one-to-one relationship with a unique
`(TenantId, LeavePolicyRuleId)` index and restrictive delete behavior. The
publish validation loads the CancellationRule but does not validate any
cancellation-specific semantics; its three booleans have no invalid
combinations under the current model.

## 3. Current runtime support

`LeavePolicyCancellationRule` is currently persisted/editor configuration only.
The answer to “does runtime currently use CancellationRule?” is **NO**.

The current policy resolver returns the winning published policy/version/rule
identity for a date and employee, but does not load or evaluate the rule's
CancellationRule. Request preview/validation evaluates eligibility,
entitlement, request, calendar, attachment, and clubbing configuration, but
not cancellation. Submission persists the resolved
`LeavePolicyVersionId`/`LeavePolicyRuleId` and request snapshots, but does not
create or use a CancellationRule. Approval and withdrawal validate their own
status/authorization contracts only. My Leave Requests reads persisted
request data and events only.

The safe reuse option is therefore a new cancellation-specific application
service that loads the captured request policy version and rule, including
their persisted CancellationRule and EntitlementRule, under tenant scope. It
must not call the current-policy resolver or rerun the submission validation
pipeline. Existing configuration GET/PUT contracts remain configuration
contracts; they are not sufficient by themselves as a runtime eligibility
response.

## 4. Recommended first runtime slice

Implement exactly:

- authenticated linked employee owner only;
- immediate direct cancellation, with no manager approval;
- `Approved -> Cancelled` only;
- full-request cancellation only;
- persisted captured policy rule must contain `CancelAllowed = true`;
- captured entitlement mode must be `Unlimited` or `NoBalanceRequired`;
- no cancellation reason and an empty request body;
- no date-window rule in this slice;
- no balance mutation in this slice;
- append one immutable `Cancelled` event;
- use the existing Employee lock, transaction, and deadlock retry boundary;
- retain the request, request days, approval history, and all immutable fields.

This is the smallest safe slice supported by the current runtime. Allocated
cancellation is explicitly rejected until reservation/consumption/release
semantics exist. No manager, HR, TenantAdmin, `Leave.Approve` holder, or other
employee is included.

## 5. Actor and approval decision

The actor is the authenticated employee who owns the request, resolved through
the existing `IEmployeeIdentityResolver` and current
`AccountEmployeeCurrentLink` path. Authorization requires the live tenant,
authenticated user, current account-to-employee link, and request ownership.
Another employee, a manager, an HR account, a TenantAdmin, or a user holding
`Leave.Approve` alone must not cancel it.

No dedicated cancellation permission is required for the first self-service
slice. Ownership plus the captured policy capability is the narrower rule and
matches the existing self-service submission/withdrawal approach. No
permission is added in this design.

Cancellation is **DIRECT CANCELLATION**: an eligible owner command commits
`Approved -> Cancelled` immediately. The current schema has no cancellation
request state, stage/approver assignment, cancellation-request relation, or
separate approval event. Introducing “cancellation requires approval” would
need additional workflow states and schema/authorization design, so it is not
safe to infer from the current `CancelAllowed` boolean.

## 6. Status and mutability rules

The only allowed transition is:

```text
Approved -> Cancelled
```

These are deterministic invalid transitions:

| Current status | Cancel |
|---|---:|
| `PendingApproval` | invalid; Withdraw handles this state |
| `Approved` | allowed if policy and safety gates pass |
| `Rejected` | invalid |
| `Withdrawn` | invalid |
| `Cancelled` | invalid; repeated cancel is not idempotent success |

Cancellation is full-request only. It must not select days or alter
quantities. The following remain unchanged:

- `EmployeeId`, `LeaveTypeId`, `LeavePeriodId`;
- `LeavePolicyVersionId`, `LeavePolicyRuleId`;
- `EmployeeEmploymentHistoryId`;
- `PolicyGenderSnapshot`;
- `StartDate`, `EndDate`;
- `RequestedQuantity`, `ChargeableQuantity`;
- `SubmittedAtUtc`;
- `RequestDays` and their date/quantity/calculation snapshots;
- `Created`/`Submitted`/`Approved` history.

Only `Status` changes to `Cancelled`, with normal rowversion/audit effects as
applicable, and one `Cancelled` event is appended. No request, day, or event
is deleted or rewritten.

Partial cancellation is deferred. The present model has no partial lifecycle,
replacement linkage, balance allocation per day, or event payload for selected
days. Supporting it now would undermine request-day immutability and overlap
and balance correctness.

## 7. Captured policy and rule decision

Cancellation must use the policy context captured on the request:

```text
LeaveRequest.LeavePolicyVersionId
LeaveRequest.LeavePolicyRuleId
LeaveRequest.LeaveTypeId
```

The implementation should tenant-safely load the exact historical
`LeavePolicyVersion` and `LeavePolicyRule`, verify that the rule belongs to
that version and LeaveType, and inspect that rule's persisted
`CancellationRule`. It should also load the same rule's persisted
`EntitlementRule`. It must not resolve today’s newest policy, require the
parent policy to remain active, or reject a request merely because its
captured published version is now Retired. Published versions are immutable;
Retired versions remain queryable for history. A newer Draft/Published version
must not reinterpret an old Approved request.

The captured `CancelAllowed` flag is required. A missing CancellationRule,
`CancelAllowed = false`, missing EntitlementRule, invalid captured references,
or an unsupported captured entitlement mode must fail restrictively without
changing status or writing an event.

Recommended stable categories are:

- `CancellationNotAllowed` for missing/disabled `CancelAllowed` capability;
- `UnsupportedConfiguration` for a malformed or unsupported captured policy
  context;
- `AllocatedCancellationBalanceReleaseNotReady` for Allocated entitlement.

The policy configuration currently has no time or quantity semantics, so the
first slice must not invent them or silently use current policy data.

## 8. Date/time and leave-date behavior

The current CancellationRule contains no date restriction. There is also no
authoritative tenant business timezone field in the current `LeavePeriod`
entity/runtime that can support advanced “today” decisions. The current
request services work with persisted `DateOnly` values and UTC audit timestamps;
they do not establish a business-local clock for cancellation windows.

Accordingly, the first slice has no date gate: future, in-progress, and past
Approved requests are all eligible on the date dimension, subject to status,
ownership, captured `CancelAllowed`, and the Allocated safety gate. This is not
a claim that every business wants past or in-progress cancellation; it is a
consequence of the actual configuration model. The product must explicitly
decide a future date-window model before adding one.

If a later rule adds “cancel before start”, same-day, or N-days semantics, it
must add a reviewed field/contract and an authoritative tenant timezone/
business-date source. Until then, do not use `DateTime.Now`, server-local
time, or an unsafe UTC-to-local conversion.

## 9. Entitlement and balance safety

`LeaveRequest` does not store `EntitlementMode` directly. It stores the
captured `LeavePolicyVersionId` and `LeavePolicyRuleId`; the mode can be
determined by loading the captured rule's persisted `LeavePolicyEntitlementRule`
under tenant/version/rule/LeaveType predicates. This is safe only when the
captured rule and its entitlement child are present and immutable. It must not
rerun current policy resolution.

The current balance model is not a request lifecycle ledger. `EmployeeLeaveBalance`
has Granted, Reserved, and Consumed quantities, and
`LeaveBalanceTransaction` has tenant/employee/type/period/policy references,
actor, source, idempotency, and fingerprint fields. However, the actual
`LeaveBalanceTransactionType` enum and poster currently support only:

```text
Opening, Accrual, ExternalGrant
```

The current poster rejects reservation/consumption/release transaction types.
No production submission path reserves balance; Allocated submission is
blocked by `AllocatedBalanceReservationNotReady`. Therefore the repository
does not prove that an Approved Allocated request consumed or reserved balance.

First-slice rule: `Unlimited` and `NoBalanceRequired` may cancel without a
balance mutation. `Allocated` must return
`AllocatedCancellationBalanceReleaseNotReady` before status/event persistence.
It must not be treated as safe merely because a historical Allocated row exists.

For a future Allocated workflow, cancellation will require an atomic,
idempotent reversal/release contract linked to the originating request,
reservation/consumption transaction(s), and projection. The exact future
transaction type, quantity/sign convention, effective date, source reference,
and idempotency key must be approved from the actual ledger design. It must
append compensating history rather than edit prior ledger rows, and lock the
balance scope with a reviewed ordering relative to the Employee request lock.

## 10. Transaction, locking, and retry design

Cancellation changes an Approved blocking request into a non-blocking request,
so it must share the existing serialization boundary with submission and
other blocking-state mutations:

1. Resolve authenticated tenant, user, and linked employee.
2. Locate the target safely enough to identify its employee, applying tenant
   and owner predicates; return isolation-safe NotFound when out of scope.
3. Enter the existing whole-operation deadlock retry boundary.
4. Begin a fresh transaction.
5. Acquire the existing Employee row lock for `(TenantId, EmployeeId)` with
   `UPDLOCK, HOLDLOCK` on the same connection and transaction.
6. Re-read the request under the lock.
7. Revalidate tenant ownership and linked employee ownership.
8. Revalidate `Approved` status.
9. Load and validate the captured CancellationRule and EntitlementRule.
10. Reject Allocated before any mutation in the first slice.
11. Set status to `Cancelled`.
12. Append one `Cancelled` event with the authenticated user and owner
    employee actor identity.
13. Save and commit atomically.

Any validation, authorization, policy, status, persistence, or commit failure
rolls back and writes no status, event, or balance change. Deadlock 1205 only
is retried for the complete operation, at most three total attempts, with a
fresh transaction/tracking state and reacquired Employee lock. Non-deadlock
errors are not retried. Server-side re-read plus the Employee lock remains the
authoritative stale-state check; no client rowversion is needed.

## 11. Required race outcomes

### Cancel versus overlapping submission

Both operations must acquire the same Employee lock before the
overlap-sensitive read and write.

- If Cancel commits first, A becomes `Cancelled` and non-blocking; B may
  submit if all other validation passes.
- If Submission commits first, it observes A as `Approved`, rejects B for
  overlap, and Cancel may then commit.
- B must never persist from a stale view that observed A as non-blocking
  without holding the Employee serialization boundary.

### Cancel versus Cancel

The Employee lock serializes the commands. Exactly one sees `Approved` and
commits. The winner produces final `Cancelled` status and one `Cancelled`
event. The loser re-reads `Cancelled` and returns deterministic
`InvalidStatusTransition`/HTTP 409 or an established concurrency conflict;
it writes no second event.

### Other and future operations

There is no other implemented transition from Approved today. Any future
Modify, balance adjustment, attendance consumption, or other operation that
changes overlap or balance meaning must use compatible Employee locking. A
balance-affecting operation must publish a consistent lock ordering across the
Employee request scope and balance/ledger scope, with real SQL Server deadlock
coverage before production use.

## 12. Event and schema design

The current `LeaveRequestEventType` values are:

```text
Created = 0
Submitted = 1
Approved = 2
Rejected = 3
Withdrawn = 4
```

`Cancelled` does not exist yet. If implementation is approved, add the next
explicit value `Cancelled = 5`; do not renumber existing values.

`LeaveRequestEvent.EventType` is configured with `HasConversion<int>()` and
the migration/snapshot define an unrestricted SQL Server `int` column. There
is no event-type check constraint. Therefore adding enum value 5 should not
require a migration, provided no separate schema restriction is introduced.
This remains a code change, not an authorization to implement it in 4D.4K.

The event should contain the existing fields: tenant, request, EventType,
UTC timestamp, `ActorType = User`, `ActorUserId` from the authenticated
account, `ActorEmployeeId` from the linked owner, and any correlation value
the command contract establishes. Preserve `Submitted` and `Approved`; append
rather than update. The current event schema has no reason/comment field, so
the first request body remains empty and cancellation reason is deferred.

## 13. Proposed API contract

Do not implement this endpoint in 4D.4K. The recommended future endpoint is:

```text
POST /api/leave-requests/{requestId}/cancel
```

The body should be empty because reason is deferred. The client must not send
TenantId, EmployeeId, UserId, Status, Balance, PolicyRuleId, or ActorId. The
server derives tenant, identity, ownership, status, captured policy context,
and entitlement mode.

Recommended result shape should mirror the existing transition-command
pattern:

```text
RequestId
Status = Cancelled
EventType = Cancelled
OccurredAtUtc
```

Recommended errors are:

| Condition | Result |
|---|---|
| Missing/invalid session or tenant | Existing Unauthorized/session behavior |
| Unlinked account or invalid live identity | Existing identity failure behavior |
| Nonexistent, other-employee, or cross-tenant request | Isolation-safe NotFound |
| Not `Approved` | Conflict with `InvalidStatusTransition` |
| Missing/disabled captured CancelAllowed | Conflict or validation result with `CancellationNotAllowed` |
| Malformed/missing captured runtime configuration | Conflict/unsupported result with `UnsupportedConfiguration` |
| Captured Allocated mode | Deterministic unsupported result `AllocatedCancellationBalanceReleaseNotReady` |
| SQL deadlock exhausted or EF concurrency failure | Existing `ConcurrencyConflict` mapping |

No raw provider details should be exposed. A missing LeaveType or retired
parent policy must not erase an otherwise interpretable captured request; the
exact captured version/rule lookup is authoritative. An invalid reference or
missing required child is restrictive `UnsupportedConfiguration`, not
permissive cancellation.

## 14. Read models and frontend design

Cancelled requests must remain visible in `GET /api/leave-requests`,
`GET /api/leave-requests/{requestId}`, My Leave Requests list/detail, and
history. The detail should show `Approved` followed by `Cancelled` after an
authoritative refresh. Request days and immutable quantities remain visible.

The Approval Inbox already returns actionable `PendingApproval` work, so an
Approved request is not present there. Cancellation requires no mutation-side
inbox cleanup; the existing PendingApproval filter remains unchanged.

Future My Leave Requests UX should not duplicate policy logic in React. Two
options were considered:

1. Show Cancel for every `Approved` request and let the backend reject
   disabled/unsupported cases. This is the smallest contract but can show an
   action that is predictably unavailable.
2. Extend My Leave Request detail with authoritative `CanCancel` and a stable
   `CancellationBlockedReason`, or add a separate eligibility endpoint. This
   is clearer but adds response and policy-eligibility surface area.

Recommendation: for the first backend slice, include authoritative
`CanCancel` plus a non-sensitive `CancellationBlockedReason` in the detail
contract only if the service can compute it from the captured rule and mode
without date-window evaluation. The backend remains final authority. Until
that contract exists, showing Cancel for Approved and handling controlled
`CancellationNotAllowed`, `UnsupportedConfiguration`, Allocated, 401, 404,
and 409 responses is acceptable. Do not add a preview or rerun submission
policy validation merely to render the button.

When implemented, the UX should require confirmation, use an empty command,
disable the button while in flight, prevent duplicate requests, show success
only after server success, and refresh authoritative detail/list state. It
must not edit dates, quantities, Leave Type, policy references, submitted
time, or history locally.

## 15. Security and historical cases

- Another employee, cross-tenant user, manager, HR, TenantAdmin, and an
  approval-permission holder without ownership receive safe NotFound where
  request scope is unauthorized.
- An unlinked or deactivated account uses existing identity/session behavior;
  no cached eligibility or fallback identity is permitted.
- A removed employee link after approval must fail through live identity
  resolution; historical request ownership is not a substitute for a current
  authenticated link.
- An inactive employee must follow the identity resolver's established
  behavior; cancellation must not invent a new fallback rule.
- A retired captured policy version remains usable for historical
  interpretation if its rule and CancellationRule/EntitlementRule are intact.
  A newer Draft/current policy cannot override it.
- A later deactivated LeaveType does not authorize changing the captured
  request context; the cancellation service should use the persisted relation
  and return restrictive `UnsupportedConfiguration` only when that historical
  context is no longer structurally interpretable.

## 16. Test plan

### Backend foundation

Cover owner authorization, no manager dependency, no implicit permission,
tenant/employee isolation, unlinked/deactivated identity behavior, direct
Approved-to-Cancelled success, all invalid statuses, disabled/missing rule,
historical captured version, newer policy non-interference, unsupported date
configuration, and Allocated no-write behavior. Assert full immutability of
request/days and exact preservation of Submitted and Approved events.

Assert exactly one Cancelled event with authenticated ActorUserId and owner
ActorEmployeeId. Failed authorization, policy, status, Allocated, save, and
commit paths must write no event and no balance mutation. Verify atomic
rollback. Verify response/error mapping for NotFound, Unauthorized,
InvalidStatusTransition, CancellationNotAllowed, UnsupportedConfiguration,
AllocatedCancellationBalanceReleaseNotReady, and ConcurrencyConflict.

### Real SQL Server

Use only disposable `HRMS_LeaveRequestConcurrency_*` databases and do not use
an existing database. Add bounded tests for Cancel-vs-Cancel,
Cancel-vs-overlapping Submission, Cancel-vs-non-overlapping Submission,
different-Employee isolation, tenant isolation, exact event counts, preserved
history, and rollback. Verify deadlock 1205 retry behavior using the existing
three-attempt policy. Do not claim SQLite/in-memory proof for Employee row
locks, HOLDLOCK, rowversion, SQL error numbers, or races.

If Allocated support is later added, add real tests for Employee/balance lock
ordering, reservation release/reversal idempotency, projection consistency,
and deadlock behavior.

### Frontend

After the backend contract is implemented, test eligible Approved visibility,
all non-eligible statuses, policy-blocked/unsupported/Allocated responses,
confirmation, empty body, in-flight disabled state, double-submit protection,
success refresh, persisted Approved-plus-Cancelled history, 401/404, and
ConcurrencyConflict. Tests must verify that React does not reproduce policy
or entitlement logic.

## 17. Modify boundary

Modify remains separate and is not designed or implemented here. It should
not mutate an Approved request in place. The current safe direction is a
replacement/new request with immutable original history and explicit linkage,
but its detailed authorization, validation, overlap, balance, and workflow
contract belongs to a later phase.

## 18. Migration assessment

**Migration required for the recommended first slice: NO.**

`LeaveRequestStatus.Cancelled` already exists. The event table already has an
integer EventType column with `HasConversion<int>()`, no event-type check
constraint, and existing immutable event storage; a future code-only
`Cancelled = 5` is sufficient for the event value. No reason field, partial
request fields, cancellation-approval state, or Allocated balance linkage is
needed by the recommended Unlimited/NoBalanceRequired direct slice.

This is not a claim that future Allocated release, cancellation approval,
partial cancellation, or cancellation windows need no schema. Those features
must receive separate design and migration assessment before implementation.

## 19. Recommended implementation slices

Because the first slice deliberately rejects Allocated, the recommended order
is:

1. **Phase 4D.4L — Cancel Backend Foundation:** owner-only direct command,
   captured-rule evaluation, Unlimited/NoBalanceRequired gate, Approved to
   Cancelled, existing Employee lock/retry, immutable event, API/errors, and
   focused tests.
2. **Phase 4D.4M — Real SQL Server Cancel Concurrency Validation:** disposable
   database races and atomicity/isolation assertions.
3. **Phase 4D.4N — My Leave Requests Cancel Frontend:** authoritative
   eligibility presentation or Approved-action fallback, confirmation,
   in-flight protection, refresh, and controlled errors.
4. **Later Allocated balance phase:** reservation/consumption/release ledger
   design and implementation, projection/lock ordering, idempotency, and
   real SQL Server validation before enabling Allocated cancellation.

Cancellation approval, date windows, partial cancellation, reasons, and
Modify remain separately reviewed work.

## Phase 4D.4M SQL Server cancellation concurrency validation

Added test-only `SqlServerLeaveRequestCancellationConcurrencyTests`, reusing
`SqlServerLeaveRequestConcurrencyFixture` and the existing
`HRMS_LeaveRequestConcurrency_*` disposable database convention. The fixture
now seeds the captured Unlimited entitlement and `CancelAllowed = true`
CancellationRule required by valid cancellation operations for both synthetic
tenants; no production lock/retry code was changed.

The focused class covers Cancel-vs-Cancel, Cancel-vs-overlapping Submission,
Cancel-vs-non-overlapping Submission, different-Employee isolation, and
independent tenant operations. Assertions read fresh SQL contexts and verify
serialized final state, exactly one Cancelled event, persisted tenant/request
and actor user/employee identity, and preserved Submitted/Approved history.
The cancellation service uses the captured policy version/rule and supported
Unlimited entitlement; the existing foundation tests cover the Allocated
no-write safety gate, so no optional shared-fixture Allocated SQL test was
added. Existing genuine SQL Server 1205 classifier/retry validation was reused
because cancellation enters the same three-attempt retry boundary and adds no
distinct retry wiring.

The focused SQL Server command was attempted in this workspace but stopped
before test discovery after remaining silent for 60 seconds, matching the
known zero-diagnostic MSBuild environment failure. SQL Server cancellation
validation is therefore **BLOCKED**, not a PASS claim, pending user
PowerShell execution. Frontend cancellation remains deferred to Phase 4D.4N.

## 20. Phase 4D.4K completion statement

This review is read-only. No production file, frontend file, API, entity,
migration, package, environment, or database was changed or accessed.

## Phase 4D.4L implementation status

The Approved Leave cancellation backend foundation is implemented with:

- `POST /api/leave-requests/{requestId}/cancel`;
- authenticated linked-owner self-service only, with no dedicated permission
  and no manager resolution;
- isolation-safe tenant/employee ownership checks;
- only `Approved -> Cancelled`, with deterministic invalid-transition errors;
- full-request status transition with immutable RequestDays and request
  snapshot fields preserved;
- captured `LeavePolicyVersionId`/`LeavePolicyRuleId` lookup, requiring the
  captured CancellationRule with `CancelAllowed = true`;
- support for captured `Unlimited` and `NoBalanceRequired` entitlement modes;
- `AllocatedCancellationBalanceReleaseNotReady` before any status/event
  mutation for Allocated requests;
- no business-date gate, current-policy resolution, submission revalidation,
  balance mutation, cancellation reason, or partial cancellation;
- reuse of the existing Employee `UPDLOCK, HOLDLOCK` boundary and whole-
  operation SQL Server 1205 retry policy;
- explicit `LeaveRequestEventType.Cancelled = 5`, appending one immutable
  Cancelled event while preserving Submitted and Approved history;
- shared Result-to-HTTP mapping and an empty request body.

Focused foundation and API tests were added for authorization, status rules,
captured-policy behavior, historical rule lookup, entitlement safety,
immutability, event actor identity, lock/retry orchestration, bodyless route,
delegation, and shared error mapping. The schema exact-value test now includes
the approved Cancelled enum value. No migration was generated. Real SQL Server
cancellation concurrency validation remains pending for Phase 4D.4M, and all
frontend cancellation work remains pending for Phase 4D.4N.

## Phase 4D.4N frontend implementation status

The My Leave Requests detail page now supports employee cancellation on the
existing `/leave-management/my-requests/:requestId` route:

- the typed API client posts an empty command to
  `POST /api/leave-requests/{requestId}/cancel` and mirrors the authoritative
  cancellation response;
- Cancel is shown only for persisted `Approved` status, while Withdraw remains
  shown only for `PendingApproval`; no dedicated permission or manager check
  was added;
- cancellation policy, entitlement safety, Allocated blocking, and all other
  eligibility decisions remain server-side; no current-policy, balance, or
  business-date logic was added to the frontend;
- the action requires confirmation, disables itself while pending, prevents
  duplicate calls, and displays accessible `Cancelling…` feedback;
- successful cancellation shows feedback and refetches authoritative detail,
  allowing persisted `Cancelled` status and event history to render naturally;
- `CancellationNotAllowed`, Allocated balance-release blocking, stale status,
  concurrency conflict, and missing-request responses use controlled messages;
- focused API-client and detail-page tests cover visibility, empty-body
  requests, confirmation, in-flight protection, refresh, history, and error
  handling. Backend files, migrations, database access, and package
  dependencies were not changed.

Modify, partial cancellation, cancellation reasons, and administrative or
approval-based cancellation remain deferred.
