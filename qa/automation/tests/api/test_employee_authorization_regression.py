"""API regression suite for Module 05 — authorization, tenant isolation and account linking.

Automated from qa/05-employee/cases/12-authz-tenant-security.yaml and the account-linking surface
reachable from the Employee module. Covers the AccountEmployeeLink separation-of-duties carve-out
(reserved even from TenantAdmin), the Manager-role list-scope narrowing, and the single-grant-covers-
every-un-dedicated-sub-resource boundary.

EMP-SEC-002 (401 vs 403) is already covered by the Employee smoke suite and is not duplicated here.
Role-specific splits needing an Employee.View-without-EmployeeSensitive user are out of this batch.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.employee_api import (
    create_family,
    get_link_candidates,
    get_link_for_user,
    list_employees,
    upsert_contact,
)
from data.test_data import unique_employee_email
from utils.allure_evidence import qa_cases

pytestmark = [allure.feature("Employee Management"), pytest.mark.api, pytest.mark.regression]


@allure.story("Authorization: account linking")
@allure.title("EMP-SEC-012 — AccountEmployeeLink.* is refused even to TenantAdmin (separation of duties)")
@qa_cases("EMP-SEC-012")
def test_account_link_endpoints_denied_to_tenant_admin(admin_api_client):
    with allure.step("candidate-users listing => 403"):
        assert get_link_candidates(admin_api_client, "users").status_code == 403
    with allure.step("candidate-employees listing => 403"):
        assert get_link_candidates(admin_api_client, "employees").status_code == 403
    with allure.step("link state for a user => 403"):
        assert get_link_for_user(admin_api_client, str(uuid.uuid4())).status_code == 403


@allure.story("Authorization: list scope")
@allure.title("EMP-LIST-013 — a Manager-role caller is scoped to a subset, not the whole tenant directory")
@qa_cases("EMP-LIST-013")
def test_manager_list_is_scoped(admin_api_client, manager_api_client):
    admin_total = list_employees(admin_api_client).json()["data"]["totalCount"]
    manager_response = list_employees(manager_api_client)
    assert manager_response.status_code == 200, manager_response.text
    manager_total = manager_response.json()["data"]["totalCount"]
    assert manager_total >= 1, "the manager should at least see themselves / their reports"
    assert manager_total < admin_total, (
        f"the Manager-scoped list ({manager_total}) should be narrower than the full tenant directory ({admin_total})"
    )


@allure.story("Authorization: resource boundary")
@allure.title("EMP-SEC-004 — one Employee.Edit grant covers every un-dedicated sub-resource")
@qa_cases("EMP-SEC-004")
def test_single_grant_covers_subresources(admin_api_client, created_employee):
    employee_id = created_employee["id"]
    with allure.step("Contact write is accepted"):
        assert upsert_contact(admin_api_client, employee_id, officialEmail=unique_employee_email()).status_code == 200
    with allure.step("Family write (same grant) is also accepted"):
        assert create_family(
            admin_api_client, employee_id, firstName="Jane", lastName="Doe", relationship="Spouse",
        ).status_code == 201
