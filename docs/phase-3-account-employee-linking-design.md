# Phase 3A — login-account-to-employee linking design and migration-risk review

Status: REVISED DESIGN ONLY — all proposed decisions below remain pending approval. Phase 3B is not authorized.

Review date: 2026-09-03. Authoritative source revision: `2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e` in `amit1410/HRMS`.

## 1. Scope, provenance, and validation status

`AGENTS.md` was read before source inspection, both locally and at the requested checkpoint. The checkpoint tree contains no nested `AGENTS.md`. The original report was created in an earlier review. This revision updates that existing report and supersedes its nullable User.EmployeeId recommendation.

This environment cannot access the Windows path `D:\HRMS`. Its available checkout is `/workspace/scratch/72f2d400cec1/HRMS`, at the older revision `fc4d81ff68bc7068af63dff775276a6ee4f2ee52`, initially clean. The exact requested Phase 2 commit was verified and its source read through GitHub without modifying the checkout. All current-behavior findings below refer to that pinned remote revision, not the older local source. Uncommitted changes or an existing report on the Windows machine cannot be verified here. Recheck those before transferring this report or implementing Phase 3B.

Only this Markdown report was revised in this task. No application/test code, dependencies, migrations, database contents, commits, pushes, or deployments were changed. No database connections, preflight queries, builds, tests, or API startup were performed.

| Validation item | Status |
| --- | --- |
| Backend focused tests, 49/49 | User-reported Phase 2 result; not rerun or independently certified |
| Frontend tests, 292/292 | User-reported Phase 2 result; not rerun or independently certified |
| Backend and frontend builds | User-reported passed; not rerun |
| Browser acceptance | Outstanding |
| SQL Server concurrency | Outstanding |
| Legacy reconciliation | Outstanding |
| Phase 3A validation | Static source/design review only |

## 2. Current identity and authorization

### Source reference convention

All repository links in the source index at the end are pinned to the baseline commit. References such as S1 and S8 below refer to that index; methods are specified to make the review reproducible.

### Entities and existing relationship

`User` is a tenant-owned login with a stable GUID, email, password hash, names, `IsActive`, `LastLoginDate`, and role assignments. `Employee` is a separate tenant-owned HR record with a stable GUID, employment dates/status, sensitive personal fields, and employment history. The Employee documentation explicitly states that login linking is a later concern and its work email is unrelated to a login. Neither entity has a link property or navigation to the other. The EF configurations, context, model snapshot, and complete checkpoint entity tree contain no account/employee mapping entity. **There is no existing account-to-employee relationship to reuse.** Do not infer one from matching emails, role names, manager categories, names, or employee codes. [S1–S3]

Both use `BaseEntity` timestamps. Neither has a soft-delete property. Employee deletion currently physically removes the row through `EmployeeService.DeleteAsync`; direct reports are checked and database reference failures return conflict. Existing employee child/audit relationships include cascading deletes. `Employee.Status` has `Active`, `Resigned`, and `Terminated`; there is no dedicated `Retired` enum member. `User.IsActive` and employment status are independent. [S1, S9, S10]

### Request and authentication flow

1. `Program.cs` applies forwarded headers, CORS, host/shard resolution, rate limiting, JWT authentication, and authorization in that order. `TenantShardResolver.ResolveByHostAsync` normalizes the host, performs a catalog lookup, and caches the descriptor. The host chooses the database; verified claims choose the tenant rows. Shared mode and tenant-sharded mode are both supported. [S4]
2. `POST /api/auth/login` is anonymous and rate limited. `AuthService.LoginAsync` requires a resolved shard, explicitly scopes account lookup to its tenant, verifies the password, checks account activation and tenant status, reloads roles/permissions, and issues tokens. The client does not choose a tenant ID in the login body. [S5]
3. `JwtTokenService` issues HMAC-SHA256 JWTs containing `sub`, `jti`, `uid`, `tid`, `tcode`, email, names, repeated roles and permissions. There is no employee claim. Refresh tokens are cryptographically random opaque values stored as SHA-256 hashes. JWT validation checks signing algorithm, signature, issuer, audience and lifetime. No credentials are included in this report. [S6]
4. Default, fallback, and named permission policies all use the tenant-scoped base. `TenantMatchesShardHandler` rejects authenticated requests without a resolved host, mismatched tenant GUID, or inconsistent tenant code. `HttpTenantContext` reads `uid` and `tid` only from an authenticated principal. Tenant query filters fail closed when the tenant is absent; SaveChanges stamps inserted tenant IDs and prevents tenant changes on updates. Filters alone do not enforce foreign-key tenant consistency. [S3, S7]
5. `POST /api/auth/refresh` is anonymous. It finds a stored hash, checks host/token tenant disagreement when a shard exists, checks a supplied bearer tenant when present, rejects expired/revoked tokens, and reloads account activation and tenant status. A conditional update consumes the token; replay revokes active refresh tokens for the account. Replacement issuance reloads grants. **Source caveat:** the host check in `RefreshAsync` remains null-tolerant, unlike authenticated route authorization. In shared mode, an unresolved host is not explicitly rejected there. Record this as an auth-hardening prerequisite; do not assert that every refresh path already requires a resolved host. [S5, S7]
6. `POST /api/auth/logout` requires authentication and revokes the supplied refresh token within the caller's tenant/user scope. It does not revoke an already-issued JWT. `GET /api/auth/me` rereads the tenant-scoped account and roles/permissions; missing user returns 404 and inactive user returns 403. It returns no employee identity. [S5]
7. The bearer policies do not reread `User.IsActive` for every ordinary API request. Activation and grant changes are observed at login/refresh and `/me`, but an existing JWT can retain access until expiration on other endpoints. There is no session/security-version claim enforcing immediate invalidation. This must be addressed explicitly for secure link operations and self-service; token refresh alone is insufficient. [S5–S7]

### Administration, roles, employee access, and frontend

`Permissions.User` contains View/Create/Edit/Delete, and seeded accounts and role assignments exist. However, the checkpoint controller/service tree, frontend routes and navigation contain **no implemented user-management or role-assignment administration screen/API**. Do not claim an existing user edit page is available for integration. Employee administration does exist: employee routes use separate view/create/edit/delete/export permissions, and sensitive data uses additional permissions. Employment history has its own View/Change permissions. [S8–S10, S12]

