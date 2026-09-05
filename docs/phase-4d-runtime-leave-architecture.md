# Phase 4D.0 — Runtime Leave Architecture Design

Status: DESIGN ONLY — runtime Leave code, schema, migrations, API endpoints, UI, and database operations are not implemented.

This document is the canonical runtime design proposal. It depends on the configuration model in [the Phase 4 Leave Management Design](phase-4-leave-management-design.md) and the hardening findings recorded in [the Policy Foundation Review](phase-4-policy-foundation-review.md).

## Phase 4D.1 Implementation Status

Implemented as a reusable application foundation:

- `IEmployeeIdentityResolver` / `EmployeeIdentityResolver` resolve the current Employee only from the authenticated tenant and `AccountEmployeeCurrentLink`. An absent link is rejected; there is no UserId, email, username, or employee-code fallback.
- `IEffectiveEmploymentResolver` / `EffectiveEmploymentResolver` resolve one `EmployeeEmploymentHistory` row for `EffectiveFrom <= DateOnly <= EffectiveTo` within the tenant and return an ID-based `EffectiveEmploymentSnapshot` containing the Leave applicability dimensions and employment dates/status. Zero rows return `NotFound`; multiple rows return `ConfigurationAmbiguity`.
- `ILeavePolicyResolver` now composes the effective-employment resolver. Published active versions are filtered by LeaveType and date, applicability is evaluated using only populated constraints, specificity is the count of populated applicability dimensions, priority wins first, and an exact priority/specificity tie returns `ConfigurationAmbiguity`. No database ordering or identifier is used as a tie-breaker. `NoPolicy` remains the existing status name and is also exposed as the `NotConfigured` alias.
- The existing `ILeavePeriodResolver` remains independent and DateOnly-based: inclusive boundaries, one match succeeds, zero matches return `NotConfigured`, and multiple active matches return `ConfigurationAmbiguity`.
- Dependency injection registers the identity and employment resolvers. No public runtime endpoint was added.

Deliberately unimplemented: Policy effective-date selection for multi-day requests, balances, ledgers, accrual, LeaveRequest, RequestDay, workflow, approval, calendar evaluation, Sandwich, Clubbing runtime evaluation, attachments, cancellation, and retroactive reconciliation. Focused tests were authored for identity and employment-resolution behavior; execution remains subject to the documented Codex test/build environment limitations.

## Phase 4D.1 Implementation & Conformance Status

The current configuration source confirms that `LeavePolicyApplicabilitySet` supports `Gender` plus the following ID dimensions: Holding Company, LOB, Organization, Department, Sub Department, Section, Sub Section, Function, Sub Function, Grade, Designation, Employee Type, `CountryLocationId`, `WorkLocationId`, and `CostCenterId`. There is no separate `CountryId` or `LocationId` field. `EffectiveEmploymentSnapshot` mirrors these exact fields; it does not introduce a new applicability dimension or fall back to the current employment row.

`Gender` is therefore a legitimate current applicability constraint. It is sourced from the Employee record because the effective employment-history entity does not contain a dated Gender field; if historical Gender changes are later required, that source decision must be revisited before runtime evaluation. Specificity counts Gender and each populated supported ID once, without weighting or summing across sets.

The conformance hardening adds authenticated-tenant checks to the explicit-tenant employment, policy, and period resolver calls and excludes inactive LeaveTypes from runtime policy candidates. Policy resolution still accepts a caller-supplied `DateOnly`; it does not choose a request StartDate, submission date, or per-day strategy. All resolvers remain read-only. No schema or API endpoint was added.

## 1. Executive Summary

The Phase 4C policy foundation is suitable for designing a runtime Leave layer, but not for silently choosing unresolved business semantics. Runtime processing should resolve a linked Employee, an effective employment snapshot, an independent LeavePeriod, and one published policy rule before evaluating a request. Published policy references and date-level request decisions must be retained so later configuration changes cannot rewrite historical truth.

The recommended first runtime slices are a policy-evaluator foundation, then an immutable balance ledger, then a request aggregate and submission pipeline. Advanced accrual, calendar expansion, Clubbing adjacency, attachments, cancellation, and approval behavior require explicit decisions before their runtime implementation.

## 2. Current Foundation Dependencies

The design reuses these existing seams:

- `ITenantContext` and the authenticated tenant/host resolution path provide the server-derived tenant boundary. Runtime callers must never select a tenant from a self-service payload.
- `AuthService`, the current account-to-employee link, and the existing `/me.employeeIdentity` contract provide the self-service Employee identity. The link, not username, email, or equal GUID assumptions, is authoritative.
- `EmployeeEmploymentHistory` is effective-dated with inclusive `EffectiveFrom`/`EffectiveTo` and stores the current applicability foreign keys plus historical snapshots. `EmployeeEmploymentService` and related validation are the existing employment conventions.
- `EmployeeManagerResolver` resolves a date-specific direct manager and reports missing, inactive, overlapping, cyclic, and legacy-conflict states. It is a future workflow dependency, not an approval service.
- `ILeavePeriodResolver` resolves one active period for `TenantId + DateOnly`, returning `Resolved`, `NotConfigured`, `ConfigurationAmbiguity`, or `InvalidTenant`.
- `ILeavePolicyResolver` resolves a published policy rule for tenant, employee, LeaveType, and date using effective employment, applicability, priority, specificity, and ambiguity detection. It does not yet evaluate detailed rules.
- `Result<T>`/ProblemDetails mappings provide the existing application/API error vocabulary. Runtime errors should extend that vocabulary through typed categories rather than ad-hoc strings.
- Leave configuration uses scoped opaque concurrency tokens and HTTP 409 conflict handling. Runtime aggregates should use a database rowversion/equivalent token where concurrent state transitions matter.
- Existing audit conventions should remain separate from a future immutable Leave business-event stream.

No runtime service should expose `DbContext` directly to callers. Proposed interfaces below are design contracts, not implemented types.

## 3. Runtime Domain Boundaries

The boundaries remain distinct:

| Boundary | Runtime responsibility |
|---|---|
| Identity | Account to linked Employee and tenant |
| Employment | Effective historical employment snapshot |
| Policy | Published policy/version/rule resolution |
| Period | Active LeavePeriod resolution by business date |
| Eligibility | Whether the Employee qualifies on the chosen date |
| Entitlement | Interpretation of configured quantity/source/mode |
| Balance | Employee-period state and reservations |
| Calendar | Holiday, weekly-off, and chargeable-date classification |
| Request | Employee intent, date details, status, and revisions |
| Workflow | Future approval decisions and routing |
| Documents | Future file persistence, separate from requirement evaluation |

Policy configuration is immutable after publication. Runtime records must reference the configuration used and snapshot decisions that cannot safely be recomputed later.

## 4. Employee Identity Resolution

For self-service operations, the authenticated account resolves through the existing account-to-employee current link and `/me.employeeIdentity` dependency to one `EmployeeId` in the authenticated tenant. The server derives this identity; a self-service request must not accept an arbitrary EmployeeId as authority. In particular, `UserId == EmployeeId` and username/email matching are invalid strategies.

Administrative operations may select a subject Employee, but only through separate permissions and explicit server-side tenant/employee ownership checks. The future request API should therefore have separate self-service and administrative command paths even if they share application components.

Identity failures need distinct outcomes: account not linked, linked Employee missing, inactive account, inactive Employee, and tenant mismatch. These are not policy ambiguity or balance errors.

## 5. Effective Employment Snapshot

Introduce a pure application result, such as `EffectiveEmploymentSnapshot`, resolved for `TenantId + EmployeeId + EffectiveDate`. It should contain the selected history record identity, Employee identity, effective dates, employment status/type, DOJ or other service dates available on the authoritative Employee/employment model, manager reference, and every applicability dimension currently used by policy matching:

`HoldingCompanyId`, `LobId`, `OrganisationId`, `DepartmentId`, `SubDepartmentId`, `SectionId`, `SubSectionId`, `FunctionId`, `SubFunctionId`, `GradeId`, `DesignationId`, `EmployeeTypeId`, `CountryLocationId`, `WorkLocationId`, and `CostCenterId`, plus Employee gender where the policy model uses it.

The resolver must return no record or configuration ambiguity when zero or multiple history rows apply. It must not fall back to the current Employee row. Historical snapshot names may support explanation, but IDs must remain tenant-validated.

The current model does not establish authoritative confirmation-date/probation-source and notice-period state for all future eligibility decisions. Service-start date, rehire treatment, and service-month completion are also unresolved. Those are explicit prerequisites for service/probation/notice evaluation, not reasons to duplicate employment data in Leave.

## 6. LeavePeriod Resolution

`ILeavePeriodResolver` is the foundation contract:

```text
ResolveAsync(TenantId, EffectiveDate)
  1 active inclusive match  -> Resolved(period)
  0 matches                -> NotConfigured
  >1 active matches        -> ConfigurationAmbiguity
```

Matching is `StartDate <= EffectiveDate <= EndDate`, using `DateOnly` business dates. Inactive periods remain readable historically but are ignored for new/current resolution. The resolver must not order its way out of ambiguity and must not add `LeavePeriodId` to PolicyVersion or policy rules. It is independent from holiday, weekly-off, attendance, and payroll calendars.

Runtime request processing should resolve a period for each date or for a request segment after the cross-boundary policy decision is approved. `Month` request limits remain a separate semantic: if Gregorian calendar month is approved, it must be represented explicitly; it must not be inferred from LeavePeriod.

## 7. Policy Resolution Pipeline

The proposed application pipeline is:

1. Resolve authenticated tenant and linked Employee.
2. Validate the requested LeaveType belongs to that tenant and is usable.
3. Resolve the effective employment snapshot for the policy date.
4. Query Published, active policy versions effective on that date.
5. Select the LeavePolicyRule for the LeaveType and evaluate applicability sets.
6. Rank by higher numeric priority, then highest matching-set specificity.
7. Return exactly one winner or a typed failure.

Outcomes should be `Resolved`, `NotConfigured`/`NoPolicy`, `NotApplicable`, `ConfigurationAmbiguity`, `InvalidTenant`, `NoApplicableEmployment`, and identity failures as distinct categories. A tie is a safe failure; the evaluator must never choose by ID or insertion order.

The existing `ILeavePolicyResolver` is a useful foundation, but the eventual evaluator must compose it with detailed evaluators rather than making policy resolution itself evaluate balances, dates, files, or workflow.

## 8. Policy Effective-Date Strategy

The safest initial rule is to select policy using the request StartDate and reject a request that crosses a policy-effective-date boundary. This provides one explainable policy context without silently mixing rules. A later design may support date-level segmentation, but it must define how quantities, eligibility, entitlement, approval, and balances work when segments differ.

This is a business decision blocker for runtime request implementation: the repository does not yet freeze StartDate selection versus per-date resolution versus submission-date selection. It does not block designing the evaluator interfaces. Submission date should remain audit metadata, not the default policy-effective date.

Requests crossing a PolicyVersion boundary, LeavePeriod boundary, or effective-employment boundary should be rejected in the initial MVP. A future segmented model can retain one request aggregate with multiple RequestDay contexts only after business approval.

## 9. Eligibility Evaluation

`ILeaveEligibilityEvaluator` should consume a resolved policy rule, effective employment snapshot, business date, and any authoritative employment/notice provider. It should return eligible, ineligible with a stable reason, or unavailable/ambiguous configuration.

Configured inputs are `EligibilityMode`, minimum service value/unit, probation mode, and notice-period mode. Runtime cannot safely complete these until the following are approved and sourced: service start date (original DOJ, group DOJ, or rehire date), month-boundary calculation, rehire continuity, authoritative confirmation/probation source, notice-period source, statutory formulas, and emergency exceptions. No emergency bypass or statutory formula is proposed here.

## 10. Entitlement Interpretation

`ILeaveEntitlementInterpreter` should translate configuration into a typed entitlement capability:

- `Allocated`: a finite quantity and a balance bucket are required.
- `Unlimited`: request rules and calendar rules still apply, but no artificial maximum balance should be fabricated.
- `NoBalanceRequired`: the request can proceed without a normal paid Leave balance; it is not a negative balance and not a large/infinite balance.

`PolicyAccrual` is configuration for future credits. `ExternalGrant` must be connected later through an explicit grant-source boundary; it does not authorize Attendance, overtime, or manager-grant transactions. `NoBalanceRequired` is the configured model for LWP/unpaid-style Leave.

The interpreter should be pure over a policy snapshot. It should not create a balance, post a ledger transaction, or run a scheduler.

## 11. Balance Architecture

Use a materialized current balance for efficient availability plus an immutable transaction ledger as historical truth. The balance is a projection guarded by concurrency; it is rebuildable from valid ledger transactions and policy snapshots. A balance record should not be fabricated for `NoBalanceRequired` or `Unlimited` unless a later reporting requirement explicitly needs a non-spending projection.

Recommended displayed measures are `Granted`, `Consumed`, `Reserved`, and `Available`, with `Available = Granted - Consumed - Reserved` for finite Allocated Leave. Reservations prevent two pending requests from spending the same quantity. No-balance and unlimited paths bypass finite-balance checks while retaining request and policy validation.

## 12. Balance Ledger

`LeaveBalanceTransaction` should be append-only and identify tenant, Employee, LeaveType, LeavePeriod, quantity/direction, source type/key, policy version/rule, request/event correlation, effective business date, created timestamp, and idempotency key.

Initial required transaction types are `Opening`, `Accrual`, `ManualAdjustment`, `LeaveReserved`, `LeaveReleased`, and `LeaveConsumed`. `CancellationRecredit`, `CarryForward`, `Expiry`, `Encashment`, and `ExternalGrant` are future-reserved until their semantics are approved. Corrections should be compensating transactions, never edits to historical rows.

## 13. Accrual Architecture

The configuration currently supports `None`, `Upfront`, `Monthly`, `SemiAnnual`, and `Annual`; `Quarterly` remains enum-compatible but is rejected pending approval. The runtime should use a hybrid scheduled plus idempotent catch-up model: a background process identifies due credits, and a safe catch-up path can repair missed runs. Both use a deterministic accrual-period key and unique source/idempotency key.

Each credit records the resolved LeavePeriod, policy version/rule, due date, event date, quantity, and source. No credit is generated by absence of configuration. Scheduler frequency, due-date timing, proration, rounding, carry-forward, lapse, encashment, and external grant authority remain decisions for the balance/accrual phase.

## 14. Leave Request Aggregate

Proposed `LeaveRequest` aggregate, not implemented:

- tenant and Employee identity;
- LeaveType, resolved LeavePeriod, PolicyVersion, and LeavePolicyRule references;
- requested date range and requested quantity/unit;
- status, reason, submission and decision metadata;
- current revision/concurrency token;
- idempotency correlation;
- policy/evaluation snapshot reference or hash;
- timestamps and actor identity.

The aggregate owns the request lifecycle and immutable revisions/events. It does not own organizational manager discovery, balance policy, file storage, or payroll/attendance side effects.

## 15. Leave Request Day Model

Use a proposed `LeaveRequestDay` child for each business date in the request. It should support the currently approved `1.0` full-day and `0.5` half-day quantities without enabling quarter-day/hour/shift behavior. Candidate fields are DateOnly, requested fraction, day classification, chargeable quantity, holiday/weekly-off/sandwich flags, evaluation reason, and the policy/period context used.

Before submission these values may be preview calculations. Once a request is committed, chargeable quantities and decision reasons should be authoritative snapshots; recomputation from future calendars or policies must not rewrite history.

## 16. Request Validation Pipeline

The future Apply Leave pipeline should be ordered as follows:

