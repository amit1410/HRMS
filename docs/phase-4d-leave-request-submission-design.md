# Phase 4D.3F — Idempotent Leave Request Submission Design

Status: DESIGN ONLY. This document defines the future submission boundary; it does not authorize request persistence, balance reservation, approval, migration generation, or database access.

## 1. Submission contract

The future employee self-service endpoint is `POST /api/leave-requests`. Its body contains only the current client-selectable request facts:

```json
{
  "leaveTypeId": "...",
  "startDate": "yyyy-MM-dd",
  "endDate": "yyyy-MM-dd",
  "idempotencyKey": "..."
}
```

Tenant and Employee are server-authoritative. The endpoint must not accept tenant, Employee, LeavePeriod, PolicyVersion, PolicyRule, employment-history, quantity, status, fingerprint, audit timestamp, rowversion, or balance fields. Reason is not added because it is not part of the current Preview contract or persisted request shape.

The endpoint requires authentication and linked-employee self-service identity. The existing administrative Leave Policy permissions must remain separate. A dedicated `Leave.RequestCreate`/self-service permission is still recommended before production authorization hardening; no permission is introduced by this design.

## 2. Revalidation and transaction boundary

Preview is advisory. Submit must rerun `ILeaveRequestValidationService` using the authenticated tenant and `IEmployeeIdentityResolver`; it must never trust Preview IDs, quantities, fingerprint, or employment context supplied by a client. The rerun resolves identity, active LeaveType, StartDate employment, independent LeavePeriod, published applicable Policy, eligibility, request/calendar guards, normalized RequestDays, quantities, and entitlement mode.

The successful validation result is the only input to persistence. One database transaction must create the `LeaveRequest`, all validated `LeaveRequestDay` rows, and the initial `LeaveRequestEvent`. A failure or concurrency conflict rolls back all three sets, so there is no request without its day snapshots or business event. The transaction must be tenant-scoped and use a serialization/locking strategy appropriate to the provider for overlap and limit checks; an ordinary read-then-insert transaction is not sufficient for concurrent claims.

The initial status is `PendingApproval`. The initial event is `Submitted`, with the authenticated account as `ActorUserId`, the linked Employee as `ActorEmployeeId`, `ActorType` representing the authenticated employee action, server UTC `OccurredAtUtc`, and the request correlation identifier when available. `SubmittedAtUtc` is set by the server. No auto-approval is inferred.

## 3. Idempotency and retry behavior

The database uniqueness grain is the existing `(TenantId, EmployeeId, IdempotencyKey)` unique index. The fingerprint is the existing canonical implementation used by Preview and must be computed again after submission revalidation. It includes:

- resolved EmployeeId;
- LeaveTypeId;
- ISO StartDate and EndDate;
- normalized, ordered RequestDay dates and future portion selections;
- normalized Reason only if Reason becomes part of a later persisted contract.

It excludes IdempotencyKey itself, RequestId, RequestDay/Event IDs, CreatedDate, ModifiedDate, SubmittedAtUtc, rowversion, preview timestamps, and random values. Tenant is already in the uniqueness boundary; it remains part of the server-side semantic scope even if it is not duplicated in the hash.

If an existing row is found for the same tenant, employee, and key, compare fingerprints:

- same fingerprint: return the existing request as the same logical success;
- different fingerprint: return a stable `IdempotencyConflict` with HTTP 409.

The recommended response convention is `201 Created` for the insert winner and `200 OK` for an equivalent retry. Both responses contain the same safe submission response. If identical concurrent requests race, the unique constraint selects one winner; the loser catches the unique-key conflict, reloads the existing tenant/employee row, compares fingerprints, and returns 200 or the deterministic 409. A raw provider exception must never escape. A different payload using the same key follows the same reload-and-conflict path.

## 4. Persisted authoritative snapshot

The request stores the validated `EmployeeId`, `LeaveTypeId`, `LeavePeriodId`, `LeavePolicyVersionId`, `LeavePolicyRuleId`, `EmployeeEmploymentHistoryId`, `PolicyGenderSnapshot`, StartDate, EndDate, RequestedQuantity, ChargeableQuantity, `PendingApproval`, server `SubmittedAtUtc`, IdempotencyKey, PayloadFingerprint, audit fields, and SQL Server rowversion. These are historical decision facts, not instructions to re-resolve the request later. Policy priority/specificity remain internal resolver metadata and are not persisted as duplicate request fields.

The exact validated RequestDays are inserted with their DateOnly date, server-derived requested and chargeable quantities, nullable classification/reason snapshots, and `IsEmployeeRequested`. No `WorkingDay`, Holiday, WeekOff, or Sandwich value may be fabricated when the validator returned null.

