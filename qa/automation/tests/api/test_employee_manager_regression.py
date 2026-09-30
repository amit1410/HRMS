"""API regression suite for Module 05 — Reporting manager / supervisor hierarchy.

Automated from qa/05-employee/cases/07-manager-supervisors.yaml. Covers the L1-controlled-by-
Employment rule, the freely-assignable L2-L5/Time/ERO/CHRO peer fields, self-reference and
cross-tenant rejection, the supervisor-options candidate source (including the slots that have no
source), and the legacy self-report guard.

The employment-history-driven cases (EMP-MGR-002/008/009/010/011/018) require multi-step effective-
dated setup with PositionChangeReason master data and are out of this batch.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_full,
    create_personal_details,
    delete_employee,
    get_supervisor,
    get_supervisor_options,
    update_full,
    upsert_supervisor,
)
from data.test_data import today_iso, unique_employee_code, unique_employee_email, unique_last_name
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


@pytest.fixture
def make_employee(admin_api_client):
    """Factory creating disposable Active employees (personal-details path), all deleted on teardown."""
    created: list[str] = []

    def _make():
        response = create_personal_details(
            admin_api_client, firstName="QaAuto", lastName=unique_last_name(prefix="QAAUTOMGR"), dateOfJoining=today_iso(),
        )
        assert response.status_code == 201, response.text
        employee_id = response.json()["data"]["id"]
        created.append(employee_id)
        return employee_id

    yield _make

    for employee_id in created:
        delete_employee(admin_api_client, employee_id)


@allure.story("Reporting manager")
@allure.title("EMP-MGR-003 — with no Employment manager, L1 must be left null on the supervisor endpoint")
@qa_cases("EMP-MGR-003")
def test_l1_controlled_by_employment(admin_api_client, created_employee, make_employee):
    employee_id = created_employee["id"]
    someone = make_employee()
    with allure.step("supplying any l1ManagerId when Employment has no manager => 400"):
        assert upsert_supervisor(admin_api_client, employee_id, l1ManagerId=someone).status_code == 400
    with allure.step("omitting l1ManagerId => 200"):
        assert upsert_supervisor(admin_api_client, employee_id, l2ManagerId=None).status_code == 200


@allure.story("Reporting manager")
@allure.title("EMP-MGR-005 — L2-L5/Time/ERO/CHRO are freely assignable peer fields and persist as submitted")
@qa_cases("EMP-MGR-005")
def test_peer_supervisor_fields(admin_api_client, created_employee, make_employee):
    employee_id = created_employee["id"]
    m = [make_employee() for _ in range(3)]
    assignment = dict(
        l2ManagerId=m[0], l3ManagerId=m[1], l4ManagerId=m[2],
        l5ManagerId=m[0], timeManagerId=m[1], eroId=m[2], chroManagerId=m[0],
    )
    response = upsert_supervisor(admin_api_client, employee_id, **assignment)
    assert response.status_code == 200, response.text
    supervisor = get_supervisor(admin_api_client, employee_id).json()["data"]
    for field, expected in assignment.items():
        assert supervisor[field] == expected, f"{field} did not persist as submitted"


@allure.story("Reporting manager")
@allure.title("EMP-MGR-006 — an employee cannot be their own supervisor at any level")
@qa_cases("EMP-MGR-006")
def test_self_reference_rejected(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    assert upsert_supervisor(admin_api_client, employee_id, l2ManagerId=employee_id).status_code == 400
    assert upsert_supervisor(admin_api_client, employee_id, timeManagerId=employee_id).status_code == 400


@allure.story("Reporting manager")
@allure.title("EMP-MGR-007 / EMP-SEC-009 — a nonexistent/cross-tenant supervisor id is rejected with 400")
@qa_cases("EMP-MGR-007", "EMP-SEC-009")
def test_invalid_supervisor_reference_rejected(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    assert upsert_supervisor(admin_api_client, employee_id, chroManagerId=str(uuid.uuid4())).status_code == 400
    assert upsert_supervisor(admin_api_client, employee_id, eroId=str(uuid.uuid4())).status_code == 400


@allure.story("Reporting manager")
@allure.title("EMP-MGR-013 / EMP-MGR-014 / EMP-MGR-015 — supervisor-options: valid type lists, bad/unsourced types are 400")
@qa_cases("EMP-MGR-013", "EMP-MGR-014", "EMP-MGR-015")
def test_supervisor_options(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("type=L1 => 200 with a (possibly empty) candidate list"):
        ok = get_supervisor_options(admin_api_client, employee_id, "L1")
        assert ok.status_code == 200, ok.text
        assert isinstance(ok.json()["data"], list)
    with allure.step("type=NotARealType => 400"):
        assert get_supervisor_options(admin_api_client, employee_id, "NotARealType").status_code == 400
    with allure.step("type=L4 and type=Ero have no candidate source => 400"):
        assert get_supervisor_options(admin_api_client, employee_id, "L4").status_code == 400
        assert get_supervisor_options(admin_api_client, employee_id, "Ero").status_code == 400


@allure.story("Reporting manager")
@allure.title("EMP-MGR-019 — self-report through the legacy full-edit path is rejected")
@qa_cases("EMP-MGR-019")
def test_legacy_self_report_rejected(admin_api_client):
    created = create_full(
        admin_api_client, employeeCode=unique_employee_code(), firstName="QaAuto",
        lastName=unique_last_name(), email=unique_employee_email(), dateOfJoining=today_iso(),
    )
    assert created.status_code == 201, created.text
    employee = created.json()["data"]
    try:
        response = update_full(
            admin_api_client, employee["id"], employeeCode=employee["employeeCode"],
            firstName="QaAuto", lastName=employee["lastName"], email=employee["email"],
            dateOfJoining=today_iso(), reportingManagerId=employee["id"],
        )
        assert response.status_code == 400, response.text
    finally:
        delete_employee(admin_api_client, employee["id"])


@allure.story("Reporting manager")
@allure.title("EMP-MGR-017 — supervisor GET/PUT on an unknown/cross-tenant employee id is a 404")
@qa_cases("EMP-MGR-017")
def test_supervisor_unknown_employee_is_404(admin_api_client):
    unknown = uuid.uuid4()
    assert get_supervisor(admin_api_client, str(unknown)).status_code == 404
    assert upsert_supervisor(admin_api_client, str(unknown), l2ManagerId=None).status_code == 404