Roles, permissions and role-permission grants are shared reference data within each HRMS database; `UserRoles` carries a tenant ID. `AuthService.LoadAuthorizationAsync` explicitly scopes assignments by tenant and user. `UserRoleConfiguration` uses `(UserId, RoleId)` as its key and a simple UserId FK, not a composite same-tenant FK; legacy tenant mismatches must be included in preflight. Do not expand this review into repairing all authorization relationships. [S5, S8]

`SeedData.RolePermissionMap` assigns `Permissions.All` to SuperAdmin and TenantAdmin, and `DatabaseSeeder.SeedRolePermissionsAsync` adds missing grants on startup. Adding a new permission to the catalog without revising the grant strategy would therefore silently grant it to those roles and potentially restore a deliberately revoked grant. Role descriptions do not override runtime tenant scope: SuperAdmin must still operate within an authenticated, host-matched tenant. The Employee role currently receives Geography.View, not employee directory or approval rights. [S8]

`AuthProvider` holds the user response in memory, restores via refresh, and reacts to refreshed/expired session events. The transport stores the access token in memory and refresh token in localStorage; it does not persist a trusted user/employee profile. `fetchCurrentUser` exists, but the provider does not automatically reload `/me` after an administrator changes another user's link. `RequirePermission`, `permissions.ts`, and `visibleNavItems` provide cosmetic UI checks; API checks remain decisive. [S12]

### Audit and Phase 2 manager resolution

`EmployeeAuditLog` records tenant/employee, fields, old/new values, reason, source and timestamps. `ChangedBy` is a string documented as email or employee code, and the subsection controller supplies the email claim. `EmployeeAuditService.LogChangeAsync` saves separately and catches `DbUpdateException`; its current behavior is unsuitable for a security operation requiring durable, atomic audit. Its employee FK cascades on deletion. Base timestamps and ordinary application logs are not a substitute for immutable actor IDs and link history. [S11]

`EmployeeManagerResolver.ResolveAsync(employeeId, asOfDate)` reads the effective employment-history row, using inclusive `EffectiveFrom`/`EffectiveTo`. No applicable employment, overlaps, no assignment, invalid reference, current-date legacy disagreement, ineligible manager employment and reporting cycles are explicit outcomes. It does not silently fall back to legacy manager columns. It checks a cycle at the requested date; `WouldCreateCycleAsync` additionally checks future effective-start boundaries for proposed assignments. Supervisor reads use resolved L1, while divergent L1 updates are refused. L2/L3/HR/time categories are not login permissions. [S13]

This is manager resolution, not a complete manager authorization policy. A future approval handler must independently establish the caller's linked employee, relevant permission and reporting scope. LegacyConflict and other unresolved states must deny the affected approval operation rather than select a guessed approver. Browser acceptance, legacy reconciliation and real SQL Server concurrency remain open.

### Revision evidence and limits

This revision re-read pinned UserConfiguration, EmployeeConfiguration, AuthenticatedUserDto, AuthService, TenantMatchesShardRequirement, EmployeeManagerResolver, EmployeeEmploymentService, SeedData role-grant definitions, DatabaseSeeder, SchemaPreparer, App routes and AuthProvider. UserConfiguration has no `(TenantId, Id)` alternate key; EmployeeConfiguration already defines it. The role-grant map includes all catalog permissions for SuperAdmin and TenantAdmin. The `/me` DTO currently contains account, tenant, names, roles and permissions only. These observations support the revised proposals below. Other baseline observations and the pinned source index are retained from the original review; this is not a fresh certification of every source path or of the Windows working tree.

No source checkout was advanced, and no database or application was started. Existing untracked report status is preserved. Before implementation, reconcile this document with the actual Windows branch and any local report changes.

## 3. Proposed data model — pending approval

Use two dedicated tables: `AccountEmployeeCurrentLinks` for current identity and `AccountEmployeeLinkEvents` for immutable history. Do not add `User.EmployeeId`, an active flag on historical events, or effective-dated link intervals. An unlinked account has no current-link row. An inactive account's retained row still reserves its employee.

All GUIDs are application-generated; SQL Server types below are logical migration targets, with explicit SQLite mappings inspected during Phase 3B. All columns are required unless marked NULL. UTC timestamps are server-generated `datetime2(7)`; reasons are trimmed, nonempty and limited to 500 characters in service validation and database checks. No event inherits mutable ModifiedDate behavior from BaseEntity.

### Exact proposed columns and constraints

| Table | Columns |
| --- | --- |
| AccountEmployeeCurrentLinks | `LinkId uniqueidentifier` PK; `TenantId uniqueidentifier`; `UserId uniqueidentifier`; `EmployeeId uniqueidentifier` |
| AccountEmployeeLinkEvents | `Id uniqueidentifier` PK; `TenantId uniqueidentifier`; `SubjectUserId uniqueidentifier`; `ActorUserId uniqueidentifier`; `Sequence bigint`; `Operation nvarchar(10)`; `PreviousEventId uniqueidentifier NULL`; `PreviousLinkId uniqueidentifier NULL`; `NewLinkId uniqueidentifier NULL`; `BeforeEmployeeId uniqueidentifier NULL`; `AfterEmployeeId uniqueidentifier NULL`; `OccurredAtUtc datetime2(7)`; `Reason nvarchar(500)`; `CorrelationId nvarchar(100)` |

`LinkId` is stable for one association lifetime. A successful Link or Replace uses its new event ID as the new LinkId. Unlink removes the current row but never deletes its creation event. Relinking the same pair creates a fresh event/LinkId. Current-link original actor/time are read from that creation event; never copied from the latest operation or overwritten.

The latest committed event ID for an account is its opaque `revision`, including when unlinked; null means no events have ever existed. Sequence starts at 1 and increments per tenant/account under the transaction lock described below. Order events by Sequence, never timestamp or GUID. This avoids the stale-null problem when an account goes unlinked → linked → unlinked.

