"""API regression suite for Module 05 — Employee create (legacy full record + personal-details path).

Automated from qa/05-employee/cases/02-employee-create-core.yaml. Uniqueness, request-shape and
cross-field validation are exercised end to end against a live tenant. Every created record carries a
unique `QAAUTO` marker and is deleted in a `finally`.

Cross-tenant reuse steps (EMP-CREATE-004 step 3, EMP-CREATE-002/003 with a Tenant-B reference) are
not automatable against a single provisioned QA tenant; the invalid-reference contract is exercised
through the fabricated-GUID path the catalogue documents as identical.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import create_full, create_personal_details, delete_employee
from data.test_data import (
    today_iso,
    unique_employee_code,
    unique_employee_email,
    unique_last_name,
)
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


def _base_full(**overrides):
    body = dict(
        employeeCode=unique_employee_code(),
        firstName="QaAuto",
        lastName=unique_last_name(),
        email=unique_employee_email(),
        dateOfJoining=today_iso(),
    )
    body.update(overrides)
    return body


@allure.story("Employee create")
@allure.title("EMP-CREATE-004 — a duplicate email (any case) is rejected with 409")
@qa_cases("EMP-CREATE-004")
def test_duplicate_email_rejected(admin_api_client):
    email = unique_employee_email()
    first = create_full(admin_api_client, **_base_full(email=email))
    assert first.status_code == 201, first.text
    employee_id = first.json()["data"]["id"]
    try:
        dup = create_full(admin_api_client, **_base_full(email=email.upper()))
        assert dup.status_code == 409, dup.text
    finally:
        delete_employee(admin_api_client, employee_id)


@allure.story("Employee create")
@allure.title("EMP-CREATE-005 — personal-details create needs only personal fields; no code/dept/manager assigned")
@qa_cases("EMP-CREATE-005")
def test_personal_details_create_is_minimal(admin_api_client):
    response = create_personal_details(
        admin_api_client, firstName="QaAuto", lastName=unique_last_name(), dateOfJoining=today_iso(),
    )
    assert response.status_code == 201, response.text
    data = response.json()["data"]
    try:
        assert not data.get("employeeCode"), "personal-details create should not assign an employee code yet"
        assert data.get("departmentId") is None
        assert data.get("designationId") is None
        assert data.get("reportingManagerId") is None
    finally:
        delete_employee(admin_api_client, data["id"])


@allure.story("Employee create")
@allure.title("EMP-CREATE-008 — birth country/state/city cascade is enforced")
@qa_cases("EMP-CREATE-008")
def test_birth_location_cascade(admin_api_client):
    with allure.step("birth state without a birth country => 400"):
        r = create_personal_details(
            admin_api_client, firstName="QaAuto", lastName=unique_last_name(),
            dateOfJoining=today_iso(), birthStateId=str(uuid.uuid4()),
        )
        assert r.status_code == 400, r.text
    with allure.step("birth city without a birth state => 400"):
        r = create_personal_details(
            admin_api_client, firstName="QaAuto", lastName=unique_last_name(),
            dateOfJoining=today_iso(), birthCityId=str(uuid.uuid4()),
        )
        assert r.status_code == 400, r.text


@allure.story("Employee create")
@allure.title("EMP-CREATE-009 — legacy create enforces code format/length, email and phone shape")
@qa_cases("EMP-CREATE-009")
def test_legacy_create_request_shape(admin_api_client):
    with allure.step("employee code with a leading special char => 400"):
        assert create_full(admin_api_client, **_base_full(employeeCode="#ABC")).status_code == 400
    with allure.step("employee code longer than 20 chars => 400"):
        assert create_full(admin_api_client, **_base_full(employeeCode="A" * 21)).status_code == 400
    with allure.step("malformed email => 400"):
        assert create_full(admin_api_client, **_base_full(email="not-an-email")).status_code == 400
    with allure.step("phone with letters => 400"):
        assert create_full(admin_api_client, **_base_full(phone="abc***")).status_code == 400


@allure.story("Employee create")
@allure.title("EMP-CREATE-010 — DateOfLeaving/Status cross-field rules are enforced")
@qa_cases("EMP-CREATE-010")
def test_leaving_date_status_cross_field(admin_api_client):
    with allure.step("Active with a leaving date => 400"):
        r = create_full(admin_api_client, **_base_full(status="Active", dateOfLeaving=today_iso()))
        assert r.status_code == 400, r.text
    with allure.step("Resigned without a leaving date => 400"):
        r = create_full(admin_api_client, **_base_full(status="Resigned"))
        assert r.status_code == 400, r.text
    with allure.step("Resigned with a leaving date before joining => 400"):
        r = create_full(admin_api_client, **_base_full(
            status="Resigned", dateOfJoining="2024-06-01", dateOfLeaving="2024-05-01"))
        assert r.status_code == 400, r.text


@allure.story("Employee create")
@allure.title("EMP-CREATE-011 — status defaults to Active when omitted")
@qa_cases("EMP-CREATE-011")
def test_status_defaults_to_active(admin_api_client):
    response = create_full(admin_api_client, **_base_full())  # no status field
    assert response.status_code == 201, response.text
    data = response.json()["data"]
    try:
        assert data["status"] == "Active"
    finally:
        delete_employee(admin_api_client, data["id"])


@allure.story("Employee create")
@allure.title("EMP-CREATE-002/003 — a nonexistent department reference is rejected with 400")
@qa_cases("EMP-CREATE-002", "EMP-CREATE-003")
def test_invalid_department_reference_rejected(admin_api_client):
    # A cross-tenant id is reported identically to a fabricated one (single QA tenant here).
    response = create_full(admin_api_client, **_base_full(departmentId=str(uuid.uuid4())))
    assert response.status_code == 400, response.text