1. Authenticate and resolve linked Employee.
2. Validate tenant, LeaveType, dates, unit, and basic range.
3. Resolve effective employment and the approved policy date strategy.
4. Resolve LeavePeriod.
5. Resolve published policy rule and fail on ambiguity.
6. Evaluate Eligibility.
7. Evaluate Request Rules, including min/max/consecutive, notice, backdate, period limits, and full/half-day mode.
8. Resolve business calendar classifications.
9. Calculate chargeable RequestDays.
10. Evaluate Clubbing against the approved adjacency model.
11. Evaluate attachment requirement; do not store files in this phase.
12. Evaluate entitlement and finite balance/reservation.
13. Detect overlaps and conflicting requests.
14. Persist the request, day snapshots, reservation, event, and outbox work atomically.

Preview, if approved, must be non-mutating and advisory. Final POST must recalculate every authoritative result and must not trust preview output.

## 17. Calendar/Week-Off/Holiday Evaluation

Design an `IEmployeeBusinessCalendar` façade over future `IHolidayResolver` and `IWeeklyOffResolver` abstractions. Inputs are tenant, Employee, date, and effective employment/context; output identifies working day, holiday, weekly off, or combined classification. The current configuration has no runtime holiday/weekly-off source, attendance integration, shift lookup, roster generation, or calendar master tables.

Holiday and weekly-off sources, employee assignment, time zone/business-date provider, and historical calendar snapshots must be resolved before Apply Leave. LeavePeriod must not be used as a business calendar.

## 18. Sandwich Evaluation

`ILeaveCalendarEvaluator` should consume RequestDays, the resolved CalendarRule, and business-calendar classifications and return chargeable dates plus explanations. It must keep normal in-span Holiday/Week-Off treatment separate from SandwichMode and Prefix/Suffix/Between configuration.

No algorithm is frozen for holiday clusters, weekly-off gaps, chains, partial days, or policy/date boundaries. Exact expansion semantics are therefore deferred and block implementation of runtime sandwich behavior, not design of the interface.

## 19. Clubbing Evaluation

`ILeaveClubbingEvaluator` should consume the candidate request, existing relevant requests, date-level classifications, and the symmetric normalized `NotAllowed` pair configuration. It must produce allowed/blocked plus a reason without mutating requests.

The exact definition of adjacent, holiday/weekly-off bridging, pending versus approved requests, half-day adjacency, chains, and multiple requests is unresolved. The initial runtime MVP should disable Clubbing evaluation or reject configured Clubbing use until that definition receives business approval. It must not infer adjacency from the stored pair alone.

## 20. Attachment Requirement Evaluation

`ILeaveAttachmentRequirementEvaluator` should return a typed requirement such as `NotRequired`, `Optional`, or `Required(DocumentLabel)` based on the resolved AttachmentRule and requested quantity. This is separate from persistence. A future `IDocumentStorage` boundary should handle metadata, provider, virus scanning, retention, access control, and accepted formats after those decisions are made. No upload, file bytes, document table, or request attachment is part of this design implementation.

## 21. Cancellation Runtime Boundary

The policy switches `WithdrawAllowed`, `CancelAllowed`, and `ModifyAllowed` remain independent. Future services must distinguish Withdraw before a final approved state, Cancel of an already effective/approved request, and Modify as a business change. Each depends on workflow state, dates, balance reservation/consumption, attendance, and payroll decisions that are not frozen.

The recommended audit-preserving Modify model is an immutable request revision or replacement request linked to the original, with compensating release/re-reservation transactions where applicable. No runtime action, timing cutoff, approval reversal, re-credit, attendance, or payroll behavior is implemented here.

## 22. Approval/Manager Boundary

LeaveRequest should publish a future approval command/event boundary rather than discover managers itself. An approval service may ask an adapter backed by `EmployeeManagerResolver` for a date-specific direct manager, but manager resolution alone does not mean approval routing is ready. L1/L2 routing, approver eligibility, delegation, escalation, auto-approval, re-notification, and reversal require a separate workflow decision.

The first approval implementation should be one explicit stage with state-transition and idempotency guards, not a new generic workflow engine unless an existing HRMS workflow is adopted.

## 23. Request State Machine

Recommended initial states are:

`Draft` (optional server-side draft) → `Submitted` → `PendingApproval` → `Approved` or `Rejected`.

From eligible states, `Withdrawn` is a distinct employee retraction. `Cancelled` is a distinct post-approval/effective cancellation. Modification should create a new revision/replacement relationship rather than silently mutating an approved historical decision. Exact allowed transitions depend on future workflow and cancellation decisions.

## 24. Concurrency Strategy

Runtime aggregates and balance projections should use SQL Server `rowversion` or the repository-equivalent opaque token. State transitions must include the expected token and return 409 on stale writes. Balance reservation should use a short transaction with an atomic conditional availability update or a narrowly scoped `UPDLOCK`/`SERIALIZABLE` section; use pessimistic locking only around the finite balance row. Unique constraints protect request idempotency, ledger sources, and overlap keys where deterministic.

Deadlock retries should be bounded and limited to idempotent transaction boundaries. No automatic retry should repeat an externally visible command without its idempotency key.

## 25. Idempotency Strategy

Accept an `Idempotency-Key` for future command POSTs. Scope it by tenant, linked Employee, operation, and key; persist the request fingerprint and final response/reference. Reuse of a key with a different payload is a validation conflict. Accrual, grant, reservation, consumption, release, and approval decisions should each have a unique business source/event key so retries cannot duplicate ledger effects.

## 26. Transaction Boundaries

- Balance initialization: balance projection and opening ledger transaction together.
- Accrual/grant: due-event identity, ledger row, balance projection, and outbox record together.
- Submit: request, RequestDays, finite reservation (if applicable), request event, and outbox record together.
- Approval: guarded state transition and consume/release reservation according to the approved policy together.
- Withdraw/cancel/modify: guarded request transition, release/re-credit/replacement records, and event/outbox together; exact effects remain deferred.

External notifications and storage provider calls must be outbox-driven and not held inside the database transaction.

## 27. Historical/Audit Strategy

Generic audit logs explain actor and field changes; they are not the business ledger. Use an immutable `LeaveRequestEvent` stream for Submitted, Approved, Rejected, Withdrawn, Cancelled, Modified, and balance-related outcomes. Events should include actor, tenant, timestamp, request revision, correlation/idempotency key, and before/after status where relevant.

Persist references to Employee, LeaveType, LeavePeriod, PolicyVersion, and PolicyRule, plus the effective employment/policy decision and RequestDay chargeable snapshots. Keep configuration immutable after Publish; future policy edits must create versions and never rewrite historical request decisions.

## 28. Security & Tenant Isolation

Tenant is server-derived and checked at every aggregate boundary. Self-service operations can read/create only the linked Employee's requests and balances and only with dedicated permissions. Admin/HR operations require explicit subject-selection permission and must validate the selected Employee in the tenant. Policy ambiguity, identity-link failure, missing employment, and missing period must fail closed. No client-supplied EmployeeId, tenant, balance, policy, or approval result is authoritative.

Proposed future permissions are `Leave.RequestSelf`, `Leave.RequestViewOwn`, `Leave.RequestManage`, `Leave.Approve`, `Leave.BalanceView`, and `Leave.BalanceManage`, subject to repository naming review. They must not be seeded in this phase.

## 29. Proposed Runtime API

These are proposals only; no endpoints are implemented:

- `GET /api/leave/self/summary`
- `GET /api/leave/self/balances`
- `GET /api/leave/self/requests`
- `POST /api/leave/requests/preview` (recommended non-mutating preview)
- `POST /api/leave/requests`
- `GET /api/leave/requests/{id}`
- future command endpoints for withdraw/cancel/modify with explicit authorization
- future approval endpoints in a separate workflow boundary

Final submission must recalculate after preview. ProblemDetails should distinguish Validation, NotConfigured, NotApplicable, ConfigurationAmbiguity, NotEligible, InsufficientBalance, Conflict, Permission, Concurrency, and identity/link failures.

## 30. Proposed Runtime Tables

All entries are **PROPOSED — NOT IMPLEMENTED**.

| Entity/table | Initial classification | Purpose |
|---|---|---|
| LeaveBalance | Phase 4D.2 required | Materialized finite availability projection |
| LeaveBalanceTransaction | Phase 4D.2 required | Immutable balance truth |
| LeaveRequest | Phase 4D.3 required | Request aggregate |
| LeaveRequestDay | Phase 4D.3 required | Date-level authoritative evaluation snapshot |
| LeaveRequestEvent | Phase 4D.3/4D.5 required | Immutable request business history |
| LeaveApproval | Approval phase required | Explicit approval decisions, if not an existing workflow model |
| OutboxMessage | Existing-platform decision | Reliable external notification/event delivery |
| Document metadata/attachment | Future/Apply Leave dependent | Separate storage boundary after document decisions |

No table is proposed for runtime holiday master data in this phase; its ownership/source must first be established.

## 31. Future Migration Strategy

Use bounded slices and keep design abstractions schema-free:

1. **4D.1** evaluator contracts, typed outcomes, identity/employment adapters, and resolver composition; no runtime schema if possible.
2. **4D.2** `LeaveBalance` and immutable ledger with idempotency/source constraints.
3. **4D.3** `LeaveRequest`, `LeaveRequestDay`, and request event/revision foundation.
4. **4D.4** preview and authoritative submission with finite reservation.
5. **4D.5** approval boundary and approval persistence if required.
6. **4D.6** Withdraw, Cancel, and immutable Modify revisions.
7. **4D.7** accrual scheduling/catch-up and external grants.
8. **4D.8** advanced calendar, Sandwich, Clubbing, attachment storage, and other approved features.

Each migration must remain additive, tenant-aware, idempotency-safe, and independently reviewable.

## 32. Retroactive Policy Strategy

Published versions are immutable, but an effective date earlier than publication can still alter historical interpretation. The recommended initial rule is to prohibit retroactive Published versions through lifecycle validation. A later administrative reconciliation workflow may permit restricted retroactivity only with impact analysis, request/balance treatment, compensating ledger entries, and explicit audit. Runtime must never silently recompute existing requests from a newly effective historical version.

## 33. Open Decision Register

| Decision | Why needed / affected component | Resolve before | Recommended default / risk |
|---|---|---|---|
| Policy date: StartDate, per-day, or submission | Policy evaluator and multi-date requests | 4D.1 runtime behavior / Apply Leave | StartDate + reject crossings initially; wrong choice changes entitlements |
| Cross-boundary requests | Request and RequestDay model | Apply Leave | Reject initially; segmentation is more complex |
| Tenant/business timezone | Notice, backdate, accrual, cancellation | 4D.1 production evaluator | Add authoritative business-date provider; never server local time |
| Service start and month boundary | Eligibility | Eligibility runtime / Apply Leave | Require business decision; wrong service date changes eligibility |
| Rehire continuity | Eligibility | Apply Leave | Explicit continuity policy |
| Confirmation/probation source | Eligibility | Apply Leave | Add/use authoritative employment source |
| Notice-period source | Eligibility/request rules | Apply Leave | Do not infer from unrelated fields |
| Month limit semantics | Request Rule evaluator | Apply Leave | Approve Gregorian month or leave unsupported |
| Holiday and weekly-off source | Calendar evaluator | Apply Leave | Define employee/tenant historical source |
| Sandwich expansion semantics | Calendar evaluator | Apply Leave | Approve exact gap/cluster/chain algorithm |
| Clubbing adjacency | Clubbing evaluator | Apply Leave | Keep disabled until adjacency is frozen |
| Attachment storage, retention, formats | Attachment evaluator/storage | Apply Leave | Requirement-only until storage/security decisions |
| Accrual due timing/recovery | Balance/accrual | 4D.2/4D.7 | Hybrid idempotent catch-up |
| Proration/rounding/carry-forward | Balance | 4D.2 | Exclude from MVP |
| Negative balance/lapse/encashment | Balance | 4D.2/advanced | Safe disabled defaults |
| Cancellation timing/effects | Cancellation/balance/workflow | 4D.6 | No runtime commands until approved |
| Modify model | Request revisions/balance | 4D.3/4D.6 | Immutable revision/replacement |
| Approval routing/delegation/escalation | Workflow/manager adapter | Approval phase | One explicit stage initially |
| Reservation timing | Balance/request/approval | 4D.2/4D.4 | Reserve at submit for finite balances; approve exact effects |
| Retroactive policies | Policy/balance/request history | 4D.1/4D.2 | Prohibit initially |
| Employment change during request | Request/policy/approval | 4D.3 | Snapshot at evaluation; approve revalidation policy |
| Termination/deactivation handling | Identity/request/workflow | Apply Leave/approval | Explicit policy for existing pending requests |

## 34. Runtime Blocker Classification

These decisions are not blockers to writing 4D.1 contracts, but they are blockers to production behavior in the indicated slices:

- **Blocks 4D.1 Policy Evaluator behavior:** policy effective-date selection; authoritative business-date/timezone source; effective-employment/service snapshot boundary; confirmation/notice source if the evaluator includes those checks; applicability interpretation of `CountryLocationId` versus any future independent Location dimension.
- **Blocks 4D.2 Balance Engine:** finite balance identity and policy-change treatment; service/eligibility inputs; accrual due timing; proration; rounding; carry-forward; negative balance; lapse; encashment; external grant authority; retroactive-policy reconciliation.
- **Blocks 4D.3 Leave Request Foundation:** cross-boundary request strategy; Month limit semantics; authoritative employment-change snapshot rule; overlap semantics; exact RequestDay snapshot contract; whether required attachments can be submitted without storage.
- **Blocks Apply Leave:** exact holiday/weekly-off sources; Sandwich algorithm; Clubbing adjacency; attachment persistence/validation; request timing/cutoffs; identity and business timezone finalization; request idempotency contract.
- **Blocks Approval:** routing, state transitions, delegation/escalation, manager eligibility, approval idempotency, and re-notification.
- **Can defer beyond initial Apply Leave:** quarter-day/hour/shift support, advanced carry-forward/encashment, statutory formulas, Payroll/Attendance integration, and optional document-provider implementation when the MVP excludes required uploads.

## 35. MVP Runtime Recommendation

After the decision gates, the safest MVP is full-day and half-day only; StartDate policy selection with cross-boundary rejection; independent LeavePeriod resolution; Allocated, Unlimited, and NoBalanceRequired; upfront entitlement first; no proration, carry-forward, rounding, negative balance, lapse, encashment, ExternalGrant execution, runtime Clubbing, or file upload. Request rules and calendar treatment should run only where their source semantics are approved. No modification or cancellation command should be enabled until its runtime effects are designed.

This recommendation is not an approval of the unresolved choices. It is the smallest implementation candidate that preserves historical correctness and avoids fake balances or guessed date semantics.

## 36. Recommended Phase Plan

- **4D.1 Runtime Policy Evaluator Foundation:** implement typed outcomes/contracts and adapters for tenant, identity, employment snapshot, LeavePeriod, and policy resolution; resolve date/timezone gates before enabling behavior.
- **4D.2 Balance and Immutable Ledger:** implement finite balance projection, transactions, reservation concurrency, and idempotent opening/accrual/manual adjustments.
- **4D.3 Leave Request Foundation:** implement request/revision/event and RequestDay schema with policy/period/employment snapshot references.
- **4D.4 Apply Leave Preview and Submission:** implement authoritative validation, overlap checks, idempotent submission, and reservation.
- **4D.5 Approval Foundation:** integrate a bounded approval boundary and manager resolver adapter.
- **4D.6 Withdraw, Cancel, and Modify:** implement only after timing, state, balance, attendance, and payroll effects are approved; prefer immutable revisions.
- **4D.7 Accrual and Grants:** add hybrid scheduler/catch-up, proration/rounding/carry-forward only after decisions.
- **4D.8 Advanced Calendar, Clubbing, Attachments, and integrations:** enable each independently after its runtime semantics and data sources are frozen.

## 37. Final Readiness Decision

**READY TO IMPLEMENT PHASE 4D.1 RUNTIME POLICY EVALUATOR FOUNDATION**

This means the hardened configuration foundation supports design and implementation of the evaluator's typed, non-runtime-contract foundation. It does not authorize Apply Leave, balances, ledger, accrual jobs, approvals, or runtime date calculations. The effective-date, timezone, employment-source, and cross-boundary decisions above must be treated as gates before production evaluator behavior is enabled.

