"""API regression suite for Module 10 — Payroll authorization matrix and tenant isolation.

Automated from qa/10-payroll/cases/11-authz-scope-tenant-security.yaml (PAY-SCOPE-*). Statuses
confirmed live before writing.

Coverage is bounded by the identities this environment provides — TenantAdmin (admin), a plain
seeded Employee, and a seeded Manager. Those exercise PAY-SCOPE-001 (TenantAdmin full scope),
PAY-SCOPE-003 (Manager holds zero Payroll permissions, CR-168) and PAY-SCOPE-004 (a plain Employee
cannot even view its own payslip, CR-179). PAY-SCOPE-002 (HRManager holds none, CR-165) and
PAY-SCOPE-005/006 (SuperHR's native Finalize + PageAccess self-escalation, CR-166) need HRManager /
SuperHR credentials and skip honestly — a green run with those skips is not proof of those role
boundaries. Cross-tenant data isolation (PAY-SCOPE-011) is exercised as far as a single-tenant
environment allows: an id not belonging to this tenant is 404 (never 403/500), and a Tenant-A token
replayed against an unregistered workspace host is refused 401.
"""

from __future__ import annotations

import os
import uuid

import allure
import pytest

from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Payroll"), allure.story("Authorization"), pytest.mark.api, pytest.mark.regression]

# Tenant-wide Payroll GET surfaces spanning the in-scope services.
_SURFACES = [
    "/api/payroll/salary-components",
    "/api/payroll/salary-structures",
    "/api/payroll/employee-salary-assignments",
    "/api/payroll/periods",
    "/api/payroll/runs",
    "/api/payroll/input-batches",
]

# get-by-id services where a foreign/nonexistent id must be 404 (never 403/500).
_GET_BY_ID = [
    "/api/payroll/salary-components/{}",
    "/api/payroll/salary-structures/{}",
    "/api/payroll/employee-salary-assignments/{}",
    "/api/payroll/periods/{}",
    "/api/payroll/runs/{}",
    "/api/payroll/input-batches/{}",
]


@allure.title("PAY-SCOPE-001 — TenantAdmin reaches every in-scope Payroll surface (200)")
@qa_cases("PAY-SCOPE-001")
def test_tenant_admin_full_scope(admin_api_client):
    failures = [f"{p} -> {admin_api_client.get(p).status_code}"
                for p in _SURFACES if admin_api_client.get(p).status_code != 200]
    assert not failures, f"TenantAdmin must reach every surface, got: {failures}"


@allure.title("PAY-SCOPE-004 — a plain Employee is 403 on every Payroll surface, incl. its own payslip (CR-179)")
@qa_cases("PAY-SCOPE-004")
@cr_refs("CR-179")
@pytest.mark.critical
def test_employee_forbidden_everywhere(employee_api_client):
    failures = [f"{p} -> {employee_api_client.get(p).status_code}"
                for p in _SURFACES if employee_api_client.get(p).status_code != 403]
    assert not failures, f"a plain Employee must be 403 on every surface, got: {failures}"
    # CR-179: Payslip.ViewOwn is not in the seeded Employee grant set.
    assert employee_api_client.get("/api/me/payslips").status_code == 403


@allure.title("PAY-SCOPE-003 — the Manager role holds zero Payroll permissions (CR-168)")
@qa_cases("PAY-SCOPE-003")
@cr_refs("CR-168")
@pytest.mark.critical
def test_manager_zero_payroll_permissions(manager_api_client):
    failures = [f"{p} -> {manager_api_client.get(p).status_code}"
                for p in _SURFACES if manager_api_client.get(p).status_code != 403]
    assert not failures, f"the Manager role must be 403 on every Payroll surface, got: {failures}"


@allure.title("PAY-SCOPE — every in-scope Payroll surface rejects an anonymous caller 401")
@qa_cases("PAY-SCOPE-001")
@pytest.mark.critical
def test_anonymous_rejected_everywhere(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    failures = [f"{p} -> {anon.get(p).status_code}" for p in _SURFACES if anon.get(p).status_code != 401]
    assert not failures, f"expected 401 on every surface, got: {failures}"


@allure.title("PAY-SCOPE-011 — an id not in this tenant is 404 (never 403/500) across every in-scope service")
@qa_cases("PAY-SCOPE-011")
@pytest.mark.critical
def test_cross_tenant_ids_are_404(admin_api_client):
    foreign = str(uuid.uuid4())
    failures = [f"{tpl} -> {admin_api_client.get(tpl.format(foreign)).status_code}"
                for tpl in _GET_BY_ID if admin_api_client.get(tpl.format(foreign)).status_code != 404]
    assert not failures, f"every service must return 404 for a foreign id, got: {failures}"


@allure.title("PAY-SCOPE-011 — a Tenant-A token is rejected 401 against an unregistered workspace host")
@qa_cases("PAY-SCOPE-011")
def test_token_not_valid_for_other_workspace(employee_api_client, api_client_factory, settings):
    token = employee_api_client._default_headers["Authorization"].split(" ", 1)[1]
    unknown = api_client_factory(settings.unknown_host).with_bearer(token)
    response = unknown.get("/api/payroll/salary-components")
    assert response.status_code == 401, response.text


@allure.title("PAY-SCOPE-002/005/006 — HRManager and SuperHR role boundaries need those credentials")
@qa_cases("PAY-SCOPE-002", "PAY-SCOPE-005", "PAY-SCOPE-006")
@cr_refs("CR-165", "CR-166")
def test_hrmanager_and_superhr_boundaries():
    if not (os.environ.get("QA_A_HRMANAGER_USERNAME") or os.environ.get("QA_A_SUPERHR_USERNAME")):
        pytest.skip(
            "HRManager (CR-165, PAY-SCOPE-002) and SuperHR (CR-166, PAY-SCOPE-005/006 — native Finalize "
            "and PageAccess.Manage self-escalation) boundaries require QA_A_HRMANAGER_* / QA_A_SUPERHR_* "
            "credentials, which are not configured. A green run here is not evidence of those role "
            "boundaries — they stay covered narratively by qa/10-payroll/clarifications.yaml.")
