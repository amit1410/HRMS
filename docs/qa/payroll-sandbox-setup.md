# QAAUTO Payroll Sandbox: Setup, Happy Path and Reset (ANEVRA01, DEV/TEST only)

> **Status: implemented and run once.** Date: 2026-09-29. Tenant `ANEVRA01` (host
> `anevra01.localhost`) on the local Development stack (MySQL). This implements
> `docs/qa/payroll-automation-plan.md` (the design, with source citations) up to Bank Advice and GL
> generation. Nothing was committed. No `Frontend/` code was changed. The only `Backend/` change is
> the Bank master seeder in §4.1, and `PayrollCalculationEngine` validation is used exactly as it is.
>
> **Automated E2E (2026-09-29):** `qa/automation/tests/api/test_payroll_happy_path_e2e.py` runs this
> same path as ordered pytest stages with Allure evidence, one new sandbox month per session
> (`origin: "pytest-e2e"` in the manifest). The F1–F3 defects in §8
> have read-only regression checks in `qa/automation/tests/api/test_payroll_known_defects.py`. See
> "Tests (Payroll happy-path E2E on the QAAUTO sandbox)" in `qa/automation/README.md`.
>
> **Bank Advice happy path (2026-09-29, later the same day):** the sandbox employee now has a salary
> bank account, so Bank Advice goes `Draft → Prepared → Approved → Exported` with one `Valid` payment
> (§5.1). This needed one Backend change: a Development-only, opt-in seeder for a single fake Bank
> master row, because no public API creates banks (§4.1). The E2E has 12 stages now; months 1950-04
> (E2E), 1950-05 (CLI) and 1950-06 (E2E) ran the full bank advice lifecycle.
>
> **GL lifecycle + Payroll Analytics (2026-09-29, later the same day):**
> `qa/automation/tests/api/test_gl_analytics_e2e.py` takes a month's journal
> `Generated → Validated → Approved → Posted`, exports it, and reconciles the run's analytics against
> Payroll, Bank Advice and GL (§5.2). Months 1950-07 to 1950-10. New defects F6 and F7 (§8). No
> application code changed for this phase.

## 1. What it is

`qa/automation/sandbox/payroll_sandbox.py` is a command-line tool, not a pytest suite. It makes a
real payroll happy path possible without touching real data.

The calculation engine needs a **Closed attendance period whose dates exactly equal the payroll
period's** (`OvertimeService.ResolveAsync`). Attendance processing and close are tenant-wide, so the
tool uses a reserved deep-past window, **1950-01 to 1959-12**. Every real ANEVRA01 employee joined in
2026, so the attendance sweep for a sandbox month contains only the sandbox employee.

Every write goes through the public API as the Phase 6 QA Admin (`TenantAdmin`). No database writes,
no role or permission changes, no payroll-control changes. The one row the API can't create, the
`QAAUTO-BANK` master, comes from a Development-only, opt-in startup seeder (§4.1), not from this tool.

## 2. How to run

From `qa/automation`, using the venv's Python (in Git Bash, prefix `MSYS_NO_PATHCONV=1`):

```powershell
python -m sandbox.payroll_sandbox preflight           # read-only guards
$env:QA_PAYROLL_SANDBOX = "1"; $env:QA_PAYROLL_SANDBOX_TENANT = "ANEVRA01"
python -m sandbox.payroll_sandbox seed                # find-or-create masters (idempotent)
python -m sandbox.payroll_sandbox cycle               # resume the open month, or run the next free one
python -m sandbox.payroll_sandbox status              # manifest plus live guards, read-only
python -m sandbox.payroll_sandbox retire              # prints the L1 retirement plan
python -m sandbox.payroll_sandbox retire --execute    # performs it (not run yet)
```

- **State.** `qa/automation/.state/payroll-sandbox-manifest.json` (git-ignored) records the pinned
  tenant id and every id the sandbox created, plus per-step evidence for each cycle. Writes to an
  existing row are refused unless its id is in the manifest.
- **Idempotency.** `seed` finds each master by its natural key and reuses it. A second and third
  `seed` created nothing. `cycle` resumes an incomplete month from its first unfinished step. Once a
  month is complete, the next `cycle` uses the next free month, because an approved run can't be
  re-opened.
- **Fail closed.** If a key exists but the manifest doesn't know it, or a shape has drifted, the tool
  stops and reports. It never "fixes" a row.

## 3. Guards (every command runs these first; any failure means no writes)

