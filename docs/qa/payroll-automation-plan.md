# QAAUTO Payroll Happy Path: Automation Plan (ANEVRA01, DEV/TEST only)

> **Status: implemented (2026-09-29) through Bank Advice `Exported` and GL generation.** See
> `docs/qa/payroll-sandbox-setup.md` for the tool, the live results, cleanup and remaining blockers.
> Nothing was committed. §4.3's "if one ever becomes necessary" case did happen for exactly one row:
> the Bank master (no public API creates banks) now comes from a Development-only, opt-in seeder,
> `DatabaseSeeder.SeedQaAutomationBankAsync` (sandbox doc §4.1). Nothing else changes application
> code, and `PayrollCalculationEngine` validation is used exactly as it is.
> Date: 2026-09-29. Target: tenant `ANEVRA01` (host `anevra01.localhost`) on the local
> Development stack (MySQL). Builds on `qa/automation/` (pytest framework, `core/payroll_api.py`,
> `core/employee_api.py`, and the Phase 6 QA identities).

## 0. Summary

**Current state.** The existing Payroll smoke suite (`qa/automation/tests/api/test_payroll_smoke.py`)
cannot get a run to `Calculated`. Every eligible employee fails with `OvertimeNotFinalized`
(blocker #8 in `qa/automation/README.md`).

**Root cause (source-verified).** `OvertimeService.ResolveAsync` requires an `AttendancePeriod`
whose `StartDate`/`EndDate` exactly equal the payroll period's dates, with `Status == Closed`. It has
no fallback when that period is missing (`OvertimeService.cs:119`). The attendance resolver, by
contrast, tolerates a missing period (`AttendancePayrollSnapshotResolver.cs:14`). The engine calls
both resolvers for every eligible employee (`PayrollCalculationEngine.cs:60-71`).

**Why closing an attendance month is not enough on its own.** Attendance monthly processing and
close are tenant-wide:
- They sweep in every employee with `DateOfJoining <= period.EndDate` (`AttendanceMonthlyProcessor.cs:60, 248, 295`).
- Any swept employee with an open employment-history row and no processed day rows produces blocking
  `NotProcessed` exceptions (`:311-312`), and those exceptions block close (`:305`).
- For the smoke suite's future dates (2071+), even the QAAUTO employee's own days can't resolve. A
  shift day with no punches stays `NotProcessed` while "now" is before the shift end
  (`AttendanceDayProcessor.cs:59-60`).

**Proposal.** Use a reserved deep-past sandbox window (`1950-01` … `1959-12`) that predates every real
employee's date of joining (DOJ). Tenant-wide sweeps over that window then contain only the QAAUTO
employee:
- One QAAUTO employee, backdated into the window, with **no employment history**.
- A salary assignment and an accounting configuration that are **bounded to the window**, so neither
  can leak into a real payroll period.
- Every step goes through existing public APIs as the Phase 6 QA Admin (`TenantAdmin`).

**Reachable end state:** `Calculated → Approved → Finalized`, plus Bank Advice and GL journal. Approval
depends on the maker-checker preflight check (§4, G8).

**Recorded blockers (§8):**
- The realistic-attendance tier (Tier 2) is blocked after two attempts.
- Approval with a single identity is conditional on the tenant's control settings.
- Some additive rows that reference real employees can't be avoided through the API. They need
  sign-off.

---

## 1. Minimum prerequisites for a successful calculation

Each row lists what the engine or its upstream services enforce, and what fails if it's missing.

| # | Prerequisite | Enforced at | Failure if missing |
|---|---|---|---|
| P1 | Authenticated tenant and caller with `Payroll.*` permissions | engine `:16`; `HasPermission` policies | 401/403 |
| P2 | **Employee**: `Status == Active`, `DateOfJoining <= period.EndDate`, `DateOfLeaving` null or `>= period.StartDate` | `PayrollRunService.cs:59` | Excluded from run ("not employed"/"not active") |
| P3 | **Salary Component** (active) | structure/assignment services | Structure create fails |
| P4 | **Salary Structure** (active), with a version effective on the assignment date that has ≥1 active component | `EmployeeSalaryAssignmentService.cs:58-60`; engine `:81-82` | `NoSalaryStructureVersion` |
| P5 | **Employee Salary Assignment**: `Active`, `EffectiveFrom <= period.EndDate`, `EffectiveTo` null or `>= period.EndDate`, and exactly one overlapping the period | `PayrollRunService.cs:52`; engine `:79-80`; overlap guard `ESAService.cs:61` | Excluded ("No effective salary assignment") / `CalculationFailed` (multiple) |
| P6 | **Payroll Period**: `Draft`/`Open` and `IsActive` when the run is created | `PayrollRunService.cs:37` | 409 on run create |
| P7 | **Payroll Run**: `Regular`, `Prepared` (population snapshot built) | engine `:19-21`; `PrepareAsync` `:44-63` | 409 "Only prepared payroll runs may be calculated" |
| P8 | **Attendance Period** with `StartDate`/`EndDate` **exactly** equal to the payroll period's, `Status == Closed` | `OvertimeService.cs:119` (unconditional); `AttendancePayrollSnapshotResolver.cs:15` | `OvertimeNotFinalized` / `AttendanceNotFinalized` |
| P9 | **Current `PayrollAttendanceSnapshot`** for the employee in that attendance period | resolver `:16-17`; created only by `CloseAsync` from monthly summaries (`AttendanceMonthlyProcessor.cs:127-136`) | `AttendanceSnapshotMissing` |
| P10 | Overtime snapshot: **optional** (none returns null and adds no OT line) | `OvertimeService.cs:120-121` | n/a |
| P11 | Statutory: **optional** (no `EmployeeStatutoryProfile` gives zero statutory) | `StatutoryPayrollService.cs:24` | If a profile exists, PF/ESI need a basis config (`:46-47`) |
| P12 | Loans, reimbursements, adjustments: **optional** (none means no-op) | engine `:121-150, :167-180` | n/a |
| P13 | Net pay ≥ 0 | engine `:102, :119` | `NegativeNetPay` |

Attendance period dates are always a calendar month (`AttendanceMonthlyProcessor.cs:19`), so the
payroll period must be a calendar month too.

**Attendance values do not change amounts.** Component proration uses only assignment and
employment dates (`PayrollCalculationEngine.cs:103-108, 232-233`). The attendance snapshot's
eligible, payable and LOP days are stored on the result for traceability. A structure with one
**non-proratable FixedAmount** component therefore gives a deterministic net pay.

### Beyond `Calculated` (for the full happy path)

| # | Step | Requirement | Enforced at |
|---|---|---|---|
| D1 | Approve / Finalize | `Calculated → Approved → Finalized` only. If a `PayrollControlConfiguration` exists with `RequireMakerChecker && PreventSelfApproval`, the approver must not be the run's `StartedByUserId` | `PayrollRunService.cs:70-75, 101`; `PayrollApprovalGuard.cs:12-15` |
| D2 | Bank Advice | Run `Approved`/`Finalized`; current `Calculated` results in one currency; exactly one active `Salary`-purpose `EmployeeBankDetails` per employee (needs a Bank master row) | `BankAdviceService.cs:20-37` |
| D3 | GL journal | Run `Approved`/`Finalized`; an `Active` accounting configuration version effective at the period end date; mappings; active GL accounts | `PayrollAccountingService.cs:38-45` |
| D4 | Order | Close attendance **before** finalizing payroll. A finalized run blocks attendance close/reopen for the same dates | `AttendanceMonthlyProcessor.cs:123` |

---

## 2. Can each prerequisite be created through an existing public API?

The QA Admin holds `TenantAdmin`, which is granted every permission except `AccountEmployeeLink.*`
(`SeedData.cs:456-458`). All permissions below are therefore available to it.

| Prerequisite | Public API | Verdict |
|---|---|---|
| Employee (P2) | Employee personal-details create (`core/employee_api.create_personal_details`) | **Yes.** DOJ has no lower bound other than DOB+18 when a DOB is given (`EmployeePersonalDetailsRequestValidator.cs:36-54`), so omit DOB. This path creates **no** employment history (history rows are only written by `EmployeeEmploymentService.cs:491-547`). |
| Salary Component / Structure / Assignment (P3-P5) | `ISalaryComponentService`, `ISalaryStructureService`, `IEmployeeSalaryAssignmentService` (wrappers already exist) | **Yes.** The assignment request supports `EffectiveTo` (`ESAService.cs:61`). |
| Payroll Period / Run / Prepare / Calculate / Transition (P6-P7, D1) | `/api/payroll/periods`, `/api/payroll/runs` (wrappers exist) | **Yes** |
| Run readiness | `GET /api/payroll/runs/{id}/readiness` (`get_run_readiness` exists) | **Yes.** Advisory only; its individual checks were not reviewed. |
| Attendance Period create / process / close-preview / close (P8-P9) | `/api/attendance/periods`, `.../{id}/process`, `.../{id}/close-preview`, `.../{id}/close`, `.../{id}/summaries`, `.../{id}/exceptions` (`AttendanceMonthlyController`) | **Yes.** New wrappers are needed in `core/attendance_api.py`. |
| Payroll controls (read only) | `GET /api/payroll/controls` | **Yes.** Read only; never updated by this plan. |
| Bank detail (D2) | Employee bank-detail sub-resource (used by `test_bank_detail_crud_lifecycle`) | **Yes, if an active Bank master exists.** Otherwise a QAAUTO Bank master via the masters API (verify, §9). |
| GL accounts / configuration / version / mappings (D3) | `/api/payroll/accounting` (`IPayrollAccountingService`, wrappers exist) | **Yes** |
| Statutory profile (P11) | Not provisioned | Skipped by design (optional). |

### Not available through the API, and the alternative used

1. **Scoping attendance processing or close to a subset of employees.** There is no such parameter;
   the sweep is tenant-wide (`AttendanceMonthlyProcessor.cs:60, 248`). *Alternative:* the reserved
   deep-past sandbox window (§3.2), which makes the QAAUTO employee the only one in the sweep.
2. **Materializing attendance day rows generically.** `IAttendanceDayProcessor` and
   `IAttendancePunchIngestionService` are not exposed by any controller. Days are processed only as a
   side effect of admin corrections, regularization or On-Duty approval, leave approval, or device
   ingestion. *Alternative (Tier 1):* none is needed. With no employment history, the monthly
   processor checks zero days for the QAAUTO employee (`:76`, `:279`), so there are zero exceptions.
   Tier 2 is blocked (§8, B2).
3. **Bounding an employment-history row.** `EmployeeEmploymentRequest` has no `EffectiveTo`
   (`EmploymentRequest.cs:8-23`). *Alternative:* keep no history for the QAAUTO employee.
4. **Deleting or resetting payroll or attendance artifacts.** There are no delete endpoints for
   periods, runs, structures, components, assignments or attendance periods. *Alternative:*
   deactivate through the API (§5, L1), or a manual database reset (§5, L2).
5. **Seeding by direct database writes.** Deliberately not proposed. Every row goes through service
   validation.

---

## 3. Idempotent, QAAUTO-only seed design

### 3.1 Two layers

- **Masters** (find-or-create, reused every run): the employee, salary component, structure,
  assignment, bank detail, and GL accounts/configuration/version/mappings.
- **Cycles** (append-only, resumable): one sandbox month per execution. Each cycle has its own
  payroll period, attendance period and payroll run. Payroll runs are one-way after `Approved`, so a
  fresh calculation always uses a **new** month. Re-running the same cycle key picks up from the
  first incomplete step and never creates duplicates.

### 3.2 Reserved sandbox window and constants

| Constant | Value | Why |
|---|---|---|
| `SANDBOX_START` | `1950-01-01` | Predates any plausible real DOJ. Guarded live by G7. |
| `SANDBOX_END` | `1959-12-31` | 120 cycle months. Bounds the assignment and GL version. |
| Employee DOJ | `1950-01-01`, no DOB, no DOL, `Active` | Eligible for every sandbox month (P2). |
| Assignment | `EffectiveFrom 1950-01-01`, `EffectiveTo 1959-12-31` | **Never effective in a real period**, so the employee is never eligible in a real run. |
| Component / structure `EffectiveFrom` | `1950-01-01` | Needed so a version resolves on the assignment date (`ESAService.cs:60`). |
| Structure line | FixedAmount `30000`, `IsProratable=false` | Deterministic net pay (matches the smoke suite). |
| GL configuration version | `1950-01-01 … 1959-12-31`, `Active` | `GenerateAsync` picks the active version effective at the period end date (`PayrollAccountingService.cs:40`). An open-ended QAAUTO version could become the configuration for **real** runs. |
| Currency / frequency | `INR` / `Monthly` | Matches the smoke suite. |

The smoke suite's 2071–2470 periods do not overlap this window.

### 3.3 Natural keys (all QAAUTO-prefixed and deterministic, never random)

| Entity | Key |
|---|---|
| Employee | FirstName `QaAutoPayroll`, LastName `QAAUTO-HP-01`, DOJ `1950-01-01`. Use employee code `QAAUTO-HP-EMP01` if the tenant's code configuration allows manual codes (verify, §9). |
| Salary Component | Code `QAAUTO-HP-BASIC` |
| Salary Structure | Code `QAAUTO-HP-STRUCT` |
| Assignment | (employee, `QAAUTO-HP-STRUCT`, `1950-01-01`); remarks `QAAUTO-HP` |
| Bank detail | Account holder `QAAUTO HP`, a clearly fake account number; purpose `Salary` |
| GL accounts | `QAAUTO-HP-GL-EXP`, `QAAUTO-HP-GL-PAY` |
| Accounting configuration | Code `QAAUTO-HP-ACCT` |
| Payroll Period (per cycle) | Code `QAAUTO-HP-YYYYMM`, name `QAAUTO HP YYYY-MM` |
| Attendance Period (per cycle) | `(Year, Month)` inside the sandbox window. It has no name field, so it is identified by the reserved window plus the manifest. |
| Payroll Run (per cycle) | Found by `PayrollPeriodId`; notes `QAAUTO-HP cycle YYYY-MM` (the run number is server-generated) |

### 3.4 Find-or-create rules

1. Look up by natural key through list/search endpoints.
   - **0 matches:** create.
   - **1 match:** reuse only if the key is QAAUTO-HP-prefixed **and** its shape matches the expected
     values (type, amount, dates, status).
   - **Anything else** (more than one match, a drifted shape, or a match without the prefix):
     **STOP and report.** Never update to "fix" it.
2. **Non-QAAUTO rows are never updated, transitioned, deactivated or deleted.** Writes only target
   IDs that the seed created itself and recorded in the manifest.
3. The only permitted mutations of QAAUTO rows are the flow's own state transitions (open, process,
   close, prepare, calculate, approve, finalize, generate), plus retirement (§5, L1).