| Constraint | Proposed definition |
| --- | --- |
| Principal keys | Add `AK_Users_TenantId_Id` before dependent FKs. Reuse existing Employees `(TenantId, Id)` alternate key; inspect migration to avoid duplication. |
| Current uniqueness | Ordinary non-null unique indexes `UX_AccountEmployeeCurrentLinks_TenantId_UserId` and `UX_AccountEmployeeCurrentLinks_TenantId_EmployeeId`. No filters or nullable identity components. |
| Current principal FKs | `(TenantId, UserId)` → Users `(TenantId, Id)` and `(TenantId, EmployeeId)` → Employees `(TenantId, Id)`. |
| Event ordering | Unique `(TenantId, SubjectUserId, Sequence)`; check Sequence > 0. Alternate key `(TenantId, SubjectUserId, Id)` for event references. |
| Event principal FKs | `(TenantId, SubjectUserId)` and `(TenantId, ActorUserId)` → Users `(TenantId, Id)`; optional `(TenantId, BeforeEmployeeId)` and `(TenantId, AfterEmployeeId)` → Employees `(TenantId, Id)`. |
| Event chain FKs | Optional `(TenantId, SubjectUserId, PreviousEventId)` and `(TenantId, SubjectUserId, PreviousLinkId)` → events `(TenantId, SubjectUserId, Id)`. First sequence requires null PreviousEventId; later sequences require non-null and a different event ID. Service verifies predecessor is the latest sequence. |
| Current creation FK | `(TenantId, UserId, LinkId)` → events `(TenantId, SubjectUserId, Id)`. Service verifies referenced event is Link/Replace and its AfterEmployeeId equals current EmployeeId. |
| New link reference | For Link/Replace enforce `NewLinkId = Id` and non-null; Unlink requires null. This identifies the retained creation event without a circular current-table FK. |
| Tenant and deletion | Both new tables reference local Tenants. All new FKs use NO ACTION / restrictive behavior, never cascade or SET NULL. |
| History lookup | Index `(TenantId, BeforeEmployeeId, OccurredAtUtc, Id)` and equivalent AfterEmployeeId; account history uses the sequence index. |

Operation checks must explicitly require null/non-null values so SQL three-valued CHECK behavior cannot admit incomplete events:

| Operation | Required event shape |
| --- | --- |
| Link | PreviousLinkId and BeforeEmployeeId null; NewLinkId = Id; AfterEmployeeId non-null. PreviousEventId can reference an earlier Unlink. |
| Unlink | PreviousLinkId and BeforeEmployeeId non-null; NewLinkId and AfterEmployeeId null. |
| Replace | Both link IDs and employee IDs non-null; NewLinkId = Id; previous/new links differ; before/after employees differ. |

The service additionally verifies that PreviousLinkId references a creation event with the exact previous employee and account, and that the current projection agrees with the event chain. FKs enforce tenant and reference existence; they do not by themselves enforce the complete event state machine or projection equality. Direct writes outside the reviewed service are prohibited. A privileged database writer remains a trust boundary.

### Immutability and deletion consequences

No update/delete API, generic edit DTO, import or background job may modify events. EF must reject Modified/Deleted event entries; this does not protect raw SQL. Proposed production database principal grants permit SELECT/INSERT on events and deny UPDATE/DELETE, with schema deployment performed through a separate authorized identity. Review existing broad database privileges before claiming this control is effective. SQLite test protection is application-level; file owners are outside that boundary. No trigger or operational privilege change is executed in this phase.

Restrictive history FKs deliberately prevent hard deletion of a user or employee referenced by any event, including an actor, even after unlink. Current links also prevent deletion. Map known deletion conflicts to 409 without exposing history to ordinary employee viewers. Account deactivation and employment separation preserve history. This is a stronger deletion policy than the previous report and requires explicit approval. Any later erasure/retention policy needs a separate design; do not silently drop constraints, delete history, or pretend unlink enables deletion.

## 4. Proposed permissions, grants and administration

All proposals in this section await approval. Introduce exactly `AccountEmployeeLink.View`, `AccountEmployeeLink.ViewHistory`, and `AccountEmployeeLink.Manage`. Manage does not imply either read permission; command routes require View + Manage. History requires ViewHistory and can be granted independently of Manage. Each endpoint checks current active actor and tenant-scoped database grants, in addition to existing authenticated host/tenant policies; an old JWT with revoked grants is insufficient.

| Existing/proposed role | View | ViewHistory | Manage | Proposed assignment |
| --- | --- | --- | --- | --- |
| Existing SuperAdmin / TenantAdmin | No new grant | No new grant | No new grant | Explicit special-role assignment required |
| Existing HRAdmin / HRManager / Manager / Employee | No new grant | No new grant | No new grant | Existing employee/user permissions do not imply linking |
| Proposed AccountLinkAdministrator | Yes | No | Yes | Named, reviewed active accounts via tenant-scoped UserRoles |
| Proposed AccountLinkAuditor | Yes | Yes | No | Separately reviewed audit operators |
| Service account | No default | No default | No default | Classification currently requires operator verification |

A person needing both management and history can receive both special roles after approval. Allocate stable unused IDs without renumbering existing permissions/roles. Separate the permission catalog from broad `Permissions.All` startup grants; otherwise adding these constants would silently expand administrator authority. Do not change existing non-link grants. Special-role definitions and their grants must not be restored automatically after revocation by normal startup. Proposed provisioning is a separately reviewed operator procedure; no runtime grant action, role assignment, or new general role-management feature is authorized here. Shared RolePermissions affect all members of that role in a physical shared database; UserRoles assignments must include the tenant.

Recommend one small standalone `/administration/account-employee-links` page, guarded by View, with a separately guarded ViewHistory section. This avoids requiring broad Employee.View merely to link accounts. Reuse existing layout, teal styling, API envelope, permission guards and dialogs. Do not invent an existing user-administration screen. General user CRUD, role editing, employee editing and impersonation are excluded. An employee-detail shortcut can be deferred.

Propose prohibiting an operator from changing their own account's link; another reviewed operator must do it. No self-claim endpoint. Routine reuse of one person's login for another person is prohibited; replacement is for verified corrections with a reason, not credential sharing. The source has no reliable account-kind field: operators must verify individual-account ownership; a technical service-account classification model would need separate approval.

## 5. Proposed API and atomic operation contracts

Prefix: `/api/account-employee-links`. Reuse the existing envelope; successful mutations return the fresh current-state DTO. `revision` and LinkId are freshness tokens, not authorization credentials. Tenant and ActorUserId are always server-derived. Administrative employee IDs identify authorized target objects only; they never establish self identity.

