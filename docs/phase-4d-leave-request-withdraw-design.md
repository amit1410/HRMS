# Phase 4D.4G — Leave Request Withdraw Workflow Design

## Status and validated baseline

This document is design-only. It authorizes no API, entity, migration,
frontend, or database change.

The validated workflow currently creates `PendingApproval` requests, and the
approved single-step manager workflow supports `PendingApproval -> Approved`
and `PendingApproval -> Rejected`. `PendingApproval` and `Approved` block
overlap/counting; `Rejected`, `Withdrawn`, and `Cancelled` do not.

Submission and approval mutations share a tenant-plus-Employee SQL Server
`UPDLOCK, HOLDLOCK` boundary and a whole-operation retry policy for causal
deadlock 1205 errors, with three total attempts. Withdraw must reuse those
invariants.

## Recommended first slice

Implement only employee self-service withdrawal:

```text
PendingApproval -> Withdrawn
```

Withdraw is a state transition, not deletion. The original request, request
days, Submitted timestamp, quantities, policy/employment snapshots, and prior
events remain unchanged. Only status, normal audit/rowversion fields, and one
new immutable Withdrawn event change.

Approved cancellation is a separate future workflow. Withdraw must not become
an alias for Cancel.

## Ownership and authorization

The authoritative actor is the authenticated employee who owns the request.
The service should resolve the tenant, user, and Employee through the existing
`IEmployeeIdentityResolver` / `AccountEmployeeCurrentLink` path and require:

1. an authenticated active account under the current tenant;
2. a current account-to-Employee link;
3. a target request in that tenant whose `EmployeeId` is the linked Employee.

No manager, HR, administrator, or other employee may withdraw on behalf of the
owner in this slice. The request must not accept tenant, employee, user,
manager, actor, or status values from the client.

### Permission decision

No dedicated `Leave.Withdraw` permission is recommended for the first slice.
Ownership plus the authenticated linked employee identity is the smallest
repository-consistent self-service rule; existing self-service preview,
submission, and My Leave Requests APIs do not use an employee-selectable
workflow permission. A future administrative/on-behalf-of capability would
need a separately reviewed permission and audit contract.

The current identity resolver requires an authenticated tenant and user, finds
the current account link, and verifies that the linked Employee exists. An
unlinked account therefore receives the established identity failure. Existing
authentication/live account handling remains authoritative for deactivated
accounts. The resolver itself checks Employee existence rather than inventing a
new active-Employee rule; withdraw implementation must preserve that current
self-service behavior and test any future inactive-employee policy explicitly.

## Endpoint and public contract

```text
POST /api/leave-requests/{requestId}/withdraw
```

The first slice should use no request body. The route supplies only the target
identifier; tenant, owner, actor, and status are server-derived. No reason or
comment is required or accepted because the current `LeaveRequestEvent` shape
has no reason field. A later reason contract requires separate event-storage
design.

## Status rules and isolation

Only `PendingApproval` may be withdrawn. `Approved`, `Rejected`, `Withdrawn`,
and `Cancelled` return the established deterministic
`InvalidStatusTransition`/conflict result. Repeated withdrawal is not silently
idempotent.

Nonexistent, cross-tenant, and another employee's request must not reveal
existence. The service should return the repository's isolation-safe NotFound
result after applying current tenant and linked-owner predicates. A manager's
relationship to the request owner does not grant withdrawal authority.

Withdrawal does not require current manager resolution. A manager change,
missing manager, or manager ineligibility must not prevent the linked owner
from withdrawing their own pending request.

## Transaction and locking design

The service should follow the same whole-operation shape as approval and
submission:

1. resolve current authenticated identity;
2. safely resolve the target request's employee within the tenant;
3. enter the existing deadlock retry boundary;
4. begin a fresh transaction;
5. acquire the existing Employee row lock using `UPDLOCK, HOLDLOCK` scoped by
   `TenantId + EmployeeId`;
6. re-read the request inside that transaction;
7. revalidate tenant and linked-owner authorization;
8. revalidate that status is `PendingApproval`;
9. set status to `Withdrawn`;
10. append one Withdrawn event;
11. save and commit atomically.

Any failure rolls back both status and event. Do not introduce `sp_getapplock`,
a guard table, a second lock order, or a global isolation-level change.

### Retry and rowversion

Reuse the existing retry policy: retry only causal SQL Server error 1205, at
most three total attempts, with a fresh transaction and reacquired Employee
lock on every retry. Cancellation must stop further retries. The public
endpoint should not require a client rowversion; server-side re-read under the
Employee lock is the authoritative stale-state check, while the existing
rowversion remains an internal EF concurrency safeguard.

