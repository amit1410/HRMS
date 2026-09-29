"""Payroll happy-path E2E on the QAAUTO payroll sandbox (ANEVRA01, DEV/TEST only).

One sandbox month per session, through the public API as the Phase 6 QA Admin:

    sandbox ready (incl. salary bank account) -> payroll period -> Closed attendance period ->
    run created + prepared -> calculate -> recalculate -> approve -> bank advice generated (valid) ->
    bank advice prepared -> approved -> exported -> GL journal -> analytics read smoke ->
    real-data isolation check

Safety and idempotency come from `sandbox/payroll_sandbox.py`, which this suite drives:
- Preflight guards G1-G10 run before the first write. If any fails, every test here errors and
  nothing is written.
- Every write goes through `Api.write`, which refuses unless the Host is the pinned tenant host and
  the target is either a new QAAUTO-HP key or an id already in the manifest.
- `seed` is find-or-create, so a re-run creates no masters. Each session takes the next free month
  of the reserved 1950-1959 window, because an Approved run can't be re-opened. Completed steps are
  recorded in the manifest in the CLI's own format, so `python -m sandbox.payroll_sandbox cycle`
  can resume a month this suite left incomplete.
- A fingerprint of real (non-sandbox) employees, payroll, attendance and bank advice is taken
  before the first write and must be identical at the end.

Evidence attached to Allure is sandbox-only (the sandbox employee's rows, the sandbox run's totals)
or aggregate counts. Raw list responses, which carry real employees' names, are never attached.

Writes need QA_PAYROLL_SANDBOX=1 and QA_PAYROLL_SANDBOX_TENANT=ANEVRA01; without them the module
skips. Stages share state and run in file order, so run this module serially (or with
`--dist loadgroup`). A stage whose prerequisite did not pass skips and names that stage.

Design, cleanup and the defect/blocker ids used below: docs/qa/payroll-automation-plan.md and
docs/qa/payroll-sandbox-setup.md.
"""

from __future__ import annotations

import calendar
import csv
import io
import os
from collections import Counter
from dataclasses import dataclass, field
from datetime import date
from decimal import Decimal

import allure
import pytest

from sandbox import payroll_sandbox as sb
from utils.allure_evidence import attach_json, qa_cases

pytestmark = [
    allure.feature("Payroll"),
    allure.story("Happy Path E2E (QAAUTO sandbox)"),
    pytest.mark.api,
    pytest.mark.e2e,
    pytest.mark.xdist_group("payroll-happy-path-e2e"),
]

ANY_STATUS = range(100, 600)
EXPECTED_AMOUNT = Decimal("30000.00")
ZERO = Decimal("0.00")
BANK_ADVICE_CSV_HEADER = ["Sequence", "EmployeeCode", "EmployeeName", "AccountHolder", "BankName", "AccountNumber", "IFSC",
                          "NetPay", "Currency", "PaymentReference"]
EXCLUSION_REASONS = {"Employee is not active", "Employee is not employed during the period", "No effective salary assignment"}


def money(value) -> Decimal:
    return Decimal(str(value)).quantize(Decimal("0.01"))


def iso(value: date) -> str:
    return value.isoformat()


@dataclass
class HappyPath:
    api: sb.Api
    claims: dict
    preflight: dict
    fingerprint_before: dict
    passed: set = field(default_factory=set)
    facts: dict = field(default_factory=dict)
    key: str | None = None
    cycle: dict | None = None
    start: date | None = None
    end: date | None = None
    pay_date: date | None = None
    code: str | None = None

    @property
    def manifest(self) -> dict:
        return self.api.manifest

    @property
    def employee_id(self) -> str:
        return self.manifest["masters"]["employee"]["id"]

    @property
    def run_id(self) -> str:
        return self.cycle["payrollRunId"]

    def require(self, *stages: str) -> None:
        missing = [s for s in stages if s not in self.passed]
        if missing:
            pytest.skip(f"Blocked: prerequisite stage(s) {missing} did not pass in this session.")

    def read(self, path: str, **params):
        """GET returning (status, unwrapped data, response); the test asserts the status."""
        response = self.api.client.get(path, params=params or None)
        return response.status_code, sb._data(response), response

    def write(self, method: str, path: str, **kwargs):
        """Guarded write (see `sb.Api.write`) returning any HTTP status for the test to assert."""
        return self.api.write(method, path, expect=ANY_STATUS, **kwargs)

    def mark(self, step: str, detail) -> None:
        sb._mark(self.api, self.cycle, step, detail)

    def save(self) -> None:
        sb.save_manifest(self.manifest)

    def record_unexpected(self, what: str, status: int, data) -> None:
        """A write the product should have refused went through. Keep its ids with the cycle."""
        entry = {"what": what, "status": status, "id": data.get("id") if isinstance(data, dict) else None, "atUtc": sb.now_utc()}
        self.cycle.setdefault("unexpectedWrites", []).append(entry)
        self.save()

    def current_results(self) -> list:
        status, data, response = self.read(f"/api/payroll/runs/{self.run_id}/results", pageSize=100)
        assert status == 200, f"GET results returned {status}: {response.text[:400]}"
        return data["items"]


def result_digest(row: dict) -> dict:
    """Same keys as the CLI's `_single_result`, so the manifest stays resumable by the CLI."""
    return {
        "id": row["id"], "calculationVersion": row["calculationVersion"], "grossEarnings": row["grossEarnings"],
        "totalDeductions": row["totalDeductions"], "netPay": row["netPay"], "status": row["status"],
        "eligibleDays": row["eligibleDays"], "calendarDays": row["calendarDays"],
        "components": [(c["componentCode"], c["calculatedAmount"]) for c in row["components"]],
    }


def results_fingerprint(rows: list) -> list:
    return sorted((r["id"], r["employeeId"], str(money(r["netPay"])), r["status"], r["isCurrent"], r["calculationVersion"]) for r in rows)


