"""API regression suite for Module 07 — Leave Types (configuration surface).

Automated from qa/07-leave/cases/01-leave-types-policies.yaml (LEAVE-TYPE-*). Covers the
read/paging/filter contract, the create/update guard rails (duplicate code, validation,
ConcurrencyToken), and the permission matrix (PolicyView to read, TypeManage to mutate).

Every write this suite issues is a NEGATIVE one that the server is expected to REJECT (duplicate
code, missing code, stale ConcurrencyToken), so nothing is ever persisted — verified in probing
that the referenced seed LeaveType (CL) is left byte-for-byte unchanged. No usable-policy or
submittable-request dependency, so these run (not skip) wherever the tenant has its seed Leave
configuration.
"""

from __future__ import annotations

import uuid

import allure
import pytest

from core.leave_api import create_leave_type, get_leave_type, list_leave_types, update_leave_type
from utils.allure_evidence import qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-001 — list is paged and honors the isActive filter")
@qa_cases("LEAVE-TYPE-001")
@pytest.mark.critical
def test_list_is_paged_and_active_filtered(admin_api_client):
    response = list_leave_types(admin_api_client, isActive=True, page=1, pageSize=50)
    assert response.status_code == 200, response.text
    data = response.json()["data"]
    for field in ("items", "page", "pageSize", "totalCount", "totalPages"):
        assert field in data, f"missing paging field {field!r}"
    assert data["page"] == 1 and data["pageSize"] == 50
    assert all(item["isActive"] for item in data["items"]), "isActive=true must exclude inactive types"


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-001 — isActive=false returns only inactive types (empty in a clean tenant)")
@qa_cases("LEAVE-TYPE-001")
def test_list_inactive_filter_is_disjoint(admin_api_client):
    response = list_leave_types(admin_api_client, isActive=False)
    assert response.status_code == 200, response.text
    assert all(not item["isActive"] for item in response.json()["data"]["items"])


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-002 — a single Leave Type is retrievable by id with its full field set")
@qa_cases("LEAVE-TYPE-002")
def test_get_single_type_by_id(admin_api_client):
    items = list_leave_types(admin_api_client).json()["data"]["items"]
    if not items:
        pytest.skip("Tenant has no Leave Types configured to fetch by id.")
    target = items[0]
    response = get_leave_type(admin_api_client, target["id"])
    assert response.status_code == 200, response.text
    body = response.json()["data"]
    assert body["id"] == target["id"] and body["code"] == target["code"]
    for field in ("name", "defaultUnit", "isPaid", "isCompOff", "isActive", "concurrencyToken"):
        assert field in body, f"missing field {field!r}"


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-002 — an unknown Leave Type id is a clean 404, not a 500")
@qa_cases("LEAVE-TYPE-002")
def test_get_unknown_type_is_404(admin_api_client):
    response = get_leave_type(admin_api_client, str(uuid.uuid4()))
    assert response.status_code == 404, response.text


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-004 — a duplicate code (case-insensitive) is rejected 409 and creates nothing")
@qa_cases("LEAVE-TYPE-004")
@pytest.mark.critical
def test_duplicate_code_rejected(admin_api_client):
    existing = list_leave_types(admin_api_client, isActive=True).json()["data"]["items"]
    if not existing:
        pytest.skip("Tenant has no Leave Type whose code could be duplicated.")
    code = existing[0]["code"]
    with allure.step("exact-case duplicate => 409"):
        assert create_leave_type(
            admin_api_client, code=code, name="QAAUTO dup probe", defaultUnit="Day", isPaid=True, isActive=True
        ).status_code == 409
    with allure.step("different-case duplicate => 409 (case-insensitive)"):
        assert create_leave_type(
            admin_api_client, code=code.lower() if code.upper() == code else code.upper(),
            name="QAAUTO dup probe", defaultUnit="Day", isPaid=True, isActive=True
        ).status_code == 409


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-003 — create shape-validation rejects a missing code with a field error")
@qa_cases("LEAVE-TYPE-003")
def test_create_missing_code_is_validation_error(admin_api_client):
    response = create_leave_type(admin_api_client, name="QAAUTO no code", defaultUnit="Day", isPaid=True)
    assert response.status_code == 400, response.text
    errors = response.json().get("errors") or []
    assert any((e.get("field") or "").lower() == "code" for e in errors), response.text


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-005 — an update with a stale/absent ConcurrencyToken is rejected 409, nothing mutated")
@qa_cases("LEAVE-TYPE-005")
@pytest.mark.critical
def test_stale_concurrency_token_rejected(admin_api_client):
    items = list_leave_types(admin_api_client).json()["data"]["items"]
    if not items:
        pytest.skip("Tenant has no Leave Type to attempt a concurrency-guarded update against.")
    t = items[0]
    same_values = dict(
        code=t["code"], name=t["name"], description=t.get("description"),
        defaultUnit=t["defaultUnit"], isPaid=t["isPaid"], isCompOff=t["isCompOff"], isActive=t["isActive"],
    )
    with allure.step("stale token => 409"):
        assert update_leave_type(
            admin_api_client, t["id"], concurrencyToken="1999-01-01T00:00:00.0000000Z", **same_values
        ).status_code == 409
    with allure.step("absent token => 409 (home-grown token fails closed when missing)"):
        assert update_leave_type(admin_api_client, t["id"], **same_values).status_code == 409
    with allure.step("the target is left unchanged"):
        after = get_leave_type(admin_api_client, t["id"]).json()["data"]
        assert after["code"] == t["code"] and after["name"] == t["name"] and after["isActive"] == t["isActive"]
        assert after["concurrencyToken"] == t["concurrencyToken"], "no write should have occurred"


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-010 / LEAVE-AUTHZ-002 — the Leave Type surface rejects an anonymous caller 401")
@qa_cases("LEAVE-TYPE-010", "LEAVE-AUTHZ-002")
@pytest.mark.critical
def test_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert list_leave_types(anon).status_code == 401


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-001 / LEAVE-AUTHZ-003 — reading types needs PolicyView: Employee and Manager get 403")
@qa_cases("LEAVE-TYPE-001", "LEAVE-AUTHZ-003")
@pytest.mark.critical
def test_read_requires_policy_view(employee_api_client, manager_api_client):
    assert list_leave_types(employee_api_client).status_code == 403
    assert list_leave_types(manager_api_client).status_code == 403


@allure.story("Leave Types")
@allure.title("LEAVE-TYPE-003 / LEAVE-AUTHZ-003 — creating a type needs TypeManage: Employee and Manager get 403")
@qa_cases("LEAVE-TYPE-003", "LEAVE-AUTHZ-003")
def test_create_requires_type_manage(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        response = create_leave_type(
            client, code=f"QAAUTO{uuid.uuid4().hex[:6]}", name="QAAUTO probe", defaultUnit="Day", isPaid=True, isActive=True
        )
        assert response.status_code == 403, response.text