# Phase 4D.2 — Balance & Immutable Ledger Foundation Design

Status: DESIGN ONLY — no balance/ledger entities, DbSets, services, migrations, transactions, or database operations are implemented.

## Design scope and repository patterns

This section extends the Phase 4D architecture using the repository's existing conventions: tenant-owned entities implement `ITenantEntity`; `HrmsDbContext` applies global tenant filters and stamps/guards tenant IDs; application services use `Result<T>` and ProblemDetails mappings; configuration writes use expected concurrency tokens and 409 conflicts; service workflows use `BeginTransactionAsync`; and history is append-oriented with restrictive deletes. The existing Phase 4 design also names `EmployeeLeaveBalance` as a rebuildable projection and `LeaveBalanceTransaction` as the append-oriented ledger. Those names are retained here; “LeaveBalance” below means that employee balance projection.

No existing runtime transaction, idempotency, or ledger infrastructure was found that can be reused as a complete Leave balance implementation. The existing audit/event and transaction patterns should be reused, but a centralized Leave transaction poster and runtime schema remain future work.

## 1. Business concepts

- **Entitlement configuration** is the published policy statement describing what may be granted and how it is credited.
- **Balance** is the current usable finite Leave state for one Employee, LeaveType, and LeavePeriod.
- **Ledger** is the immutable historical record of every balance-changing business transaction.
- **Reservation** temporarily holds quantity for an in-flight request so concurrent requests cannot spend it twice.
- **Consumption** permanently charges quantity after the applicable approval/state transition.
- **Accrual** creates a policy-authorized entitlement credit.
- **External grant** records a credit whose authoritative source is outside the Leave balance service.

These are not interchangeable. Policy configuration is not a balance, a balance projection is not historical truth, and a pending reservation is not consumption.

## 2. Source of truth recommendation

Recommend **immutable ledger plus materialized balance projection**. A ledger-only design maximizes conceptual purity but makes every balance read and concurrency decision expensive. A mutable-balance-only design is fast but loses auditability and makes repair/reconciliation unsafe. The combined model provides:

- immutable audit and historical explanation;
- fast current reads from the projection;
- reconstruction and reconciliation from ledger rows;
- a narrow, lockable balance row for finite-balance concurrency;
- explicit compensating transactions rather than destructive edits for corrections and future retroactivity.

The ledger is authoritative. `EmployeeLeaveBalance` is a rebuildable projection/cache with a revision and last-ledger sequence. No code path may mutate the projection without appending the corresponding ledger entry in the same database transaction.

## 3. Balance identity and LeavePeriod

The minimum stable balance key is:

```text
TenantId + EmployeeId + LeaveTypeId + LeavePeriodId
```

`PolicyId`, `PolicyVersionId`, `LeavePolicyRuleId`, entitlement source, and employment-history ID must not be part of the balance identity. A policy can change during one LeavePeriod; fragmenting the balance would make available quantity and consumption history misleading. Each policy-authorized ledger transaction should instead retain nullable `PolicyVersionId` and `LeavePolicyRuleId` references.

Every finite `Allocated` balance belongs to exactly one independently resolved LeavePeriod. The balance implementation must obtain that period through `ILeavePeriodResolver` for the transaction's business effective date and fail on `NotConfigured` or `ConfigurationAmbiguity`. Policy and period resolution remain separate. `Unlimited` and `NoBalanceRequired` do not require a finite balance row.

## 4. Entitlement modes and sources

| Configuration | Balance representation | Credit authority |
|---|---|---|
| `Allocated` + `PolicyAccrual` | finite `EmployeeLeaveBalance`, created lazily when the first valid transaction is posted | policy accrual component |
| `Allocated` + `ExternalGrant` | finite balance, created when an authoritative grant arrives | external grant boundary |
| `Unlimited` | no artificial large balance; typed policy/read-model state | no finite credit required |
| `NoBalanceRequired` | no finite balance and no paid-balance debit | request/policy path only |

`Unlimited` must never be represented as `999999`, `decimal.MaxValue`, or a fabricated balance. `NoBalanceRequired` must never be represented as zero available, negative paid balance, or a fake infinite balance. Request Rules, eligibility, calendar treatment, and future approval can still apply to both modes.

`ExternalGrant` should use a future `ILeaveEntitlementGrantSource`/`IExternalLeaveGrantService` boundary. The source supplies an authoritative event ID/reference, quantity, effective date, tenant, Employee, LeaveType, and idempotency key. Attendance, overtime conversion, manager grants, and Comp Off earning are not assumed or implemented.

## 5. Proposed EmployeeLeaveBalance

**PROPOSED — NOT IMPLEMENTED.**

Candidate fields:

- `Id`, `TenantId`, `EmployeeId`, `LeaveTypeId`, `LeavePeriodId`;
- `GrantedQuantity`, `ReservedQuantity`, and `ConsumedQuantity` as `decimal(9,3)`;
- a SQL Server `rowversion`/concurrency token;
- a monotonic `LastLedgerSequence` or equivalent projection revision;
- created/updated audit metadata.

The balance should not store `AvailableQuantity` as an independently mutable fact. Calculate it as `GrantedQuantity - ReservedQuantity - ConsumedQuantity` for the initial finite model. Future expiry/carry-forward/encashment projections may introduce additional explicitly modeled components, but they must preserve ledger reconstruction.

Recommended unique index: `(TenantId, EmployeeId, LeaveTypeId, LeavePeriodId)`. Employee, LeaveType, and LeavePeriod references must be tenant-aware composite relationships with restrictive deletes.

## 6. Balance invariants

For the initial safe model:

```text
GrantedQuantity >= 0
ReservedQuantity >= 0
ConsumedQuantity >= 0
ReservedQuantity + ConsumedQuantity <= GrantedQuantity
AvailableQuantity = GrantedQuantity - ReservedQuantity - ConsumedQuantity
```

Negative-balance allowance is not a required capability and remains disabled. A future approved negative-balance policy would require a separate invariant and evaluator decision; it must not weaken the initial model implicitly.

An Allocated balance may validly begin at zero when accrual is scheduled. Configured entitlement quantity is not the same thing as currently granted quantity.

## 7. Proposed immutable ledger

**PROPOSED — NOT IMPLEMENTED.** The canonical name is `LeaveBalanceTransaction`.

Required initial fields:

- immutable event `Id`;
- `TenantId`, `EmployeeId`, `LeaveTypeId`, `LeavePeriodId`, and nullable `EmployeeLeaveBalanceId` if a projection exists;
- typed `TransactionType`;
- positive `Quantity` using `decimal(9,3)`;
- `EffectiveDate` as a DateOnly business date;
- `OccurredAtUtc`/recorded timestamp for when the system posted it;
- idempotency key and source type/reference;
- correlation ID;
- nullable authorizing `PolicyVersionId` and `LeavePolicyRuleId`;
- actor/source metadata sufficient to distinguish user, system, and external integration.

Request IDs, approval IDs, and event IDs should become nullable foreign-key references only in the later request/approval migration slices. The balance foundation must not create a premature LeaveRequest dependency.

## 8. Ledger quantity convention and transaction types

Use **positive quantities plus transaction type direction**, rather than signed deltas. This is clearer in audit views and prevents a negative sign from being accidentally applied twice. The poster maps each typed transaction to projection components.

Required for the first balance/ledger foundation:

- `Opening`;
- `Accrual`;
- `ExternalGrant`;
- `ManualAdjustment` (permissioned and reasoned; implementation may follow the foundation if approved);
- a generic compensating correction type only if needed by the first implementation.

Reserved for later request/advanced phases:

- `RequestReservation`;
- `ReservationRelease`;
- `ApprovalConsumption`;
- `CancellationCredit`;
- `CarryForward`;
- `Expiry`;
- `Encashment`.

The existing Phase 4 design names equivalent `RequestReservation`, `ApprovalConsumption`, `RejectionRelease`, `CancellationCredit`, `CarryForward`, `Encashment`, and `Expiry` concepts. Their final names should be frozen with the implementation contract; this design does not create them.

## 9. Reservation and consumption model

The safest future request model is reserve at submission for finite Allocated balances. Validating without reservation permits two 4-day pending requests to pass against a 5-day balance. Reserving only at approval creates the same race unless approval serializes and revalidates, and gives users a misleading pending state.

Conceptual flow:

```text
submit -> validate -> atomically reserve -> PendingApproval
approve -> atomically convert reservation to consumption
reject/withdraw -> atomically release reservation
cancel -> future compensating credit, only after cancellation semantics are approved
```

Reservation should be represented as ledger transactions affecting the materialized projection, not as a second authoritative state store. A separate reservation table is not recommended initially. The request ID becomes the source reference once LeaveRequest exists. In 4D.2, reservation transaction types and request FKs should remain deferred until 4D.3 creates the request aggregate; the poster contract can be designed now with nullable source metadata.

## 10. Centralized transaction poster

Propose `ILeaveBalanceTransactionPoster` as the only balance mutation boundary. It should:

1. validate tenant, mode, period, quantity, source, and idempotency;
2. obtain or create the finite balance safely;
3. lock or conditionally update the balance scope;
4. enforce the balance invariants;
5. append one immutable ledger row;
6. update the materialized projection and ledger sequence;
7. commit everything atomically.

No admin, accrual, grant, request, or cancellation service should assign balance quantities directly. Even a manual correction is a ledger transaction with actor, reason, correlation, and expected revision.

Suggested future interfaces are separate responsibilities:

- `ILeaveBalanceReader`: typed finite/unlimited/no-balance read model;
- `ILeaveBalanceTransactionPoster`: atomic ledger + projection mutation;
- `ILeaveBalanceReservationService`: request-linked reserve/release/consume orchestration;
- `ILeaveAccrualScheduleCalculator`: due occurrence calculation only;
- `ILeaveEntitlementGrantSource`: external authority adapter.

There should be no single service that calculates policy, schedules accrual, mutates balances, and owns requests.

## 11. Concurrency strategy

Use a SQL Server `rowversion` on `EmployeeLeaveBalance` for optimistic stale-write detection. The existing audit timestamp token is appropriate for low-contention configuration, but a financial-like balance projection needs a database-generated concurrency token. The primary reservation/consumption strategy should be a short transaction with a conditional balance update guarded by the expected revision, or a narrowly scoped `UPDLOCK` on the one balance row where EF cannot express the condition safely.

Conceptually reserve only when:

```text
GrantedQuantity - ReservedQuantity - ConsumedQuantity >= requestedQuantity
```

and the expected rowversion/revision still matches. A failed condition returns `InsufficientBalance` or `BalanceConflict`, not a partial reservation. Avoid broad serializable locks; use deterministic lock order and keep transactions short. Provider-recognized deadlocks may be retried a bounded number of times only for idempotent operations.

Real SQL Server tests are mandatory before claiming this correct. SQLite/in-memory tests cannot validate rowversion, unique-index races, lock behavior, isolation, or deadlock recovery.

## 12. Idempotency and uniqueness

Every business credit/debit/reservation event needs a stable idempotency key independent of its immutable event ID. Recommended unique key:

```text
TenantId + IdempotencyKey
```

The key must be operation-specific and payload-fingerprinted. Reusing a key with a different payload is a conflict; reusing it with the same payload returns the original outcome. Source metadata should also be retained for diagnosis. Examples include employee/LeaveType/period/accrual-occurrence for accrual and request ID plus operation for reservation/consumption.

The database unique index is the final duplicate barrier. The poster first checks existing idempotency state, then handles a unique-key race by reading and returning the winning result. Timestamps are never uniqueness keys.

## 13. Effective and posted dates

`EffectiveDate` is the business date to which a transaction belongs. `OccurredAtUtc` is the recording timestamp. They must remain separate for backdated approved adjustments, accrual due dates, and external grants. Explicit DateOnly input remains authoritative for manual or future command operations. Scheduled operations require an authoritative tenant timezone/business-date provider; server local time and UTC date conversion are not acceptable substitutes.

Policy accrual should resolve the policy effective on the accrual due/effective date, not whichever policy is current when a delayed job runs. Already posted ledger rows are not silently recomputed after policy edits.

## 14. Reconciliation and rebuild

Provide a future read-only reconciliation service/report that recomputes the projection from immutable ledger rows and compares it with `EmployeeLeaveBalance`, including granted, reserved, consumed, and last-ledger sequence. A mismatch is a high-severity diagnostic and should stop further automated mutation for the affected scope until reviewed; repair must be a controlled rebuild or compensating transaction, never an arbitrary balance overwrite.

Ledger rows must contain enough source/type/quantity/date information to reconstruct reservations: reservation increases reserved, release decreases reserved, and consumption decreases reserved while increasing consumed. This reconstruction rule must be finalized before request-linked transaction implementation.

## 15. Accrual architecture

Separate due-occurrence calculation from ledger posting. Proposed contracts are `ILeaveAccrualScheduleCalculator` and the centralized transaction poster. The calculator determines whether an occurrence is due; it does not mutate balances. The poster validates eligibility, period, policy references, idempotency, and atomic ledger/projection updates.

Recommend **scheduled posting plus idempotent catch-up verification** rather than scheduler-only or lazy-only accrual. Scheduled work gives predictable auditability and scale; catch-up repairs missed runs and supports operational recovery. Lazy balance reads must not silently create financial credits unless a separately approved policy explicitly allows it.

Current active configuration is `None`, `Upfront`, `Monthly`, `SemiAnnual`, and `Annual`; `Quarterly` remains reserved and configuration-rejected. Upfront due date, monthly boundary, joining-month treatment, rehire, semiannual/annual cycle anchors, proration, rounding, and timezone still require decisions. Accrual must be LeavePeriod-aware rather than assuming January–December.

## 16. Initialization strategy

Recommend lazy, concurrency-safe balance creation on the first valid finite transaction, protected by the unique balance key. Do not eagerly create the Cartesian product of every Employee, LeaveType, and LeavePeriod. A concurrent creator must either observe the existing unique row or retry by reading it; it must not post duplicate Opening transactions.

`Opening` is a ledger transaction, not a direct seeded quantity. Legacy opening balances require a future controlled import/reconciliation path with actor/source/reason and idempotency. No balance is initialized when policy resolution is missing or ambiguous.

## 17. Policy changes, employment changes, and lifecycle

Balance identity remains continuous across a policy change within a LeavePeriod. New credits carry the new policy references; existing consumed quantity is not rewritten. Increases/decreases, schedule changes, retroactive effects, and existing requests require an explicit reconciliation decision. The initial runtime should prohibit or restrict retroactive published versions that would require automatic balance recalculation.

Employment changes in Department, Grade, Location, or Employee Type should not fragment an existing Employee/LeaveType/LeavePeriod balance by default. Future credits may use the new applicable policy; past ledger entries retain their original policy references. This continuity recommendation requires business confirmation where entitlement is legally tied to a classification.

Termination must preserve all balances and ledger history. Lapse, final settlement, and encashment are future operations. Rehire semantics—same EmployeeId/history, reuse of a period balance, or new balance treatment—remain unresolved and block affected initialization/accrual behavior.

## 18. Read-model outcomes

Future balance reads should return a typed mode and avoid false quantities:

- finite Allocated: period, Granted, Reserved, Consumed, Available, and as-of information;
- Unlimited: `mode = Unlimited`, `availableQuantity = null`, display meaning “Unlimited”;
- NoBalanceRequired: `mode = NoBalanceRequired`, no zero quantity implying denial.

Useful typed errors include `BalanceNotApplicable`, `BalanceNotInitialized`, `InsufficientBalance`, `BalanceConflict`, `BalanceConfigurationAmbiguity`, and `AccrualPending`. Missing policy and missing period remain configuration errors, not zero balances.

## 19. Audit, source, and actor

