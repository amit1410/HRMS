# Phase 4 Leave Policy Foundation Review

Status: static read-only review. No source, migration, database, package, or operational configuration was changed by this review. The worktree was already dirty; existing changes were preserved.

## 1. Executive summary

The Phase 4 configuration model is conceptually coherent and additive: a tenant-owned `LeavePolicy` owns versions, each version owns Leave-Type rules, and detailed rules are attached to those rules. Published and Retired mutation guards are present in the configuration service, and the migration chain is additive and unapplied.

The foundation is **not ready for runtime Leave architecture design** until the high-severity tenant query-filter gap is corrected and Clubbing integrity is strengthened. `HrmsDbContext` filters Leave Types, Periods, Policies, Versions, PolicyRules, Eligibility, Entitlement, and Applicability, but does not filter Request, Calendar, Attachment, Clubbing, or Cancellation entities. Composite tenant-aware FKs and route checks reduce exposure, but the repository-wide tenant invariant is incomplete.

Other important review findings are: the database does not itself enforce that Clubbing participants belong to the same version or that a reverse pair cannot be inserted; Publish validation only checks Clubbing self-pairs and does not revalidate the complete Clubbing collection; LeavePeriod has no resolver/business timezone or calendar-basis implementation; and later-phase test evidence is incomplete in the current source tree.

## 2. Implemented architecture

Implemented configuration tables and typed entities are:

- `LeaveTypes`
- `LeavePeriods`
- `LeavePolicies`
- `LeavePolicyVersions`
- `LeavePolicyRules`
- `LeavePolicyApplicabilitySets`
- `LeavePolicyEligibilityRules`
- `LeavePolicyEntitlementRules`
- `LeavePolicyRequestRules`
- `LeavePolicyCalendarRules`
- `LeavePolicyAttachmentRules`
- `LeavePolicyClubbingRules`
- `LeavePolicyCancellationRules`

There are no runtime Leave request, balance, ledger, approval, or document-storage entities in the Phase 4 configuration implementation.

## 3. Entity/cardinality map

```text
LeavePolicy 1 -> many LeavePolicyVersion
LeavePolicyVersion 1 -> many LeavePolicyRule
LeavePolicyVersion 1 -> many LeavePolicyApplicabilitySet
LeavePolicyVersion 1 -> many LeavePolicyClubbingRule
LeavePolicyRule 1 -> zero-or-one EligibilityRule
LeavePolicyRule 1 -> zero-or-one EntitlementRule
LeavePolicyRule 1 -> zero-or-one RequestRule
LeavePolicyRule 1 -> zero-or-one CalendarRule
LeavePolicyRule 1 -> zero-or-one AttachmentRule
LeavePolicyRule 1 -> zero-or-one CancellationRule
```

The ownership model is internally coherent. Each PolicyRule represents one Policy Version plus one Leave Type. No detailed rule is attached directly to Policy, Version, Employee, or LeaveType.

## 4. Lifecycle model

Versions have server-assigned sequential `VersionNumber`, `Status`, effective dates, integer `Priority`, audit timestamps, and an opaque timestamp-derived concurrency token. Draft versions are editable; Published and Retired versions are guarded as immutable. Effective dates are separate from lifecycle status. No `LeavePeriodId` exists on PolicyVersion or detailed rules. Higher numeric priority wins before specificity in the resolver.

Draft cloning copies typed detailed rules and applicability. Leave Type removal preserves retained detailed children and removes the removed active rule; historical versions are protected by lifecycle and restrictive relationships.

## 5. Tenant isolation review

### Positive findings

- Leave entities carry `TenantId`.
- Leave parent/detail FKs use composite tenant-aware keys where configured.
- Service routes validate Policy -> Version -> Rule/LeaveType ownership.
- SaveChanges stamps new tenant rows and prevents TenantId mutation.
- Country is intentionally a global master; `CountryLocationId` is not tenant-composite.

