"""API smoke suite for Module 18 — Confirmation & Probation Policy (Phase 9a foundation).

Automated from qa/18-confirmation-probation/cases/*.yaml. Covers the policy master lifecycle
(list → create → draft version → publish), read-side resolution / employee confirmation status,
and the authorization boundary (unauthenticated 401, Employee-without-manage 403).

Requires QA_TENANT_A_HOST plus, per test: QA_A_ADMIN_USERNAME/PASSWORD (admin_api_client, holds
Confirmation.PolicyManage/Publish) and QA_A_EMPLOYEE_USERNAME/PASSWORD (employee_api_client, which
holds neither). Each fixture skips on its own if its pair is unset or sign-in fails. The suite
creates policies with unique codes (there is no delete endpoint in 9a); it never mutates another
tenant's or employee's data.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.confirmation_api import (
    create_confirmation_policy,
    create_confirmation_policy_version,
    get_confirmation_policy,
    get_confirmation_policy_editor,
    get_employee_confirmation_status,
    list_confirmation_policies,
    publish_confirmation_policy_version,
)
from core.employee_api import list_employees
from utils.env_utils import require_env

pytestmark = [allure.feature("Confirmation & Probation"), pytest.mark.api]


def _unique_code() -> str:
    return f"QACONF{uuid.uuid4().hex[:6].upper()}"


@allure.story("Confirmation policies")
@allure.title("CONF-POL-001 — GET /api/confirmation-policies returns a paged, IsActive-filtered list")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_confirmation_policies_list_is_readable(admin_api_client):
    response = list_confirmation_policies(admin_api_client, isActive=True)
    assert response.status_code == 200, response.text
    paged = response.json()["data"]
    assert "items" in paged and "totalCount" in paged
    assert all(item["isActive"] for item in paged["items"]), "Expected every item to be IsActive=true per the filter"


@allure.story("Confirmation policies")
@allure.title("CONF-POL-002 — Admin creates a policy, a draft version, and publishes it")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_policy_version_lifecycle(admin_api_client):
    code = _unique_code()
    created = create_confirmation_policy(admin_api_client, code=code, name="QA Automation probation", description="smoke", isActive=True)
    assert created.status_code == 201, created.text
    policy_id = created.json()["data"]["id"]

    fetched = get_confirmation_policy(admin_api_client, policy_id)
    assert fetched.status_code == 200, fetched.text
    assert fetched.json()["data"]["code"] == code

    version = create_confirmation_policy_version(
        admin_api_client,
        policy_id,
        effectiveFrom="2026-01-01",
        effectiveTo=None,
        priority=1,
        probation={"probationDuration": 6, "probationDurationUnit": "Months", "triggerDaysBeforeEnd": 30},
        applicability=[],
    )
    assert version.status_code == 200, version.text
    version_id = version.json()["data"]["version"]["id"]

    published = publish_confirmation_policy_version(admin_api_client, policy_id, version_id)
    assert published.status_code == 200, published.text
    assert published.json()["data"]["status"] == "Published", published.text

    editor = get_confirmation_policy_editor(admin_api_client, policy_id)
    assert editor.status_code == 200, editor.text
    assert editor.json()["data"]["probation"]["probationDuration"] == 6


@allure.story("Confirmation policies")
@allure.title("CONF-POL-003 — Duplicate policy code is rejected with a conflict")
@pytest.mark.regression
def test_duplicate_code_is_rejected(admin_api_client):
    code = _unique_code()
    first = create_confirmation_policy(admin_api_client, code=code, name="QA dup probe", isActive=True)
    assert first.status_code == 201, first.text
    second = create_confirmation_policy(admin_api_client, code=code.lower(), name="QA dup probe 2", isActive=True)
    assert second.status_code == 409, second.text


@allure.story("Employee confirmation status")
@allure.title("CONF-EMP-001 — Employee confirmation status is readable and reports policy-managed state")
@pytest.mark.regression
def test_employee_confirmation_status_is_readable(admin_api_client):
    employees = list_employees(admin_api_client, page=1, pageSize=1)
    assert employees.status_code == 200, employees.text
    items = employees.json()["data"]["items"]
    if not items:
        pytest.skip("No employees exist in this tenant to read a confirmation status for.")
    status = get_employee_confirmation_status(admin_api_client, items[0]["id"])
    assert status.status_code == 200, status.text
    body = status.json()["data"]
    assert "isPolicyManaged" in body and isinstance(body["isPolicyManaged"], bool)


@allure.story("Authorization")
@allure.title("CONF-AUTHZ-001 — no bearer token is rejected 401 on the confirmation surface")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_unauthenticated_call_is_rejected(api_client_factory):
    host = require_env("QA_TENANT_A_HOST")
    anonymous_client = api_client_factory(host)
    response = list_confirmation_policies(anonymous_client)
    assert response.status_code == 401, response.text


@allure.story("Authorization")
@allure.title("CONF-AUTHZ-002 — an Employee (no Confirmation.PolicyManage) gets 403 creating a policy")
@pytest.mark.smoke
@pytest.mark.regression
@pytest.mark.critical
def test_employee_cannot_manage_policies(employee_api_client):
    response = create_confirmation_policy(employee_api_client, code=_unique_code(), name="QA probe", isActive=True)
    assert response.status_code == 403, response.text
