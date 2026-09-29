"""QAAUTO Payroll sandbox for ANEVRA01: DEV/TEST-only prerequisite setup plus one happy-path cycle.

Design: docs/qa/payroll-automation-plan.md. How to run, what it creates, and cleanup:
docs/qa/payroll-sandbox-setup.md.

Every write goes through the public API as the Phase 6 QA Admin (TenantAdmin). Nothing here writes
to the database directly, and PayrollCalculationEngine validation is used exactly as it is: the
engine needs a Closed AttendancePeriod whose dates equal the payroll period's, so this tool creates
one inside a reserved deep-past window (1950-01..1959-12) that predates every real employee's date
of joining. The tenant-wide attendance sweep then contains only the sandbox employee.

Usage (from qa/automation, with the venv's python):
    python -m sandbox.payroll_sandbox preflight          read-only guards, no writes
    python -m sandbox.payroll_sandbox seed               find-or-create the QAAUTO-HP masters
    python -m sandbox.payroll_sandbox cycle              resume the open cycle, or start the next month
    python -m sandbox.payroll_sandbox status             manifest plus live state, read-only
    python -m sandbox.payroll_sandbox retire [--execute] L1 retirement (prints the plan unless --execute)

`seed`, `cycle` and `retire --execute` refuse to write unless QA_PAYROLL_SANDBOX=1 and
QA_PAYROLL_SANDBOX_TENANT=ANEVRA01 are set in the environment.
"""

from __future__ import annotations

import argparse
import base64
import calendar
import hashlib
import json
import os
import sys
from datetime import date, datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from dotenv import load_dotenv  # noqa: E402

load_dotenv(ROOT / ".env")

from config.settings import load_settings  # noqa: E402
from core.api_client import ApiClient  # noqa: E402

# --- Fixed identity of the sandbox. Changing any of these means a different sandbox. ---

TENANT_CODE = "ANEVRA01"
TENANT_HOST = "anevra01.localhost"
QA_ADMIN_EMAIL = "qaauto-admin@anevra01.qa-automation.invalid"
PREFIX = "QAAUTO-HP"

SANDBOX_START = date(1950, 1, 1)
SANDBOX_END = date(1959, 12, 31)

EMPLOYEE_FIRST_NAME = "QaAutoPayroll"
EMPLOYEE_LAST_NAME = f"{PREFIX}-01"
COMPONENT_CODE = f"{PREFIX}-BASIC"
STRUCTURE_CODE = f"{PREFIX}-STRUCT"
GL_EXPENSE_CODE = f"{PREFIX}-GL-EXP"
GL_PAYABLE_CODE = f"{PREFIX}-GL-PAY"
ACCOUNTING_CODE = f"{PREFIX}-ACCT"
MONTHLY_AMOUNT = 30000
CURRENCY = "INR"

# Salary bank account. The Bank master row has no public create API; it comes from the
# Development-only, opt-in DatabaseSeeder.SeedQaAutomationBankAsync (DevelopmentSeed:EnableQaAutomationBank).
# The account itself goes through the public employee bank-detail API. All values are fake.
BANK_CODE = "QAAUTO-BANK"
BANK_ACCOUNT_HOLDER = "QAAUTO HP Sandbox"
BANK_ACCOUNT_NUMBER = "999000000001"
BANK_IFSC = "QAAT0000001"
BANK_BRANCH = "QAAUTO Sandbox Branch"

# QA_SANDBOX_STATE_DIR lets a CI runner keep the one manifest outside its throwaway checkout. There
# must only ever be one manifest per tenant: a second, empty one fails preflight G7b/G7c.
STATE_DIR = Path(os.environ.get("QA_SANDBOX_STATE_DIR") or ROOT / ".state")
MANIFEST_PATH = STATE_DIR / "payroll-sandbox-manifest.json"
EMPTY_GUID = "00000000-0000-0000-0000-000000000000"

CYCLE_STEPS = [
    "payroll_period_created",
    "attendance_period_created",
    "attendance_processed",
    "attendance_closed",
    "run_created",
    "run_prepared",
    "calculated",
    "recalculated",
    "approved",
    "bank_advice_generated",
    "bank_advice_prepared",
    "bank_advice_approved",
    "bank_advice_exported",
    "gl_journal_generated",
]


class SandboxAbort(Exception):
    """A guard failed. Nothing after the failing check was written."""


def log(message: str) -> None:
    print(message, flush=True)


def iso(value: date) -> str:
    return value.isoformat()


def now_utc() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


# --- Manifest: the source of truth for which rows this sandbox owns. ---


def load_manifest() -> dict:
    if MANIFEST_PATH.exists():
        return json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    return {"schema": 1, "tenant": {}, "masters": {}, "cycles": {}}


def save_manifest(manifest: dict) -> None:
    STATE_DIR.mkdir(parents=True, exist_ok=True)
    manifest["updatedAtUtc"] = now_utc()
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2, sort_keys=True), encoding="utf-8")


CYCLE_OWNED_KEYS = ("payrollPeriodId", "attendancePeriodId", "payrollRunId", "bankAdviceBatchId", "glJournalId")


def owned_ids(manifest: dict) -> set[str]:
    """Ids this sandbox created: every master's `id`, plus each cycle's top-level ids. Step
    details are evidence, not ownership, so they are never read here."""
    ids: set[str] = set()

    def walk(node):
        if isinstance(node, dict):
            if isinstance(node.get("id"), str):
                ids.add(node["id"].lower())
            for value in node.values():
                if isinstance(value, dict):
                    walk(value)

    walk(manifest.get("masters", {}))
    for cycle in manifest.get("cycles", {}).values():
        ids.update(cycle[k].lower() for k in CYCLE_OWNED_KEYS if isinstance(cycle.get(k), str))
    return ids


# --- API wrapper with the per-write guard (plan §4.2). ---