### HIGH finding: incomplete global query filters

`HrmsDbContext` currently applies Leave filters to:

`LeaveType`, `LeavePeriod`, `LeavePolicy`, `LeavePolicyVersion`, `LeavePolicyRule`, `LeavePolicyEligibilityRule`, `LeavePolicyEntitlementRule`, and `LeavePolicyApplicabilitySet`.

It does not apply filters to:

`LeavePolicyRequestRule`, `LeavePolicyCalendarRule`, `LeavePolicyAttachmentRule`, `LeavePolicyClubbingRule`, or `LeavePolicyCancellationRule`.

This violates the stated defense-in-depth tenant architecture and can expose unscoped rows to direct DbSet queries or future consumers. Correct the filters before runtime design. This review did not modify code.

## 6. Permission matrix

| Operation | TypeManage | PeriodManage | PolicyView | PolicyManage | PolicyPublish |
|---|---:|---:|---:|---:|---:|
| View Leave Types | yes | - | - | - | - |
| Mutate Leave Types | yes | - | - | - | - |
| View Leave Periods | - | yes | - | - | - |
| Mutate Leave Periods | - | yes | - | - | - |
| View Policy configuration | - | - | yes | normally yes | not by itself |
| Mutate Draft policy configuration | - | - | no | yes | no |
| Validate Draft | - | - | no | yes | no |
| Publish/Retire | - | - | no | no | yes |

Controller attributes consistently separate PolicyView, PolicyManage, and PolicyPublish. `PolicyPublish` does not grant Draft configuration mutation, and `PolicyManage` does not grant Publish/Retire.

## 7. Policy resolution model

`LeavePolicyResolver` statically implements the intended shape:

1. Validate tenant Employee and LeaveType.
2. Resolve exactly one effective employment-history row.
3. Select active Published versions effective on the date whose parent Policy is active.
4. Match applicability predicates.
5. Rank by numeric Priority, then highest matching-set specificity.
6. Return `ConfigurationAmbiguity` for an unresolved tie.

This is sufficient as a foundation for design discussion, not runtime acceptance. Missing decisions include policy behavior across employment/policy boundaries, policy snapshot semantics for pending requests, business timezone, and LeavePeriod resolution.

## 8. Applicability model

Dimensions inside one `LeavePolicyApplicabilitySet` are ANDed. Sets are ORed. Zero sets are tenant-wide. Specificity is the number of populated dimensions in the best matching set. Priority is evaluated before specificity; equal final rank returns ambiguity.

Actual backend dimensions are Holding Company, LOB, Organization, Department, Sub Department, Section, Sub Section, Function, Sub Function, Grade, Designation, Employee Type, Country via `CountryLocationId`, Work Location, and Cost Center. There is no independent `LocationId` field. The Country/Location naming discrepancy remains an architectural decision for later review; it was not changed here.

No detailed rule duplicates applicability dimensions.

## 9. Eligibility review

Implemented typed fields are `EligibilityMode`, `MinimumServiceValue`, `MinimumServiceUnit`, `ProbationMode`, and `NoticePeriodMode`. No-row baseline is Immediate eligibility with probation and notice allowed. Draft-only mutation, conditional validation, and Publish validation are present.

Deferred: service-month boundary, rehire semantics, confirmation source, statutory formulas, and emergency exceptions. No speculative fields were found.

## 10. Entitlement review

Implemented modes are Allocated, Unlimited, and NoBalanceRequired. Implemented sources are PolicyAccrual, ExternalGrant, and NoBalanceRequired. Typed fields include quantity, accrual frequency, and accrual timing. Quantity uses decimal precision `decimal(9,3)`.

NoBalanceRequired is separate from negative balance. ExternalGrant is configuration only. No scheduler, balance, Attendance, Comp Off transaction, or Payroll integration exists.

Deferred: proration, rounding, carry-forward order/expiry, negative-balance limits, lapse, encashment, grant authority, and scheduled accrual execution.

