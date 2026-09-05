# Phase 4A Leave Management design and implementation-readiness review

Status: DESIGN ONLY — no Leave implementation, migration, database access, or operational permission change is authorized by this review.

## A. Existing repository findings

The repository is a .NET 10 API/application/infrastructure solution with a React 19 frontend. Tenant-owned shard entities implement `ITenantEntity`; `HrmsDbContext` applies tenant query filters and stamps/guards `TenantId` during `SaveChanges`. Composite tenant-aware foreign keys are used where a child must not reference another tenant's row. The existing audit and account-link designs favor append-only history, restrictive deletes, correlation identifiers, and optimistic/concurrency checks.

Relevant inspected files include:

- `Backend/HRMS.Domain/Entities/Employee.cs`
- `Backend/HRMS.Domain/Entities/EmployeeEmployment.cs`
- `Backend/HRMS.Domain/Entities/EmployeeEmploymentHistory.cs`
- `Backend/HRMS.Domain/Entities/EmployeeSupervisor.cs`
- `Backend/HRMS.Domain/Entities/AccountEmployeeCurrentLink.cs`
- `Backend/HRMS.Domain/Entities/AccountEmployeeLinkEvent.cs`
- `Backend/HRMS.Application/Services/AuthService.cs`
- `Backend/HRMS.Application/Services/EmployeeEmploymentService.cs`
- `Backend/HRMS.Application/Services/EmployeeManagerResolver.cs`
- `Backend/HRMS.Application/Services/AccountEmployeeLinkService.cs`
- `Backend/HRMS.Infrastructure/Persistence/HrmsDbContext.cs`
- `Backend/HRMS.Infrastructure/Persistence/Configurations/AccountEmployeeLinkConfiguration.cs`
- `Backend/HRMS.Infrastructure/Persistence/Seed/SeedData.cs`
- `Backend/HRMS.API/Security/HasPermissionAttribute.cs`
- `Backend/HRMS.API/Controllers/AuthController.cs`
- `Backend/HRMS.API/Controllers/AccountEmployeeLinksController.cs`
- `Frontend/HRMS.Web/src/auth/AuthProvider.tsx`
- `Frontend/HRMS.Web/src/auth/permissions.ts`
- `Frontend/HRMS.Web/src/pages/administration/AccountEmployeeLinksPage.tsx`

Existing migrations include employee/employment and account-employee-linking migrations, but no Leave tables or Leave migration. Existing tests cover tenant isolation, employment history, manager resolution, permissions, immutable account-link events, and SQLite-isolated application behavior. SQL Server concurrency acceptance remains a separate outstanding concern from Phase 3B.

## B. Existing Leave functionality

No `Leave`, `LeaveType`, `LeavePolicy`, `LeaveBalance`, `LeaveRequest`, `LeaveApplication`, `Holiday`, `WorkingDay`, `WeekOff`, `Attendance`, approval-workflow, or leave frontend route/component was found under `Backend`, `Frontend`, `Database`, or the existing documentation. There are no Leave entities, DbSets, endpoints, services, permissions, seed rows, or migrations. `EmployeeSupervisor` and `EmployeeManagerResolver` are manager infrastructure, not Leave approval functionality.

This is a greenfield Leave foundation. Phase 4B should begin with reviewed domain contracts and migrations only after this design is approved.

## C. Dependencies on Employee and Employment

`Employee` is tenant-owned and has a stable GUID, employee code, joining/leaving dates, status, personal data, and legacy organization/manager fields. `EmployeeEmployment` is a one-to-one employment file containing hire, confirmation, probation, notice, and job-status data. `EmployeeEmploymentHistory` is the authoritative effective-dated position record and includes Holding Company, LOB, Organisation, Department, Sub Department, Section, Sub Section, Function, Sub Function, Grade, Designation, Employee Type, Country location, Work Location, Cost Center, ManagerId, and employment status.

Leave must reference `EmployeeId` and resolve employment dimensions as of the relevant business date through `EmployeeEmploymentHistory`. It must not duplicate current organization dimensions into policy assignments or requests merely to make filtering convenient. A request may snapshot the resolved policy/rule and calculation inputs for historical explanation, but those snapshots are not replacements for the employee/employment source of truth.

Employment status, DateOfJoining, DateOfLeaving, confirmation/probation and notice-period fields are inputs to eligibility. A date-sensitive service must use the tenant business date/time-zone policy rather than process-local time.

## D. Account-employee identity integration

Login authenticates a tenant-hosted `User`; the access token carries user/tenant and permission claims. `GET api/auth/me` reloads the user through the tenant-scoped context, rejects a missing/deactivated account, reloads current roles and permissions, and resolves `EmployeeIdentity` from the current account-employee link. The link is tenant-scoped and has unique `(TenantId, UserId)` and `(TenantId, EmployeeId)` constraints. Relinking appends immutable link events and does not rewrite earlier ownership.

Leave self-service endpoints must resolve the employee from the authenticated tenant context and current link at request time. A normal employee must not be allowed to choose an arbitrary `EmployeeId` in a self-service body. No current link is an explicit, safe `NotLinked` outcome; an invalid link or absent/overlapping/ineligible employment is not silently treated as active employment.

Use cases are separate:

- Self-service: current authenticated `UserId` → current tenant link → `EmployeeId`; own requests and balances only.
- Manager: authenticated linked employee plus explicit Leave manager permission and a date-aware subordinate/scope resolver.
- HR/admin: tenant-scoped employee target with explicit broad permission; never cross-tenant.
- System/background: a non-interactive service identity and explicit tenant/job scope, with actor and reason recorded. It must not impersonate an employee login.

Account deactivation must deny new privileged Leave actions and normally deny new self-service submissions. Existing approved records remain historical. Unlink/replacement must not change the `EmployeeId` stored on existing requests or alter their historical actor data; policy for pending requests is defined in section R.

## E. Manager-resolution integration

`EmployeeManagerResolver.ResolveAsync(employeeId, asOfDate)` reads the effective employment history and validates tenant scope, overlap, manager existence, manager eligibility, and reporting cycles. It returns explicit statuses including `Resolved`, `NoApplicableEmployment`, `NoAssignedManager`, `ManagerNotEligible`, `InvalidManagerReference`, `OverlappingEmployment`, `ReportingCycle`, and `LegacyConflict`. It does not silently prefer legacy columns when current effective history disagrees. `EmployeeSupervisor` also stores L1-L5, Time Manager, ERO, and CHRO references, but it is legacy/specialized supervisor data rather than a configurable approval workflow.

For Leave, use an adapter around the resolver, not direct reads of legacy manager columns. Approval routing must snapshot the resolved approver employee/account identity and resolution date when a request is submitted. A missing manager, inactive/ineligible manager, no linked login, cycle, overlap, or legacy conflict should produce a visible `RequiresReview`/routing failure and no silently guessed approver.

The current source does not provide a complete L1/L2/L3/HR/time approval policy. A future Leave router can map configured stages to the effective employment manager chain and specialized manager references, but it must validate every stage independently. Manager changes while pending must not rewrite history. The recommended default is to keep the originally assigned stage assignment and require explicit reassignment/re-routing with an immutable event; automatic movement to a new manager is unsafe because it changes who was accountable without an explainable decision.

## F. Proposed Leave domain model

All proposed Leave records are tenant-scoped, use GUID primary keys, server-generated audit timestamps, and restrictive deletes for records referenced by history or transactions. Every write validates the tenant context and referenced tenant IDs. Configuration is effective-dated and versioned; transactional records retain the selected version.

Core entities:

| Entity | Purpose | Key relationships and safeguards |
|---|---|---|
| `LeaveType` | Tenant-owned semantic leave category and request constraints | Unique `(TenantId, Code)`; inactive is a soft state; referenced historically, so restrict delete |
| `LeavePolicy` | Stable policy identity/assignment container | Tenant-scoped; owns versions; no destructive overwrite |
| `LeavePolicyVersion` | Effective-dated immutable configuration version | Unique `(TenantId, LeavePolicyId, VersionNumber)`; non-overlapping effective range |
| `LeavePolicyRule` | Entitlement/rule for a LeaveType in a policy version | Unique `(TenantId, PolicyVersionId, LeaveTypeId)`; immutable once used |
| `LeavePolicyApplicability` | Dimension predicates for policy selection | Tenant-scoped, normalized predicates; deterministic priority; restrictive references |
| `LeavePeriod` | Tenant leave-year instance and calendar boundaries | Unique `(TenantId, PeriodCode)` and non-overlapping active year intervals |
| `EmployeeLeaveBalance` | Query projection/current summary for a period/type/employee | Unique `(TenantId, EmployeeId, LeavePeriodId, LeaveTypeId)`; concurrency token; rebuildable from ledger |
| `LeaveBalanceTransaction` | Append-oriented balance ledger | Tenant, employee, period, type, immutable amount/type/reason/source; idempotency key unique |
| `LeaveRequest` | Aggregate root for a request and lifecycle | Tenant, employee, type, selected versions, dates, quantity, state, revision; restrictive history links |
| `LeaveRequestDay` | Optional per-day calculation audit | Introduce only if daily partial/holiday/sandwich explanations require it; unique request/date |
| `LeaveApprovalStage` | Configured stage instance for one request | Immutable stage order/type plus assigned employee/account snapshot; revision and decision state |
| `LeaveRequestEvent` | Immutable request lifecycle/audit history | Tenant/request, event type, actor account/employee snapshot, correlation, prior/new state |
| `LeaveCalendar` | Tenant/location calendar definition | Versioned/effective-dated, not copied into every request |
| `LeaveCalendarDay` | Holiday/week-off/half-day exceptions | Unique calendar/date; restrictive historical references |

`LeaveRequestDay` is justified only when the product needs to explain a multi-day quantity at day granularity, preserve per-day partial units, or support later Attendance integration. The first foundation can calculate and snapshot a normalized day breakdown in a value object/JSON audit payload only if the selected storage and query requirements are approved; a relational day table is safer for reporting and reconciliation.

Important indexes include tenant plus employee/date, tenant plus status/approver, tenant plus period/type, effective-date lookup indexes on policy/calendar tables, and unique idempotency/source keys. Do not cascade-delete requests, approvals, events, ledger rows, policy versions, or referenced employees. Soft-deactivate configuration and retain history.

## G. Leave Type design

`LeaveType` should include:

- stable `Code`, display `Name`, description, active state, display ordering;
- unit enum initially `FullDay` and `HalfDay`; `Hours` should be a later capability behind a schedule/attendance abstraction;
- paid/unpaid classification as business metadata, with payroll integration treated as a consumer rather than an implicit payroll calculation;
- reason required, attachment required, attachment threshold, and safe attachment metadata policy;
- applicability constraints where legally required, such as gender, probation, confirmation, employee type, or country, expressed as explicit rules rather than ad hoc UI filtering;
- minimum/maximum request quantity, minimum advance notice, maximum backdated days, future-request horizon;
- negative balance permission, sandwich/holiday/week-off handling, cancellation allowed and cancellation cutoff;
- stable display color only as presentation metadata.

LeaveType defines meaning and request constraints. It must not contain employee-specific entitlement balances or policy assignment. Changes to a used type should create a version or be restricted to non-semantic presentation fields; historical requests retain the type identity and the resolved rule snapshot.

## H. Policy and versioning design

`LeavePolicy` is the stable business policy; `LeavePolicyVersion` is an immutable effective-dated revision; `LeavePolicyRule` holds entitlement and behavior for each LeaveType; applicability rows determine which employee dimensions select the policy. A version has `EffectiveFrom`, optional `EffectiveTo`, status, version number, author/reason, and a concurrency token. Published/used versions cannot be edited in place; supersede them.

Assignment should be by applicability dimensions, not one row per employee. Support predicates for Holding Company, LOB, Organisation, Department, Sub Department, Grade, Designation, Employee Type, Country, Location/Work Location, and optionally Cost Center. A direct employee exception can be a separately named, audited exception assignment later, not the default model.

Resolution algorithm:

1. Resolve tenant and business date.
2. Load exactly one applicable employment record for the date; reject overlap or unresolved employment.
3. Select active policy versions whose effective range contains the date.
4. Evaluate all applicability predicates against tenant-scoped dimension IDs.
5. Rank by explicit priority, then specificity (number of constrained dimensions), then stable policy/version key.
6. Reject ties at the same deterministic rank; do not choose by insertion order.
7. Use an approved fallback policy only when one is explicitly configured; otherwise return `NoPolicy`.
8. Persist policy ID, version ID, rule ID, resolution date, and calculation inputs on request/ledger operations.

Changing a policy never rewrites historical balances or approved requests. Future accrual jobs use the effective version. Pending requests either retain their selected version or require an explicit revalidation event under the tenant policy; the safer default is retain the version selected at submission and block only if a later legal rule requires revalidation.

## I. Applicability/resolution algorithm

Dimension values are read dynamically from effective employment for the operation's relevant date. A request spanning a transfer boundary must use an approved policy: the recommended foundation rejects spans across incompatible effective employment/policy boundaries and asks the user to split the request, avoiding an arbitrary mixed entitlement. Later support can calculate each segment separately.

No policy resolution may cross a tenant filter. All dimension IDs are validated as tenant-owned and active/effective. Employee Code is display/reporting data only and never an identity key.

## J. Leave-year model

`LeavePeriod` represents a tenant-defined leave year with a stable code, `StartDate`, `EndDate`, calendar basis (`Calendar`, `Financial`, or `Custom`), timezone/business-date policy, status, and optional predecessor/successor. The invariant is one unambiguous period for any eligible date; overlapping periods for the same tenant are rejected. Future changes create new period definitions/instances; they do not reinterpret old transactions.

Jan–Dec, Apr–Mar, and custom cycles are configuration, not constants. Carry-forward and expiry are ledger operations at the boundary. The first implementation should materialize periods explicitly rather than derive them from the machine's calendar year.

## K. Balance ledger model

Balance is derived from an append-oriented `LeaveBalanceTransaction` ledger, with a projection for fast reads. Supported transaction types are `Opening`, `Accrual`, `CarryForward`, `Adjustment`, `RequestReservation`, `ApprovalConsumption`, `RejectionRelease`, `CancellationCredit`, `Encashment`, and `Expiry`.

For an employee/type/period, expose:

`Opening + Accrued + CarriedForward + Adjusted - Consumed - Reserved - Expired - Encashment = Available`.

The exact sign convention must be centralized in the domain service. A pending request reserves quantity; approval converts reservation to consumption in one transaction; rejection releases it; cancellation credits only according to the lifecycle rule. Every transaction records source request/event, actor, reason, correlation/idempotency key, and created time. Corrections append compensating rows rather than editing a prior row.

`EmployeeLeaveBalance` is a projection/cache with a revision and last-ledger sequence. Reconciliation recomputes it from the ledger and flags divergence. It is not the authoritative history. A write locks or serializes the employee/type/period ledger scope, checks the latest revision, appends the required rows, and updates the projection atomically.

## L. Entitlement and accrual rules

Rules should support annual or monthly entitlement, accrual frequency and timing, joiner/leaver proration, probation/confirmation conditions, carry-forward cap/expiry, encashment eligibility, maximum accumulation, lapse, negative balance, and leave-year basis. Each rule records its unit/precision and rounding mode; quantity calculations must not use floating-point ambiguity.

Foundation scope: annual entitlement, explicit opening/accrual, configurable joiner proration, carry-forward cap, reservation/consumption/release, and period boundary expiry. Later enhancements: hourly entitlement, complex probation transitions, encashment/payroll settlement, shift-aware accrual, retroactive policy recalculation, and statutory country packs.

Accrual is a scheduled/application job, not application startup work. It is idempotent by tenant/period/rule/employee/accrual occurrence and records the policy version used. Employee transfers or status changes trigger a reviewed reevaluation for future accruals; they do not mutate past ledger rows.

## M. Leave request lifecycle

Recommended states are `Draft` (optional), `Submitted`, `PendingApproval`, `Approved`, `Rejected`, `Withdrawn`, `Cancelled`, and `Expired` where applicable. Transitions are explicit commands with allowed predecessor state, expected request revision, actor permission, and immutable event.

On submit, the backend resolves identity, employment, type/rule/policy/period, dates, day quantity, eligibility, and available/reserved balance. It ignores client quantity and rejects invalid effective employment, unavailable policy, insufficient balance, duplicate idempotency key, or a conflicting current revision. Submit creates the reservation and approval instances atomically.

Store employee ID, leave type, selected policy/version/rule, period, dates, partial-day data, server-calculated quantity, reason, attachment metadata (not arbitrary secret-bearing content), status, current approval revision, created/submitted/cancelled timestamps, and immutable actor/audit fields. A request retains enough snapshot data to explain the decision even after current employment or policy changes.

## N. Day and quantity calculation

The Leave engine owns date-range validation, tenant calendar lookup, holiday/week-off inclusion rules, half-day positions, sandwich behavior, minimum/maximum durations, and server-side quantity calculation. It uses the employee's effective work location/calendar for each relevant date. It must return a day-level explanation or deterministic calculation record.

Attendance/shift systems do not yet exist in this repository. Define `ILeaveWorkCalendarProvider`/equivalent abstraction so the first provider uses Leave-managed tenant/location calendar data, while a future Attendance provider can supply shifts and scheduled workdays. Do not create a hard dependency on Attendance now.

A request crossing an employment or work-location boundary should be rejected or split according to an explicitly approved rule; default to reject-and-split for auditability. Time-zone and date-only semantics must be tenant-defined. The backend, not the browser, is authoritative.

## O. Approval workflow

Use a Leave-specific workflow abstraction backed by generic stage records, rather than prematurely building a fully configurable workflow platform. The abstraction should support ordered stages such as L1, L2, L3, HR, and Time Manager, configurable stage count/order, explicit approver source, optional auto-approval with policy reason, rejection, withdrawal, cancellation approval, reassignment, and escalation.