| # | Check | This run |
|---|---|---|
| G1 | `QA_PAYROLL_SANDBOX=1` and `QA_PAYROLL_SANDBOX_TENANT=ANEVRA01` (write commands only) | set |
| G2 | API base is `http://localhost:<port>` | `http://localhost:5080/` |
| G3 | `QA_TENANT_A_HOST == anevra01.localhost`; re-checked before every write | pass |
| G4 | `/swagger/index.html` returns 200 (Swagger is mapped only in Development) | pass |
| G5 | JWT `tcode == ANEVRA01`; `tid` pinned in the manifest after the first write | pass, pinned |
| G6 | Actor is `qaauto-admin@anevra01.qa-automation.invalid` with `TenantAdmin` | pass |
| G7a | Earliest non-sandbox date of joining is after 1959-12-31 | 2026-09-01 |
| G7b/c | No attendance or payroll period in 1950–1959 that the manifest doesn't own | clean |
| G7d | Sandbox employee has zero employment-history rows | 0 |
| G8 | Maker-checker: self-approval blocked only if a control row is persisted | no row, so not blocked |
| G9 | Hazard audit: active open-ended salary assignments | 0 |
| G10 | `QAAUTO-BANK` master present and active (report only; without it, `seed` skips the bank account and Bank Advice payments validate `Invalid`) | present, active |

G4 uses the Swagger UI page because `swagger.json` currently returns 500 (see §8, F2).

G8 note: `GET /api/payroll/controls` returns `requireMakerChecker: true` and
`preventSelfApproval: true` even when **no row exists** (the id is all zeros). `PayrollApprovalGuard`
allows approval when the row is absent. The tool reads the id, not the flags. If anyone saves payroll
controls in ANEVRA01, approval will need a second payroll-capable identity (plan B3).

## 4. Prerequisites created

All rows are in ANEVRA01 only. Ids are in the manifest.

| Entity | Key | Shape | Id |
|---|---|---|---|
| Employee | `QaAutoPayroll QAAUTO-HP-01` | DOJ 1950-01-01, Active, no DOB, **no employment history** (personal-details path) | `0a60783c-9ab6-45b4-bcf0-36287aec55ae` |
| Salary component | `QAAUTO-HP-BASIC` | Earning, FixedAmount, effective 1950-01-01 | `572f742e-022c-494b-a930-17fd75de5e5c` |
| Salary structure | `QAAUTO-HP-STRUCT` | One line: FixedAmount 30000, not proratable | `f738db3a-e533-44ba-acfc-893616eca296` |
| Salary assignment | employee + structure | **1950-01-01 .. 1959-12-31**, INR, Monthly, CTC 30000/360000 | `7b1c4b85-7bca-4257-b935-6cbab460d0c9` |
| GL accounts | `QAAUTO-HP-GL-EXP` (Expense), `QAAUTO-HP-GL-PAY` (Liability) | active | `61f5a5ee-…`, `9bf7884d-…` |
| Accounting configuration | `QAAUTO-HP-ACCT` | | `20c65231-02eb-4558-9b67-63f86e7b8a44` |
| Accounting version | | **1950-01-01 .. 1959-12-31**, Active, aggregation `Account` | `4708980b-b09f-4238-a83d-cf506bbf9d3a` |
| GL mappings | Earnings (component → debit EXP / credit PAY); NetPayable (credit PAY) | active | `4d9fc5f8-…`, `bee3600a-…` |
| Payroll period (cycle) | `QAAUTO-HP-195001` | 1950-01-01 .. 1950-01-31, pay date 1950-02-01 | `e6c20cc7-cb84-4fbd-9b57-78160d226af6` |
| Attendance period (cycle) | 1950 / 1 | 1950-01-01 .. 1950-01-31 (equal to the payroll period) | `3672590f-9d00-4a31-85aa-d22a5b4d6cf6` |
| Payroll run (cycle) | `PR-1950-01-001` | Regular | `046935bf-1beb-4e95-b281-aa78e9195795` |
| Bank advice batch (cycle) | | Draft, 1 failed payment (§5) | `3f9df436-e4ce-4122-b4fb-3fe2610b171f` |
| GL journal (cycle) | | Generated, balanced | `edb8eeac-77ca-467d-9c55-187203a1003b` |
| Bank master (seeder, §4.1) | `QAAUTO-BANK`, "QAAUTO Sandbox Bank (not a real bank)" | active | `aaaaaaaa-0000-4a00-9a00-000000000031` (fixed) |
| Salary bank account | sandbox employee, `QAAUTO-BANK` | holder `QAAUTO HP Sandbox`, account `999000000001`, IFSC `QAAT0000001`, branch `QAAUTO Sandbox Branch`, Savings, **Salary**, Active, effective from 1950-01-01. All values are fake. | `d89df8c4-6468-4ca9-8521-5c028968f27b` |