## Required race outcomes

All operations affecting whether a request blocks overlap must use the same
Employee lock.

### Withdraw versus overlapping submission

If withdraw serializes first, the request becomes `Withdrawn` before overlap
evaluation and the new overlapping request may submit. If submission
serializes first, it observes the existing `PendingApproval` blocker and must
reject the new overlap; withdrawal may then commit. The invalid outcome is a
new overlapping request persisted from a stale, non-serialized view.

### Withdraw versus approve

The first committed operation wins. The final state is exactly one of
`Withdrawn` or `Approved`; the loser gets a deterministic invalid-transition
or concurrency result. Exactly one terminal event exists, and `Submitted`
remains preserved.

### Withdraw versus reject

The same rule applies: exactly one of `Withdrawn` or `Rejected` commits, never
both events. The loser receives a deterministic transition/concurrency result.

### Withdraw versus withdraw

The Employee lock serializes both attempts. Exactly one succeeds, the final
status is `Withdrawn`, and exactly one Withdrawn event is appended.

## Event history

The existing event schema already stores tenant, request, timestamp, actor
type, actor user, actor employee, and correlation fields. Add a code-level
`Withdrawn` event value using the existing integer conversion; preserve
`Created`, `Submitted`, `Approved`, and `Rejected` events and append rather
than update history.

The successful event should capture the authenticated owner's `UserId` and
`EmployeeId`, `LeaveBalanceActorType.User`, and the repository time-provider
UTC timestamp. Failed authorization, invalid transitions, and failed commits
must write no event.

## Read-model effects

No read-service redesign is required. My Leave Requests should continue to
return a withdrawn request and its immutable days/history because it is the
employee's persisted request. The actionable Approval Inbox already filters to
`PendingApproval`, so a successful withdrawal naturally removes the request
from the manager inbox after refresh. No mutation-side inbox cleanup is
needed.

Withdrawal must not rerun entitlement, calendar, attachment, request-limit,
or policy validation. It acts on the persisted request state. Historical
Allocated requests, if they exist, may be withdrawn as persisted requests
under the ownership/status rules; this does not enable new Allocated
submission or balance reservation.

## Error contract

Reuse existing Result/API mapping:

| Condition | Expected outcome |
|---|---|
| missing authentication or tenant | existing Unauthorized behavior |
| missing account link | existing identity NotFound behavior |
| nonexistent/other-employee/cross-tenant request | isolation-safe NotFound |
| valid identity but invalid ownership/authorization | safe Forbidden or NotFound per established self-service convention |
| status is not PendingApproval | Conflict with `InvalidStatusTransition` |
| deadlock exhausted or EF concurrency failure | existing `ConcurrencyConflict` mapping |

Raw provider details must not be exposed.

## Migration assessment

**Existing schema sufficient: YES.** `LeaveRequestStatus` already includes
`Withdrawn`; request status, rowversion, tenant/employee ownership, immutable
days, and actor-bearing events already exist. `LeaveRequestEventType` currently
contains `Created`, `Submitted`, `Approved`, and `Rejected`; adding the numeric
`Withdrawn` enum member is a code-level value under the existing integer column
and does not require a migration. No migration is authorized or generated by
this design.

## Future test plan

### Foundation/API behavior

- linked owner withdraws own PendingApproval request;
- another employee, cross-tenant account, unlinked account, deactivated
  account, manager, and unrelated admin cannot withdraw it;
- missing current manager does not block the owner;
- Approved, Rejected, Withdrawn, and Cancelled cannot be withdrawn;
- successful transition preserves Submitted and appends exactly one Withdrawn
  event with correct actor user/employee;
- failed authorization/transition persists neither status nor event;
- My Leave Requests still reads the withdrawn request;
- approval inbox no longer returns it.

### Real SQL Server validation

Use only the existing disposable `HRMS_LeaveRequestConcurrency_*` convention
and fixture. Add bounded tests for Withdraw-vs-Withdraw, Withdraw-vs-Approve,
Withdraw-vs-Reject, Withdraw-vs-overlapping-submission, different-employee
isolation, tenant isolation, event atomicity, and actor persistence. Assert
persisted state and event counts, not only task results. Do not use an existing
database or claim SQL race validation from SQLite/in-memory tests.

### Future frontend validation