Each request gets immutable stage instances containing stage type/order, assigned approver employee and account IDs, display snapshots, assignment/resolution timestamp, decision, decision timestamp, reason, and revision. Decisions append `LeaveRequestEvent` records and update the aggregate in one transaction. History must show who was assigned and who acted, including reassignment.

Default routing snapshots the manager at submission. If the manager changes while pending, preserve the original assignment and require an explicit authorized reassignment/re-route event. A tenant may later choose automatic rerouting, but it must retain the original assignment, reason, old/new identities, and policy version.

Approver absence, inactive account, missing linked login, manager cycle, cross-tenant reference, or unresolved legacy conflict blocks the affected stage with an actionable review state. It must not silently grant approval to HR or the next manager.

## P. Authorization model

Use existing `Resource.Action` permission conventions and `HasPermissionAttribute`/tenant-scoped policies. Proposed names are:

- `Leave.ViewOwn`, `Leave.Request`, `Leave.CancelOwn`
- `Leave.ViewTeam`, `Leave.Approve`
- `Leave.ViewAll`, `Leave.Manage`
- `Leave.BalanceAdjust`, `Leave.PolicyManage`, `Leave.TypeManage`, `Leave.CalendarManage`

These are proposals only; no grants are added in Phase 4A. `ViewHistory`/audit visibility should be explicit if the product separates it from request view.

Own endpoints require the authenticated linked employee and must not accept an employee override. Team endpoints require manager scope plus `ViewTeam`; approval requires `Approve` and an assigned stage. HR/admin endpoints require explicit broad permission and tenant scope. Configuration writes require the matching management permission. Balance adjustments require `BalanceAdjust`, a reason, expected revision, and immutable audit.

The existing seeded `AccountLinkAdministrator` and `AccountLinkAuditor` permissions do not imply Leave permissions. Existing broad administrator role mappings must not be expanded accidentally by adding Leave permissions to a catch-all grant list; provisioning and least-privilege review are required.

## Q. Tenant isolation

Every Leave table includes `TenantId`; every tenant-owned FK either uses a composite `(TenantId, Id)` principal key or is validated in the service and database constraint. Global query filters remain defense in depth, not the sole protection. Tenant context comes from authenticated host/token agreement, never a client body field.

Cross-tenant IDs must behave as not found or validation failure without confirming another tenant's existence. Reports, candidate lists, policy resolution, approval scope, attachments, background jobs, and ledger reconciliation all carry an explicit tenant boundary. Tests must attempt cross-tenant request, employee, policy, calendar, manager, and approver references.

## R. Concurrency and race analysis

Primary races are two submissions consuming one balance, approval versus cancellation/rejection, competing approvers, balance adjustment during reservation, policy publication during submit, duplicate submit, employee deactivation, and account unlink/replacement during a request.

Use an aggregate `Revision`/SQL rowversion, expected-revision commands, unique idempotency keys, unique current balance/request constraints where applicable, and transactions that encompass validation, reservation/consumption/release, state transition, approval event, and projection update. Serialize the employee/type/period balance scope using the provider-appropriate transaction/locking strategy. Do not claim SQL Server deadlock or race coverage until it is executed against isolated SQL Server test data.

An approval command must require the stage revision and assigned approver; the first valid decision wins. Cancellation cannot credit a request whose approval transaction already consumed the balance. A retry with the same idempotency key returns the original outcome without duplicate ledger/events.

Employee deactivation blocks new actions but leaves existing requests subject to explicit policy. Account unlink/replacement never changes request ownership; pending approval may be blocked for re-authenticated reassignment, with an event. These choices require business approval before implementation.

## S. Audit and history

Audit creation/change of LeaveTypes, policy versions/rules/applicability, leave periods/calendars, balance adjustments, accruals, reservations, approvals/rejections, cancellation, withdrawal, reassignment, routing failure, and administrator actions. Record tenant, aggregate/request, event type, old/new state or summary, actor account and linked employee where available, reason, correlation/idempotency key, effective/business date, and UTC timestamp.

Events are append-only. Do not expose sensitive attachment contents or unrestricted audit to ordinary employees. Retention/erasure is a separate legal decision; historical foreign keys should be restrictive until that policy exists.

## T. API design

Configuration endpoints, all tenant-scoped and permission guarded:

- `GET/POST/PATCH` `/api/leave/types`; deactivate rather than delete used types.
- `GET/POST` `/api/leave/policies`, `/api/leave/policies/{id}/versions`, and version rules/applicability endpoints.
- `GET/POST` `/api/leave/periods` and `/api/leave/calendars` plus calendar-day maintenance.

Employee self-service:

- `GET /api/me/leave/balances`
- `GET /api/me/leave/requests` and `GET /api/me/leave/requests/{id}`
- `POST /api/me/leave/requests`
- `POST /api/me/leave/requests/{id}/withdraw` and, where allowed, `/cancel`

Manager:

- `GET /api/leave/approvals/pending`
- `GET /api/leave/team` (date range and scope limited)
- `POST /api/leave/requests/{id}/approve` and `/reject`

HR/admin:

- `GET /api/leave/employees/{employeeId}/balances`
- `POST /api/leave/employees/{employeeId}/balance-adjustments`
- `GET /api/leave/requests` for permitted tenant-wide lookup
- explicit reassignment/reconciliation endpoints only after policy approval.

Use problem details/status codes that distinguish unauthorized, forbidden, not linked, no policy, validation, conflict/stale revision, routing review, and insufficient balance. Never return another tenant's existence through a different error. Commands accept an idempotency key and expected revision; quantity is calculated server-side.

## U. Frontend design

Employee pages: My Leave dashboard, balance cards, Apply Leave form with server-previewed calculation, My Requests, and request detail/timeline. The UI must show server state, reservation, policy/date errors, forbidden/not-linked states, and stale-revision conflicts distinctly.

Manager pages: Approval Inbox, request review with effective employment/routing explanation, and Team Leave Calendar limited to authorized scope. Admin pages: Leave Types, Leave Policies/version rules, Leave Year, Holiday/Work Calendar, employee balance inquiry, and audited balance adjustment.

The frontend must never infer eligibility or balances from local arithmetic. It should retain selected state after save, refresh current state/timeline after successful commands, and handle 403 as forbidden rather than empty data. Identity refresh should use the existing `/me` pattern where account-link changes affect self-service.

## V. Migration and data risks

Before any migration, inventory tenant employees with no current employment record, overlapping employment, missing manager, inactive manager, no account link, missing work location/calendar, invalid policy dimensions, duplicate configuration, and historical dates outside the first Leave period. Existing employees must not receive balances merely because the API starts.

Initial rollout needs an explicit opening-balance/import plan, policy assignment for uncovered employees, leave-year choice per tenant, and reconciliation evidence. Existing account-employee links are useful for self-service identity but are not evidence that every employee has a login. No existing Leave tables were found, so there is no schema collision identified; that must be rechecked before migration generation.

No migration is created or applied in Phase 4A.

## W. Testing strategy

Unit tests should cover policy ranking/ties/fallback, effective employment selection, entitlement/proration/rounding, leave-year boundaries, holiday/week-off/sandwich calculation, partial days, state transitions, and ledger arithmetic.

Backend integration tests should cover tenant filters/composite FKs, own identity versus arbitrary employee IDs, manager scope, permission combinations, missing/unlinked/inactive identities, policy version snapshots, immutable events, attachment metadata, idempotency, stale revisions, and forbidden/error distinctions.

Provider/SQL Server tests should use isolated test-owned databases and cover balance reservation races, approve/cancel races, first-decision-wins approval, unique constraints, transaction rollback, deadlock/retry policy, and ledger/projection reconciliation. These have not been executed.

Frontend tests should cover employee submit/refresh, reload/persisted balance, manager approval/rejection, policy/calendar errors, 403 versus empty states, stale revision handling, and `/me` identity refresh. Browser acceptance should cover employee → manager → employee flows, tenant boundaries, and audit timeline. Phase 3B browser acceptance remains separately deferred and is not implied by this design.

## X. Phased implementation plan

1. **Phase 4A — design/readiness review (this document):** decisions, data-contract review, policy/legal inputs, and migration inventory.
2. **Phase 4B — foundation:** LeaveType, LeavePeriod, policy/version/rule/applicability model, permission catalog proposal, validation, and deterministic policy resolution. No balances or request writes yet.
3. **Phase 4C — ledger/entitlement:** append-only balance transactions, projection, opening/accrual/carry-forward/expiry, idempotent jobs, and reconciliation.
4. **Phase 4D — requests/calculation:** LeaveRequest lifecycle, server day calculation, calendar provider, reservations, cancellation/withdrawal, and request history.
5. **Phase 4E — approvals:** stage abstraction, manager resolution adapter, routing snapshots, reassignment, approval races, and immutable approval history.
6. **Phase 4F — frontend:** employee, manager, and admin pages with permission/error/state handling.
7. **Phase 4G — integration/acceptance:** Attendance/Payroll adapters, SQL Server concurrency, migration rehearsal, security review, and browser acceptance.

## Y. Open design decisions

Business owners must decide: leave-year basis and timezone; paid/unpaid semantics and payroll handoff; statutory/gender/country rules; probation/confirmation treatment; sandwich/holiday/week-off policy; balance precision and rounding; negative balance; carry-forward/encashment; cancellation cutoff; whether pending requests retain or revalidate policy; manager-change routing; missing-manager escalation; attachment storage/retention; period-crossing request behavior; and legal retention/erasure.

Technical approval is also needed for the first calendar provider, SQL Server locking/isolation strategy, background-job host/idempotency store, and whether `LeaveRequestDay` is relational in the foundation. No permission grants should be provisioned until the permission catalog and role mapping are separately reviewed.

## Z. Recommendation for Phase 4B

Approve a narrow foundation containing `LeaveType`, `LeavePeriod`, `LeavePolicy`, `LeavePolicyVersion`, `LeavePolicyRule`, and applicability/resolution services, with tenant-safe keys, effective dating, immutable published versions, deterministic conflict handling, and isolated unit/integration tests. Do not add employee balances, request submission, approvals, or UI in 4B. First obtain the open business decisions above and complete a read-only employee/employment data-quality inventory before generating a migration.

Phase 3B browser acceptance and the parked API startup investigation remain deferred technical validation; this Phase 4A review neither reopens nor passes them.

# Phase 4A.1 — Leave Policy Configuration Specification

Status: SPECIFICATION ONLY — this section finalizes the configuration boundary for review before Phase 4B implementation. It creates no Leave code, entity, migration, API, UI, seed data, or database change.

The reference screenshots identify 82 policy/configuration concepts. They are grouped below into bounded policy rules rather than reproduced as 82 nullable columns or copied as a UI form. Some concepts are deliberately deferred because their semantics require Attendance, Payroll, statutory, or business decisions.

## A. Reference requirements mapped

| Reference concept | Final home | Decision |
|---|---|---|
| Leave Type, code, name, description | `LeaveType` | Stable tenant master |
| Policy name/code/description/document | `LeavePolicy` | Stable policy identity; document is versioned metadata |
| Effective dates, active, draft/published/retired | `LeavePolicyVersion` | Version lifecycle, never destructive update |
| Gender and employment dimensions | Applicability set | Stable IDs; null means unrestricted |
| Immediate/minimum service/confirmation/probation/status | Eligibility rule | Versioned policy behavior |
| Manager/HR/Time Manager apply-on-behalf | Authorization + request policy | No duplicate permission flags |
| Full/half/quarter/hour units | LeaveType capability + rule | Full/half in foundation; quarter/hour deferred |
| Annual/monthly/quarterly credit and proration | Entitlement rule | Engine-owned; quarterly only if business-approved |
| Maximum accumulation, carry-forward, expiry, lapse, encashment | Entitlement rule | Ledger/Payroll consumers execute later |
| Sandwich and holiday/week-off behavior | Calendar rule | One explicit treatment model, no contradictory flags |
| Advance, backdate, future horizon, duration tiers | Request-rule set | Backend validation |
| Request and consecutive/month/year limits | Request-rule set | Balance and request constraints remain distinct |
| Attachment mode/threshold | Attachment rule | Metadata only in this phase |
| Declaration/text/instructions | Versioned policy content | Accepted text is snapshotted on request |
| Clubbing | Normalized clubbing rules | Explicit symmetric relationship by default |
| Withdrawal/cancellation/modification | Cancellation rule + workflow | Command behavior implemented later |
| Previous leave year controls | Special request rule | Disabled by default; exceptional audited path only |
| Planned/unplanned | Backend-derived classification | Not a free-text/manual trust field |
| Auto approval | Workflow configuration | Only constrained, auditable future behavior |
| Delegation | Separate future Delegation module | Policy may require nomination, not store the delegate |
| Leave Pool | Open business decision | No implementation assumption |

## B. Final LeaveType boundary

`LeaveType` is the relatively stable tenant master: `Code`, `Name`, `Description`, active state, display order/color, paid/unpaid classification, and supported unit capability. Code is unique within a tenant and is never reused for a different semantic leave type. Deactivation is preferred over deletion once referenced.

LeaveType does not own annual entitlement, attachment thresholds, sandwich treatment, notice rules, probation rules, carry-forward, or maximum duration. Those vary by tenant, population, leave year, or policy version. A universal capability such as “this type can be requested in half-day units” may be declared on LeaveType, but the applicable policy rule may narrow it.

## C. Final LeavePolicy boundary

`LeavePolicy` is a stable named policy package identity with tenant, code, name, description, optional policy/reference document metadata, and audit ownership. It is not the employee's balance and does not directly contain mutable effective rules.

One policy identity may have successive published versions. A policy may contain rules for multiple LeaveTypes, but resolution is per LeaveType rule (section H), allowing one employee to receive Sick Leave from one applicable policy and Comp Off from another without a single broad policy suppressing a special benefit.

## D. LeavePolicyVersion model

`LeavePolicyVersion` contains `LeavePolicyId`, monotonic `VersionNumber`, `EffectiveFrom`, optional `EffectiveTo`, lifecycle (`Draft`, `Published`, `Retired`), change reason, created/published/retired audit data, and a concurrency token. A published version is immutable; corrections create a new version. Effective ranges for one policy cannot overlap.

Only Published versions participate in normal resolution. Draft versions are editable and testable by authorized configuration users but cannot affect requests. Retired versions remain queryable for history. A request stores the selected policy/version/rule IDs, resolution date, and calculation input snapshot so today’s configuration cannot rewrite yesterday’s explanation.

## E. Policy rule decomposition

Use a balanced aggregate:

- `LeavePolicyApplicabilitySet`: one OR branch, with nullable stable dimension IDs and explicit `Priority` inherited or overridden by the set.
- `LeavePolicyEligibilityRule`: minimum service, probation/confirmation, status, gender/statutory/family prerequisites, and notice-period behavior.
- `LeavePolicyEntitlementRule`: entitlement/accrual, proration, carry-forward, accumulation, negative balance, lapse, and encashment settings.
- `LeavePolicyRequestRule`: unit/partial-day capability, date notice, quantity/request limits, previous-period and planned/unplanned behavior.
- `LeavePolicyCalendarRule`: holiday/week-off baseline and sandwich mode.
- `LeavePolicyAttachmentRule`: attachment mode, threshold, allowed metadata/category, and retention reference.
- `LeavePolicyClubbingRule`: normalized pair relationship between LeaveTypes.
- `LeavePolicyCancellationRule`: withdrawal, cancellation, modification, cutoffs, and approval requirement.
- `LeavePolicyContent`: versioned declaration, instructions, terms, and reference-document metadata.

These are logical subordinate configuration areas. They may be implemented as owned/value objects or focused tables where queryability and FK integrity require it; the implementation must not create a table for every checkbox. A single versioned aggregate boundary is preferable for publish validation and immutable snapshots.

## F. Applicability model

Use a strongly relational `LeavePolicyApplicabilitySet` with nullable columns for the dimensions actually present in HRMS: `Gender` where the existing enum is appropriate, and IDs for Holding Company, LOB, Organisation, Department, Sub Department, Section, Sub Section, Function, Sub Function, Grade, Designation, Employee Type, Country, Work Location, and Cost Center. “Location” is represented by the existing Country/WorkLocation concepts; do not invent a second location master.

Each non-null ID is a tenant-scoped FK (or composite tenant-aware FK). Names and codes are display projections from current master data, never copied into applicability. Null means “this dimension does not restrict the set.” A set with every dimension null is the explicit all-employees fallback branch.

This is safer than a generic `Field + Operator + Value` rule engine for a bounded HR dimension catalogue: database integrity remains strong, unsupported fields cannot be smuggled into policy resolution, and administrators receive typed controls. Employee-code rule-builder concepts are not duplicated.

## G. Multiple applicability-set semantics

One version/rule can have zero or more applicability sets. Sets are OR-ed. Within one set, every non-null dimension is AND-ed. Thus:

`(Department=IT AND Grade=G5) OR (Department=Finance AND Grade=G6)`

is two sets, each with two populated columns. A version with no sets is invalid unless its rule is explicitly marked as the tenant fallback. A fallback must be unique per LeaveType resolution rank.

Each set has an explicit priority. A more specific set does not silently outrank a higher-priority policy; priority is evaluated first. Specificity is the count of populated restricting dimensions, with a documented weighting only if business approval later says some dimensions outrank others. The foundation uses count, not GUID, name, creation time, or row order.

## H. Resolution algorithm and policy-vs-type strategy

Resolution input is `(TenantId, EmployeeId, LeaveTypeId, EffectiveDate)`:

1. Resolve the employee through the tenant-scoped context and reject a missing/inactive employee where the operation requires active status.
2. Select exactly one effective employment-history row for the date; return `NoApplicableEmployment` or `ConfigurationAmbiguity` for zero/multiple rows.
3. Load Published policy versions effective on the date and rules for the requested LeaveType.
4. Evaluate applicability sets against stable employment/master IDs and typed gender/status inputs.
5. Rank matching rules by explicit policy priority, then set/rule specificity (number of populated applicability dimensions), then require uniqueness. No hidden tie-breaker is allowed.
6. Return the unique policy/version/rule, or `NoPolicy`, or `ConfigurationAmbiguity`.