class Api:
    def __init__(self, client: ApiClient, manifest: dict, writes_enabled: bool):
        self.client = client
        self.manifest = manifest
        self.writes_enabled = writes_enabled

    def get(self, path: str, expect=(200,), **params):
        response = self.client.get(path, params=params or None)
        if response.status_code not in expect:
            raise SandboxAbort(f"GET {path} returned {response.status_code}: {response.text[:400]}")
        return response.status_code, _data(response)

    def paged(self, path: str, **params) -> list:
        items, page = [], 1
        while True:
            _, data = self.get(path, page=page, pageSize=100, **params)
            items.extend(data["items"])
            if len(items) >= data["totalCount"] or not data["items"]:
                return items
            page += 1

    def write(self, method: str, path: str, *, target_id: str | None = None, new_key: str | None = None,
              json_body=None, params=None, expect=(200, 201)):
        """`new_key` marks a create whose natural key must carry the sandbox prefix (or be the
        reserved window's attendance period); `target_id` marks a write to an existing row, which
        must be one this sandbox recorded in its manifest."""
        if not self.writes_enabled:
            raise SandboxAbort("Writes are disabled. Set QA_PAYROLL_SANDBOX=1 and QA_PAYROLL_SANDBOX_TENANT=ANEVRA01.")
        if self.client._host != TENANT_HOST:
            raise SandboxAbort(f"Host header drifted to {self.client._host!r}; refusing to write.")
        if target_id is None and new_key is None:
            raise SandboxAbort(f"{method} {path}: a write must name either a new sandbox key or an owned id.")
        if new_key is not None and not (new_key.upper().startswith(PREFIX) or new_key.startswith("attendance:195")):
            raise SandboxAbort(f"{method} {path}: new key {new_key!r} is not a sandbox key.")
        if target_id is not None and target_id.lower() not in owned_ids(self.manifest):
            raise SandboxAbort(f"{method} {path}: {target_id} is not in the sandbox manifest; refusing to write.")
        # GET appears here only for endpoints with side effects (bank advice export moves the batch to Exported).
        send = {"POST": self.client.post, "PUT": self.client.put, "DELETE": self.client.delete, "GET": self.client.get}[method]
        kwargs = {"params": params}
        if method not in ("DELETE", "GET"):
            kwargs["json"] = json_body
        response = send(path, **kwargs)
        if response.status_code not in expect:
            raise SandboxAbort(f"{method} {path} returned {response.status_code}: {response.text[:600]}")
        return response.status_code, _data(response), response


def _data(response):
    try:
        body = response.json()
    except ValueError:
        return response.text
    return body.get("data") if isinstance(body, dict) and "data" in body else body


def sign_in(settings) -> tuple[ApiClient, dict]:
    host = os.environ.get("QA_TENANT_A_HOST", "")
    username = os.environ.get("QA_A_ADMIN_USERNAME") or os.environ.get("QA_A_ADMIN_IDENTIFIER")
    password = os.environ.get("QA_A_ADMIN_PASSWORD")
    if not username or not password:
        raise SandboxAbort("QA_A_ADMIN_USERNAME/QA_A_ADMIN_PASSWORD are not configured.")
    client = ApiClient(settings, host)
    response = client.post("/api/auth/login", json={"identifier": username, "password": password})
    if response.status_code != 200:
        raise SandboxAbort(f"QA Admin sign-in failed ({response.status_code}).")
    token = response.json()["data"]["accessToken"]
    client.with_bearer(token)
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    return client, json.loads(base64.urlsafe_b64decode(payload))


# --- Preflight (plan §4.1). Read-only; raises SandboxAbort on the first hard failure. ---


