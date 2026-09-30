"""API regression suite for Module 07 — Comp-Off (Attendance area, Leave-integrated: Leave_CompOff sheet).

Automated from qa/07-leave/cases/11-comp-off.yaml (LEAVE-COMPOFF-*).

Highest-value finding automated here is CR-82: GET /api/attendance/comp-off/operations carries NO
declarative [HasPermission] attribute — every other action in the module declares one. Authorization
lives entirely inside the service, which is observable because an Employee-only caller is rejected with
the SERVICE's message ("...lacks the required Attendance permission.") rather than the attribute
filter's message ("You do not have permission to perform this action."). The end result is correct
(only certain roles get data) but the pattern is structurally different and invisible to a
[HasPermission] grep. This suite pins that behavior with cr_refs(CR-82); it is not marked known_defect
because CR-82 is classified Open/awaiting-decision in qa/07-leave/clarifications.yaml, not a confirmed
defect.

Self-service reads are non-destructive. This suite issues no comp-off writes: earning/policy creation
is permanent tenant state, so only the authorization guard (Employee/Manager both lack CompOff.Manage)
is exercised, via calls the server rejects before persisting anything.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.leave_api import (
    create_compoff_earning,
    create_compoff_policy,
    get_compoff_balance,
    get_compoff_earnings,
    get_compoff_ledger,
    get_compoff_operations,
)
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]

# The attribute filter and the service's own permission scan return distinct messages; the difference
# is what makes CR-82's service-side enforcement observable from the wire.
_ATTRIBUTE_403 = "You do not have permission to perform this action."


# --- Self-service balance / earnings / ledger --------------------------------------------------


@allure.story("Comp-Off self-service")
@allure.title("LEAVE-COMPOFF-013 — balance/earnings/ledger are self-scoped with no employeeId parameter")
@qa_cases("LEAVE-COMPOFF-013")
@pytest.mark.critical
def test_self_service_is_self_scoped(employee_api_client):
    balance = get_compoff_balance(employee_api_client)
    assert balance.status_code == 200, balance.text
    own_employee_id = balance.json()["data"]["employeeId"]
    # A stray employeeId must not reach another employee's balance.
    probed_resp = employee_api_client.get("/api/attendance/comp-off/balance", params={"employeeId": str(uuid.uuid4())})
    assert probed_resp.status_code == 200, probed_resp.text
    assert probed_resp.json()["data"]["employeeId"] == own_employee_id

    assert get_compoff_earnings(employee_api_client).status_code == 200
    assert get_compoff_ledger(employee_api_client).status_code == 200


@allure.story("Comp-Off self-service")
@allure.title("LEAVE-COMPOFF-014 — an unlinked account is rejected (identity required), not given an empty balance")
@qa_cases("LEAVE-COMPOFF-014")
def test_unlinked_account_rejected(admin_api_client):
    # The seeded QA Admin is authorized (holds CompOff perms) but is not a linked Employee.
    response = get_compoff_balance(admin_api_client)
    assert response.status_code in (401, 404), response.text
    assert "identity" in response.text.lower() or "not linked" in response.text.lower(), response.text


@allure.story("Comp-Off self-service")
@allure.title("LEAVE-AUTHZ-002 — an anonymous comp-off balance call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_compoff_balance_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_compoff_balance(anon).status_code == 401


# --- CR-82: Operations query (no declarative permission attribute) ------------------------------


@allure.story("Comp-Off operations")
@allure.title("LEAVE-COMPOFF-015 / LEAVE-COMPOFF-016 / CR-82 — Operations authorization is service-enforced, not attribute-enforced")
@qa_cases("LEAVE-COMPOFF-015", "LEAVE-COMPOFF-016")
@cr_refs("CR-82")
@pytest.mark.critical
def test_operations_authorization_is_service_enforced(employee_api_client, manager_api_client):
    # An Employee-only caller reaches the controller action (no [HasPermission] gate) and is then
    # Forbidden by the service's OWN permission scan — evidenced by the service message, distinct from
    # the attribute filter's message.
    emp = get_compoff_operations(employee_api_client)
    assert emp.status_code == 403, emp.text
    assert emp.json().get("message") != _ATTRIBUTE_403, (
        "Operations 403 should come from the service's permission scan (CR-82), not the [HasPermission] "
        f"attribute filter. Got the attribute message: {emp.text}"
    )
    # A Manager (holds CompOff.ViewTeam) is admitted by the same service scan.
    mgr = get_compoff_operations(manager_api_client)
    assert mgr.status_code == 200, mgr.text


@allure.story("Comp-Off operations")
@allure.title("LEAVE-COMPOFF-017 — a Manager's Operations query returns a well-formed paged result")
@qa_cases("LEAVE-COMPOFF-017")
def test_manager_operations_paged(manager_api_client):
    response = get_compoff_operations(manager_api_client, page=1, pageSize=5)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    assert data["page"] == 1 and data["pageSize"] == 5
    assert {"items", "totalCount", "totalPages"} <= set(data)


@allure.story("Comp-Off operations")
@allure.title("LEAVE-AUTHZ-002 — an anonymous Operations call is still rejected 401 (controller-level [Authorize])")
@qa_cases("LEAVE-AUTHZ-002", "LEAVE-COMPOFF-015")
def test_operations_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_compoff_operations(anon).status_code == 401


# --- Management actions require CompOff.Manage --------------------------------------------------


@allure.story("Comp-Off management")
@allure.title("LEAVE-COMPOFF-001 — creating an earning/policy needs CompOff.Manage: Employee and Manager get 403")
@qa_cases("LEAVE-COMPOFF-001")
def test_management_requires_manage_permission(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert create_compoff_earning(client).status_code == 403
        assert create_compoff_policy(client).status_code == 403
