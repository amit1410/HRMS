"""API regression suite for Module 08 — Attendance Foundation / Configuration.

Automated from qa/08-attendance/cases/01-foundation-config.yaml (ATT-SHIFT-*, ATT-PATTERN-*,
ATT-APPLIC-*, ATT-ROSTER-*, ATT-UPLOAD-*).

All mutating tests run as the TenantAdmin QA admin (the only seeded identity that holds
ShiftManage/PatternManage/RosterManage/RosterUpload — CR-112), create only QAAUTO-marked config,
and clean themselves up: Shifts have no delete endpoint so they are deactivated via Update with a
freshly re-read ConcurrencyToken (see the `disposable_shift` fixture); Applicability rules are
deleted; Roster overrides are removed. Employee/Manager identities are used only for the
authorization-denial assertions, where a 403 is the documented expected outcome.

Two source-verified quirks this suite pins deliberately (not treated as framework bugs):
  * CR-91 / ATT-APPLIC-005 — POST /applicability echoes the request (no id, conditions trimmed) and
    GET drops most dimension conditions; the rule id is therefore recovered from the list, matched on
    the unique QAAUTO RuleName, for get/delete.
  * ATT-APPLIC-018 — create uses POST /applicability while read/update/delete use /shift-applicability.
"""

from __future__ import annotations

import uuid
from datetime import date, timedelta

import allure
import pytest

from core.attendance_api import (
    add_applicability,
    assign_roster,
    create_pattern,
    create_shift,
    delete_applicability,
    get_applicability,
    get_applicability_by_id,
    get_patterns,
    get_roster,
    get_roster_template,
    get_shifts,
    remove_roster,
    resolve_roster,
    update_shift,
    validate_roster_upload,
)
from data.test_data import random_suffix
from utils.allure_evidence import cr_refs, known_defect, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]


def _find_rule_id(client, rule_name: str) -> str | None:
    listing = get_applicability(client, page=1, pageSize=200)
    if listing.status_code != 200:
        return None
    return next((r["id"] for r in listing.json()["data"]["items"] if r.get("ruleName") == rule_name), None)


# --- Shifts ------------------------------------------------------------------------------------


@allure.story("Foundation / Shifts")
@allure.title("ATT-SHIFT-001 — GET shifts returns the tenant's configured shifts as a list")
@qa_cases("ATT-SHIFT-001")
@pytest.mark.critical
def test_list_shifts(admin_api_client):
    response = get_shifts(admin_api_client, page=1, pageSize=50)
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list), response.text


@allure.story("Foundation / Shifts")
@allure.title("ATT-SHIFT-002 — a valid ShiftRequest is created and persists its code/name/times exactly")
@qa_cases("ATT-SHIFT-002")
@pytest.mark.critical
def test_create_shift_persists(disposable_shift):
    assert disposable_shift["shiftCode"].startswith("QAAUTO-")
    assert disposable_shift["startTime"] == "09:00:00" and disposable_shift["endTime"] == "18:00:00"
    assert disposable_shift["isActive"] is True
    assert disposable_shift["plannedDurationMinutes"] == 540


@allure.story("Foundation / Shifts")
@allure.title("ATT-SHIFT-003 — a blank ShiftCode/ShiftName is rejected 400")
@qa_cases("ATT-SHIFT-003")
def test_create_shift_blank_code_rejected(admin_api_client):
    response = create_shift(
        admin_api_client,
        shiftCode="", shiftName="", startTime="09:00:00", endTime="18:00:00",
        effectiveFrom="2026-01-01", minimumWorkMinutes=480, fullDayWorkMinutes=480,
        captureMode="BiometricOnly", allowedAttendanceSources="Biometric",
    )
    assert response.status_code == 400, response.text


@allure.story("Foundation / Shifts")
@allure.title("ATT-SHIFT-004 — a duplicate ShiftCode within the tenant is rejected 409")
@qa_cases("ATT-SHIFT-004")
@pytest.mark.critical
def test_duplicate_shift_code_rejected(admin_api_client, disposable_shift):
    duplicate = create_shift(
        admin_api_client,
        shiftCode=disposable_shift["shiftCode"], shiftName="dup", startTime="09:00:00", endTime="18:00:00",
        effectiveFrom="2026-01-01", minimumWorkMinutes=480, fullDayWorkMinutes=480,
        captureMode="BiometricOnly", allowedAttendanceSources="Biometric",
    )
    assert duplicate.status_code == 409, duplicate.text