Resolution is independently performed per LeaveType, while returning a `ResolvedLeavePolicyRule` that includes the common version plus all rule sections needed by the caller. This gives an employee-wide coherent version for one type without forcing unrelated LeaveTypes into one policy. A future “policy package” may group display/configuration but must not change per-type resolution semantics.

## I. Leave period design

`LeavePeriod` is a tenant-owned named period with `PeriodCode`, `StartDate`, `EndDate`, basis (`CalendarYear`, `FinancialYear`, `Custom`), status, timezone/business-date policy, and audit metadata. A tenant has one configured default leave-year basis, but multiple named periods may coexist only when their purpose and selection are explicit; normal balance resolution must select one unambiguous period.

Policies reference the applicable period basis or period family, not a hard-coded Jan–Dec assumption. A published policy version may be effective-dated independently of a period boundary, but entitlement calculations must resolve the exact period containing the effective date. Period definitions are not retroactively edited; corrections create a successor and preserve prior ledger meaning.

## J. Eligibility configuration

`LeavePolicyEligibilityRule` should support:

- eligibility timing: immediate, after a duration from joining, or after confirmation;
- service duration value and unit (`Days`, `Months`, `Years`) with a declared boundary convention;
- probation allowed/denied and confirmation required;
- allowed employee statuses, with active as the conservative default;
- notice-period leave allowed/denied;
- gender applicability when legally/business required;
- ESIC applicability condition only where the existing employee field is authoritative and the rule is approved;
- family-detail prerequisite category, without copying family sensitive data into policy;
- minimum service and optional joining/exit proration eligibility inputs.

Eligibility is evaluated by the future engine using effective employment and employee data; this specification does not implement it. Rules must distinguish “not eligible,” “missing required data,” and “requires HR review.” A missing family detail or ESIC value must not silently make a leave eligible.

## K. Entitlement configuration

`LeavePolicyEntitlementRule` should support one entitlement basis per rule: annual grant, monthly accrual, or a later approved quarterly frequency. It records quantity/precision, accrual timing (period start/end or scheduled date), proration mode for joiners/leavers, confirmation behavior, rounding mode, maximum accumulation, negative-balance allowance, carry-forward cap/expiry, lapse, and encashment eligibility.

Entitlement is not balance. The later engine converts these settings into ledger transactions and records the rule/version used. Quarterly accrual is not needed for the foundation unless a real policy requires it. Rounding must be explicit (`None`, `Floor`, `Ceiling`, `Nearest`) with precision and a deterministic midpoint rule.

## L. Partial-day model

Represent requested quantity as a decimal domain quantity with a unit and a per-day segment, not as separate LeaveTypes. Foundation capabilities are `FullDay` and `HalfDay`; a half-day identifies first/second session or a tenant-defined half-day segment. Quarter-day and hourly leave are deferred until schedule/attendance semantics, precision, and payroll impact are approved.

Do not support arbitrary combinations such as half-plus-quarter by adding flags. If later required, introduce a `LeaveDayPortion` value object with bounded portions and a schedule provider. The server calculates and validates quantity; the client cannot submit a trusted total.

## M. Request-rule configuration

`LeavePolicyRequestRule` separates request constraints from balance constraints:

- allowed unit/partial-day modes;
- minimum and maximum quantity per request;
- maximum consecutive days;
- maximum quantity per month/year;
- maximum request count per month/year, or unlimited;
- minimum advance notice and future horizon;
- maximum backdated days and emergency exception policy;
- previous-period request/approval/cancellation switches, all disabled by default;
- planned/unplanned classification inputs;
- whether an employee may request when the balance is zero (only if a negative-balance rule permits it).

Duration-tier notice rules are justified by the reference requirement. Model them as ordered `LeavePolicyAdvanceNoticeBand` rows: minimum/maximum requested quantity plus required calendar/business days. Bands must be contiguous/non-overlapping or include an explicit default; ties are invalid. This avoids a proliferation of nullable `NoticeDaysFor...` columns.

Request count limits are policy constraints; available balance is a ledger constraint; calendar/day eligibility is a calendar engine constraint. They must remain separate in errors and audit.

## N. Holiday, week-off, and sandwich model

Use two layers:

1. Baseline treatment: `PublicHolidayTreatment` and `WeekOffTreatment`, each `Exclude` or `Include`, determining normal quantity when a non-working date lies inside a requested range.
2. `SandwichMode`: `NotApplicable`, `IncludeBetweenLeave`, `IncludeAdjacentAndBetween`, or an equivalent explicitly named mode whose semantics are fixed in the domain contract.

The foundation recommendation is `Exclude` baseline for holidays/week-offs and `NotApplicable` sandwich unless the policy opts in. Sandwich semantics must define independently whether a holiday/week-off is counted when it is between two leave dates, directly before the range (prefix), directly after the range (suffix), or any combination. Prefer a structured `SandwichRule` with boolean bounded positions (`Prefix`, `Suffix`, `Between`) over four unrelated flags; reject contradictory modes.

“Public holiday included” is not the same setting as “sandwich included.” A calendar date may be excluded normally yet included by a sandwich rule. The future day engine must record which rule caused each date's inclusion/exclusion.

## O. Attachment and declaration model

`AttachmentRequirement` is `None`, `Optional`, `Required`, or `RequiredAboveQuantity`. When threshold mode is selected, a positive threshold quantity is required; otherwise the threshold is absent/ignored. The rule may carry an approved document category and size/count policy, but Phase 4A stores no uploaded file.

Declaration belongs to `LeavePolicyVersion`/content, not LeaveType, because wording changes with policy/legal versions. Configuration includes `DeclarationRequired`, versioned declaration text, and acknowledgement label. Instructions and terms/conditions are also versioned content. A submitted request later stores the exact accepted declaration text/version and acknowledgement timestamp.

## P. Clubbing design

Use normalized `LeavePolicyClubbingRule` rows with `LeaveTypeAId`, `LeaveTypeBId`, `Relation` (`Allowed`/`NotAllowed`), and optional order semantics. The default is symmetric: a NotAllowed pair applies in either order, enforced by canonical ordering of the two type IDs. This means “Casual cannot be clubbed with Sick” also prevents Sick followed by Casual.

Direction-specific rules are deferred. If a tenant genuinely requires “A may precede B but not follow B,” add an explicit `Before`/`After` relation and require both directional rows; do not silently reinterpret the symmetric default. Duplicate/reversed conflicting rows are invalid at publish time.

## Q. Cancellation and special configuration

`LeavePolicyCancellationRule` should define employee withdrawal before approval, cancellation of approved leave, cancellation cutoff in days, behavior after leave starts, modification allowed, and whether cancellation requires approval. Modification should be modeled as a new command/revision or cancel-and-replace event, not an in-place date mutation.

Previous-period requests/approval/cancellation are disabled by default. If legally required, permit only a separately authorized, date-bounded, reasoned path that retains the historical policy/period and immutable audit. Auto approval belongs to workflow configuration and must be restricted by conditions, approver bypass reason, and audit; it is not a simple LeaveType flag.

Apply-on-behalf is a combination: authorization grants the capability (`Leave.RequestForOthers` or a reviewed manager/HR permission), manager scope determines permitted employees, and policy may restrict the action to L1/L2/L3/HR/Time Manager categories. Avoid duplicating “manager can apply” as an unreviewed policy boolean. A privileged administrator capability must be permission-controlled and audited, never inferred from a role name.

Delegation is not implemented here. A policy may set `DelegationRequired`, which later blocks submission or routes a task to a future Delegation module. It must not store delegate identity in Leave until the separate module defines scope, dates, revocation, and authorization.

Leave Pool is deferred: the screenshots do not establish whether it means shared quota, donated leave, common organizational entitlement, or another concept. No pool foreign key or balance-sharing semantics should be invented.

## R. Strongly typed enum catalogue

Recommended bounded enums/value objects:

| Name | Values/shape | Scope |
|---|---|---|
| `LeaveUnit` | `Day`, `Hour` (Hour deferred operationally) | Domain quantity |
| `PartialDayMode` | `FullDayOnly`, `HalfDay` (Quarter/Hourly extension deferred) | Type/rule capability |
| `LeavePolicyStatus` | `Draft`, `Published`, `Retired` | Version lifecycle |
| `AccrualFrequency` | `Annual`, `Monthly`, `Quarterly` (quarterly gated) | Entitlement |
| `AccrualTiming` | `PeriodStart`, `PeriodEnd`, `ScheduledDate` | Entitlement |
| `AttachmentRequirement` | `None`, `Optional`, `Required`, `RequiredAboveQuantity` | Attachment rule |
| `HolidayTreatment` | `Exclude`, `Include` | Calendar baseline |
| `WeekOffTreatment` | `Exclude`, `Include` | Calendar baseline |
| `SandwichMode` | `NotApplicable`, `IncludeBetween`, `IncludeAdjacentAndBetween` | Calendar rule |
| `EligibilityServiceUnit` | `Days`, `Months`, `Years` | Eligibility |
| `RoundingMode` | `None`, `Floor`, `Ceiling`, `Nearest` | Entitlement |
| `RequestLimitPeriod` | `Month`, `LeaveYear`, `Request` | Limits |
| `CancellationMode` | `NotAllowed`, `EmployeeAllowed`, `ApprovalRequired` | Cancellation |
| `PolicyRuleRelation` | `Allowed`, `NotAllowed` | Clubbing |
| `PolicyVersionContentKind` | `Declaration`, `Instruction`, `Terms` | Versioned content |

Gender, employee status, employment type, country, and organizational values remain existing domain/master data, not administrator-defined enums. Policy codes, leave types, document categories, and applicability masters remain data, not enums.

## S. Future Policy Configuration UI layout

The UI should edit a Draft version and publish only after server validation. Suggested sections are:

1. Basic Details
2. Applicability
3. Eligibility
4. Entitlement & Credit
5. Request Rules
6. Calendar & Sandwich Rules
7. Documents & Declaration
8. Clubbing Rules
9. Cancellation / Special Rules
10. Version & Publish

No section should expose a field that is irrelevant to the selected mode. The backend remains authoritative for all validation and publish conflicts.

## T. Conditional-field matrix

| Field/control | Control and requiredness | Visibility/default | Backend home and dependency |
|---|---|---|---|
| Policy Code | Text, required, unique per tenant | Always; no default | `LeavePolicy`; immutable after publication |
| Policy Name/Description | Text/textarea; name required | Always | `LeavePolicy` |
| Reference document metadata | Text/URI/document reference; optional | Always | Versioned content; no upload in 4A |
| Leave Type | Tenant-scoped selector; required | Rule editor | `LeavePolicyRule`; must be active |
| Effective From/To | Date/date; from required, to optional | Version section | `LeavePolicyVersion`; no overlap |
| Version status | Read-only lifecycle control | Draft initially | Server publish validation |
| Applicability dimensions | Typed selectors; optional individually | Always | Applicability set; null = unrestricted |
| Additional applicability set | Repeater; optional, at least one for restricted rule | Add set action | OR sets; duplicate/conflicting sets rejected |
| Gender | Existing enum selector; optional | Applicability | Typed condition |
| Minimum service | Number + unit; optional as pair | Show when service restriction enabled | Eligibility; positive value |
| Confirmation required | Checkbox; default false | Always | Eligibility |
| Probation allowed | Tri-state/explicit policy choice; default policy decision required | Eligibility | Eligibility; no ambiguous null after publish |
| Notice-period allowed | Checkbox; default false | Eligibility | Eligibility |
| Family/ESIC prerequisite | Typed selector; optional | Only when business/legal rule selected | Eligibility; missing data = review/deny, not allow |
| Entitlement basis | Dropdown: Annual/Monthly/(Quarterly) | Required for paid balance rule; not for Unpaid | Entitlement |
| Entitlement quantity | Decimal; required for credited types | When balance entitlement applies | Entitlement; precision/rounding required |
| Accrual timing/frequency | Dropdowns; required for accrual basis | Show for accrual basis | Entitlement |
| Joiner/leaver proration | Dropdown; optional/default explicit no-proration | Entitlement | Entitlement engine |
| Rounding/precision | Dropdown + number; required when fractional | Show for fractional quantity | Entitlement |
| Carry forward | Dropdown off/on; cap/expiry required when on | Default off | Entitlement |
| Negative balance | Checkbox; default false | Always | Entitlement/request validation |
| Encashment/lapse | Explicit dropdowns; default not eligible/no lapse action | Always | Entitlement; Payroll later |
| Partial-day mode | Dropdown FullDay/HalfDay | Show if type supports it | Request rule; type capability must permit |
| Request min/max | Decimal pair; optional but ordered | Request rules | Request rule |
| Maximum consecutive days | Number; optional | Request rules | Request rule |
| Monthly/yearly quantity/count limits | Repeater by limit period; optional | Add limit action | Request rule; no duplicate period/measure |
| Advance notice bands | Repeater; optional; quantity range + days required | Add tier action | Request rule; bands contiguous/non-overlap |
| Future horizon/backdate | Number fields; explicit defaults | Request rules | Request rule |
| Emergency exception | Policy choice + permission/reason requirement | Hidden unless enabled | Request/workflow; future scope |
| Holiday/week-off treatment | Two dropdowns; explicit defaults | Calendar section | Calendar rule; independent baseline |
| Sandwich positions/mode | Structured selector; default not applicable | Show when enabled | Calendar rule; no contradictory flags |
| Attachment mode | Dropdown None/Optional/Required/RequiredAboveQuantity | Always | Attachment rule |
| Attachment threshold | Positive decimal; required only above-threshold | Conditional on threshold mode | Attachment rule |
| Declaration required/text | Checkbox + rich text; text required when checked | Documents section | Versioned content |
| Instructions/terms | Versioned rich text; optional | Always | Versioned content |
| Clubbing pair/relation | Multi-select/repeater; optional | Clubbing section | Canonical symmetric pair |
| Withdrawal/cancellation/modification | Dropdowns + cutoff; explicit defaults | Cancellation section | Cancellation rule |
| Previous-period controls | Three explicit switches; default false | Special rules | Separate exceptional permission/audit |
| Planned/unplanned | Backend-derived policy choice | Read-only explanation | Request rule; no trusted client flag |
| Auto approval | Workflow mode; default never | Only for approved workflow scope | Workflow, not LeaveType |
| Delegation required | Checkbox; default false | Special rules | Marker only; future Delegation module |
| Leave Pool | Not rendered | Deferred | Open decision; no model yet |

## U. Phase ownership matrix

| Requirement/settings group | Phase |
|---|---|
| LeaveType, policy identity, draft/publish/version validation | Phase 4B — Policy Foundation Backend |
| Applicability sets and deterministic resolution | Phase 4B — Policy Foundation Backend |
| Eligibility rule persistence/validation | Phase 4B — Policy Foundation Backend |
| Policy configuration UI and conditional controls | Phase 4C — Policy UI |
| Annual/monthly entitlement, proration, carry-forward, accrual | Phase 4D — Balance/Entitlement Engine |
| Opening, adjustment, reservation, consumption, expiry ledger | Phase 4D — Balance/Entitlement Engine |
| Holiday/week-off calendar and sandwich calculation | Phase 4E — Calendar/Day Calculation |
| Leave request, quantity, notice, limits, attachments | Phase 4F — Apply Leave |
| L1/L2/L3/HR/Time Manager stages and auto approval | Phase 4H — Approval Workflow |
| Withdraw, cancel, modify, historical-period exceptions | Phase 4J — Cancellation/Adjustments |
| Delegation, Leave Pool, quarter/hour units, shift-aware rules | Future/Deferred |

The labels are intentionally non-contiguous with the earlier implementation plan: they identify ownership of the settings, not authorization to start those phases now.

## V. Data-quality dependencies

Required for policy resolution:

- tenant-scoped active Employee identity;
- exactly one non-overlapping effective employment-history record for the resolution date;
- stable IDs for any populated applicability dimension;
- valid date-of-joining/status inputs when eligibility uses them;
- an unambiguous LeavePeriod.

Required later for request calculation or approval, but not necessarily policy matching:

- Work Location/calendar assignment for holiday/day calculation;
- manager assignment when the selected workflow requires one;
- linked account↔employee identity for self-service;
- active linked approver account for an approval stage;
- family/ESIC detail where a policy explicitly requires it;
- opening balance/ledger initialization for paid leave.

Missing optional dimensions mean unrestricted applicability only when the policy leaves them null. Missing required employee data must produce a data-quality outcome, not broaden eligibility.

## W. Five sample policy walkthroughs

1. **Casual Leave:** LeaveType `CASUAL`; India applicability is represented by the tenant/country ID; permanent employee type and selected organization dimensions are in one applicability set. The version contains annual entitlement 12, HalfDay capability, no sandwich, maximum three consecutive days, and explicit request notice. The type itself does not contain “12” or “three.”

2. **Sick Leave:** LeaveType `SICK`; annual entitlement 12, backdate maximum three days, and `RequiredAboveQuantity` attachment at quantity greater than two with the approved medical document category. Holiday treatment and sandwich remain independent settings.

3. **Maternity Leave:** LeaveType `MATERNITY`; gender applicability is an explicit rule, document requirement is `Required`, and service/eligibility conditions are versioned. The employee's family information is referenced only as an eligibility prerequisite if legally approved; sensitive details are not copied to policy/request snapshots.

4. **Comp Off:** LeaveType `COMP_OFF`; applicability includes WorkLocation ID, with a rule for its request window and expiry. It may use a distinct accrual/source ledger transaction later. Location is selected by stable ID; changing the location name does not rewrite the policy.

5. **Unpaid Leave:** LeaveType `UNPAID`; entitlement basis is `NoBalance`/non-credited, approval is required through workflow, and request availability when paid balance is insufficient is an explicit request/workflow policy decision. It is not implemented as a negative paid balance unless the tenant separately allows that.

These examples validate that type identity, population applicability, entitlement, request rules, calendar rules, documents, and approvals remain separate and versionable.

## X. Open business decisions