## 11. Accrual review

Configured frequencies include None, Upfront, Monthly, SemiAnnual, and Annual. Quarterly remains enum-compatible but is rejected by service validation. This is **DESIGN CLEANUP RECOMMENDED**, not an immediate blocker: rejecting an enum value is safe for current writes, but the public enum and UI contract can mislead clients and should be reconciled when the accrual design is finalized.

## 12. Request Rule review

Typed fields cover minimum, maximum, and maximum consecutive quantities; minimum advance notice days; backdate mode/days; request and quantity limits; Month/LeavePeriod limit mode; and FullDayOnly/HalfDayAllowed partial-day modes. Quantity fields use decimal precision `decimal(9,3)`.

Quarter-day, hour, and shift modes are absent. The future runtime resolver must define whether Month means calendar month and how LeavePeriod means the independently resolved active period, including requests crossing period boundaries. No LeavePeriod FK was introduced.

## 13. Calendar/Sandwich review

Typed fields are HolidayTreatment, WeekOffTreatment, SandwichMode, and Prefix/Suffix/Between flags. Defaults are Holiday Exclude, Week-Off Exclude, and Sandwich Disabled. Normal holiday/week-off treatment is separate from sandwich configuration. No calendar source or date-expansion engine exists.

## 14. Attachment review

Typed fields are AttachmentRequirement, ThresholdQuantity, and DocumentLabel. Modes are None, Optional, Required, and RequiredAboveQuantity. Threshold precision is `decimal(9,3)` and service validation activates it only for RequiredAboveQuantity. No upload, storage, document runtime, file type, size, retention, or declaration/content implementation exists.

## 15. Clubbing review

Clubbing is version-level and references two PolicyRules. The service orders participants by stable Rule ID and rejects self-pairs, duplicate normalized pairs, unselected types, and invalid relationships. The only relation is `NotAllowed`, and the relationship is conceptually symmetric.

### HIGH integrity gap

The database unique index covers `(TenantId, VersionId, LowerRuleId, HigherRuleId)`, but the database cannot by itself prevent insertion of the reverse `(HigherRuleId, LowerRuleId)` row, nor ensure both Rule FKs belong to the same Version. Same-version and reverse-pair guarantees are service-only. Publish validation checks self-pairs but does not revalidate all membership, relation, and duplicate invariants from persisted rows. Strengthen the model/service before runtime design.

Exact adjacency, holiday/week-off bridging, partial-day adjacency, and chain evaluation remain deferred.

## 16. Cancellation review

The three independent boolean fields are `WithdrawAllowed`, `CancelAllowed`, and `ModifyAllowed`. No-row/all-false is the safe baseline. Draft-only PUT, PolicyView GET, and restrictive historical relationships are present.

Timing, approval-state behavior, modification model, approval reversal, balance restoration, Attendance, and Payroll are absent and correctly deferred. No runtime action endpoints were found.

## 17. Publish-validation coverage matrix

| Configuration | Draft validation | Publish validation | No-row/default valid | Blocking errors |
|---|---|---|---|---|
| Version metadata | service validation | yes | n/a | yes |
| Leave Type selection | service validation | yes | no active rules blocks | yes |
| Applicability | service validation | yes | zero groups valid | yes |
| Eligibility | service validation | yes | yes | yes |
| Entitlement | service validation | yes | yes | yes |
| Request Rules | service validation | yes | yes | yes |
| Calendar | service validation | yes | yes | yes |
| Attachment | service validation | yes | yes | yes |
| Clubbing | save validation; persisted publish check is partial | partial: self-pair only | yes | incomplete |
| Cancellation | booleans have no invalid combination; no explicit validator | no explicit validator | yes | none required for current fields |

The primary coverage defect is Clubbing: Publish does not fully revalidate the persisted collection. Cancellation currently has no invalid state beyond boolean values, but an explicit no-op validator would make coverage auditable.