| Method and suffix | Permission | Request/response |
| --- | --- | --- |
| GET `/users/{userId}` | View | `{ userId, status: Linked/Unlinked/Invalid, currentLink: null or {linkId, employeeId, displayName, employeeCode}, revision }` |
| GET `/employees/{employeeId}` | View | Minimal reverse mapping or Unlinked; no full user profile |
| GET `/candidates/users?search=&page=&pageSize=` | View + Manage | Active unlinked accounts excluding actor; ID, displayName and login email for disambiguation only |
| GET `/candidates/employees?search=&page=&pageSize=` | View + Manage | Unlinked employees allowed for new linking under section 7; ID, displayName, optional code and eligibility summary |
| POST `/users/{userId}/link` | View + Manage | `{employeeId, expectedRevision, reason}`; expectedRevision explicitly present, possibly null |
| POST `/users/{userId}/unlink` | View + Manage | `{expectedLinkId, expectedEmployeeId, expectedRevision, reason}`; all preconditions non-null |
| POST `/users/{userId}/replace` | View + Manage | `{expectedLinkId, expectedEmployeeId, expectedRevision, newEmployeeId, reason}`; atomic correction |
| GET `/users/{userId}/history?page=&pageSize=` | ViewHistory | Immutable events ordered by Sequence descending; tenant-scoped IDs, operation, actor, before/after, reason, UTC time; no full profiles |

Page starts at 1, default pageSize 25, maximum 50; reject out-of-range values. Search minimum two trimmed characters, maximum 100; deterministic name/ID order. Return `{items, page, pageSize, hasMore}`. Parameterize search and escape wildcard syntax. Candidate responses are advisory; repeat object, occupancy and eligibility checks on mutation. History reasons may contain sensitive free text: UI guidance forbids secrets and personal identifiers, restrict history access and HTML-escape display.

Permission denial is 403 before object lookup. Unknown and other-tenant IDs produce indistinguishable 404. Malformed requests, omitted preconditions and blank/overlong reason produce 400. Known occupancy, ineligibility and stale-state errors produce 409 (`LinkConflict`, `SubjectNotEligible`, `EmployeeNotEligible`, `LinkStateChanged`). Generic conflicts must not identify another account or tenant. Do not return raw constraint names, SQL, tokens or profile fields. Current/history read endpoints are separate permission boundaries.

### Mutation transaction and concurrency protocol

1. Validate authenticated host/tenant scope and request shape. Begin a short transaction in the selected tenant database. Check actor activation and current required grants within the consistency boundary.
2. Serialize changes for the subject account using its stable Users row, even when no current link exists. Proposed SQL Server implementation uses tenant-qualified `UPDLOCK, HOLDLOCK` reads within SERIALIZABLE isolation; SQLite uses an explicit write transaction with bounded busy handling. Lock required User rows in sorted GUID order, then involved Employees in sorted GUID order, consistently across commands. Lock/read relevant grants and employment ranges until commit. Inspect generated SQL and provider behavior; in-memory locks are insufficient.
3. Read latest event/sequence and current link; validate their agreement. Compare expectedRevision and, for Unlink/Replace, exact expectedLinkId/employee. Reject corrupt or stale state without attempting repair. Null revision only matches a never-changed account.
4. Validate target existence, same tenant, activation and new-link eligibility. Check employee occupancy and effective history under the transaction. Unlink does not require the subject or employee to remain active.
5. Allocate one new event ID, next sequence and server timestamp. Link inserts one event and current row. Unlink appends one event and conditionally deletes exactly the expected current row. Replace appends one event and conditionally deletes the old row and inserts the new row. Use staged SaveChanges inside the same transaction when necessary for FK ordering; never commit between them.
6. Require the expected affected-row count. Commit only if current state and event persist together. Audit failure, insertion conflict or unexpected row count rolls back the complete operation, preserving the old mapping and history.

SQL Server ordinary unique indexes are the final authority for simultaneous claims in both directions. Transaction locking supplies ordering for changes, activation checks and event sequences. SQLite tests do not prove SQL Server concurrency. Translate only recognized constraint/concurrency failures; bounded whole-transaction retries may handle a known rolled-back deadlock/busy failure after revalidation. Do not blindly retry an unknown commit result. Client retries use the same expected revision, so a previously committed mutation returns stale conflict and cannot append a duplicate success event; refresh current state and, if permitted, history.

### Replacement and conflict cases

| State | Outcome |
| --- | --- |
| Subject unlinked or old link/version mismatches | 409; use ordinary Link only after fresh review |
| Target equals current employee | 409 validation conflict; no fake replacement event |
| Target employee belongs to another account, including disabled account | 409 generic conflict; do not displace, swap or auto-unlink |
| Target missing/other tenant | Indistinguishable 404 |
| Target fails new-link eligibility or subject disabled | 409; existing link remains |
| Two replacements compete for same subject | One winner; stale loser; one new current link and one committed event |
| Different accounts replace toward same employee | At most one winner; losing account retains original link |
| Unlink races Replace | Serialized winner; stale loser; no partially replaced state |
| Event or new-row insert fails | Full rollback retains original current row and all prior events |

Replacement is one explicit command with mandatory reason and confirmation of both identities. Cross-account transfer requires separate reviewed unlink and fresh link operations; there is no silent displacement or two-account swap. Replacement does not rewrite employment, permissions or historical ownership.

## 6. Proposed server-side identity and /me contract

Resolver: authenticated `uid` + host-matched `tid` → active same-tenant User → current-link row → same-tenant Employee → separate effective-employment assessment. No employee claim, client employee ID, email, code, browser storage or cached `/me` response may select self. Resolve current state on every link-sensitive request; no cross-request identity cache initially. Invalid references or projection/history disagreement fail closed and raise an internal diagnostic without exposing other tenants.

Preserve every existing `/api/auth/me` field and its missing/inactive-user behavior. Add one object:

```json
{
  "employeeIdentity": {
    "status": "Linked",
    "revision": "00000000-0000-0000-0000-000000000001",
    "linkId": "00000000-0000-0000-0000-000000000001",
    "employee": {
      "id": "00000000-0000-0000-0000-000000000002",
      "displayName": "Example Employee",
      "employeeCode": null
    },
    "employmentEligibility": "ActiveEmployment",
    "businessDate": "2026-09-03"
  }
}
```

Illustrative GUIDs only. Status enum: `Linked`, `Unlinked`, `Invalid`. Unlinked returns null linkId/employee, `NotLinked` eligibility, and the latest revision or null. Invalid returns no employee/link ID, null revision, `RequiresReview` and denies employee actions. Eligibility enum: `NotLinked`, `FutureJoining`, `NoApplicableEmployment`, `ActiveEmployment`, `Separated`, `RequiresReview`. `ActiveEmployment` describes employment only and grants no operation permission. businessDate is a server ISO date, provisionally UTC to match Phase 2. Reasons requiring administrative investigation are not exposed as raw database details.

Propose keeping login/refresh payloads unchanged in Phase 3B and fetching `/me` after login/restore and refresh. Use a separate frontend identity state with Unknown/loading until `/me` completes; absent property means feature unavailable, not Unlinked. This limits changes to anonymous auth flows. Feature entry and window focus refetch identity; administrative changes in another session are enforced by server checks even before the UI refreshes.