def preflight(api: Api, claims: dict, settings, *, for_writes: bool) -> dict:
    report: dict = {"checks": {}, "warnings": []}
    checks = report["checks"]
    manifest = api.manifest

    if for_writes:
        if os.environ.get("QA_PAYROLL_SANDBOX") != "1" or os.environ.get("QA_PAYROLL_SANDBOX_TENANT") != TENANT_CODE:
            raise SandboxAbort("G1: writes need QA_PAYROLL_SANDBOX=1 and QA_PAYROLL_SANDBOX_TENANT=ANEVRA01.")
    checks["G1_opt_in"] = "set" if for_writes else "not required (read-only)"

    api_url = settings.api_url("/")
    if not (api_url.startswith("http://localhost:") or api_url.startswith("http://127.0.0.1:")):
        raise SandboxAbort(f"G2: API base {api_url} is not a local HTTP endpoint.")
    checks["G2_local_api"] = api_url

    if api.client._host != TENANT_HOST:
        raise SandboxAbort(f"G3: QA_TENANT_A_HOST must be {TENANT_HOST}, got {api.client._host!r}.")
    checks["G3_tenant_host"] = TENANT_HOST

    # Swagger UI is mapped only when IsDevelopment() (Program.cs). swagger.json itself currently
    # returns 500 (MasterImportController.Validate breaks generation), so the UI page is the probe.
    swagger = api.client.get("/swagger/index.html")
    if swagger.status_code != 200:
        raise SandboxAbort(f"G4: /swagger/index.html returned {swagger.status_code}; not a Development server.")
    checks["G4_development_server"] = "swagger UI mapped (200)"

    tid, tcode = claims.get("tid"), claims.get("tcode")
    if (tcode or "").upper() != TENANT_CODE:
        raise SandboxAbort(f"G5: JWT tenant code is {tcode!r}, expected {TENANT_CODE}.")
    pinned = manifest.get("tenant", {}).get("tid") or os.environ.get("QA_PAYROLL_SANDBOX_TENANT_ID")
    if pinned and pinned.lower() != str(tid).lower():
        raise SandboxAbort(f"G5: JWT tid {tid} does not match the pinned tenant id {pinned}.")
    checks["G5_tenant_identity"] = {"tcode": tcode, "tid": tid, "pinned": bool(pinned)}

    email, role = claims.get("email"), claims.get("role")
    roles = role if isinstance(role, list) else [role]
    if (email or "").lower() != QA_ADMIN_EMAIL or "TenantAdmin" not in roles:
        raise SandboxAbort(f"G6: signed-in actor is {email!r} with roles {roles}; expected the QA Admin (TenantAdmin).")
    checks["G6_actor"] = {"email": email, "roles": roles}

    # G7: sandbox purity.
    employee_id = manifest.get("masters", {}).get("employee", {}).get("id")
    _, earliest = api.get("/api/employees", sortBy="dateOfJoining", pageSize=10)
    outsiders = [e for e in earliest["items"] if not e["fullName"].endswith(EMPLOYEE_LAST_NAME)]
    if outsiders and outsiders[0]["dateOfJoining"] <= iso(SANDBOX_END):
        raise SandboxAbort(f"G7a: a non-sandbox employee joined on {outsiders[0]['dateOfJoining']}, inside the sandbox window.")
    checks["G7a_earliest_real_doj"] = outsiders[0]["dateOfJoining"] if outsiders else None

    known_attendance = {c.get("attendancePeriodId") for c in manifest.get("cycles", {}).values()}
    attendance = api.paged("/api/attendance/periods")
    foreign_window = [p for p in attendance if 1950 <= p["year"] <= 1959 and p["id"] not in known_attendance]
    if foreign_window:
        raise SandboxAbort(f"G7b: attendance periods in the sandbox window that this sandbox did not create: {foreign_window}")
    checks["G7b_attendance_window"] = "clean"
    open_real = [p for p in attendance if p["year"] > 1959 and p["status"] != "Closed"]
    if open_real:
        report["warnings"].append(
            f"{len(open_real)} real attendance period(s) are not Closed; a new sandbox employee makes their "
            "monthly summaries stale until they are re-processed: " + ", ".join(f"{p['year']}-{p['month']:02d} {p['status']}" for p in open_real))

    known_payroll = {c.get("payrollPeriodId") for c in manifest.get("cycles", {}).values()}
    periods = api.paged("/api/payroll/periods")
    in_window = [p for p in periods if p["startDate"] <= iso(SANDBOX_END) and p["endDate"] >= iso(SANDBOX_START)]
    foreign = [p["code"] for p in in_window if not p["code"].upper().startswith(PREFIX)]
    unowned = [p["code"] for p in in_window if p["code"].upper().startswith(PREFIX) and p["id"] not in known_payroll]
    if foreign or unowned:
        raise SandboxAbort(f"G7c: payroll periods overlap the sandbox window (foreign={foreign}, not in manifest={unowned}).")
    checks["G7c_payroll_window"] = "clean"

    if employee_id:
        _, history = api.get(f"/api/employees/{employee_id}/employment-history")
        if history:
            raise SandboxAbort(f"G7d: the sandbox employee has {len(history)} employment-history row(s); it would add blocking exceptions to real attendance months.")
        checks["G7d_sandbox_employee_history"] = 0

    _, controls = api.get("/api/payroll/controls")
    persisted = controls["id"] != EMPTY_GUID
    blocks_self_approval = persisted and controls["requireMakerChecker"] and controls["preventSelfApproval"]
    checks["G8_maker_checker"] = {
        "persistedRow": persisted,
        "selfApprovalBlocked": blocks_self_approval,
        "note": "GET returns default flags when no row exists; PayrollApprovalGuard allows approval when the row is absent.",
    }
    report["selfApprovalBlocked"] = blocks_self_approval

    # G9: hazard audit, report only.
    assignments = api.paged("/api/payroll/employee-salary-assignments")
    open_ended = [a for a in assignments if a["status"] == "Active" and not a.get("effectiveTo")]
    if open_ended:
        report["warnings"].append(f"G9: {len(open_ended)} active open-ended salary assignment(s) exist; those employees are eligible in real runs.")
    checks["G9_open_ended_active_assignments"] = len(open_ended)

    # G10: the QAAUTO bank master (Development-only seeder). Report only: without it, Bank Advice
    # payments validate Invalid, and everything else still runs.
    _, banks = api.get("/api/master-data/banks", search=BANK_CODE, activeOnly="false")
    qa_banks = [b for b in banks if b["code"].upper() == BANK_CODE]
    checks["G10_qaauto_bank_master"] = {"present": bool(qa_banks), "active": bool(qa_banks) and qa_banks[0]["isActive"]}
    if not qa_banks or not qa_banks[0]["isActive"]:
        report["warnings"].append(
            f"G10: no active {BANK_CODE} bank master. Set DevelopmentSeed:EnableQaAutomationBank=true and restart the API "
            "(docs/qa/payroll-sandbox-setup.md); until then Bank Advice payments validate Invalid.")
    return report


# --- Isolation fingerprint (plan §6, I4): must be identical before and after a write session. ---


def fingerprint(api: Api) -> dict:
    owned = owned_ids(api.manifest)

    def digest(rows: list, fields: tuple) -> dict:
        kept = sorted(tuple(str(r.get(f)) for f in fields) for r in rows if str(r.get("id", "")).lower() not in owned)
        return {"count": len(kept), "sha256": hashlib.sha256(json.dumps(kept).encode()).hexdigest()[:16]}

    employees = [e for e in api.paged("/api/employees") if not e["fullName"].endswith(EMPLOYEE_LAST_NAME)]
    return {
        "employees": digest(employees, ("id", "status", "dateOfJoining", "employeeCode")),
        "payrollPeriods": digest(api.paged("/api/payroll/periods"), ("id", "status", "concurrencyVersion", "isActive")),
        "payrollRuns": digest(api.paged("/api/payroll/runs"), ("id", "status", "concurrencyVersion", "employeeCount")),
        "salaryAssignments": digest(api.paged("/api/payroll/employee-salary-assignments"), ("id", "status", "concurrencyVersion")),
        "salaryComponents": digest(api.paged("/api/payroll/salary-components"), ("id", "isActive", "concurrencyVersion")),
        "salaryStructures": digest(api.paged("/api/payroll/salary-structures"), ("id", "isActive", "concurrencyVersion")),
        "attendancePeriods": digest(api.paged("/api/attendance/periods"), ("id", "status", "concurrencyVersion", "dataVersion")),
        # A batch of a sandbox run is sandbox data even before its id is recorded (see the adopt path in run_cycle).
        "bankAdviceBatches": digest([b for b in api.paged("/api/payroll/bank-advice") if b["payrollRunId"].lower() not in owned],
                                    ("id", "status")),
        # GL accounts/configurations/journals: their list endpoints return 500 (they bind the
        # abstract PagedQuery), so they cannot be fingerprinted through the API.
    }


