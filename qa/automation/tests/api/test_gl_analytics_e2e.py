"""GL journal lifecycle + Payroll Analytics E2E on the QAAUTO payroll sandbox (ANEVRA01, DEV/TEST only).

One sandbox month per session, through the public API as the Phase 6 QA Admin:

    sandbox month complete (run Approved, bank advice Exported, GL journal Generated) ->
    journal balanced -> Validated -> Approved -> export (Approved) -> Posted -> export (Posted) ->
    regeneration/cancel/reversal/history gaps as they are today ->
    analytics totals vs Payroll, Bank Advice and GL -> dimension drill-down -> variance filters ->
    pre/post reconciliation (clean run) -> reconciliation finding on a sandbox mismatch ->
    real-data isolation check

Safety and idempotency (same model as test_payroll_happy_path_e2e.py, which it reuses):
- Preflight guards G1-G10 run before the first write. Every write goes through `sb.Api.write`, which
  refuses unless the Host is the pinned tenant host and the target is an id in the manifest.
- The month comes from `sb.run_cycle`: it resumes an incomplete sandbox month, or runs the next free
  one. The GL lifecycle only ever moves a journal this session's month generated. Earlier months keep
  their Generated journals as evidence and are never transitioned here.
- The cycle records `glLifecycle` (origin, state) and one manifest step per transition. A later
  session resumes an `open` lifecycle only while its journal is still Generated; otherwise it marks
  it `abandoned:<status>` (kept as evidence) and uses a fresh month. A journal is never re-transitioned.
- The mismatch-reconciliation stage writes at most one reconciliation to an earlier sandbox run whose
  bank advice total differs from its net pay, records its id in that cycle's `analyticsEvidence`, and
  on reruns only reads it back.
- GL export is a stateless GET (CR-276), so it is read, not written.

Evidence attached to Allure is sandbox-only or aggregate. Raw lists that could carry real employees'
names are never attached. Known product defects are tagged (`cr_refs`) where a stage pins today's
behavior; their correct-contract checks are in test_gl_analytics_known_defects.py.

Writes need QA_PAYROLL_SANDBOX=1 and QA_PAYROLL_SANDBOX_TENANT=ANEVRA01; without them the module
skips. Stages share state and run in file order, so run this module serially.

Design and cleanup: docs/qa/payroll-sandbox-setup.md (§5.2 for this suite).
"""

from __future__ import annotations

import csv
import io
import os
import uuid
from dataclasses import dataclass, field
from decimal import Decimal

import allure
import pytest

from sandbox import payroll_sandbox as sb
from utils.allure_evidence import attach_json, cr_refs, qa_cases

pytestmark = [
    allure.feature("Payroll"),
    allure.story("GL lifecycle + Analytics E2E (QAAUTO sandbox)"),
    pytest.mark.api,
    pytest.mark.e2e,
    pytest.mark.xdist_group("payroll-gl-analytics-e2e"),
]

ORIGIN = "pytest-gl-analytics"
ANY_STATUS = range(100, 600)
EXPECTED_AMOUNT = Decimal("30000.00")
ZERO = Decimal("0.00")
BOM = b"\xef\xbb\xbf"
GL_CSV_HEADER = ["JournalNumber", "JournalDate", "AccountCode", "AccountName", "Description", "Debit", "Credit", "Currency",
                 "SourceType", "SourceId"]
VARIANCE_CSV_HEADER = ["EmployeeCode", "EmployeeName", "PreviousGross", "CurrentGross", "GrossDelta", "GrossVariancePercent",
                       "PreviousDeductions", "CurrentDeductions", "DeductionDelta", "DeductionVariancePercent", "PreviousNet",
                       "CurrentNet", "NetDelta", "NetVariancePercent", "Classification"]
FINDINGS_CSV_HEADER = ["ControlCode", "Scope", "Severity", "Action", "Metric", "EmployeeId", "ExpectedValue", "ActualValue",
                       "Difference", "VariancePercent", "Message", "Status", "GeneratedAtUtc", "ResolutionNote", "ResolutionReference"]
BANK_FINDINGS = {"MissingBankAdvice", "BankAdviceAmountMismatch", "BankAdviceCountMismatch", "DuplicateBankPaymentReference"}
GL_FINDINGS = {"MissingPayrollJournal", "AccountingUnbalanced", "DuplicatePayrollJournal", "AccountingPayrollTotalMismatch"}
GL_STEPS = ["gl_journal_validated", "gl_journal_approved", "gl_journal_exported_approved", "gl_journal_posted",
            "gl_journal_exported_posted"]


def money(value) -> Decimal:
    return Decimal(str(value)).quantize(Decimal("0.01"))


def transition_error(current: str, target: str) -> str:
    return f"Invalid journal transition from {current} to {target}."


@dataclass
class GlSession:
    api: sb.Api
    claims: dict
    preflight: dict
    fingerprint_before: dict
    passed: set = field(default_factory=set)
    facts: dict = field(default_factory=dict)
    key: str | None = None
    cycle: dict | None = None

    @property
    def manifest(self) -> dict:
        return self.api.manifest

    @property
    def employee_id(self) -> str:
        return self.manifest["masters"]["employee"]["id"]

    @property
    def run_id(self) -> str:
        return self.cycle["payrollRunId"]

    @property
    def journal_id(self) -> str:
        return self.cycle["glJournalId"]

    @property
    def journal_path(self) -> str:
        return f"/api/payroll/accounting/{self.journal_id}"

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

    def journal(self) -> dict:
        status, data, response = self.read(self.journal_path)
        assert status == 200, f"GET journal returned {status}: {response.text[:400]}"
        return data

    def current_results(self, run_id: str | None = None) -> list:
        status, data, response = self.read(f"/api/payroll/runs/{run_id or self.run_id}/results", pageSize=100)
        assert status == 200, f"GET results returned {status}: {response.text[:400]}"
        return data["items"]

    def refused(self, method: str, path: str, target_id: str, what: str, message: str) -> None:
        status, data, response = self.write(method, path, target_id=target_id)
        if status in (200, 201):
            self.record_unexpected(what, status, data)
        assert status == 409, f"{what}: expected 409, got {status}: {response.text[:400]}"
        assert message in response.text, f"{what}: {response.text[:400]}"


def api_message(response) -> str | None:
    try:
        return response.json().get("message")
    except ValueError:
        return None