On revision change or `IdentityContextChanged`, clear employee-scoped queries, drafts and pending requests, then refetch `/me`. Discard late responses if the identity revision/generation changed. Do not automatically replay a write under a new identity. Inactive-account responses clear auth state; ordinary unlinked accounts retain their existing administrative access. Link.View is not required for one's own minimal `/me` summary.

### Immediate effect and requests already in flight

Next-request means a request authorized after unlink/relink commits reads the new mapping regardless of JWT age. Read-only responses already authorized may finish with their captured identity; they cannot be recalled after delivery. An in-flight response must never mix old identity with newly resolved employee data. Frontend generation checks discard obsolete responses where observable.

Future self-service writes require an expected identity revision as a freshness precondition, not an employee selector. At the final write transaction, reread and lock the same subject User row and current link, check active account, revision and relevant employment eligibility, and retain the lock until commit. If the write locks first it commits under the old identity before unlink can commit. If unlink/relink commits first, the write fails without business changes. A check outside the write transaction is insufficient. No production self-service write endpoint is created merely to demonstrate this; use a test-only consumer in Phase 3B.

Link reads, history, candidates, mutations, `/me` and future link-sensitive endpoints check current actor activation on every request. Ordinary unrelated APIs retain their current behavior; broader account/session revocation is a separately scoped auth-hardening decision, not silently bundled into linking. Existing logout does not revoke access JWTs. The earlier refresh unresolved-host caveat remains a separately tracked auth-hardening concern; all new authenticated link routes require resolved host/tenant agreement and no identity is added to anonymous token responses.

## 7. Proposed linking and employment eligibility matrix

These choices are proposals, not implemented lifecycle policy. New linking and replacement share the same eligibility rules; unlink remains available for cleanup by another active authorized operator.

| Subject/employee state | New Link / replacement target | Retain existing link | Active-employment actions |
| --- | --- | --- | --- |
| Active account, coherent currently active employment | Allow after verified identity and occupancy checks | Yes | Still require action permission and module policy |
| Active account, future DateOfJoining, no contradictory separation/overlap evidence | Allow prejoining identity setup, even if initial employment has not yet been entered | Yes | Deny before joining; after joining require valid applicable history |
| Active account, joining reached but no applicable history | Reject pending correction | Yes | Deny; NoApplicableEmployment |
| Active account, currently non-active/separated employee | Reject new links and replacements in Phase 3B | Yes by default | Deny active-employment actions |
| Active account, overlapping history or contradictory lifecycle evidence | Reject pending reconciliation | Yes | Deny; RequiresReview |
| Disabled subject account, any employee | Reject new links/replacements | Yes, still reserves employee | Deny all link-sensitive actions by that account |
| Missing or other-tenant account/employee | Reject | Invalid state must fail closed | Deny |

Use effective history with inclusive EffectiveFrom/EffectiveTo and the Phase 2 server business date. A scheduled future separation must not block otherwise eligible current employment. Employee.Status alone is insufficient: denormalized status may lag scheduled changes. Missing initial history is allowed only for coherent future-joining setup; it does not confer eligibility on joining day. Review DateOfJoining, DateOfLeaving and effective history together; contradictory evidence needs reconciliation, never guessed access.

The source has Active/Resigned/Terminated employee statuses and a Retirement change-reason label, not a complete retirement policy. DateOfLeaving is described as last working day but the service can set it to non-active EffectiveFrom. Proposed conservative boundary: effective non-active row denies starting on EffectiveFrom; contradictory date semantics return RequiresReview. Tenant timezone and last-working-day semantics require business approval before production employment-dependent actions. Do not add a new Retired status or separation workflow here.

No administrator can perform employee actions merely because they can manage links. No impersonation path or early-action bypass is proposed. Retained links after separation may support separately approved future history access, but this phase grants none. Separation does not automatically disable login or release its reserved employee link.

Phase 2 manager resolution remains employee-to-employee, date-specific and unchanged. It is not a general employee-eligibility or approval-authorization service. Future manager actions require linked actor identity, employment eligibility, explicit action permission and the existing manager-scope resolution. Manager conflicts/cycles deny affected manager operations; absence of a manager must not by itself invalidate an otherwise legitimate employee identity. Do not grant roles on link or modify manager categories, employment rows, employee codes or supervisors.

Future business records must store immutable employee owner and acting account IDs at action time. Relinking must not rewrite earlier audit ownership or transfer requests. Actual Leave ownership, historical-read and approval policies remain outside Phase 3B.

## 8. Proposed migration, rollout and preflight

### Additive sequence and operational gates

1. Reconcile target branch with the pinned baseline and actual AGENTS.md. Inventory pending migration risks and actual physical tenant databases/shards under separately approved operational access; this phase performs no database inventory.
2. Approve schema, deletion policy, event immutability controls, permissions, role provisioning, contracts and eligibility decisions. Agree backup/restore and rollout window. A new migration does not make earlier unsafe migrations safe.
3. Generate the additive migration only under Phase 3B authorization. Add Users `(TenantId, Id)` alternate key; reuse Employees key. Create event table with principal FKs, event self-reference keys/FKs, checks and indexes; then current table with creation-event FK, principal FKs and both ordinary unique indexes. Do not add nullable employee columns to Users or backfill associations.
4. Inspect provider SQL, existing key names, table/column mappings and migration diff. Users alternate-key creation scans/builds an index and may lock/block writes despite Id already being unique. Check orphan tenant data, collation/type consistency, disk/log capacity and trusted constraints. Never promise zero downtime.
5. Test the actual SQL Server migration on a separately authorized fresh disposable database populated with synthetic baseline data. Validate rollback-on-failure and preservation of IDs, employment and existing grants. SQLite tests must enable foreign keys per connection and check mapped CHECK/RESTRICT/unique behavior; they cannot establish SQL Server locking or DDL safety. Ordinary non-null uniqueness replaces the previous filtered-index proposal.
6. Apply schema per physical tenant database only after separate authorization. Shared mode migrates once per shared database; each shard needs validation. Current link, events, Users and Employees stay co-located; no catalog links or cross-database FK. New code on old schema is unsupported when it accesses mapped link tables, even if UI is hidden.
7. Deploy consistent server enforcement before enabling linking/self-service; deploy frontend, provision reviewed special roles/assignments separately, then enable a small manually verified pilot. No automatic email/name/code matching, bulk linking, role expansion or legacy correction.