4. The QAAUTO employee must have **zero** employment-history rows. If any appear, STOP; see §8, B2
   for why.

### 3.5 Per-cycle sequence (resumable; each step checks current state first)

| Step | Action | Expected / assertion |
|---|---|---|
| C1 | Allocate the next sandbox month not in the manifest. Confirm `GET /api/attendance/periods?year=&month=` is empty and no `QAAUTO-HP-YYYYMM` payroll period exists. | Free month |
| C2 | `POST /api/payroll/periods` (calendar month, FiscalYear = year, PeriodNumber = month, PayDate = 1st of the next month, `isActive`) | `Draft` |
| C3 | `POST /api/attendance/periods {year, month}` | Start/End equal the payroll period's dates |
| C4 | `POST .../periods/{id}/process` | `EmployeesProcessed == 1`. **More than 1 means the sandbox is contaminated: STOP.** |
| C5 | `GET .../periods/{id}/close-preview` | `CanClose == true`, `BlockingExceptionCount == 0` |
| C6 | `POST .../periods/{id}/close` (comment `QAAUTO-HP sandbox close`) | `Closed`; snapshot created for the QAAUTO employee |
| C7 | `POST /api/payroll/runs {payrollPeriodId, runType: Regular, notes}` | `Draft` |
| C8 | `POST /api/payroll/runs/{id}/prepare` | Eligible set is **exactly** {QAAUTO employee}; every other employee is `Excluded` |
| C9 | `GET .../readiness` | Record the result (advisory) |
| C10 | `POST .../calculate` | `calculatedCount 1`, `failedCount 0`, status `Calculated`, net `30000.00`, result `attendanceSnapshotId` not null, errors empty |
| C11 | Transition `Approved` | Only if G8 passes; otherwise stop here and record B3 |
| C12 | `POST /api/payroll/bank-advice` generate (then validate/prepare/approve/export) | 1 payment, QAAUTO employee, amount = net |
| C13 | `POST /api/payroll/accounting` generate (then validate/approve/post/export) | Lines reference only the QAAUTO result; balanced |
| C14 | Transition `Finalized` | `Finalized`. The sandbox month is now immutable by design (D4). |

