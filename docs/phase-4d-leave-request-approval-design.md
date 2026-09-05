# Phase 4D.4B — Leave Request Approval Workflow Design

## Scope and current state

This document is design-only. No approval, rejection, withdrawal, cancellation,
or modification runtime behavior is implemented by Phase 4D.4B.

The current submission flow creates a `LeaveRequest` in `PendingApproval`,
persists authoritative `LeaveRequestDay` rows, and appends a `Submitted`
`LeaveRequestEvent`. The current read API is employee self-service and remains
read-only. Unlimited and NoBalanceRequired submissions are supported;
Allocated submission remains blocked by `AllocatedBalanceReservationNotReady`.

## Recommended first slice

Implement only a single-step direct-manager workflow:

```text
PendingApproval ──approve──> Approved
PendingApproval ──reject───> Rejected
```

The first slice should be available only for requests that already exist. It
must not add Allocated submission or balance reservation behavior.

Multi-level approval, HR override, comments/reasons, and policy-configured
routing are deferred until their authorization and configuration contracts are
explicitly designed.

## Authorization and identity

The authoritative rule should be:

1. Resolve the authenticated tenant and user from the existing request context.
2. Resolve the approver's Employee through the existing
   `IEmployeeIdentityResolver` account-to-employee link.
3. Require a dedicated approval permission, recommended as
   `Leave.Approve`, enforced by backend authorization and service checks.
4. Require the approver to be the requesting employee's currently effective
   direct manager.
5. Reject self-approval even when the account has the approval permission.

The first slice should require both the linked Employee identity and the
approval permission. An unlinked HR/admin/service account is not an approver
for this slice. An HR/admin override should be a later, explicit permission
and policy decision; it must not be inferred from a broad role name.

Tenant and employee predicates must be applied to every lookup. A manager from
another tenant, a request belonging to another employee, and a nonexistent
request should all produce the repository's non-disclosing `NotFound` result
where the caller is not authorized to know that the request exists.

Permission evaluation must occur at request time. A permission revoked after
login, a deactivated account, or a deactivated/ineligible manager must not be
accepted from stale frontend state.

## Manager resolution and snapshot decision

The existing `EmployeeManagerResolver` is the authoritative manager path. It
resolves the effective `EmployeeEmploymentHistory` record for the requested
date, uses its `ManagerId`, enforces tenant scope, checks that the manager is
actively employed, and detects overlapping employment, invalid references,
cycles, and legacy conflicts. `Employee.ReportingManagerId` and
`EmployeeSupervisor.L1ManagerId` are legacy/current compatibility inputs; they
are not a replacement for the effective employment record.

The model supports L1–L5 and specialized supervisor fields, but those are
supervisor data, not an approval workflow configuration or an approval chain.

### Decision: current manager at action time

The first approval slice should authorize against the current effective direct
manager at action time, using the same resolver and the current business date.
This means:

- a manager change can move authority to the new manager;
- the former manager loses authority after the effective change;
- a missing, inactive, ambiguous, cyclic, or legacy-conflicted manager blocks
  the action deterministically;
- the `LeaveRequest.EmployeeEmploymentHistoryId` remains the authoritative
  employment snapshot for the request, but is not treated as a permanent
  approval authorization snapshot.

If the business later requires approval to remain with the submission-time
manager, the schema will need an immutable approver snapshot on the request (or
a separate routing record). That is not introduced in this phase.

## Future API shapes

These are design contracts only; no routes are added now.

### Approval inbox

`GET /api/leave-approvals?page=1&pageSize=25&status=PendingApproval`

The service should derive the current approver identity and return only
`PendingApproval` requests whose employee has that approver as the effective
direct manager in the same tenant. Results should be ordered by
`SubmittedAtUtc ASC`, then stable request ID ordering, so older work is shown
first. The page should include request ID, employee summary, leave type,
start/end dates, requested and chargeable quantities, status, and submission
timestamp. Tenant ID, client-selected employee IDs, and internal lock data are
not request inputs or public response fields.

### Approve

`POST /api/leave-requests/{requestId}/approve`

The first slice should use an empty request body. Tenant, target employee,
approver employee, and status are server-derived. No comment is accepted until
an event-reason contract exists.

### Reject

`POST /api/leave-requests/{requestId}/reject`

The first slice should also use an empty request body. Rejection reason is
**deferred**, not silently optional: the current event schema has no reason
column or structured reason payload. A later design may require a bounded
reason after adding the appropriate event storage.

## Transition rules and errors

Allowed transitions in the first slice:

| Current status | Approve | Reject |
|---|---:|---:|
| PendingApproval | allowed | allowed |
| Approved | invalid | invalid |
| Rejected | invalid | invalid |
| Withdrawn | invalid | invalid |
| Cancelled | invalid | invalid |

Transitions are not silently idempotent. A second action against a request
that is no longer `PendingApproval` returns a deterministic validation/status
transition error and writes no event.

Recommended stable application outcomes are:

- unauthorized/unlinked identity: existing `Unauthorized` or `NotFound`
  identity behavior;
