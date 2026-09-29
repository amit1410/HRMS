# Module 9 — Separation / Exit Management (Initiation, State Machine, Notice Period, Last Working
Date, Approval Workflow, Employment/Account Integration, Manager/Leave/Attendance Impact, Clearance,
Benefits/Recoveries, Exit Interview, Documents, Exit/Offboarding Completion, Final Settlement Handoff,
Settlement State Machine, Authorization/Scope, Tenant Isolation/Audit/Reports, Concurrency/Provider
Parity, Frontend)

Status: manual test-case catalogue complete and validated (`python qa/tools/build_test_catalogue.py
--module 09-separation` regenerates the module's CSVs/coverage stats, and a full run across all nine
modules regenerates `qa/HRMS_Test_Cases.xlsx`, with no errors). No automation implemented yet
(Playwright is Phase 8+, on explicit approval). No product source was changed to produce this module.

## What is here

| File | Purpose |
|---|---|
| `functionalities.yaml` | Functionality registry (343 functionalities: F-SEP-001…343). Every test case references one or more of these ids; the builder fails the build if a functionality has zero cases or a case references an unknown id. |
| `cases/01-initiation-state-machine.yaml` … `cases/14-frontend.yaml` | The manual test cases, one YAML file per topic area, each mapped to its own Excel worksheet. Unlike Attendance's one-file-per-worksheet layout, several files here bundle 2–4 closely related functionality groups into one worksheet (e.g. `05-manager-leave-attendance-impact.yaml` covers Manager Impact + Leave Integration + Attendance Integration + Comp-Off/Overtime; `10-settlement-exit-closure.yaml` covers Settlement Handoff + Settlement State Machine + the remaining Exit-closure readiness blockers), matching the natural grouping the research passes surfaced rather than forcing a rigid one-group-per-file split. |
| `clarifications.yaml` | The CR-129…CR-164 QA-risk register for this module, plus re-verified (not merely restated) cross-references to CR-53 (Modules 5–8), CR-66 (Module 6), CR-77 (Modules 5/6/7/8), and CR-110 (Module 8) that this module's own code confirms, reframes, or — in CR-53's case — resolves. |
| `*.generated.csv`, `coverage-stats.json` | Build output — **do not hand-edit**. Regenerate with `python qa/tools/build_test_catalogue.py --module 09-separation`. |