@allure.story("Foundation / Shifts")
@allure.title("ATT-SHIFT-013 — Update requires a matching ConcurrencyToken (stale token is 409)")
@qa_cases("ATT-SHIFT-013")
@pytest.mark.critical
def test_shift_update_requires_concurrency_token(admin_api_client, disposable_shift):
    stale = update_shift(
        admin_api_client, disposable_shift["id"],
        shiftCode=disposable_shift["shiftCode"], shiftName="renamed", startTime="09:00:00", endTime="18:00:00",
        effectiveFrom="2026-01-01", minimumWorkMinutes=480, fullDayWorkMinutes=480,
        captureMode="BiometricOnly", allowedAttendanceSources="Biometric",
        concurrencyToken="00000000-0000-0000-0000-000000000000",
    )
    assert stale.status_code == 409, stale.text


@allure.story("Foundation / Shifts")
@allure.title("ATT-SHIFT-016 — only ShiftManage can create a Shift: Employee and Manager get 403")
@qa_cases("ATT-SHIFT-016")
@pytest.mark.critical
def test_create_shift_requires_shift_manage(employee_api_client, manager_api_client):
    body = dict(
        shiftCode=f"QAAUTO-{random_suffix(6)}", shiftName="x", startTime="09:00:00", endTime="18:00:00",
        effectiveFrom="2026-01-01", minimumWorkMinutes=480, fullDayWorkMinutes=480,
        captureMode="BiometricOnly", allowedAttendanceSources="Biometric",
    )
    assert create_shift(employee_api_client, **body).status_code == 403
    assert create_shift(manager_api_client, **body).status_code == 403


# --- Shift Patterns ----------------------------------------------------------------------------


@allure.story("Foundation / Patterns")
@allure.title("ATT-PATTERN-001 — GET patterns returns the tenant's shift patterns")
@qa_cases("ATT-PATTERN-001")
def test_list_patterns(admin_api_client):
    response = get_patterns(admin_api_client)
    assert response.status_code == 200, response.text
    assert isinstance(response.json()["data"], list), response.text


@allure.story("Foundation / Patterns")
@allure.title("ATT-PATTERN-002 — a valid 1-day Shift Pattern referencing an active Shift is created")
@qa_cases("ATT-PATTERN-002")
def test_create_pattern(admin_api_client, disposable_shift):
    code = f"QAAUTO-P{random_suffix(5)}"
    response = create_pattern(
        admin_api_client,
        code=code, name=code, cycleLengthDays=1, effectiveFrom="2026-01-01",
        days=[{"sequenceDay": 1, "shiftId": disposable_shift["id"], "dayType": "Shift"}],
    )
    assert response.status_code in (200, 201), response.text
    assert response.json()["data"]["cycleLengthDays"] == 1


@allure.story("Foundation / Patterns")
@allure.title("ATT-PATTERN-003 — a CycleLengthDays/Days.Count mismatch is rejected 400")
@qa_cases("ATT-PATTERN-003")
def test_pattern_cycle_mismatch_rejected(admin_api_client, disposable_shift):
    code = f"QAAUTO-P{random_suffix(5)}"
    response = create_pattern(
        admin_api_client,
        code=code, name=code, cycleLengthDays=3, effectiveFrom="2026-01-01",
        days=[{"sequenceDay": 1, "shiftId": disposable_shift["id"], "dayType": "Shift"}],
    )
    assert response.status_code == 400, response.text


@allure.story("Foundation / Patterns")
@allure.title("ATT-PATTERN-009 — no update/delete endpoint exists for Shift Patterns")
@qa_cases("ATT-PATTERN-009")
def test_pattern_has_no_update_or_delete_route(admin_api_client):
    put = admin_api_client.put(f"/api/attendance/patterns/{uuid.uuid4()}", json={})
    delete = admin_api_client.delete(f"/api/attendance/patterns/{uuid.uuid4()}")
    assert put.status_code in (404, 405), put.text
    assert delete.status_code in (404, 405), delete.text