## 5. Policy and employment changes between Preview and Submit

Submit uses the current authoritative resolution for the request dates. If PolicyVersion, PolicyRule, employment history, LeavePeriod, eligibility, or supported configuration has changed since Preview, the submission uses the new valid result or fails. The client does not select the earlier Preview context. The successful response returns the final persisted IDs, status, dates, quantities, and RequestDays so the UI can display what was actually submitted.

The full-span single-context rule remains mandatory: every date must resolve to the same EmployeeEmploymentHistoryId, LeavePeriodId, LeavePolicyVersionId, and LeavePolicyRuleId. Boundary changes reject the submission; requests are not split.

## 6. Overlap and request limits

Overlap is checked at `(TenantId, EmployeeId, RequestDay.Date)` against parent requests in `PendingApproval` or `Approved`. `Rejected`, `Withdrawn`, and `Cancelled` do not block. The application must pre-check using RequestDay-grain queries, not only root StartDate/EndDate ranges.

The current `(TenantId, LeaveRequestId, Date)` unique index prevents duplicate days inside one request but does not prevent two different requests from claiming the same employee/date. Therefore an application pre-check alone has a race. The implementation must choose and test a real concurrency strategy, such as a SQL Server serializable transaction with appropriate locks/locking hints over the tenant/employee/date scope, or a separately approved active-overlap enforcement design. No unsafe unconditional unique index may be added because historical rejected/withdrawn/cancelled requests must remain storable.

Limit checks use the frozen active statuses (`PendingApproval`, `Approved`) and server-calculated chargeable quantities. Month means Gregorian `YYYY-MM` of each RequestDay.Date; LeavePeriod limits use the resolved LeavePeriodId. The current request schema can support these queries, but read-then-insert checks have the same race risk as overlap. Limits must not be described as concurrency-safe until the transaction/locking strategy is validated on SQL Server.

## 7. Entitlement boundary

`Unlimited` and `NoBalanceRequired` may be candidates for the first persistence implementation: they do not require a finite balance, fake balance read, ledger row, or reservation mutation.

`Allocated` is not production-persistable in this slice. The validator returns `BalanceReservationRequired = true`, but reservation transaction types, atomic projection/ledger updates, deadlock handling, and SQL Server concurrency acceptance are not complete. The service must return a deterministic `AllocatedBalanceReservationNotReady`/unsupported result before creating a request, or route the work to a separately authorized reservation phase. It must never create `PendingApproval` while silently ignoring the required reservation.

The eventual Allocated atomic boundary should be Request + RequestDays + Submitted event + reservation ledger/projection mutation, with idempotency and concurrency handled as one unit. That boundary is future design/implementation work.

## 8. Errors and response

Reuse the existing Result/API envelope and status mapping:

| Condition | Result category | HTTP recommendation |
|---|---|---:|
| malformed input or unsupported current policy | ValidationFailed, stable code | 400 |
| account not linked / authentication failure | existing Unauthorized/Forbidden convention | 401/403 |
| unavailable LeaveType, period, or policy | NotFound/configuration unavailable | 404 |
| ambiguous configuration | Conflict / ConfigurationAmbiguity | 409 |
| active overlap or limit violation | Conflict / stable Overlap or RequestLimitExceeded | 409 |
| same key, different fingerprint | Conflict / `IdempotencyConflict` | 409 |
| same-key equivalent retry | successful existing-request response | 200 |
| unsupported Allocated reservation boundary | stable validation/unsupported category | 400 |

Expected failures must not become 500 responses, and provider exception details must not be exposed.

The successful response should contain `RequestId`, `Status`, LeaveType display data if already available through established DTO conventions, StartDate, EndDate, RequestedQuantity, ChargeableQuantity, SubmittedAtUtc, and RequestDays. Internal policy ranking, raw audit data, and rowversion are not public response fields unless a later transition contract specifically requires a concurrency token.

## 9. Current schema assessment

Yes: the current schema is sufficient for the basic idempotent Unlimited/NoBalanceRequired request, day, and Submitted-event persistence shape. It already has the tenant/employee/idempotency unique index, bounded fingerprint, tenant-aware historical FKs, RequestDay uniqueness, immutable event fields, PendingApproval status, and rowversion.

No new migration is authorized or required by this design. The current schema does not by itself provide cross-request active-overlap uniqueness or concurrency-safe request-limit enforcement. Those are implementation/concurrency-strategy concerns; if the selected SQL Server enforcement strategy requires a schema object, it needs a separate bounded migration decision before implementation.