Approval is required for: tenant leave-year basis/timezone; whether financial/custom periods can coexist; service-month boundary semantics; confirmation/probation behavior; legal gender/country/ESIC/family rules; paid/unpaid and Payroll handoff; quarterly accrual; proration and rounding; carry-forward/expiry/encashment/lapse; negative balance; sandwich prefix/suffix/between semantics; emergency notice exceptions; previous-period operations; attachment storage/retention; declaration/legal retention; symmetric versus directional clubbing; cancellation/modification and manager-change routing; auto-approval conditions; delegation behavior; Leave Pool meaning; and quarter/hour/shift support.

Technical approval is required for the exact aggregate/table split, relational `LeaveRequestDay` decision, calendar provider contract, permission names/grants, SQL Server balance locking strategy, and background accrual/idempotency mechanism. No proposed permission is granted by this document.

## Y. Differences from the original Phase 4A design

The original design proposed the broad Leave foundation. This specification makes it implementable by:

- choosing per-LeaveType rule resolution while retaining version coherence;
- replacing generic applicability conditions with typed nullable dimension sets and explicit OR/AND semantics;
- decomposing policy behavior into focused rule areas without a giant table;
- separating baseline holiday/week-off treatment from sandwich treatment;
- modeling duration-tier advance notice as normalized bands;
- defining symmetric clubbing as the default with canonical pairs;
- placing declarations/instructions in policy versions;
- treating apply-on-behalf as permissions plus manager scope, not duplicated flags;
- deferring Leave Pool, delegation execution, quarter/hour units, and Attendance-dependent behavior;
- defining conditional UI and phase ownership so Phase 4B cannot expand into Apply Leave.

## Z. Exact recommended Phase 4B implementation scope

Implement only the reviewed policy foundation:

1. Tenant-scoped LeaveType master with stable code and lifecycle.
2. LeavePolicy identity and draft/published/retired LeavePolicyVersion.
3. LeavePolicyRule for a LeaveType, with focused eligibility, request, entitlement, calendar, attachment/content, and clubbing configuration contracts.
4. Typed `LeavePolicyApplicabilitySet` using existing HRMS IDs and tenant-safe FKs.
5. LeavePeriod definition and unambiguous effective-period resolution.
6. Publish-time validation for date overlaps, duplicate rules, applicability conflicts, notice bands, clubbing contradictions, and required conditional fields.
7. Deterministic `(TenantId, EmployeeId, LeaveTypeId, EffectiveDate)` policy resolution returning `NoPolicy` or `ConfigurationAmbiguity` explicitly.
8. Unit and isolated integration tests for tenant isolation, effective employment, OR/AND applicability, priority/specificity ties, lifecycle immutability, and missing-data outcomes.

Do not implement balances, accrual jobs, LeaveRequest, day calculation, approval, cancellation, attachments, Holiday Calendar, Attendance/Payroll integration, frontend, seed data, permissions grants, or migrations that initialize employee balances in Phase 4B. Migration generation remains a later approved action after data-quality inventory and business decisions.

## Phase 4B Implementation Status

Implementation started within the approved Phase 4B boundary. Added foundation domain types for `LeaveType`, `LeavePeriod`, `LeavePolicy`, `LeavePolicyVersion`, `LeavePolicyRule`, and `LeavePolicyApplicabilitySet`, plus `LeaveUnit` and `LeavePolicyVersionStatus`. Added explicit tenant-aware EF configurations, indexes, unique constraints, restrictive deletes, enum/date mappings, DbSets, and tenant query filters. Applicability uses existing HRMS master IDs, including a simple global FK for the existing global `Country` master and composite tenant-aware FKs for tenant masters.

Added application contracts and services for draft-version creation, period validation, publish validation, and deterministic per-LeaveType resolution. Draft versions are excluded from resolution; only active policies and Published versions effective on the inclusive requested date are candidates. Empty applicability sets mean tenant-wide applicability. Populated dimensions inside a set are AND-ed; multiple sets are OR-ed. Candidates rank by explicit priority and then the highest matching-set specificity; equal best candidates return `ConfigurationAmbiguity`, while no candidate returns `NoPolicy`. Effective employment is selected from the single non-overlapping `EmployeeEmploymentHistory` row covering the requested date.

Added focused SQLite-backed resolver tests for no-policy, lifecycle/date filtering, empty applicability, specificity, ambiguity, and employment transfer dates. The test source is present but cannot yet be executed because the current workspace's Infrastructure/test build evaluation fails before compilation while resolving existing project/framework references. Application compilation succeeds; the Infrastructure/API/test build currently reports a failed build with no compiler diagnostics in normal verbosity, and detailed evaluation shows the existing intermediate/project-reference resolution failure. `NU1900` is only a restore warning and is not treated as the build failure.

No public API, permission, seed/configuration data, future Leave rule entity, balance, request, calendar, workflow, UI, migration, or database operation was added. Exactly one migration named `LeavePolicyFoundation` remains to be generated after the Infrastructure model can be compiled and focused tests pass; it has not been generated or applied. No Phase 3B investigation was reopened.

## Phase 4B.1 Validation Status

The project graph was verified: all five backend projects target `net10.0`, every declared relative `ProjectReference` resolves to an existing project, and no files under the five project-local `bin`/`obj` directories were Git-tracked. The one bounded cleanup removed only those generated `bin` and `obj` directories. The one normal solution restore then returned exit code 1 with no diagnostic output. Per the Phase 4B.1 stop condition, no alternate restore/build cycle, migration generation, or test execution was attempted.

## Phase 4C.0.1 — Foundation Decision Closure

This is a design decision record only. It does not authorize Phase 4C.1 implementation, restore, build, tests, migration generation, startup, or database access.

### Decision status summary

| Decision | Status | Frozen outcome |
|---|---|---|
| LeavePeriod relation | **CLOSED** | No `LeavePeriodId` on `LeavePolicyVersion`. Policy resolution and leave-period resolution are independent and are combined later by entitlement/balance processing. |
| LeavePeriod purpose | **CLOSED** | Tenant accounting/leave-year boundary for entitlement, accrual, carry-forward, lapse, and balance statements; it contains no policy rules. |
| LeavePeriod resolution | **CLOSED** | `(TenantId, EffectiveDate)` returns exactly one active containing period; zero is `LeavePeriodNotConfigured`; more than one is `LeavePeriodConfigurationAmbiguity`. |
| Priority | **CLOSED** | Higher numeric value is stronger. Priority is evaluated before specificity. |
| Empty applicability | **CLOSED** | Zero groups means all otherwise eligible employees in the tenant, with specificity 0. |
| Group logic | **CLOSED** | Dimensions within a group are AND; groups are OR; specificity is the maximum matching-group specificity, never a sum. |
| Resolution scope | **CLOSED** | Resolve independently for `(TenantId, EmployeeId, LeaveTypeId, EffectiveDate)`, not one global policy per employee. |
| LeaveType inactivity | **CLOSED** | Inactive types cannot be newly attached or published; historical references remain; inactive types are not offered for future requests. No physical delete. |
| Policy inactivity | **CLOSED** | Inactive parents cannot create/publish versions and are excluded from normal current/future resolution; historical versions remain queryable as history. No cascade retirement/delete. |
| Version lifecycle | **CLOSED** | Draft never resolves; Published resolves only within inclusive dates; Retired is retained and excluded from normal runtime selection. |
| Version editing | **CLOSED** | Draft is editable; Published and Retired are immutable. Changes require a new Draft version. |
| Version cloning | **CLOSED** | Plan an explicit Create New Version command with optional copy of active LeaveType rules and applicability only; new identity/number/status/dates are generated and audit IDs are never copied. |
| Draft vs Publish validation | **CLOSED** | Draft Save permits incomplete but structurally safe work; Publish requires strict rules, references, dates, priority, rules, and overlap checks. |
| Policy code | **CLOSED** | Editable until the policy has a Published version; thereafter immutable unless a separately controlled rename operation is approved. Names/descriptions follow audit conventions. |
| LeaveType code | **CLOSED** | IDs remain relationship keys. Code is editable only before published historical use; afterward use controlled rename or keep immutable. |
| Permissions | **CLOSED** | `Leave.TypeManage`, `Leave.PeriodManage`, `Leave.PolicyView`, `Leave.PolicyManage`, `Leave.PolicyPublish`; no grants are added now. |
| Publish authorization | **CLOSED** | PolicyManage can create/edit/validate Drafts. PolicyPublish is separately required for Publish and Retire. PolicyView is read-only. |
| API errors | **CLOSED** | Reuse repository response/problem-details conventions with ValidationError, NotFound, Forbidden, ConcurrencyConflict, ConfigurationAmbiguity, InvalidLifecycleTransition, DuplicateCode, and CrossTenantReference categories. |
| Draft concurrency | **CLOSED** | Reuse the established `RowVersion`/concurrency-token convention where supported; if absent on Leave, add the smallest consistent token during implementation. Stale writes return 409. |
| API boundaries | **CLOSED** | Separate identity, version, LeaveType-rule, applicability, and lifecycle operations; no SaveEverything endpoint and no excessively chatty per-field commands. |
| Editor loading | **CLOSED** | One read-oriented editor DTO may include Policy, CurrentVersion, rules/types, applicability groups, allowed actions, revision token, and safe validation summary; writes remain grouped commands. |
| Editor save | **CLOSED** | Section-based tabs/cards with Save Draft, not a rigid wizard. Only supported sections are interactive. |
| Master controls | **CLOSED** | Searchable Code – Name controls; persist IDs; cascade and clear invalid children; null displays as All; never create an ALL record. |
| Resolution preview | **DEFERRED** | Phase 4C.6; desirable but not required for initial UI or publish. |
| 82-item coverage | **CLOSED** | The existing numbered mapping remains complete; future concepts stay mapped but unimplemented. |
| Coding prerequisites | **BLOCKED** | Phase 4C.1 waits for Phase 4B restore, compilation, focused tests, and reviewed `LeavePolicyFoundation` migration generation. |

### A. LeavePeriod and policy-version boundary

`LeavePolicyVersion` answers which policy configuration applies to an employee, LeaveType, and effective date. `LeavePeriod` answers which tenant accounting/leave-year period contains a date. They deliberately have separate lifecycles and effective ranges. For example, two policy versions covering January–June and July onward can both operate inside one January–December LeavePeriod. Later processing performs both resolutions and combines their results; Phase 4C.1 must not add a foreign key between these concepts.

The future period resolver uses inclusive `StartDate`/`EndDate` semantics and rejects ambiguity rather than selecting by creation order. Periods do not contain policy rules and do not create employee balances.

### B. Frozen policy resolution semantics

For a requested LeaveType, active policy parents and Published versions effective on the date are candidates only when they contain an active rule for that LeaveType. A zero-group version is tenant-wide at specificity 0. For a matching version with groups, its candidate specificity is the highest count of populated restrictive dimensions among matching groups. Priority is compared first: 100 beats 50 even if the latter is more specific. Only after equal priority does specificity apply. Equal best priority and specificity across candidates returns `ConfigurationAmbiguity`; GUID, name, timestamps, database order, and `FirstOrDefault` are forbidden tie-breakers.

Resolution is independent per LeaveType: Casual, Sick, Maternity, and Comp Off may resolve to different policy versions for the same employee/date. Employment is resolved effective-dated, not merely from today’s current row.

### C. Lifecycle, immutability, and cloning

Draft is editable and never runtime-selectable. Published is runtime-selectable only within inclusive effective dates and is business-immutable. Retired is retained for history and excluded from normal current/future selection; effective dates remain the historical temporal boundary. No UI action may suggest that retirement deletes history.

LeaveType and Policy parent deactivation is non-destructive. An inactive LeaveType cannot be newly selected or published, but an old Published version remains explainable. An inactive policy cannot create/publish versions or resolve normally, while its records remain available to authorized history views.

Create New Version is an explicit operation. Optional cloning, when implemented, copies only active LeaveType selections and applicability values, assigns a new ID and server-generated version number, starts Draft, and requires new effective dates. It never copies audit identifiers or silently republishes.

### D. Validation and authorization contract

Draft Save requires identity and structurally valid values, supplied dates in order, valid enum values, tenant-owned references, and no duplicate active rule. It may temporarily contain zero rules, zero groups, and absent future configuration. Publish additionally requires an active parent, at least one active valid LeaveType rule, valid applicability references, valid priority, and no overlapping Published version within the same policy. It does not attempt a global proof of every possible cross-policy match.

The API must independently enforce tenant scope and permissions. PolicyManage without PolicyPublish cannot publish or retire. PolicyView cannot mutate. Errors use the existing response/problem-details style and map to 400, 403, 404, or 409 as appropriate; no parallel error framework is introduced.

### E. Concurrency recommendation

Repository inspection found `RowVersion` configured with `IsRowVersion()` for concurrency-sensitive existing resources, but the current Phase 4B Leave entities do not yet expose a token. Phase 4C.1 should use the smallest compatible mechanism: a server-generated row-version/concurrency token on editable Draft aggregate resources, returned as an opaque DTO token and required on grouped updates and lifecycle commands. The UI sends the token; a stale update returns 409: “Configuration changed by another user. Reload before saving.” It must preserve unsaved values and never auto-overwrite. Version-number uniqueness remains backed by `(TenantId, LeavePolicyId, VersionNumber)`.

### F. API aggregate boundaries and editor read model

Use these grouped operations: policy identity; Draft version metadata; LeaveType rule selection as one version-scoped set; applicability groups as one version-scoped set; and lifecycle commands Validate, Publish, Retire, and Create New Version. A single editor GET may return the parent, current/draft version, selected types, applicability groups, allowed actions, revision, and safe validation summary. This balances coherent saves with a small number of requests and does not make all writes one giant transaction endpoint.

### G. UI decision record

The editor is section-based: Overview, Leave Types, Applicability, future Eligibility, future Entitlement & Credit, future Request Rules, future Calendar & Sandwich, future Documents & Declaration, future Clubbing, future Cancellation & Special, Review & Publish, and Version History. Only Overview, Leave Types, Applicability, and lifecycle review are interactive in the initial Phase 4C release. Published/Retired versions show no normal Edit action.

Applicability cards state AND inside a group and OR between groups. Master selectors are searchable and cascading where the real repository hierarchy supports it. A null field is labelled All and persisted as null. Resolution Preview is deferred to 4C.6 until Phase 4B has been validated; it is advisory and not a publish gate.

### H. Requirement coverage and implementation gate

The 82-item screenshot/reference table in Phase 4C.0 remains complete. Requirements not represented in the initial interactive UI—including entitlement, eligibility, attachments, declarations, clubbing, cancellation, previous-period actions, planned/unplanned classification, auto-approval, delegation, and Leave Pool—remain explicitly mapped to future owners/phases or open decisions. No unsupported control or endpoint is implied by this closure.

Phase 4C.1 must not begin until backend restore succeeds, Domain/Application/Infrastructure compile, focused Phase 4B Leave tests pass, and the `LeavePolicyFoundation` migration is generated and statically reviewed without being applied. The current Phase 4B environment blocker remains **BLOCKED** and is not retried here.

## Phase 4C.0 — Leave Policy Configuration API/UI Specification

This section is a design specification only. No Phase 4C controller, DTO, permission, frontend route, migration, or seed data exists as a result of this review.

### A. Navigation and product boundary

The signed-in application should expose a permission-filtered **Leave Management** area with only the Phase 4C configuration screens: Leave Types (`/leave-management/types`), Leave Periods (`/leave-management/periods`), and Leave Policies (`/leave-management/policies`). Holiday Calendar, Balance Administration, Apply Leave, approvals, and other future areas should not be functional navigation items in Phase 4C. The existing navigation convention filters menu items cosmetically while `RequirePermission` and server authorization remain the security boundary.

The frontend should reuse the existing shared API client, `useApiQuery`, `PageHeader`, `Card`, `DataTable`, `Badge`, `Notice`, `ErrorState`, `ConfirmDialog`, pagination, and `MasterDropdown` patterns. API modules belong under `Frontend/HRMS.Web/src/api`; route pages belong under `src/pages`; no EF entity crosses the API boundary.

### B. Leave Type UI

The list is a paginated, searchable table with Code, Name, Paid/Unpaid, Default Unit, Active/Inactive, Last Updated, and permitted actions. The editor contains Code, Name, Description, Default Unit (currently Day; Hour may exist in the foundation enum but is not operationally enabled), Paid, and Active. Codes are trimmed and normalized by the API using one documented invariant rule; the UI displays the canonical server value.

Create and edit are separate permission-gated actions. Duplicate code returns a field-level conflict without clearing the form. Deactivation is a confirmation action and is preferred to deletion. A LeaveType referenced by a published version is never physically deleted; deactivation does not rewrite historical versions. No entitlement, attachment, sandwich, probation, carry-forward, or request setting appears on this screen.

### C. Leave Period UI

The list shows Code, Name, Start Date, End Date, Active, and actions. The editor uses date-only controls and validates `StartDate <= EndDate`. The API rejects an active period that overlaps another active period for the same tenant; the message identifies the conflicting date range without exposing database details. Same code in different tenants is valid. The UI must not assume January–December or April–March.

The current Phase 4B model has no `LeavePeriodId` on `LeavePolicyVersion`; the Phase 4C API must not invent one in a controller. Before implementation, decide whether a version selects a period explicitly or whether effective dates alone associate it with a period. This is a design gap, not a reason to duplicate period values in policy rows.

### D. Policy list

The policy list contains Policy Code, Policy Name, Active, current Published Version, effective range, Leave Type count, applicability-group count, lifecycle status, and actions. View is available with `Leave.PolicyView`; draft editing and version creation require `Leave.PolicyManage`; publishing and retiring require separate `Leave.PolicyPublish`. Published versions are read-only. Actions are View, Edit Draft, Create New Version, Validate, Publish, Retire, and Version History, each rendered only when authorized and valid for lifecycle state.

### E. Policy editor architecture