The ledger is business history; generic audit remains useful for administrative field changes. Every ledger row needs source category such as `User`, `System`, or `External`, optional `ActorUserId`/`ActorEmployeeId`, source reference, correlation ID, and reason where relevant. Scheduled processes must not impersonate a fake human account. An accrual batch may have a system source and a batch correlation ID; an external grant retains the external event reference.

## 20. Tenant safety and delete behavior

All proposed balance and ledger rows are tenant-owned and require global filters, explicit tenant predicates, and tenant-aware composite FKs to Employee, LeaveType, LeavePeriod, and balance. Self-service reads derive Employee identity through `IEmployeeIdentityResolver`; HR/admin reads require explicit permissions and subject selection.

Referenced Employee, LeaveType, LeavePeriod, balance, and ledger history must use Restrict/NoAction deletes. Existing soft-deactivation conventions remain the lifecycle mechanism. No cascade may erase ledger entries or a historical balance projection.

## 21. Future migration/table classification

| Proposed table | Classification | Initial scope |
|---|---|---|
| `EmployeeLeaveBalances` | Required for 4D.2 implementation | finite projection, unique balance key, rowversion |
| `LeaveBalanceTransactions` | Required for 4D.2 implementation | immutable ledger, typed transaction, idempotency/source keys |
| `LeaveBalanceReservations` | Not recommended initially | ledger-based reservations avoid a second authority |
| `LeaveAccrualRuns` | Future/optional operational table | scheduler observability only; ledger idempotency remains authoritative |
| `LeaveRequests` / `LeaveRequestDays` | Defer to 4D.3 | request aggregate and date snapshots |
| `LeaveRequestEvents` / approvals | Defer to request/approval phases | workflow and business history |

The eventual 4D.2 implementation migration should create only the balance projection and ledger tables, plus their tenant-aware keys, indexes, constraints, and concurrency columns. It must not create request, approval, accrual-run, attachment, calendar, or integration tables.

## 22. Future test strategy

Unit/service tests should cover quantity direction, invariants, mode behavior, idempotency decisions, policy/period references, and reconciliation calculations. EF model tests should cover tenant filters, composite FKs, unique balance keys, restrictive deletes, precision, and rowversion mapping.

Real SQL Server integration tests are required for concurrent credit, concurrent reservation, unique-key races, rowversion conflicts, atomic ledger/projection updates, transaction isolation, and bounded deadlock retry. SQLite and EF InMemory are insufficient evidence for those behaviors.

## 23. Balance-specific open decision register

| Decision | Affected component | Required before | Recommended direction | Risk if deferred |
|---|---|---|---|---|
| Tenant/business timezone | scheduled accrual and date-dependent commands | accrual execution | authoritative tenant provider | wrong due dates and notice/cutoff behavior |
| Initial/upfront grant date | accrual/initialization | 4D.2 accrual-capable implementation | approve period/eligibility date explicitly | duplicate or mistimed grants |
| Monthly/semiannual/annual anchor | accrual scheduler | accrual phase | derive from LeavePeriod only after approval | calendar-year assumptions |
| Service and rehire interaction | eligibility/accrual | accrual and Apply Leave | explicit service-start and continuity policy | over/under-granting |
| Eligibility source completeness | accrual and request | accrual | authoritative confirmation/notice sources | credits without eligibility proof |
| Proration and rounding | accrual quantities | balance/accrual | exclude MVP | inconsistent fractions |
| Carry-forward and expiry | period transition | advanced balance | ledger credits/debits with explicit ordering | unreconcilable balances |
| Negative balance | reservation/availability | balance implementation if enabled | disabled initially | unpaid Leave confusion and overspend |
| Lapse/encashment | period close/payroll | advanced balance | defer | incorrect statutory/payroll effects |
| External grant authority | grant adapter | external grants | source-event authority contract | duplicate/untrusted credits |
| Reservation timing | request/balance | 4D.3/Apply Leave | reserve at submission | double spending or misleading pending state |
| Cancellation re-credit | request/balance | cancellation runtime | compensating ledger entry | incorrect restoration |
| Manual adjustment permissions | admin poster | 4D.2 implementation | dedicated permission/reason/revision | unauthorized balance changes |
| Retroactive policy reconciliation | policy/balance | balance + policy lifecycle | prohibit initially; explicit reconciliation later | silent historical mutation |

## 24. Blocker classification

- **Blocks 4D.2 Balance/Ledger Foundation implementation:** no structural blocker remains for designing finite balance + immutable ledger tables, provided the implementation excludes unresolved advanced semantics. The exact projection name and transaction enum names should be confirmed before coding, but this is a bounded naming gate rather than a business blocker.
- **Blocks accrual implementation:** timezone, upfront date, cycle anchors, service/rehire, eligibility-source completeness, proration, rounding, and external-grant authority.
- **Blocks Apply Leave:** reservation timing and request linkage, exact balance check timing, cross-boundary requests, calendar/Clubbing semantics, attachment storage, and identity/business-date completion.
- **Can defer:** carry-forward, lapse, encashment, negative balance, Payroll/Attendance integration, advanced fractional units, and scheduler operational observability when not needed by the first accrual slice.

## 25. Recommended 4D.2 implementation boundary

The next implementation phase should create only:

1. `EmployeeLeaveBalance` with tenant-safe identity, decimal(9,3) quantities, and rowversion;
2. `LeaveBalanceTransaction` with immutable positive quantity, typed source/effective/posted metadata, tenant-safe references, and unique idempotency key;
3. centralized transaction posting for Opening, Accrual, and ExternalGrant only;
4. typed finite/unlimited/no-balance read behavior;
5. invariant and projection-equivalence tests;
6. SQL Server integration coverage before concurrency claims.

Request reservation/consumption transaction types should be enabled only after LeaveRequest exists in 4D.3, while the balance poster's extension point can be designed now. No accrual scheduler, LeaveRequest, Apply Leave, approval, cancellation, calendar, Clubbing, attachment, or runtime evaluator is part of this implementation boundary.

## 26. Phase 4D.2 Design Readiness

**READY TO IMPLEMENT PHASE 4D.2 BALANCE/IMMUTABLE LEDGER FOUNDATION**

This readiness is limited to the bounded finite-balance projection, immutable ledger, idempotency, tenant integrity, and concurrency foundation. It does not approve unresolved accrual, reservation workflow, LeaveRequest, approval, cancellation, retroactive reconciliation, or advanced entitlement semantics.

## 27. Phase 4D.2 Implementation Status

The bounded balance/ledger foundation is implemented in the application and persistence layers. `EmployeeLeaveBalance` is a tenant-owned, lazily created materialized projection keyed uniquely by `TenantId + EmployeeId + LeaveTypeId + LeavePeriodId`; it stores `GrantedQuantity`, `ReservedQuantity`, and `ConsumedQuantity` at `decimal(9,3)` precision, derives Available as `Granted - Reserved - Consumed`, and uses SQL Server `rowversion` for projection concurrency. Unlimited and NoBalanceRequired entitlement modes deliberately do not create finite balance rows or fake quantities.

`LeaveBalanceTransaction` is append-only business history. Its quantity is always positive and the initial poster supports only the unambiguous credit types `Opening`, `Accrual`, and `ExternalGrant`; each increases Granted while leaving Reserved and Consumed unchanged. Generic ManualAdjustment, Reservation, ReservationRelease, Consumption, CancellationCredit, carry-forward, expiry, and encashment remain deferred. Policy version/rule references are historical metadata and are not part of balance identity. EffectiveDate is a DateOnly business date; OccurredAtUtc is the immutable posting timestamp.

`ILeaveBalanceTransactionPoster` is the internal mutation boundary. It validates tenant-owned Employee, LeaveType, LeavePeriod, optional policy references, actor references, positive quantity, source rules, and idempotency. A SHA-256 semantic payload fingerprint distinguishes a safe same-payload retry from idempotency-key reuse with a different payload. The balance lookup/create, projection update, ledger append, and commit are one database transaction; the unique balance and tenant/idempotency indexes remain the database authority for races. `ILeaveBalanceReader` exposes a read-only finite projection and returns no-balance-found rather than interpreting absence as Unlimited or NoBalanceRequired.

The additive migration is `20260904160000_LeaveBalanceLedgerFoundation`, introducing only `EmployeeLeaveBalances` and `LeaveBalanceTransactions`, with tenant-aware restrictive foreign keys, unique business/idempotency indexes, quantity check constraints, and rowversion. It has not been applied. Existing migration scaffolding could not be executed in the Codex environment because Infrastructure asset resolution exits with no compiler diagnostics; the checked-in migration is limited to the approved model and received a static safety review.

Automated foundation tests cover lazy creation, balance reuse, credit projection effects, positive quantity, idempotency replay/conflict, cross-tenant rejection, missing finite balance, and ledger update protection. SQL Server-specific rowversion, uniqueness-race, locking, and deadlock behavior remains outstanding acceptance evidence; no scheduler, request, reservation, approval, cancellation, or other Leave runtime was implemented.

## Phase 4D.3 — LeaveRequest Aggregate & RequestDay Foundation Design

This section is design-only. `LeaveRequest`, `LeaveRequestDay`, request events, request APIs, reservation transactions, and request migrations are **PROPOSED — NOT IMPLEMENTED**. It depends on the 4D.1 policy/period resolvers and the 4D.2 balance/ledger foundation, but does not alter either implementation.

### 1. Aggregate boundary

`LeaveRequest` should be the aggregate root for one employee-submitted Leave intent and its date-level calculation snapshot. It owns request dates, request quantities, current lifecycle state, policy/period context, and its RequestDay children. It must not own manager discovery, approval routing, balance projection mutation, document storage, or calendar master data.

The minimum required identity/state is: `Id`, server-derived `TenantId`, server-derived `EmployeeId`, selected `LeaveTypeId`, `StartDate`, `EndDate`, requested and chargeable totals, initial status, creation/submission timestamps, an idempotency key, and a SQL Server rowversion. `LeavePeriodId`, `LeavePolicyVersionId`, and `LeavePolicyRuleId` are required historical references once resolution succeeds. Reason/comment is a bounded business field only if the existing product requirement confirms it; rich text is not needed. Current totals are denormalized summaries whose invariants must equal the RequestDay projection.

`CreatedAt`/`SubmittedAt` are audit timestamps, not business dates. A future `BusinessSubmissionDate` must come from the tenant business-date provider and must not be derived from server local time or UTC date.

### 2. Identity and tenant

Self-service creation must obtain EmployeeId only from `IEmployeeIdentityResolver`; the client must not select an arbitrary subject EmployeeId. TenantId comes from authenticated tenant context and is stamped/validated server-side. HR/admin creation is a separate permissioned command with explicit subject selection and ownership checks; it must not be an optional EmployeeId branch on the self-service endpoint.

LeaveType selection may send a stable LeaveTypeId, which the server validates for tenant ownership, active/current availability, and a matching published policy. Names and codes are display/lookup values, never request identity.

### 3. Policy, employment, and period references

The request should retain the resolved `LeavePeriodId`, `LeavePolicyVersionId`, and `LeavePolicyRuleId`; these are authoritative historical references, not instructions to re-resolve current configuration. The effective employment history row used for the decision should also be retained as `EmployeeEmploymentHistoryId` or represented by a compact decision snapshot. The preferred first form is the history-row reference plus a compact snapshot of the applicability values actually used, because it explains the decision without copying the whole employment record.

The compact policy decision snapshot should contain only decision outputs needed for explanation: selected policy/rule identity, priority/specificity, eligibility outcome, entitlement mode/source, request-rule version values, and calendar/attachment decisions when those evaluators are implemented. It must not become an unbounded JSON copy of policy configuration.

Gender is a current applicability dimension, but it is sourced from `Employee.Gender`, not dated employment history. This is a historical-explainability risk: a later demographic correction could make a recomputation differ from the original decision. The request should snapshot the Gender value used (or the resulting applicability decision) while the system lacks demographic history. This does not modify the Employee model.

### 4. Effective-date strategy and request boundaries

The resolver accepts an explicit DateOnly; it must not choose a request date internally. The business decision for whether StartDate, every requested date, or submission date selects policy remains open. The safest initial recommendation is: resolve using the requested effective date supplied by the caller and require one policy context for the entire initial request. A request crossing a policy-version, LeavePeriod, or effective-employment boundary should be rejected and submitted as separate requests until per-day policy resolution is approved.

Per-day resolution is more flexible but requires multiple policy snapshots, potentially multiple balances/reservations, and clearer cross-boundary approval semantics. One request spanning multiple contexts should not be silently split. Cross-Policy, cross-LeavePeriod, and cross-employment behavior therefore blocks Apply Leave implementation, not this design.

LeavePeriod remains independent from Policy. With the initial one-period boundary, `LeaveRequest.LeavePeriodId` is sufficient; if cross-period requests are later approved, `LeaveRequestDay.LeavePeriodId` becomes required and the aggregate must coordinate multiple balances.

### 5. Proposed LeaveRequestDay model

A separate `LeaveRequestDay` is recommended and is **REQUIRED FOR 4D.3 REQUEST FOUNDATION**. Calendar, Sandwich, partial-day, overlap, and historical explanation are date-level concerns that do not fit safely in only StartDate/EndDate totals.

Required day concepts are: `Id`, `TenantId`, `LeaveRequestId`, `Date`, employee-requested quantity, chargeable quantity, a persisted day classification/calculation reason, and an `IsRequestedByEmployee` flag. `LeavePeriodId` and per-day policy references are future-extensible fields and are not required when the initial boundary guarantees one context. `IsHoliday`, `IsWeekOff`, and `IsSandwich` should be represented as a stable classification/flags snapshot only after the calendar evaluator contract is approved; they are not current runtime behavior.

RequestDay quantities use `decimal(9,3)` and initially permit `1.000` and `0.500` only. `HalfDayAllowed` does not freeze FirstHalf/SecondHalf semantics, so no half-day portion enum should be invented. Until portion semantics are approved, the initial overlap rule should conservatively prevent two independently submitted half-day claims for the same date.

If summary totals are stored on the request, enforce:

```text
RequestedQuantity = sum(RequestDay.RequestedQuantity)
ChargeableQuantity = sum(RequestDay.ChargeableQuantity)
StartDate = min(RequestDay.Date)
EndDate = max(RequestDay.Date)
```

These are aggregate invariants, not independently editable fields.

### 6. Calendar and Sandwich snapshots

The future `ILeaveBusinessCalendarResolver`/`IEmployeeBusinessCalendar` should accept Tenant, Employee, and DateOnly and return working-day, holiday, weekly-off, and source information. Holiday and weekly-off masters are unresolved and must not be added here.

For historical correctness, the submitted RequestDay should persist the classification and calculation reason used at submission. Recomputing against a changed calendar years later is not authoritative. A Sandwich-generated date may be stored as a RequestDay with `IsRequestedByEmployee = false`, `ChargeableQuantity > 0`, and reason `Sandwich` so the employee and support staff can explain the charge. Exact prefix/suffix search limits, holiday/week-off bridging, and whether generated days participate in every rule remain business decisions and block Apply Leave.

### 7. Clubbing boundary

The future `ILeaveClubbingEvaluator` should consume the new request’s date/day context, existing relevant request states, and the business calendar, and return Allowed, NotAllowed, or an inability/ambiguity outcome. The current symmetric `NotAllowed` pair configuration is reusable, but exact adjacency is not frozen: immediate calendar day versus working day, holiday/week-off gaps, half-days, pending requests, and multi-request chains all require approval. Clubbing must remain separate from overlap and Sandwich evaluation.

### 8. Attachment requirement boundary

`ILeaveAttachmentRequirementEvaluator` should return NotRequired, Optional, or Required(DocumentLabel) from the resolved policy and the authoritative quantity selected by business design. Whether `RequiredAboveQuantity` uses employee-requested or chargeable quantity is unresolved and blocks attachment enforcement. `LeaveRequestAttachment` and file storage should be deferred; requirement evaluation must not create upload/storage tables in the request foundation.

### 9. Overlap and quantity rules