**Leak check (plan I5).** The assignment and the accounting version are bounded to 1959. The sandbox
employee therefore can't be eligible in, and the sandbox GL version can't be selected for, any real
payroll period. Verified live: the effective-assignment lookup for today returns 404. The bank
account has no end date (`EmployeeBankDetail` has no `EffectiveTo`), but it belongs to the sandbox
employee only, and that employee is never eligible in a real run, so no real bank advice can pick it.

**Not created, by design.** Statutory profiles or configuration, loans, reimbursements, adjustments,
overtime policies, shifts and rosters. None is needed for calculation.

### 4.1 Bank master: Development-only seeder

No public API creates a Bank master row (§6). Instead of a direct database write, one fixed row comes
from `DatabaseSeeder.SeedQaAutomationBankAsync`
(`Backend/HRMS.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`). It follows the same pattern as
`SeedQaAutomationUsersAsync` and runs from `SeedShardAsync` on every startup, but only does anything
when **all** of these hold:

- the host environment is Development;
- the tenant code is `ANEVRA01`;
- `DevelopmentSeed:EnableQaAutomationBank` is `true` (env var or user-secrets, never committed JSON).

It inserts `QAAUTO-BANK` with the fixed id above if no row with that code exists in ANEVRA01. It
never updates an existing row, active or not, so a bank retired by hand stays retired. Unit tests:
`Backend/HRMS.Tests/QaAutomationBankSeedTests.cs` (each gate, idempotency, no reactivation).

To enable it (once; the row then persists without the flag):

```powershell
dotnet user-secrets set "DevelopmentSeed:EnableQaAutomationBank" "true" --project Backend/HRMS.API
# or, for one run only:
$env:DevelopmentSeed__EnableQaAutomationBank = "true"; dotnet run --project Backend/HRMS.API
```

The 2026-09-29 run used the env-var form, so user-secrets were not changed. The startup log shows
`QA automation bank QAAUTO-BANK was created for tenant ANEVRA01.` (or `already exists … not modified`).

The salary account itself goes through the public, validated
`POST /api/employees/{id}/bank-details` endpoint (`Employee.Edit` + `EmployeeSensitive.Edit`, both
held by `TenantAdmin`). `seed` finds or creates it: it reuses an existing current Salary account only
when it's on `QAAUTO-BANK` with the expected holder, last four digits and effective date, and stops if
the sandbox employee has any other current bank account. If the bank master is missing, `seed`
reports `bankAccount: skipped` and continues, so payroll and GL still run.

**Tenant visibility.** `QAAUTO-BANK` is an active bank in ANEVRA01, so HR users see it in the bank
drop-down on real employees. Nothing stops someone from picking it for a real employee. Its name says
"not a real bank". Retire it (§7) when the sandbox stops.

## 5. Happy-path result (cycle 1950-01)

| Step | Outcome |
|---|---|
| Payroll period | Created `Draft` |
| Attendance period | Created `Open`, dates equal the payroll period's |
| Attendance process | `employeesProcessed = 1` (the sandbox employee only), 0 blocking exceptions, `ReadyToClose` |
| Attendance close | Close preview `canClose = true`; period `Closed`; 1 `PayrollSnapshotCreated` event |
| Run create / prepare | `Prepared`; population 37, eligible **exactly** {sandbox employee}, 36 `Excluded`; readiness `ready`, 0 errors |
| **Calculate** | `Calculated`; calculated 1, failed 0; gross 30000.00, deductions 0.00, **net 30000.00**; no calculation errors |
| Results | 1 current result: component `QAAUTO-HP-BASIC` 30000.00, calendar days 31, eligible days 31, calculation version 1 |
| **Recalculate** | `Calculated`; calculated 1, failed 0; a new result (version 2) supersedes version 1; net still 30000.00 |
| Approve | `Approved` (same user; no control row exists, so G8 allowed it) |
| **Bank Advice generate** | **Reachable.** Batch created (`Draft`). Its single payment is `Invalid` / `Failed`: "No active salary bank account is effective for the pay date." Batch totals count valid payments only, so both are 0. |
| **GL journal generate** | **Reachable and complete.** `Generated`; debit `QAAUTO-HP-GL-EXP` 30000.00, credit `QAAUTO-HP-GL-PAY` 30000.00; balanced |
| Finalize | Not done, deliberately (outside "far enough to verify reachability") |