Use section-based tabs/cards with independent **Save Draft** operations, not a rigid wizard. Existing HRMS forms support section-oriented editing, while a draft may be incomplete. The header displays parent policy, version, lifecycle badge, effective dates, priority, and Save Draft, Validate, Publish, and Retire actions. The final order is: 1 Overview, 2 Leave Types, 3 Applicability, 4 Eligibility (future/disabled), 5 Entitlement & Credit (future/disabled), 6 Request Rules (future/disabled), 7 Calendar & Sandwich (future/disabled), 8 Documents & Declaration (future/disabled), 9 Clubbing (future/disabled), 10 Cancellation & Special Rules (future/disabled), 11 Review & Publish, 12 Version History. Version History is preferably a separate read-only tab/page.

### F. Basic Details

Parent fields are Policy Code, Policy Name, Description, and Active. Version fields are server-assigned/displayed Version Number, Effective From, Effective To, Priority, and Status. The labels are human-friendly Policy and Policy Version; internal IDs remain hidden. Draft Save may be incomplete but rejects malformed dates, invalid tenant IDs, and impossible references. Publish requires the Phase 4B invariants.

Priority uses the implemented meaning: **higher number is evaluated first; specificity is the tie-breaker**. Help text explains that equal best priority and specificity across candidates is a configuration ambiguity, not an arbitrary choice.

### G. Leave Type selection

The version editor has searchable available/selected Leave Type tables showing code/name, paid status, and active state. Selecting a type creates one active `LeavePolicyRule`; duplicate active selections are rejected server-side. The screen does not show entitlement or request-rule columns until those backend entities exist.

### H. Applicability group UX

Render each `LeavePolicyApplicabilitySet` as a stacked card labelled **Group N**. Within a card, populated dimensions are visibly joined by **AND**. Cards are joined by a prominent **OR** separator. Provide Add Group, Delete Group, a compact summary, and a review preview such as `Holding Company HC01 / Department IT / Grade G5`.

Blank means unrestricted and is shown as `All`, `All Departments`, or equivalent; no fake master row is persisted. Zero groups means **all employees in this tenant**. The API persists stable IDs only and reports inactive or cross-tenant references as field errors.

### I. Cascading employment masters

Use searchable `MasterDropdown` controls and display `Code – Name`; persist IDs. The actual hierarchy is Holding Company → LOB → Organization → Department → Sub Department → Section → Sub Section, and Function → Sub Function. Grade, Designation, Employee Type, Country, Work Location, and Cost Center are independent selectors. There is no separate Location master in the current model; the UI must say so rather than fabricate one.

When a parent changes, clear invalid children and reload candidates. Initial hydration must not clear valid saved children while options are loading. Country is shared reference data in the current repository; tenant-scoped masters are resolved with tenant-safe APIs. A child response that violates the selected parent is a validation error.

### J. Priority, specificity, and preview

The editor explains that priority precedes specificity. A future **Test Policy Resolution** panel can accept Employee, Effective Date, and Leave Type and show Policy, Version, matched group, Priority, Specificity, No Policy, or Ambiguity. Put it in 4C.6 (or a separately approved 4C.1 increment), not as a publish prerequisite: publish cannot cheaply prove every cross-policy employee combination, and runtime resolution remains authoritative.

Specificity is the count of restrictive populated dimensions in the best matching group. If one version matches several groups, display the highest matching-group specificity. Never use GUID, creation order, policy name, database order, or FirstOrDefault as a hidden tie-breaker.

### K. Draft behavior

Drafts can be saved section by section and may have zero Leave Types or zero applicability groups. They may not contain malformed dates, duplicate active LeaveType selections, or references outside the tenant. Save Draft returns a revision/concurrency token. Failed saves leave client values intact. Published versions cannot be edited in place; Create New Version is the explicit path for changes.

### L. Publish validation and retire

Validate and Publish use the foundation invariants: active same-tenant parent; valid dates and priority; at least one active rule; all LeaveTypes and masters in scope; no duplicate active LeaveType rule; valid sets; and no overlapping published version in the same policy. Runtime ambiguity across independent policies remains possible and is surfaced by resolution.

Validation returns field errors plus a safe summary. Publish requires deliberate confirmation and a final revision token. Examples: “This version has no active Leave Types,” “Department IT is not under the selected Organization,” and “Published version overlaps v2 from 01-Jan-2027 to 31-Mar-2027.” Retire is not Delete: it prevents future selection while preserving historical configuration. Published rows are never physically removed.

### M. Version history UX

History is a read-only timeline/table showing version, effective range, status, priority, actor/timestamps when available, Leave Type count, and applicability count. Draft has Edit; Published has View/Create New Version; Retired has View. Cloning is recommended only after a transactional API exists; initially Create New Version may require explicit recreation rather than an implicit copy.

### N. Admin API contract

These are proposed contracts only and should use the standard response envelope, pagination, validation errors, authorization attributes, tenant context, and concurrency conventions.

* `GET /api/leave-types`, `GET /api/leave-types/{id}`, `POST /api/leave-types`, `PUT /api/leave-types/{id}`, `POST /api/leave-types/{id}/deactivate`.
* `GET /api/leave-periods`, `GET /api/leave-periods/{id}`, `POST /api/leave-periods`, `PUT /api/leave-periods/{id}`, and deactivate if the existing lifecycle pattern requires it.
* `GET /api/leave-policies`, `GET /api/leave-policies/{id}`, `POST /api/leave-policies`, `PUT /api/leave-policies/{id}` for stable identity.
* `GET /api/leave-policies/{policyId}/versions`, `GET /api/leave-policies/{policyId}/versions/{versionId}`, `POST /api/leave-policies/{policyId}/versions`, and `PATCH/PUT` for a Draft.
* `PUT /api/leave-policies/{policyId}/versions/{versionId}/leave-types` for draft selections; `GET/PUT .../applicability` for draft groups with a revision token.
* `POST .../validate`, `POST .../publish`, and `POST .../retire` as explicit lifecycle commands. No giant SavePolicyEverything endpoint.

All endpoints are tenant-scoped by server context. Return 400 for validation, 403 for permission denial, 404 for a tenant-invisible resource, 409 for concurrency/duplicate/overlap conflicts, and a typed ambiguity result where preview/resolution is exposed.

### O. DTO contracts

Do not expose EF entities. Representative shapes are:

```text
LeaveTypeListItem { id, code, name, description, defaultUnit, isPaid, isActive, updatedAt }
LeavePeriodDto { id, code, name, startDate, endDate, isActive, revision }
LeavePolicyListItem { id, code, name, isActive, currentPublishedVersion, effectiveFrom, effectiveTo, leaveTypeCount, applicabilitySetCount }
LeavePolicyVersionEditor {
  id, policy { id, code, name, description, isActive }, versionNumber,
  effectiveFrom, effectiveTo, priority, status, revision,
  leaveTypes: [{ id, code, name, isPaid, isActive }],
  applicabilitySets: [{ id, holdingCompanyId, lobId, organizationId, departmentId,
    subDepartmentId, sectionId, subSectionId, functionId, subFunctionId,
    gradeId, designationId, employeeTypeId, gender, countryId, workLocationId, costCenterId }],
  actions: { canEdit, canValidate, canPublish, canRetire, canCreateVersion }
}
```

Master references may include `{ id, code, name }` for display, but requests contain IDs and a revision. `leavePeriodId` remains absent until the Phase 4B period relationship decision is resolved.

### P. Configuration permissions

Propose `Leave.TypeManage`, `Leave.PeriodManage`, `Leave.PolicyView`, `Leave.PolicyManage`, and `Leave.PolicyPublish`. Separate Publish is recommended because drafting and activating have different operational risk. PolicyView is read-only; PolicyManage creates/edits Drafts; PolicyPublish validates/publishes/retires. No permissions or grants are added in this design task.

Route guards hide unavailable navigation, but APIs independently enforce tenant and permission checks. A PolicyView-only user sees read-only versions; a PolicyManage-only user cannot publish.

### Q. Concurrency UX

Every editor response carries the repository concurrency token. Update, applicability replacement, rule selection, publish, and retire require it. A stale write returns 409 with a friendly conflict; the UI preserves unsaved values and offers reload/compare, never automatic overwrite. Publish re-reads and validates in one application transaction. Server-side version-number allocation plus the composite unique constraint surfaces a conflict on a race.

### R. Error handling

400 renders the validation summary and field messages, focusing the first invalid control. 403 is a permission message, never an empty list. 404 is tenant-scoped unavailable. 409 distinguishes duplicate code, overlap, stale revision, and lifecycle conflict. Resolver ambiguity is an actionable configuration warning. Network/5xx errors use the existing error component and preserve Draft values. Stack traces, credentials, tokens, connection strings, and database details never reach the UI.

### S. Accessibility and responsiveness

Every control has a visible label or accessible name; groups use fieldsets/legends; AND/OR semantics are text, not color alone. Validation links focus to controls. Status badges include text. Confirmation dialogs trap focus, expose title/description, support Escape/cancel, and return focus. Applicability groups stack on tablet rather than becoming an unreadable horizontal matrix. Desktop is primary, with full keyboard operation for dropdowns, tabs, groups, and review.

### T. Performance and data loading

Policy lists are server-paginated and searchable. Master dropdowns use debounced server-side search, lazy loading, existing caching conventions, and parent IDs for cascading children. Do not load every employee/master into one page. Use one coherent version payload for rules/groups and targeted lookup calls for searchable masters; cancel superseded requests via existing query-hook behavior.

### U. Screenshot-to-new-UI requirement register

The 82 reference concepts are mapped below. Planned owners are future models, not permission to create them in 4C.0. Exclusion/inclusion concepts remain distinct requirements even where one future rule object will store them.

| # | Reference setting | New UI section | Planned owner | Phase / visibility |
|---:|---|---|---|---|
| 1 | Leave Type | Leave Types | LeaveType | 4C.2 |
| 2 | Policy Name | Overview | LeavePolicy | 4C.3 |
| 3 | Policy Code | Overview | LeavePolicy | 4C.3 |
| 4 | Description | Overview | LeavePolicy | 4C.3 |
| 5 | Policy/reference document | Documents & Declaration | future content | Future; hidden |
| 6 | Effective From | Overview | LeavePolicyVersion | 4C.3 |
| 7 | Effective To | Overview | LeavePolicyVersion | 4C.3 |
| 8 | Active status | Overview | LeavePolicy | 4C.3 |
| 9 | Draft status | Version & Publish | LeavePolicyVersion | 4C.3 |
| 10 | Published status | Version & Publish | LeavePolicyVersion | 4C.5 |
| 11 | Retired status | Version History | LeavePolicyVersion | 4C.5 |
| 12 | Publish/approval status | Review & Publish | lifecycle/workflow | 4C.5; simple publish |
| 13 | Gender | Applicability | applicability set | 4C.4 |
| 14 | Holding Company | Applicability | applicability set | 4C.4 |
| 15 | LOB | Applicability | applicability set | 4C.4 |
| 16 | Organization | Applicability | applicability set | 4C.4 |
| 17 | Department | Applicability | applicability set | 4C.4 |
| 18 | Sub Department | Applicability | applicability set | 4C.4 |
| 19 | Section | Applicability | applicability set | 4C.4 |
| 20 | Sub Section | Applicability | applicability set | 4C.4 |
| 21 | Function | Applicability | applicability set | 4C.4 |
| 22 | Sub Function | Applicability | applicability set | 4C.4 |
| 23 | Grade | Applicability | applicability set | 4C.4 |
| 24 | Designation | Applicability | applicability set | 4C.4 |
| 25 | Employee Type | Applicability | applicability set | 4C.4 |
| 26 | Country | Applicability | applicability set | 4C.4 |
| 27 | Location | Applicability | current model has none | Deferred; explain unavailable |
| 28 | Work Location | Applicability | applicability set | 4C.4 |
| 29 | Cost Center | Applicability | applicability set | 4C.4 |
| 30 | Eligible immediately after joining | Eligibility | future eligibility rule | Future; hidden |
| 31 | Eligible after X days | Eligibility | future eligibility rule | Future; hidden |
| 32 | Eligible after X months | Eligibility | future eligibility rule | Future; hidden |
| 33 | Confirmed employees only | Eligibility | future eligibility rule | Future; hidden |
| 34 | Probation allowed/not allowed | Eligibility | future eligibility rule | Future; hidden |
| 35 | Notice-period restriction | Eligibility | future eligibility rule | Future; hidden |
| 36 | Minimum service | Eligibility | future eligibility rule | Future; hidden |
| 37 | Employee status restriction | Eligibility | future eligibility rule | Future; hidden |
| 38 | ESIC applicability | Eligibility | future statutory rule | Future; hidden |
| 39 | Family-detail prerequisite | Eligibility | future eligibility rule | Future; hidden |
| 40 | Reporting manager applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 41 | L1 applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 42 | L2 applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 43 | L3 applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 44 | HR applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 45 | Time Manager applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 46 | Administrator applies on behalf | Apply on behalf | authorization/workflow | 4H; hidden |
| 47 | Full Day | Request Rules | future request rule | 4F; hidden |
| 48 | Half Day | Request Rules | future partial-day rule | 4F; hidden |
| 49 | Quarter Day | Request Rules | future partial-day rule | Future; hidden |
| 50 | Hourly leave | Request Rules | future partial-day rule | Future; hidden |
| 51 | Partial-day combinations | Request Rules | future partial-day rule | Future; hidden |
| 52 | Annual entitlement | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 53 | Monthly entitlement | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 54 | Quarterly entitlement | Entitlement & Credit | future entitlement rule | Future; hidden |
| 55 | Joining-date proration | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 56 | Exit-date proration | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 57 | Confirmation-based entitlement | Entitlement & Credit | future entitlement rule | Future; hidden |
| 58 | Rounding | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 59 | Maximum accumulation | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 60 | Negative balance | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 61 | Carry forward | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 62 | Carry-forward cap | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 63 | Carry-forward expiry | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 64 | Encashment eligibility | Entitlement & Credit | future entitlement rule | Future; hidden |
| 65 | Leave lapse | Entitlement & Credit | future entitlement rule | 4D; hidden |
| 66 | Sandwich not applicable | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 67 | Public holiday included in sandwich | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 68 | Weekly off included in sandwich | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 69 | Both included in sandwich | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 70 | Prefix holiday | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 71 | Suffix holiday | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 72 | Prefix week-off | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 73 | Suffix week-off | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 74 | Holiday/week-off between leave days | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 75 | Exclude public holidays normally | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 76 | Include public holidays normally | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 77 | Exclude weekly offs normally | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 78 | Include weekly offs normally | Calendar & Sandwich | future calendar rule | 4E; hidden |
| 79 | Minimum advance notice | Request Rules | future request rule | 4F; hidden |
| 80 | Future request horizon | Request Rules | future request rule | 4F; hidden |
| 81 | Backdated allowance and limit | Request Rules | future request rule | 4F; hidden |
| 82 | Duration-tier notice/emergency exception | Request Rules | future request rule | Future; hidden |

Attachments, declarations/instructions, clubbing, cancellation, previous-period transactions, planned/unplanned classification, auto-approval, delegation, and Leave Pool remain explicitly recorded in Phase 4A.1 as future specialized rules or open decisions. They are not lost and are not unsupported Phase 4B fields or Phase 4C endpoints.

### V. Conceptual walkthrough

An administrator creates **India Corporate Leave Policy** (code `IND-CORP`, active), then version 3 effective 01-Jan-2027 onward with priority 10. Overview distinguishes parent identity from version metadata. Leave Types selects Casual, Sick, and Earned Leave. Applicability adds one group: Holding Company `HC01`, Country `India`, Employee Type `Permanent`; blanks display as All. Save Draft is allowed while incomplete.

Validate checks dates, active types, tenant-safe masters, and overlaps. Review & Publish displays 3 Leave Types, 1 group, priority 10, and warns that cross-policy ambiguity remains a runtime concern. After explicit confirmation and a current revision token, Publish changes the version to Published. The list shows it read-only; future changes use Create New Version. No seed data is created.

### W. Phase 4C implementation breakdown

* **4C.1** server DTOs, query/command contracts, controllers, tenant/permission guards, validation, and concurrency envelopes.
* **4C.2** Leave Type and Leave Period API consumption plus list/edit/deactivate pages.
* **4C.3** Policy list, parent identity, Draft version creation, Overview, and revision-safe Save Draft.
* **4C.4** Leave Type selection, applicability groups, master search/cascades, and AND/OR summaries.
* **4C.5** Validate, Publish, Retire, confirmation/review, and version history.
* **4C.6** optional resolution preview after restore/build/migration prerequisites pass and its response is approved.

### X. Coding prerequisites and Phase 4B blocker

Phase 4C coding prerequisites remain: backend restore must succeed; Domain/Application/Infrastructure/API/tests must compile; focused Leave tests must pass; and `LeavePolicyFoundation` must be generated and reviewed but not applied until separately authorized. The current Phase 4B restore/build blocker remains documented separately; this task does not retry it. Phase 4C.0 performs no restore, build, tests, migration, startup, or database operation.

### Y. Open UI/business decisions

Resolve before implementation: canonical code normalization case; whether LeavePolicyVersion explicitly references LeavePeriod; whether cloning copies LeaveType selections/applicability; exact PolicyPublish ownership; reference-document representation; whether later publish approval is needed; planned/unplanned semantics; emergency exceptions; delegation; Leave Pool; statutory/family/ESIC rules; and whether resolution preview is required or advisory. None should be guessed in code.

## Phase 4C.0.2 — Detailed Leave Policy Rule Specification

This is specification-only. It refines the screenshot-derived requirements without adding code, entities, migrations, APIs, UI, or Phase 4B changes.

### A. Rule aggregate design

`LeavePolicyRule` remains the per-LeaveType anchor inside a `LeavePolicyVersion`. Detailed behavior belongs to that rule because Casual, Sick, Earned, Maternity, Comp Off, and Unpaid Leave can differ within one version. No rule inheritance graph is introduced; each resolved rule contains the complete effective configuration for its LeaveType.