Overlap is independent of Clubbing. A future query should use tenant, Employee, date range, and lifecycle status, with RequestDay-level comparison for exact dates. Rejected, Withdrawn, and Cancelled requests should not consume overlap capacity; whether Submitted/PendingApproval count, and how Approved overlap interacts with half-days, require business confirmation. A unique RequestDay key should prevent duplicate `(TenantId, LeaveRequestId, Date)` rows initially; a future portion key may be added only when first/second-half semantics are frozen.

RequestRule limits must be applied after authoritative day/chargeable calculation only if business confirms that minimum/maximum and consecutive limits use chargeable quantity. Month-based limit semantics remain open; no Gregorian-month fallback is allowed. LeavePeriod-based limits can use `ILeavePeriodResolver`, but the statuses included in counts and quantities must be approved.

### 10. Request state machine

The minimal extensible lifecycle is `PendingApproval`, `Approved`, `Rejected`, `Withdrawn`, and `Cancelled`; `Submitted` may be an auditable transition/event rather than a long-lived state if routing is immediate. A successful initial submission should enter PendingApproval when approval is required. Auto-approval can later transition Submitted/ PendingApproval to Approved without changing the aggregate shape.

Rejected is an approval outcome and must not consume balance. Withdrawn is employee retraction before a final state; Cancelled is distinct post-approval business cancellation. Timing and balance effects remain deferred. No generic status assignment should be exposed; future named transition commands must enforce allowed transitions and expected rowversion.

Modification should use an immutable revision/replacement model rather than mutating a submitted request. A new revision can retain the original request link, recalculate RequestDays, and coordinate reservation release/replacement later. Mutating historical submitted rows would make balance and audit reconstruction unsafe.

### 11. Request events and audit

`LeaveRequestEvent` is recommended as **REQUIRED FOR 4D.3** for the initial Submitted/Created event and immutable lifecycle history; approval-specific assignment tables remain deferred. Events are business history, separate from generic audit logs and the balance ledger. They should record request, tenant, event type, actor/source, UTC time, business date where relevant, revision, correlation/idempotency key, and state transition summary. Existing immutable `AccountEmployeeLinkEvent` conventions are the closest repository pattern.

### 12. Reservation and transaction boundary

For finite Allocated Leave, the approved balance design recommends reservation at successful submission. The future atomic boundary is:

```text
validate -> create Request + RequestDays -> reserve balance -> append reservation ledger -> PendingApproval
```

Failure in any step rolls back all steps. Approval converts reservation to consumption; rejection/withdrawal releases it; cancellation re-credit remains a later decision. Unlimited and NoBalanceRequired still create Request/RequestDays and run policy/request/calendar/approval checks, but skip finite balance lookup and reservation. They must not create fake balances or debit another LeaveType.

Reservation/Consumption ledger types and a `LeaveRequestId` FK should be added only after the request schema exists, in a later bounded migration. 4D.3 should not add a direct `EmployeeLeaveBalanceId` to the request; the balance is derived from Employee, LeaveType, and LeavePeriod, while future ledger source references provide traceability.

### 13. Idempotency, concurrency, and security

Future self-service POST should accept the repository/API-standard `Idempotency-Key`. Recommended request uniqueness is `TenantId + EmployeeId + IdempotencyKey`, with a canonical payload fingerprint. Same key and same semantic payload returns the original request; same key with a changed payload returns a 409 conflict. Final submission must recalculate all policy, employment, calendar, overlap, and balance facts; a preview is never authority.

Use SQL Server rowversion on LeaveRequest for approval, withdraw, cancel, and modify transitions. RequestDay rows become immutable after submission; a modification creates a new revision/replacement. Tenant-aware composite FKs and global filters are required, with Restrict/NoAction from Employee, LeaveType, Policy, Period, and request history. Request-to-RequestDay aggregate deletion should be prohibited after persistence rather than used to represent business statuses.

### 14. Validation pipeline and errors

The future Apply Leave pipeline should be ordered as: authenticate tenant; resolve linked Employee; validate LeaveType and dates; obtain business date/time context; resolve effective employment; resolve LeavePeriod; resolve Policy; evaluate eligibility; evaluate Request Rules; classify calendar days; apply approved Sandwich calculation; calculate requested/chargeable quantities; evaluate Clubbing; evaluate attachment requirement; detect overlap; interpret entitlement mode; reserve finite balance; atomically persist request/day/event state.

Typed Result/ProblemDetails outcomes should distinguish identity-not-linked, employment-not-found/ambiguous, period-not-configured/ambiguous, policy-not-configured/not-applicable/ambiguous, not-eligible, request-rule violation, calendar unavailable, Clubbing not allowed, attachment required, overlap, insufficient balance, concurrency conflict, and idempotency conflict. Employee-facing ambiguity messages should be safe and generic; diagnostics may retain conflicting internal IDs server-side.

### 15. Proposed APIs (not implemented)

The minimal future surface is `POST /api/leave/requests`, `GET /api/leave/requests/{id}`, and `GET /api/leave/self/requests`. Withdraw, cancel, modify, and approval endpoints should be added only with their runtime semantics. A non-mutating `POST /api/leave/requests/preview` is recommended because it can explain day classification, chargeable quantity, balance mode/impact, and attachment requirement, but the final POST must rerun every authoritative check.

Recommended future permissions follow existing namespacing: self request read/create permissions (or an explicitly documented authenticated-linked-employee capability), plus separate `Leave.RequestManage` and future `Leave.Approve`. No permission is seeded by this design.

### 16. Proposed tables and migration scope

| Proposed table | Classification | Scope |
|---|---|---|
| `LeaveRequests` | Required for 4D.3 implementation | request root, references, totals, status, rowversion, idempotency |
| `LeaveRequestDays` | Required for 4D.3 implementation | one row per requested/calculated date and quantity snapshot |
| `LeaveRequestEvents` | Recommended for 4D.3 implementation | immutable Created/Submitted and later lifecycle events |
| `LeaveRequestAttachments` | Defer to attachment/storage phase | file metadata/storage/retention |
| approval assignments/decisions | Defer to approval phase | routing and decisions |

The future 4D.3 migration should create only the request, day, and (if accepted) event tables, their tenant-aware keys/indexes, rowversions, and restrictive references. It must not create balance, reservation, approval, attachment, accrual, calendar, or integration tables. Before that migration, the malformed EF snapshot/scaffolding problem must be diagnosed and corrected in a separate hardening phase; it is a schema-implementation blocker, not a design blocker.

### 17. Open decision register and blockers

| Decision | Recommendation | Required before | Classification |
|---|---|---|---|
| Policy date for a multi-day request | one explicit caller-supplied context for MVP; reject boundary crossing | request runtime | Blocks 4D.4 Apply Leave |
| Cross-Policy/Period/employment requests | reject initially; do not silently split | request runtime | Blocks Apply Leave |
| Tenant business timezone | authoritative tenant provider | request submission | Blocks Apply Leave; not schema |
| Month limit semantics | approve calendar/month definition | request-rule evaluator | Blocks Apply Leave enforcement |
| Limit quantity basis | approve requested versus chargeable | request-rule evaluator | Blocks Apply Leave enforcement |
| Request-count statuses | approve Pending/Approved treatment | request-rule evaluator | Blocks Apply Leave enforcement |
| Half-day portion | approve whether and how first/second half is represented | overlap/UI | Blocks Apply Leave half-day correctness |
| Holiday/weekly-off source | approve authoritative provider | calendar evaluator | Blocks Apply Leave |
| Sandwich expansion | approve gap search and generated-day rules | calendar evaluator | Blocks Apply Leave |
| Clubbing adjacency | approve calendar/working-day and status semantics | Clubbing evaluator | Blocks Apply Leave |
| Attachment threshold basis/storage | approve quantity basis and persistence policy | attachment enforcement | Blocks Apply Leave attachment enforcement |
| Request initial status | PendingApproval recommendation | request runtime | Blocks approval integration |
| Request event timing | Created/Submitted event from day one | request schema | Can be decided before schema migration |
| Reservation timing/linkage | reserve at submission; add Request FK later | reservation runtime | Blocks Apply Leave/4D.4 |
| Modify model | immutable revision/replacement | modify runtime | Blocks Modify |
| Cancellation timing/re-credit | separate future design | cancellation runtime | Blocks Withdraw/Cancel |

The 4D.2 SQL Server validation debt—rowversion, unique-key races, transaction isolation, and deadlocks—does not block pure request schema design, but it blocks production-ready finite-balance reservation and therefore Apply Leave. The malformed snapshot issue blocks generating the 4D.3 schema migration and must be resolved first.

### 18. MVP recommendation and implementation plan

The safest initial Apply Leave MVP, subject to business approval, is one LeavePeriod, one PolicyRule, and one effective-employment snapshot per request; full-day/half-day quantities only; no quarter/hour/shift; no cross-boundary requests; finite balance reservation at submission; Unlimited/NoBalanceRequired with no balance; no runtime Clubbing until adjacency is frozen; no upload until attachment storage is approved; no Modify; and Withdraw only after its state/timing semantics are approved.

Recommended bounded sequence:

1. **4D.2A** — validate SQL Server balance behavior and repair the EF snapshot/scaffolding chain.
2. **4D.3A** — implement `LeaveRequests`, `LeaveRequestDays`, and accepted request events with tenant-safe EF mappings and idempotency/rowversion.
3. **4D.3B** — implement request validation components for identity, effective employment, period, policy, eligibility, and basic request rules.
4. **4D.4A** — implement non-mutating preview with authoritative recalculation on POST.
5. **4D.4B** — implement submission and finite-balance reservation atomically.
6. **4D.5** — implement approval boundary and decision history.
7. **4D.6** — implement rejection/withdrawal and reservation release, then separately cancellation/modify after decisions.

### 19. Phase 4D.3 readiness

**READY FOR PHASE 4D.2A VALIDATION/SNAPSHOT HARDENING AND THEN 4D.3 REQUEST SCHEMA IMPLEMENTATION**

This means the Request/RequestDay design is sufficiently bounded to proceed after the known snapshot and SQL Server validation dependencies are addressed. It does not authorize request schema creation in this phase, Apply Leave, reservation execution, approval, calendar/Clubbing runtime, attachments, cancellation, or modification.

## Phase 4D.2A — Balance/Ledger Validation & EF Snapshot Hardening Status

This phase was validation/hardening only. No LeaveRequest, LeaveRequestDay, reservation, consumption, accrual scheduler, approval, frontend, database operation, or new migration was added.

### Snapshot diagnosis and repair

The prior EF scaffolding failure was a genuine source artifact, not merely the Codex package-resolution failure. `HrmsDbContextModelSnapshot.cs` contained an out-of-order LeavePolicyRule relationship block: it attempted to declare detail navigations such as `EligibilityRule` before the corresponding `HasOne(...).WithOne(...)` metadata had been established. EF therefore materialized the string-named LeavePolicyRule as a shared/dictionary entity while processing that block and failed with `Navigation ... (Dictionary<string, object>).EligibilityRule was not found`. The snapshot also contained the expected property/relationship blocks for the later Leave configuration additions, so historical migration files were not rewritten.

The minimal repair removes only those premature detail-navigation declarations; the actual relationship declarations remain. The final snapshot additionally contains the two 4D.2 model definitions: `EmployeeLeaveBalance` and `LeaveBalanceTransaction`, including table names, decimal(9,3) quantities, rowversion, business/idempotency indexes, and check constraints. Existing non-Leave model content and all historical migration files remain intact. The current source snapshot has not been regenerated wholesale.

EF could not be rerun to prove a clean scaffold in this environment because the Infrastructure `ResolvePackageAssets` target exits with status 1 and no compiler diagnostics; `--no-build` consequently used the older compiled snapshot and reproduced the old error. This is separate from the source diagnosis. A normal PowerShell rebuild must occur before treating EF scaffolding as fully validated.

### Migration-chain and model review

The reviewed chain remains additive in the intended order: Leave policy foundation, eligibility, entitlement, request rules, calendar, attachment, Clubbing, cancellation, foundation hardening, and the 4D.2 balance/ledger foundation. No historical migration `.cs` or Designer file was modified in this phase. The permanent 4D.2 migration remains `20260904160000_LeaveBalanceLedgerFoundation`; no new migration was generated here and none has been applied.

The balance model conforms to the approved design: tenant-owned global filtering; tenant-aware restrictive references to Employee, LeaveType, LeavePeriod, and ledger balance; unique `TenantId + EmployeeId + LeaveTypeId + LeavePeriodId`; derived Available; rowversion concurrency; nonnegative projection checks; and decimal(9,3). The ledger model is append-only through the DbContext guard, has positive quantity checks, bounded source/correlation/idempotency/fingerprint fields, unique `TenantId + IdempotencyKey`, historical policy references, UTC occurrence time, DateOnly EffectiveDate, and restrictive tenant-aware references. Opening, Accrual, and ExternalGrant are the only poster-supported types; ManualAdjustment and request-related types remain deferred.

The fingerprint is a deterministic SHA-256 hex value over invariant, explicitly ordered semantic command values: tenant, Employee, LeaveType, LeavePeriod, transaction type, fixed-scale quantity, EffectiveDate, policy references, source, actor, and correlation data. It excludes OccurredAtUtc, random IDs, object hash values, dictionaries, and culture-dependent formatting. Same-key/same-payload retries reuse the existing transaction; same-key/different-payload attempts return Conflict. A concurrent duplicate-key race is protected by the database unique index but remains outstanding SQL Server acceptance evidence.

### Validation evidence and remaining debt

The authored `LeaveBalanceFoundationTests` cover lazy creation, balance reuse, Opening/Accrual/ExternalGrant projection effects, derived Available, positive quantities, idempotency replay/conflict, cross-tenant rejection, missing finite balance, append-only protection, tenant filters, unique indexes, precision, rowversion metadata, and check constraints. They were not executable in Codex because the test build fails at the same zero-diagnostic asset-resolution stage. The full backend regression was not run.

No isolated SQL Server database was accessed. Real SQL Server validation remains outstanding for concurrent balance creation, unique-key races, rowversion conflicts, concurrent credits, idempotency races, transaction isolation/atomicity, locking, and deadlocks. This debt blocks production-ready finite-balance reservation/Apply Leave, but not pure Request schema design. No deadlock test or broad retry policy was added.

### 4D.3 request-schema readiness

The source snapshot issue is diagnosed and minimally repaired, the migration chain is preserved, and the Balance/Ledger design remains within scope. Before generating a Request migration, rebuild Infrastructure in a normal PowerShell environment and run EF model/scaffolding diff detection without applying a database change. Resolve any remaining model-vs-snapshot operations before creating request tables. The 4D.2A outcome is therefore:

**PHASE 4D.2A HARDENED — READY FOR PHASE 4D.3 REQUEST SCHEMA IMPLEMENTATION**

This readiness does not authorize request implementation in this phase and does not claim SQL Server concurrency acceptance.

## Phase 4D.3A — Request Schema Foundation Implementation Status

Phase 4D.3A adds the persistence foundation only. `LeaveRequest` is the aggregate root, `LeaveRequestDay` is required date-level historical detail, and `LeaveRequestEvent` is included as immutable business lifecycle history for the initial `Created`/`Submitted` events. No request creation service, validation pipeline, approval transition, reservation, or API was added.

### Implemented model

`LeaveRequest` contains the tenant-scoped Employee, LeaveType, LeavePeriod, published Policy Version, Policy Rule, and effective `EmployeeEmploymentHistory` references; `PolicyGenderSnapshot`; DateOnly `StartDate`/`EndDate`; decimal(9,3) `RequestedQuantity` and `ChargeableQuantity`; the approved initial `LeaveRequestStatus` values (`PendingApproval`, `Approved`, `Rejected`, `Withdrawn`, `Cancelled`); nullable `SubmittedAtUtc`; bounded request idempotency and payload-fingerprint storage; audit fields; and SQL Server rowversion concurrency. The policy, period, employment, and gender values are historical decision context, not instructions to re-resolve current configuration.