@pytest.fixture(scope="module")
def hp(settings):
    if os.environ.get("QA_PAYROLL_SANDBOX") != "1" or os.environ.get("QA_PAYROLL_SANDBOX_TENANT") != sb.TENANT_CODE:
        pytest.skip("Payroll happy-path E2E writes to the QAAUTO sandbox; set QA_PAYROLL_SANDBOX=1 and "
                    "QA_PAYROLL_SANDBOX_TENANT=ANEVRA01 to run it (docs/qa/payroll-sandbox-setup.md).")
    try:
        client, claims = sb.sign_in(settings)
    except sb.SandboxAbort as exc:
        pytest.skip(f"Cannot sign in as the QA Admin: {exc}")
    manifest = sb.load_manifest()
    api = sb.Api(client, manifest, writes_enabled=False)
    try:
        report = sb.preflight(api, claims, settings, for_writes=True)
    except sb.SandboxAbort as exc:
        pytest.fail(f"Sandbox preflight refused, so nothing was written: {exc}", pytrace=False)
    manifest.setdefault("tenant", {}).update({"code": sb.TENANT_CODE, "tid": claims["tid"], "host": sb.TENANT_HOST})
    sb.save_manifest(manifest)
    before = sb.fingerprint(api)
    api.writes_enabled = True
    context = HappyPath(api=api, claims=claims, preflight=report, fingerprint_before=before)

    yield context

    api.writes_enabled = False
    if context.cycle is not None:
        context.cycle.setdefault("e2e", {}).update({"finishedAtUtc": sb.now_utc(), "stagesPassed": sorted(context.passed)})
        sb.save_manifest(manifest)


# --- Stage 1: sandbox exists ---


@allure.title("Sandbox ready: preflight guards pass and the QAAUTO-HP masters exist (find-or-create)")
@qa_cases("PAY-ESA-006")
def test_01_sandbox_ready(hp):
    with allure.step(f"Preflight guards G1-G10 passed before any write ({len(hp.preflight['warnings'])} warning(s))"):
        attach_json("preflight", hp.preflight)

    with allure.step("Seed the sandbox masters (find-or-create; a re-run creates nothing)"):
        try:
            outcome = sb.seed(hp.api)
        except sb.SandboxAbort as exc:
            pytest.fail(f"Seed stopped fail-closed: {exc}", pytrace=False)
        attach_json("seed-outcome", {"created": outcome["created"], "bankAccount": outcome["bankAccount"],
                                     "realRunLeakCheck": outcome["realRunLeakCheck"]})
        hp.facts["seed_created"] = outcome["created"]
        hp.facts["seed_bank_account"] = outcome["bankAccount"]

    masters = hp.manifest["masters"]
    with allure.step("Every master the cycle needs is recorded in the manifest"):
        attach_json("manifest-masters", masters)
        for key in ("employee", "salaryComponent", "salaryStructure", "salaryAssignment", "gl"):
            assert key in masters, f"Master {key!r} is missing from the manifest."
        for key in ("expenseAccount", "payableAccount", "configuration", "version"):
            assert key in masters["gl"], f"GL master {key!r} is missing from the manifest."
        assert set(masters["gl"]["mappings"]) == {"earnings", "netPayable"}

    assignment_id = masters["salaryAssignment"]["id"]
    effective = f"/api/payroll/employee-salary-assignments/by-employee/{hp.employee_id}/effective"
    with allure.step("PAY-ESA-006: exactly one assignment is effective inside the sandbox window"):
        status, data, response = hp.read(effective, date="1955-06-15")
        assert status == 200, f"{status}: {response.text[:400]}"
        assert data["id"] == assignment_id
        assert (data["effectiveFrom"], data["effectiveTo"]) == (iso(sb.SANDBOX_START), iso(sb.SANDBOX_END))

    with allure.step("PAY-ESA-006: no assignment is effective today, so the sandbox can't leak into a real run"):
        status, _, response = hp.read(effective, date=date.today().isoformat())
        assert status == 404, f"The sandbox assignment resolves for today ({status}): {response.text[:300]}"

    hp.passed.add("sandbox_ready")


# --- Stage 2: payroll period ---


@allure.title("PAY-PR-001: a Draft payroll period is created for the next free sandbox month")
@qa_cases("PAY-PR-001")
def test_02_payroll_period_created(hp):
    hp.require("sandbox_ready")
    year, month = sb._next_month(hp.manifest)
    hp.key = f"{year:04d}-{month:02d}"
    hp.start = date(year, month, 1)
    hp.end = date(year, month, calendar.monthrange(year, month)[1])
    hp.pay_date = date(year + (month == 12), month % 12 + 1, 1)
    hp.code = f"{sb.PREFIX}-{year:04d}{month:02d}"
    allure.dynamic.parameter("sandboxMonth", hp.key)

    with allure.step(f"Allocate sandbox month {hp.key}: no payroll period {hp.code} exists yet"):
        clash = [p["id"] for p in hp.api.paged("/api/payroll/periods") if p["code"].upper() == hp.code]
        assert not clash, f"{hp.code} already exists but the manifest doesn't record it. Stop and inspect (fail-closed)."
    hp.cycle = hp.manifest.setdefault("cycles", {}).setdefault(hp.key, {"state": "open"})
    hp.cycle.update({"origin": "pytest-e2e", "e2e": {"startedAtUtc": sb.now_utc()}})
    hp.save()

    with allure.step(f"POST /api/payroll/periods {hp.code}"):
        status, period, response = hp.write("POST", "/api/payroll/periods", new_key=hp.code, json_body={
            "code": hp.code, "name": f"QAAUTO HP {hp.key}", "periodType": "Monthly", "startDate": iso(hp.start),
            "endDate": iso(hp.end), "payDate": iso(hp.pay_date), "fiscalYear": year, "periodNumber": month, "isActive": True})
        assert status in (200, 201), f"{status}: {response.text[:600]}"
        hp.cycle["payrollPeriodId"] = period["id"]
        hp.mark("payroll_period_created", {"id": period["id"], "code": hp.code, "status": period["status"],
                                           "start": iso(hp.start), "end": iso(hp.end)})
        attach_json("payroll-period", period)

    with allure.step("The period is Draft with the requested calendar-month dates and pay date"):
        assert period["status"] == "Draft"
        assert (period["code"], period["startDate"], period["endDate"], period["payDate"]) == (
            hp.code, iso(hp.start), iso(hp.end), iso(hp.pay_date))
        assert period["isActive"] is True

    hp.passed.add("payroll_period")


# --- Stage 3: Closed attendance period ---