The existing SQL Server balance concurrency/deadlock debt remains open and blocks Allocated reservation production readiness. Request idempotency, overlap, and limit races also require provider-specific concurrency tests.

## 10. Future implementation and test slices

Recommended sequence:

1. **Phase 4D.3G — Unlimited/NoBalanceRequired submission foundation:** submission DTO, revalidation, idempotency lookup/unique-conflict recovery, atomic Request/Days/Submitted-event transaction, PendingApproval, tenant/actor snapshots, and deterministic result mapping. Reject Allocated before persistence.
2. **Phase 4D.3H — submission API and UI integration:** authenticated self-service permission decision, `POST /api/leave-requests`, retry response behavior, and Preview-to-Submit stale-state handling. No approval.
3. **Phase 4D.4 — overlap/limit concurrency hardening:** SQL Server locking or approved active-claim strategy, race tests, and request-limit acceptance.
4. **Later balance phase:** reservation design, SQL Server concurrency/deadlock tests, then Allocated composition with request persistence.

Focused future tests must cover successful atomic submission, exact one Request/Days/Submitted event, server-derived authority, revalidation, unsupported-no-write paths, context changes, tenant isolation, unlinked/deactivated identity, all overlap statuses, limit statuses, same/different idempotency payloads, concurrent same-key races, no duplicate days/events, rollback with no partial rows, Unlimited/NoBalanceRequired, and deterministic Allocated non-persistence.

SQLite/in-memory tests can cover mapping, transaction composition, and basic uniqueness. Real SQL Server is required for rowversion behavior, unique-conflict recovery under concurrency, overlap locking/serialization, request-limit races, deadlocks, and the eventual balance reservation boundary.

No implementation, migration, database access, or frontend submission change is part of Phase 4D.3F.
# Phase 4D.3H implementation status

The submission foundation now has an Application-level `ILeaveRequestSubmissionService` and an Infrastructure
SQL Server lock adapter. It accepts only LeaveType/date/idempotency input, reruns the existing authoritative
validator, rejects `Allocated` before persistence, and atomically persists only `Unlimited` and
`NoBalanceRequired` requests as `PendingApproval` with ordered RequestDays and exactly one `Submitted` event.

The SQL Server adapter acquires the tenant-plus-Employee scope with parameterized `UPDLOCK, HOLDLOCK` inside
the submission transaction. Existing idempotency is checked before overlap, and the existing unique index
remains the final race arbiter. All future Approve, Reject, Withdraw, and Cancel operations that affect
overlap/counting must acquire this same Employee-scoped lock.

No HTTP submission endpoint or frontend Submit action was added. SQL Server race/deadlock testing and bounded
deadlock retry remain outstanding; isolated foundation tests do not prove SQL Server locking behavior. Balance
reservation remains a separate prerequisite for `Allocated` entitlement.

## Phase 4D.3K public submission API

`POST /api/leave-requests` is now exposed by the authenticated `LeaveRequestsController` using the
existing `ILeaveRequestSubmissionService`. The public body contains only `LeaveTypeId`, `StartDate`,
`EndDate`, and `IdempotencyKey`; tenant, user, Employee, period, policy, employment history, status,
quantities, days, and timestamps remain server-authoritative.

New requests return HTTP 201 with the authoritative persisted response. Same-key/same-fingerprint replays
return HTTP 200 with `IsReplay=true`; same-key/different-fingerprint, overlap, and concurrency conflicts use
the existing HTTP 409 envelope. Validation, identity, Allocated reservation, and UnsupportedConfiguration
failures continue through the shared result mapping, with Allocated remaining blocked before persistence.

The endpoint uses the same authenticated self-service `[Authorize]` model as preview; no new submit permission
was seeded. Preview remains a separate side-effect-free endpoint. No approval/status-transition API, balance
mutation, migration, or frontend Submit action was added.

## Phase 4D.3L frontend submission

The Apply Leave page at `/leave-management/apply` now requires a successful preview before enabling
`Submit Leave Request`. Preview and submission reuse one hidden draft idempotency key; it changes only on
Reset/New Request. The UI sends only LeaveTypeId, StartDate, EndDate, and IdempotencyKey, and displays
server-authoritative quantities and RequestDays.

HTTP 201 and HTTP 200 replay responses both show a completed confirmation. The UI preserves the draft for
validation/conflict errors, prevents duplicate in-flight submits, handles idempotency/overlap/concurrency
conflicts, keeps Allocated blocked because balance reservation is unavailable, and requires a new preview
after semantic form edits. Unlimited and NoBalanceRequired remain the supported submission modes. Approval,
status actions, balance reservation, history, and frontend workflow notifications remain future work.
