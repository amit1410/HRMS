"""API regression suite for Module 07 — Leave reports (all seven kinds + CSV export).

Automated from qa/07-leave/cases/13-reports.yaml (LEAVE-RPT-*).

All reports require Leave.ReportsView (export additionally requires ReportsExport); per CR-79 the
Manager role holds neither, and — per LEAVE-AUTHZ-006 — the manager approval path never widens report
scope. Reads are non-destructive. Report rows can carry real employee names, so this suite asserts
shape/invariants and status codes only, and never attaches raw report rows to evidence.
"""

from __future__ import annotations

import allure
import pytest

from core.leave_api import export_leave_report, get_leave_report
from utils.allure_evidence import cr_refs, qa_cases
from utils.env_utils import require_env

pytestmark = [allure.feature("Leave Management"), pytest.mark.api, pytest.mark.regression]

PAGED_REPORTS = ("requests", "balances", "accounting", "pending")
LIST_REPORTS = ("usage", "organization", "calendar")
ALL_REPORTS = PAGED_REPORTS + LIST_REPORTS


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-001 — all seven report kinds are independently reachable (200)")
@qa_cases("LEAVE-RPT-001")
@pytest.mark.critical
def test_all_report_kinds_reachable(admin_api_client):
    for report in ALL_REPORTS:
        response = get_leave_report(admin_api_client, report)
        assert response.status_code == 200, f"{report}: {response.text}"


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-004 — paged reports honor page/pageSize and expose paging metadata")
@qa_cases("LEAVE-RPT-004")
def test_paged_reports_shape(admin_api_client):
    for report in PAGED_REPORTS:
        data = get_leave_report(admin_api_client, report, page=1, pageSize=2).json()["data"]
        assert data["page"] == 1 and data["pageSize"] == 2, report
        assert {"items", "totalCount", "totalPages"} <= set(data), report


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-005 / LEAVE-BAL-002 — the Balances report carries entitlement math (Available == Granted − Reserved − Consumed)")
@qa_cases("LEAVE-RPT-005", "LEAVE-BAL-002")
def test_balances_report_invariant(admin_api_client):
    rows = get_leave_report(admin_api_client, "balances").json()["data"]["items"]
    checked = 0
    for r in rows:
        if all(k in r for k in ("granted", "reserved", "consumed", "available")):
            assert abs(r["available"] - (r["granted"] - r["reserved"] - r["consumed"])) < 1e-6, r
            checked += 1
    if checked == 0:
        pytest.skip("No Allocated balance rows in the Balances report to check the invariant against.")


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-002 — the Requests report supports a status filter")
@qa_cases("LEAVE-RPT-002")
def test_requests_report_status_filter(admin_api_client):
    response = get_leave_report(admin_api_client, "requests", status="Approved")
    assert response.status_code == 200, response.text
    for row in response.json()["data"]["items"]:
        assert row["status"] == "Approved", row


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-010 — CSV export returns text/csv reusing the report's filtered query")
@qa_cases("LEAVE-RPT-010")
def test_csv_export(admin_api_client):
    response = export_leave_report(admin_api_client, "requests")
    assert response.status_code == 200, response.text
    assert "text/csv" in response.headers.get("content-type", ""), response.headers.get("content-type")
    assert response.text.lstrip("﻿").splitlines(), "export should have at least a header line"


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-012 — an unsupported report name is a clean 400, not a 404 or crash")
@qa_cases("LEAVE-RPT-012")
def test_unsupported_export_name_is_400(admin_api_client):
    response = export_leave_report(admin_api_client, "not-a-real-report")
    assert response.status_code == 400, response.text


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-013 / LEAVE-AUTHZ-006 — reports need ReportsView: Employee and Manager get 403 (no manager-path widening)")
@qa_cases("LEAVE-RPT-013", "LEAVE-AUTHZ-006")
@cr_refs("CR-79")
@pytest.mark.critical
def test_reports_require_reports_view(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert get_leave_report(client, "requests").status_code == 403


@allure.story("Leave reports")
@allure.title("LEAVE-RPT-010 — export needs ReportsExport: Employee and Manager get 403")
@qa_cases("LEAVE-RPT-010")
def test_export_requires_reports_export(employee_api_client, manager_api_client):
    for client in (employee_api_client, manager_api_client):
        assert export_leave_report(client, "requests").status_code == 403


@allure.story("Leave reports")
@allure.title("LEAVE-AUTHZ-002 — an anonymous report call is rejected 401")
@qa_cases("LEAVE-AUTHZ-002")
def test_reports_unauthenticated_is_401(api_client_factory):
    anon = api_client_factory(require_env("QA_TENANT_A_HOST"))
    assert get_leave_report(anon, "requests").status_code == 401