**Resume logic.**
- If the attendance period is already `Closed`, skip C3–C6.
- If the run is already at or past `Calculated`, skip C7–C10.
- A run stuck at `Prepared` with errors may be recalculated. Recalculation is allowed before approval
  (engine `:20-21`).
- Any unexpected failure means stop, keep the manifest, and report. Never compensate by editing
  other data.

### 3.6 Manifest

Keep a git-ignored JSON file at `qa/automation/.state/payroll-sandbox-manifest.json`. It records:
- the pinned tenant id (`tid`), and
- for every entity: natural key, created id, sandbox month, last observed state, and timestamps.

The manifest drives resumption, per-write ownership checks (§4.2) and cleanup (§5). If it is lost, it
can be rebuilt read-only from the `QAAUTO-HP-*` codes plus attendance periods in 1950–1959.

### 3.7 Where it lives

Put it in `qa/automation/` only: a sandbox module with `preflight`, `seed-masters`, `run-cycle`,
`verify` and `retire` entry points, plus a pytest session fixture that happy-path tests depend on. No
changes to `Backend/` or `Frontend/`.

---

## 4. DEV/TEST guard

### 4.1 Preflight (runs before the first write; all checks must pass; fails closed)

The seed exits non-zero with **no writes** if any check fails.

| # | Check | How |
|---|---|---|
| G1 | Explicit opt-in | `QA_PAYROLL_SANDBOX=1` **and** `QA_PAYROLL_SANDBOX_TENANT=ANEVRA01` (typed confirmation) |
| G2 | Local API only | Base URL host ∈ {`localhost`, `127.0.0.1`}, port = `QA_API_PORT` (5080). Refuse anything else. |
| G3 | Tenant host | `QA_TENANT_A_HOST == "anevra01.localhost"` exactly |
| G4 | Development server | `GET /swagger/v1/swagger.json` returns 200. Swagger is only mapped when `IsDevelopment()` (`Program.cs:143-146`). Any other status: abort. |
| G5 | Tenant identity pinned | Decode the login JWT. Its `tid` claim must equal `QA_PAYROLL_SANDBOX_TENANT_ID`, pinned in the git-ignored `.env` after a one-time manual confirmation. Cross-check `GET /api/tenants/current/branding`. |
| G6 | Actor | Signed-in email is `qaauto-admin@anevra01.qa-automation.invalid` (the Phase 6 QA Admin, `.invalid` TLD) with role `TenantAdmin` |
| G7 | Sandbox purity (read-only) | (a) Earliest DOJ among non-QAAUTO employees is after `SANDBOX_END`. (b) No attendance period in 1950–1959 that is missing from the manifest. (c) No payroll period overlapping 1950–1959 without a `QAAUTO-HP-` code. (d) The QAAUTO-HP employee has zero employment-history rows. |
| G8 | Maker-checker | `GET /api/payroll/controls`. If `RequireMakerChecker && PreventSelfApproval`, the cycle stops at `Calculated` (B3). The configuration is **never** changed. |
| G9 | Hazard audit (read-only, report only) | Active QAAUTO assignments with `EffectiveTo == null` left over from the smoke suite, open-ended QAAUTO accounting versions, and QAAUTO employees that do have employment history. See B7. |