@allure.title("ATT-MONTHLY-001/004/018/019: the matching attendance month is created, processed (sandbox employee only) and Closed")
@qa_cases("ATT-MONTHLY-001", "ATT-MONTHLY-004", "ATT-MONTHLY-018", "ATT-MONTHLY-019")
def test_03_attendance_period_closed(hp):
    hp.require("payroll_period")
    year, month = hp.start.year, hp.start.month

    with allure.step(f"No attendance period exists for {hp.key} yet"):
        status, listed, response = hp.read("/api/attendance/periods", year=year, month=month)
        assert status == 200, f"{status}: {response.text[:300]}"
        assert listed["items"] == [], f"An attendance period for {hp.key} exists but the manifest doesn't record it."

    with allure.step("ATT-MONTHLY-001: create an Open period whose dates equal the payroll period's"):
        status, period, response = hp.write("POST", "/api/attendance/periods", new_key=f"attendance:{hp.key}",
                                            json_body={"year": year, "month": month})
        assert status == 201, f"{status}: {response.text[:600]}"
        hp.cycle["attendancePeriodId"] = period["id"]
        hp.mark("attendance_period_created", {"id": period["id"], "status": period["status"],
                                              "start": period["startDate"], "end": period["endDate"]})
        attach_json("attendance-period-created", period)
        assert period["status"] == "Open"
        assert (period["startDate"], period["endDate"]) == (iso(hp.start), iso(hp.end))
    attendance_id = period["id"]

    with allure.step("ATT-MONTHLY-004: processing sweeps exactly the sandbox employee, no blocking exceptions, ReadyToClose"):
        status, overview, response = hp.write("POST", f"/api/attendance/periods/{attendance_id}/process", target_id=attendance_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("attendance-process-overview", overview)
        assert (overview["employeesProcessed"], overview["blockingExceptions"]) == (1, 0), (
            "Sandbox contamination guard: the tenant-wide sweep must contain only the sandbox employee with no "
            f"blocking exceptions, got {overview['employeesProcessed']} / {overview['blockingExceptions']}. Stop.")
        status, summaries, response = hp.read(f"/api/attendance/periods/{attendance_id}/summaries", pageSize=100)
        assert status == 200, f"{status}: {response.text[:300]}"
        assert [s["employeeId"] for s in summaries["items"]] == [hp.employee_id]
        attach_json("attendance-summary (sandbox employee)", summaries["items"][0])
        assert overview["status"] == "ReadyToClose"
        hp.mark("attendance_processed", {"employeesProcessed": overview["employeesProcessed"],
                                         "blockingExceptions": overview["blockingExceptions"], "status": overview["status"]})

    with allure.step("ATT-MONTHLY-018: the close preview allows closing"):
        status, preview, response = hp.read(f"/api/attendance/periods/{attendance_id}/close-preview")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("attendance-close-preview", preview)
        assert preview["canClose"] is True, preview["blockers"]
        assert (preview["blockingExceptionCount"], preview["blockers"]) == (0, [])

    with allure.step("ATT-MONTHLY-019: close succeeds and records a payroll snapshot"):
        status, closed, response = hp.write("POST", f"/api/attendance/periods/{attendance_id}/close", target_id=attendance_id,
                                            json_body={"comment": f"{sb.PREFIX} e2e close {hp.key}"})
        assert status == 200, f"{status}: {response.text[:600]}"
        assert closed["status"] == "Closed"
        status, events, response = hp.read(f"/api/attendance/periods/{attendance_id}/events")
        assert status == 200, f"{status}: {response.text[:300]}"
        snapshots = [e for e in events if e["eventType"] == "PayrollSnapshotCreated"]
        hp.mark("attendance_closed", {"status": closed["status"], "canClose": preview["canClose"], "payrollSnapshotsCreated": len(snapshots)})
        attach_json("attendance-events", [{k: e[k] for k in ("eventType", "occurredAtUtc", "dataVersion")} for e in events])
        assert len(snapshots) >= 1, "Close recorded no PayrollSnapshotCreated event."

    hp.passed.add("attendance_closed")


# --- Stage 4: run created and prepared ---


@allure.title("PAY-PR-011/012: a Regular run is created and prepared; the eligible population is exactly the sandbox employee")
@qa_cases("PAY-PR-011", "PAY-PR-012")
def test_04_payroll_run_prepared(hp):
    hp.require("attendance_closed")
    period_id = hp.cycle["payrollPeriodId"]

    with allure.step("The sandbox period has no runs yet"):
        assert hp.api.paged("/api/payroll/runs", payrollPeriodId=period_id) == []

    with allure.step("PAY-PR-011: create a Regular run; the run number is generated"):
        status, run, response = hp.write("POST", "/api/payroll/runs", target_id=period_id, json_body={
            "payrollPeriodId": period_id, "runType": "Regular", "notes": f"{sb.PREFIX} e2e cycle {hp.key}"})
        assert status in (200, 201), f"{status}: {response.text[:600]}"
        hp.cycle["payrollRunId"] = run["id"]
        hp.mark("run_created", {"id": run["id"], "runNumber": run["runNumber"], "status": run["status"]})
        assert run["status"] == "Draft"
        assert run["runType"] == "Regular"
        assert run["runNumber"].startswith(f"PR-{hp.start.year:04d}-{hp.start.month:02d}-"), run["runNumber"]
    run_id = run["id"]

    with allure.step("PAY-PR-012: prepare; only the sandbox employee is eligible, everyone else is Excluded with a reason"):
        status, run, response = hp.write("POST", f"/api/payroll/runs/{run_id}/prepare", target_id=run_id, params={"rebuild": "false"})
        assert status == 200, f"{status}: {response.text[:600]}"
        eligible = [e for e in run["employees"] if e["isEligible"]]
        excluded = [e for e in run["employees"] if not e["isEligible"]]
        reasons = Counter(e["exclusionReason"] for e in excluded)
        attach_json("prepare-population (aggregate; sandbox row only)", {
            "status": run["status"], "employeeCount": run["employeeCount"], "eligibleCount": run["eligibleCount"],
            "excludedCount": run["excludedCount"], "exclusionReasons": dict(reasons), "sandboxRow": eligible[0] if eligible else None})
        assert [e["employeeId"] for e in eligible] == [hp.employee_id], (
            f"Eligible population must be exactly the sandbox employee, got {len(eligible)} row(s). Stop.")
        assert run["status"] == "Prepared"
        assert (run["eligibleCount"], run["excludedCount"]) == (1, run["employeeCount"] - 1)
        assert eligible[0]["employeeSalaryAssignmentId"] == hp.manifest["masters"]["salaryAssignment"]["id"]
        assert set(reasons) <= EXCLUSION_REASONS, f"Unexpected exclusion reasons: {dict(reasons)}"

    with allure.step("Readiness reports ready with no errors"):
        status, readiness, response = hp.read(f"/api/payroll/runs/{run_id}/readiness")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("readiness", readiness)
        hp.mark("run_prepared", {"status": run["status"], "employeeCount": run["employeeCount"], "eligible": len(eligible),
                                 "excluded": len(excluded), "readiness": readiness})
        assert readiness["ready"] is True and readiness["errorCount"] == 0, readiness

    hp.facts["employee_count"] = run["employeeCount"]
    hp.passed.add("run_prepared")


# --- Stage 5: calculate ---


@allure.title("PAY-CALC-001/002: calculate gives 1 eligible, 1 calculated, 0 failed, and Gross = Net = 30000.00")
@qa_cases("PAY-CALC-001", "PAY-CALC-002")
def test_05_calculate(hp):
    hp.require("run_prepared")
    run_id = hp.run_id

    with allure.step("POST calculate"):
        status, summary, response = hp.write("POST", f"/api/payroll/runs/{run_id}/calculate", target_id=run_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("calculation-summary", summary)
        status, errors, response = hp.read(f"/api/payroll/runs/{run_id}/calculation-errors")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("calculation-errors", errors)

    with allure.step("Run summary: Calculated, 1 calculated, 0 failed, gross 30000.00, deductions 0.00, net 30000.00"):
        assert summary["status"] == "Calculated", f"{summary}; errors: {errors}"
        assert (summary["calculatedCount"], summary["failedCount"]) == (1, 0), f"{summary}; errors: {errors}"
        assert summary["employeeCount"] == hp.facts["employee_count"]
        assert (money(summary["grossEarnings"]), money(summary["totalDeductions"]), money(summary["netPay"])) == (
            EXPECTED_AMOUNT, ZERO, EXPECTED_AMOUNT)
        assert errors == []
        status, run, response = hp.read(f"/api/payroll/runs/{run_id}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert (run["status"], run["eligibleCount"]) == ("Calculated", 1)

    with allure.step("One current result, for the sandbox employee only: QAAUTO-HP-BASIC 30000.00"):
        rows = hp.current_results()
        assert [r["employeeId"] for r in rows] == [hp.employee_id]
        row = rows[0]
        attach_json("payroll-result (sandbox employee)", row)
        assert (row["status"], row["isCurrent"], row["currencyCode"]) == ("Calculated", True, sb.CURRENCY)
        assert (money(row["grossEarnings"]), money(row["totalDeductions"]), money(row["netPay"])) == (
            EXPECTED_AMOUNT, ZERO, EXPECTED_AMOUNT)
        assert [(c["componentCode"], money(c["calculatedAmount"])) for c in row["components"]] == [(sb.COMPONENT_CODE, EXPECTED_AMOUNT)]
        days = (hp.end - hp.start).days + 1
        assert (row["calendarDays"], row["eligibleDays"]) == (days, days)
        digest = result_digest(row)
        hp.facts["first_result"] = digest
        hp.mark("calculated", {"summary": summary, "errors": errors, "result": digest})

    with allure.step("PAY-CALC-002: a plain calculate on an already Calculated run is refused (409)"):
        status, data, response = hp.write("POST", f"/api/payroll/runs/{run_id}/calculate", target_id=run_id)
        assert status == 409, f"Expected 409, got {status}: {response.text[:400]}"

    hp.passed.add("calculated")


# --- Stage 6: recalculate ---


@allure.title("PAY-CALC-004/024: recalculate creates a new calculation version that supersedes the first; the amount is unchanged")
@qa_cases("PAY-CALC-004", "PAY-CALC-024")
def test_06_recalculate(hp):
    hp.require("calculated")
    run_id = hp.run_id
    first = hp.facts["first_result"]

    with allure.step("POST recalculate: Calculated, 1 calculated, 0 failed, net 30000.00"):
        status, summary, response = hp.write("POST", f"/api/payroll/runs/{run_id}/recalculate", target_id=run_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("recalculation-summary", summary)
        assert summary["status"] == "Calculated"
        assert (summary["calculatedCount"], summary["failedCount"]) == (1, 0), summary
        assert (money(summary["grossEarnings"]), money(summary["netPay"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT)

    with allure.step("PAY-CALC-004: the current result is version prior + 1; gross, net and components are unchanged"):
        rows = hp.current_results()
        assert [r["employeeId"] for r in rows] == [hp.employee_id]
        row = rows[0]
        attach_json("payroll-result after recalculation (sandbox employee)", row)
        assert row["calculationVersion"] == first["calculationVersion"] + 1, (first["calculationVersion"], row["calculationVersion"])
        assert row["id"] != first["id"], "Recalculation returned the superseded result row."
        assert row["isCurrent"] is True
        assert (money(row["grossEarnings"]), money(row["netPay"])) == (money(first["grossEarnings"]), money(first["netPay"]))
        assert [(c["componentCode"], money(c["calculatedAmount"])) for c in row["components"]] == [
            (code, money(amount)) for code, amount in first["components"]]

    with allure.step("PAY-CALC-024: read endpoints return only the current attempt (no superseded result, no errors)"):
        status, single, response = hp.read(f"/api/payroll/runs/{run_id}/results/{hp.employee_id}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert (single["id"], single["calculationVersion"]) == (row["id"], row["calculationVersion"])
        status, errors, response = hp.read(f"/api/payroll/runs/{run_id}/calculation-errors")
        assert status == 200 and errors == [], f"{status}: {errors}"

    hp.facts["current_result"] = result_digest(row)
    hp.mark("recalculated", {"summary": summary, "result": result_digest(row), "supersededResultId": first["id"]})
    hp.passed.add("recalculated")


# --- Stage 7: approve ---


@allure.title("PAY-PR-017/021, PAY-CALC-003, BANKADV-GEN-003, GLACC-GEN-003: outputs are refused before approval; approve; recalculation is refused after")
@qa_cases("PAY-PR-017", "PAY-PR-021", "PAY-CALC-003", "BANKADV-GEN-003", "GLACC-GEN-003")
def test_07_approve(hp):
    hp.require("recalculated")
    if hp.preflight["selfApprovalBlocked"]:
        pytest.skip("B3: a persisted payroll control row blocks self-approval, and approving needs a second "
                    "payroll-capable identity. The run is left Calculated.")
    run_id = hp.run_id

    with allure.step("BANKADV-GEN-003 / GLACC-GEN-003: bank advice and GL generation on a Calculated run are refused (409)"):
        for what, path in (("bank advice", f"/api/payroll/runs/{run_id}/bank-advice"),
                           ("GL journal", f"/api/payroll/runs/{run_id}/accounting/generate")):
            status, data, response = hp.write("POST", path, target_id=run_id)
            if status in (200, 201):
                hp.record_unexpected(f"{what} generated before approval", status, data)
            assert status == 409, f"{what} before approval: expected 409, got {status}: {response.text[:400]}"

    with allure.step("PAY-PR-017: transition Calculated -> Approved"):
        status, run, response = hp.write("POST", f"/api/payroll/runs/{run_id}/transition", target_id=run_id, params={"target": "Approved"})
        assert status == 200, f"{status}: {response.text[:600]}"
        assert run["status"] == "Approved"
        hp.mark("approved", {"status": run["status"]})

    actor = hp.claims.get("sub")
    with allure.step("PAY-PR-021: run history has an Approved row by the approving user"):
        status, history, response = hp.read(f"/api/payroll/runs/{run_id}/history")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("run-history", history)
        approved = [h for h in history if h["changeType"] == "Approved"]
        assert len(approved) == 1 and approved[0]["actorUserId"] == actor, approved
        assert {"Created", "PopulationGenerated", "Prepared", "Approved"} <= {h["changeType"] for h in history}
        hp.facts["self_approved"] = run["startedByUserId"] == actor
        attach_json("maker-checker evidence", {"startedByUserId": run["startedByUserId"], "approvedByUserId": actor,
                                               "selfApproved": hp.facts["self_approved"],
                                               "controlsRowPersisted": hp.preflight["checks"]["G8_maker_checker"]["persistedRow"]})

    with allure.step("PAY-CALC-003: recalculate on an Approved run is refused (409)"):
        status, data, response = hp.write("POST", f"/api/payroll/runs/{run_id}/recalculate", target_id=run_id)
        assert status == 409, f"Expected 409, got {status}: {response.text[:400]}"

    hp.passed.add("approved")


# --- Stage 8: bank advice generated ---


@allure.title("BANKADV-GEN-004/008/009/010/012, BANKADV-ACCT-001/006/007/008, BANKADV-VAL-006: bank advice generates a Draft batch "
              "whose one payment is Valid/Ready on the sandbox salary account")
@allure.description(
    "The sandbox employee's salary account is on the QAAUTO-BANK master, which only the Development-only, opt-in "
    "DevelopmentSeed:EnableQaAutomationBank seeder creates (there is no public bank-create API). The account itself "
    "was created through the public employee bank-detail API by the sandbox seed.")
@qa_cases("BANKADV-GEN-004", "BANKADV-GEN-008", "BANKADV-GEN-009", "BANKADV-GEN-010", "BANKADV-GEN-012",
          "BANKADV-ACCT-001", "BANKADV-ACCT-006", "BANKADV-ACCT-007", "BANKADV-ACCT-008", "BANKADV-VAL-006")
def test_08_bank_advice_generated(hp):
    hp.require("approved")
    run_id = hp.run_id
    account = hp.manifest["masters"].get("bankAccount")
    if account is None:
        pytest.fail(f"The sandbox employee has no salary bank account ({hp.facts['seed_bank_account']}). Bank Advice "
                    "payments would validate Invalid. Set DevelopmentSeed:EnableQaAutomationBank=true, restart the API and "
                    "re-run (docs/qa/payroll-sandbox-setup.md).", pytrace=False)
    before = results_fingerprint(hp.current_results())

    with allure.step("BANKADV-ACCT-001 precondition: exactly one current Salary account, effective on or before the pay date"):
        status, details, response = hp.read(f"/api/employees/{hp.employee_id}/bank-details")
        assert status == 200, f"{status}: {response.text[:300]}"
        current = [d for d in details if d["isActive"] and d["status"] == "Active" and d["accountPurpose"] == "Salary"]
        attach_json("salary bank account (sandbox employee, masked)", current)
        assert [d["id"] for d in current] == [account["id"]]
        assert current[0]["bankId"] == account["bankId"] and current[0]["effectiveFrom"] <= iso(hp.pay_date)
        assert current[0]["maskedAccountNumber"] != sb.BANK_ACCOUNT_NUMBER, "The employee API returned the full account number."

    with allure.step("POST /api/payroll/runs/{id}/bank-advice"):
        status, batch, response = hp.write("POST", f"/api/payroll/runs/{run_id}/bank-advice", target_id=run_id)
        assert status in (200, 201), f"{status}: {response.text[:600]}"
        hp.cycle["bankAdviceBatchId"] = batch["id"]
        hp.mark("bank_advice_generated", {
            "id": batch["id"], "status": batch["status"], "totalEmployees": batch["totalEmployees"], "totalAmount": batch["totalAmount"],
            "payments": [{"employeeId": p["employeeId"], "netPay": p["netPay"], "validationStatus": p["validationStatus"],
                          "paymentStatus": p["paymentStatus"], "validationMessage": p.get("validationMessage")} for p in batch["payments"]]})
        attach_json("bank-advice-batch (sandbox run)", batch)

    with allure.step("BANKADV-GEN-008/009: Draft batch for this run, numbered BA/{PayDate:yyyyMM}/{RunId:N}/1, with a Generated event"):
        assert batch["status"] == "Draft"
        assert (batch["payrollRunId"], batch["payrollPeriodId"], batch["payDate"]) == (run_id, hp.cycle["payrollPeriodId"], iso(hp.pay_date))
        assert batch["batchNumber"] == f"BA/{hp.pay_date:%Y%m}/{run_id.replace('-', '')}/1", batch["batchNumber"]
        assert batch["generatedAtUtc"]
        status, fetched, response = hp.read(f"/api/payroll/bank-advice/{batch['id']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert [h["changeType"] for h in fetched["history"]] == ["Generated"], fetched["history"]

    assert len(batch["payments"]) == 1, f"Expected one payment, got {len(batch['payments'])}."
    payment = batch["payments"][0]
    with allure.step("BANKADV-GEN-010 / BANKADV-ACCT-006/007: one payment for the sandbox employee; net pay and currency are the result's"):
        assert payment["employeeId"] == hp.employee_id
        assert (payment["sequence"], payment["paymentReference"]) == (1, f"{batch['batchNumber']}/0001")
        assert money(payment["netPay"]) == money(hp.facts["current_result"]["netPay"]) == EXPECTED_AMOUNT
        assert payment["currencyCode"] == sb.CURRENCY

    with allure.step("BANKADV-VAL-006: the payment is Valid/Ready with no validation message"):
        assert (payment["validationStatus"], payment["paymentStatus"]) == ("Valid", "Ready"), payment.get("validationMessage")
        # The API omits null fields (JsonIgnoreCondition.WhenWritingNull), so absent means null.
        assert payment.get("validationMessage") is None

    with allure.step("BANKADV-ACCT-001/008: account fields come from the salary account; the account number is masked"):
        masked = "X" * (len(sb.BANK_ACCOUNT_NUMBER) - 4) + sb.BANK_ACCOUNT_NUMBER[-4:]
        assert (payment["accountHolderName"], payment["bankName"], payment["maskedAccountNumber"]) == (
            sb.BANK_ACCOUNT_HOLDER, account["bankName"], masked)
        assert (payment.get("ifscCode"), payment.get("branchName")) == (sb.BANK_IFSC, sb.BANK_BRANCH)

    with allure.step("Batch totals count the one valid payment: 1 employee, 30000.00"):
        assert (batch["totalEmployees"], money(batch["totalAmount"])) == (1, EXPECTED_AMOUNT)

    with allure.step("BANKADV-GEN-004: a second generate while this batch is active is refused (409)"):
        status, data, response = hp.write("POST", f"/api/payroll/runs/{run_id}/bank-advice", target_id=run_id)
        if status in (200, 201):
            hp.record_unexpected("second bank advice batch", status, data)
        assert status == 409, f"Expected 409, got {status}: {response.text[:400]}"

    with allure.step("BANKADV-GEN-012: payroll results are unchanged by generation"):
        assert results_fingerprint(hp.current_results()) == before

    hp.facts["bank_advice_batch"] = batch
    hp.facts["bank_advice_total"] = money(batch["totalAmount"])
    hp.passed.add("bank_advice")


# --- Stage 9: bank advice prepared, approved, exported ---


@allure.title("BANKADV-LIFE-001/007/011, BANKADV-EXP-001/002/003/005/006, BANKADV-CANCEL-001, BANKADV-AUDIT-001/003: "
              "the batch goes Draft -> Prepared -> Approved -> Exported, and the CSV carries the one masked payment")
@allure.description(
    "Exported is the last Bank Advice status; there is no bank-submission or payment-confirmation step after it "
    "(BANKADV-EXP-008). Export is a GET that changes state (Approved -> Exported). The payroll run itself is left "
    "Approved: finalizing it is outside this stage.")
@qa_cases("BANKADV-LIFE-001", "BANKADV-LIFE-007", "BANKADV-LIFE-011", "BANKADV-EXP-001", "BANKADV-EXP-002",
          "BANKADV-EXP-003", "BANKADV-EXP-005", "BANKADV-EXP-006", "BANKADV-CANCEL-001", "BANKADV-AUDIT-001",
          "BANKADV-AUDIT-003")
def test_09_bank_advice_prepared_approved_exported(hp):
    hp.require("bank_advice")
    batch_id = hp.cycle["bankAdviceBatchId"]
    base = f"/api/payroll/bank-advice/{batch_id}"
    generated = hp.facts["bank_advice_batch"]
    payment = generated["payments"][0]

    def refused(method: str, path: str, what: str, message: str) -> None:
        status, data, response = hp.write(method, path, target_id=batch_id)
        if status in (200, 201):
            hp.record_unexpected(what, status, data)
        assert status == 409, f"{what}: expected 409, got {status}: {response.text[:400]}"
        assert message in response.text, response.text[:400]

    with allure.step("BANKADV-EXP-001 / BANKADV-LIFE-007: export and approve are refused while the batch is Draft (409)"):
        refused("GET", f"{base}/export", "export of a Draft batch", "Only an approved bank advice batch can be exported.")
        refused("POST", f"{base}/approve", "approval of a Draft batch", "Only a prepared bank advice batch can be approved.")

    with allure.step("POST prepare: Draft -> Prepared; totals unchanged"):
        status, batch, response = hp.write("POST", f"{base}/prepare", target_id=batch_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("bank-advice after prepare", batch)
        assert batch["status"] == "Prepared"
        assert (batch["totalEmployees"], money(batch["totalAmount"])) == (1, EXPECTED_AMOUNT)
        hp.mark("bank_advice_prepared", {"status": batch["status"], "totalEmployees": batch["totalEmployees"], "totalAmount": batch["totalAmount"]})

    with allure.step("BANKADV-LIFE-001: validate and prepare are refused once the batch is Prepared (409)"):
        for action in ("validate", "prepare"):
            refused("POST", f"{base}/{action}", f"{action} of a Prepared batch", "Only a draft bank advice batch can be validated or prepared.")

    if hp.preflight["selfApprovalBlocked"]:
        hp.passed.add("bank_advice_prepared")
        pytest.skip("B3: a persisted payroll control row blocks self-approval; approving the batch needs a second "
                    "payroll-capable identity. The batch is left Prepared.")

    actor = hp.claims.get("sub")
    with allure.step("BANKADV-LIFE-011: approve stamps ApprovedAtUtc; the approver is the generating user (no control row)"):
        status, batch, response = hp.write("POST", f"{base}/approve", target_id=batch_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("bank-advice after approve", batch)
        assert batch["status"] == "Approved" and batch["approvedAtUtc"]
        assert batch.get("exportedAtUtc") is None
        hp.mark("bank_advice_approved", {"status": batch["status"], "approvedAtUtc": batch["approvedAtUtc"]})
        attach_json("maker-checker evidence (bank advice)", {
            "approvedByUserId": actor, "controlsRowPersisted": hp.preflight["checks"]["G8_maker_checker"]["persistedRow"]})

    with allure.step("BANKADV-LIFE-007: a second approve is refused (409)"):
        refused("POST", f"{base}/approve", "second approval", "Only a prepared bank advice batch can be approved.")

    with allure.step("BANKADV-EXP-005: the first export returns the CSV and moves the batch and its payment to Exported"):
        status, csv_text, response = hp.write("GET", f"{base}/export", target_id=batch_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        assert response.headers.get("Content-Type", "").startswith("text/csv"), response.headers.get("Content-Type")
        disposition = response.headers.get("Content-Disposition", "")
        expected_name = f"bank-advice-{generated['batchNumber'].replace('/', '-')}.csv"
        assert expected_name in disposition, disposition
        allure.attach(csv_text, name="bank-advice export (sandbox run, masked)", attachment_type=allure.attachment_type.CSV)
        status, exported, response = hp.read(base)
        assert status == 200, f"{status}: {response.text[:300]}"
        assert exported["status"] == "Exported" and exported["exportedAtUtc"]
        assert [p["paymentStatus"] for p in exported["payments"]] == ["Exported"]
        hp.mark("bank_advice_exported", {"status": exported["status"], "exportedAtUtc": exported["exportedAtUtc"],
                                         "csvRows": len(csv_text.strip().splitlines()) - 1})

    with allure.step("BANKADV-EXP-002/003: header plus one row matching the payment; only the masked account number appears"):
        rows = list(csv.reader(io.StringIO(csv_text)))
        assert rows[0] == BANK_ADVICE_CSV_HEADER
        assert len(rows) == 2, f"Expected a header and one row, got {len(rows)} line(s)."
        row = dict(zip(rows[0], rows[1]))
        assert (row["Sequence"], row["EmployeeCode"], row["EmployeeName"], row["PaymentReference"]) == (
            "1", payment["employeeCode"], payment["employeeName"], payment["paymentReference"])
        assert (row["AccountHolder"], row["BankName"], row["AccountNumber"], row["IFSC"]) == (
            sb.BANK_ACCOUNT_HOLDER, hp.manifest["masters"]["bankAccount"]["bankName"], payment["maskedAccountNumber"], sb.BANK_IFSC)
        assert (money(row["NetPay"]), row["Currency"]) == (EXPECTED_AMOUNT, sb.CURRENCY)
        assert sb.BANK_ACCOUNT_NUMBER not in csv_text, "The export contains the full account number."

    with allure.step("BANKADV-EXP-006: a re-export returns the same file and changes nothing"):
        status, again, response = hp.write("GET", f"{base}/export", target_id=batch_id)
        assert status == 200, f"{status}: {response.text[:300]}"
        assert again == csv_text
        status, after, response = hp.read(base)
        assert status == 200, f"{status}: {response.text[:300]}"
        assert (after["status"], after["exportedAtUtc"]) == ("Exported", exported["exportedAtUtc"])
        assert len(after["history"]) == len(exported["history"])

    with allure.step("BANKADV-CANCEL-001: an Exported batch can't be cancelled (409)"):
        status, data, response = hp.write("POST", f"{base}/cancel", target_id=batch_id, json_body={"reason": f"{sb.PREFIX} e2e negative check"})
        if status in (200, 201):
            hp.record_unexpected("cancellation of an Exported batch", status, data)
        assert status == 409, f"Expected 409, got {status}: {response.text[:400]}"
        assert "This bank advice batch cannot be cancelled." in response.text

    with allure.step("BANKADV-AUDIT-001/003: one history row per transition, newest first"):
        attach_json("bank-advice history", after["history"])
        assert [h["changeType"] for h in after["history"]] == ["Exported", "Approved", "Prepared", "Generated"]

    with allure.step("The payroll run is still Approved: Bank Advice doesn't move the run"):
        status, run, response = hp.read(f"/api/payroll/runs/{hp.run_id}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert run["status"] == "Approved"

    hp.passed.add("bank_advice_exported")


# --- Stage 10: GL journal ---


@allure.title("GLACC-GEN-004/009/011/012/013/014/016, GLACC-RPT-005: the GL journal is Generated and balanced, Debit = Credit = 30000.00")
@qa_cases("GLACC-GEN-004", "GLACC-GEN-009", "GLACC-GEN-011", "GLACC-GEN-012", "GLACC-GEN-013", "GLACC-GEN-014",
          "GLACC-GEN-016", "GLACC-RPT-005")
def test_10_gl_journal_generated(hp):
    hp.require("approved")
    run_id = hp.run_id
    gl = hp.manifest["masters"]["gl"]
    before = results_fingerprint(hp.current_results())

    with allure.step("POST /api/payroll/runs/{id}/accounting/generate"):
        status, journal, response = hp.write("POST", f"/api/payroll/runs/{run_id}/accounting/generate", target_id=run_id)
        assert status in (200, 201), f"{status}: {response.text[:600]}"
        hp.cycle["glJournalId"] = journal["id"]
        hp.mark("gl_journal_generated", {
            "id": journal["id"], "status": journal["status"], "totalDebit": journal["totalDebit"], "totalCredit": journal["totalCredit"],
            "lines": [{k: line.get(k) for k in ("accountCode", "debit", "credit", "description", "sourceType")} for line in journal["lines"]]})
        attach_json("gl-journal (sandbox run)", journal)

    with allure.step("GLACC-GEN-012/014: Generated, and Debit = Credit = 30000.00"):
        assert journal["status"] == "Generated"
        assert (money(journal["totalDebit"]), money(journal["totalCredit"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT)
        assert sum(money(l["debit"]) for l in journal["lines"]) == sum(money(l["credit"]) for l in journal["lines"]) == EXPECTED_AMOUNT

    with allure.step("GLACC-GEN-013: journal PJ/{PayDate:yyyyMM}/{RunId:N}/1, dated the pay date, INR, for this run"):
        assert journal["journalNumber"] == f"PJ/{hp.pay_date:%Y%m}/{run_id.replace('-', '')}/1", journal["journalNumber"]
        assert (journal["payrollRunId"], journal["payrollPeriodId"], journal["journalDate"], journal["currencyCode"]) == (
            run_id, hp.cycle["payrollPeriodId"], iso(hp.pay_date), sb.CURRENCY)

    with allure.step("GLACC-GEN-009/011: one earning debit to the expense account, one net-payable credit to the payable account"):
        debits = [l for l in journal["lines"] if money(l["debit"]) > 0]
        credits = [l for l in journal["lines"] if money(l["credit"]) > 0]
        assert len(journal["lines"]) == 2
        assert [(l["accountCode"], money(l["debit"]), l["sourceType"], l["description"]) for l in debits] == [
            (gl["expenseAccount"]["code"], EXPECTED_AMOUNT, "PayrollResultComponent", sb.COMPONENT_CODE)]
        assert [(l["accountCode"], money(l["credit"]), l["sourceType"]) for l in credits] == [
            (gl["payableAccount"]["code"], EXPECTED_AMOUNT, "PayrollResult")]

    with allure.step("GLACC-RPT-005: GET by id returns the same journal and every line"):
        status, fetched, response = hp.read(f"/api/payroll/accounting/{journal['id']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert (fetched["journalNumber"], fetched["status"], money(fetched["totalDebit"]), money(fetched["totalCredit"])) == (
            journal["journalNumber"], "Generated", EXPECTED_AMOUNT, EXPECTED_AMOUNT)
        assert sorted(l["id"] for l in fetched["lines"]) == sorted(l["id"] for l in journal["lines"])

    with allure.step("GLACC-GEN-004: a second generate while this journal is active is refused (409)"):
        status, data, response = hp.write("POST", f"/api/payroll/runs/{run_id}/accounting/generate", target_id=run_id)
        if status in (200, 201):
            hp.record_unexpected("second GL journal", status, data)
        assert status == 409, f"Expected 409, got {status}: {response.text[:400]}"

    with allure.step("GLACC-GEN-016: payroll results are unchanged by generation"):
        assert results_fingerprint(hp.current_results()) == before

    if all(step in hp.cycle.get("steps", {}) for step in sb.CYCLE_STEPS):
        hp.cycle["state"] = "complete"
        hp.save()
    hp.passed.add("gl_journal")


# --- Stage 11: analytics read smoke ---


@allure.title("PYA-OVW-001/003/004/007: analytics overview, control totals and run summary read back the sandbox run's totals")
@qa_cases("PYA-OVW-001", "PYA-OVW-003", "PYA-OVW-004", "PYA-OVW-007")
def test_11_payroll_analytics_read_smoke(hp):
    hp.require("recalculated")
    run_id = hp.run_id

    with allure.step("PYA-OVW-001: overview has 1 employee, gross 30000.00, deductions 0.00, net 30000.00"):
        status, overview, response = hp.read(f"/api/payroll/analytics/runs/{run_id}/overview")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("analytics-overview", overview)
        assert (overview["payrollRunId"], overview["employeeCount"]) == (run_id, 1)
        assert (money(overview["grossTotal"]), money(overview["deductionTotal"]), money(overview["netPayTotal"])) == (
            EXPECTED_AMOUNT, ZERO, EXPECTED_AMOUNT)
        if "approved" in hp.passed:
            assert overview["status"] == "Approved"

    with allure.step("PYA-OVW-004: bank advice and accounting totals come from the active batch and journal"):
        if "bank_advice" in hp.passed:
            # The active (non-cancelled) batch's total, which counts valid payments only.
            assert money(overview["bankAdviceTotal"]) == hp.facts["bank_advice_total"] == EXPECTED_AMOUNT
        else:
            allure.attach("Not asserted: the bank_advice stage did not pass.", name="bankAdviceTotal", attachment_type=allure.attachment_type.TEXT)
        if "gl_journal" in hp.passed:
            assert (money(overview["accountingDebitTotal"]), money(overview["accountingCreditTotal"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT)
        else:
            allure.attach("Not asserted: the gl_journal stage did not pass.", name="accountingTotals", attachment_type=allure.attachment_type.TEXT)

    with allure.step("PYA-OVW-003: control totals count only the current result (the superseded version is excluded)"):
        status, totals, response = hp.read(f"/api/payroll/analytics/runs/{run_id}/control-totals")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("analytics-control-totals", totals)
        assert (totals["employeeCount"], totals["resultCount"]) == (1, 1)
        assert (money(totals["earningsTotal"]), money(totals["grossTotal"]), money(totals["deductionTotal"]), money(totals["netPayTotal"])) == (
            EXPECTED_AMOUNT, EXPECTED_AMOUNT, ZERO, EXPECTED_AMOUNT)

    with allure.step("PYA-OVW-007: run summary carries run type, status, the same control totals, and non-null evidence totals"):
        status, summary, response = hp.read(f"/api/payroll/analytics/runs/{run_id}/summary")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("analytics-run-summary", summary)
        assert (summary["payrollRunId"], summary["payrollPeriodId"], summary["runType"]) == (run_id, hp.cycle["payrollPeriodId"], "Regular")
        assert summary["totals"] == totals
        if "bank_advice" in hp.passed:
            assert summary.get("bankAdviceTotal") is not None and money(summary["bankAdviceTotal"]) == EXPECTED_AMOUNT
        if "gl_journal" in hp.passed:
            assert summary.get("accountingDebitTotal") is not None and summary.get("accountingCreditTotal") is not None
            assert (money(summary["accountingDebitTotal"]), money(summary["accountingCreditTotal"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT)

    hp.passed.add("analytics")


# --- Stage 12: isolation ---


@allure.title("Isolation (plan I4): real employees, payroll, attendance and bank advice data are unchanged by this session")
@allure.tag("SANDBOX-I4")
def test_12_real_tenant_data_unchanged(hp):
    after = sb.fingerprint(hp.api)
    attach_json("fingerprint (non-sandbox rows: count + hash)", {"before": hp.fingerprint_before, "after": after})
    if hp.cycle is not None:
        attach_json(f"manifest cycle {hp.key}", hp.cycle)
    assert after == hp.fingerprint_before, "Non-sandbox data changed during this session."