Conventions (roles, tenant naming, `[VERB /path] => [status] expected` step format, `auto`/`ex`/`cr`
field meanings, provider-gated cases reporting `NOT EXECUTED - environment unavailable`) are identical
to [Module 1's README](../01-authentication/README.md) — read that once; it is not repeated here. As in
Module 8, no case in this module cites an `ex:` existing-test reference (see below for why).

## The real implementation (source-verified across 6 parallel deep-dive research passes, not assumed from the brief's example list)

Discovered by reading all 7 Separation-family controllers (`SeparationController`,
`SeparationClearanceController`, `SeparationBenefitsController`, `SeparationExitInterviewController`,
`SeparationDocumentController`, `SeparationExitController`, `SeparationSettlementController`); every
Application-layer service (`SeparationService`, `ClearanceService`, `SeparationBenefitsService`,
`ExitInterviewService`, `SeparationDocumentService`, `SeparationExitService`,
`SeparationSettlementOrchestrationService`, plus the Payroll-side `PayrollRetroSettlementService` for
the final-settlement handoff); every Domain entity/enum under `Separation*`/`SeparationBenefits`; both
migration chains; `Permissions.cs`/`SeedData.RolePermissionMap`'s full Separation/Clearance/
SeparationBenefits slice; every Separation/SeparationBenefits frontend page and API client; and the
~45 existing xUnit classes touching this surface (not individually classified — see below).
`docs/phase-8a` through `docs/phase-8h-separation-production-closure.md` were read where they exist and
current code wins wherever they diverge, per `CLAUDE.md`.

**CR-53 is CONFIRMED FIXED here, not merely re-exposed.** Modules 5–8 could only describe the read side
of this finding — every OTHER module's write paths never touch `User.IsActive`. This module confirms
directly that `SeparationExitService.ExecuteAsync` is the sanctioned mechanism those modules'
findings presupposed: four sequential, individually-idempotent, individually-committed checkpoints
(`ExecuteEmploymentAsync`, `ExecuteAccessAsync`, `ExecuteSessionsAsync`, `ExecuteRolesAsync`) that
genuinely deactivate the account, revoke every session, and end-date every role — all wired to real
`AuthService.SignInAsync`/refresh-token checks. The residual risk is entirely about whether an
organization's operational process always routes terminations through this workflow, and about the
narrow non-atomic crash window between checkpoints (see `CR-53` in `clarifications.yaml`).

**CR-66 reconfirms with the largest blast radius found so far.** `SuperHR` holds `PageAccess.Manage`
but zero native Separation-family permissions — via one `PUT /api/page-access/roles/{roleId}` call it
can self-grant `Separation.Manage`, every Clearance permission, and all six
`Payroll.SeparationBenefits.*` permissions, then drive the entire lifecycle end-to-end including the
irreversible account-deactivation step CR-53 describes.

**CR-77 recurs as a 6th-or-later independent mechanism that additionally disagrees with itself.**
`SeparationService.GetTeamAsync` (via `IEmployeeManagerResolver`) and `ClearanceService.GetInboxAsync`
(a raw SQL join against the legacy `Employee.ReportingManagerId` column) are two different,
occasionally-disagreeing manager-resolution mechanisms within the *same module*; `TaskActionAsync`
adds a third, ClearanceService-local check.

**CR-110 does NOT extend to Comp-Off/Overtime, but exposed a structurally identical sibling gap on
Leave balances (CR-158).** Separation's settlement/benefits layer never reads Comp-Off/Overtime at all
(confirmed zero grep matches), but `SeparationBenefitsService.CalculateLeaveAsync` reads
`EmployeeLeaveBalances` for encashment without ever reserving/consuming the quantity it reads — the
same class of hole, on a different resource, in a non-concurrent (notice-period) window rather than a
true race.

**Four `EmployeeSeparationStatus` values are declared but structurally unreachable** (`Submitted`,
`NoticePeriod`, `Exited`, `Cancelled` — CR-129), and the same pattern recurs across Clearance,
Settlement, and Payroll enums (nine more dead values — CR-141). **Three declared permissions
(`Separation.Approve`/`Review`/`NoticeManage`) are checked by zero endpoints** (CR-134), the same shape
as Module 7's CR-80. **A read-only-looking `GET` silently writes to the database** —
`SeparationSettlementOrchestrationService.GetStatusAsync` self-heals orchestration status and can
surface a 409/503 from what looks like a pure read (CR-153).

**Frontend confirms the backend's coarse-grained permission model and adds several dead-handler/
hardcoded-value bugs of its own.** Only `MySeparationPage` performs any client-side permission check
(CR-160); `SeparationDocumentsPage` hardcodes `expectedConcurrencyVersion=1` for Approve/Issue, a real
concurrency bug (CR-161); the Settlement and Exit-Closure dashboards both surface only a bare blocker
*count*, never the blocker detail, because the dedicated readiness client functions are dead exports
(CR-162); 13 of 17 routes have no navigation entry (CR-163); and `MySeparationPage`'s create-form
renders on *any* API error, not specifically "no active case" (CR-164).

## Coverage matrix — which behavior is exercised where

| Area | Fully exercised on | Spot-checked on | Notable findings |
|---|---|---|---|
| Initiation (self + HR-initiated) | Full validation chain, duplicate-active-case guard, concurrency fallback, reason master data | Backdated/same-day/future-dated LWD boundaries, rehire-then-resign | InitiatedBy is always Hr for employer-initiated cases (CR-136) |
| State Machine | Full declared-order enum, every real transition, terminal-set boundaries | Repeated-action-is-Conflict-not-idempotent contrast with LWD revision's deliberate no-op | 4 of 12 declared statuses are unreachable (CR-129) |
| Notice Period | Snapshot-at-approval-only, served/shortfall/waiver formulas, zero/shortened/extended notice | Multi-partial-waiver capping, disposition classification | Money computed only downstream from a DIFFERENT table (CR-154/CR-264) |
| Last Working Date | Proposed vs Approved LWD, pre- vs post-approval revision asymmetry | Backdated pre-approval revision, LWD-after-notice-end extension | Two "revise LWD" paths enforce materially different strictness (CR-133) |
| Approval Workflow | Manager/HR authority resolution, maker-checker both stages, concurrency | Manager-scope date asymmetry (as-of-today vs as-of-RequestDate) | Terminal Rejected/Withdrawn, no rework path (CR-131 territory) |
| Employment/Account Integration | Full 4-checkpoint exit execution, idempotency, partial-failure/retry | Crash-window between checkpoints, self-execution block | CR-53 confirmed FIXED here (see above) |
| Manager/Leave/Attendance Impact | Reassignment blocker (legacy column only), Leave/Attendance write-absence proofs | EmployeeSupervisor never referenced at all | Terminated manager's reports can dangle indefinitely (CR-159) |
| Clearance | Template snapshot-at-start, mandatory-task completion gate, owner-type enforcement gaps | Multi-asset-row hiding, reopen-preserves-history | No enforcement for Role/Department/Hr/Accounts/It/Admin owners (CR-138) |
| Benefits/Recoveries | Policy/version lifecycle, formula types, override maker-checker, wage resolution | Multi-active-policy ambiguity, dead config fields | ContractEnd/Other can NEVER be gratuity-eligible (CR-144) |
| Exit Interview | Full status lifecycle, question-type validation, revision history | Silent-reset-of-HR-review-by-employee-resubmit | HR notes/rehire-recommendation structurally excluded from employee DTO |
| Documents | Template/merge-field/unsafe-content validation, generation idempotency, maker-checker approve | Historical-employment-snapshot proof, self vs HR download gates | Event history omits download/view/failure events (CR-149) |
| Exit/Offboarding Completion | 10-blocker readiness (4 new blockers verified here), retry idempotency | ClosedAtUtc derivation (no dedicated column) | 6 blockers shared with Group F, 4 unique to this group |
| Final Settlement Handoff | Initiate/retry idempotency, additive recalculation, zero/negative-settlement guards | IdempotencyKey never actually compared (CR-155), reason-mapping fallback | Attendance/OT/CompOff never read; taxes never integrated |
| Settlement State Machine | Both status enums' dead-value inventory, concurrency races | Orphan `FinalSettlementCancel` permission (CR-152) | GET-with-write-side-effect (CR-153) |
| Authorization/Scope | Full permission inventory, seeded role-scope matrix (8 roles), relationship-vs-permission proof | ExitInterview/Clearance name-reuse quirks | SeparationService is an independent, 6th-or-later scope mechanism |
| CR-66 Escalation | SuperHR/HRAdmin self-grant scenarios end-to-end, audit-trail proof | Escalation reaching irreversible exit-execution capability | Largest blast radius of any module's CR-66 finding so far |
| Tenant Isolation | GET/mutation/cross-service NotFound sweep, number-sequence isolation | Foreign-tenant-rows-untouched proof | HR-initiated cross-tenant denial at existence check, not authz |
| Audit/History | Append-only proof for all 5 event-table families | LWD/notice-waiver old->new formatting | Settlement history omits 3 declared-but-never-raised event types |
| Reports/Queues | Every dashboard/inbox/register's filter+paging contract | Large-data authorization-before-paging proof | No export capability exists anywhere in this module (CR-308) |
| Provider Parity | Concurrency-token/lock-semantics contract across 7 entities | Document-number and gratuity-idempotency races on real providers | Only 3 of 9 provider-acceptance classes run a full business flow |
| Large Data | 1000+-row scale on every dashboard, 100 REAL exit executions | 2-minute/30-second performance ceilings | Exact-count proof of zero leakage into 900 untouched separations |
| Frontend | Loading/empty/error and dead-handler audit across all 18 pages | Hardcoded concurrency version, raw-JSON rendering, orphan routes | Only 1 of 18 pages performs any client-side permission check |

## Sheets produced for this module

| Sheet | Cases (approx.) | Focus |
|---|---:|---|
| Separation_Initiation | 35 | Self/HR-initiated creation, validation chain, EmployeeSeparationStatus state machine |
| Separation_Notice | 27 | Notice period computation/waiver, Last Working Date mechanics |
| Separation_Approval | 19 | Manager/HR approval authority, maker-checker, concurrency, inboxes |
| Separation_Employment | 22 | Exit readiness, 4-checkpoint exit execution, CR-53 login/account/role behavior |
| Separation_Impact | 22 | Manager reassignment, Leave/Attendance/Overtime/Comp-Off integration |
| Separation_Clearance | 35 | Template configuration, task lifecycle, asset returns, completion gate, concurrency |
| Separation_Benefits | 26 | Gratuity policy configuration, calculation/override/finalization, recoveries |
| Separation_ExitInterview | 21 | Template configuration, assignment, employee questionnaire, HR review |
| Separation_Documents | 24 | Template configuration, generation, maker-checker approve/issue, supersede/cancel, download |
| Separation_Settlement | 25 | Final Settlement Handoff, Settlement state machine, remaining Exit-closure blockers |
| Separation_Scope | 22 | Permission inventory, seeded role-scope matrix, CR-66 escalation, tenant isolation |
| Separation_Audit_Reports | 15 | Append-only audit trails, dashboards/inboxes/registers |
| Separation_Provider_Scale | 11 | Provider-parity contract, concurrency races, large-data scale |
| Separation_Frontend | 21 | Page-level loading/empty/error, dead handlers, navigation discoverability |

**Total: 331 test cases, 343 functionalities** (`python qa/tools/build_test_catalogue.py --module
09-separation` regenerates and reports the authoritative counts; treat `coverage-stats.json` as
authoritative over this table if they ever diverge).

## Notable, source-verified behavior worth flagging before automating (see `clarifications.yaml`)

- **CR-129**: Four `EmployeeSeparationStatus` values are declared but never assigned by any code path.
- **CR-130**: `Closed` is excluded from `Terminal`, potentially blocking a rehired employee's new case.
- **CR-131**: No employee self-service withdrawal path exists once HR Review has begun.
- **CR-132**: Manager-scope date asymmetry between team-listing (today) and approval authority (RequestDate).
- **CR-133**: Pre- and post-approval "revise LWD" paths enforce materially different validation strictness.
- **CR-134**: Three declared permissions (`Approve`/`Review`/`NoticeManage`) are checked by zero endpoints.
- **CR-135**: HRManager cannot list-all/view-by-id, operate documents, or run exit-closure despite reviewing most cases.
- **CR-136**: Employer-initiated cases always skip Manager Review; `InitiatedBy` is always `Hr`, never `Manager`/`System`.
- **CR-137**: Several Conflict messages use a bare PascalCase code inconsistent with the rest of the module.
- **CR-138**: No ownership enforcement exists on Role/Department/Hr/Accounts/It/Admin clearance-task owner types.
- **CR-139**: Seeded IT/Accounts roles hold zero Clearance permissions despite owning IT/Accounts/Finance tasks conceptually.
- **CR-140**: `ClearanceViewSelf` is defined but gates zero controller actions.
- **CR-141**: Nine declared enum values across Clearance/Settlement/Payroll are structurally unreachable.
- **CR-142**: Asset return `ActualReturnDate`/`IssuedDate` are never populated.
- **CR-143**: Two gratuity policy-version config fields are settable but never read by any calculation.
- **CR-144**: `ContractEnd`/`Other` separation reasons can NEVER be gratuity-eligible.
- **CR-145**: Multiple simultaneously-active gratuity policies silently resolve alphabetically with no ambiguity error.
- **CR-146**: No seeded role except SuperAdmin/TenantAdmin holds any `Payroll.SeparationBenefits.*` permission.
- **CR-147**: No dedicated ExitInterview/Document/Settlement/ExitClosure permission family exists at all.
- **CR-148**: An employee re-submitting mid-HR-review silently resets HR's in-progress state.
- **CR-149**: Document download/view/generation-failure event types are declared but never raised.
- **CR-150**: No alumni/magic-link document access path exists once exit execution deactivates the account.
- **CR-151**: The hand-rolled PDF renderer has no wrapping/pagination; PDF bytes are stored inline, never purged.
- **CR-152**: `Payroll.FinalSettlementCancel` is an orphan permission with no method/endpoint anywhere.
- **CR-153**: `GET .../settlement` can silently write to the database from what looks like a pure read.
- **CR-154**: Two independent, non-reconciled notice-fact tracks feed Separation's readiness vs Payroll's money calculation.
- **CR-155**: `IdempotencyKey` is stored but never actually compared on a settlement-initiate replay.
- **CR-156**: No automated/monetary path exists from Clearance asset recovery into Final Settlement.
- **CR-157**: Manual settlement-line amounts have no sign validation; negative totals pass Approve, only blocked at Finalize.
- **CR-158**: Leave-balance-based encashment is never reserved/consumed against the balance it reads (CR-110 sibling).
- **CR-159**: No code path reassigns direct reports/`EmployeeSupervisor` references when a manager is separated.
- **CR-160**: Only 1 of 18 Separation-family pages performs any client-side permission check.
- **CR-161**: `SeparationDocumentsPage` hardcodes `expectedConcurrencyVersion=1` for Approve/Issue.
- **CR-162**: Settlement/Exit-Closure dashboards show only a blocker count, never blocker detail (dead readiness exports).
- **CR-163**: 13 of 17 Separation routes have no navigation entry.
- **CR-164**: `MySeparationPage`'s create-form renders on any API error, not specifically "no active case."

`clarifications.yaml` also carries re-verified (not merely restated) cross-references to **CR-53**
(confirmed FIXED here — see above), **CR-66** (largest blast radius found so far), **CR-77** (a
6th-or-later mechanism disagreeing with itself), and **CR-110** (does not recur for Comp-Off/Overtime,
but exposed the CR-158 sibling on Leave balances).

None of these are asserted as confirmed defects — each is exposed by specific test cases and awaits a
product decision, per the classification column in `clarifications.yaml`.

## Existing automated coverage referenced by this module

Separation has a substantial existing xUnit inventory (~45 classes touching this surface, spanning
foundation/state-machine, notice/LWD, approval, employment/exit-execution, clearance, benefits, exit
interview, documents, settlement, large-data, and provider-acceptance variants) and Vitest coverage of
varying density across its 18 frontend pages (`SeparationExitInterviewLargeDataTests.cs`,
`SeparationClearanceLargeDataTests.cs`, `SeparationDocumentLargeDataTests.cs`,
`SeparationSettlementLargeDataTests.cs`, and `SeparationExitLargeDataTests.cs` are the five large-data
suites cited narratively in the Provider/Scale coverage matrix above; the MySQL/SQL Server
`*ProviderAcceptance.cs` family is cited in `Separation_Provider_Scale`).

Given this scale — consistent with Module 7 and Module 8's own precedent, both of which found
100%-per-case verification against the existing suite impractical at this size — this module follows
the identical precedent: **no case in this module cites an `ex:` existing-test reference.** Every
existing test class discovered during research is instead documented narratively, per topic area,
inside the relevant `src:` fields, the coverage matrix above, and `clarifications.yaml`'s carry-forward
entries; `qa/tools/existing_test_layers.yaml` was **not** extended for this module, consistent with
`qa/tools/build_test_catalogue.py`'s validation (which only requires `ex:` references to resolve when
they are actually used).

Two pages are flagged explicitly as having **zero** Vitest coverage of any kind despite being the most
consequential UI surfaces in the module: `SeparationInboxPage.tsx` (the core manager/HR approval
workflow) and `SeparationDocumentsPage.tsx` (the document lifecycle, including the CR-161 hardcoded-
concurrency-version bug) — see `Separation_Frontend`'s `SEP-FE-005`/`SEP-FE-012` and CR-163's
navigation-discoverability finding for the same two pages.

A green run of any MySQL/SQL-Server-tagged provider-acceptance class is **not** evidence of provider
parity if its env var was absent — they skip silently, per `CLAUDE.md`'s documented convention, and
`Separation_Provider_Scale`'s own cases report `NOT EXECUTED - environment unavailable` explicitly
rather than PASS when this is the case.