The employment reference is tenant- and employee-aware. A corresponding `(TenantId, EmployeeId, Id)` alternate key was added to the employment-history model because the approved request reference otherwise could not be enforced as a tenant-safe historical FK. No existing business data is changed by this source change; migration generation remains a separate step.

`LeaveRequestDay` stores one DateOnly row per request date, employee-requested and chargeable decimal(9,3) quantities, `IsEmployeeRequested`, and bounded nullable classification/reason snapshot strings. Its initial unique key is `(TenantId, LeaveRequestId, Date)`. No half-day portion enum, per-day policy/period duplication, holiday FK, calendar table, or Sandwich algorithm is introduced. The schema can later represent system-added days by setting `IsEmployeeRequested` false and recording an approved reason, but does not create or calculate them now.

`LeaveRequestEvent` is tenant-scoped, append-only through the existing DbContext immutable-history guard, and carries event type, UTC occurrence time, reusable actor type, optional user/employee actor references, and correlation ID. It is business lifecycle history rather than generic audit and contains no approval-specific data.

### Conformance boundaries

All request/day/event entities have tenant query filters, tenant-aware principal relationships, and Restrict delete behavior for historical references. There is no direct EmployeeLeaveBalance FK, no LeaveBalanceTransaction → LeaveRequest FK, no attachment or approval table, and no request number. Request-level policy references are authoritative for the initial one-policy/one-period/one-employment-context boundary; runtime rejection of cross-boundary requests remains for 4D.3B/4D.4.

Gender is snapshotted because current applicability legitimately uses `Employee.Gender`, which is not versioned in employment history. This reduces but does not eliminate the broader risk of later correction to employee demographic data. Half-day portion semantics remain unresolved; only 1.000 and 0.500 quantities are supported conceptually. Reason/comment persistence was not added because the current schema decision did not freeze a bounded request reason field.

No payload hashing or retry behavior is implemented here; the bounded fingerprint column is a schema anchor for 4D.4 submission idempotency. No status transition methods are implemented. Preview, Policy/eligibility/request-rule evaluation, business timezone, calendar/holiday/week-off, Sandwich, Clubbing, attachment requirement/storage, overlap, finite balance availability, reservation, cancellation, modification, and approval remain deliberately unimplemented.

### Validation and migration boundary

`LeaveRequestSchemaFoundationTests` provides static model coverage for tenant filters, unique idempotency/date keys, decimal precision, rowversion metadata, check constraints, tenant-aware Employee and version-aware Policy Rule relationships, restrictive deletes, and the approved initial enums. These tests do not validate Apply Leave behavior. Infrastructure/test execution and EF migration scaffolding must use the repaired current snapshot and a current successful Infrastructure build; no request migration is generated or applied in this phase unless that precondition succeeds.

The request schema migration, when authorized, is bounded to `LeaveRequests` and `LeaveRequestDays`, with `LeaveRequestEvents` included because lifecycle history is foundational in this design. It must not include approvals, attachments, reservations, accrual runs, or changes to the balance/ledger tables. The previously identified 4D.2 snapshot/tooling and SQL Server concurrency debts remain tracked dependencies; SQL Server balance acceptance blocks production-ready reservation, not pure request schema review.

### Remaining implementation blockers

Before 4D.3B/4D.4, business/runtime decisions are still required for Policy date selection, cross-boundary behavior, business timezone, month semantics, request-limit counting and quantity basis, calendar sources, Sandwich expansion, Clubbing adjacency, attachment threshold/storage, half-day portions, overlap statuses, and approval routing. The current EF snapshot must remain healthy and Infrastructure must build from current source before request migration generation.

## Phase 4D.3A Migration Static Review Status

The generated migration is `20260904142643_LeaveRequestFoundation.cs`, with Designer `20260904142643_LeaveRequestFoundation.Designer.cs`. The post-scaffolding `HrmsDbContextModelSnapshot.cs` was also updated. This review was read-only; the migration remains unapplied.

### Migration inventory

`Up()` performs, in order: one DropIndex on `LeavePolicyClubbingRules`; three AlterColumn operations on existing balance/ledger tables; one AddUniqueConstraint on `EmployeeEmploymentHistory`; three new tables (`LeaveRequests`, `LeaveRequestDays`, `LeaveRequestEvents`); and the generated index operations for Clubbing, Balance/Ledger, and the three request tables. No raw SQL or data operations were found. The three new tables are within the approved 4D.3A foundation, and `LeaveRequestEvents` is included because the current 4D.3 design explicitly classified immutable Created/Submitted history as foundational.

### Data-loss warning finding

The warning is caused by these existing-column changes in `Up()`:

| Location | Operation | Reason EF flags it |
|---|---|---|
| lines 18–28 | `LeaveBalanceTransactions.PayloadFingerprint`: nullable to non-nullable with `defaultValue: ""` | Existing null values must be converted to a required value |
| lines 30–40 | `LeaveBalanceTransactions.IdempotencyKey`: nullable to non-nullable with `defaultValue: ""` | Existing null values must be converted to a required value |
| lines 42–52 | `EmployeeLeaveBalances.RowVersion`: nullable to non-nullable rowversion with `defaultValue: new byte[0]` | Existing column definition is changed to required rowversion metadata |

These columns were created as nullable in the earlier 4D.2 migration but are required by the current model configuration. This is unexpected model/snapshot drift in a request migration, not an intentional request-schema operation. The migration also drops/recreates the existing Clubbing normalized-pair index and adds the employment-history alternate key required by the tenant-safe employment reference. The alternate key is non-destructive, but the balance/ledger column alterations and unrelated index churn mean the generated migration is not limited to the approved request schema.

### Static safety classification

`LeaveRequests`, `LeaveRequestDays`, and `LeaveRequestEvents` contain the expected tenant-aware references, DateOnly dates, decimal(9,3) quantities, rowversion/idempotency fields, and restrictive historical FKs. The Policy Rule FK remains version-aware; PolicyRule-to-LeaveType consistency remains application-validated rather than database-enforced. RequestDay uniqueness is `(TenantId, LeaveRequestId, Date)`, with half-day portion semantics still deferred. No balance/ledger request FK or reservation behavior was added.

No DropTable, DropColumn, RenameColumn, RenameTable, raw SQL, InsertData, UpdateData, or DeleteData occurs in `Up()`; the DropTable operations present are only in `Down()`. However, because `Up()` alters existing Balance/Ledger columns and performs unrelated existing-schema index changes, the migration is classified:

**UNEXPECTED DESTRUCTIVE MIGRATION — DO NOT APPLY**

The generated migration and snapshot were not modified or removed in this review. A separate bounded correction must first resolve the 4D.2 model/migration drift, then regenerate the request migration.

## Phase 4D.2B — Balance/Ledger Migration Conformance Repair Status

The unapplied `20260904142643_LeaveRequestFoundation` migration exposed baseline drift rather than a request requirement. Its warning-triggering changes were nullable-to-required alterations for `LeaveBalanceTransactions.PayloadFingerprint`, `LeaveBalanceTransactions.IdempotencyKey`, and `EmployeeLeaveBalances.RowVersion`; it also carried Balance/Ledger lookup-index churn and Clubbing index replacement.

The 4D.2 source migration already created the two bounded required string columns and the non-null SQL Server rowversion correctly, but its Designer metadata was incomplete: it omitted required/nullability annotations and the two current Balance lookup indexes (`TenantId + LeaveTypeId` and `TenantId + LeavePeriodId`). The 4D.2 migration now contains those missing Balance indexes, and its Designer plus the pre-request snapshot were restored from the complete generated model with request entities removed. Balance/Ledger indexes, precision, checks, tenant-aware restrictive relationships, and the corrected Clubbing relationship ordering are retained.

The request migration was removed through `dotnet ef migrations remove` and the two identified untracked unapplied migration artifacts were then removed because the command left them on disk. `LeaveRequest`, `LeaveRequestDay`, `LeaveRequestEvent`, their EF configuration, DbSets, tests, and design documentation remain. No request migration was regenerated, no database was accessed, and no migration was applied.

This repair does not resolve the separate SQL Server concurrency acceptance debt. It only restores the migration baseline so a later regenerated request migration can be reviewed for request-only operations. The next safe action is a fresh build and regeneration/review of `LeaveRequestFoundation`; no Apply Leave or 4D.3B runtime work is included here.

### Remaining conformance blocker

The current model requires the SQL Server filtered unique normalized Clubbing index (`[NormalizedPairKey] IS NOT NULL`), while `20260904150000_LeavePolicyFoundationHardening` creates the same unique index without that filter. The unsafe request migration therefore attempted to drop and recreate this pre-existing index. This is an unapplied historical-migration/model metadata mismatch, not a request prerequisite. That migration was intentionally not modified in 4D.2B because the phase instructions require stopping for approval before changing its semantics. Request migration regeneration remains blocked until this Clubbing index baseline decision is resolved.

## Phase 4C.11A — Clubbing Migration Baseline Conformance Repair

The canonical Clubbing index is `IX_LeavePolicyClubbingRules_TenantId_LeavePolicyVersionId_NormalizedPairKey` over `(TenantId, LeavePolicyVersionId, NormalizedPairKey)`, unique, with the SQL Server filter `[NormalizedPairKey] IS NOT NULL`. The 4C.11 migration source omitted that filter, while the current repaired model metadata and the 4D.2B Designer contained it; this source/Designer baseline drift was the cause of unrelated index drop/recreation in the removed request migration. `NormalizedPairKey` is a computed string from the required lower and higher rule IDs, and the self-pair check preserves the approved canonical pair semantics, so a valid persisted Clubbing row cannot logically produce NULL. The filter is therefore a provider/metadata accommodation and does not weaken uniqueness for valid rows.

The unapplied 4C.11 migration and its Designer were minimally aligned with the existing filtered-index model. No Clubbing relationship, key, computed expression, check constraint, delete behavior, or business semantics changed. The request migration was not regenerated; the database remains untouched.

## Phase 4D.3B — Leave Request Validation Design

This section defines the validation contract before a `LeaveRequest` can be created or submitted. It is design-only. It does not add a validator, submission service, controller, reservation operation, approval behavior, or migration. The request schema is already present from 4D.3A; this phase defines how a future orchestrator must obtain and validate the authoritative decision context.

### 1. Request input and authority

The self-service command should contain only client-selectable request facts: `LeaveTypeId`, `StartDate`, `EndDate`, and a bounded date/partial-day selection. It may contain a plain reason only if a later schema decision adds that field; the current request schema does not impose a reason requirement. It must carry the API-standard `Idempotency-Key` and may carry attachment metadata only when attachment storage and threshold semantics are approved. The client must not supply `TenantId`, subject `EmployeeId`, policy IDs, employment IDs, balance totals, or chargeable totals.

Tenant comes from authenticated tenant context. Employee comes only from `IEmployeeIdentityResolver`, which uses the authenticated tenant and `AccountEmployeeCurrentLink`; no UserId, email, username, or employee-code fallback is permitted. HR/admin subject creation is a separate permissioned command and must not be an optional EmployeeId branch of self-service validation.

The server should accept a stable `LeaveTypeId`, then prove tenant ownership, active availability, and policy applicability. Names and codes are lookup/display values only.

### 2. Recommended validation pipeline

The future validation orchestrator should execute these stages in order and stop or safely aggregate only independent input errors:

1. Authenticate the request and establish tenant context.
2. Resolve the linked Employee through `IEmployeeIdentityResolver`; reject an absent, ambiguous, or deactivated subject according to the existing `ResultStatus` conventions.
3. Validate shape: required LeaveType and dates, `StartDate <= EndDate`, valid partial-day input, bounded text, and a required idempotency key.
4. Load the tenant-owned active `LeaveType`; reject unknown, inactive, or unsupported units. The initial runtime unit is days only.
5. Obtain an authoritative business date/time context from a future tenant timezone provider. Do not use `DateTime.UtcNow.Date` or server-local date as business today.
6. Resolve effective employment for the selected effective date and retain its `HistoryId` and Gender decision input.
7. Resolve exactly one active `LeavePeriod` independently for that date.
8. Resolve exactly one published active Policy rule using the explicit date and the already-established Employee context.
9. Verify the one-context boundary across every requested and later system-generated date; reject a differing period, policy version/rule, or employment history rather than split the request.
10. Evaluate Eligibility from the resolved rule and employment snapshot.
11. Evaluate frozen Request Rules that have an approved quantity, calendar, and status-count basis.
12. Classify dates through a future business-calendar abstraction and calculate requested/chargeable quantities.
13. Apply approved Sandwich expansion, preserving generated-day explanations.
14. Evaluate overlap and, once adjacency is frozen, Clubbing.
15. Evaluate attachment requirement using the approved threshold basis; actual file validation/storage is separate.
16. Interpret entitlement mode. Set `BalanceReservationRequired` for finite Allocated leave only; do not mutate balance here.
17. Return an authoritative validation result for a later atomic request/reservation command.

Policy and employment should not be independently re-resolved with different dates. The current `LeavePolicyResolver` already resolves employment internally but returns policy IDs rather than the employment `HistoryId` and Gender used. A future orchestration contract must either carry the same employment decision through both evaluations or add an internal enriched resolution result; it must never silently choose a second, current employment context.

### 3. Initial single-context boundary

The initial MVP recommendation is one Employee, one LeaveType, one LeavePeriod, one PolicyVersion/PolicyRule, and one effective-employment context per request. The requested span must be rejected if any requested or approved system-generated date resolves to a different context. The server should test this by resolving the StartDate context and checking every date in the candidate span with the explicit resolver inputs; it must not infer continuity from IDs or silently split the request.

The date that selects Policy remains a business decision. The safest recommendation is an explicit StartDate effective date for the initial MVP, followed by full-span consistency checks. Submission date must not select historical Policy. LeavePeriod is independently resolved from the same effective date. Cross-Policy, cross-Period, and cross-employment requests should be rejected until per-day context and multi-balance reservation are designed.

Eligibility service duration should be evaluated as of the same effective request date, recommended as StartDate for the initial boundary. The date inclusivity and service-month rules remain unresolved and block implementation of those calculations.

### 4. Business date and employment eligibility

Advance notice, backdating, same-day behavior, and future cancellation timing require a tenant/business timezone and an authoritative business-date provider. The schema’s `DateOnly` values do not remove this requirement. Timezone is not a request-table blocker, but it blocks correct request validation and Apply Leave.

The current Eligibility configuration supports `Immediate` and `MinimumService`, service units Days/Months, probation Allowed/NotAllowed/AfterConfirmation, and notice Allowed/NotAllowed/AllowedWithApproval. `Immediate` is implementable without service calculation. Minimum-service validation can be implemented only after approval of whether service is measured from DateOfJoining, GroupDateOfJoining, or another employment date, and how day/month boundaries are counted. The current employment snapshot provides joining/leaving and employment status data but no confirmed source for a confirmation date; `AfterConfirmation` therefore remains deferred. `AllowedWithApproval` is a workflow interaction, not a pre-approval-only eligibility conclusion. Rehire, statutory formulas, and emergency exceptions remain unresolved.

### 5. Request Rules and quantity contract

The client should send a date span plus a supported full-day/half-day selection; the server should construct RequestDay rows and calculate both totals. The client must not be authoritative for `RequestedQuantity` or `ChargeableQuantity`. A later command may echo a preview total, but final validation must recalculate it.

Storage remains decimal(9,3), but the only approved units are `1.000` and `0.500`; quarter-day, hour, and shift semantics are not supported. `RequestedQuantity` is the employee-selected quantity before calendar treatment. `ChargeableQuantity` is the server-calculated quantity after approved holiday, week-off, and Sandwich rules. The aggregate must satisfy both totals as sums of RequestDay quantities.

The following Request Rule fields require explicit runtime semantics before enforcement: minimum/maximum quantity, maximum consecutive quantity, minimum advance notice, backdating, period limits, and PartialDayMode. In particular, maximum consecutive quantity must be defined as calendar span, employee-requested days, or chargeable quantity; it must not be guessed. Minimum/maximum and consecutive checks should use chargeable quantity only if that basis is approved.

