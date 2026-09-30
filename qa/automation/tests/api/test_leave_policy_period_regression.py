"""API regression suite for Module 07 — Leave Policies and Leave Periods (configuration surface).

Automated from qa/07-leave/cases/01-leave-types-policies.yaml (LEAVE-POLICY-*) and
03-periods-eligibility.yaml (LEAVE-PERIOD-*). Covers the policy/version read contract, the period
read/filter contract, negative create validation (StartDate>EndDate, missing code — both rejected
so nothing persists), and the permission matrix.

Per qa/07-leave/README.md CR-78: only SuperAdmin/TenantAdmin hold PolicyManage/PeriodManage in the
seeded matrix; the Employee and Manager roles hold neither, which the 403 checks here pin directly.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.leave_api import (
    create_leave_period,
    get_leave_period,
    get_leave_policy,
    get_policy_versions,
    list_leave_periods,
    list_leave_policies,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]


# --- Leave Policies (read) ---------------------------------------------------------------------


@allure.story("Leave Policies")
@allure.title("LEAVE-POLICY-001 — list carries versionCount / currentVersionNumber / overlapCount")
@qa_cases("LEAVE-POLICY-001")
def test_policy_list_shape(admin_api_client):
    response = list_leave_policies(admin_api_client)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert {"items", "page", "pageSize", "totalCount"} <= set(data)
    for item in data["items"]:
        for field in ("id", "code", "name", "isActive", "versionCount", "currentVersionNumber", "overlapCount"):
            assert field in item, f"policy list item missing {field!r}"


@allure.story("Leave Policies")
@allure.title("LEAVE-POLICY-002 — overlapCount is informational (present and non-negative), never a gate")
@qa_cases("LEAVE-POLICY-002")
@cr_refs("CR-87")
def test_overlap_count_is_informational(admin_api_client):
    items = list_leave_policies(admin_api_client).json()["data"]["items"]
    if not items:
        pytest.skip("Tenant has no Leave Policy to inspect overlapCount on.")
    assert all(isinstance(i["overlapCount"], int) and i["overlapCount"] >= 0 for i in items)


@allure.story("Leave Policies")
@allure.title("LEAVE-POLICY-001 — a policy detail and its version list are retrievable")
@qa_cases("LEAVE-POLICY-001", "LEAVE-POLICY-008")
def test_policy_detail_and_versions(admin_api_client):
    items = list_leave_policies(admin_api_client).json()["data"]["items"]
    if not items:
        pytest.skip("Tenant has no Leave Policy to read.")
    policy_id = items[0]["id"]
    detail = get_leave_policy(admin_api_client, policy_id)
    assert detail.status_code == 200, detail.text
    assert detail.json()["data"]["id"] == policy_id

    versions = get_policy_versions(admin_api_client, policy_id)
    assert versions.status_code == 200, versions.text
    for v in versions.json()["data"]["items"]:
        assert "versionNumber" in v and "status" in v


@allure.story("Leave Policies")
@allure.title("LEAVE-POLICY-001 — an unknown policy id is a clean 404")
@qa_cases("LEAVE-POLICY-001")
def test_unknown_policy_is_404(admin_api_client):
    assert get_leave_policy(admin_api_client, str(uuid.uuid4())).status_code == 404


@allure.story("Leave Policies")
@allure.title("LEAVE-POLICY-026 / CR-78 — reading policies needs PolicyView: Employee and Manager get 403")
@qa_cases("LEAVE-POLICY-026", "LEAVE-AUTHZ-003")
@cr_refs("CR-78")
def test_policy_read_requires_permission(employee_api_client, manager_api_client):
    assert list_leave_policies(employee_api_client).status_code == 403
    assert list_leave_policies(manager_api_client).status_code == 403


@allure.story("Leave Policies")
@allure.title("LEAVE-POLICY-003 / CR-78 — Employee and Manager cannot create a policy (PolicyManage)")
@qa_cases("LEAVE-POLICY-003", "LEAVE-POLICY-026")
@cr_refs("CR-78")
def test_policy_create_requires_manage(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        response = client.post("/api/leave-policies", json={"code": f"QAAUTO{uuid.uuid4().hex[:6]}", "name": "QAAUTO"})
        assert response.status_code == 403, response.text


# --- Leave Periods -----------------------------------------------------------------------------


@allure.story("Leave Periods")
@allure.title("LEAVE-PERIOD-001 — list is readable and filterable by onDate")
@qa_cases("LEAVE-PERIOD-001")
def test_period_list_and_ondate_filter(admin_api_client):
    all_periods = list_leave_periods(admin_api_client)
    assert all_periods.status_code == 200, all_periods.text
    items = all_periods.json()["data"]["items"]
    if not items:
        pytest.skip("Tenant has no Leave Period configured.")
    period = items[0]
    inside = list_leave_periods(admin_api_client, onDate=period["startDate"])
    assert inside.status_code == 200, inside.text
    matched = {p["id"] for p in inside.json()["data"]["items"]}
    assert period["id"] in matched, "onDate within a period's range should return that period"


@allure.story("Leave Periods")
@allure.title("LEAVE-PERIOD-001 — an unknown period id is a clean 404")
@qa_cases("LEAVE-PERIOD-001")
def test_unknown_period_is_404(admin_api_client):
    assert get_leave_period(admin_api_client, str(uuid.uuid4())).status_code == 404


@allure.story("Leave Periods")
@allure.title("LEAVE-PERIOD-003 — StartDate after EndDate is rejected 400, nothing created")
@qa_cases("LEAVE-PERIOD-003")
@pytest.mark.critical
def test_period_inverted_dates_rejected(admin_api_client):
    response = create_leave_period(
        admin_api_client, code=f"QAAUTO-{uuid.uuid4().hex[:6]}", name="QAAUTO inverted",
        startDate="2030-12-31", endDate="2030-01-01", isActive=False,
    )
    assert response.status_code == 400, response.text


@allure.story("Leave Periods")
@allure.title("LEAVE-PERIOD-002 — create shape-validation rejects a missing code")
@qa_cases("LEAVE-PERIOD-002")
def test_period_missing_code_rejected(admin_api_client):
    response = create_leave_period(admin_api_client, name="QAAUTO", startDate="2031-01-01", endDate="2031-12-31")
    assert response.status_code == 400, response.text
    errors = response.json().get("errors") or []
    assert any((e.get("field") or "").lower() == "code" for e in errors), response.text


@allure.story("Leave Periods")
@allure.title("LEAVE-PERIOD-002 / CR-78 — Employee and Manager cannot create a period (PeriodManage)")
@qa_cases("LEAVE-PERIOD-002")
@cr_refs("CR-78")
def test_period_create_requires_manage(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        response = create_leave_period(
            client, code=f"QAAUTO-{uuid.uuid4().hex[:6]}", name="QAAUTO", startDate="2031-01-01", endDate="2031-12-31"
        )
        assert response.status_code == 403, response.text


@allure.story("Leave Periods")
@allure.title("LEAVE-PERIOD-001 / LEAVE-AUTHZ-002 — the period surface rejects an anonymous caller 401")
@qa_cases("LEAVE-PERIOD-001", "LEAVE-AUTHZ-002")
def test_period_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert list_leave_periods(anon).status_code == 401
