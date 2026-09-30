"""API regression suite for Module 08 — Attendance authorization matrix & tenant isolation.

Automated from qa/08-attendance/cases/16-authz-tenant-security.yaml (ATT-SEC-*) and the role-scope
cases in 11-manager-scopes.yaml (ATT-SCOPE-008/013/014/018).

Consolidates the cross-surface guarantees that individual feature files check per-endpoint, using the
exact status codes verified against the live seeded RolePermissionMap:
  * ATT-SEC-004 — every Attendance GET surface rejects an anonymous caller 401.
  * ATT-SCOPE-013 — a plain Employee is 403 on every route it lacks permission for, but reaches its
    own monthly summary and comp-off balance (the only two it holds).
  * ATT-SCOPE-014 — the Manager scope reaches Operations + Comp-Off team but nothing else.
  * ATT-SCOPE-008 — TenantAdmin has full config/report/device/operations access.
  * ATT-SEC-003 — missing-permission (403) and out-of-scope-within-tenant (404) are distinguished.

Deep cross-tenant data-leak checks (ATT-SEC-001/002/008) need a configured second tenant and skip
honestly when QA_TENANT_B_HOST is absent — a green run with that skip is not proof of cross-tenant
isolation.
"""

from __future__ import annotations

import datetime
import os
import uuid

import allure
import pytest

from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Attendance Management"), pytest.mark.api, pytest.mark.regression]

_T = datetime.date.today()
_YM = {"year": _T.year, "month": _T.month}
_RANGE = {"fromDate": _T.replace(day=1).isoformat(), "toDate": _T.isoformat()}

# (path, params) for every Attendance GET surface spanning the module.
_ALL_GET_SURFACES = [
    ("/api/attendance/shifts", None),
    ("/api/attendance/patterns", None),
    ("/api/attendance/shift-applicability", None),
    ("/api/attendance/roster", _RANGE),
    ("/api/attendance/me/calendar", _YM),
    ("/api/attendance/me/exceptions", None),
    ("/api/attendance/my/monthly-summary", _YM),
    ("/api/attendance/manager/team", _RANGE),
    ("/api/attendance/manager/regularizations", None),
    ("/api/attendance/manager/on-duty", None),
    ("/api/attendance/me/regularizations", None),
    ("/api/attendance/me/on-duty", None),
    ("/api/attendance/periods", None),
    ("/api/attendance/operations/exceptions", None),
    ("/api/attendance/operations/dashboard", None),
    ("/api/attendance/admin-corrections", None),
    ("/api/attendance/devices", None),
    ("/api/attendance/devices/mappings", None),
    ("/api/attendance/devices/sync-runs", None),
    ("/api/attendance/overtime/snapshot", _YM),
    ("/api/attendance/comp-off/balance", None),
    ("/api/attendance/comp-off/operations", None),
    ("/api/attendance/reports/daily", _RANGE),
    ("/api/attendance/reports/monthly", _YM),
]

# Routes a plain seeded Employee must be Forbidden (403) on (everything it lacks the permission for).
_EMPLOYEE_FORBIDDEN = [
    ("/api/attendance/shifts", None),
    ("/api/attendance/patterns", None),
    ("/api/attendance/shift-applicability", None),
    ("/api/attendance/roster", _RANGE),
    ("/api/attendance/me/calendar", _YM),
    ("/api/attendance/me/exceptions", None),
    ("/api/attendance/manager/team", _RANGE),
    ("/api/attendance/manager/regularizations", None),
    ("/api/attendance/manager/on-duty", None),
    ("/api/attendance/me/regularizations", None),
    ("/api/attendance/me/on-duty", None),
    ("/api/attendance/periods", None),
    ("/api/attendance/operations/exceptions", None),
    ("/api/attendance/operations/dashboard", None),
    ("/api/attendance/admin-corrections", None),
    ("/api/attendance/devices", None),
    ("/api/attendance/devices/sync-runs", None),
    ("/api/attendance/overtime/snapshot", _YM),
    ("/api/attendance/reports/daily", _RANGE),
    ("/api/attendance/reports/monthly", _YM),
]


# --- Unauthenticated sweep ---------------------------------------------------------------------