Generic `0.500` storage is not enough to distinguish two half-day claims on the same date. First-half/second-half semantics are not frozen, so safe half-day submission and precise half-day overlap remain blocked. Until approved, the implementation subset should support full-day requests only or conservatively reject duplicate same-date half-day claims.

### 6. Advance notice, backdating, and limits

`MinimumAdvanceNoticeDays` needs a decision on inclusive/exclusive counting, calendar versus working days, and same-day requests. `BackdatedRequestMode` and `MaximumBackdatedDays` need the same business-date source plus an approved treatment for emergency exceptions. These are not safe to implement from enum names alone.

The current model represents one `MaximumRequestsPerPeriod` and one `MaximumQuantityPerPeriod`, with `RequestLimitPeriod` Month or LeavePeriod. Month semantics are not frozen; no Gregorian-month fallback is permitted. LeavePeriod limits can query the resolved period, but the business must approve which statuses count. A conservative recommendation is to count requests that hold employee entitlement or block dates (`PendingApproval` and `Approved`) and exclude `Rejected`, `Withdrawn`, and `Cancelled`; this remains a recommendation requiring approval. The count must use the same requested/chargeable quantity basis selected for the rule.

### 7. Calendar and Sandwich boundary

Calendar validation requires a read-only `ILeaveBusinessCalendarResolver`/`IEmployeeBusinessCalendar` accepting Tenant, Employee, and DateOnly and returning WorkingDay, Holiday, WeekOff, and source information. No authoritative Holiday or Weekly-Off source currently exists in this runtime foundation. Holiday and week-off treatment therefore cannot be enforced yet, and no Calendar FK should be introduced.

Once approved, the evaluator must persist the selected classification and calculation reason on each RequestDay. A Sandwich-generated date should be a RequestDay with `IsEmployeeRequested = false`, its classification snapshot, positive chargeable quantity when applicable, and reason such as `Sandwich`. Prefix, suffix, and between search limits, bridging behavior, and whether generated days count toward every Request Rule remain unresolved and block Apply Leave. Sandwich must not be silently approximated by scanning an unbounded date range.

### 8. Overlap and Clubbing

Overlap is separate from Clubbing and must be checked at RequestDay grain. The future query needs tenant, Employee, candidate dates, and lifecycle status. The business must approve whether `PendingApproval` counts; `Rejected`, `Withdrawn`, and `Cancelled` should not block overlap. Full-day overlap is deterministic. Half-day overlap is not deterministic until portion semantics are frozen, so Date-only uniqueness and conservative same-date rejection are the safe interim boundary.

Clubbing consumes the existing normalized symmetric NotAllowed pairs, but needs an `ILeaveClubbingEvaluator` with neighboring requests, LeaveType pair, request statuses, and business-calendar context. Immediate calendar adjacency, working-day adjacency, holiday/week-off bridging, half-day contact, and multi-request chains are unresolved. Clubbing validation remains deferred and must not be inferred from the normalized database pair alone.

### 9. Attachments, reason, and snapshots

The attachment evaluator should return NotRequired, Optional, or Required(DocumentLabel) from the resolved Policy. `RequiredAboveQuantity` cannot be enforced until the business chooses RequestedQuantity, ChargeableQuantity, or another basis. The current schema has no Reason field and no attachment relation; no reason requirement or file-storage behavior is added by this design.

After applicability evaluation, `PolicyGenderSnapshot` must capture the exact `Employee.Gender` value used. The request’s PolicyVersionId and PolicyRuleId are authoritative historical references. A compact decision result may additionally preserve priority, specificity, eligibility outcome, entitlement mode/source, Request Rule values, and calendar/attachment outcomes once evaluated; it must not copy the entire Policy as unbounded JSON. Published Policy rows are immutable, and historical requests must never be explained by re-resolving current Policy.

### 10. Entitlement and reservation boundary

Validation must return the resolved entitlement mode and a boolean `BalanceReservationRequired` without touching balances:

| Mode | Validation result | Later submission behavior |
|---|---|---|
| Allocated | `BalanceReservationRequired = true` | Recheck available quantity and reserve atomically at submission. |
| Unlimited | `BalanceReservationRequired = false` | Run all non-balance rules; create no fake balance or credit. |
| NoBalanceRequired | `BalanceReservationRequired = false` | Run all non-balance rules; perform no paid-balance debit. |

Finite balance availability is not a 4D.3B validation side effect. Final Apply Leave must recheck `Available >= ChargeableQuantity` within the request-plus-reservation transaction. Reservation, release, and consumption ledger types remain later work. The outstanding SQL Server rowversion, locking, unique-key race, isolation, and deadlock validation debt blocks production-ready finite reservation but not this design.

### 11. Idempotency and validation result

The future POST should use the repository/API idempotency convention, preferably the `Idempotency-Key` header. The database grain is `TenantId + EmployeeId + IdempotencyKey`. The fingerprint should canonicalize the normalized LeaveType, dates, day selections, reason if eventually persisted, and other semantic inputs. It must exclude random IDs, CreatedDate, SubmittedAtUtc, rowversion, preview timestamps, and server-local formatting. Same key plus the same semantic payload returns the original request; same key plus a different payload returns `ResultStatus.Conflict`/HTTP 409.

The internal validation result should contain EmployeeId, LeaveTypeId, StartDate, EndDate, requested and chargeable totals, RequestDay snapshots, LeavePeriodId, PolicyVersionId, PolicyRuleId, EmployeeEmploymentHistoryId, PolicyGenderSnapshot, entitlement mode/source, `BalanceReservationRequired`, attachment result when evaluable, and diagnostic decision metadata. It is an input to persistence, not an authorization token; final submission must rerun all authoritative checks.

### 12. Error contract

Use the existing `Result<T>` categories rather than exceptions for expected validation outcomes. Shape and quantity errors map to `ValidationFailed`; missing or unlinked identity maps to `Unauthorized`/`NotFound` according to the existing identity service; inactive or disallowed subjects map to `Forbidden`; missing configuration maps to `NotFound` or a safe configuration-unavailable result; resolver ambiguity, overlap, and idempotency mismatch map to `Conflict`. Internal diagnostics may retain conflicting IDs, but employee-facing messages must not expose policy or employment configuration details unnecessarily.

Recommended typed internal failure categories are: `EmployeeIdentityNotLinked`, `EmploymentNotFound`, `EmploymentAmbiguity`, `LeavePeriodNotConfigured`, `LeavePeriodAmbiguity`, `PolicyNotConfigured`, `PolicyNotApplicable`, `PolicyAmbiguity`, `NotEligible`, `RequestRuleViolation`, `BusinessDateUnavailable`, `CalendarNotConfigured`, `SandwichUnableToEvaluate`, `ClubbingNotAllowed`, `AttachmentRequired`, `OverlappingRequest`, `InsufficientBalance` (later reservation), `ConcurrencyConflict` (later persistence), and `IdempotencyConflict`.

### 13. Frozen, ready, deferred, and blocked rules

| Rule | Status | Boundary |
|---|---|---|
| Server-authoritative tenant and linked Employee | READY | 4D.3C validation foundation |
| Stable LeaveType ownership/active check | READY | 4D.3C |
| Explicit DateOnly shape and StartDate <= EndDate | READY | 4D.3C |
| Resolver status handling and safe ambiguity errors | READY | 4D.3C |
| Historical Policy/Period/Employment references | READY | 4D.3C decision contract |
| One-context request boundary recommendation | READY subject to approval | 4D.3C/4D.4 |
| Immediate eligibility | READY | 4D.3C |
| Minimum service, confirmation, rehire rules | DEFERRED/BLOCKED | business decision before runtime enforcement |
| Full-day-only quantity validation | READY for restricted MVP | 4D.3C |
| Half-day submission/overlap | BLOCKED | half-day portion decision |
| Advance notice/backdating | BLOCKED | timezone and counting semantics |
| Month and period limits | BLOCKED | month and status-count decisions |
| Calendar, holiday, week-off | DEFERRED | authoritative calendar source |
| Sandwich | DEFERRED | expansion/search decisions |
| Overlap | BLOCKED | status and half-day decisions |
| Clubbing | DEFERRED | adjacency/status/calendar decisions |
| Attachment threshold | BLOCKED | quantity basis and storage decision |
| Entitlement mode interpretation | READY as a non-mutating result | reservation later |
| Balance availability/reservation | DEFERRED | Apply Leave/reservation phase |
| Approval routing and decisions | DEFERRED | approval phase |

### 14. Smallest safe implementation subset

The next implementation can safely cover: authenticated tenant establishment; linked Employee resolution; basic input shape; active tenant-owned LeaveType lookup; explicit employment/period/policy resolution; safe resolver error mapping; StartDate-based one-context decision object; `Immediate` eligibility; full-day-only shape and decimal normalization; historical snapshot values; and a non-mutating validation result. It must reject unsupported HalfDay requests, unresolved calendar-dependent policies, unresolved month/limit semantics, and any cross-context span rather than guess.

It must not calculate business today, enforce advance/backdate, evaluate MinimumService/AfterConfirmation, calculate holidays or Sandwich, enforce Clubbing/overlap, evaluate attachment thresholds, inspect finite balance, or create any request/reservation state until the relevant decisions and dependencies are available.

### 15. Decision register and blocker classification

| Decision | Recommendation | Required before | Classification |
|---|---|---|---|
| Policy effective date | explicit StartDate for MVP, then prove full-span consistency | request validation | Blocks 4D.3B runtime until approved |
| Cross-Policy/Period/employment span | reject; do not split | request validation | Blocks 4D.4 |
| Tenant timezone/business today | authoritative tenant provider | request validation | Blocks 4D.3B date rules / Apply Leave |
| Service day/month and rehire semantics | approve source and boundaries | eligibility evaluator | Blocks 4D.3B eligibility |
| Confirmation/notice source | approve employment data and workflow meaning | eligibility evaluator | Blocks 4D.3B eligibility |
| Half-day portion | freeze first/second-half semantics | quantity/overlap/UI | Blocks half-day Apply Leave |
| Advance/backdate counting | approve inclusive basis and exceptions | request-rule evaluator | Blocks Apply Leave |
| Month semantics | define business month | request-rule evaluator | Blocks month limits |
| Limit statuses and quantity basis | approve | request-rule evaluator | Blocks limits/overlap |
| Holiday/weekly-off source | approve provider | calendar evaluator | Blocks Calendar/Apply Leave |
| Sandwich expansion | approve search and generated-day participation | Sandwich evaluator | Blocks Apply Leave |
| Clubbing adjacency | approve adjacency, bridging, statuses, chains | Clubbing evaluator | Blocks Clubbing/Apply Leave |
| Attachment threshold/storage | approve basis and file boundary | attachment evaluator | Blocks attachment enforcement |
| Reason requirement | confirm optional/required and add schema only if needed | request runtime/schema | Can defer if optional |
| Request status counting | approve PendingApproval/Approved treatment | limits/overlap | Blocks Apply Leave |
| Reservation timing | submission-time reservation | reservation phase | Blocks Apply Leave, not validation design |
| Approval routing | separate workflow design | approval phase | Can defer from validation |

The EF snapshot/migration baseline is no longer a 4D.3B design blocker. The remaining SQL Server balance concurrency debt blocks production-ready reservation and Apply Leave, not this validation contract. Attachment storage, cancellation, modification, and approval are intentionally deferred.

### 16. Recommended next phase

Because the required date, quantity, employment, calendar, limit, overlap, Clubbing, and attachment decisions are not all frozen, the recommended next phase is **PHASE 4D.3B.1 BUSINESS DECISION FREEZE REQUIRED**. After those decisions are recorded, a bounded `PHASE 4D.3C REQUEST VALIDATION FOUNDATION IMPLEMENTATION` may implement the ready subset and explicitly reject unsupported rules.

No request validation code, Apply Leave, reservation, approval, frontend, migration, or database operation is authorized by this section.

## Phase 4D.3B.1 — Leave Request Business Decision Freeze

This section freezes the smallest safe decision set for a future 4D.3C validation foundation. It does not implement validation or submission. The rule for this phase is explicit: a valid Policy configuration whose required runtime dependency is unavailable must produce an unsupported-configuration outcome; it must never be silently ignored or approximated.

### 1. Authoritative sources inspected

Tenant has no timezone/business-timezone property or resolver. The existing employment service explicitly documents a UTC business-date fallback, but that fallback is not authoritative for Leave rules and is not adopted here. No Holiday, weekly-off, work-schedule, or business-calendar entity/service exists in the current Leave runtime. `EmployeeEmployment` contains current `ConfirmationDate`, `JobStatus`, `ProbationPeriod`, and `NoticePeriod` fields, but it is a single current employment file rather than effective-dated history; `EffectiveEmploymentResolver` returns `EmployeeEmploymentHistory` context and does not expose those fields. `Employee` and the effective employment snapshot provide joining/group-joining and employment-status data, but do not freeze rehire/service-origin or confirmation semantics.

The request schema already provides `LeaveRequest`, `LeaveRequestDay`, and `LeaveRequestEvent`, with DateOnly dates, decimal(9,3) quantities, status values `PendingApproval`, `Approved`, `Rejected`, `Withdrawn`, and `Cancelled`, and the tenant-aware historical references described in 4D.3A.

### 2. Decisions frozen for the MVP

#### Policy, period, and employment context

Request StartDate is the anchor for Policy, LeavePeriod, and effective-employment resolution. The existing resolvers must receive that explicit DateOnly; submission date, current date, latest VersionNumber, and arbitrary newest Policy are prohibited. The selected Policy must be Published, active, applicable, and uniquely ranked by the existing priority/specificity rules. Equal best priority and specificity remains `ConfigurationAmbiguity`.

After resolving StartDate, the future validator must evaluate every employee-requested date in the span and every later generated date when that evaluator exists. Every date must retain the same `EmployeeEmploymentHistoryId`, `LeavePeriodId`, `LeavePolicyVersionId`, and `LeavePolicyRuleId`. Any difference rejects the request. Requests are not split across contexts. This is compatible with the current resolver contracts, although a future orchestration result must carry the employment snapshot alongside the policy result rather than re-resolve it inconsistently.

#### Quantity and request days

The MVP is FullDayOnly. Each supported employee-requested day contributes `1.000`; the server creates RequestDay rows and derives `RequestedQuantity`. Client totals are advisory only. Runtime `0.500` requests are rejected as unsupported until first-half/second-half semantics are frozen. Quarter-day, hourly, and shift units remain unsupported.

The equality `RequestedQuantity == ChargeableQuantity` is frozen only for the restricted subset that is full-day, has no Sandwich, and uses no calendar treatment requiring holiday or weekly-off classification. It is not a general Leave invariant. Any other active configuration returns unsupported configuration until its dependency is implemented.

Month means Gregorian calendar month of `LeaveRequestDay.Date`, represented as `YYYY-MM`. LeavePeriod limits remain independently keyed by the resolved LeavePeriodId; no LeavePeriod definition currently conflicts with this month rule.

For request limits and consecutive limits, the frozen quantity basis is server-calculated chargeable RequestDay quantity. `MaxConsecutiveQuantity` means the sum of consecutive chargeable RequestDay quantities within the request after classification; it is not the raw StartDate/EndDate span. In the no-calendar subset this equals the supported RequestDay count.

#### Status participation

For historical request-limit and overlap queries, `PendingApproval` and `Approved` count and block. `Rejected`, `Withdrawn`, and `Cancelled` do not count and do not block. This matches the current lifecycle intent: rejected/retracted/finally cancelled requests are not active claims. Approval workflow may add further states later, but no new status is introduced here.

#### Entitlement mode

`Allocated` returns `BalanceReservationRequired = true` for later submission-time reservation. `Unlimited` and `NoBalanceRequired` return false and do not require, create, or infer a finite balance. The validator does not inspect or mutate `EmployeeLeaveBalance`; final availability and reservation remain a later atomic phase.