| Component | Cardinality | Responsibility |
|---|---:|---|
| `LeavePolicyEligibilityRule` | 0..1 per policy rule | service, probation, status, notice-period eligibility |
| `LeavePolicyEntitlementRule` | 0..1 per policy rule | source, accrual, proration, rounding, balance limits |
| `LeavePolicyRequestRule` | 0..1 per policy rule | quantity, dates, partial day, notice, count/quantity limits |
| `LeavePolicyCalendarRule` | 0..1 per policy rule | holiday/week-off baseline and sandwich settings |
| `LeavePolicyAttachmentRule` | 0..1 per policy rule | requirement and threshold metadata |
| `LeavePolicyClubbingRule` | 0..many | normalized LeaveType relationships |
| `LeavePolicyCancellationRule` | 0..1 per policy rule | withdraw, cancel, modify, and cutoffs |
| `LeavePolicyContent` | 0..many typed records | instructions, declaration, terms, references |
| `LeavePolicyNoticeBand` | 0..many under request rule | duration-dependent advance notice |

These are specialized parts of one versioned aggregate, not one-checkbox tables. Value-object storage is acceptable for inseparable settings, but the API and persistence contracts retain these cardinalities and the immutable Published boundary.

### B. Eligibility

`LeavePolicyEligibilityRule` answers when an applicability-matched employee may use the LeaveType. It may contain `EligibilityMode` (Immediate or MinimumService), service value/unit, `ProbationMode`, `NoticePeriodMode`, permitted employee statuses, optional ESIC applicability, family-condition dependency, and legally approved exceptions. Employee Type, department, grade, country, work location, and similar “who” conditions remain on `LeavePolicyApplicabilitySet`; they are not duplicated as eligibility flags. Gender normally remains applicability.

### C. Service eligibility

The source is the authoritative joining date from effective employment, never account creation, Employee Code, or a policy-stored calculated date. `MinimumServiceUnit` is Days or Months. The conservative boundary is inclusive: eligibility begins on the date the required service completes. Days use date arithmetic; Months use calendar-month arithmetic with an explicitly documented end-of-month rule. Group DOJ and organization DOJ are not used unless the employment model later makes one authoritative. Rehire semantics are deferred because supported rehire identity is not established.

### D. Probation and confirmation

Use `ProbationMode`: Allowed, NotAllowed, or AfterConfirmation. Different entitlement after confirmation is an entitlement/versioning concern, not another eligibility flag. The repository does not establish an authoritative confirmation date/status; AfterConfirmation must therefore fail safely as unavailable until that dependency is resolved, never infer confirmation from account state.

### E. Notice period

Use `NoticePeriodMode`: Allowed, NotAllowed, or AllowedWithApproval. The final value expresses eligibility only. Approval routing remains in the future workflow and is not duplicated here.

### F. Entitlement

`LeavePolicyEntitlementRule` is optional. It contains `EntitlementMode`, quantity, `EntitlementPeriod`, `AccrualFrequency`, `AccrualTiming`, join/exit proration, rounding, accumulation, negative-balance, carry-forward, lapse, encashment, and source settings. `EntitlementPeriod` is the resolved tenant `LeavePeriod`, never a hard-coded calendar year. Future quantity storage should use decimal days (recommended SQL precision `decimal(9,3)`).

`EntitlementMode` is Allocated, Unlimited, or NoBalanceRequired. Allocated participates in the later ledger; Unlimited has no finite allocation; NoBalanceRequired is appropriate for Unpaid Leave. This prevents unpaid leave from being modeled as unlimited negative paid balance.

### G. Accrual and timing

`AccrualFrequency` is None, Upfront, Monthly, Quarterly, SemiAnnual, or Annual. None means no scheduled credit and is valid for external grants or NoBalanceRequired. Upfront credits at the period/eligibility start chosen by the later engine; other values define cadence only. `AccrualTiming` is StartOfPeriod or EndOfPeriod and is visible only when frequency is scheduled; it is null/not applicable for None. Scheduling is Phase 4D, not this specification.

### H. Proration

Use `ProrationMode`: None, RemainingDays, RemainingMonths, or CompletedMonths. Initial implementation should support None plus one approved proportional method; statutory formulas remain deferred. Join proration uses LeavePeriod start and joining date; exit proration uses employment end and LeavePeriod end. Only the method is configured; calculated quantities are runtime values.

### I. Rounding

Use `RoundingMode`: None, Down, Up, or Nearest, with an optional positive `RoundingIncrement`, initially 0.5 day where half-day support is enabled. For example, 7.42 rounded Nearest 0.5 is 7.5. Zero/negative and unsupported increments are invalid.

### J. Carry-forward and negative balance

Use `CarryForwardMode`: Disabled, Capped, or Unlimited. `CarryForwardLimit` means the maximum quantity transferred from the immediately preceding resolved LeavePeriod, not total balance. `CarryForwardExpiryMode` is None, AtPeriodEnd, or AfterDays; AfterDays requires a positive relative number.

Use `NegativeBalanceMode`: NotAllowed, AllowedUnlimited, or AllowedUpToQuantity. The last requires a positive `MaximumNegativeQuantity`. Negative balance must not override NoBalanceRequired.

### K. Request rules

`LeavePolicyRequestRule` contains minimum, maximum, and maximum-consecutive requested quantity; advance/backdate modes; separate request-count and quantity limits; `PartialDayMode`; and backend-derived planned/unplanned classification. In the initial Day system all quantities are decimal days. `PartialDayMode` is FullDayOnly or HalfDayAllowed. Quarter-day and hourly modes are deferred and rejected by the initial API.

Request count and total quantity are separate: maximum requests per month/LeavePeriod limits submissions; maximum quantity per month/LeavePeriod limits days. Null means unlimited. These do not replace balance checks.

### L. Notice bands

The safe initial rule is `MinimumAdvanceNoticeDays`. If duration bands are confirmed as required, use zero-or-many `LeavePolicyNoticeBand` records with `FromQuantity`, nullable `ToQuantity`, and `RequiredAdvanceDays`. Bands must be positive, ordered, non-overlapping, and have at most one final open-ended range. A banded rule replaces simple minimum notice for covered quantities; it is not accidentally combined with it.

### M. Backdated requests and planned/unplanned

Use `BackdatedRequestMode`: NotAllowed, Allowed, or AllowedUpToDays. The third requires a positive `MaximumBackdatedDays`; Allowed is explicitly unlimited. The backend derives Planned when advance requirements are met and Unplanned when an approved emergency/backdate exception applies. Employees do not manually choose the classification in the initial design.

### N. Calendar treatment

`LeavePolicyCalendarRule` separates baseline counting from sandwich behavior. `HolidayTreatment` is Exclude or Include; `WeekOffTreatment` is Exclude or Include. These answer whether holidays/week-offs inside a leave span normally consume leave. They do not encode prefix, suffix, or intervening sandwich logic.

### O. Sandwich

Use `SandwichMode`: Disabled, Holiday, WeekOff, or HolidayAndWeekOff, plus positions `BetweenLeaveDays`, `Prefix`, and `Suffix`. Friday/Monday leave can include a weekend only when WeekOff and BetweenLeaveDays are enabled. A holiday before Tuesday leave remains excluded when Prefix is disabled. Exact adjacency across separate requests, holidays, and week-offs must be approved before Phase 4E; no calculation engine is specified here.

### P. Attachments

`LeavePolicyAttachmentRule` is 0..1 with `AttachmentRequirement`: None, Optional, Required, or RequiredAboveQuantity. It may include ThresholdQuantity, DocumentLabel, and a future allowed category. RequiredAboveQuantity requires a positive threshold. Phase 4C configuration stores metadata only; it stores no files. Submission-time enforcement is the safe initial default. Delayed medical evidence is deferred pending storage/retention design.

### Q. Content and declaration

Use typed `LeavePolicyContent` records rather than one wide text row: Instruction, Declaration, Terms, and ReferenceDocument. Records are scoped to the version/rule, have display order and active state, and contain text or sanitized reference metadata. A future LeaveRequest must preserve the exact declaration content/version and acknowledgement time; it must not depend on current text. Upload, rich-text sanitization, and retention are deferred.

### R. Clubbing

Use 0..many normalized `LeavePolicyClubbingRule` relationships scoped to a version/type context, with OtherLeaveTypeId and restriction relation. The default NotAllowed relation is symmetric. Canonical pair ordering prevents A-B/B-A duplicates. Directional behavior is deferred. Clubbing means separate requests whose ranges are immediately adjacent; whether excluded holidays/week-offs preserve adjacency requires a later calendar decision. A single continuous request is not clubbing.

### S. Cancellation and modification

Keep `Withdrawal` (employee retracts before final approval), `Cancellation` (reverses approved leave), and `Modification` (new revision or cancel-and-replace) distinct. `CancellationMode` is NotAllowed, BeforeLeaveStart, UpToDaysBeforeStart, or AfterApprovalWithWorkflow. Approval stages are not embedded. Approved historical requests are never silently edited.

### T. Apply-on-behalf, approval, delegation, and Leave Pool

Apply-on-behalf belongs to permissions, employee scope, and the future request service; do not add ManagerCanApply/HRCanApply/AdminCanApply flags. Auto-approval remains deferred to workflow. Actual delegation remains deferred; a future DelegationRequired hint is possible only if request submission needs it. Leave Pool remains an open business term with no schema until shared-quota/donation semantics are clarified.

### U. Unpaid Leave

Unpaid Leave uses `EntitlementMode=NoBalanceRequired`, no normal accrual, and still has request/notice/workflow rules. Availability after paid balance exhaustion is a separate request/workflow decision. It is not negative paid balance.

### V. Comp Off

Comp Off may use `EntitlementSource`: PolicyAccrual, ExternalGrant, or NoBalanceRequired. ExternalGrant is the preferred initial model; issuance and expiry are later controlled services. Attendance/extra-work integration is deferred and not forced into annual entitlement.

### W. Statutory leave and inheritance

Maternity, Paternity, and other statutory types use typed configuration plus approved applicability/eligibility. No country’s law is a global constant; statutory packs and legal formulas are future work. There is no Base Policy → Child Policy override graph. Each resolved rule is explicit and historically explainable.

### X. Validation matrix

The detailed configuration requires at least these 24 conditional validations: (1) dates ordered; (2) Draft identity valid; (3) parent active for Publish; (4) at least one active rule for Publish; (5) active same-tenant LeaveType; (6) unique active rule; (7) no same-policy Published overlap; (8) valid priority; (9) same-tenant active applicability masters; (10) hierarchy parent/child consistency; (11) positive service value/unit; (12) confirmation source available for AfterConfirmation; (13) positive allocated entitlement; (14) no accrual timing when frequency None; (15) proration compatible with entitlement mode; (16) positive supported rounding increment; (17) carry-forward limit/expiry compatible with mode; (18) negative limit required and positive when bounded; (19) request min/max/consecutive positive and ordered; (20) half-day increments valid and quarter/hour rejected initially; (21) backdate limit required and positive when bounded; (22) notice bands ordered/non-overlapping; (23) attachment threshold required and positive when conditional; (24) clubbing pair distinct, canonical, same-tenant, and unique.

### Y. Conditional UI matrix

| Field | Owner | Default/required | Conditional behavior | Phase |
|---|---|---|---|---|
| Eligibility mode; service value/unit | EligibilityRule | Immediate; service required for MinimumService | show value/unit together | 4C.7 |
| Probation/notice-period mode | EligibilityRule | Allowed | future Eligibility section | 4C.7 |
| Entitlement mode; quantity/period | EntitlementRule | no rule until added; quantity required for Allocated | period is LeavePeriod | 4C.8 |
| Accrual frequency/timing | EntitlementRule | None | timing only when scheduled | 4C.8 |
| Proration; rounding/increment | EntitlementRule | None | increment only when rounding enabled | 4C.8 |
| Carry-forward/limit/expiry | EntitlementRule | Disabled | limit/expiry conditional | 4C.8 |
| Negative mode/limit | EntitlementRule | NotAllowed | limit only when bounded | 4C.8 |
| Request quantities/partial-day | RequestRule | optional; FullDayOnly | half-day only initially | 4C.9 |
| Notice/backdate/count/quantity limits | RequestRule | no notice; NotAllowed; unlimited | bounded values become visible | 4C.9 |
| Holiday/week-off/sandwich | CalendarRule | Exclude/Exclude/Disabled | positions when sandwich enabled | 4C.10 |
| Attachment/threshold | AttachmentRule | None | threshold only above quantity | 4C.10 |
| Content | Content | none | future Documents section | Future |
| Clubbing pair | ClubbingRule | none | future Clubbing section | 4C.10 |
| Cancellation/cutoff | CancellationRule | NotAllowed | cutoff only for cutoff modes | 4C.10 |

### Z. Safe defaults

Recommended new-Draft defaults are: Immediate eligibility; no entitlement rule until deliberately added; Accrual None; no proration/rounding; CarryForward Disabled; NegativeBalance NotAllowed; FullDayOnly; no minimum notice unless configured; Backdated NotAllowed; Holiday/WeekOff Exclude; Sandwich Disabled; Attachment None; Cancellation NotAllowed. These avoid silently granting permissive behavior.

### AA. Five policy walkthroughs

* **Casual Leave:** 12 days per resolved LeavePeriod, Full/Half Day, minimum 0.5, maximum 3 consecutive, one-day notice, no backdating, no sandwich, holidays/week-offs excluded, no attachment, no negative balance. Values are split across Entitlement, Request, and Calendar rules.
* **Sick Leave:** 12 days, HalfDayAllowed, no mandatory advance notice, backdate up to 3 days, and RequiredAboveQuantity attachment above 2 days. Delayed upload is deferred.
* **Maternity:** approved special applicability/eligibility, versioned entitlement, and mandatory document metadata; statutory law/formula remains a future country/legal pack.
* **Comp Off:** ExternalGrant source with later grant/expiry service; no Attendance integration in this phase.
* **Unpaid Leave:** NoBalanceRequired, no accrual, request constraints still apply, and approval is future workflow; it is not negative paid balance.

### AB. Final relationships and enum catalogue

```text
LeavePolicyVersion
  └─ LeavePolicyRule (one per active LeaveType)
       ├─ 0..1 EligibilityRule
       ├─ 0..1 EntitlementRule
       ├─ 0..1 RequestRule ── 0..* NoticeBands
       ├─ 0..1 CalendarRule
       ├─ 0..1 AttachmentRule
       ├─ 0..* typed Content records
       ├─ 0..* Clubbing relationships
       └─ 0..1 CancellationRule
```

Recommended stable enums are: `EligibilityMode` (Immediate, MinimumService); `ServiceUnit` (Days, Months); `ProbationMode` (Allowed, NotAllowed, AfterConfirmation); `NoticePeriodMode` (Allowed, NotAllowed, AllowedWithApproval); `EntitlementMode` (Allocated, Unlimited, NoBalanceRequired); `EntitlementSource` (PolicyAccrual, ExternalGrant, NoBalanceRequired); `AccrualFrequency` (None, Upfront, Monthly, Quarterly, SemiAnnual, Annual); `AccrualTiming` (StartOfPeriod, EndOfPeriod); `ProrationMode` (None, RemainingDays, RemainingMonths, CompletedMonths); `RoundingMode` (None, Down, Up, Nearest); `CarryForwardMode` (Disabled, Capped, Unlimited); `CarryForwardExpiryMode` (None, AtPeriodEnd, AfterDays); `NegativeBalanceMode` (NotAllowed, AllowedUnlimited, AllowedUpToQuantity); `PartialDayMode` (FullDayOnly, HalfDayAllowed); `BackdatedRequestMode` (NotAllowed, Allowed, AllowedUpToDays); `HolidayTreatment`/`WeekOffTreatment` (Exclude, Include); `SandwichMode` (Disabled, Holiday, WeekOff, HolidayAndWeekOff); `AttachmentRequirement` (None, Optional, Required, RequiredAboveQuantity); `ContentKind` (Instruction, Declaration, Terms, ReferenceDocument); `CancellationMode` (NotAllowed, BeforeLeaveStart, UpToDaysBeforeStart, AfterApprovalWithWorkflow); and `ClubbingRelation` (NotAllowed, with Allowed only if later needed). These are bounded behavior, not admin master data.

### AC. Phase ownership and implementation order

Configuration/API/UI ownership is separate from execution: 4C.7 Eligibility; 4C.8 Entitlement; 4C.9 Request rules, notice bands, and partial-day; 4C.10 Calendar, attachment, clubbing, and cancellation; 4D ledger/accrual/carry-forward/negative balance; 4E day and sandwich calculation; 4F Apply Leave/request runtime; 4H approval/apply-on-behalf/auto-approval; 4J cancellation/modification. Statutory packs, Attendance/Payroll, delegation, Leave Pool, and quarter/hour/shift support remain future.

After environment recovery, implement in this order: 4C.1 foundation API/DTOs/permissions/concurrency; 4C.2 LeaveType/Period UI; 4C.3 policy/version editor; 4C.4 type selection/applicability; 4C.5 lifecycle/history; then 4C.7 eligibility, 4C.8 entitlement, 4C.9 request rules, and 4C.10 remaining configuration. Runtime phases follow only after their contracts are approved.

### AD. Open decisions and coding prerequisites

Open decisions requiring approval are: service-month end-of-month rule; authoritative confirmation source; statutory gender/country/ESIC/family rules; accrual/proration/rounding formulas; carry-forward consumption and expiry; encashment/lapse; emergency notice exceptions; band requirement; attachment timing/storage/retention; declaration legal retention; clubbing adjacency across holidays; cancellation after start/end; manager-change routing; auto-approval; delegation; Leave Pool meaning; Comp Off grant authority/expiry; paid/unpaid Payroll handoff; and quarter/hour/shift support.

Phase 4C.1 remains gated on successful Phase 4B restore, compilation of Domain/Application/Infrastructure/API/tests, passing focused Leave tests, and generation plus static review of `LeavePolicyFoundation` without applying it. This task did not attempt that blocker.