### 4.2 Per-write guard

Before every mutating call:
- The `Host` header must still equal the pinned host.
- The target must be either a new create whose natural key starts with `QAAUTO-HP`, or an ID present
  in the manifest.
- A `PUT` or transition on any other ID is refused client-side.

### 4.3 Server-side guard (not proposed)

Every prerequisite is reachable through the API, so no server-side seeder is needed. If one ever
becomes necessary, it should copy the existing `SeedQaAutomationUsersAsync` gating in
`DatabaseSeeder.cs`: Development only, tenant code `ANEVRA01`, and a `DevelopmentSeed:*` config
opt-in. That would be a separate, reviewed change.

---

## 5. Cleanup / reset (QAAUTO rows only, always tenant-filtered)

**L0: per cycle (default, API).** Nothing destructive. The finalized sandbox month is kept as test
evidence. If a test needs to regenerate bank advice, the existing batch can be cancelled
(`CancelAsync`).

**L1: retire (API, reversible-ish).** Use this when the sandbox programme ends or needs to stop:
1. Deactivate the assignment, the structure and the component.
2. Deactivate the GL mappings (`DeactivateMappingAsync`) and set the GL accounts inactive.
3. Deactivate the bank detail.
4. Close and lock the QAAUTO-HP payroll periods.
5. **Retire the employee:** set a non-active status and `DateOfLeaving = SANDBOX_END`. The validator
   requires both together (`EmployeeRequestValidator.cs:92-98`). This removes the employee from every
   real attendance sweep and real payroll population, because its DOL is before any real period.
   After retirement, no new cycles can run.