## 18. Published immutability

All reviewed service mutation paths for Version Settings, Leave Types, Applicability, Eligibility, Entitlement, Request, Calendar, Attachment, and Cancellation check Draft status. Clubbing also checks Draft status. Publish and Retire enforce their lifecycle transitions. Restrictive FKs preserve historical configuration.

Frontend read-only behavior is present across the editor. Backend immutability is the authoritative guard. The incomplete global filters and Clubbing integrity gaps remain HIGH risks even though they do not directly permit normal lifecycle mutation.

## 19. Concurrency review

Policy identity, version metadata, Leave Type assignment, Applicability, detailed per-Type rules, and lifecycle use the timestamp-derived opaque token and map stale writes to 409 in the service. Clubbing replacement currently checks the version token but does not wrap its save in the same explicit concurrency exception mapping as most detailed rules. This is MEDIUM consistency debt: verify provider behavior and return contract before runtime writes depend on it.

The token is an audit timestamp rather than a database rowversion. That is an accepted current convention but should be revisited for high-contention runtime aggregates.

## 20. API consistency review

Policy identity/version/configuration APIs use a consistent `/api/leave-policies/{policyId}/versions/{versionId}` hierarchy. Per-Leave-Type rules use nested `eligibility`, `entitlement`, `request-rules`, `calendar`, `attachments`, and `cancellation` routes. Clubbing correctly uses a version-level aggregate route. GET is read-oriented and PUT is aggregate replacement/upsert. Controllers use shared result-to-action handling for 400/403/404/409.

Recommended cleanup: normalize singular/plural naming (`calendar` versus `attachments`, `request-rules`) and centralize detailed-rule error/concurrency behavior. No route change is made in this review.

## 21. Frontend architecture review

One `LeavePolicyEditorPage` composes identity, version, Leave Types, applicability, Eligibility, Entitlement, Request Rules, Calendar, Attachment, Clubbing, Cancellation, lifecycle, and deferred cards. This preserves one editor but creates giant-component and repeated-fetch risk. Detailed sections independently load per Leave Type and use local state; dirty state is reported upward but is not a centralized cross-section model.

Observed recommendations: add a shared detailed-rule hook, standardize section error/loading/dirty behavior, and add a version-switch confirmation that covers every section. Existing lint warnings include synchronous state updates in effects. These are MEDIUM/LOW maintainability concerns, not changes made here.

## 22. Frontend master lookup review

Applicability reuses `MasterDropdown` and existing master APIs. Values are displayed as Code - Name and IDs are submitted. Parent changes clear dependent children. Country uses the global master contract. Historical inactive references are preserved by the Leave Type flow.

Because `MasterDropdown` is shared, its changes should receive regression coverage on non-Leave master and employment forms. No separate Leave master client was introduced.

## 23. Migration inventory

| Migration | Up tables | Existing tables altered | FK/delete posture | Data/destructive operations |
|---|---|---|---|---|
| `20260904090754_LeavePolicyFoundation` | LeavePeriods, LeavePolicies, LeaveTypes, LeavePolicyVersions, LeavePolicyApplicabilitySets, LeavePolicyRules | none | tenant-aware, Restrict | none |
| `20260904110149_LeavePolicyEligibilityRules` | LeavePolicyEligibilityRules | none | Rule one-to-one, Restrict | none |
| `20260904111427_LeavePolicyEntitlementRules` | LeavePolicyEntitlementRules | none | Rule one-to-one, Restrict | none |
| `20260904120000_LeavePolicyRequestRules` | LeavePolicyRequestRules | none | Rule one-to-one, Restrict | none |
| `20260904123000_LeavePolicyCalendarRules` | LeavePolicyCalendarRules | none | Rule one-to-one, Restrict | none |
| `20260904130000_LeavePolicyAttachmentRules` | LeavePolicyAttachmentRules | none | Rule one-to-one, Restrict | none |
| `20260904133000_LeavePolicyClubbingRules` | LeavePolicyClubbingRules | none | version/two-rule FKs, Restrict | none |
| `20260904140000_LeavePolicyCancellationRules` | LeavePolicyCancellationRules | none | Rule one-to-one, Restrict | none |