### 3. Decisions intentionally unsupported in the MVP

#### Business timezone and date rules

No authoritative tenant/business timezone source exists. Advance notice, backdating, same-day determination, and any business-today comparison are unsupported. The validator must not use UTC or server-local date and must not silently ignore configured `MinimumAdvanceNoticeDays` or backdating rules. Only the baseline equivalent of no advance restriction and `BackdatedRequestMode.NotAllowed` can be admitted by the restricted subset; a configured positive notice or allowed backdating rule returns unsupported configuration.

#### Calendar and Sandwich

No authoritative Holiday or Weekly-Off source exists. `HolidayTreatment.Exclude`, `WeekOffTreatment.Exclude`, and `SandwichMode.Disabled`, with no calendar-dependent calculation required, are the only safe baseline configuration. Any Holiday/WeekOff inclusion or exclusion that requires classification, or any enabled Sandwich mode, is unsupported. No system-added Sandwich RequestDays are generated in the MVP. A future calendar provider must return date classification and source information, which will then be snapshotted into RequestDay classification/reason fields.

#### Eligibility

`EligibilityMode.Immediate` is supported. `MinimumService` is unsupported because the repository has DateOfJoining, GroupDateOfJoining, and FirstHiredDate values but has not frozen which is the service origin or how rehire periods combine. Days versus months, inclusivity, and statutory/emergency exceptions remain unresolved. `ProbationMode.Allowed` is baseline-safe. `NotAllowed` can be evaluated only against an authoritative effective employment state; `AfterConfirmation` is unsupported because ConfirmationDate/JobStatus are not part of the effective-dated resolver result. `NoticePeriodMode.Allowed` is baseline-safe. `NotAllowed` and `AllowedWithApproval` are unsupported because no effective-dated notice-period state exists and the latter requires workflow semantics. No inference from JobStatus, employment reason, or dates is permitted.

#### Half-day, overlap, and Clubbing

Half-day submission remains unsupported. Full-day overlap is evaluated at RequestDay grain for the same tenant, Employee, and Date against PendingApproval or Approved parents. Precise half-day overlap is deferred until portion semantics exist. Clubbing is deferred entirely: the normalized symmetric pair is configuration data, not an adjacency algorithm. A configured Clubbing rule that would need evaluation must return unsupported configuration rather than be ignored.

#### Attachments and reason

The current request schema does not require a Reason field; no mandatory-reason rule is introduced. `RequiredAboveQuantity` should use ChargeableQuantity because the requirement follows chargeable Leave, but the existing design explicitly left this as a business-owner decision. It therefore remains **REQUIRES CONFIRMATION** before attachment enforcement. Actual attachment metadata, upload, and storage remain later work. `None` is the only attachment mode safe for the restricted validation subset; other modes return unsupported configuration until the threshold and storage contract are approved.

### 4. Minimum service, confirmation, and notice source boundary

The current employment model has useful current-contract fields, but they are not an authoritative effective-dated source for a historical Leave decision. The future contract must either extend effective employment resolution with versioned confirmation/notice/service-origin facts or explicitly introduce a separate authoritative source. Until then, 4D.3C may validate only Immediate eligibility and baseline Allowed probation/notice modes. It must not silently select the current `EmployeeEmployment` row when resolving a historical StartDate.

### 5. Future idempotency fingerprint

The request fingerprint will canonicalize server-resolved EmployeeId, LeaveTypeId, StartDate, EndDate, normalized employee-requested day selections, and normalized Reason only if Reason becomes part of the persisted request contract. It excludes generated RequestId, CreatedDate, SubmittedAtUtc, rowversion, preview timestamps, and random values. Tenant is already part of the uniqueness boundary; whether it is also included in the hash is an implementation detail, but the canonical semantic payload must remain tenant-scoped. Existing balance fingerprint principles are reused: deterministic field order, invariant DateOnly formatting, and invariant decimal formatting. No hashing is implemented here.

### 6. Unsupported-configuration error contract

The existing `Result<T>` has Success, ValidationFailed, Unauthorized, Forbidden, NotFound, and Conflict statuses but no dedicated Unsupported status. The future validator should use a stable internal error category such as `UnsupportedConfiguration` and map it through the existing application result boundary as a validation failure, not NotFound. The public message should state that the selected Leave configuration is not supported for the current runtime; internal diagnostics may identify the rule and dependency. Bad input remains ValidationFailed, missing configuration remains NotFound/configuration-unavailable, ambiguity remains Conflict, and idempotency/overlap remains Conflict.

### 7. Phase 4D.3C implementation matrix

| Validation rule | 4D.3C status | Implementation boundary |
|---|---|---|
| Authenticated tenant | READY | server tenant context |
| Linked Employee identity | READY | `IEmployeeIdentityResolver` only |
| LeaveType required/tenant-owned/active | READY | application validation |
| DateOnly shape and StartDate <= EndDate | READY | input validation |
| StartDate Policy/Period/Employment anchor | READY | existing resolvers |
| Same context across request span | READY | reject, never split |
| Published Policy and ambiguity handling | READY | existing resolver contract |
| Immediate eligibility | READY | no service calculation |
| Minimum service | UNSUPPORTED-MVP | service-origin/rehire source missing |
| Baseline probation/notice Allowed modes | READY | no restricted-state evaluation |
| Confirmation/probation restrictions | BLOCKED-BY-SOURCE | effective-dated source missing |
| Full-day quantity | READY | one supported day = 1.000 |
| Half-day | UNSUPPORTED-MVP | portion semantics absent |
| Advance notice | UNSUPPORTED-MVP | timezone/counting source absent |
| Backdating not allowed baseline | READY | only exact baseline |
| Allowed/bounded backdating | UNSUPPORTED-MVP | timezone and exception rules absent |
| Gregorian Month definition | READY | YYYY-MM of RequestDay.Date |
| Request limits | READY only for frozen status/chargeable basis and supported subset | no calendar-dependent limits |
| Maximum consecutive | READY for full-day supported subset | consecutive chargeable RequestDays |
| Holiday/Weekly-Off calendar | BLOCKED-BY-SOURCE | authoritative calendar absent |
| Sandwich | UNSUPPORTED-MVP | disabled baseline only |
| Full-day overlap | READY for approved statuses | RequestDay grain |
| Half-day overlap | DEFERRED | portion semantics absent |
| Clubbing | UNSUPPORTED-MVP | adjacency/calendar semantics absent |
| Attachment None | READY | no file dependency |
| Attachment Required/Optional | UNSUPPORTED-MVP | upload contract absent |
| RequiredAboveQuantity | BLOCKED-BY-SOURCE/DECISION | chargeable basis requires confirmation; storage absent |
| Entitlement mode result | READY | non-mutating reservation flag |
| Balance availability/reservation | DEFERRED | later atomic submission phase |
| Request idempotency contract | READY | existing key/fingerprint design |

### 8. Exact 4D.3C scope

Once the remaining source/decision prerequisites are accepted, 4D.3C may implement only request-validation input/result contracts, tenant and linked Employee identity, active LeaveType checks, explicit employment/period/Policy resolution, cross-boundary rejection, Immediate eligibility, baseline full-day RequestDay normalization, the unsupported-configuration guard, entitlement-mode interpretation, deterministic errors, and focused unit tests. It must not persist requests, create events, calculate balances, reserve funds, run approval, or expose an API unless separately authorized.

4D.3C must reject any active Policy whose selected rule requires an unsupported semantic. It may not treat missing calendar data as “all working days,” treat missing confirmation as confirmed, use current employment as historical employment, ignore Clubbing, or skip attachment/notice/backdate requirements.

### 9. Remaining business decisions

The following are still required before the corresponding runtime can be production-safe: tenant timezone/provider; effective-dated confirmation/probation and notice source; service-origin and rehire semantics; advance/backdate counting and emergency rules; confirmation of ChargeableQuantity as the attachment threshold basis; Holiday/Weekly-Off source; Sandwich expansion and generated-day participation; half-day portion semantics; and Clubbing adjacency/bridging/status rules. Cancellation, modification, approval routing, reservation release, consumption, and SQL Server balance concurrency remain later-phase decisions or validation debt.

### 10. Decision-freeze readiness

The restricted MVP decisions are frozen sufficiently for 4D.3C. Missing timezone, calendar, effective confirmation/notice, half-day, Clubbing, and attachment dependencies are explicit unsupported-configuration outcomes, not silent fallbacks. The next recommended phase is **PHASE 4D.3C REQUEST VALIDATION FOUNDATION IMPLEMENTATION**, limited to the exact scope above; the later source/decision work can expand support without changing the safety boundary. No source implementation is authorized by this section.

## Phase 4D.3C — Request Validation Foundation Implementation

The side-effect-free validation foundation is implemented in the Application layer through
`ILeaveRequestValidationService` and `LeaveRequestValidationService`. It accepts only a LeaveType,
DateOnly start/end range, and bounded idempotency key; tenant and Employee are obtained from the
authenticated context and `IEmployeeIdentityResolver`. It orchestrates the existing employment,
LeavePeriod, and published Policy resolvers and verifies that every date in the inclusive span keeps
the same employment-history, period, policy-version, and policy-rule context.

The supported MVP subset is intentionally narrow: active day-based LeaveTypes, Immediate eligibility,
baseline Allowed probation/notice modes, full-day RequestDays at 1.000 quantity, no advance-notice or
backdating rule, no calendar-dependent Holiday/Week-Off treatment, Sandwich disabled, no Clubbing
adjacency dependency, no attachment requirement, and a configured EntitlementMode. Request and
chargeable quantities are calculated from the normalized server-owned day list. `Allocated` returns
`BalanceReservationRequired = true`; `Unlimited` and `NoBalanceRequired` return false without
reading or creating balances.

Unsupported configured semantics return a deterministic `UnsupportedConfiguration` validation failure.
The service does not use machine/UTC dates for business rules, does not infer employment or
confirmation state, does not calculate calendar classifications, and does not silently waive Clubbing
or attachment requirements. The deterministic SHA-256 payload fingerprint is prepared in memory
from the resolved Employee, LeaveType, dates, and normalized day sequence; retry timestamps and
generated identifiers are excluded.

No LeaveRequest, LeaveRequestDay, LeaveRequestEvent, EmployeeLeaveBalance, or LeaveBalanceTransaction
is created or changed. No API, migration, reservation, approval, calendar, Sandwich, Clubbing
adjacency, attachment, or half-day runtime was added. Focused tests cover shape short-circuiting,
safe unlinked-identity failure, and deterministic fingerprint stability. Application compilation
succeeded with the existing NU1900 warning; Infrastructure compilation was blocked by the known
Codex ResolvePackageAssets environment failure with zero compiler diagnostics. The existing SQL
Server Balance/Ledger concurrency debt remains open. The next recommended bounded slice is review
of this validation foundation followed by request preview or explicitly authorized persistence/
submission work.

### Phase 4D.3D.1 conformance review

The preview controller remains thin: it accepts the four approved input fields, delegates to
`ILeaveRequestValidationService`, and maps the existing Result envelope. The public response no longer
exposes `PolicyPriority` or `PolicySpecificity`; those are resolver-ranking internals retained only in
the internal validation result. `PolicyGenderSnapshot` is also not public. RequestDay classification
and calculation reason remain nullable and are passed through without synthesized values.

Focused API coverage now includes authentication metadata, the exact overposting-resistant request
contract, absence of a submission route, server-derived context and quantities, RequestDay null
preservation, Allocated/Unlimited behavior, fingerprint mapping, unsupported configuration, and all
standard validation/identity/permission/not-found/conflict status mappings. The endpoint remains
strictly read-only. A dedicated employee self-service Leave permission is still required before final
production authorization hardening; no new permission was introduced in this review.

## Phase 4D.3D — Leave Request Preview API

The validated request foundation is exposed through an authenticated, read-only
`POST /api/leave-requests/preview` endpoint. The request body contains only `LeaveTypeId`,
`StartDate`, `EndDate`, and `IdempotencyKey`. Tenant and Employee identity remain server-authoritative;
policy, period, employment, quantities, RequestDays, entitlement mode, reservation requirement, and
fingerprint are never accepted from the client.

The controller delegates all decisions to `ILeaveRequestValidationService` and maps the existing
`Result<T>` statuses into the standard API envelope. The response contains safe resolved context IDs,
server-derived quantities, RequestDays, entitlement mode, `BalanceReservationRequired`, attachment
status, fingerprint, and nullable day classification/reason fields. It does not expose policy entities,
gender applicability internals, or balance values.

The endpoint requires authentication but does not use an administrative policy permission because no
dedicated employee self-service Leave permission currently exists. Existing tenant authentication and
linked Employee resolution remain authoritative. Unsupported configuration is returned as a stable
`UnsupportedConfiguration` validation failure; expected validation, not-found, unauthorized, forbidden,
and conflict results use the repository's standard mapping.

Preview has no persistence path: it does not call SaveChanges, create LeaveRequest/RequestDay/Event
rows, inspect or mutate balances, post ledger entries, reserve balance, or claim idempotency keys.
No schema, migration, frontend, calendar, Sandwich, Clubbing adjacency, attachment, approval, or
submission behavior was added. Focused API tests cover authentication metadata, response mapping,
authoritative result mapping, and unsupported-configuration mapping. Test/build execution remains
subject to the known Codex ResolvePackageAssets environment limitation where applicable.

### Phase 4D.3C.1 conformance review

The validator review confirmed that EmployeeId and LeaveTypeId are included in the semantic fingerprint;
the earlier report's shorthand reference to excluding IDs was imprecise. The canonical hash excludes
only runtime/generated metadata and the IdempotencyKey itself. The validator accepts no client-
authoritative tenant, Employee, policy, period, employment, or quantity totals, and its path contains
no persistence or balance/ledger mutation operations. Its restricted RequestDay baseline intentionally
leaves classification and calculation reason null because no authoritative calendar provider exists;
it does not fabricate a WorkingDay/holiday classification. Focused tests now cover shape short-circuiting,
unlinked identity, fingerprint stability, and fingerprint changes for Employee, LeaveType, dates, and
normalized days. Test execution remains subject to the documented Codex environment limitation.
### Phase 4D.3E — Leave Request Preview UI

The employee self-service preview screen is available at `/leave-management/apply` behind the authenticated application shell. It collects only Leave Type, Start Date, and End Date; an internal bounded idempotency key is generated once per draft session and reused for repeated previews, then replaced on Reset.

The screen calls `POST /api/leave-requests/preview` and displays only server-authoritative quantities, entitlement mode, reservation-required status, and normalized RequestDays. It does not calculate working days, expose tenant/employee inputs, show resolver-ranking internals, or provide a submission/persistence action. Nullable day classification and calculation reason values remain displayed as `—`. Unsupported configuration responses are shown as configuration limitations, and editing the draft clears stale preview data.

Focused UI tests cover the active Leave Type list, exact request shape, idempotency-key reuse/reset behavior, authoritative response rendering, nullable day values, unsupported configuration UX, and the absence of persistence calls. Request submission, persistence/idempotency conflict handling, balance reservation, and approval remain future work.

## Phase 4D.4A — My Leave Requests

Authenticated employees can read their own persisted leave requests through `GET /api/leave-requests`
and `GET /api/leave-requests/{requestId}`. The read service resolves TenantId, UserId, and EmployeeId
from the authenticated account-to-employee link, applies tenant and employee predicates, orders list
results newest first, and returns authoritative request, RequestDay, and persisted event data. Requests
belonging to another employee or tenant are returned as NotFound. This phase adds no status mutation,
approval, balance, or workflow behavior and requires no schema migration.

The self-service UI is available at `/leave-management/my-requests` with a read-only detail route at
`/leave-management/my-requests/{requestId}`. It displays server quantities, dates, status, day breakdown,
and actual persisted history events; no approve, reject, withdraw, cancel, or modify controls are shown.