`SchemaPreparer` may delete/recreate stale SQLite schemas; do not start normal API against a valued database as an upgrade technique. Existing SQLite preservation requires its own plan. SQL Server initializer/startup migration and seeding must be accounted for before application startup. [S14]

| Compatibility state | Risk/required behavior |
| --- | --- |
| Old API + additive schema | Old code ignores links; restrictive FKs can now reject employee deletion. Validate and translate expected conflicts. |
| New API + old schema | Unsupported; fail closed, do not treat missing table as Unlinked. |
| Mixed API fleet | Keep feature inactive until every relevant node enforces immediate identity/activation checks. |
| Old frontend + new API | Existing /me fields preserved; added summary is optional to old clients. |
| New frontend + old API | Missing summary/capability means unavailable; no fallback edits. |

After any link event exists, recovery is roll-forward: disable affected features, preserve both tables, investigate and apply a reviewed fix. Do not drop events/current links or revert to an API that bypasses required checks while self-service remains enabled. Dropping the schema destroys attribution and cannot undo already observed identity changes. Any pre-data downgrade also requires explicit authorization and proof that both tables are empty. No rollback, correction or migration execution is authorized by this report.

### Read-only legacy preflight examples — NOT EXECUTED

SQL below is report text only. A deployment operator needs separate authorization for one identified application database, never system databases. Do not print credentials or employee personal fields. Correction authority is separate from read-only preflight authority. Counts/IDs can still be sensitive operational data and should remain in the authorized review.

Before migration:

```sql
SELECT u.Id, u.TenantId
FROM dbo.Users AS u LEFT JOIN dbo.Tenants AS t ON t.Id = u.TenantId
WHERE t.Id IS NULL;
SELECT e.Id, e.TenantId
FROM dbo.Employees AS e LEFT JOIN dbo.Tenants AS t ON t.Id = e.TenantId
WHERE t.Id IS NULL;
SELECT TenantId, Id, COUNT_BIG(*) AS DuplicateCount
FROM dbo.Users GROUP BY TenantId, Id HAVING COUNT_BIG(*) > 1;
SELECT TenantId, Id, COUNT_BIG(*) AS DuplicateCount
FROM dbo.Employees GROUP BY TenantId, Id HAVING COUNT_BIG(*) > 1;
SELECT ur.UserId, ur.RoleId, ur.TenantId
FROM dbo.UserRoles AS ur
LEFT JOIN dbo.Users AS u ON u.Id = ur.UserId
LEFT JOIN dbo.Roles AS r ON r.Id = ur.RoleId
WHERE u.Id IS NULL OR r.Id IS NULL OR ur.TenantId <> u.TenantId;
SELECT h.Id, h.TenantId, h.EmployeeId
FROM dbo.EmployeeEmploymentHistory AS h
LEFT JOIN dbo.Employees AS e ON e.TenantId = h.TenantId AND e.Id = h.EmployeeId
WHERE e.Id IS NULL;
SELECT a.EmployeeId, a.Id AS FirstHistoryId, b.Id AS SecondHistoryId
FROM dbo.EmployeeEmploymentHistory AS a
JOIN dbo.EmployeeEmploymentHistory AS b
 ON a.TenantId = b.TenantId AND a.EmployeeId = b.EmployeeId AND a.Id < b.Id
 AND a.EffectiveFrom <= COALESCE(b.EffectiveTo, CONVERT(date, '99991231', 112))
 AND b.EffectiveFrom <= COALESCE(a.EffectiveTo, CONVERT(date, '99991231', 112));
```

After approved schema creation only; these tables do not exist at baseline:

```sql
SELECT TenantId, UserId, COUNT_BIG(*) AS DuplicateCount
FROM dbo.AccountEmployeeCurrentLinks
GROUP BY TenantId, UserId HAVING COUNT_BIG(*) > 1;
SELECT TenantId, EmployeeId, COUNT_BIG(*) AS DuplicateCount
FROM dbo.AccountEmployeeCurrentLinks
GROUP BY TenantId, EmployeeId HAVING COUNT_BIG(*) > 1;
SELECT c.TenantId, c.LinkId
FROM dbo.AccountEmployeeCurrentLinks AS c
LEFT JOIN dbo.Users AS u ON u.TenantId = c.TenantId AND u.Id = c.UserId
LEFT JOIN dbo.Employees AS e ON e.TenantId = c.TenantId AND e.Id = c.EmployeeId
LEFT JOIN dbo.AccountEmployeeLinkEvents AS ev
 ON ev.TenantId = c.TenantId AND ev.SubjectUserId = c.UserId AND ev.Id = c.LinkId
WHERE u.Id IS NULL OR e.Id IS NULL OR ev.Id IS NULL
 OR ev.AfterEmployeeId IS NULL OR ev.AfterEmployeeId <> c.EmployeeId
 OR ev.Operation NOT IN ('Link', 'Replace');
WITH Latest AS (
 SELECT *, ROW_NUMBER() OVER (
  PARTITION BY TenantId, SubjectUserId ORDER BY Sequence DESC) AS rn
 FROM dbo.AccountEmployeeLinkEvents
)
SELECT ev.TenantId, ev.SubjectUserId, ev.Id
FROM Latest AS ev
LEFT JOIN dbo.AccountEmployeeCurrentLinks AS c
 ON c.TenantId = ev.TenantId AND c.UserId = ev.SubjectUserId
WHERE ev.rn = 1 AND (
 (ev.Operation = 'Unlink' AND c.LinkId IS NOT NULL) OR
 (ev.Operation IN ('Link', 'Replace') AND
  (c.LinkId IS NULL OR c.LinkId <> ev.NewLinkId OR c.EmployeeId <> ev.AfterEmployeeId)));
SELECT name, is_disabled, is_not_trusted
FROM sys.foreign_keys
WHERE parent_object_id IN (
 OBJECT_ID(N'dbo.AccountEmployeeCurrentLinks'), OBJECT_ID(N'dbo.AccountEmployeeLinkEvents'));
SELECT name, is_unique, is_disabled, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.AccountEmployeeCurrentLinks');
```

Before activation both new tables must be empty; later rows must reconcile with approved immutable events and current state. Compare actor/subject/employee tenant FKs, sequence continuity, predecessor semantics, all checks and alternate keys during the approved validation run; these examples are not an exhaustive migration verifier. Legacy discrepancies block affected objects pending separately authorized correction. No findings about live data are asserted here.

## 9. Acceptance plan — proposed, not executed