The chain is chronologically ordered and additive. Each Down `DropTable` is normal rollback behavior, not an Up destructive operation.

## 24. Migration safety review

Static search across the eight Leave migrations found no Up `DropTable`, `DropColumn`, destructive `AlterColumn`, `RenameColumn`, `RenameTable`, `Sql`, `InsertData`, `UpdateData`, or `DeleteData`. The only matching drops are rollback methods. No operational data migration or seed is present.

The chain is **MEDIUM deployment risk** before preflight: schema changes are additive and individually small, but the first migration introduces several tenant-aware FKs to existing/master tables and the migrations have not been exercised against the target SQL Server in this review. No migration was generated or applied.

## 25. Model snapshot review

The final snapshot contains each expected Leave entity once, including all eight detailed/configuration tables and Clubbing relationships. Typed enum, decimal, unique ownership, and Restrict FK metadata are present. No Leave runtime entities were found in the snapshot.

The snapshot reflects the model, but it does not correct the missing runtime query filters or database-level Clubbing semantic constraints; those are code/model review findings rather than snapshot duplication findings.

## 26. Runtime dependency review

Future Apply Leave identity should use `GET api/auth/me` and its current tenant-scoped account-to-employee link. Existing link history preserves prior ownership. This review does not claim browser acceptance complete.

`EmployeeManagerResolver` is a future routing dependency only. Its existence does not make Leave approval ready.

## 27. Effective employment readiness

`EmployeeEmploymentHistory` contains all current applicability dimensions represented by the Policy model, including the CountryLocationId, Work Location, and Cost Center fields. The resolver already requires exactly one effective row and rejects no-row/overlap states. It is **conditionally ready for runtime design**, subject to business-date/timezone, transfer-boundary, and unresolved eligibility decisions.

## 28. LeavePeriod readiness

**NOT READY** for runtime resolution. The current model supports tenant-scoped Code, dates, and active state plus service overlap checks, but has no resolver, calendar basis, timezone/business-date policy, explicit status model beyond `IsActive`, or predecessor/successor semantics described by the design. These must be specified before a balance or request engine depends on one active period.

## 29. Open decision register

### Eligibility

Service-month boundary, rehire behavior, authoritative confirmation source, statutory formulas, and emergency exceptions.

### Entitlement

Proration formula, rounding, carry-forward consumption/expiry, negative-balance semantics, lapse, encashment, ExternalGrant authority, and actual accrual scheduling.

### Request

Planned/unplanned semantics, notice-band semantics, emergency exceptions, quarter-day/hour/shift support, and period-boundary behavior.

### Calendar

Holiday source, weekly-off source, business-day/timezone treatment, and runtime sandwich date expansion.

### Attachment

Declaration/content, storage provider, retention, accepted formats, size/count limits, and request-time validation.

### Clubbing

Exact preceding/succeeding/intervening adjacency, holiday/week-off bridging, partial-day behavior, and chain semantics.

### Cancellation/workflow

Timing, approval-state behavior, modification model, manager routing, approval reversal, balance restoration, Attendance/Payroll reversal, delegation, escalation, and auto-approval.

### Runtime

Policy evaluation date, pending-request versioning, balance creation, immutable ledger transactions, recalculation/retroactivity, idempotency, and audit/event strategy.

## 30. Runtime-blocking decision classification

### A. Must resolve before runtime Policy Evaluator

- tenant business date/timezone
- effective employment overlap and transfer-boundary behavior
- LeavePeriod resolution and calendar basis
- policy resolution tie/fallback behavior confirmation
- exact applicability Country/Location interpretation
- pending request policy-version retention/revalidation

