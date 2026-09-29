# Module 7 — Leave Management (Types, Policies, Periods, Eligibility, Entitlement/Accrual, Balances,
# Requests, Working Days, Approval, Comp Off, Calendar, Dashboard, Reports, Reminder Worker, Authorization/
# Scope, Tenant Isolation, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py`
regenerates `qa/HRMS_Test_Cases.xlsx` across all seven modules with no errors). No automation implemented
yet (Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (319 functionalities: F-LEAVE-001…319). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-leave-types-policies.yaml` … `cases/16-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to an Excel worksheet — see the sheet table below (16 source files produce 15 worksheets; Cancellation/Withdrawal shares the `Leave_Approvals` sheet with Approval, and HR Dashboard shares `Leave_Calendar` with Calendar). |
| `clarifications.yaml` | The CR-77…CR-90 QA-risk register for this module, plus short cross-references to CR-53 (Module 5), CR-57 (Module 5/6), and CR-66 (Module 6) that this module's cases exercise directly. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr` field
meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical to
[Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here.

## The real implementation (source-verified, not assumed from the brief's example section list)

Discovered by reading all 13 controllers (`LeaveTypesController`, `LeavePoliciesController`,
`LeavePeriodsController`, `LeaveRequestsController`, `LeaveApprovalsController`,
`LeaveRequestOptionsController`, `LeaveBalanceSummaryController`, `LeaveBalanceImportsController`,
`LeaveCalendarController`, `HrLeaveDashboardController`, `LeaveReportsController`,
`LeaveWorkingDayConfigurationController`, `CompOffController`); every Application-layer Leave/CompOff
service (policy/period resolution, the eligibility engine, accrual/proration, balance accounting on both
the simple ledger and the grant-allocation layer, submission/approval/cancellation/withdrawal, the
authorization service, the reminder processor, working-day calendar resolution, Comp-Off earning/approval/
expiry/correction); every Domain entity, EF configuration, and enum under the Leave/CompOff surface; both
migration chains; `Permissions.cs`/`SeedData.RolePermissionMap`'s Leave/CompOff slice; every Leave/CompOff
frontend page and API client; and the full existing xUnit (58+ classes)/Vitest inventory touching this
surface. `docs/phase-4*.md` were read but current code wins wherever they diverge, per `CLAUDE.md`.

**Leave is a deliberately restricted MVP wearing a much larger configuration UI.** `LeaveRequestValidationService
.ValidateSupportedPolicy` — the one gate every Preview/Submit call passes through — hard-rejects any resolved
policy rule where `PartialDayMode` is `HalfDayAllowed` (not `FullDayOnly`), `SandwichMode` is anything but
`Disabled` (or any `ApplyTo*` flag is set), `AttachmentRequirement` is anything but `None`, or a `NotAllowed`
Clubbing rule references the resolved rule. `LeavePolicyFoundationService.ValidateForPublishAsync` never checks
any of this before allowing Publish, and the Policy Editor UI (`LeavePolicyRequestRulesSection.tsx`,
`LeavePolicyCalendarSection.tsx`, `LeavePolicyAttachmentSection.tsx`, `LeavePolicyClubbingSection.tsx`) freely
lets an administrator configure all four — a policy published with any of them becomes entirely unusable for
real leave requests, discovered only when an employee actually tries (**CR-81**). `AccrualFrequency.Quarterly`
is explicitly blocked at config time with its own message; `SemiAnnual` is **not** blocked, saves and publishes
cleanly, and then never accrues anything — `LeaveAccrualProcessor.IsDue` simply has no branch for it (a silent
dead configuration, not an error).

**The single highest-impact finding in this module (CR-82, arguably worse than Module 6's CR-66):**
`GET /api/attendance/comp-off/operations` — the manager/HR-facing, multi-employee, richly-filterable Comp-Off
query — carries **no `[HasPermission]` attribute at all**. Every other action in every other controller in
this module (and, per Module 6, across the whole app) declares one; this one relies solely on the
controller-level `[Authorize]`, meaning any authenticated user of any role reaches the service method, which
then performs its own internal permission scan (`CompOffViewAll` → `CompOffViewTeam` → `CompOffManage` →
`CompOffApprove` → `CompOffViewHistory`, first match wins). The end result (only certain roles get data back)
happens to be correct, but the *pattern* is structurally different from every other endpoint in Modules 1–7 and
is invisible to any audit that greps for `[HasPermission]` attributes — the frontend's own
`CompOffOperationsPage.tsx` compensates with a client-side `canAny(...)` gate, but that is cosmetic against a
direct API call.

**HRAdmin and HRManager are both permission-gapped for Leave, in different ways (CR-78, CR-79).** HRAdmin holds
only `Leave.DashboardViewAll`/`ReportsView`/`ReportsExport` — no `TypeManage`/`PeriodManage`/`PolicyManage`/
`PolicyPublish`/`Approve`/`BalanceImport`/`BalanceViewImportHistory`. HRManager holds **zero** `Leave.*` or
`Attendance.CompOff.*` permissions of any kind. Only SuperAdmin/TenantAdmin can configure or publish a Leave
Policy, Type, or Period, import balances, or approve leave in the seeded matrix — and (**CR-66** cross-reference,
confirmed concretely for Leave here) an HRAdmin or SuperHR session can self-grant every one of those withheld
permissions in one `PUT /api/page-access/roles/{roleId}` call, defeating this design entirely.

**Leave has its OWN, third, independent "who can see/act on whose leave" mechanism (CR-77), on top of the two
Module 5/6 already found.** `LeaveAuthorizationService.BuildCurrentManagerPredicate`/`CanAccessEmployeeAsync`
resolves "is the caller this employee's current manager" directly off `EmployeeEmploymentHistory.ManagerId` —
code-independent from both `RoleScopeResolver`/`UserRoleAssignmentScope` (Module 6) and
`EmployeeAccessScopeService`'s Attendance-permission-keyed narrowing (Module 5/6, **CR-57**). It never inspects
which *role* granted the caller's `Leave.Approve` — an HRBP who happens to be someone's manager-of-record is
authorized via this path exactly like an actual Manager-role holder. Several Leave services additionally carry
a parallel **"legacy" fallback** (used when `ILeaveAuthorizationService` is not registered in DI, as some
existing xUnit harnesses construct services directly) with materially different visibility rules — in
particular, `LeaveCalendarService`'s legacy branch shows a non-self viewer only Approved events unless they
hold `Leave.Approve` **and** are the exact current manager, with **no role-scope fallback at all** (**CR-83**).

**CR-53 (separated employees) has a genuinely two-part story in Leave, confirmed directly.** The account-level
retention gap (Module 5/6's CR-53) means a terminated employee's balance (`GET /api/leave-balances/mine`),
calendar events, and manager-inbox visibility all remain exactly as before separation — nothing re-checks
`EmploymentEligibility`. **But** Leave has its own, genuine, working business rule that CR-53 does *not*
explain: `LeaveRequestValidationService.ValidateEmploymentContext` correctly blocks a **new** request dated
after `DateOfLeaving`. The two must not be conflated — one is a login/RBAC-retention gap, the other is Leave's
own submission-time eligibility gate, and this module's cases test both explicitly (`F-LEAVE-289`…`293`).

**Two independently-verified functional gaps not obviously visible from the controller/permission layer alone
(CR-89, CR-90):** `LeavePolicyCancellationRule.WithdrawAllowed` is a real, saved, UI-rendered policy toggle that
`LeaveRequestWithdrawalService` never reads — withdrawal of any `PendingApproval` request is unconditionally
allowed regardless of this flag, unlike Cancellation's own `CancelAllowed`, which **is** enforced (and, per
`F-LEAVE-210`, defaults to *restrictive* when the rule is entirely absent — the opposite of Eligibility/
Calendar/Attachment's permissive-by-default convention). Separately, Withdrawal has **no** Comp-Off-specific
branch at all — Submission, Approval, and Cancellation each explicitly check `LeaveType.IsCompOff` and delegate
to `ICompOffService`; Withdrawal does not, risking a stranded `Reserved` `CompOffLeaveAllocation` row when a
pending Comp-Off leave request is withdrawn (`LEAVE-COMPOFF-022` tests this directly as an open question, not
an asserted defect).

**Provider-parity risks are real but narrower than a first read suggests.** SQLite takes **no lock at all**
during Leave request submission/approval/cancellation/withdrawal (`SqlServerLeaveRequestSubmissionLock
.AcquireAsync` returns immediately on SQLite) — the employee-row lock (`SELECT…FOR UPDATE` on MySQL,
`WITH (UPDLOCK, HOLDLOCK)` on SQL Server) is the *entire* concurrency-control mechanism, and a SQLite-only test
run proves nothing about it. `IsIdempotencyViolation`'s own duplicate-key detection checks SQL Server's
2601/2627 numbers explicitly but has no equivalent MySQL 1062 numeric check — correctness on MySQL depends on
an index-name-string match inside the exception chain that this module's own research could not independently
confirm actually fires (**F-LEAVE-317**). A MySQL-specific `AddCompOffLeaveTypePhase6D` migration exists with no
SQL Server counterpart in the inventory reviewed — flagged for a follow-up schema-diff, not asserted as a live
defect.

## Coverage matrix — which behavior is exercised where

| Behavior | Fully exercised on | Spot-checked on | Kind-specific cases |
|---|---|---|---|
| Leave Type / Policy / Version / per-type rules | CRUD, uniqueness/immutability, versioning state machine, publish/retire gates, applicability groups | **CR-81's four unreachable-at-runtime settings**, the home-grown ConcurrencyToken's fail-closed-on-missing behavior | Baseline-row-deletes-the-rule pattern (Eligibility/Calendar/Attachment permissive vs. Cancellation restrictive) |
| Policy Effective Dating | Priority-then-specificity resolution, inclusive boundaries, expired/future/open-ended, overlap/gap | **Same-priority-same-specificity ConfigurationAmbiguity**, publish-time supersession (truncate vs. retire predecessor) | The built-in `POST /leave-policies/test` resolver-debugger endpoint |
| Leave Periods / Eligibility Engine | Period resolution, closed-period requests, MinimumService/Probation/AfterConfirmation boundaries | **Overlap prevention only checked for IsActive periods**, ProbationMode's Unsupported-vs-Ineligible distinction | Notice-period `AllowedWithApproval`≡`Allowed` at runtime (F-LEAVE-083 area) |
| Entitlement / Accrual | Daily/Monthly/Annual due-date rules, proration formula (worked examples), MaximumAccumulation cap, carry-forward FIFO | **SemiAnnual configures but never accrues**, occurrence claim/lease/replay idempotency | Manually-calculated proration test data (24/365 and 1.5/31 worked examples) |
| Balance / Import | Reservation/consumption/release grant-FIFO mechanics, DB-level non-negative check constraint | **Two-phase Validate→Commit atomicity**, malformed-string round-trip in error rows | Cross-tenant EmployeeCode resolution |
| Request Options / Creation | Preview→Submit, idempotency replay, overlap/limit/consecutive-day rules, employment-context boundary | **`GET /leave-types/available` is NOT eligibility-filtered (contradicts the brief's own expectation)** | Fri+weekend+Mon consecutive-quantity streak subtlety |
| Working Days | Holiday/weekly-off specificity tie-break, sandwich-mode (unreachable at runtime) | Holiday-over-weekly-off precedence on the same date | "Alternate weekends" is not a first-class concept — overlapping WeeklyOff rows both apply |
| Balance Reservation / Concurrency | Grant-FIFO allocation, employee-row-lock serialization, deadlock retry (3 attempts, 25ms) | **Release of an already-expired grant destroys balance via inline expiry, not restore**, MySQL/SQL Server lock-code numbering confusion documented explicitly | SQLite's lock is a true no-op, not merely weaker |
| Approval / Manager Inbox | Approve/Reject, self-approval hard block, manager-change re-resolution, CompOff-branching | **CR-77's three-way scope-mechanism proof**, legacy-fallback's narrower behavior | Attendance-day-reprocessing failure rolling back a whole approval |
| Cancellation / Withdrawal | Owner-only, status-machine gates, no manager-cancel path exists at all | **CR-89 (WithdrawAllowed ignored)**, CR-90 (Comp-Off withdrawal has no release branch) | Cancel-after-period-close balance-restoration target |
| Comp Off | Earn/approve/reject, credit math (ratio/rounding/day-cap/month-cap), expiry, correction, Leave-integration Reserve/Consume/Release/Restore | **CR-82's missing permission attribute**, employeeId/leaveRequestId pairing (un)validated on Reserve | EligibilityMode's unconstrained free-text field |
| Calendar / Dashboard | Own+team visibility, date-range validation, KPI/aging/trend widgets, filter narrowing | **CR-83's new-vs-legacy PendingApproval visibility divergence**, HRAdmin-vs-HRManager dashboard access | includeManager=false on Dashboard/Reports vs. true on Approvals/Calendar |
| Reports | All 7 report kinds + CSV export, paging/sort/filter, MaxExportRows cap | Calendar report's unscoped-by-employee-authorization shape, unsupported-report-name handling | Accounting report's 3-source merge |
| Reminder Worker | Eligibility/occurrence numbering, claim/lease/retry, restart durability, manager-change re-resolution | **MaxAttempts-reached is silent and permanent for that occurrence**, disabled-by-default (CR-86) | Concurrent-worker claim race (MySQL, opt-in) |
| Authorization / Scope Matrix / CR-77 | Full role×permission matrix, Self/Manager/HRBP/ERO/Tenant-wide combinations | **CR-77 proof set (zero-scope manager approval, role-agnostic manager check, Manager+HRBP union)**, legacy-path narrower fallback | ERO holds zero Leave permissions at all |
| CR-53 (Leave-side) | Balance/calendar/inbox retention post-separation, genuine submission-time eligibility gate | Approve-after-separation (approver-only check) | Distinguishing CR-53 from Leave's own ValidateEmploymentContext |
| CR-66 Cross-Reference | HRAdmin/SuperHR self-escalation into full Leave configuration+approval | End-to-end: escalate, refresh token, then actually create+publish a policy | — |
| Tenant Isolation | Consolidated matrix across configuration/requests/balances/CompOff/reminders/reports | **Configuration-table lookups rely on shard boundary alone (no redundant TenantId filter)**, worker per-tenant-scope switching | Cross-tenant EmployeeCode balance-import probe |
| Frontend | Loading/empty/error states, permission-gated actions, 409-vs-400 messaging, direct navigation, refresh | Policy Editor's server-driven `AllowedActions`, Comp-Off's Attendance-area placement | Client-side-only gate on Comp-Off Operations (backs CR-82) |

## Sheets produced for this module

| Sheet | Cases | Steps | Focus |
|---|---:|---:|---|
| Leave_TypesPolicies | 37 | 76 | Leave Type + Policy/Version/rule-sub-resource CRUD, versioning state machine, publish/retire gates, applicability groups |
| Leave_EffectiveDates | 14 | 28 | LeavePolicyResolver priority/specificity tie-break, boundaries, supersession, ConfigurationAmbiguity |
| Leave_PeriodEligibility | 29 | 54 | Leave Periods, MinimumService/Probation/AfterConfirmation/Notice-Period eligibility engine |
| Leave_Accrual | 19 | 34 | Accrual due-dates, proration (worked numeric examples), carry-forward, entitlement expiry |
| Leave_Balance | 27 | 49 | Self-service balance read, Opening Balance / Balance Import (two-phase Validate→Commit) |
| Leave_Requests | 28 | 62 | Request Options, Preview/Submit pipeline, min/max/consecutive/notice/limit rules |
| Leave_WorkingDays | 11 | 19 | Holiday/weekly-off specificity, sandwich-mode (unreachable), calendar-rule day-classification |
| Leave_Concurrency | 16 | 27 | Grant-FIFO reservation, employee-lock serialization, deadlock retry, provider-parity notes |
| Leave_Approvals | 31 | 59 | Approve/Reject workflow, Manager inbox, **Cancellation/Withdrawal** (10-cancellation.yaml shares this sheet) |
| Leave_CompOff | 30 | 56 | Earning/approval/expiry/correction, Operations query, Leave-integration Reserve/Consume/Release/Restore |
| Leave_Calendar | 15 | 21 | Team Leave Calendar **and HR Leave Dashboard** (12-calendar-dashboard.yaml shares this sheet) |
| Leave_Reports | 15 | 33 | All 7 report kinds, CSV export, filter/sort/paging |
| Leave_Reminders | 14 | 30 | Eligibility, occurrence numbering, claim/lease, retry, restart, tenant isolation |
| Leave_Security | 25 | 46 | Permission matrix, Self/Manager/Scoped/Tenant-wide, CR-77 proof set, CR-53/CR-66 cross-reference, tenant isolation |
| Leave_Frontend | 14 | 31 | Page-level loading/empty/error/permission-gated behavior |

**Total: 325 test cases, 625 steps, 319 functionalities** (`python qa/tools/build_test_catalogue.py`
regenerates and reports these numbers; treat `coverage-stats.json` as authoritative over this table if they
ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-77**: Leave has its own, third, independent manager/scope-resolution mechanism
  (`LeaveAuthorizationService`), code-separate from Module 6's `RoleScopeResolver` and Module 5/6's
  Attendance-permission-keyed `EmployeeAccessScopeService` (CR-57) — plus a parallel "legacy" fallback inside
  several Leave services with materially different behavior.
- **CR-78 / CR-79**: HRAdmin holds almost no Leave configuration/approval permissions; HRManager holds none at
  all — only SuperAdmin/TenantAdmin can fully administer the module in the seeded matrix.
- **CR-80**: `Leave.BalanceView`, `Leave.BalanceAdjust`, and `Attendance.CompOff.ViewHistory` are seeded but
  dead — required by no endpoint, granted to no role usefully.
- **CR-81**: Half-day leave, sandwich-day treatment, attachment requirements, and clubbing rules are all fully
  configurable and publishable, and all four are hard-rejected at actual request time — a policy published with
  any of them is silently broken.
- **CR-82 (the single highest-impact finding)**: `GET /api/attendance/comp-off/operations` has no declarative
  permission attribute at all — authorization lives entirely inside the service.
- **CR-83**: `LeaveCalendarService`'s "new" vs. "legacy" code paths expose materially different PendingApproval
  visibility rules, an artifact of DI wiring rather than a documented decision.
- **CR-84**: Leave-approval "manager" access is really "whoever's EmployeeId is ManagerId on someone's current
  employment row," independent of the RBAC `Manager` role name entirely.
- **CR-85**: Balance-import's Employee/LeaveType/LeavePeriod lookups have no explicit TenantId filter
  (correctness currently rests on the shard boundary alone).
- **CR-86**: Both the Accrual Worker and the Reminder Worker are disabled by default, with no seeded
  configuration turning them on.
- **CR-87**: Cross-*policy* overlap (as opposed to cross-*version*) is never blocked at Publish time, only
  passively counted for display.
- **CR-88**: Every Leave configuration page renders its full mutating UI regardless of the caller's actual
  granted permissions, relying on a 403 at submit time (mirrors Module 4/5's CR-44 pattern).
- **CR-89**: `WithdrawAllowed` is a real, saved, displayed policy flag with zero runtime enforcement effect.
- **CR-90**: Withdrawing a pending Comp-Off leave request has no Comp-Off-specific release branch, unlike
  Submission/Approval/Cancellation, which all have one.

`clarifications.yaml` also carries short cross-references to **CR-53** (Module 5, separated employees keep an
active login — Leave confirms the read-side gap directly while also confirming its OWN genuine,
submission-time-only eligibility gate is unrelated and does work), **CR-57** (Module 5/6, the Attendance-
permission-keyed Employee-module scope mechanism — Leave's CR-77 is a *third*, independent mechanism, not a
reuse of this one), and **CR-66** (Module 6, PageAccess.Manage self-escalation — confirmed here to reach every
Leave.* permission HRAdmin/SuperHR are otherwise denied, in one call).

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a product
decision, per the classification column in `clarifications.yaml`.

## Existing automated coverage referenced by this module

This module's case files cite `ex:` existing-test references more sparingly than Modules 1–6 — the sheer size
of the Leave/CompOff xUnit inventory (58+ classes covering foundation/API/concurrency/provider-acceptance
layers for every lifecycle action) meant prioritizing exhaustive, source-verified manual-case coverage over
100%-verified per-case existing-test linkage in the time available. Where cited, every reference was verified
against the actual class/method (`qa/tools/existing_test_layers.yaml` was extended additively with
`CompOffOperationalQueryTests`, `CompOffLeaveIntegrationTests`, `CompOffCorrectionEndToEndTests`,
`CompOffConsumedCreditCorrectionTests`, `MySqlHrLeaveDashboardIntegrationTests`,
`HrLeaveDashboardAuthorizationTests`, `MySqlLeaveReportsIntegrationTests`, `MySqlLeaveReminderConcurrencyTests`
— none of Modules 1–6's existing entries were changed).

The full class inventory discovered (not all individually cited via `ex:`, but all read/classified during
research) spans: `LeaveRequestSubmissionFoundationTests`, `LeaveRequestSubmissionApiTests`,
`LeaveRequestSubmissionRetryPolicyTests`, `LeaveRequestApprovalFoundationTests`, `LeaveRequestApprovalApiTests`,
`LeaveRequestCancellationFoundationTests`, `LeaveRequestCancellationApiTests`,
`LeaveRequestWithdrawalFoundationTests`, `LeaveRequestWithdrawalApiTests`, `LeaveRequestReadServiceTests`,
`LeaveRequestReadApiTests`, `LeaveApprovalReadServiceTests`, `LeaveApprovalReadApiTests`,
`LeaveRequestPreviewApiTests`, `LeaveRequestRequestLimitTests`, `LeaveRequestSchemaFoundationTests`,
`LeaveRequestAllocatedSubmissionTests`, `LeaveRequestAllocatedLifecycleAccountingTests`,
`LeaveRequestAllocatedCancellationAccountingTests`, `LeaveBalanceFoundationTests`,
`LeaveBalanceAccountingFoundationTests`, `LeaveBalanceSummaryReaderTests`, `LeaveBalanceImportTests`,
`LeavePolicyFoundationTests`, `LeavePeriodResolverTests`, `RuntimeLeavePolicyEvaluatorFoundationTests`,
`LeaveConfigurationEndpointAuthorizationTests`, `HrLeaveDashboardAuthorizationTests`,
`AttendanceLeaveIntegrationTests`, `SeparationExitLeaveIntegrationTests`, ten `CompOff*Tests` classes, six
`SqlServer*Concurrency*Tests` classes (opt-in, `HRMS_SQLSERVER_TEST_CONNECTION`), and eight `MySql*Tests`
classes (opt-in, `HRMS_MYSQL_TEST_CONNECTION`/`_CATALOG_TEST_CONNECTION`). A green run of any MySQL/SQL-Server-
tagged class is **not** evidence of provider parity if its env var was absent — they skip silently, per
`CLAUDE.md`'s documented convention, confirmed directly for Leave in `Leave_Concurrency`'s `LEAVE-PROV-002/003`
cases.

Vitest coverage exists for essentially every Leave/CompOff page listed in `Leave_Frontend` (one `.test.tsx`
per `.tsx` page, per the frontend file inventory under `Frontend/HRMS.Web/src/pages/leave/` and
`pages/attendance/CompOff*.tsx`) — not individually cited via `ex:` in this pass for the same reason as above.