Use synthetic users/employees with explicit tenant contexts. Test outcomes below are required evidence, not results obtained in this review.

| Area | Observable acceptance criteria |
| --- | --- |
| Model/provider | Both ordinary unique indexes reject duplicates; multiple accounts with no current row are valid; cross-tenant current/event/actor FKs fail; checks reject invalid event shape and empty reason; foreign keys enabled in SQLite. |
| Atomic operations | Link/Unlink/Replace produce exactly one event each, ordered revisions, consistent projection; audit insert failure or current-row failure rolls back everything. Failed Replace retains original association. |
| Stale state | Old preconditions fail after A→unlinked→A and unlinked→linked→unlinked; repeated POST cannot append duplicate committed events; same-employee replacement rejected. |
| SQL Server concurrency | Independent connections race two users→one employee, one user→two employees, competing replacements, unlink versus replace/link, and target occupied by disabled account. Exactly permitted winners persist with matching event count; no lost old links on failure. |
| State-change races | Link/Replace race subject/actor deactivation, grant revocation, employee deletion and employment changes; transaction order determines outcome; no validation outside transaction is accepted as proof. |
| In-flight self writes | Test-only consumer holds the shared identity lock; write-before-unlink commits original owner; unlink-before-write rejects stale context. No production Leave endpoint added for testing. |
| Tenant and privacy | Wrong/unknown host, missing uid/tid, cross-tenant IDs and tampered tenant input fail closed. Unauthorized users receive no object/candidate/history disclosure. No secrets/full profiles in responses or events. |
| Permissions | Existing User.Edit/Employee.Edit/manager role cannot link; View cannot search candidates/mutate/read history; Manage alone cannot mutate; ViewHistory can read history but not mutate. Revoked live grants deny with old token. |
| Grants/startup | New permissions do not enter broad admin grants; reviewed role assignment tenant-scoped; existing grants unchanged; startup cannot restore removed special grants/assignments. |
| Sessions | Old JWT observes unlink/relink immediately on next link-sensitive request. Disabled actor denied by link APIs and /me; retained relationship cannot authorize it. Existing logout limitations documented, not falsely claimed fixed. |
| Eligibility | Future joining may link but cannot act; joining day without history denied; active history still needs permission; separated targets cannot newly link; existing separated links retained; gaps/overlaps/conflicting dates fail closed. |
| Phase 2 | Effective-date boundaries and manager resolution unchanged; no role/manager/code/employment changes; manager identity does not imply approval rights. |
| Immutability/deletion | Event updates/deletes rejected through application; production principal restrictions separately validated where approved. Hard deletes of historical actor/subject/employee rejected even after unlink. Original attribution survives name/code/relink changes. |
| Frontend/contracts | Existing /me fields unchanged; post-login/refresh fetch, Unknown/Unavailable/Unlinked distinction; separate history guard; mandatory confirmation/reason; stale conflict refresh; discard late identity responses/drafts; no automatic write replay. |
| Migration/rollout | Synthetic populated baseline upgrade preserves data and grants; Users alternate key created once; both new tables empty; inspect SQLite and SQL Server mapping; shared/sharded scope; mixed-version activation blocked. |
| Recovery | Feature disable retains events/current links; no destructive downgrade after data; all correction actions separately authorized. |

Run SQLite backend and focused frontend tests during approved Phase 3B, followed by repository-required regression gates. SQL Server concurrency and migration tests require separately authorized disposable database creation/access with allowlisted fresh database identity, independent connections and deterministic barriers. No fallback to HRMS, catalog, master, or an existing database is acceptable. Do not print test connection strings. Application-level and SQLite evidence cannot substitute for this gate.

Browser acceptance, SQL Server concurrency testing, and legacy reconciliation remain outstanding. No builds, application tests, migrations or SQL were executed for this document revision.

## 10. Approval checklist — all items unresolved until explicit approval

- [ ] Approve two-table schema, event-ID link identity, per-account revision chain, ordinary unique indexes and Users alternate key.
- [ ] Approve restrictive historical FKs blocking hard deletion after unlink, and event retention/database-principal protection strategy; nominate retention decision owner.
- [ ] Approve three permissions, special-role matrix, named operators and separate provisioning authority; no default broad-role grants.
- [ ] Approve standalone minimal administration page and self-link prohibition; no user/role CRUD or impersonation.
- [ ] Approve immediate next-request enforcement and defined in-flight transaction ordering, with broader unrelated auth hardening separately scoped.
- [ ] Approve additive /me contract, frontend identity refresh and freshness preconditions.
- [ ] Approve future-joining setup, rejection of new separated/inactive/disabled targets, retained historical links and conservative inconsistent-data denial.
- [ ] Resolve tenant business timezone, separation/last-working-day semantics, retirement interpretation and any future post-employment access before employment-dependent production actions. Proposed temporary date basis is Phase 2 UTC.
- [ ] Approve explicit atomic replacement for verified correction, mandatory reasons, no occupied-target displacement and fresh confirmation on conflicts.
- [ ] Approve Phase 3B migration generation/inspection scope; migration application, deployment, production grants, existing-data preflight and any correction remain separate authorities.
- [ ] Separately authorize isolated disposable SQL Server testing and define its safe database selection; until then report it not run and do not assert concurrency safety.

No checkbox is checked by the instruction to revise this report. The design is ready for decision review; production readiness remains unproven. Missing business/operational approvals must be recorded, not silently inferred by an implementer.

Approval status: the repository owner approved the bounded Phase 3B development choices on 2026-09-03. Operational rollout, existing-data access, migration application, production grants, browser acceptance, SQL Server concurrency execution, and legacy reconciliation remain separately authorized.

## 11. Bounded Phase 3B prompt

**DO NOT EXECUTE — PENDING APPROVAL.**