**L2: hard reset (manual DB operation, human-run only).** Use only when L1 isn't enough.

Rules:
- Run against the **ANEVRA01 shard only**. Never `hrms_catalog`.
- Take a backup first.
- Run in one transaction, with pre-counts compared to the manifest. Abort if any count exceeds the
  expected value.

Every statement is filtered by `TenantId = @anevraTenantId` **and** by manifest IDs, or by a join to
a parent whose code starts with `QAAUTO-HP-`. Never delete by `LIKE 'QAAUTO%'` alone.

Order, children first. Derive the exact table list and foreign-key order from the `HrmsDbContext`
model at implementation time:
1. GL journal lines and batches of QAAUTO runs.
2. Bank-advice payments, history and batches of QAAUTO runs.
3. Result components, statutory results, results, calculation errors and history of QAAUTO runs.
4. **`PayrollRunEmployees` of QAAUTO runs, deleted by `PayrollRunId`.** This also removes the
   Excluded rows that reference real employees. Never delete by a real `EmployeeId`.
5. Run history and runs, then period history and periods (codes `QAAUTO-HP-*`).
6. For sandbox attendance periods (manifest IDs, years 1950–1959): overtime snapshots, attendance
   snapshots, monthly summaries, period events, then the periods.

Do **not** hard-delete the QAAUTO employee, assignment or structure. Real attendance periods and real
payroll runs will hold summary, snapshot and `PayrollRunEmployees` rows that reference the employee,
and the foreign key is `Restrict` (`PayrollPeriodRunConfiguration.cs:35`). Removing them would mean
editing real periods, so L1 retirement is the end state for those rows.