The My Leave Requests detail page should show Withdraw only for the owner's
PendingApproval request, require confirmation, disable it in flight, prevent
duplicate calls, and refresh authoritative detail/list state after success.
Approved, Rejected, Withdrawn, and Cancelled requests show no Withdraw action.
Tests should cover stale 409 transitions, concurrency conflicts, 401/404 safe
handling, and the absence of a reason field while reasons remain deferred.

## Implementation slicing recommendation

1. **Phase 4D.4H — Withdraw backend foundation:** enum/event value, owner-only
   service, endpoint, shared lock/retry orchestration, atomic transition, and
   focused service/API tests.
2. **Phase 4D.4I — Real SQL Server Withdraw concurrency validation:** the
   bounded race scenarios and disposable-database assertions above.
3. **Phase 4D.4J — My Leave Requests Withdraw frontend:** detail action,
   confirmation, refresh behavior, error handling, and focused UI tests.

Cancel remains a future `Approved -> Cancelled` workflow with separate policy,
time-window, and possible balance consequences. Modify should not mutate an
immutable request in place; it likely requires a replacement/version strategy,
new authoritative validation, and explicit linkage between original and
replacement requests.

## Phase 4D.4I SQL Server concurrency validation

Added the test-only `SqlServerLeaveRequestWithdrawalConcurrencyTests` class,
reusing `SqlServerLeaveRequestConcurrencyFixture`. It uses only the fixture's
uniquely named `HRMS_LeaveRequestConcurrency_*` disposable database convention
and does not connect to an existing HRMS database.

The focused scenarios cover Withdraw-vs-Withdraw, Withdraw-vs-Approve,
Withdraw-vs-Reject, Withdraw-vs-overlapping submission,
Withdraw-vs-non-overlapping submission, different-employee isolation, and
cross-tenant isolation. Assertions read persisted state from fresh contexts
and verify exactly one terminal event, preserved Submitted history, and owner
actor identity where applicable.

The tests exercise the production withdrawal, approval, and submission
services. All three use the same tenant-plus-Employee `UPDLOCK, HOLDLOCK`
serialization boundary; withdrawal uses the existing whole-operation SQL
Server deadlock retry policy. No additional 1205 generator or production lock
change was added. Real SQL Server execution remains a validation gate until
the focused class runs successfully. Withdraw frontend work remains deferred
to Phase 4D.4J.

## Phase 4D.4H implementation status

The owner-only backend foundation is implemented with:

- `POST /api/leave-requests/{requestId}/withdraw`;
- no request body and no dedicated `Leave.Withdraw` permission;
- authenticated current account-to-Employee identity and tenant-scoped owner
  checks;
- no manager resolution and no policy/entitlement/calendar revalidation;
- only `PendingApproval -> Withdrawn`, with deterministic invalid-transition
  failures for other statuses;
- reuse of the existing Employee-scoped `UPDLOCK, HOLDLOCK` lock and bounded
  deadlock retry policy;
- atomic status update plus one immutable Withdrawn event containing the owner
  user and employee actor identity;
- `Withdrawn = 4` added without renumbering existing event values.

Focused foundation and API contract tests were added for ownership,
cross-tenant/unlinked isolation, terminal statuses, lock/retry orchestration,
route/body shape, delegation, and shared error mapping. Real SQL Server race
validation remains pending for Phase 4D.4I, and the My Leave Requests frontend
action remains pending for Phase 4D.4J. No migration or frontend change was
made in this phase.

## Phase 4D.4J frontend implementation status

The My Leave Requests frontend now supports employee withdrawal on the
existing `/leave-management/my-requests/:requestId` detail route:

- the typed API client posts an empty command to
  `POST /api/leave-requests/{requestId}/withdraw` and mirrors the backend
  response (`requestId`, `status`, `eventType`, and `occurredAtUtc`);
- Withdraw is visible only when the authoritative detail status is
  `PendingApproval`;
- the action uses the repository's `window.confirm` pattern, disables itself
  while in flight, shows `Withdrawing…`, and prevents duplicate calls;
- successful withdrawal shows feedback and refetches authoritative detail so
  persisted `Withdrawn` status and event history are rendered;
- invalid status transitions, concurrency conflicts, and missing requests use
  controlled user-facing messages; existing shared authentication behavior
  remains responsible for 401/session handling;
- no dedicated permission, manager dependency, policy validation, identity
  fallback, or client-side history fabrication was added;
- focused API-client and detail-page tests cover visibility, confirmation,
  empty-body requests, in-flight protection, success refresh, stale/conflict,
  and missing-request handling.

Backend files, migrations, database access, and package dependencies were not
changed. Cancel and Modify remain deferred.