def journal_digest(journal: dict) -> dict:
    return {"id": journal["id"], "status": journal["status"], "journalNumber": journal["journalNumber"],
            "totalDebit": journal["totalDebit"], "totalCredit": journal["totalCredit"],
            "lines": [{k: line.get(k) for k in ("sequence", "accountCode", "debit", "credit", "description", "sourceType")}
                      for line in journal["lines"]]}


def to_microsecond(stamp: str) -> str:
    """'2026-09-29T15:40:32.8647444Z' -> '2026-09-29T15:40:32.864744'."""
    head, _, fraction = stamp.rstrip("Z").partition(".")
    return f"{head}.{(fraction + '000000')[:6]}"


def without_timestamps(recon: dict) -> dict:
    return {**{k: v for k, v in recon.items() if k != "generatedAtUtc"},
            "findings": [{k: v for k, v in f.items() if k != "generatedAtUtc"} for f in recon["findings"]]}


def csv_rows(response, *, bom: bool) -> list[list[str]]:
    if bom:
        assert response.content.startswith(BOM), "Expected a UTF-8 BOM at the start of the CSV."
    return list(csv.reader(io.StringIO(response.content.decode("utf-8-sig"))))


@pytest.fixture(scope="module")
def gl(settings):
    if os.environ.get("QA_PAYROLL_SANDBOX") != "1" or os.environ.get("QA_PAYROLL_SANDBOX_TENANT") != sb.TENANT_CODE:
        pytest.skip("GL + Analytics E2E writes to the QAAUTO sandbox; set QA_PAYROLL_SANDBOX=1 and "
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
    context = GlSession(api=api, claims=claims, preflight=report, fingerprint_before=before)

    yield context

    api.writes_enabled = False
    if context.cycle is not None:
        context.cycle.setdefault("glLifecycle", {}).update({"finishedAtUtc": sb.now_utc(), "stagesPassed": sorted(context.passed)})
        sb.save_manifest(manifest)


def claim_cycle(gl: GlSession) -> tuple[str, str]:
    """Resume this suite's open lifecycle while its journal is still Generated; otherwise complete
    (or start) a sandbox month with `sb.run_cycle` and claim it."""
    cycles = gl.manifest.setdefault("cycles", {})
    for key in sorted(k for k, c in cycles.items() if c.get("glLifecycle", {}).get("state") == "open"):
        cycle = cycles[key]
        status, journal, _ = gl.read(f"/api/payroll/accounting/{cycle.get('glJournalId')}")
        if cycle.get("state") == "complete" and status == 200 and journal["status"] == "Generated":
            return key, "resumed"
        cycle["glLifecycle"]["state"] = f"abandoned:{journal['status'] if status == 200 else status}"
        gl.save()

    try:
        outcome = sb.run_cycle(gl.api, gl.preflight["selfApprovalBlocked"], new_month=False)
    except sb.SandboxAbort as exc:
        if str(exc).startswith("B3"):
            pytest.skip(f"{exc} The GL lifecycle needs an Approved run.")
        pytest.fail(f"The sandbox cycle stopped fail-closed: {exc}", pytrace=False)
    key = outcome["cycle"]
    cycles[key].setdefault("origin", ORIGIN)
    cycles[key]["glLifecycle"] = {"origin": ORIGIN, "state": "open", "startedAtUtc": sb.now_utc()}
    gl.save()
    return key, "new"


# --- Stage 1: a complete sandbox month ---


@allure.title("Sandbox month ready: preflight passes, masters exist, and one month is complete through a Generated GL journal")
@qa_cases("GLACC-GEN-003")
def test_01_sandbox_month_ready(gl):
    with allure.step(f"Preflight guards G1-G10 passed before any write ({len(gl.preflight['warnings'])} warning(s))"):
        attach_json("preflight", gl.preflight)

    with allure.step("Seed the sandbox masters (find-or-create; a re-run creates nothing)"):
        try:
            outcome = sb.seed(gl.api)
        except sb.SandboxAbort as exc:
            pytest.fail(f"Seed stopped fail-closed: {exc}", pytrace=False)
        attach_json("seed-outcome", {"created": outcome["created"], "bankAccount": outcome["bankAccount"]})
        assert outcome["bankAccount"] == "present", outcome["bankAccount"]

    with allure.step("Claim a sandbox month: resume this suite's open lifecycle, or complete the next month"):
        gl.key, how = claim_cycle(gl)
        gl.cycle = gl.manifest["cycles"][gl.key]
        allure.dynamic.parameter("sandboxMonth", gl.key)
        attach_json(f"claimed cycle {gl.key} ({how})", {k: v for k, v in gl.cycle.items() if k != "steps"})
        missing = [s for s in sb.CYCLE_STEPS if s not in gl.cycle.get("steps", {})]
        assert gl.cycle.get("state") == "complete" and not missing, f"Cycle {gl.key} is incomplete: {missing}"

    with allure.step("GLACC-GEN-003 precondition: the run is Approved and its bank advice batch is Exported"):
        status, run, response = gl.read(f"/api/payroll/runs/{gl.run_id}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert run["status"] == "Approved", run["status"]
        status, batch, response = gl.read(f"/api/payroll/bank-advice/{gl.cycle['bankAdviceBatchId']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert batch["status"] == "Exported", batch["status"]

    with allure.step("The month's GL journal exists and is still Generated (no lifecycle step taken yet)"):
        journal = gl.journal()
        assert journal["status"] == "Generated", journal["status"]
        assert not any(s in gl.cycle.get("steps", {}) for s in GL_STEPS), "A lifecycle step is recorded for a Generated journal."

    earlier = sorted(k for k, c in gl.manifest["cycles"].items()
                     if k < gl.key and c.get("state") == "complete" and c.get("payrollRunId"))
    gl.facts["previous_run_id"] = gl.manifest["cycles"][earlier[-1]]["payrollRunId"] if earlier else None
    gl.facts["previous_key"] = earlier[-1] if earlier else None
    gl.passed.add("month_ready")


# --- Stage 2: the generated journal ---


@allure.title("GLACC-GEN-004/005/009/011/012/013/014, GLACC-REV-006, GLACC-EXP-001, GLACC-LC-003/012: the Generated journal is balanced, "
              "traces to the current result, and can't skip Validate")
@qa_cases("GLACC-GEN-004", "GLACC-GEN-005", "GLACC-GEN-009", "GLACC-GEN-011", "GLACC-GEN-012", "GLACC-GEN-013", "GLACC-GEN-014",
          "GLACC-REV-006", "GLACC-EXP-001", "GLACC-LC-003", "GLACC-LC-012", "GLACC-RPT-005")
@cr_refs("CR-279")
def test_02_journal_generated_and_balanced(gl):
    gl.require("month_ready")
    journal = gl.journal()
    gl_masters = gl.manifest["masters"]["gl"]
    attach_json("gl-journal (sandbox run)", journal)

    with allure.step("GLACC-GEN-014 / GLACC-REV-006: status is Generated (never observed as Draft)"):
        assert journal["status"] == "Generated"

    with allure.step("GLACC-GEN-012: Debit = Credit = 30000.00, and the lines add up to the header totals"):
        assert (money(journal["totalDebit"]), money(journal["totalCredit"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT)
        assert sum(money(l["debit"]) for l in journal["lines"]) == money(journal["totalDebit"])
        assert sum(money(l["credit"]) for l in journal["lines"]) == money(journal["totalCredit"])
        for line in journal["lines"]:
            assert (money(line["debit"]) > 0) != (money(line["credit"]) > 0), f"Line {line['sequence']} is not single-sided: {line}"

    with allure.step("GLACC-GEN-013: journal number PJ/{PayDate:yyyyMM}/{RunId:N}/1, dated the pay date, INR, for this run and period"):
        status, period, response = gl.read(f"/api/payroll/periods/{gl.cycle['payrollPeriodId']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert period["code"] == gl.cycle["steps"]["payroll_period_created"]["detail"]["code"]
        pay = period["payDate"]
        assert journal["journalNumber"] == f"PJ/{pay[:4]}{pay[5:7]}/{gl.run_id.replace('-', '')}/1", journal["journalNumber"]
        assert (journal["payrollRunId"], journal["payrollPeriodId"], journal["journalDate"], journal["currencyCode"]) == (
            gl.run_id, gl.cycle["payrollPeriodId"], pay, sb.CURRENCY)

    rows = gl.current_results()
    assert [r["employeeId"] for r in rows] == [gl.employee_id]
    result = rows[0]
    superseded = gl.cycle["steps"]["recalculated"]["detail"].get("supersededResultId")
    with allure.step("GLACC-GEN-005/009/011: one earning debit per current component, one net-payable credit for the current result"):
        debits = [l for l in journal["lines"] if money(l["debit"]) > 0]
        credits = [l for l in journal["lines"] if money(l["credit"]) > 0]
        components = [c for c in result["components"] if money(c["calculatedAmount"]) != 0]
        assert [(l["accountCode"], l["sourceType"], l["sourceId"], money(l["debit"])) for l in debits] == [
            (gl_masters["expenseAccount"]["code"], "PayrollResultComponent", c["id"], money(c["calculatedAmount"])) for c in components]
        assert [(l["accountCode"], l["sourceType"], l["sourceId"], money(l["credit"])) for l in credits] == [
            (gl_masters["payableAccount"]["code"], "PayrollResult", result["id"], money(result["netPay"]))]
        assert superseded not in {l["sourceId"] for l in journal["lines"]}, "A line references the superseded result."

    with allure.step("GLACC-GEN-004: a second generate while this journal is active is refused (409)"):
        gl.refused("POST", f"/api/payroll/runs/{gl.run_id}/accounting/generate", gl.run_id, "second GL journal",
                   "An active journal already exists for this payroll run.")

    with allure.step("GLACC-EXP-001: export of a Generated journal is refused (409)"):
        status, _, response = gl.read(f"{gl.journal_path}/export")
        assert status == 409, f"Expected 409, got {status}: {response.text[:300]}"
        assert "Only an approved or posted journal can be exported." in response.text

    with allure.step("GLACC-LC-003 / GLACC-LC-012: approve and post skipping Validate are refused (409)"):
        gl.refused("POST", f"{gl.journal_path}/approve", gl.journal_id, "approve a Generated journal", transition_error("Generated", "Approved"))
        gl.refused("POST", f"{gl.journal_path}/post", gl.journal_id, "post a Generated journal", transition_error("Generated", "Posted"))
        assert gl.journal()["status"] == "Generated"

    gl.facts["generated_journal"] = journal
    gl.facts["current_result"] = result
    gl.passed.add("journal_generated")


# --- Stage 3: validate ---


@allure.title("GLACC-LC-001/002/008/011, GLACC-CONC-005, GLACC-EXP-001: Validate moves Generated -> Validated; repeat, post and export are refused")
@qa_cases("GLACC-LC-001", "GLACC-LC-002", "GLACC-LC-008", "GLACC-LC-011", "GLACC-CONC-005", "GLACC-EXP-001")
@cr_refs("CR-280")
def test_03_validate(gl):
    gl.require("journal_generated")
    before = gl.facts["generated_journal"]

    with allure.step("POST validate: 200, 'Payroll journal validated.', status Validated; totals and lines unchanged"):
        status, journal, response = gl.write("POST", f"{gl.journal_path}/validate", target_id=gl.journal_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("journal after validate", journal_digest(journal))
        assert journal["status"] == "Validated"
        assert api_message(response) == "Payroll journal validated.", api_message(response)
        assert (journal["totalDebit"], journal["totalCredit"]) == (before["totalDebit"], before["totalCredit"])
        assert [l["id"] for l in journal["lines"]] == [l["id"] for l in before["lines"]]
        gl.mark("gl_journal_validated", {"status": journal["status"], "message": api_message(response)})

    with allure.step("GLACC-CONC-005: the response matches a fresh read"):
        assert journal_digest(gl.journal()) == journal_digest(journal)

    with allure.step("GLACC-LC-001: a second validate is refused (409)"):
        gl.refused("POST", f"{gl.journal_path}/validate", gl.journal_id, "second validate", transition_error("Validated", "Validated"))
    with allure.step("GLACC-LC-008: post from Validated is refused (409)"):
        gl.refused("POST", f"{gl.journal_path}/post", gl.journal_id, "post a Validated journal", transition_error("Validated", "Posted"))
    with allure.step("GLACC-EXP-001: export of a Validated journal is refused (409)"):
        status, _, response = gl.read(f"{gl.journal_path}/export")
        assert status == 409, f"Expected 409, got {status}: {response.text[:300]}"
        assert "Only an approved or posted journal can be exported." in response.text

    gl.passed.add("validated")


# --- Stage 4: approve ---


@allure.title("GLACC-LC-006/011/012: Approve moves Validated -> Approved; a repeat and a backward validate are refused")
@allure.description("ApprovedAtUtc and ConcurrencyVersion are not in PayrollJournalDto, so GLACC-LC-006 is checked through the status "
                    "only. Approval by the generating user is allowed because no payroll control row exists (preflight G8).")
@qa_cases("GLACC-LC-006", "GLACC-LC-011", "GLACC-LC-012")
def test_04_approve(gl):
    gl.require("validated")
    if gl.preflight["selfApprovalBlocked"]:
        pytest.skip("B3: a persisted payroll control row blocks self-approval; approving the journal needs a second "
                    "payroll-capable identity. The journal is left Validated.")

    with allure.step("POST approve: 200, 'Payroll journal approved.', status Approved"):
        status, journal, response = gl.write("POST", f"{gl.journal_path}/approve", target_id=gl.journal_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("journal after approve", journal_digest(journal))
        assert journal["status"] == "Approved"
        assert api_message(response) == "Payroll journal approved.", api_message(response)
        assert gl.journal()["status"] == "Approved"
        gl.mark("gl_journal_approved", {"status": journal["status"], "message": api_message(response)})
        attach_json("maker-checker evidence (GL)", {"approvedByUserId": gl.claims.get("sub"),
                                                    "controlsRowPersisted": gl.preflight["checks"]["G8_maker_checker"]["persistedRow"]})

    with allure.step("GLACC-LC-012: a second approve and a backward validate are refused (409)"):
        gl.refused("POST", f"{gl.journal_path}/approve", gl.journal_id, "second approve", transition_error("Approved", "Approved"))
        gl.refused("POST", f"{gl.journal_path}/validate", gl.journal_id, "validate an Approved journal", transition_error("Approved", "Validated"))

    gl.passed.add("approved")


# --- Stage 5: export while Approved ---


@allure.title("GLACC-EXP-001/002/005, GLACC-REV-005: an Approved journal exports as CSV; the export changes nothing")
@qa_cases("GLACC-EXP-001", "GLACC-EXP-002", "GLACC-EXP-005", "GLACC-REV-005")
@cr_refs("CR-276")
def test_05_export_while_approved(gl):
    gl.require("approved")
    journal = gl.journal()

    with allure.step("GLACC-EXP-005: GET export is 200 text/csv; charset=utf-8, file payroll-journal-{number}.csv"):
        status, _, response = gl.read(f"{gl.journal_path}/export")
        assert status == 200, f"{status}: {response.text[:600]}"
        assert response.headers.get("Content-Type") == "text/csv; charset=utf-8", response.headers.get("Content-Type")
        expected_name = f"payroll-journal-{journal['journalNumber'].replace('/', '-')}.csv"
        assert f"filename={expected_name}" in response.headers.get("Content-Disposition", ""), response.headers.get("Content-Disposition")
        allure.attach(response.content, name="gl export while Approved (sandbox journal)", attachment_type=allure.attachment_type.CSV)

    with allure.step("GLACC-EXP-002: exact 10-column header, then one row per line in sequence order, matching the journal"):
        rows = csv_rows(response, bom=False)
        assert rows[0] == GL_CSV_HEADER
        assert len(rows) == 1 + len(journal["lines"])
        for row, line in zip(rows[1:], sorted(journal["lines"], key=lambda l: l["sequence"])):
            record = dict(zip(GL_CSV_HEADER, row))
            assert (record["JournalNumber"], record["JournalDate"], record["AccountCode"], record["AccountName"], record["Description"],
                    record["Currency"], record["SourceType"], record["SourceId"]) == (
                journal["journalNumber"], journal["journalDate"], line["accountCode"], line["accountName"], line["description"],
                line["currencyCode"], line["sourceType"], line["sourceId"])
            assert (money(record["Debit"]), money(record["Credit"])) == (money(line["debit"]), money(line["credit"]))
        assert sum(money(r[5]) for r in rows[1:]) == sum(money(r[6]) for r in rows[1:]) == EXPECTED_AMOUNT

    with allure.step("GLACC-REV-005 / CR-276: the export is stateless; the journal is still Approved"):
        assert gl.journal()["status"] == "Approved"

    gl.facts["export_approved"] = response.content
    gl.mark("gl_journal_exported_approved", {"status": "Approved", "csvRows": len(rows) - 1, "bytes": len(response.content)})
    gl.passed.add("exported_approved")


# --- Stage 6: post ---


@allure.title("GLACC-LC-008/009/011/012: Post moves Approved -> Posted ('posted within HRMS'); nothing moves a Posted journal back")
@qa_cases("GLACC-LC-008", "GLACC-LC-009", "GLACC-LC-011", "GLACC-LC-012")
def test_06_post(gl):
    gl.require("approved")

    with allure.step("POST post: 200, 'Payroll journal posted within HRMS.', status Posted"):
        status, journal, response = gl.write("POST", f"{gl.journal_path}/post", target_id=gl.journal_id)
        assert status == 200, f"{status}: {response.text[:600]}"
        attach_json("journal after post", journal_digest(journal))
        assert journal["status"] == "Posted"
        assert api_message(response) == "Payroll journal posted within HRMS.", api_message(response)
        assert (money(journal["totalDebit"]), money(journal["totalCredit"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT)
        assert gl.journal()["status"] == "Posted"
        gl.mark("gl_journal_posted", {"status": journal["status"], "message": api_message(response)})

    with allure.step("GLACC-LC-012: post, validate and approve on a Posted journal are all refused (409)"):
        for action, target in (("post", "Posted"), ("validate", "Validated"), ("approve", "Approved")):
            gl.refused("POST", f"{gl.journal_path}/{action}", gl.journal_id, f"{action} a Posted journal", transition_error("Posted", target))
        assert gl.journal()["status"] == "Posted"

    gl.passed.add("posted")


# --- Stage 7: export while Posted ---


@allure.title("GLACC-EXP-004, GLACC-REV-004/005: a Posted journal exports byte-identical CSV three times and stays Posted (never Exported)")
@qa_cases("GLACC-EXP-004", "GLACC-REV-004", "GLACC-REV-005")
@cr_refs("CR-276")
def test_07_export_while_posted(gl):
    gl.require("posted", "exported_approved")
    exports = []
    with allure.step("GET export three times while Posted"):
        for _ in range(3):
            status, _, response = gl.read(f"{gl.journal_path}/export")
            assert status == 200, f"{status}: {response.text[:300]}"
            exports.append(response.content)
        allure.attach(exports[0], name="gl export while Posted (sandbox journal)", attachment_type=allure.attachment_type.CSV)

    with allure.step("GLACC-EXP-004: every export is byte-identical to the one taken while Approved"):
        assert exports == [gl.facts["export_approved"]] * 3

    with allure.step("GLACC-REV-004 / CR-276: the journal is still Posted; export never moves it to Exported"):
        journal = gl.journal()
        assert journal["status"] == "Posted", journal["status"]

    gl.mark("gl_journal_exported_posted", {"status": journal["status"], "exports": len(exports)})
    gl.cycle["glLifecycle"]["state"] = "complete"
    gl.save()
    gl.facts["posted_journal"] = journal
    gl.passed.add("gl_lifecycle")


# --- Stage 8: reversal, cancellation and history, as they are today ---


@allure.title("GLACC-REV-001/002, GLACC-HIST-003: a Posted journal can't be regenerated, cancelled or reversed, and its history isn't exposed")
@allure.description(
    "Pins today's behavior, which the Module 16 risk register calls defects: no cancel/reverse endpoint exists (CR-275), so a "
    "journal permanently blocks regeneration for its run; the journal DTO carries no history and no /history route exists "
    "(CR-278). The route probes use a random journal id, so they can never change data. The correct-contract checks are in "
    "test_gl_analytics_known_defects.py and fail until these are fixed.")
@qa_cases("GLACC-REV-001", "GLACC-REV-002", "GLACC-HIST-003")
@cr_refs("CR-275", "CR-278")
def test_08_regeneration_cancel_reversal_history_gaps(gl):
    gl.require("gl_lifecycle")

    with allure.step("GLACC-REV-002: regenerating for the run is refused (409) while the Posted journal exists, with no API remedy"):
        gl.refused("POST", f"/api/payroll/runs/{gl.run_id}/accounting/generate", gl.run_id, "regenerate after post",
                   "An active journal already exists for this payroll run.")

    probe = str(uuid.uuid4())
    with allure.step("GLACC-REV-001: no cancel or reverse route exists (bare 404, no API response body)"):
        outcomes = {}
        for action in ("cancel", "reverse"):
            response = gl.api.client.post(f"/api/payroll/accounting/{probe}/{action}")
            outcomes[action] = {"status": response.status_code, "contentType": response.headers.get("Content-Type"), "body": response.text[:120]}
        control = gl.api.client.post(f"/api/payroll/accounting/{probe}/validate")
        outcomes["validate (control: a route that exists)"] = {"status": control.status_code, "message": api_message(control)}
        attach_json("route probes (random journal id)", outcomes)
        assert control.status_code == 404 and api_message(control) == "Payroll journal not found."
        for action in ("cancel", "reverse"):
            assert outcomes[action]["status"] in (404, 405) and not outcomes[action]["body"], outcomes[action]

    with allure.step("GLACC-HIST-003: the journal DTO has no history field, and GET /{id}/history is not a route"):
        journal = gl.journal()
        assert not {"history", "histories"} & {k.lower() for k in journal}, sorted(journal)
        response = gl.api.client.get(f"{gl.journal_path}/history")
        assert response.status_code == 404 and not response.text, (response.status_code, response.text[:200])

    with allure.step("Observed lifecycle (the only history the API offers is what this suite recorded)"):
        steps = gl.cycle["steps"]
        observed = [("gl_journal_generated", "Generated")] + [(s, steps[s]["detail"]["status"]) for s in GL_STEPS if s in steps]
        attach_json("observed journal status per step", [{"step": s, "status": st, "atUtc": steps[s]["atUtc"]} for s, st in observed])
        assert [st for _, st in observed] == ["Generated", "Validated", "Approved", "Approved", "Posted", "Posted"]

    gl.passed.add("gl_gaps")


# --- Stage 9: analytics totals against Payroll, Bank Advice and GL ---


@allure.title("PYA-OVW-001/003/004/007/011, GLACC-REC-006: overview, control totals and run summary (JSON and CSV) match Payroll, "
              "Bank Advice and the Posted journal")
@qa_cases("PYA-OVW-001", "PYA-OVW-003", "PYA-OVW-004", "PYA-OVW-007", "PYA-OVW-011", "GLACC-REC-006")
def test_09_analytics_totals_match_sources(gl):
    gl.require("month_ready")
    run_id = gl.run_id

    with allure.step("Source totals: current payroll results, the Exported bank advice batch, and the GL journal"):
        results = gl.current_results()
        gross = sum(money(r["grossEarnings"]) for r in results)
        deductions = sum(money(r["totalDeductions"]) for r in results)
        net = sum(money(r["netPay"]) for r in results)
        employer = sum(money(r["employerContributions"]) for r in results)
        status, batch, response = gl.read(f"/api/payroll/bank-advice/{gl.cycle['bankAdviceBatchId']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        active_payments = [p for p in batch["payments"] if p["paymentStatus"] != "Cancelled"]
        journal = gl.journal()
        net_payable_credit = sum(money(l["credit"]) for l in journal["lines"] if l["sourceType"] == "PayrollResult")
        sources = {"results": {"count": len(results), "gross": gross, "deductions": deductions, "net": net, "employer": employer},
                   "bankAdvice": {"status": batch["status"], "totalEmployees": batch["totalEmployees"], "totalAmount": money(batch["totalAmount"]),
                                  "paymentsNet": sum(money(p["netPay"]) for p in active_payments)},
                   "journal": {"status": journal["status"], "totalDebit": money(journal["totalDebit"]),
                               "totalCredit": money(journal["totalCredit"]), "netPayableCredit": net_payable_credit}}
        attach_json("source totals (sandbox run)", sources)

    with allure.step("The sources agree: net = bank advice total = payments = journal net-payable credit; gross = journal debit"):
        assert (len(results), gross, deductions, net) == (1, EXPECTED_AMOUNT, ZERO, EXPECTED_AMOUNT)
        assert net == sources["bankAdvice"]["totalAmount"] == sources["bankAdvice"]["paymentsNet"] == net_payable_credit
        assert batch["totalEmployees"] == len(active_payments) == len(results)
        assert money(journal["totalDebit"]) == money(journal["totalCredit"]) == gross + employer

    with allure.step("PYA-OVW-001/003/004 / GLACC-REC-006: overview equals the sources"):
        status, overview, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/overview")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("analytics-overview", overview)
        assert (overview["payrollRunId"], overview["status"], overview["employeeCount"]) == (run_id, "Approved", len(results))
        assert (money(overview["grossTotal"]), money(overview["deductionTotal"]), money(overview["netPayTotal"]),
                money(overview["employerContributionTotal"])) == (gross, deductions, net, employer)
        assert money(overview["bankAdviceTotal"]) == sources["bankAdvice"]["totalAmount"]
        assert (money(overview["accountingDebitTotal"]), money(overview["accountingCreditTotal"])) == (
            money(journal["totalDebit"]), money(journal["totalCredit"]))
        assert (overview["anomalyCount"], overview["criticalFindings"]) == (0, 0)

    with allure.step("PYA-OVW-003: control totals count only the current result (the recalculation superseded one)"):
        status, totals, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/control-totals")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("analytics-control-totals", totals)
        assert (totals["employeeCount"], totals["resultCount"]) == (1, 1)
        assert (money(totals["earningsTotal"]), money(totals["grossTotal"]), money(totals["deductionTotal"]), money(totals["netPayTotal"])) == (
            gross, gross, deductions, net)

    with allure.step("PYA-OVW-007: run summary carries the same totals and the bank advice/accounting evidence totals"):
        status, summary, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/summary")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json("analytics-run-summary", summary)
        assert (summary["payrollRunId"], summary["payrollPeriodId"], summary["runType"], summary["status"]) == (
            run_id, gl.cycle["payrollPeriodId"], "Regular", "Approved")
        assert summary["totals"] == totals
        assert (money(summary["bankAdviceTotal"]), money(summary["accountingDebitTotal"]), money(summary["accountingCreditTotal"])) == (
            net, money(journal["totalDebit"]), money(journal["totalCredit"]))

    with allure.step("PYA-OVW-011: summary.csv and control-totals.csv are BOM + header + one row matching the JSON"):
        status, _, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/summary.csv")
        assert status == 200, f"{status}: {response.text[:300]}"
        rows = csv_rows(response, bom=True)
        allure.attach(response.content, name="summary.csv (sandbox run)", attachment_type=allure.attachment_type.CSV)
        assert len(rows) == 2, rows
        row = dict(zip(rows[0], rows[1]))
        assert (row["PayrollRunId"], row["RunType"], row["Status"], row["EmployeeCount"], row["ResultCount"]) == (
            run_id, "Regular", "Approved", "1", "1")
        assert (money(row["GrossTotal"]), money(row["NetPayTotal"]), money(row["BankAdviceTotal"]),
                money(row["AccountingDebitTotal"]), money(row["AccountingCreditTotal"])) == (
            gross, net, net, money(journal["totalDebit"]), money(journal["totalCredit"]))
        status, _, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/control-totals.csv")
        assert status == 200, f"{status}: {response.text[:300]}"
        rows = csv_rows(response, bom=True)
        assert len(rows) == 2, rows
        row = dict(zip(rows[0], rows[1]))
        assert (int(row["EmployeeCount"]), int(row["ResultCount"]), money(row["GrossTotal"]), money(row["NetPayTotal"])) == (
            totals["employeeCount"], totals["resultCount"], money(totals["grossTotal"]), money(totals["netPayTotal"]))

    gl.facts["net"] = net
    gl.passed.add("analytics_totals")


# --- Stage 10: dimension drill-down ---


@allure.title("PYA-DIM-004/006/007/008: department and cost-center drill-down put the sandbox employee in one UNCLASSIFIED row that sums to the run")
@qa_cases("PYA-DIM-004", "PYA-DIM-006", "PYA-DIM-007", "PYA-DIM-008")
def test_10_dimension_drilldown(gl):
    gl.require("analytics_totals")
    status, totals, response = gl.read(f"/api/payroll/analytics/runs/{gl.run_id}/control-totals")
    assert status == 200, f"{status}: {response.text[:300]}"

    for dimension in ("departments", "cost-centers"):
        with allure.step(f"GET {dimension}: one UNCLASSIFIED row (the sandbox employee has no {dimension[:-1]}); totals equal the run"):
            status, rows, response = gl.read(f"/api/payroll/analytics/runs/{gl.run_id}/{dimension}")
            assert status == 200, f"{status}: {response.text[:300]}"
            attach_json(f"analytics-{dimension}", rows)
            assert [(r["code"], r["name"]) for r in rows] == [("UNCLASSIFIED", "Historical dimension unavailable")]
            assert rows == sorted(rows, key=lambda r: r["code"])
            assert sum(r["employeeCount"] for r in rows) == totals["employeeCount"]
            for key in ("grossTotal", "deductionTotal", "netPayTotal"):
                assert sum(money(r[key]) for r in rows) == money(totals[key]), key
            assert all(money(r["earningsTotal"]) == money(r["grossTotal"]) for r in rows)

    gl.passed.add("dimensions")


# --- Stage 11: variance filters ---


def default_comparison(gl: GlSession) -> dict | None:
    """What GetVarianceAsync picks without compareRunId: the other non-Cancelled run of the same type
    with the latest period end date. Metadata only; non-sandbox results are never read."""
    periods = {p["id"]: p["endDate"] for p in gl.api.paged("/api/payroll/periods")}
    runs = [r for r in gl.api.paged("/api/payroll/runs") if r["id"] != gl.run_id and r["runType"] == "Regular" and r["status"] != "Cancelled"]
    if not runs:
        return None
    chosen = max(runs, key=lambda r: periods.get(r["payrollPeriodId"], ""))
    sandbox_runs = {c.get("payrollRunId") for c in gl.manifest["cycles"].values()}
    return {"id": chosen["id"], "runNumber": chosen["runNumber"], "status": chosen["status"],
            "periodEnd": periods.get(chosen["payrollPeriodId"]), "isSandbox": chosen["id"] in sandbox_runs}


@allure.title("PYA-VAR-001/002/011/013, PYA-CVAR-004: variance CSV filtered by compareRunId, and the default comparison run")
@allure.description(
    "The JSON variance list binds the abstract PagedQuery and returns 500 (F1), so the variance rows are read from variance.csv, "
    "which takes the same compareRunId filter. Without compareRunId the service picks the latest-ending other run, not the previous "
    "period (CR-302); in ANEVRA01 that is a far-future non-sandbox run with no results, so the sandbox row reads NewValue. "
    "Component variance amounts are checked in test_gl_analytics_known_defects.py (F6, F7).")
@qa_cases("PYA-VAR-001", "PYA-VAR-002", "PYA-VAR-011", "PYA-VAR-013", "PYA-CVAR-004")
@cr_refs("CR-302")
def test_11_variance_filters(gl):
    gl.require("analytics_totals")
    previous = gl.facts.get("previous_run_id")
    if previous is None:
        pytest.skip("No earlier complete sandbox month to compare against.")
    name = f"{sb.EMPLOYEE_FIRST_NAME} {sb.EMPLOYEE_LAST_NAME}"

    with allure.step(f"variance.csv?compareRunId=<sandbox {gl.facts['previous_key']}>: one row, same pay, delta 0, NormalComparable"):
        status, _, response = gl.read(f"/api/payroll/analytics/runs/{gl.run_id}/variance.csv", compareRunId=previous)
        assert status == 200, f"{status}: {response.text[:300]}"
        assert f"filename=payroll-variance-{gl.run_id.replace('-', '')}.csv" in response.headers.get("Content-Disposition", "")
        rows = csv_rows(response, bom=True)
        allure.attach(response.content, name="variance.csv vs previous sandbox month", attachment_type=allure.attachment_type.CSV)
        assert rows[0] == VARIANCE_CSV_HEADER
        assert len(rows) == 2, f"Expected one data row, got {len(rows) - 1}."
        row = dict(zip(rows[0], rows[1]))
        assert row["EmployeeName"] == name
        assert (money(row["PreviousGross"]), money(row["CurrentGross"]), money(row["GrossDelta"]), money(row["PreviousNet"]),
                money(row["CurrentNet"]), money(row["NetDelta"])) == (EXPECTED_AMOUNT, EXPECTED_AMOUNT, ZERO, EXPECTED_AMOUNT, EXPECTED_AMOUNT, ZERO)
        assert (Decimal(row["GrossVariancePercent"]), row["DeductionVariancePercent"], row["Classification"]) == (Decimal(0), "", "NormalComparable")

    with allure.step("PYA-VAR-002/013 / CR-302: without compareRunId the latest-ending other run is the comparison"):
        chosen = default_comparison(gl)
        attach_json("default comparison run (metadata only)", chosen)
        status, _, response = gl.read(f"/api/payroll/analytics/runs/{gl.run_id}/variance.csv")
        assert status == 200, f"{status}: {response.text[:300]}"
        rows = csv_rows(response, bom=True)
        assert len(rows) == 2, f"Expected one data row, got {len(rows) - 1}."
        row = dict(zip(rows[0], rows[1]))
        if chosen and chosen["isSandbox"]:
            assert (money(row["PreviousGross"]), row["Classification"]) == (EXPECTED_AMOUNT, "NormalComparable"), row
        else:
            assert (row["PreviousGross"], row["Classification"], money(row["GrossDelta"])) == ("", "NewValue", EXPECTED_AMOUNT), row

    with allure.step("PYA-CVAR-004: component drill-down answers with one row for the sandbox component, with or without compareRunId"):
        for params in ({"compareRunId": previous}, {}):
            status, components, response = gl.read(f"/api/payroll/analytics/runs/{gl.run_id}/components", **params)
            assert status == 200, f"{status}: {response.text[:300]}"
            attach_json(f"components {'vs previous sandbox month' if params else '(default comparison)'}", components)
            assert [c["componentCode"] for c in components] == [sb.COMPONENT_CODE]

    gl.passed.add("variance")


# --- Stage 12: reconciliation of the clean run ---


@allure.title("PYA-RECGEN-001/012, PYA-BANK-005, PYA-GL-007, PYA-VER-001, PYA-OVW-005/006: pre and post reconciliation of the clean "
              "sandbox run pass every check with no findings")
@qa_cases("PYA-RECGEN-001", "PYA-RECGEN-012", "PYA-BANK-005", "PYA-GL-007", "PYA-VER-001", "PYA-OVW-005", "PYA-OVW-006")
def test_12_reconciliation_clean_run(gl):
    gl.require("gl_lifecycle", "analytics_totals")
    run_id = gl.run_id
    generated = {}

    for kind, rec_type, step in (("pre", "PrePayroll", "reconciliation_pre"), ("post", "PostPayroll", "reconciliation_post")):
        prior = gl.cycle.get("steps", {}).get(step, {}).get("detail", {}).get("version", 0)
        with allure.step(f"POST /api/payroll/reconciliation/runs/{{id}}/{kind}: version {prior + 1}, Generated, no findings"):
            status, recon, response = gl.write("POST", f"/api/payroll/reconciliation/runs/{run_id}/{kind}", target_id=run_id)
            assert status == 200, f"{status}: {response.text[:600]}"
            attach_json(f"reconciliation {kind}", recon)
            gl.mark(step, {"id": recon["id"], "version": recon["version"], "status": recon["status"], "totalChecks": recon["totalChecks"],
                           "findings": [f["controlCode"] for f in recon["findings"]]})
            assert (recon["payrollRunId"], recon["type"], recon["status"]) == (run_id, rec_type, "Generated")
            assert recon["version"] == prior + 1, (prior, recon["version"])
            codes = {f["controlCode"] for f in recon["findings"]}
            assert not codes & (BANK_FINDINGS | GL_FINDINGS | {"PayrollEquationMismatch", "NoPayrollResults"}), codes
            assert recon["findings"] == [], codes
            assert (recon["failedChecks"], recon["warningChecks"], recon["criticalChecks"]) == (0, 0, 0)
            assert recon["totalChecks"] >= 1 and recon["passedChecks"] == recon["totalChecks"]
            generated[kind] = recon

    with allure.step("PYA-VER-001: GET by id returns the post reconciliation unchanged (timestamps compared to the microsecond)"):
        status, fetched, response = gl.read(f"/api/payroll/reconciliation/{generated['post']['id']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        # The POST echoes the in-memory DateTime (7 fractional digits); MySQL stores datetime(6).
        assert without_timestamps(fetched) == without_timestamps(generated["post"])
        assert to_microsecond(fetched["generatedAtUtc"]) == to_microsecond(generated["post"]["generatedAtUtc"])

    with allure.step("findings.csv for the run is a header with no rows; overview has 0 open/critical findings and 0 anomalies"):
        status, _, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/findings.csv")
        assert status == 200, f"{status}: {response.text[:300]}"
        rows = csv_rows(response, bom=True)
        assert rows == [FINDINGS_CSV_HEADER], rows[:3]
        status, overview, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/overview")
        assert status == 200, f"{status}: {response.text[:300]}"
        assert (overview["openFindings"], overview["criticalFindings"], overview["anomalyCount"]) == (0, 0, 0)

    gl.passed.add("reconciliation_clean")


# --- Stage 13: reconciliation of a sandbox run with a real mismatch ---


def mismatch_cycle(gl: GlSession) -> tuple[str, dict, dict] | None:
    """The earliest complete sandbox month whose active bank advice total differs from its net pay
    (the 1950-01..03 batches were generated before the salary account existed, so they total 0)."""
    for key, cycle in sorted(gl.manifest["cycles"].items()):
        if key == gl.key or cycle.get("state") != "complete" or not cycle.get("payrollRunId"):
            continue
        status, overview, _ = gl.read(f"/api/payroll/analytics/runs/{cycle['payrollRunId']}/overview")
        if status == 200 and money(overview["netPayTotal"]) > 0 and money(overview["bankAdviceTotal"]) != money(overview["netPayTotal"]):
            return key, cycle, overview
    return None


@allure.title("PYA-BANK-002, PYA-GL-007, PYA-OVW-005, PYA-VER-001, PYA-CSV-001, PYA-EXC-008: post reconciliation flags a sandbox run "
              "whose bank advice total differs from net pay")
@allure.description(
    "Uses an earlier sandbox month whose bank advice batch is Draft with one Invalid payment (total 0.00 against net pay 30000.00). "
    "The reconciliation is generated once and its id is kept in that cycle's analyticsEvidence; reruns read it back. The mismatch "
    "is a Finding only: no anomaly flag is raised for it (CR-298), and the exceptions register can't be listed anyway (F1).")
@qa_cases("PYA-BANK-002", "PYA-GL-007", "PYA-OVW-005", "PYA-VER-001", "PYA-CSV-001", "PYA-EXC-008")
@cr_refs("CR-298")
def test_13_reconciliation_flags_bank_advice_mismatch(gl):
    gl.require("month_ready")
    found = mismatch_cycle(gl)
    if found is None:
        pytest.skip("No sandbox month has a bank advice total different from its net pay.")
    key, cycle, overview = found
    run_id = cycle["payrollRunId"]
    evidence = cycle.setdefault("analyticsEvidence", {})
    allure.dynamic.parameter("evidenceMonth", key)

    with allure.step(f"Evidence month {key}: bank advice total {overview['bankAdviceTotal']} against net pay {overview['netPayTotal']}"):
        status, batch, response = gl.read(f"/api/payroll/bank-advice/{cycle['bankAdviceBatchId']}")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json(f"bank advice batch {key} (sandbox)", {"status": batch["status"], "totalAmount": batch["totalAmount"],
                                                          "payments": [{k: p.get(k) for k in ("validationStatus", "paymentStatus", "netPay")}
                                                                       for p in batch["payments"]]})
        active_payments = len([p for p in batch["payments"] if p["paymentStatus"] != "Cancelled"])

    if evidence.get("postReconciliationId"):
        with allure.step(f"Read back the reconciliation recorded earlier ({evidence['postReconciliationId']})"):
            status, recon, response = gl.read(f"/api/payroll/reconciliation/{evidence['postReconciliationId']}")
            assert status == 200, f"{status}: {response.text[:300]}"
    else:
        with allure.step("POST post reconciliation once and record its id in the cycle"):
            status, recon, response = gl.write("POST", f"/api/payroll/reconciliation/runs/{run_id}/post", target_id=run_id)
            assert status == 200, f"{status}: {response.text[:600]}"
            evidence.update({"postReconciliationId": recon["id"], "origin": ORIGIN, "atUtc": sb.now_utc(),
                             "findings": [f["controlCode"] for f in recon["findings"]]})
            gl.save()
    attach_json(f"reconciliation post {key}", recon)

    net, bank_total = money(overview["netPayTotal"]), money(overview["bankAdviceTotal"])
    with allure.step("PYA-BANK-002: one Critical BankAdviceAmountMismatch, expected = net pay, actual = bank advice total"):
        assert (recon["payrollRunId"], recon["type"], recon["status"]) == (run_id, "PostPayroll", "Generated")
        mismatches = [f for f in recon["findings"] if f["controlCode"] == "BankAdviceAmountMismatch"]
        assert len(mismatches) == 1, [f["controlCode"] for f in recon["findings"]]
        finding = mismatches[0]
        assert (finding["severity"], finding["action"], finding["metric"], finding["status"]) == (
            "Critical", "RequireAcknowledgement", "BankAdviceTotal", "Open")
        assert (money(finding["expectedValue"]), money(finding["actualValue"]), money(finding["difference"])) == (net, bank_total, bank_total - net)
        assert recon["criticalChecks"] >= 1

    with allure.step("No count mismatch while payments match results; PYA-GL-007: no accounting finding for the balanced, matching journal"):
        codes = [f["controlCode"] for f in recon["findings"]]
        assert ("BankAdviceCountMismatch" in codes) == (active_payments != overview["employeeCount"]), (codes, active_payments)
        assert not set(codes) & GL_FINDINGS, codes

    with allure.step("PYA-OVW-005 / PYA-EXC-008 / CR-298: overview counts the open critical finding; no anomaly flag is raised"):
        status, after, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/overview")
        assert status == 200, f"{status}: {response.text[:300]}"
        attach_json(f"analytics-overview {key}", after)
        assert after["openFindings"] >= 1 and after["criticalFindings"] >= 1, after
        assert after["anomalyCount"] == 0, after

    with allure.step("PYA-CSV-001: findings.csv drills down to the Open BankAdviceAmountMismatch row"):
        status, _, response = gl.read(f"/api/payroll/analytics/runs/{run_id}/findings.csv")
        assert status == 200, f"{status}: {response.text[:300]}"
        rows = csv_rows(response, bom=True)
        allure.attach(response.content, name=f"findings.csv {key} (sandbox run)", attachment_type=allure.attachment_type.CSV)
        assert rows[0] == FINDINGS_CSV_HEADER
        records = [dict(zip(rows[0], r)) for r in rows[1:]]
        assert any(r["ControlCode"] == "BankAdviceAmountMismatch" and r["Status"] == "Open" and r["Severity"] == "Critical" for r in records), records

    gl.passed.add("reconciliation_mismatch")


# --- Stage 14: isolation ---


@allure.title("Isolation (plan I4): real employees, payroll, attendance and bank advice data are unchanged by this session")
@allure.tag("SANDBOX-I4")
def test_14_real_tenant_data_unchanged(gl):
    after = sb.fingerprint(gl.api)
    attach_json("fingerprint (non-sandbox rows: count + hash)", {"before": gl.fingerprint_before, "after": after})
    if gl.cycle is not None:
        attach_json(f"manifest cycle {gl.key}", gl.cycle)
    assert after == gl.fingerprint_before, "Non-sandbox data changed during this session."