---

## 6. Tenant isolation checks

| # | Check | Type |
|---|---|---|
| I1 | The server derives the tenant only from host or JWT `tid`. Every service query filters `TenantId` (e.g. engine `:17, :73-74`, both resolvers), and EF global filters and a SaveChanges stamp apply (CLAUDE.md). | Design guarantee (cited, not re-tested) |
| I2 | G5 pinned `tid` before any write; G3 host on every call | Preflight / per call |
| I3 | In-cycle, exact-set assertions: C4 processed == 1; C8 eligible == {QAAUTO}; C10 results == 1 (QAAUTO); C12 payments == 1; C13 lines reference only the QAAUTO result | Positive scoping |
| I4 | Real data unchanged. Take a read-only fingerprint (count plus hash of `id,status,concurrencyVersion`) of non-QAAUTO payroll periods, non-QAAUTO payroll runs, attendance periods outside 1950–1959, and non-QAAUTO salary assignments. Compare before and after each cycle; they must be identical. | Before/after diff |
| I5 | Real-run leak check: effective assignment for the QAAUTO employee **today** returns 404; the QAAUTO accounting version is not effective today | Negative |
| I6 | Cross-tenant (optional, needs `QA_TENANT_B_HOST`, skipped otherwise as the smoke suite already does): the ANEVRA01 token on the Tenant B host returns 401; a Tenant B admin gets 404 for the QAAUTO run and employee IDs; Tenant B lists contain no `QAAUTO-HP` codes | Negative |
| I7 | Provider: ANEVRA01 runs on MySQL, so this path exercises real provider behaviour (locking, concurrency tokens) that the SQLite unit suite does not prove | Note |

---

## 7. What this plan deliberately does not do

- It does not make the engine's overtime resolution conditional. The smoke suite's README suggests
  this, but it would change engine behaviour. If product decides to do it, it is a separate engine
  change with its own tests.
- It does not grant, create or elevate any role or permission, and does not change payroll control
  settings.