## Phase 4B.2 — Restore Diagnosis Status

This bounded restore-diagnosis attempt did not recover backend validation. The solution input is valid: `HRMS.slnx` exists, lists the five expected backend projects, and `dotnet sln HRMS.slnx list` exited 0. The installed SDK is .NET SDK 10.0.400 with .NET/AspNetCore runtime 10.0.11.

One diagnostic solution restore was captured in `.phase4b-restore-diagnostic/` with exit code 1, a console log, and an MSBuild binary log. The solution-level failure occurred in `_FilterRestoreGraphProjectInputItems` with a `Build FAILED` summary reporting zero errors. The diagnostic log also records missing SDK workload resolver directories under the installed SDK (`Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` and `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`).

The authorized project-isolation sequence then restored Domain, Application, and Infrastructure successfully. Application produced NU1900 because vulnerability data could not be loaded from the configured NuGet source; this remained a warning and did not fail restore. API was the first failing project: `Backend/HRMS.API/HRMS.API.csproj`, exit code 1. Its captured 790-byte log contains only “Determining projects to restore” followed by `Build FAILED`, with zero warnings and zero errors. No Tests restore was attempted after the first failure. API’s declared project references resolve to the existing Infrastructure and Application projects, and its target framework is `net10.0`; no repository-local ProjectReference defect was established.

Classification: **PHASE 4B ENVIRONMENT-BLOCKED — DO NOT START PHASE 4C.1**. The actionable evidence is limited to an SDK/MSBuild workload-resolver/intermediate-state failure and a silent API restore failure. No narrow repository-local corrective action was justified, so no source or project file was changed. No build, test, migration generation, API startup, or database operation was performed in this attempt.

### Z. Detailed-rule recommendation

Proceed with zero-or-one specialized rule records under each LeavePolicyRule, typed content and notice-band children only where one-to-many is real, normalized symmetric clubbing relationships, and explicit value objects/enums for bounded behavior. Keep Published versions immutable and keep all runtime engines, balances, requests, approvals, and integrations outside this specification.

Proceed only with Phase 4C.1 after Phase 4B restore/build/test/migration prerequisites are healthy. Implement a narrow DTO-based, tenant-safe configuration API and section-based UI for Leave Types, Leave Periods, policy identity, Draft versions, LeaveType selections, typed applicability, validation, lifecycle, and history. Keep future rule entities and Apply Leave out of the release. Preserve per-LeaveType resolution, inclusive effective dates, empty-applicability tenant-wide semantics, higher-priority-first ordering, highest matching-set specificity, and explicit ambiguity results.

## Phase 4C.10D Implementation Status

Phase 4C.10D adds typed `LeavePolicyCancellationRule` configuration owned by `LeavePolicyRule` with zero-or-one cardinality. The frozen fields implemented are `WithdrawAllowed`, `CancelAllowed`, and `ModifyAllowed`, all defaulting to false. No timing/cutoff fields were added because cancellation timing, approval-state dependencies, modification semantics, and workflow routing remain unresolved.

The nested Policy/Version/LeaveType GET and PUT endpoints require `Leave.PolicyView` and `Leave.PolicyManage`. Draft writes are tenant-safe, concurrency-protected, and lifecycle-enforced; Published and Retired configuration is immutable and readable. Cancellation validation is included in Draft validation and strict Publish validation, with the safe no-row baseline remaining valid.

The Policy editor includes a per-LeaveType Request Changes & Cancellation section with separate configuration controls for Withdraw, Cancel, and Modify. It contains no employee request action buttons and makes no claims about approval reversal, balance re-credit, Attendance, Payroll, or runtime behavior. Historical Published/Retired configuration is read-only.

Migration `20260904140000_LeavePolicyCancellationRules` creates only `LeavePolicyCancellationRules`, with typed boolean fields, tenant-aware one-to-one ownership, unique `(TenantId, LeavePolicyRuleId)`, and Restrict deletes. Previous Leave migrations were not modified; no migration was applied and no database was accessed. Runtime LeaveRequest, approval, balance, Attendance, Payroll, and cancellation execution remain deferred.

## Phase 4C.10C Implementation Status

Phase 4C.10C adds normalized, symmetric `LeavePolicyClubbingRule` pairs scoped to a Policy Version and referencing two participating `LeavePolicyRule` rows. The only frozen relation is `ClubbingRelation.NotAllowed`; absence of rows retains the documented default behavior. Pair members must be distinct, selected in the same active version, same tenant, and are normalized by stable rule IDs so `(A,B)` and `(B,A)` are one logical relationship. Runtime adjacency, holiday/week-off bridging, chains, overrides, and request evaluation remain deferred.

The version-level GET/PUT Clubbing endpoints require `Leave.PolicyView` and `Leave.PolicyManage`. Draft replacement is atomic at the aggregate save boundary, validates self-pairs and duplicate/reverse pairs, and uses the existing version concurrency token. Published/Retired configuration is immutable and remains readable. Draft Publish validation rejects malformed persisted pairs without implementing runtime adjacency calculation.

The Policy editor includes a version-oriented Leave Clubbing section with selected Leave Type pair selectors, symmetric explanation, add/remove controls, duplicate prevention support, scoped errors, and read-only historical display. No Cancellation, LeaveRequest, LeaveBalance, Approval, or adjacency evaluator was added.

Migration `20260904133000_LeavePolicyClubbingRules` creates only `LeavePolicyClubbingRules`, with TenantId, version ownership, two tenant-aware rule foreign keys, typed relation, normalized-pair uniqueness, and Restrict deletes. Previous Leave migrations were not modified; no migration was applied and no database was accessed. Codex backend Tests/API/Infrastructure builds may remain limited by silent ResolvePackageAssets execution behavior; frontend typecheck is the relevant static check, while Vitest/Vite may remain blocked by Codex `spawn EPERM`. Phase 4C.10D Cancellation remains deferred.

## Phase 4C.10B Implementation Status

Phase 4C.10B adds typed `LeavePolicyAttachmentRule` configuration, owned by `LeavePolicyRule` with zero-or-one cardinality. Implemented fields are `AttachmentRequirement` (`None`, `Optional`, `Required`, `RequiredAboveQuantity`), optional positive `ThresholdQuantity` using decimal(9,3), and optional `DocumentLabel`. Threshold values are active only for `RequiredAboveQuantity`; other modes clear threshold values. Absence of a row and `None` are the safe baseline.

The nested Policy/Version/LeaveType GET and PUT endpoints require `Leave.PolicyView` and `Leave.PolicyManage`. Writes are Draft-only, tenant/ownership checked, concurrency protected, and Published/Retired configuration is immutable. Attachment validation is integrated into Validate Draft and strict Publish validation. The editor provides a per-LeaveType Attachment & Declaration section with conditional threshold controls, historical read-only presentation, and scoped conflict/error handling.

Declaration text/content was not implemented: the design’s content retention, legal preservation, and request-time acknowledgement semantics remain unresolved, so no duplicate content fields or `LeavePolicyContent` storage were invented. File upload/storage, document metadata, retention, accepted file types, runtime document validation, LeaveRequestAttachment, LeaveRequest, balances, Clubbing, and Cancellation remain deferred.

Migration `20260904130000_LeavePolicyAttachmentRules` creates only `LeavePolicyAttachmentRules`, with typed enum/string/decimal columns, tenant-aware one-to-one ownership, unique `(TenantId, LeavePolicyRuleId)`, and Restrict delete behavior. Previous Leave migrations were not modified; no migration was applied and no database was accessed. Domain/Application static builds remain the available successful checks; Codex Infrastructure/Tests/API may reproduce the known silent ResolvePackageAssets limitation. Frontend typecheck was rerun after the feature; focused Vitest and Vite build execution remain subject to the known Codex `spawn EPERM` environment limitation.

## Phase 4C.10A Implementation Status

Phase 4C.10A adds the typed per-LeaveType `LeavePolicyCalendarRule`, owned by `LeavePolicyRule` with zero-or-one cardinality. Frozen fields are `HolidayTreatment` (`Exclude`/`Include`), `WeekOffTreatment` (`Exclude`/`Include`), `SandwichMode` (`Disabled`, `Holiday`, `WeekOff`, `HolidayAndWeekOff`), and the frozen position flags `ApplyToPrefix`, `ApplyToSuffix`, and `ApplyToBetween`. Defaults remain Exclude/Exclude/Disabled with all positions false. The normal holiday/week-off treatment is intentionally separate from sandwich configuration.

The nested Policy/Version/LeaveType GET and PUT endpoints require `Leave.PolicyView` and `Leave.PolicyManage`. Writes are Draft-only, tenant/ownership checked, typed, concurrency protected, and immutable for Published/Retired versions. Calendar validation is included in Validate Draft and strict Publish validation. Disabled sandwich clears position flags; Holiday/WeekOff modes clear irrelevant position flags, while Both permits both categories. No runtime date traversal or consumption calculation is implemented.

The Policy editor now contains a per-LeaveType Calendar & Sandwich section with separate normal counting controls, conditional sandwich mode/position controls, section-specific saving, scoped errors, and historical read-only presentation. No holiday/weekly-off master, attendance integration, Holiday Calendar, Sandwich calculation engine, attachment, clubbing, cancellation, LeaveRequest, or balance functionality was added.

Migration `20260904123000_LeavePolicyCalendarRules` creates only `LeavePolicyCalendarRules`, with typed enum/boolean columns, tenant-aware one-to-one ownership, a unique `(TenantId, LeavePolicyRuleId)` index, and Restrict delete behavior. Previous Leave migrations were not modified; no migration was applied and no database was accessed. Domain/Application static builds passed. Codex Infrastructure/Tests/API builds reproduced the known silent ResolvePackageAssets limitation with no compiler diagnostics. Frontend typecheck passed; lint retained existing non-fatal warnings; focused Vitest and Vite build execution remained blocked by Codex `spawn EPERM`. Phase 4C.10B Attachments and all later runtime phases remain deferred.

## Phase 4C.9 Implementation Status

Phase 4C.9 adds the typed, per-LeaveType `LeavePolicyRequestRule` configuration owned by `LeavePolicyRule` with zero-or-one cardinality. The frozen implementation covers decimal minimum, maximum, and maximum-consecutive request quantities; calendar-day minimum advance notice; `BackdatedRequestMode` (`NotAllowed`, `Allowed`, and bounded `AllowedUpToDays`); independent maximum request-count and maximum quantity limits scoped to `Month` or the independently resolved `LeavePeriod`; and `PartialDayMode` (`FullDayOnly` or `HalfDayAllowed`). Absence of a row is the safe baseline: no request restriction, no backdating, no notice, and full-day-only behavior.

The nested Policy/Version/LeaveType GET and PUT endpoints require `Leave.PolicyView` and `Leave.PolicyManage` respectively. Ownership, tenant query filters, Draft-only writes, immutable Published/Retired history, typed validation, concurrency tokens, and strict Validate Draft/Publish integration follow the existing Eligibility and Entitlement patterns. The editor has a per-LeaveType Request Rules section with conditional controls and a section-specific save operation; historical versions are read-only and errors are scoped to the section.

Notice bands remain deferred. Although the design describes a possible `LeavePolicyNoticeBand` shape, whether bands are required and their final precedence/emergency semantics remain open; no NoticeBand table or JSON rule storage was introduced. Planned/unplanned classification remains backend-derived. Quarter-day, hourly, shift, emergency bypass, working-day/cutoff semantics, workflow/routing, calendar/sandwich, attachment, cancellation, runtime requests, and balances remain deferred to their approved phases.

The new migration `20260904120000_LeavePolicyRequestRules` creates only `LeavePolicyRequestRules`, with typed columns, decimal(9,3) quantity precision, tenant-aware one-to-one ownership, a unique `(TenantId, LeavePolicyRuleId)` index, and restrictive delete behavior. Previous Leave migrations were not modified and no migration was applied or database accessed. Domain/Application static builds passed; Codex Tests/API build execution reproduced the known silent ResolvePackageAssets environment limitation with no compiler diagnostics. Frontend typecheck passed; lint retains existing non-fatal warnings. No package, NuGet, TLS, or system configuration was changed. Request Rule focused runtime validation remains subject to the normal PowerShell environment where Codex execution is limited.

Previous status before Phase 4B.4: **Partially complete — environment-blocked validation**. That execution-environment blocker is superseded by the final validation recorded below; the HRMS.Tests restore issue was not retried or investigated further in Phase 4B.4.

## Phase 4B.4 — Production Backend and Migration Validation

### Implementation

The Phase 4B Leave Policy Foundation remains within the approved boundary: `LeaveType`, `LeavePeriod`, `LeavePolicy`, `LeavePolicyVersion`, `LeavePolicyRule`, `LeavePolicyApplicabilitySet`, the foundation services, and deterministic resolution only. No Phase 4C detailed rule entities, balances, requests, approvals, calendars, UI, seed data, or runtime Leave operations were added.

Two narrow validation corrections were required: the three configuration mappings for entities without a `Tenant` navigation were corrected to retain their tenant-aware composite relationships, and the focused test fixture received the correct `HRMS.Infrastructure.Persistence.HrmsDbContext` reference/imports. Additional focused tests cover the remaining Phase 4B invariants without adding Phase 4C scenarios.

### Production build and migration validation

The user manually supplied successful restores for `HRMS.API.csproj` and `HRMS.Tests.csproj` using already-restored assets. No `dotnet restore` command was retried during this validation. The five `project.assets.json` files and generated NuGet props/targets were verified present.

All production projects and the test project built with `--no-restore`; the workload resolver property was passed only command-scoped where used. Domain completed with 0 warnings and 0 errors. Application completed with 2 warnings (`NU1900` and pre-existing `CA2024`) and 0 errors. Infrastructure, Tests, and API each completed with 2 propagated `NU1900` warnings and 0 errors.

Focused Leave tests passed 11/11. Backend regression passed 534 of 546 tests, with 0 failures and 12 skips; the skips are existing SQL Server acceptance cases without `HRMS_SQLSERVER_TEST_SERVER`.

Exactly one migration was generated: `20260904090754_LeavePolicyFoundation.cs` and its `20260904090754_LeavePolicyFoundation.Designer.cs`; `HrmsDbContextModelSnapshot.cs` was updated. The migration adds only `LeaveTypes`, `LeavePeriods`, `LeavePolicies`, `LeavePolicyVersions`, `LeavePolicyApplicabilitySets`, and `LeavePolicyRules`. It contains tenant-scoped uniqueness for type/period/policy codes, version numbers within tenant and policy, and rules within version and LeaveType; tenant-aware composite foreign keys; the existing global Country relationship; date-only mappings for period/version effective dates; integer enum persistence; and Restrict delete behavior. No pre-existing table is altered in `Up`; the only `DropTable` operations are the normal `Down` rollback for the six new tables. There are no destructive alters, renames, SQL/data mutations, operational seeds, balances, requests, or approval tables. Static classification: **SAFE FOUNDATION MIGRATION**.

The database was not accessed, the migration was not applied, and the API was not started. `MSBuildEnableWorkloadResolver=false` was used only as a local command/process-scoped environment workaround; it is not application, project, repository, or Leave configuration. `NU1900` remains an external vulnerability-feed connectivity warning; auditing, NuGet configuration, package sources, and TLS settings were not changed.

### Automated test validation

Automated test execution is now successful in this environment: focused Leave tests 11/11 passed and the backend regression is 534 passed, 0 failed, 12 skipped out of 546. The earlier HRMS.Tests restore issue was execution-environment-specific and was resolved by the user's normal-PowerShell restore; no additional restore troubleshooting was performed here.

Phase 4B production foundation validation is complete. The frozen decisions remain unchanged, including the independent LeavePeriod lifecycle, no `LeavePeriodId` on `LeavePolicyVersion`, Draft/Published/Retired lifecycle, priority-before-specificity resolution, AND within applicability sets, OR across sets, tenant-wide empty applicability, ambiguity on equal best candidates, effective employment resolution, and the Phase 4C boundary.

## Phase 4C.1 Implementation Status

### Implementation

Phase 4C.1 implements the backend-only Leave Policy Configuration surface using DTOs, application services, existing `Result`/ProblemDetails mapping, `HasPermission`, tenant context/global filters, and the repository's soft-deactivation conventions. It covers Leave Types, Leave Periods, policy identity, Draft version creation and editing, optional safe Draft cloning of active rules/applicability, grouped LeaveType selection, applicability groups, validation, publish, retire, version history, and an editor read DTO. Published and Retired versions remain immutable; LeaveType and policy codes become immutable after published historical use. No frontend or Phase 4C.0.2 detailed rule entities were added.

The five stable permissions were appended without renumbering existing permissions: `Leave.TypeManage` (30), `Leave.PeriodManage` (31), `Leave.PolicyView` (32), `Leave.PolicyManage` (33), and `Leave.PolicyPublish` (34). SuperAdmin/TenantAdmin grants continue to follow the existing `Permissions.All` convention; ordinary operational roles were not broadly granted Leave permissions. PolicyManage remains separate from PolicyPublish.

### Production build and API validation

The restored assets supplied by the user were used; no restore command was run during this implementation. Domain, Application, Infrastructure, Tests, and API all built successfully with `--no-restore`. `MSBuildEnableWorkloadResolver=false` was passed only command-scoped where needed and is not application, project, repository, machine, or Leave configuration. API startup, frontend startup, browser acceptance, migration application, and database access were not performed.

The endpoint surface is:

* `/api/leave-types` and `/api/leave-periods` list/read/create/update, with separate manage permissions and PolicyView reads.
* `/api/leave-policies` list/read/create/update and `/editor` read.
* `/api/leave-policies/{policyId}/versions` list/read/create/update, grouped `/leave-types`, and `/applicability` read/write.
* `/validate`, `/publish`, and `/retire` lifecycle commands, protected respectively by PolicyManage, PolicyPublish, and PolicyPublish.