# --- Seed: find-or-create the masters (plan §3.4). ---


def _single(rows: list, what: str):
    if len(rows) > 1:
        raise SandboxAbort(f"More than one {what} matches the sandbox key; stop and inspect by hand.")
    return rows[0] if rows else None


def seed(api: Api) -> dict:
    masters = api.manifest.setdefault("masters", {})
    created: list[str] = []

    # Employee: personal-details path, which writes no employment history.
    _, found = api.get("/api/employees", search=EMPLOYEE_LAST_NAME, pageSize=100)
    employee = _single([e for e in found["items"] if e["fullName"] == f"{EMPLOYEE_FIRST_NAME} {EMPLOYEE_LAST_NAME}"], "employee")
    if employee is None:
        if "employee" in masters:
            raise SandboxAbort("The manifest records a sandbox employee that the API no longer returns.")
        _, data, _ = api.write("POST", "/api/employees/personal-details", new_key=EMPLOYEE_LAST_NAME, json_body={
            "firstName": EMPLOYEE_FIRST_NAME, "lastName": EMPLOYEE_LAST_NAME, "dateOfJoining": iso(SANDBOX_START)}, expect=(201,))
        employee = {"id": data["id"], "dateOfJoining": data["dateOfJoining"], "status": data.get("status", "Active")}
        created.append("employee")
    if employee["dateOfJoining"] != iso(SANDBOX_START) or employee["status"] != "Active":
        raise SandboxAbort(f"Sandbox employee drifted: DOJ {employee['dateOfJoining']}, status {employee['status']}.")
    if masters.get("employee", {}).get("id", employee["id"]) != employee["id"]:
        raise SandboxAbort("Sandbox employee id does not match the manifest.")
    masters["employee"] = {"id": employee["id"], "key": f"{EMPLOYEE_FIRST_NAME} {EMPLOYEE_LAST_NAME}", "dateOfJoining": iso(SANDBOX_START)}
    save_manifest(api.manifest)
    _, history = api.get(f"/api/employees/{employee['id']}/employment-history")
    if history:
        raise SandboxAbort("The sandbox employee has employment history; see plan §8 B2. Stop.")

    bank_account = _ensure_bank_account(api, employee["id"], created)

    # Salary component.
    components = api.paged("/api/payroll/salary-components", search=COMPONENT_CODE)
    component = _single([c for c in components if c["code"].upper() == COMPONENT_CODE], "salary component")
    if component is None:
        _, component, _ = api.write("POST", "/api/payroll/salary-components", new_key=COMPONENT_CODE, json_body={
            "code": COMPONENT_CODE, "name": "QAAUTO HP Basic (sandbox)",
            "description": "QAAUTO payroll sandbox component. Bounded to the 1950-1959 sandbox window.",
            "componentType": "Earning", "calculationType": "FixedAmount", "statutoryType": "None",
            "isTaxable": True, "isStatutory": False, "isRecurring": True, "affectsGross": True, "affectsNetPay": True,
            "displayOrder": 1, "effectiveFrom": iso(SANDBOX_START), "isActive": True})
        created.append("salaryComponent")
    if not (component["componentType"] == "Earning" and component["calculationType"] == "FixedAmount" and component["isActive"]):
        raise SandboxAbort(f"Sandbox salary component drifted: {component}")
    masters["salaryComponent"] = {"id": component["id"], "code": COMPONENT_CODE}
    save_manifest(api.manifest)

    # Salary structure with one non-proratable FixedAmount line, so net pay is deterministic.
    structures = api.paged("/api/payroll/salary-structures", search=STRUCTURE_CODE)
    structure = _single([s for s in structures if s["code"].upper() == STRUCTURE_CODE], "salary structure")
    if structure is None:
        _, structure, _ = api.write("POST", "/api/payroll/salary-structures", new_key=STRUCTURE_CODE, json_body={
            "code": STRUCTURE_CODE, "name": "QAAUTO HP Structure (sandbox)",
            "description": "QAAUTO payroll sandbox structure. Bounded to the 1950-1959 sandbox window.",
            "effectiveFrom": iso(SANDBOX_START), "isActive": True,
            "components": [{"salaryComponentId": component["id"], "sequence": 1, "calculationType": "FixedAmount",
                            "value": MONTHLY_AMOUNT, "isProratable": False, "isEditableAtEmployeeLevel": False,
                            "isActive": True, "effectiveFrom": iso(SANDBOX_START)}]})
        created.append("salaryStructure")
    if not structure["isActive"]:
        raise SandboxAbort("Sandbox salary structure is inactive.")
    masters["salaryStructure"] = {"id": structure["id"], "code": STRUCTURE_CODE}
    save_manifest(api.manifest)

    # Assignment bounded to the window, so it is never effective in a real payroll period.
    _, by_employee = api.get(f"/api/payroll/employee-salary-assignments/by-employee/{employee['id']}", pageSize=100)
    rows = by_employee["items"]
    ours = [a for a in rows if a["salaryStructureId"] == structure["id"] and a["effectiveFrom"] == iso(SANDBOX_START)]
    if len(rows) != len(ours):
        raise SandboxAbort("The sandbox employee has salary assignments this sandbox did not create.")
    assignment = _single(ours, "salary assignment")
    if assignment is None:
        _, assignment, _ = api.write("POST", "/api/payroll/employee-salary-assignments", new_key=f"{PREFIX}-ASSIGNMENT", json_body={
            "employeeId": employee["id"], "salaryStructureId": structure["id"],
            "effectiveFrom": iso(SANDBOX_START), "effectiveTo": iso(SANDBOX_END),
            "annualCtc": MONTHLY_AMOUNT * 12, "monthlyCtc": MONTHLY_AMOUNT, "currencyCode": CURRENCY,
            "payFrequency": "Monthly", "status": "Active", "changeReason": "NewHire",
            "remarks": f"{PREFIX} sandbox assignment, bounded to {iso(SANDBOX_START)}..{iso(SANDBOX_END)}."})
        created.append("salaryAssignment")
    if assignment["status"] != "Active" or assignment.get("effectiveTo") != iso(SANDBOX_END):
        raise SandboxAbort(f"Sandbox assignment drifted: status {assignment['status']}, effectiveTo {assignment.get('effectiveTo')}.")
    masters["salaryAssignment"] = {"id": assignment["id"], "effectiveFrom": iso(SANDBOX_START), "effectiveTo": iso(SANDBOX_END)}
    save_manifest(api.manifest)

    # GL accounting, bounded to the window. The GL list endpoints return 500, so these rows are
    # tracked by the manifest alone; a code conflict without a manifest entry stops the seed.
    gl = masters.setdefault("gl", {})
    for key, code, account_type in (("expenseAccount", GL_EXPENSE_CODE, "Expense"), ("payableAccount", GL_PAYABLE_CODE, "Liability")):
        if key not in gl:
            _, data, _ = _create_unlisted(api, "/api/payroll/accounting/accounts", code, {
                "code": code, "name": f"QAAUTO HP {account_type} (sandbox)", "accountType": account_type, "isActive": True})
            gl[key] = {"id": data["id"], "code": code}
            created.append(f"gl.{key}")
            save_manifest(api.manifest)
    if "configuration" not in gl:
        _, data, _ = _create_unlisted(api, "/api/payroll/accounting/configurations", ACCOUNTING_CODE, {
            "code": ACCOUNTING_CODE, "name": "QAAUTO HP Accounting (sandbox)", "isActive": True})
        gl["configuration"] = {"id": data["id"], "code": ACCOUNTING_CODE}
        created.append("gl.configuration")
        save_manifest(api.manifest)
    if "version" not in gl:
        _, data, _ = api.write("POST", f"/api/payroll/accounting/configurations/{gl['configuration']['id']}/versions",
                               target_id=gl["configuration"]["id"], json_body={
                                   "effectiveFrom": iso(SANDBOX_START), "effectiveTo": iso(SANDBOX_END), "aggregationMode": "Account"})
        gl["version"] = {"id": data["id"], "effectiveFrom": iso(SANDBOX_START), "effectiveTo": iso(SANDBOX_END)}
        created.append("gl.version")
        save_manifest(api.manifest)
    mappings = gl.setdefault("mappings", {})
    wanted = {
        "earnings": {"salaryComponentId": component["id"], "mappingType": "Earnings",
                     "debitAccountId": gl["expenseAccount"]["id"], "creditAccountId": gl["payableAccount"]["id"], "priority": 1, "isActive": True},
        "netPayable": {"mappingType": "NetPayable", "creditAccountId": gl["payableAccount"]["id"], "priority": 1, "isActive": True},
    }
    for key, body in wanted.items():
        if key not in mappings:
            _, data, _ = api.write("POST", f"/api/payroll/accounting/versions/{gl['version']['id']}/mappings",
                                   target_id=gl["version"]["id"], json_body=body)
            mappings[key] = {"id": data["id"], "mappingType": body["mappingType"]}
            created.append(f"gl.mapping.{key}")
            save_manifest(api.manifest)

    # I5: the bounded assignment must not be effective in any real period (today is one).
    leak = api.client.get(f"/api/payroll/employee-salary-assignments/by-employee/{employee['id']}/effective", params={"date": date.today().isoformat()})
    if leak.status_code != 404:
        raise SandboxAbort(f"I5: the sandbox assignment resolves for today ({leak.status_code}); it would leak into real runs.")
    return {"created": created, "masters": masters, "bankAccount": bank_account,
            "realRunLeakCheck": "no effective assignment today (404)"}


