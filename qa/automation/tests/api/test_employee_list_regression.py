"""API regression suite for Module 05 — Employee directory: list, search, filter, sort, paging.

Automated from qa/05-employee/cases/01-employee-list.yaml. Every test maps to one or more manual
QA case ids (see the `@qa_cases(...)` labels / titles) and asserts the documented contract against a
live tenant. Assertions are on status codes and structural invariants (never on another tenant's
data values); exact validation-message wording is intentionally matched loosely (a keyword) so a
cosmetic copy change does not turn into a false regression.

Setup and cleanup use only records this run itself creates (unique `QAAUTO` markers, deleted in a
`finally`); no pre-existing employee is ever read for its values, edited, or deleted. Cross-tenant
(A+B) isolation is exercised through the nonexistent-id path the catalogue documents as identical to
a cross-tenant id, because this environment provisions a single QA tenant.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_full,
    delete_employee,
    list_employees,
)
from data.test_data import (
    today_iso,
    unique_employee_code,
    unique_employee_email,
    unique_last_name,
)
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


def _items(response):
    return response.json()["data"]["items"]


@allure.story("Employee directory")
@allure.title("EMP-LIST-001 — default list is page 1 of 20, ordered by employee code ascending")
@qa_cases("EMP-LIST-001")
def test_default_list_page_and_ordering(admin_api_client):
    response = list_employees(admin_api_client)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1
    assert data["pageSize"] == 20
    assert len(data["items"]) <= 20
    codes = [(item.get("employeeCode") or "") for item in data["items"]]
    assert codes == sorted(codes), f"Expected employee codes ascending, got {codes}"


@allure.story("Employee directory")
@allure.title("EMP-LIST-002 — page/pageSize bounds are rejected with 400, not clamped")
@qa_cases("EMP-LIST-002")
def test_paging_bounds_validation(admin_api_client):
    with allure.step("page=0 => 400"):
        assert list_employees(admin_api_client, page=0).status_code == 400
    with allure.step("pageSize=0 => 400"):
        assert list_employees(admin_api_client, pageSize=0).status_code == 400
    with allure.step("pageSize=101 => 400"):
        assert list_employees(admin_api_client, pageSize=101).status_code == 400
    with allure.step("pageSize=100 => 200, at most 100 items"):
        ok = list_employees(admin_api_client, pageSize=100)
        assert ok.status_code == 200, ok.text
        assert len(_items(ok)) <= 100
    with allure.step("no pageSize => defaults to 20"):
        default = list_employees(admin_api_client)
        assert default.json()["data"]["pageSize"] == 20


@allure.story("Employee directory")
@allure.title("EMP-LIST-003 — a page beyond the last returns an empty list with the real total, not an error")
@qa_cases("EMP-LIST-003")
def test_page_beyond_last_is_empty(admin_api_client):
    response = list_employees(admin_api_client, page=999, pageSize=20)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    # An out-of-range page is an empty page, not an error, and still reports the real (non-negative)
    # total for the query. The exact value isn't asserted because a parallel run mutates the count.
    assert data["items"] == []
    assert isinstance(data["totalCount"], int) and data["totalCount"] >= 0


@allure.story("Employee directory")
@allure.title("EMP-LIST-004 / EMP-LIST-015 — search matches code, name and email case-insensitively; a miss is empty")
@qa_cases("EMP-LIST-004", "EMP-LIST-015")
def test_search_matches_code_name_email(admin_api_client):
    code = unique_employee_code()
    last_name = unique_last_name()
    email = unique_employee_email()
    created = create_full(
        admin_api_client,
        employeeCode=code, firstName="QaAutoSearch", lastName=last_name,
        email=email, dateOfJoining=today_iso(),
    )
    assert created.status_code == 201, created.text
    employee_id = created.json()["data"]["id"]
    try:
        with allure.step("search by employee code, uppercased"):
            found = list_employees(admin_api_client, search=code.upper())
            assert any(i["id"] == employee_id for i in _items(found)), "employee not found by code search"
        with allure.step("search by last name, lowercased (case-insensitive)"):
            found = list_employees(admin_api_client, search=last_name.lower())
            assert any(i["id"] == employee_id for i in _items(found)), "employee not found by name search"
        with allure.step("search by an email fragment"):
            fragment = email.split("@")[0]
            found = list_employees(admin_api_client, search=fragment)
            assert any(i["id"] == employee_id for i in _items(found)), "employee not found by email search"
        with allure.step("search matching nobody => empty"):
            miss = list_employees(admin_api_client, search=f"QAAUTO-NO-SUCH-{uuid.uuid4().hex}")
            assert _items(miss) == []
    finally:
        delete_employee(admin_api_client, employee_id)


@allure.story("Employee directory")
@allure.title("EMP-LIST-005 — search text over 100 characters is rejected; exactly 100 is accepted")
@qa_cases("EMP-LIST-005")
def test_search_length_boundary(admin_api_client):
    assert list_employees(admin_api_client, search="a" * 101).status_code == 400
    assert list_employees(admin_api_client, search="a" * 100).status_code == 200


@allure.story("Employee directory")
@allure.title("EMP-LIST-007 — an unrecognized status filter is a 400, not silently ignored")
@qa_cases("EMP-LIST-007")
def test_invalid_status_filter_is_rejected(admin_api_client):
    response = list_employees(admin_api_client, status="NotARealStatus")
    assert response.status_code == 400, response.text


@allure.story("Employee directory")
@allure.title("EMP-LIST-008 — whitelisted sort fields work both directions; an unlisted field is a 400")
@qa_cases("EMP-LIST-008")
def test_sort_whitelist(admin_api_client):
    assert list_employees(admin_api_client, sortBy="lastName", sortDescending=False).status_code == 200
    assert list_employees(admin_api_client, sortBy="dateOfJoining", sortDescending=True).status_code == 200
    assert list_employees(admin_api_client, sortBy="notAField").status_code == 400


@allure.story("Employee directory")
@allure.title("EMP-LIST-006 / EMP-LIST-016 — status filter returns only matching rows; combined filters are an AND")
@qa_cases("EMP-LIST-006", "EMP-LIST-016")
def test_status_filter_and_combination(admin_api_client):
    with allure.step("status=Active returns only Active rows"):
        active = list_employees(admin_api_client, status="Active", pageSize=100)
        assert active.status_code == 200, active.text
        assert all(i["status"] == "Active" for i in _items(active)), "a non-Active row leaked into a status=Active filter"
    with allure.step("status + a random departmentId together never widen the result (AND semantics)"):
        combined = list_employees(
            admin_api_client, status="Active", departmentId=str(uuid.uuid4()), pageSize=100,
        )
        assert combined.status_code == 200, combined.text
        # A department id that matches nobody must not return anyone despite status matching many.
        assert _items(combined) == [], "combining a non-matching department with status returned rows (OR, not AND)"


@allure.story("Employee directory")
@allure.title("EMP-LIST-011 — 'no employees at all' is distinguishable from 'filtered to empty'")
@qa_cases("EMP-LIST-011")
def test_filtered_empty_vs_truly_empty(admin_api_client):
    unfiltered_total = list_employees(admin_api_client).json()["data"]["totalCount"]
    if unfiltered_total == 0:
        pytest.skip("Tenant has no employees at all — cannot contrast filtered-empty with truly-empty.")
    # A department that matches nobody: items empty but the unfiltered total is still positive.
    filtered = list_employees(admin_api_client, departmentId=str(uuid.uuid4()))
    assert filtered.status_code == 200, filtered.text
    assert _items(filtered) == []
    assert unfiltered_total > 0


@allure.story("Employee directory")
@allure.title("EMP-SEC-011 — a malformed GUID in the route is a routing 404, not a validation 400")
@qa_cases("EMP-SEC-011")
def test_malformed_guid_is_routing_404(admin_api_client):
    response = admin_api_client.get("/api/employees/not-a-guid")
    assert response.status_code == 404, response.text


@allure.story("Employee directory")
@allure.title("EMP-SEC-006 — a nonexistent/cross-tenant employee id is reported as 404 (identical treatment)")
@qa_cases("EMP-SEC-006")
def test_unknown_employee_id_is_404(admin_api_client):
    # The catalogue documents a cross-tenant id as indistinguishable from a fabricated one; this
    # environment has a single QA tenant, so the fabricated-GUID path is the observable contract.
    response = admin_api_client.get(f"/api/employees/{uuid.uuid4()}")
    assert response.status_code == 404, response.text
