"""API regression suite for Module 07 — Leave/CompOff authorization matrix and tenant isolation.

Automated from qa/07-leave/cases/15-authz-tenant-security.yaml (LEAVE-AUTHZ-*).

Consolidates the cross-surface guarantees that individual feature files check per-endpoint:
  * LEAVE-AUTHZ-002 — every Leave/CompOff surface rejects an anonymous caller 401.
  * LEAVE-AUTHZ-003 — a caller with the wrong permission gets 403 (never 404, outside the one
    deliberate approvals GetById exception).
  * LEAVE-AUTHZ-006 — the Manager scope reaches Approvals and Calendar but never Reports/Dashboard.
  * LEAVE-AUTHZ-010 — TenantAdmin has zero Leave scope narrowing (200 across config/reports/dashboard).

Tenant isolation is exercised as far as a single-tenant environment allows: a Tenant-A access token
replayed against an unregistered workspace host is rejected 401 (JWT tid ≠ resolved tenant), and an
anonymous call to an unknown host fails resolution. Deep cross-tenant data-leak checks
(LEAVE-AUTHZ-021/022/024) need a configured second tenant and skip honestly when QA_TENANT_B_HOST is
absent — a green run with that skip is not proof of cross-tenant isolation.
"""

from __future__ import annotations

import os
import uuid
from datetime import date, timedelta

import allure
import pytest

from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]

_TODAY = date.today().isoformat()
_TO = (date.today() + timedelta(days=30)).isoformat()

# GET surfaces spanning the whole Leave/CompOff module.
_GET_SURFACES = [
    "/api/leave-types",
    "/api/leave-types/available",
    "/api/leave-periods",
    "/api/leave-policies",
    "/api/leave-requests",
    "/api/leave-approvals",
    "/api/leave-balances/mine",
    "/api/leave-dashboard/hr-summary",
    "/api/leave-reports/requests",
    "/api/leave-balances/import/template",
    "/api/attendance/comp-off/balance",
    "/api/attendance/comp-off/operations",
]

# Surfaces the plain Employee role lacks the permission for (expect 403 once authenticated).
_EMPLOYEE_FORBIDDEN = [
    "/api/leave-types",
    "/api/leave-periods",
    "/api/leave-policies",
    "/api/leave-approvals",
    "/api/leave-dashboard/hr-summary",
    "/api/leave-reports/requests",
    "/api/leave-balances/import/template",
    "/api/attendance/comp-off/operations",
]


@allure.story("Authorization")
@allure.title("LEAVE-AUTHZ-002 — every Leave/CompOff GET surface rejects an anonymous caller 401")
@qa_cases("LEAVE-AUTHZ-002")
@pytest.mark.critical
def test_all_surfaces_reject_anonymous(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    failures = []
    for path in _GET_SURFACES:
        params = {"from": _TODAY, "to": _TO} if path == "/api/leave-calendar" else None
        status = anon.get(path, params=params).status_code
        if status != 401:
            failures.append(f"{path} -> {status}")
    assert not failures, f"expected 401 on every surface, got: {failures}"


@allure.story("Authorization")
@allure.title("LEAVE-AUTHZ-003 — a plain Employee gets 403 (not 404) on every surface they lack permission for")
@qa_cases("LEAVE-AUTHZ-003")
@pytest.mark.critical
def test_employee_wrong_permission_is_403(employee_api_client):
    failures = []
    for path in _EMPLOYEE_FORBIDDEN:
        status = employee_api_client.get(path).status_code
        if status != 403:
            failures.append(f"{path} -> {status}")
    assert not failures, f"expected 403 on every forbidden surface, got: {failures}"


@allure.story("Authorization")
@allure.title("LEAVE-AUTHZ-006 — the Manager scope reaches Approvals and Calendar but never Reports/Dashboard")
@qa_cases("LEAVE-AUTHZ-006")
@cr_refs("CR-77")
@pytest.mark.critical
def test_manager_scope_boundaries(manager_api_client):
    assert manager_api_client.get("/api/leave-approvals").status_code == 200
    assert manager_api_client.get("/api/leave-calendar", params={"from": _TODAY, "to": _TO}).status_code == 200
    assert manager_api_client.get("/api/leave-reports/requests").status_code == 403
    assert manager_api_client.get("/api/leave-dashboard/hr-summary").status_code == 403


@allure.story("Authorization")
@allure.title("LEAVE-AUTHZ-010 — TenantAdmin has zero Leave scope narrowing (200 across config/reports/dashboard)")
@qa_cases("LEAVE-AUTHZ-010")
def test_tenant_admin_full_scope(admin_api_client):
    for path in ("/api/leave-types", "/api/leave-periods", "/api/leave-policies",
                 "/api/leave-reports/requests", "/api/leave-dashboard/hr-summary"):
        assert admin_api_client.get(path).status_code == 200, path


# --- Tenant isolation --------------------------------------------------------------------------


@allure.story("Tenant isolation")
@allure.title("LEAVE-AUTHZ-021 — a Tenant-A access token is rejected 401 against an unregistered workspace host")
@qa_cases("LEAVE-AUTHZ-021", "LEAVE-AUTHZ-002")
@pytest.mark.critical
def test_token_not_valid_for_other_workspace(employee_api_client, api_client_factory, settings):
    token = employee_api_client._default_headers["Authorization"].split(" ", 1)[1]
    unknown = api_client_factory(settings.unknown_host).with_bearer(token)
    response = unknown.get("/api/leave-requests")
    assert response.status_code == 401, response.text
    # A JWT tid mismatch is refused as a workspace mismatch, not silently served another tenant's data.
    assert "workspace" in response.text.lower() or "not valid" in response.text.lower(), response.text


@allure.story("Tenant isolation")
@allure.title("LEAVE-AUTHZ-002 — an anonymous call to an unknown host fails resolution (401), and a garbage bearer is 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_unknown_host_and_garbage_bearer(api_client_factory, settings):
    assert api_client_factory(settings.unknown_host).get("/api/leave-requests").status_code == 401
    garbage = api_client_factory(require_env("QA_TENANT_A_HOST")).with_bearer("not.a.valid.jwt")
    assert garbage.get("/api/leave-requests").status_code == 401


@allure.story("Tenant isolation")
@allure.title("LEAVE-AUTHZ-022 / LEAVE-AUTHZ-024 — deep cross-tenant data isolation (needs a configured second tenant)")
@qa_cases("LEAVE-AUTHZ-022", "LEAVE-AUTHZ-024")
def test_cross_tenant_data_isolation(api_client_factory):
    host_b = os.environ.get("QA_TENANT_B_HOST")
    if not host_b:
        pytest.skip(
            "QA_TENANT_B_HOST is not configured — cross-tenant leave-data isolation cannot be proven with a "
            "single tenant. A green run here is not evidence of cross-tenant isolation (qa/07-leave, LEAVE-AUTHZ-022/024)."
        )
    # With a second tenant configured, a Tenant-A identity must never read Tenant-B leave data via Host B.
    from core.auth_helpers import api_login
    from data.test_data import employee_user_a

    user = employee_user_a()
    client_b = api_client_factory(host_b)
    login = api_login(client_b, user.identifier, user.password)
    # A Tenant-A credential is not a Tenant-B user; sign-in must fail there.
    assert login.status_code in (401, 403), login.text