def _ensure_bank_account(api: Api, employee_id: str, created: list) -> str:
    """Find-or-create the sandbox employee's salary account on the QAAUTO bank. A missing bank master
    is reported, not fatal, so payroll and GL still run; Bank Advice payments then validate Invalid."""
    masters = api.manifest["masters"]
    _, banks = api.get("/api/master-data/banks", search=BANK_CODE)
    bank = _single([b for b in banks if b["code"].upper() == BANK_CODE], "bank master")
    if bank is None:
        if "bankAccount" in masters:
            raise SandboxAbort(f"The manifest records a sandbox bank account, but the {BANK_CODE} bank master is missing or inactive.")
        return (f"skipped: no active {BANK_CODE} bank master (set DevelopmentSeed:EnableQaAutomationBank=true and "
                "restart the API)")

    path = f"/api/employees/{employee_id}/bank-details"
    _, details = api.get(path)
    current = [d for d in details if d["isActive"] and d["status"] == "Active"]
    account = _single([d for d in current if d["accountPurpose"] == "Salary"], "active salary bank account")
    if len(current) != (account is not None):
        raise SandboxAbort("The sandbox employee has current bank accounts this sandbox did not create.")
    if account is None:
        _, account, _ = api.write("POST", path, target_id=employee_id, expect=(201,), json_body={
            "bankId": bank["id"], "accountHolderName": BANK_ACCOUNT_HOLDER, "accountNumber": BANK_ACCOUNT_NUMBER,
            "accountType": "Savings", "accountPurpose": "Salary", "status": "Active", "ifscCode": BANK_IFSC,
            "branchName": BANK_BRANCH, "effectiveFrom": iso(SANDBOX_START)})
        created.append("bankAccount")
    shape = (account["bankId"], account["accountHolderName"], account["maskedAccountNumber"][-4:], account.get("effectiveFrom"))
    if shape != (bank["id"], BANK_ACCOUNT_HOLDER, BANK_ACCOUNT_NUMBER[-4:], iso(SANDBOX_START)):
        raise SandboxAbort(f"The sandbox employee's salary bank account drifted: {shape}")
    if masters.get("bankAccount", {}).get("id", account["id"]) != account["id"]:
        raise SandboxAbort("Sandbox bank account id does not match the manifest.")
    # `bankId` is evidence only: the bank master is seeder-owned and never written by this tool.
    masters["bankAccount"] = {"id": account["id"], "bankId": bank["id"], "bankCode": BANK_CODE, "bankName": bank["name"],
                              "accountPurpose": "Salary", "effectiveFrom": iso(SANDBOX_START)}
    save_manifest(api.manifest)
    return "present"