# --- Applicability -----------------------------------------------------------------------------


@allure.story("Foundation / Applicability")
@allure.title("ATT-APPLIC-001 / ATT-APPLIC-009 — create an Applicability rule and delete it")
@qa_cases("ATT-APPLIC-001", "ATT-APPLIC-009")
@pytest.mark.critical
def test_applicability_create_and_delete(admin_api_client, disposable_shift):
    rule_name = f"QAAUTO rule {random_suffix(8)}"
    created = add_applicability(
        admin_api_client, ruleName=rule_name, shiftId=disposable_shift["id"], priority=10, effectiveFrom="2026-01-01"
    )
    assert created.status_code in (200, 201), created.text
    rule_id = _find_rule_id(admin_api_client, rule_name)
    assert rule_id is not None, "created rule should appear in the shift-applicability list"
    try:
        assert get_applicability_by_id(admin_api_client, rule_id).status_code == 200
    finally:
        assert delete_applicability(admin_api_client, rule_id).status_code == 200


@allure.story("Foundation / Applicability")
@allure.title("ATT-APPLIC-002 — RuleName is required (blank is rejected 400)")
@qa_cases("ATT-APPLIC-002")
def test_applicability_rule_name_required(admin_api_client, disposable_shift):
    response = add_applicability(
        admin_api_client, ruleName="", shiftId=disposable_shift["id"], priority=1, effectiveFrom="2026-01-01"
    )
    assert response.status_code == 400, response.text


@allure.story("Foundation / Applicability")
@allure.title("ATT-APPLIC-003 — exactly one of ShiftId/ShiftPatternId must be set (neither is 400)")
@qa_cases("ATT-APPLIC-003")
@pytest.mark.critical
def test_applicability_xor_target(admin_api_client):
    response = add_applicability(admin_api_client, ruleName=f"QAAUTO {random_suffix(6)}", priority=1, effectiveFrom="2026-01-01")
    assert response.status_code == 400, response.text
    assert "one" in response.text.lower() and ("shift" in response.text.lower() or "pattern" in response.text.lower())


@allure.story("Foundation / Applicability")
@allure.title("ATT-APPLIC-005 / CR-91 — GET rule detail exposes only the trimmed condition set")
@qa_cases("ATT-APPLIC-005")
@cr_refs("CR-91")
def test_applicability_get_drops_conditions(admin_api_client, disposable_shift):
    rule_name = f"QAAUTO cond {random_suffix(8)}"
    add_applicability(admin_api_client, ruleName=rule_name, shiftId=disposable_shift["id"], priority=5, effectiveFrom="2026-01-01")
    rule_id = _find_rule_id(admin_api_client, rule_name)
    assert rule_id is not None
    try:
        detail = get_applicability_by_id(admin_api_client, rule_id)
        assert detail.status_code == 200, detail.text
        conditions = detail.json()["data"]["conditions"]
        # CR-91: the exposed condition map is the trimmed subset, not the full ~16 dimensions.
        assert isinstance(conditions, dict)
        allure.attach(str(sorted(conditions)), name="exposed condition keys", attachment_type=allure.attachment_type.TEXT)
        assert "HoldingCompany" not in conditions and "Organisation" not in conditions
    finally:
        delete_applicability(admin_api_client, rule_id)


@allure.story("Foundation / Applicability")
@allure.title("ATT-APPLIC-018 — create uses POST /applicability; /shift-applicability has no create route")
@qa_cases("ATT-APPLIC-018")
def test_applicability_route_naming_inconsistency(admin_api_client, disposable_shift):
    wrong = admin_api_client.post(
        "/api/attendance/shift-applicability",
        json={"ruleName": f"QAAUTO {random_suffix(6)}", "shiftId": disposable_shift["id"], "priority": 1, "effectiveFrom": "2026-01-01"},
    )
    assert wrong.status_code in (404, 405), wrong.text