Requests use explicit DTOs and FluentValidation plus service-level tenant, lifecycle, date, uniqueness, active-reference, and hierarchy checks. Country remains a global master; other applicability dimensions use tenant-aware existing master relationships. Concurrency uses the existing audit timestamp as an opaque required update token and maps stale/missing tokens to the repository's 409 conflict result; no extra persistence column was required, so no Phase 4C.1 migration was generated. Version-number uniqueness remains database-backed and allocation conflicts return a retryable conflict.

### Automated validation

Focused Phase 4C.1 authorization/API tests pass 14/14. They verify PolicyView read enforcement and that PolicyManage does not implicitly grant Publish/Retire. Existing Phase 4B focused Leave tests pass 11/11. The current full backend regression is 549 total, 537 passed, 0 failed, and 12 skipped; the skips are the existing SQL Server acceptance tests requiring `HRMS_SQLSERVER_TEST_SERVER`. No assertions were weakened and no automated test was added for future balances, requests, approvals, or detailed rule entities.

### Migration and safety

The existing `20260904090754_LeavePolicyFoundation` migration was not modified and remains unapplied. No new migration was necessary because the API reuses existing foundation audit fields for its opaque concurrency token. Static review confirms the foundation migration remains a schema-only, tenant-safe **SAFE FOUNDATION MIGRATION** creating only the six approved Phase 4B tables, with no pre-existing table alterations, data SQL, operational seeds, balances, requests, approvals, or detailed rules. Database changes are **none**.

`NU1900` remains a non-fatal vulnerability-feed connectivity warning from `api.nuget.org`; auditing, NuGet configuration, package sources, package versions, and TLS settings were not changed.

### Deferred work and conformance

UI work is deferred to Phase 4C.2. Detailed eligibility, entitlement, request, calendar/sandwich, attachment, clubbing, cancellation, balance, request, approval, and runtime Leave behavior remain deferred to their approved phases. The implementation conforms to the frozen independent LeavePeriod model, no `LeavePeriodId` on LeavePolicyVersion, Draft → Published → Retired lifecycle, priority-before-specificity resolution, AND/OR applicability semantics, tenant-wide empty applicability, explicit ambiguity, effective-dated employment, per-LeaveType resolution, and tenant-safe relationships.

## Phase 4C.2 Implementation Status

Phase 4C.2 adds the frontend-only Leave Type and Leave Period administration surface. The authenticated application now exposes `/leave-management/types` and `/leave-management/periods`, with navigation entries shown only when the user has `Leave.TypeManage` or `Leave.PeriodManage`, respectively. No unfinished Policy, Calendar, Balance, or Apply Leave navigation was added.

The frontend permission catalogue mirrors all five backend Leave permission names. Typed API functions in `src/api/leaveConfiguration.ts` consume the Phase 4C.1 list, get, create, and update contracts, including pagination/filter parameters and opaque concurrency tokens. No backend source, migration, database, NuGet, TLS, or system configuration was changed.

Leave Types provide searchable/status-filtered lists, empty/loading/error states, create/edit forms, day/hour unit presentation (with Hour marked as future processing), Paid/Unpaid controls, active status, soft deactivation confirmation, server validation display, and 409 conflict handling without discarding form values. Leave Periods provide the equivalent administration flow with date-only-safe display, native date inputs, required/date-range validation, overlap/duplicate conflict display, soft deactivation, and concurrency handling. Forms use labels, field error semantics, keyboard-accessible existing confirmation dialogs, responsive grids, and existing HRMS styling/components.

The two unused-callback TypeScript errors reported during the prior manual build were corrected in `src/api/leaveConfiguration.test.ts` by changing the affected stub callbacks to `()`; no test semantics changed. TypeScript validation now passes and lint exits successfully with existing repository warnings plus the two edit-loading effect-state warnings. The user independently verified that the normal-PowerShell production build succeeds (`vite v8.2.2`, 161 modules transformed, exit 0). The prior user-run Vitest execution reached discovery and reported 32 passed/2 failed test files and 320 passed/7 failed tests; all seven failures were the selector issues corrected in the Leave page tests. In the Codex execution environment, post-fix Vite/Vitest still return `spawn EPERM` while loading `vite.config.ts`; no Windows security or package troubleshooting was performed. The post-fix focused and full frontend test counts therefore remain unavailable here and are not represented as passed. Focused test sources cover API client behavior, list/loading/empty/filter/form/create/edit/deactivate flows, date-only rendering, permission visibility, backend validation, and 409 preservation.

Policy Editor work remains deferred to Phase 4C.3. No Leave Policy list, version editor, rule assignment, applicability, lifecycle, detailed rules, balances, requests, approvals, Calendar, or frontend migration/database work was started.

## Phase 4C.3 Implementation Status

Phase 4C.3 adds the frontend Policy list and Draft Policy Editor shell at `/leave-management/policies` and `/leave-management/policies/:policyId`. The list consumes the Phase 4C.1 policy query contract with search/status filters, accurate latest-version summary wording, empty/loading/error states, and PolicyManage-gated identity creation/editing. The editor consumes the policy editor and version-list contracts, supports PolicyView read access, server-generated Draft creation, optional server-side `copyFromVersionId` cloning, version selection, Draft EffectiveFrom/EffectiveTo/Priority editing, date-only-safe display, and Save Draft with the existing opaque concurrency token.

Published and Retired versions are presented read-only. Publish, Retire, Validate, LeaveType assignment, applicability editing, detailed rules, and future operational sections are intentionally not functional in this phase. PolicyView and PolicyManage route/action behavior follows the existing permission guard and API-client conventions; PolicyPublish does not expose any Phase 4C.3 action.

Focused Policy list/editor/navigation tests were added, covering loading/empty/list/filtering, permissions, identity create/edit/conflict handling, editor loading, Draft/Published/Retired states, Draft creation, version settings/save/conflict, switching versions, and the deferred lifecycle boundary. TypeScript typecheck passes and lint exits successfully with existing repository warnings. Focused Vitest and the Codex production build currently stop before discovery/bundling with environment-level `spawn EPERM` while loading `vite.config.ts`; no environment troubleshooting was performed. The user must perform final normal-PowerShell validation. No backend, migration, database, package, Node, or system changes were made.

The final Leave Period validation failure was traced to an actual UI error-state defect: create mode used the empty string as its editor sentinel, while page-level rendering tested that value as falsy. A local invalid date range therefore rendered the same message both in the page notice and editor notice. The page-level notice now renders only when no editor is open (`editingId === null`); editor validation remains intact and is asserted within the named editor form. The Leave Type page received the same narrow sentinel correction to prevent the analogous duplicate presentation. Duplicate-code, overlap, and concurrency errors remain editor-scoped while the editor is open.

## Phase 4C.4 Implementation Status

Phase 4C.4 replaces the Policy Editor shell's deferred Leave Types and Applicability cards with functional Draft configuration sections. Leave Types load through the existing Leave Type API, preserve selected inactive historical references, prevent new inactive assignments, and save the selected IDs atomically through the Phase 4C.1 aggregate endpoint with the version concurrency token. Published, Retired, and PolicyView-only versions remain read-only.

Applicability groups use the Phase 4C.1 read/write contracts and existing searchable master lookup APIs. The UI presents ALL conditions within a group and OR between groups, treats zero groups as intentional tenant-wide applicability, filters hierarchical child lookups by modeled parents, clears invalid descendants on parent changes, and persists only master IDs. Country is mapped to the actual global-master contract field `CountryLocationId`. The backend exposes no separate `LocationId` master/field, so no invented Location endpoint was added; Work Location remains available through the tenant master API.

The implementation reuses `MasterDropdown`, existing master-data clients, cached lookup requests, section-scoped save/error handling, opaque concurrency tokens, and the existing responsive HRMS styles. Focused Phase 4C.4 tests cover selected/inactive Leave Types, aggregate ID persistence, tenant-wide/ALL/OR applicability, read-only access, removal, and failed-save state preservation. TypeScript typecheck passes and lint exits successfully with repository warnings. Codex Vitest and Vite execution stop before discovery/bundling with environment-level `spawn EPERM` while loading `vite.config.ts`; normal-PowerShell validation remains required. No backend, migration, database, package, Node, or system changes were made. Validate, Publish, Retire, detailed rules, and Phase 4C.5 lifecycle UI remain deferred.

## Phase 4C.5 Implementation Status

Phase 4C.5 adds lifecycle controls to the existing Policy Editor. Draft users with `Leave.PolicyManage` can intentionally run non-mutating `Validate Draft`; the structured backend result is retained in an in-editor validation panel with separate Valid/Not ready, Errors, and Warnings presentation. Configuration changes mark a prior result potentially stale and expose Validate Again without changing Draft status.

Publish and Retire use the actual Phase 4C.1 command endpoints, `AllowedActions`, and the independent `Leave.PolicyPublish` permission. Each action uses the existing accessible confirmation dialog, prevents duplicate submission while pending, refreshes the selected editor/version list after success, and transitions configuration to the established read-only Published/Retired state. Publish/Retire failures remain visible without discarding editor state; no automatic retry is performed.

Version History is a read-oriented section backed by the version list contract. It shows version number, lifecycle status, effective dates, priority, Leave Type and applicability counts, creation/update metadata, and opens historical versions through the existing version selector flow. It is not an audit-event log. PolicyView users can inspect history and historical configuration; PolicyManage without PolicyPublish can validate/edit Drafts but cannot Publish or Retire. PolicyPublish without PolicyManage can see lifecycle commands when the backend `AllowedActions` permits them, but cannot edit Draft configuration.

Focused lifecycle/history test sources cover validation visibility/results/staleness, permission combinations, Publish and Retire confirmation/success/conflict paths, read-only transitions, and history rendering/navigation. TypeScript typecheck and lint pass with existing non-fatal warnings. Codex Vitest and Vite stop before discovery/bundling with environment-level `spawn EPERM` while loading `vite.config.ts`; normal-PowerShell validation remains required. No backend, migration, database, package, Node, or system changes were made. Detailed Leave rules and later rule-editor work remain deferred.

## Phase 4C.7 Implementation Status

### Frozen scope and deferred decisions

The frozen Eligibility fields implemented are `EligibilityMode` (`Immediate` or `MinimumService`), optional positive `MinimumServiceValue` with `MinimumServiceUnit` (`Days` or `Months`) when MinimumService is selected, `ProbationMode` (`Allowed` or `NotAllowed`), and `NoticePeriodMode` (`Allowed`, `NotAllowed`, or `AllowedWithApproval`). The absence of an Eligibility row represents the documented baseline: Immediate, probation allowed, and notice allowed.

Service-month boundary semantics, rehire semantics, confirmation source/status for `AfterConfirmation`, statutory formulas, emergency exceptions, and other unresolved business sources remain deferred. `AfterConfirmation` is represented in the enum for contract compatibility but is rejected safely because its authoritative confirmation source is not frozen. No Applicability, Entitlement, Request, balance, or runtime Leave fields were added.

### Implementation

`LeavePolicyEligibilityRule` is a typed zero-or-one child of `LeavePolicyRule`, with tenant-aware ownership and an explicit composite uniqueness constraint. The API is nested under the existing policy/version/LeaveType route: GET and PUT `/api/leave-policies/{policyId}/versions/{versionId}/leave-types/{leaveTypeId}/eligibility`. Reads require `Leave.PolicyView`; Draft writes require `Leave.PolicyManage`; Publish permission is not required for configuration edits. Draft-only lifecycle checks, route ownership checks, enum/conditional validation, opaque concurrency tokens, 409 conflict mapping, and baseline row removal are implemented. Existing Draft LeaveType replacement now preserves Eligibility children for retained LeaveTypes, and safe Draft cloning copies typed Eligibility configuration.

The Policy Editor adds a separate Eligibility section for each selected LeaveType. Draft users with PolicyManage can edit and save the typed fields; PolicyView users and Published/Retired versions see the saved configuration read-only. Errors remain scoped to the Eligibility section and failed/409 saves preserve entered values. No Apply Leave evaluator was added.

### Validation and migration status

Domain and Application `--no-restore` builds passed with the command-scoped `MSBuildEnableWorkloadResolver=false` workaround. The user verified the same Infrastructure build in normal PowerShell with exit code 0 and only NU1900 vulnerability-feed warnings; the earlier Codex `ResolvePackageAssets` failure is therefore classified as environment-specific. Codex Tests and API build attempts still silently exited 1 with 0 warnings and 0 errors and were not debugged further. Backend focused/full test execution therefore remains pending normal-PowerShell validation.

Exactly one new migration was generated: `20260904110149_LeavePolicyEligibilityRules.cs` with its Designer; the model snapshot includes the same typed entity. It creates only `LeavePolicyEligibilityRules`, with TenantId, typed Eligibility fields, the tenant-aware one-to-one LeavePolicyRule FK, unique ownership, and restrictive delete behavior. Static review found no destructive operations, existing-table alterations, SQL/data mutations, operational seeds, or future Leave tables. `20260904090754_LeavePolicyFoundation` was not modified. The migration was not applied.

Frontend Eligibility typecheck passed and lint exited successfully with existing non-fatal warnings. Codex Vitest and Vite remain blocked before discovery/bundling by the known environment-level `spawn EPERM` limitation; normal PowerShell validation is required. No restore, database access, migration application, package change, NuGet/TLS change, or system configuration change was performed. Phase 4C.8 Entitlement/Accrual work remains deferred.

## Phase 4C.8 Implementation Status

### Frozen scope and deferred decisions

The frozen configuration implemented in this phase is the typed core: `EntitlementMode` (`Allocated`, `Unlimited`, `NoBalanceRequired`), `EntitlementSource` (`PolicyAccrual`, `ExternalGrant`, `NoBalanceRequired`), decimal `EntitlementQuantity` using `decimal(9,3)`, `AccrualFrequency` (`None`, `Upfront`, `Monthly`, `SemiAnnual`, `Annual`), and conditional `AccrualTiming` (`StartOfPeriod`, `EndOfPeriod`). `Quarterly` remains represented for contract compatibility but is rejected until business approval. No `LeavePeriodId` was added; entitlement resolves against the independently resolved tenant LeavePeriod.

Proration formulas, rounding modes/increments, carry-forward consumption and expiry, negative-balance limits, lapse, encashment, scheduled job timing, external grant authority, Attendance/Comp Off earning, and Payroll handoff remain open or runtime-owned. They are not exposed as functional fields. No balance, ledger, request, approval, or runtime transaction entities were added.

### Implementation

`LeavePolicyEntitlementRule` is a typed zero-or-one child of `LeavePolicyRule`, with TenantId, an explicit tenant-aware composite ownership FK, unique ownership index, and Restrict delete behavior. Nested GET/PUT endpoints follow the Eligibility route: `/api/leave-policies/{policyId}/versions/{versionId}/leave-types/{leaveTypeId}/entitlement`. GET requires `Leave.PolicyView`; Draft PUT requires `Leave.PolicyManage`; PolicyPublish is not required for configuration editing. Conditional validation rejects contradictory modes, non-positive Allocated quantities, finite quantities for Unlimited/NoBalanceRequired, normal accrual for NoBalanceRequired, scheduled accrual without PolicyAccrual, missing timing, and unsupported Quarterly accrual.

Draft cloning preserves typed Entitlement children for retained LeaveType rules. Existing LeaveType replacement preserves retained Eligibility/Entitlement children. Draft validation and strict Publish validation include Entitlement validation, while an absent Entitlement row remains the documented optional baseline rather than being interpreted as zero, Unlimited, or NoBalanceRequired.

The Policy Editor now provides a separate per-LeaveType Entitlement section. Draft + PolicyManage can edit mode, source, quantity, frequency, and timing; PolicyView and Published/Retired versions see readable historical configuration only. Conditional controls clear stale quantity/timing values from outgoing payloads, failed/409 saves preserve entered values, and no Request Rules controls are shown.

### Validation and migration

Domain and Application builds completed with `--no-restore`; Application reported only NU1900. Infrastructure again silently exited 1 in Codex during package resolution with NU1900 and no compiler diagnostics; the user-provided normal-PowerShell Infrastructure success remains accepted. Codex Tests/API builds and backend test execution were not completed because of the silent environment limitation. Frontend TypeScript passed; lint passed with existing non-fatal warnings; Vitest and Vite hit the known Codex `spawn EPERM` limitation before discovery/build.

Exactly one new migration was produced: `20260904111427_LeavePolicyEntitlementRules.cs` and its Designer, with the model snapshot updated for Eligibility and Entitlement metadata. It creates only `LeavePolicyEntitlementRules` with typed enum fields, nullable decimal quantity, TenantId, unique ownership, and restrictive FK. Static review found no destructive operations, existing-table alterations, SQL/data mutations, operational seeds, balance/request tables, or future rule tables. The migration was not applied. An EF migration-removal attempt was stopped by the environment before removal and reported a SQL Server encryption connection failure while checking migration history; no migration update or data operation succeeded.

No restore, package/NuGet/TLS/system configuration change, API startup, balance initialization, or runtime Leave operation was performed. Phase 4C.9 Request Rules and all runtime entitlement/balance work remain deferred.

## Phase 4C.11 Resolver-readiness contract

Phase 4C.11 hardens the configuration foundation without beginning runtime Leave processing. All tenant-owned detailed configuration entities use the same tenant query-filter pattern. Clubbing integrity is protected by tenant/version-aware participant foreign keys, a different-participant check, and a computed canonical unordered-pair key with a scoped unique index. Draft and Publish validation both inspect persisted Clubbing invariants; no adjacency evaluator was added.

The Application `ILeavePeriodResolver` foundation contract resolves `(TenantId, EffectiveDate)` against active LeavePeriods using inclusive `StartDate <= EffectiveDate <= EndDate` semantics. It returns `Resolved` for exactly one match, `NotConfigured` for zero matches, `ConfigurationAmbiguity` for multiple matches, and `InvalidTenant` for an empty tenant identifier. It returns configuration only and does not create balances, evaluate requests, or calculate calendar days. Month-limit semantics, business timezone/calendar basis, and runtime LeavePeriod usage remain open for later design.