**Attendance finalization method.** Public API only: `POST /api/attendance/periods` →
`POST …/{id}/process` → `GET …/{id}/close-preview` → `POST …/{id}/close`. No day rows are
materialized. The sandbox employee has no employment history, so the processor counts 0 employment
days and 0 exceptions for it. Close then writes a `PayrollAttendanceSnapshot` with 0 eligible and 0
payable days.

The engine does not use attendance days to compute amounts (plan B6), so pay is still full. This
proves the pipeline end to end, not attendance-driven pay. `PayrollResultDto` does not expose
`attendanceSnapshotId`, so the link is shown by the close event and the absence of
`AttendanceSnapshotMissing`, not read from the result.

### 5.1 Bank Advice happy path (cycles 1950-04 to 1950-06)

Same steps as above up to Approve, then:

| Step | Outcome |
|---|---|
| Precondition | Exactly one current Salary account on the sandbox employee, effective 1950-01-01 (before the pay date); the employee API returns it masked (`********0001`) |
| **Generate** `POST /api/payroll/runs/{id}/bank-advice` | `Draft`, number `BA/{PayDate:yyyyMM}/{RunId:N}/1`. One payment: **`Valid` / `Ready`**, no validation message, net 30000.00 INR, holder `QAAUTO HP Sandbox`, bank "QAAUTO Sandbox Bank (not a real bank)", account `XXXXXXXX0001`, IFSC `QAAT0000001`, branch `QAAUTO Sandbox Branch`. Totals **1 employee, 30000.00**. A second generate is 409. Payroll results unchanged. |
| Negative checks on `Draft` | export 409 ("Only an approved bank advice batch can be exported."), approve 409 ("Only a prepared …") |
| **Prepare** `POST …/bank-advice/{id}/prepare` | `Prepared`, totals unchanged. Validate and prepare again are both 409 ("Only a draft …") |
| **Approve** `POST …/{id}/approve` | `Approved`, `approvedAtUtc` set. Same user as the generator; allowed because no payroll control row exists (G8, plan B3). A second approve is 409. |
| **Export** `GET …/{id}/export` | 200 `text/csv`, file `bank-advice-BA-…-1.csv`. Header plus one row that matches the payment; the full account number does not appear. Batch `Exported`, `exportedAtUtc` set, payment `Exported`. |
| Re-export | Byte-identical CSV; status, `exportedAtUtc` and history unchanged |
| Cancel an `Exported` batch | 409 ("This bank advice batch cannot be cancelled.") |
| History | `Exported, Approved, Prepared, Generated` (newest first), one row per transition |
| Payroll run | Still `Approved`; Bank Advice doesn't move the run. Not finalized (deliberately). |
| Analytics | Overview and run summary `bankAdviceTotal` = 30000.00 |
| Isolation | Real-data fingerprint unchanged in every session |

`Exported` is the last Bank Advice status. There is no bank-submission, payment-confirmation or
paid/reconciled status after it (BANKADV-EXP-008), so the batch lifecycle is complete.

| Month | Driver | Run | Bank advice batch | GL journal |
|---|---|---|---|---|
| 1950-04 | pytest E2E | `PR-1950-04-001` | `08539358-8db1-4c11-a711-dd820a6a513b` | `d12821fd-e665-4538-998e-ca201ddd04f2` |
| 1950-05 | CLI `cycle` | `PR-1950-05-001` | `27de75ea-636a-40b2-856c-83d22341ad53` (adopted, see below) | `adfb54c5-3da4-43e2-87cf-00d90cf2ec9f` |
| 1950-06 | pytest E2E | `PR-1950-06-001` | `1a1e93d5-8de3-4e85-86df-b1bd6898f252` | `ff271f00-6c47-4650-b300-fb98aab6c09b` |

**1950-05 note.** The first CLI `cycle` for 1950-05 crashed right after generating the batch: the
tool read `validationMessage`, which the API omits when it is null. The batch existed, but its id
wasn't saved to the manifest, so the resumed `cycle` got 409 ("An active bank advice batch already
exists"). The tool was fixed, and it now adopts the single active `Draft` batch of a run the
manifest owns (`bankAdviceBatchAdopted: true` in the manifest), and stops in any other case. The
fingerprint treats every batch of a sandbox run as sandbox data, so the adoption didn't trip the
isolation check.