@allure.story("Foundation / Applicability")
@allure.title("ATT-APPLIC — creating an Applicability rule requires PatternManage: Employee/Manager get 403")
@qa_cases("ATT-APPLIC-001")
def test_applicability_requires_permission(employee_api_client, manager_api_client):
    body = dict(ruleName=f"QAAUTO {random_suffix(6)}", shiftId=str(uuid.uuid4()), priority=1, effectiveFrom="2026-01-01")
    assert add_applicability(employee_api_client, **body).status_code == 403
    assert add_applicability(manager_api_client, **body).status_code == 403


# --- Roster ------------------------------------------------------------------------------------


@allure.story("Foundation / Roster")
@allure.title("ATT-ROSTER-001 / ATT-ROSTER-011 — assign a Shift for one date, then remove the override")
@qa_cases("ATT-ROSTER-001", "ATT-ROSTER-011")
@pytest.mark.critical
def test_roster_assign_then_remove(admin_api_client, created_employee, disposable_shift):
    day = (date.today() + timedelta(days=420)).isoformat()
    assigned = assign_roster(
        admin_api_client,
        employeeIds=[created_employee["id"]], fromDate=day, toDate=day, shiftId=disposable_shift["id"], dayType="Shift",
    )
    assert assigned.status_code in (200, 201), assigned.text
    assert assigned.json()["data"][0]["employeeId"] == created_employee["id"]
    removed = remove_roster(admin_api_client, created_employee["id"], day)
    assert removed.status_code == 200, removed.text


@allure.story("Foundation / Roster")
@allure.title("ATT-ROSTER-002 — an empty employee list and an inverted date range are rejected 400")
@qa_cases("ATT-ROSTER-002")
def test_roster_assign_validation(admin_api_client, disposable_shift):
    day = (date.today() + timedelta(days=420))
    empty = assign_roster(admin_api_client, employeeIds=[], fromDate=day.isoformat(), toDate=day.isoformat(), shiftId=disposable_shift["id"], dayType="Shift")
    assert empty.status_code == 400, empty.text
    inverted = assign_roster(
        admin_api_client, employeeIds=[str(uuid.uuid4())],
        fromDate=day.isoformat(), toDate=(day - timedelta(days=5)).isoformat(), shiftId=disposable_shift["id"], dayType="Shift",
    )
    assert inverted.status_code == 400, inverted.text


@allure.story("Foundation / Roster")
@allure.title("ATT-ROSTER-004 — DayType=Shift requires a ShiftId (missing ShiftId is 400)")
@qa_cases("ATT-ROSTER-004")
def test_roster_shift_daytype_requires_shift(admin_api_client, created_employee):
    day = (date.today() + timedelta(days=421)).isoformat()
    response = assign_roster(admin_api_client, employeeIds=[created_employee["id"]], fromDate=day, toDate=day, dayType="Shift")
    assert response.status_code == 400, response.text


@allure.story("Foundation / Roster")
@allure.title("ATT-ROSTER-012 — removing an override with no explicit roster row is NotFound")
@qa_cases("ATT-ROSTER-012")
def test_roster_remove_nonexistent_is_404(admin_api_client, created_employee):
    day = (date.today() + timedelta(days=500)).isoformat()
    response = remove_roster(admin_api_client, created_employee["id"], day)
    assert response.status_code == 404, response.text


@allure.story("Foundation / Roster")
@allure.title("ATT-ROSTER-014 — the roster grid query requires FromDate/ToDate and is paged")
@qa_cases("ATT-ROSTER-014", "ATT-ROSTER-015")
def test_roster_grid_requires_dates_and_pages(admin_api_client):
    assert get_roster(admin_api_client).status_code == 400
    today = date.today()
    paged = get_roster(admin_api_client, fromDate=today.isoformat(), toDate=today.isoformat(), page=1, pageSize=10)
    assert paged.status_code == 200, paged.text
    data = paged.json()["data"]
    assert data["page"] == 1 and {"items", "totalCount", "totalPages"} <= set(data)