def _create_unlisted(api: Api, path: str, code: str, body: dict):
    status, data, response = api.write("POST", path, new_key=code, json_body=body, expect=(200, 201, 409))
    if status == 409:
        raise SandboxAbort(f"{code} already exists but is not in the manifest, and its list endpoint returns 500 so its id "
                           "cannot be recovered through the API. Restore the manifest, or see the L2 reset notes.")
    return status, data, response


# --- Cycle: one sandbox month through Payroll Period -> ... -> GL journal (plan §3.5). ---


def _next_month(manifest: dict) -> tuple[int, int]:
    used = set(manifest.get("cycles", {}))
    year, month = SANDBOX_START.year, SANDBOX_START.month
    while date(year, month, 1) <= SANDBOX_END:
        key = f"{year:04d}-{month:02d}"
        if key not in used:
            return year, month
        year, month = (year, month + 1) if month < 12 else (year + 1, 1)
    raise SandboxAbort("The sandbox window has no free months left.")


def _done(cycle: dict, step: str) -> bool:
    return step in cycle.get("steps", {})


def _mark(api: Api, cycle: dict, step: str, detail) -> None:
    cycle.setdefault("steps", {})[step] = {"atUtc": now_utc(), "detail": detail}
    save_manifest(api.manifest)
    log(f"  [ok] {step}: {json.dumps(detail, default=str)[:300]}")