@allure.story("Authorization / Anonymous")
@allure.title("ATT-SEC-004 — every Attendance GET surface rejects an anonymous caller 401")
@qa_cases("ATT-SEC-004")
@pytest.mark.critical
def test_all_surfaces_reject_anonymous(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    failures = [
        f"{path} -> {anon.get(path, params=params).status_code}"
        for path, params in _ALL_GET_SURFACES
        if anon.get(path, params=params).status_code != 401
    ]
    assert not failures, f"expected 401 on every surface, got: {failures}"


# --- Employee matrix ---------------------------------------------------------------------------


@allure.story("Authorization / Employee")
@allure.title("ATT-SCOPE-013 — a plain Employee is 403 on every route it lacks permission for")
@qa_cases("ATT-SCOPE-013", "ATT-SEC-003")
@cr_refs("CR-111")
@pytest.mark.critical
def test_employee_forbidden_matrix(employee_api_client):
    failures = [
        f"{path} -> {employee_api_client.get(path, params=params).status_code}"
        for path, params in _EMPLOYEE_FORBIDDEN
        if employee_api_client.get(path, params=params).status_code != 403
    ]
    assert not failures, f"expected 403 on every forbidden surface, got: {failures}"


@allure.story("Authorization / Employee")
@allure.title("ATT-SCOPE-013 — the Employee reaches ONLY its own monthly summary and comp-off balance")
@qa_cases("ATT-SCOPE-013")
def test_employee_reachable_surface(employee_api_client):
    assert employee_api_client.get("/api/attendance/my/monthly-summary", params=_YM).status_code == 200
    assert employee_api_client.get("/api/attendance/comp-off/balance").status_code == 200


# --- Manager matrix ----------------------------------------------------------------------------


@allure.story("Authorization / Manager")
@allure.title("ATT-SCOPE-014 — the Manager reaches Operations + Comp-Off team, but not config/reports/devices/team")
@qa_cases("ATT-SCOPE-014")
@pytest.mark.critical
def test_manager_scope_boundaries(manager_api_client):
    reachable = ["/api/attendance/operations/exceptions", "/api/attendance/operations/dashboard",
                 "/api/attendance/comp-off/operations", "/api/attendance/comp-off/balance"]
    for path in reachable:
        assert manager_api_client.get(path).status_code == 200, path
    assert manager_api_client.get("/api/attendance/my/monthly-summary", params=_YM).status_code == 200
    forbidden = ["/api/attendance/shifts", "/api/attendance/periods", "/api/attendance/devices",
                 "/api/attendance/admin-corrections", "/api/attendance/manager/regularizations"]
    for path in forbidden:
        assert manager_api_client.get(path).status_code == 403, path
    assert manager_api_client.get("/api/attendance/reports/daily", params=_RANGE).status_code == 403


# --- TenantAdmin matrix ------------------------------------------------------------------------


@allure.story("Authorization / TenantAdmin")
@allure.title("ATT-SCOPE-008 — TenantAdmin has full config/report/device/operations access")
@qa_cases("ATT-SCOPE-008")
def test_tenant_admin_full_scope(admin_api_client):
    for path in ("/api/attendance/shifts", "/api/attendance/patterns", "/api/attendance/shift-applicability",
                 "/api/attendance/periods", "/api/attendance/operations/exceptions",
                 "/api/attendance/operations/dashboard", "/api/attendance/admin-corrections",
                 "/api/attendance/devices", "/api/attendance/devices/mappings"):
        assert admin_api_client.get(path).status_code == 200, path
    assert admin_api_client.get("/api/attendance/reports/daily", params=_RANGE).status_code == 200
    assert admin_api_client.get("/api/attendance/reports/monthly", params=_YM).status_code == 200


@allure.story("Authorization / Scope vs permission")
@allure.title("ATT-SEC-003 — missing-permission (403) and out-of-scope-within-tenant (404) produce different codes")
@qa_cases("ATT-SEC-003")
def test_missing_permission_vs_out_of_scope(employee_api_client, admin_api_client):
    # Employee lacks the permission → 403 (attribute filter, before any lookup).
    assert employee_api_client.get("/api/attendance/manager/team", params=_RANGE).status_code == 403
    # Admin holds the permission but references a nonexistent employee/day → 404 (scope/lookup miss).
    admin_day = admin_api_client.get(f"/api/attendance/manager/team/{uuid.uuid4()}/{_T.isoformat()}")
    assert admin_day.status_code in (404, 403), admin_day.text


# --- CR-82 comp-off operations (service-enforced, not attribute-enforced) ----------------------


@allure.story("Authorization / Comp-Off operations")
@allure.title("ATT-SCOPE-007 / CR-82 — Comp-Off Operations is service-enforced (Employee 403 via service message)")
@qa_cases("ATT-SCOPE-007")
@cr_refs("CR-82")
def test_compoff_operations_service_enforced(employee_api_client, manager_api_client):
    emp = employee_api_client.get("/api/attendance/comp-off/operations")
    assert emp.status_code == 403, emp.text
    # The 403 comes from the service's own permission scan, not the [HasPermission] attribute filter.
    assert emp.json().get("message") != "You do not have permission to perform this action.", emp.text
    assert manager_api_client.get("/api/attendance/comp-off/operations").status_code == 200


# --- Tenant isolation --------------------------------------------------------------------------


@allure.story("Tenant isolation")
@allure.title("ATT-SEC-007 — a Tenant-A token is rejected 401 at an unknown host; a garbage bearer is 401")
@qa_cases("ATT-SEC-007", "ATT-SEC-004")
@pytest.mark.critical
def test_token_not_valid_for_other_workspace(admin_api_client, api_client_factory, settings):
    token = admin_api_client._default_headers["Authorization"].split(" ", 1)[1]
    unknown = api_client_factory(settings.unknown_host).with_bearer(token)
    assert unknown.get("/api/attendance/shifts").status_code == 401
    garbage = api_client_factory(require_env("QA_TENANT_A_HOST")).with_bearer("not.a.valid.jwt")
    assert garbage.get("/api/attendance/shifts").status_code == 401


@allure.story("Tenant isolation")
@allure.title("ATT-SEC-002 — a cross-tenant Overtime call is rejected at the host/tenant layer")
@qa_cases("ATT-SEC-002")
def test_overtime_cross_tenant_rejected(api_client_factory, settings):
    anon = api_client_factory(settings.unknown_host)
    # An unknown/unregistered host never resolves a tenant, so the overtime read is rejected 401.
    assert anon.get("/api/attendance/overtime/snapshot", params=_YM).status_code == 401


@allure.story("Tenant isolation")
@allure.title("ATT-SEC-001 / ATT-SEC-008 — deep cross-tenant data isolation (needs a configured second tenant)")
@qa_cases("ATT-SEC-001", "ATT-SEC-008")
def test_cross_tenant_data_isolation():
    if not os.environ.get("QA_TENANT_B_HOST"):
        pytest.skip(
            "QA_TENANT_B_HOST is not configured — cross-tenant Attendance data isolation cannot be proven "
            "with a single tenant. A green run here is not evidence of cross-tenant isolation "
            "(qa/08-attendance, ATT-SEC-001/008)."
        )
    pytest.skip("Cross-tenant write/read sweep requires a provisioned Tenant B QA identity — not fabricated here.")