**Earlier months.** The 1950-01 to 1950-03 batches were generated before the bank account existed.
They stay `Draft` with one `Invalid` payment, as evidence. They are not regenerated: that would need a
cancel plus a new generate, and it adds nothing the later months don't prove.

### 5.2 GL lifecycle and Payroll Analytics (cycles 1950-07 to 1950-10)

Same steps as §5.1 through GL generation (`sb.run_cycle`), then:

| Step | Outcome |
|---|---|
| Journal as generated | `Generated`, Debit = Credit = 30000.00. Line 1 debits `QAAUTO-HP-GL-EXP` for the current result's `QAAUTO-HP-BASIC` component (`sourceId` = component id); line 2 credits `QAAUTO-HP-GL-PAY` "Net Payable" (`sourceId` = current result id). No line points at the superseded result. |
| Refusals on `Generated` | second generate, export, approve and post are all 409 |
| **Validate** | `Validated`, message "Payroll journal validated."; totals and lines unchanged. Repeat validate, post and export are 409. |
| **Approve** | `Approved`, message "Payroll journal approved.". Same user as the generator; allowed because no payroll control row exists (G8). Repeat approve and backward validate are 409. |
| **Export while Approved** | 200 `text/csv; charset=utf-8`, `payroll-journal-PJ-{yyyyMM}-{RunId:N}-1.csv`, the 10-column header and one row per line. Status stays `Approved`. |
| **Post** | `Posted`, message "Payroll journal posted within HRMS." (no external system). Post, validate and approve on `Posted` are 409. |
| **Export while Posted** (×3) | byte-identical to the `Approved` export; status stays `Posted` |
| After posting | Regenerating for the run is 409 ("An active journal already exists for this payroll run."). There is no `cancel`, `reverse` or `history` route (bare 404), and the journal DTO has no history or timestamps. |
| Analytics totals | Overview, control totals, run summary, `summary.csv` and `control-totals.csv` all agree with the sources: net 30000.00 = bank advice total = payment = journal net-payable credit; gross 30000.00 = journal debit. |
| Drill-down | departments and cost-centers: one `UNCLASSIFIED` row (the sandbox employee has neither), summing to the run |
| Variance | `variance.csv?compareRunId=<previous sandbox month>`: delta 0, `NormalComparable`. Without `compareRunId` the service picks the other run with the latest period end, `PR-2424-04-001` (a `Prepared` non-sandbox run with no results), so the row is `NewValue` (CR-302). The JSON list is 500 (F1). |
| Reconciliation, clean run | pre and post: version 1, `Generated`, no findings, every check passed |
| Reconciliation, 1950-01 | post: one Critical `BankAdviceAmountMismatch`, expected 30000.00, actual 0.00 (that batch predates the salary account). No count or GL finding. Overview: 1 open, 1 critical, 0 anomalies (a bank mismatch never raises an anomaly flag, CR-298). Generated once (id in the cycle's `analyticsEvidence`), read back on reruns. |
| Isolation | Real-data fingerprint unchanged in every session |

`Posted` is the last reachable GL status. `Exported`, `Cancelled` and history-type `Reversed` exist in
the enums but no code path sets them (CR-275, CR-276, CR-277).

| Month | Journal | Final status | Reconciliations |
|---|---|---|---|
| 1950-07 | `e8881833-5b75-40a4-9544-80fa05803a8e` | Posted | pre v1 only (the session stopped on a test bug, since fixed) |
| 1950-08 | `38730515-e41c-4ce7-bfde-0bc072a37a7d` | Posted | pre v1, post v1 |
| 1950-09 | `c62adb9f-4c53-4d0b-a269-681cade26e64` | Posted | pre v1, post v1 |
| 1950-10 | `b488adbf-e0b7-4474-a832-f7294d61b257` | Posted | pre v1, post v1 |
| 1950-01 | `edb8eeac-…` (not transitioned) | Generated | post v1 `58b4f804-6112-4f7d-87fa-34d37d77087c` (mismatch evidence) |

**Cleanup.** A `Posted` journal can't be cancelled, reversed or regenerated through the API. The L2
reset in §7 already deletes a run's journal, lines, sources and history. To also remove this phase's
reconciliations, run these before the `PayrollRuns` delete (same `@tid`/`@run`; check the column
names against the shard schema first):

```sql
DELETE f FROM PayrollReconciliationFindings f JOIN PayrollReconciliations r ON r.Id = f.PayrollReconciliationId WHERE r.TenantId = @tid AND r.PayrollRunId = @run;
DELETE FROM PayrollReconciliations    WHERE TenantId = @tid AND PayrollRunId = @run;
DELETE FROM PayrollAnalyticsSnapshots WHERE TenantId = @tid AND PayrollRunId = @run;
```

## 6. Not creatable through public APIs

| Need | Why not | What was done |
|---|---|---|
| **Bank master** (needed for an employee bank detail, so Bank Advice payments validate) | ANEVRA01 had 0 banks. `/api/masters/{kind}` has no `banks` kind ("Master type is not supported"). Banks are only seeded for the demo tenants (`DatabaseSeeder.SeedBanksAsync`), Master Import has no bank type, and `/api/master-data/banks` is read-only. | One fixed `QAAUTO-BANK` row from a Development-only, opt-in seeder (§4.1). The employee bank account goes through the public API. |
| Scoping attendance process/close to one employee | No such parameter; the sweep is tenant-wide | Reserved 1950s window |
| Generic attendance-day processing | No controller exposes day processing | Not needed; the employee has no employment history |
| Bounded employment history | `EmployeeEmploymentRequest` has no `EffectiveTo` | The employee has no history (plan B2) |
| Delete/reset of periods, runs, structures, assignments, attendance periods | No delete endpoints | L1 retire (API) or L2 manual reset (§7) |
| Recovering GL ids without the manifest | The GL list endpoints return 500 (§8, F1) | GL ids are tracked only by the manifest; a code conflict without a manifest entry stops `seed` |

## 7. Cleanup / reset

**L0 (default): keep.** A completed sandbox month is kept as evidence. It is `Approved`, not
`Finalized`. A `Draft`, `Prepared` or `Approved` Bank Advice batch can be cancelled through
`POST /api/payroll/bank-advice/{id}/cancel` if a regenerate is ever needed. An `Exported` batch
can't be cancelled.

**L1: retire through the API.** Use this when the sandbox should stop.
1. Run `retire` to review the plan, then `retire --execute`. It deactivates both GL mappings, the
   assignment, the structure and the component, closes the sandbox salary bank account
   (`DELETE /api/employees/{id}/bank-details/{bankDetailId}`, a soft close), and sets both GL
   accounts inactive. Only ids from the manifest are touched.
2. **Manually**, in the Employee UI, set the sandbox employee to `Resigned` with date of leaving
   `1959-12-31`. The validator requires status and date together. This removes the employee from
   every real attendance sweep and every real payroll population.
3. The accounting version is already bounded to 1959, so it needs no action. No API retires a version.
4. **Bank master.** Unset `DevelopmentSeed:EnableQaAutomationBank`. No API retires a bank, so to hide
   `QAAUTO-BANK` from the bank drop-down, set it inactive by hand on the ANEVRA01 shard (never
   `hrms_catalog`): `UPDATE Banks SET IsActive = 0 WHERE TenantId = @tid AND Code = 'QAAUTO-BANK';`.
   The seeder never reactivates it.

After L1, new cycles can't run until the steps are reversed.

**L2: hard reset (manual SQL, human-run only, not executed here).**
- Run against the **ANEVRA01 shard only**, never `hrms_catalog`.
- Take a backup first. Use one transaction.
- Compare pre-counts to the manifest.
- Set `@tid` (the manifest's `tenant.tid`), then for each cycle set `@run`, `@pp` and `@ap` from the
  manifest.
- Column names follow the EF entity properties. **Check them against the shard schema before
  running.**

```sql
-- GL journal of the sandbox run
DELETE s FROM PayrollJournalLineSources s JOIN PayrollJournalLines l ON l.Id = s.PayrollJournalLineId JOIN PayrollJournalBatches b ON b.Id = l.PayrollJournalBatchId WHERE b.TenantId = @tid AND b.PayrollRunId = @run;
DELETE l FROM PayrollJournalLines l JOIN PayrollJournalBatches b ON b.Id = l.PayrollJournalBatchId WHERE b.TenantId = @tid AND b.PayrollRunId = @run;
DELETE h FROM PayrollJournalHistories h JOIN PayrollJournalBatches b ON b.Id = h.PayrollJournalBatchId WHERE b.TenantId = @tid AND b.PayrollRunId = @run;
DELETE FROM PayrollJournalBatches WHERE TenantId = @tid AND PayrollRunId = @run;
-- Bank advice of the sandbox run
DELETE h FROM BankAdviceHistories h JOIN BankAdviceBatches b ON b.Id = h.BankAdviceBatchId WHERE b.TenantId = @tid AND b.PayrollRunId = @run;
DELETE p FROM BankAdvicePayments p JOIN BankAdviceBatches b ON b.Id = p.BankAdviceBatchId WHERE b.TenantId = @tid AND b.PayrollRunId = @run;
DELETE FROM BankAdviceBatches WHERE TenantId = @tid AND PayrollRunId = @run;
-- Results, errors and history of the sandbox run
DELETE c FROM PayrollResultComponents c JOIN PayrollResults r ON r.Id = c.PayrollResultId WHERE r.TenantId = @tid AND r.PayrollRunId = @run;
DELETE FROM PayrollStatutoryResults    WHERE TenantId = @tid AND PayrollRunId = @run;
DELETE FROM PayrollResults             WHERE TenantId = @tid AND PayrollRunId = @run;
DELETE FROM PayrollCalculationErrors   WHERE TenantId = @tid AND PayrollRunId = @run;
DELETE FROM PayrollCalculationHistories WHERE TenantId = @tid AND PayrollRunId = @run;
-- Population rows: by run id only. This also removes the Excluded rows that point at real employees.
DELETE FROM PayrollRunEmployees        WHERE TenantId = @tid AND PayrollRunId = @run;
DELETE FROM PayrollRunHistories        WHERE TenantId = @tid AND PayrollRunId = @run;
DELETE FROM PayrollRuns                WHERE TenantId = @tid AND Id = @run;
DELETE FROM PayrollPeriodHistories     WHERE TenantId = @tid AND PayrollPeriodId = @pp;
DELETE FROM PayrollPeriods             WHERE TenantId = @tid AND Id = @pp AND Code LIKE 'QAAUTO-HP-%';
-- Sandbox attendance period (1950-1959 only)
DELETE FROM PayrollOvertimeSnapshots           WHERE TenantId = @tid AND AttendancePeriodId = @ap;
DELETE FROM PayrollAttendanceSnapshots         WHERE TenantId = @tid AND AttendancePeriodId = @ap;
DELETE FROM EmployeeAttendanceMonthlySummaries WHERE TenantId = @tid AND AttendancePeriodId = @ap;
DELETE FROM AttendancePeriodEvents             WHERE TenantId = @tid AND AttendancePeriodId = @ap;
DELETE FROM AttendancePeriods                  WHERE TenantId = @tid AND Id = @ap AND Year BETWEEN 1950 AND 1959;
```

After an L2 reset, remove that cycle's entry from the manifest.

Keep the masters (employee, assignment, structure, component, GL) and retire them with L1. Once a
real payroll run or real attendance month is prepared after today, it holds `Excluded` or summary
rows that reference the sandbox employee under `Restrict` foreign keys, and removing those would mean
editing real periods.

## 8. Product defects found (reported, not fixed)

| ID | Finding | Evidence |
|---|---|---|
| F1 | `GET /api/payroll/accounting/accounts`, `/configurations` and `/api/payroll/accounting` (journal list) return **500**: "Could not create an instance of type 'HRMS.Application.Common.PagedQuery'". The actions bind `[FromQuery] PagedQuery`, and `PagedQuery` is `abstract`. The same binding is on `GET /api/payroll/runs/{id}/employees`, several `PayrollAnalyticsController` lists, three `AttendanceWorkflowController` lists and `YearEndTaxController` runs. | Live 500s; `PayrollOutputsController.cs:62,68,84`; `PayrollRunsController.cs:19` |
| F2 | `GET /swagger/v1/swagger.json` returns 500: "Failed to generate Operation for action - HRMS.API.Controllers.MasterImportController.Validate". | Live |
| F3 | `GET /api/payroll/controls` reports `requireMakerChecker`/`preventSelfApproval` as `true` when no row exists, while the guard treats a missing row as "no controls". The UI can show a control that isn't enforced. | `PayrollControlService.cs:15`, `PayrollApprovalGuard.cs:13` |
| F4 | Bank Advice export is an HTTP **GET that changes state**. The first `GET /api/payroll/bank-advice/{id}/export` moves the batch `Approved → Exported`, marks every payment `Exported` and writes a history row. A retried, prefetched or cached GET can export a batch, and anyone who holds only `Payroll.BankAdviceExport` changes state by reading. Found this phase; no QA case flags it (BANKADV-EXP-005 describes the transition as expected). | `PayrollOutputsController.cs:132`; `BankAdviceService.ExportAsync`; live in §5.1 |
| F6 | `GET /api/payroll/analytics/runs/{id}/components` sums **superseded** results. `GetComponentVarianceAsync` reads `PayrollResultComponents` for the run with no `IsCurrent`/`Calculated` filter, unlike every other analytics read (`Results()`). After one recalculation the sandbox run reports `currentAmount` 60000.00 for a component whose current result is 30000.00. `GetControlTotalsAsync`'s source totals (reimbursement, loan, variable pay, adjustment) and `AddSettlementSourceFindingAsync` use the same unfiltered query, so they would double-count too once those sources are non-zero (not observed: they are 0 in the sandbox). No QA case flags it; PYA-CVAR-001 expects the right sums. Regression check: `test_component_variance_counts_current_results_only`. | Live on 1950-07..10; `PayrollAnalyticsService.cs:90-91` |
| F7 | The same endpoint **never finds the comparison run's amount**: `baseAmount` is 0 and `delta` equals the full current amount for a component present in both runs. The lookups `current.FirstOrDefault(x => x.Key == key)` / `prior.FirstOrDefault(x => x.Key == key)` compare anonymous-type keys with `==`, which is reference equality in C#; the keys come from `current` via `Union`, so only `current` ever matches. Two sandbox months with identical pay report base 0, current 60000.00, delta 60000.00 (both ways round). PYA-CVAR-001/-004 and PYA-LARGE-003 expect real base amounts. Regression check: `test_component_variance_matches_comparison_run`. | Live; `PayrollAnalyticsService.cs:93` |
| F5 | Observation, needs a product decision: the employee bank-detail API masks the IFSC (`QAAT*****01`, `SensitiveDataMasker.Ifsc`), but the Bank Advice payment DTO and CSV carry it in full (`QAAT0000001`). A bank file may need the full IFSC, so this may be intended. The account number is masked in both. | `EmployeeBankDetailService.MapToDto`; `BankAdviceService.GenerateAsync` / `ToDto` / `ExportAsync` |

## 9. Remaining blockers

1. ~~**Bank Advice beyond generation.**~~ **Resolved (2026-09-29).** A Development-only, opt-in
   seeder provides the `QAAUTO-BANK` master (§4.1), and the salary account is created through the
   public API. Bank Advice now runs to `Exported` (§5.1). There is still no public API to create or
   retire banks in any tenant; that is a product gap, not a sandbox blocker.
2. **Maker-checker is conditional (plan B3).** Run approval and Bank Advice approval both worked
   because no control row exists. If one is ever saved with self-approval prevention, run
   Approve/Finalize and Bank Advice approve/cancel need a second payroll-capable identity. The E2E then
   stops the batch at `Prepared` and skips with a B3 message.
3. ~~**GL beyond generation**~~ **Resolved (2026-09-29).** Validate, approve, post and export run
   on each session's own month (§5.2). `Posted` is the end of the reachable lifecycle: cancel,
   reversal, `Exported` and history are product gaps (CR-275/276/278), kept visible as failing
   `known_defect` checks. GL list and fingerprint are still blocked by F1, and so are the analytics
   variance/findings/exceptions/controls lists (read through their CSV exports where one exists).
4. **Realistic attendance (plan B2)** is still blocked. The snapshot carries 0 eligible/payable days.
5. **Footprint (plan B4/B5).** Each sandbox run's prepare writes `Excluded` population rows for every
   tenant employee (36 this run). Until L1 retirement, the sandbox employee (DOJ 1950, Active)
   appears in every future real attendance month (0 employment days, no blocking exceptions) and in
   every real payroll population (`Excluded`). No real data changed: counts and hashes of real
   employees, periods, runs, assignments, components, structures, attendance periods and bank advice
   were identical before and after `seed` and `cycle`.
6. **Cross-tenant isolation (plan I6)** was not re-tested live: `QA_TENANT_B_HOST` is not configured.
   Isolation rests on server-side tenant resolution (host/JWT `tid`), the pinned-`tid` guard, and the
   exact-set assertions above.