def run_cycle(api: Api, self_approval_blocked: bool, new_month: bool) -> dict:
    masters = api.manifest.get("masters", {})
    for key in ("employee", "salaryStructure", "salaryAssignment"):
        if key not in masters:
            raise SandboxAbort("Run `seed` first.")
    employee_id = masters["employee"]["id"]
    cycles = api.manifest.setdefault("cycles", {})
    open_cycles = [k for k, c in cycles.items() if c.get("state") != "complete"]
    if open_cycles and not new_month:
        key = sorted(open_cycles)[0]
        year, month = int(key[:4]), int(key[5:])
        log(f"Resuming cycle {key}")
    else:
        year, month = _next_month(api.manifest)
        key = f"{year:04d}-{month:02d}"
        log(f"Starting cycle {key}")
    cycle = cycles.setdefault(key, {"state": "open"})
    start = date(year, month, 1)
    end = date(year, month, calendar.monthrange(year, month)[1])
    pay_date = date(year + (month == 12), month % 12 + 1, 1)
    code = f"{PREFIX}-{year:04d}{month:02d}"

    if not _done(cycle, "payroll_period_created"):
        existing = [p for p in api.paged("/api/payroll/periods") if p["code"].upper() == code]
        if existing:
            raise SandboxAbort(f"Payroll period {code} exists but the manifest has no record of creating it.")
        _, period, _ = api.write("POST", "/api/payroll/periods", new_key=code, json_body={
            "code": code, "name": f"QAAUTO HP {key}", "periodType": "Monthly", "startDate": iso(start), "endDate": iso(end),
            "payDate": iso(pay_date), "fiscalYear": year, "periodNumber": month, "isActive": True})
        cycle["payrollPeriodId"] = period["id"]
        _mark(api, cycle, "payroll_period_created", {"id": period["id"], "code": code, "status": period["status"], "start": iso(start), "end": iso(end)})

    if not _done(cycle, "attendance_period_created"):
        _, listed = api.get("/api/attendance/periods", year=year, month=month)
        if listed["items"]:
            raise SandboxAbort(f"Attendance period {key} exists but the manifest has no record of creating it.")
        _, period, _ = api.write("POST", "/api/attendance/periods", new_key=f"attendance:{key}", json_body={"year": year, "month": month}, expect=(201,))
        if (period["startDate"], period["endDate"]) != (iso(start), iso(end)):
            raise SandboxAbort(f"Attendance period dates {period['startDate']}..{period['endDate']} do not equal the payroll period's.")
        cycle["attendancePeriodId"] = period["id"]
        _mark(api, cycle, "attendance_period_created", {"id": period["id"], "status": period["status"], "start": period["startDate"], "end": period["endDate"]})
    attendance_id = cycle["attendancePeriodId"]

    if not _done(cycle, "attendance_processed"):
        _, overview, _ = api.write("POST", f"/api/attendance/periods/{attendance_id}/process", target_id=attendance_id)
        if overview["employeesProcessed"] != 1 or overview["blockingExceptions"] != 0:
            raise SandboxAbort(f"Sandbox contaminated: processing swept {overview['employeesProcessed']} employee(s) with "
                               f"{overview['blockingExceptions']} blocking exception(s); expected exactly the sandbox employee and none.")
        _, summaries = api.get(f"/api/attendance/periods/{attendance_id}/summaries", pageSize=100)
        if [s["employeeId"] for s in summaries["items"]] != [employee_id]:
            raise SandboxAbort("The attendance summaries are not exactly the sandbox employee.")
        _mark(api, cycle, "attendance_processed", {"employeesProcessed": overview["employeesProcessed"], "blockingExceptions": overview["blockingExceptions"], "status": overview["status"]})

    if not _done(cycle, "attendance_closed"):
        _, preview = api.get(f"/api/attendance/periods/{attendance_id}/close-preview")
        if not preview["canClose"]:
            raise SandboxAbort(f"Close preview refuses: {preview['blockers']}")
        _, closed, _ = api.write("POST", f"/api/attendance/periods/{attendance_id}/close", target_id=attendance_id,
                                 json_body={"comment": f"{PREFIX} sandbox close {key}"})
        if closed["status"] != "Closed":
            raise SandboxAbort(f"Attendance period did not close: {closed['status']}")
        _, events = api.get(f"/api/attendance/periods/{attendance_id}/events")
        snapshots = [e for e in events if e["eventType"] == "PayrollSnapshotCreated"]
        _mark(api, cycle, "attendance_closed", {"status": closed["status"], "canClose": preview["canClose"], "payrollSnapshotsCreated": len(snapshots)})

    period_id = cycle["payrollPeriodId"]
    if not _done(cycle, "run_created"):
        existing = api.paged("/api/payroll/runs", payrollPeriodId=period_id)
        if existing:
            raise SandboxAbort(f"Payroll period {code} already has runs the manifest does not record.")
        _, run, _ = api.write("POST", "/api/payroll/runs", target_id=period_id, json_body={
            "payrollPeriodId": period_id, "runType": "Regular", "notes": f"{PREFIX} sandbox cycle {key}"})
        cycle["payrollRunId"] = run["id"]
        _mark(api, cycle, "run_created", {"id": run["id"], "runNumber": run["runNumber"], "status": run["status"]})
    run_id = cycle["payrollRunId"]

    if not _done(cycle, "run_prepared"):
        _, run, _ = api.write("POST", f"/api/payroll/runs/{run_id}/prepare", target_id=run_id, params={"rebuild": "false"})
        eligible = [e["employeeId"] for e in run["employees"] if e["isEligible"]]
        if eligible != [employee_id]:
            raise SandboxAbort(f"Eligible population is {eligible}; expected exactly the sandbox employee.")
        _, readiness = api.get(f"/api/payroll/runs/{run_id}/readiness")
        _mark(api, cycle, "run_prepared", {"status": run["status"], "employeeCount": run["employeeCount"], "eligible": len(eligible),
                                           "excluded": run["employeeCount"] - len(eligible), "readiness": readiness})

    expected_net = float(MONTHLY_AMOUNT)
    if not _done(cycle, "calculated"):
        _, summary, _ = api.write("POST", f"/api/payroll/runs/{run_id}/calculate", target_id=run_id)
        _, errors = api.get(f"/api/payroll/runs/{run_id}/calculation-errors")
        if summary["status"] != "Calculated" or summary["calculatedCount"] != 1 or summary["failedCount"] != 0:
            raise SandboxAbort(f"Calculation did not complete cleanly: {summary}; errors: {errors}")
        result = _single_result(api, run_id, employee_id, expected_net)
        _mark(api, cycle, "calculated", {"summary": summary, "errors": errors, "result": result})

    if not _done(cycle, "recalculated"):
        _, summary, _ = api.write("POST", f"/api/payroll/runs/{run_id}/recalculate", target_id=run_id)
        if summary["status"] != "Calculated" or summary["calculatedCount"] != 1 or summary["failedCount"] != 0:
            raise SandboxAbort(f"Recalculation did not complete cleanly: {summary}")
        result = _single_result(api, run_id, employee_id, expected_net)
        first = cycle["steps"]["calculated"]["detail"]["result"]
        if result["calculationVersion"] <= first["calculationVersion"] or result["id"] == first["id"]:
            raise SandboxAbort(f"Recalculation did not supersede the first result: {first} -> {result}")
        _mark(api, cycle, "recalculated", {"summary": summary, "result": result, "supersededResultId": first["id"]})

    if not _done(cycle, "approved"):
        if self_approval_blocked:
            cycle["state"] = "blocked:maker-checker"
            save_manifest(api.manifest)
            raise SandboxAbort("B3: a persisted payroll control row blocks self-approval; a second payroll-capable identity is needed to approve.")
        _, run, _ = api.write("POST", f"/api/payroll/runs/{run_id}/transition", target_id=run_id, params={"target": "Approved"})
        _mark(api, cycle, "approved", {"status": run["status"]})

    if not _done(cycle, "bank_advice_generated"):
        status, batch, response = api.write("POST", f"/api/payroll/runs/{run_id}/bank-advice", target_id=run_id, expect=(200, 201, 409))
        if status == 409:
            # An interrupted earlier attempt may have generated the batch without recording it. The run is
            # sandbox-owned, so its single active batch is too; anything else stops.
            active = [b for b in api.paged("/api/payroll/bank-advice") if b["payrollRunId"] == run_id and b["status"] != "Cancelled"]
            if len(active) != 1 or active[0]["status"] != "Draft":
                raise SandboxAbort(f"Bank advice generation was refused: {response.text[:400]}")
            batch = active[0]
            cycle["bankAdviceBatchAdopted"] = True
        cycle["bankAdviceBatchId"] = batch["id"]
        # The API omits null fields, so a Valid payment has no validationMessage key.
        _mark(api, cycle, "bank_advice_generated", {
            "id": batch["id"], "status": batch["status"], "totalEmployees": batch["totalEmployees"], "totalAmount": batch["totalAmount"],
            "payments": [{"employeeId": p["employeeId"], "netPay": p["netPay"], "validationStatus": p["validationStatus"],
                          "paymentStatus": p["paymentStatus"], "validationMessage": p.get("validationMessage")} for p in batch["payments"]]})

    batch_id = cycle["bankAdviceBatchId"]
    if not _done(cycle, "bank_advice_prepared"):
        status, batch, response = api.write("POST", f"/api/payroll/bank-advice/{batch_id}/prepare", target_id=batch_id, expect=(200, 409))
        if status == 409:
            raise SandboxAbort(f"Bank advice prepare was refused (a payment is Invalid; see G10): {response.text[:400]}")
        _mark(api, cycle, "bank_advice_prepared", {"status": batch["status"], "totalEmployees": batch["totalEmployees"], "totalAmount": batch["totalAmount"]})

    if not _done(cycle, "bank_advice_approved"):
        if self_approval_blocked:
            cycle["state"] = "blocked:maker-checker"
            save_manifest(api.manifest)
            raise SandboxAbort("B3: a persisted payroll control row blocks self-approval of the bank advice batch.")
        _, batch, _ = api.write("POST", f"/api/payroll/bank-advice/{batch_id}/approve", target_id=batch_id)
        _mark(api, cycle, "bank_advice_approved", {"status": batch["status"], "approvedAtUtc": batch.get("approvedAtUtc")})

    if not _done(cycle, "bank_advice_exported"):
        _, csv_text, _ = api.write("GET", f"/api/payroll/bank-advice/{batch_id}/export", target_id=batch_id, expect=(200,))
        _, batch = api.get(f"/api/payroll/bank-advice/{batch_id}")
        _mark(api, cycle, "bank_advice_exported", {"status": batch["status"], "exportedAtUtc": batch.get("exportedAtUtc"),
                                                   "csvRows": len(csv_text.strip().splitlines()) - 1})

    if not _done(cycle, "gl_journal_generated"):
        status, journal, response = api.write("POST", f"/api/payroll/runs/{run_id}/accounting/generate", target_id=run_id, expect=(200, 201, 409))
        if status == 409:
            raise SandboxAbort(f"GL journal generation was refused: {response.text[:400]}")
        cycle["glJournalId"] = journal["id"]
        _mark(api, cycle, "gl_journal_generated", {
            "id": journal["id"], "status": journal["status"], "totalDebit": journal["totalDebit"], "totalCredit": journal["totalCredit"],
            "lines": [{k: line.get(k) for k in ("accountCode", "debit", "credit", "description", "sourceType")} for line in journal["lines"]]})

    cycle["state"] = "complete"
    save_manifest(api.manifest)
    return {"cycle": key, **cycle}


