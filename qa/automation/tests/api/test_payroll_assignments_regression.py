"""API regression suite for Module 10 — Employee Salary Assignment.

Automated from qa/10-payroll/cases/03-employee-salary-assignment.yaml (PAY-ESA-001..017). Statuses
and messages confirmed live before writing. Assignments have no delete endpoint, so each is
deactivated best-effort in teardown; the QAAUTO employees created here are deleted best-effort.

PAY-ESA-012 pins CR-171 (a re-verified risk-register entry, not a new defect): the assignment's
SetActiveAsync writes a fresh history row even on a no-op status change — deliberately contrasted
with the idempotent SalaryComponent/SalaryStructure behavior.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.payroll_api import (
    create_employee_salary_assignment,
    deactivate_salary_structure,
    get_assignments_by_employee,
    get_effective_assignment,
    get_employee_salary_assignment,
    get_employee_salary_assignment_history,
    list_employee_salary_assignments,
    set_employee_salary_assignment_active,
    update_employee_salary_assignment,
)
from data.test_data import today_iso
from utils.allure_evidence import cr_refs, qa_cases
from utils.payroll_data import (
    cleanup_assignment,
    cleanup_component,
    cleanup_employee,
    cleanup_structure,
    make_assignment,
    make_component,
    make_employee,
    make_structure,
)


pytestmark = [allure.feature("Payroll"), allure.story("Employee Salary Assignment"), pytest.mark.api, pytest.mark.regression]


@pytest.fixture
def component(admin_api_client):
    row = make_component(admin_api_client)
    yield row
    cleanup_component(admin_api_client, row["id"])


@pytest.fixture
def structure(admin_api_client, component):
    row = make_structure(admin_api_client, component["id"], editable=True)
    yield row
    cleanup_structure(admin_api_client, row["id"])


@pytest.fixture
def employee(admin_api_client):
    row = make_employee(admin_api_client)
    yield row
    cleanup_employee(admin_api_client, row["id"])


@pytest.fixture
def assignment(admin_api_client, employee, structure):
    row = make_assignment(admin_api_client, employee["id"], structure["id"])
    yield row
    cleanup_assignment(admin_api_client, row["id"])


def _body(employee_id, structure_id, **overrides):
    body = {
        "employeeId": employee_id, "salaryStructureId": structure_id, "effectiveFrom": today_iso(),
        "annualCtc": 600000, "monthlyCtc": 50000, "currencyCode": "INR", "payFrequency": "Monthly",
        "status": "Active", "changeReason": "NewHire",
    }
    body.update(overrides)
    return body


@allure.title("PAY-ESA-001 — a well-formed assignment binds the employee to the structure and CTC")
@qa_cases("PAY-ESA-001")
@pytest.mark.critical
def test_create_valid_assignment(assignment, employee, structure):
    assert assignment["status"] == "Active"
    assert assignment["employeeId"] == employee["id"]
    assert assignment["salaryStructureId"] == structure["id"]


@allure.title("PAY-ESA-002 — inverted dates, negative CTC and a non-3-letter currency are rejected")
@qa_cases("PAY-ESA-002")
def test_request_validation(admin_api_client, employee, structure):
    inverted = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"], effectiveFrom="2026-06-01", effectiveTo="2026-01-01"))
    assert inverted.status_code == 400 and "EffectiveTo cannot be earlier than EffectiveFrom." in inverted.text, inverted.text

    negative = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"], annualCtc=-1))
    assert negative.status_code == 400 and "CTC values cannot be negative." in negative.text, negative.text

    currency = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"], currencyCode="IN"))
    assert currency.status_code == 400 and "CurrencyCode must be a three-letter code." in currency.text, currency.text


@allure.title("PAY-ESA-003 — an unknown employee is 404 and an inactive structure is 409")
@qa_cases("PAY-ESA-003")
def test_employee_and_structure_must_exist(admin_api_client, employee, structure):
    unknown = create_employee_salary_assignment(
        admin_api_client, **_body(str(uuid.uuid4()), structure["id"]))
    assert unknown.status_code == 404 and "Employee not found." in unknown.text, unknown.text

    deactivate_salary_structure(admin_api_client, structure["id"])
    inactive = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"]))
    assert inactive.status_code == 409 and "The Salary Structure is inactive." in inactive.text, inactive.text


@allure.title("PAY-ESA-004 — an EffectiveFrom with no active structure version is rejected 400")
@qa_cases("PAY-ESA-004")
def test_no_effective_structure_version(admin_api_client, employee, structure):
    response = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"], effectiveFrom="2019-01-01"))
    assert response.status_code == 400, response.text
    assert "No single active Salary Structure version is effective on the assignment date." in response.text


@allure.title("PAY-ESA-005 — an employee cannot have two overlapping active assignments")
@qa_cases("PAY-ESA-005")
@pytest.mark.critical
def test_overlapping_assignment_rejected(admin_api_client, assignment, employee, structure):
    second = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"], effectiveFrom="2026-06-01"))
    assert second.status_code == 409, second.text
    assert "The employee already has an overlapping salary assignment." in second.text


@allure.title("PAY-ESA-006 — GetEffective returns 404 before hire and the assignment on a covered date")
@qa_cases("PAY-ESA-006")
def test_get_effective_resolution(admin_api_client, assignment, employee):
    before = get_effective_assignment(admin_api_client, employee["id"], date="2019-01-01")
    assert before.status_code == 404, before.text
    assert "No effective salary assignment exists for this employee and date." in before.text

    covered = get_effective_assignment(admin_api_client, employee["id"], date=today_iso())
    assert covered.status_code == 200 and covered.json()["data"]["id"] == assignment["id"], covered.text


@allure.title("PAY-ESA-007 — an override on a non-editable structure component is 403")
@qa_cases("PAY-ESA-007")
def test_override_non_editable_forbidden(admin_api_client, employee, component):
    non_editable = make_structure(admin_api_client, component["id"], editable=False)
    try:
        scc = non_editable["components"][0]["id"]
        response = create_employee_salary_assignment(
            admin_api_client, **_body(employee["id"], non_editable["id"],
                                      components=[{"salaryStructureComponentId": scc, "overrideValue": 1000}]))
        assert response.status_code == 403, response.text
        assert "This Salary Structure component does not allow employee-level overrides." in response.text
    finally:
        cleanup_structure(admin_api_client, non_editable["id"])


@allure.title("PAY-ESA-008 — a FixedAmount override carrying a percentage is rejected 400")
@qa_cases("PAY-ESA-008")
def test_override_shape_must_match_type(admin_api_client, employee, structure):
    scc = structure["components"][0]["id"]
    response = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"],
                                  components=[{"salaryStructureComponentId": scc, "overridePercentage": 50}]))
    assert response.status_code == 400, response.text
    assert "A fixed amount override requires a non-negative value." in response.text


@allure.title("PAY-ESA-009 — two overrides for the same structure component are rejected 400")
@qa_cases("PAY-ESA-009")
def test_duplicate_overrides_rejected(admin_api_client, employee, structure):
    scc = structure["components"][0]["id"]
    response = create_employee_salary_assignment(
        admin_api_client, **_body(employee["id"], structure["id"], components=[
            {"salaryStructureComponentId": scc, "overrideValue": 1000},
            {"salaryStructureComponentId": scc, "overrideValue": 2000}]))
    assert response.status_code == 400, response.text
    assert "Duplicate employee salary overrides are not allowed." in response.text


@allure.title("PAY-ESA-010 — an update with a stale ExpectedConcurrencyVersion is 409")
@qa_cases("PAY-ESA-010")
def test_stale_concurrency_on_update(admin_api_client, assignment, employee, structure):
    response = update_employee_salary_assignment(
        admin_api_client, assignment["id"], **_body(employee["id"], structure["id"],
                                                     effectiveFrom=assignment["effectiveFrom"],
                                                     expectedConcurrencyVersion=assignment["concurrencyVersion"] + 99))
    assert response.status_code == 409, response.text
    assert "The employee salary assignment was changed by another user." in response.text


@allure.title("PAY-ESA-012 — SetActiveAsync writes a history row even on a no-op (CR-171)")
@qa_cases("PAY-ESA-012")
@cr_refs("CR-171")
def test_activate_active_still_writes_history(admin_api_client, assignment):
    baseline = len(get_employee_salary_assignment_history(admin_api_client, assignment["id"]).json()["data"])
    activated = set_employee_salary_assignment_active(admin_api_client, assignment["id"], True)
    assert activated.status_code == 200 and activated.json()["data"]["status"] == "Active", activated.text
    after = len(get_employee_salary_assignment_history(admin_api_client, assignment["id"]).json()["data"])
    assert after == baseline + 1, (
        "CR-171: activating an already-Active assignment must still append a history row "
        f"(baseline {baseline}, after {after})")


@allure.title("PAY-ESA-015 — history for an unknown assignment id is 404")
@qa_cases("PAY-ESA-015")
def test_history_missing_is_404(admin_api_client):
    response = get_employee_salary_assignment_history(admin_api_client, str(uuid.uuid4()))
    assert response.status_code == 404, response.text
    assert "Employee salary assignment not found." in response.text


@allure.title("PAY-ESA-016 — an assignment id not in this tenant is 404")
@qa_cases("PAY-ESA-016")
@pytest.mark.critical
def test_foreign_assignment_id_is_404(admin_api_client):
    response = get_employee_salary_assignment(admin_api_client, str(uuid.uuid4()))
    assert response.status_code == 404, response.text
    assert "Employee salary assignment not found." in response.text


@allure.title("PAY-ESA-017 — list/filter and by-employee read paths")
@qa_cases("PAY-ESA-017")
def test_list_and_by_employee(admin_api_client, assignment, employee):
    listed = list_employee_salary_assignments(admin_api_client, employeeId=employee["id"], status="Active")
    assert listed.status_code == 200, listed.text
    assert all(item["employeeId"] == employee["id"] for item in listed.json()["data"]["items"])
    assert any(item["id"] == assignment["id"] for item in listed.json()["data"]["items"])

    scoped = get_assignments_by_employee(admin_api_client, employee["id"])
    assert scoped.status_code == 200, scoped.text
    assert any(item["id"] == assignment["id"] for item in scoped.json()["data"]["items"])