- It does not create statutory profiles or configuration, loans, reimbursements, adjustments,
  overtime policies, shifts or rosters.

---

## 8. Blockers and risks

| ID | Status | Description |
|---|---|---|
| B1 | **Resolved by design** | `OvertimeNotFinalized` for any payroll period without a matching closed attendance month. Addressed by the sandbox attendance month (C3–C6), not by any engine change. |
| B2 | **BLOCKER (stopped after 2 attempts)**: Tier 2, realistic attendance (present/absent days, non-zero payable days) | It needs an employment-history row, because admin corrections require resolvable employment (`AttendanceAdminCorrectionService.cs:36-37`). That row would be open-ended. **Attempt 1:** bound the history. `EmployeeEmploymentRequest` has no `EffectiveTo` (`EmploymentRequest.cs:8-23`). **Attempt 2:** set `DateOfLeaving`. It must be null for an `Active` employee (`EmployeeRequestValidator.cs:92-94`), and payroll eligibility requires `Active` (`PayrollRunService.cs:59`). With open history and no day rows in real months, the QAAUTO employee would add `NotProcessed` blocking exceptions to **every real attendance month** and block the tenant's real close. There is also no generic day-processing API. **Needs a product decision.** |
| B3 | **Conditional** | If ANEVRA01 has maker-checker with self-approval prevention (G8), Approve/Finalize needs a second payroll-capable identity. Only `SuperAdmin`/`TenantAdmin` hold payroll permissions (smoke README, CR-165/CR-168). Provisioning a second admin is out of scope, so the cycle stops at `Calculated`. |
| B4 | **Needs sign-off (unavoidable via API)** | Each QAAUTO run's prepare writes an `Excluded` `PayrollRunEmployees` row for **every** tenant employee (`PayrollRunService.cs:51, 60`). The employee foreign key is `Restrict` (`PayrollPeriodRunConfiguration.cs:35`), so those real employees can't be deleted through `Employee.Delete` while QAAUTO runs exist. This is already true for the smoke suite's runs. The rows are additive only and carry no amounts. |
| B5 | **Needs sign-off** | Until L1 retirement, the QAAUTO employee (DOJ 1950, active, no DOL) appears in every **real** attendance month (a summary and snapshot with 0 employment days) and every real payroll population (`Excluded`). This is visible to HR but causes no blocking exceptions and no pay. |
| B6 | **Semantic gap** | In Tier 1, the attendance snapshot shows 0 eligible and payable days while pay is full, because the engine doesn't use attendance to compute amounts (engine `:103-108`; phase-6b doc). It proves the pipeline end to end, not attendance-driven pay. |
| B7 | **Existing hazard (audit only, G9)** | Smoke-suite teardown is best-effort. A leftover open-ended QAAUTO assignment (`EffectiveFrom = today`, `EffectiveTo = null`) makes that employee **eligible in a real current-month run**. Any QAAUTO employee with employment history (e.g. from the Employee suite's employment round-trip) would add blocking exceptions to real attendance months. Report these; don't fix without approval. |
| B8 | **Design note** | The smoke suite's 2071+ future periods can't be used for a happy path. Future shift days stay `NotProcessed`, and every real employee is swept into the attendance month. |

---

## 9. Verify live before implementation (not provable by static reading)

1. Salary component/structure and assignment create accept `EffectiveFrom = 1950-01-01`.
2. Payroll period create accepts `FiscalYear 1950` with the 1950 dates.
3. Personal-details employee create accepts DOJ `1950-01-01` without a DOB, and leaves employment
   history empty. Also check whether a manual employee code (`QAAUTO-HP-EMP01`) is allowed.
4. The earliest non-QAAUTO DOJ in ANEVRA01 is after `SANDBOX_END` (G7a). This needs a DOJ-sorted
   employee query or a full paged scan.
5. An active Bank master row exists (or can be created as QAAUTO), and a bank detail accepts
   `EffectiveFrom` null or `1950-01-01`.
6. Which `PayrollGLMappingType` values are needed to map a single Earning component plus net pay
   payable.
7. What `PayrollReadinessService` checks (not reviewed), and whether any of them fail for the
   sandbox cycle.
8. Whether a `PayrollControlConfiguration` exists in ANEVRA01 (G8), and what
   `GET /api/tenants/current/branding` returns for tenant identity (G5 cross-check).