- request outside the approver's tenant/employee scope: `NotFound`;
- valid request but insufficient permission or manager authorization:
  `Forbidden` / `ApproverNotAuthorized` mapped through existing conventions;
- invalid current status: `Conflict` with an `InvalidStatusTransition`
  application code;
- manager ambiguity or reconciliation failure: `Conflict` with a stable
  `ConfigurationAmbiguity`/manager-resolution message;
- concurrent state/transaction failure after bounded retry:
  existing `ConcurrencyConflict` behavior.

Raw SQL errors must never be exposed.

## Transaction, locking, and deadlock behavior

Every approve/reject attempt must use the same tenant-plus-Employee SQL Server
serialization boundary as submission:

1. resolve immutable authenticated identity outside or at the start of the
   attempt;
2. begin a fresh transaction;
3. acquire the existing Employee lock using `UPDLOCK, HOLDLOCK` on the same
   DbContext connection and transaction;
4. re-read the request under the lock with tenant and employee scope;
5. re-resolve current manager authorization and re-check `PendingApproval`;
6. update status and append exactly one event in the same unit of work;
7. save and commit atomically.

Approval/rejection must use the existing bounded whole-operation retry boundary:
SQL Server deadlock error 1205 only, maximum three total attempts, rollback and
fresh transaction before retry, clean tracking state/fresh unit of work, and
the existing 25 ms bounded delay. Non-1205 errors, invalid transitions,
authorization failures, and business configuration failures are not retried.

Important races:

- approve versus approve: the Employee lock serializes them; the second sees
  `Approved` and writes no second event;
- approve versus reject: the first committed transition wins; the second gets
  an invalid-transition conflict;
- reject versus new submission: the lock serializes rejection and overlap
  evaluation, so a newly submitted overlapping request cannot observe a stale
  `PendingApproval` blocker;
- approve versus new submission: both remain serialized for the employee;
  `Approved` continues to block overlap/counting as it does today.

The transition service must not create a second lock order or a separate
unbounded retry policy.

## Event history

The existing `LeaveRequestEvent` already stores the required actor and audit
identity fields:

- TenantId
- LeaveRequestId
- OccurredAtUtc
- ActorType
- ActorUserId
- ActorEmployeeId
- CorrelationId

The event table is append-only and the DbContext rejects event updates/deletes.
The first implementation should add `Approved` and `Rejected` event values to
the existing integer-backed event enum and append one event per successful
transition. It must preserve `Created` and `Submitted` and must not overwrite
them.

Because the current event entity has no reason/comment field, rejection reason
is deferred. Adding a required reason later would require explicit event
storage design rather than putting unstructured data in `CorrelationId`.

## Rowversion and stale clients

The service should use server-side Employee locking plus a fresh status read;
the public action should not require a client-provided rowversion in the first
slice. The request rowversion remains an internal EF concurrency safeguard and
may be returned only where an established API contract requires it. Adding a
client token would not replace the employee lock because rejection changes the
overlap-visible state and must serialize with submission.

## Withdraw, cancel, and modify — deferred analysis

These are not part of the approval implementation:

- Withdraw: likely `PendingApproval -> Withdrawn`, initiated by the requesting
  employee, using the same Employee lock and an append-only event.
- Cancel: likely `Approved -> Cancelled`, with policy/time-window and actor
  authorization still to be designed. It must also use the Employee lock
  because it changes whether the request blocks/counts.
- Modify: should not mutate the immutable request snapshot in place. It likely
  needs a replacement/version strategy with idempotency, new authoritative
  validation, and explicit linkage between the superseded and replacement
  requests.

No status-transition endpoints exist in this phase.

## Allocated impact

Approval design does not unblock Allocated submission or balance reservation.
The first workflow implementation can operate on already persisted historical
requests, including a request whose persisted policy configuration represents
Allocated, but no new Allocated request may be created until reservation is
implemented and validated. Approval must not add a balance mutation as a side
effect.

## Migration assessment

**Existing schema sufficient for the first approval/rejection slice: YES**,
provided rejection comments/reasons are deferred and `Approved`/`Rejected`
are added as code enum values using the existing integer event column. Existing
status, rowversion, employee/tenant keys, request employment snapshot, and
event actor fields are sufficient for the atomic single-step transition.

No migration is generated by this design phase.

## Implementation test plan

### Authorization

- direct linked manager with `Leave.Approve` can approve;
- direct linked manager with `Leave.Approve` can reject;
- non-manager with permission is blocked;
- self-approval is blocked;
- manager from another tenant is blocked and receives non-disclosing result;
- permission revoked after login is blocked;
- deactivated approver/account is blocked;
- unlinked approver is blocked;
- changed manager follows the current-manager-at-action-time decision;
- nonexistent or unauthorized request does not leak existence.

### Status and atomicity

- `PendingApproval -> Approved` updates status and writes one Approved event;
- `PendingApproval -> Rejected` updates status and writes one Rejected event;
- terminal statuses cannot transition;
- failed authorization/status checks write no event and do not change status;
- event actor user/employee and timestamp are correct;
- request, status, and event commit atomically.

### Concurrency and isolation

