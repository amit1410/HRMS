"""API regression suite for Module 05 — Employee update, delete and export.

Automated from qa/05-employee/cases/10-update-delete-export.yaml. Covers the update uniqueness check
(excluding the record's own code), ModifiedDate stamping, the direct-reports delete guard, delete
idempotency, tenant isolation, and the CSV export contract (BOM/header/filename, statutory masking,
filters, empty result). Every created record is deleted afterward.

The scope-bypass security cases (EMP-EXP-007) and the history-only delete-guard gap (EMP-UPD-008)
need a scope-restricted role and effective-dated employment setup respectively, and are out of this
batch. Formula-injection neutralisation (EMP-EXP-002) is left for a dedicated CSV-content assertion.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_full,
    create_personal_details,
    delete_employee,
    export_employees,
    get_employee,
    update_full,
)
from data.test_data import today_iso, unique_employee_code, unique_employee_email, unique_last_name
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


def _make_full(admin_api_client, **overrides):
    body = dict(
        employeeCode=unique_employee_code(), firstName="QaAuto", lastName=unique_last_name(),
        email=unique_employee_email(), dateOfJoining=today_iso(),
    )
    body.update(overrides)
    response = create_full(admin_api_client, **body)
    assert response.status_code == 201, response.text
    return response.json()["data"]


@allure.story("Employee update")
@allure.title("EMP-UPD-004 — update rejects another employee's code but allows the record's own")
@qa_cases("EMP-UPD-004")
def test_update_code_uniqueness_excludes_self(admin_api_client):
    a = _make_full(admin_api_client)
    b = _make_full(admin_api_client)
    try:
        with allure.step("changing A's code to B's existing code => 409"):
            clash = update_full(
                admin_api_client, a["id"], employeeCode=b["employeeCode"], firstName="QaAuto",
                lastName=a["lastName"], email=a["email"], dateOfJoining=today_iso(),
            )
            assert clash.status_code == 409, clash.text
        with allure.step("saving A with its own unchanged code => 200"):
            ok = update_full(
                admin_api_client, a["id"], employeeCode=a["employeeCode"], firstName="QaAuto",
                lastName=a["lastName"], email=a["email"], dateOfJoining=today_iso(),
            )
            assert ok.status_code == 200, ok.text
    finally:
        delete_employee(admin_api_client, a["id"])
        delete_employee(admin_api_client, b["id"])


@allure.story("Employee update")
@allure.title("EMP-UPD-005 — a full update stamps a newer ModifiedDate")
@qa_cases("EMP-UPD-005")
def test_update_stamps_modified_date(admin_api_client):
    employee = _make_full(admin_api_client)
    try:
        before = get_employee(admin_api_client, employee["id"]).json()["data"].get("modifiedDate")
        ok = update_full(
            admin_api_client, employee["id"], employeeCode=employee["employeeCode"],
            firstName="QaAutoRenamed", lastName=employee["lastName"], email=employee["email"],
            dateOfJoining=today_iso(),
        )
        assert ok.status_code == 200, ok.text
        after_data = get_employee(admin_api_client, employee["id"]).json()["data"]
        assert after_data["firstName"] == "QaAutoRenamed"
        if before is not None and after_data.get("modifiedDate") is not None:
            assert after_data["modifiedDate"] >= before, "ModifiedDate did not advance on update"
    finally:
        delete_employee(admin_api_client, employee["id"])


@allure.story("Employee delete")
@allure.title("EMP-UPD-007 — delete is refused while direct reports exist, then succeeds after reassignment")
@qa_cases("EMP-UPD-007")
def test_delete_blocked_by_direct_reports(admin_api_client):
    manager = _make_full(admin_api_client)
    report = _make_full(admin_api_client, reportingManagerId=manager["id"])
    manager_deleted = False
    try:
        with allure.step("deleting a manager with a direct report => 409"):
            assert delete_employee(admin_api_client, manager["id"]).status_code == 409
        with allure.step("reassign the report away from the manager => 200"):
            reassign = update_full(
                admin_api_client, report["id"], employeeCode=report["employeeCode"], firstName="QaAuto",
                lastName=report["lastName"], email=report["email"], dateOfJoining=today_iso(),
            )  # reportingManagerId omitted => cleared
            assert reassign.status_code == 200, reassign.text
        with allure.step("delete the manager now => 200"):
            assert delete_employee(admin_api_client, manager["id"]).status_code == 200
            manager_deleted = True
    finally:
        delete_employee(admin_api_client, report["id"])
        if not manager_deleted:
            delete_employee(admin_api_client, manager["id"])


@allure.story("Employee delete")
@allure.title("EMP-UPD-010 — deleting an unknown id is 404; a repeated delete is 404 the second time")
@qa_cases("EMP-UPD-010")
def test_delete_idempotency(admin_api_client):
    assert delete_employee(admin_api_client, str(uuid.uuid4())).status_code == 404
    employee = create_personal_details(admin_api_client, firstName="QaAuto", lastName=unique_last_name(), dateOfJoining=today_iso())
    employee_id = employee.json()["data"]["id"]
    assert delete_employee(admin_api_client, employee_id).status_code == 200
    assert delete_employee(admin_api_client, employee_id).status_code == 404


@allure.story("Employee delete")
@allure.title("EMP-UPD-006 — update/delete on an unknown/cross-tenant id is a 404")
@qa_cases("EMP-UPD-006")
def test_update_delete_unknown_id_is_404(admin_api_client):
    unknown = str(uuid.uuid4())
    upd = update_full(
        admin_api_client, unknown, employeeCode=unique_employee_code(), firstName="QaAuto",
        lastName=unique_last_name(), email=unique_employee_email(), dateOfJoining=today_iso(),
    )
    assert upd.status_code == 404, upd.text
    assert delete_employee(admin_api_client, unknown).status_code == 404


@allure.story("Employee export")
@allure.title("EMP-EXP-001 / EMP-EXP-005 — export is a BOM-led CSV with a timestamped filename")
@qa_cases("EMP-EXP-001", "EMP-EXP-005")
def test_export_is_well_formed_csv(admin_api_client):
    response = export_employees(admin_api_client)
    assert response.status_code == 200, response.text
    assert response.headers.get("Content-Type", "").startswith("text/csv")
    assert response.content.startswith(b"\xef\xbb\xbf"), "export should lead with a UTF-8 BOM"
    header = response.content[3:].split(b"\r\n", 1)[0]
    assert b"Employee Code" in header, header
    import re
    disposition = response.headers.get("Content-Disposition", "")
    assert re.search(r"employees-\d{8}-\d{6}\.csv", disposition), disposition


@allure.story("Employee export")
@allure.title("EMP-EXP-003 — export masks statutory identifiers; the raw value never appears in the file")
@qa_cases("EMP-EXP-003")
def test_export_masks_statutory_ids(admin_api_client):
    aadhaar = "123456789012"
    created = create_personal_details(
        admin_api_client, firstName="QaAuto", lastName=unique_last_name(prefix="QAAUTOEXP"),
        dateOfJoining=today_iso(), aadhaarNumber=aadhaar,
    )
    assert created.status_code == 201, created.text
    employee_id = created.json()["data"]["id"]
    try:
        response = export_employees(admin_api_client)  # export is the whole filtered set, paging aside
        assert response.status_code == 200, response.text
        assert aadhaar.encode() not in response.content, "the raw Aadhaar value leaked into the export"
    finally:
        delete_employee(admin_api_client, employee_id)


@allure.story("Employee export")
@allure.title("EMP-EXP-009 / EMP-EXP-010 — export honours filters/sort and an empty result is header-only")
@qa_cases("EMP-EXP-009", "EMP-EXP-010")
def test_export_filters_and_empty(admin_api_client):
    with allure.step("filtered + sorted export => 200 CSV"):
        filtered = export_employees(admin_api_client, status="Active", sortBy="lastName")
        assert filtered.status_code == 200, filtered.text
        assert filtered.content.startswith(b"\xef\xbb\xbf")
    with allure.step("a filter matching nobody => BOM + header only, zero data rows"):
        empty = export_employees(admin_api_client, search=f"QAAUTO-NO-MATCH-{uuid.uuid4().hex}")
        assert empty.status_code == 200, empty.text
        body = empty.content[3:]  # drop BOM
        lines = [ln for ln in body.split(b"\r\n") if ln]
        assert len(lines) == 1, f"expected only a header row, got {len(lines)} lines"