def _single_result(api: Api, run_id: str, employee_id: str, expected_net: float) -> dict:
    _, results = api.get(f"/api/payroll/runs/{run_id}/results", pageSize=100)
    rows = results["items"]
    if [r["employeeId"] for r in rows] != [employee_id]:
        raise SandboxAbort(f"Current results are not exactly the sandbox employee: {[r['employeeId'] for r in rows]}")
    row = rows[0]
    if float(row["netPay"]) != expected_net or float(row["grossEarnings"]) != expected_net or row["currencyCode"] != CURRENCY:
        raise SandboxAbort(f"Unexpected result amounts: gross {row['grossEarnings']}, net {row['netPay']}, {row['currencyCode']}")
    return {"id": row["id"], "calculationVersion": row["calculationVersion"], "grossEarnings": row["grossEarnings"],
            "totalDeductions": row["totalDeductions"], "netPay": row["netPay"], "status": row["status"],
            "eligibleDays": row["eligibleDays"], "calendarDays": row["calendarDays"],
            "components": [(c["componentCode"], c["calculatedAmount"]) for c in row["components"]]}


# --- Retire (plan §5, L1). Dry run unless --execute. ---


def retire(api: Api, execute: bool) -> list:
    masters = api.manifest.get("masters", {})
    gl = masters.get("gl", {})
    plan = []
    for mapping in gl.get("mappings", {}).values():
        plan.append(("DELETE", f"/api/payroll/accounting/mappings/{mapping['id']}", mapping["id"], None, None))
    if "salaryAssignment" in masters:
        a = masters["salaryAssignment"]["id"]
        plan.append(("POST", f"/api/payroll/employee-salary-assignments/{a}/deactivate", a, None, None))
    if "salaryStructure" in masters:
        s = masters["salaryStructure"]["id"]
        plan.append(("POST", f"/api/payroll/salary-structures/{s}/deactivate", s, None, None))
    if "salaryComponent" in masters:
        c = masters["salaryComponent"]["id"]
        plan.append(("POST", f"/api/payroll/salary-components/{c}/deactivate", c, None, None))
    if "bankAccount" in masters and "employee" in masters:
        b = masters["bankAccount"]["id"]
        plan.append(("DELETE", f"/api/employees/{masters['employee']['id']}/bank-details/{b}", b, None, None))
    for key, account_type in (("expenseAccount", "Expense"), ("payableAccount", "Liability")):
        if key in gl:
            acc = gl[key]
            plan.append(("PUT", f"/api/payroll/accounting/accounts/{acc['id']}", acc["id"], None,
                         {"code": acc["code"], "name": f"QAAUTO HP {account_type} (sandbox, retired)", "accountType": account_type, "isActive": False}))
    for method, path, target, params, body in plan:
        log(f"  {'EXEC' if execute else 'PLAN'} {method} {path}")
        if execute:
            api.write(method, path, target_id=target, params=params, json_body=body)
    log("  MANUAL: retire the sandbox employee (Status Resigned + DateOfLeaving 1959-12-31; the validator requires both)"
        " in the Employee UI; see docs/qa/payroll-sandbox-setup.md. The accounting version is already bounded to"
        " 1950-1959 and needs no action. The QAAUTO-BANK master has no API to retire it: unset"
        " DevelopmentSeed:EnableQaAutomationBank and, if wanted, set it inactive by hand (the seeder never reactivates it).")
    return plan


# --- Entry point. ---


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["preflight", "seed", "cycle", "status", "retire"])
    parser.add_argument("--execute", action="store_true", help="retire: perform the writes instead of printing them")
    parser.add_argument("--new-month", action="store_true", help="cycle: start the next free month even if one is open")
    args = parser.parse_args(argv)

    writes = args.command in ("seed", "cycle") or (args.command == "retire" and args.execute)
    settings = load_settings()
    manifest = load_manifest()
    try:
        client, claims = sign_in(settings)
        api = Api(client, manifest, writes_enabled=False)
        report = preflight(api, claims, settings, for_writes=writes)
        log(json.dumps({"preflight": report}, indent=2, default=str))
        if args.command == "status":
            log(json.dumps({"manifest": manifest}, indent=2, default=str))
        if args.command == "retire" and not args.execute:
            retire(api, execute=False)
        if not writes:
            return 0

        manifest.setdefault("tenant", {}).update({"code": TENANT_CODE, "tid": claims["tid"], "host": TENANT_HOST})
        save_manifest(manifest)
        before = fingerprint(api)
        api.writes_enabled = True
        if args.command == "seed":
            outcome = seed(api)
        elif args.command == "cycle":
            outcome = run_cycle(api, report["selfApprovalBlocked"], args.new_month)
        else:
            outcome = retire(api, execute=True)
        api.writes_enabled = False
        after = fingerprint(api)
        isolation = {"unchanged": before == after, "before": before, "after": after}
        log(json.dumps({"outcome": outcome, "isolation": isolation}, indent=2, default=str))
        if before != after:
            log("ISOLATION CHECK FAILED: non-sandbox data changed during this session.")
            return 2
        return 0
    except SandboxAbort as exc:
        log(f"STOPPED: {exc}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