- approve versus approve has one winner/event;
- approve versus reject has one committed transition/event;
- reject versus overlapping new submission is serialized correctly;
- approve versus overlapping new submission is serialized correctly;
- SQL Server deadlock 1205 uses the existing three-attempt retry boundary;
- non-1205 failures are not retried;
- tenant and employee predicates prevent cross-scope reads/actions;
- stale manager/configuration resolution is rechecked after the lock.

## Deferred items

HR override policy, rejection reasons, approval inbox UI/API, multi-level
routing, notification, balance reservation,
withdraw/cancel/modify transitions, and any manager snapshot migration require
separate reviewed phases.

## Phase 4D.4C implementation status

The first backend foundation is implemented with:

- `POST /api/leave-requests/{requestId}/approve`;
- `POST /api/leave-requests/{requestId}/reject`;
- the dedicated `Leave.Approve` permission using the next stable permission
  identifier;
- live linked-account, active-user, permission, tenant, self-approval, and
  current direct-manager checks;
- action-time manager resolution through `EmployeeManagerResolver`;
- the existing Employee-scoped `UPDLOCK, HOLDLOCK` lock and whole-operation
  SQL Server deadlock retry boundary;
- atomic status plus immutable actor event persistence;
- `Approved` and `Rejected` event values while retaining `Created` and
  `Submitted` history.

The endpoints accept no request body fields. Rejection reasons, approval inbox,
frontend actions, SQL Server race validation, multi-level approval, and
Withdraw/Cancel/Modify remain deferred. Allocated submission and balance
reservation remain blocked. No migration was generated.

## Phase 4D.4D SQL Server concurrency validation

Added the test-only `SqlServerLeaveRequestApprovalConcurrencyTests` class using
the existing `SqlServerLeaveRequestConcurrencyFixture`. Each run creates and
drops a uniquely named `HRMS_LeaveRequestConcurrency_*` disposable database and
uses the existing migration/seed convention.

The scenarios cover approve-versus-approve, approve-versus-reject,
reject-versus-overlapping submission, approve-versus-overlapping submission,
different-employee activity, and cross-tenant request isolation. Assertions
scope persisted state by tenant, employee, request, and idempotency key, and
verify immutable Submitted history plus exactly one terminal event. Successful
approval also verifies the persisted manager actor user and employee identity.

Approval uses the production account-to-employee resolver, current manager
resolver, `Leave.Approve` grant, employee `UPDLOCK,HOLDLOCK` lock, and existing
whole-operation deadlock retry policy. No additional deadlock generator is
added because the existing real 1205 classifier tests and retry-policy tests
already validate that shared boundary.

Real SQL Server execution remains a separate validation gate; this section is
not a PASS claim until the focused class executes successfully. Withdraw,
Cancel, Modify, multi-level approval, rejection reasons, balance reservation,
and Allocated submission remain outside this phase.

## Phase 4D.4E approval inbox read API

The backend approval inbox read slice is implemented separately from employee
self-read APIs:

- `GET /api/leave-approvals` returns a deterministic, paged actionable inbox;
- `GET /api/leave-approvals/{requestId}` returns authorized request detail;
- both routes require authentication and the live `Leave.Approve` permission;
- the account must resolve through the current employee link and be the current
  direct manager at read time;
- the default list scope is `PendingApproval` only;
- tenant, manager, employee, and self-request isolation is enforced in the
  read service;
- persisted request quantities, days, and events are returned without rerunning
  submission validation or acquiring mutation locks.

Focused service and API contract tests cover authorization, manager changes,
status filtering, ordering, paging, detail days/events, isolation, delegation,
and shared HTTP error mapping. No mutation service, submission behavior,
locking, retry policy, migration, or frontend behavior was changed. Frontend
approval inbox work remains deferred.

## Phase 4D.4F frontend approval inbox

The manager approval inbox frontend is implemented with these routes:

- `/leave-management/approvals` lists the authenticated manager's actionable
  PendingApproval requests with backend paging;
- `/leave-management/approvals/:requestId` shows the authorized request detail.

The Leave Approvals navigation entry is visible only when the current frontend
permission state includes `Leave.Approve`; backend authorization remains
authoritative. The list displays employee and leave-type summaries, dates,
server-provided requested and chargeable quantities, status, submission time,
and a detail link. The detail view renders persisted RequestDays and event
history without recalculating or fabricating values.

Approve and Reject are available only for a pending request and require a
simple confirmation. They send empty POST commands to the existing backend
endpoints, disable the action while in flight, and navigate back to the
refreshed inbox after success. Invalid status transitions, concurrency
conflicts, authorization failures, missing requests, and session errors are
shown through controlled user-facing messages without automatic retries.
Rejection does not expose or submit a reason because the current backend
contract accepts no body.

Focused API-client and page tests cover request shapes, paging, empty/error
states, detail days and history, action visibility, no rejection reason,
single in-flight actions, successful transitions, and stale-transition
handling. Backend files, mutation behavior, migrations, and package
dependencies were not changed in this frontend phase. Withdraw, Cancel,
Modify, multi-level approval, notifications, balance reservation, and
Allocated submission remain deferred or blocked as documented above.