### B. Must resolve before Balance Engine

- entitlement accrual timing/scheduling
- proration and rounding
- carry-forward order/expiry
- negative balance
- lapse and encashment
- ExternalGrant authority
- period boundary and retroactive policy strategy

### C. Must resolve before Apply Leave

- request quantity/partial-day semantics, including Month versus LeavePeriod limits
- calendar/holiday/week-off sources and sandwich expansion
- Clubbing adjacency semantics
- attachment/declaration request-time behavior
- cancellation/modify/withdraw timing and state behavior
- account identity and not-linked behavior
- idempotency and request concurrency

### D. Can defer beyond initial Apply Leave

- statutory packs not required by the initial tenant
- quarter/hour/shift support
- advanced delegation/escalation
- Payroll/Attendance integration
- document retention/storage provider details if attachments are not enabled

## 31. Test coverage/evidence matrix

| Area | Evidence status | Review result |
|---|---|---|
| Foundation/resolver | Authored; documented prior 11/11 pass, current suite not rerun | not executed in this review |
| Applicability | Authored frontend/backend sources; no current runtime execution evidence | authored but not executed |
| Lifecycle | Frontend lifecycle test source exists; environment previously blocked Vitest | authored but not executed here |
| Eligibility | Frontend focused test source exists; backend coverage is mixed into foundation tests | authored but not executed here |
| Entitlement | Frontend focused test source exists | authored but not executed here |
| Request Rules | Frontend focused test source exists | authored but not executed here |
| Calendar | Frontend focused test source exists | authored but not executed here |
| Attachment | Component exists, focused test file not found | missing focused evidence |
| Clubbing | Component exists, focused test file not found | missing focused evidence |
| Cancellation | Three frontend tests and two authorization tests authored in source | not executed; environment blocked |
| Authorization | `LeaveConfigurationEndpointAuthorizationTests.cs` authored; documented prior 14/14 was for an earlier scope | not rerun |
| Tenant isolation | Existing broader tests plus resolver tests; detailed-rule end-to-end evidence incomplete | incomplete |
| Concurrency | Sources contain 409 handling and focused frontend patterns | runtime execution pending |
| Publish validation | Service coverage exists but Clubbing persisted-row validation is partial | needs correction |

No unexecuted test is claimed as passed in this review.

## 32. Validation-environment debt

- Backend: Codex may silently fail `ResolvePackageAssets` with exit 1 and no actionable diagnostics. User normal PowerShell previously proved Infrastructure build success with `--no-restore` and `MSBuildEnableWorkloadResolver=false`.
- Frontend: Codex Vitest/Vite may fail before discovery with `spawn EPERM` while loading `vite.config.ts`.
- These are execution limitations, not source findings. Normal PowerShell validation remains required.

## 33. Migration deployment risk

Overall classification: **MEDIUM**.

Reasons for low risk: additive tables, no data backfill, typed columns, tenant-aware FKs, Restrict deletes, and normal rollback methods.

Reasons it is not Low: first deployment requires SQL Server compatibility and tenant/data preflight; the Clubbing database invariant is incomplete; and all migrations are currently unapplied.

## 34. Proposed legacy/read-only preflight

Before any future migration application, perform a read-only inventory that checks for pre-existing table/name collisions; duplicate tenant codes; duplicate LeaveType/Policy/Period codes; overlapping active LeavePeriods; invalid or cross-tenant master references; duplicate Policy Version numbers; duplicate PolicyRule Version+LeaveType pairs; invalid detailed-rule ownership; and Clubbing self/reverse/cross-version pairs. Confirm target SQL Server compatibility, backup/rollback plan, migration history, and absence of unexpected operational Leave tables. No such preflight or database access was performed here.

## 35. Recommended next phase

Do not begin implementation yet. First correct the HIGH tenant-filter and Clubbing-integrity findings, then approve the unresolved runtime decisions. Once those are addressed, the recommended next phase is:

**PHASE 4D.0 - RUNTIME LEAVE ARCHITECTURE DESIGN**

Design only: Policy Evaluator, LeavePeriod Resolver, effective employment snapshot, employee identity source, entitlement interpretation, balance ledger, accrual transactions, Leave request aggregate/day representation, validation, Calendar/Sandwich, Clubbing, attachments, cancellation runtime, workflow boundary, manager integration, concurrency/idempotency, retroactivity, and audit/event strategy.

## Final readiness decision

**NOT READY FOR RUNTIME LEAVE ARCHITECTURE DESIGN**

Genuine blockers:

1. Missing global query filters for five tenant-owned detailed rule entities.
2. Clubbing reverse-pair and same-version integrity is not database-enforced, and Publish validation is incomplete for persisted Clubbing rows.

## Phase 4C.11 Remediation Status

Phase 4C.11 addressed the scoped structural findings without beginning runtime Leave architecture. `HrmsDbContext` now applies the established tenant query filter to Request, Calendar, Attachment, Clubbing, and Cancellation rules. Country remains the intentional global-master exception.

Clubbing hardening adds the non-destructive `LeavePolicyRules` alternate key `(TenantId, LeavePolicyVersionId, Id)`, version-aware composite foreign keys for both participants, a `CK_LeavePolicyClubbingRules_DifferentParticipants` check constraint, and a persisted computed canonical unordered-pair key based on the textual GUID representation. The unique index is scoped to TenantId, PolicyVersionId, and that canonical key, so reverse pairs collide independently of application insertion order or GUID comparison implementation. The application continues to normalize and validate pairs, and Publish now independently validates tenant/version ownership, selected active participants, self-pairs, duplicates/reverse pairs, and relation values. Exact adjacency evaluation remains deferred.

The Application now exposes `ILeavePeriodResolver`. It uses active rows and inclusive `StartDate <= EffectiveDate <= EndDate` semantics, returning `Resolved`, `NotConfigured`, `ConfigurationAmbiguity`, or `InvalidTenant`. It returns typed configuration only and does not calculate balances, requests, accrual, calendar days, or runtime Leave behavior. Month-limit semantics and business calendar/timezone semantics remain open.

New authored tests cover the resolver boundaries, ambiguity, tenant scope, insertion-order independence, and the five previously missing detailed-entity query filters. Codex could not execute the Tests project because the known no-diagnostic ResolvePackageAssets environment failure/hang prevented test discovery. Domain and Application builds passed; Infrastructure, Tests, and API Codex attempts remained environment-limited. No frontend behavior changed, no migration was applied, and no database was accessed.

The hardening migration is `20260904150000_LeavePolicyFoundationHardening.cs` with its Designer. It alters only the existing Clubbing/PolicyRule schema as required for integrity and contains no data operations. Before application, a read-only preflight must detect existing self-pairs, reverse duplicates, cross-version/cross-tenant participants, and missing rules. Subject to successful normal-PowerShell build/test and migration preflight, the Policy foundation is now ready for runtime Leave architecture design.

### Reassessment

- Previous HIGH 1 tenant filters: **RESOLVED** in source.
- Previous HIGH 2 Clubbing integrity: **RESOLVED structurally** for new schema state; existing unapplied-target data still requires the documented preflight.
- Previous HIGH 3 Clubbing Publish validation: **RESOLVED** in service validation.
- LeavePeriod readiness: **READY FOR FUTURE RUNTIME ARCHITECTURE DESIGN** for the deterministic period lookup contract; business calendar/timezone and Month semantics remain later decisions.

## Phase 4C.11 final readiness

**READY FOR RUNTIME LEAVE ARCHITECTURE DESIGN**

This readiness decision is architectural only. It does not authorize Apply Leave, LeaveRequest, balances, ledger, approval workflow, or database migration application.