```text
Implement only the explicitly approved account-to-employee linking foundation.
Read AGENTS.md and the revised docs/phase-3-account-employee-linking-design.md.
Verify the target branch contains Phase 2 checkpoint 2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e.
Reconcile intervening source/report changes before coding.

Require an explicit decision record for section 10. Do not treat recommendations,
this handoff, or the Phase 3A revision request as implementation approval.
If a required design choice is missing, identify that exact decision before its
implementation. Do not invent role grants, eligibility policies or deletion rules.

Implement the approved current-link/event tables, tenant composite FKs, Users
alternate key, ordinary uniqueness, immutable event checks and transaction protocol.
Generate and inspect an additive migration only if expressly authorized; do not apply it.
No automatic matching or backfill and no nullable User.EmployeeId relationship.

Add the three approved permissions, live actor/grant checks and protection against
blanket startup grants. Implement the approved minimal administration page and thin
controllers/application services for current state, candidates, history, link,
unlink and explicit atomic replacement. Do not grant roles to existing accounts.

Implement server-side current identity checks and additive /me handling, without
employee token claims or client employee IDs selecting self. Keep account identity,
employment eligibility and business permissions separate. Preserve all Phase 2
employment/manager rules, supervisors, employee codes and historical ownership.

Implement only the approved eligibility matrix; do not build Leave, attendance,
balances, separation, approval workflows, impersonation or general user/role CRUD.
Use a test-only self-service consumer for transaction/freshness verification.

Add focused SQLite/backend/frontend tests and required regression validation.
Prepare SQL Server migration/concurrency coverage, but execute it only with separate
explicit authorization for a fresh disposable test database. Never connect to an
existing application/catalog/system database or print credentials.
Do not start the normal API, execute legacy preflight, correct existing data, apply
migrations, deploy, commit or push. Do not claim unrun acceptance checks passed.

Finish with changes, actual test results, inspected migration risks, remaining
approval decisions and separate rollout/grant steps. Keep browser acceptance,
SQL Server concurrency and legacy reconciliation outstanding unless subsequently
executed under explicit authorization and supported by evidence.
```

## 12. Pinned source index

The following source index was established in the original checkpoint review; the revision rechecks are listed in section 2. Paths are repository-relative; links identify the exact revision rather than a moving branch.

- **S1 — Identity entities:** [User.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Domain/Entities/User.cs), [Employee.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Domain/Entities/Employee.cs), [BaseEntity.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Domain/Common/BaseEntity.cs).
- **S2 — Identity mappings:** [UserConfiguration.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Configurations/UserConfiguration.cs), [EmployeeConfiguration.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Configurations/EmployeeConfiguration.cs), [HrmsDbContextModelSnapshot.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Migrations/HrmsDbContextModelSnapshot.cs).
- **S3 — Tenant filtering and stamps:** [HrmsDbContext.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/HrmsDbContext.cs), especially OnModelCreating and ApplyAuditAndTenantStamps.
- **S4 — Host and database selection:** [Program.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Program.cs), [TenantShardResolutionMiddleware.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Middleware/TenantShardResolutionMiddleware.cs), [TenantShardResolver.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Sharding/TenantShardResolver.cs), [ShardConnectionStringFactory.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Sharding/ShardConnectionStringFactory.cs), [DependencyInjection.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/DependencyInjection.cs).
- **S5 — Authentication operations:** [AuthController.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Controllers/AuthController.cs), [AuthService.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Services/AuthService.cs), especially LoginAsync, RefreshAsync, LogoutAsync, GetCurrentUserAsync, IssueTokensAsync and LoadAuthorizationAsync.
- **S6 — Token and claims:** [JwtTokenService.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Security/JwtTokenService.cs), [HrmsClaimTypes.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Security/HrmsClaimTypes.cs).
- **S7 — Request authorization:** [AuthenticationServiceCollectionExtensions.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Security/AuthenticationServiceCollectionExtensions.cs), [TenantMatchesShardRequirement.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Security/TenantMatchesShardRequirement.cs), [HttpTenantContext.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Security/HttpTenantContext.cs).
- **S8 — Permission and role grants:** [Permissions.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Domain/Authorization/Permissions.cs), [SeedData.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Seed/SeedData.cs) (PermissionIds and RolePermissionMap), [DatabaseSeeder.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Seed/DatabaseSeeder.cs), [UserRoleConfiguration.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Configurations/UserRoleConfiguration.cs).
- **S9 — Employee administration:** [EmployeesController.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Controllers/EmployeesController.cs), [EmployeeService.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Services/EmployeeService.cs), [EmployeeSubResourcesController.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.API/Controllers/EmployeeSubResourcesController.cs).
- **S10 — Employment lifecycle values:** [EmployeeStatus.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Domain/Enums/EmployeeStatus.cs).
- **S11 — Existing audit:** [EmployeeAuditLog.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Domain/Entities/EmployeeAuditLog.cs), [EmployeeAuditLogConfiguration.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/Configurations/EmployeeAuditLogConfiguration.cs), [EmployeeAuditService.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Services/EmployeeAuditService.cs).
- **S12 — Frontend identity/navigation:** [AuthProvider.tsx](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/auth/AuthProvider.tsx), [permissions.ts](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/auth/permissions.ts), [auth.ts](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/api/auth.ts), [session.ts](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/api/session.ts), [App.tsx](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/App.tsx), [navigation.ts](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/layout/navigation.ts), [Sidebar.tsx](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Frontend/HRMS.Web/src/layout/Sidebar.tsx).
- **S13 — Phase 2 effective employment/manager resolution:** [EmployeeManagerResolver.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Services/EmployeeManagerResolver.cs), [EmployeeSupervisorService.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Services/EmployeeSupervisorService.cs), [EmployeeEmploymentService.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Application/Services/EmployeeEmploymentService.cs).
- **S14 — Startup migration risk:** [DatabaseInitializer.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/DatabaseInitializer.cs), [SchemaPreparer.cs](https://github.com/amit1410/HRMS/blob/2d7bf26dcb9540a8f02c1c32c6889bf1a5da101e/Backend/HRMS.Infrastructure/Persistence/SchemaPreparer.cs).

Phase 3A revision ends with this report. All proposed decisions, implementation and operational execution await explicit approval.

## 13. Phase 3B approval record

Approved by the repository owner on 2026-09-03 for bounded development and local isolated SQLite/backend/frontend tests. Approved choices are the two-table current-link/event model; restrictive historical foreign keys with no event deletion, purge, or automatic expiry in this phase (permanent retention and erasure policy deferred); the three AccountEmployeeLink permissions with AccountLinkAdministrator and AccountLinkAuditor definitions and no automatic broad-role grants or operational provisioning; the standalone administration page and self-link prohibition; live actor/grant checks, server-derived identity, revision freshness and atomic replacement; additive `/me` identity with unchanged login/refresh contracts; the approved future-joining and conservative eligibility matrix using provisional Phase 2 UTC dates; and backend `TimeProvider` UTC timestamps without database defaults.

Unused stable permission and role IDs must be selected and recorded during implementation without renumbering existing IDs. Migration generation and inspection, but not application, are authorized. Existing-database access, legacy reconciliation, operational grants, deployment, browser acceptance, SQL Server concurrency execution, and production readiness remain outside this approval.