@allure.story("Foundation / Roster")
@allure.title("ATT-ROSTER-006 — resolving/assigning roster for a nonexistent employee is NotFound, not Forbidden")
@qa_cases("ATT-ROSTER-006")
def test_roster_nonexistent_employee_is_404(admin_api_client):
    # An authorized caller referencing an out-of-scope / nonexistent employee gets NotFound (404),
    # never Forbidden (403) — the scope-vs-permission distinction ATT-ROSTER-006 pins.
    unknown = str(uuid.uuid4())
    resolved = resolve_roster(admin_api_client, unknown, date.today().isoformat())
    assert resolved.status_code == 404, resolved.text
    day = (date.today() + timedelta(days=422)).isoformat()
    assigned = assign_roster(
        admin_api_client, employeeIds=[unknown], fromDate=day, toDate=day, shiftId=str(uuid.uuid4()), dayType="Shift"
    )
    assert assigned.status_code in (400, 404), assigned.text


# --- Roster upload -----------------------------------------------------------------------------


@allure.story("Foundation / Roster upload")
@allure.title("ATT-UPLOAD-001 / KNOWN-DEFECT — the roster upload CSV template must download as text/csv")
@qa_cases("ATT-UPLOAD-001")
@known_defect("ATT-UPLOAD-TEMPLATE-500")
@pytest.mark.known_defect
def test_roster_upload_template(admin_api_client):
    """CONFIRMED DEFECT (discovered by this suite; deterministic, reproduced twice).
    `GET /api/attendance/roster/template` returns HTTP 500 ("No file provider has been configured to
    process the supplied file.") because AttendanceFoundationController.Template() calls
    `File("EmployeeCode,Date,ShiftCode,DayType\\r\\n", "text/csv", "attendance-roster-template.csv")`
    — a string first argument binds ControllerBase's *virtual-path* File overload
    (VirtualFileResult), so the framework tries to resolve the CSV header text as a physical file
    path and fails. The fix is to pass the bytes: `File(Encoding.UTF8.GetBytes(csv), "text/csv",
    fileName)`. ATT-UPLOAD-001 is therefore unshippable as written. This test asserts the correct
    contract, so it FAILS until the controller is fixed and is deselected from the green gate via
    `-m "not known_defect"`."""
    response = get_roster_template(admin_api_client)
    assert response.status_code == 200, response.text
    assert "text/csv" in response.headers.get("Content-Type", "")
    assert "EmployeeCode" in response.text and "ShiftCode" in response.text


@allure.story("Foundation / Roster upload")
@allure.title("ATT-UPLOAD-002 — an empty upload file is rejected at the controller (400)")
@qa_cases("ATT-UPLOAD-002")
def test_roster_upload_empty_file_rejected(admin_api_client):
    response = validate_roster_upload(admin_api_client, "empty.csv", b"")
    assert response.status_code == 400, response.text


@allure.story("Foundation / Roster upload")
@allure.title("ATT-UPLOAD-003 — a header row not matching the required column order is caught")
@qa_cases("ATT-UPLOAD-003")
def test_roster_upload_bad_header(admin_api_client):
    response = validate_roster_upload(admin_api_client, "bad.csv", b"WrongCol1,WrongCol2\r\nx,y\r\n")
    assert response.status_code in (200, 400), response.text
    if response.status_code == 200:
        batch = response.json()["data"]
        allure.attach(str(batch.get("status")), name="upload batch status", attachment_type=allure.attachment_type.TEXT)
        assert batch["status"] in ("Failed", "Validated") and batch["validRows"] == 0


@allure.story("Foundation / Roster upload")
@allure.title("ATT-UPLOAD — roster upload validate requires RosterUpload: Employee/Manager get 403")
@qa_cases("ATT-UPLOAD-002")
def test_roster_upload_requires_permission(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        response = validate_roster_upload(client, "x.csv", b"EmployeeCode,Date,ShiftCode,DayType\r\n")
        assert response.status_code == 403, response.text


@allure.story("Foundation")
@allure.title("ATT-SHIFT-015 — a Tenant-A shifts read replayed at an unknown host is rejected 401")
@qa_cases("ATT-SHIFT-015")
def test_foundation_unknown_host_rejected(admin_api_client, api_client_factory, settings):
    token = admin_api_client._default_headers["Authorization"].split(" ", 1)[1]
    unknown = api_client_factory(settings.unknown_host).with_bearer(token)
    assert unknown.get("/api/attendance/shifts").status_code == 401
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert anon.get("/api/attendance/shifts").status_code == 401
